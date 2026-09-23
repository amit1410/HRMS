# Payroll production deployment runbook

## Pre-deploy

Confirm the approved commit, provider configuration, secret references, migration plan, backup completion, and a maintenance window when the migration compatibility review requires one.

## Deployment sequence

1. Announce the change and stop payroll mutations if the release requires a maintenance window.
2. Back up the catalog and affected tenant databases.
3. Apply the reviewed database migrations with the repository provider selected explicitly.
4. Deploy the API and verify `/health` and `/ready`.
5. Deploy the frontend.
6. Verify `/api/payroll/production-health` and `/api/payroll/integrity` as an authorized operator.
7. Run a bounded payroll smoke test: period lookup, readiness, calculation status, output access, and audit history.
8. Inspect logs using the release/correlation identifier.
9. Record go/no-go.

Application rollback is preferred when the schema remains backward compatible. Database rollback is not assumed safe; use a restore or forward fix for destructive/irreversible migration changes.
