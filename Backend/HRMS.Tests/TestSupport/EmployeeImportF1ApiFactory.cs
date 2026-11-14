using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Real API test host whose only tenant target is the currently approved disposable F1 shard.
/// The production shard connection factory substitutes the catalog shard key, and startup worker
/// flags are explicitly isolated from the normal HRMS runtime.
/// </summary>
public sealed class EmployeeImportF1ApiFactory : HrmsApiFactory
{
    private readonly EmployeeImportMySqlAcceptanceConfiguration f1;
    private readonly bool workerEnabled;
    private readonly string? targetTenantCode;
    private readonly Guid? targetOperationId;
    private readonly IInterceptor[] interceptors;

    public EmployeeImportF1ApiFactory(EmployeeImportMySqlAcceptanceConfiguration f1, bool workerEnabled, params IInterceptor[] interceptors)
        : this(f1, workerEnabled, null, null, interceptors)
    {
    }

    public EmployeeImportF1ApiFactory(EmployeeImportMySqlAcceptanceConfiguration f1, bool workerEnabled,
        string targetTenantCode, Guid targetOperationId, params IInterceptor[] interceptors)
        : this(f1, workerEnabled, (string?)targetTenantCode, (Guid?)targetOperationId, interceptors)
    {
    }

    private EmployeeImportF1ApiFactory(EmployeeImportMySqlAcceptanceConfiguration f1, bool workerEnabled,
        string? targetTenantCode, Guid? targetOperationId, IInterceptor[] interceptors)
    {
        this.f1 = f1;
        this.workerEnabled = workerEnabled;
        this.targetTenantCode = targetTenantCode;
        this.targetOperationId = targetOperationId;
        this.interceptors = interceptors;
    }

    protected override DatabaseProviderType TestTenantProvider => DatabaseProviderType.MySql;
    protected override bool RequireInitialization => false;

    /// <summary>Stops the real hosted worker and waits for host shutdown to complete.</summary>
    public Task StopHostAsync(CancellationToken cancellationToken = default) => Server.Host.StopAsync(cancellationToken);

    protected override void ConfigureTestConfiguration(IConfigurationBuilder configuration)
    {
        base.ConfigureTestConfiguration(configuration);
        var tenantTemplate = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(
            EmployeeImportMySqlCredentialAdapter.ApplicationConnection(f1, ShardingOptions.ShardKeyPlaceholder))
        {
            Database = ShardingOptions.ShardKeyPlaceholder,
            Pooling = false
        };
        var catalogConnection = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(
            EmployeeImportMySqlCredentialAdapter.ApplicationConnection(f1, f1.CatalogDatabase))
        {
            Database = f1.CatalogDatabase,
            Pooling = false
        };
        configuration.AddJsonFile(f1.StorageConfigurationPath, optional: false, reloadOnChange: false);
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MySql",
            ["Database:CatalogProvider"] = "MySql",
            ["ConnectionStrings:Catalog"] = catalogConnection.ConnectionString,
            ["Sharding:MySqlConnectionStringTemplate"] = tenantTemplate.ConnectionString,
            ["Database:SkipInitialization"] = "true",
            ["Database:StartupMode"] = "Initialize",
            ["Database:SeedDemoTenants"] = "false",
            ["EmployeeBulkImportWorker:Enabled"] = workerEnabled.ToString(),
            ["EmployeeBulkImportWorker:PollIntervalSeconds"] = "1",
            ["EmployeeBulkImportWorker:TargetTenantCode"] = targetTenantCode,
            ["EmployeeBulkImportWorker:TargetOperationId"] = targetOperationId?.ToString("D"),
            ["WelcomeInvitations:Enabled"] = "false",
            ["LeaveReminders:Enabled"] = "false",
            ["ConfirmationReminders:Enabled"] = "false",
            ["LeaveAccrual:Enabled"] = "false",
            ["AttendanceDeviceWorker:Enabled"] = "false"
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        if (interceptors.Length != 0)
            services.AddSingleton<IDbContextOptionsConfiguration<HrmsDbContext>>(new AcceptanceInterceptorConfiguration(interceptors));
    }

    private sealed class AcceptanceInterceptorConfiguration(IInterceptor[] values) : IDbContextOptionsConfiguration<HrmsDbContext>
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddInterceptors(values);
    }
}
