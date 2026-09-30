"""API regression suite for Module 08 — Attendance Reports & Export.

Automated from qa/08-attendance/cases/12-reports.yaml (ATT-RPT-*).

Reports/exports are reachable only by a caller holding Attendance.Report.View (+ .Report.Export for
export routes); under the seeded matrix that is the TenantAdmin QA admin. The Manager role holds no
Report permission at all (ATT-RPT-012) and a plain Employee holds none either (ATT-RPT-013) — both
are asserted as 403. All reads are non-destructive.
"""

from __future__ import annotations

import csv
import io
import uuid
from datetime import date, timedelta

import allure
import pytest

from core.attendance_api import (
    export_daily_report,
    export_exceptions_report,
    export_monthly_report,
    get_daily_report,
    get_exceptions_report,
    get_monthly_report,
)
from utils.allure_evidence import qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Attendance Management"), pytest.mark.api, pytest.mark.regression]

_FORMULA_PREFIXES = ("=", "+", "-", "@")


# --- Daily report ------------------------------------------------------------------------------


@allure.story("Reports / Daily")
@allure.title("ATT-RPT-001 — the daily report returns a paged result for a date range")
@qa_cases("ATT-RPT-001")
@pytest.mark.critical
def test_daily_report_paged(admin_api_client):
    today = date.today()
    response = get_daily_report(admin_api_client, today.replace(day=1).isoformat(), today.isoformat(), page=1, pageSize=5)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert data["page"] == 1 and {"items", "totalCount", "totalPages"} <= set(data)


@allure.story("Reports / Daily")
@allure.title("ATT-RPT-001 — the daily report requires both dates and rejects an inverted range")
@qa_cases("ATT-RPT-001")
def test_daily_report_date_validation(admin_api_client):
    today = date.today()
    no_dates = admin_api_client.get("/api/attendance/reports/daily")
    assert no_dates.status_code == 400, no_dates.text
    inverted = get_daily_report(admin_api_client, today.isoformat(), today.replace(day=1).isoformat())
    assert inverted.status_code == 400, inverted.text


@allure.story("Reports / Daily")
@allure.title("ATT-RPT-002 — the daily report interactive range is capped at 366 days")
@qa_cases("ATT-RPT-002")
def test_daily_report_range_cap(admin_api_client):
    start = date(2024, 1, 1)
    over = get_daily_report(admin_api_client, start.isoformat(), (start + timedelta(days=400)).isoformat())
    assert over.status_code == 400, over.text


# --- Monthly report ----------------------------------------------------------------------------


@allure.story("Reports / Monthly")
@allure.title("ATT-RPT-003 — the monthly report returns a paged result and rejects an invalid month")
@qa_cases("ATT-RPT-003")
def test_monthly_report(admin_api_client):
    today = date.today()
    ok = get_monthly_report(admin_api_client, today.year, today.month, page=1, pageSize=5)
    assert ok.status_code == 200, ok.text
    assert {"items", "totalCount"} <= set(ok.json()["data"])
    assert get_monthly_report(admin_api_client, today.year, 13).status_code == 400


# --- Export ------------------------------------------------------------------------------------


@allure.story("Reports / Export")
@allure.title("ATT-RPT-009 — the daily CSV export is well-formed, text/csv, and formula-injection hardened")
@qa_cases("ATT-RPT-009")
@pytest.mark.critical
def test_daily_export_csv_hardened(admin_api_client):
    today = date.today()
    response = export_daily_report(admin_api_client, today.replace(day=1).isoformat(), today.isoformat())
    assert response.status_code == 200, response.text
    assert "text/csv" in response.headers.get("Content-Type", ""), response.headers
    rows = list(csv.reader(io.StringIO(response.content.decode("utf-8-sig"))))
    assert rows and len(rows[0]) > 1, "export must have a header row with multiple columns"
    # Any cell that begins with a formula trigger must be neutralised (prefixed), never raw.
    offenders = [
        cell for row in rows for cell in row
        if cell[:1] in _FORMULA_PREFIXES and not cell.startswith(("'", "\t"))
        and not _looks_like_plain_number_or_date(cell)
    ]
    allure.attach("\n".join(offenders) or "(none)", name="unescaped formula-leading cells", attachment_type=allure.attachment_type.TEXT)
    assert not offenders, f"CSV cells starting with a formula char must be escaped: {offenders[:5]}"


