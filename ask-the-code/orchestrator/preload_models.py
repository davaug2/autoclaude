"""Eagerly download every HuggingFace model used by LLM Guard scanners.

Runs once during `docker build` (as the unprivileged app user) so models
end up baked into the image layer at $HF_HOME and the container does not
have to call out to huggingface.co at runtime.

We instantiate every scanner the orchestrator *could* configure, regardless
of the current env vars, so toggling a scanner on at runtime via `.env`
does not require a rebuild.
"""
from __future__ import annotations

import sys


def main() -> int:
    print("[preload] importing scanners...", flush=True)
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

    targets = [
        ("PromptInjection", lambda: PromptInjection(threshold=0.5)),
        ("TokenLimit", lambda: TokenLimit(limit=2000)),
        ("InputToxicity", lambda: InputToxicity(threshold=0.7)),
        ("BanTopics", lambda: BanTopics(topics=["medical"], threshold=0.6)),
        ("Code", lambda: Code(languages=["Python"], is_blocked=True)),
        ("InputSecrets", lambda: InputSecrets()),
        ("OutputSecrets", lambda: OutputSecrets(redact_mode="all")),
        ("Sensitive", lambda: Sensitive(redact=True)),
        ("OutputRegex", lambda: OutputRegex(patterns=["dummy"], is_blocked=True, redact=False)),
        ("Bias", lambda: Bias(threshold=0.7)),
        ("MaliciousURLs", lambda: MaliciousURLs(threshold=0.7)),
        ("OutputToxicity", lambda: OutputToxicity(threshold=0.7)),
        ("NoRefusal", lambda: NoRefusal(threshold=0.5)),
    ]

    failures: list[str] = []
    for name, factory in targets:
        try:
            print(f"[preload] {name}...", flush=True)
            factory()
        except Exception as exc:  # noqa: BLE001
            failures.append(f"{name}: {exc}")
            print(f"[preload] {name} FAILED: {exc}", flush=True)

    if failures:
        print(f"[preload] {len(failures)} failures:\n  - " + "\n  - ".join(failures),
              flush=True)
        return 1
    print(f"[preload] OK — {len(targets)} scanners preloaded", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
