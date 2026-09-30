"""API regression suite for Module 08 — Attendance Devices & Ingestion.

Automated from qa/08-attendance/cases/13-devices-ingestion.yaml (ATT-DEV-*) and the punch-idempotency
concurrency cases (ATT-CONC-007/009).

All device/mapping writes run as the TenantAdmin QA admin (the only seeded identity holding the
Device.* permissions — ATT-DEV-042), create only QAAUTO-marked devices (disabled on teardown by the
`disposable_device` fixture — Disabled is terminal, ATT-DEV-006) and mappings (deactivated inline),
and import only synthetic, uniquely-keyed punches that resolve to no real attendance day. Two
source-verified findings are pinned deliberately:
  * CR-120 / ATT-DEV-005 — two devices may share a SerialNumber (no uniqueness constraint).
  * CR-117 / ATT-DEV-039 — a replayed external event is durably de-duplicated (receipt idempotency),
    even when the punch itself was rejected for want of a resolvable roster.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.attendance_api import (
    create_device,
    create_device_mapping,
    deactivate_device_mapping,
    get_device,
    get_devices,
    import_punches,
    set_device_status,
    update_device_mapping,
)
from data.test_data import random_suffix
from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Attendance Management"), pytest.mark.api, pytest.mark.regression]


# --- Device CRUD -------------------------------------------------------------------------------


@allure.story("Devices / CRUD")
@allure.title("ATT-DEV-001 — GET devices is paged and a created device round-trips")
@qa_cases("ATT-DEV-001")
@pytest.mark.critical
def test_list_and_get_device(admin_api_client, disposable_device):
    listing = get_devices(admin_api_client, page=1, pageSize=5)
    assert listing.status_code == 200, listing.text
    assert {"items", "totalCount", "totalPages"} <= set(listing.json()["data"])
    fetched = get_device(admin_api_client, disposable_device["id"])
    assert fetched.status_code == 200, fetched.text
    assert fetched.json()["data"]["code"] == disposable_device["code"]


@allure.story("Devices / CRUD")
@allure.title("ATT-DEV-002 — invalid TimeZoneId / ConnectionMode are rejected")
@qa_cases("ATT-DEV-002")
def test_device_invalid_fields_rejected(admin_api_client):
    code = f"QAAUTO-{random_suffix(6)}"
    bad_tz = create_device(
        admin_api_client, code=code, name=code, deviceType="Biometric",
        timeZoneId="Not/A_Zone", connectionMode="Push",
    )
    assert bad_tz.status_code in (400, 422), bad_tz.text
    bad_mode = create_device(
        admin_api_client, code=code + "M", name=code, deviceType="Biometric",
        timeZoneId="UTC", connectionMode="Telepathy",
    )
    assert bad_mode.status_code in (400, 422), bad_mode.text


@allure.story("Devices / CRUD")
@allure.title("ATT-DEV-004 — a duplicate device Code within the tenant is rejected 409")
@qa_cases("ATT-DEV-004")
@pytest.mark.critical
def test_duplicate_device_code_rejected(admin_api_client, disposable_device):
    duplicate = create_device(
        admin_api_client, code=disposable_device["code"], name="dup", deviceType="Biometric",
        timeZoneId="UTC", connectionMode="Push",
    )
    assert duplicate.status_code == 409, duplicate.text


@allure.story("Devices / CRUD")
@allure.title("ATT-DEV-005 / CR-120 — two devices may share an identical SerialNumber (no uniqueness)")
@qa_cases("ATT-DEV-005")
@cr_refs("CR-120")
def test_shared_serial_number_allowed(admin_api_client, disposable_device):
    twin_code = f"QAAUTO-{random_suffix(6)}"
    twin = create_device(
        admin_api_client, code=twin_code, name=twin_code, deviceType="Biometric",
        serialNumber=disposable_device["serialNumber"], timeZoneId="UTC", connectionMode="Push",
    )
    assert twin.status_code in (200, 201), twin.text
    try:
        assert twin.json()["data"]["serialNumber"] == disposable_device["serialNumber"]
    finally:
        set_device_status(admin_api_client, twin.json()["data"]["id"], "disable")


@allure.story("Devices / CRUD")
@allure.title("ATT-DEV-006 — Disabled is a terminal status (re-activate after disable is rejected)")
@qa_cases("ATT-DEV-006")
def test_disabled_is_terminal(admin_api_client, disposable_device):
    disabled = set_device_status(admin_api_client, disposable_device["id"], "disable")
    assert disabled.status_code == 200, disabled.text
    assert disabled.json()["data"]["status"] == "Disabled"
    reactivate = set_device_status(admin_api_client, disposable_device["id"], "activate")
    assert reactivate.status_code in (400, 409), reactivate.text


@allure.story("Devices / CRUD")
@allure.title("ATT-DEV-008 — CredentialReference is never returned in any device DTO")
@qa_cases("ATT-DEV-008")
def test_credential_reference_never_returned(admin_api_client):
    code = f"QAAUTO-{random_suffix(6)}"
    created = create_device(
        admin_api_client, code=code, name=code, deviceType="Biometric",
        timeZoneId="UTC", connectionMode="Pull", credentialReference="super-secret-token",
    )
    assert created.status_code in (200, 201), created.text
    device_id = created.json()["data"]["id"]
    try:
        body = get_device(admin_api_client, device_id).json()["data"]
        assert "credentialReference" not in body, body
        assert "super-secret-token" not in get_device(admin_api_client, device_id).text
    finally:
        set_device_status(admin_api_client, device_id, "disable")


# --- Mappings ----------------------------------------------------------------------------------


@allure.story("Devices / Mappings")
@allure.title("ATT-DEV-010 / ATT-DEV-012 — create a mapping; an overlapping active (device, external id) is Conflict")
@qa_cases("ATT-DEV-010", "ATT-DEV-012")
@pytest.mark.critical
def test_mapping_create_and_overlap_conflict(admin_api_client, disposable_device, created_employee):
    ext = f"EXT-{random_suffix(6)}"
    first = create_device_mapping(
        admin_api_client, deviceId=disposable_device["id"], externalEmployeeIdentifier=ext,
        employeeId=created_employee["id"], effectiveFrom="2026-01-01", effectiveTo=None, status="Active",
    )
    assert first.status_code in (200, 201), first.text
    mapping_id = first.json()["data"]["id"]
    try:
        overlap = create_device_mapping(
            admin_api_client, deviceId=disposable_device["id"], externalEmployeeIdentifier=ext,
            employeeId=created_employee["id"], effectiveFrom="2026-06-01", effectiveTo=None, status="Active",
        )
        assert overlap.status_code == 409, overlap.text
    finally:
        deactivate_device_mapping(admin_api_client, mapping_id)


@allure.story("Devices / Mappings")
@allure.title("ATT-DEV-011 — mapping identity fields cannot be changed via update")
@qa_cases("ATT-DEV-011")
def test_mapping_identity_immutable(admin_api_client, disposable_device, created_employee):
    ext = f"EXT-{random_suffix(6)}"
    created = create_device_mapping(
        admin_api_client, deviceId=disposable_device["id"], externalEmployeeIdentifier=ext,
        employeeId=created_employee["id"], effectiveFrom="2026-01-01", effectiveTo=None, status="Active",
    )
    assert created.status_code in (200, 201), created.text
    mapping_id = created.json()["data"]["id"]
    try:
        response = update_device_mapping(
            admin_api_client, mapping_id, deviceId=disposable_device["id"],
            externalEmployeeIdentifier=f"HACKED-{random_suffix(4)}", employeeId=str(uuid.uuid4()),
            effectiveFrom="2026-01-01", effectiveTo=None, status="Active",
        )
        # The update either rejects the identity change or ignores it — never silently repoints identity.
        assert response.status_code in (200, 400, 409), response.text
        if response.status_code == 200:
            assert response.json()["data"]["externalEmployeeIdentifier"] == ext, response.text
            assert response.json()["data"]["employeeId"] == created_employee["id"], response.text
    finally:
        deactivate_device_mapping(admin_api_client, mapping_id)


# --- Ingestion / idempotency -------------------------------------------------------------------


@allure.story("Devices / Ingestion")
@allure.title("ATT-DEV-039 / CR-117 / ATT-CONC-009 — a replayed external event is durably de-duplicated")
@qa_cases("ATT-DEV-039", "ATT-CONC-009")
@cr_refs("CR-117")
@pytest.mark.critical
def test_import_receipt_idempotency(admin_api_client, disposable_device, created_employee):
    sfx = random_suffix(6)
    ext = f"EXT-{sfx}"
    mapping = create_device_mapping(
        admin_api_client, deviceId=disposable_device["id"], externalEmployeeIdentifier=ext,
        employeeId=created_employee["id"], effectiveFrom="2026-01-01", effectiveTo=None, status="Active",
    )
    mapping_id = mapping.json()["data"]["id"] if mapping.status_code in (200, 201) else None
    try:
        punch = {
            "externalEventId": f"EV-{sfx}", "externalEmployeeIdentifier": ext,
            "occurredAt": "2026-01-15T09:00:00+00:00", "direction": "In",
        }
        first = import_punches(admin_api_client, disposable_device["id"], [punch])
        assert first.status_code == 200, first.text
        assert first.json()["data"]["received"] == 1, first.text

        replay = import_punches(admin_api_client, disposable_device["id"], [punch])
        assert replay.status_code == 200, replay.text
        body = replay.json()["data"]
        assert body["received"] == 1 and body["duplicate"] == 1, body
        assert body["items"][0]["status"] == "Duplicate", body
    finally:
        if mapping_id:
            deactivate_device_mapping(admin_api_client, mapping_id)


@allure.story("Devices / Ingestion")
@allure.title("ATT-DEV-016 — an import batch over the per-call limit is rejected outright")
@qa_cases("ATT-DEV-016")
def test_import_oversized_batch_rejected(admin_api_client, disposable_device):
    punches = [
        {"externalEventId": f"EV-{i}", "externalEmployeeIdentifier": f"EXT-{i}",
         "occurredAt": "2026-01-15T09:00:00+00:00", "direction": "In"}
        for i in range(1001)
    ]
    response = import_punches(admin_api_client, disposable_device["id"], punches)
    assert response.status_code in (400, 413), response.text


@allure.story("Devices / Ingestion")
@allure.title("ATT-DEV-017 / ATT-DEV-019 — an unmapped punch is accounted per-item without aborting the batch")
@qa_cases("ATT-DEV-017", "ATT-DEV-019")
def test_import_unmapped_punch_accounted(admin_api_client, disposable_device):
    punch = {
        "externalEventId": f"EV-{random_suffix(6)}", "externalEmployeeIdentifier": f"UNMAPPED-{random_suffix(6)}",
        "occurredAt": "2026-01-15T09:00:00+00:00", "direction": "In",
    }
    response = import_punches(admin_api_client, disposable_device["id"], [punch])
    assert response.status_code == 200, response.text
    body = response.json()["data"]
    assert body["received"] == 1, body
    assert body["items"][0]["status"] in ("Unmapped", "Rejected"), body


# --- Authorization -----------------------------------------------------------------------------


@allure.story("Devices / Authorization")
@allure.title("ATT-DEV-042 — Employee and Manager roles hold zero device permissions (403 across routes)")
@qa_cases("ATT-DEV-042", "ATT-DEV-041")
@pytest.mark.critical
def test_device_routes_denied_to_non_admins(employee_api_client, manager_api_client):
    routes = [
        "/api/attendance/devices",
        "/api/attendance/devices/mappings",
        "/api/attendance/devices/sync-runs",
        "/api/attendance/devices/issues",
    ]
    for client in (employee_api_client, manager_api_client):
        for route in routes:
            assert client.get(route).status_code == 403, route


@allure.story("Devices / Authorization")
@allure.title("ATT-DEV — an anonymous device call is rejected 401")
@qa_cases("ATT-DEV-001")
def test_device_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert anon.get("/api/attendance/devices").status_code == 401
