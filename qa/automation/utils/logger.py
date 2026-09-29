"""Shared logging setup for the automation framework.

Usage: `log = get_logger(__name__)` then `log.info(...)`. Level comes from QA_LOG_LEVEL
(default INFO); this is deliberately independent of pytest's own log capture so the same helper
works whether tests are run standalone or through pytest.
"""

from __future__ import annotations

import logging
import os
import sys

_CONFIGURED = False


def _configure_root() -> None:
    global _CONFIGURED
    if _CONFIGURED:
        return
    level_name = os.environ.get("QA_LOG_LEVEL", "INFO").upper()
    level = getattr(logging, level_name, logging.INFO)
    handler = logging.StreamHandler(stream=sys.stdout)
    handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)-8s %(name)s: %(message)s"))
    root = logging.getLogger("qa_automation")
    root.setLevel(level)
    root.addHandler(handler)
    root.propagate = False
    _CONFIGURED = True


def get_logger(name: str) -> logging.Logger:
    _configure_root()
    return logging.getLogger(f"qa_automation.{name}")
