using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HRMS.Tests;

public sealed class LeaveRequestSchemaFoundationTests
{
    [Fact]
    public void Request_schema_has_tenant_safe_keys_precision_and_rowversion()
    {
        using var db = new SqliteInMemoryDatabase();
        using var context = db.CreateContext(new TestTenantContext(Guid.NewGuid()));
        var request = context.Model.FindEntityType(typeof(LeaveRequest))!;
        var day = context.Model.FindEntityType(typeof(LeaveRequestDay))!;

        Assert.NotNull(request.GetQueryFilter());
        Assert.NotNull(day.GetQueryFilter());
        Assert.Contains(request.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(["TenantId", "EmployeeId", "IdempotencyKey"]));
        Assert.Contains(day.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(["TenantId", "LeaveRequestId", "Date"]));
        Assert.Equal(9, request.FindProperty(nameof(LeaveRequest.RequestedQuantity))!.GetPrecision());
        Assert.Equal(3, request.FindProperty(nameof(LeaveRequest.RequestedQuantity))!.GetScale());
        Assert.Equal(9, day.FindProperty(nameof(LeaveRequestDay.ChargeableQuantity))!.GetPrecision());
        Assert.Equal(3, day.FindProperty(nameof(LeaveRequestDay.ChargeableQuantity))!.GetScale());
        Assert.True(request.FindProperty(nameof(LeaveRequest.RowVersion))!.IsConcurrencyToken);
        var designRequest = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LeaveRequest))!;
        Assert.Contains(designRequest.GetCheckConstraints(), x => x.Name == "CK_LeaveRequests_DateAndQuantity");
    }

    [Fact]
    public void Request_references_are_tenant_aware_and_historical_children_are_not_cascaded()
    {
        using var db = new SqliteInMemoryDatabase();
        using var context = db.CreateContext(new TestTenantContext(Guid.NewGuid()));
        var request = context.Model.FindEntityType(typeof(LeaveRequest))!;
        var day = context.Model.FindEntityType(typeof(LeaveRequestDay))!;
        var eventType = context.Model.FindEntityType(typeof(LeaveRequestEvent))!;

        Assert.Contains(request.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Employee)
            && fk.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "EmployeeId"]));
        Assert.Contains(request.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(LeavePolicyRule)
            && fk.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "LeavePolicyVersionId", "LeavePolicyRuleId"]));
        Assert.Contains(request.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(EmployeeEmploymentHistory)
            && fk.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "EmployeeId", "EmployeeEmploymentHistoryId"]));
        Assert.All(request.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.All(day.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.All(eventType.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Null(context.Model.FindEntityType("HRMS.Domain.Entities.LeaveRequestAttachment"));
    }

    [Fact]
    public void Request_schema_has_only_the_approved_event_type_values()
    {
        Assert.Equal(["PendingApproval", "Approved", "Rejected", "Withdrawn", "Cancelled"],
            Enum.GetNames<LeaveRequestStatus>());
        Assert.Equal(["Created", "Submitted", "Approved", "Rejected", "Withdrawn", "Cancelled"], Enum.GetNames<LeaveRequestEventType>());
        Assert.Equal([0, 1, 2, 3, 4, 5], Enum.GetValues<LeaveRequestEventType>().Select(x => (int)x));
    }
}
