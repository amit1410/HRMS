namespace HRMS.Application.DTOs.Employees;

public sealed record PortalAccountDto(
    Guid EmployeeId,
    string State,
    Guid? UserId,
    string? Email,
    DateTime? InvitationExpiresAtUtc,
    DateTime? LastInviteSentAtUtc);

public sealed record CreatePortalAccountResponse(
    PortalAccountDto Account,
    string? DevelopmentInviteUrl,
    string Message);

public sealed class CreatePortalAccountRequest
{
    public string? LoginEmail { get; set; }
}
