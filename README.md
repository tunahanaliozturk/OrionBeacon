# OrionBeacon

[![CI/CD](https://github.com/tunahanaliozturk/OrionBeacon/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/tunahanaliozturk/OrionBeacon/actions/workflows/ci-cd.yml)
[![NuGet](https://img.shields.io/nuget/v/OrionBeacon.svg)](https://www.nuget.org/packages/OrionBeacon/)

Leader election for .NET. Run the same service on several instances and OrionBeacon makes sure
exactly one of them is the leader at a time, so scheduled jobs, outbox draining, and other
"only one node should do this" work runs once, not once per instance.

Part of the **Orion** family. Usable entirely on its own.

## Why

Scale a worker to three instances and your nightly job runs three times. The fix is leader
election: candidates compete for a renewable lease in a shared store, the holder is the leader,
and if it dies the lease lapses and another takes over. OrionBeacon implements that with fencing
tokens (so a leader whose lease lapsed cannot keep writing as if it were still in charge) and a
hosted loop that keeps the state current, leaving you a single `IsLeader` flag to check.

## Install

```
dotnet add package OrionBeacon
```

## Quick start

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

The hosted loop registered by `AddOrionBeacon` acquires and renews the lease in the background and
resigns on shutdown, so a healthy follower is promoted promptly.

## Fencing tokens

Each new leadership term gets a strictly increasing fencing token. Pass it to any resource the
leader writes to and have that resource reject anything carrying a lower token than the highest it
has seen. This is what makes election safe under a stop-the-world pause: a leader that was frozen
past its lease, then resumed, carries a stale token and is fenced out.

## Storage

The default `InMemoryLeaseStore` elects a leader only within one process, which is right for a
single node or for tests. To elect across a cluster, implement `ILeaseStore` over Redis, a
database, or another shared store and register it before `AddOrionBeacon()`; the in-memory store
is only added if none is present. Implementations must make `TryAcquireOrRenewAsync` atomic and
hand out a strictly increasing fencing token on each acquisition.

## Telemetry and events

Subscribe to the `Moongazing.OrionBeacon` meter: `orionbeacon.attempts` (tagged `outcome`),
`orionbeacon.transitions` (tagged `direction`), and an `orionbeacon.is_leader` gauge. Register an
`ILeadershipObserver` to react to `OnElected` and `OnDeposed`. The observer is fault-safe: an
exception it throws never disrupts election.

## Design

- Multi-targets `net8.0`, `net9.0`, `net10.0`.
- `TreatWarningsAsErrors`, latest analyzers, nullable enabled.
- The election state machine is driven one cycle at a time, so it is fully testable without real
  timers; the hosted service simply calls it on the renew interval.

## License

MIT.
