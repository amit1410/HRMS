using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

public sealed class DevelopmentEmailSenderTests
{
    [Fact]
    public async Task Welcome_invitation_does_not_report_success_when_development_provider_does_not_send()
    {
        var sender = new DevelopmentEmailSender(NullLogger<DevelopmentEmailSender>.Instance);

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendWelcomeInviteAsync(
            new WelcomeEmailMessage("employee@example.test", "Employee", "Tenant", "https://invite.example.test/token", DateTime.UtcNow)));

        Assert.Equal(EmailDeliveryFailureKind.Server, exception.Kind);
        Assert.Equal("Welcome invitation delivery is disabled for the Development email provider.", exception.Message);
        Assert.Null(exception.Host);
        Assert.Equal(0, exception.Port);
    }
}
