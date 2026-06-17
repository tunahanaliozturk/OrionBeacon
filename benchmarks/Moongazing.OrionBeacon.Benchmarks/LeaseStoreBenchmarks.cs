namespace Moongazing.OrionBeacon.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// Measures the core lease-store critical section: the locked acquire / renew / denied paths of
/// <see cref="InMemoryLeaseStore.TryAcquireOrRenewAsync"/>, plus the release path. These are the
/// hot operations every elector cycle runs against the store, and they are fully in-memory.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class LeaseStoreBenchmarks
{
    private const string Resource = "orion-leader";
    private const string Holder = "candidate-a";
    private const string Rival = "candidate-b";

    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(15);

    private InMemoryLeaseStore renewStore = null!;
    private InMemoryLeaseStore deniedStore = null!;

    [GlobalSetup]
    public void Setup()
    {
        // A store where Holder already owns an unexpired lease: drives the renew path.
        renewStore = new InMemoryLeaseStore();
        renewStore.TryAcquireOrRenewAsync(Resource, Holder, Duration).GetAwaiter().GetResult();

        // A store where Holder owns the lease so a rival candidate is denied.
        deniedStore = new InMemoryLeaseStore();
        deniedStore.TryAcquireOrRenewAsync(Resource, Holder, Duration).GetAwaiter().GetResult();
    }

    /// <summary>Acquire a free lease on a fresh store (cold first-term acquisition).</summary>
    [Benchmark(Baseline = true)]
    public LeaseAcquisition Acquire()
    {
        var store = new InMemoryLeaseStore();
        return store.TryAcquireOrRenewAsync(Resource, Holder, Duration).GetAwaiter().GetResult();
    }

    /// <summary>Renew a lease this candidate already holds (the steady-state leader path).</summary>
    [Benchmark]
    public LeaseAcquisition Renew() =>
        renewStore.TryAcquireOrRenewAsync(Resource, Holder, Duration).GetAwaiter().GetResult();

    /// <summary>A rival candidate is denied because the holder's lease is unexpired (follower path).</summary>
    [Benchmark]
    public LeaseAcquisition Denied() =>
        deniedStore.TryAcquireOrRenewAsync(Resource, Rival, Duration).GetAwaiter().GetResult();
}
