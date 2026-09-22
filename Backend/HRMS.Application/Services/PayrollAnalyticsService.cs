using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace HRMS.Application.Services;

public sealed class PayrollAnalyticsService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock) : IPayrollAnalyticsService
{
    public async Task<Result<PayrollAnalyticsOverviewDto>> GetOverviewAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        var context = await LoadRunAsync(payrollRunId, ct); if (!context.Succeeded) return Result<PayrollAnalyticsOverviewDto>.Failure(context.Status, context.Message);
        var run = context.Value!; var results = await Results(payrollRunId, ct); var totals = Totals(results);
        var bank = await db.BankAdviceBatches.AsNoTracking().Where(x => x.TenantId == run.TenantId && x.PayrollRunId == payrollRunId && x.Status != BankAdviceStatus.Cancelled).Select(x => (decimal?)x.TotalAmount).SumAsync(ct) ?? 0m;
        var journals = await db.PayrollJournalBatches.AsNoTracking().Where(x => x.TenantId == run.TenantId && x.PayrollRunId == payrollRunId && x.Status != PayrollJournalStatus.Cancelled).ToListAsync(ct);
        var open = await db.PayrollReconciliationFindings.CountAsync(x => x.TenantId == run.TenantId && x.Reconciliation!.PayrollRunId == payrollRunId && x.Status == PayrollFindingStatus.Open, ct);
        var critical = await db.PayrollReconciliationFindings.CountAsync(x => x.TenantId == run.TenantId && x.Reconciliation!.PayrollRunId == payrollRunId && x.Status == PayrollFindingStatus.Open && x.Severity == PayrollFindingSeverity.Critical, ct);
        var anomalies = await db.PayrollAnomalyFlags.CountAsync(x => x.TenantId == run.TenantId && x.PayrollRunId == payrollRunId && x.Status == PayrollFindingStatus.Open, ct);
        return Result<PayrollAnalyticsOverviewDto>.Success(new(run.Id, run.RunNumber, run.Status, results.Count, totals.Gross, totals.Deductions, totals.Net, totals.Employer, totals.Tax, bank, journals.Sum(x => x.TotalDebit), journals.Sum(x => x.TotalCredit), open, critical, anomalies));
    }

    public async Task<Result<PayrollRunSummaryDto>> GetRunSummaryAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(payrollRunId, ct); if (!run.Succeeded) return Result<PayrollRunSummaryDto>.Failure(run.Status, run.Message);
        var totals = await GetControlTotalsAsync(payrollRunId, ct); if (!totals.Succeeded) return Result<PayrollRunSummaryDto>.Failure(totals.Status, totals.Message);
        var bank = await db.BankAdviceBatches.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.PayrollRunId == payrollRunId && x.Status != BankAdviceStatus.Cancelled).Select(x => (decimal?)x.TotalAmount).SumAsync(ct);
        var journals = await db.PayrollJournalBatches.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.PayrollRunId == payrollRunId && x.Status != PayrollJournalStatus.Cancelled).ToListAsync(ct);
        return Result<PayrollRunSummaryDto>.Success(new(run.Value!.Id, run.Value.PayrollPeriodId, run.Value.RunType, run.Value.Status, totals.Value!, bank, journals.Count == 0 ? null : journals.Sum(x => x.TotalDebit), journals.Count == 0 ? null : journals.Sum(x => x.TotalCredit)));
    }

    public async Task<Result<PayrollControlTotalsDto>> GetControlTotalsAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(payrollRunId, ct); if (!run.Succeeded) return Result<PayrollControlTotalsDto>.Failure(run.Status, run.Message);
        var results = await Results(payrollRunId, ct); var totals = Totals(results);
        var components = db.PayrollResultComponents.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.PayrollResult!.PayrollRunId == payrollRunId);
        decimal Source(string name) => components.Where(x => x.CalculationSource.ToLower().Contains(name.ToLower())).Sum(x => x.CalculatedAmount);
        var dto = new PayrollControlTotalsDto(results.Select(x => x.EmployeeId).Distinct().Count(), results.Count, totals.Earnings, totals.Deductions, totals.Gross, totals.Net, totals.Employer, totals.Tax, Source("Reimbursement"), Source("Loan"), Source("VariablePay"), Source("Adjustment"));
        return Result<PayrollControlTotalsDto>.Success(dto);
    }

    public Task<Result<IReadOnlyList<PayrollDimensionSummaryDto>>> GetDepartmentSummaryAsync(Guid payrollRunId, CancellationToken ct = default) => GetDimensionSummaryAsync(payrollRunId, true, ct);
    public Task<Result<IReadOnlyList<PayrollDimensionSummaryDto>>> GetCostCenterSummaryAsync(Guid payrollRunId, CancellationToken ct = default) => GetDimensionSummaryAsync(payrollRunId, false, ct);

    private async Task<Result<IReadOnlyList<PayrollDimensionSummaryDto>>> GetDimensionSummaryAsync(Guid payrollRunId, bool department, CancellationToken ct)
    {
        var run = await LoadRunAsync(payrollRunId, ct); if (!run.Succeeded) return Result<IReadOnlyList<PayrollDimensionSummaryDto>>.Failure(run.Status, run.Message);
        var rows = await db.PayrollRunEmployees.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.PayrollRunId == payrollRunId).Join(db.PayrollResults.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.PayrollRunId == payrollRunId && x.IsCurrent && x.Status == PayrollResultStatus.Calculated), e => new { e.TenantId, e.EmployeeId }, r => new { r.TenantId, r.EmployeeId }, (e, r) => new { e.DepartmentId, e.CostCenterId, r }).ToListAsync(ct);
        var grouped = rows.GroupBy(x => department ? x.DepartmentId : x.CostCenterId).ToList(); var output = new List<PayrollDimensionSummaryDto>();
        var dimensionIds = grouped.Where(x => x.Key.HasValue).Select(x => x.Key!.Value).ToList();
        var dimensionNames = department
            ? (await db.Departments.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && dimensionIds.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)).ToDictionary(x => x.Id, x => (x.Code, x.Name))
            : (await db.CostCenters.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && dimensionIds.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct)).ToDictionary(x => x.Id, x => (x.Code, x.Name));
        foreach (var group in grouped)
        {
            var id = group.Key; var code = "UNCLASSIFIED"; var name = "Historical dimension unavailable";
            if (id is Guid value && dimensionNames.TryGetValue(value, out var dimension)) { code = dimension.Code; name = dimension.Name; }
            output.Add(new PayrollDimensionSummaryDto(id, code, name, group.Select(x => x.r.EmployeeId).Distinct().Count(), group.Count(), group.Sum(x => x.r.GrossEarnings), group.Sum(x => x.r.GrossEarnings), group.Sum(x => x.r.TotalDeductions), group.Sum(x => x.r.NetPay), group.Sum(x => x.r.EmployerContributions)));
        }
        return Result<IReadOnlyList<PayrollDimensionSummaryDto>>.Success(output.OrderBy(x => x.Code).ToList());
    }

    public async Task<Result<PagedResult<PayrollExceptionDto>>> GetExceptionsAsync(PagedQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<PayrollExceptionDto>>.Unauthorized("No authenticated tenant.");
        var source = db.PayrollAnomalyFlags.AsNoTracking().Where(x => x.TenantId == tenantId).Join(db.Employees.AsNoTracking(), x => new { x.TenantId, Id = x.EmployeeId }, e => new { e.TenantId, Id = (Guid?)e.Id }, (x, e) => new { x, EmployeeCode = e.EmployeeCode });
        if (!string.IsNullOrWhiteSpace(query.Search)) source = source.Where(x => x.EmployeeCode.Contains(query.Search) || x.x.Message.Contains(query.Search));
        var total = await source.CountAsync(ct); var rows = await source.OrderByDescending(x => x.x.CreatedAtUtc).ThenBy(x => x.x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<PayrollExceptionDto>>.Success(new(rows.Select(x => new PayrollExceptionDto(x.x.Id, x.x.PayrollRunId, x.x.EmployeeId, x.EmployeeCode, x.x.AnomalyType, x.x.Severity, x.x.Metric, x.x.CurrentValue, x.x.ComparisonValue, x.x.Difference, x.x.VariancePercent, x.x.Status, x.x.Message, x.x.CreatedAtUtc, x.x.AcknowledgedAtUtc, x.x.AcknowledgedByUserId, x.x.ResolutionNote)).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<PagedResult<PayrollVarianceRowDto>>> GetVarianceAsync(Guid payrollRunId, Guid? compareRunId, PagedQuery query, CancellationToken ct = default)
    {
        var currentRun = await LoadRunAsync(payrollRunId, ct); if (!currentRun.Succeeded) return Result<PagedResult<PayrollVarianceRowDto>>.Failure(currentRun.Status, currentRun.Message);
        var comparisonId = compareRunId ?? await db.PayrollRuns.AsNoTracking().Where(x => x.TenantId == currentRun.Value!.TenantId && x.RunType == currentRun.Value.RunType && x.Id != payrollRunId && x.Status != PayrollRunStatus.Cancelled).OrderByDescending(x => x.PayrollPeriod!.EndDate).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var current = await Results(payrollRunId, ct); var prior = comparisonId.HasValue ? await Results(comparisonId.Value, ct) : [];
        var priorByEmployee = prior.GroupBy(x => x.EmployeeId).ToDictionary(x => x.Key, x => x.First());
        var rows = current.Select(x => Variance(x, priorByEmployee.GetValueOrDefault(x.EmployeeId))).ToList();
        if (!string.IsNullOrWhiteSpace(query.Search)) rows = rows.Where(x => x.EmployeeCode.Contains(query.Search, StringComparison.OrdinalIgnoreCase) || x.EmployeeName.Contains(query.Search, StringComparison.OrdinalIgnoreCase)).ToList();
        var total = rows.Count; var page = rows.OrderBy(x => x.EmployeeCode).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return Result<PagedResult<PayrollVarianceRowDto>>.Success(new(page, query.Page, query.PageSize, total));
    }

    public async Task<Result<IReadOnlyList<PayrollComponentVarianceDto>>> GetComponentVarianceAsync(Guid payrollRunId, Guid? compareRunId, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(payrollRunId, ct); if (!run.Succeeded) return Result<IReadOnlyList<PayrollComponentVarianceDto>>.Failure(run.Status, run.Message);
        var compare = compareRunId ?? await db.PayrollRuns.AsNoTracking().Where(x => x.TenantId == run.Value!.TenantId && x.Id != payrollRunId && x.RunType == run.Value.RunType && x.Status != PayrollRunStatus.Cancelled).OrderByDescending(x => x.PayrollPeriod!.EndDate).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var current = await db.PayrollResultComponents.AsNoTracking().Where(x => x.TenantId == run.Value!.TenantId && x.PayrollResult!.PayrollRunId == payrollRunId).GroupBy(x => new { x.CalculationSource, x.ComponentCode, x.ComponentName }).Select(x => new { x.Key, Amount = x.Sum(y => y.CalculatedAmount) }).ToListAsync(ct);
        var prior = compare.HasValue ? await db.PayrollResultComponents.AsNoTracking().Where(x => x.TenantId == run.Value.TenantId && x.PayrollResult!.PayrollRunId == compare.Value).GroupBy(x => new { x.CalculationSource, x.ComponentCode, x.ComponentName }).Select(x => new { x.Key, Amount = x.Sum(y => y.CalculatedAmount) }).ToListAsync(ct) : [];
        var keys = current.Select(x => x.Key).Union(prior.Select(x => x.Key)).ToList();
        return Result<IReadOnlyList<PayrollComponentVarianceDto>>.Success(keys.Select(key => { var c = current.FirstOrDefault(x => x.Key == key)?.Amount ?? 0m; var p = prior.FirstOrDefault(x => x.Key == key)?.Amount ?? 0m; return new PayrollComponentVarianceDto(key.CalculationSource, key.ComponentCode, key.ComponentName, p, c, c - p, Percent(c - p, p)); }).ToList());
    }

    public async Task<Result<PagedResult<PayrollFindingDto>>> GetFindingsAsync(Guid payrollRunId, PagedQuery query, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(payrollRunId, ct);
        if (!run.Succeeded) return Result<PagedResult<PayrollFindingDto>>.Failure(run.Status, run.Message);
        var source = db.PayrollReconciliationFindings.AsNoTracking()
            .Where(x => x.TenantId == tenant.TenantId && x.Reconciliation!.PayrollRunId == payrollRunId);
        if (!string.IsNullOrWhiteSpace(query.Search))
            source = source.Where(x => x.ControlCode.Contains(query.Search) || x.Message.Contains(query.Search));
        var total = await source.CountAsync(ct);
        var rows = await source.OrderByDescending(x => x.GeneratedAtUtc).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<PayrollFindingDto>>.Success(new(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<PayrollOutputFile>> ExportVarianceAsync(Guid payrollRunId, Guid? compareRunId, CancellationToken ct = default)
    {
        var result = await GetVarianceAsync(payrollRunId, compareRunId, new ExportQuery(), ct);
        if (!result.Succeeded) return Result<PayrollOutputFile>.Failure(result.Status, result.Message);
        var csv = new CsvBuilder("EmployeeCode", "EmployeeName", "PreviousGross", "CurrentGross", "GrossDelta", "GrossVariancePercent", "PreviousDeductions", "CurrentDeductions", "DeductionDelta", "DeductionVariancePercent", "PreviousNet", "CurrentNet", "NetDelta", "NetVariancePercent", "Classification");
        foreach (var row in result.Value!.Items)
            csv.AppendRow(row.EmployeeCode, row.EmployeeName, F(row.BaseGross), F(row.CurrentGross), F(row.GrossDelta), F(row.GrossVariancePercent), F(row.BaseDeduction), F(row.CurrentDeduction), F(row.DeductionDelta), F(row.DeductionVariancePercent), F(row.BaseNet), F(row.CurrentNet), F(row.NetDelta), F(row.NetVariancePercent), row.Classification);
        return Result<PayrollOutputFile>.Success(new PayrollOutputFile($"payroll-variance-{payrollRunId:N}.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes()));
    }

    public async Task<Result<PayrollOutputFile>> ExportFindingsAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        var result = await GetFindingsAsync(payrollRunId, new ExportQuery(), ct);
        if (!result.Succeeded) return Result<PayrollOutputFile>.Failure(result.Status, result.Message);
        var csv = new CsvBuilder("ControlCode", "Scope", "Severity", "Action", "Metric", "EmployeeId", "ExpectedValue", "ActualValue", "Difference", "VariancePercent", "Message", "Status", "GeneratedAtUtc", "ResolutionNote", "ResolutionReference");
        foreach (var row in result.Value!.Items)
            csv.AppendRow(row.ControlCode, row.Scope.ToString(), row.Severity.ToString(), row.Action.ToString(), row.Metric.ToString(), row.EmployeeId?.ToString(), F(row.ExpectedValue), F(row.ActualValue), F(row.Difference), F(row.VariancePercent), row.Message, row.Status.ToString(), row.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture), row.ResolutionNote, row.ResolutionReference);
        return Result<PayrollOutputFile>.Success(new PayrollOutputFile($"payroll-findings-{payrollRunId:N}.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes()));
    }

    public async Task<Result<PayrollOutputFile>> ExportRunSummaryAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        var result = await GetRunSummaryAsync(payrollRunId, ct); if (!result.Succeeded) return Result<PayrollOutputFile>.Failure(result.Status, result.Message); var x = result.Value!;
        var t = x.Totals; var csv = new CsvBuilder("PayrollRunId", "PayrollPeriodId", "RunType", "Status", "EmployeeCount", "ResultCount", "EarningsTotal", "GrossTotal", "DeductionTotal", "NetPayTotal", "EmployerContributionTotal", "TaxTotal", "ReimbursementTotal", "LoanRecoveryTotal", "VariablePayTotal", "AdjustmentTotal", "BankAdviceTotal", "AccountingDebitTotal", "AccountingCreditTotal");
        csv.AppendRow(x.PayrollRunId.ToString(), x.PayrollPeriodId.ToString(), x.RunType.ToString(), x.Status.ToString(), t.EmployeeCount.ToString(CultureInfo.InvariantCulture), t.ResultCount.ToString(CultureInfo.InvariantCulture), F(t.EarningsTotal), F(t.GrossTotal), F(t.DeductionTotal), F(t.NetPayTotal), F(t.EmployerContributionTotal), F(t.TaxTotal), F(t.ReimbursementTotal), F(t.LoanRecoveryTotal), F(t.VariablePayTotal), F(t.AdjustmentTotal), F(x.BankAdviceTotal), F(x.AccountingDebitTotal), F(x.AccountingCreditTotal));
        return Result<PayrollOutputFile>.Success(new PayrollOutputFile($"payroll-summary-{payrollRunId:N}.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes()));
    }

    public async Task<Result<PayrollOutputFile>> ExportControlTotalsAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        var result = await GetControlTotalsAsync(payrollRunId, ct); if (!result.Succeeded) return Result<PayrollOutputFile>.Failure(result.Status, result.Message); var t = result.Value!;
        var csv = new CsvBuilder("EmployeeCount", "ResultCount", "EarningsTotal", "DeductionTotal", "GrossTotal", "NetPayTotal", "EmployerContributionTotal", "TaxTotal", "ReimbursementTotal", "LoanRecoveryTotal", "VariablePayTotal", "AdjustmentTotal"); csv.AppendRow(t.EmployeeCount.ToString(CultureInfo.InvariantCulture), t.ResultCount.ToString(CultureInfo.InvariantCulture), F(t.EarningsTotal), F(t.DeductionTotal), F(t.GrossTotal), F(t.NetPayTotal), F(t.EmployerContributionTotal), F(t.TaxTotal), F(t.ReimbursementTotal), F(t.LoanRecoveryTotal), F(t.VariablePayTotal), F(t.AdjustmentTotal));
        return Result<PayrollOutputFile>.Success(new PayrollOutputFile($"payroll-control-totals-{payrollRunId:N}.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes()));
    }

    public async Task<Result<PayrollOutputFile>> ExportExceptionsAsync(PagedQuery query, CancellationToken ct = default)
    {
        var result = await GetExceptionsAsync(new ExportQuery { Search = query.Search }, ct); if (!result.Succeeded) return Result<PayrollOutputFile>.Failure(result.Status, result.Message); var csv = new CsvBuilder("RunId", "EmployeeCode", "EmployeeId", "AnomalyType", "Severity", "Metric", "CurrentValue", "ComparisonValue", "Difference", "VariancePercent", "Status", "Message", "CreatedAtUtc", "ResolutionNote");
        foreach (var x in result.Value!.Items) csv.AppendRow(x.PayrollRunId.ToString(), x.EmployeeCode, x.EmployeeId?.ToString(), x.AnomalyType.ToString(), x.Severity.ToString(), x.Metric.ToString(), F(x.CurrentValue), F(x.ComparisonValue), F(x.Difference), F(x.VariancePercent), x.Status.ToString(), x.Message, x.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture), x.ResolutionNote);
        return Result<PayrollOutputFile>.Success(new PayrollOutputFile("payroll-exceptions.csv", "text/csv; charset=utf-8", csv.ToUtf8Bytes()));
    }

    public async Task<Result<PayrollReconciliationDto>> GenerateReconciliationAsync(Guid payrollRunId, PayrollReconciliationType type, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(payrollRunId, ct); if (!run.Succeeded) return Result<PayrollReconciliationDto>.Failure(run.Status, run.Message); var tenantId = run.Value!.TenantId;
        var latest = await db.PayrollReconciliations.Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.ReconciliationType == type).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        var version = (latest?.Version ?? 0) + 1; var now = clock.GetUtcNow().UtcDateTime; var reconciliation = new PayrollReconciliation { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = payrollRunId, ReconciliationType = type, Version = version, GeneratedAtUtc = now, GeneratedByUserId = tenant.UserId };
        var results = await Results(payrollRunId, ct); var totals = Totals(results); var checks = new List<PayrollReconciliationFinding>();
        if (type == PayrollReconciliationType.PrePayroll && results.Count == 0) checks.Add(Finding(reconciliation, "NoPayrollResults", PayrollFindingSeverity.Critical, PayrollControlAction.BlockApproval, PayrollControlMetric.EmployeeCount, 1, 0, "No persisted payroll results are available for pre-payroll review."));
        if (type == PayrollReconciliationType.PostPayroll)
        {
            var difference = totals.Gross - totals.Deductions - totals.Net;
            if (difference != 0m) checks.Add(Finding(reconciliation, "PayrollEquationMismatch", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.NetPay, 0, difference, "Persisted payroll totals do not reconcile under the existing gross minus deductions equals net definition."));
            foreach (var result in results.Where(x => x.NetPay < 0m)) { checks.Add(Finding(reconciliation, "NegativeNetPay", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.NetPay, 0m, result.NetPay, "Persisted result has negative net pay.", result.EmployeeId)); if (!await db.PayrollAnomalyFlags.AnyAsync(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.AnomalyType == PayrollAnomalyType.NegativeNetPay && x.EmployeeId == result.EmployeeId && x.Status == PayrollFindingStatus.Open, ct)) db.PayrollAnomalyFlags.Add(Anomaly(tenantId, payrollRunId, result.EmployeeId, result.Id, PayrollAnomalyType.NegativeNetPay, PayrollFindingSeverity.Critical, PayrollControlMetric.NetPay, result.NetPay, null, "Persisted result has negative net pay.")); }
            if (totals.Net == 0m && results.Count > 0) { checks.Add(Finding(reconciliation, "ZeroNetPay", PayrollFindingSeverity.Warning, PayrollControlAction.Informational, PayrollControlMetric.NetPay, 0, totals.Net, "Run net pay is zero; review whether this is expected.")); if (!await db.PayrollAnomalyFlags.AnyAsync(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.AnomalyType == PayrollAnomalyType.ZeroNetPay && x.Status == PayrollFindingStatus.Open, ct)) db.PayrollAnomalyFlags.Add(Anomaly(tenantId, payrollRunId, null, null, PayrollAnomalyType.ZeroNetPay, PayrollFindingSeverity.Warning, PayrollControlMetric.NetPay, totals.Net, null, "Run net pay is zero; review whether this is expected.")); }
            await AddSourceFindingsAsync(tenantId, payrollRunId, reconciliation.Id, totals, results.Count, checks, ct);
        }
        var controls = await db.PayrollVarianceControls.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.EffectiveFrom <= run.Value.PayrollPeriod!.EndDate && (x.EffectiveTo == null || x.EffectiveTo >= run.Value.PayrollPeriod.EndDate) && (x.AppliesToRunType == null || x.AppliesToRunType == run.Value.RunType)).ToListAsync(ct);
        foreach (var control in controls.Where(x => x.Scope == PayrollControlScope.PayrollRun))
        {
            var actual = control.Metric switch { PayrollControlMetric.GrossPay => totals.Gross, PayrollControlMetric.NetPay => totals.Net, PayrollControlMetric.TotalDeduction => totals.Deductions, PayrollControlMetric.EmployeeCount => results.Count, PayrollControlMetric.Tax => totals.Tax, PayrollControlMetric.EmployerContribution => totals.Employer, _ => 0m };
            if (control.AbsoluteThreshold is decimal absolute && Math.Abs(actual) > absolute || control.PercentageThreshold is decimal percentage && Math.Abs(actual) > percentage) checks.Add(Finding(reconciliation, control.Code, control.Severity, control.Action, control.Metric, control.AbsoluteThreshold, actual, $"Control {control.Code} exceeded its configured threshold."));
        }
        reconciliation.Findings = checks; reconciliation.TotalChecks = Math.Max(1, controls.Count + 1); reconciliation.FailedChecks = checks.Count(x => x.Severity == PayrollFindingSeverity.Critical); reconciliation.CriticalChecks = reconciliation.FailedChecks; reconciliation.WarningChecks = checks.Count(x => x.Severity == PayrollFindingSeverity.Warning); reconciliation.PassedChecks = reconciliation.TotalChecks - checks.Count; db.PayrollReconciliations.Add(reconciliation);
        db.PayrollAnalyticsSnapshots.Add(new PayrollAnalyticsSnapshot { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = payrollRunId, SnapshotType = type == PayrollReconciliationType.PrePayroll ? PayrollAnalyticsSnapshotType.PrePayroll : PayrollAnalyticsSnapshotType.PostPayroll, DataVersion = version, GeneratedAtUtc = now, GeneratedByUserId = tenant.UserId, EmployeeCount = results.Count, GrossTotal = totals.Gross, EarningsTotal = totals.Earnings, DeductionTotal = totals.Deductions, EmployerContributionTotal = totals.Employer, TaxTotal = totals.Tax, NetPayTotal = totals.Net, CurrencyCode = results.Select(x => x.CurrencyCode).FirstOrDefault() ?? "INR" });
        await db.SaveChangesAsync(ct); return Result<PayrollReconciliationDto>.Success(ToDto(reconciliation));
    }

    private async Task AddSourceFindingsAsync(Guid tenantId, Guid payrollRunId, Guid reconciliationId, (decimal Gross, decimal Earnings, decimal Deductions, decimal Net, decimal Employer, decimal Tax) totals, int resultCount, List<PayrollReconciliationFinding> checks, CancellationToken ct)
    {
        var bank = await db.BankAdviceBatches.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.Status != BankAdviceStatus.Cancelled).ToListAsync(ct);
        if (bank.Count == 0 && resultCount > 0)
            checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "MissingBankAdvice", PayrollFindingSeverity.Warning, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.BankAdviceTotal, totals.Net, 0m, "No active bank advice exists for a run with persisted payroll results."));
        if (bank.Count > 0)
        {
            var bankAmount = bank.Sum(x => x.TotalAmount);
            var bankCount = await db.BankAdvicePayments.AsNoTracking().CountAsync(x => x.TenantId == tenantId && bank.Select(b => b.Id).Contains(x.BankAdviceBatchId) && x.PaymentStatus != BankAdvicePaymentStatus.Cancelled, ct);
            if (bankAmount != totals.Net) checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "BankAdviceAmountMismatch", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.BankAdviceTotal, totals.Net, bankAmount, "Active bank advice total does not match persisted payroll net payable."));
            if (bankCount != resultCount) checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "BankAdviceCountMismatch", PayrollFindingSeverity.Warning, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.EmployeeCount, resultCount, bankCount, "Active bank advice payment count does not match persisted payroll result count."));
            var duplicateReference = await db.BankAdvicePayments.AsNoTracking().Where(x => x.TenantId == tenantId && bank.Select(b => b.Id).Contains(x.BankAdviceBatchId) && x.PaymentStatus != BankAdvicePaymentStatus.Cancelled && x.PaymentReference != string.Empty).GroupBy(x => x.PaymentReference).AnyAsync(x => x.Count() > 1, ct);
            if (duplicateReference) checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "DuplicateBankPaymentReference", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.BankAdviceTotal, null, null, "Active bank advice contains a duplicate payment reference."));
        }

        var journals = await db.PayrollJournalBatches.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.Status != PayrollJournalStatus.Cancelled).ToListAsync(ct);
        if (journals.Count == 0 && resultCount > 0)
            checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "MissingPayrollJournal", PayrollFindingSeverity.Warning, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.GLTotal, totals.Net, 0m, "No active payroll journal exists for a run with persisted payroll results."));
        if (journals.Count > 0)
        {
            if (journals.Any(x => x.TotalDebit != x.TotalCredit)) checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "AccountingUnbalanced", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.GLTotal, journals.Sum(x => x.TotalDebit), journals.Sum(x => x.TotalCredit), "Persisted payroll journal debit and credit totals are not balanced."));
            if (journals.Count(x => x.PayrollRunId == payrollRunId) > 1) checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "DuplicatePayrollJournal", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.GLTotal, 1, journals.Count, "More than one active payroll journal exists for the run."));
            var postedCredit = journals.Sum(x => x.TotalCredit);
            if (journals.All(x => x.TotalDebit == x.TotalCredit) && postedCredit != totals.Net)
                checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "AccountingPayrollTotalMismatch", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.GLTotal, totals.Net, postedCredit, "Balanced accounting evidence does not match the persisted payroll net payable."));

            var journalIds = journals.Select(x => x.Id).ToList();
            var sourceLinks = await db.PayrollJournalLineSources.AsNoTracking().Where(x => x.TenantId == tenantId && journalIds.Contains(x.JournalLine!.PayrollJournalBatchId)).ToListAsync(ct);
            foreach (var link in sourceLinks.Where(x => x.SourceType.Contains("Reversal", StringComparison.OrdinalIgnoreCase)))
            {
                var valid = await db.PayrollReversals.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == link.SourceId && (x.OriginalPayrollRunId == payrollRunId || x.ReversalRunId == payrollRunId), ct);
                if (!valid)
                    checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "BrokenAccountingReversalLinkage", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.GLTotal, null, link.Amount, "Accounting evidence references a reversal that is not linked to this payroll run."));
            }
            foreach (var link in sourceLinks.Where(x => x.SourceType.Contains("Adjustment", StringComparison.OrdinalIgnoreCase)))
            {
                if (!await db.PayrollAdjustments.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == link.SourceId, ct))
                    checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "BrokenAccountingAdjustmentLinkage", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.GLTotal, null, link.Amount, "Accounting evidence references an adjustment that does not exist for the tenant."));
            }
        }

        var statutory = await db.PayrollStatutoryResults.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).ToListAsync(ct);
        var statutorySources = await db.PayrollStatutoryReturnSources.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).ToListAsync(ct);
        if (statutory.Count > 0 && statutorySources.Count == 0)
            checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "MissingStatutorySource", PayrollFindingSeverity.Warning, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.Tax, statutory.Sum(x => x.TotalAmount), 0m, "Persisted statutory results have no statutory return source."));
        else if (statutory.Count > 0 && statutorySources.Count > 0)
        {
            foreach (var group in statutory.GroupBy(x => x.StatutoryType))
            {
                var expected = group.Sum(x => x.TotalAmount);
                var source = statutorySources.Where(x => StatutorySourceMatches(group.Key, x.SourceType)).Sum(x => x.Amount);
                if (source != expected)
                    checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, $"{group.Key}StatutorySourceMismatch", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.Tax, expected, source, $"Persisted {group.Key} statutory results do not match statutory return source totals."));
            }
            if (statutory.Sum(x => x.TotalAmount) != statutorySources.Sum(x => x.Amount) && !checks.Any(x => x.ControlCode.EndsWith("StatutorySourceMismatch", StringComparison.Ordinal)))
                checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "StatutorySourceMismatch", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.Tax, statutory.Sum(x => x.TotalAmount), statutorySources.Sum(x => x.Amount), "Persisted statutory result totals do not match statutory return source totals."));
        }

        await AddSettlementSourceFindingAsync(reconciliationId, tenantId, payrollRunId, "Reimbursement", db.ReimbursementSettlements.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).Select(x => x.Amount), checks, ct);
        if (await db.ReimbursementSettlements.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.ReimbursementClaimLineId.HasValue).GroupBy(x => x.ReimbursementClaimLineId).AnyAsync(x => x.Count() > 1, ct))
            checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "DuplicateReimbursementSettlement", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, null, null, "More than one persisted reimbursement settlement claims the same claim line."));
        await AddSettlementSourceFindingAsync(reconciliationId, tenantId, payrollRunId, "VariablePay", db.VariablePaySettlements.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).Select(x => x.Amount), checks, ct);
        if (await db.VariablePaySettlements.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).GroupBy(x => x.VariablePayAwardId).AnyAsync(x => x.Count() > 1, ct))
            checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "DuplicateVariablePaySettlement", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, null, null, "More than one persisted variable-pay settlement claims the same award."));
        await AddSettlementSourceFindingAsync(reconciliationId, tenantId, payrollRunId, "Loan", db.LoanRepayments.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).Select(x => x.Amount), checks, ct);
        if (await db.LoanRepayments.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.LoanInstallmentId.HasValue).GroupBy(x => x.LoanInstallmentId).AnyAsync(x => x.Count() > 1, ct))
            checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "DuplicateLoanRecovery", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, null, null, "More than one persisted loan repayment claims the same installment."));
        await AddSettlementSourceFindingAsync(reconciliationId, tenantId, payrollRunId, "Adjustment", db.PayrollAdjustmentApplications.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).Select(x => x.AppliedAmount), checks, ct);
        if (await db.PayrollAdjustmentApplications.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).GroupBy(x => x.PayrollAdjustmentId).AnyAsync(x => x.Count() > 1, ct))
            checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "DuplicatePayrollAdjustmentApplication", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, null, null, "More than one persisted application claims the same payroll adjustment."));
        var reversalAdjustments = await db.PayrollAdjustmentApplications.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId)
            .Join(db.PayrollAdjustments.AsNoTracking().Where(x => x.TenantId == tenantId && x.SourceType == "PayrollReversal"), x => new { x.TenantId, Id = x.PayrollAdjustmentId }, x => new { x.TenantId, Id = x.Id }, (application, adjustment) => adjustment)
            .ToListAsync(ct);
        foreach (var adjustment in reversalAdjustments)
        {
            var linked = adjustment.OriginalPayrollRunId is Guid originalRunId && await db.PayrollReversals.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.OriginalPayrollRunId == originalRunId && x.ReversalRunId == payrollRunId && x.Status != PayrollReversalStatus.Cancelled, ct);
            if (!linked)
                checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "BrokenPayrollAdjustmentReversalLinkage", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, adjustment.Amount, adjustment.AppliedAmount, "Payroll adjustment is marked as a reversal but has no valid persisted reversal linkage."));
        }
        await AddFinalSettlementFindingsAsync(reconciliationId, tenantId, payrollRunId, checks, ct);
    }

    private async Task AddFinalSettlementFindingsAsync(Guid reconciliationId, Guid tenantId, Guid payrollRunId, List<PayrollReconciliationFinding> checks, CancellationToken ct)
    {
        var cases = await db.FinalSettlementCases.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).Select(x => new { x.Id, x.NetSettlement }).ToListAsync(ct);
        if (cases.Count == 0) return;
        var caseIds = cases.Select(x => x.Id).ToList();
        var lines = await db.FinalSettlementLines.AsNoTracking().Where(x => x.TenantId == tenantId && caseIds.Contains(x.FinalSettlementCaseId)).ToListAsync(ct);
        foreach (var settlement in cases)
        {
            var settlementLines = lines.Where(x => x.FinalSettlementCaseId == settlement.Id).ToList();
            var actual = settlementLines.Sum(x => x.IsDeduction ? -x.Amount : x.Amount);
            if (actual != settlement.NetSettlement)
                checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "FinalSettlementSourceMismatch", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, settlement.NetSettlement, actual, "Persisted Final Settlement lines do not reconcile to the finalized settlement amount."));
            await AddFinalSettlementCategoryFindingsAsync(reconciliationId, tenantId, payrollRunId, settlement.Id, settlementLines, checks, ct);
            var duplicateSources = settlementLines.Where(x => x.SourceId.HasValue).GroupBy(x => new { x.SourceType, x.SourceId }).Any(x => x.Count() > 1);
            if (duplicateSources)
                checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, "DuplicateFinalSettlementSource", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, null, null, "Final Settlement contains a duplicated persisted source."));
        }
    }

    private async Task AddFinalSettlementCategoryFindingsAsync(Guid reconciliationId, Guid tenantId, Guid payrollRunId, Guid settlementId, List<FinalSettlementLine> lines, List<PayrollReconciliationFinding> checks, CancellationToken ct)
    {
        var gratuity = await db.GratuityCalculations.AsNoTracking().Where(x => x.TenantId == tenantId && x.FinalSettlementId == settlementId).Select(x => (decimal?)x.FinalGratuityAmount).FirstOrDefaultAsync(ct);
        if (gratuity.HasValue)
            AddCategoryFinding("Gratuity", FinalSettlementLineType.Gratuity, gratuity.Value, "MissingFinalSettlementGratuity", lines, reconciliationId, tenantId, payrollRunId, checks);

        var leave = await db.LeaveEncashmentCalculations.AsNoTracking().Where(x => x.TenantId == tenantId && x.FinalSettlementId == settlementId).Select(x => (decimal?)x.GrossAmount).FirstOrDefaultAsync(ct);
        if (leave.HasValue)
            AddCategoryFinding("LeaveEncashment", FinalSettlementLineType.LeaveEncashment, leave.Value, "MissingFinalSettlementLeaveEncashment", lines, reconciliationId, tenantId, payrollRunId, checks);

        var notices = await db.NoticeSettlementCalculations.AsNoTracking().Where(x => x.TenantId == tenantId && x.FinalSettlementId == settlementId).Select(x => new { x.Type, x.Amount }).ToListAsync(ct);
        foreach (var notice in notices)
        {
            var lineType = notice.Type == NoticeSettlementType.NoticeRecovery ? FinalSettlementLineType.NoticeRecovery : FinalSettlementLineType.NoticePay;
            AddCategoryFinding(lineType == FinalSettlementLineType.NoticeRecovery ? "NoticeRecovery" : "NoticePay", lineType, notice.Amount, lineType == FinalSettlementLineType.NoticeRecovery ? "MissingFinalSettlementNoticeRecovery" : "MissingFinalSettlementNoticePay", lines, reconciliationId, tenantId, payrollRunId, checks);
        }

        await AddPersistedSettlementCategoryFindingsAsync("Reimbursement", lines, reconciliationId, tenantId, payrollRunId, checks, ct);
        await AddPersistedSettlementCategoryFindingsAsync("Loan", lines, reconciliationId, tenantId, payrollRunId, checks, ct);
        await AddPersistedSettlementCategoryFindingsAsync("VariablePay", lines, reconciliationId, tenantId, payrollRunId, checks, ct);
        await AddPersistedSettlementCategoryFindingsAsync("Adjustment", lines, reconciliationId, tenantId, payrollRunId, checks, ct);
    }

    private async Task AddPersistedSettlementCategoryFindingsAsync(string source, List<FinalSettlementLine> lines, Guid reconciliationId, Guid tenantId, Guid payrollRunId, List<PayrollReconciliationFinding> checks, CancellationToken ct)
    {
        var sourceLines = lines.Where(x => x.SourceType.Contains(source, StringComparison.OrdinalIgnoreCase) || source switch
        {
            "Reimbursement" => x.LineType == FinalSettlementLineType.Reimbursement,
            "Loan" => x.LineType == FinalSettlementLineType.Recovery && x.ComponentCode.Contains("Loan", StringComparison.OrdinalIgnoreCase),
            "VariablePay" => x.LineType == FinalSettlementLineType.Bonus,
            "Adjustment" => x.LineType == FinalSettlementLineType.Recovery && x.ComponentCode.Contains("Adjustment", StringComparison.OrdinalIgnoreCase),
            _ => false
        }).ToList();
        if (sourceLines.Count == 0) return;

        var ids = sourceLines.Where(x => x.SourceId.HasValue).Select(x => x.SourceId!.Value).Distinct().ToList();
        var amounts = source switch
        {
            "Reimbursement" => (await db.ReimbursementSettlements.AsNoTracking().Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Amount, ct)),
            "Loan" => (await db.LoanRepayments.AsNoTracking().Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Amount, ct)),
            "VariablePay" => (await db.VariablePaySettlements.AsNoTracking().Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Amount, ct)),
            "Adjustment" => (await db.PayrollAdjustmentApplications.AsNoTracking().Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.AppliedAmount, ct)),
            _ => new Dictionary<Guid, decimal>()
        };
        var missingCode = $"MissingFinalSettlement{source}";
        var mismatchCode = $"FinalSettlement{source}Mismatch";
        foreach (var line in sourceLines)
        {
            if (line.SourceId is not Guid sourceId || !amounts.TryGetValue(sourceId, out var expected))
            {
                checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, missingCode, PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, line.Amount, 0m, $"Final Settlement {source} line has no resolvable persisted source."));
                continue;
            }
            var actual = line.IsDeduction ? -line.Amount : line.Amount;
            var normalizedExpected = line.IsDeduction ? -expected : expected;
            if (actual != normalizedExpected)
                checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, mismatchCode, PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, normalizedExpected, actual, $"Final Settlement {source} line does not match the persisted source amount."));
        }
    }

    private static void AddCategoryFinding(string source, FinalSettlementLineType lineType, decimal expected, string missingCode, List<FinalSettlementLine> lines, Guid reconciliationId, Guid tenantId, Guid payrollRunId, List<PayrollReconciliationFinding> checks)
    {
        var matching = lines.Where(x => x.LineType == lineType || x.SourceType.Contains(source, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matching.Count == 0)
        {
            checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, missingCode, PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, expected, 0m, $"Persisted {source} source is not represented in the Final Settlement lines."));
            return;
        }
        var actual = matching.Sum(x => x.IsDeduction ? -x.Amount : x.Amount);
        if (actual != expected)
            checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, $"FinalSettlement{source}Mismatch", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, expected, actual, $"Persisted {source} source does not match its Final Settlement line."));
    }

    private async Task AddSettlementSourceFindingAsync(Guid reconciliationId, Guid tenantId, Guid payrollRunId, string source, IQueryable<decimal> settlementAmounts, List<PayrollReconciliationFinding> checks, CancellationToken ct)
    {
        var componentAmount = await db.PayrollResultComponents.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollResult!.PayrollRunId == payrollRunId && x.CalculationSource.ToLower().Contains(source.ToLower())).Select(x => (decimal?)x.CalculatedAmount).SumAsync(ct) ?? 0m;
        var settlementAmount = await settlementAmounts.Select(x => (decimal?)x).SumAsync(ct) ?? 0m;
        if (componentAmount != settlementAmount && (componentAmount != 0m || settlementAmount != 0m)) checks.Add(FindingForRun(reconciliationId, tenantId, payrollRunId, $"{source}SourceMismatch", PayrollFindingSeverity.Critical, PayrollControlAction.RequireAcknowledgement, PayrollControlMetric.ComponentAmount, settlementAmount, componentAmount, $"Persisted {source} payroll source amount does not match its settlement amount."));
    }

    private static PayrollReconciliationFinding FindingForRun(Guid reconciliationId, Guid tenantId, Guid payrollRunId, string code, PayrollFindingSeverity severity, PayrollControlAction action, PayrollControlMetric metric, decimal? expected, decimal? actual, string message) => new() { Id = Guid.NewGuid(), TenantId = tenantId, PayrollReconciliationId = reconciliationId, ControlCode = code, Scope = PayrollControlScope.PayrollRun, Severity = severity, Action = action, Metric = metric, ExpectedValue = expected, ActualValue = actual, Difference = actual - expected, Message = message, GeneratedAtUtc = DateTime.UtcNow };
    private static bool StatutorySourceMatches(StatutoryType type, string sourceType) => type switch
    {
        StatutoryType.ProvidentFund => sourceType.Contains("ProvidentFund", StringComparison.OrdinalIgnoreCase) || sourceType.Contains("PF", StringComparison.OrdinalIgnoreCase),
        StatutoryType.Esi => sourceType.Contains("Esi", StringComparison.OrdinalIgnoreCase) || sourceType.Contains("ESI", StringComparison.OrdinalIgnoreCase),
        StatutoryType.ProfessionalTax => sourceType.Contains("ProfessionalTax", StringComparison.OrdinalIgnoreCase) || sourceType.Equals("PT", StringComparison.OrdinalIgnoreCase),
        StatutoryType.IncomeTax => sourceType.Contains("IncomeTax", StringComparison.OrdinalIgnoreCase) || sourceType.Contains("TDS", StringComparison.OrdinalIgnoreCase),
        _ => sourceType.Contains(type.ToString(), StringComparison.OrdinalIgnoreCase)
    };

    public async Task<Result<PayrollReconciliationDto>> GetReconciliationAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.PayrollReconciliations.AsNoTracking().Include(x => x.Findings).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct); return row is null ? Result<PayrollReconciliationDto>.NotFound("Reconciliation not found.") : Result<PayrollReconciliationDto>.Success(ToDto(row));
    }

    public async Task<Result<PayrollReconciliationDto>> ActOnFindingAsync(Guid findingId, string action, PayrollFindingActionRequest request, CancellationToken ct = default)
    {
        var finding = await db.PayrollReconciliationFindings.Include(x => x.Reconciliation).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == findingId, ct); if (finding is null) return Result<PayrollReconciliationDto>.NotFound("Finding not found.");
        if (finding.Status is PayrollFindingStatus.Resolved or PayrollFindingStatus.AcceptedException) return Result<PayrollReconciliationDto>.Conflict("Finding is already closed.");
        var now = clock.GetUtcNow().UtcDateTime; if (string.Equals(action, "acknowledge", StringComparison.OrdinalIgnoreCase)) { finding.Status = PayrollFindingStatus.Acknowledged; finding.AcknowledgedAtUtc = now; finding.AcknowledgedByUserId = tenant.UserId; } else if (string.Equals(action, "resolve", StringComparison.OrdinalIgnoreCase)) { finding.Status = PayrollFindingStatus.Resolved; finding.ResolvedAtUtc = now; finding.ResolvedByUserId = tenant.UserId; finding.ResolutionNote = request.Note; finding.ResolutionReference = request.Reference; } else if (string.Equals(action, "accept-exception", StringComparison.OrdinalIgnoreCase)) { finding.Status = PayrollFindingStatus.AcceptedException; finding.ResolvedAtUtc = now; finding.ResolvedByUserId = tenant.UserId; finding.ResolutionNote = request.Note; finding.ResolutionReference = request.Reference; } else return Result<PayrollReconciliationDto>.Invalid("action", "Unsupported finding action.");
        await db.SaveChangesAsync(ct); var result = await GetReconciliationAsync(finding.PayrollReconciliationId, ct); return result;
    }

    public async Task<Result<PagedResult<PayrollAnalyticsControlDto>>> ListControlsAsync(PagedQuery query, CancellationToken ct = default)
    { var source = db.PayrollVarianceControls.AsNoTracking().Where(x => x.TenantId == tenant.TenantId); if (!string.IsNullOrWhiteSpace(query.Search)) source = source.Where(x => x.Code.Contains(query.Search) || x.Name.Contains(query.Search)); var total = await source.CountAsync(ct); var rows = await source.OrderBy(x => x.Code).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct); return Result<PagedResult<PayrollAnalyticsControlDto>>.Success(new(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total)); }
    public async Task<Result<PayrollAnalyticsControlDto>> CreateControlAsync(PayrollAnalyticsControlRequest request, CancellationToken ct = default) { if (tenant.TenantId is not Guid tenantId) return Result<PayrollAnalyticsControlDto>.Unauthorized("No authenticated tenant."); if (string.IsNullOrWhiteSpace(request.Code) || (request.AbsoluteThreshold is null && request.PercentageThreshold is null)) return Result<PayrollAnalyticsControlDto>.Invalid("A control code and at least one threshold are required."); if (await db.PayrollVarianceControls.AnyAsync(x => x.TenantId == tenantId && x.Code == request.Code && x.EffectiveFrom == request.EffectiveFrom, ct)) return Result<PayrollAnalyticsControlDto>.Conflict("A control with this code and effective date already exists."); var row = Apply(new PayrollVarianceControl { Id = Guid.NewGuid(), TenantId = tenantId }, request); db.PayrollVarianceControls.Add(row); await db.SaveChangesAsync(ct); return Result<PayrollAnalyticsControlDto>.Success(ToDto(row)); }
    public async Task<Result<PayrollAnalyticsControlDto>> UpdateControlAsync(Guid id, PayrollAnalyticsControlRequest request, CancellationToken ct = default) { var row = await db.PayrollVarianceControls.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct); if (row is null) return Result<PayrollAnalyticsControlDto>.NotFound("Analytics control not found."); Apply(row, request); await db.SaveChangesAsync(ct); return Result<PayrollAnalyticsControlDto>.Success(ToDto(row)); }

    private async Task<Result<PayrollRun>> LoadRunAsync(Guid id, CancellationToken ct) { if (tenant.TenantId is not Guid tenantId) return Result<PayrollRun>.Unauthorized("No authenticated tenant."); var run = await db.PayrollRuns.Include(x => x.PayrollPeriod).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); return run is null ? Result<PayrollRun>.NotFound("Payroll run not found.") : Result<PayrollRun>.Success(run); }
    private async Task<List<PayrollResult>> Results(Guid runId, CancellationToken ct) => await db.PayrollResults.AsNoTracking().Include(x => x.Employee).Where(x => x.TenantId == tenant.TenantId && x.PayrollRunId == runId && x.IsCurrent && x.Status == PayrollResultStatus.Calculated).ToListAsync(ct);
    private static (decimal Gross, decimal Earnings, decimal Deductions, decimal Net, decimal Employer, decimal Tax) Totals(List<PayrollResult> r) => (r.Sum(x => x.GrossEarnings), r.Sum(x => x.GrossEarnings), r.Sum(x => x.TotalDeductions), r.Sum(x => x.NetPay), r.Sum(x => x.EmployerContributions), 0m);
    private static PayrollVarianceRowDto Variance(PayrollResult current, PayrollResult? prior) { var employee = current.Employee; var gp = prior?.GrossEarnings; var dp = prior?.TotalDeductions; var np = prior?.NetPay; return new(current.EmployeeId, employee?.EmployeeCode ?? string.Empty, string.Join(' ', new[] { employee?.FirstName, employee?.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), gp, current.GrossEarnings, current.GrossEarnings - (gp ?? 0m), Percent(current.GrossEarnings - (gp ?? 0m), gp), dp, current.TotalDeductions, current.TotalDeductions - (dp ?? 0m), Percent(current.TotalDeductions - (dp ?? 0m), dp), np, current.NetPay, current.NetPay - (np ?? 0m), Percent(current.NetPay - (np ?? 0m), np), prior is null ? "NewValue" : "NormalComparable", []); }
    private static decimal? Percent(decimal delta, decimal? baseValue) => baseValue is decimal value && value != 0m ? decimal.Round(delta / Math.Abs(value) * 100m, 6) : null;
    private static PayrollReconciliationFinding Finding(PayrollReconciliation r, string code, PayrollFindingSeverity severity, PayrollControlAction action, PayrollControlMetric metric, decimal? expected, decimal? actual, string message, Guid? employeeId = null) => new() { Id = Guid.NewGuid(), TenantId = r.TenantId, PayrollReconciliationId = r.Id, ControlCode = code, Scope = PayrollControlScope.PayrollRun, Severity = severity, Action = action, Metric = metric, EmployeeId = employeeId, ExpectedValue = expected, ActualValue = actual, Difference = actual - expected, Message = message, GeneratedAtUtc = r.GeneratedAtUtc };
    private static PayrollAnomalyFlag Anomaly(Guid tenantId, Guid runId, Guid? employeeId, Guid? resultId, PayrollAnomalyType type, PayrollFindingSeverity severity, PayrollControlMetric metric, decimal current, decimal? comparison, string message) => new() { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, PayrollResultId = resultId, AnomalyType = type, Severity = severity, Metric = metric, CurrentValue = current, ComparisonValue = comparison, Difference = comparison.HasValue ? current - comparison.Value : null, Message = message, CreatedAtUtc = DateTime.UtcNow };
    private static PayrollVarianceControl Apply(PayrollVarianceControl row, PayrollAnalyticsControlRequest r) { row.Code = r.Code.Trim(); row.Name = r.Name.Trim(); row.Scope = r.Scope; row.Metric = r.Metric; row.AbsoluteThreshold = r.AbsoluteThreshold; row.PercentageThreshold = r.PercentageThreshold; row.Severity = r.Severity; row.Action = r.Action; row.AppliesToRunType = r.AppliesToRunType; row.IsActive = r.IsActive; row.EffectiveFrom = r.EffectiveFrom; row.EffectiveTo = r.EffectiveTo; return row; }
    private static PayrollAnalyticsControlDto ToDto(PayrollVarianceControl x) => new(x.Id, x.Code, x.Name, x.Scope, x.Metric, x.AbsoluteThreshold, x.PercentageThreshold, x.Severity, x.Action, x.AppliesToRunType, x.IsActive, x.EffectiveFrom, x.EffectiveTo, x.ConcurrencyVersion);
    private static PayrollReconciliationDto ToDto(PayrollReconciliation x) => new(x.Id, x.PayrollRunId, x.ReconciliationType, x.Version, x.Status, x.TotalChecks, x.PassedChecks, x.WarningChecks, x.FailedChecks, x.CriticalChecks, x.GeneratedAtUtc, x.Findings.Select(f => new PayrollFindingDto(f.Id, f.ControlCode, f.Scope, f.Severity, f.Action, f.Metric, f.EmployeeId, f.ExpectedValue, f.ActualValue, f.Difference, f.VariancePercent, f.Message, f.Status, f.GeneratedAtUtc, f.ResolutionNote, f.ResolutionReference)).ToList());
    private static PayrollFindingDto ToDto(PayrollReconciliationFinding f) => new(f.Id, f.ControlCode, f.Scope, f.Severity, f.Action, f.Metric, f.EmployeeId, f.ExpectedValue, f.ActualValue, f.Difference, f.VariancePercent, f.Message, f.Status, f.GeneratedAtUtc, f.ResolutionNote, f.ResolutionReference);
    private static string? F(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
    private sealed class ExportQuery : PagedQuery { public ExportQuery() { PageSize = 50000; } }
}
