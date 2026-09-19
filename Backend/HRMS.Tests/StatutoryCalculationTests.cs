using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class StatutoryCalculationTests
{
    [Theory]
    [InlineData(StatutoryType.ProvidentFund, 1000, 1200)]
    [InlineData(StatutoryType.Esi, 750, 1250)]
    [InlineData(StatutoryType.ProfessionalTax, 500, 0)]
    [InlineData(StatutoryType.IncomeTax, 500, 0)]
    public async Task Configured_statutory_type_calculates_from_explicit_test_configuration(StatutoryType statutoryType, int expectedEmployee, int expectedEmployer)
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var runId = Guid.NewGuid(); var resultId = Guid.NewGuid(); var componentId = Guid.NewGuid(); var configId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var assignmentId = Guid.NewGuid(); var structureId = Guid.NewGuid(); var runEmployeeId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext()))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = "STAT" + tenantId.ToString("N")[..8], TenantName = "Statutory", Host = tenantId + ".test", ShardKey = tenantId.ToString("N") });
            seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "STAT-001", FirstName = "Stat", LastName = "Test", Email = employeeId + "@test.local", DateOfJoining = new(2026, 1, 1) });
            seed.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "STAT-P", Name = "Statutory period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Closed });
            seed.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "STAT-001", Status = PayrollRunStatus.Calculated });
            seed.SalaryComponents.Add(new SalaryComponent { Id = componentId, TenantId = tenantId, Code = "BASIC", Name = "Basic", ComponentType = SalaryComponentType.Earning, EffectiveFrom = new(2026, 1, 1), IsActive = true });
            seed.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "STAT-STRUCT", Name = "Statutory structure", IsActive = true });
            seed.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2026, 1, 1), IsActive = true });
            seed.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new(2026, 1, 1), CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
            seed.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = new(2026, 9, 30), IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
            seed.PayrollResults.Add(new PayrollResult { Id = resultId, TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, PeriodStartDate = new(2026, 9, 1), PeriodEndDate = new(2026, 9, 30), EmploymentSnapshotDate = new(2026, 9, 30), CalendarDays = 30, EligibleDays = 30, GrossEarnings = 10000, TotalDeductions = 0, NetPay = 10000, CurrencyCode = "INR" });
            seed.PayrollResultComponents.Add(new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = resultId, SalaryComponentId = componentId, ComponentCode = "BASIC", ComponentName = "Basic", ComponentType = SalaryComponentType.Earning, CalculatedAmount = 10000, IsEarning = true });
            var rules = statutoryType switch { StatutoryType.ProvidentFund => "{\"EmployeeRate\":10,\"EmployerRate\":12,\"WageCeiling\":15000}", StatutoryType.Esi => "{\"EmployeeRate\":7.5,\"EmployerRate\":12.5,\"WageCeiling\":15000}", _ => "{}" };
            seed.StatutoryConfigurations.Add(new StatutoryConfiguration { Id = configId, TenantId = tenantId, Code = $"{statutoryType}-CONFIG", Name = $"Configured {statutoryType}", JurisdictionCode = "IN", StatutoryType = statutoryType, IsActive = true });
            seed.StatutoryConfigurationVersions.Add(new StatutoryConfigurationVersion { Id = versionId, TenantId = tenantId, StatutoryConfigurationId = configId, EffectiveFrom = new(2026, 1, 1), Status = StatutoryConfigurationStatus.Active, ConfigurationJson = rules });
            if (statutoryType is StatutoryType.ProvidentFund or StatutoryType.Esi) seed.StatutoryComponentBasis.Add(new StatutoryComponentBasis { Id = Guid.NewGuid(), TenantId = tenantId, StatutoryConfigurationVersionId = versionId, SalaryComponentId = componentId, Include = true });
            if (statutoryType is StatutoryType.ProfessionalTax or StatutoryType.IncomeTax) seed.StatutorySlabs.Add(new StatutorySlab { Id = Guid.NewGuid(), TenantId = tenantId, StatutoryConfigurationVersionId = versionId, FromAmount = 0, ToAmount = null, Rate = 5, FixedAmount = 0, Sequence = 1 });
            seed.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, JurisdictionCode = "IN", PfApplicable = statutoryType == StatutoryType.ProvidentFund, EsiApplicable = statutoryType == StatutoryType.Esi, ProfessionalTaxApplicable = statutoryType == StatutoryType.ProfessionalTax, IncomeTaxApplicable = statutoryType == StatutoryType.IncomeTax, EffectiveFrom = new(2026, 1, 1) });
            await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var result = await db.PayrollResults.SingleAsync(x => x.Id == resultId); var component = await db.PayrollResultComponents.Where(x => x.PayrollResultId == resultId).ToListAsync();
        var summary = await new StatutoryPayrollService(db, new TestTenantContext(tenantId), TimeProvider.System).CalculateAsync(result, component);
        Assert.True(summary.Succeeded, summary.Message); Assert.Equal(expectedEmployee, summary.Value!.EmployeeAmount); Assert.Equal(expectedEmployer, summary.Value.EmployerAmount);
        Assert.Equal(configId, await db.PayrollStatutoryResults.Select(x => x.StatutoryConfigurationId).SingleAsync());
    }
}
