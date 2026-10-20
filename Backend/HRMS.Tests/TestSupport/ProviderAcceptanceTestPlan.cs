namespace HRMS.Tests.TestSupport;

/// <summary>Executable-case inventory consumed by the gated provider acceptance runner.</summary>
public static class ProviderAcceptanceTestPlan
{
    public static IReadOnlyList<string> Cases { get; } =
    [
        "SQL Server migration-up",
        "SQL Server migration-down",
        "MySQL migration-up",
        "MySQL migration-down",
        "Welcome invitation schema and indexes",
        "Atomic worker claiming",
        "Concurrent worker processing",
        "Lease expiration and recovery",
        "Stale lease-owner rejection",
        "Tenant isolation",
        "Idempotency",
        "Retry scheduling",
        "Batch completion",
        "Activation-token behavior using fake email delivery",
        "Cleanup ownership and database absence"
    ];
}
