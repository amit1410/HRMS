# Employee import recovery operator runbook

This utility is allowlisted to WEB1 operation `b89d04aa-1419-4c85-a7e3-48ab1d92d111`. It opens the catalog and the resolved tenant shard, but never starts an `IHostedService`. Keep `EmployeeBulkImportWorker:Enabled` false until the recovery is reviewed and the separate worker start is authorized.

## Deployment prerequisites

1. Deploy the application binaries containing the recovery service and the historical initial-onboarding date fix.
2. Apply the matching provider migration to WEB1 only after separate migration approval:
   - MySQL: `20261104173339_AddEmployeeImportRecoveryAudit`
   - SQL Server: `20261104173442_AddEmployeeImportRecoveryAudit`
3. Configure the same catalog, tenant-shard, encrypted workbook storage, and encryption-key settings used by the API. The operator process reads appsettings, Development appsettings, User Secrets, environment variables, then the configured external storage configuration file.
4. Ensure the local operator has approved database access and can identify WEB1's expected provider and shard key. Do not put any secret in command-line arguments.

The migration adds `EmployeeImportRecoveryAudits` with tenant and operation foreign keys, before/after counters, operator, timestamp, reason, outcome, and a sanitized error code. Provider migrations install database triggers that reject audit updates/deletes. The EF context also rejects updates/deletes. `Down` drops the table and triggers; rollback is appropriate only before recovery events have been written because those events are intentionally retained.

## Dry run

From `D:\HRMS`, run the isolated recovery-tool DLL (or the reviewed deployed copy) with the expected shard key from the approved tenant catalog/configuration review. Calling the DLL directly avoids rebuilding or replacing API binaries:

```powershell
dotnet .\artifacts\recovery-build\bin\HRMS.EmployeeImportRecovery\debug\HRMS.EmployeeImportRecovery.dll --operation b89d04aa-1419-4c85-a7e3-48ab1d92d111 --provider MySql --shard web1 --dry-run true
```

The default mode is read-only. It reports sanitized blockers and counter values. It verifies exact tenant routing, operation ID/state, expired lease, row/operation/batch counter agreement, employee links and import audit links, encrypted workbook availability and hash, and current workbook validation. A passing dry run is a prerequisite, not authorization to write.

## Apply after separate owner approval

Only after reviewing a fresh passing dry-run and receiving separate owner approval for live recovery, obtain an approval/change ticket and run:

```powershell
dotnet .\artifacts\recovery-build\bin\HRMS.EmployeeImportRecovery\debug\HRMS.EmployeeImportRecovery.dll --operation b89d04aa-1419-4c85-a7e3-48ab1d92d111 --provider MySql --shard web1 --apply true --operator <DOMAIN-operator> --reason "historical onboarding fix approved" --owner-approval-id <approval-ticket>
```

At the prompt, type exactly:

```text
APPLY WEB1 EMPLOYEE IMPORT RECOVERY b89d04aa-1419-4c85-a7e3-48ab1d92d111 <approval-ticket>
```

The command refuses to proceed if the worker is enabled. It revalidates under a serializable transaction, takes the expired operation lease using the same concurrency token/lease protocol as the worker, requeues Failed and Skipped rows (and abandoned Processing rows without an employee link), resets only failed/skipped counters, retains all Succeeded and Pending rows, reconciles the batch, and commits the append-only audit event atomically. It leaves the operation in Processing under a fresh 10-minute lease; the worker must remain disabled pending a separate start approval. Do not run the command again while that lease is active.

## Rollback and verification

If the apply command reports blocked or failed, do not reset rows manually. The transaction rolls back row, counter, batch, lease, and successful audit changes together. A sanitized Rejected/Failed audit attempt is written separately when the database is available. Inspect only operation counters, row-status aggregates, lease metadata, and recovery audit records through approved read-only access.

After an applied recovery, verify one `Applied` audit entry with the approved operator/ticket reason, counters `Succeeded=0`, `Failed=0`, `Skipped=0`, `Pending=10,000`, and a live recovery lease. Then obtain separate approval before enabling the worker. After processing, verify result aggregates sum to 10,000, no unexpected failure/skip rows remain, and each Succeeded row's employee reference exists. Do not use a new import operation for this recovery.
