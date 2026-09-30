"""API regression suite for Module 07 — Leave approval inbox, approve/reject, and cancellation/withdrawal.

Automated from qa/07-leave/cases/09-approval-manager-scope.yaml (LEAVE-APPR-*, LEAVE-INBOX-*) and
10-cancellation.yaml (LEAVE-CANCEL-*).

The permission/scope/404 contract runs everywhere: approve/reject require Leave.Approve (Employee is
403), the inbox and its GetById require Leave.Approve, and an out-of-scope/unknown request id is a
uniform 404 (not a 403 — the one deliberate GetById exception, LEAVE-INBOX-004). The state-machine
lifecycle (approve a real pending request, cancel an approved one) needs a submittable request for
the QA employee and honest-skips when no usable policy resolves (qa/07-leave/README.md, CR-81).

Inbox rows can carry real employee names; this suite asserts only their shape and never attaches raw
rows to evidence.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.leave_api import (
    approve_leave_request,
    get_approval_detail,
    get_approval_inbox,
    reject_leave_request,
)
from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Leave Management"), pytest.mark.api, pytest.mark.regression]


# --- Inbox read + authorization ----------------------------------------------------------------


@allure.story("Leave approvals")
@allure.title("LEAVE-INBOX-002 — the approval inbox is a paged list carrying employee code/name per row")
@qa_cases("LEAVE-INBOX-002")
def test_inbox_paged_shape(admin_api_client):
    response = get_approval_inbox(admin_api_client, page=1, pageSize=5)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert data["page"] == 1 and data["pageSize"] == 5
    assert {"items", "totalCount", "totalPages"} <= set(data)
    for row in data["items"]:
        for field in ("requestId", "employeeCode", "employeeName", "leaveTypeCode", "startDate", "endDate"):
            assert field in row, f"inbox row missing {field!r}"


@allure.story("Leave approvals")
@allure.title("LEAVE-INBOX-001 / LEAVE-AUTHZ-003 — the inbox requires Leave.Approve: Employee is 403, Manager 200")
@qa_cases("LEAVE-INBOX-001", "LEAVE-AUTHZ-003")
@pytest.mark.critical
def test_inbox_requires_approve_permission(employee_api_client, manager_api_client):
    assert get_approval_inbox(employee_api_client).status_code == 403
    assert get_approval_inbox(manager_api_client).status_code == 200


@allure.story("Leave approvals")
@allure.title("LEAVE-AUTHZ-002 — an anonymous inbox call is rejected 401")
@qa_cases("LEAVE-AUTHZ-002")
def test_inbox_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert get_approval_inbox(anon).status_code == 401


@allure.story("Leave approvals")
@allure.title("LEAVE-INBOX-004 — GetById for an unknown/out-of-scope request is a 404, never a 403")
@qa_cases("LEAVE-INBOX-004")
def test_approval_detail_unknown_is_404(manager_api_client):
    assert get_approval_detail(manager_api_client, str(uuid.uuid4())).status_code == 404


@allure.story("Leave approvals")
@allure.title("LEAVE-AUTHZ-003 — the approval-detail endpoint itself requires Leave.Approve (Employee 403)")
@qa_cases("LEAVE-AUTHZ-003")
def test_approval_detail_requires_permission(employee_api_client):
    assert get_approval_detail(employee_api_client, str(uuid.uuid4())).status_code == 403


# --- Approve / reject authorization + scope ----------------------------------------------------


@allure.story("Leave approvals")
@allure.title("LEAVE-APPR-001 / LEAVE-AUTHZ-003 — an Employee (no Leave.Approve) cannot approve or reject (403)")
@qa_cases("LEAVE-APPR-001", "LEAVE-AUTHZ-003")
@pytest.mark.critical
def test_employee_cannot_approve_or_reject(employee_api_client):
    rid = str(uuid.uuid4())
    assert approve_leave_request(employee_api_client, rid).status_code == 403
    assert reject_leave_request(employee_api_client, rid).status_code == 403


@allure.story("Leave approvals")
@allure.title("LEAVE-APPR-004 — an authorized approver hitting an unknown request id gets 404, not 403")
@qa_cases("LEAVE-APPR-004", "LEAVE-INBOX-004")
def test_authorized_approver_unknown_request_is_404(manager_api_client, admin_api_client):
    rid = str(uuid.uuid4())
    assert approve_leave_request(manager_api_client, rid).status_code == 404
    assert reject_leave_request(admin_api_client, rid).status_code == 404


@allure.story("Leave cancellation")
@allure.title("LEAVE-CANCEL-007 / LEAVE-CANCEL-010 — a manager cancelling another employee's request is a uniform 404 (no manager-cancel path)")
@qa_cases("LEAVE-CANCEL-007", "LEAVE-CANCEL-010")
@cr_refs("CR-84")
def test_no_manager_cancellation_path(manager_api_client):
    from core.leave_api import cancel_leave_request

    # Cancel is owner-only (RequestCancelOwn resolves owner == caller); there is no manager-initiated
    # cancellation anywhere in the API, so a manager cancelling a request that is not their own can only
    # ever be NotFound, never an authorized cancel.
    assert cancel_leave_request(manager_api_client, str(uuid.uuid4())).status_code == 404


# --- State-machine lifecycle (needs a usable policy; honest-skip otherwise) ---------------------


@allure.story("Leave approvals")
@allure.title("LEAVE-APPR-002 — a submitted request appears to its authorized approver and can be approved")
@qa_cases("LEAVE-APPR-002", "LEAVE-INBOX-001")
def test_manager_can_approve_reports_request(employee_api_client, manager_api_client):
    import uuid as _uuid
    from datetime import date, timedelta

    from core.leave_api import (
        get_available_leave_types,
        preview_leave_request,
        submit_leave_request,
        withdraw_leave_request,
    )

    start = date.today() + timedelta(days=13)
    available = get_available_leave_types(employee_api_client)
    if available.status_code != 200:
        pytest.skip("QA employee cannot resolve available leave types (unlinked or unconfigured).")
    leave_type = None
    for candidate in available.json()["data"]["items"]:
        preview = preview_leave_request(
            employee_api_client, leaveTypeId=candidate["id"], startDate=start.isoformat(),
            endDate=start.isoformat(), idempotencyKey=str(_uuid.uuid4()),
        )
        if preview.status_code == 200:
            leave_type = candidate
            break
    if leave_type is None:
        pytest.skip(
            "No available Leave Type resolves a usable policy for the QA employee — cannot create a pending "
            "request to approve (qa/07-leave/README.md, CR-81)."
        )

    submission = submit_leave_request(
        employee_api_client, leaveTypeId=leave_type["id"], startDate=start.isoformat(),
        endDate=start.isoformat(), idempotencyKey=str(_uuid.uuid4()),
    )
    if submission.status_code != 201:
        pytest.skip(f"Submit rejected ({submission.status_code}) by a business rule caught only at submit: {submission.text}")
    request_id = submission.json()["data"]["requestId"]

    detail = get_approval_detail(manager_api_client, request_id)
    if detail.status_code != 200:
        # QA manager may not be this employee's current manager-of-record; clean up and skip.
        withdraw_leave_request(employee_api_client, request_id)
        pytest.skip(f"QA manager is not in scope for this request ({detail.status_code}); manager-of-record relationship not established.")

    approved = approve_leave_request(manager_api_client, request_id)
    assert approved.status_code == 200, approved.text
    assert approved.json()["data"]["status"] == "Approved", approved.text
    # Approved leave is not owner-withdrawable; leave it as a QAAUTO-owned Approved record for the
    # cancellation lifecycle, or the owner cancels it if the policy allows.
    from core.leave_api import cancel_leave_request

    cancel_leave_request(employee_api_client, request_id)
