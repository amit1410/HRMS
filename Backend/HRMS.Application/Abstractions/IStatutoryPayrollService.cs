using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;

namespace HRMS.Application.Abstractions;

public interface IStatutoryPayrollService
{
    Task<Result<StatutoryCalculationSummary>> CalculateAsync(PayrollResult result, IReadOnlyList<PayrollResultComponent> components, CancellationToken ct = default);
    Task<Result<PagedResult<StatutoryConfigurationDto>>> GetConfigurationsAsync(StatutoryConfigurationQuery query, CancellationToken ct = default);
    Task<Result<StatutoryConfigurationDto>> GetConfigurationAsync(Guid id, CancellationToken ct = default);
    Task<Result<StatutoryConfigurationDto>> CreateConfigurationAsync(StatutoryConfigurationRequest request, CancellationToken ct = default);
    Task<Result<StatutoryConfigurationVersionDto>> AddVersionAsync(Guid configurationId, StatutoryConfigurationVersionRequest request, CancellationToken ct = default);
    Task<Result<EmployeeStatutoryProfileDto>> GetProfileAsync(Guid employeeId, CancellationToken ct = default);
    Task<Result<EmployeeStatutoryProfileDto>> SaveProfileAsync(Guid employeeId, EmployeeStatutoryProfileRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollStatutoryResultDto>>> GetResultsAsync(Guid payrollRunId, Guid employeeId, CancellationToken ct = default);
}
