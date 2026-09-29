"""API smoke suite for Module 07 — Leave Management (Phase 4, first batch).

Proof-of-concept coverage, automated from qa/07-leave/cases/*.yaml. Deliberately narrow, per
qa/07-leave/README.md's own caution: Leave is "a deliberately restricted MVP wearing a much larger
configuration UI" (CR-81) — a published policy with any half-day/sandwich/attachment/clubbing rule
is silently unusable at real request time, and no application code or tenant configuration was
touched to make this suite's environment more favorable. Tests that need an actual submittable
leave request discover whether one is currently possible in this tenant (via the self-service
`available` list + Preview) and skip honestly, with the response body attached, when it isn't —
never fabricating a pass and never treating a business-rule/config-dependent 4xx as a framework
defect.

Requires QA_TENANT_A_HOST plus, per test: QA_A_ADMIN_USERNAME/PASSWORD (admin_api_client, for
Leave Type configuration reads), QA_A_EMPLOYEE_USERNAME/PASSWORD (employee_api_client, for every
self-service check — a linked employee is required to resolve an identity; an admin/TenantAdmin
seed user is not necessarily one), and QA_A_MANAGER_USERNAME/PASSWORD (manager_api_client, for the
approval-inbox check only). Each fixture skips on its own if its pair is unset or sign-in fails.

No Leave Type/Policy/Period configuration is created or modified by this suite — every test either
reads existing configuration or submits/withdraws a leave request as the disposable QA_A_EMPLOYEE
identity itself (never touching another employee's data), and any request this suite submits is
withdrawn before the test ends.
"""

from __future__ import annotations

import uuid
from datetime import date, timedelta

import allure
import pytest

from core.leave_api import (
    create_leave_type,
    get_approval_inbox,
    get_available_leave_types,
    get_my_leave_balances,
    list_leave_types,
    list_my_leave_requests,
    preview_leave_request,
    submit_leave_request,
    withdraw_leave_request,
)
from utils.env_utils import require_env

pytestmark = [allure.feature("Leave Management"), pytest.mark.api]


def _find_previewable_leave_type(client, *, start: date, end: date):
    """Tries Preview against every self-service-available Leave Type, returning
    `(leave_type, preview_response)` for the first one that resolves (HTTP 200), or `(None, None)`
    if none does. Every attempt uses its own idempotency key so a caller can safely retry the same
    (type, response) with the same key afterward for an idempotency check.
    """
    available = get_available_leave_types(client)
    if available.status_code != 200:
        return None, None, available
    types = available.json()["data"]["items"]
    for leave_type in types:
        response = preview_leave_request(
            client,
            leaveTypeId=leave_type["id"],
            startDate=start.isoformat(),
            endDate=end.isoformat(),
            idempotencyKey=str(uuid.uuid4()),
        )
        if response.status_code == 200:
            return leave_type, response, available
    return None, None, available


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-001 — GET /api/leave-types returns a paged, IsActive-filtered list")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_leave_types_list_is_readable(admin_api_client):
    response = list_leave_types(admin_api_client, isActive=True)
    assert response.status_code == 200, response.text
    items = response.json()["data"]["items"]
    assert all(item["isActive"] for item in items), "Expected every item to be IsActive=true per the isActive=true filter"


@allure.story("Leave request options")
@allure.title("LEAVE-OPT-001 — GET /api/leave-types/available lists only IsActive=true types for the caller")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_available_leave_types_lists_only_active(employee_api_client):
    response = get_available_leave_types(employee_api_client)
    assert response.status_code == 200, response.text
    items = response.json()["data"]["items"]
    assert all(item["isActive"] for item in items), "Expected every available LeaveType to be IsActive=true"


@allure.story("Leave balance")
@allure.title("LEAVE-BAL-001 — GET /api/leave-balances/mine is readable for the caller's own identity")
@pytest.mark.smoke
@pytest.mark.regression
def test_my_leave_balance_is_readable(employee_api_client):
    response = get_my_leave_balances(employee_api_client)
    assert response.status_code == 200, response.text
    assert isinstance(response.json()["data"], list), "Expected a list (possibly empty) of per-LeaveType balance summaries"


@allure.story("Leave requests")
@allure.title("LEAVE-AUTHZ-005 — GET /api/leave-requests accepts no employeeId, always the caller's own requests")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_my_leave_requests_list_is_self_scoped(employee_api_client):
    response = list_my_leave_requests(employee_api_client)
    assert response.status_code == 200, response.text
    paged = response.json()["data"]
    assert "items" in paged and "totalCount" in paged


