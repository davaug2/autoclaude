"""FastAPI entry point — POST /ask wired to the LangChain pipeline."""
from __future__ import annotations

import asyncio
import contextlib
import json
import os
import sys
import time
import uuid
from typing import Any

import redis.asyncio as redis_async
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from guards import Guards
from mcp_client import MCPState
from pipeline import Pipeline

RATE_LIMIT_PER_HOUR = int(os.environ.get("RATE_LIMIT_PER_HOUR", "30"))
REDIS_URL = os.environ.get("REDIS_URL", "redis://redis:6379/0")


def _log(level: str, **fields: Any) -> None:
    payload = {
        "ts": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "service": "orchestrator",
        "level": level,
        **fields,
    }
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, default=str) + "\n")
    sys.stdout.flush()


def _build_callbacks() -> list[Any]:
    pk = os.environ.get("LANGFUSE_PUBLIC_KEY")
    sk = os.environ.get("LANGFUSE_SECRET_KEY")
    if not pk or not sk:
        return []
    try:  # langfuse v2
        from langfuse.callback import CallbackHandler  # type: ignore
        return [CallbackHandler(
            public_key=pk, secret_key=sk,
            host=os.environ.get("LANGFUSE_HOST", "https://cloud.langfuse.com"),
        )]
    except ImportError:
        pass
    try:  # langfuse v3
        from langfuse.langchain import CallbackHandler  # type: ignore
        return [CallbackHandler()]
    except ImportError:
        _log("warn", msg="langfuse package not importable, tracing disabled")
        return []


app = FastAPI(title="ask-the-code orchestrator", version="1.0.0")

state: dict[str, Any] = {
    "ready": False,
    "guards": Guards(),
    "mcp": MCPState(),
    "redis": None,
    "pipeline": None,
}


@app.on_event("startup")
async def _startup() -> None:
    _log("info", msg="loading LLM Guard scanners...")
    loop = asyncio.get_running_loop()
    await loop.run_in_executor(None, state["guards"].build)

    _log("info", msg="connecting to MCP server...")
    try:
        await state["mcp"].connect()
    except Exception as exc:  # noqa: BLE001
        _log("error", msg="mcp connect failed", error=str(exc))
        raise

    state["redis"] = redis_async.from_url(REDIS_URL, decode_responses=True)
    try:
        await state["redis"].ping()
    except Exception as exc:  # noqa: BLE001
        _log("warn", msg="redis ping failed", error=str(exc))

    callbacks = _build_callbacks()
    state["pipeline"] = Pipeline(state["guards"], state["mcp"], callbacks=callbacks)
    state["ready"] = True
    _log(
        "info",
        msg="orchestrator ready",
        mcp_tools=[t.name for t in state["mcp"].tools],
        langfuse=bool(callbacks),
    )


@app.on_event("shutdown")
async def _shutdown() -> None:
    if state["redis"]:
        with contextlib.suppress(Exception):
            await state["redis"].aclose()


class AskRequest(BaseModel):
    question: str = Field(..., min_length=1, max_length=10_000)
    user_id: str = Field(..., min_length=1, max_length=128)


class AskResponse(BaseModel):
    answer: str | None = None
    blocked: bool = False
    reason: str | None = None
    metadata: dict[str, Any] = Field(default_factory=dict)
    request_id: str


@app.get("/health")
async def health() -> dict[str, Any]:
    if not state["ready"]:
        raise HTTPException(status_code=503, detail="starting")
    if not state["guards"].ready:
        raise HTTPException(status_code=503, detail="guards not ready")
    if not state["mcp"].ready:
        raise HTTPException(status_code=503, detail="mcp not ready")
    return {"status": "ok", "mcp_tools": [t.name for t in state["mcp"].tools]}


async def _rate_limit(user_id: str) -> tuple[bool, int]:
    r: redis_async.Redis = state["redis"]
    bucket = int(time.time() // 3600)
    key = f"rl:{user_id}:{bucket}"
    count = await r.incr(key)
    if count == 1:
        await r.expire(key, 3700)
    return count <= RATE_LIMIT_PER_HOUR, int(count)


@app.post("/ask", response_model=AskResponse)
async def ask(req: AskRequest) -> AskResponse:
    if not state["ready"]:
        raise HTTPException(status_code=503, detail="not ready")

    allowed, used = await _rate_limit(req.user_id)
    if not allowed:
        rid = uuid.uuid4().hex
        _log("warn", event="rate_limited", request_id=rid, user_id=req.user_id,
             used=used, limit=RATE_LIMIT_PER_HOUR)
        return AskResponse(
            answer=None, blocked=True, reason="rate_limited",
            metadata={"used": used, "limit": RATE_LIMIT_PER_HOUR},
            request_id=rid,
        )

    pipeline: Pipeline = state["pipeline"]
    try:
        result = await pipeline.run(question=req.question, user_id=req.user_id)
    except Exception as exc:  # noqa: BLE001
        _log("error", event="pipeline_error", user_id=req.user_id, error=str(exc))
        raise HTTPException(status_code=500, detail=f"pipeline error: {exc}") from exc

    _log(
        "info",
        event="answered" if not result.blocked else "blocked",
        request_id=result.request_id,
        user_id=req.user_id,
        question=req.question,
        reason=result.reason,
        metadata=result.metadata,
    )

    return AskResponse(
        answer=result.answer,
        blocked=result.blocked,
        reason=result.reason,
        metadata=result.metadata,
        request_id=result.request_id,
    )
