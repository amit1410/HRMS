using HRMS.Application.DTOs.Employees;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class EmployeeCodeEmploymentFlowTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly DateOnly Today = new(2026, 3, 4);
    private static readonly Guid Holding = OrganizationTestHarness.HoldingCompanyId(Tenant, "HC01");
    private static readonly Guid Organisation = OrganizationTestHarness.OrganisationId(Tenant, "ORG01");
    private static readonly Guid Designation = OrganizationTestHarness.DesignationId(Tenant, "SE");
    private static readonly Guid Grade = OrganizationTestHarness.GradeId(Tenant, "G1");
    private static readonly Guid Country = OrganizationTestHarness.CountryId("IN");
    private static readonly Guid WorkLocation = OrganizationTestHarness.WorkLocationId(Tenant, "WL-MUM");
    private static readonly Guid Reason = OrganizationTestHarness.PositionChangeReasonId(Tenant, "NEW_HIRE");

    [Fact]
    public async Task Simple_configuration_creates_version_and_assigns_sequential_codes_to_pending_employees()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var department = await AddDepartmentAsync(harness, "IT");
        var lob = await AddLobAsync(harness, "LOB01");

        var configuration = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true,
            AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.Simple,
            Prefix = "EMP",
            Separator = "-",
            NextNumber = 1,
            Padding = 5,
            EffectiveFrom = Today
        });
        Assert.True(configuration.Succeeded, configuration.Message);

        var first = await CreatePendingEmployeeAsync(harness, "Simple One");
        var second = await CreatePendingEmployeeAsync(harness, "Simple Two");
        var firstResult = await harness.Employment().CreateChangeAsync(first, Request(department, lob), "tester");
        var secondResult = await harness.Employment().CreateChangeAsync(second, Request(department, lob), "tester");

        Assert.True(firstResult.Succeeded, firstResult.Message);
        Assert.True(secondResult.Succeeded, secondResult.Message);
        Assert.Equal("EMP-00001", await EmployeeCodeAsync(harness, first));
        Assert.Equal("EMP-00002", await EmployeeCodeAsync(harness, second));
        Assert.Equal(2, await harness.CreateUnscopedContext().EmployeeEmploymentHistory.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant));
    }

    [Fact]
    public async Task Rule_based_generation_uses_explicit_master_code_mappings()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var department = await AddDepartmentAsync(harness, "IT");
        var lob = await AddLobAsync(harness, "LOB01");

        var configuration = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true,
            AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.RuleBased,
            Prefix = "EMP",
            Separator = "/",
            NextNumber = 1,
            Padding = 5,
            EffectiveFrom = Today
        });
        Assert.True(configuration.Succeeded, configuration.Message);

        var rule = await harness.CodeConfiguration().SaveRuleAsync(null, new EmployeeCodeRuleRequest
        {
            Name = "IT rule",
            Priority = 1,
            Status = EmployeeCodeRuleStatus.Active,
            Conditions = [new EmployeeCodeConditionRequest
            {
                Field = EmployeeCodeConditionField.Department,
                Operator = EmployeeCodeConditionOperator.Equals,
                ReferenceId = department
            }],
            Segments =
            [
                new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.HoldingCompanyCode },
                new() { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.LobCode },
                new() { SequenceOrder = 3, SegmentType = EmployeeCodeSegmentType.OrganisationCode },
                new() { SequenceOrder = 4, SegmentType = EmployeeCodeSegmentType.DepartmentCode },
                new() { SequenceOrder = 5, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 5 }
            ]
        });
        Assert.True(rule.Succeeded, rule.Message);

        var employee = await CreatePendingEmployeeAsync(harness, "Rule One");
        var result = await harness.Employment().CreateChangeAsync(employee, Request(department, lob), "tester");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("HC01/LOB01/ORG01/IT/00001", await EmployeeCodeAsync(harness, employee));
    }

    [Fact]
    public async Task Preview_uses_the_same_rule_and_sequence_state_without_allocating()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var department = await AddDepartmentAsync(harness, "IT");
        var lob = await AddLobAsync(harness, "LOB01");
        var configuration = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.RuleBased, Separator = "/",
            EffectiveFrom = Today, NextNumber = 1, Padding = 5
        });
        Assert.True(configuration.Succeeded, configuration.Message);
        var rule = await harness.CodeConfiguration().SaveRuleAsync(null, new EmployeeCodeRuleRequest
        {
            Name = "Preview IT", Priority = 1, Status = EmployeeCodeRuleStatus.Active,
            Conditions = [new() { Field = EmployeeCodeConditionField.Department, ReferenceId = department }],
            Segments =
            [
                new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.HoldingCompanyCode },
                new() { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.LobCode },
                new() { SequenceOrder = 3, SegmentType = EmployeeCodeSegmentType.OrganisationCode },
                new() { SequenceOrder = 4, SegmentType = EmployeeCodeSegmentType.DepartmentCode },
                new() { SequenceOrder = 5, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 5 }
            ]
        });
        Assert.True(rule.Succeeded, rule.Message);

        var preview = await harness.CodeConfiguration().PreviewAsync(new EmployeeCodePreviewRequest
        {
            EffectiveFrom = Today, HoldingCompanyId = Holding, LobId = lob,
            OrganisationId = Organisation, DepartmentId = department,
            DesignationId = Designation, CountryLocationId = Country, WorkLocationId = WorkLocation,
            CostCenterId = null
        });
        Assert.True(preview.Succeeded, preview.Message);
        Assert.Equal("HC01/LOB01/ORG01/IT/00001", preview.Value!.Code);
        Assert.False(preview.Value.SequenceReserved);
        Assert.Equal(0, await harness.CreateUnscopedContext().EmployeeCodeSequences.IgnoreQueryFilters().CountAsync());

        var employee = await CreatePendingEmployeeAsync(harness, "Preview One");
        var saved = await harness.Employment().CreateChangeAsync(employee, Request(department, lob), "tester");
        Assert.True(saved.Succeeded, saved.Message);
        Assert.Equal(preview.Value.Code, await EmployeeCodeAsync(harness, employee));
    }

    [Fact]
    public async Task Preview_requires_a_tenant_and_effective_active_version()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.ActAs(null);
        var unauthorized = await harness.CodeConfiguration().PreviewAsync(new EmployeeCodePreviewRequest { EffectiveFrom = Today });
        Assert.Equal(ResultStatus.Unauthorized, unauthorized.Status);
    }

    [Fact]
    public async Task Preview_rejects_missing_master_values_and_dates_outside_the_active_version()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var department = await AddDepartmentAsync(harness, "IT");
        var lob = await AddLobAsync(harness, "LOB01");
        var configuration = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.RuleBased, EffectiveFrom = Today,
            EffectiveTo = Today.AddDays(1), Separator = "/"
        });
        Assert.True(configuration.Succeeded, configuration.Message);
        var rule = await harness.CodeConfiguration().SaveRuleAsync(null, new EmployeeCodeRuleRequest
        {
            Name = "Preview validation", Priority = 1, Status = EmployeeCodeRuleStatus.Active,
            Conditions = [new() { Field = EmployeeCodeConditionField.Department, ReferenceId = department }],
            Segments =
            [
                new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.DepartmentCode },
                new() { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 5 }
            ]
        });
        Assert.True(rule.Succeeded, rule.Message);

        var outside = await harness.CodeConfiguration().PreviewAsync(new EmployeeCodePreviewRequest { EffectiveFrom = Today.AddDays(2), DepartmentId = department });
        Assert.Equal(ResultStatus.ValidationFailed, outside.Status);

        var missing = await harness.CodeConfiguration().PreviewAsync(new EmployeeCodePreviewRequest
        {
            EffectiveFrom = Today, DepartmentId = Guid.NewGuid(), LobId = lob
        });
        Assert.Equal(ResultStatus.ValidationFailed, missing.Status);
        Assert.Equal(0, await harness.CreateUnscopedContext().EmployeeCodeSequences.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Preview_is_tenant_scoped_even_when_using_another_tenants_master_id()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var configuration = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.RuleBased, EffectiveFrom = Today
        });
        Assert.True(configuration.Succeeded, configuration.Message);
        var otherDepartment = OrganizationTestHarness.DepartmentId(SeedData.TenantIds.Demo02, "OPS");

        var result = await harness.ActAs(SeedData.TenantIds.Demo02).CodeConfiguration().PreviewAsync(new EmployeeCodePreviewRequest
        {
            EffectiveFrom = Today, DepartmentId = otherDepartment
        });

        Assert.NotEqual(ResultStatus.Success, result.Status);
        Assert.DoesNotContain("HC01", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task Rule_based_generation_matches_work_location_by_id_even_when_saved_code_is_stale()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var department = OrganizationTestHarness.DepartmentId(Tenant, "ENG");
        var lob = OrganizationTestHarness.LobId(Tenant, "LOB-IT");
        var configuration = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true,
            AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.RuleBased,
            Separator = "/",
            NextNumber = 1,
            Padding = 5,
            EffectiveFrom = Today
        });
        Assert.True(configuration.Succeeded, configuration.Message);

        var rule = await harness.CodeConfiguration().SaveRuleAsync(null, new EmployeeCodeRuleRequest
        {
            Name = "Bengaluru work location rule",
            Priority = 1,
            Status = EmployeeCodeRuleStatus.Active,
            Conditions = [new EmployeeCodeConditionRequest
            {
                Field = EmployeeCodeConditionField.WorkLocation,
                Operator = EmployeeCodeConditionOperator.Equals,
                ReferenceId = WorkLocation
            }],
            Segments =
            [
                new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.WorkLocationCode },
                new() { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 5 }
            ]
        });
        Assert.True(rule.Succeeded, rule.Message);

        using (var arrange = harness.CreateContext())
        {
            var condition = await arrange.EmployeeCodeRuleConditions.SingleAsync(c => c.EmployeeCodeRuleId == rule.Value!.Id);
            condition.Value = "OLD-LOCATION-CODE";
            await arrange.SaveChangesAsync();
        }

        var employee = await CreatePendingEmployeeAsync(harness, "Location One");
        var result = await harness.Employment().CreateChangeAsync(employee, Request(department, lob), "tester");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("WL-MUM/00001", await EmployeeCodeAsync(harness, employee));
    }

    [Fact]
    public async Task Location_condition_is_rejected_when_no_separate_location_master_exists()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var configuration = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true,
            AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.RuleBased,
            EffectiveFrom = Today
        });
        Assert.True(configuration.Succeeded, configuration.Message);

        var result = await harness.CodeConfiguration().SaveRuleAsync(null, new EmployeeCodeRuleRequest
        {
            Name = "Unsupported location rule",
            Priority = 1,
            Status = EmployeeCodeRuleStatus.Active,
            Conditions = [new() { Field = EmployeeCodeConditionField.Location, ReferenceId = WorkLocation }],
            Segments = [new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 5 }]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("separate Location master", result.Message);
    }

    [Fact]
    public async Task Default_rule_keeps_all_output_segments_and_padding_after_reload()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var configuration = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true,
            AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.RuleBased,
            Separator = "/",
            NextNumber = 1,
            Padding = 8,
            EffectiveFrom = Today
        });
        Assert.True(configuration.Succeeded, configuration.Message);

        var saved = await harness.CodeConfiguration().SaveRuleAsync(null, new EmployeeCodeRuleRequest
        {
            Name = "Test",
            Priority = 100,
            Status = EmployeeCodeRuleStatus.Active,
            IsDefault = true,
            Conditions = [],
            Segments =
            [
                new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.HoldingCompanyCode },
                new() { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.LobCode },
                new() { SequenceOrder = 3, SegmentType = EmployeeCodeSegmentType.OrganisationCode },
                new() { SequenceOrder = 4, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 8 }
            ]
        });
        Assert.True(saved.Succeeded, saved.Message);

        var reloaded = await harness.CodeConfiguration().GetRuleAsync(saved.Value!.Id);
        Assert.True(reloaded.Succeeded, reloaded.Message);
        Assert.True(reloaded.Value!.IsDefault);
        Assert.Empty(reloaded.Value.Conditions);
        Assert.Equal(
            new[] { EmployeeCodeSegmentType.HoldingCompanyCode, EmployeeCodeSegmentType.LobCode, EmployeeCodeSegmentType.OrganisationCode, EmployeeCodeSegmentType.SequentialNumber },
            reloaded.Value.Segments.OrderBy(s => s.SequenceOrder).Select(s => s.SegmentType));
        Assert.Equal(8, reloaded.Value.Segments.Single(s => s.SegmentType == EmployeeCodeSegmentType.SequentialNumber).PaddingLength);
    }

    [Fact]
    public async Task New_active_version_on_2026_09_13_can_use_a_versioned_rule_and_generate_code()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var department = await AddDepartmentAsync(harness, "IT");
        var lob = await AddLobAsync(harness, "LOB01");
        var oldVersion = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.RuleBased, Separator = "/",
            NextNumber = 1, Padding = 8, EffectiveFrom = new DateOnly(2026, 9, 2), EffectiveTo = new DateOnly(2026, 9, 12)
        });
        Assert.True(oldVersion.Succeeded, oldVersion.Message);

        var oldRule = await harness.CodeConfiguration().SaveRuleAsync(null, new EmployeeCodeRuleRequest
        {
            Name = "Test", Priority = 100, Status = EmployeeCodeRuleStatus.Active, IsDefault = true,
            Segments =
            [
                new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.HoldingCompanyCode },
                new() { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.LobCode },
                new() { SequenceOrder = 3, SegmentType = EmployeeCodeSegmentType.OrganisationCode },
                new() { SequenceOrder = 4, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 8 }
            ]
        });
        Assert.True(oldRule.Succeeded, oldRule.Message);

        var newVersion = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.RuleBased, Separator = "/",
            NextNumber = 1, Padding = 8, EffectiveFrom = new DateOnly(2026, 9, 13)
        });
        Assert.True(newVersion.Succeeded, newVersion.Message);

        var copiedRule = await harness.CodeConfiguration().SaveRuleAsync(oldRule.Value!.Id, new EmployeeCodeRuleRequest
        {
            ConfigurationVersionId = newVersion.Value!.VersionId,
            Name = "Test", Priority = 100, Status = EmployeeCodeRuleStatus.Active, IsDefault = true,
            Segments = oldRule.Value.Segments.Select(s => new EmployeeCodeSegmentRequest
            {
                SequenceOrder = s.SequenceOrder, SegmentType = s.SegmentType, PaddingLength = s.PaddingLength, FixedValue = s.FixedValue
            }).ToList()
        });
        Assert.True(copiedRule.Succeeded, copiedRule.Message);
        Assert.NotEqual(oldRule.Value.Id, copiedRule.Value!.Id);

        var employee = await CreatePendingEmployeeAsync(harness, "Effective Date");
        var request = Request(department, lob);
        request.EffectiveFrom = new DateOnly(2026, 9, 13);
        var result = await harness.Employment().CreateChangeAsync(employee, request, "tester");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("HC01/LOB01/ORG01/00000001", await EmployeeCodeAsync(harness, employee));
    }

    [Fact]
    public async Task Manual_assignment_rejects_duplicate_codes_without_creating_history()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var department = await AddDepartmentAsync(harness, "IT");
        var lob = await AddLobAsync(harness, "LOB01");
        var saved = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = false,
            AssignmentMode = EmployeeCodeAssignmentMode.Manual,
            GenerationMethod = null,
            Prefix = "EMP",
            EffectiveFrom = Today
        });
        Assert.True(saved.Succeeded, saved.Message);

        var first = await CreatePendingEmployeeAsync(harness, "Manual One");
        var firstRequest = Request(department, lob);
        firstRequest.EmployeeCode = "MAN-00001";
        Assert.True((await harness.Employment().CreateChangeAsync(first, firstRequest, "tester")).Succeeded);

        var second = await CreatePendingEmployeeAsync(harness, "Manual Two");
        var duplicate = await harness.Employment().CreateChangeAsync(second, firstRequest, "tester");

        Assert.False(duplicate.Succeeded);
        Assert.Equal(ResultStatus.Conflict, duplicate.Status);
        Assert.Null(await EmployeeCodeAsync(harness, second));
        Assert.Equal(0, await harness.CreateUnscopedContext().EmployeeEmploymentHistory.IgnoreQueryFilters().CountAsync(x => x.EmployeeId == second));
    }

    [Fact]
    public async Task Failed_rule_generation_rolls_back_code_sequence_and_history()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var department = await AddDepartmentAsync(harness, "IT");
        var lob = await AddLobAsync(harness, "LOB01");
        var configuration = await harness.CodeConfiguration().SaveAsync(new EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true,
            AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.RuleBased,
            Prefix = "EMP",
            Separator = "/",
            NextNumber = 1,
            Padding = 5,
            EffectiveFrom = Today
        });
        Assert.True(configuration.Succeeded, configuration.Message);

        var employee = await CreatePendingEmployeeAsync(harness, "No Match");
        var result = await harness.Employment().CreateChangeAsync(employee, Request(department, lob), "tester");

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Null(await EmployeeCodeAsync(harness, employee));
        using var raw = harness.CreateUnscopedContext();
        Assert.Equal(0, await raw.EmployeeEmploymentHistory.IgnoreQueryFilters().CountAsync(x => x.EmployeeId == employee));
        Assert.Equal(0, await raw.EmployeeCodeSequences.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant));
    }

    [Fact]
    public async Task Promotion_keeps_the_existing_employee_code()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var employee = OrganizationTestHarness.EmployeeId(Tenant, "EMP-001");
        var department = OrganizationTestHarness.DepartmentId(Tenant, "ENG");
        var request = Request(department, OrganizationTestHarness.LobId(Tenant, "LOB-IT"));
        request.DesignationId = Designation;
        var hire = await harness.Employment().CreateChangeAsync(employee, request, "tester");
        Assert.True(hire.Succeeded, hire.Message);

        var promotion = Request(department, OrganizationTestHarness.LobId(Tenant, "LOB-IT"));
        promotion.EffectiveFrom = Today.AddDays(30);
        promotion.ChangeReason = EmploymentChangeReason.Promotion;
        promotion.PositionChangeReasonId = OrganizationTestHarness.PositionChangeReasonId(Tenant, "PROMO");
        var changed = await harness.Employment().CreateChangeAsync(employee, promotion, "tester");

        Assert.True(changed.Succeeded, changed.Message);
        Assert.Equal("EMP-001", await EmployeeCodeAsync(harness, employee));
        Assert.Equal(2, await harness.CreateUnscopedContext().EmployeeEmploymentHistory.IgnoreQueryFilters().CountAsync(x => x.EmployeeId == employee));
    }

    private static EmploymentChangeRequest Request(Guid department, Guid lob) => new()
    {
        EffectiveFrom = Today,
        EmployeeCode = null,
        HoldingCompanyId = Holding,
        LobId = lob,
        OrganisationId = Organisation,
        DepartmentId = department,
        DesignationId = Designation,
        GradeId = Grade,
        CountryLocationId = Country,
        WorkLocationId = WorkLocation,
        PositionChangeReasonId = Reason,
        ChangeReason = EmploymentChangeReason.NewJoining,
        EmploymentType = EmploymentType.FullTime,
        EmploymentStatus = EmployeeStatus.Active
    };

    private static async Task<Guid> CreatePendingEmployeeAsync(OrganizationTestHarness harness, string firstName)
    {
        var result = await harness.Employees().CreatePersonalDetailsAsync(new EmployeePersonalDetailsRequest
        {
            FirstName = firstName,
            LastName = "Synthetic",
            DateOfJoining = Today,
            Gender = Gender.Male,
            BloodGroup = BloodGroup.OPositive,
            MaritalStatus = MaritalStatus.Single
        });
        Assert.True(result.Succeeded, result.Message);
        return result.Value!.Id;
    }

    private static async Task<string?> EmployeeCodeAsync(OrganizationTestHarness harness, Guid employeeId) =>
        await harness.CreateUnscopedContext().Employees.IgnoreQueryFilters().Where(x => x.Id == employeeId).Select(x => x.EmployeeCode).SingleAsync();

    private static async Task<Guid> AddDepartmentAsync(OrganizationTestHarness harness, string code)
    {
        var id = Guid.NewGuid();
        using var context = harness.CreateContext();
        context.Departments.Add(new Department { Id = id, TenantId = Tenant, Code = code, Name = code, IsActive = true });
        await context.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> AddLobAsync(OrganizationTestHarness harness, string code)
    {
        var id = Guid.NewGuid();
        using var context = harness.CreateContext();
        context.LinesOfBusiness.Add(new Lob { Id = id, TenantId = Tenant, Code = code, Name = code, HoldingCompanyId = Holding, IsActive = true });
        await context.SaveChangesAsync();
        return id;
    }
}