def _looks_like_plain_number_or_date(cell: str) -> bool:
    # A leading '-' on a plain negative number / ISO date is not a formula-injection risk.
    body = cell.lstrip("-")
    return body.replace(".", "").replace("/", "").replace(":", "").replace(" ", "").replace("-", "").isdigit()


@allure.story("Reports / Export")
@allure.title("ATT-RPT-007 — an export route requires Report.Export; the Manager/Employee roles get 403")
@qa_cases("ATT-RPT-007", "ATT-RPT-012", "ATT-RPT-013")
@pytest.mark.critical
def test_export_requires_export_permission(employee_api_client, manager_api_client):
    today = date.today()
    for client in (employee_api_client, manager_api_client):
        assert export_daily_report(client, today.replace(day=1).isoformat(), today.isoformat()).status_code == 403
        assert export_monthly_report(client, today.year, today.month).status_code == 403


# --- Permission matrix -------------------------------------------------------------------------


@allure.story("Reports / Authorization")
@allure.title("ATT-RPT-012 — the seeded Manager role cannot reach any report route")
@qa_cases("ATT-RPT-012")
@pytest.mark.critical
def test_manager_denied_all_reports(manager_api_client):
    today = date.today()
    assert get_daily_report(manager_api_client, today.replace(day=1).isoformat(), today.isoformat()).status_code == 403
    assert get_monthly_report(manager_api_client, today.year, today.month).status_code == 403
    assert get_exceptions_report(manager_api_client, today.year, today.month).status_code == 403


@allure.story("Reports / Authorization")
@allure.title("ATT-RPT-013 — an employee without report permission is denied both report types")
@qa_cases("ATT-RPT-013")
def test_employee_denied_reports(employee_api_client):
    today = date.today()
    assert get_daily_report(employee_api_client, today.replace(day=1).isoformat(), today.isoformat()).status_code == 403
    assert get_monthly_report(employee_api_client, today.year, today.month).status_code == 403


@allure.story("Reports / Authorization")
@allure.title("ATT-RPT-014 — the Exceptions report needs View + Export + Exception.View together")
@qa_cases("ATT-RPT-014", "ATT-RPT-004")
def test_exceptions_report_permission_matrix(admin_api_client, manager_api_client):
    today = date.today()
    # Admin holds all three: the read reaches the service (200 with a period, or a clean 404 when the
    # queried period has not been created) — never a 403/500.
    admin_read = get_exceptions_report(admin_api_client, today.year, today.month)
    assert admin_read.status_code in (200, 404), admin_read.text
    # Manager lacks Report.View entirely, so it never even reaches the Exception.View check.
    assert get_exceptions_report(manager_api_client, today.year, today.month).status_code == 403
    assert export_exceptions_report(manager_api_client, today.year, today.month).status_code == 403


@allure.story("Reports / Authorization")
@allure.title("ATT-RPT-011 — an unknown employee filter returns a clean scoped result, never a leak or error")
@qa_cases("ATT-RPT-011")
def test_report_employee_filter_scoped(admin_api_client):
    today = date.today()
    response = get_daily_report(
        admin_api_client, today.replace(day=1).isoformat(), today.isoformat(), employeeId=str(uuid.uuid4())
    )
    assert response.status_code == 200, response.text
    assert response.json()["data"]["totalCount"] == 0


@allure.story("Reports / Authorization")
@allure.title("ATT-RPT — an anonymous report call is rejected 401")
@qa_cases("ATT-RPT-001")
def test_report_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    today = date.today()
    assert get_daily_report(anon, today.replace(day=1).isoformat(), today.isoformat()).status_code == 401
