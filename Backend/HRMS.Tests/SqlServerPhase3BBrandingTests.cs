using HRMS.Application.Common;
using HRMS.Application.DTOs.Tenants;
using HRMS.Application.Services;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

[Collection("SQL Server Phase 3B")]
public sealed class SqlServerPhase3BBrandingTests
{
    private readonly SqlServerPhase3BFixture _fixture;

    public SqlServerPhase3BBrandingTests(SqlServerPhase3BFixture fixture) => _fixture = fixture;

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task Synthetic_hosts_return_their_public_display_names()
    {
        var run = _fixture.RequireRun();
        await using var catalog = run.CreateCatalogContext();

        var tenantA = await catalog.Tenants.SingleAsync(x => x.Host == "tenant-a.localhost");
        var tenantB = await catalog.Tenants.SingleAsync(x => x.Host == "tenant-b.localhost");
        var branding = await catalog.TenantBranding.AsNoTracking().ToDictionaryAsync(x => x.TenantId);

        Assert.True(branding[tenantA.Id].IsPublic);
        Assert.Equal(SqlServerPhase3BFixture.TenantADisplayName, branding[tenantA.Id].DisplayName);
        Assert.True(branding[tenantB.Id].IsPublic);
        Assert.Equal(SqlServerPhase3BFixture.TenantBDisplayName, branding[tenantB.Id].DisplayName);
    }

    [Fact]
    public void Synthetic_branding_names_are_nonempty_and_distinct()
    {
        Assert.False(string.IsNullOrWhiteSpace(SqlServerPhase3BFixture.TenantADisplayName));
        Assert.False(string.IsNullOrWhiteSpace(SqlServerPhase3BFixture.TenantBDisplayName));
        Assert.NotEqual(SqlServerPhase3BFixture.TenantADisplayName, SqlServerPhase3BFixture.TenantBDisplayName);
    }
}
