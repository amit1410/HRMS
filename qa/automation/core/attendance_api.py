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


@allure.step("Get Attendance monthly report")
def get_monthly_report(client: ApiClient, year: int, month: int, **query):
    return client.get("/api/attendance/reports/monthly", params={"year": year, "month": month, **query})


@allure.step("Get Attendance exceptions report")
def get_exceptions_report(client: ApiClient, year: int, month: int, **query):
    return client.get("/api/attendance/reports/exceptions", params={"year": year, "month": month, **query})


@allure.step("Export Attendance daily report")
def export_daily_report(client: ApiClient, from_date: str, to_date: str, **query):
    return client.get("/api/attendance/reports/daily/export", params={"fromDate": from_date, "toDate": to_date, **query})


@allure.step("Export Attendance monthly report")
def export_monthly_report(client: ApiClient, year: int, month: int, **query):
    return client.get("/api/attendance/reports/monthly/export", params={"year": year, "month": month, **query})


@allure.step("Export Attendance exceptions report")
def export_exceptions_report(client: ApiClient, year: int, month: int, **query):
    return client.get("/api/attendance/reports/exceptions/export", params={"year": year, "month": month, **query})


# --- Foundation / Configuration: Shifts ---


@allure.step("List Shifts")
def get_shifts(client: ApiClient, **query):
    return client.get("/api/attendance/shifts", params=query)


@allure.step("Create a Shift")
def create_shift(client: ApiClient, **fields):
    return client.post("/api/attendance/shifts", json=fields)


@allure.step("Update Shift {shift_id}")
def update_shift(client: ApiClient, shift_id: str, **fields):
    return client.put(f"/api/attendance/shifts/{shift_id}", json=fields)


# --- Foundation / Configuration: Shift Patterns ---


@allure.step("List Shift Patterns")
def get_patterns(client: ApiClient):
    return client.get("/api/attendance/patterns")


@allure.step("Create a Shift Pattern")
def create_pattern(client: ApiClient, **fields):
    return client.post("/api/attendance/patterns", json=fields)


# --- Foundation / Configuration: Applicability rules ---


@allure.step("Create an Applicability rule (POST /applicability)")
def add_applicability(client: ApiClient, **fields):
    return client.post("/api/attendance/applicability", json=fields)


@allure.step("List Applicability rules (GET /shift-applicability)")
def get_applicability(client: ApiClient, **query):
    return client.get("/api/attendance/shift-applicability", params=query)


@allure.step("Get Applicability rule {rule_id}")
def get_applicability_by_id(client: ApiClient, rule_id: str):
    return client.get(f"/api/attendance/shift-applicability/{rule_id}")


@allure.step("Update Applicability rule {rule_id}")
def update_applicability(client: ApiClient, rule_id: str, **fields):
    return client.put(f"/api/attendance/shift-applicability/{rule_id}", json=fields)


@allure.step("Delete Applicability rule {rule_id}")
def delete_applicability(client: ApiClient, rule_id: str):
    return client.delete(f"/api/attendance/shift-applicability/{rule_id}")


# --- Foundation / Configuration: Roster ---


@allure.step("Resolve roster for employee {employee_id} on {date}")
def resolve_roster(client: ApiClient, employee_id: str, date: str):
    return client.get(f"/api/attendance/employees/{employee_id}/roster/{date}")


@allure.step("Assign roster")
def assign_roster(client: ApiClient, **fields):
    return client.post("/api/attendance/roster/assign", json=fields)


@allure.step("Remove roster override for {employee_id} on {date}")
def remove_roster(client: ApiClient, employee_id: str, date: str):
    return client.delete(f"/api/attendance/roster/{employee_id}/{date}")


@allure.step("Query roster grid")
def get_roster(client: ApiClient, **query):
    return client.get("/api/attendance/roster", params=query)


@allure.step("Download roster upload template")
def get_roster_template(client: ApiClient):
    return client.get("/api/attendance/roster/template")


@allure.step("Validate a roster upload (multipart CSV)")
def validate_roster_upload(client: ApiClient, filename: str, content: bytes):
    return client.post_multipart("/api/attendance/roster/upload/validate", files={"file": (filename, content, "text/csv")})


# --- Monthly processing / Periods ---


@allure.step("List Attendance periods")
def get_periods(client: ApiClient, **query):
    return client.get("/api/attendance/periods", params=query)


@allure.step("Get Attendance period {period_id}")
def get_period(client: ApiClient, period_id: str):
    return client.get(f"/api/attendance/periods/{period_id}")


@allure.step("Create an Attendance period {year}-{month}")
def create_period(client: ApiClient, year: int, month: int):
    return client.post("/api/attendance/periods", json={"year": year, "month": month})


@allure.step("Process Attendance period {period_id}")
def process_period(client: ApiClient, period_id: str):
    return client.post(f"/api/attendance/periods/{period_id}/process")


@allure.step("Close-preview Attendance period {period_id}")
def close_preview(client: ApiClient, period_id: str):
    return client.get(f"/api/attendance/periods/{period_id}/close-preview")


@allure.step("Close Attendance period {period_id}")
def close_period(client: ApiClient, period_id: str):
    return client.post(f"/api/attendance/periods/{period_id}/close")


