using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Application.Services;

namespace HRMS.Tests;

public sealed class LeaveRequestValidationFoundationTests
{
    [Fact]
    public async Task Invalid_shape_is_rejected_before_identity_or_database_access()
    {
        var identity = new ThrowingIdentityResolver();
        var service = new LeaveRequestValidationService(
            null!, identity, null!, null!, null!);

        var result = await service.ValidateAsync(new(
            Guid.Empty,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 2, 1),
            "key"));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("leaveTypeId", result.Errors![0].Field);
        Assert.False(identity.Called);
    }

    [Fact]
    public async Task Unlinked_identity_failure_is_propagated_without_side_effects()
    {
        var identity = new FixedIdentityResolver(Result<RuntimeEmployeeIdentity>.NotFound(
            "The authenticated account is not linked to an Employee."));
        var service = new LeaveRequestValidationService(
            null!, identity, null!, null!, null!);

        var result = await service.ValidateAsync(new(
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 1),
            "retry-key"));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal("The authenticated account is not linked to an Employee.", result.Message);
    }

    [Fact]
    public void Semantic_fingerprint_is_deterministic_and_excludes_submission_time()
    {
        var input = new LeaveRequestValidationInput(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 2),
            "key");
        var days = new[]
        {
            new LeaveRequestDayValidationResult(new DateOnly(2026, 9, 1), 1.000m, 1.000m, "WorkingDay", "MVP full-day baseline", true),
            new LeaveRequestDayValidationResult(new DateOnly(2026, 9, 2), 1.000m, 1.000m, "WorkingDay", "MVP full-day baseline", true)
        };

        var first = LeaveRequestValidationFingerprint.Create(
            Guid.Parse("22222222-2222-2222-2222-222222222222"), input, days);
        var second = LeaveRequestValidationFingerprint.Create(
            Guid.Parse("22222222-2222-2222-2222-222222222222"), input with { IdempotencyKey = "different-retry-key" }, days);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void Semantic_fingerprint_changes_when_employee_leave_type_or_dates_change()
    {
        var input = new LeaveRequestValidationInput(Guid.NewGuid(), new(2026, 9, 1), new(2026, 9, 2), "key");
        var days = new[]
        {
            new LeaveRequestDayValidationResult(new(2026, 9, 1), 1m, 1m, null, null, true),
            new LeaveRequestDayValidationResult(new(2026, 9, 2), 1m, 1m, null, null, true)
        };
        var employee = Guid.NewGuid();
        var first = LeaveRequestValidationFingerprint.Create(employee, input, days);

        Assert.NotEqual(first, LeaveRequestValidationFingerprint.Create(Guid.NewGuid(), input, days));
        Assert.NotEqual(first, LeaveRequestValidationFingerprint.Create(employee, input with { LeaveTypeId = Guid.NewGuid() }, days));
        Assert.NotEqual(first, LeaveRequestValidationFingerprint.Create(employee, input with { EndDate = new(2026, 9, 3) }, days));
        Assert.NotEqual(first, LeaveRequestValidationFingerprint.Create(employee, input, days[..1]));
    }

    private sealed class ThrowingIdentityResolver : IEmployeeIdentityResolver
    {
        public bool Called { get; private set; }

        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default)
        {
            Called = true;
            throw new InvalidOperationException("Identity must not be resolved for invalid shape input.");
        }
    }

    private sealed class FixedIdentityResolver(Result<RuntimeEmployeeIdentity> result) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
