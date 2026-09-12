using HRMS.Application.Abstractions;

namespace HRMS.Application.Services;

public sealed class AttendanceBusinessDateResolver : IAttendanceBusinessDateResolver
{
    public DateOnly Resolve(DateTime punchAtUtc, DateOnly candidateDate, TimeOnly shiftStart, TimeOnly shiftEnd, bool crossesMidnight, int postShiftMinutes, TimeZoneInfo timeZone)
    {
        if (!crossesMidnight) return candidateDate;
        var time = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(punchAtUtc, DateTimeKind.Utc), timeZone));
        var cutoff = shiftEnd.AddMinutes(Math.Max(0, postShiftMinutes));
        return time <= cutoff && time < shiftStart ? candidateDate.AddDays(-1) : candidateDate;
    }
}
