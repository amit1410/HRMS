using System.Text;
using HRMS.Application.DTOs.Banks;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// Bank master CSV/XLSX import, export and history, exercised through the real services over the in-memory
/// database so validation, the transactional confirm and the shared history table all participate.
/// </summary>
public class BankImportServiceTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;

    private const string Header = "BankCode,BankName,ShortName,IFSCPrefix,BankType,Country,Active,EffectiveFrom,Remarks";

    private static Stream Csv(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public void Template_csv_lists_the_expected_columns()
    {
        using var harness = OrganizationTestHarness.CreateAsync().GetAwaiter().GetResult();

        var csv = Encoding.UTF8.GetString(harness.BankImports().BuildTemplate(BankFileFormat.Csv));

        foreach (var column in new[] { "BankCode", "BankName", "IFSCPrefix", "BankType", "Active", "EffectiveFrom", "Remarks" })
        {
            Assert.Contains(column, csv);
        }
    }

    [Fact]
    public async Task Validate_flags_duplicate_of_existing_in_create_only_and_bad_rows()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var content = Header + "\r\n"
            + "SBI,Duplicate,,,,,true,,\r\n"           // existing code — error under CreateOnly
            + "YES,,,, ,,true,,\r\n"                    // missing name — error
            + "NEWB,New Bank,NB,NEWB0001,NotAType,India,true,2024-04-01,ok\r\n"; // bad BankType — error

        var result = await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, Csv(content), "banks.csv");

        Assert.True(result.Succeeded);
        var preview = result.Value!;
        Assert.Equal(3, preview.TotalRows);
        Assert.Equal(3, preview.ErrorRows);
        Assert.Equal(0, preview.ValidRows);
    }

    [Fact]
    public async Task Validate_accepts_a_clean_new_row()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var content = Header + "\r\n" + "KOTAK,Kotak Mahindra Bank,Kotak,KKBK,Private,India,true,2024-04-01,New\r\n";

        var result = await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, Csv(content), "banks.csv");

        var preview = result.Value!;
        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(1, preview.NewRows);
        Assert.Equal("Create", preview.Rows[0].Action);
    }

    [Fact]
    public async Task Confirm_create_only_creates_new_banks_and_records_history()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var content = Header + "\r\n" + "KOTAK,Kotak Mahindra Bank,Kotak,KKBK,Private,India,true,2024-04-01,New\r\n";
        var preview = (await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, Csv(content), "banks.csv")).Value!;

        var confirm = await harness.BankImports().ConfirmAsync(
            new BankImportConfirmRequest(BankImportMode.CreateOnly, "banks.csv", preview.InputRows), "hr@demo01.com");

        Assert.True(confirm.Succeeded);
        Assert.Equal(1, confirm.Value!.CreatedRows);

        var kotak = await harness.Banks().GetAsync(new BankQuery { Search = "KOTAK" });
        Assert.Single(kotak.Value!.Items);

        var history = await harness.BankImports().GetHistoryAsync();
        var batch = Assert.Single(history.Value!);
        Assert.Equal("Completed", batch.Status);
        Assert.Equal(1, batch.SuccessfulRows);
        Assert.Equal("hr@demo01.com", batch.ImportedBy);
    }

    [Fact]
    public async Task Confirm_create_or_update_updates_an_existing_bank()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var content = Header + "\r\n" + "SBI,State Bank of India,SBI,SBIN,Public,India,true,2024-04-01,Updated remarks\r\n";
        var preview = (await harness.BankImports().ValidateAsync(BankImportMode.CreateOrUpdate, Csv(content), "banks.csv")).Value!;
        Assert.Equal(1, preview.UpdateRows);

        var confirm = await harness.BankImports().ConfirmAsync(
            new BankImportConfirmRequest(BankImportMode.CreateOrUpdate, "banks.csv", preview.InputRows), "hr@demo01.com");

        Assert.True(confirm.Succeeded);
        Assert.Equal(1, confirm.Value!.UpdatedRows);

        var sbi = (await harness.Banks().GetAsync(new BankQuery { Search = "SBI" })).Value!.Items.Single(b => b.Code == "SBI");
        Assert.Equal("SBIN", sbi.IfscPrefix);
        Assert.Equal("Updated remarks", sbi.Remarks);
    }

    [Fact]
    public async Task Xlsx_template_round_trips_through_validation()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var xlsx = harness.BankImports().BuildTemplate(BankFileFormat.Xlsx);

        var result = await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, new MemoryStream(xlsx), "template.xlsx");

        Assert.True(result.Succeeded);
        var preview = result.Value!;
        Assert.Equal(1, preview.TotalRows);
        Assert.Equal("HDFC", preview.InputRows[0].BankCode);
    }

    [Fact]
    public async Task Export_csv_contains_a_seeded_bank()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var banks = (await harness.Banks().GetAllForExportAsync(new BankQuery())).Value!;

        var csv = Encoding.UTF8.GetString(harness.BankImports().BuildExport(banks, BankFileFormat.Csv));

        Assert.Contains("SBI", csv);
        Assert.Contains("State Bank of India", csv);
    }

    [Fact]
    public async Task Unsupported_extension_is_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, Csv("x"), "banks.txt");

        Assert.False(result.Succeeded);
    }

    // ---- Validation parity with the interactive create/update flow ----

    [Fact]
    public async Task Validate_flags_an_invalid_bank_code_format()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var content = Header + "\r\n" + "BAD CODE,New Bank,,,,,true,,\r\n"; // space is not an allowed code character

        var preview = (await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, Csv(content), "banks.csv")).Value!;

        Assert.Equal(1, preview.ErrorRows);
        Assert.Equal("Error", preview.Rows[0].Action);
    }

    [Fact]
    public async Task Validate_flags_a_short_name_over_the_max_length()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var content = Header + "\r\n" + $"NEWB,New Bank,{new string('x', 101)},,,,true,,\r\n";

        var preview = (await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, Csv(content), "banks.csv")).Value!;

        Assert.Equal(1, preview.ErrorRows);
        Assert.Contains(preview.Rows[0].Errors, e => e.Contains("Short name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_flags_invalid_ifsc_prefix_characters()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var content = Header + "\r\n" + "NEWB,New Bank,,SB!N,,,true,,\r\n"; // '!' is not a letter or digit

        var preview = (await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, Csv(content), "banks.csv")).Value!;

        Assert.Equal(1, preview.ErrorRows);
        Assert.Contains(preview.Rows[0].Errors, e => e.Contains("IFSC prefix", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_flags_remarks_over_the_max_length()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var content = Header + "\r\n" + $"NEWB,New Bank,,,,,true,,{new string('x', 1001)}\r\n";

        var preview = (await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, Csv(content), "banks.csv")).Value!;

        Assert.Equal(1, preview.ErrorRows);
        Assert.Contains(preview.Rows[0].Errors, e => e.Contains("Remarks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_flags_a_duplicate_bank_name_within_the_file()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var content = Header + "\r\n"
            + "AAAA,Same Name,,,,,true,,\r\n"
            + "BBBB,Same Name,,,,,true,,\r\n"; // distinct codes, same name

        var preview = (await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, Csv(content), "banks.csv")).Value!;

        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(1, preview.ErrorRows);
        Assert.Contains(preview.Rows[1].Errors, e => e.Contains("duplicated in this file", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_flags_a_bank_name_that_already_exists_in_the_tenant()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        // A new code, but the name belongs to the seeded SBI bank.
        var content = Header + "\r\n" + "NEWX,State Bank of India,,,,,true,,\r\n";

        var preview = (await harness.BankImports().ValidateAsync(BankImportMode.CreateOnly, Csv(content), "banks.csv")).Value!;

        Assert.Equal(1, preview.ErrorRows);
        Assert.Contains(preview.Rows[0].Errors, e => e.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_allows_an_update_that_keeps_the_existing_name()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        // Re-importing SBI with its own name must not trip the name-uniqueness check (exclude-self).
        var content = Header + "\r\n" + "SBI,State Bank of India,,SBIN,Public,India,true,,\r\n";

        var preview = (await harness.BankImports().ValidateAsync(BankImportMode.CreateOrUpdate, Csv(content), "banks.csv")).Value!;

        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal(1, preview.UpdateRows);
        Assert.Equal("Update", preview.Rows[0].Action);
    }

    [Fact]
    public async Task Confirm_reports_a_name_collision_as_a_conflict_rather_than_throwing()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        // A name that collides with the seeded SBI bank would previously have thrown from the service mid
        // import (a 500). It must now surface as a preview error and a clean conflict from confirm.
        var rows = new[]
        {
            new BankImportRow(2, "NEWX", "State Bank of India", null, null, null, null, true, null, null),
        };

        var confirm = await harness.BankImports().ConfirmAsync(
            new BankImportConfirmRequest(BankImportMode.CreateOnly, "banks.csv", rows), "hr@demo01.com");

        Assert.False(confirm.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, confirm.Status);

        // Nothing was written, and no history row was recorded for the aborted attempt.
        var created = await harness.Banks().GetAsync(new BankQuery { Search = "NEWX" });
        Assert.Empty(created.Value!.Items);
        var history = await harness.BankImports().GetHistoryAsync();
        Assert.Empty(history.Value!);
    }
}
