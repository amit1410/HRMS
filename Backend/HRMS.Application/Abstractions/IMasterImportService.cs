using HRMS.Application.Common;
using HRMS.Application.DTOs.Masters;

namespace HRMS.Application.Abstractions;

public interface IMasterImportService
{
    byte[] Template(string kind);
    Task<Result<MasterImportPreview>> ValidateAsync(string kind, MasterImportMode mode, Stream file, string? fileName, CancellationToken ct = default);
    Task<Result<MasterImportResult>> ConfirmAsync(MasterImportConfirmRequest request, string importedBy, CancellationToken ct = default);
}
