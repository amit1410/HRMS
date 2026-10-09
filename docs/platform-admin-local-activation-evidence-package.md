# Platform-admin account recovery: local activation and evidence package

Status: preparation only. This document is an operator checklist and approval template. It does not authorize or perform a backup, restore, migration, SMTP delivery, credential change, or production deployment.

## 1. Exact local target

| Field | Required value |
|---|---|
| Environment | Local activation only |
| Database provider | MySQL |
| MySQL endpoint | `127.0.0.1:3306` |
| Catalog database | `hrms_catalog` |
| Migration assembly | `HRMS.Infrastructure.MySqlCatalogMigrations` |
| Required pending migration | `20261008130000_AddPlatformPasswordRecovery` |
| Scope | Catalog only; no tenant shard is opened by the catalog-only commands |
| Production status | Blocked until local end-to-end validation passes and separate production approval is granted |

The target identity must be rechecked by the migration host at execution time. Any provider, endpoint, catalog, environment, migration-history, or pending-migration mismatch is a stop condition.

## 2. Local activation preflight

Complete and attach evidence for each item before requesting migration approval. Do not mark an item complete from an assumption or from application-level tests alone.

- [ ] Operator identity and change ticket are recorded; the operator is authorized for the local catalog.
- [ ] Approved secret mechanism is available for the migration host. No secret is present in JSON, Git, scripts, command-line arguments, logs, shell history, screenshots, or tickets.
- [ ] Protected configuration supplies the catalog connection and the required migration-host execution boundary: `MigrationHost:AllowedCatalogs` includes `hrms_catalog`, and `MigrationHost:RequiredEnvironment` equals the submitted environment value.
- [ ] The catalog connection resolves to MySQL at `127.0.0.1:3306`, database `hrms_catalog`; record only sanitized identity evidence, never the connection string.
- [ ] The release has been built and its required API and migration assemblies have immutable SHA-256 hashes recorded.
- [ ] The migration ID is present in the MySQL catalog migration assembly and is the only approved pending catalog migration for this activation.
- [ ] The approved physical-or-logical backup checklist in section 3 is complete; the selected method is restricted to this local PlatformSuperAdmin recovery.
- [ ] The isolated restore validation in section 4 is complete, with separately approved cleanup still outstanding or completed and evidenced.
- [ ] Local recovery configuration is ready: trusted local platform URL, fake/test email provider or test double, and no real recipient delivery.
- [ ] A local end-to-end test window and rollback/stop owner are recorded.

### Read-only catalog gate (do not execute as part of this preparation)

The existing host command is the catalog-only preflight. It must be run only by an authorized operator after the prerequisites above are satisfied:

```powershell
dotnet run --project Backend/HRMS.MigrationHost -- catalog-preflight `
  --catalog hrms_catalog `
  --provider MySql `
  --environment <approved-local-environment> `
  --approved-migrations 20261008130000_AddPlatformPasswordRecovery
```

Expected evidence is a sanitized target report showing provider `MySql`, database `hrms_catalog`, environment, and the pending migration ID. The command is read-only: it opens no tenant target and attempts no schema write.

The operator must stop if the catalog is not in `MigrationHost:AllowedCatalogs`, the environment does not equal `MigrationHost:RequiredEnvironment`, the provider/database identity differs, the catalog is unavailable, migration history is unexpected, or any pending migration is not explicitly approved.

## 3. Backup approval checklist

This is a separate approval gate. A generic statement that “a backup exists” is insufficient.

The approved alternatives are: (a) a physical backup, or (b) a complete and consistent MySQL Shell logical dump of `hrms_catalog`. This choice applies only to local PlatformSuperAdmin recovery evidence; it does not replace full-instance or general disaster-recovery backup requirements. Migration-history reconciliation remains separate and is not part of either backup method.

### Backup method, source identity, and scope

