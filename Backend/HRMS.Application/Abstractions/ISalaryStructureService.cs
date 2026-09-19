using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface ISalaryStructureService
{
    Task<Result<PagedResult<SalaryStructureDto>>> GetAsync(SalaryStructureQuery query, CancellationToken ct = default);
    Task<Result<SalaryStructureDto>> GetByIdAsync(Guid id, DateOnly? effectiveOn = null, CancellationToken ct = default);
    Task<Result<SalaryStructureDto>> CreateAsync(SalaryStructureRequest request, CancellationToken ct = default);
    Task<Result<SalaryStructureDto>> UpdateAsync(Guid id, SalaryStructureRequest request, CancellationToken ct = default);
    Task<Result<SalaryStructureDto>> SetActiveAsync(Guid id, bool active, int? expectedConcurrencyVersion, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SalaryStructureHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default);
    Task<Result<SalaryStructureComponentDto>> AddComponentAsync(Guid id, SalaryStructureComponentRequest request, CancellationToken ct = default);
    Task<Result<SalaryStructureComponentDto>> UpdateComponentAsync(Guid id, Guid componentId, SalaryStructureComponentRequest request, CancellationToken ct = default);
    Task<Result<bool>> RemoveComponentAsync(Guid id, Guid componentId, CancellationToken ct = default);
}
