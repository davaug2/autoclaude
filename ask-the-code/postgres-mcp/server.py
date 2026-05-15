"""postgres-mcp — read-only Postgres tools scoped to a single company_id.

Tools:
* list_conversations(company_id, start_date, end_date, status?, limit=50)
* get_conversation(company_id, conversation_id)
* get_company_configs(company_id, config_keys?)
* list_config_keys(company_id)

Every query has `WHERE company_id = $1` as the first predicate. UUIDs are
validated before they reach the DB. Sensitive config values (any key matching
SENSITIVE_KEY_PATTERN) are masked.

Default schema (override via env if your tables differ):

    conversations(id uuid, company_id uuid, started_at, ended_at,
                  channel text, status text, customer_id_hashed text)
    company_configs(company_id uuid, config_key text, config_value text)
"""
from __future__ import annotations

import json
import os
import re
import sys
import uuid
from datetime import date, datetime
from typing import Any

from mcp.server.fastmcp import FastMCP
from psycopg_pool import ConnectionPool

DB_URL = os.environ["OPERATIONS_DB_URL"]
CONVERSATIONS_TABLE = os.environ.get("CONVERSATIONS_TABLE", "conversations")
CONFIGS_TABLE = os.environ.get("CONFIGS_TABLE", "company_configs")
MAX_CONVERSATIONS = int(os.environ.get("MAX_CONVERSATIONS", "100"))
MAX_CONFIGS = int(os.environ.get("MAX_CONFIGS", "50"))

SENSITIVE_KEY_PATTERN = re.compile(
    os.environ.get("SENSITIVE_KEY_PATTERN", r"(secret|token|password|api_key|apikey)"),
    re.IGNORECASE,
)

mcp = FastMCP(
    "postgres-mcp",
    host=os.environ.get("MCP_HOST", "0.0.0.0"),
    port=int(os.environ.get("MCP_PORT", "3000")),
)
pool = ConnectionPool(DB_URL, min_size=1, max_size=4, open=False)


def _log(level: str, **fields: Any) -> None:
    sys.stdout.write(json.dumps({"service": "postgres-mcp", "level": level, **fields},
                                 ensure_ascii=False, default=str) + "\n")
    sys.stdout.flush()


def _uuid(value: str, field: str) -> str:
    try:
        return str(uuid.UUID(value))
    except (TypeError, ValueError, AttributeError) as exc:
        raise ValueError(f"{field} must be a valid UUID, got {value!r}") from exc


def _parse_date(value: str | date, field: str) -> date:
    if isinstance(value, date) and not isinstance(value, datetime):
        return value
    try:
        return date.fromisoformat(str(value))
    except ValueError as exc:
        raise ValueError(f"{field} must be ISO date YYYY-MM-DD") from exc


def _mask_if_sensitive(key: str, value: Any) -> Any:
    if SENSITIVE_KEY_PATTERN.search(key or ""):
        return "[MASKED]"
    return value


def _serialise(row: dict[str, Any]) -> dict[str, Any]:
    out = {}
    for k, v in row.items():
        if isinstance(v, (datetime, date)):
            out[k] = v.isoformat()
        else:
            out[k] = v
    return out


@mcp.tool()
def list_conversations(
    company_id: str,
    start_date: str,
    end_date: str,
    status: str | None = None,
    limit: int = 50,
) -> list[dict[str, Any]]:
    """List conversation METADATA for a company within [start_date, end_date].

    Returns at most `limit` rows (capped at MAX_CONVERSATIONS, default 100).
    Message content lives in MongoDB — use get_conversation_messages there.
    """
    cid = _uuid(company_id, "company_id")
    sd = _parse_date(start_date, "start_date")
    ed = _parse_date(end_date, "end_date")
    if ed < sd:
        raise ValueError("end_date must be >= start_date")
    lim = max(1, min(int(limit), MAX_CONVERSATIONS))

    sql = f"""
        SELECT id::text AS conversation_id,
               company_id::text AS company_id,
               started_at, ended_at, channel, status, customer_id_hashed
        FROM {CONVERSATIONS_TABLE}
        WHERE company_id = %(cid)s
          AND started_at >= %(sd)s
          AND started_at < (%(ed)s::date + INTERVAL '1 day')
          {"AND status = %(status)s" if status else ""}
        ORDER BY started_at DESC
        LIMIT %(lim)s
    """
    params = {"cid": cid, "sd": sd, "ed": ed, "lim": lim}
    if status:
        params["status"] = status

    with pool.connection() as conn, conn.cursor() as cur:
        cur.execute(sql, params)
        cols = [d.name for d in cur.description]
        rows = [_serialise(dict(zip(cols, r))) for r in cur.fetchall()]

    _log("info", event="list_conversations", company_id=cid, hits=len(rows))
    return rows


