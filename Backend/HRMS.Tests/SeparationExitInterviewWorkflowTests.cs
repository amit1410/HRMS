using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationExitInterviewConcurrencyTests
{
    [Fact]
    public async Task Employee_submit_vs_employee_submit()
    {
        using var database = new SqliteInMemoryDatabase(); var setup = await ExitInterviewTestData.CreateAsync(database); await ExitInterviewTestData.SaveDraftAsync(database, setup);
        await using var left = database.CreateContext(new TestTenantContext(setup.TenantId, setup.EmployeeUserId)); await using var right = database.CreateContext(new TestTenantContext(setup.TenantId, setup.EmployeeUserId));
        var results = await Task.WhenAll(ExitInterviewTestData.Service(left, setup, setup.EmployeeUserId).SubmitAsync(setup.SeparationId), ExitInterviewTestData.Service(right, setup, setup.EmployeeUserId).SubmitAsync(setup.SeparationId));
        Assert.Equal(2, results.Count(x => x.Succeeded)); await using var fresh = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); Assert.Equal(SeparationExitInterviewStatus.EmployeeSubmitted, await fresh.SeparationExitInterviews.Where(x => x.Id == setup.InterviewId).Select(x => x.Status).SingleAsync()); Assert.Equal(1, await fresh.SeparationExitInterviewEvents.CountAsync(x => x.ExitInterviewId == setup.InterviewId && x.EventType == SeparationExitInterviewEventType.EmployeeSubmitted));
    }

    [Fact]
    public async Task Employee_submit_vs_hr_complete()
    {
        using var database = new SqliteInMemoryDatabase(); var setup = await ExitInterviewTestData.CreateAsync(database); await ExitInterviewTestData.SaveDraftAsync(database, setup);
        await using var employee = database.CreateContext(new TestTenantContext(setup.TenantId, setup.EmployeeUserId)); await using var hr = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId));
        var results = await Task.WhenAll(Task.Run(async () => (await ExitInterviewTestData.Service(employee, setup, setup.EmployeeUserId).SubmitAsync(setup.SeparationId)).Succeeded), Task.Run(async () => (await ExitInterviewTestData.Service(hr, setup, setup.HrUserId).CompleteAsync(setup.SeparationId, new(null, null, null, SeparationExitInterviewRehireRecommendation.NotAssessed, null))).Succeeded));
        Assert.Equal(1, results.Count(x => x)); await using var fresh = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); Assert.Equal(SeparationExitInterviewStatus.EmployeeSubmitted, await fresh.SeparationExitInterviews.Where(x => x.Id == setup.InterviewId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Hr_complete_vs_reopen()
    {
        using var database = new SqliteInMemoryDatabase(); var setup = await ExitInterviewTestData.CreateAsync(database); await ExitInterviewTestData.SaveDraftAsync(database, setup); await ExitInterviewTestData.SubmitAsync(database, setup);
        await using var left = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); await using var right = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId));
        var results = await Task.WhenAll(ExitInterviewTestData.Service(left, setup, setup.HrUserId).CompleteAsync(setup.SeparationId, new(null, null, null, SeparationExitInterviewRehireRecommendation.NotAssessed, null)), ExitInterviewTestData.Service(right, setup, setup.HrUserId).ReopenAsync(setup.SeparationId, new("Concurrent reopen")));
        Assert.Equal(2, results.Count(x => x.Succeeded)); await using var fresh = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); Assert.Contains(await fresh.SeparationExitInterviews.Where(x => x.Id == setup.InterviewId).Select(x => x.Status).SingleAsync(), new[] { SeparationExitInterviewStatus.Completed, SeparationExitInterviewStatus.Reopened }); Assert.Equal(2, await fresh.SeparationExitInterviewEvents.CountAsync(x => x.ExitInterviewId == setup.InterviewId && (x.EventType == SeparationExitInterviewEventType.InterviewCompleted || x.EventType == SeparationExitInterviewEventType.InterviewReopened)));
    }

    [Fact]
    public async Task Hr_note_update_vs_complete()
    {
        using var database = new SqliteInMemoryDatabase(); var setup = await ExitInterviewTestData.CreateAsync(database); await ExitInterviewTestData.SaveDraftAsync(database, setup); await ExitInterviewTestData.SubmitAsync(database, setup);
        await using var left = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); await using var right = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId));
        var noteTask = Task.Run(async () => (await ExitInterviewTestData.Service(left, setup, setup.HrUserId).AddHrNoteAsync(setup.SeparationId, new("Concurrent note"))).Succeeded);
        var completeTask = Task.Run(async () => (await ExitInterviewTestData.Service(right, setup, setup.HrUserId).CompleteAsync(setup.SeparationId, new(null, null, null, SeparationExitInterviewRehireRecommendation.NotAssessed, null))).Succeeded);
        try { await Task.WhenAll(noteTask, completeTask); } catch (Microsoft.Data.Sqlite.SqliteException) { await completeTask; await using var retry = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); await ExitInterviewTestData.Service(retry, setup, setup.HrUserId).AddHrNoteAsync(setup.SeparationId, new("Concurrent note")); }
        await using var fresh = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); Assert.Equal(SeparationExitInterviewStatus.Completed, await fresh.SeparationExitInterviews.Where(x => x.Id == setup.InterviewId).Select(x => x.Status).SingleAsync()); Assert.Equal(1, await fresh.SeparationExitInterviewHrNotes.CountAsync(x => x.ExitInterviewId == setup.InterviewId));
    }

    [Fact]
    public async Task Reopen_vs_reopen()
    {
        using var database = new SqliteInMemoryDatabase(); var setup = await ExitInterviewTestData.CreateAsync(database); await ExitInterviewTestData.SaveDraftAsync(database, setup); await ExitInterviewTestData.SubmitAsync(database, setup); await ExitInterviewTestData.CompleteAsync(database, setup);
        await using var left = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); await using var right = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId));
        var results = await Task.WhenAll(ExitInterviewTestData.Service(left, setup, setup.HrUserId).ReopenAsync(setup.SeparationId, new("Rework one")), ExitInterviewTestData.Service(right, setup, setup.HrUserId).ReopenAsync(setup.SeparationId, new("Rework two")));
        Assert.Single(results, x => x.Succeeded); await using var fresh = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); Assert.Equal(1, await fresh.SeparationExitInterviewEvents.CountAsync(x => x.ExitInterviewId == setup.InterviewId && x.EventType == SeparationExitInterviewEventType.InterviewReopened));
    }

    [Fact]
    public async Task Template_publish_vs_template_publish()
    {
        using var database = new SqliteInMemoryDatabase(); var setup = await ExitInterviewTestData.CreateAsync(database); await using var seed = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); var service = ExitInterviewTestData.Service(seed, setup, setup.HrUserId); var version = await service.CreateVersionAsync(setup.TemplateId, new(2, new(2027, 1, 1), null, new[] { new ExitInterviewQuestionRequest("Q2", "Question 2", SeparationExitInterviewQuestionType.LongText, 1, true) })); Assert.True(version.Succeeded, version.Message);
        await using var left = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); await using var right = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); var results = await Task.WhenAll(ExitInterviewTestData.Service(left, setup, setup.HrUserId).PublishVersionAsync(version.Value!.Id), ExitInterviewTestData.Service(right, setup, setup.HrUserId).PublishVersionAsync(version.Value.Id)); Assert.All(results, x => Assert.True(x.Succeeded)); await using var fresh = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); Assert.Equal(1, await fresh.SeparationExitInterviewTemplateVersions.CountAsync(x => x.Id == version.Value.Id && x.Status == SeparationExitInterviewTemplateVersionStatus.Published));
    }
}

