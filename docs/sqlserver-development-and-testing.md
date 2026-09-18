# SQL Server development and testing

SQL Server validation is opt-in and must use disposable databases. Do not use
`HRMS`, `HRMS_Catalog`, `DEMO01`, `DEMO02`, or a production tenant for an
integration run.

## Supported configuration

The SQL Server acceptance fixtures use these process-scoped variables:

- `HRMS_SQLSERVER_TEST_SERVER`: server/instance address used by the disposable
  acceptance harness
- `HRMS_SQLSERVER_TEST_AUTH`: `Integrated` for the current Windows-auth test
  path
- `HRMS_SQLSERVER_TEST_CONNECTION`: dedicated disposable-database connection
  used by the integration smoke harness
- `Database__CatalogProvider=SqlServer`
- `ConnectionStrings__Catalog`: SQL Server catalog connection
- `Sharding__SqlServerConnectionStringTemplate`: tenant template containing the
  validated `{shardKey}` placeholder, or the approved legacy SQL Server
  fallback

Local SQL Server Express or Developer instances are suitable when the test
principal can create/drop only databases owned by the acceptance run. Local
connections commonly require `TrustServerCertificate=True`; use the approved
instance's authentication policy and never put credentials in source control.

The repository's fixtures fail closed when the required variables are absent.
Skipped SQL Server tests must be reported as environment-blocked, not counted
as provider parity.

## Safe test sequence

1. Confirm the server is reachable and the test principal is authorized.
2. Generate a unique run ID and disposable database names.
3. Validate names against the acceptance harness ownership rules.
4. Apply the current SQL Server catalog and tenant migrations.
5. Seed synthetic tenant-scoped data only.
6. Run connectivity, migration, authorization, Leave, Attendance, query
   translation, concurrency, and tenant-isolation tests.
7. Capture sanitized results without credentials or tokens.
8. Stop clients/contexts and remove only databases owned by that run.

Do not use broad `DROP DATABASE`, wildcard cleanup, production connection
strings, or test fallback to SQLite for a SQL Server claim.

## Provider parity matrix

Compare SQL Server and MySQL business results for equivalent synthetic data:

- tenant routing and cross-tenant denial;
- Account ↔ Employee identity resolution;
- role/page access and organizational scopes;
- Leave balances, requests, approvals, calendars, and dashboards;
- Attendance days, punches, workflows, monthly processing, and reports;
- transaction rollback and concurrency outcomes.

SQL text does not need to match. Authorized populations, state transitions,
counts, and persisted invariants must match.

## Migration rehearsal

Validate both an empty disposable database and an existing disposable database
that contains representative Users, Employees, account links, roles, Leave
records, balances, punches, and Attendance summaries. Before upgrading, record
counts and take a disposable backup. After upgrading, verify counts, tenant
keys, migration history, concurrency columns, indexes, and representative
queries.

## Current environment status

The local repository may have provider-matrix and MySQL coverage without having
a SQL Server runtime. When `HRMS_SQLSERVER_TEST_SERVER`,
`HRMS_SQLSERVER_TEST_CONNECTION`, or the required authentication configuration
is absent, SQL Server runtime parity remains blocked until a disposable SQL
Server environment is supplied.
