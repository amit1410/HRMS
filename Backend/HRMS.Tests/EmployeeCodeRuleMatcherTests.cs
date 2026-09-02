using HRMS.Application.EmployeeCodes;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;

namespace HRMS.Tests;

public sealed class EmployeeCodeRuleMatcherTests
{
    [Fact]
    public void Chooses_lowest_priority_matching_rule()
    {
        var matcher = new EmployeeCodeRuleMatcher();
        var rules = new[]
        {
            new EmployeeCodeRule { Id = Guid.NewGuid(), Priority = 10, Status = EmployeeCodeRuleStatus.Active },
            new EmployeeCodeRule { Id = Guid.NewGuid(), Priority = 1, Status = EmployeeCodeRuleStatus.Active }
        };
        var context = new EmployeeCodeGenerationContext(DateOnly.FromDateTime(DateTime.Today), new Dictionary<EmployeeCodeConditionField, string?>(), new Dictionary<EmployeeCodeSegmentType, string?>());
        Assert.Equal(1, matcher.Match(rules, context)!.Priority);
    }

    [Fact]
    public void Renderer_rejects_missing_master_code()
    {
        var renderer = new EmployeeCodeRenderer();
        var rule = new EmployeeCodeRule { Segments = { new EmployeeCodeSegment { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.DepartmentCode } } };
        var context = new EmployeeCodeGenerationContext(new DateOnly(2026, 1, 1), new Dictionary<EmployeeCodeConditionField, string?>(), new Dictionary<EmployeeCodeSegmentType, string?>());
        var result = renderer.Render(rule, context, 1);
        Assert.Null(result.Code);
        Assert.Contains("DepartmentCode", result.Error);
    }

    [Fact]
    public void Renderer_formats_sequence_with_padding()
    {
        var renderer = new EmployeeCodeRenderer();
        var rule = new EmployeeCodeRule { Segments = { new EmployeeCodeSegment { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.FixedText, FixedValue = "EMP" }, new EmployeeCodeSegment { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 5 } } };
        var context = new EmployeeCodeGenerationContext(new DateOnly(2026, 1, 1), new Dictionary<EmployeeCodeConditionField, string?>(), new Dictionary<EmployeeCodeSegmentType, string?>());
        Assert.Equal("EMP-00007", renderer.Render(rule, context, 7).Code);
    }

    [Fact]
    public void Renderer_preserves_all_master_segment_order()
    {
        var renderer = new EmployeeCodeRenderer();
        var rule = new EmployeeCodeRule
        {
            Segments =
            {
                new EmployeeCodeSegment { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.HoldingCompanyCode },
                new EmployeeCodeSegment { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.LobCode },
                new EmployeeCodeSegment { SequenceOrder = 3, SegmentType = EmployeeCodeSegmentType.OrganisationCode },
                new EmployeeCodeSegment { SequenceOrder = 4, SegmentType = EmployeeCodeSegmentType.DepartmentCode },
                new EmployeeCodeSegment { SequenceOrder = 5, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 5 }
            }
        };
        var context = new EmployeeCodeGenerationContext(
            new DateOnly(2026, 9, 1),
            new Dictionary<EmployeeCodeConditionField, string?>(),
            new Dictionary<EmployeeCodeSegmentType, string?>
            {
                [EmployeeCodeSegmentType.HoldingCompanyCode] = "HC01",
                [EmployeeCodeSegmentType.LobCode] = "LOB01",
                [EmployeeCodeSegmentType.OrganisationCode] = "ORG01",
                [EmployeeCodeSegmentType.DepartmentCode] = "IT"
            });

        Assert.Equal("HC01/LOB01/ORG01/IT/00001", renderer.Render(rule, context, 1, "/").Code);
    }
}
