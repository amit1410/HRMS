using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

internal static class PayrollOutputProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenantContext)
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var runEmployeeId = Guid.NewGuid();
        var structureId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var earningId = Guid.NewGuid();
        var deductionId = Guid.NewGuid();
        var configurationId = Guid.NewGuid();
        var configurationVersionId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            TenantCode = $"OUT{tenantId:N}"[..12],
            TenantName = "Payroll output provider test",
            Host = $"{tenantId:N}.output.test",
            ShardKey = tenantId.ToString("N")
        });
        await db.SaveChangesAsync();
        tenantContext.TenantId = tenantId;

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await SeedAsync(db, tenantId, employeeId, runId, resultId, periodId, assignmentId, runEmployeeId,
                structureId, versionId, earningId, deductionId, configurationId, configurationVersionId);
            var identity = new LinkedIdentity(tenantId, employeeId);
            tenantContext.UserId = identity.UserId;
            var service = new PayrollOutputService(db, tenantContext, identity, TimeProvider.System);

            var beforePublication = await service.GetOwnAsync(new());
            Assert.True(beforePublication.Succeeded, beforePublication.Message);
            Assert.Empty(beforePublication.Value!.Items);

            var first = await service.GenerateAsync(runId);
            Assert.True(first.Succeeded, first.Message);
            var firstSlip = Assert.Single(first.Value!);
            Assert.Equal(9000m, firstSlip.NetPay);
            Assert.Equal(3, firstSlip.Lines.Count);
            Assert.Contains(firstSlip.Lines, x => x.ComponentCode == "BASIC" && x.Amount == 10000m && !x.IsStatutory);
            Assert.Contains(firstSlip.Lines, x => x.ComponentCode == "LOAN" && x.Amount == 1000m && !x.IsStatutory);
            Assert.Contains(firstSlip.Lines, x => x.IsStatutory && x.Amount == 500m && x.EmployerAmount == 1200m);
            Assert.Equal(10000m, firstSlip.GrossEarnings);
            Assert.Equal(1000m, firstSlip.TotalDeductions);

            var second = await service.GenerateAsync(runId);
            Assert.True(second.Succeeded, second.Message);
            var secondSlip = Assert.Single(second.Value!);
            Assert.NotEqual(firstSlip.PayslipNumber, secondSlip.PayslipNumber);
            Assert.Equal(2, secondSlip.Version);
            Assert.Equal(PayslipStatus.Superseded, await db.Payslips.Where(x => x.Id == firstSlip.Id).Select(x => x.Status).SingleAsync());

            var register = await service.GetRegisterAsync(runId, new() { PageSize = 1 });
            Assert.True(register.Succeeded, register.Message);
            Assert.Single(register.Value!.Items);
            Assert.Equal(10000m, register.Value.Items[0].GrossEarnings);
            Assert.Equal(1000m, register.Value.Items[0].TotalDeductions);
            Assert.Equal(9000m, register.Value.Items[0].NetPay);
            Assert.Equal(1, register.Value.TotalPages);

            var export = await service.ExportRegisterAsync(runId, new());
            Assert.True(export.Succeeded, export.Message);
            var csv = Encoding.UTF8.GetString(export.Value!.Content);
            Assert.Contains($"OUT-{employeeId:N}"[..12], csv);
            Assert.Contains("\"Output Employee, Test\"", csv);

            var published = await service.PublishAsync(runId);
            Assert.True(published.Succeeded, published.Message);
            var publishedSlip = Assert.Single(published.Value!);
            Assert.Equal(PayslipStatus.Published, publishedSlip.Status);
            Assert.Empty((await service.GenerateAsync(runId)).Value!);

            var own = await service.GetOwnAsync(new());
            Assert.True(own.Succeeded, own.Message);
            Assert.Single(own.Value!.Items);
            Assert.Equal(publishedSlip.Id, own.Value.Items[0].Id);

            var snapshot = await service.GetAsync(publishedSlip.Id, publishedOnly: true);
            Assert.True(snapshot.Succeeded, snapshot.Message);
            Assert.Equal("Output Employee, Test", snapshot.Value!.EmployeeName);
            Assert.Equal(9000m, snapshot.Value.NetPay);

            var otherTenant = new TestTenantContext(Guid.NewGuid());
            var otherIdentity = new LinkedIdentity(otherTenant.TenantId!.Value, Guid.NewGuid());
            var isolatedService = new PayrollOutputService(db, otherTenant, otherIdentity, TimeProvider.System);
            Assert.False((await isolatedService.GetAsync(publishedSlip.Id)).Succeeded);
            Assert.Empty((await isolatedService.GetRegisterAsync(runId, new())).Value!.Items);
            Assert.Empty((await isolatedService.GetOwnAsync(new())).Value!.Items);
        }
        finally
        {
            await transaction.RollbackAsync();
            db.ClearChangeTracker();
            db.Tenants.Remove(new Tenant { Id = tenantId });
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedAsync(HrmsDbContext db, Guid tenantId, Guid employeeId, Guid runId, Guid resultId,
        Guid periodId, Guid assignmentId, Guid runEmployeeId, Guid structureId, Guid versionId, Guid earningId,
        Guid deductionId, Guid configurationId, Guid configurationVersionId)
    {
        var period = new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = $"OUT-{tenantId:N}"[..12], Name = "Output September", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Locked };
        var employee = new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"OUT-{employeeId:N}"[..12], FirstName = "Output", LastName = "Employee, Test", Email = $"{employeeId:N}@output.test", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active };
        var structure = new SalaryStructure { Id = structureId, TenantId = tenantId, Code = $"STRUCT-{structureId:N}"[..16], Name = "Output structure", IsActive = true };
        var version = new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
        var earning = new SalaryComponent { Id = earningId, TenantId = tenantId, Code = "BASIC", Name = "Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, AffectsGross = true, AffectsNetPay = true };
        var deduction = new SalaryComponent { Id = deductionId, TenantId = tenantId, Code = "LOAN", Name = "Loan, recovery", ComponentType = SalaryComponentType.Deduction, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, AffectsNetPay = true };
        var assignment = new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new DateOnly(2026, 1, 1), MonthlyCtc = 10000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active };
        var runEmployee = new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = period.EndDate, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible };
        var result = new PayrollResult { Id = resultId, TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = period.StartDate, PeriodEndDate = period.EndDate, EmploymentSnapshotDate = period.EndDate, CalendarDays = 30, EligibleDays = 30, ProrationFactor = 1, CurrencyCode = "INR", GrossEarnings = 10000, TotalDeductions = 1000, NetPay = 9000, EmployerContributions = 1200, Status = PayrollResultStatus.Calculated, IsCurrent = true, Components = [new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, SalaryComponentId = earningId, ComponentCode = "BASIC", ComponentName = "Basic", ComponentType = SalaryComponentType.Earning, CalculatedAmount = 10000, UnproratedAmount = 10000, IsEarning = true, CalculationSource = "Structure", CalculationSequence = 1 }, new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, SalaryComponentId = deductionId, ComponentCode = "LOAN", ComponentName = "Loan, recovery", ComponentType = SalaryComponentType.Deduction, CalculatedAmount = 1000, UnproratedAmount = 1000, IsDeduction = true, CalculationSource = "Employee", CalculationSequence = 2 }] };
        var configuration = new StatutoryConfiguration { Id = configurationId, TenantId = tenantId, Code = $"PF-{configurationId:N}"[..12], Name = "Configured PF", StatutoryType = StatutoryType.ProvidentFund, IsActive = true };
        var configurationVersion = new StatutoryConfigurationVersion { Id = configurationVersionId, TenantId = tenantId, StatutoryConfigurationId = configurationId, EffectiveFrom = period.StartDate, Status = StatutoryConfigurationStatus.Active, ConfigurationJson = "{\"employeeRate\":0.05}" };
        db.Employees.Add(employee); db.SalaryComponents.AddRange(earning, deduction); db.SalaryStructures.Add(structure); db.SalaryStructureVersions.Add(version); db.EmployeeSalaryAssignments.Add(assignment); db.PayrollPeriods.Add(period); db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = $"RUN-{runId:N}"[..12], Status = PayrollRunStatus.Finalized, EmployeeCount = 1 }); db.PayrollRunEmployees.Add(runEmployee); db.PayrollResults.Add(result); db.StatutoryConfigurations.Add(configuration); db.StatutoryConfigurationVersions.Add(configurationVersion); db.PayrollStatutoryResults.Add(new PayrollStatutoryResult { Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = resultId, PayrollRunId = runId, EmployeeId = employeeId, StatutoryType = StatutoryType.ProvidentFund, JurisdictionCode = "IN", StatutoryConfigurationId = configurationId, StatutoryConfigurationVersionId = configurationVersionId, CalculationBasis = 10000, EmployeeAmount = 500, EmployerAmount = 1200, TotalAmount = 1700, AppliedRate = 0.05m, CreatedAtUtc = DateTime.UtcNow }); await db.SaveChangesAsync();
    }

    private sealed record LinkedIdentity(Guid TenantId, Guid EmployeeId) : IEmployeeIdentityResolver
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Task<HRMS.Application.Common.Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(HRMS.Application.Common.Result<RuntimeEmployeeIdentity>.Success(new(TenantId, UserId, EmployeeId)));
    }
}
