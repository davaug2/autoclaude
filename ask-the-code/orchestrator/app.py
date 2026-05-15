"""FastAPI entry point.

Endpoints
---------
* POST /ask              — legacy alias for /ask/business (kept for back-compat)
* POST /ask/business     — code Q&A pipeline (BusinessPipeline)
* POST /ask/diagnostics  — operational diagnostics pipeline (DiagnosticsPipeline)
* GET  /health           — 200 once both pipelines are ready
"""
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
from mcp_client import MCPRegistry
from pipelines.business import BusinessPipeline
from pipelines.diagnostics import DiagnosticsPipeline

RATE_LIMIT_PER_HOUR = int(os.environ.get("RATE_LIMIT_PER_HOUR", "30"))
DIAGNOSTICS_RATE_LIMIT_PER_HOUR = int(os.environ.get("DIAGNOSTICS_RATE_LIMIT_PER_HOUR", "60"))
REDIS_URL = os.environ.get("REDIS_URL", "redis://redis:6379/0")
ENABLE_DIAGNOSTICS = os.environ.get("ENABLE_DIAGNOSTICS", "true").lower() == "true"


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
    try:
        from langfuse.callback import CallbackHandler  # type: ignore
        return [CallbackHandler(
            public_key=pk, secret_key=sk,
            host=os.environ.get("LANGFUSE_HOST", "https://cloud.langfuse.com"),
        )]
    except ImportError:
        pass
    try:
        from langfuse.langchain import CallbackHandler  # type: ignore
        return [CallbackHandler()]
    except ImportError:
        _log("warn", msg="langfuse package not importable, tracing disabled")
        return []


app = FastAPI(title="ask-the-code orchestrator", version="2.0.0")

state: dict[str, Any] = {
    "ready": False,
    "guards": Guards(),
    "mcp": MCPRegistry(),
    "redis": None,
    "business": None,
    "diagnostics": None,
}


@app.on_event("startup")
async def _startup() -> None:
    _log("info", msg="loading LLM Guard scanners...")
    loop = asyncio.get_running_loop()
    await loop.run_in_executor(None, state["guards"].build)

    _log("info", msg="connecting to MCP servers...")
    await state["mcp"].connect_business()
    if ENABLE_DIAGNOSTICS:
        try:
            await state["mcp"].connect_diagnostics()
        except Exception as exc:  # noqa: BLE001
            _log("error", msg="diagnostics MCP connect failed", error=str(exc))
            raise

    state["redis"] = redis_async.from_url(REDIS_URL, decode_responses=True)
    with contextlib.suppress(Exception):
        await state["redis"].ping()

    callbacks = _build_callbacks()
    state["business"] = BusinessPipeline(state["guards"], state["mcp"], callbacks=callbacks)
    if ENABLE_DIAGNOSTICS:
        state["diagnostics"] = DiagnosticsPipeline(state["guards"], state["mcp"], callbacks=callbacks)

    state["ready"] = True
    _log(
        "info", msg="orchestrator ready",
        business_tools=[t.name for t in state["mcp"].business_tools],
        diagnostics_tools=[t.name for t in state["mcp"].diagnostics_tools],
        langfuse=bool(callbacks),
    )


@app.on_event("shutdown")
async def _shutdown() -> None:
    if state["redis"]:
        with contextlib.suppress(Exception):
            await state["redis"].aclose()


class AskBusinessRequest(BaseModel):
    question: str = Field(..., min_length=1, max_length=10_000)
    user_id: str = Field(..., min_length=1, max_length=128)


class AskDiagnosticsRequest(BaseModel):
    question: str = Field(..., min_length=1, max_length=10_000)
    user_id: str = Field(..., min_length=1, max_length=128)
    company_hint: str | None = Field(default=None, max_length=256)


class AskResponse(BaseModel):
    answer: str | None = None
    blocked: bool = False
    reason: str | None = None
    metadata: dict[str, Any] = Field(default_factory=dict)
    request_id: str
    requires_clarification: bool | None = None
    companies: list[dict[str, Any]] | None = None


@app.get("/health")
async def health() -> dict[str, Any]:
    if not state["ready"]:
        raise HTTPException(status_code=503, detail="starting")
    if not state["guards"].ready:
        raise HTTPException(status_code=503, detail="guards not ready")
    if not state["mcp"].business_ready:
        raise HTTPException(status_code=503, detail="business mcp not ready")
    if ENABLE_DIAGNOSTICS and not state["mcp"].diagnostics_ready:
        raise HTTPException(status_code=503, detail="diagnostics mcp not ready")
    return {
        "status": "ok",
        "business_tools": [t.name for t in state["mcp"].business_tools],
        "diagnostics_tools": [t.name for t in state["mcp"].diagnostics_tools],
    }


