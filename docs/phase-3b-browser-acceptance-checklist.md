# Phase 3B browser acceptance checklist

Run `tools/Start-Phase3BBrowserAcceptance.ps1` in a private PowerShell window with `HRMS_SQLSERVER_TEST_SERVER` set and Windows Integrated Security. It creates fresh run-owned databases, applies the real migration chains, seeds synthetic data, selects unused ports, and leaves the environment running. Do not use normal appsettings or existing HRMS databases.

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

Record each item as Passed, Failed, or Pending with sanitized status codes and visible UI behavior. Browser acceptance is not complete until the operator supplies results. Stop with `tools/Stop-Phase3BBrowserAcceptance.ps1 -StatePath <state>`, which stops only recorded process IDs and removes only manifest-owned databases.
