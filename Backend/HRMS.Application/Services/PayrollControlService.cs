using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollControlService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock) : IPayrollControlService
{
    public async Task<Result<PayrollControlConfigurationDto>> GetAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollControlConfigurationDto>.Unauthorized("No authenticated tenant.");
        var row = await db.PayrollControlConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        return Result<PayrollControlConfigurationDto>.Success(ToDto(row ?? new PayrollControlConfiguration { Id = Guid.Empty, TenantId = tenantId, UpdatedAtUtc = clock.GetUtcNow().UtcDateTime }));
    }

    public async Task<Result<PayrollControlConfigurationDto>> UpdateAsync(PayrollControlConfigurationRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollControlConfigurationDto>.Unauthorized("No authenticated tenant.");
        var row = await db.PayrollControlConfigurations.SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (row is null)
        {
            row = new PayrollControlConfiguration { Id = Guid.NewGuid(), TenantId = tenantId };
            db.PayrollControlConfigurations.Add(row);
        }
        row.RequireMakerChecker = request.RequireMakerChecker;
        row.PreventSelfApproval = request.PreventSelfApproval;
        row.RequireReasonForReopen = request.RequireReasonForReopen;
        row.RequireReasonForCancellation = request.RequireReasonForCancellation;
        row.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        row.UpdatedByUserId = tenant.UserId;
        await db.SaveChangesAsync(ct);
        return Result<PayrollControlConfigurationDto>.Success(ToDto(row));
    }

    private static PayrollControlConfigurationDto ToDto(PayrollControlConfiguration row) => new(row.Id, row.RequireMakerChecker, row.PreventSelfApproval, row.RequireReasonForReopen, row.RequireReasonForCancellation, row.UpdatedAtUtc, row.UpdatedByUserId);
}
