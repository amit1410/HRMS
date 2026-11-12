# Controlled migration and release runbook

This workflow is intentionally split into read-only validation, separately approved mutation, and deployment. It has not been run against E2E01.

## Credentials and target gates

Obtain credentials from the approved secret manager or the existing deployment secret mechanism. Do not put them in this repository, command history, logs, or a ticket. Configure `ConnectionStrings:Catalog`, the provider-specific `Sharding` template, and the `MigrationHost:AllowedCatalogs` / `MigrationHost:AllowedTenants` entries. A tenant allowlist entry is exactly `TenantCode:TenantId:ShardKey`.

## Empty-catalog bootstrap limitation

The migration host intentionally requires an existing catalog `Tenants` row to validate the requested tenant
identity. It therefore cannot safely bootstrap a genuinely empty catalog: allowing a missing row would weaken
the existing-database identity gate and could authorize the wrong target. The supported workflow is a separate,
explicitly authorized provisioning/bootstrap operation that creates the catalog schema, creates the initial
tenant identity, and records the resulting catalog/shard values before this host is used. No bootstrap path is
implemented by the migration host, and `migrate` must not be used as a substitute for that operation.

## Read-only validation

Build the host, then run:

```powershell
dotnet run --project Backend/HRMS.MigrationHost -- validate --catalog <authorized-catalog> --tenant-id <guid> --tenant-code <code> --shard <shard-key> --provider MySql
```

This checks catalog and tenant identity, provider, migration history, and pending migrations. It does not create databases, history tables, schema, or seed data. Treat any mismatch as a stop condition.

## Immutable release

Publish the API and migration host to a new, immutable directory; never copy over a running API directory:

```powershell
dotnet publish Backend/HRMS.API/HRMS.API.csproj -c Release -o artifacts/release/api
dotnet publish Backend/HRMS.MigrationHost/HRMS.MigrationHost.csproj -c Release -o artifacts/release/migration-host
dotnet run --project Backend/HRMS.MigrationHost -- verify-release --publish-dir artifacts/release/api
```

The verifier prints SHA-256 hashes and checks the API discard endpoint, both migration assemblies, the discard migration ID, and required dependent assemblies. Record the hashes with the release approval.

## Backup and separately approved migration

Before mutation, the operator must verify a completed backup, retention, encryption, and a tested restore path. Request separate migration approval identifying the exact catalog, tenant, release hashes, and approved migration IDs. Then run:

```powershell
dotnet run --project Backend/HRMS.MigrationHost -- migrate --catalog <authorized-catalog> --tenant-id <guid> --tenant-code <code> --shard <shard-key> --provider MySql --approved-migrations <id1>,<id2> --confirm --backup-verified --restore-ready
```

The host reacquires identity and history after provider locking, applies only the listed chain, reports before/after state, and never seeds or rolls back destructively. A second run with no pending migrations is a no-op. Do not use this command for E2E01 without new authorization.

## WEB1 Employee Import tenant-only plan

The Employee Import schema change is a separate explicit plan. It does not replace or change the existing Welcome Invitation tenant-only plan. Select it with `--tenant-only --tenant-plan employee-import-web1`; do not run the general dual-context `migrate` command for this change. The current host requires the catalog allowlist entry and both tenant allowlist forms: `TenantCode:TenantId:ShardKey` and, for tenant-only execution, `TenantCode:TenantId:ShardKey:Database`.

Before considering execution, a separately authorized read-only preflight must establish all of the following:

- The catalog identity is `hrms_catalog`, and exactly one active WEB1 route has the approved tenant ID, host `web1.localhost`, shard `web1`, and MySQL provider.
- The configured catalog and shard endpoints match the operator-approved endpoint. Runtime identity from both connections must match the expected database (`hrms_catalog` or `hrms_web1`), MySQL version `8.0.46`, hostname, port, and server UUID.
- The prerequisite `ImportBatches`, `Tenants`, `Users`, and EF history objects and keys match the expected baseline. Both Employee Import tables/objects are absent. The `ImportBatches` `(TenantId, Id)` unique key is absent and is expected to be added by the first migration.
- Applied history is a strict prefix of the selected MySQL migration assembly. The complete pending set is exactly, and in this order, `20261028043743_AddEmployeeImportPersistence`, `20261028052420_AddEmployeeImportClaimLease`.
- The reviewed immutable release contains both MySQL migrations, their designers, and the matching model snapshot; record release hashes with the approval.

The operator must record an encrypted backup reference, approved retention, and isolated restore-test reference. Schedule an approved maintenance window, prevent concurrent schema/application writes, and pause/drain Employee Import workers and active import execution. The MigrationHost advisory lock does not pause application workers. `verify-release` checks the published MySQL migration assembly for both Employee Import migration IDs and the model snapshot type; confirm the snapshot’s Employee Import table and lease definitions in the reviewed source inventory, and retain verifier output and SHA-256 hashes with the approval record.

