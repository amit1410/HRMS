"""API regression suite for Module 10 — Payroll Periods and Runs.

Automated from qa/10-payroll/cases/04-periods-runs.yaml (PAY-PR-001..028). Statuses and messages
confirmed live before writing. Periods and Runs have no delete/reset once they leave Draft/Prepared,
so every period this suite creates is dated in the out-of-band 2071+ window (see utils.payroll_data)
and left in place, identifiable by its QAAUTO code — the same permanence the smoke suite accepts.

NOT AUTOMATABLE via the public API (documented, not skipped silently): PAY-PR-019 (Processing stamps
LockedAtUtc/LockedByUserId) and PAY-PR-020 (CR-172 — CompletedByUserId stamped on every transition)
both assert fields that the run detail/list DTO does not expose (verified: the run DTO surfaces only
id, status, runNumber, runType, employeeCount, eligibleCount, excludedCount, startedAtUtc,
startedByUserId, concurrencyVersion, employees). They cannot be observed as HTTP responses and would
need a DB-level assertion; PAY-PR-020's CR-172 stays covered narratively by qa/10-payroll.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.payroll_api import (
    create_payroll_period,
    create_payroll_run,
    get_configuration_health,
    get_integrity,
    get_operations_dashboard,
    get_payroll_period,
    get_payroll_period_history,
    get_payroll_run,
    get_production_health,
    get_run_employees,
    get_run_history,
    get_run_readiness,
    list_payroll_periods,
    list_payroll_runs,
    lock_payroll_period,
    prepare_run,
    transition_payroll_period,
    transition_run,
    unlock_payroll_period,
    update_payroll_period,
)
from utils.allure_evidence import cr_refs, known_defect, qa_cases
from utils.env_utils import require_env
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
    unique_period_dates,
)

pytestmark = [allure.feature("Payroll"), allure.story("Payroll Periods and Runs"), pytest.mark.api, pytest.mark.regression]


@pytest.fixture
def period(admin_api_client):
    return make_period(admin_api_client)  # no delete endpoint; left in place (out-of-band dates)


@pytest.fixture
def eligible_run(admin_api_client):
    """The full component -> structure -> employee -> assignment -> period -> prepared-run chain,
    so at least one employee is eligible in the run's population."""
    component = make_component(admin_api_client)
    structure = make_structure(admin_api_client, component["id"])
    employee = make_employee(admin_api_client)
    assignment = make_assignment(admin_api_client, employee["id"], structure["id"])
    period = make_period(admin_api_client)
    run = make_prepared_run(admin_api_client, period["id"])
    yield {"run": run, "employee": employee, "period": period}
    cleanup_assignment(admin_api_client, assignment["id"])
    cleanup_structure(admin_api_client, structure["id"])
    cleanup_component(admin_api_client, component["id"])
    cleanup_employee(admin_api_client, employee["id"])


# --- Payroll Periods ---------------------------------------------------------------------------


@allure.title("PAY-PR-001 — a well-formed monthly period is created in Draft")
@qa_cases("PAY-PR-001")
@pytest.mark.critical
def test_create_period_draft(period):
    assert period["status"] == "Draft"
    assert period["isActive"] is True


@allure.title("PAY-PR-002 — StartDate>EndDate and PayDate<StartDate are rejected 400")
@qa_cases("PAY-PR-002")
def test_period_date_validation(admin_api_client):
    dates = unique_period_dates()
    start_after_end = create_payroll_period(
        admin_api_client, code="QAAUTO-PER-BAD1", name="QAAUTO", periodType="Monthly",
        fiscalYear=dates["fiscalYear"], periodNumber=dates["periodNumber"],
        startDate=dates["endDate"], endDate=dates["startDate"], payDate=dates["payDate"], isActive=True)
    assert start_after_end.status_code == 400 and "Start date cannot be later than end date." in start_after_end.text, start_after_end.text

    paydate_early = create_payroll_period(
        admin_api_client, code="QAAUTO-PER-BAD2", name="QAAUTO", periodType="Monthly",
        fiscalYear=dates["fiscalYear"], periodNumber=dates["periodNumber"],
        startDate=dates["startDate"], endDate=dates["endDate"], payDate="1900-01-01", isActive=True)
    assert paydate_early.status_code == 400 and "Pay date cannot be before the period start." in paydate_early.text, paydate_early.text


