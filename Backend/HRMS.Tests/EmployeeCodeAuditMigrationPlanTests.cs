using Xunit;

public sealed class EmployeeCodeAuditMigrationPlanTests
{
    [Fact]
    public void Plan_selection_accepts_audit_plan_only_for_tenant_only_with_its_exact_migration()
    {
        EmployeeImportMigrationGuard.ValidatePlanSelection(true, EmployeeCodeAuditMigrationPlan.Name,
            [EmployeeCodeAuditMigrationPlan.MigrationId]);
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidatePlanSelection(
            false, EmployeeCodeAuditMigrationPlan.Name, [EmployeeCodeAuditMigrationPlan.MigrationId]));
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidatePlanSelection(
            true, EmployeeCodeAuditMigrationPlan.Name, [EmployeeCodeAuditMigrationPlan.MigrationId, "unrelated"]));
    }

    [Fact]
    public void Plan_selection_rejects_unknown_tenant_plan()
    {
        var error = Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidatePlanSelection(
            true, "unknown-plan", []));
        Assert.Contains("Unsupported tenant migration plan 'unknown-plan'", error.Message);
    }

    [Fact]
    public void Accepts_only_the_explicit_WEB1_identity_and_database()
    {
        EmployeeCodeAuditMigrationGuard.ValidateTarget(EmployeeCodeAuditMigrationPlan.TenantId, "hrms_catalog", "WEB1", "web1", "hrms_web1", "MySql");
        Assert.Throws<InvalidOperationException>(() => EmployeeCodeAuditMigrationGuard.ValidateTarget(Guid.NewGuid(), "hrms_catalog", "WEB1", "web1", "hrms_web1", "MySql"));
        Assert.Throws<InvalidOperationException>(() => EmployeeCodeAuditMigrationGuard.ValidateTarget(EmployeeCodeAuditMigrationPlan.TenantId, "hrms_catalog", "WEB1", "web1", "other_db", "MySql"));
    }

    [Fact]
    public void Requires_the_complete_exact_pending_migration_set()
    {
        EmployeeCodeAuditMigrationGuard.ValidatePending([EmployeeCodeAuditMigrationPlan.MigrationId]);
        Assert.Throws<InvalidOperationException>(() => EmployeeCodeAuditMigrationGuard.ValidatePending([]));
        Assert.Throws<InvalidOperationException>(() => EmployeeCodeAuditMigrationGuard.ValidatePending([EmployeeCodeAuditMigrationPlan.MigrationId, "unrelated"]));
        Assert.Throws<InvalidOperationException>(() => EmployeeCodeAuditMigrationGuard.ValidatePendingOrApplied([], []));
    }

    [Fact]
    public void Approval_gate_requires_each_evidence_flag_and_allowlisted_approval()
    {
        EmployeeCodeAuditMigrationGuard.ValidateApproval(true, true, true, true, "APP-1", true, "backup-1", "restore-1");
        Assert.Throws<InvalidOperationException>(() => EmployeeCodeAuditMigrationGuard.ValidateApproval(false, true, true, true, "APP-1", true, "backup-1", "restore-1"));
        Assert.Throws<InvalidOperationException>(() => EmployeeCodeAuditMigrationGuard.ValidateApproval(true, true, true, true, null, false, "backup-1", "restore-1"));
        Assert.Throws<InvalidOperationException>(() => EmployeeCodeAuditMigrationGuard.ValidateApproval(true, true, true, true, "APP-1", false, "backup-1", "restore-1"));
    }

    [Fact]
    public void Existing_audit_table_fails_closed()
    {
        EmployeeCodeAuditMigrationGuard.ValidateAuditTableAbsent(false);
        Assert.Throws<InvalidOperationException>(() => EmployeeCodeAuditMigrationGuard.ValidateAuditTableAbsent(true));
    }

    [Fact]
    public async Task Repeat_execution_is_a_noop_after_approved_migration_is_applied()
    {
        Assert.False(EmployeeCodeAuditMigrationGuard.ValidatePendingOrApplied([], [EmployeeCodeAuditMigrationPlan.MigrationId]));
        var inspections = 0;
        var invocations = 0;
        await EmployeeCodeAuditMigrationWorkflow.ExecuteAsync(
            _ =>
            {
                inspections++;
                return Task.FromResult<(IReadOnlyList<string>, bool)>( ([], false) );
            },
            _ => Task.FromResult<IAsyncDisposable>(new NoopLock()),
            (state, _) =>
            {
                if (state.ShouldExecute) invocations++;
                return Task.CompletedTask;
            }, CancellationToken.None);
        Assert.Equal(2, inspections);
        Assert.Equal(0, invocations);
    }

    private sealed class NoopLock : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
