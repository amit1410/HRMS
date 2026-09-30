"""API regression suite for Module 08 — Attendance Monthly Processing / Periods.

Automated from qa/08-attendance/cases/05-monthly-processing.yaml (ATT-MONTHLY-*).

Period rows have no delete endpoint, so the create/duplicate lifecycle uses one fixed, far-future
month (2076-01) that is inert (zero employees ever fall in it) and idempotent on re-run (a second
create is the documented 409). No Open/ReadyToClose real period is closed or reopened — those need
processed attendance data this environment does not seed — so close/reopen are exercised only for
their guard behaviour (wrong-state rejection, reason-required), never to mutate a real period.

Self monthly-summary is reached with the linked QA Employee identity (the only one that resolves an
Employee for `my/monthly-summary` — the QA Admin/TenantAdmin is not a linked Employee, CR-111).
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.attendance_api import (
    close_period,
    create_period,
    get_my_monthly_summary,
    get_period,
    get_periods,
    process_period,
    reopen_period,
)
from utils.allure_evidence import qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Attendance Management"), pytest.mark.api, pytest.mark.regression]

_FAR_YEAR, _FAR_MONTH = 2076, 1


# --- Period reads / paging ---------------------------------------------------------------------


@allure.story("Monthly / Periods")
@allure.title("ATT-MONTHLY-003 — GET periods returns a paged result")
@qa_cases("ATT-MONTHLY-003")
@pytest.mark.critical
def test_list_periods_paged(admin_api_client):
    response = get_periods(admin_api_client, page=1, pageSize=10)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert data["page"] == 1 and {"items", "totalCount", "totalPages"} <= set(data)


@allure.story("Monthly / Periods")
@allure.title("ATT-MONTHLY-001 / ATT-MONTHLY-002 — create a period, then a duplicate (Year, Month) is rejected 409")
@qa_cases("ATT-MONTHLY-001", "ATT-MONTHLY-002")
@pytest.mark.critical
def test_create_period_and_duplicate_rejected(admin_api_client):
    first = create_period(admin_api_client, _FAR_YEAR, _FAR_MONTH)
    # First run creates it (200/201); every later run hits the duplicate guard (409) — both are correct.
    assert first.status_code in (200, 201, 409), first.text
    duplicate = create_period(admin_api_client, _FAR_YEAR, _FAR_MONTH)
    assert duplicate.status_code == 409, duplicate.text


@allure.story("Monthly / Periods")
@allure.title("ATT-MONTHLY-001 — an out-of-range month is rejected 400")
@qa_cases("ATT-MONTHLY-001")
def test_create_period_bad_month_rejected(admin_api_client):
    assert create_period(admin_api_client, 2076, 13).status_code == 400
    assert create_period(admin_api_client, 2076, 0).status_code == 400


@allure.story("Monthly / Periods")
@allure.title("ATT-MONTHLY-003 — GET an unknown period id is a clean 404")
@qa_cases("ATT-MONTHLY-003")
def test_get_unknown_period_is_404(admin_api_client):
    assert get_period(admin_api_client, str(uuid.uuid4())).status_code == 404


# --- Close / reopen guards ---------------------------------------------------------------------


@allure.story("Monthly / Close-Reopen")
@allure.title("ATT-MONTHLY-021 — closing a non-ReadyToClose (Open) period is rejected")
@qa_cases("ATT-MONTHLY-021", "ATT-MONTHLY-019")
def test_close_open_period_rejected(admin_api_client):
    # Ensure the fixed far-future period exists and is Open, then attempt to close it directly.
    create_period(admin_api_client, _FAR_YEAR, _FAR_MONTH)
    period = next(
        (p for p in get_periods(admin_api_client, page=1, pageSize=100).json()["data"]["items"]
         if p.get("year") == _FAR_YEAR and p.get("month") == _FAR_MONTH),
        None,
    )
    if period is None:
        pytest.skip("The far-future guard period is not listed in this environment; cannot exercise the close guard.")
    response = close_period(admin_api_client, period["id"])
    assert response.status_code in (400, 409), response.text


@allure.story("Monthly / Close-Reopen")
@allure.title("ATT-MONTHLY-022 — reopen requires a non-empty reason (empty reason is rejected)")
@qa_cases("ATT-MONTHLY-022")
def test_reopen_requires_reason(admin_api_client):
    create_period(admin_api_client, _FAR_YEAR, _FAR_MONTH)
    period = next(
        (p for p in get_periods(admin_api_client, page=1, pageSize=100).json()["data"]["items"]
         if p.get("year") == _FAR_YEAR and p.get("month") == _FAR_MONTH),
        None,
    )
    if period is None:
        pytest.skip("The far-future guard period is not listed; cannot exercise the reopen-reason guard.")
    # An empty reason is rejected on validation grounds (400) before the state check; a wrong-state
    # reopen of an Open period is 400/409. Either way it must never be a 200 that mutates the period.
    empty_reason = reopen_period(admin_api_client, period["id"], reason="")
    assert empty_reason.status_code in (400, 409), empty_reason.text


# --- Authorization -----------------------------------------------------------------------------


@allure.story("Monthly / Authorization")
@allure.title("ATT-MONTHLY-031 — a caller with only Monthly.ViewTeam (Manager) is denied the Monthly.ViewAll routes")
@qa_cases("ATT-MONTHLY-031")
@pytest.mark.critical
def test_manager_denied_period_reads(manager_api_client):
    assert get_periods(manager_api_client, page=1, pageSize=5).status_code == 403


@allure.story("Monthly / Authorization")
@allure.title("ATT-MONTHLY-037 — only Monthly.Process can trigger processing: Employee/Manager get 403")
@qa_cases("ATT-MONTHLY-037")
def test_process_requires_permission(employee_api_client, manager_api_client):
    assert process_period(employee_api_client, str(uuid.uuid4())).status_code == 403
    assert process_period(manager_api_client, str(uuid.uuid4())).status_code == 403


@allure.story("Monthly / Authorization")
@allure.title("ATT-MONTHLY-004 — an anonymous periods call is rejected 401")
@qa_cases("ATT-MONTHLY-004")
def test_periods_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert anon.get("/api/attendance/periods").status_code == 401


# --- Self monthly summary ----------------------------------------------------------------------


@allure.story("Monthly / Self")
@allure.title("ATT-MONTHLY-033 — an employee views their own monthly summary (paged)")
@qa_cases("ATT-MONTHLY-033")
@pytest.mark.critical
def test_employee_self_monthly_summary(employee_api_client):
    from datetime import date

    today = date.today()
    response = get_my_monthly_summary(employee_api_client, today.year, today.month)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert {"items", "totalCount", "totalPages"} <= set(data), response.text


@allure.story("Monthly / Self")
@allure.title("ATT-MONTHLY-033 — the Admin (not a linked Employee) cannot resolve a self monthly summary")
@qa_cases("ATT-MONTHLY-033")
def test_admin_self_monthly_summary_not_linked(admin_api_client):
    from datetime import date

    today = date.today()
    response = get_my_monthly_summary(admin_api_client, today.year, today.month)
    # The self route resolves the caller's own linked Employee; the TenantAdmin seed user is not one.
    assert response.status_code == 404, response.text
