using System.Linq.Expressions;
using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Masters;
using HRMS.Domain.Common;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Lightweight lookup service for all organizational master data. Each method queries a
/// tenant-scoped master DbSet and returns <see cref="MasterLookupDto"/> for dropdown population.
/// </summary>
public class MasterLookupService : IMasterLookupService
{
    private readonly IHrmsDbContext _db;

    public MasterLookupService(IHrmsDbContext db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<MasterLookupDto>> GetHoldingCompaniesAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QueryAsync(_db.HoldingCompanies, query, ct);

    public async Task<IReadOnlyList<MasterLookupDto>> GetLinesOfBusinessAsync(MasterLookupQuery query, CancellationToken ct = default)
    {
        IQueryable<Lob> q = _db.LinesOfBusiness;
        if (query.ActiveOnly) q = q.Where(x => x.IsActive);
        if (query.Search is { Length: > 0 } search) q = q.Where(x => x.Code.Contains(search) || x.Name.Contains(search));
        if (query.ParentId is Guid holdingCompanyId) q = q.Where(x => x.HoldingCompanyId == holdingCompanyId);
        return await q.OrderBy(x => x.Code).Select(x => new MasterLookupDto(x.Id, x.Code, x.Name, x.IsActive)).ToListAsync(ct);
    }

    public Task<IReadOnlyList<MasterLookupDto>> GetOrganisationsAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QueryAsync(_db.Organisations, query, ct);

    public Task<IReadOnlyList<MasterLookupDto>> GetDepartmentsAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QueryAsync(_db.Departments, query, ct);

    public Task<IReadOnlyList<MasterLookupDto>> GetBanksAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QueryAsync(_db.Banks, query, ct);

    public async Task<IReadOnlyList<MasterLookupDto>> GetSubDepartmentsAsync(MasterLookupQuery query, CancellationToken ct = default)
    {
        IQueryable<SubDepartment> q = _db.SubDepartments;
        if (query.ActiveOnly) q = q.Where(s => s.IsActive);
        if (query.Search is { Length: > 0 } search)
            q = q.Where(s => s.Code.Contains(search) || s.Name.Contains(search));
        if (query.ParentId is Guid deptId)
            q = q.Where(s => s.DepartmentId == deptId);

        return await q.OrderBy(s => s.Code)
            .Select(s => new MasterLookupDto(s.Id, s.Code, s.Name, s.IsActive))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MasterLookupDto>> GetSectionsAsync(MasterLookupQuery query, CancellationToken ct = default)
    {
        IQueryable<Section> q = _db.Sections;
        if (query.ActiveOnly) q = q.Where(s => s.IsActive);
        if (query.Search is { Length: > 0 } search)
            q = q.Where(s => s.Code.Contains(search) || s.Name.Contains(search));
        if (query.ParentId is Guid subDeptId)
            q = q.Where(s => s.SubDepartmentId == subDeptId);

        return await q.OrderBy(s => s.Code)
            .Select(s => new MasterLookupDto(s.Id, s.Code, s.Name, s.IsActive))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MasterLookupDto>> GetSubSectionsAsync(MasterLookupQuery query, CancellationToken ct = default)
    {
        IQueryable<SubSection> q = _db.SubSections;
        if (query.ActiveOnly) q = q.Where(s => s.IsActive);
        if (query.Search is { Length: > 0 } search)
            q = q.Where(s => s.Code.Contains(search) || s.Name.Contains(search));
        if (query.ParentId is Guid sectionId)
            q = q.Where(s => s.SectionId == sectionId);

        return await q.OrderBy(s => s.Code)
            .Select(s => new MasterLookupDto(s.Id, s.Code, s.Name, s.IsActive))
            .ToListAsync(ct);
    }

    public Task<IReadOnlyList<MasterLookupDto>> GetFunctionsAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QueryAsync(_db.Functions, query, ct);

    public async Task<IReadOnlyList<MasterLookupDto>> GetSubFunctionsAsync(MasterLookupQuery query, CancellationToken ct = default)
    {
        IQueryable<SubFunction> q = _db.SubFunctions;
        if (query.ActiveOnly) q = q.Where(s => s.IsActive);
        if (query.Search is { Length: > 0 } search)
            q = q.Where(s => s.Code.Contains(search) || s.Name.Contains(search));
        if (query.ParentId is Guid funcId)
            q = q.Where(s => s.FunctionId == funcId);

        return await q.OrderBy(s => s.Code)
            .Select(s => new MasterLookupDto(s.Id, s.Code, s.Name, s.IsActive))
            .ToListAsync(ct);
    }

    public Task<IReadOnlyList<MasterLookupDto>> GetGradesAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QuerySortedAsync(_db.Grades, query, g => g.SortOrder, ct);

    public Task<IReadOnlyList<MasterLookupDto>> GetDesignationsAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QueryAsync(_db.Designations, query, ct);

    public Task<IReadOnlyList<MasterLookupDto>> GetEmployeeTypesAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QuerySortedAsync(_db.EmployeeTypes, query, t => t.SortOrder, ct);

    public Task<IReadOnlyList<MasterLookupDto>> GetWorkLocationsAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QueryAsync(_db.WorkLocations, query, ct);

    public Task<IReadOnlyList<MasterLookupDto>> GetCostCentersAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QueryAsync(_db.CostCenters, query, ct);

    public Task<IReadOnlyList<MasterLookupDto>> GetPositionChangeReasonsAsync(MasterLookupQuery query, CancellationToken ct = default)
        => QuerySortedAsync(_db.PositionChangeReasons, query, r => r.SortOrder, ct);

    // --- Private helpers ---

    private static async Task<IReadOnlyList<MasterLookupDto>> QueryAsync<T>(
        IQueryable<T> source, MasterLookupQuery query, CancellationToken ct)
        where T : class, ITenantEntity
    {
        IQueryable<T> q = source;

        if (query.ActiveOnly)
            q = q.Where(e => EF.Property<bool>(e, "IsActive"));

        if (query.Search is { Length: > 0 } search)
            q = q.Where(e =>
                EF.Property<string>(e, "Code").Contains(search) ||
                EF.Property<string>(e, "Name").Contains(search));

        return await q.OrderBy(e => EF.Property<string>(e, "Code"))
            .Select(e => new MasterLookupDto(
                EF.Property<Guid>(e, "Id"),
                EF.Property<string>(e, "Code"),
                EF.Property<string>(e, "Name"),
                EF.Property<bool>(e, "IsActive")))
            .ToListAsync(ct);
    }

    private static async Task<IReadOnlyList<MasterLookupDto>> QuerySortedAsync<T>(
        IQueryable<T> source, MasterLookupQuery query, Expression<Func<T, int>> sortSelector, CancellationToken ct)
        where T : class, ITenantEntity
    {
        IQueryable<T> q = source;

        if (query.ActiveOnly)
            q = q.Where(e => EF.Property<bool>(e, "IsActive"));

        if (query.Search is { Length: > 0 } search)
            q = q.Where(e =>
                EF.Property<string>(e, "Code").Contains(search) ||
                EF.Property<string>(e, "Name").Contains(search));

        return await q
            .OrderBy(sortSelector)
            .ThenBy(e => EF.Property<string>(e, "Code"))
            .Select(e => new MasterLookupDto(
                EF.Property<Guid>(e, "Id"),
                EF.Property<string>(e, "Code"),
                EF.Property<string>(e, "Name"),
                EF.Property<bool>(e, "IsActive")))
            .ToListAsync(ct);
    }
}
