"""Small helpers for reading required/optional configuration from the environment.

`require_env` skips the current test (rather than failing or fabricating a value) when a variable
is missing, so a test suite run without QA credentials configured reports honest skips instead of
false passes or false failures.
"""

from __future__ import annotations

import os

import pytest


def require_env(name: str) -> str:
    value = os.environ.get(name)
    if not value:
        pytest.skip(f"{name} is not set — see qa/automation/.env.example")
    return value


def require_env_any(*names: str) -> str:
    """Like `require_env`, but accepts the first of several accepted variable names.

    Used for QA_A_ADMIN_USERNAME with a fallback to the earlier QA_A_ADMIN_IDENTIFIER name, so a
    Phase 1 .env keeps working without silently guessing a value.
    """
    for name in names:
        value = os.environ.get(name)
        if value:
            return value
    joined = " or ".join(names)
    pytest.skip(f"None of {joined} is set — see qa/automation/.env.example")


def optional_env(name: str, default: str | None = None) -> str | None:
    return os.environ.get(name, default)


def is_env_configured(*names: str) -> bool:
    """Non-skipping presence check, for reporting/metadata (never returns the value itself)."""
    return all(os.environ.get(name) for name in names)
