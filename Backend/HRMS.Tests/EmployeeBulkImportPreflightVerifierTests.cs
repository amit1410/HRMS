using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.Extensions.Options;

namespace HRMS.Tests;

public sealed class EmployeeBulkImportPreflightVerifierTests
{
    private static readonly DateTime Now = DateTime.Parse("2026-10-31T10:00:00Z").ToUniversalTime();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Missing_EmployeeImport_authorization_stops_before_database_or_artifact_access()
    {
        var readStore = new FakeReadStore(Snapshot(TenantId));
        var workbookStore = new FakeWorkbookStore();
        var verifier = Create(readStore, new TestContext(TenantId, UserId), new FakeAuthorization(false), workbookStore,
            new FakeValidation(Valid(1)));

        var report = await verifier.VerifyAsync(EmployeeBulkImportPreflightVerifier.TargetOperationId);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, x => x.Name == "authorization" && !x.Passed);
        Assert.Equal(0, readStore.FindCalls);
        Assert.Equal(0, workbookStore.OpenCalls);
    }

    [Fact]
    public async Task Enabled_worker_blocks_preflight_before_any_tenant_reads()
    {
        var readStore = new FakeReadStore(Snapshot(TenantId));
        var workbookStore = new FakeWorkbookStore();
        var verifier = new EmployeeBulkImportPreflightVerifier(readStore, new TestContext(TenantId, UserId),
            new FakeAuthorization(true), workbookStore, new FakeValidation(Valid(1)),
            Options.Create(new EmployeeBulkImportWorkerOptions { Enabled = true }), new FixedClock(Now), expectedRows: 1);

        var report = await verifier.VerifyAsync(EmployeeBulkImportPreflightVerifier.TargetOperationId);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, check => check.Name == "worker_disabled" && !check.Passed);
        Assert.Equal(0, readStore.FindCalls);
        Assert.Equal(0, workbookStore.OpenCalls);
    }

    [Fact]
    public async Task Different_authenticated_tenant_cannot_see_target_operation()
    {
        var otherTenantId = Guid.NewGuid();
        var readStore = new FakeReadStore(Snapshot(TenantId));
        var workbookStore = new FakeWorkbookStore();
        var verifier = Create(readStore, new TestContext(otherTenantId, Guid.NewGuid()), new FakeAuthorization(true), workbookStore,
            new FakeValidation(Valid(1)));

        var report = await verifier.VerifyAsync(EmployeeBulkImportPreflightVerifier.TargetOperationId);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, x => x.Name == "operation_identity" && !x.Passed && x.Category == "operation.not_found_in_authenticated_tenant");
        Assert.Equal(0, workbookStore.OpenCalls);
    }

    [Fact]
    public async Task Incorrect_operation_id_is_rejected_before_any_reads()
    {
        var readStore = new FakeReadStore(Snapshot(TenantId));
        var workbookStore = new FakeWorkbookStore();
        var verifier = Create(readStore, new TestContext(TenantId, UserId), new FakeAuthorization(true), workbookStore,
            new FakeValidation(Valid(1)));

        var report = await verifier.VerifyAsync(Guid.NewGuid());

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, x => x.Name == "operation_allowlist" && !x.Passed);
        Assert.Equal(0, readStore.FindCalls);
        Assert.Equal(0, workbookStore.OpenCalls);
    }

    [Fact]
    public async Task Active_processing_lease_fails_and_does_not_open_artifact()
    {
        var snapshot = Snapshot(TenantId) with
        {
            State = EmployeeImportOperationState.Claimed,
            ClaimLeaseId = Guid.NewGuid(),
            ClaimLeaseExpiresAtUtc = Now.AddMinutes(10)
        };
        var workbookStore = new FakeWorkbookStore();
        var verifier = Create(new FakeReadStore(snapshot), new TestContext(TenantId, UserId), new FakeAuthorization(true), workbookStore,
            new FakeValidation(Valid(1)));

        var report = await verifier.VerifyAsync(EmployeeBulkImportPreflightVerifier.TargetOperationId);

        Assert.False(report.Passed);
        Assert.True(report.ActiveLease);
        Assert.Contains(report.Checks, x => x.Name == "no_active_lease" && !x.Passed);
        Assert.Equal(0, workbookStore.OpenCalls);
    }

    [Fact]
    public async Task Invalid_authenticated_artifact_is_reported_as_sanitized_failure()
    {
        var workbookStore = new FakeWorkbookStore { Failure = new InvalidDataException("secret workbook details must not escape") };
        var verifier = Create(new FakeReadStore(Snapshot(TenantId)), new TestContext(TenantId, UserId), new FakeAuthorization(true), workbookStore,
            new FakeValidation(Valid(1)));

        var report = await verifier.VerifyAsync(EmployeeBulkImportPreflightVerifier.TargetOperationId);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, x => x.Name == "artifact_integrity" && !x.Passed && x.Category == "artifact.verification_failed");
        Assert.DoesNotContain(report.Checks, x => x.Category.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Invalid_current_master_references_fail_with_only_aggregate_categories()
    {
        var preview = new EmployeeBulkUploadPreviewDto(1, 0, 1, 0,
        [new(2, "Invalid", [new("DepartmentId", "Department Code *", 1, "reference.department_missing", "raw detail omitted", "guidance omitted")])]);
        var result = HRMS.Application.Common.Result<EmployeeBulkUploadValidationResult>.Success(new(preview, []));
        var verifier = Create(new FakeReadStore(Snapshot(TenantId)), new TestContext(TenantId, UserId), new FakeAuthorization(true),
            new FakeWorkbookStore(), new FakeValidation(result));

        var report = await verifier.VerifyAsync(EmployeeBulkImportPreflightVerifier.TargetOperationId);

        Assert.False(report.Passed);
        Assert.Equal(0, report.ValidatedRows);
        Assert.Equal(1, report.InvalidRows);
        Assert.Equal(1, report.SanitizedErrorCategories["reference.department_missing"]);
        Assert.DoesNotContain(report.SanitizedErrorCategories.Keys, x => x.Contains("raw", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Successful_preflight_returns_only_metadata_and_aggregate_counts()
    {
        var workbookStore = new FakeWorkbookStore();
        var verifier = Create(new FakeReadStore(Snapshot(TenantId)), new TestContext(TenantId, UserId), new FakeAuthorization(true), workbookStore,
            new FakeValidation(Valid(1)));

        var report = await verifier.VerifyAsync(EmployeeBulkImportPreflightVerifier.TargetOperationId);

        Assert.True(report.Passed);
        Assert.Equal(EmployeeBulkImportPreflightVerifier.TargetOperationId, report.OperationId);
        Assert.Equal("WEB1", report.TenantCode);
        Assert.Equal("Queued", report.State);
        Assert.NotNull(report.ConfirmedAtUtc);
        Assert.Equal(1, report.PendingRows);
        Assert.Equal(0, report.SucceededRows);
        Assert.Equal(0, report.FailedRows);
        Assert.Equal(0, report.SkippedRows);
        Assert.False(report.ActiveLease);
        Assert.Equal(1, report.ValidatedRows);
        Assert.Equal(0, report.InvalidRows);
        Assert.Equal(1, workbookStore.OpenCalls);
        Assert.All(report.Checks, check => Assert.True(check.Passed, check.Name + ": " + check.Category));
    }

    private static EmployeeBulkImportPreflightVerifier Create(
        FakeReadStore readStore,
        ITenantContext tenant,
        ICurrentAuthorizationContext authorization,
        FakeWorkbookStore workbookStore,
        FakeValidation validation) => new(readStore, tenant, authorization, workbookStore, validation,
            Options.Create(new EmployeeBulkImportWorkerOptions()), new FixedClock(Now), expectedRows: 1);

    private static EmployeeBulkUploadValidationResult Valid(int rows) => new(
        new EmployeeBulkUploadPreviewDto(rows, rows, 0, 0, []),
        Enumerable.Range(2, rows).Select(row => new ValidatedEmployeeOnboardingRow(row, new EmployeeOnboardingRequest())).ToArray());

    private static EmployeeImportPreflightOperationSnapshot Snapshot(Guid tenantId) => new(
        tenantId, EmployeeBulkImportPreflightVerifier.TargetOperationId,
        new string('a', 64), EmployeeImportOperationState.Queued, Now.AddMinutes(-4), 1, 0, 0, 0, null, null);

    private sealed class TestContext(Guid? tenantId, Guid? userId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
        public bool HasTenant => TenantId.HasValue;
    }

    private sealed class FakeAuthorization(bool granted) : ICurrentAuthorizationContext
    {
        public bool HasAnyPermission(params string[] permissions) => granted && permissions.Contains(Permissions.Employee.Import);
    }

    private sealed class FakeReadStore(EmployeeImportPreflightOperationSnapshot? operation) : IEmployeeImportPreflightReadStore
    {
        public int FindCalls { get; private set; }
        public Task<EmployeeImportPreflightOperationSnapshot?> FindOperationAsync(Guid tenantId, Guid operationId, CancellationToken cancellationToken = default)
        {
            FindCalls++;
            return Task.FromResult(operation?.TenantId == tenantId && operation.OperationId == operationId ? operation : null);
        }
        public Task<EmployeeImportPreflightRowCounts> CountRowsAsync(Guid tenantId, Guid operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(operation?.TenantId == tenantId && operation.OperationId == operationId
                ? new EmployeeImportPreflightRowCounts(1, 0, 0, 0, 0)
                : new EmployeeImportPreflightRowCounts(0, 0, 0, 0, 0));
    }

    private sealed class FakeWorkbookStore : IEmployeeImportWorkbookStore
    {
        public int OpenCalls { get; private set; }
        public Exception? Failure { get; init; }
        public Task SaveAsync(Guid operationId, Stream workbook, string expectedSha256, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsAvailableAsync(Guid operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteExpiredAsync(Guid operationId, DateTime completedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(Guid operationId, string expectedSha256, CancellationToken cancellationToken = default)
        {
            OpenCalls++;
            if (Failure is not null) throw Failure;
            return Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]));
        }
    }

    private sealed class FakeValidation(HRMS.Application.Common.Result<EmployeeBulkUploadValidationResult> result) : IEmployeeBulkUploadValidationService
    {
        public FakeValidation(EmployeeBulkUploadValidationResult result)
            : this(HRMS.Application.Common.Result<EmployeeBulkUploadValidationResult>.Success(result)) { }

        public Task<HRMS.Application.Common.Result<EmployeeBulkUploadPreviewDto>> ValidateAsync(Stream workbook, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HRMS.Application.Common.Result<EmployeeBulkUploadValidationResult>> ValidateForExecutionAsync(Stream workbook, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
