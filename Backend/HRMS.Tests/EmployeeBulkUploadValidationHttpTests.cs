using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

[Collection("Employment endpoints")]
public sealed class EmployeeBulkUploadValidationHttpTests(HrmsApiFactory factory) : IClassFixture<HrmsApiFactory>
{
    private const string Route = "/api/employees/bulk-upload/validate";
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;

    [Fact]
    public async Task Upload_enforces_auth_and_both_permissions_and_resolves_only_authenticated_tenant_masters()
    {
        var bytes = await BuildValidWorkbookAsync();
        using (var anonymous = factory.CreateClientFor(HrmsApiFactory.Demo01Host))
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(anonymous, bytes)).StatusCode);

        using (var noImport = AuthorizedClient(Permissions.EmployeeSensitive.Edit))
            Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(noImport, bytes)).StatusCode);
        using (var noSensitive = AuthorizedClient(Permissions.Employee.Import))
            Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(noSensitive, bytes)).StatusCode);

        int employeeCountBefore = 0;
        await factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo01Host, async services =>
        {
            var db = services.GetRequiredService<IHrmsDbContext>();
            employeeCountBefore = await db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == Demo01);
        });
        using (var authorized = AuthorizedClient(Permissions.Employee.Import, Permissions.EmployeeSensitive.Edit))
        using (var response = await PostAsync(authorized, bytes))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var preview = await response.Content.ReadFromJsonAsync<EmployeeBulkUploadPreviewDto>();
            Assert.NotNull(preview); Assert.Equal(1, preview!.ValidRows); Assert.Equal(0, preview.InvalidRows);
        }
        await factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo01Host, async services =>
        {
            var db = services.GetRequiredService<IHrmsDbContext>();
            Assert.Equal(employeeCountBefore, await db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == Demo01));
        });

        var foreignCode = "FOREIGN_" + Guid.NewGuid().ToString("N")[..10];
        await factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo02Host, async services =>
        {
            var db = services.GetRequiredService<IHrmsDbContext>();
            db.Departments.Add(new Department { Id = Guid.NewGuid(), TenantId = SeedData.TenantIds.Demo02, Code = foreignCode, Name = "Foreign only", IsActive = true });
            await db.SaveChangesAsync();
        });
        var foreignWorkbook = SetDepartment(bytes, foreignCode);
        using var tenantClient = AuthorizedClient(Permissions.Employee.Import, Permissions.EmployeeSensitive.Edit);
        using var foreignResponse = await PostAsync(tenantClient, foreignWorkbook);
        Assert.Equal(HttpStatusCode.OK, foreignResponse.StatusCode);
        var foreignPreview = await foreignResponse.Content.ReadFromJsonAsync<EmployeeBulkUploadPreviewDto>();
        Assert.NotNull(foreignPreview);
        Assert.Contains(foreignPreview!.Rows[0].Issues, x => x.ErrorCode == "master.invalid" && x.ColumnName == "Department Code *");
    }

    [Fact]
    public async Task Import_preparation_requires_import_create_and_sensitive_edit_permissions_and_does_not_create_employees()
    {
        var bytes = await BuildValidWorkbookAsync();
        int before = 0;
        await factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo01Host, async services =>
        {
            before = await services.GetRequiredService<IHrmsDbContext>().Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == Demo01);
        });

        using (var anonymous = factory.CreateClientFor(HrmsApiFactory.Demo01Host))
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostPrepareAsync(anonymous, bytes)).StatusCode);
        using (var noCreate = AuthorizedClient(Permissions.Employee.Import, Permissions.EmployeeSensitive.Edit))
            Assert.Equal(HttpStatusCode.Forbidden, (await PostPrepareAsync(noCreate, bytes)).StatusCode);
        using (var noSensitive = AuthorizedClient(Permissions.Employee.Import, Permissions.Employee.Create))
            Assert.Equal(HttpStatusCode.Forbidden, (await PostPrepareAsync(noSensitive, bytes)).StatusCode);

        using (var authorized = AuthorizedClient(Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit))
        using (var response = await PostPrepareAsync(authorized, bytes))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("operationId", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("confirmationToken", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("876543210987", body);
            Assert.DoesNotContain("FGHIJ5678K", body);
        }

        await factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo01Host, async services =>
        {
            Assert.Equal(before, await services.GetRequiredService<IHrmsDbContext>().Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == Demo01));
        });
    }

    [Fact]
    public async Task Confirm_returns_202_and_status_is_authenticated_tenant_scoped_and_sanitized()
    {
        var bytes = await BuildValidWorkbookAsync();
        int before = 0;
        await factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo01Host, async services =>
        {
            before = await services.GetRequiredService<IHrmsDbContext>().Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == Demo01);
        });

        using var authorized = AuthorizedClient(Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit);
        using var preparation = await PostPrepareAsync(authorized, bytes);
        Assert.Equal(HttpStatusCode.OK, preparation.StatusCode);
        var prepared = await preparation.Content.ReadFromJsonAsync<ApiResponse<EmployeeBulkImportPreparationDto>>();
        Assert.NotNull(prepared?.Data);

        using var confirmation = await authorized.PostAsJsonAsync("/api/employees/bulk-upload/confirm", new
        {
            OperationId = prepared!.Data!.OperationId,
            ConfirmationToken = prepared.Data.ConfirmationToken,
            Confirm = true
        });
        Assert.Equal(HttpStatusCode.Accepted, confirmation.StatusCode);
        var accepted = await confirmation.Content.ReadFromJsonAsync<ApiResponse<EmployeeBulkImportAcceptedDto>>();
        Assert.Equal("Queued", accepted?.Data?.State);
        Assert.Equal($"/api/employees/bulk-upload/{prepared.Data.OperationId:D}", accepted?.Data?.StatusUrl);

        await factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo01Host, async services =>
        {
            var db = services.GetRequiredService<IHrmsDbContext>();
            Assert.Equal(before, await db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == Demo01));
            Assert.Equal(EmployeeImportOperationState.Queued,
                (await db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync(x => x.Id == prepared.Data.OperationId)).State);
        });

        using (var anonymous = factory.CreateClientFor(HrmsApiFactory.Demo01Host))
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anonymous.GetAsync($"/api/employees/bulk-upload/{prepared.Data.OperationId:D}")).StatusCode);
        using (var noImport = AuthorizedClient(Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit))
            Assert.Equal(HttpStatusCode.Forbidden,
                (await noImport.GetAsync($"/api/employees/bulk-upload/{prepared.Data.OperationId:D}")).StatusCode);
        var foreignUser = SeedData.Users.First(x => x.TenantId == SeedData.TenantIds.Demo02);
        using (var foreignTenant = factory.CreateClientFor(HrmsApiFactory.Demo02Host))
        {
            var token = TestTokens.Create(foreignUser.Id, SeedData.TenantIds.Demo02, "DEMO02", foreignUser.Email,
                roles: [RoleNames.TenantAdmin], permissions: [Permissions.Employee.Import]);
            foreignTenant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.NotFound,
                (await foreignTenant.GetAsync($"/api/employees/bulk-upload/{prepared.Data.OperationId:D}")).StatusCode);
        }
        using var statusResponse = await authorized.GetAsync($"/api/employees/bulk-upload/{prepared.Data.OperationId:D}");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content.ReadFromJsonAsync<ApiResponse<EmployeeBulkImportStatusDto>>();
        Assert.Equal("Queued", status?.Data?.State);
        Assert.Equal((1, 0, 0, 0), (status!.Data!.TotalRows, status.Data.SucceededRows, status.Data.FailedRows, status.Data.SkippedRows));
        Assert.True(status.Data.LastUpdatedAtUtc > DateTime.UnixEpoch);
        var serialized = await statusResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("876543210987", serialized);
        Assert.DoesNotContain("FGHIJ5678K", serialized);
        Assert.DoesNotContain("HTTP", serialized);
    }

    [Fact]
    public async Task Import_history_is_authorized_paginated_and_tenant_scoped()
    {
        var bytes = await BuildValidWorkbookAsync();
        using var authorized = AuthorizedClient(Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit);
        using var preparation = await PostPrepareAsync(authorized, bytes);
        Assert.Equal(HttpStatusCode.OK, preparation.StatusCode);
        var prepared = await preparation.Content.ReadFromJsonAsync<ApiResponse<EmployeeBulkImportPreparationDto>>();
        Assert.NotNull(prepared?.Data);

        using (var anonymous = factory.CreateClientFor(HrmsApiFactory.Demo01Host))
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/employees/bulk-upload/history")).StatusCode);
        using (var noPermission = AuthorizedClient(Permissions.Employee.Create))
            Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.GetAsync("/api/employees/bulk-upload/history")).StatusCode);

        using var page = await authorized.GetAsync("/api/employees/bulk-upload/history?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var result = await page.Content.ReadFromJsonAsync<ApiResponse<EmployeeBulkImportHistoryDto>>();
        Assert.NotNull(result?.Data);
        Assert.Equal((1, 1), (result!.Data!.Page, result.Data.PageSize));
        Assert.InRange(result.Data.Items.Count, 0, 1);
        Assert.Contains(result.Data.Items, x => x.OperationId == prepared!.Data!.OperationId);
        var serialized = await page.Content.ReadAsStringAsync();
        Assert.DoesNotContain("876543210987", serialized);
        Assert.DoesNotContain("FGHIJ5678K", serialized);
        Assert.DoesNotContain("confirmationToken", serialized, StringComparison.OrdinalIgnoreCase);

        using (var invalidPage = await authorized.GetAsync("/api/employees/bulk-upload/history?page=0&pageSize=101"))
            Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);

        var foreignUser = SeedData.Users.First(x => x.TenantId == SeedData.TenantIds.Demo02);
        using var foreignTenant = factory.CreateClientFor(HrmsApiFactory.Demo02Host);
        var token = TestTokens.Create(foreignUser.Id, SeedData.TenantIds.Demo02, "DEMO02", foreignUser.Email,
            roles: [RoleNames.TenantAdmin], permissions: [Permissions.Employee.Import]);
        foreignTenant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var foreignPage = await foreignTenant.GetAsync("/api/employees/bulk-upload/history?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, foreignPage.StatusCode);
        var foreignResult = await foreignPage.Content.ReadFromJsonAsync<ApiResponse<EmployeeBulkImportHistoryDto>>();
        Assert.DoesNotContain(foreignResult!.Data!.Items, x => x.OperationId == prepared!.Data!.OperationId);
    }

    private HttpClient AuthorizedClient(params string[] permissions)
    {
        var client = factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        var token = TestTokens.Create(SeedData.Users[0].Id, Demo01, "DEMO01", "admin@demo01.com",
            roles: [RoleNames.TenantAdmin], permissions: permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, byte[] bytes)
    {
        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        multipart.Add(file, "file", "employee-upload.xlsx");
        return await client.PostAsync(Route, multipart);
    }

    private static async Task<HttpResponseMessage> PostPrepareAsync(HttpClient client, byte[] bytes)
    {
        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        multipart.Add(file, "file", "employee-upload.xlsx");
        return await client.PostAsync("/api/employees/bulk-upload/prepare", multipart);
    }

    private async Task<byte[]> BuildValidWorkbookAsync()
    {
        byte[] template = [];
        await factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo01Host, async services =>
        {
            var configs = services.GetRequiredService<IEmployeeCodeConfigurationService>();
            var config = await configs.SaveAsync(new EmployeeCodeConfigurationRequest
            {
                AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto, GenerationMethod = EmployeeCodeGenerationMethod.Simple,
                Prefix = "WEB", Separator = "-", NextNumber = 1, Padding = 5, EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-10)
            });
            Assert.True(config.Succeeded, config.Message);
            var templates = services.GetRequiredService<IEmployeeBulkUploadTemplateService>();
            var result = await templates.GenerateAsync(); Assert.True(result.Succeeded, result.Message); template = result.Value!;
            var db = services.GetRequiredService<IHrmsDbContext>();
            var designation = await db.Designations.AsNoTracking().Where(x => x.TenantId == Demo01 && x.IsActive).Select(x => x.Code).FirstAsync();
            template = Fill(template, designation, DateOnly.FromDateTime(DateTime.UtcNow));
        });
        return template;
    }

    private static byte[] Fill(byte[] bytes, string designation, DateOnly date)
    {
        using var workbook = new XLWorkbook(new MemoryStream(bytes)); var sheet = workbook.Worksheet("Employees");
        var columns = sheet.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
        void Set(string header, object value) => sheet.Cell(2, columns[header]).Value = XLCellValue.FromObject(value);
        Set("First Name *", "HTTP"); Set("Last Name *", "Preview"); Set("Aadhaar Number *", "876543210987"); Set("PAN *", "FGHIJ5678K");
        Set("Date of Joining *", date.ToDateTime(TimeOnly.MinValue)); Set("Employment Effective From *", date.ToDateTime(TimeOnly.MinValue));
        Set("Job Status *", "Active"); Set("Department Code *", "ENG"); Set("Designation Code *", designation);
        Set("Employment Change Reason *", "NewJoining"); Set("Employment Type *", "FullTime"); Set("Official Email", "http.preview@example.test");
        using var output = new MemoryStream(); workbook.SaveAs(output); return output.ToArray();
    }

    private static byte[] SetDepartment(byte[] bytes, string department)
    {
        using var workbook = new XLWorkbook(new MemoryStream(bytes)); var sheet = workbook.Worksheet("Employees");
        var column = sheet.Row(1).CellsUsed().Single(x => x.GetString() == "Department Code *").Address.ColumnNumber;
        sheet.Cell(2, column).Value = department;
        using var output = new MemoryStream(); workbook.SaveAs(output); return output.ToArray();
    }
}