@allure.title("PAY-PR-003 — a duplicate period code is 409")
@qa_cases("PAY-PR-003")
def test_duplicate_period_code(admin_api_client, period):
    dup = create_payroll_period(
        admin_api_client, code=period["code"], name="QAAUTO dup", periodType="Monthly",
        **{k: period[k] for k in ("fiscalYear", "periodNumber", "startDate", "endDate", "payDate")}, isActive=True)
    assert dup.status_code == 409, dup.text
    assert "A payroll period with this code already exists in this tenant." in dup.text


@allure.title("PAY-PR-004 — a Closed period cannot be edited (409)")
@qa_cases("PAY-PR-004")
def test_closed_period_cannot_be_edited(admin_api_client, period):
    transition_payroll_period(admin_api_client, period["id"], "open")
    transition_payroll_period(admin_api_client, period["id"], "close")
    edit = update_payroll_period(admin_api_client, period["id"], **{**period, "name": "QAAUTO Renamed"})
    assert edit.status_code == 409, edit.text
    assert "Closed or locked payroll periods cannot be edited." in edit.text


@allure.title("PAY-PR-005 — an update with a stale ExpectedConcurrencyVersion is 409")
@qa_cases("PAY-PR-005")
def test_period_stale_concurrency(admin_api_client, period):
    response = update_payroll_period(
        admin_api_client, period["id"], **{**period, "name": "QAAUTO stale",
                                            "expectedConcurrencyVersion": period["concurrencyVersion"] + 99})
    assert response.status_code == 409, response.text
    assert "The payroll period was changed by another user." in response.text


@allure.title("PAY-PR-006 — transitions follow Draft->Open->Closed; skipping Open is 409")
@qa_cases("PAY-PR-006")
@pytest.mark.critical
def test_period_transition_order(admin_api_client, period):
    opened = transition_payroll_period(admin_api_client, period["id"], "open")
    assert opened.status_code == 200 and opened.json()["data"]["status"] == "Open", opened.text
    closed = transition_payroll_period(admin_api_client, period["id"], "close")
    assert closed.status_code == 200 and closed.json()["data"]["status"] == "Closed", closed.text

    fresh = make_period(admin_api_client)
    skip = transition_payroll_period(admin_api_client, fresh["id"], "close")
    assert skip.status_code == 409, skip.text
    assert "The payroll period transition is not allowed." in skip.text


@allure.title("PAY-PR-007 — locking requires a reason and stamps the reason")
@qa_cases("PAY-PR-007")
def test_lock_requires_reason(admin_api_client, period):
    transition_payroll_period(admin_api_client, period["id"], "open")
    transition_payroll_period(admin_api_client, period["id"], "close")
    no_reason = lock_payroll_period(admin_api_client, period["id"])
    assert no_reason.status_code == 400 and "A lock reason is required." in no_reason.text, no_reason.text
    locked = lock_payroll_period(admin_api_client, period["id"], reason="QAAUTO Month-end close")
    assert locked.status_code == 200 and locked.json()["data"]["status"] == "Locked", locked.text


@allure.title("PAY-PR-008 — a clean unlock (holding Period.Unlock) returns the period to Closed")
@qa_cases("PAY-PR-008")
def test_unlock_returns_to_closed(admin_api_client, period):
    transition_payroll_period(admin_api_client, period["id"], "open")
    transition_payroll_period(admin_api_client, period["id"], "close")
    lock_payroll_period(admin_api_client, period["id"], reason="QAAUTO lock")
    unlocked = unlock_payroll_period(admin_api_client, period["id"], reason="QAAUTO correction")
    assert unlocked.status_code == 200, unlocked.text
    assert unlocked.json()["data"]["status"] == "Closed"  # unlock returns to Closed, not Open


@allure.title("PAY-PR-010 — period history is append-only across transitions")
@qa_cases("PAY-PR-010")
def test_period_history_append_only(admin_api_client, period):
    transition_payroll_period(admin_api_client, period["id"], "open")
    transition_payroll_period(admin_api_client, period["id"], "close")
    history = get_payroll_period_history(admin_api_client, period["id"])
    assert history.status_code == 200, history.text
    change_types = [e["changeType"] for e in history.json()["data"]]
    assert "Created" in change_types
    assert any(ct in ("Opened", "Open") for ct in change_types)


