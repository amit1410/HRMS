namespace HRMS.Tests;

public sealed class BrowserAcceptanceScriptTests
{
    [Fact]
    public void Setup_script_resolves_the_nested_project_from_script_root()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "tools", "Phase3BBrowserAcceptance", "Phase3BBrowserAcceptance.csproj");
        var script = LoadScript(repositoryRoot);

        Assert.True(File.Exists(projectPath));
        Assert.Contains("$projectPath = Join-Path $PSScriptRoot 'Phase3BBrowserAcceptance\\Phase3BBrowserAcceptance.csproj'", script);
        Assert.DoesNotContain("Join-Path $PSScriptRoot 'Phase3BBrowserAcceptance.csproj'", script);
    }

    [Fact]
    public void Setup_script_is_independent_of_the_caller_working_directory()
    {
        var script = LoadScript(FindRepositoryRoot());

        Assert.Contains("$PSScriptRoot", script);
        Assert.DoesNotContain("Get-Location", script);
        Assert.Contains("$projectPath", script);
    }

    [Fact]
    public void Setup_failure_is_checked_before_any_state_read()
    {
        var script = LoadScript(FindRepositoryRoot());
        var exitCapture = script.IndexOf("$setupExitCode = $LASTEXITCODE", StringComparison.Ordinal);
        var exitCheck = script.IndexOf("if ($setupExitCode -ne 0)", StringComparison.Ordinal);
        var stateRead = script.IndexOf("$run = Get-Content -LiteralPath $state", StringComparison.Ordinal);

        Assert.True(exitCapture >= 0);
        Assert.True(exitCheck > exitCapture);
        Assert.True(stateRead > exitCheck);
    }

    [Fact]
    public void Setup_requires_a_state_file_after_successful_setup()
    {
        var script = LoadScript(FindRepositoryRoot());
        var stateCheck = script.IndexOf("Test-Path -LiteralPath $state -PathType Leaf", StringComparison.Ordinal);
        var stateRead = script.IndexOf("$run = Get-Content -LiteralPath $state", StringComparison.Ordinal);

        Assert.True(stateCheck >= 0);
        Assert.True(stateRead > stateCheck);
        Assert.Contains("did not create expected state file", script);
    }

    [Fact]
    public void Acceptance_launcher_configures_both_tenant_api_origins_for_the_selected_port()
    {
        var script = LoadScript(FindRepositoryRoot());

        Assert.Contains("$acceptanceApiOrigins = @(", script);
        Assert.Contains("http://tenant-a.localhost:$apiPort", script);
        Assert.Contains("http://tenant-b.localhost:$apiPort", script);
        Assert.Contains("$env:VITE_API_CSP_CONNECT_SRC = $acceptanceApiOrigins -join ' '", script);
    }

    [Fact]
    public void Acceptance_launcher_requires_expected_branding_before_announcing_readiness()
    {
        var script = LoadScript(FindRepositoryRoot());

        Assert.Contains("function Wait-ForBranding", script);
        Assert.Contains("$body.data.displayName -eq $ExpectedDisplayName", script);
        Assert.Contains("Wait-ForBranding -ApiPort $apiPort -Workspace 'tenant-a'", script);
        Assert.Contains("Wait-ForBranding -ApiPort $apiPort -Workspace 'tenant-b'", script);
        Assert.Contains("throw \"Acceptance branding readiness failed", script);
    }

    private static string LoadScript(string repositoryRoot) =>
        File.ReadAllText(Path.Combine(repositoryRoot, "tools", "Start-Phase3BBrowserAcceptance.ps1"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HRMS.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
