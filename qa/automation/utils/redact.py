"""Redaction helpers so failure evidence (logs, Allure attachments) never carries credentials or
session tokens, even when it's built from real request/response bodies for debugging.
"""

from __future__ import annotations

import copy
import re
from typing import Any

_SENSITIVE_KEYS = {
    "password",
    "currentpassword",
    "newpassword",
    "confirmpassword",
    "accesstoken",
    "refreshtoken",
    "token",
    "secret",
    "otp",
}

_AUTH_HEADER_RE = re.compile(r"^(Bearer|Basic)\s+.+$", re.IGNORECASE)


def _redact_value(value: str) -> str:
    if len(value) <= 8:
        return "***redacted***"
    return f"{value[:4]}…***redacted***"


def redact_json(body: Any) -> Any:
    """Deep-copies a JSON-able structure, masking any key that looks like a credential/token."""
    if body is None:
        return None
    clone = copy.deepcopy(body)
    _redact_in_place(clone)
    return clone


def _redact_in_place(node: Any) -> None:
    if isinstance(node, dict):
        for key, value in node.items():
            if key.lower() in _SENSITIVE_KEYS and isinstance(value, str):
                node[key] = _redact_value(value)
            else:
                _redact_in_place(value)
    elif isinstance(node, list):
        for item in node:
            _redact_in_place(item)


def redact_headers(headers: dict | None) -> dict:
    if not headers:
        return {}
    redacted = {}
    for key, value in headers.items():
        if key.lower() == "authorization" and isinstance(value, str) and _AUTH_HEADER_RE.match(value):
            scheme = value.split(" ", 1)[0]
            redacted[key] = f"{scheme} ***redacted***"
        else:
            redacted[key] = value
    return redacted


def redact_text(text: str, max_len: int = 2000) -> str:
    """Best-effort redaction for a raw response body that may not be valid JSON."""
    truncated = text[:max_len]
    for key in ("accessToken", "refreshToken", "password", "token"):
        truncated = re.sub(
            rf'("{key}"\s*:\s*")[^"]*(")',
            rf"\1***redacted***\2",
            truncated,
            flags=re.IGNORECASE,
        )
    if len(text) > max_len:
        truncated += f"...<{len(text) - max_len} more bytes truncated>"
    return truncated
