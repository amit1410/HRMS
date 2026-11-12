# Backup Encryption Prototype — NOT APPROVED FOR PRODUCTION BACKUPS

This is an offline, synthetic-data-only .NET 10 prototype. It is isolated under `tools/BackupEncryptionPrototype`; it does not connect to databases, MySQL Shell, HRMS application code, recovery workflows, APIs, SMTP, or production keys/data.

## Streaming local-recovery archive (synthetic test path)

`StreamingBackupArchive` is a separate binary stream format for bounded-memory testing. It writes authenticated JSON metadata (backup ID and optional WEB1 source scope), then ordered AES-256-GCM records. Each record nonce is a random 4-byte per-key prefix followed by a big-endian 64-bit sequence; header bytes, sequence, and chunk length are authenticated as AAD. A terminal authenticated record binds total chunk count and plaintext byte count; EOF must immediately follow it. The chunk size is 1 MiB by default and capped at 4 MiB. The encryption/decryption working set is O(chunk size), independent of dump size; buffers are cleared after use.

`StreamingArtifactPublisher` streams an already-running producer's stdout directly into the encrypted writer, checks the producer exit result before publication, writes only ciphertext and a DPAPI CurrentUser-wrapped key into a restricted staging directory, authenticates the complete file, hashes it, and atomically renames the directory. The wrapped key's DPAPI entropy includes the backup ID. It refuses overwrites and cleans staging on failure. `VerifyThenDecrypt` requires a seekable artifact and authenticates the whole artifact before emitting plaintext. `IsolatedRestoreVerifier` further requires a separate loopback disposable target, distinct server UUID, database-scoped grants, disabled event scheduler, and identical schema/migration history/table counts/import aggregates; it calls the target's abort hook on a failed restore.

These are tested components, not a production backup operator. No concrete MySQL source identity/snapshot reader, `mysqldump` process launcher, credential-option-file loader, MySQL restore adapter, disposable-instance provisioner, or certificate provisioning workflow is included. Do not invoke the publisher with WEB1 data. The local ECDSA evidence writer requires a non-exportable CNG key in the current user's certificate store, but its production certificate and ACL provisioning remain unverified. The MigrationHost v2 evidence contract is tested synthetically; that does not prove a real backup/restore.

## Version 2 format

The package is `HREB`, version `2`, followed by a fixed 54-byte little-endian header: flags (zero), chunk size, UTF-8 manifest byte length, chunk count, 16-byte random package identifier, and the first 12 bytes of that identifier repeated as a consistency check. The encrypted manifest is length-prefixed, followed by authenticated chunk records and one authenticated `HREE` end record. All records are ordered and extra/truncated data is rejected.

The manifest is exactly canonical, compact UTF-8 JSON with no BOM, comments, trailing commas, duplicate properties, or alternate numeric/string spellings. Its property order is `Files`, then `TotalLength`; entries are `Path`, `Length`, `Sha256`. SHA-256 is exactly 64 hexadecimal characters and lengths must agree with the authenticated plaintext.

Limits are: manifest 1 MiB; 100,000 files; 1 GiB per file; 4 GiB total plaintext; 1,000,000 chunks; 4 MiB maximum chunk and allocation. All count/length arithmetic is checked. A zero-file/zero-byte package and a package containing only zero-byte files are valid and authenticated.

## Nonces and keys

AES-256-GCM uses 12-byte nonces: the first four bytes are the package identifier prefix and the final eight bytes are a little-endian record counter. Counter 0 is the manifest; counters 1 through N are chunks; N+1 is the end record. Counters are monotonic, bounded, and never reused within a package.

The supported workflow is `EncryptWithFreshKey`: it generates a new 256-bit key for each package and returns that key exactly once to the caller for custody handoff. The low-level caller-key primitive is internal and is not the proposed backup API. Code therefore enforces fresh generation at the workflow boundary, but cannot prove that an external operator later copied, re-used, or lost the returned key. A production contract must require one key-generation event per package, immutable key/package association, one-time custody transfer, non-exportable or wrapped storage in an approved KMS/HSM, audit evidence, destruction/rotation policy, and independent evidence that the same key was never used for another package. A random package identifier, nonce, or process-local registry is not proof of global key uniqueness.

