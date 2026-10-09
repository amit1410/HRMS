"""API smoke suite for Module 18 / Phase 9b — Confirmation & Probation reminder scheduler.

Automated from qa/18-confirmation-probation/cases/02-confirmation-scheduler.yaml. Covers the per-tenant
scheduler settings (read → update → read-back), the execution-log read, and the authorization boundary
(unauthenticated 401, Employee-without-manage 403 on update).

Requires QA_TENANT_A_HOST plus, per test: QA_A_ADMIN_USERNAME/PASSWORD (admin_api_client, holds
Confirmation.SchedulerView/Manage) and QA_A_EMPLOYEE_USERNAME/PASSWORD (employee_api_client, which holds
neither). Each fixture skips on its own if its pair is unset or sign-in fails. The suite only toggles this
tenant's own scheduler settings and never mutates another tenant's data or any employee's confirmation.
"""

from __future__ import annotations

import allure
import pytest

from core.confirmation_api import (
    get_scheduler_settings,
    list_scheduler_executions,
    update_scheduler_settings,
)
from utils.env_utils import require_env

pytestmark = [allure.feature("Confirmation & Probation"), pytest.mark.api]

_SETTINGS = dict(
    enabled=True,
    frequencyMinutes=60,
    defaultTriggerDaysBeforeEnd=30,
    maxAttempts=3,
    claimLeaseMinutes=30,
    resendProcessed=False,
    notifyEmployee=True,
    notifyManager=True,
    notifyHrbp=True,
)


@allure.story("Scheduler settings")
@allure.title("CONF-SCH-001 — scheduler settings are readable and report a configured flag")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_scheduler_settings_are_readable(admin_api_client):
    response = get_scheduler_settings(admin_api_client)
    assert response.status_code == 200, response.text
    body = response.json()["data"]
    assert "isConfigured" in body and isinstance(body["isConfigured"], bool)
    assert "enabled" in body and "frequencyMinutes" in body


@allure.story("Scheduler settings")
@allure.title("CONF-SCH-002 — admin updates scheduler settings and reads them back")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_scheduler_settings_update_round_trips(admin_api_client):
    updated = update_scheduler_settings(admin_api_client, **{**_SETTINGS, "frequencyMinutes": 45, "defaultTriggerDaysBeforeEnd": 15})
    assert updated.status_code == 200, updated.text
    data = updated.json()["data"]
    assert data["isConfigured"] is True
    assert data["frequencyMinutes"] == 45
    assert data["defaultTriggerDaysBeforeEnd"] == 15

    readback = get_scheduler_settings(admin_api_client)
    assert readback.status_code == 200, readback.text
    assert readback.json()["data"]["frequencyMinutes"] == 45

    # Restore a conventional default so the tenant is left tidy for other suites.
    restore = update_scheduler_settings(admin_api_client, **_SETTINGS)
    assert restore.status_code == 200, restore.text


@allure.story("Scheduler settings")
@allure.title("CONF-SCH-003 — invalid frequency is rejected with a validation error")
@pytest.mark.regression
def test_invalid_frequency_is_rejected(admin_api_client):
    response = update_scheduler_settings(admin_api_client, **{**_SETTINGS, "frequencyMinutes": 0})
    assert response.status_code in (400, 422), response.text


@allure.story("Execution log")
@allure.title("CONF-SCH-004 — the reminder execution log is a readable paged result")
@pytest.mark.regression
def test_execution_log_is_readable(admin_api_client):
    response = list_scheduler_executions(admin_api_client, page=1, pageSize=10)
    assert response.status_code == 200, response.text
    paged = response.json()["data"]
    assert "items" in paged and "totalCount" in paged
    assert isinstance(paged["items"], list)


@allure.story("Authorization")
@allure.title("CONF-SCH-AUTHZ-001 — no bearer token is rejected 401 on the scheduler surface")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_unauthenticated_call_is_rejected(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    anonymous_client = api_client_factory(host)
    response = get_scheduler_settings(anonymous_client)
    assert response.status_code == 401, response.text


@allure.story("Authorization")
@allure.title("CONF-SCH-AUTHZ-002 — an Employee (no Confirmation.SchedulerManage) gets 403 updating settings")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_cannot_manage_scheduler(employee_api_client):
    response = update_scheduler_settings(employee_api_client, **_SETTINGS)
    assert response.status_code == 403, response.text
