"""Diagnostics pipeline — operational investigation.

  rate-limit → input-guard (diagnostics profile) →
      resolve company_id (companies-mcp.search_companies if hint given) →
      ReAct agent over [companies, postgres, mongo, s3] MCP tools →
      output-sanitize (PII + BR regex) → output-block → [rewrite] → reply

Security invariants:
  * Every tool call carries a UUID company_id resolved up front.
  * The system prompt locks the agent to the resolved company.
  * Hard limits on result sizes are enforced inside each MCP server.
"""
from __future__ import annotations

import json
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

UUID_RE = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"

BASE_SYSTEM_PROMPT = """\
You are an operational diagnostics assistant. You help the support team investigate \
problems in customer chatbots using the available MCP tools.

CRITICAL RULES
- Every query is scoped to a single company. Never call a tool without a valid company_id.
- Do not invent data. If a tool returns nothing, say it was not found.
- Do not expose credentials, tokens, passwords, or API keys even if they appear in data.
- When showing conversation messages, share only what is needed to answer.

WHERE TO LOOK FIRST
- Behavior issues → search_audit_logs.
- Customer interaction → list_conversations + get_conversation_messages.
- Setup questions → list_config_keys, then get_company_configs.
- Chatbot logic → list_chatbot_flows + download_chatbot_flow.
"""

RESOLVED_PROMPT_SUFFIX = """
COMPANY_ID FOR THIS SESSION: {company_id}
COMPANY_NAME: {company_name}
Always pass this company_id to every tool call. Never query any other company.
"""

UNRESOLVED_PROMPT_SUFFIX = """
The user did not specify a company directly. If you cannot infer one from the \
question, call `search_companies` first to identify the right company, then \
proceed. Never call other tools without a confirmed company_id.
"""


def _maybe_parse(value: Any) -> Any:
    """LangChain MCP tools may return JSON strings; normalise to objects."""
    if isinstance(value, (list, dict)):
        return value
    if isinstance(value, str):
        try:
            return json.loads(value)
        except json.JSONDecodeError:
            return value
    return value


