namespace Moongazing.OrionBeacon.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// Measures the small value objects allocated on the election hot path: constructing a
/// <see cref="Lease"/> (built on every acquire and renew) and validating a
/// <see cref="LeaderElectionOptions"/> (run once per elector construction). Both are pure.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class LeaseModelBenchmarks
{
    private static readonly DateTimeOffset AcquiredAt = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset ExpiresAt = DateTimeOffset.UnixEpoch.AddSeconds(15);

    /// <summary>Construct a lease, including its non-empty argument validation.</summary>
    [Benchmark]
    public Lease ConstructLease() =>
        new("orion-leader", "candidate-a", 42L, AcquiredAt, ExpiresAt);

    /// <summary>Construct and validate election options (the per-elector startup check).</summary>
    [Benchmark]
    public LeaderElectionOptions ValidateOptions()
    {
        var options = new LeaderElectionOptions
        {
            ResourceName = "orion-leader",
            CandidateId = "candidate-a",
            LeaseDuration = TimeSpan.FromSeconds(15),
            RenewInterval = TimeSpan.FromSeconds(5),
        };

        // TryElectAsync's elector constructor runs Validate(); exercise the same public surface by
        // building an elector, which validates the options exactly once.
        _ = new LeaderElector(new InMemoryLeaseStore(), options, SharedDiagnostics);
        return options;
    }

    private static Moongazing.OrionBeacon.Diagnostics.LeaderElectionDiagnostics SharedDiagnostics { get; } = new();
}
