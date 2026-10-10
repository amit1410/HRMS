using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

public sealed class EmployeeEmploymentControllerAuditTests : IClassFixture<HrmsApiFactory>
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly Guid Employee = OrganizationTestHarness.EmployeeId(Tenant, "EMP-001");
    private static readonly Guid Department = OrganizationTestHarness.DepartmentId(Tenant, "ENG");
    private static readonly Guid Designation = OrganizationTestHarness.DesignationId(Tenant, "SE");
    private static readonly Guid Grade = OrganizationTestHarness.GradeId(Tenant, "G1");
    private static readonly Guid WorkLocation = OrganizationTestHarness.WorkLocationId(Tenant, "WL-MUM");
    private static readonly Guid Country = OrganizationTestHarness.CountryId("IN");
    private static readonly Guid Reason = OrganizationTestHarness.PositionChangeReasonId(Tenant, "NEW_HIRE");
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly HrmsApiFactory _factory;

    public EmployeeEmploymentControllerAuditTests(HrmsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Employment_controller_does_not_duplicate_the_job_status_audit()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        var token = TestTokens.Create(
            SeedData.Users[0].Id, Tenant, "DEMO01", "admin@demo01.com",
            roles: [RoleNames.TenantAdmin], permissions: Permissions.All);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var history = await client.PostAsJsonAsync(
            $"/api/employees/{Employee}/employment-history", new EmploymentChangeRequest
            {
                EffectiveFrom = Today,
                DepartmentId = Department,
                DesignationId = Designation,
                GradeId = Grade,
                WorkLocationId = WorkLocation,
                CountryLocationId = Country,
                PositionChangeReasonId = Reason,
                ChangeReason = EmploymentChangeReason.NewJoining,
                EmploymentType = EmploymentType.FullTime,
                EmploymentStatus = EmployeeStatus.Active
            });
        Assert.Equal(HttpStatusCode.Created, history.StatusCode);

        var update = await client.PutAsJsonAsync(
            $"/api/employees/{Employee}/employment", new EmployeeEmploymentRequest { JobStatus = "Confirmed" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var jobStatusAudits = 0;
        await _factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo01Host, async services =>
        {
            var db = services.GetRequiredService<IHrmsDbContext>();
            jobStatusAudits = await db.EmployeeAuditLogs.CountAsync(
                audit => audit.EmployeeId == Employee && audit.FieldName == "JobStatus");
        });

        Assert.Equal(1, jobStatusAudits);
    }
}
