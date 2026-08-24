using HRMS.API.Common;
using HRMS.Application.Common;

namespace HRMS.API.Middleware;

/// <summary>
/// Centralized exception handling. Any unhandled exception is logged with full technical detail and
/// translated into the standard <see cref="ApiResponse"/> envelope with a safe, generic message.
/// Internal exception details are only surfaced to the client in the Development environment.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}",
                context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
            {
                // Response already partly written; nothing safe we can do but re-throw.
                throw;
            }

            context.Response.Clear();

            var message = _environment.IsDevelopment()
                ? ex.Message
                : "An unexpected error occurred. Please try again later.";

            await FailureResponse.WriteAsync(context, StatusCodes.Status500InternalServerError, message);
        }
    }
}
