using HRMS.Application.Common;
using HRMS.Application.DTOs.Banks;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Server-side, tenant-scoped Bank master import (CSV/XLSX), export and import history. Validation is always
/// run before a confirmed import, and the confirm is transactional: either every valid row is applied or none
/// is. Existing banks are never silently overwritten — they change only under
/// <see cref="BankImportMode.CreateOrUpdate"/>.
/// </summary>
public interface IBankImportService
{
    /// <summary>Builds a downloadable, empty template with the expected columns and one example row.</summary>
    byte[] BuildTemplate(BankFileFormat format);

    /// <summary>Serializes banks to the requested format for export.</summary>
    byte[] BuildExport(IReadOnlyList<BankDto> banks, BankFileFormat format);

    /// <summary>Parses and validates an uploaded file, returning a preview without writing anything.</summary>
    Task<Result<BankImportPreview>> ValidateAsync(BankImportMode mode, Stream file, string? fileName, CancellationToken ct = default);

    /// <summary>Applies a previously validated set of rows in a single transaction.</summary>
    Task<Result<BankImportResult>> ConfirmAsync(BankImportConfirmRequest request, string importedBy, CancellationToken ct = default);

    /// <summary>Returns the Bank master import history for the current tenant, newest first.</summary>
    Task<Result<IReadOnlyList<BankImportHistoryDto>>> GetHistoryAsync(CancellationToken ct = default);
}
