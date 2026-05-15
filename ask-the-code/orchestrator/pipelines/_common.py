"""Shared dataclasses + helpers used by both pipelines."""
from __future__ import annotations

import time
from dataclasses import dataclass, field
from typing import Any, Callable, Awaitable

from langchain_anthropic import ChatAnthropic

from guards import Guards
from rewrite import rewrite_once


@dataclass
class PipelineResult:
    answer: str | None
    blocked: bool
    reason: str | None
    metadata: dict[str, Any]
    request_id: str
    requires_clarification: bool = False
    extras: dict[str, Any] = field(default_factory=dict)


async def apply_output_guards_with_rewrite(
    *,
    answer: str,
    prompt_for_rewrite: str,
    profile: str,
    guards: Guards,
    rewriter: ChatAnthropic,
    max_rewrites: int,
    meta: dict[str, Any],
) -> tuple[str, bool, list[str]]:
    """Run sanitize → block → (rewrite × N) → block again.

    Mutates `meta` in place to record scores/redactions/rewrites.

    Returns `(final_answer, blocked, failed_scanners)`.
    """
    san = guards.run_output_sanitize(prompt_for_rewrite, answer, profile=profile)
    answer = san.sanitized_text
    meta["output_sanitize_scores"] = san.scores
    meta["redactions"] = list(san.failed)

    blk = guards.run_output_block(prompt_for_rewrite, answer, profile=profile)
    meta["output_block_scores"] = blk.scores

    attempts = 0
    while not blk.ok and attempts < max_rewrites:
        attempts += 1
        meta["rewrites"] = attempts
        answer = await rewrite_once(
            rewriter,
            question=prompt_for_rewrite,
            answer=answer,
            issues=blk.failed,
        )
        san = guards.run_output_sanitize(prompt_for_rewrite, answer, profile=profile)
        answer = san.sanitized_text
        meta["redactions"] = list(set(meta["redactions"]) | set(san.failed))
        blk = guards.run_output_block(prompt_for_rewrite, answer, profile=profile)
        meta[f"output_block_scores_after_rewrite_{attempts}"] = blk.scores

    return answer, (not blk.ok), list(blk.failed)
