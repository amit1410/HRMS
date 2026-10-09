"""Confirmation & Probation policy API helpers (Module 18 / Phase 9a), built on the shared `ApiClient`.

Every function takes an already-authenticated `ApiClient` (see `conftest.py`'s `admin_api_client` /
`employee_api_client` fixtures) and returns the raw `requests.Response` — assertions stay in the tests.
Named after the endpoints they hit (`Backend/HRMS.API/Controllers/ConfirmationPoliciesController.cs`).
"""

from __future__ import annotations

import allure

from core.api_client import ApiClient

_BASE = "/api/confirmation-policies"
_SCHEDULER = "/api/confirmation-scheduler"


@allure.step("List confirmation policies")
def list_confirmation_policies(client: ApiClient, **query):
    return client.get(_BASE, params=query)


@allure.step("Get confirmation policy {policy_id}")
def get_confirmation_policy(client: ApiClient, policy_id: str):
    return client.get(f"{_BASE}/{policy_id}")


@allure.step("Create confirmation policy")
def create_confirmation_policy(client: ApiClient, **fields):
    return client.post(_BASE, json=fields)


@allure.step("Update confirmation policy {policy_id}")
def update_confirmation_policy(client: ApiClient, policy_id: str, **fields):
    return client.put(f"{_BASE}/{policy_id}", json=fields)


@allure.step("List confirmation policy versions for {policy_id}")
def list_confirmation_policy_versions(client: ApiClient, policy_id: str):
    return client.get(f"{_BASE}/{policy_id}/versions")


@allure.step("Get confirmation policy editor for {policy_id}")
def get_confirmation_policy_editor(client: ApiClient, policy_id: str, **query):
    return client.get(f"{_BASE}/{policy_id}/editor", params=query)


@allure.step("Create confirmation policy version for {policy_id}")
def create_confirmation_policy_version(client: ApiClient, policy_id: str, **body):
    return client.post(f"{_BASE}/{policy_id}/versions", json=body)


@allure.step("Publish confirmation policy version {version_id}")
def publish_confirmation_policy_version(client: ApiClient, policy_id: str, version_id: str):
    return client.post(f"{_BASE}/{policy_id}/versions/{version_id}/publish")


@allure.step("Resolve confirmation policy for employee {employee_id}")
def resolve_confirmation_policy(client: ApiClient, employee_id: str, effective_date: str | None = None):
    return client.post(_BASE + "/resolve", json={"employeeId": employee_id, "effectiveDate": effective_date})


@allure.step("Get employee confirmation status for {employee_id}")
def get_employee_confirmation_status(client: ApiClient, employee_id: str):
    return client.get(f"{_BASE}/employees/{employee_id}/status")


# --- Phase 9b: reminder scheduler configuration + execution log ---
# (Backend/HRMS.API/Controllers/ConfirmationSchedulerController.cs)


@allure.step("Get confirmation scheduler settings")
def get_scheduler_settings(client: ApiClient):
    return client.get(f"{_SCHEDULER}/settings")


@allure.step("Update confirmation scheduler settings")
def update_scheduler_settings(client: ApiClient, **fields):
    return client.put(f"{_SCHEDULER}/settings", json=fields)


@allure.step("List confirmation reminder executions")
def list_scheduler_executions(client: ApiClient, **query):
    return client.get(f"{_SCHEDULER}/executions", params=query)
