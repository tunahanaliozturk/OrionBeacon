namespace Moongazing.OrionBeacon.Election;

using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Leasing;
using Moongazing.OrionBeacon.Observers;

/// <summary>
/// Default <see cref="ILeaderElector"/>. Each cycle it asks the lease store to acquire or renew
/// the lease and reconciles the result into <see cref="IsLeader"/>, firing an election when it
/// gains the lease and a deposition when it loses it. Transitions are reported through the
/// diagnostics meter and a fault-safe observer.
/// </summary>
public sealed class LeaderElector : ILeaderElector
{
    private readonly ILeaseStore store;
    private readonly LeaderElectionOptions options;
    private readonly LeaderElectionDiagnostics diagnostics;
    private readonly ILeadershipObserver observer;
    private readonly object gate = new();

    /// <summary>Create an elector.</summary>
    /// <param name="store">The shared lease store.</param>
    /// <param name="options">Election options. Validated on construction.</param>
    /// <param name="diagnostics">The shared metrics instance.</param>
    /// <param name="observer">The leadership observer, or null for none.</param>
    public LeaderElector(
        ILeaseStore store,
        LeaderElectionOptions options,
        LeaderElectionDiagnostics diagnostics,
        ILeadershipObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);
        options.Validate();

        this.store = store;
        this.options = options;
        this.diagnostics = diagnostics;
        this.observer = observer ?? NullLeadershipObserver.Instance;
    }

    /// <inheritdoc />
    public bool IsLeader { get; private set; }

    /// <inheritdoc />
    public Lease? Lease { get; private set; }

    /// <inheritdoc />
    public async Task<bool> TryElectAsync(CancellationToken cancellationToken = default)
    {
        var acquisition = await store
            .TryAcquireOrRenewAsync(options.ResourceName, options.CandidateId, options.LeaseDuration, cancellationToken)
            .ConfigureAwait(false);

        diagnostics.RecordAttempt(acquisition.Outcome switch
        {
            LeaseOutcome.Acquired => "acquired",
            LeaseOutcome.Renewed => "renewed",
            _ => "denied",
        });

        Apply(acquisition.IsHeld, acquisition.Lease);
        return IsLeader;
    }

    /// <inheritdoc />
    public async Task ResignAsync(CancellationToken cancellationToken = default)
    {
        await store.ReleaseAsync(options.ResourceName, options.CandidateId, cancellationToken).ConfigureAwait(false);
        Apply(isHeld: false, lease: null);
    }

    private void Apply(bool isHeld, Lease? lease)
    {
        bool elected = false;
        bool deposed = false;
        Lease? electedLease = null;

        lock (gate)
        {
            if (isHeld && !IsLeader)
            {
                elected = true;
                electedLease = lease;
            }
            else if (!isHeld && IsLeader)
            {
                deposed = true;
            }

            IsLeader = isHeld;
            Lease = isHeld ? lease : null;
        }

        if (elected)
        {
            diagnostics.RecordTransition(elected: true);
            SafeObserve(() => observer.OnElected(electedLease!));
        }
        else if (deposed)
        {
            diagnostics.RecordTransition(elected: false);
            SafeObserve(() => observer.OnDeposed(options.ResourceName));
        }
    }

    private static void SafeObserve(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // observer is observability, not load-bearing
        catch (Exception)
#pragma warning restore CA1031
        {
            // An observer fault must never disrupt election.
        }
    }
}
