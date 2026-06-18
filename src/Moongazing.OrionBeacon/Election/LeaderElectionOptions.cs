namespace Moongazing.OrionBeacon.Election;

/// <summary>
/// Configuration for an elector: which resource is contended, this candidate's identity, and the
/// lease and renewal timing.
/// </summary>
public sealed class LeaderElectionOptions
{
    /// <summary>
    /// The name of the contended resource. All candidates competing for the same leadership must
    /// use the same value. Default <c>orion-leader</c>.
    /// </summary>
    public string ResourceName { get; set; } = "orion-leader";

    /// <summary>
    /// This candidate's stable identity, unique per instance. Default is the machine name plus a
    /// per-process suffix.
    /// </summary>
    public string CandidateId { get; set; } = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    /// <summary>
    /// How long a lease is granted for. If the leader fails to renew within this window, the lease
    /// lapses and a follower can take over. Default 15 seconds.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How often the leader renews its lease and a follower retries acquisition. Must be shorter
    /// than <see cref="LeaseDuration"/> so renewals comfortably beat expiry. Default 5 seconds.
    /// </summary>
    public TimeSpan RenewInterval { get; set; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ResourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(CandidateId);
        if (LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration), LeaseDuration, "LeaseDuration must be positive.");
        }
        if (RenewInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RenewInterval), RenewInterval, "RenewInterval must be positive.");
        }
        if (RenewInterval >= LeaseDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(RenewInterval), RenewInterval,
                "RenewInterval must be shorter than LeaseDuration so renewals beat expiry.");
        }
    }
}
