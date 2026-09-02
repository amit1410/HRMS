using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Read-only DTO for an uploaded employee document. The actual file is stored on disk or blob
/// storage; this DTO exposes only the metadata and upload audit trail.
/// </summary>
public record EmployeeDocumentDto(
    Guid Id,
    string DocumentName,
    DocumentCategory DocumentCategory,
    string? DocumentNumber,
    string FilePath,
    long FileSize,
    string ContentType,
    string? UploadedBy,
    DateTime CreatedDate);
