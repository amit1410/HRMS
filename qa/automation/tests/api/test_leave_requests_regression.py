"""API regression suite for Module 07 — Leave request options and the Preview/Submit pipeline.

Automated from qa/07-leave/cases/06-request-options-creation.yaml (LEAVE-OPT-*, LEAVE-REQ-*) and
the self-scope/authorization cases in 15-authz-tenant-security.yaml (LEAVE-AUTHZ-005).

Two tiers:
  * Shape/identity/authorization checks that fire BEFORE policy resolution — these run everywhere
    (inverted dates, missing/unknown LeaveType, self-scope, unlinked identity, 404 lifecycle ids).
  * Full Preview/Submit lifecycle — these need a resolvable, usable policy for the disposable
    QA employee. Per qa/07-leave/README.md (CR-81) Leave is a restricted MVP and the QA employee may
    have no applicable Published policy; those tests discover this via `available`+Preview and skip
    honestly with the response body attached, exactly like test_leave_smoke.py — never fabricating a
    pass and never treating a business-rule 4xx as a framework defect. Any request a lifecycle test
    does submit is withdrawn before it ends (safe cleanup).
"""

from __future__ import annotations

import uuid
from datetime import date, timedelta

import allure
import pytest

from core.leave_api import (
    get_available_leave_types,
    get_my_leave_request,
    list_my_leave_requests,
    preview_leave_request,
    submit_leave_request,
    withdraw_leave_request,
)
from utils.allure_evidence import qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Leave Management"), pytest.mark.api, pytest.mark.regression]

_NO_POLICY_SKIP = (
    "No available Leave Type resolves a usable policy for the QA employee (Preview did not return 200) — "
    "business-rule/policy-configuration-dependent (qa/07-leave/README.md, CR-81), not a framework issue."
)


def _first_previewable(client, *, start: date, end: date):
    """Returns (leave_type, preview_response) for the first available type whose Preview resolves
    (HTTP 200), else (None, last_available_response)."""
    available = get_available_leave_types(client)
    if available.status_code != 200:
        return None, available
    for leave_type in available.json()["data"]["items"]:
        response = preview_leave_request(
            client, leaveTypeId=leave_type["id"], startDate=start.isoformat(),
            endDate=end.isoformat(), idempotencyKey=str(uuid.uuid4()),
        )
        if response.status_code == 200:
            return leave_type, response
    return None, available


# --- Options / available types -----------------------------------------------------------------


@allure.story("Leave request options")
@allure.title("LEAVE-OPT-001 — available types lists only IsActive=true types for the caller")
@qa_cases("LEAVE-OPT-001")
@pytest.mark.critical
def test_available_lists_only_active(employee_api_client):
    response = get_available_leave_types(employee_api_client)
    assert response.status_code == 200, response.text
    items = response.json()["data"]["items"]
    assert all(item["isActive"] for item in items)


@allure.story("Leave request options")
@allure.title("LEAVE-OPT-002 — an active-but-unresolvable type stays in options; unavailability surfaces only at Preview")
@qa_cases("LEAVE-OPT-002")
def test_options_not_eligibility_filtered(employee_api_client):
    items = get_available_leave_types(employee_api_client).json()["data"]["items"]
    if not items:
        pytest.skip("No available Leave Types for the QA employee to probe.")
    # The options list is a static Active list; whether a policy resolves is only discovered at Preview.
    # Whichever we hit, the response must be a well-formed 200 (resolves) or 4xx (business rule), never a 5xx.
    start = date.today() + timedelta(days=7)
    response = preview_leave_request(
        employee_api_client, leaveTypeId=items[0]["id"], startDate=start.isoformat(),
        endDate=start.isoformat(), idempotencyKey=str(uuid.uuid4()),
    )
    assert response.status_code in (200, 400, 403, 404, 409), response.text
    assert response.status_code < 500, response.text


@allure.story("Leave request options")
@allure.title("LEAVE-OPT-003 / LEAVE-ELIGIBILITY-001 — an unlinked account cannot resolve options")
@qa_cases("LEAVE-OPT-003", "LEAVE-ELIGIBILITY-001")
def test_options_require_linked_identity(admin_api_client):
    # The seeded QA Admin (TenantAdmin) is authorized but is not itself a linked Employee.
    response = get_available_leave_types(admin_api_client)
    assert response.status_code in (401, 404), response.text


