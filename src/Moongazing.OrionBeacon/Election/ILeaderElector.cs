namespace Moongazing.OrionBeacon.Election;

using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// Tracks whether this candidate currently holds leadership over its resource. Application code
/// gates leader-only work on <see cref="IsLeader"/> (or the current <see cref="Lease"/> and its
/// fencing token). The hosted election loop keeps the state current by renewing the lease.
/// </summary>
public interface ILeaderElector
{
    /// <summary>True while this candidate holds the lease.</summary>
    bool IsLeader { get; }

    /// <summary>
    /// The lease this candidate currently holds, or null when it is a follower. Use its
    /// <see cref="Leasing.Lease.FencingToken"/> to fence writes to downstream resources.
    /// </summary>
    Lease? Lease { get; }

    /// <summary>
    /// Run one acquire-or-renew cycle, updating <see cref="IsLeader"/> and firing leadership
    /// transitions. The hosted loop calls this on the renew interval; tests can call it directly.
    /// Returns the leadership state after the cycle.
    /// </summary>
    /// <param name="cancellationToken">Cancels the store operation.</param>
    Task<bool> TryElectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Release leadership if held, firing a deposition. Called on shutdown so a follower can take
    /// over promptly.
    /// </summary>
    /// <param name="cancellationToken">Cancels the store operation.</param>
    Task ResignAsync(CancellationToken cancellationToken = default);
}
