"""s3-mcp — read-only chatbot flow access on S3.

Tools:
* list_chatbot_flows(company_id)
* download_chatbot_flow(company_id, flow_id)

All S3 keys are forced to start with `flows/<company_id>/`. Path traversal
attempts (`..`, leading `/`, NUL bytes) are rejected. Downloads above
MAX_FLOW_BYTES return an explicit error so the agent can ask for a
narrower scope instead of crashing the orchestrator.

IAM should grant ONLY:
  s3:ListBucket on arn:aws:s3:::<bucket>  (Prefix: flows/)
  s3:GetObject  on arn:aws:s3:::<bucket>/flows/*
"""
from __future__ import annotations

import json
import os
import sys
import uuid
from typing import Any

import boto3
from botocore.config import Config
from mcp.server.fastmcp import FastMCP

BUCKET = os.environ["S3_BUCKET"]
REGION = os.environ.get("AWS_REGION", "us-east-1")
MAX_FLOW_BYTES = int(os.environ.get("MAX_FLOW_BYTES", str(1 * 1024 * 1024)))
MAX_FLOWS = int(os.environ.get("MAX_FLOWS", "100"))

mcp = FastMCP(
    "s3-mcp",
    host=os.environ.get("MCP_HOST", "0.0.0.0"),
    port=int(os.environ.get("MCP_PORT", "3000")),
)

s3 = boto3.client(
    "s3",
    region_name=REGION,
    config=Config(retries={"max_attempts": 3, "mode": "standard"}),
)


def _log(level: str, **fields: Any) -> None:
    sys.stdout.write(json.dumps({"service": "s3-mcp", "level": level, **fields},
                                 ensure_ascii=False, default=str) + "\n")
    sys.stdout.flush()


def _uuid(value: str, field: str) -> str:
    try:
        return str(uuid.UUID(value))
    except (TypeError, ValueError, AttributeError) as exc:
        raise ValueError(f"{field} must be a valid UUID, got {value!r}") from exc


def _safe_flow_id(flow_id: str) -> str:
    fid = (flow_id or "").strip()
    if not fid:
        raise ValueError("flow_id is required")
    if any(ch in fid for ch in "\x00/\\") or ".." in fid:
        raise ValueError(f"invalid flow_id: {flow_id!r}")
    if len(fid) > 256:
        raise ValueError("flow_id too long")
    return fid


def _prefix(company_id: str) -> str:
    return f"flows/{company_id}/"


@mcp.tool()
def list_chatbot_flows(company_id: str) -> list[dict[str, Any]]:
    """List chatbot flows for the company under s3://<bucket>/flows/<company_id>/."""
    cid = _uuid(company_id, "company_id")
    prefix = _prefix(cid)

    flows: list[dict[str, Any]] = []
    token: str | None = None
    while True:
        kw: dict[str, Any] = {"Bucket": BUCKET, "Prefix": prefix, "MaxKeys": MAX_FLOWS}
        if token:
            kw["ContinuationToken"] = token
        resp = s3.list_objects_v2(**kw)
        for obj in resp.get("Contents", []):
            key: str = obj["Key"]
            if not key.startswith(prefix):
                continue
            tail = key[len(prefix):]
            if "/" in tail:
                continue  # only top-level flow objects
            flows.append({
                "flow_id": tail,
                "name": tail.rsplit(".", 1)[0],
                "updated_at": obj["LastModified"].isoformat(),
                "size_bytes": obj["Size"],
            })
            if len(flows) >= MAX_FLOWS:
                break
        if len(flows) >= MAX_FLOWS or not resp.get("IsTruncated"):
            break
        token = resp.get("NextContinuationToken")

    _log("info", event="list_chatbot_flows", company_id=cid, hits=len(flows))
    return flows


@mcp.tool()
def download_chatbot_flow(company_id: str, flow_id: str) -> dict[str, Any]:
    """Download the JSON of a chatbot flow.

    Returns {flow_id, content_type, size_bytes, body}. Body is the decoded
    text content. Files larger than MAX_FLOW_BYTES return an error asking
    for a narrower scope.
    """
    cid = _uuid(company_id, "company_id")
    fid = _safe_flow_id(flow_id)
    key = _prefix(cid) + fid

    head = s3.head_object(Bucket=BUCKET, Key=key)
    size = int(head["ContentLength"])
    if size > MAX_FLOW_BYTES:
        raise ValueError(
            f"flow too large ({size} bytes > {MAX_FLOW_BYTES}); "
            "ask for a smaller flow or a specific section",
        )

    obj = s3.get_object(Bucket=BUCKET, Key=key)
    body = obj["Body"].read().decode("utf-8", errors="replace")
    result = {
        "flow_id": fid,
        "content_type": obj.get("ContentType", "application/json"),
        "size_bytes": size,
        "body": body,
    }
    _log("info", event="download_chatbot_flow", company_id=cid,
         flow_id=fid, size_bytes=size)
    return result


if __name__ == "__main__":
    mcp.run(transport="sse")
