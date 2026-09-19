using System.Globalization;
using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollOutputService(IHrmsDbContext db, ITenantContext tenant, IEmployeeIdentityResolver identity, TimeProvider clock) : IPayrollOutputService
{
    public async Task<Result<IReadOnlyList<PayslipDto>>> GenerateAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<PayslipDto>>.Unauthorized("No authenticated tenant.");
        var run = await db.PayrollRuns.Include(x => x.PayrollPeriod).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == payrollRunId, ct);
        if (run is null) return Result<IReadOnlyList<PayslipDto>>.NotFound("Payroll run not found.");
        if (run.Status is not PayrollRunStatus.Calculated and not PayrollRunStatus.Approved and not PayrollRunStatus.Finalized) return Result<IReadOnlyList<PayslipDto>>.Conflict("Payslips can only be generated from a calculated, approved, or finalized payroll run.");
        var results = await db.PayrollResults.AsNoTracking().Include(x => x.Employee).ThenInclude(x => x!.Department).Include(x => x.Employee).ThenInclude(x => x!.Designation).Include(x => x.Components).Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.IsCurrent && x.Status == PayrollResultStatus.Calculated).ToListAsync(ct);
        var existing = await db.Payslips.Include(x => x.Lines).Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).ToListAsync(ct);
        var statutory = await db.PayrollStatutoryResults.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).ToListAsync(ct);
        var output = new List<Payslip>();
        foreach (var result in results)
        {
            var current = existing.Where(x => x.PayrollResultId == result.Id).OrderByDescending(x => x.Version).FirstOrDefault();
            if (current?.Status == PayslipStatus.Published) continue;
            if (current is not null)
            {
                if (current.Status == PayslipStatus.Generated)
                {
                    await db.Payslips.Where(x => x.TenantId == tenantId && x.Id == current.Id && x.Status == PayslipStatus.Generated).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, PayslipStatus.Superseded), ct);
                    db.ClearChangeTracker();
                    db.PayslipHistories.Add(History(current, PayslipHistoryChangeType.Superseded, "Replaced before publication."));
                }
            }
            var version = (existing.Where(x => x.PayrollResultId == result.Id).Select(x => x.Version).DefaultIfEmpty(0).Max()) + 1;
            var employee = result.Employee;
            var slip = new Payslip { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = run.Id, PayrollResultId = result.Id, PayrollRunEmployeeId = result.PayrollRunEmployeeId, EmployeeId = result.EmployeeId, PayslipNumber = $"PS/{run.PayrollPeriod!.EndDate:yyyyMM}/{result.EmployeeId:N}/{version}", PeriodStartDate = result.PeriodStartDate, PeriodEndDate = result.PeriodEndDate, PayDate = run.PayrollPeriod.PayDate, CurrencyCode = result.CurrencyCode, GrossEarnings = result.GrossEarnings, TotalDeductions = result.TotalDeductions, NetPay = result.NetPay, EmployerContributionTotal = result.EmployerContributions, GeneratedAtUtc = clock.GetUtcNow().UtcDateTime, GeneratedByUserId = tenant.UserId, EmployeeCode = employee?.EmployeeCode ?? string.Empty, EmployeeName = string.Join(' ', new[] { employee?.FirstName, employee?.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), Designation = employee?.Designation?.Name, Department = employee?.Department?.Name, WorkLocation = employee?.PayrollLocation, DateOfJoining = employee?.DateOfJoining, SalaryStructureReference = result.SalaryStructureId.ToString(), Version = version, Lines = result.Components.OrderBy(x => x.CalculationSequence).Select(x => new PayslipLine { Id = Guid.NewGuid(), TenantId = tenantId, ComponentCode = x.ComponentCode, ComponentName = x.ComponentName, ComponentType = x.ComponentType.ToString(), DisplayGroup = x.IsEarning ? "Earnings" : x.IsDeduction ? "Deductions" : "Employer Contributions", Amount = x.CalculatedAmount, Sequence = x.CalculationSequence, Source = x.CalculationSource, IsStatutory = false, EmployerAmount = x.IsEmployerContribution ? x.CalculatedAmount : null }).ToList() };
            foreach (var statutoryResult in statutory.Where(x => x.PayrollResultId == result.Id)) slip.Lines.Add(new PayslipLine { Id = Guid.NewGuid(), TenantId = tenantId, ComponentCode = statutoryResult.StatutoryType.ToString(), ComponentName = statutoryResult.StatutoryType.ToString(), ComponentType = "Statutory", DisplayGroup = "Statutory Deductions", Amount = statutoryResult.EmployeeAmount, Sequence = 10000 + slip.Lines.Count, Source = "StatutoryConfiguration", IsStatutory = true, EmployerAmount = statutoryResult.EmployerAmount });
            slip.History.Add(History(slip, PayslipHistoryChangeType.Generated, "Payslip snapshot generated from the current payroll result.")); db.Payslips.Add(slip); output.Add(slip);
        }
        await db.SaveChangesAsync(ct); db.ClearChangeTracker();
        return Result<IReadOnlyList<PayslipDto>>.Success(output.Select(ToDto).ToList(), "Payslip snapshots generated.");
    }

    public async Task<Result<IReadOnlyList<PayslipDto>>> PublishAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<PayslipDto>>.Unauthorized("No authenticated tenant.");
        var run = await db.PayrollRuns.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == payrollRunId, ct); if (run is null) return Result<IReadOnlyList<PayslipDto>>.NotFound("Payroll run not found.");
        if (run.Status is not PayrollRunStatus.Approved and not PayrollRunStatus.Finalized) return Result<IReadOnlyList<PayslipDto>>.Conflict("Payslips can only be published for an approved or finalized payroll run.");
        await db.Payslips.Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.Status == PayslipStatus.Generated).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, PayslipStatus.Published).SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), ct);
        db.ClearChangeTracker();
        var rows = await db.Payslips.Include(x => x.Lines).Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.Status == PayslipStatus.Published).ToListAsync(ct);
        db.PayslipHistories.AddRange(rows.Select(row => History(row, PayslipHistoryChangeType.Published, "Payslip published.")));
        await db.SaveChangesAsync(ct); return Result<IReadOnlyList<PayslipDto>>.Success(rows.Select(ToDto).ToList(), "Payslips published.");
    }

    public async Task<Result<PagedResult<PayslipDto>>> GetRunPayslipsAsync(Guid payrollRunId, PayrollOutputQuery query, CancellationToken ct = default) => await GetPagedAsync(db.Payslips.AsNoTracking().Include(x => x.Lines).Where(x => x.TenantId == tenant.TenantId && x.PayrollRunId == payrollRunId), query, false, ct);
    public async Task<Result<PayslipDto>> GetAsync(Guid payslipId, bool publishedOnly = false, CancellationToken ct = default) { var row = await db.Payslips.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == payslipId && (!publishedOnly || x.Status == PayslipStatus.Published), ct); return row is null ? Result<PayslipDto>.NotFound("Payslip not found.") : Result<PayslipDto>.Success(ToDto(row)); }
    public async Task<Result<string>> GetDocumentAsync(Guid payslipId, bool publishedOnly = false, CancellationToken ct = default) { var slip = await GetAsync(payslipId, publishedOnly, ct); return !slip.Succeeded ? Result<string>.Failure(slip.Status, slip.Message) : Result<string>.Success(RenderHtml(slip.Value!)); }
    public async Task<Result<PagedResult<PayslipDto>>> GetOwnAsync(PayrollOutputQuery query, CancellationToken ct = default) { var who = await identity.ResolveCurrentAsync(ct); if (!who.Succeeded) return Result<PagedResult<PayslipDto>>.Failure(who.Status, who.Message); return await GetPagedAsync(db.Payslips.AsNoTracking().Include(x => x.Lines).Where(x => x.TenantId == who.Value!.TenantId && x.EmployeeId == who.Value.EmployeeId && x.Status == PayslipStatus.Published), query, true, ct); }

    public async Task<Result<PagedResult<PayrollRegisterRowDto>>> GetRegisterAsync(Guid payrollRunId, PayrollOutputQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<PayrollRegisterRowDto>>.Unauthorized("No authenticated tenant.");
        var source = db.PayrollResults.AsNoTracking().Include(x => x.Employee).ThenInclude(x => x!.Department).Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.IsCurrent); if (!string.IsNullOrWhiteSpace(query.Search)) source = source.Where(x => (x.Employee!.EmployeeCode ?? "").Contains(query.Search) || x.Employee.FirstName.Contains(query.Search) || x.Employee.LastName.Contains(query.Search)); var total = await source.CountAsync(ct); var rows = await source.OrderBy(x => x.Employee!.EmployeeCode).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct); return Result<PagedResult<PayrollRegisterRowDto>>.Success(new PagedResult<PayrollRegisterRowDto>(rows.Select(x => new PayrollRegisterRowDto(x.EmployeeId, x.Employee?.EmployeeCode ?? "", string.Join(' ', new[] { x.Employee?.FirstName, x.Employee?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))), x.Employee?.Department?.Name, x.Employee?.PayrollLocation, x.GrossEarnings, x.TotalDeductions, x.EmployerContributions, x.NetPay, x.CurrencyCode, x.Status)).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<PayrollOutputFile>> ExportRegisterAsync(Guid payrollRunId, PayrollOutputQuery query, CancellationToken ct = default)
    {
        query.Page = 1; query.PageSize = 50000; var result = await GetRegisterAsync(payrollRunId, query, ct); if (!result.Succeeded) return Result<PayrollOutputFile>.Failure(result.Status, result.Message); var sb = new StringBuilder("EmployeeCode,EmployeeName,Department,WorkLocation,GrossEarnings,TotalDeductions,EmployerContributions,NetPay,Currency,Status\r\n"); foreach (var row in result.Value!.Items) sb.Append(string.Join(',', Csv(row.EmployeeCode), Csv(row.EmployeeName), Csv(row.Department), Csv(row.WorkLocation), row.GrossEarnings.ToString(CultureInfo.InvariantCulture), row.TotalDeductions.ToString(CultureInfo.InvariantCulture), row.EmployerContributions.ToString(CultureInfo.InvariantCulture), row.NetPay.ToString(CultureInfo.InvariantCulture), Csv(row.CurrencyCode), Csv(row.Status.ToString()))).Append("\r\n"); return Result<PayrollOutputFile>.Success(new PayrollOutputFile($"payroll-register-{payrollRunId:N}.csv", "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private async Task<Result<PagedResult<PayslipDto>>> GetPagedAsync(IQueryable<Payslip> source, PayrollOutputQuery query, bool own, CancellationToken ct) { if (query.Status is { } status) source = source.Where(x => x.Status == status); if (!string.IsNullOrWhiteSpace(query.Search)) source = source.Where(x => x.EmployeeCode.Contains(query.Search) || x.EmployeeName.Contains(query.Search)); var total = await source.CountAsync(ct); var rows = await source.OrderByDescending(x => x.PeriodEndDate).ThenBy(x => x.EmployeeCode).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct); return Result<PagedResult<PayslipDto>>.Success(new PagedResult<PayslipDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total)); }
    private static PayslipHistory History(Payslip slip, PayslipHistoryChangeType type, string message) => new() { Id = Guid.NewGuid(), TenantId = slip.TenantId, PayslipId = slip.Id, ChangeType = type, ChangedAtUtc = DateTime.UtcNow, Message = message };
    private static PayslipDto ToDto(Payslip x) => new(x.Id, x.PayrollRunId, x.PayrollResultId, x.EmployeeId, x.PayslipNumber, x.PeriodStartDate, x.PeriodEndDate, x.PayDate, x.CurrencyCode, x.GrossEarnings, x.TotalDeductions, x.NetPay, x.EmployerContributionTotal, x.EmployeeCode, x.EmployeeName, x.Designation, x.Department, x.WorkLocation, x.DateOfJoining, x.SalaryStructureReference, x.Status, x.Version, x.GeneratedAtUtc, x.Lines.OrderBy(l => l.Sequence).Select(l => new PayslipLineDto(l.Id, l.ComponentCode, l.ComponentName, l.ComponentType, l.DisplayGroup, l.Amount, l.Sequence, l.Source, l.IsStatutory, l.EmployerAmount)).ToList());
    private static string Csv(string? value) { var text = value ?? string.Empty; return text.Contains(',') || text.Contains('"') || text.Contains('\r') || text.Contains('\n') ? $"\"{text.Replace("\"", "\"\"")}\"" : text; }
    private static string RenderHtml(PayslipDto x) => $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{x.PayslipNumber}</title><style>@page{{size:A4;margin:18mm}}body{{font-family:Arial,sans-serif;color:#222}}table{{width:100%;border-collapse:collapse}}th,td{{padding:6px;border-bottom:1px solid #ddd;text-align:left}}.amount{{text-align:right}}h1{{margin-bottom:4px}}.summary{{display:flex;justify-content:space-between}}</style></head><body><h1>Payslip</h1><p>{x.PayslipNumber} | {x.PeriodStartDate:yyyy-MM-dd} to {x.PeriodEndDate:yyyy-MM-dd} | Pay date {x.PayDate:yyyy-MM-dd}</p><p><strong>{x.EmployeeName}</strong> ({x.EmployeeCode})<br>{x.Department ?? ""} {x.Designation ?? ""}</p><table><thead><tr><th>Component</th><th>Group</th><th class=\"amount\">Amount</th></tr></thead><tbody>{string.Join("", x.Lines.Select(l => $"<tr><td>{System.Net.WebUtility.HtmlEncode(l.ComponentName)}</td><td>{System.Net.WebUtility.HtmlEncode(l.DisplayGroup)}</td><td class=\"amount\">{l.Amount.ToString("N2", CultureInfo.InvariantCulture)}</td></tr>"))}</tbody></table><p class=\"summary\"><strong>Gross: {x.GrossEarnings:N2}</strong><strong>Deductions: {x.TotalDeductions:N2}</strong><strong>Net Pay: {x.NetPay:N2} {x.CurrencyCode}</strong></p></body></html>";
}
