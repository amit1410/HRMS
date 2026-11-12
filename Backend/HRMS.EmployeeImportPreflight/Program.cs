using System.Security.Claims;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Security;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Storage;
using HRMS.Application;
using HRMS.EmployeeImportPreflight;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

const string targetHost = "web1.localhost";
const string targetCode = "WEB1";
var targetOperation = Guid.Parse("b89d04aa-1419-4c85-a7e3-48ab1d92d111");
var failureStage = EmployeeBulkImportPreflightStage.Configuration;

try
{
    if (args.Length == 1 && args[0] == "--deployment-readiness")
    {
        var readinessApiDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Backend", "HRMS.API"));
        if (!Directory.Exists(readinessApiDirectory)) return Fail("configuration.api_content_root_missing");
        return await DeploymentReadinessRunner.RunAsync(readinessApiDirectory);
    }

    if (args.Length == 1 && args[0] == "--deployment-readiness-normal")
    {
        var readinessApiDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Backend", "HRMS.API"));
        if (!Directory.Exists(readinessApiDirectory)) return Fail("configuration.api_content_root_missing");
        return await DeploymentReadinessRunner.RunAsync(readinessApiDirectory, normalApiCandidate: true);
    }

    if (EmployeeBulkImportPreflightDiagnostics.InteractiveInputError(Console.IsInputRedirected) is { } redirectedError)
        return Fail(redirectedError);

    if (args.Length != 4 || args[0] != "--expected-provider" || args[2] != "--expected-shard" ||
        !Enum.TryParse<DatabaseProviderType>(args[1], ignoreCase: true, out var expectedProvider) ||
        string.IsNullOrWhiteSpace(args[3]))
        return Fail("invocation.invalid_arguments");

    // Run from the repository root so the API's existing development configuration is loaded.
    var apiDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Backend", "HRMS.API"));
    if (!Directory.Exists(apiDirectory)) return Fail("configuration.api_content_root_missing");

    var configuration = new ConfigurationBuilder()
        .SetBasePath(apiDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
        .AddUserSecrets(typeof(EmployeeBulkImportExecutionController).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();

    var mountedSecretFile = configuration["EmployeeImportStorage:ConfigurationFile"];
    if (!string.IsNullOrWhiteSpace(mountedSecretFile))
    {
        if (!Path.IsPathFullyQualified(mountedSecretFile)) return Fail("configuration.storage_secret_path_invalid");
        configuration = new ConfigurationBuilder().AddConfiguration(configuration)
            .AddJsonFile(mountedSecretFile, optional: false, reloadOnChange: false).Build();
    }

    var workerOptions = configuration.GetSection(EmployeeBulkImportWorkerOptions.SectionName)
        .Get<EmployeeBulkImportWorkerOptions>() ?? new EmployeeBulkImportWorkerOptions();
    var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
    if (jwtSettings.Validate() is not null) return Fail(EmployeeBulkImportPreflightDiagnostics.FailureCategory(EmployeeBulkImportPreflightStage.Configuration));
    try { _ = EmployeeImportStorageOptions.LoadAndValidate(configuration, new DevelopmentEnvironment(apiDirectory)); }
    catch (Exception) { return Fail(EmployeeBulkImportPreflightDiagnostics.FailureCategory(EmployeeBulkImportPreflightStage.Configuration)); }
    if (workerOptions.Enabled) return Fail("worker.enabled");
    Console.WriteLine("Enter the explicit read-only approval phrase before any database access:");
    var approvalError = EmployeeBulkImportPreflightDiagnostics.ApprovalError(Console.ReadLine());
    if (approvalError is not null) return Fail(approvalError);

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddSingleton<IHostEnvironment>(new DevelopmentEnvironment(apiDirectory));
    services.AddApplication();
    services.AddInfrastructure(configuration, new DevelopmentEnvironment(apiDirectory), includeDeliveryInfrastructure: false);
    services.AddHttpContextAccessor();
    services.AddScoped<HttpTenantContext>();
    services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<HttpTenantContext>());
    services.AddScoped<ITenantExecutionContext>(provider => provider.GetRequiredService<HttpTenantContext>());
    services.AddScoped<ICurrentAuthorizationContext, HttpCurrentAuthorizationContext>();
    services.AddJwtAuthentication(configuration);

    // This is a command-line process, not a hosted API. No worker or any other IHostedService is started.
    using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    failureStage = EmployeeBulkImportPreflightStage.Catalog;
    await using var catalogScope = root.CreateAsyncScope();
    var catalog = catalogScope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();
    var matches = await catalog.Tenants.AsNoTracking()
        .Where(tenant => tenant.Host == targetHost)
        .Take(2)
        .Select(tenant => new CatalogRoute(tenant.Id, tenant.TenantCode, tenant.Host, tenant.ShardKey, tenant.Status, tenant.DatabaseProvider))
        .ToListAsync();
    if (matches.Count != 1) return Fail("catalog.host_match_count_invalid");
    var route = matches[0];
    if (route.Status != TenantStatus.Active || route.TenantCode != targetCode || route.DatabaseProvider != expectedProvider ||
        !string.Equals(route.ShardKey, args[3], StringComparison.Ordinal))
        return Fail("catalog.route_does_not_match_expected_identity");

    var resolver = catalogScope.ServiceProvider.GetRequiredService<ITenantShardResolver>();
    var resolved = await resolver.ResolveByHostAsync(targetHost);
    if (resolved is null || resolved.TenantId != route.TenantId || resolved.TenantCode != route.TenantCode ||
        resolved.Host != route.Host || resolved.ShardKey != route.ShardKey || resolved.Status != route.Status ||
        resolved.DatabaseProvider != route.DatabaseProvider)
        return Fail("catalog.resolver_mismatch");

    failureStage = EmployeeBulkImportPreflightStage.Authentication;
    if (EmployeeBulkImportPreflightDiagnostics.InteractiveInputError(Console.IsInputRedirected) is { } redirectedJwtError)
        return Fail(redirectedJwtError);
    Console.Write("Existing WEB1 bearer token with Employee.Import permission (input hidden): ");
    string token;
    try { token = ReadSecret(); }
    catch (Exception exception) when (exception is InvalidOperationException or IOException)
    {
        return Fail("authentication.input_unavailable");
    }
    var missingJwtError = EmployeeBulkImportPreflightDiagnostics.JwtInputError(token);
    if (missingJwtError is not null) return Fail(missingJwtError);

    await using var requestScope = root.CreateAsyncScope();
    var httpContext = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
    httpContext.Request.Host = new HostString(targetHost);
    httpContext.Request.Headers.Authorization = "Bearer " + token;
    requestScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

    var authService = requestScope.ServiceProvider.GetRequiredService<IAuthenticationService>();
    var auth = await authService.AuthenticateAsync(httpContext, JwtBearerDefaults.AuthenticationScheme);
    httpContext.Request.Headers.Authorization = Microsoft.Extensions.Primitives.StringValues.Empty;
    token = string.Empty;
    if (!auth.Succeeded || auth.Principal?.Identity?.IsAuthenticated != true)
        return Fail(EmployeeBulkImportPreflightDiagnostics.JwtAuthenticationError(authenticated: false)!);
    var principal = auth.Principal!;
    httpContext.User = principal;
    var claimsTenantId = principal.FindFirstValue(HrmsClaimTypes.TenantId);
    if (!Guid.TryParse(claimsTenantId, out var authenticatedTenantId) || authenticatedTenantId != route.TenantId)
        return Fail(EmployeeBulkImportPreflightDiagnostics.TenantMismatch);

    failureStage = EmployeeBulkImportPreflightStage.Authorization;
    var shard = requestScope.ServiceProvider.GetRequiredService<IShardContext>();
    shard.Use(resolved);
    var authorization = await requestScope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
        .AuthorizeAsync(principal, resolved, Permissions.Employee.Import);
    if (!authorization.Succeeded) return Fail(EmployeeBulkImportPreflightDiagnostics.MissingEmployeeImportPermission);

    // The exact tenant shard is only resolved below this point, after host routing, JWT authentication,
    // token/catalog tenant comparison, and the application's Employee.Import policy all passed.
    failureStage = EmployeeBulkImportPreflightStage.Operation;
    var verifier = requestScope.ServiceProvider.GetRequiredService<IEmployeeBulkImportPreflightVerifier>();
    var report = await verifier.VerifyAsync(targetOperation);
    var reportedChecks = new List<EmployeeBulkImportPreflightCheck>
    {
        new("catalog_route", true, "catalog.exactly_one_active_match"),
        new("resolved_shard_identity", true, "catalog.resolver_match"),
        new("authentication", true, "authentication.jwt_valid"),
        new("tenant_identity_matches_catalog", true, "authorization.tenant_match"),
        new("employee_import_permission", true, "authorization.permission_granted")
    };
    reportedChecks.AddRange(report.Checks);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        Passed = report.Passed,
        report.OperationId,
        report.TenantId,
        report.TenantCode,
        report.State,
        report.ConfirmedAtUtc,
        report.ExpectedRows,
        report.PendingRows,
        report.ProcessingRows,
        report.SucceededRows,
        report.FailedRows,
        report.SkippedRows,
        report.ActiveLease,
        report.ValidatedRows,
        report.InvalidRows,
        report.SanitizedErrorCategories,
        Checks = reportedChecks.Select(check => new { check.Name, check.Passed, check.Category })
    }));
    return report.Passed ? 0 : 2;
}
catch (OperationCanceledException)
{
    return Fail("verification.cancelled");
}
catch (Exception)
{
    // Deliberately suppress exception messages and logs: they may contain infrastructure identifiers.
    return Fail(EmployeeBulkImportPreflightDiagnostics.FailureCategory(failureStage));
}

static int Fail(string category)
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        Passed = false,
        Checks = new[] { new { Name = "preflight_gate", Passed = false, Category = category } }
    }));
    return 2;
}

static string ReadSecret()
{
    var chars = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (chars.Count > 0) chars.RemoveAt(chars.Count - 1);
            continue;
        }
        if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
    }
    Console.WriteLine();
    var value = new string(chars.ToArray());
    chars.Clear();
    return value;
}

sealed record CatalogRoute(Guid TenantId, string TenantCode, string Host, string ShardKey, TenantStatus Status, DatabaseProviderType DatabaseProvider);

sealed class DevelopmentEnvironment(string contentRoot) : IHostEnvironment, IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "HRMS.API";
    public string ContentRootPath { get; set; } = contentRoot;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
    public string WebRootPath { get; set; } = contentRoot;
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
}

