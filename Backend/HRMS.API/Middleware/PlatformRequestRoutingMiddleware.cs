using HRMS.API.Common;

namespace HRMS.API.Middleware;

/// <summary>Applies the platform-host boundary before tenant host resolution.</summary>
public sealed class PlatformRequestRoutingMiddleware
{
    public const string PlatformHostItem = "HRMS.PlatformHost";
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public PlatformRequestRoutingMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
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
        if (isPlatformHost)
        {
            context.Items[PlatformHostItem] = true;
            await _next(context);
            return;
        }
        await _next(context);
    }

    private IReadOnlyList<string> AllowedHosts() =>
        _configuration.GetSection("Platform:AllowedHosts").Get<string[]>() ?? ["platform.localhost"];
}

public static class PlatformRequestRoutingExtensions
{
    public static IApplicationBuilder UsePlatformRequestRouting(this IApplicationBuilder app) =>
        app.UseMiddleware<PlatformRequestRoutingMiddleware>();
}