@allure.step("Reopen Attendance period {period_id}")
def reopen_period(client: ApiClient, period_id: str, reason: str | None = None):
    return client.post(f"/api/attendance/periods/{period_id}/reopen", json={"reason": reason})


@allure.step("Get period {period_id} events")
def get_period_events(client: ApiClient, period_id: str):
    return client.get(f"/api/attendance/periods/{period_id}/events")


@allure.step("Get my monthly summary {year}-{month}")
def get_my_monthly_summary(client: ApiClient, year: int, month: int):
    return client.get("/api/attendance/my/monthly-summary", params={"year": year, "month": month})


# --- Operations / Exceptions ---


@allure.step("List operations exceptions")
def get_operations_exceptions(client: ApiClient, **query):
    return client.get("/api/attendance/operations/exceptions", params=query)


@allure.step("Get operations dashboard")
def get_operations_dashboard(client: ApiClient, **query):
    return client.get("/api/attendance/operations/dashboard", params=query)


@allure.step("Bulk approve/reject exceptions")
def operations_bulk(client: ApiClient, **fields):
    return client.post("/api/attendance/operations/bulk", json=fields)


@allure.step("Resolve an exception")
def resolve_exception(client: ApiClient, **fields):
    return client.post("/api/attendance/operations/exceptions/resolve", json=fields)


@allure.step("Manual attendance correction (maker-checker)")
def operations_manual(client: ApiClient, **fields):
    return client.post("/api/attendance/operations/manual", json=fields)


@allure.step("Bulk admin corrections")
def operations_bulk_corrections(client: ApiClient, **fields):
    return client.post("/api/attendance/operations/bulk-corrections", json=fields)


@allure.step("List my operational exceptions")
def get_my_exceptions(client: ApiClient, **query):
    return client.get("/api/attendance/me/exceptions", params=query)


# --- Admin corrections ---


@allure.step("List admin corrections")
def get_admin_corrections(client: ApiClient, **query):
    return client.get("/api/attendance/admin-corrections", params=query)


@allure.step("Get admin correction {correction_id}")
def get_admin_correction(client: ApiClient, correction_id: str):
    return client.get(f"/api/attendance/admin-corrections/{correction_id}")


@allure.step("Create an admin correction")
def create_admin_correction(client: ApiClient, **fields):
    return client.post("/api/attendance/admin-corrections", json=fields)


# --- Devices / Ingestion ---


@allure.step("List devices")
def get_devices(client: ApiClient, **query):
    return client.get("/api/attendance/devices", params=query)


@allure.step("Get device {device_id}")
def get_device(client: ApiClient, device_id: str):
    return client.get(f"/api/attendance/devices/{device_id}")


@allure.step("Create a device")
def create_device(client: ApiClient, **fields):
    return client.post("/api/attendance/devices", json=fields)


@allure.step("Update device {device_id}")
def update_device(client: ApiClient, device_id: str, **fields):
    return client.put(f"/api/attendance/devices/{device_id}", json=fields)


@allure.step("Set device {device_id} status {action}")
def set_device_status(client: ApiClient, device_id: str, action: str):
    return client.post(f"/api/attendance/devices/{device_id}/{action}")


@allure.step("List device mappings")
def get_device_mappings(client: ApiClient, **query):
    return client.get("/api/attendance/devices/mappings", params=query)


@allure.step("Create a device mapping")
def create_device_mapping(client: ApiClient, **fields):
    return client.post("/api/attendance/devices/mappings", json=fields)


@allure.step("Update device mapping {mapping_id}")
def update_device_mapping(client: ApiClient, mapping_id: str, **fields):
    return client.put(f"/api/attendance/devices/mappings/{mapping_id}", json=fields)


@allure.step("Deactivate device mapping {mapping_id}")
def deactivate_device_mapping(client: ApiClient, mapping_id: str):
    return client.post(f"/api/attendance/devices/mappings/{mapping_id}/deactivate")


@allure.step("Import punches into device {device_id}")
def import_punches(client: ApiClient, device_id: str, punches: list, checkpoint: str | None = None):
    return client.post(f"/api/attendance/devices/{device_id}/import", json={"punches": punches, "checkpoint": checkpoint})


@allure.step("List device sync runs")
def get_device_sync_runs(client: ApiClient, **query):
    return client.get("/api/attendance/devices/sync-runs", params=query)


@allure.step("List device audit history")
def get_device_history(client: ApiClient, **query):
    return client.get("/api/attendance/devices/history", params=query)


@allure.step("List device ingestion issues")
def get_device_issues(client: ApiClient, **query):
    return client.get("/api/attendance/devices/issues", params=query)


# --- Overtime ---


@allure.step("Get overtime snapshot {year}-{month}")
def get_overtime_snapshot(client: ApiClient, year: int, month: int):
    return client.get("/api/attendance/overtime/snapshot", params={"year": year, "month": month})


@allure.step("Create an overtime policy")
def create_overtime_policy(client: ApiClient, **fields):
    return client.post("/api/attendance/overtime/policies", json=fields)


@allure.step("Create an overtime request")
def create_overtime_request(client: ApiClient, **fields):
    return client.post("/api/attendance/overtime/requests", json=fields)