@mcp.tool()
def get_conversation(company_id: str, conversation_id: str) -> dict[str, Any]:
    """Return one conversation's metadata. Enforces company_id ownership."""
    cid = _uuid(company_id, "company_id")
    conv = _uuid(conversation_id, "conversation_id")

    sql = f"""
        SELECT id::text AS conversation_id,
               company_id::text AS company_id,
               started_at, ended_at, channel, status, customer_id_hashed
        FROM {CONVERSATIONS_TABLE}
        WHERE id = %(conv)s AND company_id = %(cid)s
        LIMIT 1
    """
    with pool.connection() as conn, conn.cursor() as cur:
        cur.execute(sql, {"cid": cid, "conv": conv})
        row = cur.fetchone()
        if row is None:
            _log("info", event="get_conversation", company_id=cid,
                 conversation_id=conv, found=False)
            raise ValueError("conversation not found or does not belong to company")
        cols = [d.name for d in cur.description]
        return _serialise(dict(zip(cols, row)))


@mcp.tool()
def list_config_keys(company_id: str) -> list[str]:
    """List the config_key names available for the company (no values)."""
    cid = _uuid(company_id, "company_id")
    sql = f"""
        SELECT DISTINCT config_key
        FROM {CONFIGS_TABLE}
        WHERE company_id = %(cid)s
        ORDER BY config_key
        LIMIT %(lim)s
    """
    with pool.connection() as conn, conn.cursor() as cur:
        cur.execute(sql, {"cid": cid, "lim": MAX_CONFIGS})
        rows = [r[0] for r in cur.fetchall()]
    _log("info", event="list_config_keys", company_id=cid, keys=len(rows))
    return rows


@mcp.tool()
def get_company_configs(
    company_id: str,
    config_keys: list[str] | None = None,
) -> list[dict[str, Any]]:
    """Fetch config_key / config_value pairs for the company.

    If `config_keys` is omitted, returns up to MAX_CONFIGS keys.
    Sensitive keys (matching SENSITIVE_KEY_PATTERN) come back as [MASKED].
    """
    cid = _uuid(company_id, "company_id")

    if config_keys:
        keys = [str(k) for k in config_keys][: MAX_CONFIGS]
        sql = f"""
            SELECT config_key, config_value
            FROM {CONFIGS_TABLE}
            WHERE company_id = %(cid)s AND config_key = ANY(%(keys)s)
            ORDER BY config_key
            LIMIT %(lim)s
        """
        params = {"cid": cid, "keys": keys, "lim": MAX_CONFIGS}
    else:
        sql = f"""
            SELECT config_key, config_value
            FROM {CONFIGS_TABLE}
            WHERE company_id = %(cid)s
            ORDER BY config_key
            LIMIT %(lim)s
        """
        params = {"cid": cid, "lim": MAX_CONFIGS}

    with pool.connection() as conn, conn.cursor() as cur:
        cur.execute(sql, params)
        rows = cur.fetchall()

    out = [
        {"config_key": key, "config_value": _mask_if_sensitive(key, value)}
        for key, value in rows
    ]
    _log("info", event="get_company_configs", company_id=cid, hits=len(out))
    return out


if __name__ == "__main__":
    pool.open()
    try:
        mcp.run(transport="sse")
    finally:
        pool.close()
