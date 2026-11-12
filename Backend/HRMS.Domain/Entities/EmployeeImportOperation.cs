using System.Security.Cryptography;
using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>Durable, tenant-scoped identity and lifecycle for one employee workbook import.</summary>
public sealed class EmployeeImportOperation : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ImportBatchId { get; set; }
    public string WorkbookSha256 { get; set; } = string.Empty;
    public string ConfirmationTokenHash { get; set; } = string.Empty;
    public EmployeeImportOperationState State { get; private set; } = EmployeeImportOperationState.Prepared;
    public Guid InitiatedByUserId { get; set; }
    public DateTime PreparedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? ClaimLeaseId { get; private set; }
    public DateTime? ClaimLeaseExpiresAtUtc { get; private set; }
    public int TotalRows { get; set; }
    public int SucceededRows { get; private set; }
    public int FailedRows { get; private set; }
    public int SkippedRows { get; private set; }
    public string? SanitizedSummary { get; private set; }
    public Guid ConcurrencyStamp { get; private set; } = Guid.NewGuid();
    public ICollection<EmployeeImportRow> Rows { get; set; } = new List<EmployeeImportRow>();

    public bool TransitionTo(EmployeeImportOperationState next, DateTime nowUtc, string? sanitizedSummary = null)
    {
        if (!IsAllowed(State, next)) return false;
        if (next == EmployeeImportOperationState.Completed &&
            (SucceededRows != TotalRows || FailedRows != 0 || SkippedRows != 0)) return false;
        if (next == EmployeeImportOperationState.PartiallyCompleted &&
            (SucceededRows == 0 || SucceededRows + FailedRows + SkippedRows != TotalRows)) return false;
        if (next == EmployeeImportOperationState.Failed && FailedRows + SkippedRows < TotalRows - SucceededRows) return false;
        State = next;
        ConcurrencyStamp = Guid.NewGuid();
        if (next is EmployeeImportOperationState.Queued or EmployeeImportOperationState.Claimed) ConfirmedAtUtc ??= nowUtc;
        if (next == EmployeeImportOperationState.Processing) StartedAtUtc = nowUtc;
        if (next is EmployeeImportOperationState.Completed or EmployeeImportOperationState.Failed or EmployeeImportOperationState.PartiallyCompleted or EmployeeImportOperationState.Cancelled)
        {
            CompletedAtUtc = nowUtc;
            ClaimLeaseId = null;
            ClaimLeaseExpiresAtUtc = null;
        }
        SanitizedSummary = sanitizedSummary;
        return true;
    }

    public bool AcquireOrRecoverClaim(Guid leaseId, DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (leaseId == Guid.Empty || leaseDuration <= TimeSpan.Zero) return false;
        if (State == EmployeeImportOperationState.Prepared)
        {
            if (ClaimLeaseId is not null && ClaimLeaseExpiresAtUtc > nowUtc) return false;
            if (!TransitionTo(EmployeeImportOperationState.Claimed, nowUtc)) return false;
        }
        else if (State is not (EmployeeImportOperationState.Claimed or EmployeeImportOperationState.Processing) ||
                 ClaimLeaseExpiresAtUtc is null || ClaimLeaseExpiresAtUtc > nowUtc)
            return false;

        ClaimLeaseId = leaseId;
        ClaimLeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    public bool RenewClaim(Guid leaseId, DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (ClaimLeaseId != leaseId || ClaimLeaseExpiresAtUtc <= nowUtc ||
            State is not (EmployeeImportOperationState.Claimed or EmployeeImportOperationState.Processing)) return false;
        ClaimLeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    public bool Enqueue(string confirmationTokenHash, DateTime nowUtc)
    {
        if (State != EmployeeImportOperationState.Prepared || ConfirmedAtUtc is not null ||
            !FixedHashEquals(ConfirmationTokenHash, confirmationTokenHash)) return false;
        return TransitionTo(EmployeeImportOperationState.Queued, nowUtc);
    }

    public bool AcquireWorkerClaim(Guid leaseId, DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (leaseId == Guid.Empty || leaseDuration <= TimeSpan.Zero) return false;
        if (State == EmployeeImportOperationState.Queued)
        {
            if (!TransitionTo(EmployeeImportOperationState.Claimed, nowUtc)) return false;
        }
        else if (State is not (EmployeeImportOperationState.Claimed or EmployeeImportOperationState.Processing) ||
                 ClaimLeaseId is null || ClaimLeaseExpiresAtUtc > nowUtc)
            return false;

        ClaimLeaseId = leaseId;
        ClaimLeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    public bool BeginArtifactPreparation(Guid leaseId, DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (State != EmployeeImportOperationState.Prepared || leaseId == Guid.Empty || leaseDuration <= TimeSpan.Zero) return false;
        State = EmployeeImportOperationState.ArtifactPending;
        ClaimLeaseId = leaseId;
        ClaimLeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    public bool AcquireArtifactPreparationLease(Guid leaseId, string confirmationTokenHash, DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (leaseId == Guid.Empty || leaseDuration <= TimeSpan.Zero || ConfirmedAtUtc is not null ||
            State is not (EmployeeImportOperationState.ArtifactPending or EmployeeImportOperationState.Prepared) ||
            (ClaimLeaseId is not null && ClaimLeaseExpiresAtUtc > nowUtc)) return false;
        ConfirmationTokenHash = confirmationTokenHash;
        ClaimLeaseId = leaseId;
        ClaimLeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    public bool CompleteArtifactPreparation(Guid leaseId, DateTime nowUtc, string? sanitizedSummary = null)
    {
        if (leaseId == Guid.Empty || ClaimLeaseId != leaseId || ClaimLeaseExpiresAtUtc <= nowUtc ||
            State is not (EmployeeImportOperationState.ArtifactPending or EmployeeImportOperationState.Prepared)) return false;
        if (State == EmployeeImportOperationState.ArtifactPending)
        {
            if (!TransitionTo(EmployeeImportOperationState.Prepared, nowUtc, sanitizedSummary)) return false;
        }
        ClaimLeaseId = null;
        ClaimLeaseExpiresAtUtc = null;
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    public bool ReleaseArtifactPreparationLease(Guid leaseId)
    {
        if (leaseId == Guid.Empty || ClaimLeaseId != leaseId ||
            State is not (EmployeeImportOperationState.ArtifactPending or EmployeeImportOperationState.Prepared)) return false;
        ClaimLeaseId = null;
        ClaimLeaseExpiresAtUtc = null;
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    public void RecordRow(EmployeeImportRowStatus status)
    {
        if (SucceededRows + FailedRows + SkippedRows >= TotalRows)
            throw new InvalidOperationException("Import operation already has terminal outcomes for all rows.");
        switch (status)
        {
            case EmployeeImportRowStatus.Succeeded: SucceededRows++; break;
            case EmployeeImportRowStatus.Failed: FailedRows++; break;
            case EmployeeImportRowStatus.Skipped: SkippedRows++; break;
            default: throw new ArgumentOutOfRangeException(nameof(status), "Only terminal row outcomes affect operation counts.");
        }
        ConcurrencyStamp = Guid.NewGuid();
    }

    public bool ReconcileForRecovery(int rowSucceeded, int rowFailed, int rowSkipped, DateTime nowUtc, string sanitizedSummary)
    {
        if (State != EmployeeImportOperationState.Processing || ClaimLeaseId is null ||
            ClaimLeaseExpiresAtUtc is null || ClaimLeaseExpiresAtUtc <= nowUtc ||
            rowSucceeded != SucceededRows || rowFailed != FailedRows || rowSkipped != SkippedRows ||
            rowSucceeded < 0 || rowFailed < 0 || rowSkipped < 0 || rowSucceeded + rowFailed + rowSkipped > TotalRows) return false;
        FailedRows = 0;
        SkippedRows = 0;
        SanitizedSummary = sanitizedSummary.Length <= 500 ? sanitizedSummary : sanitizedSummary[..500];
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    public static bool IsAllowed(EmployeeImportOperationState current, EmployeeImportOperationState next) => (current, next) switch
    {
        (EmployeeImportOperationState.Prepared, EmployeeImportOperationState.Queued or EmployeeImportOperationState.Claimed or EmployeeImportOperationState.Cancelled or EmployeeImportOperationState.Failed) => true,
        (EmployeeImportOperationState.Queued, EmployeeImportOperationState.Claimed or EmployeeImportOperationState.Cancelled or EmployeeImportOperationState.Failed) => true,
        (EmployeeImportOperationState.ArtifactPending, EmployeeImportOperationState.Prepared or EmployeeImportOperationState.Failed or EmployeeImportOperationState.Cancelled) => true,
        (EmployeeImportOperationState.Claimed, EmployeeImportOperationState.Processing or EmployeeImportOperationState.Cancelled or EmployeeImportOperationState.Failed) => true,
        (EmployeeImportOperationState.Processing, EmployeeImportOperationState.Completed or EmployeeImportOperationState.Failed or EmployeeImportOperationState.PartiallyCompleted or EmployeeImportOperationState.Cancelled) => true,
        _ => false
    };

    private static bool FixedHashEquals(string expected, string supplied)
    {
        if (expected.Length != 64 || supplied.Length != 64) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), Convert.FromHexString(supplied));
        }
        catch (FormatException) { return false; }
    }
}
