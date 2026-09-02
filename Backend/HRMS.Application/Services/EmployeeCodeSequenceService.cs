using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class EmployeeCodeSequenceService : IEmployeeCodeSequenceService
{
    private const int MaxAttempts = 8;
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;

    public EmployeeCodeSequenceService(IHrmsDbContext db, ITenantContext tenant) { _db = db; _tenant = tenant; }

    public async Task<Result<long>> AllocateAsync(Guid ruleId, EmployeeCodeSequenceScope scope, string scopeKey, EmployeeCodeResetPeriod resetPeriod, string periodKey, long startNumber = 1, int incrementBy = 1, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<long>.Unauthorized("No authenticated tenant.");
        if (ruleId == Guid.Empty || string.IsNullOrWhiteSpace(scopeKey) || string.IsNullOrWhiteSpace(periodKey) || startNumber < 1 || incrementBy < 1)
            return Result<long>.Invalid("Invalid Employee Code sequence parameters.");

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var current = await _db.EmployeeCodeSequences.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId && s.EmployeeCodeRuleId == ruleId && s.Scope == scope && s.ScopeKey == scopeKey && s.PeriodKey == periodKey, cancellationToken);
            if (current is null)
            {
                try
                {
                    _db.EmployeeCodeSequences.Add(new EmployeeCodeSequence { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCodeRuleId = ruleId, Scope = scope, ScopeKey = scopeKey, NextNumber = checked(startNumber + incrementBy), IncrementBy = incrementBy, ResetPeriod = resetPeriod, PeriodKey = periodKey });
                    await _db.SaveChangesAsync(cancellationToken);
                    return Result<long>.Success(startNumber);
                }
                catch (DbUpdateException) when (attempt + 1 < MaxAttempts) { continue; }
            }
            else
            {
                var allocated = current.NextNumber;
                var rows = await _db.EmployeeCodeSequences.Where(s => s.Id == current.Id && s.TenantId == tenantId && s.NextNumber == allocated).ExecuteUpdateAsync(setters => setters.SetProperty(s => s.NextNumber, allocated + current.IncrementBy), cancellationToken);
                if (rows == 1) return Result<long>.Success(allocated);
            }
        }
        return Result<long>.Conflict("Employee Code sequence is busy; please retry.");
    }
}
