using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class PayrollProductionReadinessTests
{
    [Fact]
    public async Task Hundred_employee_payroll_has_reconciled_results_and_paginated_register()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var structureId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        await SeedTenantAsync(database, tenantId);
        await SeedTenantAsync(database, otherTenantId);

        await using (var db = database.CreateContext(new TestTenantContext(tenantId)))
        {
            var period = new PayrollPeriod
            {
                Id = periodId,
                TenantId = tenantId,
                Code = "LARGE-26",
                Name = "Large acceptance",
                PeriodType = PayrollPeriodType.Monthly,
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 9, 30),
                PayDate = new DateOnly(2026, 10, 5),
                FiscalYear = 2026,
                PeriodNumber = 9,
                Status = PayrollPeriodStatus.Open,
                IsActive = true
            };
            var structure = new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "LARGE", Name = "Large acceptance", IsActive = true };
            var component = new SalaryComponent
            {
                Id = componentId,
                TenantId = tenantId,
                Code = "BASIC",
                Name = "Basic",
                ComponentType = SalaryComponentType.Earning,
                CalculationType = SalaryCalculationType.FixedAmount,
                EffectiveFrom = period.StartDate,
                IsActive = true,
                AffectsGross = true,
                AffectsNetPay = true
            };
            var version = new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = period.StartDate, IsActive = true };
            version.Components.Add(new SalaryStructureComponent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SalaryStructureVersionId = versionId,
                SalaryComponentId = componentId,
                Sequence = 1,
                CalculationType = SalaryStructureCalculationType.FixedAmount,
                Value = 10000m,
                EffectiveFrom = period.StartDate,
                IsActive = true
            });
            var run = new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "LARGE-26-01", Status = PayrollRunStatus.Prepared, EmployeeCount = 100 };
            db.PayrollPeriods.Add(period);
            db.SalaryStructures.Add(structure);
            db.SalaryComponents.Add(component);
            db.SalaryStructureVersions.Add(version);
            db.PayrollRuns.Add(run);

            for (var i = 1; i <= 100; i++)
            {
                var employeeId = Guid.NewGuid();
                var assignmentId = Guid.NewGuid();
                db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"L{i:000}", FirstName = "Large", LastName = $"Employee {i}", Email = $"large-{i}@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
                db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = period.StartDate, MonthlyCtc = 10000m, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
                db.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = period.EndDate, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
            }

            await db.SaveChangesAsync();
            var readiness = await new PayrollReadinessService(db, new TestTenantContext(tenantId)).CheckAsync(runId);
            Assert.True(readiness.Succeeded, readiness.Message);
            Assert.True(readiness.Value!.Ready, string.Join(';', readiness.Value.Checks.Select(x => x.Message)));

            var calculated = await new PayrollCalculationEngine(db, new TestTenantContext(tenantId), TimeProvider.System).CalculateAsync(runId, false);
            Assert.True(calculated.Succeeded, calculated.Message);
            Assert.Equal(100, calculated.Value!.CalculatedCount);
            Assert.Equal(0, calculated.Value.FailedCount);

            var register = await new PayrollOutputService(db, new TestTenantContext(tenantId), new EmployeeIdentityResolver(db, new TestTenantContext(tenantId)), TimeProvider.System).GetRegisterAsync(runId, new PayrollOutputQuery { Page = 1, PageSize = 50 });
            Assert.True(register.Succeeded, register.Message);
            Assert.Equal(100, register.Value!.TotalCount);
            Assert.Equal(50, register.Value.Items.Count);
            Assert.Equal(1000000m, register.Value.Items.Sum(x => x.GrossEarnings) + (await new PayrollOutputService(db, new TestTenantContext(tenantId), new EmployeeIdentityResolver(db, new TestTenantContext(tenantId)), TimeProvider.System).GetRegisterAsync(runId, new PayrollOutputQuery { Page = 2, PageSize = 50 })).Value!.Items.Sum(x => x.GrossEarnings));

            await using var otherDb = database.CreateContext(new TestTenantContext(otherTenantId));
            var otherRegister = await new PayrollOutputService(otherDb, new TestTenantContext(otherTenantId), new EmployeeIdentityResolver(otherDb, new TestTenantContext(otherTenantId)), TimeProvider.System).GetRegisterAsync(runId, new PayrollOutputQuery { Page = 1, PageSize = 50 });
            Assert.True(otherRegister.Succeeded);
            Assert.Empty(otherRegister.Value!.Items);
        }
    }

    private static async Task SeedTenantAsync(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"L{tenantId:N}"[..12], TenantName = "Large test", Host = $"{tenantId:N}.large.test", ShardKey = tenantId.ToString("N") });
        await db.SaveChangesAsync();
    }
}
