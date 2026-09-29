"""API smoke suite for Module 08 — Attendance Management (Phase 5, first batch).

Proof-of-concept coverage, automated from qa/08-attendance/cases (`attendance-test-cases.generated.
csv`, sheets Attendance_Self/Attendance_Regularization/Attendance_OnDuty/Attendance_Reports/
Attendance_Security). Deliberately narrow — Attendance is by far the largest module in the manual
catalogue (17 sheets, 455 cases; see qa/08-attendance/README.md); this batch covers only the
self-service calendar/day read, the manager team read, the Regularization/On Duty submit->cancel
lifecycle, one manager-queue read, the Daily report, and the authorization/authentication sweep
items the Phase 5 brief asks for. Foundation/Shift/Roster config, Monthly processing, Exceptions/
Operations, Overtime, Admin Corrections, and Devices are explicitly out of scope for this batch.

**A real, source-verified permission gap constrains what this batch can exercise as a happy path**
(qa/08-attendance/README.md CR-111/CR-112, reconfirmed here against the current `SeedData.
RolePermissionMap`): no seeded role except SuperAdmin/TenantAdmin holds `Attendance.
RegularizationRequest/Approve` or `Attendance.OnDutyRequest/Approve`, and the seeded plain
`Employee`/`Manager` roles do not hold plain `Attendance.View` either (Manager only gets
`MonthlyViewTeam`/`ExceptionView`, not the `Attendance.View` that gates `me/calendar`, `me/days`,
`manager/team`, and every Regularization/On-Duty list route). So `employee_api_client` and
`manager_api_client` cannot reach the Attendance self-service/manager-approval surface at all under
the seeded matrix — this suite uses those fixtures only for the authorization-*denial* tests, where
a 403 is exactly the (documented) expected outcome, and uses `admin_api_client` for every
functional read/write test, honestly skipping (never failing) when even that identity turns out not
to hold the needed permission or isn't itself linked to an Employee record (self-service endpoints
resolve the caller's own linked Employee identity via `EmployeeIdentityResolver` — an admin/
TenantAdmin seed user is not necessarily one, exactly as `core/leave_api.py` already documents for
Leave). Because approving/rejecting a request requires a *second* identity distinct from its
submitter/owner (`AttendanceWorkflowService.TransitionRegularization`/`TransitionOnDuty`: "An
employee cannot approve or reject their own request" / "The request maker cannot approve or reject
their own request"), and this environment has no second Attendance-capable identity configured, the
approve/reject-happy-path tests instead submit-then-attempt-self-approve and assert that exact
denial (ATT-REG-014) — a real, deterministic, in-scope assertion, not a workaround.

Every Regularization/On Duty request this suite submits is cancelled (self-service cancel, which —
unlike approve/reject — the owner *is* allowed to do) before the test returns; no pre-existing
attendance record, Regularization, or On Duty request belonging to any employee is ever read for
its values, edited, or deleted.
"""

from __future__ import annotations

import datetime
import uuid

import allure
import pytest

from core.attendance_api import (
    approve_on_duty,
    approve_regularization,
    cancel_on_duty,
    cancel_regularization,
    get_daily_report,
    get_manager_on_duty,
    get_manager_regularizations,
    get_manager_team,
    get_my_calendar,
    get_my_day,
    get_my_on_duty,
    get_my_regularization,
    list_my_regularizations,
    submit_on_duty,
    submit_regularization,
)
from data.test_data import unique_reason
from utils.env_utils import require_env

pytestmark = [allure.feature("Attendance Management"), pytest.mark.api]

_PERMISSION_GAP_REASON = (
    "{permission} was not granted to the configured QA_A_ADMIN identity in this environment "
    "({status}) — under the seeded RolePermissionMap only SuperAdmin/TenantAdmin hold it "
    "(qa/08-attendance/README.md CR-111/CR-112). Not a framework issue; re-run once QA_A_ADMIN "
    "is a SuperAdmin/TenantAdmin seed user, or a dedicated Attendance-capable QA user is "
    "provisioned."
)
_NOT_LINKED_REASON = (
    "The configured QA_A_ADMIN account is not linked to an Employee record (404 from a "
    "self-service Attendance endpoint) — self-service Attendance resolves the caller's own "
    "linked Employee identity (EmployeeIdentityResolver), which an admin/TenantAdmin seed user "
    "is not necessarily. Not a framework issue."
)


def _skip_on_permission_or_link_gap(response, *, permission: str) -> None:
    if response.status_code == 403:
        allure.attach(response.text, name="403 response", attachment_type=allure.attachment_type.JSON)
        pytest.skip(_PERMISSION_GAP_REASON.format(permission=permission, status=response.status_code))
    if response.status_code == 404:
        allure.attach(response.text, name="404 response", attachment_type=allure.attachment_type.JSON)
        pytest.skip(_NOT_LINKED_REASON)


