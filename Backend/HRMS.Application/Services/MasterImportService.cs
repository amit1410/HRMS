using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Masters;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>Server-side, tenant-scoped master CSV validation and transactional import.</summary>
public sealed class MasterImportService : IMasterImportService
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private const int MaxRows = 5000;
    private readonly IHrmsDbContext _db;
    private readonly IMasterManagementService _masters;
    private readonly ITenantContext _tenant;

    public MasterImportService(IHrmsDbContext db, IMasterManagementService masters, ITenantContext tenant)
    { _db = db; _masters = masters; _tenant = tenant; }

    public byte[] Template(string kind)
    {
        var parent = ParentKind(kind);
        var header = parent is null ? "Code,Name,Description,IsActive" : $"Code,Name,Description,IsActive,{ParentColumn(kind)}";
        return Encoding.UTF8.GetBytes($"{header}\r\nEXAMPLE-001,Example record,Replace this example,true{(parent is null ? string.Empty : ",PARENT-CODE")}\r\n");
    }

    public async Task<Result<MasterImportPreview>> ValidateAsync(string kind, MasterImportMode mode, Stream file, string? fileName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid) return Result<MasterImportPreview>.Unauthorized("No authenticated tenant.");
        if (!Supported(kind)) return Result<MasterImportPreview>.NotFound("Master type is not supported for bulk import.");
        if (file.Length > MaxBytes) return Result<MasterImportPreview>.Invalid("File exceeds the 5 MB limit.");
        if (Path.GetExtension(fileName ?? string.Empty).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return Result<MasterImportPreview>.Invalid("Excel workbooks are not yet enabled; upload a UTF-8 CSV template.");
        if (!Path.GetExtension(fileName ?? string.Empty).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return Result<MasterImportPreview>.Invalid("Only CSV files are accepted.");
        var rows = await ParseCsvAsync(file, kind, ct);
        if (rows.Count > MaxRows) return Result<MasterImportPreview>.Invalid($"The file contains more than {MaxRows} data rows.");
        return Result<MasterImportPreview>.Success(await BuildPreviewAsync(kind, mode, rows, ct));
    }

    public async Task<Result<MasterImportResult>> ConfirmAsync(MasterImportConfirmRequest request, string importedBy, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<MasterImportResult>.Unauthorized("No authenticated tenant.");
        if (!Supported(request.MasterType)) return Result<MasterImportResult>.NotFound("Master type is not supported for bulk import.");
        if (request.Rows.Count > MaxRows) return Result<MasterImportResult>.Invalid($"The import exceeds the {MaxRows} row limit.");
        var preview = await BuildPreviewAsync(request.MasterType, request.Mode, request.Rows, ct);
        if (preview.ErrorRows > 0) return Result<MasterImportResult>.Conflict("The import changed since validation. Resolve all validation errors and validate again.");

        await using var transaction = await _db.BeginTransactionAsync(ct);
        var batch = new ImportBatch { Id = Guid.NewGuid(), TenantId = tenantId, FileName = SafeFileName(request.FileName), ImportedBy = importedBy, TotalRows = preview.TotalRows, Status = "Processing", StartedAtUtc = DateTime.UtcNow };
        _db.ImportBatches.Add(batch);
        var created = 0; var updated = 0;
        try
        {
            foreach (var row in request.Rows)
            {
                var existing = (await _masters.GetAsync(request.MasterType, new MasterManagementQuery(row.Code, null, null, 1, 2), ct)).Value?.Items.SingleOrDefault(x => x.Code.Equals(row.Code.Trim(), StringComparison.OrdinalIgnoreCase));
                var body = new MasterManagementRequest { Code = row.Code, Name = row.Name, Description = row.Description, IsActive = row.IsActive, ParentId = await ResolveParentAsync(request.MasterType, row.ParentCode, ct) };
                if (existing is null) { var result = await _masters.CreateAsync(request.MasterType, body, ct); if (!result.Succeeded) throw new InvalidOperationException(result.Message); created++; }
                else if (request.Mode == MasterImportMode.CreateOrUpdate) { var result = await _masters.UpdateAsync(request.MasterType, existing.Id, body, ct); if (!result.Succeeded) throw new InvalidOperationException(result.Message); updated++; }
            }
            batch.SuccessfulRows = created + updated; batch.Status = "Completed"; batch.CompletedAtUtc = DateTime.UtcNow; batch.Message = $"MasterType={request.MasterType};Mode={request.Mode};Created={created};Updated={updated}";
            await _db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Result<MasterImportResult>.Success(new(batch.Id, preview.TotalRows, created, updated, 0, 0, batch.Status, batch.CompletedAtUtc.Value), "Master import completed.");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<MasterImportPreview> BuildPreviewAsync(string kind, MasterImportMode mode, IReadOnlyList<MasterImportRow> rows, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var results = new List<MasterImportRowResult>(); var newRows = 0; var updates = 0;
        foreach (var row in rows)
        {
            var errors = new List<string>(); var code = row.Code.Trim(); var name = row.Name.Trim();
            if (code.Length == 0) errors.Add("Code is required."); else if (code.Length > 20) errors.Add("Code must be at most 20 characters.");
            if (name.Length == 0) errors.Add("Name is required.");
            if (!seen.Add(code)) errors.Add("Code is duplicated in this file.");
            if (ParentKind(kind) is not null && string.IsNullOrWhiteSpace(row.ParentCode)) errors.Add($"{ParentColumn(kind)} is required.");
            if (!string.IsNullOrWhiteSpace(row.ParentCode) && await ResolveParentAsync(kind, row.ParentCode, ct) is null) errors.Add("Parent code does not resolve to an active record in this tenant.");
            var existing = (await _masters.GetAsync(kind, new MasterManagementQuery(code, null, null, 1, 2), ct)).Value?.Items.SingleOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && mode == MasterImportMode.CreateOnly) errors.Add("Code already exists; use Create or update to change it.");
            if (existing is null) newRows++; else updates++;
            results.Add(new(row.RowNumber, code, name, row.ParentCode, errors.Count == 0 ? (existing is null ? "Create" : "Update") : "Error", errors));
        }
        var valid = results.Count(x => x.Errors.Count == 0);
        return new(kind, mode, rows, results, rows.Count, valid, newRows, mode == MasterImportMode.CreateOrUpdate ? updates : 0, 0, results.Count - valid);
    }

    private async Task<Guid?> ResolveParentAsync(string kind, string? code, CancellationToken ct)
    {
        var parentKind = ParentKind(kind); if (parentKind is null || string.IsNullOrWhiteSpace(code)) return null;
        return (await _masters.GetAsync(parentKind, new MasterManagementQuery(code.Trim(), true, null, 1, 2), ct)).Value?.Items.SingleOrDefault(x => x.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static async Task<List<MasterImportRow>> ParseCsvAsync(Stream stream, string kind, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        var lines = new List<string>(); while (!reader.EndOfStream && lines.Count <= MaxRows + 1) { lines.Add((await reader.ReadLineAsync(ct)) ?? string.Empty); }
        if (lines.Count == 0) throw new InvalidDataException("The CSV is empty.");
        var headers = Split(lines[0]).Select(x => x.Trim()).ToArray(); var required = ParentKind(kind) is null ? new[] { "Code", "Name", "Description", "IsActive" } : new[] { "Code", "Name", "Description", "IsActive", ParentColumn(kind) };
        if (!required.All(x => headers.Contains(x, StringComparer.OrdinalIgnoreCase))) throw new InvalidDataException("The CSV headers do not match the selected master template.");
        var index = required.ToDictionary(x => x, x => Array.FindIndex(headers, h => h.Equals(x, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase); var rows = new List<MasterImportRow>();
        for (var i = 1; i < lines.Count; i++) { if (string.IsNullOrWhiteSpace(lines[i])) continue; var cells = Split(lines[i]); if (!bool.TryParse(Cell(cells, index["IsActive"]), out var active)) throw new InvalidDataException($"Invalid IsActive at row {i + 1}."); rows.Add(new(i + 1, Cell(cells, index["Code"]), Cell(cells, index["Name"]), Cell(cells, index["Description"]), active, index.ContainsKey(ParentColumn(kind)) ? Cell(cells, index[ParentColumn(kind)]) : null)); }
        return rows;
    }

    private static string[] Split(string line) => line.Split(',');
    private static string Cell(string[] cells, int index) => index >= 0 && index < cells.Length ? cells[index].Trim().Trim('"') : string.Empty;
    private static string? ParentKind(string kind) => kind.ToLowerInvariant() switch { "lines-of-business" => "holding-companies", "sub-departments" => "departments", "sections" => "sub-departments", "sub-sections" => "sections", "sub-functions" => "functions", _ => null };
    private static string ParentColumn(string kind) => kind.ToLowerInvariant() switch { "lines-of-business" => "HoldingCompanyCode", "sub-departments" => "DepartmentCode", "sections" => "SubDepartmentCode", "sub-sections" => "SectionCode", "sub-functions" => "FunctionCode", _ => "ParentCode" };
    private static bool Supported(string kind) => kind.ToLowerInvariant() is "holding-companies" or "lines-of-business" or "organisations" or "departments" or "sub-departments" or "sections" or "sub-sections" or "functions" or "sub-functions" or "grades" or "designations" or "employee-types" or "work-locations" or "cost-centers" or "position-change-reasons";
    private static string SafeFileName(string? name) => Path.GetFileName(string.IsNullOrWhiteSpace(name) ? "master-upload.csv" : name).Replace("\r", string.Empty).Replace("\n", string.Empty);
}
