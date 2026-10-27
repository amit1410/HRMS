# Welcome Invitation HTTP 500: captured SQL evidence

Observed request: `GET /api/communication/welcome-invitations?page=1&pageSize=25`.

The existing running API logs already contain the failed executed database command,
the provider error number, the service stage, and the trace correlation ID. No
fallback DbCommandInterceptor is required for this incident.

## Evidence

Source: `Backend/HRMS.API/logs/hrms-20261018.log`.

- Line 3856, 2026-10-18 10:55:24.792 +05:30: EF `Failed executing DbCommand`.
- Line 3901, 10:55:24.832: service diagnostic records stage `Portal account enrichment`,
  exception type `MySql.Data.MySqlClient.MySqlException`, MySQL error number **1064**,
  and a trace correlation ID (redacted).
- Lines 3932–3934: exception stack passes through `RunStageAsync`, `BuildRowsAsync`,
  and `WelcomeInvitationService.ListAsync`.
- Line 4005, 10:55:24.897: the exact paginated GET completes with HTTP 500.

Executed SQL (copied from the failed command event; no parameter values included):

```sql
SELECT `h`.`Id`, `h`.`CreatedDate`, `h`.`Email`, `h`.`FirstName`, `h`.`IsActive`, `h`.`LastLoginDate`, `h`.`LastName`, `h`.`ModifiedDate`, `h`.`PasswordHash`, `h`.`TenantId`
FROM (
    SELECT u.* FROM `Users` AS u WHERE u.`Id` IN ()
) AS `h`
WHERE `h`.`TenantId` = @ef_filter__TenantId
```

The tenant parameter is a GUID and is masked as `?` in the EF event. Column names
above are schema identifiers, not employee data or credential values. The MySQL
exception reports a SQL syntax failure. The invalid empty `IN ()` occurs in the
user enrichment query. In the current source, an empty user ID collection produces
an empty string; the raw SQL builder only substitutes `NULL` for a null string.
This explains the captured statement. No query or LINQ fix was made in this task.

## Existing diagnostics and privacy limits

`Program.cs` connects Microsoft logging to Serilog, with console and rolling file
sinks configured by appsettings. The base EF command threshold is Warning; the
Development override is Information. Failed commands are errors and are therefore
eligible at either threshold. Runtime logs confirm EF command logging is active.
The existing `WelcomeInvitationService.LogFailure` also records the stage, exception
type, nested provider Number, Activity trace ID, and redacted `ToQueryString()` text.
The executed-command event above is the authority for the exact failed SQL;
`ToQueryString()` is supplementary and includes a generated SET statement.

No `EnableSensitiveDataLogging` usage was found in backend source, and the observed
command event masks its tenant parameter. Global sensitive-data logging was not
enabled. The existing service sink does not attach the exception object. Added tests
exercise both raw GUID SQL literals and provider-generated parameter declarations,
check formatted and structured log fields for tenant/employee IDs, personal data,
parameter values and credentials, and verify nested error-number and trace handling.

Existing general EF and middleware exception logs include provider exception messages;
those messages can contain data echoed by MySQL, even with sensitive-data logging off.
The existing service redaction is not a general SQL sanitizer for arbitrary SQL dialects,
comments, or identifier contents. The new tests cover this service's current SQL forms,
not every application log sink. Raw logs should not be copied into shared reports.

## Deployment and endpoint impact

No API restart, deployment, database access, database mutation, migration, tenant routing,
permission change, production diagnostic code change, or query fix was performed.
Only regression tests and this redacted report were added. Existing diagnostics are
already enabled: **no deployment is required to identify this failure**.

No interceptor was added or enabled because the conditional fallback was unnecessary.
If a future deployment needs command interception, it requires an approved API binary
deployment/restart and a default-off option, with an async operation scope entered only
by `ListAsync` and stage scopes around its database executions. Such an interceptor can
limit capture to this operation; registering it on the tenant DbContext would still add
a small callback/check cost for other database commands. A new interceptor cannot be
introduced into the current process by a configuration switch alone.

Build and test artifacts are isolated under `.welcome-sql-diagnostics-20261018`;
command output is recorded in `.welcome-sql-diagnostics-build.log` and
`.welcome-sql-diagnostics-tests.log`.

Validation completed:

```powershell
dotnet build HRMS.slnx --artifacts-path D:/HRMS/.welcome-sql-diagnostics-20261018 -m:1 -nr:false
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --artifacts-path D:/HRMS/.welcome-sql-diagnostics-20261018 --no-build --no-restore --filter 'FullyQualifiedName~WelcomeInvitationDiagnosticTests|FullyQualifiedName~WelcomeInvitationServiceTests' --results-directory D:/HRMS/.welcome-sql-diagnostics-20261018/results --logger 'trx;LogFileName=welcome-diagnostics.trx'
```

The solution build passed with zero errors and 167 warnings (including existing
compiler/analyzer warnings and NU1900 vulnerability-feed reachability warnings).
All 8 selected tests passed, including the 3 new diagnostic test cases. Results:
`.welcome-sql-diagnostics-20261018/results/welcome-diagnostics.trx`.
The new diagnostics tests generate SQL without opening a database connection;
the existing service tests use disposable SQLite in-memory stores. The running
API and its databases were not used for validation. No frontend files changed;
frontend tests and unrelated backend suites were not run.
