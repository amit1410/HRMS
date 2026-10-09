using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// CRUD for the per-tenant confirmation scheduler settings and a read of the execution log. The settings row
/// is created on first update (upsert); until then <see cref="GetAsync"/> reports the global defaults with
/// <c>IsConfigured = false</c>.
/// </summary>
public sealed class ConfirmationSchedulerSettingService : IConfirmationSchedulerSettingService
{
    private const string NoTenant = "No authenticated tenant.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ConfirmationReminderOptions _options;

    public ConfirmationSchedulerSettingService(IHrmsDbContext db, ITenantContext tenant, ConfirmationReminderOptions options)
    {
        _db = db;
        _tenant = tenant;
        _options = options;
    }

    public async Task<Result<ConfirmationSchedulerSettingDto>> GetAsync(CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<ConfirmationSchedulerSettingDto>.Unauthorized(NoTenant);
        var setting = await _db.ConfirmationSchedulerSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return Result<ConfirmationSchedulerSettingDto>.Success(ToDto(setting));
    }

    public async Task<Result<ConfirmationSchedulerSettingDto>> UpdateAsync(ConfirmationSchedulerSettingRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationSchedulerSettingDto>.Unauthorized(NoTenant);

        var errors = Validate(request);
        if (errors.Count != 0) return Result<ConfirmationSchedulerSettingDto>.Invalid("One or more fields are invalid.", errors);

        var setting = await _db.ConfirmationSchedulerSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (setting is null)
        {
            setting = new ConfirmationSchedulerSetting { Id = Guid.NewGuid(), TenantId = tenantId };
            _db.ConfirmationSchedulerSettings.Add(setting);
        }

        setting.Enabled = request.Enabled;
        setting.FrequencyMinutes = request.FrequencyMinutes;
        setting.DefaultTriggerDaysBeforeEnd = request.DefaultTriggerDaysBeforeEnd;
        setting.MaxAttempts = request.MaxAttempts;
        setting.ClaimLeaseMinutes = request.ClaimLeaseMinutes;
        setting.ResendProcessed = request.ResendProcessed;
        setting.NotifyEmployee = request.NotifyEmployee;
        setting.NotifyManager = request.NotifyManager;
        setting.NotifyHrbp = request.NotifyHrbp;
        setting.ModifiedBy = _tenant.UserId?.ToString();

        await _db.SaveChangesAsync(ct);
        return Result<ConfirmationSchedulerSettingDto>.Success(ToDto(setting), "Confirmation scheduler settings saved.");
    }

    public async Task<Result<PagedResult<ConfirmationReminderDeliveryDto>>> GetExecutionLogAsync(ConfirmationReminderLogQuery query, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<PagedResult<ConfirmationReminderDeliveryDto>>.Unauthorized(NoTenant);

        var source = _db.ConfirmationReminderDeliveries.AsNoTracking();
        if (query.EmployeeId is Guid employeeId) source = source.Where(x => x.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(query.Status)) { var s = query.Status.Trim(); source = source.Where(x => x.Status == s); }

        var page = await source
            .OrderByDescending(x => x.CreatedDate).ThenBy(x => x.Id)
            .Select(x => new ConfirmationReminderDeliveryDto(
                x.Id, x.EmployeeId, x.ConfirmationPolicyId, x.ConfirmationPolicyVersionId,
                x.ProbationEndDate, x.TriggerDate, x.RecipientType, x.RecipientEmployeeId, x.RecipientEmail,
                x.Status, x.AttemptCount, x.SentAtUtc, x.ProcessedAtUtc, x.NextAttemptAtUtc, x.LastError, x.CreatedDate))
            .ToPagedResultAsync(query, ct);
        return Result<PagedResult<ConfirmationReminderDeliveryDto>>.Success(page);
    }

    private static List<ValidationError> Validate(ConfirmationSchedulerSettingRequest request)
    {
        var errors = new List<ValidationError>();
        if (request.FrequencyMinutes < 1) errors.Add(new("frequencyMinutes", "Frequency must be at least one minute."));
        if (request.DefaultTriggerDaysBeforeEnd < 0) errors.Add(new("defaultTriggerDaysBeforeEnd", "Trigger days cannot be negative."));
        if (request.MaxAttempts is < 1) errors.Add(new("maxAttempts", "Max attempts must be at least one when set."));
        if (request.ClaimLeaseMinutes is < 1) errors.Add(new("claimLeaseMinutes", "Claim lease must be at least one minute when set."));
        return errors;
    }

    private ConfirmationSchedulerSettingDto ToDto(ConfirmationSchedulerSetting? s) =>
        s is null
            ? new ConfirmationSchedulerSettingDto(false, true, _options.DefaultFrequencyMinutes, _options.DefaultTriggerDaysBeforeEnd,
                _options.MaxAttempts, _options.ClaimLeaseMinutes, false, true, true, true, null, null, null)
            : new ConfirmationSchedulerSettingDto(true, s.Enabled, s.FrequencyMinutes, s.DefaultTriggerDaysBeforeEnd,
                s.MaxAttempts, s.ClaimLeaseMinutes, s.ResendProcessed, s.NotifyEmployee, s.NotifyManager, s.NotifyHrbp,
                s.LastRunAtUtc, s.ModifiedDate, s.ModifiedBy);
}
