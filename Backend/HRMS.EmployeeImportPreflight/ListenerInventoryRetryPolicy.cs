namespace HRMS.EmployeeImportPreflight;

/// <summary>Limits read-only listener inventory retries to transient or inconclusive snapshots.</summary>
public static class ListenerInventoryRetryPolicy
{
    public const int MaximumAttempts = 3;
    public const int RetryDelayMilliseconds = 150;

    public static bool ShouldRetry(bool inventoryAvailable, IEnumerable<string> diagnostics, int attemptIndex)
    {
        if (attemptIndex < 0 || attemptIndex >= MaximumAttempts - 1) return false;
        if (!inventoryAvailable) return false;

        return diagnostics.Any(diagnostic => diagnostic is
            "listener.no_ports_found" or
            "listener.query_failed" or
            "listener.netstat_query_failed" or
            "listener.snapshot_inconsistent");
    }
}
