using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Security;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class EmployeePortalAccountServiceTests
{
    [Fact]
    public async Task Create_creates_inactive_employee_account_role_link_and_hashed_invitation()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(fixture.EmployeeId, new());

        Assert.True(result.Succeeded);
        Assert.Equal("InvitationPending", result.Value!.Account.State);
        Assert.NotNull(result.Value.DevelopmentInviteUrl);
        Assert.NotNull(fixture.Email.Invite);
        Assert.Contains("/set-password?token=", fixture.Email.Invite!.InviteUrl);

        using var db = fixture.Database.CreateContext(fixture.TenantContext);
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == fixture.EmailAddress);
        Assert.False(user.IsActive);
        Assert.NotEqual(fixture.Email.Invite.InviteUrl.Split("token=", 2)[1], user.PasswordHash);
        Assert.Single(await db.UserRoles.IgnoreQueryFilters().Where(x => x.UserId == user.Id).ToListAsync());
        Assert.Single(await db.AccountEmployeeCurrentLinks.IgnoreQueryFilters().Where(x => x.UserId == user.Id && x.EmployeeId == fixture.EmployeeId).ToListAsync());
        var invitation = await db.UserInvitations.IgnoreQueryFilters().SingleAsync();
        Assert.NotEqual(fixture.Email.Invite.InviteUrl.Split("token=", 2)[1], invitation.TokenHash);
    }

    [Fact]
    public async Task Set_password_activates_account_and_makes_invitation_single_use()
    {
        using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(fixture.EmployeeId, new());
        var token = Uri.UnescapeDataString(fixture.Email.Invite!.InviteUrl.Split("token=", 2)[1]);

        var set = await fixture.Service.SetPasswordAsync(new SetPasswordRequest
        {
            Token = token,
            Password = "Employee-password-1",
            ConfirmPassword = "Employee-password-1"
        });
        var secondUse = await fixture.Service.SetPasswordAsync(new SetPasswordRequest
        {
            Token = token,
            Password = "Employee-password-2",
            ConfirmPassword = "Employee-password-2"
        });

        Assert.True(created.Succeeded);
        Assert.True(set.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, secondUse.Status);
        using var db = fixture.Database.CreateContext(fixture.TenantContext);
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == fixture.EmailAddress);
        var invitation = await db.UserInvitations.IgnoreQueryFilters().SingleAsync();
        Assert.True(user.IsActive);
        Assert.True(fixture.Hasher.Verify(user.PasswordHash, "Employee-password-1"));
        Assert.NotNull(invitation.UsedAtUtc);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(SqliteInMemoryDatabase database, HrmsDbContext context, TestTenantContext tenantContext, EmployeePortalAccountService service, Guid employeeId, string email, IdentityPasswordHasher hasher, RecordingEmailSender emailSender)
        {
            Database = database;
            Context = context;
            TenantContext = tenantContext;
            Service = service;
            EmployeeId = employeeId;
            EmailAddress = email;
            Hasher = hasher;
            Email = emailSender;
        }

        public SqliteInMemoryDatabase Database { get; }
        public HrmsDbContext Context { get; }
        public TestTenantContext TenantContext { get; }
        public EmployeePortalAccountService Service { get; }
        public Guid EmployeeId { get; }
        public string EmailAddress { get; }
        public IdentityPasswordHasher Hasher { get; }
        public RecordingEmailSender Email { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var database = new SqliteInMemoryDatabase();
            var tenantId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var tenant = new Tenant { Id = tenantId, TenantCode = "INVITE01", TenantName = "Invite Test", Host = "invite01.localhost", ShardKey = "invite01", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql };
            var tenantContext = new TestTenantContext(tenantId, actorId);
            var db = database.CreateContext(tenantContext);
            var hasher = new IdentityPasswordHasher();
            await DatabaseSeeder.SeedShardAsync(db, hasher, tenant, CancellationToken.None);
            db.Users.Add(new User { Id = actorId, TenantId = tenantId, Email = "hr@example.test", FirstName = "HR", LastName = "User", IsActive = true, PasswordHash = hasher.Hash("Actor-password-1") });
            var employee = new Employee { Id = Guid.NewGuid(), TenantId = tenantId, FirstName = "New", LastName = "Employee", Email = "new.employee@example.test", DateOfJoining = new DateOnly(2026, 1, 1) };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            var shard = new ShardContext();
            shard.Use(new ShardDescriptor(tenantId, tenant.TenantCode, tenant.Host, tenant.ShardKey, tenant.Status, tenant.DatabaseProvider));
            var email = new RecordingEmailSender();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Invitations:InitialPasswordExpiryHours"] = "24", ["Frontend:TenantWebUrlTemplate"] = "http://{host}:5173" }).Build();
            var service = new EmployeePortalAccountService(db, tenantContext, shard, hasher, email, TimeProvider.System, configuration, new TestEnvironment(), NullLogger<EmployeePortalAccountService>.Instance);
            return new Fixture(database, db, tenantContext, service, employee.Id, employee.Email, hasher, email);
        }

        public void Dispose() { Context.Dispose(); Database.Dispose(); }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public Task SendLeaveNotificationAsync(LeaveNotificationEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> SendPasswordResetOtpAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default) => Task.FromResult<string?>(message.Otp);
        public WelcomeEmailMessage? Invite { get; private set; }
        public Task SendWelcomeInviteAsync(WelcomeEmailMessage message, CancellationToken cancellationToken = default) { Invite = message; return Task.CompletedTask; }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
