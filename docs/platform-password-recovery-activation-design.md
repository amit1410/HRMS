# PlatformPasswordRecovery activation design

Status: design only; reviewed 2026-10-08. No database connection, database read/write, backup creation/restore, SMTP call, API start, migration execution, or password reset was performed for this design.

## Decision summary

The existing `tools/PlatformPasswordRecovery` project is an offline dry-run prompt utility. It is not an activation mechanism and must remain unable to open a catalog or change an account. A future activation, if approved, should be a separately reviewed, disabled-by-default command boundary. It must use the existing `IPasswordHasher`/`IdentityPasswordHasher` implementation and a catalog transaction, but only after the gates below have been satisfied.

The normal `PlatformPasswordRecoveryService` is a separate application flow: it creates email reset tokens, sends mail, and currently owns the ordinary reset endpoint. This plan does not modify, bypass, or replace that flow. It also does not enable a second operational reset path.

## Repository review: implemented versus proposed safeguards

### Implemented and observed

- `PlatformRoleNames.PlatformSuperAdmin` is the canonical platform role name.
- `PlatformUser` has `Id`, `Email`, `NormalizedEmail`, `IsActive`, `PasswordHash`, and `SecurityRevision`; normalized email is unique in the catalog model.
- `PlatformRefreshToken` stores only `TokenHash` and has `RevokedAtUtc`; the normal reset service revokes active refresh tokens.
- `IdentityPasswordHasher` implements `IPasswordHasher` with ASP.NET Core Identity's salted, iterated password hashing and constant-time verification.
- Platform authentication embeds `SecurityRevision` in the access token and rejects a stale revision.
- The dry-run utility requires user ID, exact email, a non-secret backup reference, visible confirmation phrases, hidden password entry, confirmation, and a minimum password policy. It prints no password or hash and has no deliberate database, SMTP, API, migration, or file-write operation.

### Proposed; not implemented or enabled

- Fresh catalog-backed identity verification and fail-closed drift checks.
- Independently verifiable backup evidence and isolated restore proof.
- Two durable, distinct approvals bound to one operation/account/evidence tuple.
- One-time execution authorization, atomic claim, expiry, concurrency protection, and replay prevention.
- Durable recovery audit evidence and post-commit validation.
- The activation transaction and its row-count assertions.

No proposed safeguard should be represented by a migration, controller, command switch, service registration, or enabled configuration until this document's gates are approved. Migration-history reconciliation remains a separate work item.

## Target identity verification

The operator supplies an exact `UserId` and exact email out of band. The future verifier must independently query the catalog and require all of the following in one read-only verification step:

1. Exactly one active `PlatformUser` row matches the supplied `Id` and `NormalizedEmail = suppliedEmail.Trim().ToUpperInvariant()`.
2. That row has an active assignment to the exact role `PlatformSuperAdmin`; role name matching is exact and ordinal, not prefix or case-folded beyond the repository's canonical value.
3. The overall active `PlatformSuperAdmin` population is exactly one. A count of zero or greater than one fails closed, even if the requested row otherwise matches.
4. The supplied email's normalized form, stored `Email`/`NormalizedEmail`, and supplied GUID are captured in the verification record. The raw password is never part of it.
5. The verification captures the observed `SecurityRevision` and a deterministic identity fingerprint such as `SHA-256(userId || 0x00 || normalizedEmail || 0x00 || securityRevision)`. The fingerprint is evidence, not a secret.

Conceptual read-only query shape (design notation only; do not run as a script):

```sql
-- one result for the requested account, with exact role and active checks
SELECT u.Id, u.Email, u.NormalizedEmail, u.SecurityRevision
FROM PlatformUsers u
JOIN PlatformUserRoles ur ON ur.PlatformUserId = u.Id
JOIN PlatformRoles r ON r.Id = ur.PlatformRoleId
WHERE u.Id = @UserId
  AND u.IsActive = 1
  AND u.NormalizedEmail = @NormalizedEmail
  AND r.Name = 'PlatformSuperAdmin';

-- separately assert COUNT(DISTINCT u.Id) = 1 for active PlatformSuperAdmin rows
```

The implementation must not rely on an email lookup alone, stale operator-provided data, a single role ID copied from a fixture, or a result that silently chooses the first match. Any identity drift between verification and execution—including changed email, inactive flag, role membership, security revision, or a second active super-admin—aborts the operation.

