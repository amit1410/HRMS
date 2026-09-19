using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace HRMS.Tests;

public sealed class PayrollOutputTests
{
    [Fact]
    public async Task Generates_immutable_payslip_lines_from_the_persisted_result_without_recalculation()
    {
        using var database = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var runId = Guid.NewGuid(); var resultId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var assignmentId = Guid.NewGuid(); var runEmployeeId = Guid.NewGuid();
        await using (var setup = database.CreateContext(new TestTenantContext()))
        {
            setup.Tenants.Add(new Tenant { Id = tenant, TenantCode = "OUT" + tenant.ToString("N")[..8], TenantName = "Output tenant", Host = tenant + ".test", ShardKey = tenant.ToString("N") }); await setup.SaveChangesAsync();
        }
        await using (var db = database.CreateContext(new TestTenantContext(tenant)))
        {
            var period = new PayrollPeriod { Id = periodId, TenantId = tenant, Code = "OUT-SEP", Name = "Output September", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Locked };
            var employee = new Employee { Id = employeeId, TenantId = tenant, EmployeeCode = "OUT-001", FirstName = "Output", LastName = "Employee", Email = "output@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active };
            var componentId = Guid.NewGuid(); var structure = new SalaryStructure { Id = Guid.NewGuid(), TenantId = tenant, Code = "OUT-STRUCT", Name = "Output structure", IsActive = true };
            var component = new SalaryComponent { Id = componentId, TenantId = tenant, Code = "BASIC", Name = "Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, AffectsGross = true, AffectsNetPay = true };
            var version = new SalaryStructureVersion { Id = Guid.NewGuid(), TenantId = tenant, SalaryStructureId = structure.Id, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
            var assignment = new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenant, EmployeeId = employeeId, SalaryStructureId = structure.Id, SalaryStructureVersionId = version.Id, EffectiveFrom = new DateOnly(2026, 1, 1), MonthlyCtc = 10000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active };
            var runEmployee = new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenant, PayrollRunId = runId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structure.Id, SalaryStructureVersionId = version.Id, EmploymentSnapshotDate = period.EndDate, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible };
            var result = new PayrollResult { Id = resultId, TenantId = tenant, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structure.Id, SalaryStructureVersionId = version.Id, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = period.StartDate, PeriodEndDate = period.EndDate, EmploymentSnapshotDate = period.EndDate, CalendarDays = 30, EligibleDays = 30, ProrationFactor = 1, CurrencyCode = "INR", GrossEarnings = 10000, TotalDeductions = 1000, NetPay = 9000, Status = PayrollResultStatus.Calculated, IsCurrent = true, Components = [new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenant, SalaryComponentId = componentId, ComponentCode = "BASIC", ComponentName = "Basic", ComponentType = SalaryComponentType.Earning, CalculatedAmount = 10000, UnproratedAmount = 10000, IsEarning = true, CalculationSource = "Structure", CalculationSequence = 1 }] };
            db.Employees.Add(employee); db.SalaryComponents.Add(component); db.SalaryStructures.Add(structure); db.SalaryStructureVersions.Add(version); db.EmployeeSalaryAssignments.Add(assignment); db.PayrollPeriods.Add(period); db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenant, PayrollPeriodId = periodId, RunNumber = "OUT-RUN", Status = PayrollRunStatus.Finalized, EmployeeCount = 1 }); db.PayrollRunEmployees.Add(runEmployee); db.PayrollResults.Add(result); await db.SaveChangesAsync();
            var service = new PayrollOutputService(db, new TestTenantContext(tenant), new LinkedIdentity(tenant, employeeId), TimeProvider.System);
            var generated = await service.GenerateAsync(runId); Assert.True(generated.Succeeded, generated.Message); var payslip = Assert.Single(generated.Value!); Assert.Equal(9000, payslip.NetPay); Assert.Single(payslip.Lines); Assert.Equal(1, await db.PayrollResults.CountAsync());
            var published = await service.PublishAsync(runId); Assert.True(published.Succeeded, published.Message); Assert.Equal(PayslipStatus.Published, Assert.Single(published.Value!).Status);
            var own = await service.GetOwnAsync(new PayrollOutputQuery()); Assert.True(own.Succeeded, own.Message); Assert.Single(own.Value!.Items);
            var register = await service.GetRegisterAsync(runId, new PayrollOutputQuery { PageSize = 1 }); Assert.True(register.Succeeded, register.Message); Assert.Single(register.Value!.Items); Assert.Equal(9000, register.Value.Items[0].NetPay);
            var export = await service.ExportRegisterAsync(runId, new PayrollOutputQuery()); Assert.True(export.Succeeded, export.Message); Assert.Contains("OUT-001", Encoding.UTF8.GetString(export.Value!.Content));
        }
    }

    private sealed record LinkedIdentity(Guid TenantId, Guid EmployeeId) : IEmployeeIdentityResolver
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Task<HRMS.Application.Common.Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(HRMS.Application.Common.Result<RuntimeEmployeeIdentity>.Success(new(TenantId, UserId, EmployeeId)));
    }
}
