using HRMS.Application.DTOs.Departments;
using HRMS.Domain.Entities;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// What the audit timestamps mean once they have been through the database.
/// <para>
/// The values are stamped in UTC, but a database provider hands them back with
/// <see cref="DateTimeKind.Unspecified"/> unless told otherwise, and a serializer then writes them without a
/// zone. The client that reads "2026-03-04T09:07:08" applies its own offset, so an audit trail read back from
/// a table disagrees with the one returned by the write that created it. These tests pin the round trip.
/// </para>
/// </summary>
public class AuditTimestampTests
{
    /// <summary>
    /// Covers the nullable case too, which is the one a per-type conversion can silently miss:
    /// <c>ModifiedDate</c> is <c>DateTime?</c>, and a convention registered only for <c>DateTime</c> would
    /// leave it Unspecified while <c>CreatedDate</c> looked correct.
    /// </summary>
    [Fact]
    public async Task Timestamps_read_back_from_the_database_are_marked_utc()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var created = await harness.Departments().CreateAsync(NewRequest("Facilities"));
        var updated = await harness.Departments().UpdateAsync(created.Value!.Id, NewRequest("Estates"));
        Assert.NotNull(updated.Value!.ModifiedDate);

        // A fresh context, so the values come off the connection rather than out of a change tracker that
        // still holds the DateTime the service produced.
        using var context = harness.CreateContext();
        var department = await context.Departments.AsNoTracking().SingleAsync(d => d.Id == created.Value.Id);

        Assert.Equal(DateTimeKind.Utc, department.CreatedDate.Kind);
        Assert.Equal(DateTimeKind.Utc, department.ModifiedDate!.Value.Kind);
    }

    /// <summary>
    /// A Local value is converted on the way in rather than relabelled, so the stored column is the same
    /// instant the caller meant. On a machine whose local time is UTC the two are identical and only the
    /// Kind assertion carries weight; anywhere else the instant comparison is the real check.
    /// </summary>
    [Fact]
    public async Task A_local_timestamp_is_stored_as_the_same_instant_not_relabelled()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var localCreated = new DateTime(2024, 6, 1, 8, 30, 0, DateTimeKind.Local);
        var localModified = new DateTime(2024, 6, 2, 9, 45, 0, DateTimeKind.Local);
        var id = Guid.NewGuid();

        using (var writer = harness.CreateContext())
        {
            writer.Departments.Add(new Department
            {
                Id = id,
                Code = "TZ",
                Name = "Timezones",
                IsActive = true,
                CreatedDate = localCreated,
                ModifiedDate = localModified
            });
            await writer.SaveChangesAsync();
        }

        using var reader = harness.CreateContext();
        var department = await reader.Departments.AsNoTracking().SingleAsync(d => d.Id == id);

        Assert.Equal(localCreated.ToUniversalTime(), department.CreatedDate);
        Assert.Equal(localModified.ToUniversalTime(), department.ModifiedDate);
        Assert.Equal(DateTimeKind.Utc, department.CreatedDate.Kind);
        Assert.Equal(DateTimeKind.Utc, department.ModifiedDate!.Value.Kind);
    }

    /// <summary>
    /// CreatedDate is written once. The save guard marks it unmodified on update, so a client that echoes an
    /// entity back with a different creation date — or a service that forgets to exclude it — cannot rewrite
    /// when a record came into existence.
    /// </summary>
    [Fact]
    public async Task An_update_cannot_rewrite_when_a_record_was_created()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var created = (await harness.Departments().CreateAsync(NewRequest("Facilities"))).Value!;

        using (var writer = harness.CreateContext())
        {
            var department = await writer.Departments.SingleAsync(d => d.Id == created.Id);
            department.CreatedDate = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            department.Name = "Facilities and Estates";
            await writer.SaveChangesAsync();
        }

        using var reader = harness.CreateContext();
        var reloaded = await reader.Departments.AsNoTracking().SingleAsync(d => d.Id == created.Id);

        Assert.Equal(created.CreatedDate, reloaded.CreatedDate);
        Assert.Equal("Facilities and Estates", reloaded.Name);
    }

    private static DepartmentRequest NewRequest(string name) => new()
    {
        Code = "FAC",
        Name = name,
        IsActive = true
    };
}
