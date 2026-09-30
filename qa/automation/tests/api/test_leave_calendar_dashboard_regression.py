"""API regression suite for Module 07 — Team Leave Calendar and HR Leave Dashboard.

Automated from qa/07-leave/cases/12-calendar-dashboard.yaml (LEAVE-CAL-*, LEAVE-DASH-*).

Calendar (from/to query params, Leave.RequestViewOwn) is a self-service surface: it always includes
the caller's own events, validates its range (end<start and the 366-day cap both 400), and rejects an
anonymous caller. The HR Dashboard (Leave.DashboardViewAll) is admin-only: per CR-79 the Employee and
Manager roles cannot open it at all. Both are non-destructive reads. Dashboard rows can reference real
departments/employees, so this suite asserts only the summary's shape, never attaching raw facets.
"""

from __future__ import annotations

from datetime import date, timedelta

import allure
import pytest

from core.leave_api import get_hr_dashboard, get_leave_calendar
from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Leave Management"), pytest.mark.api, pytest.mark.regression]

_TODAY = date.today()
_FROM = _TODAY.isoformat()
_TO = (_TODAY + timedelta(days=30)).isoformat()


# --- Calendar ----------------------------------------------------------------------------------


@allure.story("Leave calendar")
@allure.title("LEAVE-CAL-001 — the calendar returns the caller's own events for a valid range")
@qa_cases("LEAVE-CAL-001")
@pytest.mark.critical
def test_calendar_returns_own_events(employee_api_client):
    response = get_leave_calendar(employee_api_client, **{"from": _FROM, "to": _TO})
    assert response.status_code == 200, response.text
    assert isinstance(response.json()["data"], list)


@allure.story("Leave calendar")
@allure.title("LEAVE-CAL-006 — calendar events carry no reason/private field")
@qa_cases("LEAVE-CAL-006")
def test_calendar_events_have_no_private_field(employee_api_client):
    events = get_leave_calendar(employee_api_client, **{"from": _FROM, "to": _TO}).json()["data"]
    for event in events:
        assert "reason" not in event and "comment" not in event, event


@allure.story("Leave calendar")
@allure.title("LEAVE-CAL-002 — from/to are validated: end before start is 400")
@qa_cases("LEAVE-CAL-002")
@pytest.mark.critical
def test_calendar_end_before_start_rejected(employee_api_client):
    response = get_leave_calendar(employee_api_client, **{"from": _TO, "to": _FROM})
    assert response.status_code == 400, response.text


@allure.story("Leave calendar")
@allure.title("LEAVE-CAL-002 — a range exceeding 366 days is rejected 400")
@qa_cases("LEAVE-CAL-002")
def test_calendar_over_366_days_rejected(employee_api_client):
    response = get_leave_calendar(
        employee_api_client, **{"from": _FROM, "to": (_TODAY + timedelta(days=400)).isoformat()}
    )
    assert response.status_code == 400, response.text


@allure.story("Leave calendar")
@allure.title("LEAVE-AUTHZ-002 — an anonymous calendar call is rejected 401")
@qa_cases("LEAVE-AUTHZ-002")
def test_calendar_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert get_leave_calendar(anon, **{"from": _FROM, "to": _TO}).status_code == 401


# --- HR Dashboard ------------------------------------------------------------------------------


@allure.story("Leave dashboard")
@allure.title("LEAVE-DASH-001 / LEAVE-DASH-006 — the dashboard defaults its window to the current period and returns a well-formed summary")
@qa_cases("LEAVE-DASH-001", "LEAVE-DASH-006")
@pytest.mark.critical
def test_dashboard_default_window_summary(admin_api_client):
    response = get_hr_dashboard(admin_api_client)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    for field in ("from", "to", "kpis", "statusBreakdown"):
        assert field in data, f"dashboard summary missing {field!r}"
    for kpi in ("pendingApprovalRequests", "approvedRequests", "activeEmployeeCount"):
        assert kpi in data["kpis"], f"kpis missing {kpi!r}"


@allure.story("Leave dashboard")
@allure.title("LEAVE-DASH-001 — the dashboard validates its range: end before start and >366 days are 400")
@qa_cases("LEAVE-DASH-001")
def test_dashboard_range_validation(admin_api_client):
    assert get_hr_dashboard(admin_api_client, **{"from": _TO, "to": _FROM}).status_code == 400
    assert get_hr_dashboard(
        admin_api_client, **{"from": _FROM, "to": (_TODAY + timedelta(days=400)).isoformat()}
    ).status_code == 400


@allure.story("Leave dashboard")
@allure.title("LEAVE-DASH-005 / CR-79 — the dashboard needs DashboardViewAll: Employee and Manager get 403")
@qa_cases("LEAVE-DASH-005")
@cr_refs("CR-79")
@pytest.mark.critical
def test_dashboard_requires_permission(employee_api_client, manager_api_client):
    assert get_hr_dashboard(employee_api_client).status_code == 403
    assert get_hr_dashboard(manager_api_client).status_code == 403


@allure.story("Leave dashboard")
@allure.title("LEAVE-AUTHZ-002 — an anonymous dashboard call is rejected 401")
@qa_cases("LEAVE-AUTHZ-002")
def test_dashboard_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert get_hr_dashboard(anon).status_code == 401
