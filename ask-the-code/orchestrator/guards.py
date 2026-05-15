"""LLM Guard wrappers — scanner instantiation + scan_prompt/scan_output runners.

The orchestrator calls `run_input_guard`, `run_output_sanitize`, and
`run_output_block` to keep the pipeline file readable.
"""
from __future__ import annotations

import json
import os
from dataclasses import dataclass
from typing import Any

from llm_guard import scan_output, scan_prompt
from llm_guard.input_scanners import (
    BanTopics,
    Code,
    PromptInjection,
    Secrets as InputSecrets,
    TokenLimit,
    Toxicity as InputToxicity,
)
from llm_guard.output_scanners import (
    Bias,
    MaliciousURLs,
    NoRefusal,
    Regex as OutputRegex,
    Secrets as OutputSecrets,
    Sensitive,
    Toxicity as OutputToxicity,
)


@dataclass
class GuardResult:
    sanitized_text: str
    valid: dict[str, bool]
    scores: dict[str, float]
    failed: list[str]

    @property
    def ok(self) -> bool:
        return not self.failed


def _split_csv(value: str) -> list[str]:
    return [item.strip() for item in value.split(",") if item.strip()]


def _json_list(value: str) -> list[str]:
    value = value.strip()
    if not value:
        return []
    try:
        parsed = json.loads(value)
    except json.JSONDecodeError:
        return _split_csv(value)
    return [str(item) for item in parsed] if isinstance(parsed, list) else []


class Guards:
    """Bundle of pre-loaded scanners. Built once at app startup."""

    def __init__(self) -> None:
        self.input_scanners: list[Any] = []
        self.output_sanitize: list[Any] = []
        self.output_block: list[Any] = []
        self._built = False

    def build(self) -> None:
        # ---- input lane ------------------------------------------------------
        self.input_scanners = [
            PromptInjection(threshold=float(os.environ.get("PROMPT_INJECTION_THRESHOLD", "0.5"))),
            TokenLimit(limit=int(os.environ.get("INPUT_TOKEN_LIMIT", "2000"))),
            InputToxicity(threshold=float(os.environ.get("INPUT_TOXICITY_THRESHOLD", "0.7"))),
            BanTopics(
                topics=_split_csv(os.environ.get(
                    "BANNED_TOPICS",
                    "personal advice,medical,legal,financial advice",
                )),
                threshold=0.6,
            ),
            Code(
                languages=["Python", "JavaScript", "Go", "Java", "C", "C++", "Ruby", "PHP"],
                is_blocked=True,
            ),
            InputSecrets(),
        ]

        # ---- output: sanitize lane ------------------------------------------
        self.output_sanitize = [
            OutputSecrets(redact_mode="all"),
            Sensitive(redact=True),
        ]
        redact_patterns = _json_list(os.environ.get("OUTPUT_REDACT_REGEX_PATTERNS", "[]"))
        if redact_patterns:
            self.output_sanitize.append(
                OutputRegex(patterns=redact_patterns, is_blocked=False, redact=True),
            )

        # ---- output: block lane ---------------------------------------------
        self.output_block = [
            OutputToxicity(threshold=float(os.environ.get("OUTPUT_TOXICITY_THRESHOLD", "0.7"))),
            Bias(threshold=float(os.environ.get("OUTPUT_BIAS_THRESHOLD", "0.7"))),
            MaliciousURLs(threshold=float(os.environ.get("MALICIOUS_URL_THRESHOLD", "0.7"))),
        ]
        block_patterns = _json_list(os.environ.get("OUTPUT_BLOCK_REGEX_PATTERNS", "[]"))
        if block_patterns:
            self.output_block.append(
                OutputRegex(patterns=block_patterns, is_blocked=True, redact=False),
            )
        if os.environ.get("ENABLE_NO_REFUSAL", "false").lower() == "true":
            self.output_block.append(NoRefusal(threshold=0.5))

        self._built = True

    @property
    def ready(self) -> bool:
        return self._built

    # ---- runners -------------------------------------------------------------
    def run_input(self, question: str) -> GuardResult:
        sanitized, valid, scores = scan_prompt(self.input_scanners, question)
        failed = [k for k, ok in valid.items() if not ok]
        return GuardResult(sanitized, dict(valid), dict(scores), failed)

    def run_output_sanitize(self, prompt: str, answer: str) -> GuardResult:
        if not self.output_sanitize:
            return GuardResult(answer, {}, {}, [])
        sanitized, valid, scores = scan_output(self.output_sanitize, prompt, answer)
        # `is_valid=False` here just means a redaction happened — NOT a block.
        return GuardResult(sanitized, dict(valid), dict(scores),
                            [k for k, ok in valid.items() if not ok])

    def run_output_block(self, prompt: str, answer: str) -> GuardResult:
        if not self.output_block:
            return GuardResult(answer, {}, {}, [])
        _, valid, scores = scan_output(self.output_block, prompt, answer)
        return GuardResult(answer, dict(valid), dict(scores),
                            [k for k, ok in valid.items() if not ok])
