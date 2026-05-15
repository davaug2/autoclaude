"""Rewrite loop — if the output guard's block lane fails, ask the LLM to
rewrite the answer (without MCP tools, plain text in/out) and re-check.
"""
from __future__ import annotations

import os

from langchain_anthropic import ChatAnthropic
from langchain_core.messages import HumanMessage, SystemMessage

REWRITE_SYSTEM = (
    "You rewrite an assistant's answer to remove problematic content "
    "(bias, toxicity, malicious URLs, anything flagged by safety scanners) "
    "while preserving the technical content the user needs. "
    "Do not mention that a rewrite happened. Do not add disclaimers. "
    "Keep code, file paths, and line numbers exactly as they appear."
)


def make_rewriter() -> ChatAnthropic:
    return ChatAnthropic(
        model=os.environ.get("ANTHROPIC_MODEL", "claude-opus-4-7"),
        temperature=0,
        max_tokens=int(os.environ.get("REWRITE_MAX_TOKENS", "2048")),
        timeout=float(os.environ.get("REWRITE_TIMEOUT_S", "60")),
    )


async def rewrite_once(
    rewriter: ChatAnthropic,
    *,
    question: str,
    answer: str,
    issues: list[str],
) -> str:
    user_prompt = (
        f"The following answer was flagged for: {', '.join(issues) or 'unspecified'}.\n\n"
        f"Original user question:\n{question}\n\n"
        f"Original answer:\n{answer}\n\n"
        "Rewritten answer:"
    )
    msg = await rewriter.ainvoke(
        [SystemMessage(content=REWRITE_SYSTEM), HumanMessage(content=user_prompt)],
    )
    text = msg.content if isinstance(msg.content, str) else str(msg.content)
    return text.strip()
