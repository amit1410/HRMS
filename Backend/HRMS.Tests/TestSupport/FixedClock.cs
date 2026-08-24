namespace HRMS.Tests.TestSupport;

/// <summary>
/// A stopped clock. Anything that stamps a timestamp — an export filename, an expiry — can then be asserted
/// exactly instead of by pattern, and a test never races the real second boundary.
/// </summary>
public sealed class FixedClock : TimeProvider
{
    public FixedClock(DateTimeOffset now)
    {
        Now = now;
    }

    /// <summary>The instant every read returns. Assignable, so a test can advance it deliberately.</summary>
    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}
