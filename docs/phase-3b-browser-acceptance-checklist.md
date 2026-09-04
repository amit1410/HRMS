# Phase 3B browser acceptance checklist

Setup prerequisites: **Pending**. This checkpoint intentionally does not include the setup/stop scripts, run-state files, credential files, or SQL acceptance fixture source needed to create and manage a disposable browser run. Do not invent a setup procedure, use normal appsettings, or access existing HRMS databases. Obtain separate approval for reviewed setup tooling and a fresh run-owned database environment before proceeding.

Open both printed tenant URLs in Chrome or Edge. Obtain synthetic credentials only from the local run state; never put them in a report.

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
