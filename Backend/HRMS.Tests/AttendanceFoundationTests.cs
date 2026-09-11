using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class AttendanceFoundationTests
{
    [Fact]
    public void Attendance_configuration_is_tenant_filtered_and_separates_breaks_and_roster()
    {
        using var db = new SqliteInMemoryDatabase();
        using var context = db.CreateContext(new TestTenantContext(Guid.NewGuid()));

        Assert.NotNull(context.Model.FindEntityType(typeof(Shift))?.GetQueryFilter());
        Assert.NotNull(context.Model.FindEntityType(typeof(ShiftBreak))?.GetQueryFilter());
        Assert.NotNull(context.Model.FindEntityType(typeof(EmployeeRosterDay))?.GetQueryFilter());
        Assert.NotNull(context.Model.FindEntityType(typeof(EmployeeRosterChangeHistory))?.GetQueryFilter());
        Assert.Contains(context.Model.FindEntityType(typeof(EmployeeRosterDay))!.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "EmployeeId", "RosterDate"]));
    }

    [Fact]
    public void Shift_supports_normalized_capture_and_post_shift_rules()
    {
        var shift = new Shift { CaptureMode = AttendanceCaptureMode.Mixed, AllowedAttendanceSources = AttendanceSource.Biometric | AttendanceSource.Portal, PostShiftMarkOutMode = PostShiftMarkOutMode.RequiresApprovalBeyondLimit, CrossesMidnight = true };

        Assert.True(shift.AllowedAttendanceSources.HasFlag(AttendanceSource.Portal));
        Assert.Equal(PostShiftMarkOutMode.RequiresApprovalBeyondLimit, shift.PostShiftMarkOutMode);
        Assert.True(shift.CrossesMidnight);
    }
}
