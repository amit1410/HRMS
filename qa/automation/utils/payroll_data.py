"""Shared QAAUTO data builders for the Module 10 Payroll Core regression suites.

Every builder creates only its own QAAUTO-prefixed data and returns the created row's `data` dict.
Masters (components/structures/assignments) have no delete endpoint — teardown deactivates them
best-effort. Payroll Periods and Runs have no delete/reset once they leave Draft/Prepared, so this
module dates every period in a deliberately out-of-band fiscal-year window (2071-2470) whose months
are randomised, so a period created here can never collide with a real payroll cycle nor with an
earlier QAAUTO period left behind by a prior run. This mirrors, and shares the safety reasoning of,
`tests/api/test_payroll_smoke.py`'s own fixtures and `qa/10-payroll/README.md`'s data-safety design.

These are plain functions, not fixtures: each regression file wraps the ones it needs in thin
function-scoped fixtures so cleanup stays local to the test that owns the data.
"""

from __future__ import annotations

import calendar
import random
import string

import pytest

from core.api_client import ApiClient
from core.payroll_api import (
    create_employee_salary_assignment,
    create_payroll_period,
    create_payroll_run,
    create_salary_component,
    create_salary_structure,
    deactivate_salary_component,
    deactivate_salary_structure,
    prepare_run,
    set_employee_salary_assignment_active,
)

# Out-of-band fiscal window — see the module docstring. Identical intent to the smoke suite's 2071.
QA_FISCAL_YEAR_BASE = 2071
QA_FISCAL_YEAR_SPAN = 399
QA_EFFECTIVE_FROM = "2020-01-01"
QA_MONTHLY_AMOUNT = 30000


def rand_suffix(n: int = 8) -> str:
    return "".join(random.choices(string.ascii_uppercase + string.digits, k=n))


def qa_code(prefix: str) -> str:
    return f"{prefix}-{rand_suffix()}"


def unique_period_dates() -> dict:
    """A random, practically-non-colliding month in the out-of-band window."""
    year = QA_FISCAL_YEAR_BASE + random.randint(0, QA_FISCAL_YEAR_SPAN)
    month = random.randint(1, 12)
    last_day = calendar.monthrange(year, month)[1]
    pay_year, pay_month = (year, month + 1) if month < 12 else (year + 1, 1)
    return {
        "fiscalYear": year,
        "periodNumber": month,
        "startDate": f"{year:04d}-{month:02d}-01",
        "endDate": f"{year:04d}-{month:02d}-{last_day:02d}",
        "payDate": f"{pay_year:04d}-{pay_month:02d}-01",
    }


def _created(response, what: str) -> dict:
    if response.status_code not in (200, 201):
        pytest.fail(f"Setup failed: could not create the QAAUTO {what} this test needs "
                    f"({response.status_code}): {response.text}")
    return response.json()["data"]


# --- Builders ---------------------------------------------------------------------------------


def make_component(client: ApiClient, *, component_type: str = "Earning",
                   calculation_type: str = "FixedAmount", **overrides) -> dict:
    body = {
        "code": qa_code("QAAUTO-SC"),
        "name": f"QAAUTO Component {rand_suffix(6)}",
        "description": "QA automation regression component — safe to deactivate/ignore.",
        "componentType": component_type,
        "calculationType": calculation_type,
        "statutoryType": "None",
        "isTaxable": True,
        "isStatutory": False,
        "isRecurring": True,
        "affectsGross": True,
        "affectsNetPay": True,
        "displayOrder": 1,
        "effectiveFrom": QA_EFFECTIVE_FROM,
        "isActive": True,
    }
    body.update(overrides)
    return _created(create_salary_component(client, **body), "salary component")


