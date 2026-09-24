using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;

namespace HRMS.Application.Abstractions;

public interface IExitInterviewService
{
    Task<Result<ExitInterviewTemplateDto>> CreateTemplateAsync(ExitInterviewTemplateRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ExitInterviewTemplateDto>>> GetTemplatesAsync(CancellationToken ct = default);
    Task<Result<ExitInterviewTemplateVersionDto>> CreateVersionAsync(Guid templateId, ExitInterviewVersionRequest request, CancellationToken ct = default);
    Task<Result<ExitInterviewTemplateVersionDto>> PublishVersionAsync(Guid versionId, CancellationToken ct = default);
    Task<Result<ExitInterviewHrDto>> AssignAsync(Guid separationId, ExitInterviewAssignRequest request, CancellationToken ct = default);
    Task<Result<ExitInterviewEmployeeDto>> GetMineAsync(Guid? separationId = null, CancellationToken ct = default);
    Task<Result<ExitInterviewEmployeeDto>> SaveDraftAsync(Guid separationId, ExitInterviewDraftRequest request, CancellationToken ct = default);
    Task<Result<ExitInterviewEmployeeDto>> SubmitAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<PagedResult<ExitInterviewInboxItemDto>>> GetInboxAsync(ExitInterviewInboxQuery query, CancellationToken ct = default);
    Task<Result<ExitInterviewHrDto>> GetHrAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<ExitInterviewHrDto>> StartHrAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<ExitInterviewHrDto>> AddHrNoteAsync(Guid separationId, ExitInterviewNoteRequest request, CancellationToken ct = default);
    Task<Result<ExitInterviewHrDto>> CompleteAsync(Guid separationId, ExitInterviewCompletionRequest request, CancellationToken ct = default);
    Task<Result<ExitInterviewHrDto>> ReopenAsync(Guid separationId, ExitInterviewReopenRequest request, CancellationToken ct = default);
    Task<Result<ExitInterviewAnalyticsDto>> GetAnalyticsAsync(CancellationToken ct = default);
}
