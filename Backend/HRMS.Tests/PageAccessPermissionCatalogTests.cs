using HRMS.Application.Common;
using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.API.Controllers;
using HRMS.Application.DTOs.Authorization;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace HRMS.Tests;

public sealed class PageAccessPermissionCatalogTests
{
    [Fact]
    public async Task Update_returns_validation_result_when_requested_permission_is_missing_from_catalog()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        var missingPermissionId = SeedData.PermissionId(Permissions.WelcomeInvitation.View);

        await RemovePermissionFromCatalogAsync(database, missingPermissionId);

        await using var context = database.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System);

        var result = await service.UpdateAsync(roleId, new([Permissions.WelcomeInvitation.View]));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains("not provisioned", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_rejects_unknown_permission_without_logging_submitted_code_and_includes_safe_context()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        var logger = new RecordingLogger<PageAccessService>();

        await using var context = database.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System, logger);

        const string submittedUnknownCode = "password=do-not-log-token=secret";
        var result = await service.UpdateAsync(roleId, new([submittedUnknownCode]));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        var entry = Assert.Single(logger.Messages);
        var properties = Assert.Single(logger.Properties);
        Assert.Contains("UpdateRolePermissions", entry);
        Assert.Contains(tenantContext.TenantId!.Value.ToString(), entry);
        Assert.Contains(roleId.ToString(), entry);
        Assert.Contains("RejectedPermissionCount=1", entry);
        Assert.DoesNotContain(submittedUnknownCode, entry, StringComparison.Ordinal);
        Assert.DoesNotContain("password", entry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", entry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", entry, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("UpdateRolePermissions", properties["Operation"]);
        Assert.Equal(tenantContext.TenantId, properties["TenantId"]);
        Assert.Equal(roleId, properties["RoleId"]);
        Assert.Equal(1, properties["RejectedPermissionCount"]);
    }

    [Fact]
    public async Task Update_returns_actionable_error_when_catalog_provisioner_is_unavailable()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        var logger = new RecordingLogger<PageAccessService>();
        await RemovePermissionFromCatalogAsync(database, SeedData.PermissionId(Permissions.WelcomeInvitation.Send));

        await using var context = database.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System, logger);
        var before = await context.RolePermissions.AsNoTracking().Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionId).OrderBy(x => x).ToListAsync();

        var result = await service.UpdateAsync(roleId, new([Permissions.WelcomeInvitation.Send]));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains("Contact an administrator", result.Message, StringComparison.Ordinal);
        Assert.Empty(logger.Messages);
        Assert.Equal(before, await context.RolePermissions.AsNoTracking().Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionId).OrderBy(x => x).ToListAsync());
    }

    [Fact]
    public async Task Matrix_exposes_provisioned_permission_identifiers_without_filtering_grants()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        await RemovePermissionFromCatalogAsync(database, SeedData.PermissionId(Permissions.WelcomeInvitation.Send));

        await using var context = database.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System);

        var result = await service.GetMatrixAsync(SeedData.RoleId(RoleNames.TenantAdmin));

        Assert.True(result.Succeeded, result.Message);
        Assert.DoesNotContain(Permissions.WelcomeInvitation.Send, result.Value!.ProvisionedPermissions);
        Assert.Contains(Permissions.PageAccess.View, result.Value.GrantedPermissions);
    }

    [Fact]
    public async Task Update_saves_a_valid_provisioned_permission_set()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        await using var context = database.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System);

        var result = await service.UpdateAsync(SeedData.RoleId(RoleNames.TenantAdmin), new([Permissions.PageAccess.View]));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal([Permissions.PageAccess.View], result.Value!.GrantedPermissions);
    }

    [Fact]
    public async Task Authorized_page_access_save_repairs_missing_catalog_rows_without_granting_them()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var missingId = SeedData.PermissionId(Permissions.WelcomeInvitation.Send);
        await RemovePermissionFromCatalogAsync(database, missingId);

        await using var context = database.CreateContext(tenantContext);
        var provisioner = new PermissionCatalogProvisioner(context);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System,
            permissionCatalogProvisioner: provisioner);
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        var before = await context.RolePermissions.AsNoTracking().Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionId).OrderBy(x => x).ToListAsync();
        var result = await service.UpdateAsync(SeedData.RoleId(RoleNames.TenantAdmin), new([Permissions.WelcomeInvitation.Send]));

        Assert.True(result.Succeeded, result.Message);
        Assert.DoesNotContain(Permissions.WelcomeInvitation.Send, result.Value!.GrantedPermissions);
        Assert.Contains(Permissions.WelcomeInvitation.Send, result.Value.ProvisionedPermissions);
        var after = await context.RolePermissions.AsNoTracking().Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionId).OrderBy(x => x).ToListAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Provisioning_preserves_legacy_ids_294_and_304_by_permission_code()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        await using var context = database.CreateContext(new TestTenantContext());
        var scheduler = await context.Permissions.SingleAsync(x => x.Name == Permissions.Confirmation.SchedulerView);
        var dashboardAll = await context.Permissions.SingleAsync(x => x.Name == Permissions.Confirmation.DashboardViewAll);
        var hrbpSubmit = await context.Permissions.SingleAsync(x => x.Name == Permissions.Confirmation.ReviewHrbpSubmit);
        var oldIds = new[] { scheduler.Id, dashboardAll.Id, hrbpSubmit.Id };
        context.RolePermissions.RemoveRange(await context.RolePermissions.Where(x => oldIds.Contains(x.PermissionId)).ToListAsync());
        context.Permissions.RemoveRange(scheduler, dashboardAll, hrbpSubmit);
        await context.SaveChangesAsync();
        context.Permissions.AddRange(
            new Permission { Id = 294, Name = Permissions.Confirmation.SchedulerView, Description = "legacy scheduler" },
            new Permission { Id = 304, Name = Permissions.Confirmation.DashboardViewAll, Description = "legacy dashboard" });
        await context.SaveChangesAsync();

        await new PermissionCatalogProvisioner(context).EnsureProvisionedAsync();

        Assert.Equal(Permissions.Confirmation.SchedulerView, (await context.Permissions.SingleAsync(x => x.Id == 294)).Name);
        Assert.Equal(Permissions.Confirmation.DashboardViewAll, (await context.Permissions.SingleAsync(x => x.Id == 304)).Name);
    }

    [Fact]
    public async Task Provisioning_recognizes_all_welcome_invitation_codes_and_allocates_around_id_collisions()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        await using var context = database.CreateContext(new TestTenantContext());
        var welcomeCodes = new[] { Permissions.WelcomeInvitation.View, Permissions.WelcomeInvitation.Send,
            Permissions.WelcomeInvitation.Resend, Permissions.WelcomeInvitation.Retry, Permissions.WelcomeInvitation.ViewHistory };
        var removed = await context.Permissions.Where(x => welcomeCodes.Contains(x.Name)).ToListAsync();
        var removedIds = removed.Select(x => x.Id).ToArray();
        context.RolePermissions.RemoveRange(await context.RolePermissions.Where(x => removedIds.Contains(x.PermissionId)).ToListAsync());
        context.Permissions.RemoveRange(removed);
        context.Permissions.Add(new Permission { Id = 305, Name = "Legacy.Permission305", Description = "preserve" });
        await context.SaveChangesAsync();

        await new PermissionCatalogProvisioner(context).EnsureProvisionedAsync();
        await new PermissionCatalogProvisioner(context).EnsureProvisionedAsync();

        var provisioned = await context.Permissions.Where(x => welcomeCodes.Contains(x.Name)).ToListAsync();
        Assert.Equal(welcomeCodes.Length, provisioned.Count);
        Assert.Equal(welcomeCodes.Length, provisioned.Select(x => x.Id).Distinct().Count());
        Assert.DoesNotContain(provisioned, x => x.Id == 305);
        Assert.Equal("Legacy.Permission305", (await context.Permissions.SingleAsync(x => x.Id == 305)).Name);
    }

    [Fact]
    public async Task Permission_catalog_provisioning_is_idempotent_and_does_not_change_existing_role_grants()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        var removedId = SeedData.PermissionId(Permissions.WelcomeInvitation.ViewHistory);
        await RemovePermissionFromCatalogAsync(database, removedId);

        await using var context = database.CreateContext(tenantContext);
        var before = await context.RolePermissions.AsNoTracking().Where(x => x.RoleId == roleId)
            .Select(x => new { x.PermissionId }).ToListAsync();
        var provisioner = new PermissionCatalogProvisioner(context);
        await provisioner.EnsureProvisionedAsync();
        await provisioner.EnsureProvisionedAsync();
        var after = await context.RolePermissions.AsNoTracking().Where(x => x.RoleId == roleId)
            .Select(x => new { x.PermissionId }).ToListAsync();

        Assert.Equal(before.Select(x => x.PermissionId).Order(), after.Select(x => x.PermissionId).Order());
        Assert.Equal(1, await context.Permissions.CountAsync(x => x.Name == Permissions.WelcomeInvitation.ViewHistory));
    }

    [Fact]
    public async Task Update_fails_closed_for_incomplete_catalog_and_preserves_existing_grants()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        var missingPermissionIds = new[]
        {
            SeedData.PermissionId(Permissions.WelcomeInvitation.View),
            SeedData.PermissionId(Permissions.WelcomeInvitation.Send)
        };

        await using (var arrange = database.CreateContext(new TestTenantContext()))
        {
            await RemovePermissionRowsAsync(arrange, missingPermissionIds);
        }

        await using var context = database.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System);
        var before = await context.RolePermissions.AsNoTracking()
            .Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionId)
            .OrderBy(x => x)
            .ToListAsync();

        var result = await service.UpdateAsync(roleId, new([
            Permissions.Employee.View,
            Permissions.WelcomeInvitation.View,
            Permissions.WelcomeInvitation.Send
        ]));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        var after = await context.RolePermissions.AsNoTracking()
            .Where(x => x.RoleId == roleId)
            .Select(x => x.PermissionId)
            .OrderBy(x => x)
            .ToListAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Update_rejects_permission_changes_without_authenticated_tenant()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        await using var context = database.CreateContext(new TestTenantContext());
        var service = new PageAccessService(context, new TestTenantContext(), new RoleResolutionService(context), TimeProvider.System,
            permissionCatalogProvisioner: new PermissionCatalogProvisioner(context));
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        var before = await context.RolePermissions.Where(x => x.RoleId == roleId).Select(x => x.PermissionId).OrderBy(x => x).ToListAsync();

        var result = await service.UpdateAsync(roleId, new([Permissions.WelcomeInvitation.View]));

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Equal(before, await context.RolePermissions.Where(x => x.RoleId == roleId).Select(x => x.PermissionId).OrderBy(x => x).ToListAsync());
    }

    [Fact]
    public async Task Page_access_controller_returns_sanitized_http_409_for_permission_catalog_collision()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var tenantContext = new TestTenantContext(SeedData.TenantIds.Demo01, SeedData.Users[0].Id);
        var roleId = SeedData.RoleId(RoleNames.TenantAdmin);
        await RemovePermissionFromCatalogAsync(database, SeedData.PermissionId(Permissions.PageAccess.View));

        await using var context = database.CreateContext(tenantContext);
        var service = new PageAccessService(context, tenantContext, new RoleResolutionService(context), TimeProvider.System,
            permissionCatalogProvisioner: new ConflictingPermissionCatalogProvisioner());
        var controller = new PageAccessController(service);
        var action = await controller.Update(roleId, new([Permissions.PageAccess.View]), CancellationToken.None);
        var response = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        var envelope = Assert.IsType<ApiResponse<PageAccessMatrixDto>>(response.Value);
        Assert.False(envelope.Success);
        Assert.Equal("The permission catalog needs administrator repair before this change can be saved.", envelope.Message);
        Assert.DoesNotContain("Legacy.Permission294", envelope.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("connection", envelope.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", envelope.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", envelope.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(envelope.Data);
    }

    private sealed class ConflictingPermissionCatalogProvisioner : IPermissionCatalogProvisioner
    {
        public Task EnsureProvisionedAsync(CancellationToken cancellationToken = default) =>
            Task.FromException(new PermissionCatalogCollisionException("Unrecoverable catalog conflict."));
    }

    private static async Task RemovePermissionFromCatalogAsync(SqliteInMemoryDatabase database, int permissionId)
    {
        await using var context = database.CreateContext(new TestTenantContext());
        await RemovePermissionRowsAsync(context, [permissionId]);
    }

    private static async Task RemovePermissionRowsAsync(HRMS.Infrastructure.Persistence.HrmsDbContext context, IReadOnlyCollection<int> permissionIds)
    {
        var rolePermissions = await context.RolePermissions
            .Where(x => permissionIds.Contains(x.PermissionId))
            .ToListAsync();
        context.RolePermissions.RemoveRange(rolePermissions);
        var permissions = await context.Permissions
            .Where(x => permissionIds.Contains(x.Id))
            .ToListAsync();
        context.Permissions.RemoveRange(permissions);
        await context.SaveChangesAsync();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<IReadOnlyDictionary<string, object?>> Properties { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
                Properties.Add(properties.ToDictionary(property => property.Key, property => property.Value));
        }
    }
}
