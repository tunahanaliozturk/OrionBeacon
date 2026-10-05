# OrionBeacon

Leader election for .NET: run the same service on several instances and OrionBeacon keeps exactly one of them elected, so scheduled jobs, outbox draining and other "only one node should do this" work runs once, not once per instance.

![One election cycle: the hosted loop calls TryAcquireOrRenewAsync, the outcome sets IsLeader and fires OnElected or OnDeposed, a store fault is swallowed and retried](https://raw.githubusercontent.com/tunahanaliozturk/OrionBeacon/main/docs/diagrams/election-cycle.png)

## Install

    dotnet add package OrionBeacon

## Quick start

```csharp
using Moongazing.OrionBeacon;
using Moongazing.OrionBeacon.Election;

builder.Services.AddOrionBeacon(o =>
{
    o.ResourceName = "nightly-report";
    o.LeaseDuration = TimeSpan.FromSeconds(15);
    o.RenewInterval = TimeSpan.FromSeconds(5); // must be shorter than LeaseDuration
});

public sealed class NightlyReportJob(ILeaderElector elector)
{
    public async Task RunAsync(CancellationToken ct)
    {
        if (elector is not { IsLeader: true, Lease: { } lease })
        {
            return; // another instance is the leader
        }

        await ProduceReportAsync(lease.FencingToken, ct); // pass the token to downstream writes
    }

    private static Task ProduceReportAsync(long fencingToken, CancellationToken ct) => Task.CompletedTask;
}
```

`AddOrionBeacon` registers a hosted `LeaderElectionService` that acquires and renews the lease every `RenewInterval` and resigns on shutdown, so a follower is promoted promptly.

## Options (`LeaderElectionOptions`)

| Option | Default | Meaning |
|--------|---------|---------|
| `ResourceName` | `orion-leader` | The contended resource; every competing candidate uses the same value. |
| `CandidateId` | machine name + a per-process GUID | This instance's unique identity. |
| `LeaseDuration` | 15 seconds | How long a lease is granted; a leader that does not renew within it loses the lease. |
| `RenewInterval` | 5 seconds | How often the leader renews and a follower retries. Must be shorter than `LeaseDuration`. |

Options are validated at registration: empty names, non-positive durations, or a `RenewInterval` not shorter than `LeaseDuration` throw at startup.

## Behaviour

- Fencing tokens: every new leadership term gets a strictly higher `Lease.FencingToken`. A downstream resource that rejects tokens lower than the highest it has seen fences out a leader that resumed after its lease lapsed.
- Storage: the default `InMemoryLeaseStore` elects within one process (single node, tests). Register a shared `ILeaseStore` before `AddOrionBeacon()` to elect across a cluster; the in-memory store is only added when none is registered.
- Faults: a store exception on one cycle is swallowed by the hosted loop and retried on the next one; `IsLeader` keeps its previous value until a cycle succeeds.
- Events: register an `ILeadershipObserver` for `OnElected(Lease)` and `OnDeposed(string resource)`. Observer exceptions are swallowed and never disrupt election.
- Telemetry: meter `Moongazing.OrionBeacon` (`LeaderElectionDiagnostics.MeterName`) with `orion.beacon.attempts` (tag `orion.outcome`), `orion.beacon.transitions` (tag `direction`) and the `orion.beacon.is_leader` gauge.
- Testing: `ILeaderElector.TryElectAsync` runs one cycle on demand, and `InMemoryLeaseStore(TimeProvider)` takes a fake clock, so failover tests need no real delays.
- Targets net8.0, net9.0 and net10.0.

## Related packages

- `OrionBeacon.Stores.Redis` - `ILeaseStore` over Redis for cluster-wide election.
- `OrionBeacon.Stores.Relational` - `ILeaseStore` over PostgreSQL or SQL Server for cluster-wide election.
- `Orion.Abstractions` - the family contracts behind the telemetry (`OrionInstrumentation`, `OrionTelemetry`).

## Links

- Documentation and full README: https://github.com/tunahanaliozturk/OrionBeacon
- Changelog: https://github.com/tunahanaliozturk/OrionBeacon/blob/main/CHANGELOG.md
- License: MIT
