using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Departments;
using HRMS.Application.DTOs.Designations;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// The organization endpoints over the real HTTP pipeline: routing, the authorization policies, the
/// validation filter, the result-to-status mapping and the response envelope all participate. The service
/// tests already cover the rules; what can only be checked here is that each endpoint is behind the right
/// permission, that the tenant acted on comes from the token, and that a refusal arrives as the right status
/// code rather than a 200 with an error inside.
/// </summary>
public class OrganizationEndpointsTests : IClassFixture<HrmsApiFactory>
{
    /// <summary>
    /// Configured to match the API's own serializer: camelCase names and enums as strings. Reading the
    /// responses with the framework defaults instead would fail on every DTO carrying a status or a gender.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo02 = SeedData.TenantIds.Demo02;
    private static readonly Guid Demo01AdminId = SeedData.Users[0].Id;
    private static readonly Guid Demo02AdminId = SeedData.Users[2].Id;

    private readonly HrmsApiFactory _factory;

    public OrganizationEndpointsTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/employees")]
    [InlineData("/api/employees/export")]
    [InlineData("/api/departments")]
    [InlineData("/api/designations")]
    public async Task A_read_without_a_token_is_unauthorized(string url)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_write_without_a_token_is_unauthorized()
    {
        using var client = _factory.CreateClient();
        var id = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/employees", NewEmployee("EMP-401"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync($"/api/employees/{id}", NewEmployee("EMP-401"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.DeleteAsync($"/api/employees/{id}")).StatusCode);
    }

