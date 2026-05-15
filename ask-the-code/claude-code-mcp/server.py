"""MCP server exposing read-only repo inspection tools.

We deliberately do NOT embed the `claude` CLI as an MCP server here. The
public Claude Code docs describe Claude Code as an MCP *client*; there is
no maintained `claude mcp serve` mode that re-exposes Read/Grep/Glob over
the wire. Implementing those three tools natively in Python is simpler,
cheaper (no extra Anthropic API calls inside the MCP layer), and easier
to lock down to /repo. See the README for the rationale.

Transport: SSE on :3000, internal Docker network only.
"""
from __future__ import annotations

import os
import re
import subprocess
from pathlib import Path

from mcp.server.fastmcp import FastMCP

REPO_ROOT = Path(os.environ.get("REPO_DIR", "/repo")).resolve()
MAX_READ_BYTES = int(os.environ.get("MAX_READ_BYTES", str(256 * 1024)))
MAX_GREP_RESULTS = int(os.environ.get("MAX_GREP_RESULTS", "200"))
MAX_GLOB_RESULTS = int(os.environ.get("MAX_GLOB_RESULTS", "500"))

mcp = FastMCP(
    "ask-the-code-repo",
    host=os.environ.get("MCP_HOST", "0.0.0.0"),
    port=int(os.environ.get("MCP_PORT", "3000")),
)


def _safe_resolve(rel_or_abs: str) -> Path:
    """Resolve a user-supplied path strictly inside REPO_ROOT."""
    raw = Path(rel_or_abs)
    candidate = (REPO_ROOT / raw).resolve() if not raw.is_absolute() else raw.resolve()
    try:
        candidate.relative_to(REPO_ROOT)
    except ValueError as exc:
        raise ValueError(f"path escapes repo root: {rel_or_abs}") from exc
    return candidate


@mcp.tool()
def read_file(file_path: str) -> str:
    """Read a file from the repository. Paths are resolved under /repo.

    Returns up to MAX_READ_BYTES; truncated content is suffixed with a note.
    """
    path = _safe_resolve(file_path)
    if not path.is_file():
        raise FileNotFoundError(f"not a file: {file_path}")
    data = path.read_bytes()[: MAX_READ_BYTES + 1]
    truncated = len(data) > MAX_READ_BYTES
    text = data[:MAX_READ_BYTES].decode("utf-8", errors="replace")
    if truncated:
        text += f"\n\n[... truncated at {MAX_READ_BYTES} bytes ...]"
    return text


@mcp.tool()
def grep(pattern: str, path: str = ".", glob: str | None = None) -> list[dict]:
    """Search for a regex pattern in the repository. Mimics ripgrep semantics.

    Args:
        pattern: Python regex.
        path: subdirectory to search (relative to /repo). Default = whole repo.
        glob: optional shell glob to restrict files (e.g. "*.py").

    Returns up to MAX_GREP_RESULTS hits, each {file, line_no, line}.
    """
    base = _safe_resolve(path) if path not in ("", ".") else REPO_ROOT
    if not base.exists():
        raise FileNotFoundError(f"no such path: {path}")

    cmd: list[str] = ["grep", "-RnIE", "--binary-files=without-match",
                       "--exclude-dir=.git", "--exclude-dir=node_modules",
                       "--exclude-dir=.venv", "--exclude-dir=__pycache__"]
    if glob:
        cmd += ["--include", glob]
    cmd += ["-e", pattern, str(base)]

    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=20)
    if proc.returncode not in (0, 1):
        raise RuntimeError(f"grep failed: {proc.stderr.strip()}")

    hits: list[dict] = []
    for line in proc.stdout.splitlines():
        m = re.match(r"^(?P<file>[^:]+):(?P<line>\d+):(?P<text>.*)$", line)
        if not m:
            continue
        try:
            rel = Path(m["file"]).resolve().relative_to(REPO_ROOT)
        except ValueError:
            continue
        hits.append({"file": str(rel), "line_no": int(m["line"]),
                     "line": m["text"][:400]})
        if len(hits) >= MAX_GREP_RESULTS:
            break
    return hits


@mcp.tool()
def glob(pattern: str) -> list[str]:
    """List files matching a glob pattern (rooted at /repo)."""
    results: list[str] = []
    for path in REPO_ROOT.rglob(pattern):
        if path.is_file():
            try:
                results.append(str(path.resolve().relative_to(REPO_ROOT)))
            except ValueError:
                continue
            if len(results) >= MAX_GLOB_RESULTS:
                break
    return results


if __name__ == "__main__":
    if not REPO_ROOT.is_dir():
        raise SystemExit(f"REPO_DIR not mounted: {REPO_ROOT}")
    mcp.run(transport="sse")
