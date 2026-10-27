# Welcome invitation operator

The operator writes structured audit events through the `WelcomeInvitationOperator` logger category. With the default `Host.CreateDefaultBuilder` logging configuration, events are emitted to the process console (standard error). The preview event records the selected tenant, shard, physical database, batch, eligible and excluded counts, and `ReadOnlyPreview` outcome. Processing records the target and processed count. Recipient addresses are masked in preview output; SMTP credentials are never printed.

Preview does not validate SMTP settings, claim rows, write database state, or invoke account/email delivery. Processing requires configured SMTP with port 587 and STARTTLS, exact allowlist and catalog identity matches, and the exact confirmation phrase.

Read-only batch preview command template. Replace each placeholder with values verified and approved for the target environment (provided for a separately authorized operator window; **not executed**):

```powershell
dotnet run --project Backend/HRMS.WelcomeInvitationOperator -- preview --tenant-id <TENANT_ID> --tenant-code <TENANT_CODE> --shard <SHARD_KEY> --database <DATABASE_NAME> --batch-id <BATCH_ID>
```

Single-attempt processing template. Replace each placeholder with values verified and approved for the target environment (provided for a separately authorized operator window; **not executed**):

```powershell
dotnet run --project Backend/HRMS.WelcomeInvitationOperator -- process --tenant-id <TENANT_ID> --tenant-code <TENANT_CODE> --shard <SHARD_KEY> --database <DATABASE_NAME> --batch-id <BATCH_ID> --attempt-id <ATTEMPT_ID> --confirm 'PROCESS <TENANT_ID> <BATCH_ID> <ATTEMPT_ID>'
```

The single-attempt claim updates only the row matching tenant ID, batch ID, and attempt ID, and atomically checks status, retry schedule, lease expiry, and retry limit. A failed claim stops before delivery. Database claiming prevents concurrent workers from owning the same live lease. It cannot guarantee exactly-once email delivery: SMTP may accept the message and the subsequent database status update may fail. A later retry could then send a duplicate; operators should inspect delivery state before manually retrying after an ambiguous failure.