public sealed class SeparationExitInterviewFailureInjectionTests
{
    [Fact]
    public async Task Employee_submission_failure_rolls_back()
    { using var database = new SqliteInMemoryDatabase(); var setup = await ExitInterviewTestData.CreateAsync(database); await ExitInterviewTestData.SaveDraftAsync(database, setup); await using var failing = database.CreateContext(new TestTenantContext(setup.TenantId, setup.EmployeeUserId)); var result = await ExitInterviewTestData.Service(failing, setup, setup.EmployeeUserId, new ExitInterviewFailureInjector { FailSubmission = true }).SubmitAsync(setup.SeparationId); Assert.False(result.Succeeded); await using var fresh = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); Assert.Equal(SeparationExitInterviewStatus.NotStarted, await fresh.SeparationExitInterviews.Where(x => x.Id == setup.InterviewId).Select(x => x.Status).SingleAsync()); Assert.Equal(0, await fresh.SeparationExitInterviewEvents.CountAsync(x => x.ExitInterviewId == setup.InterviewId && x.EventType == SeparationExitInterviewEventType.EmployeeSubmitted)); }

    [Fact]
    public async Task Hr_completion_failure_rolls_back()
    { using var database = new SqliteInMemoryDatabase(); var setup = await ExitInterviewTestData.CreateAsync(database); await ExitInterviewTestData.SaveDraftAsync(database, setup); await ExitInterviewTestData.SubmitAsync(database, setup); await using var failing = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); var result = await ExitInterviewTestData.Service(failing, setup, setup.HrUserId, new ExitInterviewFailureInjector { FailCompletion = true }).CompleteAsync(setup.SeparationId, new(null, null, null, SeparationExitInterviewRehireRecommendation.NotAssessed, null)); Assert.False(result.Succeeded); await using var fresh = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); Assert.Equal(SeparationExitInterviewStatus.EmployeeSubmitted, await fresh.SeparationExitInterviews.Where(x => x.Id == setup.InterviewId).Select(x => x.Status).SingleAsync()); Assert.Equal(0, await fresh.SeparationExitInterviewEvents.CountAsync(x => x.ExitInterviewId == setup.InterviewId && x.EventType == SeparationExitInterviewEventType.InterviewCompleted)); }

    [Fact]
    public async Task Reopen_failure_rolls_back()
    { using var database = new SqliteInMemoryDatabase(); var setup = await ExitInterviewTestData.CreateAsync(database); await ExitInterviewTestData.SaveDraftAsync(database, setup); await ExitInterviewTestData.SubmitAsync(database, setup); await ExitInterviewTestData.CompleteAsync(database, setup); await using var failing = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); var result = await ExitInterviewTestData.Service(failing, setup, setup.HrUserId, new ExitInterviewFailureInjector { FailReopen = true }).ReopenAsync(setup.SeparationId, new("Injected failure")); Assert.False(result.Succeeded); await using var fresh = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); Assert.Equal(SeparationExitInterviewStatus.Completed, await fresh.SeparationExitInterviews.Where(x => x.Id == setup.InterviewId).Select(x => x.Status).SingleAsync()); Assert.Equal(0, await fresh.SeparationExitInterviewEvents.CountAsync(x => x.ExitInterviewId == setup.InterviewId && x.EventType == SeparationExitInterviewEventType.InterviewReopened)); }
}

