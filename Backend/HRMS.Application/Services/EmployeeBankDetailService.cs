using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class EmployeeBankDetailService : IEmployeeBankDetailService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";
    private const string BankNotFoundMessage = "The selected bank does not exist or is inactive.";
    private const string ActivePurposeConflictMessage =
        "An active bank account already exists for this purpose. Deactivate it before adding or activating another.";
    private const string HistoricalRecordMessage =
        "Historical bank details cannot be edited or reactivated. Add a new bank detail to create a current account.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeBankDetailService> _logger;

    public EmployeeBankDetailService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeBankDetailService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<EmployeeBankDetailDto>>> GetAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<IReadOnlyList<EmployeeBankDetailDto>>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<IReadOnlyList<EmployeeBankDetailDto>>.NotFound(NotFoundMessage);
        }

        var records = await (
                from b in _db.EmployeeBankDetails.AsNoTracking()
                join bank in _db.Banks on b.BankId equals bank.Id into bankJoin
                from bank in bankJoin.DefaultIfEmpty()
                where b.EmployeeId == employeeId
                orderby b.IsActive && b.Status == BankAccountStatus.Active descending,
                    b.AccountPurpose,
                    b.EffectiveFrom descending,
                    b.CreatedDate descending
                select new { Detail = b, BankName = bank != null ? bank.Name : string.Empty })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EmployeeBankDetailDto>>.Success(
            records.Select(x => MapToDto(x.Detail, x.BankName)).ToList());
    }

    public async Task<Result<EmployeeBankDetailEditDto>> GetForEditAsync(
        Guid employeeId, Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<EmployeeBankDetailEditDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeBankDetailEditDto>.NotFound(NotFoundMessage);
        }

        var lifecycle = await _db.EmployeeBankDetails.AsNoTracking()
            .Where(b => b.Id == id && b.EmployeeId == employeeId)
            .Select(b => new { b.IsActive, b.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (lifecycle is null)
        {
            return Result<EmployeeBankDetailEditDto>.NotFound("Bank detail record not found.");
        }

        // This endpoint exposes full account values specifically for editing. Check lifecycle before the
        // sensitive columns are selected so immutable historical values are never loaded for this request.
        if (!lifecycle.IsActive || lifecycle.Status != BankAccountStatus.Active)
        {
            return Result<EmployeeBankDetailEditDto>.Conflict(HistoricalRecordMessage);
        }

        var record = await (
                from b in _db.EmployeeBankDetails.AsNoTracking()
                join bank in _db.Banks on b.BankId equals bank.Id into bankJoin
                from bank in bankJoin.DefaultIfEmpty()
                where b.Id == id && b.EmployeeId == employeeId
                select new EmployeeBankDetailEditDto(
                    b.Id,
                    b.EmployeeId,
                    b.BankId,
                    bank != null ? bank.Name : string.Empty,
                    b.AccountHolderName,
                    b.AccountNumber,
                    b.AccountType,
                    b.AccountPurpose,
                    b.Status,
                    b.IfscCode,
                    b.BranchName,
                    b.EffectiveFrom,
                    b.IsActive,
                    b.DocumentOfProof,
                    b.CreatedDate,
                    b.ModifiedDate))
            .FirstOrDefaultAsync(cancellationToken);

        return record is null
            ? Result<EmployeeBankDetailEditDto>.NotFound("Bank detail record not found.")
            : Result<EmployeeBankDetailEditDto>.Success(record);
    }

    public async Task<Result<EmployeeBankDetailDto>> CreateAsync(
        Guid employeeId, EmployeeBankDetailRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeBankDetailDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeBankDetailDto>.NotFound(NotFoundMessage);
        }

        if (!await BankExistsAndActiveAsync(request.BankId, cancellationToken))
        {
            return Result<EmployeeBankDetailDto>.Invalid("BankId", BankNotFoundMessage);
        }

        if (request.Status != BankAccountStatus.Active)
        {
            return Result<EmployeeBankDetailDto>.Invalid(
                "status", "A new bank detail must be Active. Freeze, close, or deactivate an existing current record instead.");
        }

        if (await ActiveForPurposeExistsAsync(employeeId, request.AccountPurpose, null, cancellationToken))
        {
            return Result<EmployeeBankDetailDto>.Conflict(ActivePurposeConflictMessage);
        }

        var bankName = await BankNameAsync(request.BankId, cancellationToken);

        var record = new EmployeeBankDetail
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            BankId = request.BankId,
            AccountHolderName = request.AccountHolderName.Trim(),
            AccountNumber = request.AccountNumber.Trim(),
            AccountType = request.AccountType,
            AccountPurpose = request.AccountPurpose,
            Status = request.Status,
            IfscCode = Normalize(request.IfscCode),
            BranchName = Normalize(request.BranchName),
            EffectiveFrom = request.EffectiveFrom,
            DocumentOfProof = Normalize(request.DocumentOfProof),
            IsActive = true
        };

        _db.EmployeeBankDetails.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created bank detail {BankDetailId} for employee {EmployeeId} in tenant {TenantId}.",
            record.Id, employeeId, tenantId);

        return Result<EmployeeBankDetailDto>.Success(MapToDto(record, bankName), "Bank detail created.");
    }

    public async Task<Result<EmployeeBankDetailDto>> UpdateAsync(
        Guid employeeId, Guid id, EmployeeBankDetailRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeBankDetailDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeBankDetailDto>.NotFound(NotFoundMessage);
        }

        var record = await _db.EmployeeBankDetails
            .FirstOrDefaultAsync(b => b.Id == id && b.EmployeeId == employeeId, cancellationToken);

        if (record is null)
        {
            return Result<EmployeeBankDetailDto>.NotFound("Bank detail record not found.");
        }

        if (!IsCurrent(record))
        {
            return Result<EmployeeBankDetailDto>.Conflict(HistoricalRecordMessage);
        }

        if (!await BankCanBeAssignedAsync(request.BankId, record.BankId, cancellationToken))
        {
            return Result<EmployeeBankDetailDto>.Invalid("BankId", BankNotFoundMessage);
        }

        // Only Active is a current account. Frozen and Closed explicitly move this row into immutable
        // history; they never reactivate another old row. A replacement is always created as a new record.
        var remainsCurrent = request.Status == BankAccountStatus.Active;
        if (remainsCurrent &&
            await ActiveForPurposeExistsAsync(employeeId, request.AccountPurpose, record.Id, cancellationToken))
        {
            return Result<EmployeeBankDetailDto>.Conflict(ActivePurposeConflictMessage);
        }

        var bankName = await BankNameAsync(request.BankId, cancellationToken);

        record.BankId = request.BankId;
        record.AccountHolderName = request.AccountHolderName.Trim();
        record.AccountNumber = request.AccountNumber.Trim();
        record.AccountType = request.AccountType;
        record.AccountPurpose = request.AccountPurpose;
        record.Status = request.Status;
        record.IsActive = remainsCurrent;
        record.IfscCode = Normalize(request.IfscCode);
        record.BranchName = Normalize(request.BranchName);
        record.EffectiveFrom = request.EffectiveFrom;
        record.DocumentOfProof = Normalize(request.DocumentOfProof);
        record.ModifiedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated bank detail {BankDetailId} for employee {EmployeeId} in tenant {TenantId}.",
            id, employeeId, tenantId);

        return Result<EmployeeBankDetailDto>.Success(MapToDto(record, bankName), "Bank detail updated.");
    }

    public async Task<Result<bool>> DeleteAsync(
        Guid employeeId, Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<bool>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<bool>.NotFound(NotFoundMessage);
        }

        var record = await _db.EmployeeBankDetails
            .FirstOrDefaultAsync(b => b.Id == id && b.EmployeeId == employeeId, cancellationToken);

        if (record is null)
        {
            return Result<bool>.NotFound("Bank detail record not found.");
        }

        if (!IsCurrent(record))
        {
            return Result<bool>.Success(true, "Bank detail is already historical.");
        }

        // Soft delete: the bank master and employee record are retained. Closing the status together with
        // the active flag keeps the externally visible lifecycle unambiguous.
        record.IsActive = false;
        record.Status = BankAccountStatus.Closed;
        record.ModifiedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deactivated bank detail {BankDetailId} for employee {EmployeeId} in tenant {TenantId}.",
            id, employeeId, tenantId);

        return Result<bool>.Success(true, "Bank detail deactivated.");
    }

    private async Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        return await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, cancellationToken);
    }

    private async Task<bool> BankExistsAndActiveAsync(Guid bankId, CancellationToken cancellationToken)
    {
        return await _db.Banks.AsNoTracking()
            .AnyAsync(b => b.Id == bankId && b.IsActive, cancellationToken);
    }

    private async Task<bool> BankCanBeAssignedAsync(
        Guid requestedBankId, Guid currentBankId, CancellationToken cancellationToken)
    {
        return await _db.Banks.AsNoTracking()
            .AnyAsync(
                b => b.Id == requestedBankId && (b.IsActive || requestedBankId == currentBankId),
                cancellationToken);
    }

    private async Task<bool> ActiveForPurposeExistsAsync(
        Guid employeeId, AccountPurpose purpose, Guid? ignoreId, CancellationToken cancellationToken)
    {
        var q = _db.EmployeeBankDetails
            .Where(b => b.EmployeeId == employeeId
                        && b.AccountPurpose == purpose
                        && b.IsActive
                        && b.Status == BankAccountStatus.Active);

        if (ignoreId is Guid id)
        {
            q = q.Where(b => b.Id != id);
        }

        return await q.AnyAsync(cancellationToken);
    }

    private async Task<string> BankNameAsync(Guid bankId, CancellationToken cancellationToken)
    {
        var name = await _db.Banks.AsNoTracking()
            .Where(b => b.Id == bankId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(cancellationToken);
        return name ?? string.Empty;
    }

    private static EmployeeBankDetailDto MapToDto(EmployeeBankDetail b, string bankName) =>
        new(b.Id, b.EmployeeId, b.BankId, bankName, b.AccountHolderName,
            SensitiveDataMasker.AccountNumber(b.AccountNumber),
            b.AccountType, b.AccountPurpose, b.Status, SensitiveDataMasker.Ifsc(b.IfscCode),
            b.BranchName, b.EffectiveFrom, IsCurrent(b), !string.IsNullOrWhiteSpace(b.DocumentOfProof),
            b.CreatedDate, b.ModifiedDate);

    private static bool IsCurrent(EmployeeBankDetail record) =>
        record.IsActive && record.Status == BankAccountStatus.Active;

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
