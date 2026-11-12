using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

namespace HRMS.Tests;

public sealed class EmployeeBulkImportPreflightConsoleDiagnosticsTests
{
    [Fact]
    public void Empty_approval_is_rejected_as_missing()
    {
        Assert.Equal("invocation.approval_missing", EmployeeBulkImportPreflightDiagnostics.ApprovalError(null));
        Assert.Equal("invocation.approval_missing", EmployeeBulkImportPreflightDiagnostics.ApprovalError("  "));
    }

    [Fact]
    public void Incorrect_approval_is_rejected_without_echoing_input()
    {
        const string privateValue = "private-user-input";
        var category = EmployeeBulkImportPreflightDiagnostics.ApprovalError(privateValue);

        Assert.Equal("invocation.approval_invalid", category);
        Assert.DoesNotContain(privateValue, category, StringComparison.Ordinal);
    }

    [Fact]
    public void Redirected_stdin_is_rejected_before_console_reads()
    {
        Assert.Equal("invocation.redirected_input_unsupported",
            EmployeeBulkImportPreflightDiagnostics.InteractiveInputError(inputRedirected: true));
        Assert.Null(EmployeeBulkImportPreflightDiagnostics.InteractiveInputError(inputRedirected: false));
    }

    [Fact]
    public void Missing_jwt_is_distinct_from_invalid_jwt()
    {
        Assert.Equal("authentication.missing_jwt", EmployeeBulkImportPreflightDiagnostics.JwtInputError(null));
        Assert.Equal("authentication.invalid_jwt", EmployeeBulkImportPreflightDiagnostics.JwtAuthenticationError(authenticated: false));
        Assert.Null(EmployeeBulkImportPreflightDiagnostics.JwtAuthenticationError(authenticated: true));
    }

    [Fact]
    public void Missing_import_permission_has_its_own_category()
    {
        Assert.Equal("authorization.employee_import_missing", EmployeeBulkImportPreflightDiagnostics.MissingEmployeeImportPermission);
    }

    [Fact]
    public void Missing_configuration_has_fixed_sanitized_category()
    {
        var configuration = new ConfigurationBuilder().Build();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EmployeeImportStorageOptions.LoadAndValidate(configuration, environment: null));
        var category = EmployeeBulkImportPreflightDiagnostics.FailureCategory(EmployeeBulkImportPreflightStage.Configuration);

        Assert.Equal("configuration.invalid_or_missing", category);
        Assert.DoesNotContain(exception.Message, category, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EmployeeBulkImportPreflightStage.Catalog, "catalog.routing_failed")]
    [InlineData(EmployeeBulkImportPreflightStage.Operation, "operation.verification_failed")]
    [InlineData(EmployeeBulkImportPreflightStage.Artifact, "artifact.verification_failed")]
    [InlineData(EmployeeBulkImportPreflightStage.Validation, "validation.failed")]
    public void Failure_categories_are_stage_specific_and_fixed(EmployeeBulkImportPreflightStage stage, string expected)
    {
        Assert.Equal(expected, EmployeeBulkImportPreflightDiagnostics.FailureCategory(stage));
    }
}
