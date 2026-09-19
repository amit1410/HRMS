using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface ISalaryComponentService
{
    Task<Result<PagedResult<SalaryComponentDto>>> GetAsync(SalaryComponentQuery query, CancellationToken ct = default);
    Task<Result<SalaryComponentDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<SalaryComponentDto>> CreateAsync(SalaryComponentRequest request, CancellationToken ct = default);
    Task<Result<SalaryComponentDto>> UpdateAsync(Guid id, SalaryComponentRequest request, CancellationToken ct = default);
    Task<Result<SalaryComponentDto>> SetActiveAsync(Guid id, bool active, int? expectedConcurrencyVersion, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SalaryComponentHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default);
}
