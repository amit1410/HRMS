"""Leave module API helpers (Module 07), built on the shared `ApiClient`.

Every function takes an already-authenticated `ApiClient` (see `conftest.py`'s
`admin_api_client` / `employee_api_client` / `manager_api_client` fixtures) and a plain
`dict`/kwargs body, and returns the raw `requests.Response` — assertions stay in the tests, these
are just the wire calls, named after the endpoints they hit (`Backend/HRMS.API/Controllers/
LeaveTypesController.cs`, `LeaveRequestOptionsController.cs`, `LeaveRequestsController.cs`,
`LeaveApprovalsController.cs`, `LeaveBalanceSummaryController.cs`).

Leave is a deliberately restricted MVP (see `qa/07-leave/README.md`, CR-81): a published policy
with half-day/sandwich/attachment/clubbing rules is silently unusable at request time. This module
makes no assumption that any tenant has a compliant, eligible policy configured — callers (tests)
must treat a non-200 from `preview_leave_request`/`submit_leave_request` as "not exercisable in
this environment" and skip, not as a framework bug.
"""

from __future__ import annotations

import allure

from core.api_client import ApiClient


# --- Leave Types (configuration + self-service "available" lookup) ---


@allure.step("List leave types")
def list_leave_types(client: ApiClient, **query):
    return client.get("/api/leave-types", params=query)


@allure.step("Get leave type {leave_type_id}")
def get_leave_type(client: ApiClient, leave_type_id: str):
    return client.get(f"/api/leave-types/{leave_type_id}")


@allure.step("Create leave type")
def create_leave_type(client: ApiClient, **fields):
    return client.post("/api/leave-types", json=fields)


@allure.step("Update leave type {leave_type_id}")
def update_leave_type(client: ApiClient, leave_type_id: str, **fields):
    return client.put(f"/api/leave-types/{leave_type_id}", json=fields)


@allure.step("List available leave types (self-service)")
def get_available_leave_types(client: ApiClient):
    return client.get("/api/leave-types/available")


# --- Leave Requests (self-service: preview/submit/read/withdraw/cancel) ---


@allure.step("Preview a leave request")
def preview_leave_request(client: ApiClient, **fields):
    return client.post("/api/leave-requests/preview", json=fields)


@allure.step("Submit a leave request")
def submit_leave_request(client: ApiClient, **fields):
    return client.post("/api/leave-requests", json=fields)


@allure.step("List my leave requests")
def list_my_leave_requests(client: ApiClient, **query):
    return client.get("/api/leave-requests", params=query)


@allure.step("Get my leave request {request_id}")
def get_my_leave_request(client: ApiClient, request_id: str):
    return client.get(f"/api/leave-requests/{request_id}")


@allure.step("Withdraw leave request {request_id}")
def withdraw_leave_request(client: ApiClient, request_id: str):
    return client.post(f"/api/leave-requests/{request_id}/withdraw")


@allure.step("Cancel leave request {request_id}")
def cancel_leave_request(client: ApiClient, request_id: str):
    return client.post(f"/api/leave-requests/{request_id}/cancel")


# --- Leave Approvals (manager/HR: inbox + approve/reject) ---


@allure.step("Approve leave request {request_id}")
def approve_leave_request(client: ApiClient, request_id: str):
    return client.post(f"/api/leave-requests/{request_id}/approve")


@allure.step("Reject leave request {request_id}")
def reject_leave_request(client: ApiClient, request_id: str):
    return client.post(f"/api/leave-requests/{request_id}/reject")


@allure.step("Get leave approval inbox")
def get_approval_inbox(client: ApiClient, **query):
    return client.get("/api/leave-approvals", params=query)


@allure.step("Get leave approval detail {request_id}")
def get_approval_detail(client: ApiClient, request_id: str):
    return client.get(f"/api/leave-approvals/{request_id}")


# --- Leave Balances (self-service read) ---


@allure.step("Get my leave balances")
def get_my_leave_balances(client: ApiClient):
    return client.get("/api/leave-balances/mine")
