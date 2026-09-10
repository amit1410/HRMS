using System.Net;
using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

public sealed class Msg91SmsOtpSenderTests
{
    [Fact]
    public async Task Sends_flow_request_with_auth_header_template_sender_mobile_and_otp()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"type\":\"success\",\"message\":\"request-id\"}");
        var sender = CreateSender(handler);

        var result = await sender.SendAsync(Message("9876543210", "654321"));

        Assert.Null(result);
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.Equal("/api/v5/flow/", handler.Request.RequestUri!.AbsolutePath);
        Assert.True(handler.Request.Headers.TryGetValues("authkey", out var authValues));
        Assert.Equal("test-auth-key", Assert.Single(authValues!));

        using var json = JsonDocument.Parse(handler.Body!);
        var root = json.RootElement;
        Assert.Equal("approved-flow-id", root.GetProperty("flow_id").GetString());
        Assert.Equal("ANEVRA", root.GetProperty("sender").GetString());
        var recipient = root.GetProperty("recipients")[0];
        Assert.Equal("919876543210", recipient.GetProperty("mobiles").GetString());
        Assert.Equal("654321", recipient.GetProperty("VAR1").GetString());
    }

    [Fact]
    public async Task Preserves_already_international_mobile_number()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"type\":\"success\"}");
        var sender = CreateSender(handler);

        await sender.SendAsync(Message("+919876543210", "123456"));

        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("919876543210", json.RootElement.GetProperty("recipients")[0].GetProperty("mobiles").GetString());
    }

    [Fact]
    public async Task Handles_http_failure_without_exposing_provider_response()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized, "{\"type\":\"error\",\"message\":\"invalid authkey\"}");
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message("9876543210", "123456")));

        Assert.Equal("MSG91 SMS delivery failed.", exception.Message);
        Assert.DoesNotContain("invalid authkey", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_invalid_mobile_before_sending()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"type\":\"success\"}");
        var sender = CreateSender(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message("123", "123456")));
        Assert.Null(handler.Request);
    }

    private static Msg91SmsOtpSender CreateSender(RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Msg91:AuthKey"] = "test-auth-key",
            ["Msg91:FlowId"] = "approved-flow-id",
            ["Msg91:SenderId"] = "ANEVRA",
            ["Msg91:DefaultCountryCode"] = "91"
        }).Build();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://control.msg91.com/api/v5/") };
        return new Msg91SmsOtpSender(client, configuration, NullLogger<Msg91SmsOtpSender>.Instance);
    }

    private static OtpDeliveryMessage Message(string destination, string otp) =>
        new(destination, otp, "Tenant", "Person", DateTime.UtcNow, PasswordRecoveryChannel.Sms);

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
