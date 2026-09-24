using System.Diagnostics;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HRMS.Tests;

public sealed class SeparationExitInterviewLargeDataTests
{
    private readonly ITestOutputHelper output;

    public SeparationExitInterviewLargeDataTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task One_thousand_interviews_are_paged_filtered_and_aggregated_server_side()
    {
        using var database = new SqliteInMemoryDatabase();
        var baseFixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(baseFixture.TenantId, baseFixture.HrUserId));
        var service = new ExitInterviewService(db, new TestTenantContext(baseFixture.TenantId, baseFixture.HrUserId), new EmployeeIdentityResolver(db, new TestTenantContext(baseFixture.TenantId, baseFixture.HrUserId)), TimeProvider.System);
        var template = await service.CreateTemplateAsync(new("EXIT-LARGE", "Large-data exit interview", null, new(2020, 1, 1), null, null, null));
        Assert.True(template.Succeeded, template.Message);
        var questions = Enumerable.Range(1, 10).Select(i => new ExitInterviewQuestionRequest($"Q{i}", $"Question {i}", SeparationExitInterviewQuestionType.LongText, i, true)).ToArray();
        var version = await service.CreateVersionAsync(template.Value!.Id, new(1, new(2020, 1, 1), null, questions));
        Assert.True(version.Succeeded, version.Message);
        Assert.True((await service.PublishVersionAsync(version.Value!.Id)).Succeeded);

        var reasonId = Guid.NewGuid();
        db.SeparationReasons.Add(new HRMS.Domain.Entities.Separation.SeparationReason { Id = reasonId, TenantId = baseFixture.TenantId, Code = "LARGE", Name = "Large data", Category = SeparationReasonCategory.Resignation, EffectiveFrom = new(2020, 1, 1), CreatedDate = DateTime.UtcNow });
        var employees = Enumerable.Range(1, 1000).Select(i => new Employee { Id = Guid.NewGuid(), TenantId = baseFixture.TenantId, EmployeeCode = $"LARGE-{i:0000}", FirstName = "Exit", LastName = $"Employee {i}", Email = $"large-{i}@example.test", DateOfJoining = new(2020, 1, 1), CreatedDate = DateTime.UtcNow }).ToArray();
        var separations = employees.Select((employee, i) => new EmployeeSeparation { Id = Guid.NewGuid(), TenantId = baseFixture.TenantId, EmployeeId = employee.Id, SeparationNumber = $"LARGE-{i + 1:0000}", SeparationType = SeparationType.EmployeeInitiated, ReasonId = reasonId, InitiatedBy = EmployeeSeparationInitiator.Employee, RequestDate = new(2026, 1, 1), ProposedLastWorkingDate = new(2026, 10, 2), ApprovedLastWorkingDate = new(2026, 10, 2), Status = EmployeeSeparationStatus.Approved, CreatedDate = DateTime.UtcNow }).ToArray();
        var questionIds = await db.SeparationExitInterviewQuestions.Where(x => x.TemplateVersionId == version.Value.Id).OrderBy(x => x.Sequence).Select(x => x.Id).ToListAsync();
        var interviews = separations.Select((separation, i) => new SeparationExitInterview { Id = Guid.NewGuid(), TenantId = baseFixture.TenantId, EmployeeSeparationId = separation.Id, EmployeeId = separation.EmployeeId, TemplateVersionId = version.Value.Id, Status = i % 2 == 0 ? SeparationExitInterviewStatus.EmployeeSubmitted : SeparationExitInterviewStatus.EmployeeInProgress, AssignedAtUtc = DateTime.UtcNow.AddMinutes(-i), EmployeeSubmittedAtUtc = i % 2 == 0 ? DateTime.UtcNow : null, AssignedHrUserId = baseFixture.HrUserId, CreatedDate = DateTime.UtcNow }).ToArray();
        var responses = interviews.SelectMany((interview, i) => questionIds.Select(questionId => new SeparationExitInterviewResponse { Id = Guid.NewGuid(), TenantId = baseFixture.TenantId, ExitInterviewId = interview.Id, QuestionId = questionId, ResponseText = $"Response {i}", SubmittedByEmployee = true, CreatedDate = DateTime.UtcNow })).ToArray();
        db.Employees.AddRange(employees); db.EmployeeSeparations.AddRange(separations); db.SeparationExitInterviews.AddRange(interviews); db.SeparationExitInterviewResponses.AddRange(responses);
        await db.SaveChangesAsync();

        var clock = Stopwatch.StartNew();
        var page = await service.GetInboxAsync(new ExitInterviewInboxQuery { Page = 1, PageSize = 50 });
        var submitted = await service.GetInboxAsync(new ExitInterviewInboxQuery { Page = 1, PageSize = 50, Status = nameof(SeparationExitInterviewStatus.EmployeeSubmitted) });
        var assigned = await service.GetInboxAsync(new ExitInterviewInboxQuery { Page = 1, PageSize = 50, AssignedHrUserId = baseFixture.HrUserId });
        var analytics = await service.GetAnalyticsAsync();
        clock.Stop();

        Assert.True(page.Succeeded, page.Message); Assert.Equal(1000, page.Value!.TotalCount); Assert.Equal(50, page.Value.Items.Count);
        Assert.True(submitted.Succeeded, submitted.Message); Assert.Equal(500, submitted.Value!.TotalCount);
        Assert.True(assigned.Succeeded, assigned.Message); Assert.Equal(1000, assigned.Value!.TotalCount);
        Assert.True(analytics.Succeeded, analytics.Message); Assert.Equal(1000, analytics.Value!.AssignedCount); Assert.Equal(500, analytics.Value.EmployeeSubmittedCount); Assert.Equal(10000, await db.SeparationExitInterviewResponses.CountAsync());
        output.WriteLine($"Interviews: 1000; Questions per interview: 10; Responses: 10000; Page size: 50; Duration: {clock.Elapsed.TotalMilliseconds:0} ms");
        Assert.True(clock.Elapsed < TimeSpan.FromMinutes(2), $"Large-data acceptance exceeded two minutes: {clock.Elapsed}.");
    }
}
