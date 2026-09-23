# Payroll security-hardening checklist

- [ ] Tenant filters and composite tenant foreign keys cover every production-health and integrity query.
- [ ] Every diagnostic endpoint has an existing payroll permission; no support endpoint is anonymous.
- [ ] Maker-checker prevents self-approval where configured.
- [ ] Payroll, tax, statutory, bank, filing, and diagnostic exports are permission-gated.
- [ ] Connection profiles expose only non-secret configuration and secret-reference presence.
- [ ] Logs contain correlation and operation metadata, not passwords, tokens, connection strings, raw bank data, or proof payloads.
- [ ] Package downloads use controlled storage and prevent path traversal.
- [ ] Support diagnostics return safe codes and summaries, not stack traces.
- [ ] SQL Server/MySQL credentials are provided only through deployment configuration or a secret store.
- [ ] Administrative unlocks require reason, permission, and audit history.