The future command shape is:

```powershell
dotnet run --project Backend/HRMS.MigrationHost -- migrate --tenant-only --tenant-plan employee-import-web1 --catalog hrms_catalog --tenant-id <approved-guid> --tenant-code WEB1 --shard web1 --provider MySql --database hrms_web1 --approved-migrations 20261028043743_AddEmployeeImportPersistence,20261028052420_AddEmployeeImportClaimLease --expected-endpoint-host <approved-endpoint-host> --expected-endpoint-port <approved-endpoint-port> --expected-server-hostname <approved-mysql-hostname> --expected-server-uuid <approved-mysql-server-uuid> --approval <approval-id> --backup-reference <backup-reference> --restore-test-reference <restore-test-reference> --confirm --backup-verified --backup-encrypted --retention-approved --restore-ready --isolated-restore-tested --maintenance-window --workers-paused
```

This is documentation of the future guarded invocation, not authorization to run it. If any identity, history, pending-set, or schema check fails, including a partial migration state—stop and preserve the database for read-only diagnosis. Do not retry blindly and do not run `Down`: the persistence migration drops both Employee Import tables and the `ImportBatches` composite unique constraint. Recovery must follow the approved, restore-tested backup plan because MySQL DDL may commit independently of EF migration transactions.

## WEB1 Employee Code Configuration Audit tenant-only plan

The `employee-code-audit-web1` plan is restricted to catalog `hrms_catalog` for read-only tenant identity verification and tenant WEB1 (`16313e68-2e72-4a6a-b689-79b271cec295`, host `web1.localhost`, shard `web1`, MySQL, database `hrms_web1`). The complete pending tenant set must be exactly `20261030034652_AddEmployeeCodeConfigurationAudit`; the audit table must be absent before execution. Catalog schema and migration history are never inspected or changed, and no seeders run.

Execution requires explicit allowlisted approval, a verified backup, isolated restore evidence, and maintenance-window confirmation. Configure the exact tenant allowlist entry and the approval ID in `MigrationHost:AllowedApprovals`. The guarded command is:

```powershell
dotnet run --project Backend/HRMS.MigrationHost -- migrate --tenant-only --tenant-plan employee-code-audit-web1 --catalog hrms_catalog --tenant-id 16313e68-2e72-4a6a-b689-79b271cec295 --tenant-code WEB1 --shard web1 --provider MySql --database hrms_web1 --approved-migrations 20261030034652_AddEmployeeCodeConfigurationAudit --approval <approved-approval-id> --backup-reference <verified-backup-reference> --restore-test-reference <isolated-restore-reference> --confirm --backup-verified --restore-ready --isolated-restore-tested --maintenance-window
```

This command is documentation only and must be run only after the required approval and evidence are recorded. A repeat after the migration is already applied is a no-op; any other pending-set, tenant, or database mismatch fails closed.

## Employee import recovery audit tenant-only plan

The `employee-import-recovery-audit-web1` plan is restricted to WEB1 and exactly one MySQL migration, `20261104173339_AddEmployeeImportRecoveryAudit`. It creates the append-only recovery audit table and database triggers. The complete pending set must contain only this migration, and the audit table must not already exist.

Execution requires an allowlisted approval, verified backup, isolated restore evidence, and a maintenance window. Configure the exact tenant allowlist and approval ID in `MigrationHost` first. The guarded command shape is:

```powershell
dotnet run --project Backend/HRMS.MigrationHost -- migrate --tenant-only --tenant-plan employee-import-recovery-audit-web1 --catalog hrms_catalog --tenant-id 16313e68-2e72-4a6a-b689-79b271cec295 --tenant-code WEB1 --shard web1 --provider MySql --database hrms_web1 --approved-migrations 20261104173339_AddEmployeeImportRecoveryAudit --approval <allowlisted-approval-id> --backup-reference <verified-backup-reference> --restore-test-reference <isolated-restore-reference> --confirm --backup-verified --backup-encrypted --retention-approved --restore-ready --isolated-restore-tested --maintenance-window
```

This command has not been run against WEB1. It does not access or migrate the catalog schema and performs no seeding. A repeat after the migration is applied is a no-op; any other pending set or identity mismatch fails closed.

## API deployment and post-deployment verification

Deploy the compatible API release only after migration approval. Production startup defaults to schema validation and fails clearly when required schema is unavailable; it does not migrate or seed. Development/test hosts may explicitly set `Database:StartupMode=Initialize` for disposable stores.

After deployment, obtain a fresh authenticated token through the normal login flow and inspect the policy version. Request separate approval for v1 draft discard, then exercise the authenticated discard endpoint and verify both status and audit history. Resume Stage 4 only after release hashes, migration state, endpoint presence, authorization, tenant isolation, and audit results all pass.

Mutating commands are only the `migrate` command and the explicitly approved API discard operation. Everything under validation and release verification is read-only.
