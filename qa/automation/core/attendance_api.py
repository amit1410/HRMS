"""Attendance module API helpers (Module 08), built on the shared `ApiClient`.

Every function takes an already-authenticated `ApiClient` (see `conftest.py`'s `admin_api_client` /
`employee_api_client` / `manager_api_client` fixtures) and a plain `dict`/kwargs body, and returns
the raw `requests.Response` — assertions stay in the tests, these are just the wire calls, named
after the endpoints they hit (`Backend/HRMS.API/Controllers/AttendanceReadController.cs`,
`AttendanceWorkflowController.cs`, `AttendanceReportController.cs`).

Only wraps the self-service calendar/day-detail reads, the manager team read, the Regularization/
On Duty submit-approve-reject-cancel workflow, and the Daily report — the slice this smoke batch
exercises. Attendance is a far larger module (Foundation/Shift/Roster config, Monthly processing,
Exceptions/Operations, Overtime, Admin Corrections, Devices — see `qa/08-attendance/README.md`);
none of that is wrapped here, deliberately, per the Phase 5 brief's "do not automate the entire
module yet."

Per `qa/08-attendance/README.md` (CR-111/CR-112, confirmed against the current
`SeedData.RolePermissionMap`): no seeded role except SuperAdmin/TenantAdmin holds
`Attendance.RegularizationRequest/Approve` or `Attendance.OnDutyRequest/Approve`, and the seeded
plain `Employee`/`Manager` roles do not hold `Attendance.View` either. Callers (tests) must treat a
403 from any of these wrappers as "not exercisable by this seeded role in this environment" and
skip with that reason cited — not as a framework bug — exactly like `core/leave_api.py`'s CR-81
note for Leave.
"""

from __future__ import annotations

import allure

from core.api_client import ApiClient


# --- Self-service reads (me/*) ---


@allure.step("Get my attendance calendar {year}-{month}")
def get_my_calendar(client: ApiClient, year: int, month: int):
    return client.get("/api/attendance/me/calendar", params={"year": year, "month": month})


@allure.step("Get my attendance day {date}")
def get_my_day(client: ApiClient, date: str):
    return client.get(f"/api/attendance/me/days/{date}")


# --- Manager team reads ---


@allure.step("Get manager team attendance")
def get_manager_team(client: ApiClient, from_date: str, to_date: str, **query):
    return client.get("/api/attendance/manager/team", params={"fromDate": from_date, "toDate": to_date, **query})


@allure.step("Get manager team day {employee_id}/{date}")
def get_manager_day(client: ApiClient, employee_id: str, date: str):
    return client.get(f"/api/attendance/manager/team/{employee_id}/{date}")


# --- Regularization (self-service submit/list/cancel; manager queue/approve/reject) ---


@allure.step("Submit a Regularization request")
def submit_regularization(client: ApiClient, **fields):
    return client.post("/api/attendance/me/regularizations", json=fields)


@allure.step("List my Regularization requests")
def list_my_regularizations(client: ApiClient, **query):
    return client.get("/api/attendance/me/regularizations", params=query)


@allure.step("Get my Regularization request {request_id}")
def get_my_regularization(client: ApiClient, request_id: str):
    return client.get(f"/api/attendance/me/regularizations/{request_id}")


@allure.step("Cancel my Regularization request {request_id}")
def cancel_regularization(client: ApiClient, request_id: str):
    return client.post(f"/api/attendance/me/regularizations/{request_id}/cancel")


@allure.step("Get manager Regularization queue")
def get_manager_regularizations(client: ApiClient, **query):
    return client.get("/api/attendance/manager/regularizations", params=query)


@allure.step("Approve Regularization request {request_id}")
def approve_regularization(client: ApiClient, request_id: str):
    return client.post(f"/api/attendance/manager/regularizations/{request_id}/approve")


@allure.step("Reject Regularization request {request_id}")
def reject_regularization(client: ApiClient, request_id: str, comments: str):
    return client.post(f"/api/attendance/manager/regularizations/{request_id}/reject", json={"comments": comments})


# --- On Duty (self-service submit/list/cancel; manager queue/approve/reject) ---


@allure.step("Submit an On Duty request")
def submit_on_duty(client: ApiClient, **fields):
    return client.post("/api/attendance/me/on-duty", json=fields)


@allure.step("List my On Duty requests")
def list_my_on_duty(client: ApiClient, **query):
    return client.get("/api/attendance/me/on-duty", params=query)


@allure.step("Get my On Duty request {request_id}")
def get_my_on_duty(client: ApiClient, request_id: str):
    return client.get(f"/api/attendance/me/on-duty/{request_id}")


@allure.step("Cancel my On Duty request {request_id}")
def cancel_on_duty(client: ApiClient, request_id: str):
    return client.post(f"/api/attendance/me/on-duty/{request_id}/cancel")


@allure.step("Get manager On Duty queue")
def get_manager_on_duty(client: ApiClient, **query):
    return client.get("/api/attendance/manager/on-duty", params=query)


@allure.step("Approve On Duty request {request_id}")
def approve_on_duty(client: ApiClient, request_id: str):
    return client.post(f"/api/attendance/manager/on-duty/{request_id}/approve")


@allure.step("Reject On Duty request {request_id}")
def reject_on_duty(client: ApiClient, request_id: str, comments: str):
    return client.post(f"/api/attendance/manager/on-duty/{request_id}/reject", json={"comments": comments})


# --- Reports ---


@allure.step("Get Attendance daily report")
def get_daily_report(client: ApiClient, from_date: str, to_date: str, **query):
    return client.get("/api/attendance/reports/daily", params={"fromDate": from_date, "toDate": to_date, **query})
