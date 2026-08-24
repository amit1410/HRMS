using HRMS.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Extensions;

/// <summary>
/// Translates an application-layer <see cref="Result{T}"/> into an HTTP response wrapped in the
/// standard <see cref="ApiResponse{T}"/> envelope. Keeping the mapping here is what lets controllers
/// stay free of branching logic.
/// </summary>
public static class ResultExtensions
{
    public static ActionResult<ApiResponse<T>> ToActionResult<T>(this Result<T> result)
    {
        if (result.Succeeded)
        {
            return new OkObjectResult(ApiResponse<T>.Ok(result.Value!, result.Message));
        }

        return result.ToErrorResult();
    }

    /// <summary>
    /// Same mapping as <see cref="ToActionResult{T}"/>, except that success becomes 201 Created with a
    /// Location header pointing at the new resource. Failures fall through to the shared mapping, so a
    /// conflict or validation error on a POST is reported exactly as it would be anywhere else.
    /// </summary>
    /// <param name="result">The service outcome.</param>
    /// <param name="actionName">Name of the GET-by-id action on the same controller.</param>
    /// <param name="routeValues">Builds the route values for that action from the created value.</param>
    public static ActionResult<ApiResponse<T>> ToCreatedResult<T>(
        this Result<T> result,
        string actionName,
        Func<T, object> routeValues)
    {
        if (!result.Succeeded)
        {
            return result.ToErrorResult();
        }

        return new CreatedAtActionResult(
            actionName,
            controllerName: null,
            routeValues(result.Value!),
            ApiResponse<T>.Ok(result.Value!, result.Message));
    }

    /// <summary>
    /// Maps a failed result to its status code and error envelope. Exposed separately so that endpoints
    /// whose success response is not JSON — a file download, for instance — can still report failures in
    /// exactly the same shape as every other endpoint, rather than inventing a second error format.
    /// </summary>
    public static ObjectResult ToErrorResult<T>(this Result<T> result)
    {
        var statusCode = result.Status switch
        {
            ResultStatus.ValidationFailed => StatusCodes.Status400BadRequest,
            ResultStatus.Unauthorized => StatusCodes.Status401Unauthorized,
            ResultStatus.Forbidden => StatusCodes.Status403Forbidden,
            ResultStatus.NotFound => StatusCodes.Status404NotFound,
            ResultStatus.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return new ObjectResult(ApiResponse<T>.Fail(result.Message, result.Errors))
        {
            StatusCode = statusCode
        };
    }
}
