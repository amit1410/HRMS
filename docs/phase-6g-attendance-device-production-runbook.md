# Phase 6G Attendance Device Production Runbook

Status: operational readiness guidance for the completed Phase 6G foundation.

This document describes repository behavior and safe operator procedures. It
contains no credentials, connection strings, or device secrets.

## Purpose and architecture

The production flow is:

```text
External device/vendor
  -> vendor-neutral punch provider
  -> Pull worker or protected Sync Now operation
  -> durable TenantId + DeviceId lease
  -> SyncRun
  -> normalized ingestion
  -> AttendancePunch
  -> daily Attendance processor
  -> Leave / On Duty / Regularization controls
  -> monthly Attendance finalization
  -> immutable Payroll Attendance Snapshot
  -> Payroll
```

`AttendancePunch` is the authoritative raw punch. There is no second
Attendance engine, no direct device-to-Payroll path, and no automatic reopen of
a finalized Attendance period. A late finalized-period event is retained as a
traceable issue for the controlled correction/reopen lifecycle.

Phase 6G supplies the Pull worker, durable lease/recovery state, bounded retry,
stale-run recovery, and operational status. It does not require a concrete
biometric-vendor adapter in the core domain; providers implement the existing
vendor-neutral `IAttendancePunchSource` contract.

## Prerequisites

Before enabling device polling:

1. Select the actual database provider with `Database:Provider` (or
   `Database__Provider`): `SqlServer`, `MySql`, or the supported development
   `Sqlite` fallback.
2. Configure the matching catalog and tenant connection settings using the
   existing names in [configuration-and-secrets.md](configuration-and-secrets.md).
3. Apply the provider's current migrations with the approved release process.
4. Confirm the application account can connect to the catalog and tenant
   databases.
5. Configure the provider implementation and credential reference for each
   Pull device.
6. Create effective employee mappings before expecting punches to become
   authoritative AttendancePunch rows.

Do not enable demo seeding in production. Take the normal approved backups and
record the migration/change identifier before deployment.

## Configuration

The section is `AttendanceDeviceWorker`. Configuration is bound during
application startup and invalid values fail startup rather than being silently
normalized.

| Option | Unit | Default | Validation / meaning |
| --- | --- | ---: | --- |
| `Enabled` | Boolean | `false` | `false` disables background Pull polling and reports Disabled in device health. |
| `PollingIntervalSeconds` | Seconds | `60` | Must be positive. Controls the hosted worker cycle interval. |
| `DevicePageSize` | Devices | `50` | Must be 1–200. Server-side Pull-device page size. |
| `MaxDevicesPerCycle` | Devices | `100` | Must be positive. Per-tenant cycle bound. |
| `BatchSize` | Punches | `1000` | Must be positive. Normalized ingestion batch bound. |
| `LeaseDurationSeconds` | Seconds | `120` | Must be positive. Durable same-device lease lifetime. |
| `HeartbeatIntervalSeconds` | Seconds | `30` | Must be positive and strictly less than lease duration. |
| `MaxRetries` | Attempts after first | `2` | Must be non-negative. Retry count is finite. |
| `RetryBaseDelaySeconds` | Seconds | `5` | Must be positive when retries are enabled. |
| `RetryMaxDelaySeconds` | Seconds | `60` | Must not be less than the base delay. |
| `StaleRunThresholdSeconds` | Seconds | `300` | Must be positive. Age threshold for stale Running-run recovery. |

There is no `MaxConcurrentDevices` option in the current implementation. The
worker processes devices sequentially within each bounded tenant cycle.

Environment variables use the normal ASP.NET Core double-underscore form, for
example `AttendanceDeviceWorker__Enabled=true` and
`AttendanceDeviceWorker__PollingIntervalSeconds=60`.

When `Enabled=false`, no background Pull polling occurs. The protected manual
`POST /api/attendance/devices/{id}/sync` operation remains available according
to its normal permission and device/provider rules; it uses the same durable
lease as the worker.

## Lease and worker lifecycle

The lease identity is `(TenantId, DeviceId)`. Claim is atomic and durable, so
two API/worker instances may discover the same device but only one can own the
active lease. The owner receives a lease token. Heartbeat, release, ingestion
commit, checkpoint movement, and SyncRun completion require the current token.

Leases expire after `LeaseDurationSeconds`. A later worker may reclaim an
expired lease. A stale owner cannot heartbeat, release, complete the old run,
write a checkpoint, or commit ingestion after reclaim. Manual Sync Now and the
background worker share this same lease path.

Do not delete or edit a lease merely because a sync appears slow. Inspect the
lease expiry, SyncRun state, and health endpoint first. A process crash is
recovered by expiry and stale-run reconciliation; it does not require manual
database cleanup.

