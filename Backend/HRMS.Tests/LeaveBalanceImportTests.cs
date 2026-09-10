using System.Text;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class LeaveBalanceImportTests
{
    [Fact]
    public async Task Valid_csv_creates_opening_ledger_and_is_idempotent()
    {
        using var db = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(db);
        await using var context = db.CreateContext(new TestTenantContext(ids.Tenant, ids.User));
        var service = new LeaveBalanceImportService(context, new TestTenantContext(ids.Tenant, ids.User), new LeaveBalanceTransactionPoster(context, new TestTenantContext(ids.Tenant, ids.User), TimeProvider.System), TimeProvider.System);
        var csv = $"EmployeeCode,LeaveTypeCode,LeavePeriod,OpeningBalance,EffectiveDate,Remarks\nEMP-1,AL,FY2027,5.500,2027-01-01,Opening import\n";

        var validated = await service.ValidateAsync("balances.csv", new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.True(validated.Succeeded);
        var committed = await service.CommitAsync(validated.Value!.Id);
        var replay = await service.CommitAsync(validated.Value.Id);

        Assert.True(committed.Succeeded);
        Assert.True(replay.Succeeded);
        Assert.Equal(LeaveBalanceImportBatchStatus.Committed, replay.Value!.Status);
        Assert.Equal(5.500m, await context.EmployeeLeaveBalances.Select(x => x.GrantedQuantity).SingleAsync());
        Assert.Equal(1, await context.LeaveBalanceTransactions.CountAsync());
        var ledger = await context.LeaveBalanceTransactions.SingleAsync();
        Assert.Equal(HRMS.Domain.Enums.LeaveBalanceTransactionType.Opening, ledger.TransactionType);
        Assert.Equal(HRMS.Domain.Enums.LeaveBalanceSourceType.BalanceImport, ledger.SourceType);
        Assert.Equal(validated.Value.Id.ToString("D"), ledger.SourceReference);
    }

    [Fact]
    public async Task Invalid_rows_are_staged_and_cannot_be_committed()
    {
        using var db = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(db);
        await using var context = db.CreateContext(new TestTenantContext(ids.Tenant, ids.User));
        var service = new LeaveBalanceImportService(context, new TestTenantContext(ids.Tenant, ids.User), new LeaveBalanceTransactionPoster(context, new TestTenantContext(ids.Tenant, ids.User), TimeProvider.System), TimeProvider.System);
        var csv = "EmployeeCode,LeaveTypeCode,LeavePeriod,OpeningBalance,EffectiveDate\nUNKNOWN,AL,FY2027,2,2027-01-01\nEMP-1,AL,FY2027,3,2027-01-01\n";

        var result = await service.ValidateAsync("balances.csv", new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        var errors = await service.ErrorsAsync(result.Value!.Id);
        var commit = await service.CommitAsync(result.Value.Id);

        Assert.Equal(LeaveBalanceImportBatchStatus.Invalid, result.Value.Status);
        Assert.Equal(1, errors.Value!.Count);
        Assert.Equal(ResultStatus.Conflict, commit.Status);
        Assert.Empty(await context.LeaveBalanceTransactions.ToListAsync());
    }

    [Fact]
    public async Task Duplicate_rows_are_rejected_before_commit()
    {
        using var db = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(db);
        await using var context = db.CreateContext(new TestTenantContext(ids.Tenant, ids.User));
        var service = new LeaveBalanceImportService(context, new TestTenantContext(ids.Tenant, ids.User), new LeaveBalanceTransactionPoster(context, new TestTenantContext(ids.Tenant, ids.User), TimeProvider.System), TimeProvider.System);
        var csv = "EmployeeCode,LeaveTypeCode,LeavePeriod,OpeningBalance,EffectiveDate\nEMP-1,AL,FY2027,2,2027-01-01\nEMP-1,AL,FY2027,3,2027-01-01\n";

        var result = await service.ValidateAsync("balances.csv", new MemoryStream(Encoding.UTF8.GetBytes(csv)));

        Assert.Equal(1, result.Value!.InvalidRows);
        Assert.Equal(LeaveBalanceImportBatchStatus.Invalid, result.Value.Status);
    }

    private static async Task<(Guid Tenant, Guid User)> SeedAsync(SqliteInMemoryDatabase db)
    {
        var tenant = Guid.NewGuid(); var user = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid(); var period = Guid.NewGuid();
        await using var context = db.CreateContext(new TestTenantContext(tenant, user));
        context.Tenants.Add(new Tenant { Id = tenant, TenantCode = "IMPORT", Host = "import.local", ShardKey = "import", TenantName = "Import test" });
        context.Users.Add(new User { Id = user, TenantId = tenant, Email = "importer@test.invalid", PasswordHash = "hash", FirstName = "Import", LastName = "User" });
        context.Employees.Add(new Employee { Id = employee, TenantId = tenant, EmployeeCode = "EMP-1", FirstName = "Test", LastName = "Employee", Email = "employee@test.invalid", DateOfJoining = new(2020, 1, 1) });
        context.LeaveTypes.Add(new LeaveType { Id = type, TenantId = tenant, Code = "AL", Name = "Annual Leave", IsActive = true });
        context.LeavePeriods.Add(new LeavePeriod { Id = period, TenantId = tenant, Code = "FY2027", Name = "FY2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31), IsActive = true });
        await context.SaveChangesAsync();
        return (tenant, user);
    }
}
