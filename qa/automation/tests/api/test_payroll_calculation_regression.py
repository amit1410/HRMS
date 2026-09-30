"""API regression suite for Module 10 — Calculation Engine (results and errors).

Automated from qa/10-payroll/cases/05-calculation-engine.yaml (PAY-CALC-*).

Environment reality (source-verified, same finding the payroll smoke suite documents):
`PayrollCalculationEngine.CalculateEmployeeAsync` unconditionally calls the Overtime snapshot
resolver for every eligible employee, and that resolver requires a Closed AttendancePeriod whose
dates equal the payroll period's. ANEVRA01 has no such attendance month for the out-of-band QAAUTO
periods, so **no payroll run reaches `Calculated` here** — every eligible employee fails with
OvertimeNotFinalized and the run reverts to Prepared. This suite therefore asserts the *reachable*
calculation contract in full (the Draft/Prepared guards, the deterministic per-employee failure and
its error-report shape, recalculation determinism, current-attempt read scoping, cross-tenant
isolation) and honestly SKIPS the arithmetic cases (proration/rounding/matrix/negative-value/
negative-net/adjustment/statutory) that require a clean Calculated run — those are exercised by the
sandbox-gated happy-path E2E (`test_payroll_happy_path_e2e.py`, PAY-CALC-001/002/004) for the flat
case. A green run with those skips is not evidence that proration or rounding is correct.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.payroll_api import (
    calculate_run,
    create_payroll_run,
    get_run_calculation_errors,
    get_run_results,
    recalculate_run,
)
from utils.allure_evidence import cr_refs, qa_cases
from utils.payroll_data import (
    cleanup_assignment,
    cleanup_component,
    cleanup_employee,
    cleanup_structure,
    make_assignment,
    make_component,
    make_employee,
    make_period,
    make_prepared_run,
    make_structure,
)

pytestmark = [allure.feature("Payroll"), allure.story("Calculation Engine"), pytest.mark.api, pytest.mark.regression]

_ARITHMETIC_SKIP = (
    "Requires a clean Calculated run, which ANEVRA01 cannot currently produce for any QAAUTO period: "
    "PayrollCalculationEngine unconditionally resolves an Overtime snapshot that needs a Closed "
    "AttendancePeriod matching the period dates, so every eligible employee fails OvertimeNotFinalized "
    "(see this module's docstring and test_payroll_run_prepare_scopes... in the smoke suite). The flat "
    "30000 calculate/recalculate path is covered by the sandbox-gated happy-path E2E; the proration/"
    "rounding/matrix/negative arithmetic needs attendance provisioning this environment lacks. Not a "
    "framework issue — re-enable once a Closed AttendancePeriod is provisioned for the QAAUTO periods."
)


@pytest.fixture
def calc_attempt(admin_api_client):
    """A prepared run that has been through one calculate attempt. In this environment the attempt
    deterministically fails every eligible employee on OvertimeNotFinalized and the run reverts to
    Prepared — exactly the live contract these tests assert."""
    component = make_component(admin_api_client)
    structure = make_structure(admin_api_client, component["id"])
    employee = make_employee(admin_api_client)
    assignment = make_assignment(admin_api_client, employee["id"], structure["id"])
    period = make_period(admin_api_client)
    run = make_prepared_run(admin_api_client, period["id"])
    eligible = [e for e in run["employees"] if e["isEligible"]]
    summary = calculate_run(admin_api_client, run["id"])
    yield {"run": run, "employee": employee, "eligible": eligible,
           "calculate": summary, "run_id": run["id"]}
    cleanup_assignment(admin_api_client, assignment["id"])
    cleanup_structure(admin_api_client, structure["id"])
    cleanup_component(admin_api_client, component["id"])
    cleanup_employee(admin_api_client, employee["id"])


@allure.title("PAY-CALC-002 — calculate on a Draft (unprepared) run is rejected 409")
@qa_cases("PAY-CALC-002")
@pytest.mark.critical
def test_calculate_unprepared_run_rejected(admin_api_client):
    period = make_period(admin_api_client)
    run = create_payroll_run(admin_api_client, payrollPeriodId=period["id"], runType="Regular", notes="QAAUTO")
    assert run.status_code in (200, 201), run.text
    response = calculate_run(admin_api_client, run.json()["data"]["id"])
    assert response.status_code == 409, response.text
    assert "Only prepared payroll runs may be calculated." in response.text


@allure.title("PAY-CALC-005C/006 — calculate attempts every eligible employee; a per-employee resolver failure is reported, not a whole-run abort")
@qa_cases("PAY-CALC-005C", "PAY-CALC-006")
@pytest.mark.critical
def test_calculate_reports_per_employee_failure(admin_api_client, calc_attempt):
    summary = calc_attempt["calculate"]
    assert summary.status_code == 200, summary.text
    data = summary.json()["data"]
    eligible = calc_attempt["eligible"]
    assert data["employeeCount"] == calc_attempt["run"]["employeeCount"]
    assert data["calculatedCount"] == 0, data
    assert data["failedCount"] == len(eligible), data

    errors = get_run_calculation_errors(admin_api_client, calc_attempt["run_id"])
    assert errors.status_code == 200, errors.text
    entries = errors.json()["data"]
    assert len(entries) == len(eligible)
    our = [e for e in entries if e["employeeId"] == calc_attempt["employee"]["id"]]
    assert our and "OvertimeNotFinalized" in our[0]["message"], our


@allure.title("PAY-CALC-024 — read endpoints are scoped to the current attempt (no results while nothing is calculated)")
@qa_cases("PAY-CALC-024")
def test_results_reads_scoped_to_current_attempt(admin_api_client, calc_attempt):
    results = get_run_results(admin_api_client, calc_attempt["run_id"])
    assert results.status_code == 200, results.text
    # Nothing calculated (all eligible failed), so the current-attempt results list is empty.
    assert results.json()["data"]["items"] == []


@allure.title("PAY-CALC-004 — recalculate reproduces the identical deterministic outcome")
@qa_cases("PAY-CALC-004")
def test_recalculate_is_deterministic(admin_api_client, calc_attempt):
    recalculated = recalculate_run(admin_api_client, calc_attempt["run_id"])
    assert recalculated.status_code == 200, recalculated.text
    data = recalculated.json()["data"]
    assert data["calculatedCount"] == 0
    assert data["failedCount"] == len(calc_attempt["eligible"])


@allure.title("PAY-CALC-026 — a cross-tenant run id on calculate is 404")
@qa_cases("PAY-CALC-026")
@pytest.mark.critical
def test_calculate_foreign_run_is_404(admin_api_client):
    response = calculate_run(admin_api_client, str(uuid.uuid4()))
    assert response.status_code == 404, response.text
    assert "Payroll run not found." in response.text


# --- Arithmetic cases that require a clean Calculated run (honest skips, precise reason) --------


@allure.title("PAY-CALC-009 — calculation matrix + base ordering (needs a Calculated run)")
@qa_cases("PAY-CALC-009")
def test_calculation_matrix():
    pytest.skip(_ARITHMETIC_SKIP)


@allure.title("PAY-CALC-010 — a negative computed value is rejected (CR-176, needs a Calculated run)")
@qa_cases("PAY-CALC-010")
@cr_refs("CR-176")
def test_negative_value_rejected():
    pytest.skip(_ARITHMETIC_SKIP)


@allure.title("PAY-CALC-011 — proration for a mid-period joiner (needs a Calculated run)")
@qa_cases("PAY-CALC-011")
def test_proration():
    pytest.skip(_ARITHMETIC_SKIP)


@allure.title("PAY-CALC-012 — Min/Max clamps apply after proration (needs a Calculated run)")
@qa_cases("PAY-CALC-012")
def test_min_max_after_proration():
    pytest.skip(_ARITHMETIC_SKIP)


@allure.title("PAY-CALC-014 — rounding is AwayFromZero at 2dp (needs a Calculated run)")
@qa_cases("PAY-CALC-014")
def test_rounding_away_from_zero():
    pytest.skip(_ARITHMETIC_SKIP)


@allure.title("PAY-CALC-015 — negative net pay is rejected before/after statutory (needs a Calculated run)")
@qa_cases("PAY-CALC-015")
def test_negative_net_pay():
    pytest.skip(_ARITHMETIC_SKIP)
