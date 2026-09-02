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
/// The bank-details surface over the real HTTP pipeline: the tenant-resolved bank master dropdown and the
/// add / edit / update / soft-delete endpoints, each with the result-to-status mapping and response envelope
/// the API actually returns. Together with <see cref="EmployeeBankDetailsEndToEndTests"/> (service + raw
/// database) this proves the flow the UI drives.
/// </summary>
public class EmployeeBankDetailsEndpointsTests : IClassFixture<HrmsApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo01AdminId = SeedData.Users[0].Id;
    // Each test that creates a bank record targets its own employee, so the one-active-per-purpose rule
    // cannot make two tests that run in parallel on the shared host database interfere.
    private static readonly Guid EmployeeId = OrganizationTestHarness.EmployeeId(Demo01, "EMP-001");
    private static readonly Guid EmployeeId2 = OrganizationTestHarness.EmployeeId(Demo01, "EMP-002");
    private static readonly Guid EmployeeId3 = OrganizationTestHarness.EmployeeId(Demo01, "EMP-003");
    private static readonly Guid SbiId = OrganizationTestHarness.BankId(Demo01, "SBI");
    private static readonly Guid HdfcId = OrganizationTestHarness.BankId(Demo01, "HDFC");
    private static readonly Guid AxisId = OrganizationTestHarness.BankId(Demo01, "AXIS"); // seeded inactive

    private readonly HrmsApiFactory _factory;

    public EmployeeBankDetailsEndpointsTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Bank_master_dropdown_returns_active_banks_and_excludes_inactive()
    {
        using var client = Demo01Client(Permissions.Department.View);

        var response = await client.GetAsync("/api/master-data/banks?ActiveOnly=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadAsync<IReadOnlyList<MasterLookupDto>>(response);
        Assert.True(body.Success, body.Message);

        var names = body.Data!.Select(b => b.Name).ToList();
        Assert.Contains("State Bank of India", names);
        Assert.Contains("HDFC Bank", names);
        Assert.Contains("ICICI Bank", names);
        // The seeded-inactive bank must not appear in an active-only dropdown.
        Assert.DoesNotContain("Axis Bank", names);
        Assert.DoesNotContain(AxisId, body.Data!.Select(b => b.Id));
    }

    [Fact]
    public async Task Bank_master_read_without_permission_is_forbidden()
    {
        using var client = Demo01Client(Permissions.Employee.View);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/master-data/banks")).StatusCode);
    }

    [Fact]
    public async Task Bank_details_are_unauthorized_without_a_token()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/employees/{EmployeeId}/bank-details")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"/api/employees/{EmployeeId}/bank-details", Request(HdfcId, "No Token"))).StatusCode);
    }

    [Fact]
    public async Task Add_then_update_then_soft_delete_over_http()
    {
        using var client = Demo01Client();

        // Add returns the created record with the denormalized bank name and an active flag.
        var create = await client.PostAsJsonAsync(
            $"/api/employees/{EmployeeId}/bank-details", Request(HdfcId, "HTTP Salary"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var createdBody = await ReadAsync<EmployeeBankDetailDto>(create);
        Assert.True(createdBody.Success, createdBody.Message);
        var created = createdBody.Data!;
        Assert.Equal(HdfcId, created.BankId);
        Assert.Equal("HDFC Bank", created.BankName);
        Assert.Equal("********-999", created.MaskedAccountNumber);
        Assert.Equal("HDFC*****99", created.MaskedIfscCode);
        Assert.DoesNotContain("ACC-999", await create.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(created.IsActive);

        // The general read is masked even for an administrator. Raw values require the narrower endpoint.
        var editableResponse = await client.GetAsync(
            $"/api/employees/{EmployeeId}/bank-details/{created.Id}/sensitive-details");
        Assert.Equal(HttpStatusCode.OK, editableResponse.StatusCode);
        var editable = await ReadAsync<EmployeeBankDetailEditDto>(editableResponse);
        Assert.Equal("ACC-999", editable.Data!.AccountNumber);
        Assert.Equal("HDFC0000999", editable.Data.IfscCode);

        using var viewOnly = Demo01Client(Permissions.Employee.View);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewOnly.GetAsync(
                $"/api/employees/{EmployeeId}/bank-details/{created.Id}/sensitive-details")).StatusCode);

        // Reload lists it, still active.
        var listed = await ReadAsync<IReadOnlyList<EmployeeBankDetailDto>>(
            await client.GetAsync($"/api/employees/{EmployeeId}/bank-details"));
        Assert.True(listed.Success);
        var reloaded = listed.Data!.Single(b => b.Id == created.Id);
        Assert.True(reloaded.IsActive);

        // Update changes the bank and the branch.
        var updatedRequest = Request(SbiId, "HTTP Salary Updated");
        updatedRequest.BranchName = "Main Branch";
        updatedRequest.EffectiveFrom = new DateOnly(2025, 5, 1);
        var update = await client.PutAsJsonAsync(
            $"/api/employees/{EmployeeId}/bank-details/{created.Id}", updatedRequest);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updatedBody = await ReadAsync<EmployeeBankDetailDto>(update);
        Assert.True(updatedBody.Success, updatedBody.Message);
        Assert.Equal(SbiId, updatedBody.Data!.BankId);
        Assert.Equal("State Bank of India", updatedBody.Data.BankName);
        Assert.Equal("Main Branch", updatedBody.Data.BranchName);
        Assert.Equal(new DateOnly(2025, 5, 1), updatedBody.Data.EffectiveFrom);

        // Delete soft-deletes: 200 back, and the record is now listed as inactive.
        var delete = await client.DeleteAsync($"/api/employees/{EmployeeId}/bank-details/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        var deleteBody = await ReadAsync<bool>(delete);
        Assert.True(deleteBody.Success, deleteBody.Message);

        var afterDeleteResponse = await client.GetAsync($"/api/employees/{EmployeeId}/bank-details");
        var afterDeletePayload = await afterDeleteResponse.Content.ReadAsStringAsync();
        var afterDelete = JsonSerializer.Deserialize<ApiResponse<IReadOnlyList<EmployeeBankDetailDto>>>(
            afterDeletePayload, Json)!;
        var flagged = afterDelete.Data!.Single(b => b.Id == created.Id);
        Assert.False(flagged.IsActive);
        Assert.Equal(BankAccountStatus.Closed, flagged.Status);
        Assert.DoesNotContain("ACC-999", afterDeletePayload, StringComparison.Ordinal);

        // Historical rows stay masked in general reads and cannot expose full values or be reactivated
        // through the edit/update endpoints. A replacement must be posted as a new row.
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.GetAsync(
                $"/api/employees/{EmployeeId}/bank-details/{created.Id}/sensitive-details")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync(
                $"/api/employees/{EmployeeId}/bank-details/{created.Id}", updatedRequest)).StatusCode);
    }

    [Fact]
    public async Task Adding_a_second_active_record_for_the_same_purpose_conflicts()
    {
        using var client = Demo01Client();

        var first = await client.PostAsJsonAsync(
            $"/api/employees/{EmployeeId2}/bank-details", Request(SbiId, "Primary Salary"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            $"/api/employees/{EmployeeId2}/bank-details", Request(HdfcId, "Duplicate Salary"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var body = await ReadAsync<EmployeeBankDetailDto>(second);
        Assert.False(body.Success);
    }

    [Fact]
    public async Task Adding_with_an_inactive_bank_is_rejected()
    {
        using var client = Demo01Client();

        var response = await client.PostAsJsonAsync(
            $"/api/employees/{EmployeeId3}/bank-details", Request(AxisId, "Inactive Bank"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadAsync<EmployeeBankDetailDto>(response);
        Assert.False(body.Success);
    }

    [Fact]
    public async Task Adding_a_historical_status_is_rejected()
    {
        using var client = Demo01Client();
        var request = Request(SbiId, "Closed On Arrival");
        request.Status = BankAccountStatus.Closed;

        var response = await client.PostAsJsonAsync(
            $"/api/employees/{EmployeeId3}/bank-details", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadAsync<EmployeeBankDetailDto>(response);
        Assert.False(body.Success);
        Assert.Equal("status", Assert.Single(body.Errors!).Field);
    }

    [Fact]
    public async Task Bank_writes_require_sensitive_edit_permission()
    {
        using var client = Demo01Client(Permissions.Employee.Edit);

        var response = await client.PostAsJsonAsync(
            $"/api/employees/{EmployeeId3}/bank-details", Request(SbiId, "No Sensitive Permission"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static EmployeeBankDetailRequest Request(Guid bankId, string holder) => new()
    {
        BankId = bankId,
        AccountHolderName = holder,
        AccountNumber = "ACC-999",
        AccountType = AccountType.Savings,
        AccountPurpose = AccountPurpose.Salary,
        IfscCode = "HDFC0000999",
        BranchName = "Branch"
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
