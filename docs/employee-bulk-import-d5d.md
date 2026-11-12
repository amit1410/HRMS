# Employee bulk import D5D operations

`EmployeeImportOperation` is the durable queue. Confirmation changes a tenant-owned operation from `Prepared` to `Queued` in the same database save that consumes its confirmation claim. A duplicate or concurrent confirmation cannot enqueue it again. The API returns `202 Accepted` with the operation ID and status URL; it does not launch request-scoped or fire-and-forget work.

The hosted `EmployeeBulkImportWorker` polls active tenant descriptors from the catalog, selects that tenant's shard in a fresh scope, and claims operations with a database concurrency stamp and lease. It processes rows in Excel order. Each employee, audit records, generated code, and successful row outcome commit in the existing onboarding transaction. A crash after that commit is replay-safe because the row is already `Succeeded` and is skipped on recovery.

The worker is disabled unless `EmployeeBulkImportWorker:Enabled` is set to `true`. Suggested deployment settings are:

```text
EmployeeBulkImportWorker__Enabled=true
EmployeeBulkImportWorker__PollIntervalSeconds=10
EmployeeBulkImportWorker__MaxOperationsPerTenantCycle=2
```

It uses one worker loop per application process and sequential work within each tenant. Multiple application instances may run the worker; the database claim is the concurrency authority. The current lease is ten minutes and is renewed before processing each row and immediately before row success is committed. If ownership expires or is lost, the row transaction cannot commit; the next lease owner recovers the in-flight row and revalidates it. Lease timestamps use UTC application clocks, so production hosts must keep clocks synchronized. SQL Server and MySQL provider-level claim behavior still requires acceptance testing.

Status is available at `GET /api/employees/bulk-upload/{operationId}` and requires `Employee.Import`. It returns operation state, aggregate counts, timestamps, sanitized summary, and sanitized row outcomes. It contains no employee personal identifiers or master IDs.

The operation table uses the existing integer state column for `Queued`, so no migration was needed for this state addition. No migration was applied. The worker also requires the existing catalog and tenant-shard configuration, encrypted workbook storage, and externally managed encryption keys to be available to every worker instance. Keep import operation identity records (or replay-protection tombstones) for the configured retention period.
