using System.Data;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRMS.Application.Services;

/// <summary>
/// Phase 1 Job Status boundary. It deliberately accepts the existing string values instead of introducing
/// an enum or lookup table, so legacy and tenant-configured values remain valid.
/// </summary>
public sealed class JobStatusService(
    IHrmsDbContext db,
    ITenantContext tenant,
    ICurrentAuthorizationContext authorization,
    TimeProvider clock) : IJobStatusService
{
    private const string NoTenantMessage = "An authenticated tenant and account are required.";

    public Task<Result<string?>> InitializeForCreationAsync(
        Employee employee,
        string? jobStatus,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        cancellationToken.ThrowIfCancellationRequested();

        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty ||
            tenant.UserId is not Guid userId || userId == Guid.Empty)
        {
            return Task.FromResult(Result<string?>.Unauthorized(NoTenantMessage));
        }

        if (employee.Id == Guid.Empty)
            return Task.FromResult(Result<string?>.Invalid("employeeId", "Employee is required."));

        if (employee.TenantId != tenantId)
            return Task.FromResult(Result<string?>.Forbidden("Employee does not belong to the authenticated tenant."));

        if (!authorization.HasAnyPermission(Permissions.Employee.Create))
            return Task.FromResult(Result<string?>.Forbidden("This workflow is not authorized to create an employee."));

        var normalized = Normalize(jobStatus);
        if (normalized is not null && normalized.Length > 100)
            return Task.FromResult(Result<string?>.Invalid("jobStatus", "Job Status must not exceed 100 characters."));

        employee.JobStatus = normalized;

        if (normalized is not null)
        {
            db.EmployeeAuditLogs.Add(new EmployeeAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employee.Id,
                EmployeeCode = employee.EmployeeCode,
                Module = "Employee",
                Section = "Employment",
                EntityName = nameof(Employee),
                RecordId = employee.Id,
                FieldName = "JobStatus",
                OldValue = null,
                NewValue = normalized,
                ChangeType = AuditChangeType.StatusChange,
                ChangedBy = userId.ToString(),
                Reason = Normalize(reason),
                Source = JobStatusMutationSource.Creation.ToString()
            });
        }

        return Task.FromResult(Result<string?>.Success(normalized, "Job Status initialized."));
    }

    public async Task<Result<JobStatusChangeResult>> ChangeAsync(
        JobStatusChangeRequest request,
        CancellationToken cancellationToken = default)
        => await ChangeCoreAsync(request, ownsTransaction: true, cancellationToken);

    public async Task<Result<JobStatusChangeResult>> ChangeWithinTransactionAsync(
        JobStatusChangeRequest request,
        IDbContextTransaction callerTransaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callerTransaction);
        var activeTransaction = db.CurrentTransaction;
        if (activeTransaction is null ||
            !ReferenceEquals(activeTransaction.GetDbTransaction(), callerTransaction.GetDbTransaction()))
        {
            return Result<JobStatusChangeResult>.Conflict(
                "Job Status caller transaction is not active on this DbContext.");
        }

        return await ChangeCoreAsync(request, ownsTransaction: false, cancellationToken);
    }

    private async Task<Result<JobStatusChangeResult>> ChangeCoreAsync(
        JobStatusChangeRequest request,
        bool ownsTransaction,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty ||
            tenant.UserId is not Guid userId || userId == Guid.Empty)
        {
            return Result<JobStatusChangeResult>.Unauthorized(NoTenantMessage);
        }

        if (request.EmployeeId == Guid.Empty)
            return Result<JobStatusChangeResult>.Invalid("employeeId", "Employee is required.");

        var requestedStatus = Normalize(request.JobStatus);
        if (requestedStatus is null)
            return Result<JobStatusChangeResult>.Invalid("jobStatus", "Job Status is required.");
        if (requestedStatus.Length > 100)
            return Result<JobStatusChangeResult>.Invalid("jobStatus", "Job Status must not exceed 100 characters.");

        if (!IsAuthorized(request.Source))
            return Result<JobStatusChangeResult>.Forbidden("This workflow is not authorized to change Job Status.");

        // The tenant predicate is explicit as well as relying on EF's global filter. This keeps the
        // service safe if it is ever used with a context whose filters are disabled in a test or job.
        var employee = await db.Employees
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<JobStatusChangeResult>.NotFound("Employee was not found in this tenant.");

        var previous = Normalize(employee.JobStatus);
        var transition = ValidateTransition(request.Source, employee.Status, previous, requestedStatus);
        if (transition is not null)
            return Result<JobStatusChangeResult>.Conflict(transition);

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = ownsTransaction
            ? await db.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        // Re-read under the transaction so a workflow cannot validate one tenant row and update another
        // row after a concurrent change.
        employee = await db.Employees
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<JobStatusChangeResult>.NotFound("Employee was not found in this tenant.");

        previous = Normalize(employee.JobStatus);
        transition = ValidateTransition(request.Source, employee.Status, previous, requestedStatus);
        if (transition is not null)
            return Result<JobStatusChangeResult>.Conflict(transition);

        employee.JobStatus = requestedStatus;
        employee.ModifiedDate = now;

        var employment = await db.EmployeeEmployments
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employee.Id, cancellationToken);
        if (employment is null)
        {
            employment = new EmployeeEmployment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employee.Id,
                JobStatus = requestedStatus
            };
            db.EmployeeEmployments.Add(employment);
        }
        else
        {
            employment.JobStatus = requestedStatus;
            employment.ModifiedDate = now;
        }

        db.EmployeeAuditLogs.Add(new EmployeeAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            EmployeeCode = employee.EmployeeCode,
            Module = "Employee",
            Section = "Employment",
            EntityName = nameof(Employee),
            RecordId = employee.Id,
            FieldName = "JobStatus",
            OldValue = previous,
            NewValue = requestedStatus,
            ChangeType = AuditChangeType.StatusChange,
            ChangedBy = userId.ToString(),
            Reason = Normalize(request.Reason),
            Source = request.Source.ToString()
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            if (ownsTransaction)
                await transaction!.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (ownsTransaction && transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            return Result<JobStatusChangeResult>.Conflict("Job Status changed concurrently; retry the operation.");
        }
        catch (DbUpdateException)
        {
            if (ownsTransaction && transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            return Result<JobStatusChangeResult>.Unavailable("Job Status could not be saved; retry the operation.");
        }

        return Result<JobStatusChangeResult>.Success(
            new JobStatusChangeResult(employee.Id, previous, requestedStatus, request.Source),
            "Job Status changed.");
    }

    private bool IsAuthorized(JobStatusMutationSource source) => source switch
    {
        JobStatusMutationSource.Creation => authorization.HasAnyPermission(Permissions.Employee.Create),
        JobStatusMutationSource.Confirmation => authorization.HasAnyPermission(Permissions.Confirmation.Decide),
        JobStatusMutationSource.Separation => authorization.HasAnyPermission(Permissions.Separation.Manage),
        JobStatusMutationSource.Employment => authorization.HasAnyPermission(Permissions.EmploymentHistory.Change),
        _ => false
    };

    private static string? ValidateTransition(
        JobStatusMutationSource source,
        EmployeeStatus employeeStatus,
        string? previous,
        string requested)
    {
        if (string.Equals(previous, requested, StringComparison.OrdinalIgnoreCase))
            return "Job Status is already set to the requested value.";

        if (source == JobStatusMutationSource.Creation)
            return previous is null ? null : "Creation may assign Job Status only when it has not been set.";

        if (source == JobStatusMutationSource.Confirmation)
        {
            if (employeeStatus != EmployeeStatus.Active)
                return "Confirmation cannot change Job Status for an employee who is no longer active.";
            if (string.Equals(previous, "Separated", StringComparison.OrdinalIgnoreCase))
                return "A separated employee cannot be changed by Confirmation.";
            return null;
        }

        if (source == JobStatusMutationSource.Separation)
        {
            if (!string.Equals(requested, "Separated", StringComparison.OrdinalIgnoreCase))
                return "Separation may set Job Status only to 'Separated'.";
            if (employeeStatus != EmployeeStatus.Active)
                return "Separation cannot change Job Status for an employee who is already inactive.";
            return null;
        }

        if (source == JobStatusMutationSource.Employment)
            return null;

        return "Unsupported Job Status mutation source.";
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
