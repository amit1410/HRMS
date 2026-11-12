using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.BulkImport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRMS.Tests;

public sealed class EmployeeBulkImportWorkerScopeTests
{
    [Fact]
    public void Worker_is_disabled_by_default_and_enabled_requires_one_tenant_operation_pair()
    {
        var defaults = new EmployeeBulkImportWorkerOptions();
        Assert.False(defaults.Enabled);
        Assert.Null(defaults.Validate());

        Assert.NotNull(new EmployeeBulkImportWorkerOptions { Enabled = true }.Validate());
        Assert.NotNull(new EmployeeBulkImportWorkerOptions { Enabled = true, TargetTenantCode = "WEB1" }.Validate());
        Assert.NotNull(new EmployeeBulkImportWorkerOptions { Enabled = true, TargetOperationId = Guid.NewGuid() }.Validate());
        Assert.NotNull(new EmployeeBulkImportWorkerOptions
        {
            Enabled = true,
            TargetTenantCode = "web1",
            TargetOperationId = Guid.NewGuid()
        }.Validate());
        Assert.NotNull(new EmployeeBulkImportWorkerOptions
        {
            Enabled = true,
            TargetTenantCode = "WEB1",
            TargetOperationId = Guid.Empty
        }.Validate());

        Assert.Null(new EmployeeBulkImportWorkerOptions
        {
            Enabled = true,
            TargetTenantCode = "WEB1",
            TargetOperationId = Guid.NewGuid()
        }.Validate());
    }

    [Fact]
    public void Tenant_selection_includes_only_the_configured_active_tenant()
    {
        var tenants = new[]
        {
            new Tenant { Id = Guid.NewGuid(), TenantCode = "WEB1", Status = TenantStatus.Active },
            new Tenant { Id = Guid.NewGuid(), TenantCode = "WEB2", Status = TenantStatus.Active },
            new Tenant { Id = Guid.NewGuid(), TenantCode = "WEB1", Status = TenantStatus.Suspended }
        };

        var selected = EmployeeBulkImportWorker.SelectTargetTenants(tenants.AsQueryable(), "WEB1").ToArray();

        var target = Assert.Single(selected);
        Assert.Equal("WEB1", target.TenantCode);
        Assert.Equal(TenantStatus.Active, target.Status);
    }

    [Fact]
    public async Task Disabled_worker_does_not_open_any_scope_or_claim_work()
    {
        var scopes = new CountingScopeFactory();
        using var worker = new EmployeeBulkImportWorker(
            scopes,
            Options.Create(new EmployeeBulkImportWorkerOptions()),
            NullLogger<EmployeeBulkImportWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, scopes.Created);
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        public int Created { get; private set; }

        public IServiceScope CreateScope()
        {
            Created++;
            throw new InvalidOperationException("A disabled worker must not create scopes.");
        }
    }
}
