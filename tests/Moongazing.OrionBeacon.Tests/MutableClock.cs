namespace Moongazing.OrionBeacon.Tests;

/// <summary>A hand-driven clock so lease timing can be tested without real delays.</summary>
internal sealed class MutableClock(DateTimeOffset start)
{
    public DateTimeOffset Now { get; private set; } = start;

    public void Advance(TimeSpan by) => Now += by;
}
