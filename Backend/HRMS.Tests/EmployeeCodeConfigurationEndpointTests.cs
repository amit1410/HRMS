using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

[Collection("Employee Code Configuration endpoints")]
public sealed class EmployeeCodeConfigurationEndpointTests(HrmsApiFactory factory) : IClassFixture<HrmsApiFactory>
{
    [Fact]
    public async Task Configuration_edit_requires_the_existing_manage_permission()
    {
        using var client = factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(
            SeedData.Users[0].Id,
            SeedData.TenantIds.Demo01, "DEMO01", "viewer@demo01.com", roles: [RoleNames.TenantAdmin],
            permissions: [Permissions.EmployeeCodeConfiguration.View]));

        var response = await client.PutAsJsonAsync("/api/employee-code-configuration", new EmployeeCodeConfigurationRequest
        {
            VersionId = Guid.NewGuid(), AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.Simple, Prefix = "EMP", NextNumber = 1,
            Padding = 5, Separator = "-", EffectiveFrom = new DateOnly(2026, 3, 4)
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

[CollectionDefinition("Employee Code Configuration endpoints", DisableParallelization = true)]
public sealed class EmployeeCodeConfigurationEndpointsCollection { }
