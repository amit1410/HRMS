using System.Net;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.EmployeeImportPreflight;
using HRMS.Infrastructure.BulkImport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRMS.Tests;

public sealed class WorkerStateAttestationControllerTests
{
    private const string Challenge = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void AuthenticatedLocalNormalProfileReturnsOnlyRuntimeIdentityAndDisabledState()
    {
        var controller = CreateController(enabled: false);

        var result = Assert.IsType<OkObjectResult>(controller.Get("normal").Result);
        var report = Assert.IsType<WorkerStateAttestation>(result.Value);

        Assert.Equal(Environment.ProcessId, report.ProcessId);
        Assert.True(report.ProcessStartIdentity > 0);
        Assert.False(report.EmployeeBulkImportWorkerEnabled);
        Assert.True(report.AttestationValid);
        Assert.Equal(4, typeof(WorkerStateAttestation).GetProperties().Length);
        Assert.Equal(Challenge, controller.Response.Headers[WorkerStateAttestationController.ChallengeResponseHeader]);
        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportProfileIsValidOnlyForExactTargetAndAllOtherWorkersDisabled()
    {
        var validController = CreateController(enabled: true, targetCode: "WEB1", targetOperation: DeploymentReadinessRules.TargetOperationId);
        var valid = Assert.IsType<WorkerStateAttestation>(Assert.IsType<OkObjectResult>(validController.Get("import").Result).Value);
        Assert.True(valid.EmployeeBulkImportWorkerEnabled);
        Assert.True(valid.AttestationValid);

        var mismatchedTarget = CreateController(enabled: true, targetCode: "WEB2", targetOperation: DeploymentReadinessRules.TargetOperationId);
        var mismatch = Assert.IsType<WorkerStateAttestation>(Assert.IsType<OkObjectResult>(mismatchedTarget.Get("import").Result).Value);
        Assert.False(mismatch.AttestationValid);

        var mismatchedOperation = CreateController(enabled: true, targetCode: "WEB1", targetOperation: Guid.NewGuid());
        var operationMismatch = Assert.IsType<WorkerStateAttestation>(Assert.IsType<OkObjectResult>(mismatchedOperation.Get("import").Result).Value);
        Assert.False(operationMismatch.AttestationValid);

        var unrelatedEnabled = CreateController(enabled: true, welcomeEnabled: true,
            targetCode: "WEB1", targetOperation: DeploymentReadinessRules.TargetOperationId);
        var unrelated = Assert.IsType<WorkerStateAttestation>(Assert.IsType<OkObjectResult>(unrelatedEnabled.Get("import").Result).Value);
        Assert.False(unrelated.AttestationValid);
    }

    [Fact]
    public void RejectsInvalidChallengeAndNonLoopbackRequests()
    {
        var invalidChallenge = CreateController(enabled: false, challenge: "short");
        Assert.IsType<BadRequestResult>(invalidChallenge.Get("normal").Result);
        Assert.False(invalidChallenge.Response.Headers.ContainsKey(WorkerStateAttestationController.ChallengeResponseHeader));

        var remote = CreateController(enabled: false, remoteAddress: IPAddress.Parse("192.0.2.10"));
        Assert.IsType<NotFoundResult>(remote.Get("normal").Result);
    }

    [Fact]
    public void EndpointRequiresEmployeeImportPermission()
    {
        var method = typeof(WorkerStateAttestationController).GetMethod(nameof(WorkerStateAttestationController.Get))!;
        var permission = Assert.Single(method.GetCustomAttributes(typeof(HasPermissionAttribute), inherit: true));
        Assert.Equal(Permissions.Employee.Import, Assert.IsType<HasPermissionAttribute>(permission).Permission);
    }

    [Fact]
    public void RejectsAuthenticatedCallerFromAnotherTenant()
    {
        var controller = CreateController(enabled: false, tenantId: Guid.NewGuid());
        Assert.IsType<ForbidResult>(controller.Get("normal").Result);
    }

    private static WorkerStateAttestationController CreateController(
        bool enabled,
        string? targetCode = null,
        Guid? targetOperation = null,
        bool welcomeEnabled = false,
        string? challenge = null,
        IPAddress? remoteAddress = null,
        Guid? tenantId = null)
    {
        var options = Options.Create(new EmployeeBulkImportWorkerOptions
        {
            Enabled = enabled,
            TargetTenantCode = targetCode,
            TargetOperationId = targetOperation
        });
        var worker = new EmployeeBulkImportWorker(null!, options, NullLogger<EmployeeBulkImportWorker>.Instance);
        var services = new ServiceCollection()
            .AddSingleton(new WelcomeInvitationOptions { Enabled = welcomeEnabled })
            .AddSingleton(new LeaveReminderOptions { Enabled = false })
            .AddSingleton(new ConfirmationReminderOptions { Enabled = false })
            .AddSingleton(new LeaveAccrualOptions { Enabled = false })
            .AddSingleton(new AttendanceDeviceWorkerOptions { Enabled = false })
            .BuildServiceProvider();
        var controller = new WorkerStateAttestationController([worker], services,
            new TestTenantContext(tenantId ?? Guid.Parse("16313e68-2e72-4a6a-b689-79b271cec295")))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Connection.RemoteIpAddress = remoteAddress ?? IPAddress.Loopback;
        controller.HttpContext.Connection.LocalIpAddress = IPAddress.Loopback;
        controller.HttpContext.Request.Host = new HostString("web1.localhost");
        controller.HttpContext.Request.Headers[WorkerStateAttestationController.ChallengeHeader] = challenge ?? Challenge;
        return controller;
    }

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = Guid.NewGuid();
        public bool HasTenant => TenantId.HasValue;
    }
}
