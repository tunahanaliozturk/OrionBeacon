namespace Moongazing.OrionBeacon.Leasing;

/// <summary>The result of trying to acquire or renew a lease.</summary>
public enum LeaseOutcome
{
    /// <summary>The lease was free (or expired) and is now held by this candidate; a new term began.</summary>
    Acquired,

    /// <summary>This candidate already held the lease and its term was extended.</summary>
    Renewed,

    /// <summary>Another candidate holds an unexpired lease; this candidate is a follower.</summary>
    Denied,
}

/// <summary>
/// The outcome of <see cref="ILeaseStore.TryAcquireOrRenewAsync"/>: the result plus the lease the
/// caller now holds (on acquire/renew) or the id of the candidate that holds it (on denial).
/// </summary>
public sealed class LeaseAcquisition
{
    private LeaseAcquisition(LeaseOutcome outcome, Lease? lease, string? holderId)
    {
        Outcome = outcome;
        Lease = lease;
        HolderId = holderId;
    }

    /// <summary>What happened.</summary>
    public LeaseOutcome Outcome { get; }

    /// <summary>
    /// The lease held by the caller, present when <see cref="Outcome"/> is
    /// <see cref="LeaseOutcome.Acquired"/> or <see cref="LeaseOutcome.Renewed"/>.
    /// </summary>
    public Lease? Lease { get; }

    /// <summary>
    /// The id of the candidate currently holding the lease, present when <see cref="Outcome"/> is
    /// <see cref="LeaseOutcome.Denied"/>.
    /// </summary>
    public string? HolderId { get; }

    /// <summary>True when the caller now holds the lease.</summary>
    public bool IsHeld => Outcome is LeaseOutcome.Acquired or LeaseOutcome.Renewed;

    /// <summary>
    /// The caller took a free or expired lease and a new leadership term began. An
    /// <see cref="ILeaseStore"/> implementation returns this when it grants the lease to a candidate
    /// that did not previously hold it, carrying the new <paramref name="lease"/> and its freshly
    /// advanced fencing token.
    /// </summary>
    /// <param name="lease">The lease the caller now holds.</param>
    public static LeaseAcquisition Acquired(Lease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return new(LeaseOutcome.Acquired, lease, lease.HolderId);
    }

    /// <summary>
    /// The caller already held the lease and its term was extended. An <see cref="ILeaseStore"/>
    /// implementation returns this when the current holder renews; the fencing token on
    /// <paramref name="lease"/> is unchanged from the term it already held.
    /// </summary>
    /// <param name="lease">The renewed lease, carrying the same fencing token as the held term.</param>
    public static LeaseAcquisition Renewed(Lease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return new(LeaseOutcome.Renewed, lease, lease.HolderId);
    }

    /// <summary>
    /// A different candidate holds an unexpired lease, so the caller is a follower. An
    /// <see cref="ILeaseStore"/> implementation returns this when it cannot grant the lease,
    /// reporting the current <paramref name="holderId"/>.
    /// </summary>
    /// <param name="holderId">The candidate that currently holds the lease.</param>
    public static LeaseAcquisition Denied(string holderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holderId);
        return new(LeaseOutcome.Denied, null, holderId);
    }
}
