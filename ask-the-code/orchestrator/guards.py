"""LLM Guard wrappers — multi-profile scanner config.

Two profiles:

* `business` — Q&A over the source code. Default scanners.
* `diagnostics` — operational investigation over customer data. Adds
  Presidio entities (EMAIL/PHONE/PERSON/CC/IP/LOCATION) and BR-specific
  regex (CPF, CNPJ, telefone) to the SANITIZE lane.
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

BR_PII_PATTERNS = [
    r"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b",          # CPF
    r"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b",   # CNPJ
    r"\(?\d{2}\)?\s?9?\d{4}-?\d{4}",              # Telefone BR
]

DIAGNOSTICS_PII_ENTITIES = [
    "EMAIL_ADDRESS",
    "PHONE_NUMBER",
    "CREDIT_CARD",
    "IP_ADDRESS",
    "PERSON",
    "LOCATION",
]


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
    """Bundle of pre-loaded scanners keyed by profile."""

    def __init__(self) -> None:
        self._built = False
        self.input: dict[str, list[Any]] = {}
        self.output_sanitize: dict[str, list[Any]] = {}
        self.output_block: dict[str, list[Any]] = {}

    @property
    def ready(self) -> bool:
        return self._built

    def build(self) -> None:
        # ---- business profile -----------------------------------------------
        biz_input = [
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
        biz_sanitize: list[Any] = [
            OutputSecrets(redact_mode="all"),
            Sensitive(redact=True),
        ]
        redact_patterns = _json_list(os.environ.get("OUTPUT_REDACT_REGEX_PATTERNS", "[]"))
        if redact_patterns:
            biz_sanitize.append(
                OutputRegex(patterns=redact_patterns, is_blocked=False, redact=True),
            )
        biz_block: list[Any] = [
            OutputToxicity(threshold=float(os.environ.get("OUTPUT_TOXICITY_THRESHOLD", "0.7"))),
            Bias(threshold=float(os.environ.get("OUTPUT_BIAS_THRESHOLD", "0.7"))),
            MaliciousURLs(threshold=float(os.environ.get("MALICIOUS_URL_THRESHOLD", "0.7"))),
        ]
        block_patterns = _json_list(os.environ.get("OUTPUT_BLOCK_REGEX_PATTERNS", "[]"))
        if block_patterns:
            biz_block.append(
                OutputRegex(patterns=block_patterns, is_blocked=True, redact=False),
            )
        if os.environ.get("ENABLE_NO_REFUSAL", "false").lower() == "true":
            biz_block.append(NoRefusal(threshold=0.5))

        self.input["business"] = biz_input
        self.output_sanitize["business"] = biz_sanitize
        self.output_block["business"] = biz_block

        # ---- diagnostics profile --------------------------------------------
        diag_banned = _split_csv(os.environ.get(
            "DIAGNOSTICS_BANNED_TOPICS",
            "delete data,drop table,modify records,change configuration",
        ))
        diag_input = [
            PromptInjection(threshold=float(os.environ.get("PROMPT_INJECTION_THRESHOLD", "0.5"))),
            TokenLimit(limit=int(os.environ.get("INPUT_TOKEN_LIMIT", "2000"))),
            InputToxicity(threshold=float(os.environ.get("INPUT_TOXICITY_THRESHOLD", "0.7"))),
            BanTopics(topics=diag_banned, threshold=0.6),
            InputSecrets(),
        ]

        diag_sanitize: list[Any] = [
            OutputSecrets(redact_mode="all"),
            Sensitive(entity_types=DIAGNOSTICS_PII_ENTITIES, redact=True),
            OutputRegex(patterns=BR_PII_PATTERNS, is_blocked=False, redact=True),
        ]
        if redact_patterns:
            diag_sanitize.append(
                OutputRegex(patterns=redact_patterns, is_blocked=False, redact=True),
            )

        diag_block: list[Any] = [
            OutputToxicity(threshold=float(os.environ.get("OUTPUT_TOXICITY_THRESHOLD", "0.7"))),
            Bias(threshold=float(os.environ.get("OUTPUT_BIAS_THRESHOLD", "0.7"))),
            MaliciousURLs(threshold=float(os.environ.get("MALICIOUS_URL_THRESHOLD", "0.7"))),
        ]
        if block_patterns:
            diag_block.append(
                OutputRegex(patterns=block_patterns, is_blocked=True, redact=False),
            )

        self.input["diagnostics"] = diag_input
        self.output_sanitize["diagnostics"] = diag_sanitize
        self.output_block["diagnostics"] = diag_block

        self._built = True

    # ---- runners (profile-aware) --------------------------------------------
    def run_input(self, question: str, *, profile: str = "business") -> GuardResult:
        scanners = self.input.get(profile, self.input["business"])
        sanitized, valid, scores = scan_prompt(scanners, question)
        return GuardResult(sanitized, dict(valid), dict(scores),
                            [k for k, ok in valid.items() if not ok])

    def run_output_sanitize(self, prompt: str, answer: str, *, profile: str = "business") -> GuardResult:
        scanners = self.output_sanitize.get(profile, self.output_sanitize["business"])
        if not scanners:
            return GuardResult(answer, {}, {}, [])
        sanitized, valid, scores = scan_output(scanners, prompt, answer)
        return GuardResult(sanitized, dict(valid), dict(scores),
                            [k for k, ok in valid.items() if not ok])

    def run_output_block(self, prompt: str, answer: str, *, profile: str = "business") -> GuardResult:
        scanners = self.output_block.get(profile, self.output_block["business"])
        if not scanners:
            return GuardResult(answer, {}, {}, [])
        _, valid, scores = scan_output(scanners, prompt, answer)
        return GuardResult(answer, dict(valid), dict(scores),
                            [k for k, ok in valid.items() if not ok])
