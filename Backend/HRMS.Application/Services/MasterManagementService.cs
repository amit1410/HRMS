using System.Collections;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Masters;
using HRMS.Domain.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>Shared CRUD for tenant-owned organizational masters. Country is global and deliberately handled by its existing service.</summary>
public sealed class MasterManagementService : IMasterManagementService
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;

    public MasterManagementService(IHrmsDbContext db, ITenantContext tenant)
    { _db = db; _tenant = tenant; }

    public async Task<Result<MasterManagementPage>> GetAsync(string kind, MasterManagementQuery query, CancellationToken cancellationToken = default)
    {
        var definition = DefinitionFor(kind);
        if (definition is null) return Result<MasterManagementPage>.NotFound("Master type is not supported.");
        if (_tenant.TenantId is not Guid) return Result<MasterManagementPage>.Unauthorized("No authenticated tenant.");

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var result = await definition.List(query with { Page = page, PageSize = pageSize }, cancellationToken);
        var parents = await definition.ParentMap(cancellationToken);
        return Result<MasterManagementPage>.Success(new MasterManagementPage(result.Rows.Select(x => ToDto(x, definition, parents)).ToList(), page, pageSize, result.Total));
    }

    public async Task<Result<MasterManagementRecordDto>> GetByIdAsync(string kind, Guid id, CancellationToken cancellationToken = default)
    {
        var definition = DefinitionFor(kind);
        if (definition is null) return Result<MasterManagementRecordDto>.NotFound("Master type is not supported.");
        if (_tenant.TenantId is not Guid) return Result<MasterManagementRecordDto>.Unauthorized("No authenticated tenant.");
        var entity = await definition.Find(id, cancellationToken);
        return entity is null
            ? Result<MasterManagementRecordDto>.NotFound("Master record not found.")
            : Result<MasterManagementRecordDto>.Success(ToDto(entity, definition, await definition.ParentMap(cancellationToken)));
    }

    public Task<Result<MasterManagementRecordDto>> CreateAsync(string kind, MasterManagementRequest request, CancellationToken cancellationToken = default) =>
        WriteAsync(kind, null, request, cancellationToken);

    public Task<Result<MasterManagementRecordDto>> UpdateAsync(string kind, Guid id, MasterManagementRequest request, CancellationToken cancellationToken = default) =>
        WriteAsync(kind, id, request, cancellationToken);

    public async Task<Result<bool>> DeleteAsync(string kind, Guid id, CancellationToken cancellationToken = default)
    {
        if (kind.Trim().Equals("countries", StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Conflict("Countries are shared reference data and cannot be changed from the tenant Masters screen.");
        var definition = DefinitionFor(kind);
        if (definition is null) return Result<bool>.NotFound("Master type is not supported.");
        if (_tenant.TenantId is not Guid) return Result<bool>.Unauthorized("No authenticated tenant.");
        var entity = await definition.Find(id, cancellationToken);
        if (entity is null) return Result<bool>.NotFound("Master record not found.");
        if (await HasReferencesAsync(kind, id, cancellationToken))
            return Result<bool>.Conflict("This record is referenced by existing employees, employment history, child masters, or employee-code rules. Deactivate it instead; no data was deleted.");
        Set(entity, "IsActive", false);
        await _db.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true, "Master record deactivated. Existing references were preserved.");
    }

    private async Task<bool> HasReferencesAsync(string kind, Guid id, CancellationToken ct)
    {
        var key = kind.Trim().ToLowerInvariant();
        if (key == "holding-companies") return await _db.LinesOfBusiness.AnyAsync(x => x.HoldingCompanyId == id, ct) || await EmploymentReferencesAsync(id, EmployeeCodeConditionField.HoldingCompany, ct);
        if (key == "lines-of-business") return await EmploymentReferencesAsync(id, EmployeeCodeConditionField.Lob, ct);
        if (key == "organisations") return await EmploymentReferencesAsync(id, EmployeeCodeConditionField.Organisation, ct);
        if (key == "sub-departments") return await EmploymentReferencesAsync(id, EmployeeCodeConditionField.SubDepartment, ct);
        if (key == "sections") return await EmploymentReferencesAsync(id, EmployeeCodeConditionField.Section, ct);
        if (key == "sub-sections") return await EmploymentReferencesAsync(id, EmployeeCodeConditionField.SubSection, ct);
        if (key == "functions") return await _db.SubFunctions.AnyAsync(x => x.FunctionId == id, ct) || await EmploymentReferencesAsync(id, EmployeeCodeConditionField.Function, ct);
        if (key == "sub-functions") return await EmploymentReferencesAsync(id, EmployeeCodeConditionField.SubFunction, ct);
        if (key == "grades") return await EmploymentReferencesAsync(id, EmployeeCodeConditionField.Grade, ct);
        if (key == "employee-types") return await EmploymentReferencesAsync(id, EmployeeCodeConditionField.EmployeeType, ct);
        if (key == "work-locations") return await EmploymentReferencesAsync(id, EmployeeCodeConditionField.WorkLocation, ct);
        if (key == "cost-centers") return await EmploymentReferencesAsync(id, EmployeeCodeConditionField.CostCenter, ct);
        if (key == "position-change-reasons") return await _db.EmployeeEmploymentHistory.AnyAsync(x => x.PositionChangeReasonId == id, ct);
        if (key == "countries") return await _db.States.AnyAsync(x => x.CountryId == id, ct) || await _db.EmployeeEmploymentHistory.AnyAsync(x => x.CountryLocationId == id, ct) || await _db.Employees.AnyAsync(x => x.BirthCountryId == id, ct);
        return false;
    }

    private async Task<bool> EmploymentReferencesAsync(Guid id, EmployeeCodeConditionField field, CancellationToken ct)
    {
        var historyReferences = field switch
        {
            EmployeeCodeConditionField.HoldingCompany => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.HoldingCompanyId == id, ct),
            EmployeeCodeConditionField.Lob => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.LobId == id, ct),
            EmployeeCodeConditionField.Organisation => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.OrganisationId == id, ct),
            EmployeeCodeConditionField.SubDepartment => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.SubDepartmentId == id, ct),
            EmployeeCodeConditionField.Section => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.SectionId == id, ct),
            EmployeeCodeConditionField.SubSection => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.SubSectionId == id, ct),
            EmployeeCodeConditionField.Function => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.FunctionId == id, ct),
            EmployeeCodeConditionField.SubFunction => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.SubFunctionId == id, ct),
            EmployeeCodeConditionField.Grade => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.GradeId == id, ct),
            EmployeeCodeConditionField.EmployeeType => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.EmployeeTypeId == id, ct),
            EmployeeCodeConditionField.WorkLocation => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.WorkLocationId == id, ct),
            EmployeeCodeConditionField.CostCenter => await _db.EmployeeEmploymentHistory.AnyAsync(x => x.CostCenterId == id, ct),
            _ => false
        };
        var employeeReferences = field switch
        {
            EmployeeCodeConditionField.Department => await _db.Employees.AnyAsync(x => x.DepartmentId == id, ct),
            EmployeeCodeConditionField.Designation => await _db.Employees.AnyAsync(x => x.DesignationId == id, ct),
            EmployeeCodeConditionField.EmployeeType => await _db.Employees.AnyAsync(x => x.EmployeeTypeId == id, ct),
            EmployeeCodeConditionField.CostCenter => await _db.Employees.AnyAsync(x => x.CostCenterId == id, ct),
            _ => false
        };
        return historyReferences || employeeReferences
            || await _db.EmployeeCodeRuleConditions.AnyAsync(x => x.ReferenceId == id && x.Field == field, ct);
    }

    private async Task<Result<MasterManagementRecordDto>> WriteAsync(string kind, Guid? id, MasterManagementRequest request, CancellationToken cancellationToken)
    {
        if (kind.Trim().Equals("countries", StringComparison.OrdinalIgnoreCase))
            return Result<MasterManagementRecordDto>.Conflict("Countries are shared reference data and cannot be changed from the tenant Masters screen.");
        var definition = DefinitionFor(kind);
        if (definition is null) return Result<MasterManagementRecordDto>.NotFound("Master type is not supported.");
        if (_tenant.TenantId is not Guid tenantId) return Result<MasterManagementRecordDto>.Unauthorized("No authenticated tenant.");
        var code = request.Code.Trim();
        var name = request.Name.Trim();
        if (code.Length is 0 or > 20) return Result<MasterManagementRecordDto>.Invalid("code", "Code is required and must be at most 20 characters.");
        if (name.Length is 0 or > 200) return Result<MasterManagementRecordDto>.Invalid("name", "Name is required and must be at most 200 characters.");
        if (code.Any(c => !char.IsLetterOrDigit(c) && c is not ('.' or '_' or '-' or '/')))
            return Result<MasterManagementRecordDto>.Invalid("code", "Code may contain letters, digits, ., _, - and / only.");

        if (definition.ParentProperty is not null)
        {
            if (definition.ParentRequired && request.ParentId is not Guid)
                return Result<MasterManagementRecordDto>.Invalid("parentId", $"{definition.ParentLabel} is required.");
            if (request.ParentId is Guid parentId)
            {
                var parentExists = await definition.ParentExists!(parentId, cancellationToken);
                if (!parentExists) return Result<MasterManagementRecordDto>.Invalid("parentId", "The selected parent is invalid, inactive, or belongs to another tenant.");
            }
        }

        var conflict = await definition.HasConflict(code, name, id, cancellationToken);
        if (conflict) return Result<MasterManagementRecordDto>.Conflict("Code or name is already used in this organization.");

        object entity;
        if (id is Guid existingId)
        {
            var found = await definition.Find(existingId, cancellationToken);
            if (found is null) return Result<MasterManagementRecordDto>.NotFound("Master record not found.");
            entity = found;
        }
        else
        {
            entity = Activator.CreateInstance(definition.EntityType)!;
            definition.Add(entity);
            Set(entity, "Id", Guid.NewGuid());
            Set(entity, "TenantId", tenantId);
        }
        Set(entity, "Code", code);
        Set(entity, "Name", name);
        Set(entity, "Description", string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());
        Set(entity, "IsActive", request.IsActive);
        if (definition.ParentProperty is not null) Set(entity, definition.ParentProperty, request.ParentId);
        await _db.SaveChangesAsync(cancellationToken);
        return Result<MasterManagementRecordDto>.Success(ToDto(entity, definition, await definition.ParentMap(cancellationToken)), id.HasValue ? "Master record updated." : "Master record created.");
    }

    private static MasterManagementRecordDto ToDto(object entity, Definition definition, IReadOnlyDictionary<Guid, (string Code, string Name)> parents)
    {
        var parentId = definition.ParentProperty is null ? null : (Guid?)Value(entity, definition.ParentProperty);
        var parent = parentId is Guid id && parents.TryGetValue(id, out var value) ? value : default;
        return new((Guid)Value(entity, "Id")!, (string)Value(entity, "Code")!, (string)Value(entity, "Name")!, (string?)Value(entity, "Description"), (bool)Value(entity, "IsActive")!, parentId, parent.Code, parent.Name);
    }

    private static object? Value(object entity, string property) => entity.GetType().GetProperty(property)?.GetValue(entity);
    private static void Set(object entity, string property, object? value) => entity.GetType().GetProperty(property)?.SetValue(entity, value);

    private sealed record Definition(
        Type EntityType,
        Action<object> Add,
        string? ParentProperty,
        string ParentLabel,
        bool ParentRequired,
        Func<MasterManagementQuery, CancellationToken, Task<(List<object> Rows, int Total)>> List,
        Func<Guid, CancellationToken, Task<object?>> Find,
        Func<string, string, Guid?, CancellationToken, Task<bool>> HasConflict,
        Func<Guid, CancellationToken, Task<bool>>? ParentExists,
        Func<CancellationToken, Task<Dictionary<Guid, (string Code, string Name)>>> ParentMap);

    private Definition? DefinitionFor(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "holding-companies" => Build(_db.HoldingCompanies, x => _db.HoldingCompanies.Add(x), null, "Parent"),
        "lines-of-business" => Build(_db.LinesOfBusiness, x => _db.LinesOfBusiness.Add(x), "HoldingCompanyId", "Holding Company", BuildParent(_db.HoldingCompanies)),
        "organisations" => Build(_db.Organisations, x => _db.Organisations.Add(x), null, "Parent"),
        "departments" => Build(_db.Departments, x => _db.Departments.Add(x), null, "Parent"),
        "sub-departments" => Build(_db.SubDepartments, x => _db.SubDepartments.Add(x), "DepartmentId", "Department", BuildParent(_db.Departments), true),
        "sections" => Build(_db.Sections, x => _db.Sections.Add(x), "SubDepartmentId", "Sub Department", BuildParent(_db.SubDepartments)),
        "sub-sections" => Build(_db.SubSections, x => _db.SubSections.Add(x), "SectionId", "Section", BuildParent(_db.Sections)),
        "functions" => Build(_db.Functions, x => _db.Functions.Add(x), null, "Parent"),
        "sub-functions" => Build(_db.SubFunctions, x => _db.SubFunctions.Add(x), "FunctionId", "Function", BuildParent(_db.Functions)),
        "grades" => Build(_db.Grades, x => _db.Grades.Add(x), null, "Parent"),
        "designations" => Build(_db.Designations, x => _db.Designations.Add(x), null, "Parent"),
        "employee-types" => Build(_db.EmployeeTypes, x => _db.EmployeeTypes.Add(x), null, "Parent"),
        "work-locations" => Build(_db.WorkLocations, x => _db.WorkLocations.Add(x), null, "Parent"),
        "cost-centers" => Build(_db.CostCenters, x => _db.CostCenters.Add(x), null, "Parent"),
        "position-change-reasons" => Build(_db.PositionChangeReasons, x => _db.PositionChangeReasons.Add(x), null, "Parent"),
        "countries" => Build(_db.Countries, x => _db.Countries.Add(x), null, "Parent"),
        _ => null
    };

    private static Definition Build<TEntity>(DbSet<TEntity> set, Action<TEntity> add, string? parentProperty, string parentLabel, Definition? parent = null, bool parentRequired = false)
        where TEntity : class => new(typeof(TEntity), x => add((TEntity)x), parentProperty, parentLabel, parentRequired,
            (query, ct) => ListAsync(set, query, parentProperty, ct),
            (id, ct) => FindAsync(set, id, ct),
            (code, name, exclude, ct) => ConflictAsync(set, code, name, exclude, ct),
            parent is null ? null : parent.Find is null ? null : (id, ct) => ParentExistsAsync(parent, id, ct),
            parent is null ? _ => Task.FromResult(new Dictionary<Guid, (string Code, string Name)>()) : parent.ParentMap);

    private static Definition Build<TEntity, TParent>(DbSet<TEntity> set, Action<TEntity> add, string parentProperty, string parentLabel, Definition parent, bool parentRequired = false)
        where TEntity : class where TParent : class => Build(set, add, parentProperty, parentLabel, parent, parentRequired);

    private static Definition BuildParent<TEntity>(DbSet<TEntity> set) where TEntity : class
    {
        var definition = Build(set, _ => { }, null, "Parent");
        return definition with { ParentMap = ct => ParentMapAsync(set, ct) };
    }

    private static async Task<(List<object> Rows, int Total)> ListAsync<TEntity>(DbSet<TEntity> set, MasterManagementQuery query, string? parentProperty, CancellationToken ct) where TEntity : class
    {
        IQueryable<TEntity> source = set.AsNoTracking();
        if (query.IsActive is bool active) source = source.Where(x => EF.Property<bool>(x, "IsActive") == active);
        if (query.ParentId is Guid parent && parentProperty is not null) source = source.Where(x => EF.Property<Guid?>(x, parentProperty) == parent);
        if (!string.IsNullOrWhiteSpace(query.Search)) { var search = query.Search.Trim().ToLowerInvariant(); source = source.Where(x => EF.Property<string>(x, "Code").ToLower().Contains(search) || EF.Property<string>(x, "Name").ToLower().Contains(search)); }
        var total = await source.CountAsync(ct);
        var typedRows = await source.OrderBy(x => EF.Property<string>(x, "Code")).ThenBy(x => EF.Property<Guid>(x, "Id")).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return (typedRows.Cast<object>().ToList(), total);
    }

    // Keep this lookup tracked: update and deactivation mutate the returned entity.
    // List and parent lookups remain no-tracking, so read-only catalogue requests do not track rows.
    private static async Task<object?> FindAsync<TEntity>(DbSet<TEntity> set, Guid id, CancellationToken ct) where TEntity : class => await set.FirstOrDefaultAsync(x => EF.Property<Guid>(x, "Id") == id, ct);
    private static Task<bool> ConflictAsync<TEntity>(DbSet<TEntity> set, string code, string name, Guid? exclude, CancellationToken ct) where TEntity : class => set.AnyAsync(x => (!exclude.HasValue || EF.Property<Guid>(x, "Id") != exclude) && (EF.Property<string>(x, "Code").ToLower() == code.ToLower() || EF.Property<string>(x, "Name").ToLower() == name.ToLower()), ct);
    private static async Task<bool> ParentExistsAsync(Definition parent, Guid id, CancellationToken ct)
    {
        var record = await parent.Find(id, ct);
        return record is not null && (bool)Value(record, "IsActive")!;
    }

    private static async Task<Dictionary<Guid, (string Code, string Name)>> ParentMapAsync<TEntity>(DbSet<TEntity> set, CancellationToken ct) where TEntity : class
    {
        var rows = await set.AsNoTracking().ToListAsync(ct);
        return rows.ToDictionary(x => (Guid)Value(x, "Id")!, x => ((string)Value(x, "Code")!, (string)Value(x, "Name")!));
    }
}
