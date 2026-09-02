using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public class EmployeeDocumentRequest
{
    public string DocumentName { get; set; } = string.Empty;
    public DocumentCategory DocumentCategory { get; set; } = DocumentCategory.Other;
    public string? DocumentNumber { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
}
