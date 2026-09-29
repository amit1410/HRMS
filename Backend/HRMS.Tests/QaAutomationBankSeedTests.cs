using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Security;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

/// <summary>
/// The QAAUTO bank master is a Development-only, ANEVRA01-only, opt-in seed. These tests pin every gate and
/// the "never modify an existing row" rule, because the seeder runs on every startup.
/// </summary>
public class QaAutomationBankSeedTests
{
    private static readonly Guid AnevraTenantId = new("f92b6b9a-512a-42b2-b653-907cc2c1f70e");
    private static readonly Guid OtherTenantId = new("0b7d3f6e-5a8c-4d2e-9f1a-3c6b8e2d4f70");

    [Fact]
    public async Task Seeds_one_active_bank_for_ANEVRA01_in_Development_when_enabled()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await AddTenantAsync(db, AnevraTenantId, "ANEVRA01");

        await SeedAsync(db, tenant, enabled: true, isDevelopment: true);

        using var context = db.CreateContext(new TestTenantContext());
        var bank = Assert.Single(await context.Banks.IgnoreQueryFilters().Where(x => x.TenantId == AnevraTenantId).ToListAsync());
        Assert.Equal(DatabaseSeeder.QaAutomationBankCode, bank.Code);
        Assert.Equal("QAAUTO Sandbox Bank (not a real bank)", bank.Name);
        Assert.True(bank.IsActive);
    }

    [Fact]
    public async Task Is_idempotent()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await AddTenantAsync(db, AnevraTenantId, "ANEVRA01");

        await SeedAsync(db, tenant, enabled: true, isDevelopment: true);
        await SeedAsync(db, tenant, enabled: true, isDevelopment: true);

        using var context = db.CreateContext(new TestTenantContext());
        Assert.Single(await context.Banks.IgnoreQueryFilters().Where(x => x.TenantId == AnevraTenantId).ToListAsync());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Seeds_nothing_without_the_flag_or_outside_Development(bool enabled, bool isDevelopment)
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await AddTenantAsync(db, AnevraTenantId, "ANEVRA01");

        await SeedAsync(db, tenant, enabled, isDevelopment);

        using var context = db.CreateContext(new TestTenantContext());
        Assert.Empty(await context.Banks.IgnoreQueryFilters().Where(x => x.TenantId == AnevraTenantId).ToListAsync());
    }

    [Fact]
    public async Task Seeds_nothing_for_another_tenant()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await AddTenantAsync(db, OtherTenantId, "OTHER01");

        await SeedAsync(db, tenant, enabled: true, isDevelopment: true);

        using var context = db.CreateContext(new TestTenantContext());
        Assert.Empty(await context.Banks.IgnoreQueryFilters().Where(x => x.Code == DatabaseSeeder.QaAutomationBankCode).ToListAsync());
    }

    [Fact]
    public async Task Never_reactivates_or_edits_an_existing_row()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = await AddTenantAsync(db, AnevraTenantId, "ANEVRA01");
        await SeedAsync(db, tenant, enabled: true, isDevelopment: true);
        using (var edit = db.CreateContext(new TestTenantContext()))
        {
            var bank = await edit.Banks.IgnoreQueryFilters().SingleAsync(x => x.TenantId == AnevraTenantId);
            bank.IsActive = false;
            bank.Name = "Retired by hand";
            await edit.SaveChangesAsync();
        }

        await SeedAsync(db, tenant, enabled: true, isDevelopment: true);

        using var context = db.CreateContext(new TestTenantContext());
        var after = Assert.Single(await context.Banks.IgnoreQueryFilters().Where(x => x.TenantId == AnevraTenantId).ToListAsync());
        Assert.False(after.IsActive);
        Assert.Equal("Retired by hand", after.Name);
    }

    private static async Task<Tenant> AddTenantAsync(SqliteInMemoryDatabase db, Guid id, string code)
    {
        var tenant = new Tenant
        {
            Id = id,
            TenantCode = code,
            Host = $"{code.ToLowerInvariant()}.localhost",
            ShardKey = code.ToLowerInvariant(),
            DatabaseProvider = DatabaseProviderType.MySql,
            TenantName = code,
            Status = TenantStatus.Active
        };
        using var context = db.CreateContext(new TestTenantContext());
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    private static async Task SeedAsync(SqliteInMemoryDatabase db, Tenant tenant, bool enabled, bool isDevelopment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DevelopmentSeed:EnableQaAutomationBank"] = enabled.ToString() })
            .Build();
        using var context = db.CreateContext(new TestTenantContext());
        await DatabaseSeeder.SeedShardAsync(
            context, new IdentityPasswordHasher(), tenant, CancellationToken.None, NullLogger.Instance, configuration, isDevelopment);
    }
}
