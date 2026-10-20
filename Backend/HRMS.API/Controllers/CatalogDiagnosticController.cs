using HRMS.Application.Common;
using HRMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/platform/catalog-diagnostic")]
[Produces("application/json")]
public sealed class CatalogDiagnosticController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public CatalogDiagnosticController(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = "PlatformBearer", Policy = "PlatformSuperAdmin")]
    public ActionResult<ApiResponse<CatalogDiagnosticResponse>> Get()
    {
        if (!_environment.IsDevelopment() || !IsLoopback(HttpContext.Connection.RemoteIpAddress))
            return NotFound();

        try
        {
            var details = CatalogConnectionResolver.Resolve(_configuration);
            return Ok(ApiResponse<CatalogDiagnosticResponse>.Ok(
                new CatalogDiagnosticResponse(details.Provider, details.Host, details.Port, details.Database, details.ConfigurationResolution),
                "Catalog configuration resolved."));
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<CatalogDiagnosticController>>()
                .LogWarning("Catalog diagnostic configuration could not be resolved. ErrorType={ErrorType}.", exception.GetType().Name);
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<CatalogDiagnosticResponse>.Fail("Catalog configuration could not be resolved."));
        }
    }

    private static bool IsLoopback(System.Net.IPAddress? address) =>
        address is not null && System.Net.IPAddress.IsLoopback(address);
}

public sealed record CatalogDiagnosticResponse(
    string Provider,
    string? Host,
    int? Port,
    string? Database,
    string ConfigurationResolution);
