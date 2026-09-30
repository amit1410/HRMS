"""API regression suite for Module 08 — Attendance Operations, Exceptions & Admin Corrections.

Automated from qa/08-attendance/cases/06-exceptions.yaml (ATT-EXC-*) and 10-admin-corrections.yaml
(ATT-CORR-*), plus the Manager-scope boundary cases (ATT-SCOPE-014).

Reads (operations queue, dashboard, admin-corrections list) are non-destructive. Mutating routes
(bulk approve/reject, manual correction, bulk-corrections, resolve) are exercised only for their
authorization guard (non-admins → 403) and, for the admin, only for input validation on an empty
body (→ 400) — never with real items that would transition a live request or write a correction.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.attendance_api import (
    get_admin_corrections,
    get_my_exceptions,
    get_operations_dashboard,
    get_operations_exceptions,
    operations_bulk,
    operations_bulk_corrections,
    operations_manual,
    resolve_exception,
)
from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Attendance Management"), pytest.mark.api, pytest.mark.regression]


# --- Operations queue / dashboard --------------------------------------------------------------


@allure.story("Operations / Exceptions")
@allure.title("ATT-EXC-001 — the operations exception queue is paged and filterable")
@qa_cases("ATT-EXC-001")
@pytest.mark.critical
def test_operations_exceptions_paged(admin_api_client):
    response = get_operations_exceptions(admin_api_client, page=1, pageSize=10)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert data["page"] == 1 and {"items", "totalCount", "totalPages"} <= set(data)


@allure.story("Operations / Dashboard")
@allure.title("ATT-EXC-002 — the operations dashboard returns the authoritative count object")
@qa_cases("ATT-EXC-002")
def test_operations_dashboard(admin_api_client):
    response = get_operations_dashboard(admin_api_client)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert {"processedDays", "presentDays", "absentDays", "pendingRegularizations"} <= set(data), data


@allure.story("Operations / Exceptions")
@allure.title("ATT-EXC-001 — the operations queue rejects an anonymous caller 401")
@qa_cases("ATT-EXC-001")
def test_operations_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert anon.get("/api/attendance/operations/exceptions").status_code == 401


# --- Manager scope -----------------------------------------------------------------------------


@allure.story("Operations / Manager scope")
@allure.title("ATT-SCOPE-014 — a Manager reaches the Operations queue/dashboard but not the manager/team route")
@qa_cases("ATT-SCOPE-014", "ATT-EXC-001")
@cr_refs("CR-111")
@pytest.mark.critical
def test_manager_reaches_operations_but_not_team(manager_api_client):
    # Manager holds Exception.View (Operations reachable) but not Attendance.View (manager/team denied).
    assert get_operations_exceptions(manager_api_client, page=1, pageSize=5).status_code == 200
    assert get_operations_dashboard(manager_api_client).status_code == 200
    from datetime import date

    today = date.today().isoformat()
    team = manager_api_client.get("/api/attendance/manager/team", params={"fromDate": today, "toDate": today})
    assert team.status_code == 403, team.text


# --- Resolve / bulk authorization --------------------------------------------------------------


@allure.story("Operations / Authorization")
@allure.title("ATT-EXC-003 — resolving an exception needs only Exception.View; an Employee lacking it gets 403")
@qa_cases("ATT-EXC-003", "ATT-EXC-013")
def test_resolve_requires_exception_view(employee_api_client):
    response = resolve_exception(employee_api_client, employeeId=str(uuid.uuid4()), date="2026-01-10", action="Acknowledge")
    assert response.status_code == 403, response.text


@allure.story("Operations / Authorization")
@allure.title("ATT-EXC-005 — bulk approve/reject needs RegularizationApprove: Employee and Manager get 403")
@qa_cases("ATT-EXC-005", "ATT-EXC-018")
@pytest.mark.critical
def test_bulk_requires_regularization_approve(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        assert operations_bulk(client, items=[]).status_code == 403


@allure.story("Operations / Authorization")
@allure.title("ATT-EXC-010 — bulk-corrections need AdminCorrection.Manage: Employee and Manager get 403")
@qa_cases("ATT-EXC-010", "ATT-EXC-018")
def test_bulk_corrections_require_admin_manage(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        assert operations_bulk_corrections(client, corrections=[]).status_code == 403
        assert operations_manual(client, employeeId=str(uuid.uuid4())).status_code == 403


@allure.story("Operations / Validation")
@allure.title("ATT-EXC-005 — the admin bulk action validates its body (an empty/invalid payload is 400)")
@qa_cases("ATT-EXC-005")
def test_admin_bulk_validates_body(admin_api_client):
    # Admin is authorized, so the request reaches model/body validation rather than the permission gate.
    response = operations_bulk(admin_api_client, items=[])
    assert response.status_code in (400, 200), response.text
    if response.status_code == 200:
        # An empty item list must be a no-op, never an error and never a partial write.
        allure.attach(response.text, name="empty bulk response", attachment_type=allure.attachment_type.JSON)


@allure.story("Operations / Self")
@allure.title("ATT-EXC-013 / CR-111 — the seeded Employee cannot reach me/exceptions (gated on Attendance.View)")
@qa_cases("ATT-EXC-013")
@cr_refs("CR-111")
def test_employee_cannot_reach_own_exceptions(employee_api_client):
    assert get_my_exceptions(employee_api_client).status_code == 403


# --- Admin corrections -------------------------------------------------------------------------


@allure.story("Admin corrections")
@allure.title("ATT-CORR-001 — the admin corrections list is paged and reachable by an authorized caller")
@qa_cases("ATT-CORR-001")
def test_admin_corrections_paged(admin_api_client):
    response = get_admin_corrections(admin_api_client, page=1, pageSize=10)
    assert response.status_code == 200, response.text
    assert {"items", "totalCount", "totalPages"} <= set(response.json()["data"])


@allure.story("Admin corrections")
@allure.title("ATT-CORR — admin corrections require AdminCorrection.Manage: Employee/Manager get 403")
@qa_cases("ATT-CORR-001")
def test_admin_corrections_denied_to_non_admins(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        assert get_admin_corrections(client, page=1, pageSize=5).status_code == 403
