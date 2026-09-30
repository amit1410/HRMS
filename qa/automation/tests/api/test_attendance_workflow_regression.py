"""API regression suite for Module 08 — Regularization & On Duty workflow (authorization + guards).

Automated from qa/08-attendance/cases/07-regularization.yaml (ATT-REG-*), 08-on-duty.yaml (ATT-OD-*)
and 11-manager-scopes.yaml (ATT-SCOPE-009).

A source-verified environment constraint bounds what is exercisable end-to-end (CR-111/CR-112,
reconfirmed against the live seeded RolePermissionMap):
  * The seeded Employee/Manager roles hold neither Attendance.View nor Regularization/OnDuty
    Request/Approve, so every workflow route is 403 for them.
  * The only identity that holds the workflow permissions (TenantAdmin QA admin) is NOT a linked
    Employee, so its self-service submit/list resolve no identity and return 404.
Consequently the submit→approve happy path has no runnable identity in this environment; that is
asserted here as the documented current state (403 / 404), and the full lifecycle is honestly
skipped rather than fabricated. The maker-checker guard is documented via the smoke suite
(ATT-REG-014). Nothing here mutates any request.
"""

from __future__ import annotations

import datetime
import uuid

import allure
import pytest

from core.attendance_api import (
    approve_on_duty,
    approve_regularization,
    get_manager_on_duty,
    get_manager_regularizations,
    list_my_on_duty,
    list_my_regularizations,
    reject_regularization,
    submit_on_duty,
    submit_regularization,
)
from data.test_data import unique_reason
from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Attendance Management"), pytest.mark.api, pytest.mark.regression]

_YESTERDAY = (datetime.date.today() - datetime.timedelta(days=1)).isoformat()
_FUTURE = (datetime.date.today() + datetime.timedelta(days=20)).isoformat()


def _reg_body():
    return dict(
        businessDate=_YESTERDAY, requestType="MissingOutPunch",
        proposedOutAtUtc=f"{_YESTERDAY}T18:00:00Z", reason=unique_reason("reg"),
    )


# --- Employee authorization --------------------------------------------------------------------


@allure.story("Workflow / Authorization")
@allure.title("ATT-SCOPE-009 / CR-112 — the seeded Employee cannot request or list Regularization/On Duty")
@qa_cases("ATT-SCOPE-009", "ATT-REG-001", "ATT-OD-001")
@cr_refs("CR-112")
@pytest.mark.critical
def test_employee_denied_workflow(employee_api_client):
    assert submit_regularization(employee_api_client, **_reg_body()).status_code == 403
    assert list_my_regularizations(employee_api_client, page=1, pageSize=5).status_code == 403
    assert submit_on_duty(employee_api_client, startDate=_FUTURE, endDate=_FUTURE, reason=unique_reason("od")).status_code == 403
    assert list_my_on_duty(employee_api_client, page=1, pageSize=5).status_code == 403


@allure.story("Workflow / Authorization")
@allure.title("ATT-SCOPE-009 / CR-112 — the seeded Manager cannot reach the approval queues or approve")
@qa_cases("ATT-SCOPE-009", "ATT-REG-012", "ATT-OD-010")
@cr_refs("CR-112")
@pytest.mark.critical
def test_manager_denied_workflow(manager_api_client):
    assert get_manager_regularizations(manager_api_client, page=1, pageSize=5).status_code == 403
    assert get_manager_on_duty(manager_api_client, page=1, pageSize=5).status_code == 403
    assert approve_regularization(manager_api_client, str(uuid.uuid4())).status_code == 403
    assert approve_on_duty(manager_api_client, str(uuid.uuid4())).status_code == 403


# --- Admin (authorized but not a linked Employee) ----------------------------------------------


@allure.story("Workflow / Self-service identity")
@allure.title("ATT-REG-001 — an authorized-but-unlinked admin cannot resolve a self-service Regularization submit")
@qa_cases("ATT-REG-001")
def test_admin_submit_regularization_not_linked(admin_api_client):
    # Admin holds Regularization.Request but is not a linked Employee, so identity resolution fails 404.
    response = submit_regularization(admin_api_client, **_reg_body())
    assert response.status_code == 404, response.text
    assert "not linked" in response.text.lower() or "employee" in response.text.lower()


@allure.story("Workflow / Self-service identity")
@allure.title("ATT-OD-001 — an authorized-but-unlinked admin cannot resolve a self-service On Duty submit")
@qa_cases("ATT-OD-001")
def test_admin_submit_on_duty_not_linked(admin_api_client):
    response = submit_on_duty(admin_api_client, startDate=_FUTURE, endDate=_FUTURE, reason=unique_reason("od"))
    assert response.status_code == 404, response.text


@allure.story("Workflow / Manager queue")
@allure.title("ATT-REG-012 — the admin manager Regularization queue is authorized but unlinked (404)")
@qa_cases("ATT-REG-012")
def test_admin_manager_queue_not_linked(admin_api_client):
    # The manager queue resolves the caller's own managed scope from their Employee identity.
    response = get_manager_regularizations(admin_api_client, page=1, pageSize=5)
    assert response.status_code == 404, response.text


# --- Guards / negative -------------------------------------------------------------------------


@allure.story("Workflow / Guards")
@allure.title("ATT-REG-016 — rejecting a Regularization requires a comment; the route rejects a non-admin first")
@qa_cases("ATT-REG-016")
def test_reject_requires_permission(employee_api_client):
    response = reject_regularization(employee_api_client, str(uuid.uuid4()), comments="")
    assert response.status_code == 403, response.text


@allure.story("Workflow / Authorization")
@allure.title("ATT-REG-001 / ATT-OD-001 — anonymous workflow calls are rejected 401")
@qa_cases("ATT-REG-001", "ATT-OD-001")
def test_workflow_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert submit_regularization(anon, **_reg_body()).status_code == 401
    assert list_my_regularizations(anon).status_code == 401


@allure.story("Workflow / Lifecycle")
@allure.title("ATT-REG-002..011 — the submit→approve lifecycle needs a workflow-capable linked identity (honest skip)")
@qa_cases("ATT-REG-002", "ATT-REG-006", "ATT-REG-010")
def test_regularization_lifecycle_needs_capable_identity():
    pytest.skip(
        "No seeded identity can drive the Regularization submit→approve lifecycle in this environment: "
        "the linked Employee/Manager lack Regularization.Request/Approve (CR-112) and the TenantAdmin that "
        "holds them is not a linked Employee (CR-111). Provision a dedicated workflow-capable, linked QA "
        "identity to automate the full lifecycle — not fabricated here."
    )
