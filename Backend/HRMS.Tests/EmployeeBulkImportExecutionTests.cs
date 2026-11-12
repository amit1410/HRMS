using Microsoft.Extensions.Logging;
using ClosedXML.Excel;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.EmployeeCodes;
using HRMS.Application.Services;
using HRMS.Application.Validators.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Storage;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;

namespace HRMS.Tests;

public sealed class EmployeeBulkImportExecutionTests
{
    private static readonly Guid TenantId = SeedData.TenantIds.Demo01;

    [Fact]
    public async Task Preparation_is_read_only_and_confirmation_imports_multiple_rows_with_audit_and_codes()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync();
        var workbook = await f.CreateWorkbookAsync(2);
        var before = await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        var beforeContacts = await f.Db.EmployeeContacts.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        var beforeHistory = await f.Db.EmployeeEmploymentHistory.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId && !x.IsSuperseded);
        var beforeSequences = await f.Db.EmployeeCodeSequences.IgnoreQueryFilters().CountAsync();

        var prepared = await f.PrepareAsync(workbook);
        Assert.True(prepared.Succeeded, prepared.Message);
        Assert.Equal(before, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
        Assert.Equal(beforeSequences, await f.Db.EmployeeCodeSequences.IgnoreQueryFilters().CountAsync());
        Assert.NotEmpty(prepared.Value!.ConfirmationToken);
        Assert.DoesNotContain("999988887777", System.Text.Json.JsonSerializer.Serialize(prepared.Value));
        var encryptedArtifact = Assert.Single(Directory.GetFiles(f.StorageRoot, "*.hrw", SearchOption.AllDirectories));
        var encryptedBytes = await File.ReadAllBytesAsync(encryptedArtifact);
        Assert.DoesNotContain(System.Text.Encoding.UTF8.GetBytes("999988887777"), encryptedBytes);

        var imported = await f.ConfirmAsync(prepared.Value.OperationId, prepared.Value.ConfirmationToken, workbook);
        Assert.True(imported.Succeeded, imported.Message);
        Assert.Equal((2, 2, 0, 0), (imported.Value!.TotalRows, imported.Value.SucceededRows, imported.Value.FailedRows, imported.Value.SkippedRows));
        Assert.Equal("Completed", imported.Value.State);
        Assert.Equal(before + 2, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
        Assert.Equal(beforeContacts + 2, await f.Db.EmployeeContacts.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
        Assert.Equal(beforeHistory + 2, await f.Db.EmployeeEmploymentHistory.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId && !x.IsSuperseded));
        Assert.All(imported.Value.Rows, row => Assert.Equal("Succeeded", row.Status));
        Assert.Equal(2, imported.Value.Rows.Select(x => x.EmployeeCode).Distinct().Count());
        var publicResult = System.Text.Json.JsonSerializer.Serialize(imported.Value);
        Assert.DoesNotContain("999988880000", publicResult);
        Assert.DoesNotContain("ABCDE0000F", publicResult);
        Assert.DoesNotContain("Asha0", publicResult);
        var persistedOperation = System.Text.Json.JsonSerializer.Serialize(
            await f.Db.EmployeeImportOperations.IgnoreQueryFilters().Where(x => x.Id == prepared.Value.OperationId)
                .Select(x => new { x.WorkbookSha256, x.ConfirmationTokenHash, x.SanitizedSummary }).SingleAsync());
        Assert.DoesNotContain("999988880000", persistedOperation);
        Assert.DoesNotContain("ABCDE0000F", persistedOperation);
        var employeeCodes = imported.Value.Rows.Select(x => x.EmployeeCode).ToArray();
        var employeeIds = await f.Db.Employees.IgnoreQueryFilters().Where(x => x.TenantId == TenantId && employeeCodes.Contains(x.EmployeeCode)).Select(x => x.Id).ToListAsync();
        var audits = await f.Db.EmployeeAuditLogs.IgnoreQueryFilters().Where(x => employeeIds.Contains(x.EmployeeId)).ToListAsync();
        Assert.True(audits.Count >= 2);
        Assert.All(audits, audit => Assert.Equal(prepared.Value.OperationId, audit.ImportBatchId));
    }

    [Theory]
    [InlineData(2018, 1, 20)]
    [InlineData(2027, 1, 20)]
    public async Task Bulk_import_preserves_historical_and_future_initial_employment_dates(int year, int month, int day)
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureManualHistoricalCodeAsync();
        var effectiveDate = new DateOnly(year, month, day);
        var workbook = await f.CreateWorkbookAsync(1, effectiveDate, "EMP000001");

        var prepared = await f.PrepareAsync(workbook);
        Assert.True(prepared.Succeeded, prepared.Message);
        var imported = await f.ConfirmAsync(prepared.Value!.OperationId, prepared.Value.ConfirmationToken, workbook);

        Assert.True(imported.Succeeded, imported.Message);
        Assert.Equal((1, 0, 0), (imported.Value!.SucceededRows, imported.Value.FailedRows, imported.Value.SkippedRows));
        Assert.Equal("EMP000001", imported.Value.Rows.Single().EmployeeCode);
        var employee = await f.Db.Employees.IgnoreQueryFilters().SingleAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0");
        var history = await f.Db.EmployeeEmploymentHistory.IgnoreQueryFilters().SingleAsync(x => x.TenantId == TenantId && x.EmployeeId == employee.Id);
        Assert.Equal(effectiveDate, employee.DateOfJoining);
        Assert.Equal(effectiveDate, history.EffectiveFrom);
    }

    [Fact]
    public async Task Confirmation_is_durable_and_returns_before_employee_creation()
    {
        using var f = await Fixture.CreateAsync(); await f.ConfigureCodeAsync();
        var prepared = await f.PrepareAsync(await f.CreateWorkbookAsync(1)); Assert.True(prepared.Succeeded, prepared.Message);
        var before = await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        var accepted = await f.Execution.ConfirmAsync(prepared.Value!.OperationId, prepared.Value.ConfirmationToken);
        Assert.True(accepted.Succeeded, accepted.Message);
        Assert.Equal("Queued", accepted.Value!.State);
        Assert.Equal($"/api/employees/bulk-upload/{prepared.Value.OperationId:D}", accepted.Value.StatusUrl);
        Assert.Equal(before, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
        Assert.Equal(EmployeeImportOperationState.Queued,
            (await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync(x => x.Id == prepared.Value.OperationId)).State);
        f.Db.ClearChangeTracker(); // Accepted work is discovered from durable state in a fresh processing scope.
        Assert.True(await f.Execution.ProcessQueuedOperationAsync(prepared.Value.OperationId));
        var status = await f.Execution.GetStatusAsync(prepared.Value.OperationId);
        Assert.True(status.Succeeded);
        Assert.Equal((1, 1, "Completed"), (status.Value!.TotalRows, status.Value.SucceededRows, status.Value.State));
    }

    [Fact]
    public async Task Targeted_processing_executes_only_the_requested_operation_in_the_current_tenant()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync();
        var beforeEmployees = await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        var target = await f.PrepareAsync(await f.CreateWorkbookAsync(1));
        var sibling = await f.PrepareAsync(await f.CreateWorkbookAsync(2));
        Assert.True(target.Succeeded, target.Message);
        Assert.True(sibling.Succeeded, sibling.Message);
        Assert.True((await f.Execution.ConfirmAsync(target.Value!.OperationId, target.Value.ConfirmationToken)).Succeeded);
        Assert.True((await f.Execution.ConfirmAsync(sibling.Value!.OperationId, sibling.Value.ConfirmationToken)).Succeeded);

        Assert.True(await f.Execution.ProcessQueuedOperationAsync(target.Value.OperationId));

        var completed = await f.Execution.GetStatusAsync(target.Value.OperationId);
        var untouched = await f.Execution.GetStatusAsync(sibling.Value.OperationId);
        Assert.Equal("Completed", completed.Value!.State);
        Assert.Equal(1, completed.Value.SucceededRows);
        Assert.Equal("Queued", untouched.Value!.State);
        Assert.Equal(0, untouched.Value.SucceededRows);
        Assert.All(untouched.Value.Rows, row => Assert.Equal("Pending", row.Status));
        Assert.Equal(beforeEmployees + 1,
            await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
    }

    [Fact]
    public async Task Worker_restart_after_row_commit_skips_successful_row_instead_of_recreating_employee()
    {
        using var f = await Fixture.CreateAsync(new FailCompletedTransitionOnceInterceptor()); await f.ConfigureCodeAsync();
        var prepared = await f.PrepareAsync(await f.CreateWorkbookAsync(1)); Assert.True(prepared.Succeeded, prepared.Message);
        var accepted = await f.Execution.ConfirmAsync(prepared.Value!.OperationId, prepared.Value.ConfirmationToken);
        Assert.True(accepted.Succeeded, accepted.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Execution.ProcessQueuedOperationAsync(prepared.Value.OperationId));
        Assert.Equal(1, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));
        Assert.Equal(EmployeeImportRowStatus.Succeeded,
            (await f.Db.EmployeeImportRows.IgnoreQueryFilters().SingleAsync(x => x.OperationId == prepared.Value.OperationId)).Status);

        f.Clock.Advance(TimeSpan.FromMinutes(11));
        f.Db.ClearChangeTracker();
        Assert.True(await f.Execution.ProcessQueuedOperationAsync(prepared.Value.OperationId));
        var final = await f.Execution.GetStatusAsync(prepared.Value.OperationId);
        Assert.Equal("Completed", final.Value!.State);
        Assert.Equal(1, final.Value.SucceededRows);
        Assert.Equal(1, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));
    }

    [Fact]
    public async Task Repeated_preparation_of_same_workbook_resumes_same_tenant_operation_without_creating_employees()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync();
        var workbook = await f.CreateWorkbookAsync(1);
        var first = await f.PrepareAsync(workbook); Assert.True(first.Succeeded, first.Message);
        var second = await f.PrepareAsync(workbook); Assert.True(second.Succeeded, second.Message);
        Assert.Equal(first.Value!.OperationId, second.Value!.OperationId);
        Assert.NotEqual(first.Value.ConfirmationToken, second.Value.ConfirmationToken);
        Assert.Single(await f.Db.EmployeeImportOperations.IgnoreQueryFilters().ToListAsync());
        Assert.False(await f.Db.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));

        f.Tenant.UserId = SeedData.Users.First(x => x.TenantId == TenantId && x.Id != SeedData.Users.First(y => y.TenantId == TenantId).Id).Id;
        var takeover = await f.PrepareAsync(workbook);
        Assert.Equal(ResultStatus.Conflict, takeover.Status);
        Assert.Equal(EmployeeImportOperationState.Prepared,
            (await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync()).State);
    }

    [Fact]
    public async Task Preparing_a_queued_workbook_returns_its_existing_operation_id_without_creating_a_duplicate()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync();
        var workbook = await f.CreateWorkbookAsync(1);
        var prepared = await f.PrepareAsync(workbook);
        Assert.True(prepared.Succeeded, prepared.Message);
        var accepted = await f.Execution.ConfirmAsync(prepared.Value!.OperationId, prepared.Value.ConfirmationToken);
        Assert.True(accepted.Succeeded, accepted.Message);

        var retry = await f.PrepareAsync(workbook);

        Assert.Equal(ResultStatus.Conflict, retry.Status);
        Assert.Equal("This workbook already has an active or accepted import operation.", retry.Message);
        Assert.Contains(retry.Errors!, error => error.Field == "existingOperationId" &&
            error.Message == prepared.Value.OperationId.ToString("D"));
        Assert.Single(await f.Db.EmployeeImportOperations.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(EmployeeImportOperationState.Queued,
            (await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync()).State);
        Assert.False(await f.Db.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));
    }

    [Fact]
    public async Task Storage_upload_failure_leaves_recoverable_reservation_and_creates_no_employee()
    {
        using var f = await Fixture.CreateWithStoreAsync(new FailingUploadStore());
        await f.ConfigureCodeAsync();
        var result = await f.PrepareAsync(await f.CreateWorkbookAsync(1));
        Assert.Equal(ResultStatus.Conflict, result.Status);
        var operation = await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(EmployeeImportOperationState.ArtifactPending, operation.State);
        Assert.Null(operation.ClaimLeaseId);
        Assert.Equal(1, await f.Db.EmployeeImportOperations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await f.Db.EmployeeImportRows.IgnoreQueryFilters().CountAsync(x => x.OperationId == operation.Id));
        var storageFailure = Assert.Single(f.PreparationLogger.Entries);
        Assert.Equal(LogLevel.Error, storageFailure.Level);
        Assert.Contains("artifact-write", storageFailure.Message);
        Assert.IsType<IOException>(storageFailure.Exception);
        Assert.False(await f.Db.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));
    }

    [Fact]
    public async Task Retry_after_transient_storage_failure_resumes_same_operation_for_same_workbook()
    {
        var store = new RecoveringUploadStore();
        using var f = await Fixture.CreateWithStoreAsync(store);
        await f.ConfigureCodeAsync();
        var workbook = await f.CreateWorkbookAsync(1);

        var first = await f.PrepareAsync(workbook);
        Assert.Equal(ResultStatus.Conflict, first.Status);
        var reserved = await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(EmployeeImportOperationState.ArtifactPending, reserved.State);
        Assert.Null(reserved.ClaimLeaseId);

        store.Recover();
        var retry = await f.PrepareAsync(workbook);
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(reserved.Id, retry.Value!.OperationId);
        Assert.Equal(EmployeeImportOperationState.Prepared,
            (await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync()).State);
        Assert.Single(await f.Db.EmployeeImportRows.IgnoreQueryFilters().Where(x => x.OperationId == reserved.Id).ToListAsync());
        Assert.False(await f.Db.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));
    }

    [Fact]
    public async Task Retry_with_an_active_artifact_pending_lease_returns_early_conflict_without_storage_error_log()
    {
        using var f = await Fixture.CreateWithStoreAsync(new FailingUploadStore());
        await f.ConfigureCodeAsync();
        var workbook = await f.CreateWorkbookAsync(1);

        var first = await f.PrepareAsync(workbook);
        Assert.Equal(ResultStatus.Conflict, first.Status);
        var operation = await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(EmployeeImportOperationState.ArtifactPending, operation.State);

        // Represent another request currently owning this pending operation. This exercises
        // ResumePreparationAsync's early conflict, before the artifact phase logging try/catch.
        Assert.True(operation.AcquireArtifactPreparationLease(Guid.NewGuid(), new string('A', 64),
            DateTime.UtcNow, TimeSpan.FromMinutes(10)));
        await f.Db.SaveChangesAsync();
        f.PreparationLogger.Entries.Clear();

        var retry = await f.PrepareAsync(workbook);

        Assert.Equal(ResultStatus.Conflict, retry.Status);
        Assert.Contains("active or accepted", retry.Message);
        Assert.Empty(f.PreparationLogger.Entries);
    }

    [Fact]
    public async Task Retry_recovers_verified_artifact_after_prepared_state_save_failure()
    {
        using var f = await Fixture.CreateAsync(new FailPreparedTransitionOnceInterceptor());
        await f.ConfigureCodeAsync();
        var workbook = await f.CreateWorkbookAsync(1);
        var first = await f.PrepareAsync(workbook);
        Assert.False(first.Succeeded);
        f.Db.ClearChangeTracker(); // Simulate the failed request scope ending before recovery.
        var operation = await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(EmployeeImportOperationState.ArtifactPending, operation.State);
        Assert.NotNull(operation.ClaimLeaseId);
        Assert.True(File.Exists(Path.Combine(f.StorageRoot, TenantId.ToString("N"), operation.Id.ToString("N") + ".hrw")));
        Assert.False(await f.Db.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));

        f.Clock.Advance(TimeSpan.FromMinutes(11));
        var recovered = await f.PrepareAsync(workbook);
        Assert.True(recovered.Succeeded, recovered.Message);
        Assert.Equal(operation.Id, recovered.Value!.OperationId);
        Assert.Equal(EmployeeImportOperationState.Prepared,
            (await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync()).State);
        Assert.False(await f.Db.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));
    }

    [Fact]
    public async Task Invalid_workbook_is_rejected_without_preparing_or_creating_employees()
    {
        using var f = await Fixture.CreateAsync();
        var bytes = await f.CreateWorkbookAsync(1);
        var beforeEmployees = await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheet("Employees").Cell(2, 2).Value = string.Empty;
        var invalid = Save(workbook);
        var result = await f.PrepareAsync(invalid);
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Empty(await f.Db.EmployeeImportOperations.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(beforeEmployees, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
    }

    [Fact]
    public async Task Confirmation_is_one_use_and_replay_does_not_duplicate_creation()
    {
        using var f = await Fixture.CreateAsync(); await f.ConfigureCodeAsync();
        var workbook = await f.CreateWorkbookAsync(1);
        var baseline = await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        var prepared = await f.PrepareAsync(workbook); Assert.True(prepared.Succeeded, prepared.Message);
        var first = await f.ConfirmAsync(prepared.Value!.OperationId, prepared.Value.ConfirmationToken, workbook);
        Assert.True(first.Succeeded, first.Message);
        var replay = await f.ConfirmAsync(prepared.Value.OperationId, prepared.Value.ConfirmationToken, workbook);
        Assert.Equal(ResultStatus.Conflict, replay.Status);
        Assert.Equal(baseline + 1, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
    }

    [Fact]
    public async Task Confirmation_rejects_corrupted_stored_workbook()
    {
        using var f = await Fixture.CreateAsync(); await f.ConfigureCodeAsync();
        var prepared = await f.PrepareAsync(await f.CreateWorkbookAsync(1)); Assert.True(prepared.Succeeded, prepared.Message);
        Assert.Equal(ResultStatus.ValidationFailed,
            (await f.Execution.ConfirmAsync(prepared.Value!.OperationId, string.Empty)).Status);
        await f.TamperArtifactAsync(prepared.Value!.OperationId);
        var result = await f.Execution.ConfirmAsync(prepared.Value.OperationId, prepared.Value.ConfirmationToken);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.False(await f.Db.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));
    }

    [Fact]
    public async Task Stored_artifact_hash_mismatch_is_rejected_without_overwriting_the_artifact()
    {
        using var f = await Fixture.CreateAsync(); await f.ConfigureCodeAsync();
        var prepared = await f.PrepareAsync(await f.CreateWorkbookAsync(1)); Assert.True(prepared.Succeeded, prepared.Message);
        var path = Path.Combine(f.StorageRoot, TenantId.ToString("N"), prepared.Value!.OperationId.ToString("N") + ".hrw");
        var originalArtifact = await File.ReadAllBytesAsync(path);
        var operation = await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync(x => x.Id == prepared.Value.OperationId);
        operation.WorkbookSha256 = new string('f', 64);
        await f.Db.SaveChangesAsync();

        var result = await f.Execution.ConfirmAsync(prepared.Value.OperationId, prepared.Value.ConfirmationToken);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(originalArtifact, await File.ReadAllBytesAsync(path));
        Assert.False(await f.Db.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));
    }

    [Fact]
    public async Task Master_deactivation_between_prepare_and_confirm_prevents_employee_creation()
    {
        using var f = await Fixture.CreateAsync(); await f.ConfigureCodeAsync();
        var bytes = await f.CreateWorkbookAsync(1);
        var baseline = await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        var prepared = await f.PrepareAsync(bytes); Assert.True(prepared.Succeeded, prepared.Message);
        var department = await f.Db.Departments.IgnoreQueryFilters().SingleAsync(x => x.TenantId == TenantId && x.Code == "ENG");
        department.IsActive = false; await f.Db.SaveChangesAsync();
        var result = await f.ConfirmAsync(prepared.Value!.OperationId, prepared.Value.ConfirmationToken, bytes);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Failed", result.Value!.State);
        Assert.Equal(baseline, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
        Assert.Equal(EmployeeImportOperationState.Failed,
            (await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync(x => x.Id == prepared.Value.OperationId)).State);
    }

    [Fact]
    public async Task Runtime_employee_persistence_failure_rolls_back_row_and_employee()
    {
        using var f = await Fixture.CreateAsync(new FailEmployeeInterceptor()); await f.ConfigureCodeAsync();
        var bytes = await f.CreateWorkbookAsync(1);
        var baseline = await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        var prepared = await f.PrepareAsync(bytes); Assert.True(prepared.Succeeded, prepared.Message);
        var result = await f.ConfirmAsync(prepared.Value!.OperationId, prepared.Value.ConfirmationToken, bytes);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal((0, 1), (result.Value!.SucceededRows, result.Value.FailedRows));
        Assert.Equal(baseline, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
        Assert.Equal(EmployeeImportRowStatus.Failed,
            (await f.Db.EmployeeImportRows.IgnoreQueryFilters().SingleAsync(x => x.OperationId == prepared.Value.OperationId)).Status);
        var diagnostic = Assert.Single(f.PreparationLogger.Entries, x => x.Message.Contains("Employee import row onboarding failed."));
        Assert.Equal(LogLevel.Error, diagnostic.Level);
        Assert.Contains(prepared.Value.OperationId.ToString(), diagnostic.Message);
        Assert.Contains("Excel row 2", diagnostic.Message);
        Assert.Contains("stage employee_onboarding", diagnostic.Message);
        Assert.Contains("exception type InvalidOperationException", diagnostic.Message);
        Assert.Contains("error category invalid_operation", diagnostic.Message);
        Assert.DoesNotContain("sensitive-marker", diagnostic.Message);
        Assert.Null(diagnostic.Exception);
    }

    [Fact]
    public async Task Client_cancellation_rolls_back_inflight_row_and_leaves_it_for_lease_recovery()
    {
        using var f = await Fixture.CreateAsync(new CancelEmployeeInterceptor()); await f.ConfigureCodeAsync();
        var bytes = await f.CreateWorkbookAsync(1);
        var baseline = await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        var prepared = await f.PrepareAsync(bytes); Assert.True(prepared.Succeeded, prepared.Message);

        var accepted = await f.Execution.ConfirmAsync(prepared.Value!.OperationId, prepared.Value.ConfirmationToken);
        Assert.True(accepted.Succeeded, accepted.Message);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Execution.ProcessQueuedOperationAsync(prepared.Value.OperationId));

        Assert.Equal(baseline, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
        var row = await f.Db.EmployeeImportRows.IgnoreQueryFilters().SingleAsync(x => x.OperationId == prepared.Value.OperationId);
        Assert.Equal(EmployeeImportRowStatus.Processing, row.Status);
        Assert.Null(row.EmployeeId);
        var operation = await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync(x => x.Id == prepared.Value.OperationId);
        Assert.Equal(EmployeeImportOperationState.Processing, operation.State);
        Assert.NotNull(operation.ClaimLeaseExpiresAtUtc);
    }

    [Fact]
    public async Task Row_two_failure_rolls_back_that_employee_and_later_valid_rows_continue_once()
    {
        using var f = await Fixture.CreateAsync(new FailEmployeeNameInterceptor("Asha0")); await f.ConfigureCodeAsync();
        var bytes = await f.CreateWorkbookAsync(4);
        var employeeBaseline = await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        var historyBaseline = await f.Db.EmployeeEmploymentHistory.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId);
        var prepared = await f.PrepareAsync(bytes); Assert.True(prepared.Succeeded, prepared.Message);
        var result = await f.ConfirmAsync(prepared.Value!.OperationId, prepared.Value.ConfirmationToken, bytes);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal((3, 1, 0), (result.Value!.SucceededRows, result.Value.FailedRows, result.Value.SkippedRows));
        Assert.Equal("PartiallyCompleted", result.Value.State);
        Assert.Equal(new[] { "Failed", "Succeeded", "Succeeded", "Succeeded" }, result.Value.Rows.Select(x => x.Status));
        Assert.Equal(employeeBaseline + 3, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
        Assert.Equal(historyBaseline + 3, await f.Db.EmployeeEmploymentHistory.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
        Assert.False(await f.Db.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId && x.FirstName == "Asha0"));
        Assert.All(new[] { "Asha1", "Asha2", "Asha3" }, name =>
            Assert.True(f.Db.Employees.IgnoreQueryFilters().Any(x => x.TenantId == TenantId && x.FirstName == name)));
        Assert.Equal(3, result.Value.Rows.Where(x => x.Status == "Succeeded").Select(x => x.EmployeeCode).Distinct().Count());
        Assert.Equal(3, result.Value.Rows.Where(x => x.Status == "Succeeded").Select(x => x.EmployeeCode).Count());

        var diagnostic = Assert.Single(f.PreparationLogger.Entries, x => x.Message.Contains("Employee import row onboarding failed."));
        Assert.Contains("Excel row 2", diagnostic.Message);
        Assert.Contains("failure status Exception", diagnostic.Message);
        Assert.Contains("failure fields none", diagnostic.Message);
        Assert.DoesNotContain("Asha0", diagnostic.Message);
        Assert.DoesNotContain("sensitive-marker", diagnostic.Message);

        f.Db.ClearChangeTracker();
        Assert.False(await f.Execution.ProcessQueuedOperationAsync(prepared.Value.OperationId));
        Assert.Equal(employeeBaseline + 3, await f.Db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == TenantId));
    }

    [Fact]
    public async Task Cross_tenant_operation_access_is_not_found_and_required_permissions_are_declared()
    {
        using var f = await Fixture.CreateAsync(); await f.ConfigureCodeAsync();
        var prepared = await f.PrepareAsync(await f.CreateWorkbookAsync(1)); Assert.True(prepared.Succeeded, prepared.Message);
        f.Tenant.TenantId = SeedData.TenantIds.Demo02;
        var result = await f.Execution.ConfirmAsync(prepared.Value!.OperationId, prepared.Value.ConfirmationToken);
        Assert.Equal(ResultStatus.NotFound, result.Status);
        var operation = await f.Db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync(x => x.Id == prepared.Value.OperationId);
        var recovery = await new EmployeeImportPersistenceService(f.Db, f.Tenant, TimeProvider.System)
            .ResumePreparationAsync(operation.WorkbookSha256, new string('b', 64), Guid.NewGuid());
        Assert.Equal(ResultStatus.NotFound, recovery.Status);
        foreach (var actionName in new[] { "Prepare", "Confirm" })
        {
            var action = typeof(HRMS.API.Controllers.EmployeeBulkImportExecutionController).GetMethod(actionName)!;
            var permissions = action.GetCustomAttributes(typeof(HRMS.API.Security.HasPermissionAttribute), true)
                .Cast<HRMS.API.Security.HasPermissionAttribute>().Select(x => x.Permission).ToArray();
            Assert.Contains(Permissions.Employee.Import, permissions);
            Assert.Contains(Permissions.Employee.Create, permissions);
            Assert.Contains(Permissions.EmployeeSensitive.Edit, permissions);
        }

        foreach (var actionName in new[] { "Status", "History" })
        {
            var action = typeof(HRMS.API.Controllers.EmployeeBulkImportExecutionController).GetMethod(actionName)!;
            var permissions = action.GetCustomAttributes(typeof(HRMS.API.Security.HasPermissionAttribute), true)
                .Cast<HRMS.API.Security.HasPermissionAttribute>().Select(x => x.Permission).ToArray();
            Assert.Contains(Permissions.Employee.Import, permissions);
        }
    }

    [Fact]
    public async Task Expired_claim_recovers_processing_row_without_recreating_employee()
    {
        using var database = new SqliteInMemoryDatabase(); await database.SeedAsync();
        var tenant = new TestTenantContext(TenantId, SeedData.Users.First(x => x.TenantId == TenantId).Id);
        using var db = database.CreateContext(tenant);
        var clock = new AdjustableClock(DateTimeOffset.Parse("2026-10-28T00:00:00Z"));
        var persistence = new EmployeeImportPersistenceService(db, tenant, clock);
        var preparationLease = Guid.NewGuid();
        var prepared = await persistence.BeginPreparationAsync(new string('a', 64), new string('b', 64), 1, preparationLease);
        Assert.True(prepared.Succeeded, prepared.Message);
        Assert.True((await persistence.CompletePreparationAsync(prepared.Value!.Id, preparationLease)).Succeeded);
        var oldLease = Guid.NewGuid();
        Assert.True((await persistence.ClaimAsync(prepared.Value!.Id, new string('b', 64), oldLease)).Succeeded);
        Assert.True((await persistence.TransitionAsync(prepared.Value.Id, EmployeeImportOperationState.Processing)).Succeeded);
        Assert.True((await persistence.StartRowAsync(prepared.Value.Id, 2)).Succeeded);

        clock.Advance(TimeSpan.FromMinutes(11));
        var newLease = Guid.NewGuid();
        Assert.True((await persistence.ClaimAsync(prepared.Value.Id, new string('b', 64), newLease)).Succeeded);
        var recovered = await persistence.RecoverAbandonedRowAsync(prepared.Value.Id, 2);
        Assert.True(recovered.Succeeded, recovered.Message);
        Assert.Equal(EmployeeImportRowStatus.Pending, recovered.Value!.Status);
        Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId && x.Id == recovered.Value.EmployeeId));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TestAuthorization authorization;
        private readonly EmployeeCodeConfigurationService codeConfiguration;

        private Fixture(SqliteInMemoryDatabase database, HrmsDbContext db, TestTenantContext tenant, TestAuthorization authorization,
            IEmployeeImportWorkbookStore? storeOverride = null)
        {
            Database = database; Db = db; Tenant = tenant; this.authorization = authorization;
            var clock = new AdjustableClock(new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero));
            Clock = clock;
            var sequence = new EmployeeCodeSequenceService(db, tenant);
            codeConfiguration = new EmployeeCodeConfigurationService(db, tenant, new EmployeeCodeConfigurationRequestValidator());
            var jobStatus = new JobStatusService(db, tenant, authorization, clock);
            var employment = new EmployeeEmploymentService(db, tenant, authorization, NullLogger<EmployeeEmploymentService>.Instance,
                new EmployeeCodeRuleMatcher(), new EmployeeCodeRenderer(), sequence, clock, jobStatusService: jobStatus);
            var employeeService = new EmployeeService(db, tenant, clock, NullLogger<EmployeeService>.Instance,
                jobStatusService: jobStatus, employmentService: employment, authorization: authorization);
            var validation = new EmployeeBulkUploadValidationService(db, tenant, new EmployeePersonalDetailsRequestValidator(),
                new EmployeeContactRequestValidator(), new EmploymentChangeRequestValidator());
            var persistence = new EmployeeImportPersistenceService(db, tenant, clock);
            var storageRoot = Path.Combine(Path.GetTempPath(), "hrms-import-execution-tests", Guid.NewGuid().ToString("N"));
            StorageRoot = storageRoot;
            var settings = new Dictionary<string, string?>
            {
                ["EmployeeImportStorage:RootPath"] = storageRoot,
                ["EmployeeImportStorage:RetentionDays"] = "30",
                ["EmployeeImportStorage:Encryption:ActiveKeyId"] = "test-v1",
                ["EmployeeImportStorage:Encryption:Keys:test-v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var store = storeOverride ?? new EncryptedFileEmployeeImportWorkbookStore(configuration,
                new TestHostEnvironment(Path.GetTempPath()), tenant, clock, new EmployeeImportWorkbookCipher(configuration));
            PreparationLogger = new RecordingPreparationLogger();
            Execution = new EmployeeBulkImportExecutionService(db, tenant, validation, persistence, store, employeeService,
                new EmployeeImportWorkerExecutionContext(), PreparationLogger);
        }

        public SqliteInMemoryDatabase Database { get; }
        public HrmsDbContext Db { get; }
        public TestTenantContext Tenant { get; }
        public IEmployeeBulkImportExecutionService Execution { get; }
        public string StorageRoot { get; }
        public AdjustableClock Clock { get; }
        public RecordingPreparationLogger PreparationLogger { get; }

        public async Task TamperArtifactAsync(Guid operationId)
        {
            var path = Path.Combine(StorageRoot, TenantId.ToString("N"), operationId.ToString("N") + ".hrw");
            var encrypted = await File.ReadAllBytesAsync(path); encrypted[^1] ^= 1; await File.WriteAllBytesAsync(path, encrypted);
        }

        public static async Task<Fixture> CreateAsync(params IInterceptor[] interceptors)
        {
            var database = new SqliteInMemoryDatabase(); await database.SeedAsync();
            var tenant = new TestTenantContext(TenantId, SeedData.Users.First(x => x.TenantId == TenantId).Id);
            var authorization = new TestAuthorization(Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit, Permissions.EmploymentHistory.Change);
            return new Fixture(database, database.CreateContext(tenant, interceptors), tenant, authorization);
        }

        public static async Task<Fixture> CreateWithStoreAsync(IEmployeeImportWorkbookStore store)
        {
            var database = new SqliteInMemoryDatabase(); await database.SeedAsync();
            var tenant = new TestTenantContext(TenantId, SeedData.Users.First(x => x.TenantId == TenantId).Id);
            var authorization = new TestAuthorization(Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit, Permissions.EmploymentHistory.Change);
            return new Fixture(database, database.CreateContext(tenant), tenant, authorization, store);
        }

        public async Task ConfigureCodeAsync()
        {
            var result = await codeConfiguration.SaveAsync(new EmployeeCodeConfigurationRequest
            {
                AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto, GenerationMethod = EmployeeCodeGenerationMethod.Simple,
                Prefix = "EMP", Separator = "-", NextNumber = 1, Padding = 5, EffectiveFrom = new DateOnly(2026, 3, 4)
            });
            Assert.True(result.Succeeded, result.Message);
        }

        public async Task ConfigureManualHistoricalCodeAsync()
        {
            var result = await codeConfiguration.SaveAsync(new EmployeeCodeConfigurationRequest
            {
                AutoGenerate = false, AssignmentMode = EmployeeCodeAssignmentMode.Manual, GenerationMethod = null,
                Prefix = "EMP", Separator = "-", NextNumber = 1, Padding = 6,
                EffectiveFrom = new DateOnly(2017, 1, 2), EffectiveTo = new DateOnly(2031, 9, 30)
            });
            Assert.True(result.Succeeded, result.Message);
        }

        public async Task<byte[]> CreateWorkbookAsync(int rows, DateOnly? effectiveDate = null, string? manualEmployeeCode = null)
        {
            var template = await new EmployeeBulkUploadTemplateService(Db, Tenant).GenerateAsync();
            Assert.True(template.Succeeded, template.Message);
            using var workbook = new XLWorkbook(new MemoryStream(template.Value!));
            var sheet = workbook.Worksheet("Employees");
            var columns = sheet.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
            var designation = await Db.Designations.IgnoreQueryFilters().Where(x => x.TenantId == TenantId && x.IsActive).Select(x => x.Code).FirstAsync();
            var rowDate = (effectiveDate ?? new DateOnly(2026, 3, 4)).ToDateTime(TimeOnly.MinValue);
            for (var index = 0; index < rows; index++)
            {
                var row = index + 2;
                void Set(string header, object value) => sheet.Cell(row, columns[header]).Value = XLCellValue.FromObject(value);
                Set("First Name *", $"Asha{index}"); Set("Last Name *", "Rao"); Set("Aadhaar Number *", $"99998888{index:0000}");
                Set("PAN *", $"ABCDE{index:0000}F"); Set("Date of Joining *", rowDate); Set("Job Status *", "On Probation");
                Set("Employment Effective From *", rowDate); Set("Department Code *", "ENG"); Set("Designation Code *", designation);
                Set("Employment Change Reason *", "NewJoining"); Set("Employment Type *", "FullTime"); Set("Official Email", $"asha{index}@example.test");
                if (!string.IsNullOrWhiteSpace(manualEmployeeCode)) Set("Manual Employee Code", manualEmployeeCode);
            }
            return Save(workbook);
        }

        public async Task<Result<EmployeeBulkImportPreparationDto>> PrepareAsync(byte[] bytes)
        { using var stream = new MemoryStream(bytes); return await Execution.PrepareAsync(stream, "employees.xlsx"); }

        public async Task<Result<EmployeeBulkImportExecutionDto>> ConfirmAsync(Guid operationId, string token, byte[] bytes)
        {
            var accepted = await Execution.ConfirmAsync(operationId, token);
            if (!accepted.Succeeded)
                return Result<EmployeeBulkImportExecutionDto>.Failure(accepted.Status, accepted.Message, accepted.Errors);
            await Execution.ProcessQueuedOperationAsync(operationId);
            var status = await Execution.GetStatusAsync(operationId);
            return status.Succeeded
                ? Result<EmployeeBulkImportExecutionDto>.Success(new(status.Value!.OperationId, status.Value.TotalRows,
                    status.Value.SucceededRows, status.Value.FailedRows, status.Value.SkippedRows, status.Value.State, status.Value.Rows))
                : Result<EmployeeBulkImportExecutionDto>.Failure(status.Status, status.Message, status.Errors);
        }

        public void Dispose() { Db.Dispose(); Database.Dispose(); if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, true); }
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestAuthorization(params string[] permissions) : ICurrentAuthorizationContext
    {
        private readonly HashSet<string> granted = permissions.ToHashSet(StringComparer.Ordinal);
        public bool HasAnyPermission(params string[] requested) => requested.Any(granted.Contains);
    }

    private sealed class FailEmployeeInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries().Any(x => x.State == EntityState.Added && x.Entity is Employee))
                throw new InvalidOperationException("Deliberate employee persistence failure; sensitive-marker=do-not-log.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailEmployeeNameInterceptor(string employeeName) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries().Any(x => x.State == EntityState.Added && x.Entity is Employee { FirstName: var name } && name == employeeName))
                throw new InvalidOperationException("Deliberate row persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class CancelEmployeeInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries().Any(x => x.State == EntityState.Added && x.Entity is Employee))
                throw new OperationCanceledException("Simulated client cancellation.", cancellationToken);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailPreparedTransitionOnceInterceptor : SaveChangesInterceptor
    {
        private bool failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!failed && eventData.Context!.ChangeTracker.Entries().Any(x => x.Entity is EmployeeImportOperation
                    { State: EmployeeImportOperationState.Prepared } && x.State == EntityState.Modified))
            {
                failed = true;
                throw new InvalidOperationException("Simulated database transition interruption.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailCompletedTransitionOnceInterceptor : SaveChangesInterceptor
    {
        private bool failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!failed && eventData.Context!.ChangeTracker.Entries().Any(x => x.Entity is EmployeeImportOperation
                    { State: EmployeeImportOperationState.Completed } && x.State == EntityState.Modified))
            {
                failed = true;
                throw new InvalidOperationException("Simulated process interruption after employee commit.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailingUploadStore : IEmployeeImportWorkbookStore
    {
        public Task SaveAsync(Guid operationId, Stream workbook, string expectedSha256, CancellationToken cancellationToken = default)
            => throw new IOException("Simulated isolated artifact storage failure.");
        public Task<Stream?> OpenReadAsync(Guid operationId, string expectedSha256, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(null);
        public Task<bool> IsAvailableAsync(Guid operationId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<bool> DeleteExpiredAsync(Guid operationId, DateTime completedAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class RecoveringUploadStore : IEmployeeImportWorkbookStore
    {
        private readonly Dictionary<Guid, byte[]> artifacts = [];
        private bool recovered;
        public void Recover() => recovered = true;
        public Task SaveAsync(Guid operationId, Stream workbook, string expectedSha256, CancellationToken cancellationToken = default)
        {
            if (!recovered) throw new IOException("Simulated temporary storage outage.");
            using var content = new MemoryStream();
            workbook.CopyTo(content);
            artifacts.Add(operationId, content.ToArray());
            return Task.CompletedTask;
        }
        public Task<Stream?> OpenReadAsync(Guid operationId, string expectedSha256, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(artifacts.TryGetValue(operationId, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
        public Task<bool> IsAvailableAsync(Guid operationId, CancellationToken cancellationToken = default)
            => Task.FromResult(artifacts.ContainsKey(operationId));
        public Task<bool> DeleteExpiredAsync(Guid operationId, DateTime completedAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class RecordingPreparationLogger : ILogger<EmployeeBulkImportExecutionService>
    {
        public List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, eventId, formatter(state, exception), exception));
    }

    private sealed class AdjustableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan amount) => current += amount;
    }

    private static byte[] Save(XLWorkbook workbook)
    { using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray(); }
}
