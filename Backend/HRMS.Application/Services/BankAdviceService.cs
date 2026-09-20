using System.Globalization;
using System.Text;
using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class BankAdviceService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock) : IBankAdviceService
{
    public async Task<Result<BankAdviceBatchDto>> GenerateAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<BankAdviceBatchDto>.Unauthorized("No authenticated tenant.");
        var run = await db.PayrollRuns.Include(x => x.PayrollPeriod).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == payrollRunId, ct);
        if (run?.PayrollPeriod is null) return Result<BankAdviceBatchDto>.NotFound("Payroll run not found.");
        if (run.Status is not PayrollRunStatus.Approved and not PayrollRunStatus.Finalized) return Result<BankAdviceBatchDto>.Conflict("Bank advice can only be generated from an approved or finalized payroll run.");
        if (await db.BankAdviceBatches.AnyAsync(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.Status != BankAdviceStatus.Cancelled, ct)) return Result<BankAdviceBatchDto>.Conflict("An active bank advice batch already exists for this payroll run.");

        var results = await db.PayrollResults.AsNoTracking().Include(x => x.Employee).Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.IsCurrent && x.Status == PayrollResultStatus.Calculated).OrderBy(x => x.Employee!.EmployeeCode).ToListAsync(ct);
        if (results.Count == 0) return Result<BankAdviceBatchDto>.Conflict("No calculated payroll results are available for this run.");
        var currencies = results.Select(x => x.CurrencyCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (currencies.Count != 1) return Result<BankAdviceBatchDto>.Conflict("A bank advice batch cannot contain mixed currencies.");
        var version = await db.BankAdviceBatches.Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId).Select(x => (int?)x.Version).MaxAsync(ct) ?? 0;
        var batch = new BankAdviceBatch { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = payrollRunId, PayrollPeriodId = run.PayrollPeriodId, BatchNumber = $"BA/{run.PayrollPeriod.PayDate:yyyyMM}/{run.Id:N}/{version + 1}", BatchDate = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), PayDate = run.PayrollPeriod.PayDate, CurrencyCode = currencies[0], Version = version + 1, GeneratedAtUtc = clock.GetUtcNow().UtcDateTime, GeneratedByUserId = tenant.UserId };
        var employeeIds = results.Select(x => x.EmployeeId).ToList();
        var accounts = await db.EmployeeBankDetails.AsNoTracking().Include(x => x.Bank).Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId) && x.AccountPurpose == AccountPurpose.Salary && x.IsActive && x.Status == BankAccountStatus.Active && (x.EffectiveFrom == null || x.EffectiveFrom <= batch.PayDate)).ToListAsync(ct);
        var sequence = 0;
        foreach (var result in results)
        {
            sequence++;
            var accountRows = accounts.Where(x => x.EmployeeId == result.EmployeeId).OrderByDescending(x => x.EffectiveFrom).ToList();
            var account = accountRows.Count == 1 ? accountRows[0] : null;
            var validation = ValidatePayment(result, account, accountRows.Count);
            var payment = new BankAdvicePayment { Id = Guid.NewGuid(), TenantId = tenantId, BankAdviceBatchId = batch.Id, PayrollResultId = result.Id, EmployeeId = result.EmployeeId, EmployeeCode = result.Employee?.EmployeeCode ?? string.Empty, EmployeeName = string.Join(' ', new[] { result.Employee?.FirstName, result.Employee?.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), NetPay = result.NetPay, CurrencyCode = result.CurrencyCode, PaymentStatus = validation.Status == BankAdviceValidationStatus.Valid ? BankAdvicePaymentStatus.Ready : BankAdvicePaymentStatus.Failed, ValidationStatus = validation.Status, ValidationMessage = validation.Message, Sequence = sequence, PaymentReference = $"{batch.BatchNumber}/{sequence:0000}", AccountHolderName = account?.AccountHolderName ?? string.Empty, BankName = account?.Bank?.Name ?? string.Empty, MaskedAccountNumber = account is null ? string.Empty : MaskAccount(account.AccountNumber), IfscCode = account?.IfscCode, BranchName = account?.BranchName };
            batch.Payments.Add(payment);
        }
        Recalculate(batch);
        batch.History.Add(History(batch, BankAdviceHistoryChangeType.Generated, "Bank advice generated from persisted payroll results."));
        db.BankAdviceBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return Result<BankAdviceBatchDto>.Success(ToDto(batch), "Bank advice generated.");
    }

    public Task<Result<BankAdviceBatchDto>> ValidateAsync(Guid batchId, CancellationToken ct = default) => SetPreparedAsync(batchId, false, ct);
    public Task<Result<BankAdviceBatchDto>> PrepareAsync(Guid batchId, CancellationToken ct = default) => SetPreparedAsync(batchId, true, ct);

    public async Task<Result<BankAdviceBatchDto>> ApproveAsync(Guid batchId, CancellationToken ct = default)
    {
        var result = await Load(batchId, ct); if (!result.Succeeded) return Result<BankAdviceBatchDto>.Failure(result.Status, result.Message);
        var batch = result.Value!;
        if (batch.Status != BankAdviceStatus.Prepared) return Result<BankAdviceBatchDto>.Conflict("Only a prepared bank advice batch can be approved.");
        if (batch.Payments.Any(x => x.ValidationStatus != BankAdviceValidationStatus.Valid)) return Result<BankAdviceBatchDto>.Conflict("All payment instructions must pass validation before approval.");
        Recalculate(batch); if (batch.TotalAmount != batch.Payments.Sum(x => x.NetPay)) return Result<BankAdviceBatchDto>.Conflict("Bank advice total does not reconcile with its payment instructions.");
        var approvedAt = clock.GetUtcNow().UtcDateTime; var changed = await db.BankAdviceBatches.Where(x => x.TenantId == batch.TenantId && x.Id == batch.Id && x.Status == BankAdviceStatus.Prepared && x.ConcurrencyVersion == batch.ConcurrencyVersion).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, BankAdviceStatus.Approved).SetProperty(x => x.ApprovedAtUtc, approvedAt).SetProperty(x => x.ApprovedByUserId, tenant.UserId).SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), ct);
        if (changed != 1) return Result<BankAdviceBatchDto>.Conflict("Bank advice was changed by another operation.");
        db.ClearChangeTracker(); var refreshed = await Load(batchId, ct); if (!refreshed.Succeeded) return Result<BankAdviceBatchDto>.Failure(refreshed.Status, refreshed.Message); batch = refreshed.Value!; db.BankAdviceHistories.Add(History(batch, BankAdviceHistoryChangeType.Approved, "Bank advice approved.")); await db.SaveChangesAsync(ct); return Result<BankAdviceBatchDto>.Success(ToDto(batch), "Bank advice approved.");
    }

    public async Task<Result<BankAdviceBatchDto>> CancelAsync(Guid batchId, CancellationToken ct = default)
    {
        var result = await Load(batchId, ct); if (!result.Succeeded) return Result<BankAdviceBatchDto>.Failure(result.Status, result.Message);
        var batch = result.Value!; if (batch.Status is BankAdviceStatus.Exported or BankAdviceStatus.Cancelled) return Result<BankAdviceBatchDto>.Conflict("This bank advice batch cannot be cancelled.");
        var cancelledAt = clock.GetUtcNow().UtcDateTime; var changed = await db.BankAdviceBatches.Where(x => x.TenantId == batch.TenantId && x.Id == batch.Id && x.Status != BankAdviceStatus.Exported && x.Status != BankAdviceStatus.Cancelled && x.ConcurrencyVersion == batch.ConcurrencyVersion).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, BankAdviceStatus.Cancelled).SetProperty(x => x.CancelledAtUtc, cancelledAt).SetProperty(x => x.CancelledByUserId, tenant.UserId).SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), ct);
        if (changed != 1) return Result<BankAdviceBatchDto>.Conflict("Bank advice was changed by another operation.");
        db.ClearChangeTracker(); var refreshed = await Load(batchId, ct); if (!refreshed.Succeeded) return Result<BankAdviceBatchDto>.Failure(refreshed.Status, refreshed.Message); batch = refreshed.Value!; db.BankAdviceHistories.Add(History(batch, BankAdviceHistoryChangeType.Cancelled, "Bank advice cancelled.")); await db.SaveChangesAsync(ct); return Result<BankAdviceBatchDto>.Success(ToDto(batch), "Bank advice cancelled.");
    }

    public async Task<Result<PagedResult<BankAdviceBatchDto>>> GetAsync(BankAdviceQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<BankAdviceBatchDto>>.Unauthorized("No authenticated tenant.");
        var source = db.BankAdviceBatches.AsNoTracking().Include(x => x.Payments).Include(x => x.History).Where(x => x.TenantId == tenantId);
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        var total = await source.CountAsync(ct); var rows = await source.OrderByDescending(x => x.CreatedDate).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<BankAdviceBatchDto>>.Success(new PagedResult<BankAdviceBatchDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<BankAdviceBatchDto>> GetByIdAsync(Guid batchId, CancellationToken ct = default)
    {
        var result = await Load(batchId, ct);
        return result.Succeeded ? Result<BankAdviceBatchDto>.Success(ToDto(result.Value!)) : Result<BankAdviceBatchDto>.Failure(result.Status, result.Message);
    }

    public async Task<Result<PayrollOutputFile>> ExportAsync(Guid batchId, CancellationToken ct = default)
    {
        var result = await Load(batchId, ct); if (!result.Succeeded) return Result<PayrollOutputFile>.Failure(result.Status, result.Message);
        var batch = result.Value!; if (batch.Status is not BankAdviceStatus.Approved and not BankAdviceStatus.Exported) return Result<PayrollOutputFile>.Conflict("Only an approved bank advice batch can be exported.");
        var sb = new StringBuilder("Sequence,EmployeeCode,EmployeeName,AccountHolder,BankName,AccountNumber,IFSC,NetPay,Currency,PaymentReference\r\n");
        foreach (var p in batch.Payments.OrderBy(x => x.Sequence)) sb.Append(string.Join(',', p.Sequence, Csv(p.EmployeeCode), Csv(p.EmployeeName), Csv(p.AccountHolderName), Csv(p.BankName), Csv(p.MaskedAccountNumber), Csv(p.IfscCode), p.NetPay.ToString(CultureInfo.InvariantCulture), Csv(p.CurrencyCode), Csv(p.PaymentReference))).Append("\r\n");
        if (batch.Status == BankAdviceStatus.Approved) { var exportedAt = clock.GetUtcNow().UtcDateTime; var changed = await db.BankAdviceBatches.Where(x => x.TenantId == batch.TenantId && x.Id == batch.Id && x.Status == BankAdviceStatus.Approved && x.ConcurrencyVersion == batch.ConcurrencyVersion).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, BankAdviceStatus.Exported).SetProperty(x => x.ExportedAtUtc, exportedAt).SetProperty(x => x.ExportedByUserId, tenant.UserId).SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), ct); if (changed != 1) return Result<PayrollOutputFile>.Conflict("Bank advice was changed by another operation."); await db.BankAdvicePayments.Where(x => x.TenantId == batch.TenantId && x.BankAdviceBatchId == batch.Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PaymentStatus, BankAdvicePaymentStatus.Exported), ct); db.ClearChangeTracker(); var refreshed = await Load(batchId, ct); if (!refreshed.Succeeded) return Result<PayrollOutputFile>.Failure(refreshed.Status, refreshed.Message); db.BankAdviceHistories.Add(History(refreshed.Value!, BankAdviceHistoryChangeType.Exported, "Bank advice CSV exported.")); await db.SaveChangesAsync(ct); }
        return Result<PayrollOutputFile>.Success(new PayrollOutputFile($"bank-advice-{batch.BatchNumber.Replace('/', '-')}.csv", "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private async Task<Result<BankAdviceBatchDto>> SetPreparedAsync(Guid batchId, bool requireValid, CancellationToken ct)
    {
        var result = await Load(batchId, ct); if (!result.Succeeded) return Result<BankAdviceBatchDto>.Failure(result.Status, result.Message);
        var batch = result.Value!; if (batch.Status != BankAdviceStatus.Draft) return Result<BankAdviceBatchDto>.Conflict("Only a draft bank advice batch can be validated or prepared.");
        Recalculate(batch); var valid = batch.Payments.All(x => x.ValidationStatus == BankAdviceValidationStatus.Valid);
        if (requireValid && !valid) return Result<BankAdviceBatchDto>.Conflict("Bank advice contains payment validation errors.");
        if (valid)
        {
            var changed = await db.BankAdviceBatches.Where(x => x.TenantId == batch.TenantId && x.Id == batch.Id && x.ConcurrencyVersion == batch.ConcurrencyVersion).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, BankAdviceStatus.Prepared).SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), ct);
            if (changed != 1) return Result<BankAdviceBatchDto>.Conflict("Bank advice was changed by another operation.");
            db.ClearChangeTracker(); var refreshed = await Load(batchId, ct); if (!refreshed.Succeeded) return Result<BankAdviceBatchDto>.Failure(refreshed.Status, refreshed.Message); batch = refreshed.Value!; db.BankAdviceHistories.Add(History(batch, BankAdviceHistoryChangeType.Prepared, "Bank advice prepared."));
        }
        else batch.History.Add(History(batch, BankAdviceHistoryChangeType.ValidationFailed, "Bank advice validation found payment errors."));
        await db.SaveChangesAsync(ct); return Result<BankAdviceBatchDto>.Success(ToDto(batch), valid ? "Bank advice prepared." : "Bank advice validation completed with errors.");
    }

    private async Task<Result<BankAdviceBatch>> Load(Guid id, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<BankAdviceBatch>.Unauthorized("No authenticated tenant.");
        var batch = await db.BankAdviceBatches.Include(x => x.Payments).Include(x => x.History).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        return batch is null ? Result<BankAdviceBatch>.NotFound("Bank advice batch not found.") : Result<BankAdviceBatch>.Success(batch);
    }

    private static (BankAdviceValidationStatus Status, string? Message) ValidatePayment(PayrollResult result, EmployeeBankDetail? account, int accountCount)
    {
        if (result.NetPay <= 0) return (BankAdviceValidationStatus.Invalid, "Net pay must be greater than zero.");
        if (accountCount == 0) return (BankAdviceValidationStatus.Invalid, "No active salary bank account is effective for the pay date.");
        if (accountCount > 1) return (BankAdviceValidationStatus.Invalid, "Multiple active salary bank accounts are effective for the pay date.");
        if (string.IsNullOrWhiteSpace(account?.IfscCode)) return (BankAdviceValidationStatus.Invalid, "Bank routing/IFSC code is required.");
        if (string.IsNullOrWhiteSpace(account?.AccountNumber)) return (BankAdviceValidationStatus.Invalid, "Bank account number is required.");
        return (BankAdviceValidationStatus.Valid, null);
    }

    private static void Recalculate(BankAdviceBatch batch) { var valid = batch.Payments.Where(x => x.ValidationStatus == BankAdviceValidationStatus.Valid).ToList(); batch.TotalEmployees = valid.Count; batch.TotalAmount = valid.Sum(x => x.NetPay); }
    private static BankAdviceHistory History(BankAdviceBatch batch, BankAdviceHistoryChangeType type, string message) => new() { Id = Guid.NewGuid(), TenantId = batch.TenantId, BankAdviceBatchId = batch.Id, ChangeType = type, ChangedAtUtc = DateTime.UtcNow, Message = message, SnapshotJson = JsonSerializer.Serialize(new { batch.BatchNumber, batch.Status, batch.TotalEmployees, batch.TotalAmount }) };
    private static string MaskAccount(string value) { var digits = value ?? string.Empty; return digits.Length <= 4 ? digits : new string('X', digits.Length - 4) + digits[^4..]; }
    private static string Csv(string? value) { var text = value ?? string.Empty; return text.Contains(',') || text.Contains('"') || text.Contains('\r') || text.Contains('\n') ? $"\"{text.Replace("\"", "\"\"")}\"" : text; }
    private static BankAdviceBatchDto ToDto(BankAdviceBatch x) => new(x.Id, x.PayrollRunId, x.PayrollPeriodId, x.BatchNumber, x.BatchDate, x.PayDate, x.CurrencyCode, x.Status, x.TotalEmployees, x.TotalAmount, x.GeneratedAtUtc, x.ApprovedAtUtc, x.ExportedAtUtc, x.Payments.OrderBy(p => p.Sequence).Select(p => new BankAdvicePaymentDto(p.Id, p.EmployeeId, p.EmployeeCode, p.EmployeeName, p.NetPay, p.CurrencyCode, p.PaymentStatus, p.ValidationStatus, p.ValidationMessage, p.Sequence, p.PaymentReference, p.AccountHolderName, p.BankName, p.MaskedAccountNumber, p.IfscCode, p.BranchName)).ToList(), x.History.OrderByDescending(h => h.ChangedAtUtc).Select(h => new BankAdviceHistoryDto(h.Id, h.ChangeType, h.ChangedAtUtc, h.Message)).ToList());
}
