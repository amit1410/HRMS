using System.Text;
using System.Globalization;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollStatutoryComplianceService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock) : IPayrollStatutoryComplianceService
{
    public async Task<Result<PayrollCompliancePeriodDto>> CreatePeriodAsync(PayrollCompliancePeriodRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PayrollCompliancePeriodDto>.Unauthorized("No authenticated tenant.");
        if (request.PeriodEnd < request.PeriodStart) return Result<PayrollCompliancePeriodDto>.Invalid("periodEnd", "Period End cannot be earlier than Period Start.");
        var jurisdiction = request.JurisdictionCode.Trim().ToUpperInvariant();
        if (await db.PayrollCompliancePeriods.AnyAsync(x => x.TenantId == tid && x.JurisdictionCode == jurisdiction && x.ComplianceType == request.ComplianceType && x.PeriodStart == request.PeriodStart && x.PeriodEnd == request.PeriodEnd && x.Status != PayrollCompliancePeriodStatus.Cancelled, ct)) return Result<PayrollCompliancePeriodDto>.Conflict("Compliance period already exists.");
        var item = new PayrollCompliancePeriod { Id = Guid.NewGuid(), TenantId = tid, JurisdictionCode = jurisdiction, ComplianceType = request.ComplianceType, PeriodStart = request.PeriodStart, PeriodEnd = request.PeriodEnd, DueDate = request.DueDate };
        db.PayrollCompliancePeriods.Add(item); await db.SaveChangesAsync(ct); return Result<PayrollCompliancePeriodDto>.Success(ToDto(item));
    }

    public async Task<Result<PayrollStatutoryReturnDto>> GenerateAsync(Guid periodId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PayrollStatutoryReturnDto>.Unauthorized("No authenticated tenant.");
        var period = await db.PayrollCompliancePeriods.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == periodId, ct); if (period is null) return Result<PayrollStatutoryReturnDto>.NotFound("Compliance period not found.");
        if (period.Status != PayrollCompliancePeriodStatus.Open) return Result<PayrollStatutoryReturnDto>.Conflict("Compliance period is not open.");
        var existing = await db.PayrollStatutoryReturnBatches.FirstOrDefaultAsync(x => x.TenantId == tid && x.PayrollCompliancePeriodId == periodId && x.Status != PayrollStatutoryReturnStatus.Cancelled, ct); if (existing is not null) return Result<PayrollStatutoryReturnDto>.Conflict("An active return already exists for this period.");
        var statutoryType = ToStatutoryType(period.ComplianceType);
        var runIds = await db.PayrollRuns.Where(x => x.TenantId == tid && (x.Status == PayrollRunStatus.Approved || x.Status == PayrollRunStatus.Finalized) && x.PayrollPeriod!.StartDate <= period.PeriodEnd && x.PayrollPeriod.EndDate >= period.PeriodStart).Select(x => x.Id).ToListAsync(ct);
        var sources = await db.PayrollStatutoryResults.AsNoTracking().Where(x => x.TenantId == tid && x.StatutoryType == statutoryType && x.JurisdictionCode == period.JurisdictionCode && runIds.Contains(x.PayrollRunId)).ToListAsync(ct);
        if (sources.Count == 0) return Result<PayrollStatutoryReturnDto>.Conflict("MissingStatutorySource: no persisted statutory results match this period.");
        var employeeIds = sources.Select(x => x.EmployeeId).Distinct().ToList(); var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == tid && employeeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var batch = new PayrollStatutoryReturnBatch { Id = Guid.NewGuid(), TenantId = tid, PayrollCompliancePeriodId = periodId, ComplianceType = period.ComplianceType, JurisdictionCode = period.JurisdictionCode, BatchNumber = $"STAT/{period.ComplianceType}/{period.PeriodEnd:yyyyMM}/{Guid.NewGuid():N}"[..36], Status = PayrollStatutoryReturnStatus.Generated, GeneratedAtUtc = clock.GetUtcNow().UtcDateTime, GeneratedByUserId = tenant.UserId };
        var sequence = 1;
        foreach (var group in sources.GroupBy(x => x.EmployeeId))
        {
            if (!employees.TryGetValue(group.Key, out var employee)) continue;
            var row = new PayrollStatutoryReturnEmployee { Id = Guid.NewGuid(), TenantId = tid, PayrollStatutoryReturnBatchId = batch.Id, EmployeeId = group.Key, EmployeeCodeSnapshot = employee.EmployeeCode ?? string.Empty, EmployeeNameSnapshot = string.Join(' ', new[] { employee.FirstName, employee.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), Uan = employee.UanNumber, EsicNumber = employee.EsicNumber, Pan = employee.PanNumber, GrossWages = group.Sum(x => x.CalculationBasis), StatutoryWages = group.Sum(x => x.CalculationBasis), EmployeeContribution = group.Sum(x => x.EmployeeAmount), EmployerContribution = group.Sum(x => x.EmployerAmount), DeductionAmount = group.Sum(x => x.EmployeeAmount), PayableAmount = group.Sum(x => x.TotalAmount), Sequence = sequence++ };
            batch.Employees.Add(row); foreach (var source in group) { batch.Sources.Add(new PayrollStatutoryReturnSource { Id = Guid.NewGuid(), TenantId = tid, PayrollStatutoryReturnBatchId = batch.Id, PayrollStatutoryReturnEmployeeId = row.Id, PayrollRunId = source.PayrollRunId, PayrollResultId = source.PayrollResultId, PayrollStatutoryResultId = source.Id, Amount = source.TotalAmount, SourceType = source.StatutoryType.ToString() }); }
        }
        Reconcile(batch); db.PayrollStatutoryReturnBatches.Add(batch); AddHistory(batch, PayrollStatutoryComplianceHistoryChangeType.Generated, "Return generated from persisted statutory results."); await db.SaveChangesAsync(ct); return Result<PayrollStatutoryReturnDto>.Success(ToDto(batch));
    }

    public async Task<Result<PayrollStatutoryReturnDto>> ValidateAsync(Guid batchId, CancellationToken ct = default)
    { var batch = await LoadAsync(batchId, ct); if (batch is null) return Result<PayrollStatutoryReturnDto>.NotFound("Statutory return not found."); if (batch.Status is not PayrollStatutoryReturnStatus.Generated and not PayrollStatutoryReturnStatus.Validated) return Result<PayrollStatutoryReturnDto>.Conflict("Return cannot be validated in its current state."); Reconcile(batch); if (batch.Employees.Any(x => x.ValidationStatus == PayrollComplianceValidationStatus.Invalid)) return Result<PayrollStatutoryReturnDto>.Conflict("Return contains validation errors."); batch.Status = PayrollStatutoryReturnStatus.Validated; batch.ValidatedAtUtc = clock.GetUtcNow().UtcDateTime; batch.ValidatedByUserId = tenant.UserId; AddHistory(batch, PayrollStatutoryComplianceHistoryChangeType.Validated, "Return totals reconciled."); await db.SaveChangesAsync(ct); return Result<PayrollStatutoryReturnDto>.Success(ToDto(batch)); }
    public async Task<Result<PayrollStatutoryReturnDto>> ApproveAsync(Guid batchId, CancellationToken ct = default) => await TransitionAsync(batchId, PayrollStatutoryReturnStatus.Approved, PayrollStatutoryReturnStatus.Validated, PayrollStatutoryComplianceHistoryChangeType.Approved, ct);
    public async Task<Result<PayrollStatutoryReturnDto>> MarkFiledAsync(Guid batchId, string? externalReference, CancellationToken ct = default)
    { var batch = await LoadAsync(batchId, ct); if (batch is null) return Result<PayrollStatutoryReturnDto>.NotFound("Statutory return not found."); if (batch.Status is not PayrollStatutoryReturnStatus.Approved and not PayrollStatutoryReturnStatus.Exported) return Result<PayrollStatutoryReturnDto>.Conflict("Only approved or exported returns can be marked filed."); batch.Status = PayrollStatutoryReturnStatus.Filed; batch.ExternalReference = externalReference?.Trim(); batch.FiledAtUtc = clock.GetUtcNow().UtcDateTime; batch.FiledByUserId = tenant.UserId; AddHistory(batch, PayrollStatutoryComplianceHistoryChangeType.Filed, "Filed status recorded manually; no portal submission was performed."); await db.SaveChangesAsync(ct); return Result<PayrollStatutoryReturnDto>.Success(ToDto(batch)); }
    public async Task<Result<PayrollStatutoryReturnDto>> GetAsync(Guid batchId, CancellationToken ct = default) { var batch = await LoadAsync(batchId, ct); return batch is null ? Result<PayrollStatutoryReturnDto>.NotFound("Statutory return not found.") : Result<PayrollStatutoryReturnDto>.Success(ToDto(batch)); }
    public async Task<Result<PayrollOutputFile>> ExportAsync(Guid batchId, CancellationToken ct = default)
    {
        var batch = await LoadAsync(batchId, ct); if (batch is null) return Result<PayrollOutputFile>.NotFound("Statutory return not found.");
        if (batch.Status is not PayrollStatutoryReturnStatus.Approved and not PayrollStatutoryReturnStatus.Exported and not PayrollStatutoryReturnStatus.Filed) return Result<PayrollOutputFile>.Conflict("Only approved statutory returns can be exported.");
        var sb = new StringBuilder("EmployeeCode,EmployeeName,Identifier,GrossWages,StatutoryWages,EmployeeContribution,EmployerContribution,TotalDeduction,PayableAmount\r\n");
        foreach (var row in batch.Employees.OrderBy(x => x.Sequence))
        {
            var identifier = batch.ComplianceType == PayrollComplianceType.ProvidentFund ? row.Uan : batch.ComplianceType == PayrollComplianceType.Esi ? row.EsicNumber : batch.ComplianceType == PayrollComplianceType.IncomeTaxTds ? row.Pan : row.PtRegistrationReference;
            sb.Append(string.Join(',', Csv(row.EmployeeCodeSnapshot), Csv(row.EmployeeNameSnapshot), Csv(identifier), row.GrossWages.ToString("0.00", CultureInfo.InvariantCulture), row.StatutoryWages.ToString("0.00", CultureInfo.InvariantCulture), row.EmployeeContribution.ToString("0.00", CultureInfo.InvariantCulture), row.EmployerContribution.ToString("0.00", CultureInfo.InvariantCulture), row.DeductionAmount.ToString("0.00", CultureInfo.InvariantCulture), row.PayableAmount.ToString("0.00", CultureInfo.InvariantCulture))).Append("\r\n");
        }
        if (batch.Status == PayrollStatutoryReturnStatus.Approved) { batch.Status = PayrollStatutoryReturnStatus.Exported; batch.ExportedAtUtc = clock.GetUtcNow().UtcDateTime; batch.ExportedByUserId = tenant.UserId; AddHistory(batch, PayrollStatutoryComplianceHistoryChangeType.Exported, "Generic statutory CSV exported; not an official government upload file."); await db.SaveChangesAsync(ct); }
        return Result<PayrollOutputFile>.Success(new PayrollOutputFile($"statutory-return-{batch.BatchNumber.Replace('/', '-')}.csv", "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private async Task<Result<PayrollStatutoryReturnDto>> TransitionAsync(Guid id, PayrollStatutoryReturnStatus next, PayrollStatutoryReturnStatus required, PayrollStatutoryComplianceHistoryChangeType change, CancellationToken ct) { var batch = await LoadAsync(id, ct); if (batch is null) return Result<PayrollStatutoryReturnDto>.NotFound("Statutory return not found."); if (batch.Status != required) return Result<PayrollStatutoryReturnDto>.Conflict($"Return must be {required}."); batch.Status = next; batch.ApprovedAtUtc = clock.GetUtcNow().UtcDateTime; batch.ApprovedByUserId = tenant.UserId; AddHistory(batch, change, "Return approved after validation."); await db.SaveChangesAsync(ct); return Result<PayrollStatutoryReturnDto>.Success(ToDto(batch)); }
    private async Task<PayrollStatutoryReturnBatch?> LoadAsync(Guid id, CancellationToken ct) => await db.PayrollStatutoryReturnBatches.Include(x => x.Employees).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct);
    private void AddHistory(PayrollStatutoryReturnBatch batch, PayrollStatutoryComplianceHistoryChangeType change, string message) => db.PayrollStatutoryComplianceHistories.Add(new PayrollStatutoryComplianceHistory { Id = Guid.NewGuid(), TenantId = batch.TenantId, PayrollStatutoryReturnBatchId = batch.Id, ChangeType = change, ChangedAtUtc = clock.GetUtcNow().UtcDateTime, ActorUserId = tenant.UserId, Message = message });
    private static void Reconcile(PayrollStatutoryReturnBatch batch) { batch.EmployeeCount = batch.Employees.Count; batch.GrossRelevantWages = Round(batch.Employees.Sum(x => x.GrossWages)); batch.EmployeeContribution = Round(batch.Employees.Sum(x => x.EmployeeContribution)); batch.EmployerContribution = Round(batch.Employees.Sum(x => x.EmployerContribution)); batch.TotalDeduction = Round(batch.Employees.Sum(x => x.DeductionAmount)); batch.TotalPayable = Round(batch.Employees.Sum(x => x.PayableAmount)); }
    private static PayrollCompliancePeriodDto ToDto(PayrollCompliancePeriod x) => new(x.Id, x.JurisdictionCode, x.ComplianceType, x.PeriodStart, x.PeriodEnd, x.DueDate, x.Status);
    private static PayrollStatutoryReturnDto ToDto(PayrollStatutoryReturnBatch x) => new(x.Id, x.PayrollCompliancePeriodId, x.ComplianceType, x.JurisdictionCode, x.BatchNumber, x.Status, x.EmployeeCount, x.GrossRelevantWages, x.EmployeeContribution, x.EmployerContribution, x.TotalDeduction, x.TotalPayable, x.Employees.OrderBy(e => e.Sequence).Select(e => new PayrollStatutoryReturnEmployeeDto(e.Id, e.EmployeeId, e.EmployeeCodeSnapshot, e.EmployeeNameSnapshot, e.Uan, e.EsicNumber, e.Pan, e.GrossWages, e.StatutoryWages, e.EmployeeContribution, e.EmployerContribution, e.DeductionAmount, e.PayableAmount, e.ValidationStatus, e.ValidationMessage, e.Sequence)).ToList());
    private static StatutoryType ToStatutoryType(PayrollComplianceType type) => type switch { PayrollComplianceType.ProvidentFund => StatutoryType.ProvidentFund, PayrollComplianceType.Esi => StatutoryType.Esi, PayrollComplianceType.ProfessionalTax => StatutoryType.ProfessionalTax, _ => StatutoryType.IncomeTax };
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Csv(string? value) { var text = value ?? string.Empty; return text.Contains(',', StringComparison.Ordinal) || text.Contains('"', StringComparison.Ordinal) || text.Contains('\n') || text.Contains('\r') ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text; }
}
