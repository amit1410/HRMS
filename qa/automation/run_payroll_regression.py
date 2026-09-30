"""Convenience runner for the Payroll Core regression suites.

Sets the CI-equivalent pacing env (session reuse + auth-rate budget) so a local full run does not
trip the backend's auth rate limit, then invokes pytest with any extra args passed through.

    python run_payroll_regression.py                      # gate: excludes known_defect
    python run_payroll_regression.py --all                # include known_defect too
    python run_payroll_regression.py -k components         # pass-through pytest args
"""
from __future__ import annotations

import os
import sys

os.environ.setdefault("QA_REUSE_API_SESSIONS", "1")
os.environ.setdefault("QA_AUTH_RATE_BUDGET", "18")

import pytest  # noqa: E402

PAYROLL_FILES = [
    "tests/api/test_payroll_components_regression.py",
    "tests/api/test_payroll_structures_regression.py",
    "tests/api/test_payroll_assignments_regression.py",
    "tests/api/test_payroll_periods_runs_regression.py",
    "tests/api/test_payroll_calculation_regression.py",
    "tests/api/test_payroll_inputs_regression.py",
    "tests/api/test_payroll_authz_tenant_regression.py",
]


def main() -> int:
    args = [a for a in sys.argv[1:] if a != "--all"]
    include_known = "--all" in sys.argv[1:]
    marker = [] if include_known else ["-m", "not known_defect"]
    # Any arg naming a .py file is a target; everything else (flags and their values) passes through.
    has_target = any(a.endswith(".py") or "test_" in a for a in args)
    files = [] if has_target else [f for f in PAYROLL_FILES if os.path.exists(f)]
    return pytest.main([*files, *args, "-p", "no:allure", "--no-header", *marker])


if __name__ == "__main__":
    raise SystemExit(main())
