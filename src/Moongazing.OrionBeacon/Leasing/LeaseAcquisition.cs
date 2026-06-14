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

    internal static LeaseAcquisition Acquired(Lease lease) => new(LeaseOutcome.Acquired, lease, lease.HolderId);

    internal static LeaseAcquisition Renewed(Lease lease) => new(LeaseOutcome.Renewed, lease, lease.HolderId);

    internal static LeaseAcquisition Denied(string holderId) => new(LeaseOutcome.Denied, null, holderId);
}
