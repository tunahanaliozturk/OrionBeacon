namespace Moongazing.OrionBeacon.Tests;

using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;
using Moongazing.OrionBeacon.Observers;

using Xunit;

public sealed class LeaderElectorTests
{
    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Store = new InMemoryLeaseStore(() => Clock);
            Options = new LeaderElectionOptions
            {
                ResourceName = "res",
                CandidateId = "a",
                LeaseDuration = TimeSpan.FromSeconds(15),
                RenewInterval = TimeSpan.FromSeconds(5),
            };
            Elector = new LeaderElector(Store, Options, Diagnostics, Observer);
        }

        public DateTimeOffset Clock { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        public InMemoryLeaseStore Store { get; }

        public LeaderElectionOptions Options { get; }

        public LeaderElectionDiagnostics Diagnostics { get; } = new();

        public RecordingObserver Observer { get; } = new();

        public LeaderElector Elector { get; }

        public void Dispose() => Diagnostics.Dispose();
    }

    [Fact]
    public async Task First_cycle_wins_leadership_and_fires_one_election()
    {
        using var f = new Fixture();

        var isLeader = await f.Elector.TryElectAsync();

        Assert.True(isLeader);
        Assert.True(f.Elector.IsLeader);
        Assert.Equal(1, f.Elector.Lease!.FencingToken);
        Assert.Equal(1, f.Observer.Elected);
        Assert.Equal(0, f.Observer.Deposed);
    }

    [Fact]
    public async Task Renewing_does_not_re_fire_the_election()
    {
        using var f = new Fixture();
        await f.Elector.TryElectAsync();

        f.Clock = f.Clock.AddSeconds(5);
        await f.Elector.TryElectAsync();

        Assert.True(f.Elector.IsLeader);
        Assert.Equal(1, f.Observer.Elected);
    }

    [Fact]
    public async Task Losing_the_lease_to_another_holder_deposes_the_leader()
    {
        using var f = new Fixture();
        await f.Elector.TryElectAsync();

        // The lease lapses and another candidate grabs it before our next cycle.
        f.Clock = f.Clock.AddSeconds(16);
        await f.Store.TryAcquireOrRenewAsync("res", "b", TimeSpan.FromSeconds(15));

        var isLeader = await f.Elector.TryElectAsync();

        Assert.False(isLeader);
        Assert.False(f.Elector.IsLeader);
        Assert.Null(f.Elector.Lease);
        Assert.Equal(1, f.Observer.Deposed);
    }

    [Fact]
    public async Task Resigning_releases_the_lease_and_deposes()
    {
        using var f = new Fixture();
        await f.Elector.TryElectAsync();

        await f.Elector.ResignAsync();

        Assert.False(f.Elector.IsLeader);
        Assert.Equal(1, f.Observer.Deposed);

        // A follower can take over immediately.
        var takeover = await f.Store.TryAcquireOrRenewAsync("res", "b", TimeSpan.FromSeconds(15));
        Assert.Equal(LeaseOutcome.Acquired, takeover.Outcome);
    }

    [Fact]
    public async Task A_follower_never_gains_leadership_while_another_holds()
    {
        using var f = new Fixture();
        // Another candidate already holds the lease.
        await f.Store.TryAcquireOrRenewAsync("res", "b", TimeSpan.FromSeconds(15));

        var isLeader = await f.Elector.TryElectAsync();

        Assert.False(isLeader);
        Assert.Equal(0, f.Observer.Elected);
    }

    [Fact]
    public async Task A_faulting_observer_does_not_break_election()
    {
        using var f = new Fixture();
        var elector = new LeaderElector(f.Store, f.Options, f.Diagnostics, new ThrowingObserver());

        var isLeader = await elector.TryElectAsync();

        Assert.True(isLeader);
    }

    private sealed class RecordingObserver : ILeadershipObserver
    {
        public int Elected { get; private set; }
        public int Deposed { get; private set; }

        public void OnElected(Lease lease) => Elected++;
        public void OnDeposed(string resource) => Deposed++;
    }

    private sealed class ThrowingObserver : ILeadershipObserver
    {
        public void OnElected(Lease lease) => throw new InvalidOperationException("observer boom");
        public void OnDeposed(string resource) => throw new InvalidOperationException("observer boom");
    }
}
