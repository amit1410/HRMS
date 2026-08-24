namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// A generated export file. The service produces the bytes so the controller only has to hand them to the
/// client, and so the format is covered by service-level tests rather than only by an HTTP test.
/// </summary>
public record EmployeeExportDto(string FileName, string ContentType, byte[] Content, int RowCount);
