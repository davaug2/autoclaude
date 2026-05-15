"""Thin wrapper around langchain-mcp-adapters' MultiServerMCPClient.

Holds the client + cached tool list so the agent does not re-handshake
with the MCP server on every request.
"""
from __future__ import annotations

import os
from typing import Any

from langchain_mcp_adapters.client import MultiServerMCPClient


class MCPState:
    def __init__(self) -> None:
        self.client: MultiServerMCPClient | None = None
        self.tools: list[Any] = []

    async def connect(self) -> None:
        url = os.environ.get("MCP_URL", "http://claude-code-mcp:3000/sse")
        self.client = MultiServerMCPClient(
            {
                "claude-code": {
                    "url": url,
                    "transport": "sse",
                },
            },
        )
        self.tools = await self.client.get_tools()

    @property
    def ready(self) -> bool:
        return bool(self.tools)