The worker selects active Pull devices server-side, applies retry eligibility,
orders tenant discovery deterministically by tenant code and ID, and processes
bounded pages. Push and File Import devices are not polled by this worker.

## Retry and recovery

Transient provider, timeout, HTTP connectivity, and transient persistence
failures use bounded exponential backoff. Permanent/configuration failures,
unsupported providers, inactive devices, and lease contention are not retried
indefinitely. `MaxRetries` is the number of retries after the initial attempt.

`NextRetryAtUtc` is persisted and is part of server-side worker eligibility;
devices whose retry time is in the future are not selected. Retry exhaustion
leaves the run failed, does not falsely advance a checkpoint, and settles the
lease safely so another cycle can make a later attempt.

On restart, an expired lease can be reclaimed. A stale Running SyncRun is
reconciled to the repository's failed/recovered lifecycle, then a new run can
replay overlapping provider events. Phase 6F idempotency prevents duplicate
AttendancePunch rows. Operators must not delete SyncRun, AttendancePunch, or
checkpoint rows to force recovery.

Unmapped-only batches retain their issues and do not incorrectly advance the
checkpoint. Checkpoints only move for a valid current owner and never move
backward.

## Device and mapping operations

Using the authorized device operations API/UI:

1. Create the device with its code, name, device type, vendor/provider key,
   timezone, connection mode, and a safe credential reference.
2. Create an effective employee mapping for each external identity.
3. Verify the mapping date range and active status.
4. Activate the device only after provider configuration is valid.
5. Use Sync Now for a controlled first run when appropriate.
6. Confirm the SyncRun, AttendancePunch, daily Attendance, checkpoint, and
   released lease before enabling continuous Pull processing.

The API routes are under `/api/attendance/devices`. Device administration,
mapping, Sync Now, import, history, issue, reprocess, and health operations are
permission protected. Credential values are never returned by the DTOs or
health response.

## Health, readiness, and status

Use the probes as follows:

- `/health` is anonymous process liveness. It does not query a database or
  physically contact devices.
- `/ready` is anonymous catalog readiness. It checks catalog connectivity and
  returns `503 NotReady` when the catalog cannot be reached; it does not scan
  tenant databases.
- `GET /api/attendance/devices/health` is the authorized, tenant-scoped device
  diagnostic. It reports worker enabled/disabled state, active devices, active
  and stale leases, stale Running SyncRuns, and recent failures.
- Device list and SyncRun endpoints expose the safe last-attempt, last-success,
  failure, retry, and run-history information available in their contracts.

A single unavailable biometric device should not make API liveness fail. The
generic health probes do not make physical device calls.

## Deployment sequence

1. Prepare the normal release and verified backup/change record.
2. Confirm provider selection and catalog/tenant connection configuration.
3. Deploy the application artifact using the repository's normal service
   manager procedure. The existing startup database initializer applies EF
   migrations for SQL Server and MySQL; do not use the SQLite development
   fallback for production.
4. Verify the target database and migration history with the approved
   deployment identity.
5. Configure `AttendanceDeviceWorker` options and provider credential
   references outside Git.
6. Start the API with the worker disabled if a staged rollout is required.
7. Confirm `/health` and `/ready`, then perform an authorized tenant smoke test.
8. Confirm the Phase 6G migration chain is current for the selected provider.
9. Enable/start the worker and confirm device health reports Enabled.
10. Verify one controlled Pull SyncRun reaches Succeeded.
11. Verify the expected AttendancePunch, daily Attendance, checkpoint, and
    released lease.
12. Monitor unmapped issues, retry times, failures, stale leases, and stale
    runs during the rollout window.

## Migration checklist

SQL Server tenant migrations, in order, include:

- `20260926161306_AddAttendanceDeviceSyncLeasePhase6G`
- `20260926163426_AddAttendanceDeviceSyncRecoveryPhase6G`

MySQL tenant migrations, in order, include:

- `20260926161328_AddAttendanceDeviceSyncLeasePhase6G`
- `20260926163439_AddAttendanceDeviceSyncRecoveryPhase6G`

Use the provider-specific migration assembly selected by the release. Never
apply the MySQL migration assembly to SQL Server, never edit committed
historical migrations, and verify that the pending model check is clean after
deployment. The current verified state is SQL Server `None`; a fresh MySQL
pending-model check remains an operator/environment prerequisite.

## Monitoring and troubleshooting

Search structured logs and SyncRun history by safe identifiers such as tenant
ID, device ID, SyncRun ID, provider, attempt number, status, and timestamps.
Never search for or record lease tokens, credential values, secret headers, or
raw sensitive payloads.

### Device never selected

Check worker `Enabled`, device Active status, Pull connection mode, provider
registration, effective mapping, `NextRetryAtUtc`, and whether another valid
lease owns the device. Confirm the device belongs to the expected tenant.

