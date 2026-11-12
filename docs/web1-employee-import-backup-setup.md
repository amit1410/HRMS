# WEB1 Employee Import Recovery Backup Setup

## Status

Preparation only. This setup does not create a WEB1 backup, apply a migration, or connect to a database. A separate owner approval is still required for operational use. The project owner approved 30-day local retention on 2026-11-05; a local policy reference must be configured and approved before an evidence document can satisfy the local migration gate.

## Protected configuration

Run scripts/backup/Initialize-HRMSWeb1BackupConfiguration.ps1 with -Initialize in an interactive PowerShell session. It creates protected directories under the current Windows user profile, then asks for the catalog and tenant endpoints, TLS CA file, and existing least-privilege catalog/tenant credentials. Credential values are entered through Get-Credential and stored as DPAPI-protected CLIXML for the current Windows user. They are never placed in source control, JSON configuration, command-line arguments, or diagnostic output.

The configuration records the expected WEB1 route (TenantId 16313e68-2e72-4a6a-b689-79b271cec295, web1.localhost, shard web1, MySQL database hrms_web1). These values must still be verified against the live catalog using the dedicated read-only credentials. The setup script does not connect to MySQL or create database accounts.

The script fails if existing config or credential files would be overwritten. It verifies directory ACLs and permits only the current user, SYSTEM, and local Administrators. Backup files are placed outside the repository and temporary build output under %LOCALAPPDATA%\Anevra\HRMS\Backups\WEB1.

## Backup method

MySQL Shell 8.0.46 and mysqldump 8.0.46 are installed. For this single tenant database, the prepared streaming command uses mysqldump with one schema, InnoDB transaction consistency, and routines, events, triggers, and binary objects. It requires all base tables to be InnoDB and a DDL-free snapshot window. Use the exact invocation recorded in the backup evidence, with --set-gtid-purged=OFF to keep a tenant-only logical restore from changing global GTID state.