- [ ] Selected method (one only): `Physical backup` / `MySQL Shell logical dump`
- [ ] Approved backup tool and exact version/build: `____________________________`
- [ ] Tool approval/reference ID: `____________________________`
- [ ] Exact source identity is recorded: environment, provider/engine, server/instance identifier, endpoint, and catalog `hrms_catalog`: `____________________________`
- [ ] Scope is exactly `hrms_catalog`; no tenant shard, other database, unapproved object filter, or live-catalog target is included.
- [ ] Backup operator and UTC start/end timestamps are recorded: `____________________________`

For the logical-dump alternative, all of the following are mandatory:

- [ ] The artifact is a complete MySQL Shell logical dump of `hrms_catalog`, including all approved schema, data, routines, and metadata required to restore the catalog; no tables, rows, schemas, or object classes were excluded by an unapproved filter.
- [ ] MySQL Shell dump mode, options, and exact command/equivalent invocation are recorded in sanitized evidence: `____________________________`
- [ ] InnoDB consistency was verified using the approved method before acceptance, including the source transaction-consistency result and any required lock/GTID/binlog position evidence: `____________________________`
- [ ] The dump completed successfully with no skipped, failed, truncated, or otherwise incomplete objects/files; the MySQL Shell manifest/file inventory is attached: `____________________________`
- [ ] Logical-dump source identity and target catalog identity were independently compared and match exactly; a logical dump is not accepted merely because it can be imported.

For the physical-backup alternative, all of the following are mandatory:

- [ ] Physical-backup format, scope, consistency/integrity mode, and exact invocation are recorded in sanitized evidence: `____________________________`
- [ ] Physical-backup source identity was independently verified as the exact local MySQL `hrms_catalog` source; no tenant shard or live-catalog restore target was used.

### Encryption and key custody

- [ ] Encryption method, algorithm, and configuration were separately approved before use: `____________________________`
- [ ] Encryption was performed by the separately approved method or protected backup pipeline; the backup is not accepted merely because storage access is restricted.
- [ ] KMS/key-management system or other approved key custody reference: `____________________________`
- [ ] Key identifier or approved opaque reference (not key material): `____________________________`
- [ ] Key owner: `____________________________`
- [ ] Key recovery/escrow procedure and recovery authority are documented: `____________________________`
- [ ] Key custody is independent of the backup operator and storage custodian; key access was tested or independently confirmed without exposing key material.

### Retention, storage, and integrity

- [ ] Retention period and expiry policy: `____________________________`
- [ ] Approved storage location/URI or vault reference: `____________________________`
- [ ] Storage access control and deletion authority: `____________________________`
- [ ] Backup artifact identifier and size: `____________________________`
- [ ] Complete SHA-256 artifact manifest covers every backup/dump file and relevant manifest/metadata file; manifest generator and verifier versions are recorded: `____________________________`
- [ ] Tool-native integrity verification and SHA-256 manifest verification both passed; UTC timestamps are recorded: `____________________________`
- [ ] Decryption was verified successfully in a controlled procedure without exposing key material; decryption verifier/version and UTC timestamp: `____________________________`
- [ ] Backup completion UTC timestamp: `____________________________`
- [ ] Backup evidence is attached to the approval record without embedding secrets or connection strings.

Required approval: backup owner `________________`, security/key owner `________________`, approval ID `________________`, approved UTC timestamp `________________`.

## 4. Independent isolated restore validation and cleanup approval

Restore validation must be independently performed into a new isolated disposable MySQL instance and database. Never restore over `hrms_catalog`, an existing tenant database, a live catalog, or an unnamed/shared database. The restored catalog is read-only for verification; no application or migration may target it.

