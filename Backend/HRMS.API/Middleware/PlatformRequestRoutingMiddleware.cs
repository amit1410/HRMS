using HRMS.API.Common;

namespace HRMS.API.Middleware;

/// <summary>Applies the platform-host boundary before tenant host resolution.</summary>
public sealed class PlatformRequestRoutingMiddleware
{
    public const string CatalogDiagnosticBypassItem = "HRMS.CatalogDiagnosticBypass";
    public const string CatalogDiagnosticPath = "/api/platform/catalog-diagnostic";
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public PlatformRequestRoutingMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _next = next;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isPlatformPath = context.Request.Path.Value?.StartsWith("/api/platform", StringComparison.OrdinalIgnoreCase) == true;
        var isPlatformHost = AllowedHosts().Contains(context.Request.Host.Host, StringComparer.OrdinalIgnoreCase);
        if (isPlatformPath && !isPlatformHost)
        {
            await FailureResponse.WriteAsync(context, StatusCodes.Status404NotFound, "The platform endpoint is not available on this host.");
            return;
        }
        if (isPlatformHost && IsLocalCatalogDiagnostic(context))
        {
            context.Items[CatalogDiagnosticBypassItem] = true;
        }

        await _next(context);
    }

    private bool IsLocalCatalogDiagnostic(HttpContext context) =>
        _environment.IsDevelopment()
        && HttpMethods.IsGet(context.Request.Method)
        && string.Equals(context.Request.Path.Value, CatalogDiagnosticPath, StringComparison.Ordinal)
        && context.Connection.RemoteIpAddress is not null
        && System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress);

    private IReadOnlyList<string> AllowedHosts() =>
        _configuration.GetSection("Platform:AllowedHosts").Get<string[]>() ?? ["platform.localhost"];
}

public static class PlatformRequestRoutingExtensions
{
    public static IApplicationBuilder UsePlatformRequestRouting(this IApplicationBuilder app) =>
        app.UseMiddleware<PlatformRequestRoutingMiddleware>();
}
