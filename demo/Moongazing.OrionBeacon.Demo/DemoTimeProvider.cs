namespace Moongazing.OrionBeacon.Demo;

/// <summary>
/// A hand-driven <see cref="TimeProvider"/> so lease expiry can be advanced without real delays.
/// Feeds the public <see cref="Leasing.InMemoryLeaseStore(TimeProvider)"/> constructor used by the
/// failover scenario. Kept demo-local to avoid taking a dependency on
/// <c>Microsoft.Extensions.TimeProvider.Testing</c>.
/// </summary>
internal sealed class DemoTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset utcNow = start;

    public override DateTimeOffset GetUtcNow() => utcNow;

    /// <summary>Move the clock forward, lapsing any lease whose expiry the jump crosses.</summary>
    public void Advance(TimeSpan by) => utcNow += by;
}