internal sealed class ExitInterviewFailureInjector : IExitInterviewFailureInjector
{ public bool FailSubmission { get; init; } public bool FailCompletion { get; init; } public bool FailReopen { get; init; } public void BeforeEmployeeSubmissionCommit() { if (FailSubmission) throw new InvalidOperationException("Injected submission failure."); } public void BeforeHrCompletionCommit() { if (FailCompletion) throw new InvalidOperationException("Injected completion failure."); } public void BeforeReopenCommit() { if (FailReopen) throw new InvalidOperationException("Injected reopen failure."); } }

internal sealed record ExitInterviewFixture(Guid TenantId, Guid EmployeeId, Guid EmployeeUserId, Guid HrUserId, Guid SeparationId, Guid TemplateId, Guid InterviewId, Guid QuestionId);

internal static class ExitInterviewTestData
{
    public static async Task<ExitInterviewFixture> CreateAsync(SqliteInMemoryDatabase database)
    { var baseFixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2)); await using var db = database.CreateContext(new TestTenantContext(baseFixture.TenantId, baseFixture.HrUserId)); var service = Service(db, baseFixture, baseFixture.HrUserId); var template = await service.CreateTemplateAsync(new("EXIT-INTERVIEW", "Exit interview", null, new(2020, 1, 1), null, null, null)); Assert.True(template.Succeeded, template.Message); var version = await service.CreateVersionAsync(template.Value!.Id, new(1, new(2020, 1, 1), new(2026, 12, 31), new[] { new ExitInterviewQuestionRequest("Q1", "Why are you leaving?", SeparationExitInterviewQuestionType.LongText, 1, true) })); Assert.True(version.Succeeded, version.Message); Assert.True((await service.PublishVersionAsync(version.Value!.Id)).Succeeded); var assigned = await service.AssignAsync(baseFixture.SeparationId, new(version.Value.Id, baseFixture.HrUserId)); Assert.True(assigned.Succeeded, assigned.Message); return new(baseFixture.TenantId, baseFixture.EmployeeId, baseFixture.EmployeeUserId, baseFixture.HrUserId, baseFixture.SeparationId, template.Value.Id, assigned.Value!.Id, assigned.Value.Questions.Single().Id); }
    public static ExitInterviewService Service(HrmsDbContext db, ExitInterviewFixture fixture, Guid userId, IExitInterviewFailureInjector? injector = null) => new(db, new TestTenantContext(fixture.TenantId, userId), new EmployeeIdentityResolver(db, new TestTenantContext(fixture.TenantId, userId)), TimeProvider.System, injector);
    public static ExitInterviewService Service(HrmsDbContext db, NoticeFixture fixture, Guid userId, IExitInterviewFailureInjector? injector = null) => new(db, new TestTenantContext(fixture.TenantId, userId), new EmployeeIdentityResolver(db, new TestTenantContext(fixture.TenantId, userId)), TimeProvider.System, injector);
    public static async Task SaveDraftAsync(SqliteInMemoryDatabase database, ExitInterviewFixture setup) { await using var db = database.CreateContext(new TestTenantContext(setup.TenantId, setup.EmployeeUserId)); db.SeparationExitInterviewResponses.Add(new SeparationExitInterviewResponse { Id = Guid.NewGuid(), TenantId = setup.TenantId, ExitInterviewId = setup.InterviewId, QuestionId = setup.QuestionId, ResponseText = "New opportunity", SubmittedByEmployee = true, CreatedDate = DateTime.UtcNow }); await db.SaveChangesAsync(); }
    public static async Task SubmitAsync(SqliteInMemoryDatabase database, ExitInterviewFixture setup) { await using var db = database.CreateContext(new TestTenantContext(setup.TenantId, setup.EmployeeUserId)); var result = await Service(db, setup, setup.EmployeeUserId).SubmitAsync(setup.SeparationId); Assert.True(result.Succeeded, result.Message); }
    public static async Task CompleteAsync(SqliteInMemoryDatabase database, ExitInterviewFixture setup) { await using var db = database.CreateContext(new TestTenantContext(setup.TenantId, setup.HrUserId)); var result = await Service(db, setup, setup.HrUserId).CompleteAsync(setup.SeparationId, new(null, null, null, SeparationExitInterviewRehireRecommendation.NotAssessed, null)); Assert.True(result.Succeeded, result.Message); }
}
