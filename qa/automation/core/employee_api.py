"""Employee module API helpers (Module 05), built on the shared `ApiClient`.

Every function takes an already-authenticated `ApiClient` (see `conftest.py`'s
`admin_api_client` fixture — an `ApiClient.with_bearer(token)` bound to the tenant host) and a
plain `dict`/kwargs body, and returns the raw `requests.Response` — assertions stay in the tests,
these are just the wire calls, named after the endpoints they hit
(`Backend/HRMS.API/Controllers/EmployeesController.cs` /
`Backend/HRMS.API/Controllers/EmployeeSubResourcesController.cs`).

Used for setup and cleanup (creating/deleting the disposable employees a test needs) and, in the
API-layer tests, as the system under test itself.
"""

from __future__ import annotations

import allure

from core.api_client import ApiClient


@allure.step("Create employee (personal-details path)")
def create_personal_details(client: ApiClient, **fields):
    return client.post("/api/employees/personal-details", json=fields)


@allure.step("Create employee (legacy full-record path)")
def create_full(client: ApiClient, **fields):
    return client.post("/api/employees", json=fields)


@allure.step("Get employee {employee_id}")
def get_employee(client: ApiClient, employee_id: str):
    return client.get(f"/api/employees/{employee_id}")


@allure.step("List employees")
def list_employees(client: ApiClient, **query):
    return client.get("/api/employees", params=query)


@allure.step("Delete employee {employee_id}")
def delete_employee(client: ApiClient, employee_id: str):
    return client.delete(f"/api/employees/{employee_id}")


@allure.step("Get contact for {employee_id}")
def get_contact(client: ApiClient, employee_id: str):
    return client.get(f"/api/employees/{employee_id}/contact")


@allure.step("Upsert contact for {employee_id}")
def upsert_contact(client: ApiClient, employee_id: str, **fields):
    return client.put(f"/api/employees/{employee_id}/contact", json=fields)


@allure.step("Get addresses for {employee_id}")
def get_addresses(client: ApiClient, employee_id: str):
    return client.get(f"/api/employees/{employee_id}/addresses")


@allure.step("Upsert address for {employee_id}")
def upsert_address(client: ApiClient, employee_id: str, **fields):
    return client.post(f"/api/employees/{employee_id}/addresses", json=fields)


@allure.step("Get bank details for {employee_id}")
def get_bank_details(client: ApiClient, employee_id: str):
    return client.get(f"/api/employees/{employee_id}/bank-details")


@allure.step("Create bank detail for {employee_id}")
def create_bank_detail(client: ApiClient, employee_id: str, **fields):
    return client.post(f"/api/employees/{employee_id}/bank-details", json=fields)


@allure.step("Update bank detail {bank_detail_id} for {employee_id}")
def update_bank_detail(client: ApiClient, employee_id: str, bank_detail_id: str, **fields):
    return client.put(f"/api/employees/{employee_id}/bank-details/{bank_detail_id}", json=fields)


@allure.step("Delete bank detail {bank_detail_id} for {employee_id}")
def delete_bank_detail(client: ApiClient, employee_id: str, bank_detail_id: str):
    return client.delete(f"/api/employees/{employee_id}/bank-details/{bank_detail_id}")


@allure.step("Get bank master list")
def list_banks(client: ApiClient, **query):
    return client.get("/api/master-data/banks", params=query)


@allure.step("Get employment for {employee_id}")
def get_employment(client: ApiClient, employee_id: str):
    return client.get(f"/api/employees/{employee_id}/employment")


@allure.step("Upsert employment for {employee_id}")
def upsert_employment(client: ApiClient, employee_id: str, **fields):
    return client.put(f"/api/employees/{employee_id}/employment", json=fields)


@allure.step("Get supervisor for {employee_id}")
def get_supervisor(client: ApiClient, employee_id: str):
    return client.get(f"/api/employees/{employee_id}/supervisor")


@allure.step("Upsert supervisor for {employee_id}")
def upsert_supervisor(client: ApiClient, employee_id: str, **fields):
    return client.put(f"/api/employees/{employee_id}/supervisor", json=fields)


@allure.step("Get portal account for {employee_id}")
def get_portal_account(client: ApiClient, employee_id: str):
    return client.get(f"/api/employees/{employee_id}/portal-account")
