using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

/// <summary>
/// Exercises the production API pipeline over real MySQL. The fixture data is arranged in the tenant
/// database, while login, host routing, authorization and Leave operations are all performed over HTTP.
/// </summary>
public sealed class MySqlLeaveHttpIntegrationTests
{
    [Fact]
    public void MySql_test_normalization_round_trips_without_tls_options()
    {
        var normalized = MySqlApiFactory.NormalizeConnectionString(
            "Server=127.0.0.1;Port=3306;Database=test;User ID=test;Password=test;"
            + "SslMode=Preferred;CertificateFile=test.pem;CertificatePassword=test;SslCa=ca.pem;"
            + "SslCert=cert.pem;SslKey=key.pem;TlsVersion=TLSv1.2;");
        var options = new MySqlConnectionStringBuilder(normalized);

        Assert.Equal(MySqlSslMode.Disabled, options.SslMode);
        Assert.DoesNotContain(options.Keys.Cast<string>(),
            key => IsTlsOption(key) && !key.Equals("SslMode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Leave_http_pipeline_routes_authenticates_and_accounts_on_mysql()
    {
        var tenantConnection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        var catalogConnection = Environment.GetEnvironmentVariable("HRMS_MYSQL_CATALOG_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(tenantConnection) || string.IsNullOrWhiteSpace(catalogConnection))
            throw SkipException.ForSkip("MySQL Leave HTTP test requires both MySQL test connection variables.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(
            MySqlApiFactory.NormalizeConnectionString(tenantConnection));
        try
        {
            await fixture.SeedAsync();
            await AddCatalogTenantAsync(catalogConnection, fixture);

            using var factory = new MySqlApiFactory(tenantConnection, catalogConnection);
            var configuredTenantTemplate = factory.Services
                .GetRequiredService<IConfiguration>()[("Sharding:MySqlConnectionStringTemplate")];
            ReportConnectionOptions("Factory tenant template", configuredTenantTemplate);
            using var connectionDiagnostics = new MySqlConnectionDiagnostics();
            using var employeeClient = factory.CreateClientFor(fixture.HttpHost);

            var employeeLogin = await LoginAsync(employeeClient, fixture.EmployeeEmail);
            Assert.Equal(fixture.EmployeeUserId, employeeLogin.User.Id);

            var meResponse = await employeeClient.GetAsync("/api/auth/me");
            var meBody = await meResponse.Content.ReadAsStringAsync();
            Assert.True(meResponse.IsSuccessStatusCode,
                $"/me returned {(int)meResponse.StatusCode} {meResponse.StatusCode}: {meBody}");
            var me = await meResponse.Content.ReadFromJsonAsync<ApiResponse<AuthenticatedUserDto>>();
            Assert.True(me?.Data?.EmployeeIdentity is not null, $"/me did not return employee identity: {meBody}");
            Assert.Equal(fixture.EmployeeId, me.Data!.EmployeeIdentity!.Employee.Id);
            Assert.NotEqual(fixture.EmployeeUserId, me.Data.EmployeeIdentity.Employee.Id);

            var previewResponse = await employeeClient.PostAsJsonAsync(
                "/api/leave-requests/preview",
                new LeaveRequestPreviewRequest(fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-http-preview"));
            var previewBody = await previewResponse.Content.ReadAsStringAsync();
            Assert.True(previewResponse.StatusCode == HttpStatusCode.OK,
                $"Preview returned {(int)previewResponse.StatusCode} {previewResponse.StatusCode}: {previewBody}");
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            jsonOptions.Converters.Add(new JsonStringEnumConverter());
            var preview = (await previewResponse.Content.ReadFromJsonAsync<ApiResponse<LeaveRequestPreviewResponse>>(jsonOptions))!.Data!;
            Assert.Equal(fixture.EmployeeId, preview.EmployeeId);
            Assert.Equal(fixture.LeavePeriodId, preview.LeavePeriodId);
            Assert.Equal(fixture.PolicyVersionId, preview.LeavePolicyVersionId);
            Assert.Equal(1m, preview.ChargeableQuantity);

            var submitResponse = await employeeClient.PostAsJsonAsync(
                "/api/leave-requests",
                new LeaveRequestSubmissionRequest(fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-http-submit"));
            Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
            var submitted = (await submitResponse.Content.ReadFromJsonAsync<ApiResponse<LeaveRequestSubmissionResponse>>(jsonOptions))!.Data!;
            Assert.Equal(fixture.EmployeeId, submitted.EmployeeId);
            Assert.Equal(LeaveRequestStatus.PendingApproval, submitted.Status);

            var duplicateSubmit = await employeeClient.PostAsJsonAsync(
                "/api/leave-requests",
                new LeaveRequestSubmissionRequest(fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-http-submit"));
            Assert.Equal(HttpStatusCode.OK, duplicateSubmit.StatusCode);
            var replay = (await duplicateSubmit.Content.ReadFromJsonAsync<ApiResponse<LeaveRequestSubmissionResponse>>(jsonOptions))!.Data!;
            Assert.True(replay.IsReplay);
            Assert.Equal(submitted.RequestId, replay.RequestId);

            var employeeApprovalAttempt = await employeeClient.PostAsync(
                $"/api/leave-requests/{submitted.RequestId}/approve", null);
            Assert.Equal(HttpStatusCode.Forbidden, employeeApprovalAttempt.StatusCode);

            using var managerClient = factory.CreateClientFor(fixture.HttpHost);
            _ = await LoginAsync(managerClient, fixture.ManagerEmail);
            var approvalResponse = await managerClient.PostAsync(
                $"/api/leave-requests/{submitted.RequestId}/approve", null);
            Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);

            var duplicateApproval = await managerClient.PostAsync(
                $"/api/leave-requests/{submitted.RequestId}/approve", null);
            Assert.Equal(HttpStatusCode.Conflict, duplicateApproval.StatusCode);

            var crossHostClient = factory.CreateClientFor("http://unknown-mysql-http-test.localhost");
            crossHostClient.DefaultRequestHeaders.Authorization = employeeClient.DefaultRequestHeaders.Authorization;
            var crossHostResponse = await crossHostClient.GetAsync("/api/auth/me");
            // Unknown-host requests are rejected before controller execution and therefore return the
            // application's established unauthorized response, matching AuthEndpointsTests.
            Assert.Equal(HttpStatusCode.Unauthorized, crossHostResponse.StatusCode);

            await using var verification = fixture.CreateContext(new TestTenantContext(fixture.TenantId, fixture.EmployeeUserId));
            var request = await verification.LeaveRequests.SingleAsync(x => x.Id == submitted.RequestId);
            Assert.Equal(LeaveRequestStatus.Approved, request.Status);
            var balance = await verification.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
            Assert.Equal(0m, balance.ReservedQuantity);
            Assert.Equal(1m, balance.ConsumedQuantity);
            Assert.Equal(1, await verification.LeaveBalanceTransactions.CountAsync(x =>
                x.LeaveRequestId == submitted.RequestId && x.TransactionType == LeaveBalanceTransactionType.Consumption));
        }
        finally
        {
            await RemoveCatalogTenantAsync(catalogConnection, fixture.TenantId);
            await fixture.CleanupAsync();
        }
    }

    private sealed class MySqlConnectionDiagnostics : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _allListeners;

        public MySqlConnectionDiagnostics() =>
            _allListeners = DiagnosticListener.AllListeners.Subscribe(this);

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
                _subscriptions.Add(listener.Subscribe(this));
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!value.Key.EndsWith("ConnectionOpening", StringComparison.Ordinal))
                return;

            var connection = value.Value?.GetType().GetProperty("Connection")?.GetValue(value.Value) as DbConnection;
            if (connection is not MySqlConnection)
                return;

            var options = new MySqlConnectionStringBuilder(connection.ConnectionString);
            Console.WriteLine(
                "MySQL test connection opening: "
                + $"Type={connection.GetType().FullName}; "
                + $"Server={options.Server}; Port={options.Port}; Database={options.Database}; "
                + $"UserId={options.UserID}; SslMode={options.SslMode}; "
                + $"ConnectionProtocol={GetOption(options, "ConnectionProtocol")}; "
                + $"CertificateFile={Presence(options, "CertificateFile")}; "
                + $"CertificatePassword={Presence(options, "CertificatePassword")}; "
                + $"SslCa={Presence(options, "SslCa")}; SslCert={Presence(options, "SslCert")}; "
                + $"SslKey={Presence(options, "SslKey")}; TlsVersion={GetOption(options, "TlsVersion")}");
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _allListeners.Dispose();
        }

    private static string Presence(MySqlConnectionStringBuilder options, string key) =>
            options.Keys.Cast<string>().Any(existing => existing.Equals(key, StringComparison.OrdinalIgnoreCase))
                ? "present" : "absent";

    private static string GetOption(MySqlConnectionStringBuilder options, string key) =>
            options.Keys.Cast<string>().Any(existing => existing.Equals(key, StringComparison.OrdinalIgnoreCase))
                && options.TryGetValue(key, out var value)
                ? value?.ToString() ?? "empty" : "absent";
    }

    private static void ReportConnectionOptions(string label, string? connectionString)
    {
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        var options = new MySqlConnectionStringBuilder(connectionString);
        Console.WriteLine(
            $"{label}: Server={options.Server}; Port={options.Port}; Database={options.Database}; "
            + $"UserId={options.UserID}; SslMode={options.SslMode}; "
            + $"CertificateFile={Presence(options, "CertificateFile")}; "
            + $"CertificatePassword={Presence(options, "CertificatePassword")}; "
            + $"SslCa={Presence(options, "SslCa")}; SslCert={Presence(options, "SslCert")}; "
            + $"SslKey={Presence(options, "SslKey")}; TlsVersion={GetOption(options, "TlsVersion")}; "
            + $"Keys={string.Join(',', options.Keys.Cast<string>().Where(IsTlsOption))}");
    }

    private static string Presence(MySqlConnectionStringBuilder options, string key) =>
        options.ContainsKey(key) ? "present" : "absent";

    private static string GetOption(MySqlConnectionStringBuilder options, string key) =>
        options.TryGetValue(key, out var value) ? value?.ToString() ?? "empty" : "absent";

    private static bool IsTlsOption(string key) => key.Contains("ssl", StringComparison.OrdinalIgnoreCase)
        || key.Contains("certificate", StringComparison.OrdinalIgnoreCase)
        || key.Contains("tls", StringComparison.OrdinalIgnoreCase);

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Identifier = email,
            Password = MySqlLeaveLifecycleIntegrationTests.Fixture.Password
        });
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Login returned {(int)response.StatusCode} {response.StatusCode}: {responseBody}");
        var envelope = (await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>())!;
        Assert.True(envelope.Success);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope.Data!.AccessToken);
        return envelope.Data;
    }

    private static async Task AddCatalogTenantAsync(string connection, MySqlLeaveLifecycleIntegrationTests.Fixture fixture)
    {
        await using var catalog = new HrmsCatalogDbContext(new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseMySQL(MySqlApiFactory.NormalizeConnectionString(connection)).Options);
        catalog.Tenants.Add(new Tenant
        {
            Id = fixture.TenantId,
            TenantCode = fixture.TenantCode,
            TenantName = "MySQL HTTP Leave Test",
            Host = fixture.Host,
            ShardKey = $"http{fixture.TenantId:N}"[..10],
            Status = TenantStatus.Active,
            DatabaseProvider = DatabaseProviderType.MySql
        });
        await catalog.SaveChangesAsync();
    }

    private static async Task RemoveCatalogTenantAsync(string connection, Guid tenantId)
    {
        await using var catalog = new HrmsCatalogDbContext(new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseMySQL(MySqlApiFactory.NormalizeConnectionString(connection)).Options);
        var tenant = await catalog.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId);
        if (tenant is not null)
        {
            catalog.Tenants.Remove(tenant);
            await catalog.SaveChangesAsync();
        }
    }

}
