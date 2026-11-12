using System.Diagnostics;
using System.Net;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.BulkImport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/deployment/worker-state")]
public sealed class WorkerStateAttestationController(
    IEnumerable<IHostedService> hostedServices,
    IServiceProvider services,
    ITenantContext tenant) : ControllerBase
{
    public const string ChallengeHeader = "X-HRMS-Attestation-Challenge";
    public const string ChallengeResponseHeader = "X-HRMS-Attestation-Response";
    private const string TargetTenantCode = "WEB1";
    private static readonly Guid TargetTenantId = Guid.Parse("16313e68-2e72-4a6a-b689-79b271cec295");
    private static readonly Guid TargetOperationId = Guid.Parse("b89d04aa-1419-4c85-a7e3-48ab1d92d111");

    [HttpGet]
    [HasPermission(Permissions.Employee.Import)]
    [ProducesResponseType(typeof(WorkerStateAttestation), StatusCodes.Status200OK)]
    public ActionResult<WorkerStateAttestation> Get([FromQuery] string profile)
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";

        if (!IsLoopbackRequest()) return NotFound();
        if (tenant.TenantId != TargetTenantId || tenant.UserId is null) return Forbid();
        var challenge = Request.Headers[ChallengeHeader].ToString();
        if (!WorkerStateAttestationRules.IsValidChallenge(challenge)) return BadRequest();
        if (profile is not ("normal" or "import")) return BadRequest();

        var workers = hostedServices.OfType<EmployeeBulkImportWorker>().ToArray();
        var worker = workers.Length == 1 ? workers[0] : null;
        var startIdentity = GetCurrentProcessStartIdentity();
        if (worker is null || startIdentity is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var profileValid = profile == "normal"
            ? !worker.Enabled
            : worker.Enabled
                && worker.IsConfiguredFor(TargetTenantCode, TargetOperationId)
                && AreUnrelatedWorkersDisabled();

        Response.Headers[ChallengeResponseHeader] = challenge;
        return Ok(new WorkerStateAttestation(
            Environment.ProcessId,
            startIdentity.Value,
            worker.Enabled,
            profileValid));
    }

    private bool IsLoopbackRequest()
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        var local = HttpContext.Connection.LocalIpAddress;
        return remote is not null && local is not null
            && IPAddress.IsLoopback(remote) && IPAddress.IsLoopback(local)
            && string.Equals(HttpContext.Request.Host.Host, "web1.localhost", StringComparison.OrdinalIgnoreCase);
    }

    private bool AreUnrelatedWorkersDisabled()
    {
        var welcome = services.GetService<WelcomeInvitationOptions>();
        var leave = services.GetService<LeaveReminderOptions>();
        var confirmation = services.GetService<ConfirmationReminderOptions>();
        var accrual = services.GetService<LeaveAccrualOptions>();
        var attendance = services.GetService<AttendanceDeviceWorkerOptions>();
        return welcome is not null && !welcome.Enabled
            && leave is not null && !leave.Enabled
            && confirmation is not null && !confirmation.Enabled
            && accrual is not null && !accrual.Enabled
            && attendance is not null && !attendance.Enabled;
    }

    private static long? GetCurrentProcessStartIdentity()
    {
        try { return Process.GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc(); }
        catch (Exception) { return null; }
    }
}

public sealed record WorkerStateAttestation(
    int ProcessId,
    long ProcessStartIdentity,
    bool EmployeeBulkImportWorkerEnabled,
    bool AttestationValid);

public static class WorkerStateAttestationRules
{
    public static bool IsValidChallenge(string? value) => value is { Length: 64 }
        && value.All(Uri.IsHexDigit);
}
