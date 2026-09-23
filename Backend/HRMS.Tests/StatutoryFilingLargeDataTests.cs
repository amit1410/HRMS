using System.Diagnostics;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class StatutoryFilingLargeDataTests
{
    [Fact]
    public async Task Ten_thousand_source_rows_generate_validate_and_hash_without_duplication()
    {
        using var database = new SqliteInMemoryDatabase(); var tenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid()); await using var db = database.CreateContext(tenant); var tenantId = tenant.TenantId!.Value; var stopwatch = Stopwatch.StartNew();
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"LD{tenantId:N}"[..12], TenantName = "Large filing", Host = $"{tenantId:N}.large.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
        var periodId = Guid.NewGuid(); var batchId = Guid.NewGuid(); db.PayrollCompliancePeriods.Add(new PayrollCompliancePeriod { Id = periodId, TenantId = tenantId, JurisdictionCode = "IN", ComplianceType = PayrollComplianceType.IncomeTaxTds, PeriodStart = new(2026, 9, 1), PeriodEnd = new(2026, 9, 30), Status = PayrollCompliancePeriodStatus.Open });
        var employees = Enumerable.Range(1, 10000).Select(i => new Employee { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = $"LD-{i:00000}", FirstName = "Large", LastName = $"Employee {i}", Email = $"ld-{i}@example.test", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active }).ToList(); var batch = new PayrollStatutoryReturnBatch { Id = batchId, TenantId = tenantId, PayrollCompliancePeriodId = periodId, ComplianceType = PayrollComplianceType.IncomeTaxTds, JurisdictionCode = "IN", BatchNumber = "LD-202609", Status = PayrollStatutoryReturnStatus.Approved, EmployeeCount = employees.Count }; for (var i = 0; i < employees.Count; i++) batch.Employees.Add(new PayrollStatutoryReturnEmployee { Id = Guid.NewGuid(), TenantId = tenantId, PayrollStatutoryReturnBatchId = batchId, EmployeeId = employees[i].Id, EmployeeCodeSnapshot = employees[i].EmployeeCode!, EmployeeNameSnapshot = $"Large Employee {i + 1}", GrossWages = 10000m + i, StatutoryWages = 10000m + i, EmployeeContribution = 100m, DeductionAmount = 100m, PayableAmount = 100m, Sequence = i + 1 });
        db.Employees.AddRange(employees); db.PayrollStatutoryReturnBatches.Add(batch); await db.SaveChangesAsync();
        var service = new StatutoryFilingService(db, tenant, TimeProvider.System); var definition = await service.CreateDefinitionAsync(new StatutoryFilingDefinitionRequest { Code = "LD-FILING", Name = "Large statutory extract", FilingType = "ComplianceSummary", JurisdictionCode = "IN" }); Assert.True(definition.Succeeded, definition.Message); var run = await service.CreateRunAsync(new StatutoryFilingRunRequest { DefinitionId = definition.Value!.Id, FilingPeriod = "2026-09" }); Assert.True(run.Succeeded, run.Message); var generated = await service.GenerateAsync(run.Value!.Id); Assert.True(generated.Succeeded, generated.Message); Assert.Equal(10000, generated.Value!.RowCount); var validated = await service.ValidateAsync(run.Value.Id); Assert.True(validated.Succeeded, validated.Message); var package = await service.GetPackageAsync(run.Value.Id); Assert.True(package.Succeeded); Assert.False(string.IsNullOrWhiteSpace(package.Value!.PackageHash)); Assert.Equal(10000, await db.StatutoryFilingRunItems.CountAsync(x => x.RunId == run.Value.Id)); Assert.Equal(0, await db.StatutoryFilingValidationIssues.CountAsync(x => x.RunId == run.Value.Id)); stopwatch.Stop(); Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromMinutes(2));
    }
}