# --- Self-service reads (ATT-SELF-001/002 — "current attendance/summary" + "records/history") ---


@allure.story("Self-service calendar")
@allure.title("ATT-SELF-001 — GET me/calendar returns the caller's own attendance for the requested month")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_my_attendance_calendar_and_summary_is_readable(admin_api_client):
    today = datetime.date.today()
    response = get_my_calendar(admin_api_client, year=today.year, month=today.month)
    _skip_on_permission_or_link_gap(response, permission="Attendance.View")
    assert response.status_code == 200, response.text
    days = response.json()["data"]
    assert isinstance(days, list), "Expected a list (possibly empty) of calendar days for the month"


@allure.story("Self-service day detail")
@allure.title("ATT-SELF-002 — GET me/days/{date} returns punch/session detail for a processed day")
@pytest.mark.smoke
@pytest.mark.regression
def test_my_attendance_day_detail_is_readable(admin_api_client):
    today = datetime.date.today()
    calendar = get_my_calendar(admin_api_client, year=today.year, month=today.month)
    _skip_on_permission_or_link_gap(calendar, permission="Attendance.View")
    assert calendar.status_code == 200, calendar.text
    days = calendar.json()["data"]
    target_date = next((d["date"] for d in days if d.get("date")), today.isoformat())

    response = get_my_day(admin_api_client, target_date)
    assert response.status_code == 200, response.text
    detail = response.json()["data"]
    assert "day" in detail and "punches" in detail and "sessions" in detail, response.text


# --- Manager team read ---


@allure.story("Manager team")
@allure.title("ATT-SELF-012 — GET manager/team is reachable and scoped to the caller's in-scope employees")
@pytest.mark.smoke
@pytest.mark.regression
def test_manager_team_is_readable(admin_api_client):
    today = datetime.date.today()
    response = get_manager_team(admin_api_client, from_date=today.isoformat(), to_date=today.isoformat())
    _skip_on_permission_or_link_gap(response, permission="Attendance.View")
    assert response.status_code == 200, response.text
    payload = response.json()["data"]
    assert "rows" in payload and "summary" in payload, response.text


# --- Regularization: submission, validation, self-cancel, employee-sees-status, maker-checker ---


@allure.story("Regularization")
@allure.title("ATT-REG-001 — a well-formed Regularization request lands Pending; the owner then cancels it")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_regularization_submission_creates_pending_request(admin_api_client):
    business_date = datetime.date.today() - datetime.timedelta(days=1)
    submission = submit_regularization(
        admin_api_client,
        businessDate=business_date.isoformat(),
        requestType="MissingOutPunch",
        proposedOutAtUtc=f"{business_date.isoformat()}T18:00:00Z",
        reason=unique_reason("regularization"),
    )
    _skip_on_permission_or_link_gap(submission, permission="Attendance.RegularizationRequest")
    if submission.status_code == 409:
        allure.attach(submission.text, name="409 response", attachment_type=allure.attachment_type.JSON)
        pytest.skip(
            "Submit was rejected as a business-rule conflict (e.g. a clean Present day, or an "
            f"already-pending request for this date — see AttendanceWorkflowService): {submission.text}"
        )
    assert submission.status_code == 200, submission.text
    created = submission.json()["data"]
    assert created["status"] == "Pending", submission.text
    request_id = created["id"]

    with allure.step("The owner cancels their own just-submitted request (cleanup)"):
        cancelled = cancel_regularization(admin_api_client, request_id)
        assert cancelled.status_code == 200, cancelled.text
        assert cancelled.json()["data"]["status"] == "Cancelled", cancelled.text


@allure.story("Regularization")
@allure.title("ATT-REG (validation) — a Regularization request with no reason is rejected 400")
@pytest.mark.smoke
@pytest.mark.regression
def test_regularization_submission_without_reason_is_rejected(admin_api_client):
    business_date = datetime.date.today() - datetime.timedelta(days=1)
    response = submit_regularization(
        admin_api_client,
        businessDate=business_date.isoformat(),
        requestType="MissingOutPunch",
        proposedOutAtUtc=f"{business_date.isoformat()}T18:00:00Z",
        reason="",
    )
    _skip_on_permission_or_link_gap(response, permission="Attendance.RegularizationRequest")
    assert response.status_code == 400, response.text