def make_structure(client: ApiClient, component_id: str, *, value: int = QA_MONTHLY_AMOUNT,
                   proratable: bool = False, editable: bool = False, extra_components=None) -> dict:
    components = [{
        "salaryComponentId": component_id,
        "sequence": 1,
        "calculationType": "FixedAmount",
        "value": value,
        "isProratable": proratable,
        "isEditableAtEmployeeLevel": editable,
        "isActive": True,
        "effectiveFrom": QA_EFFECTIVE_FROM,
    }]
    if extra_components:
        components.extend(extra_components)
    body = {
        "code": qa_code("QAAUTO-SS"),
        "name": f"QAAUTO Structure {rand_suffix(6)}",
        "description": "QA automation regression structure — safe to deactivate/ignore.",
        "effectiveFrom": QA_EFFECTIVE_FROM,
        "isActive": True,
        "components": components,
    }
    return _created(create_salary_structure(client, **body), "salary structure")


def make_employee(client: ApiClient) -> dict:
    from core.employee_api import create_personal_details
    from data.test_data import today_iso, unique_last_name

    response = create_personal_details(
        client, firstName="QaAutoPayroll", lastName=unique_last_name(), dateOfJoining=today_iso())
    return _created(response, "employee")


def make_assignment(client: ApiClient, employee_id: str, structure_id: str, **overrides) -> dict:
    from data.test_data import today_iso

    body = {
        "employeeId": employee_id,
        "salaryStructureId": structure_id,
        "effectiveFrom": today_iso(),
        "annualCtc": QA_MONTHLY_AMOUNT * 12,
        "monthlyCtc": QA_MONTHLY_AMOUNT,
        "currencyCode": "INR",
        "payFrequency": "Monthly",
        "status": "Active",
        "changeReason": "NewHire",
        "remarks": "QAAUTO automation regression assignment.",
    }
    body.update(overrides)
    return _created(create_employee_salary_assignment(client, **body), "employee salary assignment")


def make_period(client: ApiClient, **overrides) -> dict:
    # Periods can never be deleted, so QAAUTO periods accumulate and the active-period date-overlap
    # guard can reject a randomly-chosen month that a prior run already claimed. Retry with a fresh
    # random month (of ~4800 in the out-of-band window) until a free slot is found.
    last = None
    for _ in range(12):
        body = {
            "code": qa_code("QAAUTO-PER"),
            "name": f"QAAUTO Period {rand_suffix(6)}",
            "periodType": "Monthly",
            "isActive": True,
        }
        body.update(unique_period_dates())
        body.update(overrides)
        last = create_payroll_period(client, **body)
        if last.status_code in (200, 201):
            return last.json()["data"]
        if last.status_code != 409 or "overlaps" not in last.text:
            break
    if last is not None and last.status_code not in (200, 201):
        pytest.fail(f"Setup failed: could not create the QAAUTO payroll period this test needs "
                    f"({last.status_code}): {last.text}")
    return last.json()["data"]  # pragma: no cover


def make_prepared_run(client: ApiClient, period_id: str) -> dict:
    run = _created(create_payroll_run(client, payrollPeriodId=period_id, runType="Regular",
                                      notes="QAAUTO automation regression run."), "payroll run")
    prepared = prepare_run(client, run["id"])
    if prepared.status_code != 200:
        pytest.fail(f"Setup failed: could not prepare the QAAUTO payroll run "
                    f"({prepared.status_code}): {prepared.text}")
    return prepared.json()["data"]


# --- Best-effort teardown helpers ------------------------------------------------------------


def cleanup_component(client: ApiClient, component_id: str) -> None:
    try:
        deactivate_salary_component(client, component_id)
    except Exception:
        pass


def cleanup_structure(client: ApiClient, structure_id: str) -> None:
    try:
        deactivate_salary_structure(client, structure_id)
    except Exception:
        pass


def cleanup_assignment(client: ApiClient, assignment_id: str) -> None:
    try:
        set_employee_salary_assignment_active(client, assignment_id, False)
    except Exception:
        pass


def cleanup_employee(client: ApiClient, employee_id: str) -> None:
    from core.employee_api import delete_employee
    try:
        delete_employee(client, employee_id)
    except Exception:
        pass
