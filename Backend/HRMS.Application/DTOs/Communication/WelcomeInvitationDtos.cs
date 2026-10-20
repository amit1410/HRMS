using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Communication;

public sealed class WelcomeInvitationQuery : PagedQuery
{
    public string? EmployeeCode { get; set; }
    public string? WorkEmail { get; set; }
    public Guid? HoldingCompanyId { get; set; }
    public Guid? LobId { get; set; }
    public Guid? OrganisationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? WorkLocationId { get; set; }
    public string? InvitationStatus { get; set; }
    public string? PortalStatus { get; set; }
    public DateOnly? JoiningDateFrom { get; set; }
    public DateOnly? JoiningDateTo { get; set; }
}

public sealed record WelcomeInvitationRow(
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string WorkEmail,
    DateOnly JoiningDate,
    string PortalAccountStatus,
    string InvitationStatus,
    DateTime? LastInvitationDateUtc,
    DateTime? ActivationDateUtc,
    bool CanSend,
    bool CanResend,
    bool CanRetry);

public sealed record WelcomeInvitationSummary(int NotInvited, int Pending, int Sent, int Failed, int Activated);

public sealed record WelcomeInvitationList(PagedResult<WelcomeInvitationRow> Page, WelcomeInvitationSummary Summary);

public sealed class WelcomeInvitationSelectionRequest
{
    public List<Guid> EmployeeIds { get; set; } = [];
    public WelcomeInvitationFilterContract? Filter { get; set; }
    public bool Confirmed { get; set; }
    public bool SelectAllMatching { get; set; }
    public bool Resend { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class WelcomeInvitationFilterContract
{
    public string? Search { get; set; }
    public string? EmployeeCode { get; set; }
    public string? WorkEmail { get; set; }
    public Guid? HoldingCompanyId { get; set; }
    public Guid? LobId { get; set; }
    public Guid? OrganisationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? WorkLocationId { get; set; }
    public DateOnly? JoiningDateFrom { get; set; }
    public DateOnly? JoiningDateTo { get; set; }
    public string? PortalStatus { get; set; }
    public string? InvitationStatus { get; set; }
}

public sealed record WelcomeInvitationPreview(int SelectedCount, int EligibleCount, int IneligibleCount, int MissingEmailCount, int AlreadyActiveCount, int AlreadyInvitedCount, IReadOnlyList<string> Messages);
public sealed record WelcomeInvitationSendResult(Guid BatchId, int TotalRequested, int Accepted, int Rejected, IReadOnlyList<string> Messages);
public sealed record WelcomeInvitationHistoryRow(Guid AttemptId, DateTime RequestedAtUtc, string AttemptType, string DeliveryStatus, string RecipientEmail, Guid RequestedByUserId, DateTime? SentAtUtc, string? ErrorSummary);
public sealed record WelcomeInvitationBatchStatusDto(Guid BatchId, int TotalRequested, int Accepted, int Rejected, int Queued, int Processing, int Sent, int Failed, int Completed, string Status, DateTime CreatedAtUtc, DateTime? CompletedAtUtc);