class DiagnosticsPipeline:
    profile = "diagnostics"

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
        self._agents: dict[str, Any] = {}

    def _rewrite_chat(self) -> ChatAnthropic:
        if self._rewriter is None:
            self._rewriter = make_rewriter()
        return self._rewriter

    def _agent_for(self, prompt: str) -> Any:
        if prompt in self._agents:
            return self._agents[prompt]
        llm = ChatAnthropic(
            model=self.model, temperature=0,
            max_tokens=self.agent_max_tokens, timeout=self.agent_timeout_s,
        )
        agent = create_react_agent(llm, self.mcp.diagnostics_tools, prompt=prompt)
        self._agents[prompt] = agent
        return agent

    async def _resolve_company(self, hint: str) -> dict[str, Any]:
        """Call companies-mcp.search_companies directly (without the agent)."""
        tools = {t.name: t for t in self.mcp.diagnostics_tools}
        search = tools.get("search_companies")
        if search is None:
            return {"error": "search_companies tool not available"}
        try:
            raw = await search.ainvoke({"query": hint, "limit": 5})
        except Exception as exc:  # noqa: BLE001
            return {"error": f"search_companies failed: {exc}"}
        companies = _maybe_parse(raw)
        if isinstance(companies, dict) and "results" in companies:
            companies = companies["results"]
        if not isinstance(companies, list):
            return {"error": "unexpected response from search_companies"}
        return {"results": companies}

    async def run(self, *, question: str, user_id: str,
                  company_hint: str | None = None) -> PipelineResult:
        request_id = uuid.uuid4().hex
        started = time.time()
        meta: dict[str, Any] = {
            "rewrites": 0,
            "tool_calls": [],
            "company_id": None,
            "company_name": None,
        }

        # ---- input guard -----------------------------------------------------
        in_res = self.guards.run_input(question, profile=self.profile)
        meta["input_scores"] = in_res.scores
        if not in_res.ok:
            return PipelineResult(
                answer=None, blocked=True, reason="input_blocked",
                metadata={**meta, "failed_scanners": in_res.failed,
                          "elapsed_ms": int((time.time() - started) * 1000)},
                request_id=request_id,
            )

        # ---- resolve company up front (if hint provided) --------------------
        resolved_id: str | None = None
        resolved_name: str | None = None
        if company_hint:
            res = await self._resolve_company(company_hint)
            if "error" in res:
                return PipelineResult(
                    answer=None, blocked=True, reason="company_resolution_error",
                    metadata={**meta, "error": res["error"]},
                    request_id=request_id,
                )
            companies = res["results"]
            if len(companies) == 0:
                return PipelineResult(
                    answer=None, blocked=True, reason="company_not_found",
                    metadata={**meta, "hint": company_hint},
                    request_id=request_id,
                )
            if len(companies) > 1:
                return PipelineResult(
                    answer=None, blocked=False, reason="multiple_companies_matched",
                    metadata={**meta, "elapsed_ms": int((time.time() - started) * 1000)},
                    request_id=request_id,
                    requires_clarification=True,
                    extras={"companies": companies},
                )
            company = companies[0]
            resolved_id = company.get("company_id")
            resolved_name = company.get("name")
            meta["company_id"] = resolved_id
            meta["company_name"] = resolved_name

        # ---- agent prompt: lock to company if resolved ----------------------
        if resolved_id:
            system_prompt = BASE_SYSTEM_PROMPT + RESOLVED_PROMPT_SUFFIX.format(
                company_id=resolved_id, company_name=resolved_name or "unknown",
            )
        else:
            system_prompt = BASE_SYSTEM_PROMPT + UNRESOLVED_PROMPT_SUFFIX

        agent = self._agent_for(system_prompt)
        agent_input = {
            "messages": [SystemMessage(content=system_prompt),
                          HumanMessage(content=in_res.sanitized_text)],
        }
        cfg = {
            "callbacks": self.callbacks,
            "metadata": {"user_id": user_id, "request_id": request_id,
                          "pipeline": self.profile, "company_id": resolved_id},
            "recursion_limit": int(os.environ.get("DIAGNOSTICS_AGENT_MAX_ITERS", "30")),
        }
        agent_result = await agent.ainvoke(agent_input, config=cfg)

        # ---- capture tool-call audit trail ----------------------------------
        tool_id_to_meta: dict[str, dict[str, Any]] = {}
        for msg in agent_result.get("messages", []):
            if isinstance(msg, AIMessage):
                for call in (msg.tool_calls or []):
                    record = {
                        "tool": call.get("name"),
                        "args": call.get("args") or {},
                        "result_count": None,
                    }
                    meta["tool_calls"].append(record)
                    tool_id_to_meta[call.get("id", "")] = record
                    # post-hoc detect company_id used
                    if resolved_id is None:
                        cid = (call.get("args") or {}).get("company_id")
                        if cid:
                            meta["company_id"] = cid
            else:
                # ToolMessage carries tool_call_id; compute result size
                tool_call_id = getattr(msg, "tool_call_id", None)
                if tool_call_id and tool_call_id in tool_id_to_meta:
                    content = getattr(msg, "content", "")
                    parsed = _maybe_parse(content)
                    if isinstance(parsed, list):
                        tool_id_to_meta[tool_call_id]["result_count"] = len(parsed)
                    elif isinstance(parsed, dict):
                        tool_id_to_meta[tool_call_id]["result_count"] = 1
                    elif isinstance(parsed, str):
                        tool_id_to_meta[tool_call_id]["result_count"] = len(parsed)

        final_msg = agent_result["messages"][-1] if agent_result.get("messages") else None
        raw_answer = (
            final_msg.content if isinstance(final_msg, AIMessage) and isinstance(final_msg.content, str)
            else (str(final_msg.content) if final_msg is not None else "")
        )
        if not raw_answer.strip():
            raw_answer = "Não consegui produzir uma resposta a partir dos dados disponíveis."

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
