using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class ClearanceService(IHrmsDbContext db, ITenantContext tenant, IEmployeeIdentityResolver identity, IEmployeeManagerResolver managerResolver, TimeProvider clock, IClearanceFailureInjector? failureInjector = null) : IClearanceService
{
    private static readonly HashSet<SeparationClearanceTaskStatus> Resolved = [SeparationClearanceTaskStatus.Cleared, SeparationClearanceTaskStatus.Waived, SeparationClearanceTaskStatus.NotApplicable];

    public async Task<Result<ClearanceTemplateDto>> CreateTemplateAsync(ClearanceTemplateRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenant.UserId is not Guid actor) return Result<ClearanceTemplateDto>.Unauthorized("No authenticated tenant and user.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) || request.Items.Count == 0) return Result<ClearanceTemplateDto>.Invalid("template", "A template, code, name and at least one item are required.");
        if (request.EffectiveTo < request.EffectiveFrom) return Result<ClearanceTemplateDto>.Invalid("effectiveTo", "Effective To cannot precede Effective From.");
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.SeparationClearanceTemplates.AnyAsync(x => x.TenantId == tenantId && x.Code == code, ct)) return Result<ClearanceTemplateDto>.Conflict("A clearance template with this code already exists.");
        var template = new SeparationClearanceTemplate { Id = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = request.Name.Trim(), Description = request.Description?.Trim(), EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, AppliesToSeparationType = request.AppliesToSeparationType, AppliesToReasonId = request.AppliesToReasonId, CreatedByUserId = actor };
        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.Name)) return Result<ClearanceTemplateDto>.Invalid("items", "Each clearance item needs a code and name.");
            template.Items.Add(new SeparationClearanceTemplateItem { Id = Guid.NewGuid(), TenantId = tenantId, TemplateId = template.Id, Code = item.Code.Trim().ToUpperInvariant(), Name = item.Name.Trim(), Description = item.Description?.Trim(), Category = item.Category, OwnerType = item.OwnerType, OwnerReferenceId = item.OwnerReferenceId, IsMandatory = item.IsMandatory, RequiresAssetReturn = item.RequiresAssetReturn, RequiresComment = item.RequiresComment, RequiresEvidence = item.RequiresEvidence, Sequence = item.Sequence, DueDaysBeforeLwd = item.DueDaysBeforeLwd });
        }
        db.SeparationClearanceTemplates.Add(template); await db.SaveChangesAsync(ct); return Result<ClearanceTemplateDto>.Success(ToTemplate(template));
    }

    public async Task<Result<IReadOnlyList<ClearanceTemplateDto>>> GetTemplatesAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<ClearanceTemplateDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.SeparationClearanceTemplates.AsNoTracking().Include(x => x.Items).Where(x => x.TenantId == tenantId).OrderBy(x => x.Code).ToListAsync(ct);
        return Result<IReadOnlyList<ClearanceTemplateDto>>.Success(rows.Select(ToTemplate).ToList());
    }

    public async Task<Result<SeparationClearanceDto>> StartAsync(Guid separationId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenant.UserId is not Guid actor) return Result<SeparationClearanceDto>.Unauthorized("No authenticated tenant and user.");
        var separation = await db.EmployeeSeparations.Include(x => x.Reason).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == separationId, ct);
        if (separation is null) return Result<SeparationClearanceDto>.NotFound("Separation case not found.");
        if (separation.Status is not EmployeeSeparationStatus.Approved and not EmployeeSeparationStatus.NoticePeriod) return Result<SeparationClearanceDto>.Conflict("Clearance can start only after separation approval.");
        if (await db.SeparationClearances.AnyAsync(x => x.TenantId == tenantId && x.EmployeeSeparationId == separationId, ct)) return Result<SeparationClearanceDto>.Conflict("Clearance has already been started for this separation.");
        var businessDate = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var template = await db.SeparationClearanceTemplates.Include(x => x.Items).Where(x => x.TenantId == tenantId && x.IsActive && x.EffectiveFrom <= businessDate && (x.EffectiveTo == null || x.EffectiveTo >= businessDate) && (x.AppliesToSeparationType == null || x.AppliesToSeparationType == separation.SeparationType) && (x.AppliesToReasonId == null || x.AppliesToReasonId == separation.ReasonId)).OrderByDescending(x => x.AppliesToReasonId != null).ThenByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        if (template is null) return Result<SeparationClearanceDto>.Conflict("No effective clearance template is configured for this separation.");
        var clearance = new SeparationClearance { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separationId, EmployeeId = separation.EmployeeId, TemplateId = template.Id, Status = SeparationClearanceStatus.InProgress, StartedAtUtc = clock.GetUtcNow().UtcDateTime, CreatedByUserId = actor };
        foreach (var item in template.Items.Where(x => x.IsActive).OrderBy(x => x.Sequence))
        {
            clearance.Tasks.Add(new SeparationClearanceTask { Id = Guid.NewGuid(), TenantId = tenantId, SeparationClearanceId = clearance.Id, TemplateItemId = item.Id, Code = item.Code, Name = item.Name, Category = item.Category, OwnerType = item.OwnerType, AssignedUserId = item.OwnerType == ClearanceOwnerType.SpecificUser ? item.OwnerReferenceId : null, AssignedDepartmentId = item.OwnerType == ClearanceOwnerType.Department ? item.OwnerReferenceId : null, IsMandatory = item.IsMandatory, RequiresAssetReturn = item.RequiresAssetReturn, RequiresComment = item.RequiresComment, RequiresEvidence = item.RequiresEvidence, DueDate = item.DueDaysBeforeLwd is int offset && separation.ApprovedLastWorkingDate is DateOnly lwd ? lwd.AddDays(-offset) : separation.ApprovedLastWorkingDate });
        }
        db.SeparationClearanceEvents.Add(NewEvent(clearance, SeparationClearanceEventType.ClearanceStarted, null, clearance.Status, null, actor, null, null));
        db.SeparationClearances.Add(clearance); await db.SaveChangesAsync(ct); return Result<SeparationClearanceDto>.Success(ToDto(clearance));
    }

    public Task<Result<SeparationClearanceDto>> GetAsync(Guid clearanceId, CancellationToken ct = default) => LoadResultAsync(x => x.Id == clearanceId, ct);
    public Task<Result<SeparationClearanceDto>> GetForSeparationAsync(Guid separationId, CancellationToken ct = default) => LoadResultAsync(x => x.EmployeeSeparationId == separationId, ct);

    public Task<Result<SeparationClearanceDto>> ClearTaskAsync(Guid taskId, ClearanceTaskActionRequest request, CancellationToken ct = default) => TaskActionAsync(taskId, SeparationClearanceTaskStatus.Cleared, SeparationClearanceEventType.TaskCleared, request, ct);
    public Task<Result<SeparationClearanceDto>> BlockTaskAsync(Guid taskId, ClearanceTaskActionRequest request, CancellationToken ct = default) => TaskActionAsync(taskId, SeparationClearanceTaskStatus.Blocked, SeparationClearanceEventType.TaskBlocked, request, ct);
    public Task<Result<SeparationClearanceDto>> WaiveTaskAsync(Guid taskId, ClearanceTaskActionRequest request, CancellationToken ct = default) => TaskActionAsync(taskId, SeparationClearanceTaskStatus.Waived, SeparationClearanceEventType.TaskWaived, request, ct);

    private async Task<Result<SeparationClearanceDto>> TaskActionAsync(Guid taskId, SeparationClearanceTaskStatus status, SeparationClearanceEventType eventType, ClearanceTaskActionRequest request, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId || tenant.UserId is not Guid actor) return Result<SeparationClearanceDto>.Unauthorized("No authenticated tenant and user.");
        db.ClearChangeTracker();
        var task = await db.SeparationClearanceTasks.Include(x => x.Clearance).ThenInclude(x => x!.Tasks).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, ct);
        if (task?.Clearance is null) return Result<SeparationClearanceDto>.NotFound("Clearance task not found.");
        if (task.Clearance.Status is SeparationClearanceStatus.Completed or SeparationClearanceStatus.Cancelled) return Result<SeparationClearanceDto>.Conflict("Completed clearance must be reopened before task changes.");
        if (task.Status is not SeparationClearanceTaskStatus.Pending and not SeparationClearanceTaskStatus.InProgress) return Result<SeparationClearanceDto>.Conflict("The clearance task has already reached a terminal state.");
        if (status is SeparationClearanceTaskStatus.Blocked or SeparationClearanceTaskStatus.Waived && string.IsNullOrWhiteSpace(request.Reason)) return Result<SeparationClearanceDto>.Invalid("reason", "A reason is required.");
        if (status == SeparationClearanceTaskStatus.Cleared && task.RequiresComment && string.IsNullOrWhiteSpace(request.Comment)) return Result<SeparationClearanceDto>.Invalid("comment", "A comment is required for this task.");
        if (status == SeparationClearanceTaskStatus.Cleared && task.RequiresAssetReturn && !await db.SeparationAssetReturns.AnyAsync(x => x.TenantId == tenantId && x.SeparationClearanceTaskId == task.Id && (x.ReturnStatus == SeparationAssetReturnStatus.Returned || x.ReturnStatus == SeparationAssetReturnStatus.NotApplicable), ct)) return Result<SeparationClearanceDto>.Conflict("All required assets must be returned or marked not applicable before clearance.");
        var actorIdentity = await identity.ResolveCurrentAsync(ct);
        if (actorIdentity.Succeeded && task.Clearance.EmployeeId == actorIdentity.Value?.EmployeeId) return Result<SeparationClearanceDto>.Forbidden("An employee cannot clear their own clearance task.");
        if (task.OwnerType == ClearanceOwnerType.SpecificUser && task.AssignedUserId is Guid assignedUser && assignedUser != actor) return Result<SeparationClearanceDto>.Forbidden("This task is assigned to another user.");
        if (task.OwnerType == ClearanceOwnerType.Manager)
        {
            if (!actorIdentity.Succeeded || actorIdentity.Value?.EmployeeId == task.Clearance.EmployeeId) return Result<SeparationClearanceDto>.Forbidden("Only the effective manager may act on this task.");
            var manager = await managerResolver.ResolveAsync(task.Clearance.EmployeeId, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), ct);
            if (!manager.Succeeded || manager.Value?.ManagerId != actorIdentity.Value!.EmployeeId) return Result<SeparationClearanceDto>.Forbidden("You are not the effective manager for this task.");
        }
        if (status == SeparationClearanceTaskStatus.Waived && !task.IsMandatory) return Result<SeparationClearanceDto>.Invalid("task", "Only mandatory tasks require an authorized waiver.");
        var old = task.Status; task.Status = status; task.Comment = request.Comment?.Trim(); task.BlockingReason = status == SeparationClearanceTaskStatus.Blocked ? request.Reason?.Trim() : null; task.CompletedAtUtc = status is SeparationClearanceTaskStatus.Cleared or SeparationClearanceTaskStatus.Waived ? clock.GetUtcNow().UtcDateTime : null; task.CompletedByUserId = status is SeparationClearanceTaskStatus.Cleared or SeparationClearanceTaskStatus.Waived ? actor : null; task.ModifiedDate = clock.GetUtcNow().UtcDateTime; task.ConcurrencyVersion++;
        db.SeparationClearanceEvents.Add(NewEvent(task.Clearance, eventType, task.Clearance.Status, task.Clearance.Status, task, actor, request.Reason, request.Comment, old, status));
        try { failureInjector?.BeforeTaskCommit(); await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<SeparationClearanceDto>.Conflict("The clearance task was changed concurrently."); } catch (DbUpdateException) { return Result<SeparationClearanceDto>.Unavailable("The clearance task could not be saved; retry after the transaction is rolled back."); } catch (InvalidOperationException) { return Result<SeparationClearanceDto>.Unavailable("The clearance task could not be saved; retry after the transaction is rolled back."); }
        return Result<SeparationClearanceDto>.Success(ToDto(task.Clearance));
    }

    public async Task<Result<SeparationClearanceDto>> CompleteAsync(Guid clearanceId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenant.UserId is not Guid actor) return Result<SeparationClearanceDto>.Unauthorized("No authenticated tenant and user.");
        var clearance = await db.SeparationClearances.Include(x => x.Tasks).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == clearanceId, ct); if (clearance is null) return Result<SeparationClearanceDto>.NotFound("Clearance not found.");
        if (clearance.Tasks.Any(x => x.IsMandatory && !Resolved.Contains(x.Status))) return Result<SeparationClearanceDto>.Conflict("Mandatory clearance tasks remain unresolved.");
        if (clearance.Status == SeparationClearanceStatus.Completed) return Result<SeparationClearanceDto>.Success(ToDto(clearance), "Clearance is already complete.");
        var previous = clearance.Status; clearance.Status = SeparationClearanceStatus.Completed; clearance.CompletedAtUtc = clock.GetUtcNow().UtcDateTime; clearance.ConcurrencyVersion++;
        var separation = await db.EmployeeSeparations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == clearance.EmployeeSeparationId, ct); if (separation is not null && separation.Status is EmployeeSeparationStatus.Approved or EmployeeSeparationStatus.NoticePeriod) { separation.Status = EmployeeSeparationStatus.ReadyForExit; separation.ModifiedByUserId = actor; separation.ModifiedDate = clock.GetUtcNow().UtcDateTime; separation.ConcurrencyVersion++; }
        db.SeparationClearanceEvents.Add(NewEvent(clearance, SeparationClearanceEventType.ClearanceCompleted, previous, clearance.Status, null, actor, null, null)); try { failureInjector?.BeforeCompletionCommit(); await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<SeparationClearanceDto>.Conflict("Clearance was changed concurrently."); } catch (DbUpdateException) { return Result<SeparationClearanceDto>.Unavailable("Clearance completion failed and was rolled back; retry after review."); } catch (InvalidOperationException) { return Result<SeparationClearanceDto>.Unavailable("Clearance completion failed and was rolled back; retry after review."); } return Result<SeparationClearanceDto>.Success(ToDto(clearance));
    }

    public async Task<Result<SeparationClearanceDto>> ReopenAsync(Guid clearanceId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result<SeparationClearanceDto>.Invalid("reason", "A reason is required to reopen clearance.");
        if (tenant.TenantId is not Guid tenantId || tenant.UserId is not Guid actor) return Result<SeparationClearanceDto>.Unauthorized("No authenticated tenant and user.");
        var clearance = await db.SeparationClearances.Include(x => x.Tasks).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == clearanceId, ct); if (clearance is null) return Result<SeparationClearanceDto>.NotFound("Clearance not found.");
        if (clearance.Status != SeparationClearanceStatus.Completed) return Result<SeparationClearanceDto>.Conflict("Only completed clearance can be reopened.");
        var previous = clearance.Status; clearance.Status = SeparationClearanceStatus.Reopened; clearance.ReopenedAtUtc = clock.GetUtcNow().UtcDateTime; clearance.CompletedAtUtc = null; clearance.ConcurrencyVersion++; db.SeparationClearanceEvents.Add(NewEvent(clearance, SeparationClearanceEventType.ClearanceReopened, previous, clearance.Status, null, actor, reason.Trim(), null)); await db.SaveChangesAsync(ct); return Result<SeparationClearanceDto>.Success(ToDto(clearance));
    }

    public async Task<Result<SeparationClearanceTaskDto>> AddAssetReturnAsync(Guid taskId, SeparationAssetReturnRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SeparationClearanceTaskDto>.Unauthorized("No authenticated tenant.");
        var task = await db.SeparationClearanceTasks.Include(x => x.Clearance).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, ct); if (task?.Clearance is null) return Result<SeparationClearanceTaskDto>.NotFound("Clearance task not found.");
        if (!task.RequiresAssetReturn) return Result<SeparationClearanceTaskDto>.Invalid("task", "This task does not require an asset return.");
        if (string.IsNullOrWhiteSpace(request.AssetReference) || string.IsNullOrWhiteSpace(request.AssetName)) return Result<SeparationClearanceTaskDto>.Invalid("asset", "Asset reference and name are required.");
        db.SeparationAssetReturns.Add(new SeparationAssetReturn { Id = Guid.NewGuid(), TenantId = tenantId, SeparationClearanceTaskId = task.Id, EmployeeId = task.Clearance.EmployeeId, AssetReference = request.AssetReference.Trim(), AssetType = request.AssetType.Trim(), AssetName = request.AssetName.Trim(), SerialNumber = request.SerialNumber?.Trim(), ExpectedReturnDate = request.ExpectedReturnDate, ReturnStatus = request.ReturnStatus, Condition = request.Condition, RecoveryRequired = request.RecoveryRequired, RecoveryReference = request.RecoveryReference?.Trim(), Comment = request.Comment?.Trim() }); try { failureInjector?.BeforeAssetCommit(); await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<SeparationClearanceTaskDto>.Conflict("Asset return was changed concurrently."); } catch (DbUpdateException) { return Result<SeparationClearanceTaskDto>.Unavailable("Asset return was rolled back; retry after review."); } catch (InvalidOperationException) { return Result<SeparationClearanceTaskDto>.Unavailable("Asset return was rolled back; retry after review."); } return Result<SeparationClearanceTaskDto>.Success(ToTask(task));
    }

    public async Task<Result<SeparationClearanceTaskDto>> UpdateAssetReturnAsync(Guid taskId, Guid assetId, SeparationAssetReturnRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenant.UserId is not Guid actor) return Result<SeparationClearanceTaskDto>.Unauthorized("No authenticated tenant and user.");
        var task = await db.SeparationClearanceTasks.Include(x => x.Clearance).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, ct);
        var asset = await db.SeparationAssetReturns.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == assetId && x.SeparationClearanceTaskId == taskId, ct);
        if (task?.Clearance is null || asset is null) return Result<SeparationClearanceTaskDto>.NotFound("Asset return not found.");
        if (asset.ReturnStatus is not SeparationAssetReturnStatus.PendingReturn && asset.ReturnStatus != request.ReturnStatus) return Result<SeparationClearanceTaskDto>.Conflict("The asset return has already been resolved with a different status.");
        asset.AssetReference = request.AssetReference.Trim(); asset.AssetType = request.AssetType.Trim(); asset.AssetName = request.AssetName.Trim(); asset.SerialNumber = request.SerialNumber?.Trim(); asset.ExpectedReturnDate = request.ExpectedReturnDate; asset.ReturnStatus = request.ReturnStatus; asset.Condition = request.Condition; asset.RecoveryRequired = request.RecoveryRequired; asset.RecoveryReference = request.RecoveryReference?.Trim(); asset.Comment = request.Comment?.Trim(); asset.ModifiedDate = clock.GetUtcNow().UtcDateTime; asset.ConcurrencyVersion++;
        var eventType = request.ReturnStatus == SeparationAssetReturnStatus.Lost ? SeparationClearanceEventType.AssetMarkedLost : SeparationClearanceEventType.AssetReturned;
        db.SeparationClearanceEvents.Add(NewEvent(task.Clearance, eventType, task.Clearance.Status, task.Clearance.Status, task, actor, request.Comment, request.Comment));
        try { failureInjector?.BeforeAssetCommit(); await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<SeparationClearanceTaskDto>.Conflict("Asset return was changed concurrently."); } catch (DbUpdateException) { return Result<SeparationClearanceTaskDto>.Unavailable("Asset return was rolled back; retry after review."); } catch (InvalidOperationException) { return Result<SeparationClearanceTaskDto>.Unavailable("Asset return was rolled back; retry after review."); }
        return Result<SeparationClearanceTaskDto>.Success(ToTask(task));
    }

    public Task<Result<PagedResult<ClearanceInboxItemDto>>> GetManagerInboxAsync(ClearanceInboxQuery query, CancellationToken ct = default) => GetInboxAsync(query, true, ct);
    public Task<Result<PagedResult<ClearanceInboxItemDto>>> GetFunctionalInboxAsync(ClearanceInboxQuery query, CancellationToken ct = default) => GetInboxAsync(query, false, ct);

    private async Task<Result<PagedResult<ClearanceInboxItemDto>>> GetInboxAsync(ClearanceInboxQuery query, bool managerOnly, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId || tenant.UserId is not Guid actor) return Result<PagedResult<ClearanceInboxItemDto>>.Unauthorized("No authenticated tenant and user.");
        var current = await identity.ResolveCurrentAsync(ct);
        if (managerOnly && (!current.Succeeded || current.Value?.EmployeeId is not Guid)) return Result<PagedResult<ClearanceInboxItemDto>>.Forbidden("A linked employee identity is required for manager clearance scope.");
        var actorEmployeeId = current.Value?.EmployeeId;
        Guid? actorDepartmentId = actorEmployeeId is Guid linkedEmployeeId
            ? await db.Employees.Where(x => x.TenantId == tenantId && x.Id == linkedEmployeeId).Select(x => x.DepartmentId).FirstOrDefaultAsync(ct)
            : null;
        var managerEmployeeId = managerOnly ? actorEmployeeId : (Guid?)null;
        var rows = from task in db.SeparationClearanceTasks
                   join clearance in db.SeparationClearances on new { task.TenantId, Id = task.SeparationClearanceId } equals new { clearance.TenantId, Id = clearance.Id }
                   join separation in db.EmployeeSeparations on new { task.TenantId, Id = clearance.EmployeeSeparationId } equals new { separation.TenantId, Id = separation.Id }
                   join employee in db.Employees on new { task.TenantId, Id = clearance.EmployeeId } equals new { employee.TenantId, Id = employee.Id }
                   where task.TenantId == tenantId && (managerOnly
                       ? task.OwnerType == ClearanceOwnerType.Manager && employee.ReportingManagerId == managerEmployeeId
                       : task.AssignedUserId == actor || (task.OwnerType == ClearanceOwnerType.Department && actorDepartmentId != null && task.AssignedDepartmentId == actorDepartmentId))
                   select new { task, clearance, separation, employee };
        if (query.Status is SeparationClearanceTaskStatus status) rows = rows.Where(x => x.task.Status == status);
        if (query.Category is SeparationClearanceTaskCategory category) rows = rows.Where(x => x.task.Category == category);
        if (!string.IsNullOrWhiteSpace(query.Search)) rows = rows.Where(x => (x.employee.EmployeeCode != null && x.employee.EmployeeCode.Contains(query.Search)) || (x.employee.FirstName + " " + x.employee.LastName).Contains(query.Search));
        if (query.DueDate is DateOnly due) rows = rows.Where(x => x.task.DueDate == due);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime); if (query.Overdue is true) rows = rows.Where(x => x.task.DueDate < today && !Resolved.Contains(x.task.Status));
        var total = await rows.CountAsync(ct); var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);
        var items = await rows.OrderBy(x => x.task.DueDate).ThenBy(x => x.employee.EmployeeCode).Skip((page - 1) * size).Take(size).Select(x => new ClearanceInboxItemDto(x.task.Id, x.clearance.Id, x.separation.Id, x.employee.Id, x.employee.EmployeeCode, x.employee.FirstName + " " + x.employee.LastName, x.separation.ApprovedLastWorkingDate, x.task.Name, x.task.Category, x.task.Status, x.task.DueDate, x.task.DueDate < today && !Resolved.Contains(x.task.Status), x.task.RequiresAssetReturn, x.task.Assets.OrderBy(a => a.Id).Select(a => (Guid?)a.Id).FirstOrDefault(), x.task.Assets.OrderBy(a => a.Id).Select(a => (SeparationAssetReturnStatus?)a.ReturnStatus).FirstOrDefault(), x.task.Assets.OrderBy(a => a.Id).Select(a => a.AssetReference).FirstOrDefault(), x.task.Assets.OrderBy(a => a.Id).Select(a => a.AssetName).FirstOrDefault())).ToListAsync(ct);
        return Result<PagedResult<ClearanceInboxItemDto>>.Success(new(items, page, size, total));
    }

    public async Task<Result<PagedResult<ClearanceDashboardItemDto>>> GetHrDashboardAsync(ClearanceInboxQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<ClearanceDashboardItemDto>>.Unauthorized("No authenticated tenant.");
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rows = from clearance in db.SeparationClearances
                   join separation in db.EmployeeSeparations on new { clearance.TenantId, Id = clearance.EmployeeSeparationId } equals new { separation.TenantId, Id = separation.Id }
                   join employee in db.Employees on new { clearance.TenantId, Id = clearance.EmployeeId } equals new { employee.TenantId, Id = employee.Id }
                   where clearance.TenantId == tenantId
                   select new
                   {
                       EmployeeId = employee.Id,
                       EmployeeCode = employee.EmployeeCode,
                       EmployeeName = employee.FirstName + " " + employee.LastName,
                       SeparationId = separation.Id,
                       ApprovedLastWorkingDate = separation.ApprovedLastWorkingDate,
                       ClearanceId = clearance.Id,
                       ClearanceStatus = clearance.Status,
                       MandatoryTaskCount = clearance.Tasks.Count(x => x.IsMandatory),
                       MandatoryCompletedCount = clearance.Tasks.Count(x => x.IsMandatory && Resolved.Contains(x.Status)),
                       PendingCount = clearance.Tasks.Count(x => x.Status == SeparationClearanceTaskStatus.Pending),
                       BlockedCount = clearance.Tasks.Count(x => x.Status == SeparationClearanceTaskStatus.Blocked),
                       PendingAssetCount = clearance.Tasks.SelectMany(x => x.Assets).Count(x => x.ReturnStatus == SeparationAssetReturnStatus.PendingReturn),
                       OverdueCount = clearance.Tasks.Count(x => x.DueDate < today && !Resolved.Contains(x.Status)),
                       ReadyForExit = separation.Status == EmployeeSeparationStatus.ReadyForExit
                   };
        if (query.Status is SeparationClearanceTaskStatus taskStatus) rows = rows.Where(x => taskStatus == SeparationClearanceTaskStatus.Pending ? x.PendingCount > 0 : taskStatus == SeparationClearanceTaskStatus.Blocked && x.BlockedCount > 0);
        if (!string.IsNullOrWhiteSpace(query.Search)) rows = rows.Where(x => (x.EmployeeCode != null && x.EmployeeCode.Contains(query.Search)) || x.EmployeeName.Contains(query.Search));
        var total = await rows.CountAsync(ct); var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize); var raw = await rows.OrderBy(x => x.ApprovedLastWorkingDate).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var items = raw.Select(x => new ClearanceDashboardItemDto(x.EmployeeId, x.EmployeeCode, x.EmployeeName, x.SeparationId, x.ApprovedLastWorkingDate, x.ClearanceId, x.ClearanceStatus, x.MandatoryTaskCount, x.MandatoryCompletedCount, x.PendingCount, x.BlockedCount, x.PendingAssetCount, x.OverdueCount, x.ReadyForExit)).ToList();
        return Result<PagedResult<ClearanceDashboardItemDto>>.Success(new(items, page, size, total));
    }

    private async Task<Result<SeparationClearanceDto>> LoadResultAsync(System.Linq.Expressions.Expression<Func<SeparationClearance, bool>> predicate, CancellationToken ct)
    { if (tenant.TenantId is not Guid tenantId) return Result<SeparationClearanceDto>.Unauthorized("No authenticated tenant."); var row = await db.SeparationClearances.Include(x => x.Tasks).FirstOrDefaultAsync(predicate.And(x => x.TenantId == tenantId), ct); return row is null ? Result<SeparationClearanceDto>.NotFound("Clearance not found.") : Result<SeparationClearanceDto>.Success(ToDto(row)); }

    private static SeparationClearanceEvent NewEvent(SeparationClearance c, SeparationClearanceEventType type, SeparationClearanceStatus? from, SeparationClearanceStatus? to, SeparationClearanceTask? task, Guid? actor, string? reason, string? comment, SeparationClearanceTaskStatus? fromTask = null, SeparationClearanceTaskStatus? toTask = null) => new() { Id = Guid.NewGuid(), TenantId = c.TenantId, SeparationClearanceId = c.Id, TaskId = task?.Id, EventType = type, FromStatus = from, ToStatus = to, FromTaskStatus = fromTask, ToTaskStatus = toTask, ActorUserId = actor, OccurredAtUtc = DateTime.UtcNow, Reason = reason, Comment = comment };
    private static ClearanceTemplateDto ToTemplate(SeparationClearanceTemplate x) => new(x.Id, x.Code, x.Name, x.IsActive, x.EffectiveFrom, x.EffectiveTo, x.Items.OrderBy(i => i.Sequence).Select(i => new ClearanceTemplateItemDto(i.Id, i.Code, i.Name, i.Category, i.OwnerType, i.IsMandatory, i.RequiresAssetReturn, i.RequiresComment, i.RequiresEvidence, i.Sequence, i.DueDaysBeforeLwd)).ToList());
    private static SeparationClearanceTaskDto ToTask(SeparationClearanceTask x) => new(x.Id, x.Code, x.Name, x.Category, x.OwnerType, x.Status, x.IsMandatory, x.DueDate, x.DueDate is DateOnly due && due < DateOnly.FromDateTime(DateTime.UtcNow) && !Resolved.Contains(x.Status), x.Comment, x.BlockingReason, x.RequiresAssetReturn);
    private static SeparationClearanceDto ToDto(SeparationClearance x) { var tasks = x.Tasks.OrderBy(t => t.Code).Select(ToTask).ToList(); return new(x.Id, x.EmployeeSeparationId, x.EmployeeId, x.Status, x.StartedAtUtc, x.CompletedAtUtc, x.Status == SeparationClearanceStatus.Completed, tasks.Count(t => t.IsMandatory), tasks.Count(t => t.IsMandatory && Resolved.Contains(t.Status)), tasks); }
}

internal static class ClearanceExpressionExtensions
{
    public static System.Linq.Expressions.Expression<Func<T, bool>> And<T>(this System.Linq.Expressions.Expression<Func<T, bool>> left, System.Linq.Expressions.Expression<Func<T, bool>> right)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(typeof(T));
        var body = System.Linq.Expressions.Expression.AndAlso(System.Linq.Expressions.Expression.Invoke(left, parameter), System.Linq.Expressions.Expression.Invoke(right, parameter));
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}
