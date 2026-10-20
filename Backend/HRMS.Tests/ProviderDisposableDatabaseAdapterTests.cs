using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class ProviderDisposableDatabaseAdapterTests
{
    [Fact]
    public void Provider_identifier_quoting_escapes_provider_delimiters()
    {
        Assert.Equal("[run]]name]", SqlServerDisposableDatabaseAdapter.QuoteIdentifier("run]name"));
        Assert.Equal("`run``name`", MySqlDisposableDatabaseAdapter.QuoteIdentifier("run`name"));
    }

    [Fact]
    public void Provider_identifier_validation_rejects_empty_or_oversized_names()
    {
        Assert.Throws<InvalidOperationException>(() => SqlServerDisposableDatabaseAdapter.QuoteIdentifier(string.Empty));
        Assert.Throws<InvalidOperationException>(() => MySqlDisposableDatabaseAdapter.QuoteIdentifier(new string('a', 65)));
    }

    [Fact]
    public void Acceptance_gate_requires_both_approvals_before_credentials_are_resolved()
    {
        var target = new DisposableDatabaseTarget(
            DisposableDatabaseProvider.SqlServer,
            "approved-server",
            "1433",
            ["HRMS_Phase1E_20261013T195000Z_sql"],
            "20261013T195000Z",
            "isolated-provider-acceptance",
            "environment-only",
            Path.Combine(Path.GetTempPath(), "hrms-phase1e", "manifest.json"));

        var exception = Assert.Throws<InvalidOperationException>(() => ProviderAcceptanceExecutionGate.Validate(target, new(true, false)));
        Assert.Contains("creation and deletion approval", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
