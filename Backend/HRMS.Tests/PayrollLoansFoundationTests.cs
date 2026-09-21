using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollLoansFoundationTests
{
    [Fact]
    public async Task Loan_product_creation_is_tenant_scoped_and_duplicate_safe()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await AddTenantAsync(database, tenantId);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new PayrollLoanService(db, new TestTenantContext(tenantId), TimeProvider.System);

        var request = new LoanProductRequest { Code = "SAL-ADV", Name = "Salary Advance", ProductType = LoanProductType.SalaryAdvance, MinAmount = 1000, MaxAmount = 50000, MinTenureMonths = 1, MaxTenureMonths = 3, InterestMethod = LoanInterestMethod.None };
        var created = await service.CreateProductAsync(request);
        var duplicate = await service.CreateProductAsync(request);

        Assert.True(created.Succeeded, created.Message);
        Assert.Equal(ResultStatus.Conflict, duplicate.Status);
        Assert.Single(await db.LoanProductVersions.Where(x => x.LoanProductId == created.Value!.Id).ToListAsync());
    }

    [Fact]
    public void Loan_accounting_mapping_types_are_explicit()
    {
        Assert.NotEqual(PayrollGLMappingType.EmployeeDeduction, PayrollGLMappingType.LoanPayrollRecovery);
        Assert.NotEqual(PayrollGLMappingType.LoanPayrollRecovery, PayrollGLMappingType.LoanFinalSettlementRecovery);
    }

    [Fact]
    public async Task Loan_register_is_paged_and_tenant_scoped_for_one_hundred_loans()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        await AddTenantAsync(database, tenantId);
        await AddTenantAsync(database, otherTenantId);
        var productId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await using (var db = database.CreateContext(new TestTenantContext(tenantId)))
        {
            db.LoanProducts.Add(new LoanProduct { Id = productId, TenantId = tenantId, Code = "REG", Name = "Register product", ProductType = LoanProductType.Loan, CurrencyCode = "INR" });
            db.LoanProductVersions.Add(new LoanProductVersion { Id = versionId, TenantId = tenantId, LoanProductId = productId, EffectiveFrom = new DateOnly(2026, 1, 1), MinAmount = 1, MaxAmount = 100000, MinTenureMonths = 1, MaxTenureMonths = 12, Status = LoanProductVersionStatus.Active });
            for (var i = 0; i < 100; i++)
            {
                var employeeId = Guid.NewGuid();
                db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"REG-{i:000}", FirstName = "Register", LastName = $"Employee {i}", Email = $"reg-{i}@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
                db.EmployeeLoans.Add(new EmployeeLoan { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, LoanProductId = productId, LoanProductVersionId = versionId, LoanNumber = $"LN/2026/{i:0000}", RequestedAmount = 1000 + i, ApprovedAmount = 1000 + i, Status = LoanStatus.Approved, CurrencyCode = "INR", RequestedTenureMonths = 1, ApprovedTenureMonths = 1, OutstandingPrincipal = 1000 + i, OutstandingTotal = 1000 + i, RequestedAtUtc = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        await using var readDb = database.CreateContext(new TestTenantContext(tenantId));
        var service = new PayrollLoanService(readDb, new TestTenantContext(tenantId), TimeProvider.System);
        var page1 = await service.GetRegisterAsync(new LoanRegisterQuery { Page = 1, PageSize = 50 });
        var page2 = await service.GetRegisterAsync(new LoanRegisterQuery { Page = 2, PageSize = 50 });
        Assert.True(page1.Succeeded, page1.Message);
        Assert.True(page2.Succeeded, page2.Message);
        Assert.Equal(100, page1.Value!.TotalCount);
        Assert.Equal(50, page1.Value.Items.Count);
        Assert.Equal(50, page2.Value!.Items.Count);
        Assert.Empty(page1.Value.Items.Select(x => x.Id).Intersect(page2.Value.Items.Select(x => x.Id)));
        await using var otherDb = database.CreateContext(new TestTenantContext(otherTenantId));
        var otherPage = await new PayrollLoanService(otherDb, new TestTenantContext(otherTenantId), TimeProvider.System).GetRegisterAsync(new LoanRegisterQuery { Page = 1, PageSize = 50 });
        Assert.Equal(0, otherPage.Value!.TotalCount);
    }

    private static async Task AddTenantAsync(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var catalog = database.CreateContext(new TestTenantContext());
        catalog.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"LN{tenantId:N}"[..12], TenantName = "Loan test tenant", Host = $"{tenantId:N}.loan.test", ShardKey = tenantId.ToString("N") });
        await catalog.SaveChangesAsync();
    }
}
