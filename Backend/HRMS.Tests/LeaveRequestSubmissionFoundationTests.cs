using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Application.Services;

namespace HRMS.Tests;

public sealed class LeaveRequestSubmissionFoundationTests
{
    [Fact]
    public async Task Validation_failure_is_returned_without_persistence()
    {
        var expected = Result<LeaveRequestValidationResult>.Invalid("configuration", "UnsupportedConfiguration: test");
        var validation = new FixedValidationService(expected);
        var service = new LeaveRequestSubmissionService(
            null!,
            new FixedIdentityResolver(),
            validation,
            new NoOpSubmissionLock(),
            TimeProvider.System);

        var result = await service.SubmitAsync(Input());

        Assert.False(result.Succeeded);
        Assert.Equal(expected.Message, result.Message);
        Assert.Equal(1, validation.Calls);
    }

    [Fact]
    public void Submission_contract_contains_only_client_authoritative_fields()
    {
        Assert.Equal(
            ["LeaveTypeId", "StartDate", "EndDate", "IdempotencyKey"],
            typeof(LeaveRequestSubmissionInput).GetProperties().Select(x => x.Name).ToArray());
    }

    private static LeaveRequestSubmissionInput Input() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        new(2026, 9, 1),
        new(2026, 9, 1),
        "submission-test-key");

    private sealed class FixedIdentityResolver : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"))));
    }

    private sealed class FixedValidationService(Result<LeaveRequestValidationResult> result) : ILeaveRequestValidationService
    {
        public int Calls { get; private set; }

        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(
            LeaveRequestValidationInput input,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class NoOpSubmissionLock : ILeaveRequestSubmissionLock
    {
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
