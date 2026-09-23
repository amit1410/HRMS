using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface IPayrollInputService
{
    Task<Result<PagedResult<PayrollInputTemplateDto>>> GetTemplatesAsync(CancellationToken ct = default);
    Task<Result<PayrollInputTemplateDto>> CreateTemplateAsync(PayrollInputTemplateRequest request, CancellationToken ct = default);
    Task<Result<PayrollInputTemplateDto>> UpdateTemplateAsync(Guid id, PayrollInputTemplateRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollInputBatchDto>>> GetBatchesAsync(PayrollInputBatchQuery query, CancellationToken ct = default);
    Task<Result<PayrollInputBatchDto>> CreateBatchAsync(PayrollInputBatchRequest request, CancellationToken ct = default);
    Task<Result<PayrollInputBatchDto>> UploadAsync(Guid batchId, Stream content, string fileName, CancellationToken ct = default);
    Task<Result<PayrollInputBatchDto>> ValidateAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollInputPreviewDto>> GetPreviewAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollInputLineDto>> UpdateLineAsync(Guid batchId, Guid lineId, PayrollInputLineUpdateRequest request, CancellationToken ct = default);
    Task<Result<bool>> DeleteLineAsync(Guid batchId, Guid lineId, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollInputLineDto>>> GetLinesAsync(Guid batchId, int page, int pageSize, PayrollInputLineStatus? status, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollInputIssueDto>>> GetIssuesAsync(Guid batchId, int page, int pageSize, PayrollInputIssueSeverity? severity, CancellationToken ct = default);
    Task<Result<PayrollInputBatchDto>> SubmitAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollInputBatchDto>> ApproveAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollInputBatchDto>> RejectAsync(Guid batchId, string reason, CancellationToken ct = default);
    Task<Result<PayrollInputBatchDto>> PostAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollInputBatchDto>> CancelAsync(Guid batchId, string reason, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollInputHistoryDto>>> GetHistoryAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportIssuesAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportPreviewAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportResultAsync(Guid batchId, CancellationToken ct = default);
}