## Paths, extraction, and atomic encrypted output

Paths are relative, normalized Unicode Form C, slash-separated, and contain no empty, dot, dot-dot, rooted, drive/colon, NUL, backslash alias, trailing-dot/space, or Windows-reserved (`CON`, `PRN`, `AUX`, `NUL`, `COM1`-`COM9`, `LPT1`-`LPT9`) component. The trailing-dot/space rule is applied to every component, including intermediate directories. Comparison is case-insensitive, so case and normalization collisions are rejected.

`EncryptToFile` is the high-level file workflow. It requires an existing, dedicated, access-controlled staging directory on a fixed local volume, requires the final file to be directly inside that directory, refuses UNC/shared destinations and any existing final path, writes a uniquely named restricted staging file with `FileMode.CreateNew`, flushes and closes it, reopens and authenticates the complete package, then commits with non-overwriting `File.Move`. Any failure removes the staging file; if cleanup also fails, both failures are reported. An arbitrary destination `Stream` can provide format/encryption semantics but cannot provide atomic file-commit guarantees, so `Encrypt(Stream, ...)` is intentionally only a low-level primitive.

`DecryptToStream` is internal. The documented recovery path is `DecryptToDirectory`: it writes to a unique staging directory, verifies authenticated manifest/chunks, terminal record, file sizes, hashes, and package boundary, flushes temporary files, and atomically renames staging only after all checks pass. Existing destinations and reparse-point extraction paths are refused. Cleanup failures are reported together with the original failure.

## Tests and remaining risks

`dotnet run --project tools/BackupEncryptionPrototype/BackupEncryptionPrototype.csproj` runs executable synthetic self-tests for round trips, authentication/tamper detection, truncation and trailing data, canonical/length checks, path collisions, resource boundaries, nonce-record ordering, empty packages, and (on Windows) DPAPI key wrapping. This project has no discovered xUnit/NUnit tests; a `dotnet test` invocation that reports zero tests is not evidence that these self-tests ran.

Residual risks and controls: the compatibility list-returning API materializes its requested result; the DPAPI wrapper is current-user/current-machine only and there is no KMS/HSM, independent custodian, or global key-use ledger in this prototype; filename metadata is not confidential; reparse-point checks and handle sharing modes do not eliminate every filesystem TOCTOU/race possibility. The atomic file API therefore fails closed unless the caller supplies a dedicated local staging directory that is access-controlled and not shared with untrusted users. The custodian must provide the key-generation, one-key/one-package, access, audit, retention, and destruction evidence described above. Another independent security/cryptography review is required before any operational use; this prototype remains NOT APPROVED FOR PRODUCTION BACKUPS.

## Offline review status

1. Atomic encrypted output: **closed in code and synthetic tests** for this prototype. The final path is never overwritten; arbitrary streams remain non-atomic by contract.
2. Destination path validation: **closed in code and synthetic tests** for the stated Windows/local-volume policy. Production deployment must still select and enforce the approved staging directory ACL.
3. Filesystem race safety: **closed as a fail-closed design decision, not as a race-proof claim**. Shared/UNC/non-fixed/reparse destinations are rejected; residual TOCTOU risk requires a dedicated access-controlled local staging location and operational review.

The executable self-tests are the offline gate; `dotnet test` is not expected to discover tests in this standalone project. No production approval is implied.

## Local Windows key-wrapping candidate (2026-11-07)

The synthetic-only harness now includes `WindowsDpapiKeyProtector` and a high-level file/key-envelope round trip. It wraps a fresh 256-bit AES key with current-user DPAPI, UI forbidden, without the machine-scope flag, and with entropy bound to the backup ID. The wrapped key is committed without overwrite. On failure the code attempts to remove staging, wrapped-key, and encrypted-artifact files and reports cleanup failures. Synthetic tests exercise round-trip and wrong-backup-ID rejection.

This does not make the prototype suitable for a real MySQL backup. The current package API materializes inputs and has a 4 GiB total limit; it is not a streaming `mysqldump` encryption pipeline. Current-user DPAPI also makes recovery dependent on the same Windows user profile and machine. The local recovery policy requires independent security review, a reviewed streaming backup/restore evidence producer, and protected signing-key provisioning before operational use. Never use this prototype with WEB1 data until those gates pass.
