using FluentValidation;
using HRMS.API.Common;
using HRMS.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HRMS.API.Filters;

/// <summary>
/// Runs the FluentValidation validator registered for each action argument before the action executes,
/// short-circuiting with 400 and the standard error envelope when a request is invalid.
/// <para>
/// <c>AddValidatorsFromAssembly</c> only places validators in the container; it does not hook them into
/// MVC. This filter is that hook, applied globally so every current and future endpoint validates its
/// input without the controller having to ask.
/// </para>
/// </summary>
public class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _serviceProvider;

    public ValidationFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new List<ValidationError>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (_serviceProvider.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                errors.AddRange(result.Errors.Select(failure =>
                    new ValidationError(FieldNames.ToCamelCase(failure.PropertyName), failure.ErrorMessage)));
            }
        }

        if (errors.Count > 0)
        {
            context.Result = new BadRequestObjectResult(
                ApiResponse.Fail("One or more validation errors occurred.", errors));
            return;
        }

        await next();
    }
}
