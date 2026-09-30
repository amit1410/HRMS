using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Banks;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>
/// Bank master business logic.
/// <para>
/// Every read and write goes through <c>_db.Banks</c>, which the DbContext has a tenant global query filter
/// on, so a row belonging to another organization simply is not there. Nothing in this class accepts or
/// compares a caller-supplied tenant id; the only tenant it knows is the one <see cref="ITenantContext"/>
/// resolved from the authenticated token.
/// </para>
/// <para>
/// Uniqueness of code and name is pre-checked here rather than left to the unique index. The index is still
/// the real guarantee, but a duplicate is a client mistake reported as a conflict, with the exception path
/// kept as a backstop for the concurrent case.
/// </para>
/// </summary>
public class BankService : IBankService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Bank not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<BankService> _logger;

    public BankService(IHrmsDbContext db, ITenantContext tenantContext, ILogger<BankService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<PagedResult<BankDto>>> GetAsync(BankQuery query, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<PagedResult<BankDto>>.Unauthorized(NoTenantMessage);
        }

        var page = await Project(ApplySort(Filter(query), query)).ToPagedResultAsync(query, cancellationToken);
        return Result<PagedResult<BankDto>>.Success(page);
    }

    public async Task<Result<IReadOnlyList<BankDto>>> GetAllForExportAsync(BankQuery query, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<IReadOnlyList<BankDto>>.Unauthorized(NoTenantMessage);
        }

        var rows = await Project(ApplySort(Filter(query), query)).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<BankDto>>.Success(rows);
    }

    public async Task<Result<BankDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<BankDto>.Unauthorized(NoTenantMessage);
        }

        var bank = await Project(_db.Banks.AsNoTracking().Where(b => b.Id == id)).FirstOrDefaultAsync(cancellationToken);
        return bank is null ? Result<BankDto>.NotFound(NotFoundMessage) : Result<BankDto>.Success(bank);
    }

    public async Task<Result<BankDto>> CreateAsync(BankRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<BankDto>.Unauthorized(NoTenantMessage);
        }

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(code, name, excludeId: null, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        var bank = new Bank
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = name,
            ShortName = Normalize(request.ShortName),
            IfscPrefix = NormalizeUpper(request.IfscPrefix),
            BankType = request.BankType,
            Country = Normalize(request.Country),
            EffectiveFrom = request.EffectiveFrom,
            Remarks = Normalize(request.Remarks),
            IsActive = request.IsActive
        };

        _db.Banks.Add(bank);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(code, name, excludeId: null, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            _logger.LogWarning("Bank create for tenant {TenantId} lost a uniqueness race on the database index.", tenantId);
            return raced;
        }

        _logger.LogInformation("Created bank {BankId} in tenant {TenantId}.", bank.Id, tenantId);
        return Result<BankDto>.Success(ToDto(bank, employeeAccountCount: 0), "Bank created.");
    }

    public async Task<Result<BankDto>> UpdateAsync(Guid id, BankRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<BankDto>.Unauthorized(NoTenantMessage);
        }

        var bank = await _db.Banks.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bank is null)
        {
            return Result<BankDto>.NotFound(NotFoundMessage);
        }

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(code, name, excludeId: id, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        bank.Code = code;
        bank.Name = name;
        bank.ShortName = Normalize(request.ShortName);
        bank.IfscPrefix = NormalizeUpper(request.IfscPrefix);
        bank.BankType = request.BankType;
        bank.Country = Normalize(request.Country);
        bank.EffectiveFrom = request.EffectiveFrom;
        bank.Remarks = Normalize(request.Remarks);
        bank.IsActive = request.IsActive;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(code, name, excludeId: id, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            _logger.LogWarning("Bank update for {BankId} lost a uniqueness race on the database index.", id);
            return raced;
        }

        _logger.LogInformation("Updated bank {BankId} in tenant {TenantId}.", id, tenantId);
        var count = await _db.EmployeeBankDetails.CountAsync(e => e.BankId == id, cancellationToken);
        return Result<BankDto>.Success(ToDto(bank, count), "Bank updated.");
    }

    public async Task<Result<BankDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<BankDto>.Unauthorized(NoTenantMessage);
        }

        var bank = await _db.Banks.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bank is null)
        {
            return Result<BankDto>.NotFound(NotFoundMessage);
        }

        if (bank.IsActive != isActive)
        {
            bank.IsActive = isActive;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Set bank {BankId} active={IsActive} in tenant {TenantId}.", id, isActive, tenantId);
        }

        var count = await _db.EmployeeBankDetails.CountAsync(e => e.BankId == id, cancellationToken);
        return Result<BankDto>.Success(ToDto(bank, count), isActive ? "Bank activated." : "Bank deactivated. Existing references were preserved.");
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<bool>.Unauthorized(NoTenantMessage);
        }

        var bank = await _db.Banks.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bank is null)
        {
            return Result<bool>.NotFound(NotFoundMessage);
        }

        // A bank referenced by employee bank records is not deleted, because those records would lose the
        // bank they point at. The caller is told to retire it (IsActive = false) instead.
        var count = await _db.EmployeeBankDetails.CountAsync(e => e.BankId == id, cancellationToken);
        if (count > 0)
        {
            return Result<bool>.Conflict(
                $"This bank is referenced by {count} employee bank record(s). Deactivate it instead of deleting it, so the references stay intact.");
        }

        _db.Banks.Remove(bank);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Bank {BankId} could not be deleted; it is still referenced.", id);
            return Result<bool>.Conflict("This bank is still referenced and cannot be deleted. Deactivate it instead.");
        }

        _logger.LogInformation("Deleted bank {BankId} in tenant {TenantId}.", id, tenantId);
        return Result<bool>.Success(true, "Bank deleted.");
    }

    private IQueryable<Bank> Filter(BankQuery query)
    {
        var banks = _db.Banks.AsNoTracking();

        if (query.IsActive.HasValue)
        {
            banks = banks.Where(b => b.IsActive == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Lower-cased explicitly so search behaves the same on SQL Server (case-insensitive collation),
            // MySQL and SQLite (case-sensitive by default).
            var search = query.Search.Trim().ToLowerInvariant();
            banks = banks.Where(b =>
                b.Code.ToLower().Contains(search) ||
                b.Name.ToLower().Contains(search) ||
                (b.ShortName != null && b.ShortName.ToLower().Contains(search)) ||
                (b.IfscPrefix != null && b.IfscPrefix.ToLower().Contains(search)));
        }

        return banks;
    }

    private IQueryable<BankDto> Project(IQueryable<Bank> banks) =>
        banks.Select(b => new BankDto(
            b.Id,
            b.Code,
            b.Name,
            b.ShortName,
            b.IfscPrefix,
            b.BankType,
            b.Country,
            b.EffectiveFrom,
            b.Remarks,
            b.IsActive,
            b.EmployeeBankDetails.Count,
            b.CreatedDate,
            b.ModifiedDate));

    private static IQueryable<Bank> ApplySort(IQueryable<Bank> banks, BankQuery query)
    {
        var descending = query.SortDescending;

        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => descending ? banks.OrderByDescending(b => b.Name) : banks.OrderBy(b => b.Name),
            "shortname" => descending ? banks.OrderByDescending(b => b.ShortName) : banks.OrderBy(b => b.ShortName),
            "banktype" => descending ? banks.OrderByDescending(b => b.BankType) : banks.OrderBy(b => b.BankType),
            "country" => descending ? banks.OrderByDescending(b => b.Country) : banks.OrderBy(b => b.Country),
            "isactive" => descending ? banks.OrderByDescending(b => b.IsActive) : banks.OrderBy(b => b.IsActive),
            "createddate" => descending ? banks.OrderByDescending(b => b.CreatedDate) : banks.OrderBy(b => b.CreatedDate),
            _ => descending ? banks.OrderByDescending(b => b.Code) : banks.OrderBy(b => b.Code)
        };

        return ordered.ThenBy(b => b.Id);
    }

    /// <summary>
    /// Returns a conflict result when another bank in this tenant already uses the code or the name, or null
    /// when the values are free. Comparison is case-insensitive: "SBI" and "sbi" are the same code to a human.
    /// </summary>
    private async Task<Result<BankDto>?> FindConflictAsync(string code, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalizedCode = code.ToLowerInvariant();
        var normalizedName = name.ToLowerInvariant();

        var candidates = _db.Banks.AsNoTracking();
        if (excludeId is Guid exclude)
        {
            candidates = candidates.Where(b => b.Id != exclude);
        }

        var clashes = await candidates
            .Where(b => b.Code.ToLower() == normalizedCode || b.Name.ToLower() == normalizedName)
            .Select(b => new { b.Code, b.Name })
            .ToListAsync(cancellationToken);

        if (clashes.Count == 0)
        {
            return null;
        }

        if (clashes.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<BankDto>.Conflict(
                $"A bank with code '{code}' already exists.",
                [new ValidationError("code", "This code is already in use.")]);
        }

        return Result<BankDto>.Conflict(
            $"A bank named '{name}' already exists.",
            [new ValidationError("name", "This name is already in use.")]);
    }

    private static BankDto ToDto(Bank bank, int employeeAccountCount) =>
        new(
            bank.Id,
            bank.Code,
            bank.Name,
            bank.ShortName,
            bank.IfscPrefix,
            bank.BankType,
            bank.Country,
            bank.EffectiveFrom,
            bank.Remarks,
            bank.IsActive,
            employeeAccountCount,
            bank.CreatedDate,
            bank.ModifiedDate);

    /// <summary>Trims optional text and turns whitespace-only input into null, so "empty" has one form.</summary>
    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? NormalizeUpper(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
