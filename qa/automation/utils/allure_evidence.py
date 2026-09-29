"""Allure helpers shared by the Payroll happy-path E2E and known-defect suites.

- `qa_cases(...)` tags a test with the manual QA case ids it automates (qa/*/cases/*.yaml), as both
  an Allure tag (visible on the test) and a `qa_case` label (filterable).
- `known_defect(...)` does the same for a reported product defect id (docs/qa/payroll-sandbox-setup.md §8).
- `cr_refs(...)` marks a passing test whose assertions pin behavior a QA risk-register entry (CR-xxx)
  calls a defect, so the CR stays visible in the report.
- `attach_json(...)` attaches curated evidence. Callers pass only sandbox-owned data or aggregate
  counts, never a raw list response that could carry real employees' names or codes.
"""

from __future__ import annotations

import json
from decimal import Decimal

import allure


def qa_cases(*case_ids: str):
    def decorate(fn):
        return allure.label("qa_case", *case_ids)(allure.tag(*case_ids)(fn))

    return decorate


def known_defect(*defect_ids: str):
    def decorate(fn):
        return allure.label("defect", *defect_ids)(allure.tag(*(f"KNOWN-DEFECT-{d}" for d in defect_ids))(fn))

    return decorate


def cr_refs(*cr_ids: str):
    """Tags a passing test that asserts current product behavior which a QA risk-register entry
    (qa/*/clarifications.yaml, CR-xxx) calls a defect or gap. The test documents the behavior as it
    is today; the correct-contract check, if any, lives in a `known_defect` test."""

    def decorate(fn):
        return allure.label("cr", *cr_ids)(allure.tag(*(f"CR-KNOWN-{c}" for c in cr_ids))(fn))

    return decorate


def attach_json(name: str, payload) -> None:
    allure.attach(
        json.dumps(payload, indent=2, sort_keys=True, default=_default),
        name=name,
        attachment_type=allure.attachment_type.JSON,
    )


def _default(value):
    if isinstance(value, Decimal):
        return str(value)
    return str(value)
