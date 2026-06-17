<p align="center">
  <img src="docs/logo.png" alt="OrionBeacon" width="150" />
</p>

# OrionBeacon

[![CI/CD](https://github.com/tunahanaliozturk/OrionBeacon/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/tunahanaliozturk/OrionBeacon/actions/workflows/ci-cd.yml)
[![NuGet](https://img.shields.io/nuget/v/OrionBeacon.svg)](https://www.nuget.org/packages/OrionBeacon/)

Leader election for .NET: run the same service on several instances and OrionBeacon keeps exactly one elected, so "only one node should do this" work runs once, not once per instance.

Part of the **Orion** family. Usable entirely on its own.

## Why

Scale a worker to three instances and your nightly job runs three times. The fix is leader election: candidates compete for a renewable lease in a shared store, the holder is the leader, and if it dies the lease lapses and another takes over. OrionBeacon implements that with fencing tokens (so a leader whose lease lapsed cannot keep writing as if it were still in charge) and a hosted loop that keeps the state current, leaving you a single `IsLeader` flag to check.

## Features

- **Renewable-lease election.** Candidates compete for a lease over a named resource; the holder is the leader. Miss a renewal and the lease lapses, so a healthy follower takes over.
- **Fencing tokens.** Every new leadership term gets a strictly increasing `long` token. Pass it to downstream resources to fence out a stale leader that resumed after a stop-the-world pause.
- **Hosted background loop.** `AddOrionBeacon` registers a `BackgroundService` that acquires and renews on the renew interval, survives a transient store fault, and resigns on shutdown so a follower is promoted promptly.
- **A single `IsLeader` flag.** Gate leader-only work on `ILeaderElector.IsLeader`, or read the current `Lease` for its fencing token.
- **Pluggable storage.** The default `InMemoryLeaseStore` elects within one process (single node or tests). Implement `ILeaseStore` over Redis, a database, or any shared store to elect across a cluster; the in-memory store is only added if none is registered.
- **OpenTelemetry built in.** A `Meter` named `Moongazing.OrionBeacon` exposes attempt and transition counters plus an `is_leader` gauge, with no extra dependency.
- **Leadership-change events.** Register an `ILeadershipObserver` to react to `OnElected` and `OnDeposed`. The observer is fault-safe: an exception it throws never disrupts election.
- **Testable by design.** The election state machine advances one cycle at a time, so it is fully testable without real timers or sleeps.
- **No heavy dependencies.** One reference, `Microsoft.Extensions.Hosting.Abstractions`. Multi-targets `net8.0`, `net9.0`, and `net10.0`.

## Install

```
dotnet add package OrionBeacon
```

## Quick start

Register OrionBeacon. The hosted loop starts acquiring and renewing the lease in the background.

```csharp
builder.Services.AddOrionBeacon(o =>
{
    o.ResourceName = "nightly-report";
    o.LeaseDuration = TimeSpan.FromSeconds(15);
    o.RenewInterval = TimeSpan.FromSeconds(5);   // must be shorter than the lease
});
```

Gate leader-only work on the elector:

```csharp
public sealed class NightlyReportJob(ILeaderElector elector)
{
    public async Task RunAsync(CancellationToken ct)
    {
        if (!elector.IsLeader)
        {
            return; // a different instance is the leader; do nothing here
        }

        long fence = elector.Lease!.FencingToken; // pass to downstream stores to reject stale writes
        await ProduceReportAsync(fence, ct);
    }
}
```

The hosted loop registered by `AddOrionBeacon` acquires and renews the lease in the background and resigns on shutdown, so a healthy follower is promoted promptly.

## Usage

### Check leadership

`ILeaderElector` is the surface application code touches. `IsLeader` is true while this candidate holds the lease, and `Lease` is the current lease (or `null` for a follower).

```csharp
public sealed class OutboxDrain(ILeaderElector elector)
{
    public bool ShouldRun => elector.IsLeader;

    public long? CurrentFence => elector.Lease?.FencingToken;
}
```

### Fence downstream writes

Each leadership term carries a strictly increasing fencing token. Pass it to any resource the leader writes to and have that resource reject anything carrying a lower token than the highest it has seen. This is what makes election safe under a stop-the-world pause: a leader that was frozen past its lease, then resumed, carries a stale token and is fenced out.

```csharp
if (elector is { IsLeader: true, Lease: { } lease })
{
    await store.WriteAsync(payload, fencingToken: lease.FencingToken, ct);
}
```

### A custom `ILeaseStore` for clustered election

The default `InMemoryLeaseStore` elects a leader only within one process. To elect across a cluster, implement `ILeaseStore` over a store shared by all instances and register it before `AddOrionBeacon()`; the in-memory store is only added if none is present.

```csharp
public sealed class RedisLeaseStore : ILeaseStore
{
    public Task<LeaseAcquisition> TryAcquireOrRenewAsync(
        string resource, string candidateId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        // Atomically: acquire if free or expired, renew if this candidate holds it,
        // otherwise deny. Hand out a strictly increasing fencing token on each new term.
        // ...
    }

    public Task ReleaseAsync(string resource, string candidateId, CancellationToken cancellationToken = default)
    {
        // Release only if this candidate holds the lease, so a follower can take over at once.
        // ...
    }
}
```

```csharp
builder.Services.AddSingleton<ILeaseStore, RedisLeaseStore>();
builder.Services.AddOrionBeacon(o => o.ResourceName = "jobs");
```

Two rules an implementation must honour: `TryAcquireOrRenewAsync` must be atomic so two candidates cannot both acquire, and it must assign a strictly increasing fencing token on each new acquisition. `LeaseAcquisition` reports the `Outcome` (`Acquired`, `Renewed`, or `Denied`), the `Lease` held on acquire or renew, and the `HolderId` on denial.

### React to leadership changes

Register an `ILeadershipObserver` to run code when this candidate gains or loses leadership. Implementations are observability only and must not throw; the elector swallows any fault so an observer outage never disrupts election.

```csharp
public sealed class LoggingObserver(ILogger<LoggingObserver> logger) : ILeadershipObserver
{
    public void OnElected(Lease lease) =>
        logger.LogInformation("Elected leader of {Resource}, fence {Token}", lease.Resource, lease.FencingToken);

    public void OnDeposed(string resource) =>
        logger.LogInformation("Lost leadership of {Resource}", resource);
}
```

```csharp
builder.Services.AddSingleton<ILeadershipObserver, LoggingObserver>();
builder.Services.AddOrionBeacon();
```

## Configuration

`AddOrionBeacon` takes an optional `Action<LeaderElectionOptions>`. Options are validated on registration, so an invalid combination throws at startup rather than failing silently later.

| Option | Default | Meaning |
|--------|---------|---------|
| `ResourceName` | `orion-leader` | The contended resource. Every candidate competing for the same leadership must use the same value. |
| `CandidateId` | machine name + per-process suffix | This instance's stable, unique identity. |
| `LeaseDuration` | 15 seconds | How long a lease is granted. If the leader fails to renew within this window, the lease lapses and a follower can take over. |
| `RenewInterval` | 5 seconds | How often the leader renews and a follower retries. Must be shorter than `LeaseDuration` so renewals beat expiry. |

`RenewInterval` must be positive and strictly shorter than `LeaseDuration`; `LeaseDuration` must be positive; `ResourceName` and `CandidateId` must be non-empty. A shorter renew interval relative to the lease tolerates more missed renewals before failover; a longer lease tolerates longer store outages at the cost of slower failover.

## Telemetry and events

OrionBeacon exposes a `System.Diagnostics.Metrics.Meter` named `Moongazing.OrionBeacon` (the constant `LeaderElectionDiagnostics.MeterName`). Subscribe to it from OpenTelemetry:

- `orionbeacon.attempts` (counter, tagged `outcome` = `acquired` / `renewed` / `denied`) - lease acquisition attempts.
- `orionbeacon.transitions` (counter, tagged `direction` = `elected` / `deposed`) - leadership transitions.
- `orionbeacon.is_leader` (observable gauge) - `1` while this candidate holds leadership, otherwise `0`.

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(LeaderElectionDiagnostics.MeterName));
```

For event-driven reactions rather than metrics, register an `ILeadershipObserver` (see Usage above).

## Testing

The election state machine is driven one cycle at a time through `ILeaderElector.TryElectAsync`, so it is fully testable without real timers or sleeps; the hosted service simply calls it on the renew interval. Tests drive cycles directly and inject a controllable clock into `InMemoryLeaseStore` to advance time deterministically.

```csharp
var store = new InMemoryLeaseStore();
var options = new LeaderElectionOptions { ResourceName = "res", CandidateId = "a" };
var elector = new LeaderElector(store, options, new LeaderElectionDiagnostics());

bool isLeader = await elector.TryElectAsync(); // first cycle wins; fencing token starts at 1
```

Run the library's own suite from the repository root:

```
dotnet test
```

## Benchmarks

A [BenchmarkDotNet](https://benchmarkdotnet.org/) suite covers the in-memory election hot paths: the lease-store critical section, a full elector cycle, and the small value objects on the path. No measured numbers are committed because results are hardware- and runtime-specific. See [benchmarks.md](benchmarks.md) for how to run them.

```
dotnet run -c Release --project benchmarks/Moongazing.OrionBeacon.Benchmarks
```

## Versioning

OrionBeacon follows [SemVer](https://semver.org/). The package multi-targets `net8.0`, `net9.0`, and `net10.0`. The project builds with `TreatWarningsAsErrors`, nullable reference types enabled, and the latest analyzers. Notable changes are recorded in [CHANGELOG.md](CHANGELOG.md).

## More from the Orion family

OrionBeacon is one of a set of standalone .NET libraries:

- [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard) - validation, guard clauses, and DDD primitives.
- [OrionAudit](https://github.com/tunahanaliozturk/OrionAudit) - automatic EF Core change-audit trail.
- [OrionKey](https://github.com/tunahanaliozturk/OrionKey) - source-generated strongly-typed IDs.
- [OrionLock](https://github.com/tunahanaliozturk/OrionLock) - distributed locking.
- [OrionPatch](https://github.com/tunahanaliozturk/OrionPatch) - transactional outbox for EF Core.

## Documentation

- [docs/FEATURES.md](docs/FEATURES.md) - a deeper breakdown of every capability and the type behind it.
- [docs/ROADMAP.md](docs/ROADMAP.md) - ideas under consideration, no promised dates.
- [benchmarks.md](benchmarks.md) - the benchmark suite and how to run it.

## Contributing

Issues and pull requests welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md) before opening one.

## License

This project is licensed under the [MIT License](LICENSE).

## Author

**Tunahan Ali Ozturk** - [GitHub](https://github.com/tunahanaliozturk)
