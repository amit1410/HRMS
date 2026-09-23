# Payroll production-readiness checklist

Use this checklist before a payroll release. It is operator-owned evidence; the application does not claim that backup or restore has been verified unless the operator records it.

- [ ] Confirm application commit/version and migration version.
- [ ] Confirm SQL Server/MySQL provider selection and connection availability.
- [ ] Confirm SQL Server and MySQL pending-model checks are None.
- [ ] Confirm tenant payroll configuration health is Healthy or has approved warnings.
- [ ] Confirm the active payroll period, run, and lock state.
- [ ] Confirm statutory, accounting, bank, year-end, and filing readiness.
- [ ] Confirm permissions and maker-checker roles.
- [ ] Confirm secrets are supplied by the deployment secret store, never repository files.
- [ ] Take and verify the pre-release catalog/tenant backups.
- [ ] Verify liveness, readiness, production-health, and integrity endpoints.
- [ ] Run the post-deployment payroll smoke test and inspect structured logs.
- [ ] Record go/no-go owner, on-call contact, and rollback decision.

The production-health endpoint is read-only and permission-gated. It reports configuration issues without secrets, stack traces, or raw payroll payloads.
