# Phase 1E executable provider acceptance approval manifest

Status: implementation complete; live execution remains blocked pending explicit operator approval. No SQL Server or MySQL connection was opened during this phase.

## Dedicated entry points

- `HRMS.Tests.ProviderAcceptanceRunnerEntryPoints.SqlServer_WelcomeInvitation_acceptance`
- `HRMS.Tests.ProviderAcceptanceRunnerEntryPoints.MySql_WelcomeInvitation_acceptance`
- `tools/Invoke-Phase1EProviderAcceptance.ps1 -Provider SqlServer`
- `tools/Invoke-Phase1EProviderAcceptance.ps1 -Provider MySql`

The entry points are `[Trait("Category", "Phase1EProviderAcceptance")]` and skip unless `HRMS_PHASE1E_EXECUTE=true` and the selected provider matches. Ordinary `dotnet test` therefore does not execute the live runner. The wrapper is the only documented invocation mechanism and filters to one provider entry point.

## Required process fields

`HRMS_PHASE1E_PROVIDER`, `HRMS_PHASE1E_SERVER_IDENTITY`, `HRMS_PHASE1E_INSTANCE_OR_PORT`, `HRMS_PHASE1E_DATABASE_NAMES` (two exact names: acceptance and rollback), `HRMS_PHASE1E_RUN_ID`, `HRMS_PHASE1E_MANIFEST_PATH` (absolute), `HRMS_PHASE1E_ENVIRONMENT` (`isolated-provider-acceptance`), `HRMS_PHASE1E_TEST_ONLY_CONFIGURATION` (non-secret label), `HRMS_PHASE1E_RESULT_PATH` (absolute), and all four approvals: `HRMS_PHASE1E_APPROVE_CONNECTION`, `HRMS_PHASE1E_APPROVE_CREATE`, `HRMS_PHASE1E_APPROVE_MIGRATION`, `HRMS_PHASE1E_APPROVE_DELETE`.

Credentials are accepted only from `HRMS_PHASE1E_SQLSERVER_CONNECTION` or `HRMS_PHASE1E_MYSQL_CONNECTION`, selected by the exact provider. Legacy variables such as `HRMS_SQLSERVER_TEST_CONNECTION`, development configuration, shared catalogs, `BAD1`, `MH01`, and legacy fixtures are ignored or rejected.

## Approval fields

The durable gate requires the exact provider, server identity, instance/port, approved database allowlist, unique run ID, absolute manifest path, isolated test-only designation, connection approval, creation approval, migration approval, and deletion approval. Credential values are never written to the result artifact or audit messages.

## Proposed targets

SQL Server server/instance, ports, database names, run ID, and manifest path: **unresolved; operator must populate exact approved values**.

MySQL server/instance, port, database names, run ID, and manifest path: **unresolved; operator must populate exact approved values**.

Do not copy values from the Phase 1E-4 example configuration; its values are placeholders and it is not approval authority.

## Lifecycle and guarantees

The runner creates the acceptance database through `DisposableDatabaseHarness`, applies the provider migration, verifies connectivity and the welcome-invitation schema/index model, exercises the invitation processor with a fake account/email delivery, and records a redacted JSON result. A second approved database is created solely for migration-down; rollback is performed last in that isolated lifecycle, then ownership is verified and cleanup is verified by database absence. No existing database is eligible for creation or cleanup. Partial create/migration manifests remain ineligible for automatic cleanup.

Expected duration: unresolved until an approved server is available; measure from the result artifact after the first authorized run.

## Legacy fixture quarantine

Existing provider fixtures that directly create/drop databases or call provider migrations are not modified in this phase. They remain outside the `Phase1EProviderAcceptance` category and are not selected by the dedicated wrapper. The ordinary unit command remains unaffected.
