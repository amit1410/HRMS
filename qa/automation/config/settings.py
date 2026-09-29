"""Environment configuration for the automation framework.

Everything here is resolved from environment variables (optionally loaded from a local `.env` by
conftest.py) so the same test code runs against any disposable QA environment without edits.
Nothing here is a secret — credentials live in data/test_data.py, sourced the same way.
"""

from __future__ import annotations

import os
from dataclasses import dataclass


def _env(name: str, default: str | None = None) -> str | None:
    return os.environ.get(name, default)


def _bool_env(name: str, default: bool = False) -> bool:
    value = os.environ.get(name)
    if value is None:
        return default
    return value.strip().lower() in ("1", "true", "yes", "on")


@dataclass(frozen=True)
class Settings:
    scheme: str
    ui_port: str
    api_port: str
    unknown_host: str
    headless_override: bool | None
    log_level: str

    def ui_url(self, host: str, path: str = "/") -> str:
        """Build a full UI URL for a given tenant host, e.g. ui_url('demo01.localhost', '/login')."""
        if not path.startswith("/"):
            path = f"/{path}"
        return f"{self.scheme}://{host}:{self.ui_port}{path}"

    def api_url(self, path: str) -> str:
        """Build a full API URL. The backend routes by Host header, not by the connection target,
        so API calls always connect to localhost and set Host explicitly (see core/api_client.py)."""
        if not path.startswith("/"):
            path = f"/{path}"
        return f"{self.scheme}://localhost:{self.api_port}{path}"


def load_settings() -> Settings:
    return Settings(
        scheme=_env("QA_SCHEME", "http"),
        ui_port=_env("QA_UI_PORT", "5173"),
        api_port=_env("QA_API_PORT", "5080"),
        unknown_host=_env("QA_UNKNOWN_HOST", "qa-unregistered-workspace.localhost"),
        headless_override=(_bool_env("QA_HEADLESS") if "QA_HEADLESS" in os.environ else None),
        log_level=_env("QA_LOG_LEVEL", "INFO"),
    )
