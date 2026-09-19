using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed record StatutoryConfigurationRules(decimal? EmployeeRate, decimal? EmployerRate, decimal? WageCeiling, decimal? FixedAmount, int? OptionalMonth, string? TaxRegime);
public sealed class StatutoryConfigurationQuery : PagedQuery { public string? JurisdictionCode { get; set; } public StatutoryType? StatutoryType { get; set; } public bool? IsActive { get; set; } }
public sealed record StatutoryConfigurationRequest(string JurisdictionCode, string? StateCode, StatutoryType StatutoryType, string Code, string Name, bool IsActive = true);
public sealed record StatutoryConfigurationVersionRequest(DateOnly EffectiveFrom, DateOnly? EffectiveTo, StatutoryConfigurationStatus Status, int Priority, StatutoryConfigurationRules Rules, IReadOnlyList<StatutoryComponentBasisRequest>? Basis = null, IReadOnlyList<StatutorySlabRequest>? Slabs = null);
public sealed record StatutoryComponentBasisRequest(Guid SalaryComponentId, bool Include = true, decimal Weight = 100m);
public sealed record StatutorySlabRequest(decimal FromAmount, decimal? ToAmount, decimal Rate, decimal FixedAmount, int Sequence, int? OptionalMonth = null);
public sealed record StatutoryConfigurationVersionDto(Guid Id, DateOnly EffectiveFrom, DateOnly? EffectiveTo, StatutoryConfigurationStatus Status, int Priority, string ConfigurationJson);
public sealed record StatutoryConfigurationDto(Guid Id, string JurisdictionCode, string? StateCode, StatutoryType StatutoryType, string Code, string Name, bool IsActive, int ConcurrencyVersion, IReadOnlyList<StatutoryConfigurationVersionDto> Versions);
public sealed record EmployeeStatutoryProfileRequest(string JurisdictionCode, string? StateCode, bool PfApplicable, string? Uan, bool EsiApplicable, string? EsiNumber, bool ProfessionalTaxApplicable, bool IncomeTaxApplicable, string? TaxRegime, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive = true);
public sealed record EmployeeStatutoryProfileDto(Guid Id, Guid EmployeeId, string JurisdictionCode, string? StateCode, bool PfApplicable, string? Uan, bool EsiApplicable, string? EsiNumber, bool ProfessionalTaxApplicable, bool IncomeTaxApplicable, string? TaxRegime, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);
public sealed record PayrollStatutoryResultDto(Guid Id, StatutoryType StatutoryType, string JurisdictionCode, Guid ConfigurationId, Guid ConfigurationVersionId, decimal CalculationBasis, decimal EmployeeAmount, decimal EmployerAmount, decimal TotalAmount, decimal? AppliedRate, decimal? AppliedCeiling, string CalculationMetadata);
public sealed record StatutoryCalculationSummary(decimal EmployeeAmount, decimal EmployerAmount, IReadOnlyList<PayrollStatutoryResult> Results);
