using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class EmployeeCodeConfigurationEditTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly DateOnly Start = new(2026, 3, 4);

    [Fact]
    public async Task Unused_version_can_be_edited_and_audited_without_changing_its_identity()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var created = await harness.CodeConfiguration().SaveAsync(Request());
        Assert.True(created.Succeeded, created.Message);

        var edited = await harness.CodeConfiguration().SaveAsync(Request(created.Value!.VersionId, request =>
        {
            request.AssignmentMode = EmployeeCodeAssignmentMode.Manual;
            request.AutoGenerate = false;
            request.GenerationMethod = null;
            request.Prefix = "STAFF";
            request.Padding = 6;
            request.NextNumber = 12;
            request.EffectiveFrom = Start.AddDays(1);
            request.EffectiveTo = Start.AddDays(30);
        }));

        Assert.True(edited.Succeeded, edited.Message);
        Assert.Equal(created.Value.VersionId, edited.Value!.VersionId);
        Assert.Equal(EmployeeCodeAssignmentMode.Manual, edited.Value.AssignmentMode);
        Assert.Equal("STAFF", edited.Value.Prefix);
        Assert.Equal(12, edited.Value.NextNumber);

        var audit = await harness.CodeConfiguration().GetAuditAsync(created.Value.VersionId!.Value);
        Assert.True(audit.Succeeded, audit.Message);
        Assert.Equal(new[] { "Updated", "Created" }, audit.Value!.Select(x => x.Action));
        Assert.All(audit.Value, item => Assert.Equal(harness.TenantContext.UserId, item.ActorUserId));
        Assert.Contains("EMP", audit.Value[0].OldValue);
        Assert.Contains("STAFF", audit.Value[0].NewValue);
    }

    [Fact]
    public async Task Used_version_rejects_code_format_and_effective_range_changes_with_field_errors()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var created = await harness.CodeConfiguration().SaveAsync(Request());
        Assert.True(created.Succeeded, created.Message);
        var employeeId = Guid.NewGuid();
        using (var context = harness.CreateContext())
        {
            context.Employees.Add(new Employee
            {
                Id = employeeId, TenantId = Tenant, FirstName = "Assigned", LastName = "Code",
                Email = $"assigned-{Guid.NewGuid():N}@example.test", DateOfJoining = Start,
                EmployeeCode = "EMP-00001"
            });
            await context.SaveChangesAsync();
        }

        var changed = await harness.CodeConfiguration().SaveAsync(Request(created.Value!.VersionId, request =>
        {
            request.Prefix = "NEW";
            request.EffectiveFrom = Start.AddDays(1);
        }));

        Assert.False(changed.Succeeded);
        Assert.Contains(changed.Errors!, error => error.Field == "Prefix");
        using var verify = harness.CreateUnscopedContext();
        Assert.Equal("EMP-00001", await verify.Employees.IgnoreQueryFilters().Where(x => x.Id == employeeId).Select(x => x.EmployeeCode).SingleAsync());
    }

    [Fact]
    public async Task Editing_excludes_current_version_from_overlap_check_but_rejects_other_versions()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var first = await harness.CodeConfiguration().SaveAsync(Request(update: request => request.EffectiveTo = Start.AddDays(30)));
        Assert.True(first.Succeeded, first.Message);
        var second = await harness.CodeConfiguration().SaveAsync(Request(update: request =>
        {
            request.EffectiveFrom = Start.AddDays(31);
            request.EffectiveTo = Start.AddDays(60);
            request.Prefix = "NEXT";
        }));
        Assert.True(second.Succeeded, second.Message);

        var sameRange = await harness.CodeConfiguration().SaveAsync(Request(first.Value!.VersionId, request =>
        {
            request.Prefix = "EDIT";
            request.EffectiveTo = Start.AddDays(30);
        }));
        Assert.True(sameRange.Succeeded, sameRange.Message);

        var overlap = await harness.CodeConfiguration().SaveAsync(Request(first.Value.VersionId, request =>
        {
            request.EffectiveTo = Start.AddDays(31);
        }));
        Assert.False(overlap.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, overlap.Status);
    }

    [Fact]
    public async Task Version_from_another_tenant_cannot_be_edited()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var created = await harness.CodeConfiguration().SaveAsync(Request());
        Assert.True(created.Succeeded, created.Message);

        var otherTenantResult = await harness.ActAs(SeedData.TenantIds.Demo02).CodeConfiguration()
            .SaveAsync(Request(created.Value!.VersionId, request => request.Prefix = "OTHER"));

        Assert.False(otherTenantResult.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.NotFound, otherTenantResult.Status);
    }

    [Fact]
    public async Task Sequence_cannot_be_lowered_and_generated_candidate_must_be_unique()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var created = await harness.CodeConfiguration().SaveAsync(Request(update: request => request.NextNumber = 5));
        Assert.True(created.Succeeded, created.Message);
        using (var context = harness.CreateContext())
        {
            context.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(), TenantId = Tenant, FirstName = "Existing", LastName = "Employee",
                Email = $"existing-{Guid.NewGuid():N}@example.test", DateOfJoining = Start,
                EmployeeCode = "EMP-00005"
            });
            await context.SaveChangesAsync();
        }

        var duplicate = await harness.CodeConfiguration().SaveAsync(Request(created.Value!.VersionId, request => request.NextNumber = 5));
        Assert.False(duplicate.Succeeded);
        Assert.Contains(duplicate.Errors!, error => error.Field == "NextNumber");

        var lower = await harness.CodeConfiguration().SaveAsync(Request(created.Value.VersionId, request => request.NextNumber = 4));
        Assert.False(lower.Succeeded);
        Assert.Contains(lower.Errors!, error => error.Field == "NextNumber");
    }

    private static EmployeeCodeConfigurationRequest Request(Guid? versionId = null, Action<EmployeeCodeConfigurationRequest>? update = null)
    {
        var request = new EmployeeCodeConfigurationRequest
        {
            VersionId = versionId, AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.Simple, Prefix = "EMP", Separator = "-",
            NextNumber = 1, Padding = 5, EffectiveFrom = Start, IsActive = true
        };
        update?.Invoke(request);
        return request;
    }
}
