using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FluentValidation;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Banks;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>Server-side, tenant-scoped Bank master CSV/XLSX validation, transactional import, export and history.</summary>
public sealed class BankImportService : IBankImportService
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private const int MaxRows = 5000;

    /// <summary>Marker written to <see cref="ImportBatch.Message"/> so Bank imports can be found in the shared history table.</summary>
    private const string BatchMarker = "Module=BankMaster";

    private static readonly string[] Columns =
        ["BankCode", "BankName", "ShortName", "IFSCPrefix", "BankType", "Country", "Active", "EffectiveFrom", "Remarks"];

    private readonly IHrmsDbContext _db;
    private readonly IBankService _banks;
    private readonly ITenantContext _tenant;
    private readonly IValidator<BankRequest> _validator;

    public BankImportService(IHrmsDbContext db, IBankService banks, ITenantContext tenant, IValidator<BankRequest> validator)
    {
        _db = db;
        _banks = banks;
        _tenant = tenant;
        _validator = validator;
    }

    public byte[] BuildTemplate(BankFileFormat format)
    {
        var example = new[] { "HDFC", "HDFC Bank", "HDFC", "HDFC", "Private", "India", "true", "2024-04-01", "Replace this example row" };
        return format == BankFileFormat.Xlsx
            ? WriteXlsx("Banks", Columns, [example])
            : WriteCsv(Columns, [example]);
    }

    public byte[] BuildExport(IReadOnlyList<BankDto> banks, BankFileFormat format)
    {
        var rows = banks.Select(b => new[]
        {
            b.Code,
            b.Name,
            b.ShortName ?? string.Empty,
            b.IfscPrefix ?? string.Empty,
            b.BankType?.ToString() ?? string.Empty,
            b.Country ?? string.Empty,
            b.IsActive ? "true" : "false",
            b.EffectiveFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            b.Remarks ?? string.Empty
        }).ToList();

        return format == BankFileFormat.Xlsx ? WriteXlsx("Banks", Columns, rows) : WriteCsv(Columns, rows);
    }

    public async Task<Result<BankImportPreview>> ValidateAsync(BankImportMode mode, Stream file, string? fileName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid)
        {
            return Result<BankImportPreview>.Unauthorized("No authenticated tenant.");
        }

        if (file.CanSeek && file.Length > MaxBytes)
        {
            return Result<BankImportPreview>.Invalid("File exceeds the 5 MB limit.");
        }

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        List<BankImportRow> rows;
        try
        {
            rows = extension switch
            {
                ".xlsx" => ParseXlsx(file),
                ".csv" => await ParseCsvAsync(file, ct),
                _ => throw new InvalidDataException("Only .csv and .xlsx files are accepted.")
            };
        }
        catch (InvalidDataException ex)
        {
            return Result<BankImportPreview>.Invalid(ex.Message);
        }

        if (rows.Count > MaxRows)
        {
            return Result<BankImportPreview>.Invalid($"The file contains more than {MaxRows} data rows.");
        }

        return Result<BankImportPreview>.Success(await BuildPreviewAsync(mode, rows, ct));
    }

    public async Task<Result<BankImportResult>> ConfirmAsync(BankImportConfirmRequest request, string importedBy, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return Result<BankImportResult>.Unauthorized("No authenticated tenant.");
        }

        if (request.Rows.Count == 0)
        {
            return Result<BankImportResult>.Invalid("There are no rows to import.");
        }

        if (request.Rows.Count > MaxRows)
        {
            return Result<BankImportResult>.Invalid($"The import exceeds the {MaxRows} row limit.");
        }

        // Re-validate against current data: the store may have changed since the preview was produced.
        var preview = await BuildPreviewAsync(request.Mode, request.Rows, ct);
        if (preview.ErrorRows > 0)
        {
            return Result<BankImportResult>.Conflict("The import changed since validation. Resolve all validation errors and validate again.");
        }

        await using var transaction = await _db.BeginTransactionAsync(ct);
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = SafeFileName(request.FileName),
            ImportedBy = importedBy,
            TotalRows = preview.TotalRows,
            Status = "Processing",
            StartedAtUtc = DateTime.UtcNow,
            Message = $"{BatchMarker};Status=Processing"
        };
        _db.ImportBatches.Add(batch);

        var created = 0;
        var updated = 0;
        try
        {
            foreach (var row in request.Rows)
            {
                var code = row.BankCode.Trim();
                var existing = await _db.Banks.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Code.ToLower() == code.ToLower(), ct);

                var body = ToRequest(row);
                if (existing is null)
                {
                    var result = await _banks.CreateAsync(body, ct);
                    if (!result.Succeeded)
                    {
                        throw new InvalidOperationException(result.Message);
                    }

                    created++;
                }
                else if (request.Mode == BankImportMode.CreateOrUpdate)
                {
                    var result = await _banks.UpdateAsync(existing.Id, body, ct);
                    if (!result.Succeeded)
                    {
                        throw new InvalidOperationException(result.Message);
                    }

                    updated++;
                }
            }

            batch.SuccessfulRows = created + updated;
            batch.Status = "Completed";
            batch.CompletedAtUtc = DateTime.UtcNow;
            batch.Message = $"{BatchMarker};Mode={request.Mode};Created={created};Updated={updated}";
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Result<BankImportResult>.Success(
                new BankImportResult(batch.Id, preview.TotalRows, created, updated, 0, 0, batch.Status, batch.CompletedAtUtc.Value),
                "Bank import completed.");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<Result<IReadOnlyList<BankImportHistoryDto>>> GetHistoryAsync(CancellationToken ct = default)
    {
        if (_tenant.TenantId is null)
        {
            return Result<IReadOnlyList<BankImportHistoryDto>>.Unauthorized("No authenticated tenant.");
        }

        var history = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.Message != null && b.Message.StartsWith(BatchMarker))
            .OrderByDescending(b => b.CreatedDate)
            .Select(b => new BankImportHistoryDto(
                b.Id, b.FileName, b.ImportedBy, b.TotalRows, b.SuccessfulRows, b.FailedRows, b.SkippedRows,
                b.Status, b.StartedAtUtc, b.CompletedAtUtc, b.Message, b.CreatedDate))
            .ToListAsync(ct);

        return Result<IReadOnlyList<BankImportHistoryDto>>.Success(history);
    }

    private async Task<BankImportPreview> BuildPreviewAsync(BankImportMode mode, IReadOnlyList<BankImportRow> rows, CancellationToken ct)
    {
        // Existing banks in one query, so the preview is O(1) DB round-trips rather than one per row. Both
        // the codes and the names are needed: uniqueness of each is enforced in the store, so a row that
        // would collide has to fail in the preview rather than blow up mid-import.
        var existing = await _db.Banks.AsNoTracking()
            .Select(b => new { b.Code, b.Name })
            .ToListAsync(ct);
        var existingCodes = new HashSet<string>(existing.Select(b => b.Code), StringComparer.OrdinalIgnoreCase);
        // Name is unique per tenant, so a name maps to exactly one owning code.
        var existingNameOwner = existing
            .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Code, StringComparer.OrdinalIgnoreCase);

        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<BankImportRowResult>(rows.Count);
        var newRows = 0;
        var updates = 0;

        foreach (var row in rows)
        {
            var errors = new List<string>();
            var code = row.BankCode.Trim();
            var name = row.BankName.Trim();

            // Shape rules are the same object the interactive create/update path validates, so the two can
            // never drift: code format/length, name required/length, short name, IFSC characters/length,
            // country and remarks length all come from the one validator.
            foreach (var failure in _validator.Validate(ToRequest(row)).Errors)
            {
                errors.Add(failure.ErrorMessage);
            }

            // BankType is checked here rather than by the validator: the raw cell is a string, and an
            // unrecognized value would otherwise be silently parsed to null and lost.
            if (!string.IsNullOrWhiteSpace(row.BankType) && ParseBankType(row.BankType) is null)
            {
                errors.Add($"Bank type '{row.BankType}' is not recognized. Allowed: {string.Join(", ", Enum.GetNames<BankType>())}.");
            }

            if (code.Length > 0 && !seenCodes.Add(code))
            {
                errors.Add("Bank code is duplicated in this file.");
            }

            if (name.Length > 0 && !seenNames.Add(name))
            {
                errors.Add("Bank name is duplicated in this file.");
            }

            var exists = code.Length > 0 && existingCodes.Contains(code);
            if (exists && mode == BankImportMode.CreateOnly)
            {
                errors.Add("Bank code already exists; use Create or update to change it.");
            }

            // A name already used by a DIFFERENT bank is a conflict. When the row targets an existing bank by
            // code (an update), that same bank keeping its own name is not a clash — mirror the service's
            // exclude-self behaviour so a plain re-import does not falsely fail.
            if (name.Length > 0
                && existingNameOwner.TryGetValue(name, out var ownerCode)
                && !string.Equals(ownerCode, code, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"A bank named '{name}' already exists.");
            }

            if (errors.Count == 0)
            {
                if (exists)
                {
                    updates++;
                }
                else
                {
                    newRows++;
                }
            }

            var action = errors.Count > 0 ? "Error" : exists ? "Update" : "Create";
            results.Add(new BankImportRowResult(row.RowNumber, code, name, action, errors));
        }

        var valid = results.Count(x => x.Errors.Count == 0);
        return new BankImportPreview(
            mode,
            rows,
            results,
            rows.Count,
            valid,
            newRows,
            mode == BankImportMode.CreateOrUpdate ? updates : 0,
            0,
            results.Count - valid);
    }

    private static BankRequest ToRequest(BankImportRow row) => new()
    {
        Code = row.BankCode.Trim(),
        Name = row.BankName.Trim(),
        ShortName = Blank(row.ShortName),
        IfscPrefix = Blank(row.IfscPrefix),
        BankType = ParseBankType(row.BankType),
        Country = Blank(row.Country),
        EffectiveFrom = row.EffectiveFrom,
        Remarks = Blank(row.Remarks),
        IsActive = row.Active
    };

    // ---- Parsing ----

    private static async Task<List<BankImportRow>> ParseCsvAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        var lines = new List<string>();
        while (!reader.EndOfStream && lines.Count <= MaxRows + 1)
        {
            lines.Add((await reader.ReadLineAsync(ct)) ?? string.Empty);
        }

        if (lines.Count == 0)
        {
            throw new InvalidDataException("The CSV is empty.");
        }

        var headers = SplitCsv(lines[0]).Select(h => h.Trim()).ToArray();
        var index = MapHeaders(headers);

        var rows = new List<BankImportRow>();
        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var cells = SplitCsv(lines[i]);
            rows.Add(BuildRow(i + 1, field => Cell(cells, index, field)));
        }

        return rows;
    }

    private static List<BankImportRow> ParseXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("The workbook has no worksheets.");
        var used = worksheet.RangeUsed();
        if (used is null)
        {
            throw new InvalidDataException("The worksheet is empty.");
        }

        var headerRow = used.FirstRow();
        var headers = headerRow.Cells().Select(c => c.GetString().Trim()).ToArray();
        var index = MapHeaders(headers);

        var rows = new List<BankImportRow>();
        var dataRows = used.RowsUsed().Skip(1).ToList();
        if (dataRows.Count > MaxRows + 1)
        {
            throw new InvalidDataException($"The file contains more than {MaxRows} data rows.");
        }

        foreach (var row in dataRows)
        {
            var cells = row.Cells(1, headers.Length).Select(c => c.GetString().Trim()).ToArray();
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var rowNumber = row.RowNumber();
            rows.Add(BuildRow(rowNumber, field => index.TryGetValue(field, out var col) && col < cells.Length ? cells[col] : string.Empty));
        }

        return rows;
    }

    private static BankImportRow BuildRow(int rowNumber, Func<string, string> cell)
    {
        var active = ParseBool(cell("Active"), out var value)
            ? value
            : throw new InvalidDataException($"Invalid Active value at row {rowNumber}. Use true/false.");

        DateOnly? effectiveFrom = null;
        var effectiveText = cell("EffectiveFrom");
        if (!string.IsNullOrWhiteSpace(effectiveText))
        {
            if (!TryParseDate(effectiveText, out var parsed))
            {
                throw new InvalidDataException($"Invalid EffectiveFrom value at row {rowNumber}. Use an ISO date such as 2024-04-01.");
            }

            effectiveFrom = parsed;
        }

        return new BankImportRow(
            rowNumber,
            cell("BankCode"),
            cell("BankName"),
            Blank(cell("ShortName")),
            Blank(cell("IFSCPrefix")),
            Blank(cell("BankType")),
            Blank(cell("Country")),
            active,
            effectiveFrom,
            Blank(cell("Remarks")));
    }

    private static Dictionary<string, int> MapHeaders(string[] headers)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(headers[i]) && !index.ContainsKey(headers[i]))
            {
                index[headers[i]] = i;
            }
        }

        var required = new[] { "BankCode", "BankName", "Active" };
        if (!required.All(index.ContainsKey))
        {
            throw new InvalidDataException($"The file headers must include at least: {string.Join(", ", required)}. Expected columns: {string.Join(", ", Columns)}.");
        }

        return index;
    }

    private static string Cell(IReadOnlyList<string> cells, Dictionary<string, int> index, string field) =>
        index.TryGetValue(field, out var col) && col >= 0 && col < cells.Count ? cells[col].Trim() : string.Empty;

    /// <summary>Minimal RFC-4180-ish splitter: handles double-quoted fields with embedded commas and quotes.</summary>
    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                result.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(c);
            }
        }

        result.Add(builder.ToString());
        return result;
    }

    // ---- Writing ----

    private static byte[] WriteCsv(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(",", headers.Select(EscapeCsv))).Append("\r\n");
        foreach (var row in rows)
        {
            builder.Append(string.Join(",", row.Select(EscapeCsv))).Append("\r\n");
        }

        // UTF-8 BOM so Excel opens the export with the correct encoding.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static byte[] WriteXlsx(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var c = 0; c < headers.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
            sheet.Cell(1, c + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(r + 2, c + 1).Value = rows[r][c];
            }
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ---- Small parsers ----

    private static BankType? ParseBankType(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.TryParse<BankType>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : null;

    private static bool ParseBool(string? value, out bool result)
    {
        result = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "true" or "yes" or "y" or "1" or "active":
                result = true;
                return true;
            case "false" or "no" or "n" or "0" or "inactive":
                result = false;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseDate(string value, out DateOnly result) =>
        DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string SafeFileName(string? name) =>
        Path.GetFileName(string.IsNullOrWhiteSpace(name) ? "bank-upload.csv" : name)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);
}
