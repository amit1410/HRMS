using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeDocumentService
{
    Task<Result<IReadOnlyList<EmployeeDocumentDto>>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeDocumentDto>> UploadAsync(Guid employeeId, EmployeeDocumentRequest request, string fileName, long fileSize, string contentType, string uploadedBy, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid employeeId, Guid documentId, CancellationToken cancellationToken = default);
}
