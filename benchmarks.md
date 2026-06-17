# Benchmarks

Microbenchmarks for OrionBeacon's pure, in-memory election hot paths, built with
[BenchmarkDotNet](https://benchmarkdotnet.org/). Every benchmark exercises the real public API and
touches no database, network, or external service. The default `InMemoryLeaseStore` is the store
under test, which is exactly the process-local path the library ships and tests against.

The suite is intentionally scoped to the parts of election that run on every cycle: the lease-store
critical section, a full elector cycle, and the small value objects allocated along the way. The
hosted background loop, real timers, and any clustered `ILeaseStore` implementation are out of scope
because they depend on wall-clock time or external infrastructure.

## Benchmark classes

### `LeaseStoreBenchmarks`

The `InMemoryLeaseStore.TryAcquireOrRenewAsync` critical section, which is a lock around a dictionary
lookup plus a `Lease` allocation.

- `Acquire` (baseline): acquire a free lease on a fresh store (first-term acquisition).
- `Renew`: renew a lease the candidate already holds (the steady-state leader path).
- `Denied`: a rival candidate is denied because the holder's lease is still unexpired (follower path).

### `ElectorBenchmarks`

A full `LeaderElector.TryElectAsync` cycle: the store call, the diagnostics attempt record, and the
locked `IsLeader` reconciliation. This is the unit the hosted loop runs on every renew interval.

- `ElectAsLeader` (baseline): one steady-state renew cycle for the current leader.
- `ElectAsFollower`: one cycle for a candidate denied because another instance holds the lease.

### `LeaseModelBenchmarks`

The small value objects on the hot path.

- `ConstructLease`: build a `Lease`, including its non-empty argument validation (allocated on every
  acquire and renew).
- `ValidateOptions`: construct an elector, which validates `LeaderElectionOptions` once (the
  per-elector startup check).

## Running

Run the whole suite (from the repository root):

```
dotnet run -c Release --project benchmarks/Moongazing.OrionBeacon.Benchmarks
```

Filter to one class or benchmark with the BenchmarkDotNet glob filter:

```
dotnet run -c Release --project benchmarks/Moongazing.OrionBeacon.Benchmarks -- --filter "*LeaseStoreBenchmarks*"
dotnet run -c Release --project benchmarks/Moongazing.OrionBeacon.Benchmarks -- --filter "*Renew*"
```

List every benchmark without running it:

```
dotnet run -c Release --project benchmarks/Moongazing.OrionBeacon.Benchmarks -- --list flat
```

Each class carries `[MemoryDiagnoser]` and runs on both the .NET 8 and .NET 9 runtimes
(`[SimpleJob(RuntimeMoniker.Net80)]` and `[SimpleJob(RuntimeMoniker.Net90)]`), so you need both SDKs
installed to run the full matrix. BenchmarkDotNet prints a results table (mean, error, standard
deviation, and allocations per operation) to the console and writes detailed reports under
`BenchmarkDotNet.Artifacts/`.

No measured numbers are committed here on purpose: results are hardware- and runtime-specific, so run
the suite on your own machine for figures that mean anything.
