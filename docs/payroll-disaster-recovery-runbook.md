# Payroll disaster-recovery runbook

This is an operator runbook. Backup/restore success is not claimed until an environment-specific restore test is recorded.

## Assumptions

Back up the catalog database and each tenant database with the platform's encrypted backup and point-in-time recovery capability. Define RPO/RTO, retention, encryption, and restore owners per deployment.

## Restore sequence

1. Declare the incident and stop application writes.
2. Identify the affected catalog and tenant databases and the last known-good point in time.
3. Restore catalog and tenant databases in the platform-approved order.
4. Verify provider selection, schema version, and pending migrations before starting the API.
5. Start the API in a controlled mode, then verify liveness/readiness.
6. Verify payroll periods, locks, finalized runs, payslips, bank advice, GL, statutory results, year-end snapshots, and filing submission references.
7. Run production-health and integrity checks, then reconcile key totals against the incident checkpoint.
8. Confirm external submission state before allowing any retry.
9. Obtain owner approval before reopening writes.

Rollback decision points are: stop before restore if the recovery point is wrong; stop after restore if integrity or tenant checks fail; use a forward fix when a migration is not safely reversible.
