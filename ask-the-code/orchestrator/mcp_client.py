"""Registry of MCP tool sets.

Two independent connections:

* `business`     → claude-code-mcp        (read_file, grep, glob)
* `diagnostics`  → companies-mcp, postgres-mcp, mongo-mcp, s3-mcp
"""
from __future__ import annotations

import os
from typing import Any

from langchain_mcp_adapters.client import MultiServerMCPClient


def _server(url_env: str, default_url: str) -> dict[str, str]:
    return {"url": os.environ.get(url_env, default_url), "transport": "sse"}


class MCPRegistry:
    def __init__(self) -> None:
        self._business_client: MultiServerMCPClient | None = None
        self._diagnostics_client: MultiServerMCPClient | None = None
        self.business_tools: list[Any] = []
        self.diagnostics_tools: list[Any] = []

    async def connect_business(self) -> None:
        self._business_client = MultiServerMCPClient({
            "claude-code": _server("CODE_MCP_URL", "http://claude-code-mcp:3000/sse"),
        })
        self.business_tools = await self._business_client.get_tools()

    async def connect_diagnostics(self) -> None:
        self._diagnostics_client = MultiServerMCPClient({
            "companies": _server("COMPANIES_MCP_URL", "http://companies-mcp:3000/sse"),
            "postgres":  _server("POSTGRES_MCP_URL",  "http://postgres-mcp:3000/sse"),
            "mongo":     _server("MONGO_MCP_URL",     "http://mongo-mcp:3000/sse"),
            "s3":        _server("S3_MCP_URL",        "http://s3-mcp:3000/sse"),
        })
        self.diagnostics_tools = await self._diagnostics_client.get_tools()

    @property
    def business_ready(self) -> bool:
        return bool(self.business_tools)

    @property
    def diagnostics_ready(self) -> bool:
        return bool(self.diagnostics_tools)
