using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class EmployeeAuditService : IEmployeeAuditService
{
    private const string NoTenantMessage = "No authenticated tenant.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeAuditService> _logger;

    public EmployeeAuditService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeAuditService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<PagedResult<EmployeeAuditLogDto>>> GetAsync(
        Guid employeeId, AuditQuery query, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<PagedResult<EmployeeAuditLogDto>>.Unauthorized(NoTenantMessage);
        }

        var logs = _db.EmployeeAuditLogs.AsNoTracking()
            .Where(l => l.EmployeeId == employeeId);

        if (query.DateFrom is DateOnly from)
        {
            logs = logs.Where(l => l.EffectiveDate >= from || (l.EffectiveDate == null && l.CreatedDate >= from.ToDateTime(TimeOnly.MinValue)));
        }

        if (query.DateTo is DateOnly to)
        {
            logs = logs.Where(l => l.EffectiveDate <= to || (l.EffectiveDate == null && l.CreatedDate <= to.ToDateTime(TimeOnly.MaxValue)));
        }

        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            var module = query.Module.Trim().ToLowerInvariant();
            logs = logs.Where(l => l.Module.ToLower() == module);
        }

        if (!string.IsNullOrWhiteSpace(query.Section))
        {
            var section = query.Section.Trim().ToLowerInvariant();
            logs = logs.Where(l => l.Section != null && l.Section.ToLower() == section);
        }

        if (query.ChangeType is AuditChangeType changeType)
        {
            logs = logs.Where(l => l.ChangeType == changeType);
        }

        if (!string.IsNullOrWhiteSpace(query.User))
        {
            var user = query.User.Trim().ToLowerInvariant();
            logs = logs.Where(l => l.ChangedBy.ToLower().Contains(user));
        }

        var ordered = logs.OrderByDescending(l => l.CreatedDate).ThenByDescending(l => l.Id);

        var page = await ordered
            .Select(l => new EmployeeAuditLogDto(
                l.Id,
                l.EmployeeId,
                l.EmployeeCode,
                l.Module,
                l.Section,
                l.EntityName,
                l.RecordId,
                l.FieldName,
                l.OldValue,
                l.NewValue,
                l.ChangeType,
                l.EffectiveDate,
                l.ChangedBy,
                l.Reason,
                l.Source,
                l.ImportBatchId,
                l.IpAddress,
                l.CreatedDate))
            .ToPagedResultAsync(query, cancellationToken);

        return Result<PagedResult<EmployeeAuditLogDto>>.Success(page);
    }

    public async Task LogChangeAsync(
        Guid employeeId,
        string employeeCode,
        string module,
        string? section,
        string? entityName,
        Guid? recordId,
        string? fieldName,
        string? oldValue,
        string? newValue,
        AuditChangeType changeType,
        string changedBy,
        DateOnly? effectiveDate = null,
        string? reason = null,
        string? source = null,
        Guid? importBatchId = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            _logger.LogWarning("Attempted to log audit change without a tenant context.");
            return;
        }

        var entry = new EmployeeAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            EmployeeCode = Normalize(employeeCode),
            Module = module,
            Section = Normalize(section),
            EntityName = Normalize(entityName),
            RecordId = recordId,
            FieldName = Normalize(fieldName),
            OldValue = Normalize(oldValue),
            NewValue = Normalize(newValue),
            ChangeType = changeType,
            EffectiveDate = effectiveDate,
            ChangedBy = changedBy,
            Reason = Normalize(reason),
            Source = Normalize(source),
            ImportBatchId = importBatchId,
            IpAddress = Normalize(ipAddress)
        };

        _db.EmployeeAuditLogs.Add(entry);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Failed to write audit log entry for employee {EmployeeId}, module {Module}.",
                employeeId, module);
        }
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
