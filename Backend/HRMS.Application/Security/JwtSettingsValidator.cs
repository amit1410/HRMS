using Microsoft.Extensions.Options;

namespace HRMS.Application.Security;

/// <summary>
/// Runs <see cref="JwtSettings.Validate"/> through the options pipeline, so the check applies to the
/// effective configuration at startup rather than to a snapshot taken while services were being
/// registered. Paired with <c>ValidateOnStart()</c> this still fails fast — before the first request —
/// but it can no longer pass on one set of values while the token signer uses another.
/// </summary>
public sealed class JwtSettingsValidator : IValidateOptions<JwtSettings>
{
    public ValidateOptionsResult Validate(string? name, JwtSettings options)
    {
        var error = options.Validate();

        return error is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"JWT configuration is invalid and the API cannot start. {error}");
    }
}
