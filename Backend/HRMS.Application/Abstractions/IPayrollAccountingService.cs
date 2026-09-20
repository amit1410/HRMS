using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollAccountingService
{
    Task<Result<PagedResult<PayrollGLAccountDto>>> ListAccountsAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<PayrollGLAccountDto>> CreateAccountAsync(PayrollGLAccountRequest request, CancellationToken ct = default);
    Task<Result<PayrollGLAccountDto>> UpdateAccountAsync(Guid id, PayrollGLAccountRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollAccountingConfigurationDto>>> ListConfigurationsAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<PayrollAccountingConfigurationDto>> CreateConfigurationAsync(PayrollAccountingConfigurationRequest request, CancellationToken ct = default);
    Task<Result<PayrollAccountingConfigurationVersionDto>> CreateVersionAsync(Guid configurationId, PayrollAccountingConfigurationVersionRequest request, CancellationToken ct = default);
    Task<Result<PayrollGLMappingDto>> CreateMappingAsync(Guid versionId, PayrollGLMappingRequest request, CancellationToken ct = default);
    Task<Result<PayrollGLMappingDto>> UpdateMappingAsync(Guid id, PayrollGLMappingRequest request, CancellationToken ct = default);
    Task<Result<bool>> DeactivateMappingAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollJournalDto>> GenerateAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<PayrollJournalDto>> ValidateAsync(Guid journalId, CancellationToken ct = default);
    Task<Result<PayrollJournalDto>> ApproveAsync(Guid journalId, CancellationToken ct = default);
    Task<Result<PayrollJournalDto>> PostAsync(Guid journalId, CancellationToken ct = default);
    Task<Result<PayrollJournalDto>> GetAsync(Guid journalId, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollJournalDto>>> ListAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportAsync(Guid journalId, CancellationToken ct = default);
}
