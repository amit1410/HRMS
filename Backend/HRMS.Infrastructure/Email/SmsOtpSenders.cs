using System.Text.Json;
using HRMS.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRMS.Infrastructure.Email;

public sealed class DevelopmentSmsOtpSender(bool exposeOtp = true) : ISmsOtpSender
{
    public Task<string?> SendAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(exposeOtp ? message.Otp : null);
}

public sealed class Msg91SmsOtpSender(HttpClient client, IConfiguration configuration, ILogger<Msg91SmsOtpSender> logger) : ISmsOtpSender
{
    public async Task<string?> SendAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default)
    {
        var authKey = configuration["Msg91:AuthKey"];
        if (string.IsNullOrWhiteSpace(authKey)) throw new InvalidOperationException("MSG91 configuration is incomplete.");
        var mobile = TrustedPhoneNumber.Normalize(message.Destination, configuration["Msg91:DefaultCountryCode"] ?? "91");
        var endpoint = configuration["Msg91:Endpoint"] ?? "sendotp.php";
        var query = $"authkey={Uri.EscapeDataString(authKey)}&mobile={Uri.EscapeDataString(mobile)}&otp={Uri.EscapeDataString(message.Otp)}";
        var sender = configuration["Msg91:SenderId"];
        if (!string.IsNullOrWhiteSpace(sender)) query += $"&sender={Uri.EscapeDataString(sender)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?{query}");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) { logger.LogWarning("MSG91 OTP delivery failed with status {StatusCode}.", (int)response.StatusCode); throw new InvalidOperationException("SMS delivery failed."); }
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("type", out var type) || !string.Equals(type.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SMS delivery failed.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("SMS delivery failed.");
        }
        return null;
    }
}

internal static class TrustedPhoneNumber
{
    public static string Normalize(string value, string defaultCountryCode)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.Length == 10 && !string.IsNullOrWhiteSpace(defaultCountryCode)) digits = defaultCountryCode + digits;
        if (digits.Length is < 10 or > 15) throw new InvalidOperationException("The trusted mobile number is invalid.");
        return digits;
    }
}
