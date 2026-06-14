namespace Moongazing.OrionBeacon.Observers;

using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// Consumer hook notified when this candidate gains or loses leadership. Implementations are
/// observability only: they must not throw, and the elector swallows any fault they raise so an
/// observer outage never disrupts election. Register one via DI, or leave it unset for a no-op.
/// </summary>
public interface ILeadershipObserver
{
    /// <summary>Called when this candidate becomes the leader.</summary>
    /// <param name="lease">The lease just acquired, including its fencing token.</param>
    void OnElected(Lease lease);

    /// <summary>Called when this candidate stops being the leader.</summary>
    /// <param name="resource">The resource whose leadership was lost.</param>
    void OnDeposed(string resource);
}

/// <summary>A no-op observer used when the consumer registers none.</summary>
public sealed class NullLeadershipObserver : ILeadershipObserver
{
    /// <summary>The shared no-op instance.</summary>
    public static readonly NullLeadershipObserver Instance = new();

    private NullLeadershipObserver()
    {
    }

    /// <inheritdoc />
    public void OnElected(Lease lease)
    {
    }

    /// <inheritdoc />
    public void OnDeposed(string resource)
    {
    }
}