- [ ] Independent restore verifier/custodian is distinct from the backup creator and records identity: `____________________________`
- [ ] Disposable isolated MySQL instance identity, version, endpoint/network boundary, and database name were generated and approved before creation: `____________________________`
- [ ] Disposable restore database name was generated and approved before creation: `____________________________`
- [ ] The name is distinct from `hrms_catalog` and any existing database: `____________________________`
- [ ] Restore operator and UTC start/end timestamps: `____________________________`
- [ ] Restore used the encrypted physical backup or encrypted MySQL Shell logical dump from section 3; artifact ID and complete SHA-256 manifest match: `____________________________`
- [ ] Decryption was verified before restore and the decrypted source remained isolated from the live catalog: `____________________________`
- [ ] Restore completed without using production credentials beyond the approved protected mechanism.
- [ ] Restored target identity and expected catalog schema/data were verified read-only, including relevant platform tables and row/readability checks: `____________________________`
- [ ] The logical dump, if selected, was independently restored successfully with no missing/filtered object or data discrepancies; the physical backup, if selected, passed equivalent restore/readability checks.
- [ ] Backup integrity was rechecked before or during restore; evidence reference: `____________________________`
- [ ] Recovery/readability checks passed; evidence reference: `____________________________`
- [ ] No application, tenant, SMTP, migration, live-catalog, or production endpoint was pointed at the disposable instance/database; no tenant or live-catalog restore impact occurred.
- [ ] Exact source and target identities, tool versions, UTC restore timestamps, integrity/decryption evidence, and read-only verification results are attached: `____________________________`

Cleanup is a separate action and requires separate approval:

- [ ] Cleanup approver: `____________________________`
- [ ] Cleanup approval ID: `____________________________`
- [ ] Exact disposable database target rechecked: `____________________________`
- [ ] Cleanup UTC timestamp: `____________________________`
- [ ] Cleanup result/evidence: `____________________________`

Do not drop or otherwise remove the disposable database without the separately approved cleanup record. Do not reuse the name for another validation.

## 5. Secure migration-host configuration

Use the organization’s approved secret manager or protected environment settings. The host reads the catalog connection from `ConnectionStrings:Catalog` and the execution-boundary settings from the normal configuration providers. Set these through the approved protected mechanism; do not commit or print their values.

Required protected settings:

- `ConnectionStrings:Catalog` for the local MySQL catalog.
- `MigrationHost:AllowedCatalogs` containing `hrms_catalog`.
- `MigrationHost:RequiredEnvironment` matching the approved local environment string.
- `MigrationHost:AllowedApprovals` containing the approved migration approval ID only after approval is granted.

Secret-handling rules:

- Never put passwords, tokens, keys, full connection strings, or secret-manager payloads in JSON, Git, scripts, command-line arguments, logs, tickets, or screenshots.
- Do not use a shell variable merely because it is convenient; it must be a protected environment setting or approved secret-manager injection with restricted visibility and lifecycle.
- Do not echo configuration, enable verbose secret logging, or paste command output containing credentials.
- Record only the secret mechanism name/reference, configuration version, operator, and UTC timestamp.

## 6. Catalog-only migration safety gate (do not execute here)

After the preflight, backup, restore, and approvals are complete, the existing catalog-only mutation boundary is:

```powershell
dotnet run --project Backend/HRMS.MigrationHost -- catalog-migrate `
  --catalog hrms_catalog `
  --provider MySql `
  --environment <approved-local-environment> `
  --approved-migrations 20261008130000_AddPlatformPasswordRecovery `
  --confirm `
  --backup-verified `
  --backup-encrypted `
  --retention-approved `
  --restore-ready `
  --isolated-restore-tested `
  --approval <approved-approval-id>
