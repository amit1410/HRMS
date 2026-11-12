using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeCodeConfigurationService
{
    Task<Result<EmployeeCodeConfigurationDto>> GetAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<EmployeeCodeConfigurationDto>>> GetVersionsAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<EmployeeCodeConfigurationAuditDto>>> GetAuditAsync(Guid versionId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeCodeConfigurationDto>> SaveAsync(EmployeeCodeConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<EmployeeCodeRuleDto>>> GetRulesAsync(CancellationToken cancellationToken = default);
    Task<Result<EmployeeCodeRuleDto>> GetRuleAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<EmployeeCodeRuleDto>> SaveRuleAsync(Guid? id, EmployeeCodeRuleRequest request, CancellationToken cancellationToken = default);
    Task<Result<EmployeeCodeRuleDto>> SoftDeleteRuleAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<EmployeeCodePreviewDto>> PreviewAsync(EmployeeCodePreviewRequest request, CancellationToken cancellationToken = default);
}
