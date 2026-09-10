using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public sealed class LeaveNotificationService : ILeaveNotificationService
{
    private readonly IHrmsDbContext _db;
    private readonly IEmailSender _email;
    private readonly IEmployeeManagerResolver _managerResolver;
    private readonly ILogger<LeaveNotificationService> _logger;

    public LeaveNotificationService(IHrmsDbContext db, IEmailSender email, IEmployeeManagerResolver managerResolver, ILogger<LeaveNotificationService> logger)
    { _db = db; _email = email; _managerResolver = managerResolver; _logger = logger; }

    public async Task NotifyAsync(Guid requestId, LeaveRequestEventType eventType, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = await _db.LeaveRequests.AsNoTracking()
                .Where(x => x.Id == requestId)
                .Select(x => new { x.TenantId, x.EmployeeId, x.StartDate, x.EndDate, x.ChargeableQuantity, x.Status, EmployeeName = x.Employee!.FirstName + " " + x.Employee.LastName, x.LeaveType!.Name })
                .SingleOrDefaultAsync(cancellationToken);
            if (request is null) return;

            var recipients = new List<(string Email, string Link)>();
            if (eventType is LeaveRequestEventType.Approved or LeaveRequestEventType.Rejected or LeaveRequestEventType.Cancelled)
            {
                var employeeEmail = await ResolveLinkedEmailAsync(request.TenantId, request.EmployeeId, cancellationToken);
                AddRecipient(recipients, employeeEmail, "/leave-management/my-requests/" + requestId);
            }

            if (eventType is LeaveRequestEventType.Submitted or LeaveRequestEventType.Withdrawn or LeaveRequestEventType.Cancelled)
            {
                var manager = await _managerResolver.ResolveAsync(request.EmployeeId, request.StartDate, cancellationToken);
                if (manager.Succeeded && manager.Value?.ManagerId is Guid managerId)
                {
                    var managerEmail = await ResolveLinkedEmailAsync(request.TenantId, managerId, cancellationToken);
                    AddRecipient(recipients, managerEmail, "/leave-management/approvals/" + requestId);
                }
            }

            var subject = eventType switch
            {
                LeaveRequestEventType.Submitted => "Leave approval required",
                LeaveRequestEventType.Approved => "Leave request approved",
                LeaveRequestEventType.Rejected => "Leave request rejected",
                LeaveRequestEventType.Withdrawn => "Leave request withdrawn",
                LeaveRequestEventType.Cancelled => "Leave request cancelled",
                _ => null
            };
            if (subject is null) return;
            var body = $"Hello,\n\n{request.EmployeeName}'s {request.Name} request from {request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd} ({request.ChargeableQuantity} day(s)) is {request.Status}.\n\nPlease review it in HRMS.";
            foreach (var recipient in recipients.DistinctBy(x => x.Email, StringComparer.OrdinalIgnoreCase))
                await _email.SendLeaveNotificationAsync(new(recipient.Email, subject, body), cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { _logger.LogWarning("Leave notification delivery timed out for request {LeaveRequestId}.", requestId); }
        catch (Exception exception) { _logger.LogWarning(exception, "Leave notification delivery failed for request {LeaveRequestId} and event {EventType}.", requestId, eventType); }
    }

    private static void AddRecipient(List<(string Email, string Link)> recipients, string? email, string link)
    { if (!string.IsNullOrWhiteSpace(email)) recipients.Add((email, link)); }

    private Task<string?> ResolveLinkedEmailAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken) =>
        _db.AccountEmployeeCurrentLinks.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId)
            .Join(_db.Users, link => new { link.TenantId, link.UserId }, user => new { user.TenantId, UserId = user.Id }, (_, user) => user.Email)
            .SingleOrDefaultAsync(cancellationToken);
}
