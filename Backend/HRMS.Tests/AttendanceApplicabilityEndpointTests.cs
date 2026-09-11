using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Attendance;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class AttendanceApplicabilityEndpointTests : IClassFixture<HrmsApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly HrmsApiFactory factory;

    public AttendanceApplicabilityEndpointTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Authorized_http_crud_persists_rule_name_and_target_changes()
    {
        using var client = Client(HrmsApiFactory.Demo01Host, [Permissions.Attendance.View, Permissions.Attendance.PatternManage, Permissions.Attendance.ShiftManage]);
        var shiftResponse = await client.PostAsJsonAsync("/api/attendance/shifts", Shift("API-MORNING"));
        var shiftEnvelope = await ReadAsync<ShiftDto>(shiftResponse);
        Assert.Equal(HttpStatusCode.Created, shiftResponse.StatusCode);
        Assert.NotNull(shiftEnvelope.Data);
        var shift = shiftEnvelope.Data!;
        var create = await client.PostAsJsonAsync("/api/attendance/applicability", new ShiftApplicabilityRequest { RuleName = "  API Rule  ", ShiftId = shift.Id, Priority = 10, EffectiveFrom = new(2026, 1, 1) });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var listResponse = await client.GetAsync("/api/attendance/shift-applicability");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listEnvelope = await ReadAsync<PagedResult<ShiftApplicabilityDto>>(listResponse);
        Assert.NotNull(listEnvelope.Data);
        var list = listEnvelope.Data!;
        Assert.Contains(list.Items, x => x.RuleName == "API Rule");
        var rule = Assert.Single(list.Items, x => x.RuleName == "API Rule");
        var detail = (await ReadAsync<ShiftApplicabilityDto>(await client.GetAsync($"/api/attendance/shift-applicability/{rule.Id}"))).Data!;
        Assert.Equal("API Rule", detail.RuleName);

        var update = new ShiftApplicabilityRequest { RuleName = "  Updated API Rule  ", ShiftId = shift.Id, Priority = 20, EffectiveFrom = new(2026, 2, 1), EffectiveTo = new(2026, 12, 31) };
        var updated = await client.PutAsJsonAsync($"/api/attendance/shift-applicability/{rule.Id}", update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = (await ReadAsync<ShiftApplicabilityDto>(await client.GetAsync($"/api/attendance/shift-applicability/{rule.Id}"))).Data!;
        Assert.Equal("Updated API Rule", after.RuleName);
        Assert.Equal(20, after.Priority);
        Assert.Equal(new DateOnly(2026, 2, 1), after.EffectiveFrom);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/attendance/shift-applicability/{rule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/attendance/shift-applicability/{rule.Id}")).StatusCode);
    }

    [Fact]
    public async Task Applicability_http_endpoints_enforce_permission_and_tenant_scope()
    {
        using var employee = Client(HrmsApiFactory.Demo01Host, []);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/attendance/shift-applicability")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync($"/api/attendance/shift-applicability/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PutAsJsonAsync($"/api/attendance/shift-applicability/{Guid.NewGuid()}", new ShiftApplicabilityRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.DeleteAsync($"/api/attendance/shift-applicability/{Guid.NewGuid()}")).StatusCode);

        using var manager = Client(HrmsApiFactory.Demo01Host, [Permissions.Attendance.View]);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync($"/api/attendance/shift-applicability/{Guid.NewGuid()}", new ShiftApplicabilityRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync($"/api/attendance/shift-applicability/{Guid.NewGuid()}")).StatusCode);

        using var forgedTenant = Client(HrmsApiFactory.Demo01Host, [Permissions.Attendance.View], "DEMO02", SeedData.TenantIds.Demo02);
        var response = await forgedTenant.GetAsync("/api/attendance/shift-applicability");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authorized_http_update_switches_between_shift_and_pattern_targets()
    {
        using var client = Client(HrmsApiFactory.Demo01Host, [Permissions.Attendance.View, Permissions.Attendance.PatternManage, Permissions.Attendance.ShiftManage]);
        var shiftResponse = await client.PostAsJsonAsync("/api/attendance/shifts", Shift("API-SWITCH"));
        Assert.Equal(HttpStatusCode.Created, shiftResponse.StatusCode);
        var shift = (await ReadAsync<ShiftDto>(shiftResponse)).Data!;
        var patternResponse = await client.PostAsJsonAsync("/api/attendance/patterns", new ShiftPatternRequest { Code = "API-PAT", Name = "API-PAT", CycleLengthDays = 1, EffectiveFrom = new(2026, 1, 1), Days = [new ShiftPatternDayRequest { SequenceDay = 1, ShiftId = shift.Id }] });
        Assert.Equal(HttpStatusCode.Created, patternResponse.StatusCode);
        var pattern = (await ReadAsync<ShiftPatternDto>(patternResponse)).Data!;
        var create = await client.PostAsJsonAsync("/api/attendance/applicability", new ShiftApplicabilityRequest { RuleName = "Switch Rule", ShiftId = shift.Id, Priority = 10, EffectiveFrom = new(2026, 1, 1) });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var list = (await ReadAsync<PagedResult<ShiftApplicabilityDto>>(await client.GetAsync("/api/attendance/shift-applicability"))).Data!;
        var rule = Assert.Single(list.Items, x => x.RuleName == "Switch Rule");

        var toPattern = await client.PutAsJsonAsync($"/api/attendance/shift-applicability/{rule.Id}", new ShiftApplicabilityRequest { RuleName = "Switch Rule", ShiftPatternId = pattern.Id, Priority = 10, EffectiveFrom = new(2026, 1, 1) });
        Assert.Equal(HttpStatusCode.OK, toPattern.StatusCode);
        var patternDetail = (await ReadAsync<ShiftApplicabilityDto>(await client.GetAsync($"/api/attendance/shift-applicability/{rule.Id}"))).Data!;
        Assert.Equal(pattern.Id, patternDetail.ShiftPatternId);
        Assert.Null(patternDetail.ShiftId);

        var toShift = await client.PutAsJsonAsync($"/api/attendance/shift-applicability/{rule.Id}", new ShiftApplicabilityRequest { RuleName = "Switch Rule", ShiftId = shift.Id, Priority = 10, EffectiveFrom = new(2026, 1, 1) });
        Assert.Equal(HttpStatusCode.OK, toShift.StatusCode);
        var shiftDetail = (await ReadAsync<ShiftApplicabilityDto>(await client.GetAsync($"/api/attendance/shift-applicability/{rule.Id}"))).Data!;
        Assert.Equal(shift.Id, shiftDetail.ShiftId);
        Assert.Null(shiftDetail.ShiftPatternId);
    }

    [Fact]
    public async Task Authorized_http_condition_add_update_and_remove_persists_exact_set()
    {
        using var client = Client(HrmsApiFactory.Demo01Host, [Permissions.Attendance.View, Permissions.Attendance.PatternManage, Permissions.Attendance.ShiftManage]);
        var shift = (await ReadAsync<ShiftDto>(await client.PostAsJsonAsync("/api/attendance/shifts", Shift("API-CONDITIONS")))).Data!;
        var department = Guid.NewGuid();
        var grade = Guid.NewGuid();
        var workLocation = Guid.NewGuid();
        var create = await client.PostAsJsonAsync("/api/attendance/applicability", new ShiftApplicabilityRequest { RuleName = "Condition Rule", ShiftId = shift.Id, DepartmentId = department, Priority = 10, EffectiveFrom = new(2026, 1, 1) });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var rule = Assert.Single((await ReadAsync<PagedResult<ShiftApplicabilityDto>>(await client.GetAsync("/api/attendance/shift-applicability"))).Data!.Items, x => x.RuleName == "Condition Rule");

        var add = await client.PutAsJsonAsync($"/api/attendance/shift-applicability/{rule.Id}", new ShiftApplicabilityRequest { RuleName = "Condition Rule", ShiftId = shift.Id, DepartmentId = department, GradeId = grade, Priority = 10, EffectiveFrom = new(2026, 1, 1) });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var added = (await ReadAsync<ShiftApplicabilityDto>(await client.GetAsync($"/api/attendance/shift-applicability/{rule.Id}"))).Data!;
        Assert.Equal(2, added.Conditions.Values.Count(x => x is not null));
        Assert.Equal(department, added.Conditions["Department"]);
        Assert.Equal(grade, added.Conditions["Grade"]);

        var update = await client.PutAsJsonAsync($"/api/attendance/shift-applicability/{rule.Id}", new ShiftApplicabilityRequest { RuleName = "Condition Rule", ShiftId = shift.Id, WorkLocationId = workLocation, Priority = 10, EffectiveFrom = new(2026, 1, 1) });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await ReadAsync<ShiftApplicabilityDto>(await client.GetAsync($"/api/attendance/shift-applicability/{rule.Id}"))).Data!;
        Assert.Equal(1, updated.Conditions.Values.Count(x => x is not null));
        Assert.Equal(workLocation, updated.Conditions["WorkLocation"]);
    }

    [Fact]
    public async Task Denied_applicability_mutations_preserve_persisted_rule()
    {
        using var authorized = Client(HrmsApiFactory.Demo01Host, [Permissions.Attendance.View, Permissions.Attendance.PatternManage, Permissions.Attendance.ShiftManage]);
        var shift = (await ReadAsync<ShiftDto>(await authorized.PostAsJsonAsync("/api/attendance/shifts", Shift("API-PROTECTED")))).Data!;
        var create = await authorized.PostAsJsonAsync("/api/attendance/applicability", new ShiftApplicabilityRequest { RuleName = "Original Rule", ShiftId = shift.Id, Priority = 10, EffectiveFrom = new(2026, 1, 1) });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var rule = Assert.Single((await ReadAsync<PagedResult<ShiftApplicabilityDto>>(await authorized.GetAsync("/api/attendance/shift-applicability"))).Data!.Items, x => x.RuleName == "Original Rule");

        using var employee = Client(HrmsApiFactory.Demo01Host, []);
        var update = await employee.PutAsJsonAsync($"/api/attendance/shift-applicability/{rule.Id}", new ShiftApplicabilityRequest { RuleName = "Hacked Rule", ShiftId = shift.Id, Priority = 999, EffectiveFrom = new(2026, 1, 1) });
        var delete = await employee.DeleteAsync($"/api/attendance/shift-applicability/{rule.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        var after = (await ReadAsync<ShiftApplicabilityDto>(await authorized.GetAsync($"/api/attendance/shift-applicability/{rule.Id}"))).Data!;
        Assert.Equal("Original Rule", after.RuleName);
        Assert.Equal(10, after.Priority);
        Assert.Equal(shift.Id, after.ShiftId);
    }

    [Fact]
    public async Task Applicability_http_tenant_crud_isolation_preserves_other_tenant_rule()
    {
        using var tenantA = Client(HrmsApiFactory.Demo01Host, [Permissions.Attendance.View, Permissions.Attendance.PatternManage, Permissions.Attendance.ShiftManage]);
        using var tenantB = Client(HrmsApiFactory.Demo02Host, [Permissions.Attendance.View, Permissions.Attendance.PatternManage, Permissions.Attendance.ShiftManage], "DEMO02", SeedData.TenantIds.Demo02);
        var shiftA = (await ReadAsync<ShiftDto>(await tenantA.PostAsJsonAsync("/api/attendance/shifts", Shift("A-ISO")))).Data!;
        var shiftB = (await ReadAsync<ShiftDto>(await tenantB.PostAsJsonAsync("/api/attendance/shifts", Shift("B-ISO")))).Data!;
        Assert.Equal(HttpStatusCode.OK, (await tenantA.PostAsJsonAsync("/api/attendance/applicability", new ShiftApplicabilityRequest { RuleName = "A_RULE", ShiftId = shiftA.Id, Priority = 10, EffectiveFrom = new(2026, 1, 1) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tenantB.PostAsJsonAsync("/api/attendance/applicability", new ShiftApplicabilityRequest { RuleName = "B_RULE", ShiftId = shiftB.Id, Priority = 20, EffectiveFrom = new(2026, 1, 1) })).StatusCode);
        var aRule = Assert.Single((await ReadAsync<PagedResult<ShiftApplicabilityDto>>(await tenantA.GetAsync("/api/attendance/shift-applicability"))).Data!.Items, x => x.RuleName == "A_RULE");
        var bRule = Assert.Single((await ReadAsync<PagedResult<ShiftApplicabilityDto>>(await tenantB.GetAsync("/api/attendance/shift-applicability"))).Data!.Items, x => x.RuleName == "B_RULE");
        Assert.DoesNotContain((await ReadAsync<PagedResult<ShiftApplicabilityDto>>(await tenantA.GetAsync("/api/attendance/shift-applicability"))).Data!.Items, x => x.Id == bRule.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantA.GetAsync($"/api/attendance/shift-applicability/{bRule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantA.PutAsJsonAsync($"/api/attendance/shift-applicability/{bRule.Id}", new ShiftApplicabilityRequest { RuleName = "HACKED", ShiftId = shiftA.Id, Priority = 999, EffectiveFrom = new(2026, 1, 1) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantA.DeleteAsync($"/api/attendance/shift-applicability/{bRule.Id}")).StatusCode);
        var bAfter = (await ReadAsync<ShiftApplicabilityDto>(await tenantB.GetAsync($"/api/attendance/shift-applicability/{bRule.Id}"))).Data!;
        Assert.Equal("B_RULE", bAfter.RuleName); Assert.Equal(20, bAfter.Priority); Assert.Equal(shiftB.Id, bAfter.ShiftId);
        Assert.Equal(aRule.RuleName, (await ReadAsync<ShiftApplicabilityDto>(await tenantA.GetAsync($"/api/attendance/shift-applicability/{aRule.Id}"))).Data!.RuleName);
    }

    [Fact]
    public async Task Applicability_http_tenant_detail_update_delete_isolation_is_symmetric()
    {
        using var tenantA = Client(HrmsApiFactory.Demo01Host, [Permissions.Attendance.View, Permissions.Attendance.PatternManage, Permissions.Attendance.ShiftManage]);
        using var tenantB = Client(HrmsApiFactory.Demo02Host, [Permissions.Attendance.View, Permissions.Attendance.PatternManage, Permissions.Attendance.ShiftManage], "DEMO02", SeedData.TenantIds.Demo02);
        var shiftA = (await ReadAsync<ShiftDto>(await tenantA.PostAsJsonAsync("/api/attendance/shifts", Shift("A-MATRIX")))).Data!;
        var shiftB = (await ReadAsync<ShiftDto>(await tenantB.PostAsJsonAsync("/api/attendance/shifts", Shift("B-MATRIX")))).Data!;
        Assert.Equal(HttpStatusCode.OK, (await tenantA.PostAsJsonAsync("/api/attendance/applicability", new ShiftApplicabilityRequest { RuleName = "Tenant A Rule", ShiftId = shiftA.Id, Priority = 10, EffectiveFrom = new(2026, 1, 1) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tenantB.PostAsJsonAsync("/api/attendance/applicability", new ShiftApplicabilityRequest { RuleName = "Tenant B Original Rule", ShiftId = shiftB.Id, Priority = 20, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31) })).StatusCode);
        var aList = (await ReadAsync<PagedResult<ShiftApplicabilityDto>>(await tenantA.GetAsync("/api/attendance/shift-applicability"))).Data!;
        var bList = (await ReadAsync<PagedResult<ShiftApplicabilityDto>>(await tenantB.GetAsync("/api/attendance/shift-applicability"))).Data!;
        var aRule = Assert.Single(aList.Items, x => x.RuleName == "Tenant A Rule");
        var bRule = Assert.Single(bList.Items, x => x.RuleName == "Tenant B Original Rule");
        Assert.DoesNotContain(aList.Items, x => x.Id == bRule.Id);
        Assert.DoesNotContain(bList.Items, x => x.Id == aRule.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await tenantA.GetAsync($"/api/attendance/shift-applicability/{bRule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantA.PutAsJsonAsync($"/api/attendance/shift-applicability/{bRule.Id}", new ShiftApplicabilityRequest { RuleName = "Tenant A Tried To Change B", ShiftId = shiftA.Id, Priority = 999, EffectiveFrom = new(2027, 1, 1) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantA.DeleteAsync($"/api/attendance/shift-applicability/{bRule.Id}")).StatusCode);

        var preserved = (await ReadAsync<ShiftApplicabilityDto>(await tenantB.GetAsync($"/api/attendance/shift-applicability/{bRule.Id}"))).Data!;
        Assert.Equal("Tenant B Original Rule", preserved.RuleName);
        Assert.Equal(20, preserved.Priority);
        Assert.Equal(new DateOnly(2026, 1, 1), preserved.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), preserved.EffectiveTo);
        Assert.Equal(shiftB.Id, preserved.ShiftId);
        Assert.Null(preserved.ShiftPatternId);
    }

    private HttpClient Client(string host, IReadOnlyList<string> permissions, string tenantCode = "DEMO01", Guid? tenantId = null)
    {
        var client = factory.CreateClientFor(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(Guid.NewGuid(), tenantId ?? SeedData.TenantIds.Demo01, tenantCode, permissions: permissions, roles: [RoleNames.TenantAdmin]));
        return client;
    }

    private static ShiftRequest Shift(string code) => new() { ShiftCode = code, ShiftName = code, StartTime = new(9, 0), EndTime = new(18, 0), EffectiveFrom = new(2026, 1, 1), MinimumWorkMinutes = 480, FullDayWorkMinutes = 480, CaptureMode = AttendanceCaptureMode.BiometricOnly, AllowedAttendanceSources = AttendanceSource.Biometric };

    private static async Task<ApiResponse<T>> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json);
        Assert.NotNull(body);
        return body!;
    }
}
