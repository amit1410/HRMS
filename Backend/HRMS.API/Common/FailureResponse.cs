using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Common;

namespace HRMS.API.Common;

/// <summary>
/// Writes an <see cref="ApiResponse"/> failure envelope directly to the response.
/// <para>
/// Controllers return the envelope through MVC, but several refusals happen before or outside a route —
/// the exception handler, the rate limiter, the JWT challenge, host resolution, authorization. Each of
/// those has to serialize the envelope itself, and every one of them has to agree on the shape: with
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/> a failure carrying no validation errors omits
/// <c>errors</c> entirely, and a single caller forgetting that would emit <c>"errors": null</c> for the
/// same kind of failure. One writer, one shape.
/// </para>
/// </summary>
internal static class FailureResponse
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Sets the status code and writes the envelope. Does nothing if the response has already started,
    /// since the status line and headers are gone by then and appending a second body would corrupt the
    /// first. Callers that need to react to that case must check
    /// <see cref="HttpResponse.HasStarted"/> themselves first.
    /// </summary>
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var json = JsonSerializer.Serialize(ApiResponse.Fail(message), SerializerOptions);
        return context.Response.WriteAsync(json, cancellationToken);
    }
}
