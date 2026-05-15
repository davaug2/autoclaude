"""Business pipeline — Q&A about the source code.

  rate-limit → input-guard → ReAct agent (claude-code-mcp tools) →
      output-sanitize → output-block → [rewrite-loop] → reply
"""
from __future__ import annotations

import os
import time
import uuid
from typing import Any

from langchain_anthropic import ChatAnthropic
from langchain_core.messages import AIMessage, HumanMessage, SystemMessage
from langgraph.prebuilt import create_react_agent

from guards import Guards
from mcp_client import MCPRegistry
from rewrite import make_rewriter

from ._common import PipelineResult, apply_output_guards_with_rewrite

SYSTEM_PROMPT = (
    "You are a read-only code analyst answering questions about a codebase. "
    "Use the available tools (read_file, grep, glob) to inspect files under "
    "/repo. Cite file paths and line numbers when relevant. Never invent "
    "code you have not seen. If the answer is not in the repo, say so."
)


class BusinessPipeline:
    profile = "business"

    def __init__(self, guards: Guards, mcp: MCPRegistry,
                 callbacks: list[Any] | None = None) -> None:
        self.guards = guards
        self.mcp = mcp
        self.callbacks = callbacks or []
        self.max_rewrites = int(os.environ.get("MAX_REWRITE_ATTEMPTS", "2"))
        self.model = os.environ.get("ANTHROPIC_MODEL", "claude-opus-4-7")
        self.agent_max_tokens = int(os.environ.get("AGENT_MAX_TOKENS", "4096"))
        self.agent_timeout_s = float(os.environ.get("AGENT_TIMEOUT_S", "180"))
        self._rewriter: ChatAnthropic | None = None
        self._agent: Any = None

    def _build_agent(self) -> Any:
        if self._agent is not None:
            return self._agent
        llm = ChatAnthropic(
            model=self.model, temperature=0,
            max_tokens=self.agent_max_tokens, timeout=self.agent_timeout_s,
        )
        self._agent = create_react_agent(llm, self.mcp.business_tools, prompt=SYSTEM_PROMPT)
        return self._agent

    def _rewrite_chat(self) -> ChatAnthropic:
        if self._rewriter is None:
            self._rewriter = make_rewriter()
        return self._rewriter

    async def run(self, *, question: str, user_id: str) -> PipelineResult:
        request_id = uuid.uuid4().hex
        started = time.time()
        meta: dict[str, Any] = {"rewrites": 0, "tool_calls": 0, "files_read": []}

        in_res = self.guards.run_input(question, profile=self.profile)
        meta["input_scores"] = in_res.scores
        if not in_res.ok:
            return PipelineResult(
                answer=None, blocked=True, reason="input_blocked",
                metadata={**meta, "failed_scanners": in_res.failed,
                          "elapsed_ms": int((time.time() - started) * 1000)},
                request_id=request_id,
            )

        agent = self._build_agent()
        agent_input = {
            "messages": [SystemMessage(content=SYSTEM_PROMPT),
                          HumanMessage(content=in_res.sanitized_text)],
        }
        cfg = {"callbacks": self.callbacks,
               "metadata": {"user_id": user_id, "request_id": request_id,
                             "pipeline": self.profile}}
        agent_result = await agent.ainvoke(agent_input, config=cfg)

        for msg in agent_result.get("messages", []):
            if isinstance(msg, AIMessage):
                for call in (msg.tool_calls or []):
                    meta["tool_calls"] += 1
                    if call.get("name") == "read_file":
                        path = (call.get("args") or {}).get("file_path")
                        if path:
                            meta["files_read"].append(path)

        final_msg = agent_result["messages"][-1] if agent_result.get("messages") else None
        raw_answer = (
            final_msg.content if isinstance(final_msg, AIMessage) and isinstance(final_msg.content, str)
            else (str(final_msg.content) if final_msg is not None else "")
        )
        if not raw_answer.strip():
            raw_answer = "Não consegui produzir uma resposta a partir do código disponível."

        final_answer, blocked, failed = await apply_output_guards_with_rewrite(
            answer=raw_answer,
            prompt_for_rewrite=in_res.sanitized_text,
            profile=self.profile,
            guards=self.guards,
            rewriter=self._rewrite_chat(),
            max_rewrites=self.max_rewrites,
            meta=meta,
        )
        meta["elapsed_ms"] = int((time.time() - started) * 1000)

        if blocked:
            return PipelineResult(
                answer=None, blocked=True,
                reason="output_blocked_after_rewrites",
                metadata={**meta, "failed_scanners": failed},
                request_id=request_id,
            )

        return PipelineResult(
            answer=final_answer, blocked=False, reason=None,
            metadata=meta, request_id=request_id,
        )