async def _rate_limit(user_id: str, *, scope: str, limit_per_hour: int) -> tuple[bool, int]:
    r: redis_async.Redis = state["redis"]
    bucket = int(time.time() // 3600)
    key = f"rl:{scope}:{user_id}:{bucket}"
    count = await r.incr(key)
    if count == 1:
        await r.expire(key, 3700)
    return count <= limit_per_hour, int(count)


@app.post("/ask/business", response_model=AskResponse)
@app.post("/ask", response_model=AskResponse)
async def ask_business(req: AskBusinessRequest) -> AskResponse:
    if not state["ready"]:
        raise HTTPException(status_code=503, detail="not ready")

    allowed, used = await _rate_limit(req.user_id, scope="business",
                                       limit_per_hour=RATE_LIMIT_PER_HOUR)
    if not allowed:
        rid = uuid.uuid4().hex
        _log("warn", pipeline="business", event="rate_limited",
             request_id=rid, user_id=req.user_id, used=used,
             limit=RATE_LIMIT_PER_HOUR)
        return AskResponse(answer=None, blocked=True, reason="rate_limited",
                           metadata={"used": used, "limit": RATE_LIMIT_PER_HOUR},
                           request_id=rid)

    try:
        result = await state["business"].run(question=req.question, user_id=req.user_id)
    except Exception as exc:  # noqa: BLE001
        _log("error", pipeline="business", event="pipeline_error",
             user_id=req.user_id, error=str(exc))
        raise HTTPException(status_code=500, detail=f"pipeline error: {exc}") from exc

    _log("info", pipeline="business",
         event="answered" if not result.blocked else "blocked",
         request_id=result.request_id, user_id=req.user_id,
         question=req.question, reason=result.reason, metadata=result.metadata)

    return AskResponse(
        answer=result.answer, blocked=result.blocked, reason=result.reason,
        metadata=result.metadata, request_id=result.request_id,
    )


@app.post("/ask/diagnostics", response_model=AskResponse)
async def ask_diagnostics(req: AskDiagnosticsRequest) -> AskResponse:
    if not state["ready"]:
        raise HTTPException(status_code=503, detail="not ready")
    if not ENABLE_DIAGNOSTICS or state["diagnostics"] is None:
        raise HTTPException(status_code=404, detail="diagnostics pipeline disabled")

    allowed, used = await _rate_limit(req.user_id, scope="diagnostics",
                                       limit_per_hour=DIAGNOSTICS_RATE_LIMIT_PER_HOUR)
    if not allowed:
        rid = uuid.uuid4().hex
        _log("warn", pipeline="diagnostics", event="rate_limited",
             request_id=rid, user_id=req.user_id, used=used,
             limit=DIAGNOSTICS_RATE_LIMIT_PER_HOUR)
        return AskResponse(answer=None, blocked=True, reason="rate_limited",
                           metadata={"used": used,
                                     "limit": DIAGNOSTICS_RATE_LIMIT_PER_HOUR},
                           request_id=rid)

    try:
        result = await state["diagnostics"].run(
            question=req.question, user_id=req.user_id,
            company_hint=req.company_hint,
        )
    except Exception as exc:  # noqa: BLE001
        _log("error", pipeline="diagnostics", event="pipeline_error",
             user_id=req.user_id, error=str(exc))
        raise HTTPException(status_code=500, detail=f"pipeline error: {exc}") from exc

    # AUDIT log — who queried which company, with what
    _log(
        "audit",
        pipeline="diagnostics",
        event="answered" if not result.blocked else "blocked",
        request_id=result.request_id,
        user_id=req.user_id,
        question=req.question,
        company_hint=req.company_hint,
        company_id=result.metadata.get("company_id"),
        company_name=result.metadata.get("company_name"),
        reason=result.reason,
        tool_calls=result.metadata.get("tool_calls"),
        redactions=result.metadata.get("redactions"),
        rewrites=result.metadata.get("rewrites"),
        elapsed_ms=result.metadata.get("elapsed_ms"),
    )

    return AskResponse(
        answer=result.answer,
        blocked=result.blocked,
        reason=result.reason,
        metadata=result.metadata,
        request_id=result.request_id,
        requires_clarification=result.requires_clarification or None,
        companies=result.extras.get("companies") if result.extras else None,
    )
