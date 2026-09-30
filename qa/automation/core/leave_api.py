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


# --- Leave Periods (configuration) ---


@allure.step("List leave periods")
def list_leave_periods(client: ApiClient, **query):
    return client.get("/api/leave-periods", params=query)


@allure.step("Get leave period {period_id}")
def get_leave_period(client: ApiClient, period_id: str):
    return client.get(f"/api/leave-periods/{period_id}")


@allure.step("Create leave period")
def create_leave_period(client: ApiClient, **fields):
    return client.post("/api/leave-periods", json=fields)


# --- Leave Policies (configuration read) ---


@allure.step("List leave policies")
def list_leave_policies(client: ApiClient, **query):
    return client.get("/api/leave-policies", params=query)


@allure.step("Get leave policy {policy_id}")
def get_leave_policy(client: ApiClient, policy_id: str):
    return client.get(f"/api/leave-policies/{policy_id}")


@allure.step("List versions of leave policy {policy_id}")
def get_policy_versions(client: ApiClient, policy_id: str):
    return client.get(f"/api/leave-policies/{policy_id}/versions")


# --- Leave Calendar / HR Dashboard ---


@allure.step("Get leave calendar")
def get_leave_calendar(client: ApiClient, **query):
    return client.get("/api/leave-calendar", params=query)


@allure.step("Get HR leave dashboard summary")
def get_hr_dashboard(client: ApiClient, **query):
    return client.get("/api/leave-dashboard/hr-summary", params=query)


# --- Leave Reports ---


@allure.step("Get leave report {report}")
def get_leave_report(client: ApiClient, report: str, **query):
    return client.get(f"/api/leave-reports/{report}", params=query)


@allure.step("Export leave report {report} as CSV")
def export_leave_report(client: ApiClient, report: str, **query):
    return client.get(f"/api/leave-reports/{report}/export.csv", params=query)


# --- Leave Balance Import (configuration) ---


@allure.step("Download leave-balance import template")
def get_import_template(client: ApiClient):
    return client.get("/api/leave-balances/import/template")


@allure.step("List leave-balance import history")
def get_import_history(client: ApiClient):
    return client.get("/api/leave-balances/import/history")


@allure.step("Get leave-balance import batch {batch_id}")
def get_import_batch(client: ApiClient, batch_id: str):
    return client.get(f"/api/leave-balances/import/{batch_id}")


@allure.step("Validate a leave-balance import (JSON body — contract probe only)")
def validate_import_json(client: ApiClient, body: dict | None = None):
    """Posts a JSON body to the import/validate endpoint. The real endpoint expects a multipart
    file upload, so this helper exists only to assert the wrong-content-type contract (415/4xx) —
    it never uploads a real file and so never creates a persistent import batch."""
    return client.post("/api/leave-balances/import/validate", json=body or {})


# --- Comp-Off (Attendance area, Leave-integrated — Leave_CompOff sheet) ---


@allure.step("Get my comp-off balance")
def get_compoff_balance(client: ApiClient):
    return client.get("/api/attendance/comp-off/balance")


@allure.step("Get my comp-off earnings")
def get_compoff_earnings(client: ApiClient, **query):
    return client.get("/api/attendance/comp-off/earnings", params=query)


@allure.step("Get my comp-off ledger")
def get_compoff_ledger(client: ApiClient, **query):
    return client.get("/api/attendance/comp-off/ledger", params=query)


@allure.step("Query comp-off operations (manager/HR)")
def get_compoff_operations(client: ApiClient, **query):
    return client.get("/api/attendance/comp-off/operations", params=query)


@allure.step("Create a comp-off earning (requires CompOff.Manage)")
def create_compoff_earning(client: ApiClient, **fields):
    return client.post("/api/attendance/comp-off/earnings", json=fields)


@allure.step("Create a comp-off policy (requires CompOff.Manage)")
def create_compoff_policy(client: ApiClient, **fields):
    return client.post("/api/attendance/comp-off/policies", json=fields)
