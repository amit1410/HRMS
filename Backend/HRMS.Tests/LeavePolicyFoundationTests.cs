using HRMS.Application.Services;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class LeavePolicyFoundationTests
{
    [Fact]
    public async Task Resolver_returns_no_policy_when_no_published_rule_exists()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant));
        await AddBaseAsync(context, tenant, employee, type);
        var result = await new LeavePolicyResolver(context).ResolveAsync(tenant, employee, type, new(2027, 1, 1));
        Assert.Equal(LeavePolicyResolutionStatus.NoPolicy, result.Status);
    }

    [Fact]
    public async Task Resolver_uses_published_version_and_ignores_draft_future_expired_and_inactive_rules()
    {
        using var db = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type);
        var policy = NewPolicy(tenant, true); context.LeavePolicies.Add(policy);
        context.LeavePolicyVersions.AddRange(
            Version(tenant, policy, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Draft, 1),
            Version(tenant, policy, 2, new(2028, 1, 1), null, LeavePolicyVersionStatus.Published, 1),
            Version(tenant, policy, 3, new(2025, 1, 1), new(2026, 12, 31), LeavePolicyVersionStatus.Published, 1));
        await context.SaveChangesAsync();
        var published = await context.LeavePolicyVersions.SingleAsync(x => x.VersionNumber == 2);
        context.LeavePolicyRules.Add(new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = published.Id, LeaveTypeId = type, IsActive = true });
        await context.SaveChangesAsync();
        var result = await new LeavePolicyResolver(context).ResolveAsync(tenant, employee, type, new(2027, 6, 1));
        Assert.Equal(LeavePolicyResolutionStatus.NoPolicy, result.Status);
    }

    [Fact]
    public async Task Resolver_applies_empty_sets_to_all_employees_and_specificity_at_equal_priority()
    {
        using var db = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid(); var department = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type, department);
        var policyA = NewPolicy(tenant, true); var policyB = NewPolicy(tenant, true); context.LeavePolicies.AddRange(policyA, policyB);
        var vA = Version(tenant, policyA, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Published, 10); var vB = Version(tenant, policyB, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Published, 10); context.LeavePolicyVersions.AddRange(vA, vB);
        context.LeavePolicyRules.AddRange(new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = vA.Id, LeaveTypeId = type, IsActive = true }, new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = vB.Id, LeaveTypeId = type, IsActive = true });
        context.LeavePolicyApplicabilitySets.Add(new LeavePolicyApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = vB.Id, DepartmentId = department });
        await context.SaveChangesAsync();
        var result = await new LeavePolicyResolver(context).ResolveAsync(tenant, employee, type, new(2027, 6, 1));
        Assert.Equal(LeavePolicyResolutionStatus.Resolved, result.Status); Assert.Equal(vB.Id, result.LeavePolicyVersionId); Assert.Equal(1, result.Specificity);
    }

    [Fact]
    public async Task Resolver_reports_ambiguity_for_equal_priority_and_specificity()
    {
        using var db = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid(); var department = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type, department);
        var policies = new[] { NewPolicy(tenant, true), NewPolicy(tenant, true) }; context.LeavePolicies.AddRange(policies);
        var versions = policies.Select(p => Version(tenant, p, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Published, 4)).ToArray(); context.LeavePolicyVersions.AddRange(versions);
        foreach (var version in versions) { context.LeavePolicyRules.Add(new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = version.Id, LeaveTypeId = type, IsActive = true }); context.LeavePolicyApplicabilitySets.Add(new LeavePolicyApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = version.Id, DepartmentId = department }); }
        await context.SaveChangesAsync();
        var result = await new LeavePolicyResolver(context).ResolveAsync(tenant, employee, type, new(2027, 6, 1));
        Assert.Equal(LeavePolicyResolutionStatus.ConfigurationAmbiguity, result.Status);
    }

    [Fact]
    public async Task Resolver_uses_employment_effective_on_requested_date()
    {
        using var db = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid(); var oldDepartment = Guid.NewGuid(); var newDepartment = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type, oldDepartment, newDepartment);
        var pOld = NewPolicy(tenant, true); var pNew = NewPolicy(tenant, true); context.LeavePolicies.AddRange(pOld, pNew);
        var vOld = Version(tenant, pOld, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Published, 5); var vNew = Version(tenant, pNew, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Published, 5); context.LeavePolicyVersions.AddRange(vOld, vNew);
        context.LeavePolicyRules.AddRange(new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = vOld.Id, LeaveTypeId = type, IsActive = true }, new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = vNew.Id, LeaveTypeId = type, IsActive = true });
        context.LeavePolicyApplicabilitySets.AddRange(new LeavePolicyApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = vOld.Id, DepartmentId = oldDepartment }, new LeavePolicyApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = vNew.Id, DepartmentId = newDepartment });
        await context.SaveChangesAsync();
        Assert.Equal(vOld.Id, (await new LeavePolicyResolver(context).ResolveAsync(tenant, employee, type, new(2027, 6, 30))).LeavePolicyVersionId);
        Assert.Equal(vNew.Id, (await new LeavePolicyResolver(context).ResolveAsync(tenant, employee, type, new(2027, 7, 1))).LeavePolicyVersionId);
    }

    [Fact]
    public async Task Resolver_ignores_inactive_policy_rule_and_different_leave_type()
    {
        using var db = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid(); var otherType = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type);
        context.LeaveTypes.Add(new LeaveType { Id = otherType, TenantId = tenant, Code = "OTHER", Name = "Other" });
        var inactivePolicy = NewPolicy(tenant, false); var activePolicy = NewPolicy(tenant, true); context.LeavePolicies.AddRange(inactivePolicy, activePolicy);
        var versions = new[] { Version(tenant, inactivePolicy, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Published, 1), Version(tenant, activePolicy, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Published, 2) };
        context.LeavePolicyVersions.AddRange(versions);
        context.LeavePolicyRules.AddRange(
            new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = versions[0].Id, LeaveTypeId = type, IsActive = true },
            new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = versions[1].Id, LeaveTypeId = type, IsActive = false },
            new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = versions[1].Id, LeaveTypeId = otherType, IsActive = true });
        await context.SaveChangesAsync();
        var result = await new LeavePolicyResolver(context).ResolveAsync(tenant, employee, type, new(2027, 1, 1));
        Assert.Equal(LeavePolicyResolutionStatus.NoPolicy, result.Status);
    }

    [Fact]
    public async Task Resolver_uses_priority_then_and_or_specificity()
    {
        using var db = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid(); var department = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type, department);
        var employeeEntity = await context.Employees.SingleAsync(); employeeEntity.Gender = Gender.Male;
        var low = NewPolicy(tenant, true); var high = NewPolicy(tenant, true); context.LeavePolicies.AddRange(low, high);
        var lowVersion = Version(tenant, low, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Published, 5);
        var highVersion = Version(tenant, high, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Published, 10); context.LeavePolicyVersions.AddRange(lowVersion, highVersion);
        context.LeavePolicyRules.AddRange(new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = lowVersion.Id, LeaveTypeId = type }, new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = highVersion.Id, LeaveTypeId = type });
        context.LeavePolicyApplicabilitySets.AddRange(
            new LeavePolicyApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = lowVersion.Id, DepartmentId = department },
            new LeavePolicyApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = highVersion.Id, Gender = Gender.Female, DepartmentId = department },
            new LeavePolicyApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = highVersion.Id, Gender = Gender.Male },
            new LeavePolicyApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = highVersion.Id, Gender = Gender.Male, DepartmentId = department });
        await context.SaveChangesAsync();
        var result = await new LeavePolicyResolver(context).ResolveAsync(tenant, employee, type, new(2027, 1, 1));
        Assert.Equal(LeavePolicyResolutionStatus.Resolved, result.Status);
        Assert.Equal(highVersion.Id, result.LeavePolicyVersionId);
        Assert.Equal(2, result.Specificity);
    }

    [Fact]
    public async Task Resolver_rejects_cross_tenant_employee_and_leave_type()
    {
        using var db = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var otherTenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid(); var otherType = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenant); using var context = db.CreateContext(tenantContext); await AddBaseAsync(context, tenant, employee, type);
        context.Tenants.Add(new Tenant { Id = otherTenant, TenantCode = "OTHER", Host = "other.local", ShardKey = "other", TenantName = "Other" });
        context.LeaveTypes.Add(new LeaveType { Id = otherType, TenantId = otherTenant, Code = "OTHER", Name = "Other" });
        await context.SaveChangesAsync();
        var otherEmployee = Guid.NewGuid();
        tenantContext.TenantId = otherTenant;
        context.Employees.Add(new Employee { Id = otherEmployee, TenantId = otherTenant, FirstName = "Other", LastName = "Employee", Email = Guid.NewGuid() + "@test.local", DateOfJoining = new(2026, 1, 1), Gender = Gender.Unspecified });
        context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = otherTenant, EmployeeId = otherEmployee, EffectiveFrom = new(2026, 1, 1) });
        await context.SaveChangesAsync();
        tenantContext.TenantId = tenant;
        var resolver = new LeavePolicyResolver(context);
        Assert.Equal(LeavePolicyResolutionStatus.InvalidTenant, (await resolver.ResolveAsync(tenant, otherEmployee, otherType, new(2027, 1, 1))).Status);
        Assert.Equal(LeavePolicyResolutionStatus.InvalidTenant, (await resolver.ResolveAsync(otherTenant, otherEmployee, type, new(2027, 1, 1))).Status);
    }

    [Fact]
    public async Task Foundation_service_validates_publish_lifecycle_and_ranges()
    {
        using var db = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type);
        var policy = NewPolicy(tenant, true); context.LeavePolicies.Add(policy); await context.SaveChangesAsync();
        var service = new LeavePolicyFoundationService(context);
        var invalid = await service.CreateDraftVersionAsync(tenant, policy.Id, new(2027, 2, 1), new(2027, 1, 1), 1, "test");
        Assert.Equal(ResultStatus.ValidationFailed, invalid.Status);
        var draft = await service.CreateDraftVersionAsync(tenant, policy.Id, new(2027, 1, 1), null, 1, "test");
        Assert.Equal(ResultStatus.Success, draft.Status);
        context.LeavePolicyRules.Add(new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = draft.Value!.Id, LeaveTypeId = type }); await context.SaveChangesAsync();
        Assert.Equal(ResultStatus.Success, (await service.PublishAsync(tenant, draft.Value.Id, "test")).Status);
        Assert.Equal(ResultStatus.Conflict, (await service.PublishAsync(tenant, draft.Value.Id, "test")).Status);
        var overlappingDraft = await service.CreateDraftVersionAsync(tenant, policy.Id, new(2027, 6, 1), null, 1, "test");
        context.LeavePolicyRules.Add(new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = overlappingDraft.Value!.Id, LeaveTypeId = type }); await context.SaveChangesAsync();
        Assert.Equal(ResultStatus.ValidationFailed, (await service.PublishAsync(tenant, overlappingDraft.Value.Id, "test")).Status);
    }

    [Fact]
    public async Task Configuration_service_rejects_unsupported_partial_day_mode_at_publish()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type);
        var policy = NewPolicy(tenant, true); var version = Version(tenant, policy, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Draft, 1);
        var rule = new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = version.Id, LeaveTypeId = type };
        context.LeavePolicies.Add(policy); context.LeavePolicyVersions.Add(version); context.LeavePolicyRules.Add(rule);
        context.LeavePolicyRequestRules.Add(new LeavePolicyRequestRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyRuleId = rule.Id, PartialDayMode = PartialDayMode.HalfDayAllowed });
        await context.SaveChangesAsync();

        var result = await new LeaveConfigurationService(context, new TestTenantContext(tenant)).PublishAsync(policy.Id, version.Id);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Errors!, error => error.Field == "partialDayMode" && error.Message.Contains("HalfDayAllowed") && error.Message.Contains("Full Day only"));
    }

    [Fact]
    public async Task Configuration_service_allows_disabled_sandwich_calendar_at_publish()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type);
        var policy = NewPolicy(tenant, true); var version = Version(tenant, policy, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Draft, 1);
        var rule = new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = version.Id, LeaveTypeId = type };
        context.LeavePolicies.Add(policy); context.LeavePolicyVersions.Add(version); context.LeavePolicyRules.Add(rule);
        context.LeavePolicyRequestRules.Add(new LeavePolicyRequestRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyRuleId = rule.Id, PartialDayMode = PartialDayMode.FullDayOnly });
        context.LeavePolicyCalendarRules.Add(new LeavePolicyCalendarRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyRuleId = rule.Id, SandwichMode = SandwichMode.Disabled });
        await context.SaveChangesAsync();

        var result = await new LeaveConfigurationService(context, new TestTenantContext(tenant)).PublishAsync(policy.Id, version.Id);

        Assert.NotEqual(ResultStatus.ValidationFailed, result.Status);
    }

    [Theory]
    [InlineData(SandwichMode.Holiday)]
    [InlineData(SandwichMode.WeekOff)]
    [InlineData(SandwichMode.HolidayAndWeekOff)]
    public async Task Configuration_service_rejects_unsupported_sandwich_mode_at_publish(SandwichMode sandwichMode)
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type);
        var policy = NewPolicy(tenant, true); var version = Version(tenant, policy, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Draft, 1);
        var rule = new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = version.Id, LeaveTypeId = type };
        context.LeavePolicies.Add(policy); context.LeavePolicyVersions.Add(version); context.LeavePolicyRules.Add(rule);
        context.LeavePolicyRequestRules.Add(new LeavePolicyRequestRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyRuleId = rule.Id, PartialDayMode = PartialDayMode.FullDayOnly });
        context.LeavePolicyCalendarRules.Add(new LeavePolicyCalendarRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyRuleId = rule.Id, SandwichMode = sandwichMode });
        await context.SaveChangesAsync();

        var result = await new LeaveConfigurationService(context, new TestTenantContext(tenant)).PublishAsync(policy.Id, version.Id);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Errors!, error => error.Field == "sandwichMode" && error.Message.Contains("Sandwich Leave is not supported"));
    }

    [Fact]
    public async Task Configuration_service_begin_edit_clones_published_version_and_reuses_existing_draft()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type);
        var policy = NewPolicy(tenant, true);
        var published = Version(tenant, policy, 1, new(2027, 1, 1), new(2027, 12, 31), LeavePolicyVersionStatus.Published, 9);
        var rule = new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = published.Id, LeaveTypeId = type };
        context.LeavePolicies.Add(policy); context.LeavePolicyVersions.Add(published); context.LeavePolicyRules.Add(rule);
        context.LeavePolicyRequestRules.Add(new LeavePolicyRequestRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyRuleId = rule.Id, PartialDayMode = PartialDayMode.FullDayOnly });
        await context.SaveChangesAsync();

        var service = new LeaveConfigurationService(context, new TestTenantContext(tenant));
        var first = await service.BeginEditAsync(policy.Id);
        Assert.Equal(ResultStatus.Success, first.Status);
        Assert.NotNull(first.Value?.CurrentVersion);
        Assert.Equal(LeavePolicyVersionStatus.Draft, first.Value!.CurrentVersion!.Status);
        Assert.Equal(published.EffectiveFrom, first.Value.CurrentVersion.EffectiveFrom);
        Assert.Equal(published.Priority, first.Value.CurrentVersion.Priority);

        var second = await service.BeginEditAsync(policy.Id);
        Assert.Equal(first.Value.CurrentVersion.Id, second.Value!.CurrentVersion!.Id);
        Assert.Equal(2, await context.LeavePolicyVersions.CountAsync());
        Assert.Equal(2, await context.LeavePolicyRules.CountAsync());
    }

    [Fact]
    public async Task Configuration_service_publishing_replacement_supersedes_same_policy_without_self_overlap_acknowledgement()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type);
        var policy = NewPolicy(tenant, true); var other = NewPolicy(tenant, true); other.Code = "POL2";
        var v1 = Version(tenant, policy, 1, new(2026, 1, 1), new(2026, 12, 31), LeavePolicyVersionStatus.Published, 9);
        var v2 = Version(tenant, policy, 2, new(2026, 1, 1), new(2026, 12, 31), LeavePolicyVersionStatus.Draft, 9);
        var otherVersion = Version(tenant, other, 1, new(2026, 1, 1), new(2026, 12, 31), LeavePolicyVersionStatus.Published, 0);
        context.LeavePolicies.AddRange(policy, other); context.LeavePolicyVersions.AddRange(v1, v2, otherVersion);
        context.LeavePolicyRules.AddRange(
            new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = v1.Id, LeaveTypeId = type },
            new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = v2.Id, LeaveTypeId = type },
            new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = otherVersion.Id, LeaveTypeId = type });
        await context.SaveChangesAsync();

        var service = new LeaveConfigurationService(context, new TestTenantContext(tenant));
        var blocked = await service.PublishAsync(policy.Id, v2.Id);
        Assert.Equal(ResultStatus.ValidationFailed, blocked.Status);
        Assert.Contains(blocked.Errors!, error => error.Field == "overlapAcknowledgement");
        Assert.Equal(LeavePolicyVersionStatus.Published, (await context.LeavePolicyVersions.FindAsync(v1.Id))!.Status);

        var published = await service.PublishAsync(policy.Id, v2.Id, acknowledgeOverlap: true);
        Assert.Equal(ResultStatus.Success, published.Status);
        Assert.Equal(LeavePolicyVersionStatus.Retired, (await context.LeavePolicyVersions.FindAsync(v1.Id))!.Status);
        Assert.Equal(LeavePolicyVersionStatus.Published, (await context.LeavePolicyVersions.FindAsync(v2.Id))!.Status);
    }

    [Fact]
    public async Task Configuration_service_future_replacement_truncates_predecessor_and_resolver_uses_new_version()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(tenant)); await AddBaseAsync(context, tenant, employee, type);
        var policy = NewPolicy(tenant, true);
        var v1 = Version(tenant, policy, 1, new(2026, 1, 1), new(2026, 12, 31), LeavePolicyVersionStatus.Published, 9);
        var v2 = Version(tenant, policy, 2, new(2026, 10, 1), new(2026, 12, 31), LeavePolicyVersionStatus.Draft, 9);
        context.LeavePolicies.Add(policy); context.LeavePolicyVersions.AddRange(v1, v2);
        context.LeavePolicyRules.AddRange(new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = v1.Id, LeaveTypeId = type }, new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = v2.Id, LeaveTypeId = type });
        await context.SaveChangesAsync();

        var result = await new LeaveConfigurationService(context, new TestTenantContext(tenant)).PublishAsync(policy.Id, v2.Id);
        Assert.Equal(ResultStatus.Success, result.Status);
        var predecessor = await context.LeavePolicyVersions.FindAsync(v1.Id);
        Assert.Equal(new(2026, 9, 30), predecessor!.EffectiveTo);
        Assert.Equal(LeavePolicyVersionStatus.Published, predecessor.Status);
        var resolved = await new LeavePolicyResolver(context).ResolveAsync(tenant, employee, type, new(2026, 10, 1));
        Assert.Equal(v2.Id, resolved.LeavePolicyVersionId);
    }

    [Fact]
    public async Task Foundation_service_validates_leave_period_range_overlap_and_tenant_code_scope()
    {
        using var db = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var otherTenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenant); using var context = db.CreateContext(tenantContext); await AddBaseAsync(context, tenant, employee, type);
        context.Tenants.Add(new Tenant { Id = otherTenant, TenantCode = "OTHER", Host = "other.local", ShardKey = "other", TenantName = "Other" });
        var existing = new LeavePeriod { Id = Guid.NewGuid(), TenantId = tenant, Code = "2027", Name = "2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) };
        context.LeavePeriods.Add(existing); await context.SaveChangesAsync();
        tenantContext.TenantId = otherTenant;
        context.LeavePeriods.Add(new LeavePeriod { Id = Guid.NewGuid(), TenantId = otherTenant, Code = "2027", Name = "Other 2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) }); await context.SaveChangesAsync();
        tenantContext.TenantId = tenant;
        var service = new LeavePolicyFoundationService(context);
        Assert.Equal(ResultStatus.ValidationFailed, (await service.ValidatePeriodAsync(tenant, new LeavePeriod { StartDate = new(2028, 1, 1), EndDate = new(2027, 1, 1) })).Status);
        Assert.Equal(ResultStatus.Conflict, (await service.ValidatePeriodAsync(tenant, new LeavePeriod { StartDate = new(2027, 6, 1), EndDate = new(2028, 1, 1) })).Status);
        Assert.Equal(ResultStatus.Success, (await service.ValidatePeriodAsync(tenant, new LeavePeriod { StartDate = new(2028, 1, 1), EndDate = new(2028, 12, 31) })).Status);
    }

    [Fact]
    public async Task Tenant_aware_applicability_fk_rejects_a_master_from_another_tenant()
    {
        using var db = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var otherTenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var type = Guid.NewGuid(); var otherDepartment = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenant); using var context = db.CreateContext(tenantContext); await AddBaseAsync(context, tenant, employee, type);
        context.Tenants.Add(new Tenant { Id = otherTenant, TenantCode = "OTHER", Host = "other.local", ShardKey = "other", TenantName = "Other" }); await context.SaveChangesAsync();
        tenantContext.TenantId = otherTenant;
        context.Departments.Add(new Department { Id = otherDepartment, TenantId = otherTenant, Code = "OTHER", Name = "Other" }); await context.SaveChangesAsync();
        tenantContext.TenantId = tenant;
        var policy = NewPolicy(tenant, true); var version = Version(tenant, policy, 1, new(2027, 1, 1), null, LeavePolicyVersionStatus.Draft, 1); context.LeavePolicies.Add(policy); context.LeavePolicyVersions.Add(version);
        context.LeavePolicyApplicabilitySets.Add(new LeavePolicyApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyVersionId = version.Id, DepartmentId = otherDepartment });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static LeavePolicy NewPolicy(Guid tenant, bool active) => new() { Id = Guid.NewGuid(), TenantId = tenant, Code = Guid.NewGuid().ToString("N")[..8], Name = "Policy", IsActive = active };
    private static LeavePolicyVersion Version(Guid tenant, LeavePolicy policy, int number, DateOnly from, DateOnly? to, LeavePolicyVersionStatus status, int priority) => new() { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyId = policy.Id, VersionNumber = number, EffectiveFrom = from, EffectiveTo = to, Status = status, Priority = priority };

    private static async Task AddBaseAsync(HrmsDbContext db, Guid tenant, Guid employee, Guid type, Guid? department = null, Guid? newDepartment = null)
    {
        db.Tenants.Add(new Tenant { Id = tenant, TenantCode = Guid.NewGuid().ToString("N")[..8], Host = Guid.NewGuid().ToString("N") + ".local", ShardKey = Guid.NewGuid().ToString("N"), TenantName = "Test" });
        db.LeaveTypes.Add(new LeaveType { Id = type, TenantId = tenant, Code = "SICK", Name = "Sick", DefaultUnit = LeaveUnit.Day, IsPaid = true });
        db.Employees.Add(new Employee { Id = employee, TenantId = tenant, FirstName = "Test", LastName = "Employee", Email = Guid.NewGuid() + "@test.local", DateOfJoining = new(2026, 1, 1), Gender = Gender.Unspecified });
        if (department is null)
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = new(2026, 1, 1) });
        if (department is Guid oldId)
        {
            db.Departments.Add(new Department { Id = oldId, TenantId = tenant, Code = "OLD", Name = "Old" });
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = new(2027, 1, 1), EffectiveTo = newDepartment is null ? null : new(2027, 6, 30), DepartmentId = oldId });
            if (newDepartment is Guid newId) { db.Departments.Add(new Department { Id = newId, TenantId = tenant, Code = "NEW", Name = "New" }); db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = new(2027, 7, 1), DepartmentId = newId }); }
        }
        await db.SaveChangesAsync();
    }
}
