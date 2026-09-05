# Phase 3B browser acceptance checklist

Setup prerequisites: **Pending**. This checkpoint intentionally does not include the setup/stop scripts, run-state files, credential files, or SQL acceptance fixture source needed to create and manage a disposable browser run. Do not invent a setup procedure, use normal appsettings, or access existing HRMS databases. Obtain separate approval for reviewed setup tooling and a fresh run-owned database environment before proceeding.

## Current status — live acceptance deferred

**LIVE ACCEPTANCE BLOCKED — ROOT STARTUP EXCEPTION NOT YET CAPTURED**

The API process started but exited before readiness (`ProcessExited=true`,
`ExitCode=1`, `ReadinessOutcome=Exited`, `ReadinessTimeout=false`), with empty
stdout and stderr. The verified process-capture launcher detected the exit and
correctly skipped frontend/browser startup. A diagnostic-only catch
instrumentation was prepared in an isolated checkout, but that checkout could
not build with its existing no-restore project-reference/assets state, so the
managed exception was not captured.

Runtime compatibility, build-output consistency, the launcher's process/exit
capture, and the `Development` plus `Database__SkipInitialization=true`
source control flow were verified. No evidence establishes SQL Server, TLS,
runtime, migration, or configuration as the root cause. The underlying
pre-readiness startup cause remains unresolved.

Browser acceptance remains incomplete: API readiness, frontend readiness,
end-to-end linking flows, identity refresh, concurrency/deadlock behavior,
and legacy-database reconciliation are not accepted by this record.

Deferred diagnostic entry point: reproduce the API startup failure in an
isolated buildable checkout and capture the exception emitted by the existing
`Program.cs` top-level catch before changing SQL/TLS/database configuration.

Open both printed tenant URLs in Chrome or Edge only after the separately
approved setup prerequisites are complete. Obtain synthetic credentials only
from the local run state; never put them in a report.

- [ ] Authorized operator can view current state and link an eligible future-joining employee.
- [ ] View-only user cannot search candidates or mutate; ViewHistory independently reads history; Manage is required for changes.
- [ ] Direct API calls deny unauthorized history/manage operations.
- [ ] Self-link is rejected.
- [ ] Empty replacement/unlink reasons are rejected; replacement requires explicit confirmation.
- [ ] Occupied target is rejected and the original link remains.
- [ ] Unlink/relink in one already-open session refreshes identity on the next request.
- [ ] Future-joining, missing-history, separated, and disabled-account rules match the approved design.
- [ ] Tenant A cannot read or mutate Tenant B identities, links, or history.
- [ ] Logout and account/session changes discard late identity responses and stale drafts.

Record each item as Passed, Failed, or Pending with sanitized status codes and visible UI behavior. Browser acceptance is not complete until the operator supplies results. Cleanup prerequisite: **Pending**; use only a separately approved cleanup procedure that stops verified run-owned processes and removes only manifest-owned databases.
