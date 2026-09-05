using HRMS.Application.Common;
using HRMS.Application.DTOs.AccountEmployeeLinks;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Xunit.Sdk;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerAcceptanceFactAttribute : FactAttribute
{
    public SqlServerAcceptanceFactAttribute()
    {
        if (!SqlServerAcceptanceRun.IsConfigured)
            Skip = "SQL Server acceptance tests not executed: HRMS_SQLSERVER_TEST_SERVER is absent.";
    }
}

[CollectionDefinition("SQL Server Phase 3B", DisableParallelization = true)]
public sealed class SqlServerPhase3BCollection : ICollectionFixture<SqlServerPhase3BFixture> { }

[Collection("SQL Server Phase 3B")]
public sealed class SqlServerPhase3BScenarioTests
{
    private readonly SqlServerPhase3BFixture _fixture;

    public SqlServerPhase3BScenarioTests(SqlServerPhase3BFixture fixture) => _fixture = fixture;

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task Two_accounts_competing_for_one_employee_leave_one_current_mapping_and_event()
    {
        var run = Require();
        var first = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "race-a");
        var second = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "race-b");
        var sharedEmployee = first.EmployeeId;
        var results = await RaceAsync(
            () => _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA).LinkAsync(first.UserId, new(sharedEmployee, null, "race")),
            () => _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorB).LinkAsync(second.UserId, new(sharedEmployee, null, "race")));
        Assert.Single(results, x => x?.Succeeded == true);
        await AssertPersistedOneLinkAsync(run, 0, SqlServerPhase3BFixture.TenantA, sharedEmployee);
        Assert.Equal(1, await EventCountAsync(run, 0, first.UserId) + await EventCountAsync(run, 0, second.UserId));
    }

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task One_account_competing_for_two_employees_leaves_one_current_mapping()
    {
        var run = Require();
        var first = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "one-a");
        var second = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "one-b");
        var results = await RaceAsync(
            () => _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA)
                .LinkAsync(first.UserId, new(first.EmployeeId, null, "race")),
            () => _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA)
                .LinkAsync(first.UserId, new(second.EmployeeId, null, "race")));
        Assert.Single(results, x => x?.Succeeded == true);
        Assert.Equal(1, await CurrentCountAsync(run, 0, first.UserId));
        Assert.Equal(1, await EventCountAsync(run, 0, first.UserId));
    }

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task Occupied_target_and_failed_replacement_preserve_the_original_association()
    {
        var run = Require();
        var first = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "occupied-a");
        var second = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "occupied-b");
        var original = await _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA)
            .LinkAsync(first.UserId, new(first.EmployeeId, null, "original"));
        var occupied = await _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA)
            .LinkAsync(second.UserId, new(second.EmployeeId, null, "occupied"));
        Assert.True(original.Succeeded);
        Assert.True(occupied.Succeeded);
        var replacement = await _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA)
            .ReplaceAsync(first.UserId, new(original.Value!.CurrentLink!.LinkId, first.EmployeeId, original.Value.Revision, second.EmployeeId, "occupied target"));
        Assert.Equal(ResultStatus.Conflict, replacement.Status);
        var persisted = await ReadCurrentAsync(run, 0, first.UserId);
        Assert.Equal(first.EmployeeId, persisted!.EmployeeId);
        Assert.Equal(1, await EventCountAsync(run, 0, first.UserId));
    }

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task Competing_replacements_and_unlink_do_not_create_duplicate_current_state()
    {
        var run = Require();
        var pair = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "replace-race");
        var targetA = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "replace-target-a");
        var targetB = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "replace-target-b");
        var service = _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA);
        var linked = await service.LinkAsync(pair.UserId, new(pair.EmployeeId, null, "initial"));
        Assert.True(linked.Succeeded);
        var link = linked.Value!.CurrentLink!;
        var results = await RaceAsync(
            () => _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA).ReplaceAsync(pair.UserId, new(link.LinkId, pair.EmployeeId, linked.Value.Revision, targetA.EmployeeId, "replace-a")),
            () => _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorB).UnlinkAsync(pair.UserId, new(link.LinkId, pair.EmployeeId, linked.Value.Revision, "unlink")));
        Assert.Single(results, x => x?.Succeeded == true);
        Assert.InRange(await CurrentCountAsync(run, 0, pair.UserId), 0, 1);
        Assert.Equal(2, await EventCountAsync(run, 0, pair.UserId));
        Assert.DoesNotContain(await ReadEventsAsync(run, 0, pair.UserId), x => x.Operation == "Replace" && x.AfterEmployeeId == targetB.EmployeeId);

        var competing = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "replace-race-2");
        var targetC = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "replace-target-c");
        var targetD = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "replace-target-d");
        var initial = await _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA).LinkAsync(competing.UserId, new(competing.EmployeeId, null, "initial"));
        Assert.True(initial.Succeeded);
        var initialLink = initial.Value!.CurrentLink!;
        var competingResults = await RaceAsync(
            () => _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA).ReplaceAsync(competing.UserId, new(initialLink.LinkId, competing.EmployeeId, initial.Value.Revision, targetC.EmployeeId, "replace-c")),
            () => _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorB).ReplaceAsync(competing.UserId, new(initialLink.LinkId, competing.EmployeeId, initial.Value.Revision, targetD.EmployeeId, "replace-d")));
        Assert.Single(competingResults, x => x?.Succeeded == true);
        Assert.Equal(1, await CurrentCountAsync(run, 0, competing.UserId));
        Assert.Equal(2, await EventCountAsync(run, 0, competing.UserId));
    }

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task Audit_failure_rolls_back_event_and_current_state()
    {
        var run = Require();
        var pair = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "audit");
        var context = run.CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA));
        var service = new AccountEmployeeLinkService(context, new TestTenantContext(SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA), beforeSaveHook: () => throw new InvalidOperationException("synthetic audit failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkAsync(pair.UserId, new(pair.EmployeeId, null, "fail")));
        Assert.Equal(0, await EventCountAsync(run, 0, pair.UserId));
        Assert.Equal(0, await CurrentCountAsync(run, 0, pair.UserId));
    }

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task Stale_revisions_reject_repeated_submission_and_unlink_relink_sequences()
    {
        var run = Require();
        var pair = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "stale");
        var service = _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA);
        var linked = await service.LinkAsync(pair.UserId, new(pair.EmployeeId, null, "link"));
        var unlink = await service.UnlinkAsync(pair.UserId, new(linked.Value!.CurrentLink!.LinkId, pair.EmployeeId, linked.Value.Revision, "unlink"));
        Assert.True(unlink.Succeeded);
        var stale = await service.LinkAsync(pair.UserId, new(pair.EmployeeId, linked.Value.Revision, "replay"));
        Assert.Equal(ResultStatus.Conflict, stale.Status);
        var relink = await service.LinkAsync(pair.UserId, new(pair.EmployeeId, unlink.Value!.Revision, "relink"));
        Assert.True(relink.Succeeded);
        Assert.Equal(3, await EventCountAsync(run, 0, pair.UserId));
    }

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task Historical_foreign_keys_reject_cross_tenant_rows_and_deletion_after_unlink()
    {
        var run = Require();
        var pair = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "history");
        var service = _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA);
        var linked = await service.LinkAsync(pair.UserId, new(pair.EmployeeId, null, "history"));
        await service.UnlinkAsync(pair.UserId, new(linked.Value!.CurrentLink!.LinkId, pair.EmployeeId, linked.Value.Revision, "history"));
        await using var db = run.CreateTenantContext(0, new TestTenantContext());
        db.Users.Remove(await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == pair.UserId));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var employeeDb = run.CreateTenantContext(0, new TestTenantContext());
        employeeDb.Employees.Remove(await employeeDb.Employees.IgnoreQueryFilters().SingleAsync(x => x.Id == pair.EmployeeId));
        await Assert.ThrowsAsync<DbUpdateException>(() => employeeDb.SaveChangesAsync());

        await using var fkDb = run.CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA));
        fkDb.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
        {
            Id = Guid.NewGuid(), TenantId = SqlServerPhase3BFixture.TenantB,
            SubjectUserId = pair.UserId, ActorUserId = SqlServerPhase3BFixture.ActorA,
            Sequence = 99, Operation = "Link", NewLinkId = Guid.NewGuid(),
            AfterEmployeeId = pair.EmployeeId, OccurredAtUtc = DateTime.UtcNow,
            Reason = "cross tenant", CorrelationId = Guid.NewGuid().ToString("N")
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => fkDb.SaveChangesAsync());

        await using var immutableDb = run.CreateTenantContext(0, new TestTenantContext());
        var historicalEvent = await immutableDb.AccountEmployeeLinkEvents.IgnoreQueryFilters().SingleAsync(x => x.SubjectUserId == pair.UserId && x.Operation == "Link");
        historicalEvent.Reason = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => immutableDb.SaveChangesAsync());
    }

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task Grant_revocation_and_subject_deactivation_are_seen_by_subsequent_operations()
    {
        Require();
        var pair = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "revocation");
        await using var db = _fixture.RequireRun().CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA));
        var roleId = HRMS.Infrastructure.Persistence.Seed.SeedData.RoleId(HRMS.Domain.Authorization.RoleNames.AccountLinkAdministrator);
        var manageId = HRMS.Infrastructure.Persistence.Seed.SeedData.PermissionId(HRMS.Domain.Authorization.Permissions.AccountEmployeeLink.Manage);
        db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == roleId && x.PermissionId == manageId));
        await db.SaveChangesAsync();
        Assert.Equal(ResultStatus.Forbidden, (await _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA).LinkAsync(pair.UserId, new(pair.EmployeeId, null, "revoked"))).Status);

        await using var restore = _fixture.RequireRun().CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA));
        restore.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = manageId });
        await restore.SaveChangesAsync();
        var subject = await restore.Users.SingleAsync(x => x.Id == pair.UserId);
        subject.IsActive = false;
        await restore.SaveChangesAsync();
        Assert.Equal(ResultStatus.Conflict, (await _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA).LinkAsync(pair.UserId, new(pair.EmployeeId, null, "inactive"))).Status);
    }

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task Grant_revocation_and_actor_deactivation_race_link_with_permitted_ordering()
    {
        var run = Require();
        var pair = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "state-race");
        var barrier = new Barrier(2);
        var revoke = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await using var db = run.CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA));
            var roleId = HRMS.Infrastructure.Persistence.Seed.SeedData.RoleId(HRMS.Domain.Authorization.RoleNames.AccountLinkAdministrator);
            var permissionId = HRMS.Infrastructure.Persistence.Seed.SeedData.PermissionId(HRMS.Domain.Authorization.Permissions.AccountEmployeeLink.Manage);
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == roleId && x.PermissionId == permissionId));
            await db.SaveChangesAsync();
        });
        var link = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA)
                .LinkAsync(pair.UserId, new(pair.EmployeeId, null, "grant race"));
        });
        await Task.WhenAll(revoke, link);
        var linkResult = await link;
        Assert.True(linkResult.Status is ResultStatus.Success or ResultStatus.Forbidden);
        Assert.Equal(linkResult.Succeeded ? 1 : 0, await EventCountAsync(run, 0, pair.UserId));

        var activePair = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "actor-race");
        await using var restore = run.CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA));
        var administratorRole = HRMS.Infrastructure.Persistence.Seed.SeedData.RoleId(HRMS.Domain.Authorization.RoleNames.AccountLinkAdministrator);
        var managePermission = HRMS.Infrastructure.Persistence.Seed.SeedData.PermissionId(HRMS.Domain.Authorization.Permissions.AccountEmployeeLink.Manage);
        restore.RolePermissions.Add(new RolePermission { RoleId = administratorRole, PermissionId = managePermission });
        await restore.SaveChangesAsync();
        var actorBarrier = new Barrier(2);
        var deactivateActor = Task.Run(async () =>
        {
            actorBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await using var db = run.CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA));
            var actor = await db.Users.SingleAsync(x => x.Id == SqlServerPhase3BFixture.ActorA);
            actor.IsActive = false;
            await db.SaveChangesAsync();
        });
        var actorLink = Task.Run(async () =>
        {
            actorBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA)
                .LinkAsync(activePair.UserId, new(activePair.EmployeeId, null, "actor race"));
        });
        await Task.WhenAll(deactivateActor, actorLink);
        var actorLinkResult = await actorLink;
        Assert.True(actorLinkResult.Status is ResultStatus.Success or ResultStatus.Forbidden);
        Assert.Equal(actorLinkResult.Succeeded ? 1 : 0, await EventCountAsync(run, 0, activePair.UserId));
        await using var reactivate = run.CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA));
        var actorForNextScenario = await reactivate.Users.SingleAsync(x => x.Id == SqlServerPhase3BFixture.ActorA);
        actorForNextScenario.IsActive = true;
        await reactivate.SaveChangesAsync();
    }

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task A_deadlock_inside_linking_transaction_has_one_victim_and_no_partial_event()
    {
        var run = Require();
        var first = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "deadlock-a");
        var second = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "deadlock-b");
        var barrier = new Barrier(2);
        var firstDb = run.CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA));
        var secondDb = run.CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorB));
        async Task LockThenCrossAsync(HRMS.Infrastructure.Persistence.HrmsDbContext db, Guid own, Guid other)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Users] SET [FirstName] = [FirstName] WHERE [Id] = {own}");
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Users] SET [LastName] = [LastName] WHERE [Id] = {other}");
        }
        async Task<Result<AccountEmployeeCurrentStateDto>?> ExecuteAsync(HRMS.Infrastructure.Persistence.HrmsDbContext db, Guid subject, Guid employee, Guid own, Guid other)
        {
            try
            {
                var service = new AccountEmployeeLinkService(db, new TestTenantContext(SqlServerPhase3BFixture.TenantA, db == firstDb ? SqlServerPhase3BFixture.ActorA : SqlServerPhase3BFixture.ActorB), beforeSaveHook: () => LockThenCrossAsync(db, own, other));
                return await service.LinkAsync(subject, new(employee, null, "deadlock"));
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
                return null;
            }
        }
        var outcomes = await Task.WhenAll(
            ExecuteAsync(firstDb, first.UserId, first.EmployeeId, first.UserId, second.UserId),
            ExecuteAsync(secondDb, second.UserId, second.EmployeeId, second.UserId, first.UserId));
        await firstDb.DisposeAsync();
        await secondDb.DisposeAsync();
        Assert.Single(outcomes, x => x?.Succeeded == true);
        Assert.Single(outcomes, x => x is null);
        Assert.Equal(1, await EventCountAsync(run, 0, first.UserId) + await EventCountAsync(run, 0, second.UserId));
        Assert.InRange(await CurrentCountAsync(run, 0, first.UserId) + await CurrentCountAsync(run, 0, second.UserId), 0, 1);
    }

    [SqlServerAcceptanceFact, Trait("Category", "SqlServerAcceptance")]
    public async Task Lock_contention_is_bounded_and_leaves_a_consistent_result()
    {
        var run = Require();
        var pair = await _fixture.AddPairAsync(0, SqlServerPhase3BFixture.TenantA, "lock");
        await using var blocker = run.CreateTenantContext(0, new TestTenantContext(SqlServerPhase3BFixture.TenantA));
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Users] SET [FirstName] = [FirstName] WHERE [Id] = {pair.UserId}");
        var operation = _fixture.Service(0, SqlServerPhase3BFixture.TenantA, SqlServerPhase3BFixture.ActorA).LinkAsync(pair.UserId, new(pair.EmployeeId, null, "lock"));
        var completedWhileBlocked = await Task.WhenAny(operation, Task.Delay(TimeSpan.FromMilliseconds(500))) == operation;
        await transaction.RollbackAsync();
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(completedWhileBlocked);
        Assert.True(result.Succeeded || result.Status is ResultStatus.Conflict or ResultStatus.Forbidden);
        Assert.InRange(await CurrentCountAsync(run, 0, pair.UserId), 0, 1);
    }

    private SqlServerAcceptanceRun Require() =>
        _fixture.Run ?? throw SkipException.ForSkip("SQL Server acceptance tests not executed: SQL Server configuration is absent.");

    private static async Task<Result<AccountEmployeeCurrentStateDto>?[]> RaceAsync(
        Func<Task<Result<AccountEmployeeCurrentStateDto>>> first,
        Func<Task<Result<AccountEmployeeCurrentStateDto>>> second)
    {
        using var barrier = new Barrier(2);
        var tasks = new[] { SafeCall(first, barrier), SafeCall(second, barrier) };
        return await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task<Result<AccountEmployeeCurrentStateDto>?> SafeCall(
        Func<Task<Result<AccountEmployeeCurrentStateDto>>> call,
        Barrier barrier)
    {
        try
        {
            await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            return await call().WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (DbUpdateException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static async Task<long> EventCountAsync(SqlServerAcceptanceRun run, int database, Guid userId)
    {
        await using var db = run.CreateTenantContext(database, new TestTenantContext());
        return await db.AccountEmployeeLinkEvents.IgnoreQueryFilters().LongCountAsync(x => x.SubjectUserId == userId);
    }

    private static async Task<long> CurrentCountAsync(SqlServerAcceptanceRun run, int database, Guid userId)
    {
        await using var db = run.CreateTenantContext(database, new TestTenantContext());
        return await db.AccountEmployeeCurrentLinks.IgnoreQueryFilters().LongCountAsync(x => x.UserId == userId);
    }

    private static async Task<AccountEmployeeCurrentLink?> ReadCurrentAsync(SqlServerAcceptanceRun run, int database, Guid userId)
    {
        await using var db = run.CreateTenantContext(database, new TestTenantContext());
        return await db.AccountEmployeeCurrentLinks.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId);
    }

    private static async Task AssertPersistedOneLinkAsync(SqlServerAcceptanceRun run, int database, Guid tenantId, Guid employeeId)
    {
        await using var db = run.CreateTenantContext(database, new TestTenantContext());
        Assert.Equal(1, await db.AccountEmployeeCurrentLinks.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId));
    }

    private static async Task<IReadOnlyList<AccountEmployeeLinkEvent>> ReadEventsAsync(SqlServerAcceptanceRun run, int database, Guid userId)
    {
        await using var db = run.CreateTenantContext(database, new TestTenantContext());
        return await db.AccountEmployeeLinkEvents.IgnoreQueryFilters().AsNoTracking().Where(x => x.SubjectUserId == userId).ToListAsync();
    }
}
