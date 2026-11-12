using Xunit;

public sealed class EmployeeImportRecoveryMigrationPlanTests
{
    [Fact]
    public void Recovery_audit_plan_is_tenant_only_and_has_one_exact_migration()
    {
        EmployeeImportMigrationGuard.ValidatePlanSelection(true, EmployeeImportRecoveryAuditMigrationPlan.Name,
            [EmployeeImportRecoveryAuditMigrationPlan.MigrationId]);
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidatePlanSelection(
            false, EmployeeImportRecoveryAuditMigrationPlan.Name, [EmployeeImportRecoveryAuditMigrationPlan.MigrationId]));
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidatePlanSelection(
            true, EmployeeImportRecoveryAuditMigrationPlan.Name, [EmployeeImportRecoveryAuditMigrationPlan.MigrationId, "other"]));
    }

    [Fact]
    public void Recovery_audit_migration_is_bound_to_the_exact_WEB1_target()
    {
        EmployeeImportRecoveryAuditMigrationGuard.ValidateTarget(EmployeeImportRecoveryAuditMigrationPlan.TenantId,
            "hrms_catalog", "WEB1", "web1", "hrms_web1", "MySql");
        Assert.Throws<InvalidOperationException>(() => EmployeeImportRecoveryAuditMigrationGuard.ValidateTarget(
            Guid.NewGuid(), "hrms_catalog", "WEB1", "web1", "hrms_web1", "MySql"));
    }

    [Fact]
    public void Audit_table_presence_is_a_hard_migration_guard()
    {
        EmployeeImportRecoveryAuditMigrationGuard.ValidateAuditTableAbsent(false);
        Assert.Throws<InvalidOperationException>(() => EmployeeImportRecoveryAuditMigrationGuard.ValidateAuditTableAbsent(true));
    }
    [Fact]
    public void Pending_migration_set_and_approval_evidence_are_exact_and_repeat_safe()
    {
        var migration = EmployeeImportRecoveryAuditMigrationPlan.MigrationId;
        EmployeeImportRecoveryAuditMigrationGuard.ValidatePending([migration]);
        Assert.False(EmployeeImportRecoveryAuditMigrationGuard.ValidatePendingOrApplied([], [migration]));
        Assert.Throws<InvalidOperationException>(() => EmployeeImportRecoveryAuditMigrationGuard.ValidatePending([migration, "other"]));
        EmployeeImportRecoveryAuditMigrationGuard.ValidateApproval(true, true, true, true, true, true,
            "APP-42", true, "backup-ref", "restore-ref");
        Assert.Throws<InvalidOperationException>(() => EmployeeImportRecoveryAuditMigrationGuard.ValidateApproval(
            true, false, true, true, true, true, "APP-42", true, "backup-ref", "restore-ref"));
    }
}
