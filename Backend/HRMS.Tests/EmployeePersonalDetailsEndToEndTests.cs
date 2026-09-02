using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// End-to-end proof that <em>every</em> Employee Personal Details field survives the whole round trip:
/// create → what the response returns → what is really stored → edit → updated values stored → reload.
///
/// The response masks the sensitive identifiers (Aadhaar, PAN, PF, UAN), so a bare response check cannot
/// prove those were persisted. That is why each step also reads the raw entity through an unscoped context
/// (no tenant filter, direct tables) and asserts the exact stored value — the "verify the database" part.
///
/// The intent is a field-by-field audit of the Personal Details path, not a special case for six fields:
/// every input on the form is covered, so the mapping can never regress for one field while the others pass.
/// </summary>
public class EmployeePersonalDetailsEndToEndTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly DateOnly Joined = new(2023, 5, 1);
    private static readonly DateOnly Dob = new(1994, 3, 3);

    [Fact]
    public async Task Every_personal_detail_field_creates_persists_reloads_and_updates()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var created = await harness.Employees().CreatePersonalDetailsAsync(FullRequest());
        Assert.True(created.Succeeded, created.Message);

        var id = created.Value!.Id;

        // --- Create: the API response reflects every field. ---
        AssertAll(created.Value, FullRequest());
        AssertSensitive((await harness.Employees().GetSensitiveDetailsAsync(id)).Value!, FullRequest());

        // --- Database: the raw row holds the exact values the form sent. ---
        AssertStored(await LoadRawAsync(harness, id), FullRequest());

        // --- Reload through the API: a fresh read returns the same fields. ---
        var reloaded = await harness.Employees().GetByIdAsync(id);
        Assert.True(reloaded.Succeeded);
        AssertAll(reloaded.Value!, FullRequest());

        // --- Edit: every field can change. ---
        var changed = FullRequest();
        changed.FirstName = "UpdatedFirst";
        changed.MiddleName = "UpdatedMiddle";
        changed.LastName = "UpdatedLast";
        changed.Salutation = "Dr.";
        changed.DateOfBirth = Dob.AddDays(-5);
        changed.Gender = Gender.Female;
        changed.BloodGroup = BloodGroup.ANegative;
        changed.MaritalStatus = MaritalStatus.Married;
        changed.Religion = "Religion-B";
        changed.Caste = "Caste-B";
        changed.Citizenship = "India";
        changed.DateOfJoining = Joined.AddDays(10);
        changed.JobStatus = "On Probation";
        changed.EsicApplicable = true;
        changed.EsicNumber = "ESIC-900";
        changed.MediclaimNumber = "MED-777";
        changed.Gratuity = true;
        changed.Pension = true;
        // The six highlighted fields change to new values.
        changed.PfNumber = "PF-TEST-002";
        changed.UanNumber = "999988887777";
        changed.AadhaarNumber = "444455556666";
        changed.PanNumber = "ZYXWV0987Q";

        var updated = await harness.Employees().UpdatePersonalDetailsAsync(id, changed);
        Assert.True(updated.Succeeded, updated.Message);
        AssertAll(updated.Value!, changed);
        AssertSensitive((await harness.Employees().GetSensitiveDetailsAsync(id)).Value!, changed);

        AssertStored(await LoadRawAsync(harness, id), changed);

        // --- Reload again: the updated values are what come back. ---
        var reloadedAgain = await harness.Employees().GetByIdAsync(id);
        Assert.True(reloadedAgain.Succeeded);
        AssertAll(reloadedAgain.Value!, changed);
    }

    [Fact]
    public async Task Edit_with_blank_sensitive_ids_keeps_the_stored_values()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var created = (await harness.Employees().CreatePersonalDetailsAsync(FullRequest())).Value!;

        // A real edit form cannot resend Aadhaar/PAN/PF/UAN (they come back masked), so the fields arrive
        // blank. Blank must mean "unchanged", not "cleared" — otherwise every save would wipe them.
        var request = FullRequest();
        request.AadhaarNumber = null;
        request.PanNumber = null;
        request.PfNumber = null;
        request.UanNumber = null;

        var updated = await harness.Employees().UpdatePersonalDetailsAsync(created.Id, request);
        Assert.True(updated.Succeeded, updated.Message);

        var stored = await LoadRawAsync(harness, created.Id);
        Assert.Equal("111122223333", stored.AadhaarNumber);
        Assert.Equal("ABCDE1234F", stored.PanNumber);
        Assert.Equal("PF-TEST-001", stored.PfNumber);
        Assert.Equal("UAN-TEST-001", stored.UanNumber);
    }

    private static async Task<Employee> LoadRawAsync(OrganizationTestHarness harness, Guid id)
    {
        var context = harness.CreateUnscopedContext();
        return await context.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == id);
    }

    private static EmployeePersonalDetailsRequest FullRequest() => new()
    {
        Salutation = "Mr.",
        FirstName = "TestFirst",
        MiddleName = "TestMiddle",
        LastName = "TestLast",
        DateOfBirth = Dob,
        Gender = Gender.Male,
        BloodGroup = BloodGroup.OPositive,
        MaritalStatus = MaritalStatus.Single,
        Religion = "Religion-A",
        Caste = "Caste-A",
        Citizenship = "Nigeria",
        EsicApplicable = true,
        EsicNumber = "ESIC-123",
        PfNumber = "PF-TEST-001",
        MediclaimNumber = "MED-456",
        UanNumber = "UAN-TEST-001",
        Gratuity = false,
        Pension = false,
        AadhaarNumber = "111122223333",
        PanNumber = "ABCDE1234F",
        DateOfJoining = Joined,
        JobStatus = "Active",
    };

    /// <summary>Asserts every non-sensitive field on the API response DTO matches the request, plus the masks
    /// derived from the requested sensitive identifiers.</summary>
    private static void AssertAll(EmployeeDto dto, EmployeePersonalDetailsRequest request)
    {
        Assert.Equal(request.Salutation, dto.Salutation);
        Assert.Equal(request.FirstName, dto.FirstName);
        Assert.Equal(request.MiddleName, dto.MiddleName);
        Assert.Equal(request.LastName, dto.LastName);
        Assert.Equal(request.DateOfBirth, dto.DateOfBirth);
        Assert.Equal(request.Gender, dto.Gender);
        Assert.Equal(request.BloodGroup, dto.BloodGroup);
        Assert.Equal(request.MaritalStatus, dto.MaritalStatus);
        Assert.Equal(request.Religion, dto.Religion);
        Assert.Equal(request.Caste, dto.Caste);
        Assert.Equal(request.Citizenship, dto.Citizenship);
        Assert.Equal(request.EsicApplicable, dto.EsicApplicable);
        Assert.Equal(EmployeeDto.MaskNumericId(request.EsicNumber), dto.MaskedEsicNumber);
        Assert.Equal(EmployeeDto.MaskNumericId(request.MediclaimNumber), dto.MaskedMediclaimNumber);
        Assert.Equal(request.Gratuity, dto.Gratuity);
        Assert.Equal(request.Pension, dto.Pension);
        Assert.Equal(request.JobStatus, dto.JobStatus);
        Assert.Equal(request.DateOfJoining, dto.DateOfJoining);
        // General reads and write responses expose only masked statutory identifiers.
        Assert.Equal(EmployeeDto.MaskAadhaar(request.AadhaarNumber), dto.MaskedAadhaarNumber);
        Assert.Equal(EmployeeDto.MaskPan(request.PanNumber), dto.MaskedPanNumber);
        Assert.Equal(EmployeeDto.MaskNumericId(request.PfNumber), dto.MaskedPfNumber);
        Assert.Equal(EmployeeDto.MaskNumericId(request.UanNumber), dto.MaskedUanNumber);
    }

    private static void AssertSensitive(EmployeeSensitiveDetailsDto dto, EmployeePersonalDetailsRequest request)
    {
        Assert.Equal(request.AadhaarNumber, dto.AadhaarNumber);
        Assert.Equal(request.PanNumber, dto.PanNumber);
        Assert.Equal(request.UanNumber, dto.UanNumber);
        Assert.Equal(request.PfNumber, dto.PfNumber);
        Assert.Equal(request.EsicNumber, dto.EsicNumber);
        Assert.Equal(request.MediclaimNumber, dto.MediclaimNumber);
    }

    /// <summary>Asserts the raw row holds the exact values that were requested, unmasked.</summary>
    private static void AssertStored(Employee entity, EmployeePersonalDetailsRequest request)
    {
        Assert.Equal(request.Salutation, entity.Salutation);
        Assert.Equal(request.FirstName, entity.FirstName);
        Assert.Equal(request.MiddleName, entity.MiddleName);
        Assert.Equal(request.LastName, entity.LastName);
        Assert.Equal(request.DateOfBirth, entity.DateOfBirth);
        Assert.Equal(request.Gender, entity.Gender);
        Assert.Equal(request.BloodGroup, entity.BloodGroup);
        Assert.Equal(request.MaritalStatus, entity.MaritalStatus);
        Assert.Equal(request.Religion, entity.Religion);
        Assert.Equal(request.Caste, entity.Caste);
        Assert.Equal(request.Citizenship, entity.Citizenship);
        Assert.Equal(request.EsicApplicable, entity.EsicApplicable);
        Assert.Equal(request.EsicNumber, entity.EsicNumber);
        Assert.Equal(request.MediclaimNumber, entity.MediclaimNumber);
        Assert.Equal(request.Gratuity, entity.Gratuity);
        Assert.Equal(request.Pension, entity.Pension);
        Assert.Equal(request.JobStatus, entity.JobStatus);
        Assert.Equal(request.DateOfJoining, entity.DateOfJoining);
        // The six highlighted fields, stored in full.
        Assert.Equal(request.PfNumber, entity.PfNumber);
        Assert.Equal(request.UanNumber, entity.UanNumber);
        Assert.Equal(request.AadhaarNumber, entity.AadhaarNumber);
        Assert.Equal(request.PanNumber, entity.PanNumber);
    }
}