# --- Preview shape / identity validation (pre-policy) -------------------------------------------


@allure.story("Leave requests")
@allure.title("LEAVE-REQ-013 — EndDate before StartDate is rejected 400 before any lookup")
@qa_cases("LEAVE-REQ-013")
@pytest.mark.critical
def test_preview_inverted_dates_rejected(employee_api_client):
    items = get_available_leave_types(employee_api_client).json()["data"]["items"]
    leave_type_id = items[0]["id"] if items else str(uuid.uuid4())
    start = date.today() + timedelta(days=8)
    response = preview_leave_request(
        employee_api_client, leaveTypeId=leave_type_id, startDate=start.isoformat(),
        endDate=(start - timedelta(days=1)).isoformat(), idempotencyKey=str(uuid.uuid4()),
    )
    assert response.status_code == 400, response.text


@allure.story("Leave requests")
@allure.title("LEAVE-REQ-013 — a missing leaveTypeId is a 400 field error")
@qa_cases("LEAVE-REQ-013")
def test_preview_missing_leave_type_rejected(employee_api_client):
    start = date.today() + timedelta(days=7)
    response = preview_leave_request(
        employee_api_client, startDate=start.isoformat(), endDate=start.isoformat(), idempotencyKey=str(uuid.uuid4())
    )
    assert response.status_code == 400, response.text


@allure.story("Leave requests")
@allure.title("LEAVE-OPT-002 — an unknown leaveTypeId is a clean 404 (LeaveType not found)")
@qa_cases("LEAVE-OPT-002")
def test_preview_unknown_leave_type_is_404(employee_api_client):
    start = date.today() + timedelta(days=7)
    response = preview_leave_request(
        employee_api_client, leaveTypeId=str(uuid.uuid4()), startDate=start.isoformat(),
        endDate=start.isoformat(), idempotencyKey=str(uuid.uuid4()),
    )
    assert response.status_code == 404, response.text


# --- Self-scope / authorization ----------------------------------------------------------------


@allure.story("Leave requests")
@allure.title("LEAVE-AUTHZ-005 — the requests list is self-scoped; an employeeId query param cannot widen it")
@qa_cases("LEAVE-AUTHZ-005")
@pytest.mark.critical
def test_requests_list_is_self_scoped(employee_api_client):
    baseline = list_my_leave_requests(employee_api_client)
    assert baseline.status_code == 200, baseline.text
    baseline_total = baseline.json()["data"]["totalCount"]
    # Supplying someone else's employeeId must be ignored, never returning another employee's rows.
    with_param = list_my_leave_requests(employee_api_client, employeeId=str(uuid.uuid4()))
    assert with_param.status_code == 200, with_param.text
    assert with_param.json()["data"]["totalCount"] == baseline_total


@allure.story("Leave requests")
@allure.title("LEAVE-AUTHZ-005 — the requests list is paged")
@qa_cases("LEAVE-AUTHZ-005")
def test_requests_list_paged(employee_api_client):
    response = list_my_leave_requests(employee_api_client, page=1, pageSize=5)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert data["page"] == 1 and data["pageSize"] == 5
    assert {"items", "totalCount", "totalPages", "hasNextPage", "hasPreviousPage"} <= set(data)


@allure.story("Leave requests")
@allure.title("LEAVE-AUTHZ-002 — an anonymous requests-list call is rejected 401")
@qa_cases("LEAVE-AUTHZ-002")
def test_requests_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert list_my_leave_requests(anon).status_code == 401


@allure.story("Leave requests")
@allure.title("LEAVE-CANCEL-010 — get/withdraw/cancel of an unknown request id are all a uniform 404")
@qa_cases("LEAVE-CANCEL-010", "LEAVE-CANCEL-003")
def test_unknown_request_id_is_404(employee_api_client):
    from core.leave_api import cancel_leave_request

    unknown = str(uuid.uuid4())
    assert get_my_leave_request(employee_api_client, unknown).status_code == 404
    assert withdraw_leave_request(employee_api_client, unknown).status_code == 404
    assert cancel_leave_request(employee_api_client, unknown).status_code == 404


# --- Full lifecycle (needs a usable policy; honest-skip otherwise) ------------------------------