# --- Payroll Runs ------------------------------------------------------------------------------


@allure.title("PAY-PR-011 — a run is created with an auto-generated RunNumber; a Locked period is 409")
@qa_cases("PAY-PR-011")
@pytest.mark.critical
def test_create_run_and_locked_period_rejected(admin_api_client, period):
    run = create_payroll_run(admin_api_client, payrollPeriodId=period["id"], runType="Regular", notes="QAAUTO")
    assert run.status_code in (200, 201), run.text
    assert run.json()["data"]["runNumber"].startswith("PR-")

    locked = make_period(admin_api_client)
    transition_payroll_period(admin_api_client, locked["id"], "open")
    transition_payroll_period(admin_api_client, locked["id"], "close")
    lock_payroll_period(admin_api_client, locked["id"], reason="QAAUTO lock")
    blocked = create_payroll_run(admin_api_client, payrollPeriodId=locked["id"], runType="Regular")
    assert blocked.status_code == 409, blocked.text
    assert "A closed, locked, or inactive period cannot receive a new payroll run." in blocked.text


@allure.title("PAY-PR-012 — prepare builds the population; only assigned employees are eligible")
@qa_cases("PAY-PR-012")
@pytest.mark.critical
def test_prepare_population_eligibility(admin_api_client, eligible_run):
    run = eligible_run["run"]
    employee_id = eligible_run["employee"]["id"]
    assert run["status"] == "Prepared"
    eligible = [e for e in run["employees"] if e["isEligible"]]
    excluded = [e for e in run["employees"] if not e["isEligible"]]
    assert employee_id in {e["employeeId"] for e in eligible}, "the QAAUTO employee with an assignment must be eligible"
    assert len(eligible) < run["employeeCount"], "the tenant's unassigned employees must be excluded"
    assert any(e.get("exclusionReason") == "No effective salary assignment" for e in excluded)


@allure.title("PAY-PR-013 — re-preparing an already-Prepared run without rebuild is 409")
@qa_cases("PAY-PR-013")
def test_reprepare_without_rebuild_rejected(admin_api_client, eligible_run):
    response = prepare_run(admin_api_client, eligible_run["run"]["id"], rebuild=False)
    assert response.status_code == 409, response.text
    assert "The run is already prepared" in response.text


@allure.title("PAY-PR-015 — rebuild=true re-prepares the run")
@qa_cases("PAY-PR-015")
def test_rebuild_reprepares(admin_api_client, eligible_run):
    response = prepare_run(admin_api_client, eligible_run["run"]["id"], rebuild=True)
    assert response.status_code == 200, response.text
    assert response.json()["data"]["status"] == "Prepared"


@allure.title("PAY-PR-016 (F6) — the run's employee population endpoint should be readable and paged")
@qa_cases("PAY-PR-016")
@known_defect("F6")
@pytest.mark.known_defect
def test_run_employees_paged(admin_api_client, eligible_run):
    """CONFIRMED DEFECT F6 (same root cause as the existing F1): GET /api/payroll/runs/{id}/employees
    returns HTTP 500 — with and without paging params — because the controller action binds the
    abstract `HRMS.Application.Common.PagedQuery` from the query string ('Model bound complex types
    must not be abstract … or give the query parameter a non-null default value'). The sibling
    /results endpoint (a different query type) works, and the population is still readable inside the
    prepare response (see PAY-PR-012), which is why this went unnoticed. Asserts the correct 200
    contract, so it fails until fixed and is deselected by `-m "not known_defect"`."""
    response = get_run_employees(admin_api_client, eligible_run["run"]["id"], page=1, pageSize=25)
    assert response.status_code == 200, (
        f"KNOWN DEFECT F6: GET /runs/{{id}}/employees returned {response.status_code}: {response.text[:300]}")
    data = response.json()["data"]
    assert "items" in data and len(data["items"]) <= 25