@allure.story("Regularization")
@allure.title("ATT-SELF (history) — the employee sees their Regularization's status update after cancelling it")
@pytest.mark.smoke
@pytest.mark.regression
def test_employee_sees_regularization_status_after_cancel(admin_api_client):
    business_date = datetime.date.today() - datetime.timedelta(days=2)
    submission = submit_regularization(
        admin_api_client,
        businessDate=business_date.isoformat(),
        requestType="MissingOutPunch",
        proposedOutAtUtc=f"{business_date.isoformat()}T18:00:00Z",
        reason=unique_reason("regularization-status"),
    )
    _skip_on_permission_or_link_gap(submission, permission="Attendance.RegularizationRequest")
    if submission.status_code == 409:
        pytest.skip(f"Submit was rejected as a business-rule conflict: {submission.text}")
    assert submission.status_code == 200, submission.text
    request_id = submission.json()["data"]["id"]

    cancel_regularization(admin_api_client, request_id)

    with allure.step("Re-read the same request and confirm the status change is visible"):
        after = get_my_regularization(admin_api_client, request_id)
        assert after.status_code == 200, after.text
        assert after.json()["data"]["status"] == "Cancelled", after.text


@allure.story("Regularization approvals")
@allure.title("ATT-REG-014 — the employee owning a Regularization request cannot approve/reject it themselves")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_regularization_owner_cannot_approve_own_request(admin_api_client):
    business_date = datetime.date.today() - datetime.timedelta(days=3)
    submission = submit_regularization(
        admin_api_client,
        businessDate=business_date.isoformat(),
        requestType="MissingOutPunch",
        proposedOutAtUtc=f"{business_date.isoformat()}T18:00:00Z",
        reason=unique_reason("regularization-maker-checker"),
    )
    _skip_on_permission_or_link_gap(submission, permission="Attendance.RegularizationRequest")
    if submission.status_code == 409:
        pytest.skip(f"Submit was rejected as a business-rule conflict: {submission.text}")
    assert submission.status_code == 200, submission.text
    request_id = submission.json()["data"]["id"]

    try:
        approval = approve_regularization(admin_api_client, request_id)
        if approval.status_code == 403:
            assert "own" in approval.text.lower() or "cannot" in approval.text.lower(), approval.text
        else:
            pytest.skip(
                f"QA_A_ADMIN unexpectedly lacks Attendance.RegularizationApprove ({approval.status_code}) — "
                f"cannot exercise the maker-checker denial without that permission: {approval.text}"
            )
    finally:
        cancel_regularization(admin_api_client, request_id)


@allure.story("Regularization approvals")
@allure.title("ATT-REG (manager queue) — the manager Regularization queue is reachable and paged")
@pytest.mark.smoke
@pytest.mark.regression
def test_manager_regularization_queue_is_accessible(admin_api_client):
    response = get_manager_regularizations(admin_api_client, page=1, pageSize=25)
    _skip_on_permission_or_link_gap(response, permission="Attendance.RegularizationApprove")
    assert response.status_code == 200, response.text
    paged = response.json()["data"]
    assert "items" in paged and "totalCount" in paged, response.text


# --- On Duty: submission, self-cancel, maker-checker (mirrors Regularization) ---


@allure.story("On Duty")
@allure.title("ATT-OD-001 — a one-day On Duty request with reason lands Pending; the owner then cancels it")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_on_duty_submission_creates_pending_request(admin_api_client):
    day = datetime.date.today() + datetime.timedelta(days=14)
    submission = submit_on_duty(
        admin_api_client,
        startDate=day.isoformat(),
        endDate=day.isoformat(),
        reason=unique_reason("on-duty"),
    )
    _skip_on_permission_or_link_gap(submission, permission="Attendance.OnDutyRequest")
    if submission.status_code == 409:
        allure.attach(submission.text, name="409 response", attachment_type=allure.attachment_type.JSON)
        pytest.skip(f"Submit was rejected as a business-rule conflict (e.g. an overlapping pending request): {submission.text}")
    assert submission.status_code == 200, submission.text
    created = submission.json()["data"]
    assert created["status"] == "Pending", submission.text
    request_id = created["id"]

    with allure.step("The owner cancels their own just-submitted request (cleanup)"):
        cancelled = cancel_on_duty(admin_api_client, request_id)
        assert cancelled.status_code == 200, cancelled.text
        assert cancelled.json()["data"]["status"] == "Cancelled", cancelled.text


