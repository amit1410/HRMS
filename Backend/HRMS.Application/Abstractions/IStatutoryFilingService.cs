using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IStatutoryFilingService
{
    Task<Result<StatutoryFilingDefinitionDto>> CreateDefinitionAsync(StatutoryFilingDefinitionRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<StatutoryFilingDefinitionDto>>> GetDefinitionsAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<StatutoryFilingConnectionProfileDto>>> GetConnectionProfilesAsync(CancellationToken ct = default);
    Task<Result<StatutoryFilingConnectionProfileDto>> CreateConnectionProfileAsync(StatutoryFilingConnectionProfileRequest request, CancellationToken ct = default);
    Task<Result<StatutoryFilingConnectionProfileDto>> UpdateConnectionProfileAsync(Guid id, StatutoryFilingConnectionProfileRequest request, CancellationToken ct = default);
    Task<Result<StatutoryFilingConnectionProfileDto>> ValidateConnectionProfileAsync(Guid id, CancellationToken ct = default);
    Task<Result<StatutoryFilingRunDto>> CreateRunAsync(StatutoryFilingRunRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<StatutoryFilingRunDto>>> GetRunsAsync(CancellationToken ct = default);
    Task<Result<StatutoryFilingRunDto>> GenerateAsync(Guid runId, CancellationToken ct = default);
    Task<Result<StatutoryFilingRunDto>> ValidateAsync(Guid runId, CancellationToken ct = default);
    Task<Result<StatutoryFilingRunDto>> SubmitForApprovalAsync(Guid runId, CancellationToken ct = default);
    Task<Result<StatutoryFilingRunDto>> ApproveAsync(Guid runId, CancellationToken ct = default);
    Task<Result<StatutoryFilingRunDto>> SubmitAsync(Guid runId, CancellationToken ct = default);
    Task<Result<StatutoryFilingRunDto>> RejectAsync(Guid runId, string reason, CancellationToken ct = default);
    Task<Result<StatutoryFilingRunDto>> AcknowledgeAsync(Guid runId, StatutoryFilingAcknowledgementRequest request, CancellationToken ct = default);
    Task<Result<StatutoryFilingRunDto>> CancelAsync(Guid runId, CancellationToken ct = default);
    Task<Result<StatutoryFilingPackageDto>> GetPackageAsync(Guid runId, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> DownloadAsync(Guid runId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<StatutoryFilingIssueDto>>> GetIssuesAsync(Guid runId, CancellationToken ct = default);
}