@allure.story("Leave requests")
@allure.title("LEAVE-REQ-001 — Preview runs the full pipeline with no side effects and replays identically")
@qa_cases("LEAVE-REQ-001")
@pytest.mark.critical
def test_preview_idempotent_no_side_effects(employee_api_client):
    start = date.today() + timedelta(days=7)
    leave_type, evidence = _first_previewable(employee_api_client, start=start, end=start)
    if leave_type is None:
        allure.attach(evidence.text, name="available/preview response", attachment_type=allure.attachment_type.JSON)
        pytest.skip(_NO_POLICY_SKIP)

    key = str(uuid.uuid4())
    before = list_my_leave_requests(employee_api_client).json()["data"]["totalCount"]
    first = preview_leave_request(
        employee_api_client, leaveTypeId=leave_type["id"], startDate=start.isoformat(),
        endDate=start.isoformat(), idempotencyKey=key,
    )
    second = preview_leave_request(
        employee_api_client, leaveTypeId=leave_type["id"], startDate=start.isoformat(),
        endDate=start.isoformat(), idempotencyKey=key,
    )
    assert first.status_code == 200 and second.status_code == 200, (first.text, second.text)
    assert first.json()["data"] == second.json()["data"], "same key must replay an identical result"
    after = list_my_leave_requests(employee_api_client).json()["data"]["totalCount"]
    assert after == before, "Preview must never create a request row"


@allure.story("Leave requests")
@allure.title("LEAVE-REQ-006 / LEAVE-CANCEL-001 — Submit reserves balance then the owner withdraws it back")
@qa_cases("LEAVE-REQ-006", "LEAVE-CANCEL-001")
@pytest.mark.critical
def test_submit_reserves_then_withdraw(employee_api_client):
    start = date.today() + timedelta(days=9)
    leave_type, evidence = _first_previewable(employee_api_client, start=start, end=start)
    if leave_type is None:
        allure.attach(evidence.text, name="available/preview response", attachment_type=allure.attachment_type.JSON)
        pytest.skip(_NO_POLICY_SKIP)

    submission = submit_leave_request(
        employee_api_client, leaveTypeId=leave_type["id"], startDate=start.isoformat(),
        endDate=start.isoformat(), idempotencyKey=str(uuid.uuid4()),
    )
    if submission.status_code != 201:
        pytest.skip(f"Submit rejected ({submission.status_code}) by a business rule caught only at submit: {submission.text}")
    request_id = submission.json()["data"]["requestId"]
    try:
        assert submission.json()["data"]["status"] == "PendingApproval", submission.text
    finally:
        withdrawn = withdraw_leave_request(employee_api_client, request_id)
        assert withdrawn.status_code == 200, withdrawn.text
        assert withdrawn.json()["data"]["status"] == "Withdrawn", withdrawn.text


@allure.story("Leave requests")
@allure.title("LEAVE-REQ-009 — the same key + different payload is an idempotency conflict")
@qa_cases("LEAVE-REQ-009")
def test_idempotency_conflict_on_payload_mismatch(employee_api_client):
    start = date.today() + timedelta(days=11)
    leave_type, evidence = _first_previewable(employee_api_client, start=start, end=start)
    if leave_type is None:
        allure.attach(evidence.text, name="available/preview response", attachment_type=allure.attachment_type.JSON)
        pytest.skip(_NO_POLICY_SKIP)

    key = str(uuid.uuid4())
    first = submit_leave_request(
        employee_api_client, leaveTypeId=leave_type["id"], startDate=start.isoformat(),
        endDate=start.isoformat(), idempotencyKey=key,
    )
    if first.status_code != 201:
        pytest.skip(f"Submit rejected ({first.status_code}) by a business rule caught only at submit: {first.text}")
    request_id = first.json()["data"]["requestId"]
    try:
        replay = submit_leave_request(
            employee_api_client, leaveTypeId=leave_type["id"], startDate=start.isoformat(),
            endDate=start.isoformat(), idempotencyKey=key,
        )
        assert replay.status_code in (200, 201), replay.text
        assert replay.json()["data"]["requestId"] == request_id, "identical repeat must replay the same request"

        mismatch = submit_leave_request(
            employee_api_client, leaveTypeId=leave_type["id"],
            startDate=(start + timedelta(days=1)).isoformat(), endDate=(start + timedelta(days=1)).isoformat(),
            idempotencyKey=key,
        )
        assert mismatch.status_code == 409, mismatch.text
    finally:
        withdraw_leave_request(employee_api_client, request_id)