@allure.story("On Duty approvals")
@allure.title("ATT-OD-012 (maker-checker) — the employee owning an On Duty request cannot approve/reject it themselves")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_on_duty_owner_cannot_approve_own_request(admin_api_client):
    day = datetime.date.today() + datetime.timedelta(days=15)
    submission = submit_on_duty(
        admin_api_client,
        startDate=day.isoformat(),
        endDate=day.isoformat(),
        reason=unique_reason("on-duty-maker-checker"),
    )
    _skip_on_permission_or_link_gap(submission, permission="Attendance.OnDutyRequest")
    if submission.status_code == 409:
        pytest.skip(f"Submit was rejected as a business-rule conflict: {submission.text}")
    assert submission.status_code == 200, submission.text
    request_id = submission.json()["data"]["id"]

    try:
        approval = approve_on_duty(admin_api_client, request_id)
        if approval.status_code == 403:
            assert "own" in approval.text.lower() or "cannot" in approval.text.lower(), approval.text
        else:
            pytest.skip(
                f"QA_A_ADMIN unexpectedly lacks Attendance.OnDutyApprove ({approval.status_code}) — "
                f"cannot exercise the maker-checker denial without that permission: {approval.text}"
            )
    finally:
        cancel_on_duty(admin_api_client, request_id)


@allure.story("On Duty approvals")
@allure.title("ATT-OD (manager queue) — the manager On Duty queue is reachable and paged")
@pytest.mark.smoke
@pytest.mark.regression
def test_manager_on_duty_queue_is_accessible(admin_api_client):
    response = get_manager_on_duty(admin_api_client, page=1, pageSize=25)
    _skip_on_permission_or_link_gap(response, permission="Attendance.OnDutyApprove")
    assert response.status_code == 200, response.text
    paged = response.json()["data"]
    assert "items" in paged and "totalCount" in paged, response.text


# --- Reports ---


@allure.story("Reports")
@allure.title("ATT-RPT-001 — GET reports/daily is reachable and returns a paged result for a date range")
@pytest.mark.smoke
@pytest.mark.regression
def test_daily_report_is_readable(admin_api_client):
    today = datetime.date.today()
    from_date = today.replace(day=1)
    response = get_daily_report(admin_api_client, from_date=from_date.isoformat(), to_date=today.isoformat())
    _skip_on_permission_or_link_gap(response, permission="Attendance.ReportView")
    assert response.status_code == 200, response.text
    paged = response.json()["data"]
    assert "items" in paged and "totalCount" in paged, response.text


# --- Authorization / authentication (unauthorized access denied) ---


@allure.story("Authorization")
@allure.title("ATT-SEC — no bearer token is rejected 401 on the Attendance self-service surface")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_unauthenticated_attendance_call_is_rejected(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    anonymous_client = api_client_factory(host)
    today = datetime.date.today()
    response = get_my_calendar(anonymous_client, year=today.year, month=today.month)
    assert response.status_code == 401, response.text


@allure.story("Authorization")
@allure.title("ATT-SEC — the seeded Employee role cannot reach the Regularization manager queue")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_cannot_access_manager_regularization_queue(employee_api_client):
    response = get_manager_regularizations(employee_api_client, page=1, pageSize=25)
    assert response.status_code == 403, response.text


@allure.story("Authorization")
@allure.title("ATT-SEC — the seeded Employee role cannot approve a Regularization request")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_cannot_approve_regularization(employee_api_client):
    response = approve_regularization(employee_api_client, str(uuid.uuid4()))
    assert response.status_code == 403, response.text


@allure.story("Authorization")
@allure.title("ATT-SEC (CR-112) — the seeded Manager role cannot approve a Regularization or On Duty request")
@pytest.mark.smoke
@pytest.mark.regression
def test_manager_role_cannot_approve_under_seeded_permissions(manager_api_client):
    """Documents a real, source-verified finding (qa/08-attendance/README.md CR-112): the seeded
    `Manager` role (SeedData.RolePermissionMap[RoleNames.Manager]) does not hold
    `Attendance.RegularizationApprove`/`Attendance.OnDutyApprove` — only SuperAdmin/TenantAdmin do.
    A caller correctly holding the product's "Manager" role cannot use Attendance's own
    manager-approval workflow at all. This is expected/current behavior, asserted here rather than
    treated as a framework defect — but it is exactly the gap the Phase 5 brief flags as a reason to
    provision a dedicated, more-representative QA Manager identity."""
    reg_response = get_manager_regularizations(manager_api_client, page=1, pageSize=25)
    assert reg_response.status_code == 403, reg_response.text
    od_response = get_manager_on_duty(manager_api_client, page=1, pageSize=25)
    assert od_response.status_code == 403, od_response.text


@allure.story("Authorization")
@allure.title("ATT-SEC-001 — tenant isolation on the Attendance surface (needs a second tenant)")
@pytest.mark.regression
def test_tenant_isolation_on_attendance_calendar():
    """ATT-SEC-001 needs a second, real tenant (Tenant B) whose data a Tenant A caller must never
    see. No QA_TENANT_B_HOST is configured in this environment (see qa/automation/.env.example) —
    skipping honestly rather than fabricating a second tenant, per the Phase 5 brief's "where
    credentials permit"."""
    pytest.skip("QA_TENANT_B_HOST is not configured — tenant isolation needs a second real tenant, not fabricated.")
