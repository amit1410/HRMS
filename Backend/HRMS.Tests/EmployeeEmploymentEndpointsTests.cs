using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.DTOs.Masters;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// The employment-history surface over the real HTTP pipeline: the position-change-reason master dropdown
/// (which must offer the spec's reasons), the org hierarchy dropdowns, and the add / history / current
/// endpoints with their result-to-status mapping and permission gates. Together with
/// <see cref="EmployeeEmploymentEndToEndTests"/> (service + raw database) this proves the flow the UI drives.
/// </summary>
public class EmployeeEmploymentEndpointsTests : IClassFixture<HrmsApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo01AdminId = SeedData.Users[0].Id;

    // Each test targets its own employee so parallel tests on the shared host database cannot interfere.
    private static readonly Guid Employee1 = OrganizationTestHarness.EmployeeId(Demo01, "EMP-001");
    private static readonly Guid Employee2 = OrganizationTestHarness.EmployeeId(Demo01, "EMP-002");
    private static readonly Guid Employee3 = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");
    private static readonly Guid Employee4 = OrganizationTestHarness.EmployeeId(Demo01, "EMP-004");

    // Masters
    private static readonly Guid DeptEng = OrganizationTestHarness.DepartmentId(Demo01, "ENG");
    private static readonly Guid DesigSe = OrganizationTestHarness.DesignationId(Demo01, "SE");
    private static readonly Guid GradeG1 = OrganizationTestHarness.GradeId(Demo01, "G1");
    private static readonly Guid WorkLocMum = OrganizationTestHarness.WorkLocationId(Demo01, "WL-MUM");
    private static readonly Guid CountryIn = OrganizationTestHarness.CountryId("IN");
    private static readonly Guid ReasonNewHire = OrganizationTestHarness.PositionChangeReasonId(Demo01, "NEW_HIRE");
    private static readonly Guid ReasonPromo = OrganizationTestHarness.PositionChangeReasonId(Demo01, "PROMO");

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly HrmsApiFactory _factory;

    public EmployeeEmploymentEndpointsTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Position_change_reason_master_offers_new_hire_and_retirement()
    {
        using var client = Demo01Client(Permissions.EmploymentHistory.View);

        var response = await client.GetAsync("/api/master-data/position-change-reasons");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadAsync<IReadOnlyList<MasterLookupDto>>(response);
        Assert.True(body.Success, body.Message);

        var names = body.Data!.Select(r => r.Name).ToList();
        Assert.Contains("New Hire", names);
        Assert.Contains("Retirement", names);
        Assert.Contains("Promotion", names);
        Assert.Contains("Demotion", names);
        Assert.Contains("Correction of Employment", names);
        Assert.Contains("Transfer", names);
        Assert.Contains("Location Change", names);
        Assert.Contains("Department Change", names);
        Assert.Contains("Designation Change", names);
        Assert.Contains("Grade Change", names);
    }

    [Fact]
    public async Task Position_change_reason_master_read_without_permission_is_forbidden()
    {
        using var client = Demo01Client(Permissions.Department.View);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/master-data/position-change-reasons")).StatusCode);
    }

    [Fact]
    public async Task Org_hierarchy_masters_are_available_for_the_dependent_dropdowns()
    {
        using var client = Demo01Client(Permissions.Department.View);

        var holding = await ReadAsync<IReadOnlyList<MasterLookupDto>>(await client.GetAsync("api/master-data/holding-companies?ActiveOnly=true"));
        Assert.Contains("Acme Global Holdings", holding.Data!.Select(h => h.Name));

        var lob = await ReadAsync<IReadOnlyList<MasterLookupDto>>(await client.GetAsync("api/master-data/lines-of-business?ActiveOnly=true&ParentId=" + OrganizationTestHarness.HoldingCompanyId(Demo01, "HC01")));
        Assert.Contains("IT Services", lob.Data!.Select(l => l.Name));

        var orgs = await ReadAsync<IReadOnlyList<MasterLookupDto>>(await client.GetAsync("api/master-data/organisations?ActiveOnly=true"));
        Assert.Contains("Acme Technologies Pvt Ltd", orgs.Data!.Select(o => o.Name));

        var grades = await ReadAsync<IReadOnlyList<MasterLookupDto>>(await client.GetAsync("api/master-data/grades?ActiveOnly=true"));
        Assert.Contains("Grade 1", grades.Data!.Select(g => g.Name));

        var workLocations = await ReadAsync<IReadOnlyList<MasterLookupDto>>(await client.GetAsync("api/master-data/work-locations?ActiveOnly=true"));
        Assert.Contains("Mumbai Office", workLocations.Data!.Select(w => w.Name));

        var employeeTypes = await ReadAsync<IReadOnlyList<MasterLookupDto>>(await client.GetAsync("api/master-data/employee-types?ActiveOnly=true"));
        Assert.Contains("Full Time", employeeTypes.Data!.Select(e => e.Name));

        var costCenters = await ReadAsync<IReadOnlyList<MasterLookupDto>>(await client.GetAsync("api/master-data/cost-centers?ActiveOnly=true"));
        Assert.Contains("Engineering", costCenters.Data!.Select(c => c.Name));
    }

    [Fact]
    public async Task Employment_endpoints_require_a_token()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/employees/{Employee1}/employment-history")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"/api/employees/{Employee1}/employment-history", Request(ReasonNewHire))).StatusCode);
    }

    [Fact]
    public async Task Adding_employment_requires_the_change_permission()
    {
        using var viewOnlyClient = Demo01Client(Permissions.EmploymentHistory.View);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewOnlyClient.PostAsJsonAsync(
                $"/api/employees/{Employee1}/employment-history", Request(ReasonNewHire))).StatusCode);
    }

    [Fact]
    public async Task Full_flow_add_history_and_current_over_http()
    {
        using var client = Demo01Client();

        // Add returns 201 with the denormalized master names populated.
        var create = await client.PostAsJsonAsync(
            $"/api/employees/{Employee1}/employment-history", Request(ReasonNewHire));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var createdBody = await ReadAsync<EmployeeEmploymentHistoryDto>(create);
        Assert.True(createdBody.Success, createdBody.Message);
        var created = createdBody.Data!;
        Assert.Equal(DeptEng, created.DepartmentId);
        Assert.Equal("Engineering", created.DepartmentName);
        Assert.Equal(DesigSe, created.DesignationId);
        Assert.Equal("Software Engineer", created.DesignationName);
        Assert.Equal("New Hire", created.PositionChangeReasonName);
        Assert.Null(created.EffectiveTo);

        // History lists it.
        var history = await ReadAsync<IReadOnlyList<EmployeeEmploymentHistoryDto>>(
            await client.GetAsync($"/api/employees/{Employee1}/employment-history"));
        Assert.True(history.Success);
        var single = Assert.Single(history.Data!);
        Assert.Equal(created.Id, single.Id);

        // Current returns the same open record.
        var current = await ReadAsync<EmployeeEmploymentHistoryDto>(
            await client.GetAsync($"/api/employees/{Employee1}/employment-history/current"));
        Assert.True(current.Success);
        Assert.Equal(created.Id, current.Data!.Id);
        Assert.Equal("Engineering", current.Data.DepartmentName);
    }

    [Fact]
    public async Task A_later_change_closes_the_current_and_opens_a_new_one()
    {
        using var client = Demo01Client();

        var hire = await ReadAsync<EmployeeEmploymentHistoryDto>(await client.PostAsJsonAsync(
            $"/api/employees/{Employee2}/employment-history", Request(ReasonNewHire)));
        Assert.True(hire.Success, hire.Message);

        var promoRequest = Request(ReasonPromo);
        promoRequest.EffectiveFrom = Today.AddDays(30);
        promoRequest.DesignationId = OrganizationTestHarness.DesignationId(Demo01, "SSE");
        promoRequest.GradeId = OrganizationTestHarness.GradeId(Demo01, "G2");

        var promo = await client.PostAsJsonAsync(
            $"/api/employees/{Employee2}/employment-history", promoRequest);
        Assert.Equal(HttpStatusCode.Created, promo.StatusCode);
        var promoBody = await ReadAsync<EmployeeEmploymentHistoryDto>(promo);
        Assert.True(promoBody.Success, promoBody.Message);

        // History is now two records, most recent first.
        var history = await ReadAsync<IReadOnlyList<EmployeeEmploymentHistoryDto>>(
            await client.GetAsync($"/api/employees/{Employee2}/employment-history"));
        Assert.Equal(2, history.Data!.Count);
        Assert.Equal(promoBody.Data!.Id, history.Data[0].Id);
        Assert.Equal(hire.Data!.Id, history.Data[1].Id);
    }

    [Fact]
    public async Task An_overlapping_effective_date_is_rejected_over_http()
    {
        using var client = Demo01Client();

        var first = await client.PostAsJsonAsync(
            $"/api/employees/{Employee3}/employment-history", Request(ReasonNewHire));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // A second transaction effective the same day overlaps and is rejected (400, not saved).
        var duplicate = await client.PostAsJsonAsync(
            $"/api/employees/{Employee3}/employment-history", Request(ReasonPromo));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var body = await ReadAsync<IReadOnlyList<EmployeeEmploymentHistoryDto>>(
            await client.GetAsync($"/api/employees/{Employee3}/employment-history"));
        Assert.Single(body.Data!);
    }

    [Fact]
    public async Task A_past_effective_date_is_rejected_over_http()
    {
        using var client = Demo01Client();

        var request = Request(ReasonNewHire);
        request.EffectiveFrom = Today.AddDays(-1);
        var response = await client.PostAsJsonAsync($"/api/employees/{Employee4}/employment-history", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static EmploymentChangeRequest Request(Guid reasonId) => new()
    {
        EffectiveFrom = Today,
        DepartmentId = DeptEng,
        DesignationId = DesigSe,
        GradeId = GradeG1,
        WorkLocationId = WorkLocMum,
        CountryLocationId = CountryIn,
        PositionChangeReasonId = reasonId,
        ChangeReason = EmploymentChangeReason.NewJoining,
        EmploymentType = EmploymentType.FullTime,
        EmploymentStatus = EmployeeStatus.Active
    };

    private HttpClient Demo01Client(params string[] permissions)
    {
        var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        var token = TestTokens.Create(
            Demo01AdminId,
            Demo01,
            "DEMO01",
            "admin@demo01.com",
            roles: [RoleNames.TenantAdmin],
            permissions: permissions.Length > 0 ? permissions : Permissions.All);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<ApiResponse<T>> ReadAsync<T>(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<ApiResponse<T>>(payload, Json);
        Assert.NotNull(body);
        return body!;
    }
}
