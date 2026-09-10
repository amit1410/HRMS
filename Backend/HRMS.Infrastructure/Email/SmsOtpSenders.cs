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
        var flowId = configuration["Msg91:FlowId"];
        var senderId = configuration["Msg91:SenderId"];
        if (string.IsNullOrWhiteSpace(authKey) || string.IsNullOrWhiteSpace(flowId) || string.IsNullOrWhiteSpace(senderId))
            throw new InvalidOperationException("MSG91 configuration is incomplete.");

        var mobile = TrustedPhoneNumber.Normalize(message.Destination, configuration["Msg91:DefaultCountryCode"] ?? "91");
        var variableName = configuration["Msg91:OtpVariable"] ?? "VAR1";
        var payload = new Dictionary<string, object?>
        {
            ["flow_id"] = flowId,
            ["sender"] = senderId,
            ["recipients"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["mobiles"] = mobile,
                    [variableName] = message.Otp
                }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "flow/")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("authkey", authKey);
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("MSG91 SMS delivery timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException("MSG91 SMS delivery failed due to a network error.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("MSG91 SMS delivery failed with HTTP status {StatusCode}.", (int)response.StatusCode);
                throw new InvalidOperationException("MSG91 SMS delivery failed.");
            }

            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                if (!document.RootElement.TryGetProperty("type", out var type)
                    || !string.Equals(type.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("MSG91 SMS delivery was rejected.");
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("MSG91 returned an invalid response.", exception);
            }
        }
        return null;
    }
}

internal static class TrustedPhoneNumber
{
    public static string Normalize(string value, string defaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("The trusted mobile number is invalid.");
        var trimmed = value.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];
        else if (digits.Length == 11 && digits.StartsWith("0", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(defaultCountryCode))
            digits = defaultCountryCode + digits[1..];
        else if (digits.Length == 10 && !string.IsNullOrWhiteSpace(defaultCountryCode))
            digits = defaultCountryCode + digits;
        if (digits.Length is < 10 or > 15) throw new InvalidOperationException("The trusted mobile number is invalid.");
        return digits;
    }
}