## Backup evidence prerequisite

No backup is created or restored in this phase. Before a future write, an independent backup custodian must provide a signed or otherwise tamper-evident evidence package containing:

- selected method: approved physical backup or complete and consistent MySQL Shell logical dump, restricted to this local PlatformSuperAdmin recovery;
- catalog identity: environment, engine/provider, server/instance identifier, database/catalog name, and tenant/catalog scope;
- exact source identity and exact isolated restore target identity, with tool versions and UTC start/end timestamps;
- backup artifact identifier and storage location/reference (never a credential or connection string), plus a complete SHA-256 manifest covering every artifact/dump/manifest file;
- separately approved encryption method, independent key custody, verified decryption, retention/expiry, size, and cryptographic digest evidence;
- engine-native integrity result and the tool/version used;
- for a logical dump: complete MySQL Shell dump of `hrms_catalog`, InnoDB consistency verification, successful completion, and explicit evidence that no unapproved filters omitted schema, data, routines, or metadata;
- successful independent restore into an isolated disposable MySQL instance, with read-only schema/data verification and proof the restore completed without touching production, any tenant shard, or the live catalog;
- verification results for the catalog schema and the relevant `PlatformUsers`, `PlatformUserRoles`, `PlatformRoles`, and `PlatformRefreshTokens` structures;
- operator/custodian identities and signatures or equivalent approval records.

The restore proof must be independently reproducible by a second person and must include a successful open/read-only validation of the restored catalog. A backup that has only been copied, listed, checksumed, decrypted, or shown to have a syntactically valid dump is insufficient. Missing, expired, mismatched, unverifiable, incompletely filtered, or production-restored evidence fails closed. The activation binds a canonical `backupEvidenceDigest` over the evidence package; a human-readable reference alone is not sufficient.

This physical-or-logical alternative is only an activation prerequisite for local PlatformSuperAdmin recovery. A logical backup does not replace full-instance backup, physical backup, or general disaster-recovery backup requirements. Migration-history reconciliation remains a separate work item and is not included in backup creation, restore validation, or this activation.

## Two approvals

Create one `operationId` before approvals. The immutable approval tuple is:

`operationId + targetUserId + normalizedEmail + identityFingerprint + backupEvidenceDigest + requestedScope`

Require two separate approval records, with distinct authenticated approvers and UTC timestamps:

1. **Identity/backup approval:** confirms the exact active account and the independently verified backup evidence.
2. **Execution approval:** authorizes the one password change for the same exact tuple and records the explicit no-SMTP/no-migration/no-fixture scope.

Approvals must be durable, append-only, and immutable after creation. A second approval for a different user, email, revision, backup digest, or operation ID is invalid. The target account itself cannot be an approver. Expired, revoked, superseded, self-approved, or incomplete approvals fail closed. The design should require two distinct human identities; if organizational policy permits one person to hold both roles, that exception must be separately documented and never inferred by the tool.

## One-time execution authorization and concurrency

The future activation needs a durable authorization record, not a command-line flag. Store only a cryptographic hash of a high-entropy one-time authorization value; display/provide the raw value through a separate protected channel. Bind the hash to the immutable approval tuple, an expiry, and a `Pending` state.

At the start of execution, atomically claim the authorization with a predicate equivalent to `Id = @operationId AND State = Pending AND ExpiresAtUtc > @now`, expecting exactly one affected row. A zero-row claim is a replay, expiry, revocation, or concurrent claim and must abort. Persist `ClaimedAtUtc`, claimant identity, and a nonce/fingerprint without storing the raw authorization. A unique key on `operationId` and a state transition that only permits `Pending -> Claimed -> Committed` (or `Failed`) prevent replay. A process crash must leave the operation non-reusable until a separately approved recovery of the authorization record; it must not automatically retry the password change.

Immediately before the transaction, repeat the identity and backup-evidence assertions. Use the originally observed `SecurityRevision` as an optimistic concurrency predicate. Only one invocation may proceed for the operation and account.

## Secure interactive password handling

The future command must never accept a password in an argument, environment variable, URL, standard output, standard error, log message, audit row, crash detail, shell history, or telemetry field. It must use an interactive console with echo disabled, read twice, and reject non-interactive input by default. Avoid clipboard use and do not offer a “reveal” option.

Proposed policy for this recovery operation:

