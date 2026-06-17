namespace Moongazing.OrionBeacon.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// Measures a full <see cref="LeaderElector.TryElectAsync"/> cycle over the in-memory store: the
/// store call, the diagnostics attempt record, and the locked state reconciliation. This is the
/// exact unit the hosted loop runs on every renew interval, so its per-cycle cost matters.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class ElectorBenchmarks
{
    private LeaderElectionDiagnostics diagnostics = null!;
    private LeaderElector renewElector = null!;
    private LeaderElector followerElector = null!;

    [GlobalSetup]
    public void Setup()
    {
        diagnostics = new LeaderElectionDiagnostics();

        // Leader elector: shares a store it already holds, so every cycle renews.
        var leaderStore = new InMemoryLeaseStore();
        var leaderOptions = new LeaderElectionOptions { CandidateId = "leader" };
        renewElector = new LeaderElector(leaderStore, leaderOptions, diagnostics);
        renewElector.TryElectAsync().GetAwaiter().GetResult();

        // Follower elector: another candidate holds the lease, so every cycle is denied.
        var contendedStore = new InMemoryLeaseStore();
        contendedStore
            .TryAcquireOrRenewAsync("orion-leader", "incumbent", TimeSpan.FromSeconds(30))
            .GetAwaiter().GetResult();
        var followerOptions = new LeaderElectionOptions { CandidateId = "follower" };
        followerElector = new LeaderElector(contendedStore, followerOptions, diagnostics);
    }

    [GlobalCleanup]
    public void Cleanup() => diagnostics.Dispose();

    /// <summary>One steady-state renew cycle for the current leader.</summary>
    [Benchmark(Baseline = true)]
    public bool ElectAsLeader() =>
        renewElector.TryElectAsync().GetAwaiter().GetResult();

    /// <summary>One cycle for a follower that is denied because another candidate holds the lease.</summary>
    [Benchmark]
    public bool ElectAsFollower() =>
        followerElector.TryElectAsync().GetAwaiter().GetResult();
}
