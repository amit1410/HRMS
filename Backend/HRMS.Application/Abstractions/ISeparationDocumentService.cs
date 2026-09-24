using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;

namespace HRMS.Application.Abstractions;

public interface ISeparationDocumentService
{
    Task<Result<IReadOnlyList<SeparationDocumentTemplateDto>>> GetTemplatesAsync(CancellationToken ct = default);
    Task<Result<SeparationDocumentTemplateDto>> CreateTemplateAsync(SeparationDocumentTemplateRequest request, CancellationToken ct = default);
    Task<Result<SeparationDocumentTemplateDto>> AddVersionAsync(Guid templateId, SeparationDocumentTemplateVersionRequest request, CancellationToken ct = default);
    Task<Result<SeparationDocumentTemplateDto>> PublishVersionAsync(Guid templateId, Guid versionId, int expectedConcurrencyVersion, CancellationToken ct = default);
    Task<Result<SeparationDocumentPreviewDto>> PreviewAsync(Guid versionId, SeparationDocumentPreviewRequest request, CancellationToken ct = default);
    Task<Result<SeparationDocumentReadinessDto>> GetReadinessAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<SeparationGeneratedDocumentDto>> GenerateAsync(Guid separationId, SeparationDocumentGenerateRequest request, CancellationToken ct = default);
    Task<Result<SeparationGeneratedDocumentDto>> ApproveAsync(Guid id, SeparationDocumentApproveRequest request, CancellationToken ct = default);
    Task<Result<SeparationGeneratedDocumentDto>> IssueAsync(Guid id, int expectedConcurrencyVersion, CancellationToken ct = default);
    Task<Result<SeparationGeneratedDocumentDto>> SupersedeAsync(Guid id, SeparationDocumentSupersedeRequest request, CancellationToken ct = default);
    Task<Result<SeparationGeneratedDocumentDto>> CancelAsync(Guid id, SeparationDocumentCancelRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeparationGeneratedDocumentDto>>> GetForSeparationAsync(Guid separationId, bool selfOnly = false, CancellationToken ct = default);
    Task<Result<PagedResult<SeparationDocumentDashboardItemDto>>> DashboardAsync(SeparationDocumentDashboardQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeparationDocumentEventDto>>> HistoryAsync(Guid id, CancellationToken ct = default);
    Task<Result<(byte[] Content, string FileName, string ContentType)>> DownloadAsync(Guid id, bool selfOnly = false, CancellationToken ct = default);
}
