"""Thin HTTP client for API-layer tests.

The backend resolves tenants from the request Host header (TenantShardResolutionMiddleware — see
CLAUDE.md), not from any client-supplied id. So API tests always connect to localhost:<api port>
and set Host explicitly to the tenant host under test, exactly like a real request from
`{tenant}.<domain>` would arrive at the API behind a reverse proxy.

Every request/response pair is recorded (redacted) into an optional `history` list so a failing
test can attach the exchange to its Allure result for debugging — see conftest.py.
"""

from __future__ import annotations

import time

import allure
import requests

from config.settings import Settings
from utils import auth_rate_guard
from utils.logger import get_logger
from utils.redact import redact_headers, redact_json, redact_text

log = get_logger(__name__)


class ApiClient:
    def __init__(self, settings: Settings, host: str, history: list | None = None):
        self._settings = settings
        self._host = host
        self._session = requests.Session()
        self._default_headers: dict = {}
        self.history = history if history is not None else []

    def with_bearer(self, access_token: str) -> "ApiClient":
        """Attaches an Authorization header to every subsequent request from this client.
        Mutates and returns self, so callers can chain: `client = api_client_factory(host).with_bearer(token)`.
        """
        self._default_headers["Authorization"] = f"Bearer {access_token}"
        return self

    def _headers(self, extra: dict | None = None) -> dict:
        headers = {"Host": self._host, "Content-Type": "application/json", **self._default_headers}
        if extra:
            headers.update(extra)
        return headers

    def _record(self, method: str, url: str, headers: dict, request_json, response, duration_ms: float) -> None:
        self.history.append(
            {
                "method": method,
                "url": url,
                "request_headers": redact_headers(headers),
                "request_body": redact_json(request_json),
                "status": response.status_code,
                "response_body": redact_text(response.text),
                "duration_ms": round(duration_ms, 1),
            }
        )

    def _send(self, method: str, path: str, json=None, params=None, headers=None, timeout: float = 15):
        url = self._settings.api_url(path)
        full_headers = self._headers(headers)
        log.debug("%s %s (Host=%s)", method, url, self._host)
        started = time.monotonic()
        response = self._session.request(method, url, json=json, params=params, headers=full_headers, timeout=timeout)
        duration_ms = (time.monotonic() - started) * 1000
        log.debug("-> %s in %.0fms", response.status_code, duration_ms)
        auth_rate_guard.record(path, method, response.status_code)
        self._record(method, response.url, full_headers, json, response, duration_ms)
        return response

    @allure.step("POST {path}")
    def post(
        self,
        path: str,
        json: dict | None = None,
        params: dict | None = None,
        headers: dict | None = None,
        timeout: float = 15,
    ):
        return self._send("POST", path, json=json, params=params, headers=headers, timeout=timeout)

    @allure.step("GET {path}")
    def get(self, path: str, params: dict | None = None, headers: dict | None = None, timeout: float = 15):
        return self._send("GET", path, params=params, headers=headers, timeout=timeout)

    @allure.step("PUT {path}")
    def put(
        self,
        path: str,
        json: dict | None = None,
        params: dict | None = None,
        headers: dict | None = None,
        timeout: float = 15,
    ):
        return self._send("PUT", path, json=json, params=params, headers=headers, timeout=timeout)

    @allure.step("DELETE {path}")
    def delete(self, path: str, params: dict | None = None, headers: dict | None = None, timeout: float = 15):
        return self._send("DELETE", path, params=params, headers=headers, timeout=timeout)
