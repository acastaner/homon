namespace Homon.Api.Tests;

/// <summary>
/// A <see cref="TimeProvider"/> whose "now" is set by the test, not the wall clock. Governs
/// every scheduler/retention test's notion of time — advancing it is how a test makes a probe
/// "due" or an observation "old" without sleeping. Reused by plan 009.
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset now) => _now = now;
}
