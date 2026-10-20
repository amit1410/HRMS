using HRMS.Application.Common;
using HRMS.Application.DTOs.Communication;

namespace HRMS.Application.Abstractions;

public interface IWelcomeInvitationService
{
    Task<Result<WelcomeInvitationList>> ListAsync(WelcomeInvitationQuery query, CancellationToken cancellationToken = default);
    Task<Result<WelcomeInvitationPreview>> PreviewAsync(WelcomeInvitationSelectionRequest request, CancellationToken cancellationToken = default);
    Task<Result<WelcomeInvitationSendResult>> SendAsync(WelcomeInvitationSelectionRequest request, CancellationToken cancellationToken = default);
    Task<Result<WelcomeInvitationSendResult>> RetryAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<WelcomeInvitationBatchStatusDto>> GetBatchStatusAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<WelcomeInvitationHistoryRow>>> HistoryAsync(Guid employeeId, PagedQuery query, CancellationToken cancellationToken = default);
}

public interface IWelcomeInvitationProcessor
{
    Task ProcessAsync(CancellationToken cancellationToken = default);
    Task ProcessAttemptAsync(Guid attemptId, CancellationToken cancellationToken = default);
    Task<WelcomeInvitationBatchPreview> PreviewBatchAsync(Guid tenantId, Guid batchId, CancellationToken cancellationToken = default);
    Task<int> ProcessBatchAsync(Guid tenantId, Guid batchId, bool operatorConfirmed, CancellationToken cancellationToken = default);
    Task ProcessSingleAttemptAsync(Guid tenantId, Guid batchId, Guid attemptId, bool operatorConfirmed, CancellationToken cancellationToken = default);
}

public sealed record WelcomeInvitationDryRunRow(Guid AttemptId, string MaskedRecipient, string Status);
public sealed record WelcomeInvitationExcludedRow(Guid AttemptId, string MaskedRecipient, string Reason);
public sealed record WelcomeInvitationBatchPreview(IReadOnlyList<WelcomeInvitationDryRunRow> Eligible, IReadOnlyList<WelcomeInvitationExcludedRow> Excluded)
{
    public int EligibleCount => Eligible.Count;
}
