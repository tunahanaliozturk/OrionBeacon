namespace Moongazing.OrionBeacon.Tests;

using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;
using Moongazing.OrionBeacon.Observers;

using Xunit;

/// <summary>
/// Constructor guards, transition invariants, fencing continuity, and error/cancellation paths for
/// <see cref="LeaderElector"/> beyond the core suite in <c>LeaderElectorTests</c>.
/// </summary>
public sealed class LeaderElectorEdgeTests
{
    private static LeaderElectionOptions Options() => new()
    {
        ResourceName = "res",
        CandidateId = "a",
        LeaseDuration = TimeSpan.FromSeconds(15),
        RenewInterval = TimeSpan.FromSeconds(5),
    };

    [Fact]
    public void Constructor_rejects_a_null_store()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        Assert.Throws<ArgumentNullException>(() =>
            new LeaderElector(null!, Options(), diagnostics));
    }

    [Fact]
    public void Constructor_rejects_null_options()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        Assert.Throws<ArgumentNullException>(() =>
            new LeaderElector(new InMemoryLeaseStore(), null!, diagnostics));
    }

    [Fact]
    public void Constructor_rejects_null_diagnostics()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LeaderElector(new InMemoryLeaseStore(), Options(), null!));
    }

    [Fact]
    public void Constructor_validates_options()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        var bad = Options();
        bad.RenewInterval = bad.LeaseDuration; // not shorter than lease

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LeaderElector(new InMemoryLeaseStore(), bad, diagnostics));
    }

    [Fact]
    public async Task A_null_observer_is_tolerated_and_election_still_succeeds()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        var elector = new LeaderElector(new InMemoryLeaseStore(), Options(), diagnostics, observer: null);

        var isLeader = await elector.TryElectAsync();

        Assert.True(isLeader);
        Assert.NotNull(elector.Lease);
    }

    [Fact]
    public async Task Before_the_first_cycle_the_elector_is_a_follower_with_no_lease()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        var elector = new LeaderElector(new InMemoryLeaseStore(), Options(), diagnostics);

        Assert.False(elector.IsLeader);
        Assert.Null(elector.Lease);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Resigning_while_a_follower_does_not_fire_a_spurious_deposition()
    {
        var clock = new MutableClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var store = new InMemoryLeaseStore(() => clock.Now);
        using var diagnostics = new LeaderElectionDiagnostics();
        var observer = new CountingObserver();
        var elector = new LeaderElector(store, Options(), diagnostics, observer);

        // Never became leader: a rival holds the lease.
        await store.TryAcquireOrRenewAsync("res", "b", TimeSpan.FromSeconds(15));
        await elector.TryElectAsync();
        Assert.False(elector.IsLeader);

        await elector.ResignAsync();

        Assert.Equal(0, observer.Elected);
        Assert.Equal(0, observer.Deposed);
    }

    [Fact]
    public async Task Resigning_when_never_leader_at_all_is_a_clean_noop()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        var observer = new CountingObserver();
        var elector = new LeaderElector(new InMemoryLeaseStore(), Options(), diagnostics, observer);

        await elector.ResignAsync();

        Assert.False(elector.IsLeader);
        Assert.Null(elector.Lease);
        Assert.Equal(0, observer.Deposed);
    }

    [Fact]
    public async Task Regaining_leadership_after_a_loss_fires_a_second_election_with_a_higher_token()
    {
        var clock = new MutableClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var store = new InMemoryLeaseStore(() => clock.Now);
        using var diagnostics = new LeaderElectionDiagnostics();
        var observer = new CountingObserver();
        var elector = new LeaderElector(store, Options(), diagnostics, observer);

        // Win term 1.
        await elector.TryElectAsync();
        var firstToken = elector.Lease!.FencingToken;

        // Lose it: lapse, rival grabs it, our cycle observes the loss.
        clock.Advance(TimeSpan.FromSeconds(16));
        await store.TryAcquireOrRenewAsync("res", "b", TimeSpan.FromSeconds(15));
        await elector.TryElectAsync();
        Assert.False(elector.IsLeader);

        // Rival's lease lapses; we win again on the next cycle.
        clock.Advance(TimeSpan.FromSeconds(16));
        var isLeader = await elector.TryElectAsync();

        Assert.True(isLeader);
        Assert.Equal(2, observer.Elected);
        Assert.Equal(1, observer.Deposed);
        Assert.True(elector.Lease!.FencingToken > firstToken);
    }

    [Fact]
    public async Task The_held_lease_carries_the_configured_resource_and_candidate()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        var options = Options();
        var elector = new LeaderElector(new InMemoryLeaseStore(), options, diagnostics);

        await elector.TryElectAsync();

        Assert.Equal(options.ResourceName, elector.Lease!.Resource);
        Assert.Equal(options.CandidateId, elector.Lease.HolderId);
    }

    [Fact]
    public async Task A_store_fault_propagates_out_of_TryElectAsync_and_leaves_state_unchanged()
    {
        // The elector itself does not swallow store faults; the hosted loop does. So a throwing store
        // surfaces here, and the elector must not have flipped to leader.
        using var diagnostics = new LeaderElectionDiagnostics();
        var store = new FaultyLeaseStore { Throw = true };
        var elector = new LeaderElector(store, Options(), diagnostics);

        await Assert.ThrowsAsync<InvalidOperationException>(() => elector.TryElectAsync());

        Assert.False(elector.IsLeader);
        Assert.Null(elector.Lease);
    }

    [Fact]
    public async Task A_cancelled_token_passed_to_a_cancellation_aware_store_propagates()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        var store = new FaultyLeaseStore { ObserveCancellation = true };
        var elector = new LeaderElector(store, Options(), diagnostics);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => elector.TryElectAsync(cts.Token));
    }

    [Fact]
    public async Task A_resign_store_fault_propagates_out_of_ResignAsync()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        var store = new FaultyLeaseStore();
        var elector = new LeaderElector(store, Options(), diagnostics);
        await elector.TryElectAsync();

        store.Throw = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => elector.ResignAsync());
    }

    private sealed class CountingObserver : ILeadershipObserver
    {
        public int Elected { get; private set; }
        public int Deposed { get; private set; }

        public void OnElected(Lease lease) => Elected++;
        public void OnDeposed(string resource) => Deposed++;
    }

    /// <summary>A store that can throw or observe cancellation on demand; otherwise delegates to a real store.</summary>
    private sealed class FaultyLeaseStore : ILeaseStore
    {
        private readonly InMemoryLeaseStore inner = new();

        public bool Throw { get; set; }

        public bool ObserveCancellation { get; set; }

        public Task<LeaseAcquisition> TryAcquireOrRenewAsync(
            string resource, string candidateId, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            if (ObserveCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (Throw)
            {
                throw new InvalidOperationException("store boom");
            }
            return inner.TryAcquireOrRenewAsync(resource, candidateId, duration, cancellationToken);
        }

        public Task ReleaseAsync(string resource, string candidateId, CancellationToken cancellationToken = default)
        {
            if (ObserveCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (Throw)
            {
                throw new InvalidOperationException("store boom");
            }
            return inner.ReleaseAsync(resource, candidateId, cancellationToken);
        }
    }
}
