using HRMS.Application.Abstractions;

namespace HRMS.Application.Services;

/// <summary>Neutral default until a tenant-specific timezone setting is available.</summary>
public sealed class AttendanceBusinessTimeZoneProvider : IAttendanceBusinessTimeZoneProvider
{
    public TimeZoneInfo GetTimeZone(Guid tenantId) => TimeZoneInfo.Utc;
}