- 14 to 128 Unicode scalar values; reject empty input and whitespace/control characters;
- require at least one uppercase letter, lowercase letter, digit, and non-alphanumeric symbol;
- reject confirmation mismatch, the current password if it is ever available to the caller, and obvious repeated/known-compromised values if an approved offline list is available;
- normalize neither the password nor its bytes; hash exactly the entered secret with `IPasswordHasher`;
- clear mutable buffers promptly after hashing and do not include secrets in exception messages;
- prevent shell tracing and command transcript capture in the operator procedure.

The policy is a proposed recovery-specific gate, not a change to global AVG/security settings.

## Atomic catalog transaction

Only after all preflight gates pass, the future activation performs one catalog transaction. It must use the existing injected `IPasswordHasher`, not a new algorithm and not a precomputed hash supplied by the operator.

Within the transaction:

1. Re-read the target by exact ID, active status, exact normalized email, exact `PlatformSuperAdmin` membership, and the original `SecurityRevision`; assert one row.
2. Hash the in-memory password and assign the resulting hash to exactly that tracked `PlatformUser.PasswordHash`.
3. Increment `SecurityRevision` exactly once from the observed value.
4. Revoke all currently active (`RevokedAtUtc IS NULL`) `PlatformRefreshTokens` for that exact user and capture the affected row count. Zero is valid and must be recorded; rows belonging to any other user are a failure.
5. Add one recovery audit record containing only non-secret evidence.
6. Save once and require exactly one `PlatformUser` update. Require the refresh-token update count to equal the pre-update active-token count. A provider that cannot report reliable row counts is unsupported for this activation.
7. Commit only after all assertions pass. Any exception, unexpected row count, concurrency conflict, identity drift, audit failure, or cancellation rolls back the entire transaction and emits no success result.

The transaction must not create a user, alter roles, change `IsActive`, change email, create a refresh token, alter AVG/security settings, call SMTP, touch tenant shards, read BAD1/MH01 fixtures, or modify migration history.

## Rollback and post-commit validation

Rollback is database transaction rollback for any pre-commit failure. It must leave the old password hash, old revision, refresh-token revocation state, and audit evidence unchanged. A backup restore or compensating change is a separately approved disaster-recovery procedure, not an automatic action of this tool.

After commit, perform read-only validation using a fresh context/connection:

- exactly one target row still matches the approved identity and is active;
- `SecurityRevision = original + 1`;
- the stored hash is non-empty and verifies the entered password through `IPasswordHasher` (without logging the password);
- no active refresh token remains for the target user;
- no other user's password, revision, or refresh-token state changed;
- exactly one committed audit event exists for the operation and its evidence digest matches;
- the one-time authorization is `Committed` and cannot be claimed again.

If post-commit validation fails, report the operation as an incident and stop. Do not silently retry or perform an automatic second write.

## Durable audit evidence

Use a dedicated platform recovery audit record or an approved existing platform audit sink. It should be append-only and contain: `operationId`, target user ID, normalized-email fingerprint (not necessarily the raw email), identity fingerprint, original and new security revisions, backup evidence digest/reference, approval IDs and approver IDs, authorization claim/commit times, actor/host/tool version, outcome, row counts, and failure category. Hash or redact any operator-entered email where the audit policy requires it.

Never store or emit the plaintext password, password hash, raw one-time authorization, raw reset token, refresh-token value, connection string, or backup credentials. Hashes of evidence identifiers are acceptable where they preserve audit correlation without exposing secrets.

## Threat and risk analysis

| Threat | Control | Residual risk / response |
|---|---|---|
| Wrong account or identity drift | Exact ID + normalized email + exact role + exactly-one active super-admin assertion, repeated immediately before commit | Human transcription; require two-person review and abort on any mismatch |
| False or unusable backup | Catalog identity, digest, native integrity, isolated restore proof, second-person verification | Restore evidence can age; enforce expiry and re-verify immediately before commit |
| One approver authorizes both stages or changes the target | Distinct approvers and immutable tuple binding | Organizational collusion remains; retain durable audit and independent review |
| Replay or concurrent execution | Durable one-time claim, unique operation, expiry, state machine, revision predicate | Crash after commit/before response; post-commit reconciliation is read-only and does not retry |
| Password exposure | Hidden TTY entry, no arguments/env/logs/audit, bounded policy, buffer clearing | Host compromise/keylogger; use a controlled admin workstation and incident procedure |
| Partial password/token update | One transaction, exact row counts, rollback, post-commit validation | Provider-specific row-count semantics; reject unsupported providers |
| Session survives reset | Increment revision once and revoke every active refresh token | Already-issued access tokens depend on revision validation; confirm middleware behavior in offline tests |
| Audit becomes a secret sink | Explicit allow-list schema and redaction tests | Future logging changes; code review must include secret-scan tests |
| Migration drift is mixed into recovery | Explicit out-of-scope gate and separate work item | Operators may conflate tasks; checklist requires separate ticket/approval |

