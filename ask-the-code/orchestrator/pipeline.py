"""End-to-end async pipeline.

  rate-limit → input-guard → ReAct agent (MCP tools) →
      output-sanitize → output-block → [rewrite-loop] → reply

The agent is a LangGraph ReAct agent (`create_react_agent`) bound to the
MCP tools fetched from `claude-code-mcp`.
"""
from __future__ import annotations

import os
import time
import uuid
from dataclasses import dataclass
from typing import Any

from langchain_anthropic import ChatAnthropic
from langchain_core.messages import AIMessage, HumanMessage, SystemMessage, ToolMessage
from langgraph.prebuilt import create_react_agent

from guards import Guards
from mcp_client import MCPState
from rewrite import make_rewriter, rewrite_once

SYSTEM_PROMPT = (
    "You are a read-only code analyst answering questions about a codebase. "
    "Use the available tools (read_file, grep, glob) to inspect files under "
    "/repo. Cite file paths and line numbers when relevant. Never invent "
    "code you have not seen. If the answer is not in the repo, say so."
)


@dataclass
class PipelineResult:
    answer: str | None
    blocked: bool
    reason: str | None
    metadata: dict[str, Any]
    request_id: str


class Pipeline:
    def __init__(self, guards: Guards, mcp: MCPState, callbacks: list[Any] | None = None) -> None:
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
            model=self.model,
            temperature=0,
            max_tokens=self.agent_max_tokens,
            timeout=self.agent_timeout_s,
        )
        self._agent = create_react_agent(llm, self.mcp.tools, prompt=SYSTEM_PROMPT)
        return self._agent

    def _build_rewriter(self) -> ChatAnthropic:
        if self._rewriter is None:
            self._rewriter = make_rewriter()
        return self._rewriter

    async def run(self, *, question: str, user_id: str) -> PipelineResult:
        request_id = uuid.uuid4().hex
        started = time.time()
        meta: dict[str, Any] = {"rewrites": 0, "tool_calls": 0, "files_read": []}

        # ---- input guard -----------------------------------------------------
        in_res = self.guards.run_input(question)
        meta["input_scores"] = in_res.scores
        if not in_res.ok:
            return PipelineResult(
                answer=None, blocked=True, reason="input_blocked",
                metadata={**meta, "failed_scanners": in_res.failed,
                          "elapsed_ms": int((time.time() - started) * 1000)},
                request_id=request_id,
            )

        # ---- agent invocation (ReAct loop with MCP tools) -------------------
        agent = self._build_agent()
        agent_input = {
            "messages": [
                SystemMessage(content=SYSTEM_PROMPT),
                HumanMessage(content=in_res.sanitized_text),
            ],
        }
        cfg = {"callbacks": self.callbacks, "metadata": {"user_id": user_id,
                                                          "request_id": request_id}}
        agent_result = await agent.ainvoke(agent_input, config=cfg)

        # Walk messages to extract tool calls / files read
        for msg in agent_result.get("messages", []):
            if isinstance(msg, AIMessage):
                for call in (msg.tool_calls or []):
                    meta["tool_calls"] += 1
                    if call.get("name") == "read_file":
                        path = (call.get("args") or {}).get("file_path")
                        if path:
                            meta["files_read"].append(path)
            elif isinstance(msg, ToolMessage):
                pass  # could capture content size if needed

        # Final assistant text
        final_msg = agent_result["messages"][-1] if agent_result.get("messages") else None
        raw_answer = (
            final_msg.content if isinstance(final_msg, AIMessage) and isinstance(final_msg.content, str)
            else (str(final_msg.content) if final_msg is not None else "")
        )
        if not raw_answer.strip():
            raw_answer = "Não consegui produzir uma resposta a partir do código disponível."

        # ---- output sanitize (Secrets / PII / redact regex) ------------------
        san = self.guards.run_output_sanitize(in_res.sanitized_text, raw_answer)
        answer = san.sanitized_text
        meta["output_sanitize_scores"] = san.scores
        meta["redactions"] = san.failed  # scanners that triggered a redaction

        # ---- output block (Toxicity / Bias / MaliciousURLs / block regex) ---
        blk = self.guards.run_output_block(in_res.sanitized_text, answer)
        meta["output_block_scores"] = blk.scores

        attempts = 0
        while not blk.ok and attempts < self.max_rewrites:
            attempts += 1
            meta["rewrites"] = attempts
            rewriter = self._build_rewriter()
            answer = await rewrite_once(
                rewriter,
                question=in_res.sanitized_text,
                answer=answer,
                issues=blk.failed,
            )
            # Re-run BOTH lanes — rewrite could re-introduce secrets too.
            san = self.guards.run_output_sanitize(in_res.sanitized_text, answer)
            answer = san.sanitized_text
            meta["redactions"] = list(set(meta["redactions"]) | set(san.failed))
            blk = self.guards.run_output_block(in_res.sanitized_text, answer)
            meta[f"output_block_scores_after_rewrite_{attempts}"] = blk.scores

        meta["elapsed_ms"] = int((time.time() - started) * 1000)

        if not blk.ok:
            return PipelineResult(
                answer=None, blocked=True,
                reason="output_blocked_after_rewrites",
                metadata={**meta, "failed_scanners": blk.failed},
                request_id=request_id,
            )

        return PipelineResult(
            answer=answer, blocked=False, reason=None,
            metadata=meta, request_id=request_id,
        )
