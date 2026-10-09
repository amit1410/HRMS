# Platform password recovery utility

This is an offline, non-writing dry-run utility for one explicitly verified existing `PlatformSuperAdmin` account. It is intentionally not in `HRMS.slnx`, has no EF Core/provider/API/SMTP dependency, and has no database connection or mutation code.

## Prerequisites

Before running the dry run, an authorized operator must independently verify:

1. The exact active `PlatformSuperAdmin` GUID and email from the authorized catalog read-only procedure.
2. Exactly one active `PlatformSuperAdmin` exists and the account remains unchanged.
3. A restorable backup of the catalog exists; record only a non-secret backup reference.
4. The migration discrepancy is tracked separately. This utility must not apply or mark migrations.

Run with no password in arguments:

```powershell
dotnet run --project tools/PlatformPasswordRecovery/PlatformPasswordRecovery.csproj -- --user-id <verified-guid> --email <verified-email> --backup-reference <backup-reference>
```

The operator must type the exact email, `IDENTITY VERIFIED`, `BACKUP VERIFIED`, and a new password twice. Password input is hidden. The password and generated hash are held only in process memory and are never printed, logged, written to disk, or passed as an argument. The utility exits without a database write.

## Future execution scope and approvals

This dry-run utility is not an execution tool. A separately reviewed activation must be created before any recovery write is permitted. That activation must require two separate, recorded approvals: (1) exact-account and restorable-backup approval, and (2) explicit execution approval naming the same account and backup reference. It must refuse multiple matches, inactive/non-`PlatformSuperAdmin` targets, identity drift, missing/failed backup verification, and any migration-history action.

The future transaction must use the application `IdentityPasswordHasher` through `IPasswordHasher`, update exactly one `PlatformUser.PasswordHash`, increment `SecurityRevision` once, revoke all active `PlatformRefreshTokens` for that user, and commit atomically. It must not create accounts, bypass login, send email, alter AVG/security settings, or touch BAD1/MH01 fixtures.

## Rollback and residual risks

Rollback means restoring the verified catalog backup under the approved recovery procedure, or using a separately reviewed compensating password change; neither is implemented here. A password reset invalidates existing platform sessions by revision and refresh-token revocation. Residual risks include incorrect identity transcription, a backup that was not actually restorable, operator disclosure while typing, and future activation code deviating from this scope. Re-verify the account and backup immediately before any future write.
