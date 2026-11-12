# Employee bulk import D5C

## Preparation and confirmation

`POST /api/employees/bulk-upload/prepare` accepts the XLSX as multipart form data. The API validates all rows before reserving an operation. It then persists a tenant-scoped `ArtifactPending` identity, stores the original workbook through the configured encrypted `IEmployeeImportWorkbookStore`, reads the artifact back to verify its SHA-256 identity, and only then transitions the operation to `Prepared` and returns its one-use confirmation token.

`POST /api/employees/bulk-upload/confirm` accepts JSON containing `operationId`, `confirmationToken`, and `confirm: true`. It reads the workbook from private encrypted storage, verifies the stored identity, revalidates current tenant data, claims the operation, and executes employee creation synchronously. A success response describes completed or partial execution; this endpoint does not return `202 Accepted` while work is still running.

## Artifact consistency and recovery

The database operation reservation is committed before artifact upload because relational transactions cannot atomically include S3 or filesystem writes. An operation remains `ArtifactPending` until the artifact is retrievable and verified. If upload or verification fails, preparation returns an error and releases its preparation lease when possible. If the process stops after reservation, the same initiating user can retry the same workbook after the lease expires. If the process stops after upload but before the Prepared transition, retry verifies the existing artifact and completes the transition. If the Prepared response is lost, a same-user retry rotates the one-use token on the existing operation. Completed or claimed operations cannot be re-prepared to bypass replay protection.

The operation record therefore acts as the durable recovery anchor; no plaintext workbook or employee payload is stored in relational tables. Orphan artifact cleanup must consult durable operation state and retention policy. A corrupted existing artifact is rejected and requires an operational repair/retention procedure; preparation does not overwrite it.

## D5D API transition

Confirmation remains synchronous in D5C. D5D should split confirmation into (1) token/hash validation and atomic durable enqueue, returning `202 Accepted` only after enqueue commit, and (2) worker-owned revalidation and row execution from the stored artifact. The worker must use operation claims/leases and existing per-employee onboarding transactions. It must not depend on the browser connection or an in-memory queue.

## Operation state claims

`ArtifactPending` is a preparation/recovery state and is not eligible for import execution. Only `Prepared` may be claimed for first execution. `Claimed` and `Processing` may be recovered by a future worker only after their durable lease expires and a new claim wins the concurrency check. `Completed`, `Failed`, `PartiallyCompleted`, and `Cancelled` are terminal and are never claimable. The domain transition rules reject transitions outside this graph.
