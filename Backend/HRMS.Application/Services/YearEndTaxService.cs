using System.Globalization;
using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class YearEndTaxService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock, IPayrollApprovalGuard? approvalGuard = null) : IYearEndTaxService
{
    public async Task<Result<PagedResult<YearEndTaxRunDto>>> GetRunsAsync(PagedQuery query, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out _)) return Result<PagedResult<YearEndTaxRunDto>>.Unauthorized("An authenticated tenant is required.");
        var source = db.YearEndTaxRuns.AsNoTracking().Where(x => x.TenantId == tid).OrderByDescending(x => x.TaxYear).ThenByDescending(x => x.CreatedAtUtc);
        var total = await source.CountAsync(ct); var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);
        var rows = await source.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return Result<PagedResult<YearEndTaxRunDto>>.Success(new(rows.Select(ToDto).ToList(), page, size, total));
    }

    public async Task<Result<YearEndTaxRunDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out _)) return Result<YearEndTaxRunDto>.Unauthorized("An authenticated tenant is required.");
        var row = await db.YearEndTaxRuns.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        return row is null ? Result<YearEndTaxRunDto>.NotFound("Year-end tax run was not found.") : Result<YearEndTaxRunDto>.Success(ToDto(row));
    }

    public async Task<Result<YearEndTaxRunDto>> CreateAsync(YearEndTaxRunRequest request, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out var userId)) return Result<YearEndTaxRunDto>.Unauthorized("An authenticated tenant is required.");
        if (request.TaxYear <= 0 || request.EndDate < request.StartDate || string.IsNullOrWhiteSpace(request.TaxYearCode)) return Result<YearEndTaxRunDto>.Invalid("taxYear", "Tax year identity and dates are required.");
        if (await db.YearEndTaxRuns.AnyAsync(x => x.TenantId == tid && x.TaxYear == request.TaxYear, ct)) return Result<YearEndTaxRunDto>.Conflict("A year-end tax run already exists for this tax year.");
        var overlap = await db.YearEndTaxRuns.AnyAsync(x => x.TenantId == tid && x.Status != YearEndTaxRunStatus.Cancelled && x.StartDate <= request.EndDate && x.EndDate >= request.StartDate, ct);
        if (overlap) return Result<YearEndTaxRunDto>.Conflict("The requested tax-year dates overlap an existing run.");
        var row = new YearEndTaxRun { Id = Guid.NewGuid(), TenantId = tid, TaxYear = request.TaxYear, TaxYearCode = request.TaxYearCode.Trim(), StartDate = request.StartDate, EndDate = request.EndDate, CreatedByUserId = userId, CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
        db.YearEndTaxRuns.Add(row); AddHistory(row, YearEndTaxHistoryEvent.Created, null, row.Status, "Year-end tax run created."); await db.SaveChangesAsync(ct);
        return Result<YearEndTaxRunDto>.Success(ToDto(row), "Year-end tax run created.");
    }

    public async Task<Result<YearEndTaxRunDto>> CalculateAsync(Guid id, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out var userId)) return Result<YearEndTaxRunDto>.Unauthorized("An authenticated tenant is required.");
        var run = await db.YearEndTaxRuns.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct); if (run is null) return Result<YearEndTaxRunDto>.NotFound("Year-end tax run was not found.");
        if (run.Status is not (YearEndTaxRunStatus.Draft or YearEndTaxRunStatus.Calculated)) return Result<YearEndTaxRunDto>.Conflict("Only draft or calculated runs may be recalculated.");
        if (await db.YearEndTaxAdjustments.AnyAsync(x => x.TenantId == tid && x.RunId == id && x.Status == YearEndTaxAdjustmentStatus.HandoffCreated, ct)) return Result<YearEndTaxRunDto>.Conflict("A run with a handed-off adjustment cannot be recalculated.");

        var results = await db.PayrollResults.AsNoTracking().Include(x => x.PayrollRun).ThenInclude(x => x!.PayrollPeriod).Where(x => x.TenantId == tid && x.Status == PayrollResultStatus.Calculated && x.IsCurrent && x.PayrollRun!.Status == PayrollRunStatus.Finalized && x.PayrollRun.PayrollPeriod!.StartDate <= run.EndDate && x.PayrollRun.PayrollPeriod.EndDate >= run.StartDate).ToListAsync(ct);
        var resultIds = results.Select(x => x.Id).ToList();
        var components = resultIds.Count == 0 ? [] : await db.PayrollResultComponents.AsNoTracking().Where(x => x.TenantId == tid && resultIds.Contains(x.PayrollResultId)).ToListAsync(ct);
        var statutory = resultIds.Count == 0 ? [] : await db.PayrollStatutoryResults.AsNoTracking().Where(x => x.TenantId == tid && resultIds.Contains(x.PayrollResultId) && x.StatutoryType == StatutoryType.IncomeTax).ToListAsync(ct);
        var declarations = await db.EmployeeTaxDeclarations.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.Item).Include(x => x.Lines).ThenInclude(x => x.Proofs).Where(x => x.TenantId == tid && x.Cycle!.FinancialYear == run.TaxYear && (x.Status == EmployeeTaxDeclarationStatus.Approved || x.Status == EmployeeTaxDeclarationStatus.PartiallyApproved || x.Status == EmployeeTaxDeclarationStatus.Locked)).ToListAsync(ct);
        var previous = await db.YearEndTaxPreviousEmployerInputs.AsNoTracking().Where(x => x.TenantId == tid && x.RunId == id && x.Status == YearEndTaxPreviousEmployerStatus.Approved).ToListAsync(ct);
        var profiles = await db.EmployeeStatutoryProfiles.AsNoTracking().Where(x => x.TenantId == tid && x.IsActive && x.EffectiveFrom <= run.EndDate && (x.EffectiveTo == null || x.EffectiveTo >= run.StartDate)).ToListAsync(ct);
        var configs = await db.StatutoryConfigurations.AsNoTracking().Include(x => x.Versions).Where(x => x.TenantId == tid && x.IsActive && x.StatutoryType == StatutoryType.IncomeTax).ToListAsync(ct);
        var configVersionIds = configs.SelectMany(x => x.Versions).Select(x => x.Id).ToList(); var slabs = configVersionIds.Count == 0 ? [] : await db.StatutorySlabs.AsNoTracking().Where(x => x.TenantId == tid && configVersionIds.Contains(x.StatutoryConfigurationVersionId)).ToListAsync(ct);

        var employeeIds = results.Select(x => x.EmployeeId).Concat(declarations.Select(x => x.EmployeeId)).Concat(previous.Select(x => x.EmployeeId)).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == tid && employeeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var oldEmployees = await db.YearEndTaxEmployees.Where(x => x.TenantId == tid && x.RunId == id).ToListAsync(ct); var oldAdjustments = await db.YearEndTaxAdjustments.Where(x => x.TenantId == tid && x.RunId == id).ToListAsync(ct); var oldStatements = await db.YearEndTaxStatements.Where(x => x.TenantId == tid && x.RunId == id).ToListAsync(ct);
        db.YearEndTaxStatements.RemoveRange(oldStatements); db.YearEndTaxAdjustments.RemoveRange(oldAdjustments); db.YearEndTaxEmployees.RemoveRange(oldEmployees);
        var output = new List<YearEndTaxEmployee>(); var blocking = 0; decimal dueTotal = 0, excessTotal = 0;
        foreach (var employeeId in employeeIds.Where(employees.ContainsKey))
        {
            var employeeResults = results.Where(x => x.EmployeeId == employeeId).ToList(); var employeeResultIds = employeeResults.Select(x => x.Id).ToHashSet(); var employeeComponents = components.Where(x => employeeResultIds.Contains(x.PayrollResultId)).ToList();
            var ytdGross = employeeResults.Sum(x => x.GrossEarnings); var ytdTaxable = employeeComponents.Where(x => x.IsTaxable && x.IsEarning).Sum(x => x.CalculatedAmount) - employeeComponents.Where(x => x.IsTaxable && x.IsDeduction).Sum(x => x.CalculatedAmount); var ytdTax = statutory.Where(x => x.EmployeeId == employeeId).Sum(x => x.EmployeeAmount);
            var declarationLines = declarations.Where(x => x.EmployeeId == employeeId).SelectMany(x => x.Lines.Where(l => l.Status is EmployeeTaxDeclarationLineStatus.Approved or EmployeeTaxDeclarationLineStatus.PartiallyApproved)).ToList(); var declared = declarationLines.Sum(x => x.ApprovedAmount ?? 0m); var proof = declarationLines.Where(x => !x.Item!.RequiresProof || x.Proofs.Any(p => p.Status == TaxDeclarationProofStatus.Accepted)).Sum(x => x.ApprovedAmount ?? 0m);
            var previousRows = previous.Where(x => x.EmployeeId == employeeId).ToList(); var previousIncome = previousRows.Sum(x => x.TaxableIncome); var previousTax = previousRows.Sum(x => x.TaxDeducted); var previousDeduction = previousRows.Sum(x => x.EligibleDeductionAmount);
            var taxable = Math.Max(0m, ytdTaxable + previousIncome - proof - previousDeduction); var taxOutcome = ResolveTax(taxable, profiles.Where(x => x.EmployeeId == employeeId).ToList(), configs, slabs, run.EndDate); var issue = taxOutcome.IssueCode;
            var annualTax = taxOutcome.Tax ?? 0m; var taxDue = Math.Max(0m, annualTax - ytdTax - previousTax); var excess = Math.Max(0m, ytdTax + previousTax - annualTax); if (issue is not null) blocking++;
            var row = new YearEndTaxEmployee { Id = Guid.NewGuid(), TenantId = tid, RunId = id, EmployeeId = employeeId, YtdGross = ytdGross, YtdTaxableIncome = ytdTaxable, YtdTaxDeducted = ytdTax, ApprovedDeclarationAmount = declared, ApprovedProofAmount = proof, PreviousEmployerTaxableIncome = previousIncome, PreviousEmployerTaxDeducted = previousTax, ProjectedAnnualTaxableIncome = taxable, FinalTaxableIncome = taxable, ProjectedAnnualTax = annualTax, FinalTaxLiability = annualTax, EstimatedTaxDue = taxDue, EstimatedExcessTax = excess, Status = issue is not null ? YearEndTaxEmployeeStatus.BlockingIssue : taxDue > 0 ? YearEndTaxEmployeeStatus.AdjustmentRecommended : YearEndTaxEmployeeStatus.Reconciled, BlockingIssueCode = issue, BlockingIssueMessage = taxOutcome.IssueMessage, CalculationSnapshotJson = JsonSerializer.Serialize(new { run.TaxYear, employeeId, finalizedResultCount = employeeResults.Count, declarationLineCount = declarationLines.Count }) };
            output.Add(row); db.YearEndTaxEmployees.Add(row); if (taxDue > 0 && issue is null) db.YearEndTaxAdjustments.Add(new YearEndTaxAdjustment { Id = Guid.NewGuid(), TenantId = tid, RunId = id, EmployeeId = employeeId, YearEndTaxEmployeeId = row.Id, Amount = taxDue, Direction = PayrollAdjustmentDirection.Deduction, Status = YearEndTaxAdjustmentStatus.Recommended, Reason = "Additional tax indicated by the configured annual reconciliation.", SourceCalculationReference = $"YEAR-END/{run.TaxYear}/{employeeId:N}", CreatedAtUtc = clock.GetUtcNow().UtcDateTime, CreatedByUserId = userId });
            db.YearEndTaxStatements.Add(new YearEndTaxStatement { Id = Guid.NewGuid(), TenantId = tid, RunId = id, YearEndTaxEmployeeId = row.Id, EmployeeId = employeeId, StatementReference = $"YTS/{run.TaxYear}/{employeeId:N}"[..Math.Min(100, $"YTS/{run.TaxYear}/{employeeId:N}".Length)], GeneratedAtUtc = clock.GetUtcNow().UtcDateTime, SnapshotJson = JsonSerializer.Serialize(new { row.TenantId, row.RunId, row.EmployeeId, row.YtdGross, row.YtdTaxableIncome, row.YtdTaxDeducted, row.ApprovedDeclarationAmount, row.ApprovedProofAmount, row.PreviousEmployerTaxableIncome, row.PreviousEmployerTaxDeducted, row.ProjectedRemainingTaxableIncome, row.ProjectedAnnualTaxableIncome, row.ProjectedAnnualTax, row.EstimatedTaxDue, row.EstimatedExcessTax, row.FinalTaxableIncome, row.FinalTaxLiability, row.Status, row.BlockingIssueCode, row.BlockingIssueMessage, row.CalculationSnapshotJson }) });
        }
        run.Status = YearEndTaxRunStatus.Calculated; run.EmployeeCount = output.Count; run.BlockingIssueCount = blocking; run.TotalTaxDue = dueTotal = output.Sum(x => x.EstimatedTaxDue); run.TotalExcessTax = excessTotal = output.Sum(x => x.EstimatedExcessTax); run.ConcurrencyVersion++; AddHistory(run, YearEndTaxHistoryEvent.Calculated, YearEndTaxRunStatus.Draft, run.Status, $"Calculated {output.Count} employees; due {dueTotal}; excess {excessTotal}."); await db.SaveChangesAsync(ct);
        return Result<YearEndTaxRunDto>.Success(ToDto(run), "Year-end tax reconciliation calculated.");
    }

    public async Task<Result<PagedResult<YearEndTaxEmployeeDto>>> GetEmployeesAsync(Guid id, YearEndTaxEmployeeQuery query, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out _)) return Result<PagedResult<YearEndTaxEmployeeDto>>.Unauthorized("An authenticated tenant is required.");
        var source = db.YearEndTaxEmployees.AsNoTracking().Include(x => x.Employee).Where(x => x.TenantId == tid && x.RunId == id); if (query.EmployeeId is { } eid) source = source.Where(x => x.EmployeeId == eid); if (query.Status is { } status) source = source.Where(x => x.Status == status); if (query.HasTaxDue == true) source = source.Where(x => x.EstimatedTaxDue > 0); if (query.HasBlockingIssue == true) source = source.Where(x => x.BlockingIssueCode != null);
        var total = await source.CountAsync(ct); var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize); var rows = await source.OrderBy(x => x.Employee!.EmployeeCode).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct); return Result<PagedResult<YearEndTaxEmployeeDto>>.Success(new(rows.Select(ToEmployeeDto).ToList(), page, size, total));
    }

    public async Task<Result<YearEndTaxEmployeeDto>> GetEmployeeAsync(Guid id, Guid employeeId, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out _)) return Result<YearEndTaxEmployeeDto>.Unauthorized("An authenticated tenant is required."); var row = await db.YearEndTaxEmployees.AsNoTracking().Include(x => x.Employee).FirstOrDefaultAsync(x => x.TenantId == tid && x.RunId == id && x.EmployeeId == employeeId, ct); return row is null ? Result<YearEndTaxEmployeeDto>.NotFound("Year-end employee reconciliation was not found.") : Result<YearEndTaxEmployeeDto>.Success(ToEmployeeDto(row));
    }

    public async Task<Result<YearEndTaxPreviousEmployerDto>> AddPreviousEmployerAsync(Guid runId, YearEndTaxPreviousEmployerRequest request, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out var userId)) return Result<YearEndTaxPreviousEmployerDto>.Unauthorized("An authenticated tenant is required."); var run = await db.YearEndTaxRuns.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == runId, ct); if (run is null) return Result<YearEndTaxPreviousEmployerDto>.NotFound("Year-end tax run was not found."); if (run.Status is YearEndTaxRunStatus.Approved or YearEndTaxRunStatus.Closed or YearEndTaxRunStatus.Cancelled) return Result<YearEndTaxPreviousEmployerDto>.Conflict("Previous-employer input cannot be changed in the current run state."); if (request.TaxableIncome < 0 || request.TaxDeducted < 0 || request.EligibleDeductionAmount < 0 || string.IsNullOrWhiteSpace(request.EmployerName)) return Result<YearEndTaxPreviousEmployerDto>.Invalid("input", "Employer and non-negative monetary values are required."); if (!await db.Employees.AnyAsync(x => x.TenantId == tid && x.Id == request.EmployeeId, ct)) return Result<YearEndTaxPreviousEmployerDto>.NotFound("Employee was not found in the current tenant."); if (await db.YearEndTaxPreviousEmployerInputs.AnyAsync(x => x.TenantId == tid && x.RunId == runId && x.EmployeeId == request.EmployeeId && x.EmployerReference == request.EmployerReference, ct)) return Result<YearEndTaxPreviousEmployerDto>.Conflict("This previous-employer source already exists for the run.");
        var row = new YearEndTaxPreviousEmployerInput { Id = Guid.NewGuid(), TenantId = tid, RunId = runId, EmployeeId = request.EmployeeId, EmployerName = request.EmployerName.Trim(), EmployerReference = request.EmployerReference?.Trim(), TaxableIncome = request.TaxableIncome, TaxDeducted = request.TaxDeducted, EligibleDeductionAmount = request.EligibleDeductionAmount, EvidenceReference = request.EvidenceReference?.Trim(), Status = YearEndTaxPreviousEmployerStatus.Submitted, CreatedByUserId = userId, CreatedAtUtc = clock.GetUtcNow().UtcDateTime }; db.YearEndTaxPreviousEmployerInputs.Add(row); AddHistory(run, YearEndTaxHistoryEvent.PreviousEmployerAdded, run.Status, run.Status, "Previous-employer input submitted for review."); await db.SaveChangesAsync(ct); return Result<YearEndTaxPreviousEmployerDto>.Success(ToPrevious(row));
    }

    public async Task<Result<YearEndTaxPreviousEmployerDto>> UpdatePreviousEmployerAsync(Guid runId, Guid inputId, YearEndTaxPreviousEmployerUpdateRequest request, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out var userId)) return Result<YearEndTaxPreviousEmployerDto>.Unauthorized("An authenticated tenant is required."); var row = await db.YearEndTaxPreviousEmployerInputs.Include(x => x.Run).FirstOrDefaultAsync(x => x.TenantId == tid && x.RunId == runId && x.Id == inputId, ct); if (row is null) return Result<YearEndTaxPreviousEmployerDto>.NotFound("Previous-employer input was not found."); if (row.Run!.Status is YearEndTaxRunStatus.Approved or YearEndTaxRunStatus.Closed or YearEndTaxRunStatus.Cancelled) return Result<YearEndTaxPreviousEmployerDto>.Conflict("A closed or approved run cannot be edited."); if (request.TaxableIncome < 0 || request.TaxDeducted < 0 || request.EligibleDeductionAmount < 0) return Result<YearEndTaxPreviousEmployerDto>.Invalid("input", "Monetary values cannot be negative."); row.EmployerName = request.EmployerName.Trim(); row.EmployerReference = request.EmployerReference?.Trim(); row.TaxableIncome = request.TaxableIncome; row.TaxDeducted = request.TaxDeducted; row.EligibleDeductionAmount = request.EligibleDeductionAmount; row.EvidenceReference = request.EvidenceReference?.Trim(); row.Status = YearEndTaxPreviousEmployerStatus.Submitted; row.UpdatedByUserId = userId; row.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; row.ConcurrencyVersion++; AddHistory(row.Run, YearEndTaxHistoryEvent.PreviousEmployerUpdated, row.Run.Status, row.Run.Status, "Previous-employer input updated."); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<YearEndTaxPreviousEmployerDto>.Conflict("The previous-employer input changed while it was being updated."); } return Result<YearEndTaxPreviousEmployerDto>.Success(ToPrevious(row));
    }

    public Task<Result<YearEndTaxRunDto>> SubmitAsync(Guid id, CancellationToken ct = default) => TransitionAsync(id, YearEndTaxRunStatus.Submitted, YearEndTaxHistoryEvent.Submitted, YearEndTaxRunStatus.Calculated, ct);
    public async Task<Result<YearEndTaxRunDto>> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out _)) return Result<YearEndTaxRunDto>.Unauthorized("An authenticated tenant is required.");
        var run = await db.YearEndTaxRuns.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        if (run is null) return Result<YearEndTaxRunDto>.NotFound("Year-end tax run was not found.");
        var guard = await (approvalGuard ?? new PayrollApprovalGuard(db, tenant)).ValidateAsync(run.SubmittedByUserId, "approve", ct: ct);
        if (!guard.Succeeded) return Result<YearEndTaxRunDto>.Failure(guard.Status, guard.Message, guard.Errors);
        return await TransitionAsync(id, YearEndTaxRunStatus.Approved, YearEndTaxHistoryEvent.Approved, YearEndTaxRunStatus.Submitted, ct);
    }
    public Task<Result<YearEndTaxRunDto>> CancelAsync(Guid id, CancellationToken ct = default) => TransitionAsync(id, YearEndTaxRunStatus.Cancelled, YearEndTaxHistoryEvent.Cancelled, YearEndTaxRunStatus.Draft, ct);

    public async Task<Result<YearEndTaxRunDto>> CloseAsync(Guid id, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out var userId)) return Result<YearEndTaxRunDto>.Unauthorized("An authenticated tenant is required."); var run = await db.YearEndTaxRuns.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct); if (run is null) return Result<YearEndTaxRunDto>.NotFound("Year-end tax run was not found."); if (run.Status != YearEndTaxRunStatus.Approved) return Result<YearEndTaxRunDto>.Conflict("Only an approved run may be closed."); if (run.BlockingIssueCount > 0) return Result<YearEndTaxRunDto>.Conflict("Blocking reconciliation issues must be resolved before close."); run.Status = YearEndTaxRunStatus.Closed; run.ClosedByUserId = userId; run.ClosedAtUtc = clock.GetUtcNow().UtcDateTime; run.ConcurrencyVersion++; AddHistory(run, YearEndTaxHistoryEvent.Closed, YearEndTaxRunStatus.Approved, run.Status, "Year-end tax run closed; snapshot is immutable."); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<YearEndTaxRunDto>.Conflict("The run changed while it was being closed."); } return Result<YearEndTaxRunDto>.Success(ToDto(run), "Year-end tax run closed.");
    }

    public async Task<Result<YearEndTaxAdjustmentDto>> HandoffAdjustmentAsync(Guid runId, YearEndTaxAdjustmentHandoffRequest request, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out var userId)) return Result<YearEndTaxAdjustmentDto>.Unauthorized("An authenticated tenant is required."); if (string.IsNullOrWhiteSpace(request.ComponentCode)) return Result<YearEndTaxAdjustmentDto>.Invalid("componentCode", "A configured Payroll component code is required for handoff."); var row = await db.YearEndTaxAdjustments.Include(x => x.Run).FirstOrDefaultAsync(x => x.TenantId == tid && x.RunId == runId && x.Id == request.AdjustmentId, ct); if (row is null) return Result<YearEndTaxAdjustmentDto>.NotFound("Year-end tax adjustment was not found."); if (row.Run!.Status is not (YearEndTaxRunStatus.Approved or YearEndTaxRunStatus.Closed)) return Result<YearEndTaxAdjustmentDto>.Conflict("Only an approved or closed run may hand off an adjustment."); if (row.PayrollAdjustmentId is { } existing) return Result<YearEndTaxAdjustmentDto>.Success(ToAdjustment(row), "Adjustment handoff was already completed.");
        var adjustment = new PayrollAdjustment { Id = Guid.NewGuid(), TenantId = tid, EmployeeId = row.EmployeeId, SourceType = "YearEndTaxAdjustment", SourceId = row.Id, SourceReferenceId = row.RunId, AdjustmentType = PayrollAdjustmentType.TaxAdjustment, ComponentCode = request.ComponentCode.Trim(), Description = row.Reason, Amount = row.Amount, CurrencyCode = "INR", AdjustmentNumber = $"YET/{row.Run!.TaxYear}/{row.EmployeeId:N}"[..Math.Min(60, $"YET/{row.Run.TaxYear}/{row.EmployeeId:N}".Length)], EffectiveDate = row.Run.EndDate, SalaryComponentId = request.SalaryComponentId, Direction = row.Direction, TaxTreatment = PayrollAdjustmentTaxTreatment.TaxAdjustmentOnly, StatutoryTreatment = PayrollAdjustmentStatutoryTreatment.IncludeInIncomeTax, SettlementMethod = PayrollAdjustmentSettlementMethod.Payroll, Status = PayrollAdjustmentStatus.Draft, SubmittedAtUtc = clock.GetUtcNow().UtcDateTime, SubmittedByUserId = userId };
        db.PayrollAdjustments.Add(adjustment); db.PayrollAdjustmentHistories.Add(new PayrollAdjustmentHistory { Id = Guid.NewGuid(), TenantId = tid, PayrollAdjustmentId = adjustment.Id, EventType = PayrollAdjustmentHistoryEventType.Created, NewStatus = adjustment.Status, Amount = adjustment.Amount, OccurredAtUtc = clock.GetUtcNow().UtcDateTime, ActorUserId = userId, Reason = "Created from approved year-end tax reconciliation." }); row.PayrollAdjustmentId = adjustment.Id; row.Status = YearEndTaxAdjustmentStatus.HandoffCreated; AddHistory(row.Run, YearEndTaxHistoryEvent.AdjustmentHandedOff, row.Run.Status, row.Run.Status, "Year-end adjustment handed off to PayrollAdjustment workflow."); try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Result<YearEndTaxAdjustmentDto>.Conflict("The year-end adjustment handoff conflicted with an existing source adjustment."); } return Result<YearEndTaxAdjustmentDto>.Success(ToAdjustment(row), "Year-end adjustment handed off to PayrollAdjustment workflow.");
    }

    public async Task<Result<IReadOnlyList<YearEndTaxHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out _)) return Result<IReadOnlyList<YearEndTaxHistoryDto>>.Unauthorized("An authenticated tenant is required."); if (!await db.YearEndTaxRuns.AnyAsync(x => x.TenantId == tid && x.Id == id, ct)) return Result<IReadOnlyList<YearEndTaxHistoryDto>>.NotFound("Year-end tax run was not found."); var rows = await db.YearEndTaxHistories.AsNoTracking().Where(x => x.TenantId == tid && x.RunId == id).OrderBy(x => x.OccurredAtUtc).Select(x => new YearEndTaxHistoryDto(x.Id, x.Event, x.PreviousStatus, x.NewStatus, x.ActorUserId, x.OccurredAtUtc, x.Message)).ToListAsync(ct); return Result<IReadOnlyList<YearEndTaxHistoryDto>>.Success(rows);
    }

    public async Task<Result<PayrollOutputFile>> ExportAsync(Guid id, string kind, CancellationToken ct = default)
    {
        if (!Tenant(out var tid, out _)) return Result<PayrollOutputFile>.Unauthorized("An authenticated tenant is required.");
        if (!await db.YearEndTaxRuns.AnyAsync(x => x.TenantId == tid && x.Id == id, ct)) return Result<PayrollOutputFile>.NotFound("Year-end tax run was not found.");
        var employees = await db.YearEndTaxEmployees.AsNoTracking().Include(x => x.Employee).Where(x => x.TenantId == tid && x.RunId == id).OrderBy(x => x.Employee!.EmployeeCode).ThenBy(x => x.Id).ToListAsync(ct);
        var csv = kind.Trim().ToLowerInvariant() switch
        {
            "summary" => SummaryCsv(employees),
            "exceptions" => EmployeeCsv(employees.Where(x => x.BlockingIssueCode is not null), "year-end-tax-exceptions.csv"),
            "statements" or "employees" => EmployeeCsv(employees, kind.Trim().Equals("statements", StringComparison.OrdinalIgnoreCase) ? "year-end-tax-statements.csv" : "year-end-tax-reconciliation.csv"),
            _ => null
        };
        return csv is null ? Result<PayrollOutputFile>.Invalid("kind", "Supported exports are summary, employees, statements, and exceptions.") : Result<PayrollOutputFile>.Success(csv);
    }

    private static PayrollOutputFile SummaryCsv(IReadOnlyCollection<YearEndTaxEmployee> employees)
    {
        var csv = new CsvBuilder("EmployeeCount", "BlockingIssues", "YtdGross", "YtdTaxableIncome", "TaxDeducted", "ProjectedTax", "TaxDue", "ExcessTax");
        csv.AppendRow(employees.Count.ToString(CultureInfo.InvariantCulture), employees.Count(x => x.BlockingIssueCode is not null).ToString(CultureInfo.InvariantCulture), Money(employees.Sum(x => x.YtdGross)), Money(employees.Sum(x => x.YtdTaxableIncome)), Money(employees.Sum(x => x.YtdTaxDeducted)), Money(employees.Sum(x => x.ProjectedAnnualTax)), Money(employees.Sum(x => x.EstimatedTaxDue)), Money(employees.Sum(x => x.EstimatedExcessTax)));
        return new PayrollOutputFile("year-end-tax-summary.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes());
    }

    private static PayrollOutputFile EmployeeCsv(IEnumerable<YearEndTaxEmployee> source, string fileName)
    {
        var csv = new CsvBuilder("EmployeeCode", "EmployeeId", "YtdGross", "YtdTaxableIncome", "TaxDeducted", "DeclarationAmount", "ProofAmount", "PreviousEmployerIncome", "PreviousEmployerTax", "ProjectedAnnualTax", "TaxDue", "ExcessTax", "FinalTaxableIncome", "FinalTaxLiability", "Status", "BlockingIssue");
        foreach (var x in source) csv.AppendRow(x.Employee?.EmployeeCode, x.EmployeeId.ToString("D"), Money(x.YtdGross), Money(x.YtdTaxableIncome), Money(x.YtdTaxDeducted), Money(x.ApprovedDeclarationAmount), Money(x.ApprovedProofAmount), Money(x.PreviousEmployerTaxableIncome), Money(x.PreviousEmployerTaxDeducted), Money(x.ProjectedAnnualTax), Money(x.EstimatedTaxDue), Money(x.EstimatedExcessTax), Money(x.FinalTaxableIncome), Money(x.FinalTaxLiability), x.Status.ToString(), x.BlockingIssueMessage);
        return new PayrollOutputFile(fileName, "text/csv; charset=utf-8", csv.ToUtf8Bytes());
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private async Task<Result<YearEndTaxRunDto>> TransitionAsync(Guid id, YearEndTaxRunStatus next, YearEndTaxHistoryEvent ev, YearEndTaxRunStatus required, CancellationToken ct)
    {
        if (!Tenant(out var tid, out var userId)) return Result<YearEndTaxRunDto>.Unauthorized("An authenticated tenant is required."); var run = await db.YearEndTaxRuns.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct); if (run is null) return Result<YearEndTaxRunDto>.NotFound("Year-end tax run was not found."); if (run.Status != required) return Result<YearEndTaxRunDto>.Conflict("The year-end tax lifecycle transition is not allowed."); if (next == YearEndTaxRunStatus.Approved && run.BlockingIssueCount > 0) return Result<YearEndTaxRunDto>.Conflict("Blocking reconciliation issues must be resolved before approval."); var old = run.Status; run.Status = next; if (next == YearEndTaxRunStatus.Submitted) { run.SubmittedByUserId = userId; run.SubmittedAtUtc = clock.GetUtcNow().UtcDateTime; } if (next == YearEndTaxRunStatus.Approved) { run.ApprovedByUserId = userId; run.ApprovedAtUtc = clock.GetUtcNow().UtcDateTime; } run.ConcurrencyVersion++; AddHistory(run, ev, old, next, null); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<YearEndTaxRunDto>.Conflict("The run changed while this command was in progress."); } return Result<YearEndTaxRunDto>.Success(ToDto(run));
    }

    private (decimal? Tax, string? IssueCode, string? IssueMessage) ResolveTax(decimal taxable, IReadOnlyList<EmployeeStatutoryProfile> profiles, IReadOnlyList<StatutoryConfiguration> configurations, IReadOnlyList<StatutorySlab> slabs, DateOnly asOf)
    {
        var profile = profiles.OrderByDescending(x => x.EffectiveFrom).FirstOrDefault(); if (profile is null || !profile.IncomeTaxApplicable) return (null, "TAX_PROFILE_MISSING", "No active income-tax profile is configured."); var candidates = configurations.Where(x => x.JurisdictionCode == profile.JurisdictionCode && (x.StateCode == null || x.StateCode == profile.StateCode)).SelectMany(x => x.Versions.Where(v => v.Status == StatutoryConfigurationStatus.Active && v.EffectiveFrom <= asOf && (v.EffectiveTo == null || v.EffectiveTo >= asOf)).Select(v => (Configuration: x, Version: v))).OrderByDescending(x => x.Version.Priority).ToList(); if (candidates.Count == 0) return (null, "TAX_CONFIGURATION_MISSING", "No active annual income-tax configuration is available."); if (candidates.Skip(1).Any(x => x.Version.Priority == candidates[0].Version.Priority)) return (null, "TAX_CONFIGURATION_AMBIGUOUS", "More than one annual income-tax configuration has the same priority."); var slab = slabs.Where(x => x.StatutoryConfigurationVersionId == candidates[0].Version.Id && x.FromAmount <= taxable && (x.ToAmount == null || x.ToAmount >= taxable) && x.OptionalMonth == null).OrderBy(x => x.Sequence).FirstOrDefault(); return slab is null ? (null, "TAX_SLAB_MISSING", "No annual income-tax slab matches the reconciled taxable income.") : (PayrollRoundingPolicy.RoundMoney(slab.FixedAmount + taxable * slab.Rate / 100m), null, null);
    }

    private void AddHistory(YearEndTaxRun run, YearEndTaxHistoryEvent ev, YearEndTaxRunStatus? previous, YearEndTaxRunStatus? next, string? message) => db.YearEndTaxHistories.Add(new YearEndTaxHistory { Id = Guid.NewGuid(), TenantId = run.TenantId, RunId = run.Id, Event = ev, PreviousStatus = previous, NewStatus = next, ActorUserId = tenant.UserId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime, Message = message });
    private bool Tenant(out Guid id, out Guid? userId) { id = tenant.TenantId ?? Guid.Empty; userId = tenant.UserId; return tenant.TenantId is not null; }
    private static YearEndTaxRunDto ToDto(YearEndTaxRun x) => new(x.Id, x.TaxYear, x.TaxYearCode, x.StartDate, x.EndDate, x.Status, x.EmployeeCount, x.BlockingIssueCount, x.TotalTaxDue, x.TotalExcessTax, x.CreatedAtUtc, x.SubmittedAtUtc, x.ApprovedAtUtc, x.ClosedAtUtc);
    private static YearEndTaxEmployeeDto ToEmployeeDto(YearEndTaxEmployee x) => new(x.Id, x.EmployeeId, x.Employee?.EmployeeCode ?? string.Empty, string.Join(' ', new[] { x.Employee?.FirstName, x.Employee?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))), x.YtdGross, x.YtdTaxableIncome, x.YtdTaxDeducted, x.ApprovedDeclarationAmount, x.ApprovedProofAmount, x.PreviousEmployerTaxableIncome, x.PreviousEmployerTaxDeducted, x.ProjectedRemainingTaxableIncome, x.ProjectedAnnualTaxableIncome, x.ProjectedAnnualTax, x.EstimatedTaxDue, x.EstimatedExcessTax, x.FinalTaxableIncome, x.FinalTaxLiability, x.Status, x.BlockingIssueCode, x.BlockingIssueMessage);
    private static YearEndTaxPreviousEmployerDto ToPrevious(YearEndTaxPreviousEmployerInput x) => new(x.Id, x.RunId, x.EmployeeId, x.EmployerName, x.EmployerReference, x.TaxableIncome, x.TaxDeducted, x.EligibleDeductionAmount, x.EvidenceReference, x.Status);
    private static YearEndTaxAdjustmentDto ToAdjustment(YearEndTaxAdjustment x) => new(x.Id, x.RunId, x.EmployeeId, x.Amount, x.Direction, x.Status, x.PayrollAdjustmentId, x.Reason, x.SourceCalculationReference);
}
