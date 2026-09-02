using HRMS.Application.DTOs.Masters;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Lightweight lookup service for all organizational master data. Returns <see cref="MasterLookupDto"/>
/// lists suitable for dropdown population. Each method corresponds to a master table.
/// </summary>
public interface IMasterLookupService
{
    Task<IReadOnlyList<MasterLookupDto>> GetHoldingCompaniesAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetLinesOfBusinessAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetOrganisationsAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetDepartmentsAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetBanksAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetSubDepartmentsAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetSectionsAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetSubSectionsAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetFunctionsAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetSubFunctionsAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetGradesAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetDesignationsAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetEmployeeTypesAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetWorkLocationsAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetCostCentersAsync(MasterLookupQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MasterLookupDto>> GetPositionChangeReasonsAsync(MasterLookupQuery query, CancellationToken ct = default);
}
