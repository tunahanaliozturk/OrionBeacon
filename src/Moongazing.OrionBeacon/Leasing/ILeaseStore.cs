namespace Moongazing.OrionBeacon.Leasing;

/// <summary>
/// The shared store that backs leader election. The default <see cref="InMemoryLeaseStore"/> is
/// process-local (a single node, or tests); implement this interface over Redis, a database, or
/// another store shared by all instances to elect a leader across a cluster. Implementations must
/// make <see cref="TryAcquireOrRenewAsync"/> atomic so two candidates cannot both acquire, and
/// must assign a strictly increasing fencing token on each new acquisition.
/// </summary>
public interface ILeaseStore
{
    /// <summary>
    /// Atomically acquire the lease if it is free or expired, renew it if this candidate already
    /// holds it, or report denial if another candidate holds an unexpired lease.
    /// </summary>
    /// <param name="resource">The contended resource name.</param>
    /// <param name="candidateId">This candidate's stable identity.</param>
    /// <param name="duration">How long the lease is granted or extended for.</param>
    /// <param name="cancellationToken">Cancels the store operation.</param>
    Task<LeaseAcquisition> TryAcquireOrRenewAsync(
        string resource, string candidateId, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Release the lease if this candidate holds it, so a follower can take over immediately
    /// rather than waiting for expiry. A no-op if the candidate does not hold it.
    /// </summary>
    /// <param name="resource">The contended resource name.</param>
    /// <param name="candidateId">This candidate's identity.</param>
    /// <param name="cancellationToken">Cancels the store operation.</param>
    Task ReleaseAsync(string resource, string candidateId, CancellationToken cancellationToken = default);
}