### Already running or lease conflict

Inspect the active lease expiry and current SyncRun. Wait for valid completion
or expiry/recovery. Do not delete a valid lease.

### Repeated transient failure

Check provider/network availability, attempt count, last failure code, and
`NextRetryAtUtc`. Confirm healthy devices continue independently.

### Permanent/configuration failure

Check provider key, device connection mode, safe credential reference, timezone,
and device configuration. Correct the device, then use the supported Sync Now
or the next eligible worker cycle.

### Unmapped punches

Inspect the external identity and effective mapping dates. Correct the mapping,
then use the supported issue reprocess operation. Do not insert an
AttendancePunch directly.

### Late punch for a finalized period

Expect no automatic reopen and no silent finalized-state mutation. Use the
controlled Attendance correction/reopen process and preserve historical Payroll
bindings.

### Stale run after crash

Allow lease expiry and recovery logic to reconcile the stale run. Verify the
next run succeeds and that replay did not create duplicate AttendancePunch rows.

### Checkpoint not moving

Check for an unmapped-only batch, failed/partial run, stale owner token,
provider result, and checkpoint monotonicity. Do not edit checkpoint data
directly as routine support.

## Safe restart and emergency disable

For a planned restart:

1. Set `AttendanceDeviceWorker__Enabled=false` if polling must be paused.
2. Restart/reload through the actual service manager.
3. Confirm device health reports Disabled and `/health` remains Healthy.
4. Do not clear active leases manually.
5. Start the application and allow expired leases and stale runs to recover.
6. Re-enable the worker only after `/ready` and tenant health are normal.
7. Verify the next successful SyncRun, checkpoint, lease release, and zero
   duplicate AttendancePunch rows.

Disabling the worker is preferred to deleting data. Manual Sync Now remains a
separately authorized operation according to the current API behavior.

## Rollback

If migration has not started, keep the previous application artifact running.
If an additive Phase 6G migration has succeeded but the application is
unhealthy, roll back the application artifact/configuration first; do not
automatically run destructive `Down()` migrations. Consult the database owner
for a restore or forward-fix decision if corruption is suspected.

Preserve AttendancePunch, SyncRun, checkpoint, issue, lease history, and audit
data. After any rollback, verify `/health`, `/ready`, tenant routing,
authentication, tenant isolation, and representative Attendance/device reads.

## Production smoke test

For one controlled active Pull device, record:

- device discovered: yes
- lease claimed: yes
- provider invoked: expected count
- SyncRun: Succeeded
- expected raw AttendancePunch: present
- duplicate raw AttendancePunch: 0
- daily Attendance: updated
- checkpoint: advanced correctly
- lease after completion: released
- device health: normal
- credentials and lease token visible: no

## Multi-instance behavior

Multiple API/worker instances are supported by the durable relational lease.
Two instances may discover the same device, but only one may own the valid
TenantId + DeviceId lease. The other instance skips or reports contention. An
in-memory lock is not authoritative and must not be used as an operational
substitute.

## Verified scale and provider evidence

These are acceptance observations, not published capacity limits:

- Worker scale: 10 tenants, 1,000 devices, 700 active Pull devices, page size
  50, sequential bounded processing, zero unbounded tasks, zero starved
  tenants, and zero worker-created leaked leases.
- Phase 6F ingestion scale: 10,000 employees and 50,000 inbound events.
- MySQL Phase 6G provider acceptance: 18/18 shared scenarios passed.
- SQL Server Phase 6G provider acceptance: 18/18 shared scenarios passed.

## Known limitations and readiness checklist

- Architecture: complete; AttendancePunch remains authoritative.
- Lease safety: complete; durable atomic claim and stale-owner denial verified.
- Retry/recovery: complete; bounded retry and stale-run recovery verified.
- Tenant isolation: complete in the acceptance evidence.
- Provider parity: complete in retained 18/18 provider evidence.
- Finalized-period safety: complete; no automatic reopen verified.
- Scale: complete for the documented acceptance fixtures, not a capacity SLA.
- Health: complete for internal status; physical devices are not probed by
  generic liveness.
- Secrets: complete; credentials and lease tokens are not exposed in status,
  logs, or this runbook.
- Migrations: complete for the listed Phase 6G chains; deployment still
  requires provider-specific verification.
- Deployment/rollback: documented above and subject to the normal release
  approval process.
- Dedicated metrics platform: not implemented; use structured operational
  records, SyncRun history, and health/status endpoints.
- Worker concurrency: intentionally sequential in the current implementation.
- Concrete vendor adapter: not required by the Phase 6G foundation; configure
  an approved implementation of the existing provider contract before use.

Final full regression, fresh MySQL pending-model verification, commit, and push
are separate closure gates and are not claimed by this runbook increment.