```

The host requires confirmation, backup verification, encryption verification, retention approval, restore readiness, isolated-restore evidence, and an approval ID present in `MigrationHost:AllowedApprovals`. It rechecks catalog identity and pending migrations after acquiring the catalog migration lock, applies only the explicitly approved catalog chain, and does not open or modify a tenant target.

This command is intentionally not being run for this task. No allowlist, configuration value, or database is being changed.

## 7. Migration approval template

**Approval type:** Local catalog migration for platform-admin password recovery

| Field | Approval record |
|---|---|
| Approval ID | `____________________________` |
| Environment | `____________________________` |
| Provider | `MySql` |
| Endpoint | `127.0.0.1:3306` |
| Catalog | `hrms_catalog` |
| Exact migration ID(s) | `20261008130000_AddPlatformPasswordRecovery` |
| Release/API SHA-256 hash(es) | `____________________________` |
| Catalog migration assembly hash | `____________________________` |
| Backup method and evidence ID/UTC timestamp | `____________________________` |
| Restore evidence ID, disposable DB name, UTC timestamp | `____________________________` |
| Preflight evidence ID and UTC timestamp | `____________________________` |
| Risk/stop owner | `____________________________` |
| Approver name and role | `____________________________` |
| Approval UTC timestamp | `____________________________` |
| Expiry/revocation condition | `____________________________` |

Approval statement: “I approve only the exact catalog, environment, release hashes, backup evidence, restore evidence, and migration ID listed above. Any identity mismatch, unexpected pending migration, missing evidence, or secret-handling violation voids this approval.”

## 8. Post-migration validation and fake-email recovery test

Record UTC timestamps and evidence IDs for every check. Use a fake provider or test double; do not send real SMTP mail.

- [ ] Catalog-only migration reports success for the approved migration ID.
- [ ] A read-only follow-up confirms the catalog is `hrms_catalog`, provider is MySQL, and no unexpected catalog migrations are pending.
- [ ] The catalog migration history contains the approved migration in the expected order.
- [ ] API release hashes match the approved hashes.
- [ ] Local API starts with schema validation and the intended local configuration.
- [ ] Platform recovery UI is reachable at the approved local platform URL.
- [ ] Fake/test email provider is active and its capture store is isolated from real delivery.
- [ ] Known platform-admin identifier: recovery request returns the expected generic response and produces one captured test message.
- [ ] Unknown identifier: response is indistinguishable from the known-identifier response; no account existence is disclosed.
- [ ] Captured message contains the expected local reset link origin, no secrets beyond the single-use token in the test capture, and no real recipient.
- [ ] Reset token is accepted once, expires according to the configured policy, and cannot be reused.
- [ ] Successful reset changes the password and revokes platform refresh sessions as expected.
- [ ] Invalid, expired, reused, and rate-limited cases are recorded as pass/fail without recording tokens or passwords.
- [ ] No production endpoint, production SMTP provider, production recipient, or production database was used.

Attach sanitized request/response status evidence, fake-provider message IDs, test-run identifier, release hashes, and UTC timestamps. Never attach passwords, tokens, connection strings, or secret values.

## 9. Explicit blockers and stop point

The following approvals/evidence are currently missing from this package and must be supplied before any operator performs the gates:

- Approved physical or complete MySQL Shell logical backup method and exact tool version.
- For a logical dump: complete `hrms_catalog` scope, InnoDB consistency verification, no unapproved filters, and complete dump manifest.
- Separately approved encryption method, independent key custody, key recovery evidence, complete SHA-256 artifact manifest, verified decryption, retention approval, approved storage location, integrity evidence, and UTC timestamps.
- New disposable restore database name and restore evidence, plus separately approved cleanup.
- Successful independent restore into an isolated disposable MySQL instance with read-only schema/data verification and evidence of no tenant/live-catalog impact.
- Approved secret mechanism/protected environment configuration evidence for the migration host.
- Immutable release/API and migration-assembly SHA-256 hashes.
- Catalog-preflight result for the exact target.
- Explicit migration approval ID and named approver.
- Local end-to-end recovery evidence using a fake email provider/test double.
- Separate production deployment approval; production remains blocked regardless of local preparation status.

Stop here. Do not perform backup, restore, migration, SMTP delivery, credential changes, allowlist/configuration changes, or production deployment from this document.
