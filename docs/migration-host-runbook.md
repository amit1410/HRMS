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

## API deployment and post-deployment verification

Deploy the compatible API release only after migration approval. Production startup defaults to schema validation and fails clearly when required schema is unavailable; it does not migrate or seed. Development/test hosts may explicitly set `Database:StartupMode=Initialize` for disposable stores.

After deployment, obtain a fresh authenticated token through the normal login flow and inspect the policy version. Request separate approval for v1 draft discard, then exercise the authenticated discard endpoint and verify both status and audit history. Resume Stage 4 only after release hashes, migration state, endpoint presence, authorization, tenant isolation, and audit results all pass.

Mutating commands are only the `migrate` command and the explicitly approved API discard operation. Everything under validation and release verification is read-only.
