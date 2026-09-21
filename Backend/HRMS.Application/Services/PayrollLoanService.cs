using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollLoanService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock, IPayrollApprovalGuard? approvalGuard = null) : IPayrollLoanService
{
    public async Task<Result<IReadOnlyList<LoanProductDto>>> GetProductsAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<IReadOnlyList<LoanProductDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.LoanProducts.AsNoTracking().Where(x => x.TenantId == tid).OrderBy(x => x.Code).Select(x => ToDto(x)).ToListAsync(ct);
        return Result<IReadOnlyList<LoanProductDto>>.Success(rows);
    }

    public async Task<Result<LoanProductDto>> CreateProductAsync(LoanProductRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<LoanProductDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) return Result<LoanProductDto>.Invalid("code", "Code and name are required.");
        if (request.MinAmount is < 0 || request.MaxAmount is < 0 || request.MinAmount > request.MaxAmount) return Result<LoanProductDto>.Invalid("amount", "Amount limits are invalid.");
        if (request.MinTenureMonths is < 1 || request.MaxTenureMonths is < 1 || request.MinTenureMonths > request.MaxTenureMonths) return Result<LoanProductDto>.Invalid("tenure", "Tenure limits are invalid.");
        if (await db.LoanProducts.AnyAsync(x => x.TenantId == tid && x.Code == request.Code.Trim(), ct)) return Result<LoanProductDto>.Conflict("A loan product with this code already exists.");
        var item = new LoanProduct { Id = Guid.NewGuid(), TenantId = tid, Code = request.Code.Trim(), Name = request.Name.Trim(), Description = request.Description?.Trim(), ProductType = request.ProductType, IsActive = request.IsActive, CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(), MinAmount = request.MinAmount, MaxAmount = request.MaxAmount, MinTenureMonths = request.MinTenureMonths, MaxTenureMonths = request.MaxTenureMonths, InterestMethod = request.InterestMethod, InterestRate = request.InterestRate, AllowPartialPrepayment = request.AllowPartialPrepayment, AllowEarlyClosure = request.AllowEarlyClosure, MaxConcurrentLoans = request.MaxConcurrentLoans, RecoveryPolicy = request.RecoveryPolicy };
        db.LoanProducts.Add(item); db.LoanProductVersions.Add(new LoanProductVersion { Id = Guid.NewGuid(), TenantId = tid, LoanProduct = item, EffectiveFrom = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), MinAmount = request.MinAmount ?? 0, MaxAmount = request.MaxAmount ?? 9999999999999999.99m, MinTenureMonths = request.MinTenureMonths ?? 1, MaxTenureMonths = request.MaxTenureMonths ?? 120, InterestMethod = request.InterestMethod, InterestRate = request.InterestRate ?? 0, Status = LoanProductVersionStatus.Active, AllowPartialPrepayment = request.AllowPartialPrepayment, AllowEarlyClosure = request.AllowEarlyClosure, RecoveryPolicy = request.RecoveryPolicy }); await db.SaveChangesAsync(ct); return Result<LoanProductDto>.Success(ToDto(item), "Loan product created.");
    }

    public async Task<Result<LoanProductDto>> UpdateProductAsync(Guid id, LoanProductRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<LoanProductDto>.Unauthorized("No authenticated tenant.");
        if (request.MinAmount is < 0 || request.MaxAmount is < 0 || request.MinAmount > request.MaxAmount) return Result<LoanProductDto>.Invalid("amount", "Amount limits are invalid.");
        if (request.MinTenureMonths is < 1 || request.MaxTenureMonths is < 1 || request.MinTenureMonths > request.MaxTenureMonths) return Result<LoanProductDto>.Invalid("tenure", "Tenure limits are invalid.");
        var item = await db.LoanProducts.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        if (item is null) return Result<LoanProductDto>.NotFound("Loan product not found.");
        if (await db.LoanProducts.AnyAsync(x => x.TenantId == tid && x.Id != id && x.Code == request.Code.Trim(), ct)) return Result<LoanProductDto>.Conflict("A loan product with this code already exists.");
        item.Code = request.Code.Trim(); item.Name = request.Name.Trim(); item.Description = request.Description?.Trim(); item.ProductType = request.ProductType; item.IsActive = request.IsActive; item.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(); item.MinAmount = request.MinAmount; item.MaxAmount = request.MaxAmount; item.MinTenureMonths = request.MinTenureMonths; item.MaxTenureMonths = request.MaxTenureMonths; item.InterestMethod = request.InterestMethod; item.InterestRate = request.InterestRate; item.AllowPartialPrepayment = request.AllowPartialPrepayment; item.AllowEarlyClosure = request.AllowEarlyClosure; item.MaxConcurrentLoans = request.MaxConcurrentLoans; item.RecoveryPolicy = request.RecoveryPolicy;
        await db.SaveChangesAsync(ct); return Result<LoanProductDto>.Success(ToDto(item), "Loan product updated.");
    }

    public async Task<Result<LoanProductDto>> CreateProductVersionAsync(Guid id, LoanProductVersionRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<LoanProductDto>.Unauthorized("No authenticated tenant.");
        if (request.EffectiveTo < request.EffectiveFrom || request.MinAmount < 0 || request.MaxAmount < request.MinAmount || request.MinTenureMonths < 1 || request.MaxTenureMonths < request.MinTenureMonths || request.InterestRate < 0) return Result<LoanProductDto>.Invalid("version", "Loan product version terms are invalid.");
        var product = await db.LoanProducts.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        if (product is null) return Result<LoanProductDto>.NotFound("Loan product not found.");
        var end = request.EffectiveTo ?? DateOnly.MaxValue;
        if (await db.LoanProductVersions.AnyAsync(x => x.TenantId == tid && x.LoanProductId == id && x.Status == LoanProductVersionStatus.Active && x.EffectiveFrom <= end && (x.EffectiveTo == null || x.EffectiveTo >= request.EffectiveFrom), ct)) return Result<LoanProductDto>.Conflict("Loan product versions cannot overlap.");
        db.LoanProductVersions.Add(new LoanProductVersion { Id = Guid.NewGuid(), TenantId = tid, LoanProductId = id, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, MinAmount = request.MinAmount, MaxAmount = request.MaxAmount, MinTenureMonths = request.MinTenureMonths, MaxTenureMonths = request.MaxTenureMonths, InterestMethod = request.InterestMethod, InterestRate = request.InterestRate, Status = LoanProductVersionStatus.Active, AllowPartialPrepayment = request.AllowPartialPrepayment, AllowEarlyClosure = request.AllowEarlyClosure, RecoveryPolicy = request.RecoveryPolicy });
        await db.SaveChangesAsync(ct); return Result<LoanProductDto>.Success(ToDto(product), "Loan product version created.");
    }

    public async Task<Result<IReadOnlyList<EmployeeLoanDto>>> GetLoansAsync(Guid? employeeId = null, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<IReadOnlyList<EmployeeLoanDto>>.Unauthorized("No authenticated tenant.");
        var query = db.EmployeeLoans.AsNoTracking().Include(x => x.Installments).Where(x => x.TenantId == tid); if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId.Value);
        var rows = await query.OrderByDescending(x => x.RequestedAtUtc).Take(500).ToListAsync(ct); return Result<IReadOnlyList<EmployeeLoanDto>>.Success(rows.Select(ToDto).ToList());
    }

    public async Task<Result<PagedResult<LoanRegisterRowDto>>> GetRegisterAsync(LoanRegisterQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PagedResult<LoanRegisterRowDto>>.Unauthorized("No authenticated tenant.");
        var source = db.EmployeeLoans.AsNoTracking().Where(x => x.TenantId == tid);
        if (query.ProductId is Guid productId) source = source.Where(x => x.LoanProductId == productId);
        if (query.ProductType is LoanProductType productType) source = source.Where(x => x.LoanProduct != null && x.LoanProduct.ProductType == productType);
        if (query.EmployeeId is Guid employeeId) source = source.Where(x => x.EmployeeId == employeeId);
        if (query.Status is LoanStatus status) source = source.Where(x => x.Status == status);
        if (query.From is DateOnly from) source = source.Where(x => DateOnly.FromDateTime(x.RequestedAtUtc) >= from);
        if (query.To is DateOnly to) source = source.Where(x => DateOnly.FromDateTime(x.RequestedAtUtc) <= to);
        var total = await source.CountAsync(ct);
        var rows = await source.OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.LoanNumber)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new LoanRegisterRowDto(
                x.Id, x.LoanNumber, x.EmployeeId,
                x.Employee!.EmployeeCode ?? string.Empty,
                (x.Employee.FirstName + " " + x.Employee.LastName).Trim(),
                x.LoanProduct!.Code, x.LoanProduct.ProductType,
                x.ApprovedAmount ?? 0m,
                x.Repayments.Sum(r => r.PrincipalAmount),
                x.Repayments.Sum(r => r.InterestAmount),
                x.Repayments.Sum(r => r.Amount),
                x.OutstandingPrincipal, x.OutstandingInterest, x.OutstandingTotal,
                x.Installments.Where(i => i.Status == LoanInstallmentStatus.Scheduled || i.Status == LoanInstallmentStatus.PartiallyRecovered).OrderBy(i => i.DueDate).Select(i => (DateOnly?)i.DueDate).FirstOrDefault(),
                x.Status))
            .ToListAsync(ct);
        return Result<PagedResult<LoanRegisterRowDto>>.Success(new PagedResult<LoanRegisterRowDto>(rows, query.Page, query.PageSize, total));
    }

    public async Task<Result<EmployeeLoanDto>> CreateLoanAsync(LoanRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<EmployeeLoanDto>.Unauthorized("No authenticated tenant.");
        if (request.RequestedAmount <= 0 || request.RequestedTenureMonths <= 0) return Result<EmployeeLoanDto>.Invalid("request", "Amount and tenure must be positive.");
        var product = await db.LoanProducts.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == request.LoanProductId && x.IsActive, ct); if (product is null) return Result<EmployeeLoanDto>.NotFound("Active loan product not found.");
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == request.EmployeeId, ct); if (employee is null) return Result<EmployeeLoanDto>.NotFound("Employee not found.");
        if (product.MinAmount.HasValue && request.RequestedAmount < product.MinAmount || product.MaxAmount.HasValue && request.RequestedAmount > product.MaxAmount) return Result<EmployeeLoanDto>.Invalid("requestedAmount", "Requested amount is outside product limits.");
        if (product.MinTenureMonths.HasValue && request.RequestedTenureMonths < product.MinTenureMonths || product.MaxTenureMonths.HasValue && request.RequestedTenureMonths > product.MaxTenureMonths) return Result<EmployeeLoanDto>.Invalid("requestedTenureMonths", "Requested tenure is outside product limits.");
        var version = await db.LoanProductVersions.AsNoTracking().Where(x => x.TenantId == tid && x.LoanProductId == product.Id && x.Status == LoanProductVersionStatus.Active && x.EffectiveFrom <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) && (x.EffectiveTo == null || x.EffectiveTo >= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        var loan = new EmployeeLoan { Id = Guid.NewGuid(), TenantId = tid, EmployeeId = employee.Id, LoanProductId = product.Id, LoanProductVersionId = version?.Id ?? Guid.Empty, LoanNumber = $"{(product.ProductType == LoanProductType.SalaryAdvance ? "ADV" : "LN")}/{clock.GetUtcNow().Year}/{Guid.NewGuid():N}"[..22].ToUpperInvariant(), RequestedAmount = request.RequestedAmount, CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(), RequestedTenureMonths = request.RequestedTenureMonths, InterestMethod = version?.InterestMethod ?? product.InterestMethod, InterestRate = version?.InterestRate ?? product.InterestRate ?? 0, RecoveryPolicy = version?.RecoveryPolicy ?? product.RecoveryPolicy, RequestedAtUtc = clock.GetUtcNow().UtcDateTime, RequestedByUserId = tenant.UserId, Status = product.RequiresApproval ? LoanStatus.Draft : LoanStatus.Approved, ApprovedAmount = product.RequiresApproval ? null : request.RequestedAmount, ApprovedTenureMonths = product.RequiresApproval ? null : request.RequestedTenureMonths };
        loan.OutstandingPrincipal = request.RequestedAmount; loan.OutstandingTotal = request.RequestedAmount; db.EmployeeLoans.Add(loan); AddHistory(loan, LoanHistoryEventType.Created, null, loan.Status, null); await db.SaveChangesAsync(ct); return Result<EmployeeLoanDto>.Success(ToDto(loan), "Loan request created.");
    }

    public Task<Result<EmployeeLoanDto>> SubmitAsync(Guid id, CancellationToken ct = default) => TransitionAsync(id, LoanStatus.Submitted, LoanStatus.Draft, null, LoanHistoryEventType.Submitted, ct);
    public async Task<Result<EmployeeLoanDto>> ApproveAsync(Guid id, decimal? approvedAmount = null, int? approvedTenureMonths = null, CancellationToken ct = default)
    {
        var loan = await db.EmployeeLoans.Include(x => x.Installments).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct); if (loan is null) return Result<EmployeeLoanDto>.NotFound("Loan not found."); if (loan.Status != LoanStatus.Submitted) return Result<EmployeeLoanDto>.Conflict("Only submitted loans can be approved."); var guard = await (approvalGuard ?? new PayrollApprovalGuard(db, tenant)).ValidateAsync(loan.RequestedByUserId, "approve", ct: ct); if (!guard.Succeeded) return Result<EmployeeLoanDto>.Failure(guard.Status, guard.Message, guard.Errors);
        loan.ApprovedAmount = approvedAmount ?? loan.RequestedAmount; loan.ApprovedTenureMonths = approvedTenureMonths ?? loan.RequestedTenureMonths; loan.OutstandingPrincipal = loan.ApprovedAmount.Value; loan.OutstandingTotal = loan.ApprovedAmount.Value; loan.Status = LoanStatus.Approved; loan.ApprovedAtUtc = clock.GetUtcNow().UtcDateTime; loan.ApprovedByUserId = tenant.UserId; AddHistory(loan, LoanHistoryEventType.Approved, LoanStatus.Submitted, LoanStatus.Approved, loan.ApprovedAmount); await db.SaveChangesAsync(ct); return Result<EmployeeLoanDto>.Success(ToDto(loan));
    }

    public async Task<Result<EmployeeLoanDto>> RejectAsync(Guid id, string reason, CancellationToken ct = default) { var loan = await db.EmployeeLoans.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct); if (loan is null) return Result<EmployeeLoanDto>.NotFound("Loan not found."); if (loan.Status is not LoanStatus.Submitted and not LoanStatus.Draft) return Result<EmployeeLoanDto>.Conflict("Loan cannot be rejected in its current state."); if (string.IsNullOrWhiteSpace(reason)) return Result<EmployeeLoanDto>.Invalid("reason", "Rejection reason is required."); loan.Status = LoanStatus.Rejected; loan.RejectionReason = reason.Trim(); loan.RejectedAtUtc = clock.GetUtcNow().UtcDateTime; loan.RejectedByUserId = tenant.UserId; AddHistory(loan, LoanHistoryEventType.Rejected, null, LoanStatus.Rejected, null, reason); await db.SaveChangesAsync(ct); return Result<EmployeeLoanDto>.Success(ToDto(loan)); }
    public async Task<Result<EmployeeLoanDto>> RecordDisbursementAsync(Guid id, decimal? amount = null, CancellationToken ct = default) { var loan = await db.EmployeeLoans.Include(x => x.Installments).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct); if (loan is null) return Result<EmployeeLoanDto>.NotFound("Loan not found."); if (loan.Status != LoanStatus.Approved) return Result<EmployeeLoanDto>.Conflict("Only approved loans can be recorded as disbursed."); var disbursed = amount ?? loan.ApprovedAmount ?? loan.RequestedAmount; if (disbursed <= 0 || disbursed > (loan.ApprovedAmount ?? 0)) return Result<EmployeeLoanDto>.Invalid("amount", "Disbursed amount is invalid."); loan.DisbursedAmount = disbursed; loan.Status = LoanStatus.Active; loan.DisbursedAtUtc = clock.GetUtcNow().UtcDateTime; var hadSchedule = loan.Installments.Count > 0; BuildSchedule(loan, disbursed); if (!hadSchedule) db.LoanInstallments.AddRange(loan.Installments); loan.OutstandingPrincipal = disbursed; loan.OutstandingInterest = loan.Installments.Sum(x => x.InterestAmount); loan.OutstandingTotal = loan.OutstandingPrincipal + loan.OutstandingInterest; loan.ConcurrencyVersion++; AddHistory(loan, LoanHistoryEventType.Disbursed, LoanStatus.Approved, LoanStatus.Active, disbursed); AddHistory(loan, LoanHistoryEventType.Activated, LoanStatus.Approved, LoanStatus.Active, disbursed); await db.SaveChangesAsync(ct); return Result<EmployeeLoanDto>.Success(ToDto(loan), "Loan disbursement recorded."); }
    public Task<Result<EmployeeLoanDto>> CancelAsync(Guid id, string? reason = null, CancellationToken ct = default) => CancelCoreAsync(id, reason, ct);
    public async Task<Result<EmployeeLoanDto>> RecordRepaymentAsync(Guid id, LoanRepaymentRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<EmployeeLoanDto>.Unauthorized("No authenticated tenant.");
        if (request.Amount <= 0) return Result<EmployeeLoanDto>.Invalid("amount", "Repayment amount must be positive.");
        var loan = await db.EmployeeLoans.Include(x => x.Installments).FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        if (loan is null) return Result<EmployeeLoanDto>.NotFound("Loan not found.");
        if (loan.Status != LoanStatus.Active) return Result<EmployeeLoanDto>.Conflict("Only active loans can receive repayments.");
        if (request.Amount > loan.OutstandingTotal) return Result<EmployeeLoanDto>.Invalid("amount", "Repayment cannot exceed the outstanding balance.");
        var interest = Math.Min(request.Amount, loan.OutstandingInterest);
        var principal = request.Amount - interest;
        db.LoanRepayments.Add(new LoanRepayment { Id = Guid.NewGuid(), TenantId = tid, EmployeeLoanId = loan.Id, Amount = request.Amount, PrincipalAmount = principal, InterestAmount = interest, RepaymentType = LoanRepaymentType.Manual, PaymentDate = request.PaymentDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), SourceType = "Manual", Reference = request.Reference?.Trim(), CreatedByUserId = tenant.UserId ?? Guid.Empty });
        loan.OutstandingPrincipal = Math.Max(0m, loan.OutstandingPrincipal - principal);
        loan.OutstandingInterest = Math.Max(0m, loan.OutstandingInterest - interest);
        loan.OutstandingTotal = loan.OutstandingPrincipal + loan.OutstandingInterest;
        var previous = loan.Status;
        if (loan.OutstandingTotal == 0) { loan.Status = LoanStatus.Closed; loan.ClosedAtUtc = clock.GetUtcNow().UtcDateTime; loan.ClosedByUserId = tenant.UserId; loan.ClosureReason = "Fully repaid."; }
        loan.ConcurrencyVersion++;
        AddHistory(loan, LoanHistoryEventType.ManualRepayment, previous, loan.Status, request.Amount, request.Reference);
        await db.SaveChangesAsync(ct);
        return Result<EmployeeLoanDto>.Success(ToDto(loan), "Repayment recorded.");
    }

    public async Task<Result<EmployeeLoanDto>> PartialPrepayAsync(Guid id, LoanRepaymentRequest request, CancellationToken ct = default)
    {
        var loan = await db.EmployeeLoans.Include(x => x.Installments).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct);
        if (loan is null) return Result<EmployeeLoanDto>.NotFound("Loan not found.");
        if (loan.Status != LoanStatus.Active) return Result<EmployeeLoanDto>.Conflict("Only active loans can be prepaid.");
        var product = await db.LoanProducts.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == loan.TenantId && x.Id == loan.LoanProductId, ct);
        if (product?.AllowPartialPrepayment != true) return Result<EmployeeLoanDto>.Conflict("Partial prepayment is not enabled for this product.");
        if (request.Amount <= 0 || request.Amount > loan.OutstandingPrincipal) return Result<EmployeeLoanDto>.Invalid("amount", "Prepayment must be positive and no greater than outstanding principal.");
        db.LoanRepayments.Add(new LoanRepayment { Id = Guid.NewGuid(), TenantId = loan.TenantId, EmployeeLoanId = loan.Id, Amount = request.Amount, PrincipalAmount = request.Amount, InterestAmount = 0, RepaymentType = LoanRepaymentType.PartialPrepayment, PaymentDate = request.PaymentDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), SourceType = "PartialPrepayment", Reference = request.Reference?.Trim(), CreatedByUserId = tenant.UserId ?? Guid.Empty });
        loan.OutstandingPrincipal -= request.Amount; loan.OutstandingTotal = loan.OutstandingPrincipal + loan.OutstandingInterest; loan.ConcurrencyVersion++;
        AddHistory(loan, LoanHistoryEventType.PartialPrepayment, LoanStatus.Active, LoanStatus.Active, request.Amount, request.Reference);
        await db.SaveChangesAsync(ct);
        return Result<EmployeeLoanDto>.Success(ToDto(loan), "Partial prepayment recorded.");
    }

    public async Task<Result<EmployeeLoanDto>> CloseAsync(Guid id, LoanRepaymentRequest request, CancellationToken ct = default)
    {
        var loan = await db.EmployeeLoans.Include(x => x.Installments).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct);
        if (loan is null) return Result<EmployeeLoanDto>.NotFound("Loan not found.");
        if (loan.Status != LoanStatus.Active) return Result<EmployeeLoanDto>.Conflict("Only active loans can be closed.");
        var product = await db.LoanProducts.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == loan.TenantId && x.Id == loan.LoanProductId, ct);
        if (product?.AllowEarlyClosure != true) return Result<EmployeeLoanDto>.Conflict("Early closure is not enabled for this product.");
        var closureAmount = loan.OutstandingTotal;
        if (request.Amount > 0 && request.Amount != closureAmount) return Result<EmployeeLoanDto>.Invalid("amount", $"Early closure requires the current outstanding amount of {closureAmount:0.00}.");
        db.LoanRepayments.Add(new LoanRepayment { Id = Guid.NewGuid(), TenantId = loan.TenantId, EmployeeLoanId = loan.Id, Amount = closureAmount, PrincipalAmount = loan.OutstandingPrincipal, InterestAmount = loan.OutstandingInterest, RepaymentType = LoanRepaymentType.EarlyClosure, PaymentDate = request.PaymentDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), SourceType = "EarlyClosure", Reference = request.Reference?.Trim(), CreatedByUserId = tenant.UserId ?? Guid.Empty });
        loan.OutstandingPrincipal = 0; loan.OutstandingInterest = 0; loan.OutstandingTotal = 0; loan.Status = LoanStatus.Closed; loan.ClosedAtUtc = clock.GetUtcNow().UtcDateTime; loan.ClosedByUserId = tenant.UserId; loan.ClosureReason = request.Reference?.Trim() ?? "Early closure."; loan.ConcurrencyVersion++;
        foreach (var installment in loan.Installments.Where(x => x.Status == LoanInstallmentStatus.Scheduled)) installment.Status = LoanInstallmentStatus.Cancelled;
        AddHistory(loan, LoanHistoryEventType.EarlyClosure, LoanStatus.Active, LoanStatus.Closed, closureAmount, loan.ClosureReason);
        await db.SaveChangesAsync(ct);
        return Result<EmployeeLoanDto>.Success(ToDto(loan), "Loan closed.");
    }

    public async Task<Result<IReadOnlyList<LoanInstallmentDto>>> GetScheduleAsync(Guid id, CancellationToken ct = default) { var loan = await db.EmployeeLoans.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct); if (loan is null) return Result<IReadOnlyList<LoanInstallmentDto>>.NotFound("Loan not found."); var rows = await db.LoanInstallments.AsNoTracking().Where(x => x.TenantId == loan.TenantId && x.EmployeeLoanId == loan.Id).OrderBy(x => x.InstallmentNumber).Select(x => new LoanInstallmentDto(x.Id, x.InstallmentNumber, x.DueDate, x.OpeningPrincipal, x.PrincipalAmount, x.InterestAmount, x.InstallmentAmount, x.ClosingPrincipal, x.Status, x.RecoveredAmount)).ToListAsync(ct); return Result<IReadOnlyList<LoanInstallmentDto>>.Success(rows); }

    private async Task<Result<EmployeeLoanDto>> TransitionAsync(Guid id, LoanStatus next, LoanStatus required, decimal? amount, LoanHistoryEventType eventType, CancellationToken ct) { var loan = await db.EmployeeLoans.Include(x => x.Installments).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct); if (loan is null) return Result<EmployeeLoanDto>.NotFound("Loan not found."); if (loan.Status != required) return Result<EmployeeLoanDto>.Conflict($"Loan must be {required}."); var previous = loan.Status; loan.Status = next; loan.ConcurrencyVersion++; AddHistory(loan, eventType, previous, next, amount); await db.SaveChangesAsync(ct); return Result<EmployeeLoanDto>.Success(ToDto(loan)); }
    private async Task<Result<EmployeeLoanDto>> CancelCoreAsync(Guid id, string? reason, CancellationToken ct) { var loan = await db.EmployeeLoans.Include(x => x.Installments).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct); if (loan is null) return Result<EmployeeLoanDto>.NotFound("Loan not found."); if (loan.Status is LoanStatus.Active or LoanStatus.Closed) return Result<EmployeeLoanDto>.Conflict("Active or closed loans cannot be cancelled."); var guard = await (approvalGuard ?? new PayrollApprovalGuard(db, tenant)).ValidateAsync(loan.RequestedByUserId, "cancel", reason, ct); if (!guard.Succeeded) return Result<EmployeeLoanDto>.Failure(guard.Status, guard.Message, guard.Errors); var previous = loan.Status; loan.Status = LoanStatus.Cancelled; AddHistory(loan, LoanHistoryEventType.Cancelled, previous, LoanStatus.Cancelled, null, reason); await db.SaveChangesAsync(ct); return Result<EmployeeLoanDto>.Success(ToDto(loan)); }
    private void BuildSchedule(EmployeeLoan loan, decimal principal) { if (loan.Installments.Count > 0) return; var count = loan.ApprovedTenureMonths ?? loan.RequestedTenureMonths; var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime); var remaining = principal; var monthlyRate = loan.InterestRate / 1200m; var factor = Pow(1m + monthlyRate, count); var emi = loan.InterestMethod == LoanInterestMethod.ReducingBalance && monthlyRate > 0 ? decimal.Round(principal * monthlyRate * factor / (factor - 1m), 2, MidpointRounding.AwayFromZero) : 0m; var interestTotal = loan.InterestMethod == LoanInterestMethod.Flat ? decimal.Round(principal * loan.InterestRate / 100m * count / 12m, 2, MidpointRounding.AwayFromZero) : 0m; for (var i = 1; i <= count; i++) { var opening = remaining; var interest = loan.InterestMethod switch { LoanInterestMethod.Flat => decimal.Round(interestTotal / count, 2, MidpointRounding.AwayFromZero), LoanInterestMethod.ReducingBalance => decimal.Round(opening * monthlyRate, 2, MidpointRounding.AwayFromZero), _ => 0m }; var principalPart = i == count ? remaining : loan.InterestMethod == LoanInterestMethod.ReducingBalance && monthlyRate > 0 ? decimal.Round(Math.Max(0m, emi - interest), 2, MidpointRounding.AwayFromZero) : decimal.Round(principal / count, 2, MidpointRounding.AwayFromZero); var installment = decimal.Round(principalPart + interest, 2, MidpointRounding.AwayFromZero); remaining = decimal.Round(Math.Max(0m, remaining - principalPart), 2, MidpointRounding.AwayFromZero); loan.Installments.Add(new LoanInstallment { Id = Guid.NewGuid(), TenantId = loan.TenantId, EmployeeLoanId = loan.Id, InstallmentNumber = i, DueDate = today.AddMonths(i), OpeningPrincipal = opening, PrincipalAmount = principalPart, InterestAmount = interest, InstallmentAmount = installment, ClosingPrincipal = remaining }); } }
    private static decimal Pow(decimal value, int exponent) { var result = 1m; for (var i = 0; i < exponent; i++) result *= value; return result; }
    private void AddHistory(EmployeeLoan loan, LoanHistoryEventType type, LoanStatus? previous, LoanStatus? next, decimal? amount, string? reason = null) => db.LoanHistories.Add(new LoanHistory { Id = Guid.NewGuid(), TenantId = loan.TenantId, EmployeeLoanId = loan.Id, EventType = type, PreviousStatus = previous, NewStatus = next, Amount = amount, Reason = reason, ActorUserId = tenant.UserId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime });
    private static LoanProductDto ToDto(LoanProduct x) => new(x.Id, x.Code, x.Name, x.ProductType, x.IsActive, x.CurrencyCode, x.MinAmount, x.MaxAmount, x.MinTenureMonths, x.MaxTenureMonths, x.InterestMethod, x.InterestRate, x.RecoveryPolicy);
    private static EmployeeLoanDto ToDto(EmployeeLoan x) => new(x.Id, x.EmployeeId, x.LoanProductId, x.LoanNumber, x.RequestedAmount, x.ApprovedAmount, x.DisbursedAmount, x.RequestedTenureMonths, x.ApprovedTenureMonths, x.Status, x.OutstandingPrincipal, x.OutstandingInterest, x.OutstandingTotal, x.CurrencyCode, x.Installments.OrderBy(i => i.InstallmentNumber).Select(i => new LoanInstallmentDto(i.Id, i.InstallmentNumber, i.DueDate, i.OpeningPrincipal, i.PrincipalAmount, i.InterestAmount, i.InstallmentAmount, i.ClosingPrincipal, i.Status, i.RecoveredAmount)).ToList());
}
