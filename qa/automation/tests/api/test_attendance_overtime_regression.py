"""API regression suite for Module 08 — Overtime (authorization, snapshot, guards).

Automated from qa/08-attendance/cases/09-overtime.yaml (ATT-OT-*).

Per qa/08-attendance/README.md (CR-114) NO seeded role holds any Attendance.Overtime.* permission
except SuperAdmin/TenantAdmin, and there is no team/self Overtime view endpoint at all (CR-105). So
the Employee/Manager identities are asserted 403 across every overtime route, and the admin reaches
the snapshot read (which returns 409 until an Attendance period is finalized — a documented, not-yet-
runnable precondition, ATT-OT/OvertimeNotFinalized). Overtime policy/request creation is permanent
tenant state that also needs a linked employee + resolved attendance day, so it is exercised only
for its authorization guard, never to create real overtime rows.
"""

from __future__ import annotations

import datetime
import uuid

import allure
import pytest

from core.attendance_api import (
    create_overtime_policy,
    create_overtime_request,
    get_overtime_snapshot,
)
from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Attendance Management"), pytest.mark.api, pytest.mark.regression]

_TODAY = datetime.date.today()


@allure.story("Overtime / Snapshot")
@allure.title("ATT-OT-001 — the admin reaches the overtime snapshot read (409 until a period is finalized)")
@qa_cases("ATT-OT-001")
def test_admin_snapshot_reachable(admin_api_client):
    response = get_overtime_snapshot(admin_api_client, _TODAY.year, _TODAY.month)
    # Reachable (not 403): the read runs and reports the period is not finalized, or returns data.
    assert response.status_code in (200, 404, 409), response.text
    if response.status_code == 409:
        assert "finaliz" in response.text.lower(), response.text


@allure.story("Overtime / Authorization")
@allure.title("ATT-OT / CR-114 — no seeded Employee/Manager holds any Overtime permission (403 across routes)")
@qa_cases("ATT-OT-001", "ATT-OT-002")
@cr_refs("CR-114")
@pytest.mark.critical
def test_overtime_denied_to_non_admins(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        assert get_overtime_snapshot(client, _TODAY.year, _TODAY.month).status_code == 403
        assert create_overtime_request(client, employeeId=str(uuid.uuid4())).status_code == 403
        assert create_overtime_policy(client, name="QAAUTO").status_code == 403


@allure.story("Overtime / Authorization")
@allure.title("ATT-OT — an anonymous overtime snapshot call is rejected 401")
@qa_cases("ATT-OT-001")
def test_overtime_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert get_overtime_snapshot(anon, _TODAY.year, _TODAY.month).status_code == 401


@allure.story("Overtime / Lifecycle")
@allure.title("ATT-OT-003..020 — the request→approve→finalize lifecycle needs seeded overtime data (honest skip)")
@qa_cases("ATT-OT-003", "ATT-OT-010", "ATT-OT-016")
def test_overtime_lifecycle_needs_seeded_data():
    pytest.skip(
        "The Overtime request→approve→finalize lifecycle is not runnable in this environment: it needs a "
        "workflow-capable linked identity plus a resolved, eligible attendance day and a finalizable period, "
        "none of which the seeded QAAUTO fixtures provide (CR-105/CR-114). Not fabricated here."
    )
