using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public class EmployeeSensitiveDataEndpointsTests : IClassFixture<HrmsApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo01AdminId = SeedData.Users[0].Id;
    private readonly HrmsApiFactory _factory;

    public EmployeeSensitiveDataEndpointsTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task General_reads_are_masked_and_raw_statutory_values_require_narrow_permissions()
    {
        var request = Request();

        using var createWithoutSensitivePermission = Client(Permissions.Employee.Create);
        var createWithoutSensitive = await createWithoutSensitivePermission.PostAsJsonAsync(
            "/api/employees/personal-details", request);
        Assert.Equal(HttpStatusCode.Created, createWithoutSensitive.StatusCode);
        var withoutSensitiveBody = await ReadAsync<EmployeeDto>(createWithoutSensitive);
        Assert.Null(withoutSensitiveBody.Data!.MaskedAadhaarNumber);
        Assert.Null(withoutSensitiveBody.Data.MaskedPanNumber);
        var withoutSensitiveJson = await createWithoutSensitive.Content.ReadAsStringAsync();
        Assert.DoesNotContain(request.AadhaarNumber!, withoutSensitiveJson, StringComparison.Ordinal);
        Assert.DoesNotContain(request.PanNumber!, withoutSensitiveJson, StringComparison.Ordinal);

        using var writer = Client(Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit);
        var create = await writer.PostAsJsonAsync("/api/employees/personal-details", request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var createdBody = await ReadAsync<EmployeeDto>(create);
        var created = createdBody.Data!;
        Assert.Equal("XXXX-XXXX-3333", created.MaskedAadhaarNumber);
        Assert.Equal("A****F", created.MaskedPanNumber);
        Assert.Equal("******0001", created.MaskedPfNumber);
        Assert.Equal("******7777", created.MaskedUanNumber);
        Assert.Equal("******0123", created.MaskedEsicNumber);
        Assert.Equal("******0456", created.MaskedMediclaimNumber);

        var generalJson = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain(request.AadhaarNumber!, generalJson, StringComparison.Ordinal);
        Assert.DoesNotContain(request.PanNumber!, generalJson, StringComparison.Ordinal);

        using var viewer = Client(Permissions.Employee.View);
        var generalRead = await viewer.GetAsync($"/api/employees/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, generalRead.StatusCode);
        Assert.DoesNotContain(request.AadhaarNumber!, await generalRead.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync($"/api/employees/{created.Id}/sensitive-details")).StatusCode);

        using var sensitiveViewer = Client(Permissions.Employee.View, Permissions.EmployeeSensitive.View);
        var sensitiveRead = await sensitiveViewer.GetAsync($"/api/employees/{created.Id}/sensitive-details");
        Assert.Equal(HttpStatusCode.OK, sensitiveRead.StatusCode);
        var sensitive = await ReadAsync<EmployeeSensitiveDetailsDto>(sensitiveRead);
        Assert.Equal(request.AadhaarNumber, sensitive.Data!.AadhaarNumber);
        Assert.Equal(request.PanNumber, sensitive.Data.PanNumber);
        Assert.Equal(request.EsicNumber, sensitive.Data.EsicNumber);

        var changed = Request();
        changed.AadhaarNumber = "444455556666";
        changed.PanNumber = "ZYXWV0987Q";
        using var editor = Client(Permissions.Employee.Edit, Permissions.EmployeeSensitive.Edit);
        var update = await editor.PutAsJsonAsync($"/api/employees/{created.Id}/personal-details", changed);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadAsync<EmployeeDto>(update);
        Assert.Equal("XXXX-XXXX-6666", updated.Data!.MaskedAadhaarNumber);
        Assert.Equal("Z****Q", updated.Data.MaskedPanNumber);
    }

    private static EmployeePersonalDetailsRequest Request() => new()
    {
        FirstName = "Sensitive",
        LastName = "Employee",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = Gender.Other,
        MaritalStatus = MaritalStatus.Single,
        DateOfJoining = new DateOnly(2025, 1, 1),
        AadhaarNumber = "111122223333",
        PanNumber = "ABCDE1234F",
        PfNumber = "PF-TEST-0001",
        UanNumber = "999988887777",
        EsicApplicable = true,
        EsicNumber = "ESIC-000123",
        MediclaimNumber = "MED-000456"
    };

    private HttpClient Client(params string[] permissions)
    {
        var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestTokens.Create(
                Demo01AdminId,
                Demo01,
                "DEMO01",
                "admin@demo01.com",
                roles: [RoleNames.TenantAdmin],
                permissions: permissions));
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
