using HRMS.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HRMS.API.Filters;

/// <summary>Applies the same tenant/effective-employment boundary to every employee sub-resource.</summary>
public sealed class EmployeeScopeAuthorizationFilter(
    IEmployeeAccessScopeService accessScope,
    TimeProvider timeProvider) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.RouteData.Values.TryGetValue("id", out var value)
            || !Guid.TryParse(value?.ToString(), out var employeeId))
        {
            context.Result = new NotFoundResult();
            return;
        }

        var date = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
        if (!await accessScope.CanAccessEmployeeAsync(employeeId, date, context.HttpContext.RequestAborted))
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}