The MySQL manual describes --single-transaction as a consistent snapshot for transactional tables such as InnoDB and warns that concurrent DDL can invalidate the dump. It also documents the required privileges and the need to include triggers, routines, and events where applicable. See the [mysqldump reference](https://dev.mysql.com/doc/refman/8.0/en/mysqldump.html).

## Encryption and key custody gate

The selected format is AES-256-GCM. The existing tools/BackupEncryptionPrototype is a synthetic-data implementation and is explicitly not production-approved. Its key custody is not integrated with an organizational KMS or independent custodian. Do not use it for a WEB1 artifact until a security/key owner approves the method, key custody, recovery procedure, and implementation reference. The prior CBC/HMAC backup script is not the selected production format.

Required before backup:

- Security/key-owner approval reference for AES-256-GCM and key wrapping/storage.
- Independent key-custody reference and named recovery authority.
- Successful recovery-key verification by a second authorized person.
- Approval for the selected encrypted artifact format and verifier.

## Development-only recovery policy

`MigrationHost:LocalDevelopment` is an additive policy branch for the single recovery-audit tenant plan. It is disabled unless `Enabled=true`, both the actual host environment and `--environment` are exactly `Development`, the current machine name matches the protected configuration, the catalog and tenant use the configured port and the same MySQL instance, and the server UUID matches the approved local instance. The configured endpoints must be literal loopback IP addresses. `SslMode=VerifyCA` or `VerifyFull` is required; `VerifyCA` does not verify the server hostname and is accepted only as a loopback-local exception. Do not treat it as equivalent to `VerifyFull`.

The branch still runs the existing recovery migration guard, including its WEB1 target allowlist, migration allowlist, general approval allowlist, encrypted-backup flag, retention flag, restore-test flag, maintenance and worker-pause confirmation, and audit-table absence check. It adds a separately allowlisted local policy approval and signed evidence validation. It is rejected outside Development and for any plan other than `employee-import-recovery-audit-web1`. The ordinary production migration path is unchanged.

The signed evidence envelope is ECDSA/SHA-256 over the exact payload bytes. Validation checks the trusted public key, exact WEB1 and MySQL server identity, literal loopback endpoint, migration inventory, operator and approval references, local retention-policy reference, 30-day timestamps, encrypted artifact SHA-256, authenticated-decryption result, isolated restore result, and that the restore database is not `hrms_web1` or `hrms_catalog`. The evidence and encrypted artifact must be under the protected backup root. The MigrationHost checks that the backup root has a protected Windows ACL allowing only the operator, SYSTEM, and local Administrators.

The protected external MigrationHost configuration must supply these non-secret policy fields (do not put them in repository settings):

```text
MigrationHost:LocalDevelopment:Enabled = true
MigrationHost:LocalDevelopment:MachineName = <approved Windows machine name>
MigrationHost:LocalDevelopment:Port = 3306
MigrationHost:LocalDevelopment:MySqlServerUuid = <verified local MySQL server UUID>
MigrationHost:LocalDevelopment:BackupRoot = <restricted local backup directory>
MigrationHost:LocalDevelopment:RetentionDays = 30
MigrationHost:LocalDevelopment:RetentionPolicyReference = <approved local policy record>
MigrationHost:LocalDevelopment:EvidencePublicKeyPem = <public key only>
MigrationHost:LocalDevelopment:AllowedPolicyApprovals = <explicit local approval IDs>
```

The corresponding signing private key must be kept in the Windows certificate store under an approved, restricted identity; it must never be written to source, JSON, command lines, or logs. Evidence must be produced only by a reviewed backup/restore verifier after it independently hashes and authenticates the artifact and verifies the isolated restore. A manually asserted `AuthenticatedDecryptionPassed` or `IsolatedRestorePassed` value is not evidence.

## Current implementation limits and gate

The MigrationHost now requires signed evidence schema v2: exact WEB1 identity, encrypted artifact hash, authenticated decryption, isolated restore, matching table counts and schema digests, complete migration-history equality, and matching sanitized counters for operation `b89d04aa-1419-4c85-a7e3-48ab1d92d111`. The production migration path and its existing guards remain in place.

`tools/BackupEncryptionPrototype` now contains a synthetic-only streaming archive engine. It uses 1 MiB chunks by default (4 MiB maximum), AES-256-GCM with a fresh key per artifact, unique sequence nonces, authenticated metadata/AAD, strict ordered records, and an authenticated end record that rejects truncation and trailing bytes. Encrypt/decrypt working memory is O(chunk size), not dump size. The publisher accepts a binary producer stream, checks its exit result, applies and verifies restricted Windows ACLs on its new staging directory, wraps the key with DPAPI CurrentUser bound to the backup ID, authenticates the complete artifact, computes SHA-256, and atomically publishes by directory rename. It never writes plaintext dump bytes to disk. The restore verifier authenticates the complete artifact before emitting restore bytes, requires a different verified loopback disposable instance and exact target-scoped grants, and invokes disposable-target cleanup on any restore or comparison failure. The evidence payload has been extended to v2 with table, schema, migration-history, and sanitized operation aggregate evidence. Evidence signing requires a non-exportable CNG ECDSA certificate in the current user's Windows certificate store.

This is **not ready for an operational WEB1 backup**. No concrete MySQL source identity/snapshot reader, catalog route check bound to the dump process, `mysqldump` process launcher, protected option-file loader, `mysql` restore adapter, disposable-target provisioning/cleanup implementation, or deployed signing certificate/custody exists here. Current tests use synthetic streams, fake restore targets, current-user DPAPI, and ephemeral signatures. Do not create a WEB1 backup or generate migration evidence until those adapters and certificate provisioning are completed and independently reviewed.

## Required backup and isolated-restore sequence (after approval)

1. Confirm the API and worker remain stopped; obtain the approved local maintenance window and pause DDL. Verify catalog routing read-only and compare runtime `DATABASE()`, `@@hostname`, `@@port`, and `@@server_uuid` with the protected local policy values.
2. Verify every base table is InnoDB. Run MySQL 8.0.46 `mysqldump` for only `hrms_web1` with `--single-transaction`, `--routines`, `--events`, `--triggers`, `--hex-blob`, and `--set-gtid-purged=OFF`. Maintain a DDL-free window for the complete dump. Use the dedicated least-privilege read-only account and the configured `VERIFY_CA`/`VERIFY_IDENTITY` TLS mode; never put a password in a command line.
3. Pipe dump bytes directly into the streaming authenticated-encryption writer. Do not create a plaintext dump file. Use a fresh random data key per backup, protected by Windows DPAPI CurrentUser with backup-ID binding, and zero key buffers. Commit encrypted artifact and wrapped-key metadata atomically under the restricted backup root; reject overwrite and clean temporary files on error. Confirm `mysqldump` exits successfully before publication.
4. Record backup ID, operator, UTC time, source tenant and MySQL identities, encrypted artifact SHA-256, algorithm, key-protection method, policy reference, retention expiry, and approval reference. Authenticate the complete artifact before sending any plaintext to the isolated restore target. Never print or persist plaintext.
5. Restore only to a newly created, verified isolated database or instance. Never restore over the catalog or `hrms_web1`. Disable the event scheduler and application workers on the isolated target. Verify restored table counts, migration history, and only the import operation’s sanitized counters/state. Remove the isolated test database only through a separately reviewed cleanup procedure after evidence capture.
6. Have the reviewed verifier sign the evidence payload using its protected local signing key. Verify the evidence signature, current artifact SHA-256, root ACL, and policy timestamps. Preserve the signed evidence and audit record through the 30-day retention period; perform deletion manually after expiry, with the operator, approval, and deletion result recorded.
7. Run MigrationHost read-only validation, then separately request approval to apply the allowlisted migration. Recovery apply and worker startup remain separate approvals.

The current certificate fails `VERIFY_IDENTITY` because its SAN does not match the loopback endpoint. The local policy does not claim `VERIFY_CA` provides identity verification; it requires loopback, exact server UUID checks, and CA verification as a documented development exception. Prefer resolving the SAN and using `VERIFY_IDENTITY` before any backup.

The guarded migration invocation will include the existing exact WEB1 tenant-only recovery-audit plan, all existing approval flags, plus `--local-development-policy --local-policy-approval <approved-ref> --local-evidence <signed-evidence-path> --operator <operator-id>`. This is a command shape only; do not run it until the streaming backup/evidence producer, protected configuration, security review, fresh backup, and isolated restore test are complete and separately approved.

## Retention and storage

The project owner approved 30 days for this local development recovery backup on 2026-11-05. Local operational use still requires a separately approved local policy reference recorded in the protected configuration and signed backup evidence. This local approval does not satisfy or replace production retention policy. Automatic deletion is disabled. The backup root ACL is restricted; broad users/authenticated-user write access is rejected.

## Synthetic validation

Run scripts/backup/Test-HRMSWeb1BackupSetup.ps1 to validate ACL construction, DPAPI credential serialization, and secret-free configuration using synthetic values. This test performs no network or database operation.

The existing full backup/restore candidate in scripts/Invoke-HRMSWeb1BackupRestoreVerification.ps1 is not yet approved for WEB1 use. It uses API-level connection settings and a custom CBC/HMAC format. Use it only for its synthetic tests until it is replaced with this protected configuration and the approved authenticated-encryption/key-custody implementation.