    /// <summary>
    /// Each verb sits behind its own permission, so read access never implies write access. The token here is
    /// valid and names a real administrator — only the permission list is narrowed.
    /// </summary>
    [Fact]
    public async Task Each_employee_endpoint_requires_its_own_permission()
    {
        using var viewer = Demo01Client(Permissions.Employee.View);
        var id = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/employees/{id}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync("/api/employees", NewEmployee("EMP-402"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.PutAsJsonAsync($"/api/employees/{id}", NewEmployee("EMP-402"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/employees/{id}")).StatusCode);

        // Export is separate from view: a bulk download of every personal record is its own decision.
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/employees/export")).StatusCode);
    }

    [Fact]
    public async Task Permission_on_one_resource_does_not_carry_to_another()
    {
        using var employeeViewer = Demo01Client(Permissions.Employee.View);
        using var departmentViewer = Demo01Client(Permissions.Department.View);

        Assert.Equal(HttpStatusCode.Forbidden, (await employeeViewer.GetAsync("/api/departments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeViewer.GetAsync("/api/designations")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await departmentViewer.GetAsync("/api/departments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await departmentViewer.GetAsync("/api/employees")).StatusCode);
    }

    [Fact]
    public async Task Creating_an_employee_returns_201_and_a_location_that_resolves()
    {
        using var client = Demo01Client();

        var response = await client.PostAsJsonAsync("/api/employees", NewEmployee("EMP-410"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = (await ReadAsync<EmployeeDto>(response)).Data!;
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("EMP-410", created.EmployeeCode);

        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.EndsWith($"/api/employees/{created.Id}", location!.ToString());

        var followed = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
        Assert.Equal(created.Id, (await ReadAsync<EmployeeDto>(followed)).Data!.Id);
    }

    /// <summary>
    /// The tenant an endpoint writes to comes from the verified token, and nothing in the payload can name
    /// one. A record created by DEMO02 is therefore invisible to DEMO01 even though both requests hit the
    /// same route on the same host against the same table.
    /// </summary>
    [Fact]
    public async Task A_record_created_by_one_tenant_is_invisible_to_the_other()
    {
        using var demo02 = Demo02Client();
        using var demo01 = Demo01Client();

        var body = new EmployeeRequest
        {
            EmployeeCode = "E-420",
            FirstName = "Ada",
            LastName = "Nwosu",
            Email = "ada.nwosu@demo02.com",
            Gender = Gender.Female,
            DateOfJoining = new DateOnly(2023, 8, 1),
            Status = EmployeeStatus.Active,
            DepartmentId = OrganizationTestHarness.DepartmentId(Demo02, "OPS"),
            DesignationId = OrganizationTestHarness.DesignationId(Demo02, "OPSM")
        };

        var created = await demo02.PostAsJsonAsync("/api/employees", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdId = (await ReadAsync<EmployeeDto>(created)).Data!.Id;

        var mine = await ReadAsync<PagedResult<EmployeeListItemDto>>(
            await demo02.GetAsync("/api/employees?search=E-420"));
        Assert.Single(mine.Data!.Items);

        var theirs = await ReadAsync<PagedResult<EmployeeListItemDto>>(
            await demo01.GetAsync("/api/employees?search=E-420"));
        Assert.Empty(theirs.Data!.Items);

        // Nor by direct id, which is the case a filter on the list query alone would miss.
        Assert.Equal(HttpStatusCode.NotFound, (await demo01.GetAsync($"/api/employees/{createdId}")).StatusCode);
    }

    /// <summary>
    /// A cross-tenant id is "not found", not "forbidden": a 403 would confirm the record exists somewhere,
    /// which is itself a disclosure. The same must hold for writes — a PUT or DELETE cannot reach across.
    /// </summary>
    [Fact]
    public async Task A_cross_tenant_id_is_not_found_on_every_verb()
    {
        using var client = Demo01Client();
        var theirEmployee = OrganizationTestHarness.EmployeeId(Demo02, "E-101");
        var theirDepartment = OrganizationTestHarness.DepartmentId(Demo02, "SLS");
        var theirDesignation = OrganizationTestHarness.DesignationId(Demo02, "SR");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/employees/{theirEmployee}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/employees/{theirEmployee}", NewEmployee("EMP-430"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/employees/{theirEmployee}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/departments/{theirDepartment}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/departments/{theirDepartment}",
                new DepartmentRequest { Code = "SLS", Name = "Taken Over", IsActive = true })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/designations/{theirDesignation}")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_id_is_not_found_and_a_malformed_one_never_reaches_the_action()
    {
        using var client = Demo01Client();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/employees/{Guid.NewGuid()}")).StatusCode);

        // The route constrains the id to a Guid, so this matches no route at all.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/employees/not-a-guid")).StatusCode);
    }

    [Fact]
    public async Task An_unsupported_sort_field_is_rejected_and_the_response_names_the_field()
    {
        using var client = Demo01Client();

        var response = await client.GetAsync("/api/employees?sortBy=salary");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadAsync<PagedResult<EmployeeListItemDto>>(response);
        Assert.False(body.Success);
        Assert.Null(body.Data);
        var error = Assert.Single(body.Errors!);
        Assert.Equal("sortBy", error.Field);
        Assert.Contains("employeeCode", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_out_of_range_page_size_is_rejected_by_the_query_validator()
    {
        using var client = Demo01Client();

        var response = await client.GetAsync("/api/employees?pageSize=5000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("pageSize", Assert.Single((await ReadAsync<object>(response)).Errors!).Field);
    }

    [Fact]
    public async Task An_invalid_payload_is_rejected_with_a_field_error_for_each_problem()
    {
        using var client = Demo01Client();

        var response = await client.PostAsJsonAsync("/api/employees", new EmployeeRequest
        {
            EmployeeCode = "",
            FirstName = "",
            LastName = "Nameless",
            Email = "not-an-email",
            DateOfJoining = default,
            Status = EmployeeStatus.Active,
            DepartmentId = Guid.Empty,
            DesignationId = Guid.Empty
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var fields = (await ReadAsync<EmployeeDto>(response)).Errors!.Select(e => e.Field).ToList();
        Assert.Contains("employeeCode", fields);
        Assert.Contains("firstName", fields);
        Assert.Contains("email", fields);
        Assert.Contains("dateOfJoining", fields);
        Assert.Contains("departmentId", fields);
        Assert.Contains("designationId", fields);
    }

    /// <summary>
    /// A rule the database cannot express — an id that exists but belongs to another organization — comes
    /// back as a 400 naming the field, not a 500 from a foreign key violation.
    /// </summary>
    [Fact]
    public async Task A_cross_tenant_department_in_the_payload_is_a_validation_failure()
    {
        using var client = Demo01Client();

        var body = NewEmployee("EMP-440");
        body.DepartmentId = OrganizationTestHarness.DepartmentId(Demo02, "OPS");

        var response = await client.PostAsJsonAsync("/api/employees", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single((await ReadAsync<EmployeeDto>(response)).Errors!);
        Assert.Equal("departmentId", error.Field);
    }

    [Fact]
    public async Task A_duplicate_code_is_a_conflict()
    {
        using var client = Demo01Client();

        var body = NewEmployee("EMP-001"); // already seeded in this tenant
        var response = await client.PostAsJsonAsync("/api/employees", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("employeeCode", Assert.Single((await ReadAsync<EmployeeDto>(response)).Errors!).Field);
    }

    [Fact]
    public async Task Deleting_a_department_that_still_has_employees_is_a_conflict()
    {
        using var client = Demo01Client();
        var engineering = OrganizationTestHarness.DepartmentId(Demo01, "ENG");

        var response = await client.DeleteAsync($"/api/departments/{engineering}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False((await ReadAsync<object>(response)).Success);

        // Still there afterwards.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/departments/{engineering}")).StatusCode);
    }

    [Fact]
    public async Task A_department_can_be_created_read_updated_and_deleted()
    {
        using var client = Demo01Client();

        var created = await client.PostAsJsonAsync("/api/departments", new DepartmentRequest
        {
            Code = "LAB",
            Name = "Laboratory",
            Description = "Research.",
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadAsync<DepartmentDto>(created)).Data!.Id;

        var updated = await client.PutAsJsonAsync($"/api/departments/{id}", new DepartmentRequest
        {
            Code = "LAB",
            Name = "Laboratory Services",
            IsActive = false
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var dto = (await ReadAsync<DepartmentDto>(updated)).Data!;
        Assert.Equal("Laboratory Services", dto.Name);
        Assert.False(dto.IsActive);
        Assert.NotNull(dto.ModifiedDate);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/departments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/departments/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_designation_can_be_created_and_listed()
    {
        using var client = Demo01Client();

        var created = await client.PostAsJsonAsync("/api/designations", new DesignationRequest
        {
            Code = "QA",
            Name = "Quality Analyst",
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var listed = await ReadAsync<PagedResult<DesignationDto>>(
            await client.GetAsync("/api/designations?search=quality"));

        var item = Assert.Single(listed.Data!.Items);
        Assert.Equal("QA", item.Code);
        Assert.Equal(0, item.EmployeeCount);
    }

    [Fact]
    public async Task Export_is_served_as_a_csv_download()
    {
        using var client = Demo01Client();

        var response = await client.GetAsync("/api/employees/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);

        var fileName = response.Content.Headers.ContentDisposition!.FileNameStar
                       ?? response.Content.Headers.ContentDisposition.FileName!;
        Assert.Matches(new Regex(@"^""?employees-\d{8}-\d{6}\.csv""?$"), fileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));

        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("Employee Code,First Name,Last Name,Email,Phone", text);
        Assert.Contains("Nadia", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_only_contains_the_calling_tenants_employees()
    {
        using var client = Demo02Client();

        var response = await client.GetAsync("/api/employees/export");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Grace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Nadia", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@demo01.com", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refused export must not arrive as a file: the caller asked for CSV, but an error has to come back
    /// in the JSON envelope with a status code, or a browser would save a spreadsheet full of nothing.
    /// </summary>
    [Fact]
    public async Task A_refused_export_comes_back_as_json_not_as_a_file()
    {
        using var client = Demo01Client();

        var response = await client.GetAsync("/api/employees/export?status=99");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("status", Assert.Single((await ReadAsync<object>(response)).Errors!).Field);
    }

    [Fact]
    public async Task A_list_response_carries_the_paging_envelope()
    {
        using var client = Demo01Client();

        var body = await ReadAsync<PagedResult<EmployeeListItemDto>>(
            await client.GetAsync("/api/employees?page=1&pageSize=2&sortBy=employeeCode"));

        Assert.True(body.Success);
        Assert.Equal(1, body.Data!.Page);
        Assert.Equal(2, body.Data.PageSize);
        Assert.Equal(2, body.Data.Items.Count);
        Assert.True(body.Data.TotalCount >= 6);
        Assert.True(body.Data.HasNextPage);
        Assert.False(body.Data.HasPreviousPage);
    }

    private HttpClient Demo01Client(params string[] permissions) =>
        CreateClient(Demo01, Demo01AdminId, "DEMO01", "admin@demo01.com", permissions);

    private HttpClient Demo02Client(params string[] permissions) =>
        CreateClient(Demo02, Demo02AdminId, "DEMO02", "admin@demo02.com", permissions);

    /// <summary>
    /// A client carrying a validly signed token for the given tenant. With no permissions listed the token
    /// carries all of them, so a test that is not about authorization does not have to enumerate them.
    /// </summary>
    private HttpClient CreateClient(
        Guid tenantId, Guid userId, string tenantCode, string email, IReadOnlyList<string> permissions)
    {
        var client = _factory.CreateClient();
        var token = TestTokens.Create(
            userId,
            tenantId,
            tenantCode,
            email,
            roles: [RoleNames.TenantAdmin],
            permissions: permissions.Count > 0 ? permissions : Permissions.All);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A valid DEMO01 hire. Each test uses its own code, since the host's database is shared.</summary>
    private static EmployeeRequest NewEmployee(string employeeCode) => new()
    {
        EmployeeCode = employeeCode,
        FirstName = "Test",
        LastName = "Hire",
        Email = $"{employeeCode.ToLowerInvariant()}@demo01.com",
        Phone = "555-0400",
        DateOfBirth = new DateOnly(1992, 4, 4),
        Gender = Gender.Unspecified,
        DateOfJoining = new DateOnly(2023, 1, 9),
        Status = EmployeeStatus.Active,
        DepartmentId = OrganizationTestHarness.DepartmentId(Demo01, "ENG"),
        DesignationId = OrganizationTestHarness.DesignationId(Demo01, "SE"),
        Address = "4 Test Row"
    };

    private static async Task<ApiResponse<T>> ReadAsync<T>(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<ApiResponse<T>>(payload, Json);
        Assert.NotNull(body);
        return body!;
    }
}
