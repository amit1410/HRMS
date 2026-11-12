using HRMS.Application.Common;

namespace HRMS.Application.Abstractions;

public interface IEmployeeBulkUploadTemplateService
{
    Task<Result<byte[]>> GenerateAsync(CancellationToken cancellationToken = default);
}
