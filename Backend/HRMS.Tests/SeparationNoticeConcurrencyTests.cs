using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRMS.Tests;

public sealed class SeparationNoticeConcurrencyTests
{
    [Fact] public async Task Waiver_vs_waiver() { using var db = new NoticeFileDatabase(); var f = await NoticeTestData.CreateApprovedAsync(db.CreateContext, new(2026, 10, 2)); var results = await RaceAsync(db, f, (s, i) => s.ApplyNoticeWaiverAsync(f.SeparationId, new(5, $"Waiver {i}"))); Assert.Contains(results, x => x.Succeeded); Assert.True(results.All(x => x.Succeeded || x.Status == ResultStatus.Conflict || x.Status == ResultStatus.ValidationFailed)); }
    [Fact] public async Task Lwd_revision_vs_lwd_revision() { using var db = new NoticeFileDatabase(); var f = await NoticeTestData.CreateApprovedAsync(db.CreateContext, new(2026, 10, 2)); var results = await RaceAsync(db, f, (s, i) => s.ReviseApprovedLwdAsync(f.SeparationId, new(i == 0 ? new(2026, 10, 10) : new(2026, 10, 12), $"Revision {i}"))); Assert.Contains(results, x => x.Succeeded); Assert.True(results.All(x => x.Succeeded || x.Status == ResultStatus.Conflict)); }
    [Fact] public async Task Lwd_revision_vs_waiver() { using var db = new NoticeFileDatabase(); var f = await NoticeTestData.CreateApprovedAsync(db.CreateContext, new(2026, 10, 2)); var results = await RaceAsync(db, f, (s, i) => i == 0 ? s.ReviseApprovedLwdAsync(f.SeparationId, new(new(2026, 10, 10), "Revision")) : s.ApplyNoticeWaiverAsync(f.SeparationId, new(5, "Waiver"))); Assert.Contains(results, x => x.Succeeded); Assert.True(results.All(x => x.Succeeded || x.Status == ResultStatus.Conflict || x.Status == ResultStatus.ValidationFailed)); }
    [Fact] public async Task Lwd_revision_vs_hr_action() { using var db = new NoticeFileDatabase(); var f = await NoticeTestData.CreateApprovedAsync(db.CreateContext, new(2026, 10, 2)); var results = await RaceAsync(db, f, (s, i) => i == 0 ? s.ReviseApprovedLwdAsync(f.SeparationId, new(new(2026, 10, 10), "Revision")) : s.ApplyNoticeWaiverAsync(f.SeparationId, new(5, "HR action"))); Assert.Contains(results, x => x.Succeeded); Assert.True(results.All(x => x.Succeeded || x.Status == ResultStatus.Conflict || x.Status == ResultStatus.ValidationFailed)); }
    [Fact] public async Task Waiver_vs_hr_action() { using var db = new NoticeFileDatabase(); var f = await NoticeTestData.CreateApprovedAsync(db.CreateContext, new(2026, 10, 2)); var results = await RaceAsync(db, f, (s, i) => i == 0 ? s.ApplyNoticeWaiverAsync(f.SeparationId, new(5, "Waiver")) : s.ReviseApprovedLwdAsync(f.SeparationId, new(new(2026, 10, 10), "HR action"))); Assert.Contains(results, x => x.Succeeded); Assert.True(results.All(x => x.Succeeded || x.Status == ResultStatus.Conflict || x.Status == ResultStatus.ValidationFailed)); }
    [Fact] public async Task Retry_after_concurrency_conflict() { using var db = new NoticeFileDatabase(); var f = await NoticeTestData.CreateApprovedAsync(db.CreateContext, new(2026, 10, 2)); var results = await RaceAsync(db, f, (s, _) => s.ReviseApprovedLwdAsync(f.SeparationId, new(new(2026, 10, 10), "Concurrent revision"))); Assert.Contains(results, x => x.Succeeded); await using var fresh = db.CreateContext(new TestTenantContext(f.TenantId, f.HrUserId)); var retry = await NoticeTestData.CreateService(fresh, f.TenantId, f.HrUserId).ReviseApprovedLwdAsync(f.SeparationId, new(new(2026, 10, 11), "Fresh retry")); Assert.True(retry.Succeeded, retry.Message); }

    private static async Task<Result<SeparationNoticeDto>[]> RaceAsync(NoticeFileDatabase db, NoticeFixture fixture, Func<SeparationService, int, Task<Result<SeparationNoticeDto>>> operation)
    {
        using var barrier = new Barrier(2);
        var tasks = Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
        {
            await using var context = db.CreateContext(new TestTenantContext(fixture.TenantId, i == 0 ? fixture.HrUserId : Guid.NewGuid()));
            var service = NoticeTestData.CreateService(context, fixture.TenantId, i == 0 ? fixture.HrUserId : Guid.NewGuid());
            barrier.SignalAndWait();
            return await operation(service, i);
        })).ToArray();
        return await Task.WhenAll(tasks);
    }
}

internal sealed class NoticeFileDatabase : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"hrms-notice-{Guid.NewGuid():N}.db");
    public NoticeFileDatabase()
    {
        using var context = CreateContext(new TestTenantContext());
        context.Database.EnsureCreated();
    }
    public HrmsDbContext CreateContext(ITenantContext tenant)
    {
        var connection = new SqliteConnection($"Data Source={path};Default Timeout=30");
        connection.Open();
        return new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseSqlite(connection).Options, tenant);
    }
    public void Dispose()
    {
        try { File.Delete(path); } catch { }
    }
}
