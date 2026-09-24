using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;

namespace HRMS.Tests;

public sealed class SeparationExitInterviewFoundationTests
{
    [Fact]
    public void Published_version_is_distinct_from_draft_and_has_effective_dates()
    {
        var version = new SeparationExitInterviewTemplateVersion { Status = SeparationExitInterviewTemplateVersionStatus.Published, EffectiveFrom = new DateOnly(2026, 1, 1) };
        Assert.Equal(SeparationExitInterviewTemplateVersionStatus.Published, version.Status);
        Assert.Equal(new DateOnly(2026, 1, 1), version.EffectiveFrom);
    }

    [Fact]
    public void Employee_contract_contains_no_confidential_hr_fields()
    {
        var names = typeof(ExitInterviewEmployeeDto).GetProperties().Select(x => x.Name).ToHashSet();
        Assert.DoesNotContain("ConfidentialNotes", names);
        Assert.DoesNotContain("RehireRecommendation", names);
        Assert.DoesNotContain("ReasonComment", names);
    }

    [Fact]
    public void Response_revisions_and_events_are_append_only_collections()
    {
        var interview = new SeparationExitInterview { TenantId = Guid.NewGuid() };
        interview.ResponseRevisions.Add(new SeparationExitInterviewResponseRevision { Id = Guid.NewGuid(), TenantId = interview.TenantId, ExitInterviewId = interview.Id, QuestionId = Guid.NewGuid(), ActorUserId = Guid.NewGuid() });
        interview.Events.Add(new SeparationExitInterviewEvent { Id = Guid.NewGuid(), TenantId = interview.TenantId, ExitInterviewId = interview.Id, EventType = SeparationExitInterviewEventType.InterviewAssigned });
        Assert.Single(interview.ResponseRevisions);
        Assert.Single(interview.Events);
    }

    [Fact]
    public void Rehire_recommendation_is_input_only()
    {
        var interview = new SeparationExitInterview { RehireRecommendation = SeparationExitInterviewRehireRecommendation.Conditional };
        Assert.Equal(SeparationExitInterviewRehireRecommendation.Conditional, interview.RehireRecommendation);
        Assert.Null(interview.FinalizedAtUtc);
    }
}
