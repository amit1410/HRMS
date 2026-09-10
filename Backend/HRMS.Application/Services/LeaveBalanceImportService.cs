using System.Globalization;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveBalanceImportService : ILeaveBalanceImportService
{
    private static readonly string[] RequiredHeaders = ["EmployeeCode", "LeaveTypeCode", "LeavePeriod", "OpeningBalance", "EffectiveDate"];
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ILeaveBalanceTransactionPoster _poster;
    private readonly TimeProvider _clock;

    public LeaveBalanceImportService(IHrmsDbContext db, ITenantContext tenant, ILeaveBalanceTransactionPoster poster, TimeProvider clock)
    { _db = db; _tenant = tenant; _poster = poster; _clock = clock; }

    public async Task<Result<LeaveBalanceImportBatchDto>> ValidateAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId || _tenant.UserId is not Guid userId)
            return Result<LeaveBalanceImportBatchDto>.Unauthorized("An authenticated tenant and uploader are required.");
        if (!string.Equals(Path.GetExtension(fileName), ".csv", StringComparison.OrdinalIgnoreCase))
            return Result<LeaveBalanceImportBatchDto>.Invalid("file", "Only CSV balance-import files are supported.");

        var parsed = await ParseAsync(content, ct);
        if (!parsed.Success) return Result<LeaveBalanceImportBatchDto>.Invalid("file", parsed.Error!);
        if (parsed.Rows.Count == 0) return Result<LeaveBalanceImportBatchDto>.Invalid("file", "The import file contains no data rows.");

        var batch = new LeaveBalanceImportBatch
        {
            Id = Guid.NewGuid(), TenantId = tenantId, FileName = Path.GetFileName(fileName),
            Status = LeaveBalanceImportBatchStatus.Validated, TotalRows = parsed.Rows.Count,
            UploadedByUserId = userId, UploadedAtUtc = _clock.GetUtcNow().UtcDateTime
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in parsed.Rows)
        {
            var row = new LeaveBalanceImportRow
            {
                Id = Guid.NewGuid(), TenantId = tenantId, BatchId = batch.Id, RowNumber = item.RowNumber,
                EmployeeCode = item.EmployeeCode, LeaveTypeCode = item.LeaveTypeCode, LeavePeriod = item.LeavePeriod,
                OpeningBalanceText = item.OpeningBalance, EffectiveDateText = item.EffectiveDate, Remarks = item.Remarks,
                Status = LeaveBalanceImportRowStatus.Valid
            };
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(row.EmployeeCode)) errors.Add("EmployeeCode is required.");
            if (string.IsNullOrWhiteSpace(row.LeaveTypeCode)) errors.Add("LeaveTypeCode is required.");
            if (string.IsNullOrWhiteSpace(row.LeavePeriod)) errors.Add("LeavePeriod is required.");
            if (!decimal.TryParse(row.OpeningBalanceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0 || decimal.Round(quantity, 3) != quantity)
                errors.Add("OpeningBalance must be a positive number with at most three decimal places.");
            else row.OpeningBalance = quantity;
            if (!DateOnly.TryParseExact(row.EffectiveDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effectiveDate))
                errors.Add("EffectiveDate must use yyyy-MM-dd format.");
            else row.EffectiveDate = effectiveDate;

            var key = $"{row.EmployeeCode.Trim()}|{row.LeaveTypeCode.Trim()}|{row.LeavePeriod.Trim()}";
            if (!seen.Add(key)) errors.Add("The EmployeeCode, LeaveTypeCode, and LeavePeriod combination is duplicated in this file.");
            if (errors.Count == 0)
            {
                var employee = await _db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeCode == row.EmployeeCode.Trim(), ct);
                var type = await _db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(x => x.Code == row.LeaveTypeCode.Trim() && x.IsActive, ct);
                var periods = await _db.LeavePeriods.AsNoTracking().Where(x => x.Code == row.LeavePeriod.Trim() && x.IsActive).ToListAsync(ct);
                if (employee is null || employee.Status != EmployeeStatus.Active) errors.Add("EmployeeCode was not found or is inactive in this tenant."); else row.EmployeeId = employee.Id;
                if (type is null) errors.Add("LeaveTypeCode was not found or is inactive in this tenant."); else row.LeaveTypeId = type.Id;
                if (periods.Count != 1) errors.Add(periods.Count == 0 ? "LeavePeriod was not found in this tenant." : "LeavePeriod is ambiguous in this tenant."); else row.LeavePeriodId = periods[0].Id;
                if (row.EffectiveDate is DateOnly date && periods.Count == 1 && (date < periods[0].StartDate || date > periods[0].EndDate)) errors.Add("EffectiveDate must belong to LeavePeriod.");
                if (row.EmployeeId is Guid employeeId && row.LeaveTypeId is Guid typeId && row.LeavePeriodId is Guid periodId)
                {
                    row.IdempotencyKey = Key(tenantId, employeeId, typeId, periodId);
                    if (await _db.LeaveBalanceTransactions.AnyAsync(x => x.IdempotencyKey == row.IdempotencyKey, ct)) errors.Add("An opening balance has already been imported for this employee, LeaveType, and LeavePeriod.");
                }
            }
            if (errors.Count > 0) { row.Status = LeaveBalanceImportRowStatus.Invalid; row.ErrorCode = "ValidationFailed"; row.ErrorMessage = string.Join(" ", errors); batch.InvalidRows++; }
            else { batch.ValidRows++; }
            batch.Rows.Add(row);
        }
        batch.Status = batch.InvalidRows == 0 ? LeaveBalanceImportBatchStatus.Validated : LeaveBalanceImportBatchStatus.Invalid;
        _db.LeaveBalanceImportBatches.Add(batch);
        await _db.SaveChangesAsync(ct);
        return Result<LeaveBalanceImportBatchDto>.Success(ToDto(batch), batch.Status == LeaveBalanceImportBatchStatus.Validated ? "The balance import is valid and ready to commit." : "The balance import contains validation errors.");
    }

    public async Task<Result<LeaveBalanceImportBatchDto>> CommitAsync(Guid batchId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId || _tenant.UserId is not Guid userId) return Result<LeaveBalanceImportBatchDto>.Unauthorized("An authenticated tenant and user are required.");
        var batch = await _db.LeaveBalanceImportBatches.Include(x => x.Rows).SingleOrDefaultAsync(x => x.Id == batchId, ct);
        if (batch is null) return Result<LeaveBalanceImportBatchDto>.NotFound("Import batch not found.");
        if (batch.Status == LeaveBalanceImportBatchStatus.Committed) return Result<LeaveBalanceImportBatchDto>.Success(ToDto(batch), "The import batch was already committed.");
        if (batch.Status != LeaveBalanceImportBatchStatus.Validated || batch.InvalidRows != 0) return Result<LeaveBalanceImportBatchDto>.Conflict("Only a fully valid import batch can be committed.");
        await using var tx = await _db.BeginTransactionAsync(ct);
        try
        {
            foreach (var row in batch.Rows.OrderBy(x => x.RowNumber))
            {
                if (row.EmployeeId is not Guid employeeId || row.LeaveTypeId is not Guid typeId || row.LeavePeriodId is not Guid periodId || row.OpeningBalance is not decimal quantity || row.EffectiveDate is not DateOnly date) throw new InvalidOperationException("The validated batch contains an incomplete row.");
                var result = await _poster.PostCreditAsync(new LeaveBalanceCreditCommand(tenantId, employeeId, typeId, periodId, LeaveBalanceTransactionType.Opening, quantity, date, null, null, LeaveBalanceSourceType.BalanceImport, batch.Id.ToString("D"), LeaveBalanceActorType.User, userId, null, row.IdempotencyKey!, null), ct);
                if (!result.Succeeded) throw new InvalidOperationException(result.Message);
                row.Status = LeaveBalanceImportRowStatus.Imported;
                batch.ImportedRows++;
            }
            batch.Status = LeaveBalanceImportBatchStatus.Committed; batch.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Result<LeaveBalanceImportBatchDto>.Success(ToDto(batch), "Opening balances imported successfully.");
        }
        catch (Exception ex) when (ex is DbUpdateException or DbUpdateConcurrencyException or InvalidOperationException)
        {
            await tx.RollbackAsync(ct); return Result<LeaveBalanceImportBatchDto>.Conflict(ex.Message == "The validated batch contains an incomplete row." ? ex.Message : "The import could not be committed; no opening balances were changed.");
        }
    }

    public async Task<Result<LeaveBalanceImportBatchDto>> GetAsync(Guid batchId, CancellationToken ct = default)
    { var x = await _db.LeaveBalanceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batchId, ct); return x is null ? Result<LeaveBalanceImportBatchDto>.NotFound("Import batch not found.") : Result<LeaveBalanceImportBatchDto>.Success(ToDto(x)); }
    public async Task<Result<IReadOnlyList<LeaveBalanceImportBatchDto>>> HistoryAsync(CancellationToken ct = default)
    { var rows = await _db.LeaveBalanceImportBatches.AsNoTracking().OrderByDescending(x => x.UploadedAtUtc).Take(100).ToListAsync(ct); return Result<IReadOnlyList<LeaveBalanceImportBatchDto>>.Success(rows.Select(ToDto).ToList()); }
    public async Task<Result<IReadOnlyList<LeaveBalanceImportRowDto>>> ErrorsAsync(Guid batchId, CancellationToken ct = default)
    { return Result<IReadOnlyList<LeaveBalanceImportRowDto>>.Success(await _db.LeaveBalanceImportRows.AsNoTracking().Where(x => x.BatchId == batchId && x.Status == LeaveBalanceImportRowStatus.Invalid).OrderBy(x => x.RowNumber).Select(x => new LeaveBalanceImportRowDto(x.RowNumber, x.EmployeeCode, x.LeaveTypeCode, x.LeavePeriod, x.OpeningBalanceText, x.EffectiveDateText, x.Remarks, x.Status, x.ErrorCode, x.ErrorMessage)).ToListAsync(ct)); }

    private static string Key(Guid tenant, Guid employee, Guid type, Guid period) => $"balance-import:{tenant:D}:{employee:D}:{type:D}:{period:D}";
    private static LeaveBalanceImportBatchDto ToDto(LeaveBalanceImportBatch x) => new(x.Id, x.FileName, x.Status, x.TotalRows, x.ValidRows, x.InvalidRows, x.ImportedRows, x.UploadedByUserId, x.UploadedAtUtc, x.CompletedAtUtc, x.FailureReason);
    private static async Task<(bool Success, string? Error, List<RawRow> Rows)> ParseAsync(Stream content, CancellationToken ct)
    {
        using var reader = new StreamReader(content, leaveOpen: true); var headerLine = await reader.ReadLineAsync(ct); if (headerLine is null) return (false, "The CSV header is missing.", []);
        var headers = Csv(headerLine).Select(x => x.Trim()).ToArray(); if (!RequiredHeaders.All(x => headers.Contains(x, StringComparer.OrdinalIgnoreCase))) return (false, "CSV must contain EmployeeCode, LeaveTypeCode, LeavePeriod, OpeningBalance, and EffectiveDate columns.", []);
        var index = headers.Select((x, i) => (x, i)).ToDictionary(x => x.x, x => x.i, StringComparer.OrdinalIgnoreCase); var rows = new List<RawRow>(); var line = 1;
        while (await reader.ReadLineAsync(ct) is { } text) { line++; var values = Csv(text); string V(string name) => index.TryGetValue(name, out var i) && i < values.Count ? values[i].Trim() : string.Empty; rows.Add(new(line, V("EmployeeCode"), V("LeaveTypeCode"), V("LeavePeriod"), V("OpeningBalance"), V("EffectiveDate"), index.ContainsKey("Remarks") ? V("Remarks") : null)); }
        return (true, null, rows);
    }
    private static List<string> Csv(string line) { var result = new List<string>(); var value = new System.Text.StringBuilder(); var quoted = false; for (var i = 0; i < line.Length; i++) { var c = line[i]; if (c == '"' && (i + 1 >= line.Length || line[i + 1] != '"')) quoted = !quoted; else if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; } else if (c == ',' && !quoted) { result.Add(value.ToString()); value.Clear(); } else value.Append(c); } result.Add(value.ToString()); return result; }
    private sealed record RawRow(int RowNumber, string EmployeeCode, string LeaveTypeCode, string LeavePeriod, string OpeningBalance, string EffectiveDate, string? Remarks);
}
