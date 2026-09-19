using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Configuration-driven statutory orchestration. Legal rates and ceilings are deliberately data, not code.
/// </summary>
public sealed class StatutoryPayrollService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock) : IStatutoryPayrollService
{
    public async Task<Result<StatutoryCalculationSummary>> CalculateAsync(PayrollResult result, IReadOnlyList<PayrollResultComponent> components, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || result.TenantId != tenantId)
            return Result<StatutoryCalculationSummary>.Forbidden("The payroll result does not belong to the current tenant.");

        var profile = await db.EmployeeStatutoryProfiles.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == result.EmployeeId && x.IsActive && x.EffectiveFrom <= result.PeriodEndDate && (x.EffectiveTo == null || x.EffectiveTo >= result.PeriodEndDate))
            .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        if (profile is null) return Result<StatutoryCalculationSummary>.Success(new(0, 0, []), "No statutory profile is configured.");

        var types = new[]
        {
            (StatutoryType.ProvidentFund, profile.PfApplicable),
            (StatutoryType.Esi, profile.EsiApplicable),
            (StatutoryType.ProfessionalTax, profile.ProfessionalTaxApplicable),
            (StatutoryType.IncomeTax, profile.IncomeTaxApplicable)
        };
        var output = new List<PayrollStatutoryResult>();
        foreach (var (type, applicable) in types.Where(x => x.Item2))
        {
            var configRows = await db.StatutoryConfigurations.AsNoTracking().Include(x => x.Versions)
                .Where(x => x.TenantId == tenantId && x.IsActive && x.StatutoryType == type && x.JurisdictionCode == profile.JurisdictionCode && (x.StateCode == null || x.StateCode == profile.StateCode)).ToListAsync(ct);
            var configs = configRows.SelectMany(x => x.Versions.Where(v => v.Status == StatutoryConfigurationStatus.Active && v.EffectiveFrom <= result.PeriodEndDate && (v.EffectiveTo == null || v.EffectiveTo >= result.PeriodEndDate)).Select(v => new { Configuration = x, Version = v })).OrderByDescending(x => x.Version.Priority).ToList();
            if (configs.Count == 0) continue;
            var selected = configs[0];
            if (configs.Skip(1).Any(x => x.Version.Priority == selected.Version.Priority))
                return Result<StatutoryCalculationSummary>.Conflict($"Statutory configuration is ambiguous for {type}.");

            var rules = ReadRules(selected.Version.ConfigurationJson);
            var basis = await ResolveBasisAsync(tenantId, selected.Version.Id, result, components, ct);
            if (basis is null && type is StatutoryType.ProvidentFund or StatutoryType.Esi)
                return Result<StatutoryCalculationSummary>.Invalid("basis", $"No statutory basis is configured for {type}.");
            var amountBasis = basis ?? result.GrossEarnings;
            var appliedBasis = rules.WageCeiling is > 0 ? Math.Min(amountBasis, rules.WageCeiling.Value) : amountBasis;
            decimal employeeAmount;
            decimal employerAmount;
            decimal? rate = rules.EmployeeRate;
            if (type is StatutoryType.ProfessionalTax or StatutoryType.IncomeTax)
            {
                var slab = await db.StatutorySlabs.AsNoTracking().Where(x => x.TenantId == tenantId && x.StatutoryConfigurationVersionId == selected.Version.Id && x.FromAmount <= appliedBasis && (x.ToAmount == null || x.ToAmount >= appliedBasis) && (x.OptionalMonth == null || x.OptionalMonth == result.PeriodEndDate.Month)).OrderBy(x => x.Sequence).FirstOrDefaultAsync(ct);
                if (slab is null) continue;
                employeeAmount = PayrollRoundingPolicy.RoundMoney(slab.FixedAmount + appliedBasis * slab.Rate / 100m);
                employerAmount = 0;
                rate = slab.Rate;
            }
            else
            {
                employeeAmount = PayrollRoundingPolicy.RoundMoney(appliedBasis * (rules.EmployeeRate ?? 0) / 100m);
                employerAmount = PayrollRoundingPolicy.RoundMoney(appliedBasis * (rules.EmployerRate ?? 0) / 100m);
            }
            var statutory = new PayrollStatutoryResult
            {
                Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = result.Id, PayrollRunId = result.PayrollRunId, EmployeeId = result.EmployeeId,
                StatutoryType = type, JurisdictionCode = profile.JurisdictionCode, StatutoryConfigurationId = selected.Configuration.Id, StatutoryConfigurationVersionId = selected.Version.Id,
                CalculationBasis = appliedBasis, EmployeeAmount = employeeAmount, EmployerAmount = employerAmount, TotalAmount = employeeAmount + employerAmount,
                AppliedRate = rate, AppliedCeiling = rules.WageCeiling, CalculationMetadata = JsonSerializer.Serialize(new { type, selected.Version.Id, profile.StateCode }), CreatedAtUtc = clock.GetUtcNow().UtcDateTime, PayrollResult = result
            };
            db.PayrollStatutoryResults.Add(statutory);
            output.Add(statutory);
        }
        await db.SaveChangesAsync(ct);
        return Result<StatutoryCalculationSummary>.Success(new(output.Sum(x => x.EmployeeAmount), output.Sum(x => x.EmployerAmount), output));
    }

    public async Task<Result<PagedResult<StatutoryConfigurationDto>>> GetConfigurationsAsync(StatutoryConfigurationQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PagedResult<StatutoryConfigurationDto>>.Unauthorized("No authenticated tenant.");
        var source = db.StatutoryConfigurations.AsNoTracking().Where(x => x.TenantId == tid);
        if (!string.IsNullOrWhiteSpace(query.Search)) source = source.Where(x => x.Code.Contains(query.Search) || x.Name.Contains(query.Search));
        if (!string.IsNullOrWhiteSpace(query.JurisdictionCode)) source = source.Where(x => x.JurisdictionCode == query.JurisdictionCode);
        if (query.StatutoryType is { } type) source = source.Where(x => x.StatutoryType == type);
        if (query.IsActive is { } active) source = source.Where(x => x.IsActive == active);
        var count = await source.CountAsync(ct); var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);
        var rows = await source.Include(x => x.Versions).OrderBy(x => x.Code).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return Result<PagedResult<StatutoryConfigurationDto>>.Success(new(rows.Select(ToDto).ToList(), page, size, count));
    }

    public async Task<Result<StatutoryConfigurationDto>> GetConfigurationAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<StatutoryConfigurationDto>.Unauthorized("No authenticated tenant.");
        var item = await db.StatutoryConfigurations.AsNoTracking().Include(x => x.Versions).FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        return item is null ? Result<StatutoryConfigurationDto>.NotFound("Statutory configuration not found.") : Result<StatutoryConfigurationDto>.Success(ToDto(item));
    }

    public async Task<Result<StatutoryConfigurationDto>> CreateConfigurationAsync(StatutoryConfigurationRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<StatutoryConfigurationDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.JurisdictionCode)) return Result<StatutoryConfigurationDto>.Invalid("Configuration code, name and jurisdiction are required.");
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.StatutoryConfigurations.AnyAsync(x => x.TenantId == tid && x.Code == code, ct)) return Result<StatutoryConfigurationDto>.Conflict("A statutory configuration with this code already exists.");
        var item = new StatutoryConfiguration { Id = Guid.NewGuid(), TenantId = tid, Code = code, Name = request.Name.Trim(), JurisdictionCode = request.JurisdictionCode.Trim().ToUpperInvariant(), StateCode = request.StateCode?.Trim().ToUpperInvariant(), StatutoryType = request.StatutoryType, IsActive = request.IsActive };
        db.StatutoryConfigurations.Add(item); db.StatutoryConfigurationHistories.Add(History(item, StatutoryConfigurationChangeType.Created)); await db.SaveChangesAsync(ct);
        return Result<StatutoryConfigurationDto>.Success(ToDto(item), "Statutory configuration created.");
    }

    public async Task<Result<StatutoryConfigurationVersionDto>> AddVersionAsync(Guid configurationId, StatutoryConfigurationVersionRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<StatutoryConfigurationVersionDto>.Unauthorized("No authenticated tenant.");
        if (request.EffectiveTo < request.EffectiveFrom) return Result<StatutoryConfigurationVersionDto>.Invalid("effectiveTo", "Effective To cannot be earlier than Effective From.");
        var config = await db.StatutoryConfigurations.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == configurationId, ct);
        if (config is null) return Result<StatutoryConfigurationVersionDto>.NotFound("Statutory configuration not found.");
        var overlaps = await db.StatutoryConfigurationVersions.AnyAsync(x => x.TenantId == tid && x.StatutoryConfigurationId == configurationId && x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) && (x.EffectiveTo == null || x.EffectiveTo >= request.EffectiveFrom), ct);
        if (overlaps) return Result<StatutoryConfigurationVersionDto>.Conflict("Statutory configuration versions cannot overlap.");
        var version = new StatutoryConfigurationVersion { Id = Guid.NewGuid(), TenantId = tid, StatutoryConfigurationId = configurationId, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, Status = request.Status, Priority = request.Priority, ConfigurationJson = JsonSerializer.Serialize(request.Rules), CreatedByUserId = tenant.UserId };
        db.StatutoryConfigurationVersions.Add(version);
        foreach (var basis in request.Basis ?? []) db.StatutoryComponentBasis.Add(new() { Id = Guid.NewGuid(), TenantId = tid, StatutoryConfigurationVersionId = version.Id, SalaryComponentId = basis.SalaryComponentId, Include = basis.Include, Weight = basis.Weight });
        foreach (var slab in request.Slabs ?? []) db.StatutorySlabs.Add(new() { Id = Guid.NewGuid(), TenantId = tid, StatutoryConfigurationVersionId = version.Id, FromAmount = slab.FromAmount, ToAmount = slab.ToAmount, Rate = slab.Rate, FixedAmount = slab.FixedAmount, Sequence = slab.Sequence, OptionalMonth = slab.OptionalMonth });
        db.StatutoryConfigurationHistories.Add(History(config, StatutoryConfigurationChangeType.VersionAdded)); await db.SaveChangesAsync(ct);
        return Result<StatutoryConfigurationVersionDto>.Success(ToDto(version), "Statutory configuration version added.");
    }

    public async Task<Result<EmployeeStatutoryProfileDto>> GetProfileAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<EmployeeStatutoryProfileDto>.Unauthorized("No authenticated tenant.");
        var p = await db.EmployeeStatutoryProfiles.AsNoTracking().Where(x => x.TenantId == tid && x.EmployeeId == employeeId).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        return p is null ? Result<EmployeeStatutoryProfileDto>.NotFound("Employee statutory profile not found.") : Result<EmployeeStatutoryProfileDto>.Success(ToDto(p));
    }

    public async Task<Result<EmployeeStatutoryProfileDto>> SaveProfileAsync(Guid employeeId, EmployeeStatutoryProfileRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<EmployeeStatutoryProfileDto>.Unauthorized("No authenticated tenant.");
        if (!await db.Employees.AnyAsync(x => x.TenantId == tid && x.Id == employeeId, ct)) return Result<EmployeeStatutoryProfileDto>.NotFound("Employee not found.");
        if (request.EffectiveTo < request.EffectiveFrom) return Result<EmployeeStatutoryProfileDto>.Invalid("effectiveTo", "Effective To cannot be earlier than Effective From.");
        if (await db.EmployeeStatutoryProfiles.AnyAsync(x => x.TenantId == tid && x.EmployeeId == employeeId && x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) && (x.EffectiveTo == null || x.EffectiveTo >= request.EffectiveFrom), ct)) return Result<EmployeeStatutoryProfileDto>.Conflict("Employee statutory profiles cannot overlap.");
        var p = new EmployeeStatutoryProfile { Id = Guid.NewGuid(), TenantId = tid, EmployeeId = employeeId, JurisdictionCode = request.JurisdictionCode.Trim().ToUpperInvariant(), StateCode = request.StateCode?.Trim().ToUpperInvariant(), PfApplicable = request.PfApplicable, Uan = request.Uan, EsiApplicable = request.EsiApplicable, EsiNumber = request.EsiNumber, ProfessionalTaxApplicable = request.ProfessionalTaxApplicable, IncomeTaxApplicable = request.IncomeTaxApplicable, TaxRegime = request.TaxRegime, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, IsActive = request.IsActive };
        db.EmployeeStatutoryProfiles.Add(p); db.EmployeeStatutoryProfileHistories.Add(new() { Id = Guid.NewGuid(), TenantId = tid, EmployeeStatutoryProfileId = p.Id, EmployeeId = employeeId, ChangeType = StatutoryProfileChangeType.Created, ChangedAtUtc = clock.GetUtcNow().UtcDateTime, SnapshotJson = JsonSerializer.Serialize(request) }); await db.SaveChangesAsync(ct);
        return Result<EmployeeStatutoryProfileDto>.Success(ToDto(p), "Employee statutory profile saved.");
    }

    public async Task<Result<IReadOnlyList<PayrollStatutoryResultDto>>> GetResultsAsync(Guid payrollRunId, Guid employeeId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<IReadOnlyList<PayrollStatutoryResultDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.PayrollStatutoryResults.AsNoTracking().Where(x => x.TenantId == tid && x.PayrollRunId == payrollRunId && x.EmployeeId == employeeId).OrderBy(x => x.StatutoryType).Select(x => new PayrollStatutoryResultDto(x.Id, x.StatutoryType, x.JurisdictionCode, x.StatutoryConfigurationId, x.StatutoryConfigurationVersionId, x.CalculationBasis, x.EmployeeAmount, x.EmployerAmount, x.TotalAmount, x.AppliedRate, x.AppliedCeiling, x.CalculationMetadata)).ToListAsync(ct);
        return Result<IReadOnlyList<PayrollStatutoryResultDto>>.Success(rows);
    }

    private async Task<decimal?> ResolveBasisAsync(Guid tid, Guid versionId, PayrollResult result, IReadOnlyList<PayrollResultComponent> components, CancellationToken ct)
    {
        var ids = await db.StatutoryComponentBasis.AsNoTracking().Where(x => x.TenantId == tid && x.StatutoryConfigurationVersionId == versionId && x.Include).ToListAsync(ct);
        if (ids.Count == 0) return null;
        return ids.Sum(x => components.Where(c => c.SalaryComponentId == x.SalaryComponentId).Sum(c => c.CalculatedAmount * x.Weight / 100m));
    }

    private static StatutoryConfigurationRules ReadRules(string json) => JsonSerializer.Deserialize<StatutoryConfigurationRules>(json) ?? new(null, null, null, null, null, null);
    private static StatutoryConfigurationDto ToDto(StatutoryConfiguration x) => new(x.Id, x.JurisdictionCode, x.StateCode, x.StatutoryType, x.Code, x.Name, x.IsActive, x.ConcurrencyVersion, x.Versions.OrderByDescending(v => v.EffectiveFrom).Select(ToDto).ToList());
    private static StatutoryConfigurationVersionDto ToDto(StatutoryConfigurationVersion x) => new(x.Id, x.EffectiveFrom, x.EffectiveTo, x.Status, x.Priority, x.ConfigurationJson);
    private static EmployeeStatutoryProfileDto ToDto(EmployeeStatutoryProfile x) => new(x.Id, x.EmployeeId, x.JurisdictionCode, x.StateCode, x.PfApplicable, x.Uan, x.EsiApplicable, x.EsiNumber, x.ProfessionalTaxApplicable, x.IncomeTaxApplicable, x.TaxRegime, x.EffectiveFrom, x.EffectiveTo, x.IsActive);
    private StatutoryConfigurationHistory History(StatutoryConfiguration x, StatutoryConfigurationChangeType type) => new() { Id = Guid.NewGuid(), TenantId = x.TenantId, StatutoryConfigurationId = x.Id, ChangeType = type, ChangedAtUtc = clock.GetUtcNow().UtcDateTime, SnapshotJson = JsonSerializer.Serialize(new { x.Code, x.Name, x.JurisdictionCode, x.StateCode, x.StatutoryType, x.IsActive }) };
}
