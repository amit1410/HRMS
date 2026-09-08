using HRMS.API.Controllers;
using HRMS.API.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace HRMS.Tests;

public sealed class PasswordRecoveryRateLimitTests
{
    [Fact]
    public void Development_defaults_to_twenty_requests_per_one_minute()
    {
        var settings = PasswordRecoveryRateLimitSettings.Load(Configuration(), isDevelopment: true);

        Assert.Equal(20, settings.PermitLimit);
        Assert.Equal(1, settings.WindowMinutes);
    }

    [Fact]
    public void Production_defaults_to_five_requests_per_ten_minutes()
    {
        var settings = PasswordRecoveryRateLimitSettings.Load(Configuration(), isDevelopment: false);

        Assert.Equal(5, settings.PermitLimit);
        Assert.Equal(10, settings.WindowMinutes);
    }

    [Fact]
    public void Explicit_values_override_environment_defaults()
    {
        var settings = PasswordRecoveryRateLimitSettings.Load(Configuration(new()
        {
            ["PasswordRecoveryRateLimit:PermitLimit"] = "30",
            ["PasswordRecoveryRateLimit:WindowMinutes"] = "2"
        }), isDevelopment: true);

        Assert.Equal(30, settings.PermitLimit);
        Assert.Equal(2, settings.WindowMinutes);
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("-1", null)]
    [InlineData(null, "0")]
    [InlineData(null, "-1")]
    public void Non_positive_values_are_rejected(string? permitLimit, string? windowMinutes)
    {
        var values = new Dictionary<string, string?>();
        if (permitLimit is not null) values["PasswordRecoveryRateLimit:PermitLimit"] = permitLimit;
        if (windowMinutes is not null) values["PasswordRecoveryRateLimit:WindowMinutes"] = windowMinutes;

        var settings = PasswordRecoveryRateLimitSettings.Load(Configuration(values), isDevelopment: true);

        Assert.NotNull(settings.Validate());
    }

    [Fact]
    public void Invalid_integer_values_are_rejected_during_loading()
    {
        var configuration = Configuration(new()
        {
            ["PasswordRecoveryRateLimit:PermitLimit"] = "not-a-number"
        });

        Assert.Throws<InvalidOperationException>(() =>
            PasswordRecoveryRateLimitSettings.Load(configuration, isDevelopment: true));
    }

    [Fact]
    public void All_forgot_password_endpoints_use_the_password_recovery_policy()
    {
        var endpointNames = new[] { "ForgotPassword", "SendOtp", "ResendOtp", "VerifyOtp", "ResetPassword" };

        foreach (var endpointName in endpointNames)
        {
            var method = typeof(AuthController).GetMethod(endpointName);
            Assert.NotNull(method);
            var attribute = Assert.Single(method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true));
            Assert.Equal(RateLimitingPolicies.PasswordRecovery, ((EnableRateLimitingAttribute)attribute).PolicyName);
        }
    }

    private static IConfiguration Configuration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? new Dictionary<string, string?>()).Build();
}