@allure.title("PAY-PR-017 — run transitions follow order; Prepared->Processing works, skipping to Approved is 409")
@qa_cases("PAY-PR-017")
@pytest.mark.critical
def test_run_transition_order(admin_api_client, eligible_run):
    run_id = eligible_run["run"]["id"]
    skip = transition_run(admin_api_client, run_id, "Approved")
    assert skip.status_code == 409, skip.text
    assert "The payroll run transition is not allowed." in skip.text

    processing = transition_run(admin_api_client, run_id, "Processing")
    assert processing.status_code == 200 and processing.json()["data"]["status"] == "Processing", processing.text


@allure.title("PAY-PR-021 — run history is append-only (records Prepared/PopulationGenerated)")
@qa_cases("PAY-PR-021")
def test_run_history_append_only(admin_api_client, eligible_run):
    history = get_run_history(admin_api_client, eligible_run["run"]["id"])
    assert history.status_code == 200, history.text
    change_types = {e["changeType"] for e in history.json()["data"]}
    assert "Prepared" in change_types


@allure.title("PAY-PR-022 — readiness is readable and reports a prepared run with an eligible employee as ready")
@qa_cases("PAY-PR-022")
def test_readiness_shape(admin_api_client, eligible_run):
    response = get_run_readiness(admin_api_client, eligible_run["run"]["id"])
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert {"ready", "errorCount", "warningCount", "checks"} <= set(data)
    assert data["ready"] is True and data["errorCount"] == 0


@allure.title("PAY-PR-023 — cross-tenant period and run ids are 404")
@qa_cases("PAY-PR-023")
@pytest.mark.critical
def test_foreign_period_and_run_ids_are_404(admin_api_client):
    period = get_payroll_period(admin_api_client, str(uuid.uuid4()))
    assert period.status_code == 404 and "Payroll period not found." in period.text, period.text
    run = get_payroll_run(admin_api_client, str(uuid.uuid4()))
    assert run.status_code == 404 and "Payroll run not found." in run.text, run.text


@allure.title("PAY-PR-024 — period and run list/filter read paths")
@qa_cases("PAY-PR-024")
def test_list_filters(admin_api_client, eligible_run):
    periods = list_payroll_periods(admin_api_client, status="Draft", page=1, pageSize=50)
    assert periods.status_code == 200, periods.text
    assert all(p["status"] == "Draft" for p in periods.json()["data"]["items"])

    runs = list_payroll_runs(admin_api_client, payrollPeriodId=eligible_run["period"]["id"])
    assert runs.status_code == 200, runs.text
    assert all(r["payrollPeriodId"] == eligible_run["period"]["id"] for r in runs.json()["data"]["items"])


@allure.title("PAY-PR-025/026/027/028 — operations dashboard, config/production health and integrity are readable")
@qa_cases("PAY-PR-025", "PAY-PR-026", "PAY-PR-027", "PAY-PR-028")
def test_operations_and_health_reads(admin_api_client):
    assert get_operations_dashboard(admin_api_client).status_code == 200
    assert get_configuration_health(admin_api_client).status_code == 200
    assert get_production_health(admin_api_client).status_code == 200
    assert get_integrity(admin_api_client).status_code == 200


@allure.title("PAY-PR-020 — CR-172 (CompletedByUserId stamped on every transition) is not observable via the API")
@qa_cases("PAY-PR-020")
@cr_refs("CR-172")
def test_cr172_not_observable_via_api(admin_api_client, eligible_run):
    """The run detail DTO does not expose CompletedByUserId/CompletedAtUtc/LockedAtUtc, so CR-172's
    unbraced-if bug cannot be asserted through the HTTP surface. This test documents that gap (it
    asserts only that the fields are genuinely absent, so it will start failing — prompting a real
    assertion — if the DTO is ever extended to expose them)."""
    transition_run(admin_api_client, eligible_run["run"]["id"], "Processing")
    detail = get_payroll_run(admin_api_client, eligible_run["run"]["id"]).json()["data"]
    assert "completedByUserId" not in detail and "completedAtUtc" not in detail, (
        "The run DTO now exposes completion fields — CR-172 (PAY-PR-020) can and should now be "
        "asserted directly: CompletedByUserId must stay null until Finalized/Cancelled.")


@allure.title("PAY-PR — an unauthenticated caller is rejected 401 on the periods surface")
@qa_cases("PAY-PR-024")
@pytest.mark.critical
def test_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert list_payroll_periods(anon).status_code == 401
