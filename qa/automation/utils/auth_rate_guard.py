"""Client-side pacing for the backend's `RateLimiting:Authentication` policy.

The API throttles sign-in, refresh, set-password and the anonymous branding read at 20 requests per
60 seconds per (organization, client IP) — see Backend/HRMS.API/Security/RateLimitingPolicies.cs.
Every login a test makes counts, from every identity, so a dense sequential run trips it and the
identity fixtures turn the resulting 429s into skips. That is the limiter working as designed; the
suite must slow down, never the limit go up.

This module records every request to a limited endpoint (ApiClient calls and browser requests alike)
and, when `QA_AUTH_RATE_BUDGET` is set, makes each test wait before setup until the trailing window
holds few enough requests that the test's own worst case (its "reserve") still fits under the budget.
Unset, it only records, so a local run behaves exactly as before.

Settings (environment):
    QA_AUTH_RATE_BUDGET           max limited requests in any trailing window; unset = no pacing.
                                  18 leaves a margin of 2 under the server's 20.
    QA_AUTH_RATE_WINDOW_SECONDS   trailing window, default 62 (the server's 60 plus clock margin).
    QA_AUTH_RATE_RESERVE_UI       worst-case limited requests one browser test makes, default 8.
    QA_AUTH_RATE_RESERVE_API      same for an API-only test, default 6.
    QA_AUTH_RATE_LEDGER           optional path; per-test counts, waits and 429s are written there as JSON.
"""

from __future__ import annotations

import json
import os
import time
from collections import deque
from pathlib import Path
from urllib.parse import urlsplit

from utils.logger import get_logger

log = get_logger(__name__)

LIMITED_PATHS = frozenset(
    {
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/auth/set-password",
        "/api/tenants/current/branding",
    }
)

_events: deque[float] = deque()
_current: dict | None = None
_tests: list[dict] = []
_throttled: list[dict] = []
_total_wait = 0.0


def _int_env(name: str, default: int | None) -> int | None:
    value = os.environ.get(name, "").strip()
    return int(value) if value else default


def budget() -> int | None:
    return _int_env("QA_AUTH_RATE_BUDGET", None)


def window_seconds() -> int:
    return _int_env("QA_AUTH_RATE_WINDOW_SECONDS", 62)


def reserve(is_ui: bool) -> int:
    return _int_env("QA_AUTH_RATE_RESERVE_UI", 8) if is_ui else _int_env("QA_AUTH_RATE_RESERVE_API", 6)


def is_limited(url_or_path: str, method: str = "GET") -> bool:
    if method.upper() == "OPTIONS":
        return False
    path = urlsplit(url_or_path).path if "://" in url_or_path else url_or_path.split("?", 1)[0]
    return path.rstrip("/").lower() in LIMITED_PATHS


def record(url_or_path: str, method: str = "GET", status: int | None = None, *, now: float | None = None) -> None:
    """Notes one request; call it for every request, it ignores the ones the limiter doesn't cover."""
    if not is_limited(url_or_path, method):
        return
    _events.append(time.monotonic() if now is None else now)
    if _current is not None:
        _current["limitedRequests"] += 1
    if status == 429:
        record_throttled(url_or_path, method)


def record_throttled(url_or_path: str, method: str = "GET") -> None:
    if not is_limited(url_or_path, method):
        return
    path = urlsplit(url_or_path).path if "://" in url_or_path else url_or_path
    _throttled.append({"test": _current["nodeid"] if _current else None, "method": method.upper(), "path": path})
    log.warning("429 from %s %s — the auth rate limit was hit", method.upper(), path)


def seconds_to_wait(threshold: int, now: float, window: float) -> float:
    """How long until at most `threshold` recorded requests remain inside the trailing window."""
    while _events and _events[0] <= now - window:
        _events.popleft()
    excess = len(_events) - threshold
    if excess <= 0:
        return 0.0
    return _events[excess - 1] + window - now + 0.25


def begin_test(nodeid: str, *, is_ui: bool) -> None:
    """Called before a test's fixtures run. Sleeps when pacing is enabled and the window is too full."""
    global _current, _total_wait
    limit = budget()
    waited = 0.0
    if limit is not None:
        threshold = max(limit - reserve(is_ui), 0)
        pause = seconds_to_wait(threshold, time.monotonic(), window_seconds())
        if pause > 0:
            log.info("auth rate pacing: waiting %.1fs before %s", pause, nodeid)
            time.sleep(pause)
            waited = pause
            _total_wait += pause
    _current = {"nodeid": nodeid, "ui": is_ui, "limitedRequests": 0, "waitedSeconds": round(waited, 1)}


def end_test() -> None:
    global _current
    if _current is None:
        return
    if budget() is not None and _current["limitedRequests"] > reserve(_current["ui"]):
        log.warning(
            "auth rate pacing: %s made %d limited requests, above its reserve of %d; raise QA_AUTH_RATE_RESERVE_%s",
            _current["nodeid"], _current["limitedRequests"], reserve(_current["ui"]), "UI" if _current["ui"] else "API",
        )
    _tests.append(_current)
    _current = None


def write_ledger() -> None:
    target = os.environ.get("QA_AUTH_RATE_LEDGER")
    if not target:
        return
    path = Path(target)
    path.parent.mkdir(parents=True, exist_ok=True)
    ledger = {
        "budget": budget(),
        "windowSeconds": window_seconds(),
        "reserveUi": reserve(True),
        "reserveApi": reserve(False),
        "totalWaitSeconds": round(_total_wait, 1),
        "limitedRequests": sum(t["limitedRequests"] for t in _tests),
        "throttled": _throttled,
        "tests": _tests,
    }
    path.write_text(json.dumps(ledger, indent=2), encoding="utf-8")
