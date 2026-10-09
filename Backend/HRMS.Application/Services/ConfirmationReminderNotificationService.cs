using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>
/// Recipient resolution and email delivery for confirmation-review reminders. Emails are resolved through the
/// current account→employee link (same path as <see cref="LeaveNotificationService"/>); the manager is the
/// employee's effective reporting manager; HRBPs are the tenant users currently holding the HRBP role. Links
/// are relative so they stay bound to the tenant workspace and embed no database identifier beyond the route.
/// </summary>
public sealed class ConfirmationReminderNotificationService : IConfirmationReminderNotificationService
{
    private readonly IHrmsDbContext _db;
    private readonly IEmailSender _email;
    private readonly IEmployeeManagerResolver _managerResolver;
    private readonly IConfirmationHrbpResolver _hrbpResolver;
    private readonly ILogger<ConfirmationReminderNotificationService> _logger;

    public ConfirmationReminderNotificationService(
        IHrmsDbContext db,
        IEmailSender email,
        IEmployeeManagerResolver managerResolver,
        IConfirmationHrbpResolver hrbpResolver,
        ILogger<ConfirmationReminderNotificationService> logger)
    {
        _db = db;
        _email = email;
        _managerResolver = managerResolver;
        _hrbpResolver = hrbpResolver;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConfirmationReminderRecipient>> ResolveRecipientsAsync(
        ConfirmationReminderCase reviewCase, ConfirmationRecipientToggles toggles, CancellationToken cancellationToken = default)
    {
        var recipients = new List<ConfirmationReminderRecipient>();

        if (toggles.Employee)
        {
            var employeeEmail = await ResolveLinkedEmailAsync(reviewCase.TenantId, reviewCase.EmployeeId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(employeeEmail))
                recipients.Add(new(ConfirmationReminderRecipientType.Employee, employeeEmail!, reviewCase.EmployeeId));
            else
                _logger.LogInformation("Confirmation reminder: employee {EmployeeId} has no linked email; skipping employee recipient.", reviewCase.EmployeeId);
        }

        if (toggles.Manager)
        {
            var manager = await _managerResolver.ResolveAsync(reviewCase.EmployeeId, reviewCase.ProbationEndDate, cancellationToken);
            if (manager.Succeeded && manager.Value?.ManagerId is Guid managerId)
            {
                var managerEmail = await ResolveLinkedEmailAsync(reviewCase.TenantId, managerId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(managerEmail))
                    recipients.Add(new(ConfirmationReminderRecipientType.Manager, managerEmail!, managerId));
                else
                    _logger.LogInformation("Confirmation reminder: manager {ManagerId} of employee {EmployeeId} has no linked email; skipping manager recipient.", managerId, reviewCase.EmployeeId);
            }
            else
            {
                _logger.LogInformation("Confirmation reminder: no effective manager for employee {EmployeeId} ({Reason}); skipping manager recipient.", reviewCase.EmployeeId, manager.Value?.Message ?? manager.Message);
            }
        }

        if (toggles.Hrbp)
        {
            var hrbps = await ResolveHrbpRecipientsAsync(reviewCase, cancellationToken);
            if (hrbps.Count == 0)
                _logger.LogInformation("Confirmation reminder: tenant {TenantId} has no HRBP with a linked email; skipping HRBP recipients for employee {EmployeeId}.", reviewCase.TenantId, reviewCase.EmployeeId);
            recipients.AddRange(hrbps);
        }

        // Guard against a recipient that happens to resolve to two kinds (e.g. a manager who is also an HRBP):
        // the employee is addressed once per email address.
        return recipients
            .GroupBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    public async Task<ConfirmationNotificationDeliveryResult> SendAsync(
        ConfirmationReminderCase reviewCase, ConfirmationReminderRecipient recipient, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipient.Email)) return ConfirmationNotificationDeliveryResult.Skipped;
        try
        {
            var subject = $"Confirmation review due: {reviewCase.EmployeeName}";
            var audience = recipient.Type switch
            {
                ConfirmationReminderRecipientType.Employee => "Your probation period is approaching completion.",
                ConfirmationReminderRecipientType.Manager => $"A direct report, {reviewCase.EmployeeName}, is approaching probation completion.",
                _ => $"{reviewCase.EmployeeName} is approaching probation completion."
            };
            var body =
                $"Hello,\n\n{audience}\n\n" +
                $"Policy: {reviewCase.PolicyName}\n" +
                $"Probation end date: {reviewCase.ProbationEndDate:yyyy-MM-dd}\n" +
                $"Review from: {reviewCase.TriggerDate:yyyy-MM-dd}\n\n" +
                $"Please action the confirmation review in HRMS: /confirmation/review-cases";
            await _email.SendLeaveNotificationAsync(new(recipient.Email, subject, body), cancellationToken);
            return ConfirmationNotificationDeliveryResult.Sent;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Confirmation reminder delivery timed out for employee {EmployeeId} recipient {RecipientType}.", reviewCase.EmployeeId, recipient.Type);
            return ConfirmationNotificationDeliveryResult.Failed;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Confirmation reminder delivery failed for employee {EmployeeId} recipient {RecipientType}.", reviewCase.EmployeeId, recipient.Type);
            return ConfirmationNotificationDeliveryResult.Failed;
        }
    }

    private async Task<IReadOnlyList<ConfirmationReminderRecipient>> ResolveHrbpRecipientsAsync(ConfirmationReminderCase reviewCase, CancellationToken cancellationToken)
    {
        var resolved = await _hrbpResolver.ResolveAsync(
            reviewCase.TenantId, reviewCase.EmployeeId, reviewCase.ProbationEndDate, cancellationToken);
        if (resolved.Status != ConfirmationHrbpResolutionStatus.Resolved || resolved.HrbpEmployeeId is not Guid employeeId)
            return [];

        var email = await ResolveLinkedEmailAsync(reviewCase.TenantId, employeeId, cancellationToken);
        return string.IsNullOrWhiteSpace(email)
            ? []
            : [new ConfirmationReminderRecipient(ConfirmationReminderRecipientType.Hrbp, email, employeeId)];
    }

    private Task<string?> ResolveLinkedEmailAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken) =>
        _db.AccountEmployeeCurrentLinks.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId)
            .Join(_db.Users, link => new { link.TenantId, link.UserId }, user => new { user.TenantId, UserId = user.Id }, (_, user) => user.Email)
            .SingleOrDefaultAsync(cancellationToken);
}