## Required future schema/configuration work

Before activation can be implemented, design and review a catalog schema for operation, approval, one-time authorization, and recovery-audit records with unique constraints and append-only semantics. That work requires its own migration review and isolated migration tests. It must not be executed as part of recovery, and it must not reconcile migration history. No such schema is assumed to exist merely because recovery-related uncommitted files are present in the worktree.

## Offline tests and results

The offline invariant test script is `tools/PlatformPasswordRecovery/OfflineInvariantTests.ps1`. It performs source/configuration inspection only; it does not connect to a database, start the API, call SMTP, execute migrations, access fixtures, or invoke a reset. It checks that the dry-run project remains a console-only tool, that password arguments are rejected by the documented interface, that sensitive values are not printed by the source, and that this design contains all required fail-closed gates.

Result on 2026-10-08: **PASS** (static offline checks). The checks do not prove the proposed future activation because that code and schema do not exist; implementation tests listed below are mandatory before any enablement.

Mandatory future offline/unit tests include: zero/multiple active super-admins; exact ID/email/role mismatch; identity drift after approval; invalid or mismatched backup digest; missing/duplicate/self approvals; expired/replayed/concurrently claimed authorization; password policy boundaries and secret-scan logging tests; hash failure; user update count not equal to one; refresh-token count mismatch; concurrency conflict; transaction rollback at each step; revision increments exactly once; post-commit validation failure; and migration-history commands being rejected by the activation boundary.

## Staged activation checklist and approval gates

### Stage 0 — design freeze (Gate A)

- Confirm this remains design/offline only.
- Confirm no API, SMTP, database, migration, fixture, AVG/security-setting, or operational write has been used.
- Review the future schema, provider row-count behavior, audit allow-list, and threat model.
- Keep migration-history reconciliation as a separate ticket with separate owners and approvals.

### Stage 1 — independent evidence collection (Gate B)

- Read the production catalog only through the approved read-only procedure; capture exact user ID, stored email, normalized email, role membership, active flag, and security revision.
- Assert exactly one active `PlatformSuperAdmin`.
- Create or select the catalog backup under the disaster-recovery procedure (not this tool).
- Complete integrity and isolated restore verification; record the evidence package and digest.
- Obtain the identity/backup approval bound to the exact operation tuple.

### Stage 2 — prepare the execution authorization (Gate C)

- Generate the operation ID and one-time authorization out of band.
- Obtain the distinct execution approval for the same account, revision, operation ID, and backup digest.
- Re-check authorization expiry, approval signatures, operator separation, and scope.
- Place the activation in a controlled, interactive environment with transcript/log capture disabled for secret input.

### Stage 3 — final preflight (Gate D)

- Re-read the catalog identity and assert no identity or revision drift.
- Re-validate backup evidence expiry, digest, catalog identity, and isolated restore proof.
- Atomically claim the one-time authorization; stop if the claim affects anything other than one row.
- Prompt for the new password twice without arguments/env/clipboard; validate policy.
- Obtain the explicit go/no-go from the two approval records. No approval may be added after the claim.

### Stage 4 — controlled execution (Gate E)

- Run the single transaction described above.
- Require one user update, exactly one revision increment, refresh-token row-count equality, one audit row, and commit success.
- On any failure, roll back and stop; never retry automatically.

### Stage 5 — post-commit and closeout (Gate F)

- Run fresh-context post-commit validation.
- Confirm stale access and all active refresh tokens are invalidated through the existing revision/revocation mechanisms.
- Mark the authorization committed and seal the audit package without secrets.
- Store only the evidence package and audit references in the approved incident/change record.
- Record any anomaly as an incident; do not perform migration reconciliation or unrelated account/security changes in the same change window.