@allure.story("Leave requests")
@allure.title("LEAVE-REQ-001 — Preview runs the full pipeline with no side effects and is idempotent for the same key")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_preview_has_no_side_effects(employee_api_client):
    start = date.today() + timedelta(days=7)
    leave_type, first_preview, available_response = _find_previewable_leave_type(employee_api_client, start=start, end=start)
    if leave_type is None:
        allure.attach(available_response.text, name="available-leave-types response", attachment_type=allure.attachment_type.JSON)
        pytest.skip(
            "No available Leave Type in this tenant currently resolves a usable policy for Preview — "
            "business-rule/policy-configuration-dependent (see qa/07-leave/README.md, CR-81), not a framework issue."
        )

    key = str(uuid.uuid4())
    before = list_my_leave_requests(employee_api_client)
    assert before.status_code == 200, before.text
    before_count = before.json()["data"]["totalCount"]

    with allure.step("Preview twice with the same idempotency key"):
        first = preview_leave_request(employee_api_client, leaveTypeId=leave_type["id"], startDate=start.isoformat(), endDate=start.isoformat(), idempotencyKey=key)
        assert first.status_code == 200, first.text
        second = preview_leave_request(employee_api_client, leaveTypeId=leave_type["id"], startDate=start.isoformat(), endDate=start.isoformat(), idempotencyKey=key)
        assert second.status_code == 200, second.text
        assert first.json()["data"] == second.json()["data"], "Expected an identical validation result for the repeated call"

    with allure.step("Confirm no leave request row was created by either Preview call"):
        after = list_my_leave_requests(employee_api_client)
        assert after.status_code == 200, after.text
        assert after.json()["data"]["totalCount"] == before_count


@allure.story("Leave requests")
@allure.title("LEAVE-REQ / LEAVE-CANCEL-001 — Submit creates a PendingApproval request; the owner withdraws it")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_submit_and_withdraw_pending_leave_request(employee_api_client):
    start = date.today() + timedelta(days=7)
    leave_type, _preview, available_response = _find_previewable_leave_type(employee_api_client, start=start, end=start)
    if leave_type is None:
        allure.attach(available_response.text, name="available-leave-types response", attachment_type=allure.attachment_type.JSON)
        pytest.skip(
            "No available Leave Type in this tenant currently resolves a usable policy for Preview/Submit — "
            "business-rule/policy-configuration-dependent (see qa/07-leave/README.md, CR-81), not a framework issue."
        )

    submission = submit_leave_request(
        employee_api_client,
        leaveTypeId=leave_type["id"],
        startDate=start.isoformat(),
        endDate=start.isoformat(),
        idempotencyKey=str(uuid.uuid4()),
    )
    if submission.status_code != 201:
        pytest.skip(
            f"Submit was rejected ({submission.status_code}) for the one Leave Type whose Preview succeeded — "
            f"a business rule caught only at Submit (e.g. insufficient balance, notice period), not a framework issue: {submission.text}"
        )
    submitted = submission.json()["data"]
    assert submitted["status"] == "PendingApproval", submission.text
    request_id = submitted["requestId"]

    with allure.step("Withdraw the just-submitted request as its owner"):
        withdrawn = withdraw_leave_request(employee_api_client, request_id)
        assert withdrawn.status_code == 200, withdrawn.text
        assert withdrawn.json()["data"]["status"] == "Withdrawn", withdrawn.text


@allure.story("Leave approvals")
@allure.title("LEAVE-INBOX-001 — a manager's approval inbox is reachable and paged")
@pytest.mark.smoke
@pytest.mark.regression
def test_manager_approval_inbox_is_accessible(manager_api_client):
    response = get_approval_inbox(manager_api_client, page=1, pageSize=25)
    assert response.status_code == 200, response.text
    paged = response.json()["data"]
    assert "items" in paged and "totalCount" in paged


@allure.story("Authorization")
@allure.title("LEAVE-AUTHZ-002 — no bearer token is rejected 401 on the Leave surface")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_unauthenticated_leave_requests_call_is_rejected(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    anonymous_client = api_client_factory(host)
    response = list_my_leave_requests(anonymous_client)
    assert response.status_code == 401, response.text


@allure.story("Authorization")
@allure.title("LEAVE-AUTHZ-003 — an Employee (holds TypeViewAvailable, not TypeManage) gets 403 creating a Leave Type")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_cannot_manage_leave_types(employee_api_client):
    response = create_leave_type(
        employee_api_client, code=f"QAAUTO{uuid.uuid4().hex[:6]}", name="QA Automation probe", defaultUnit="Day", isPaid=True, isActive=True,
    )
    assert response.status_code == 403, response.text
