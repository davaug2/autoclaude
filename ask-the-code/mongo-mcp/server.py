"""mongo-mcp — read-only Mongo tools scoped to a single companyId.

Tools:
* search_audit_logs(company_id, start_date, end_date, event_type?, actor_id?, limit=100)
* get_conversation_messages(company_id, conversation_id, limit=200)
* search_messages(company_id, text_query, start_date, end_date, limit=50)

Every Mongo filter is built starting with `{"companyId": <uuid>}` so a
malicious extra key in user input cannot widen the scope. Both UUIDs are
validated before reaching the query.
"""
from __future__ import annotations

import json
import os
import sys
import uuid
from datetime import datetime, timezone
from typing import Any

from mcp.server.fastmcp import FastMCP
from pymongo import ASCENDING, MongoClient

MONGO_URL = os.environ["MONGO_URL"]
DB_NAME = os.environ.get("MONGO_DB", "operations")
AUDIT_COLL = os.environ.get("MONGO_AUDIT_COLLECTION", "audit_logs")
MESSAGES_COLL = os.environ.get("MONGO_MESSAGES_COLLECTION", "messages")

MAX_AUDIT = int(os.environ.get("MAX_AUDIT_LOGS", "100"))
MAX_MESSAGES = int(os.environ.get("MAX_MESSAGES", "200"))
MAX_SEARCH = int(os.environ.get("MAX_MESSAGE_SEARCH", "50"))

mcp = FastMCP(
    "mongo-mcp",
    host=os.environ.get("MCP_HOST", "0.0.0.0"),
    port=int(os.environ.get("MCP_PORT", "3000")),
)
_client = MongoClient(MONGO_URL, uuidRepresentation="standard",
                      serverSelectionTimeoutMS=5000)
db = _client[DB_NAME]


def _log(level: str, **fields: Any) -> None:
    sys.stdout.write(json.dumps({"service": "mongo-mcp", "level": level, **fields},
                                 ensure_ascii=False, default=str) + "\n")
    sys.stdout.flush()


def _uuid(value: str, field: str) -> str:
    try:
        return str(uuid.UUID(value))
    except (TypeError, ValueError, AttributeError) as exc:
        raise ValueError(f"{field} must be a valid UUID, got {value!r}") from exc


def _parse_dt(value: str | datetime, field: str) -> datetime:
    if isinstance(value, datetime):
        return value if value.tzinfo else value.replace(tzinfo=timezone.utc)
    try:
        dt = datetime.fromisoformat(str(value))
    except ValueError as exc:
        raise ValueError(f"{field} must be ISO datetime") from exc
    return dt if dt.tzinfo else dt.replace(tzinfo=timezone.utc)


def _serialise(doc: dict[str, Any]) -> dict[str, Any]:
    out: dict[str, Any] = {}
    for k, v in doc.items():
        if k == "_id":
            out["_id"] = str(v)
        elif isinstance(v, datetime):
            out[k] = v.isoformat()
        elif isinstance(v, uuid.UUID):
            out[k] = str(v)
        else:
            out[k] = v
    return out


@mcp.tool()
def search_audit_logs(
    company_id: str,
    start_date: str,
    end_date: str,
    event_type: str | None = None,
    actor_id: str | None = None,
    limit: int = 100,
) -> list[dict[str, Any]]:
    """Return audit log events for the company within [start_date, end_date]."""
    cid = _uuid(company_id, "company_id")
    sd = _parse_dt(start_date, "start_date")
    ed = _parse_dt(end_date, "end_date")
    if ed < sd:
        raise ValueError("end_date must be >= start_date")
    lim = max(1, min(int(limit), MAX_AUDIT))

    flt: dict[str, Any] = {"companyId": cid, "timestamp": {"$gte": sd, "$lte": ed}}
    if event_type:
        if len(event_type) > 128:
            raise ValueError("event_type too long")
        flt["eventType"] = event_type
    if actor_id:
        if len(actor_id) > 256:
            raise ValueError("actor_id too long")
        flt["actorId"] = actor_id

    cursor = db[AUDIT_COLL].find(flt).sort("timestamp", -1).limit(lim)
    rows = [_serialise(doc) for doc in cursor]
    _log("info", event="search_audit_logs", company_id=cid, hits=len(rows))
    return rows


@mcp.tool()
def get_conversation_messages(
    company_id: str,
    conversation_id: str,
    limit: int = 200,
) -> list[dict[str, Any]]:
    """Return messages of one conversation, ordered by timestamp ascending.

    Filter always pins companyId AND conversationId (defense in depth).
    """
    cid = _uuid(company_id, "company_id")
    conv = _uuid(conversation_id, "conversation_id")
    lim = max(1, min(int(limit), MAX_MESSAGES))

    flt = {"companyId": cid, "conversationId": conv}
    cursor = db[MESSAGES_COLL].find(flt).sort("timestamp", ASCENDING).limit(lim)
    rows = [_serialise(doc) for doc in cursor]
    _log("info", event="get_conversation_messages",
         company_id=cid, conversation_id=conv, hits=len(rows))
    return rows


@mcp.tool()
def search_messages(
    company_id: str,
    text_query: str,
    start_date: str,
    end_date: str,
    limit: int = 50,
) -> list[dict[str, Any]]:
    """Full-text search on messages within a date window. Requires a text index."""
    cid = _uuid(company_id, "company_id")
    sd = _parse_dt(start_date, "start_date")
    ed = _parse_dt(end_date, "end_date")
    if ed < sd:
        raise ValueError("end_date must be >= start_date")
    q = (text_query or "").strip()
    if len(q) < 2:
        raise ValueError("text_query too short")
    if len(q) > 256:
        raise ValueError("text_query too long")
    lim = max(1, min(int(limit), MAX_SEARCH))

    flt = {
        "companyId": cid,
        "timestamp": {"$gte": sd, "$lte": ed},
        "$text": {"$search": q},
    }
    projection = {"score": {"$meta": "textScore"}}
    try:
        cursor = (db[MESSAGES_COLL]
                  .find(flt, projection)
                  .sort([("score", {"$meta": "textScore"})])
                  .limit(lim))
        rows = [_serialise(doc) for doc in cursor]
    except Exception as exc:  # noqa: BLE001
        # No text index → fall back to a case-insensitive regex on `text`.
        _log("warn", event="search_messages_fallback", error=str(exc))
        flt_fb = {
            "companyId": cid,
            "timestamp": {"$gte": sd, "$lte": ed},
            "text": {"$regex": q[:64], "$options": "i"},
        }
        cursor = db[MESSAGES_COLL].find(flt_fb).limit(lim)
        rows = [_serialise(doc) for doc in cursor]

    _log("info", event="search_messages", company_id=cid, hits=len(rows))
    return rows


if __name__ == "__main__":
    # Cheap connectivity sanity check at startup.
    try:
        _client.admin.command("ping")
    except Exception as exc:  # noqa: BLE001
        _log("warn", event="mongo_ping_failed", error=str(exc))
    mcp.run(transport="sse")
