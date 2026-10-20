using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;
using System.Reflection;

namespace HRMS.Tests;

public sealed class WelcomeInvitationMySqlQueryTranslationTests
{
    private static readonly MethodInfo GuidLiteralGenerator = typeof(WelcomeInvitationService)
        .GetMethod("BuildMySqlGuidLiteralList", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void Mysql_guid_literal_generation_handles_empty_single_multiple_and_duplicate_values()
    {
        var first = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var second = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        Assert.Equal(string.Empty, BuildGuidLiterals([]));
        Assert.Equal("'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'", BuildGuidLiterals([first]));
        Assert.Equal("'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'", BuildGuidLiterals([first, second]));
        Assert.Equal("'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'", BuildGuidLiterals([first, first]));
        Assert.Equal(string.Empty, BuildGuidLiterals([Guid.Empty]));
    }

    [Fact]
    public void Mysql_guid_literal_generation_is_bounded_for_chunk_size_and_contains_only_guid_literals()
    {
        var ids = Enumerable.Range(1, 501).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();
        var chunks = ids.Chunk(500).ToArray();

        Assert.Equal([500, 1], chunks.Select(chunk => chunk.Length));
        Assert.Equal(500, BuildGuidLiterals(chunks[0]).Split(", ").Length);
        Assert.Equal(1, BuildGuidLiterals(chunks[1]).Split(", ").Length);
        Assert.DoesNotContain(";", BuildGuidLiterals(ids));
        Assert.DoesNotContain("--", BuildGuidLiterals(ids));
    }

    [Fact]
    public void Guid_lists_used_by_welcome_invitation_row_queries_translate_without_opening_a_connection()
    {
        var options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL("Server=127.0.0.1;Database=translation_only;User=test;Password=test")
            .Options;
        using var db = new HrmsDbContext(options, new TestTenantContext(Guid.NewGuid()));

        var employeeIds = new List<Guid> { Guid.NewGuid() };
        var userIds = new List<Guid> { Guid.NewGuid() };

        Assert.Contains("Employees", db.Employees
            .Where(employee => employeeIds.Contains(employee.Id)).ToQueryString());
        Assert.Contains("AccountEmployeeCurrentLinks", db.AccountEmployeeCurrentLinks
            .Where(link => employeeIds.Contains(link.EmployeeId)).ToQueryString());
        Assert.Contains("Users", db.Users
            .Where(user => userIds.Contains(user.Id)).ToQueryString());
        Assert.Contains("UserInvitations", db.UserInvitations
            .Where(invitation => userIds.Contains(invitation.UserId)).ToQueryString());
        Assert.Contains("WelcomeInvitationAttempts", db.WelcomeInvitationAttempts
            .Where(attempt => employeeIds.Contains(attempt.EmployeeId)).ToQueryString());

        // SQL-generation only: ToQueryString never opens the configured MySQL connection.
        // FromSqlRaw remains an EF query root, so global tenant filters are composed onto it.
        var tenantSql = db.Employees.FromSqlRaw<Employee>(
            "SELECT e.* FROM `Employees` AS e WHERE e.`Id` IN ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')")
            .ToQueryString();
        Assert.Contains("TenantId", tenantSql);
    }

    [Fact]
    public void Welcome_invitation_list_and_raw_enrichment_queries_compose_with_mysql_tenant_filters()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL("Server=127.0.0.1;Database=translation_only;User=test;Password=test")
            .Options;
        using var db = new HrmsDbContext(options, new TestTenantContext(tenantId));

        // ListAsync's organization filters use correlated Any() queries over this tenant-filtered set.
        var holdingCompanyId = Guid.NewGuid();
        var listSql = db.Employees.AsNoTracking()
            .Where(employee => employee.TenantId == tenantId && db.EmployeeEmploymentHistory.Any(history =>
                history.EmployeeId == employee.Id && history.EffectiveTo == null && history.HoldingCompanyId == holdingCompanyId))
            .OrderBy(employee => employee.EmployeeCode).ThenBy(employee => employee.Id)
            .Skip(0).Take(20).ToQueryString();

        Assert.Contains("EmployeeEmploymentHistory", listSql);
        Assert.Contains("TenantId", listSql);
        Assert.DoesNotContain(") AS h WHERE h.TenantId", listSql, StringComparison.OrdinalIgnoreCase);

        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var statements = new[]
        {
            db.Employees.FromSqlRaw<Employee>("SELECT e.* FROM `Employees` AS e WHERE e.`Id` IN ('" + employeeId.ToString("D") + "')").ToQueryString(),
            db.AccountEmployeeCurrentLinks.FromSqlRaw<AccountEmployeeCurrentLink>("SELECT l.* FROM `AccountEmployeeCurrentLinks` AS l WHERE l.`EmployeeId` IN ('" + employeeId.ToString("D") + "')").ToQueryString(),
            db.Users.FromSqlRaw<User>("SELECT u.* FROM `Users` AS u WHERE u.`Id` IN ('" + userId.ToString("D") + "')").ToQueryString(),
            db.UserInvitations.FromSqlRaw<UserInvitation>("SELECT i.* FROM `UserInvitations` AS i WHERE i.`UserId` IN ('" + userId.ToString("D") + "')").ToQueryString(),
            db.WelcomeInvitationAttempts.FromSqlRaw<WelcomeInvitationAttempt>("SELECT a.* FROM `WelcomeInvitationAttempts` AS a WHERE a.`EmployeeId` IN ('" + employeeId.ToString("D") + "')").ToQueryString()
        };

        foreach (var sql in statements)
        {
            Assert.Contains("TenantId", sql);
            Assert.DoesNotContain(";\r\n)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(" AS ", sql);
        }
    }

    private static string BuildGuidLiterals(IEnumerable<Guid> ids)
        => (string)GuidLiteralGenerator.Invoke(null, [ids])!;
}
