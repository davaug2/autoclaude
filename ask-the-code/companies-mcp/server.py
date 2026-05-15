"""companies-mcp — resolves company hints to canonical company_id GUIDs.

Single tool: search_companies(query, limit=5).
The orchestrator calls this directly before letting the agent loose.

Assumed schema (rename via env if your DB differs):
    table:        companies     (override with COMPANIES_TABLE)
    columns:      id (uuid), name (text), status (text), created_at (timestamptz)
"""
from __future__ import annotations

import json
import os
import sys
from typing import Any

from mcp.server.fastmcp import FastMCP
from psycopg_pool import ConnectionPool

DB_URL = os.environ["COMPANIES_DB_URL"]
TABLE = os.environ.get("COMPANIES_TABLE", "companies")
NAME_COL = os.environ.get("COMPANIES_NAME_COLUMN", "name")
ID_COL = os.environ.get("COMPANIES_ID_COLUMN", "id")
STATUS_COL = os.environ.get("COMPANIES_STATUS_COLUMN", "status")
CREATED_AT_COL = os.environ.get("COMPANIES_CREATED_AT_COLUMN", "created_at")
MAX_LIMIT = int(os.environ.get("COMPANIES_MAX_LIMIT", "5"))

mcp = FastMCP(
    "companies-mcp",
    host=os.environ.get("MCP_HOST", "0.0.0.0"),
    port=int(os.environ.get("MCP_PORT", "3000")),
)

pool = ConnectionPool(DB_URL, min_size=1, max_size=4, open=False)


def _log(level: str, **fields: Any) -> None:
    payload = {"service": "companies-mcp", "level": level, **fields}
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, default=str) + "\n")
    sys.stdout.flush()


def _validate_query(q: str) -> str:
    q = (q or "").strip()
    if not q:
        raise ValueError("query is required")
    if q in {"*", "%", "_"} or len(q) < 2:
        raise ValueError("query too broad; provide at least 2 meaningful chars")
    if len(q) > 256:
        raise ValueError("query too long (max 256 chars)")
    return q


@mcp.tool()
def search_companies(query: str, limit: int = 5) -> list[dict[str, Any]]:
    """Search companies by name (case-insensitive prefix + contains).

    Args:
        query: free-text fragment (>=2 chars, no wildcards).
        limit: max results (clamped to COMPANIES_MAX_LIMIT, default 5).

    Returns: list of {company_id, name, status, created_at} sorted by best match.
    """
    q = _validate_query(query)
    lim = max(1, min(int(limit), MAX_LIMIT))

    # Two-stage scoring: exact-prefix wins over contains, both LIKE-safe.
    sql = f"""
        SELECT {ID_COL}::text AS company_id,
               {NAME_COL}     AS name,
               {STATUS_COL}   AS status,
               {CREATED_AT_COL} AS created_at,
               CASE WHEN {NAME_COL} ILIKE %(prefix)s THEN 0 ELSE 1 END AS rank
        FROM {TABLE}
        WHERE {NAME_COL} ILIKE %(prefix)s
           OR {NAME_COL} ILIKE %(contains)s
        ORDER BY rank, length({NAME_COL}), {NAME_COL}
        LIMIT %(lim)s
    """
    params = {"prefix": q + "%", "contains": "%" + q + "%", "lim": lim}

    with pool.connection() as conn, conn.cursor() as cur:
        cur.execute(sql, params)
        rows = cur.fetchall()
        cols = [d.name for d in cur.description]

    out: list[dict[str, Any]] = []
    for row in rows:
        rec = dict(zip(cols, row))
        rec.pop("rank", None)
        if rec.get("created_at") is not None:
            rec["created_at"] = rec["created_at"].isoformat()
        out.append(rec)
    _log("info", event="search_companies", query=q, hits=len(out))
    return out


if __name__ == "__main__":
    pool.open()
    try:
        mcp.run(transport="sse")
    finally:
        pool.close()
