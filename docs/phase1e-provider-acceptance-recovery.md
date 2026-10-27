# Phase 1E disposable-provider recovery

The provider acceptance harness intentionally does not delete a database after an interrupted create or migration. A durable manifest with `CreationSucceeded=false`, a missing marker, a failed migration, or an unverified server identity is never cleanup authority.

1. Stop provider acceptance execution and preserve the manifest and test log. Do not reuse its run ID or database names.
2. On the approved server only, inspect the manifest without changing the database. Confirm the exact provider, server identity, instance/port, run ID, database name, and ownership marker.
3. Compare the database-side `__HRMS_DisposableOwnership` marker with the manifest. If either side is missing or differs, treat the database as orphaned and do not drop it through the harness.
4. Ask the database administrator to verify that the database contains no non-test data and remove it using the provider’s normal change-control procedure. SQL Server cleanup may require disconnecting sessions; MySQL cleanup may require terminating active sessions.
5. Record the administrator’s server identity, database name, timestamp, and deletion result beside the manifest. Keep the failed manifest as an audit artifact.

If a cleanup was interrupted after the marker and manifest were verified, rerun cleanup with the same immutable manifest and explicit deletion approval only after the server identity still matches. If absence cannot be verified, leave the database in place and escalate.

Never use the development/shared connection, a protected fixture name, a tenant/catalog database, or a guessed prefix as recovery authority.
