# Features

A deeper breakdown of what OrionBeacon does and the types behind each capability. Every type named here is public unless noted. The namespace root is `Moongazing.OrionBeacon`.

## Leader election over a renewable lease

Leadership is modelled as a lease over a named resource. Candidates that want the same leadership all use the same `ResourceName` and compete in a shared `ILeaseStore`. Whoever holds an unexpired lease is the leader.

- `ILeaderElector` is the application-facing surface. `IsLeader` is true while this candidate holds the lease; `Lease` is the current `Lease` or `null` for a follower.
- `LeaderElector` is the default implementation. Each cycle it asks the store to acquire or renew, then reconciles the result into `IsLeader`, firing an election when it gains the lease and a deposition when it loses it.

A lease lapses if the leader fails to renew within `LeaseDuration`. Once it lapses, the next candidate to call the store acquires it and a new term begins.

## Acquire-or-renew as a single state machine cycle

`ILeaderElector.TryElectAsync` runs exactly one acquire-or-renew cycle and returns the leadership state after it. The cycle:

1. Calls `ILeaseStore.TryAcquireOrRenewAsync`.
2. Records the outcome on the diagnostics meter.
3. Reconciles `IsLeader` and `Lease` under a lock, firing a transition only when the held state actually changes (so a steady renew does not re-fire `OnElected`).

Because a cycle is an explicit method call rather than a timer tick, the whole state machine is testable without real time. `ResignAsync` releases the lease if held and fires a deposition, so a follower can take over without waiting for expiry.

## Fencing tokens

`Lease.FencingToken` is a strictly increasing `long` assigned each time the lease changes hands. It exists to close the classic split-brain gap: a leader frozen past its lease (a GC pause, a stop-the-world stall) and then resumed still believes it is the leader. If every write it makes carries its fencing token, and each downstream resource rejects any write whose token is lower than the highest it has seen, the stale leader is fenced out the moment a newer term exists.

The token increases on every new term, including across a clean `ReleaseAsync` handover. `InMemoryLeaseStore` does this by keeping a released entry as an expired tombstone so the next acquisition reads the prior token and increments it, rather than resetting to 1.

## Pluggable lease storage

`ILeaseStore` is the seam between the elector and wherever leases actually live.

- `TryAcquireOrRenewAsync(resource, candidateId, duration, ct)` atomically acquires a free or expired lease, renews a lease the caller already holds, or denies when another candidate holds an unexpired one. It returns a `LeaseAcquisition`.
- `ReleaseAsync(resource, candidateId, ct)` releases the lease only if this candidate holds it; otherwise it is a no-op.

`LeaseAcquisition` carries the `LeaseOutcome` (`Acquired`, `Renewed`, `Denied`), the `Lease` held on acquire or renew, the `HolderId` of the current holder on denial, and an `IsHeld` convenience flag.

Two invariants an implementation must hold: acquisition must be atomic so two candidates cannot both win, and each new acquisition must hand out a strictly increasing fencing token.

### `InMemoryLeaseStore`

The default store, registered only if no other `ILeaseStore` is present. It is process-local: it elects a leader among candidates in the same process, which is correct for a single node and for tests. Atomicity comes from a lock around the acquire/renew/release critical section. It takes the system clock by default; an internal constructor accepts a clock delegate so tests can drive time deterministically.

To elect across a cluster, implement `ILeaseStore` over a store shared by all instances (Redis, a relational table, etc.) and register it before `AddOrionBeacon()`.

## Hosted background loop

`LeaderElectionService` is a `BackgroundService` registered by `AddOrionBeacon`. It runs `TryElectAsync` on the `RenewInterval` for the lifetime of the application and calls `ResignAsync` on shutdown. A transient store fault on one cycle is swallowed so the loop survives to retry; if the outage outlasts the lease, the lease simply lapses and a healthy instance wins. Cancellation during shutdown is handled cleanly, letting the lease lapse on its own if resignation cannot complete.

## Options and validation

`LeaderElectionOptions` configures one elector: `ResourceName`, `CandidateId`, `LeaseDuration`, and `RenewInterval`. `Validate()` runs on registration and on elector construction, rejecting an empty resource or candidate id, a non-positive duration or interval, and a `RenewInterval` that is not strictly shorter than `LeaseDuration`. Invalid configuration fails fast at startup.

## Telemetry

`LeaderElectionDiagnostics` owns a `System.Diagnostics.Metrics.Meter` named `Moongazing.OrionBeacon` (the constant `MeterName`). It publishes:

- `orionbeacon.attempts` - counter of acquisition attempts, tagged `outcome` (`acquired` / `renewed` / `denied`).
- `orionbeacon.transitions` - counter of leadership transitions, tagged `direction` (`elected` / `deposed`).
- `orionbeacon.is_leader` - observable gauge, `1` when this candidate is the leader, otherwise `0`.

It is a disposable singleton; disposing it releases the meter. Subscribe with any OpenTelemetry metrics pipeline via `AddMeter(LeaderElectionDiagnostics.MeterName)`.

## Leadership-change observer

`ILeadershipObserver` is an optional hook with `OnElected(Lease)` and `OnDeposed(string resource)`. Register one through DI and the elector calls it on each transition. Observers are observability only and must not throw: the elector wraps each call and swallows any exception, so an observer fault never disrupts election. When none is registered, `NullLeadershipObserver` is used.

## Registration

`OrionBeaconServiceCollectionExtensions.AddOrionBeacon(services, configure?)` wires everything: the options (validated), the diagnostics singleton, an `InMemoryLeaseStore` if no `ILeaseStore` is registered, the `ILeaderElector`, and the hosted `LeaderElectionService`. It uses `TryAdd` throughout, so any of these registered earlier wins, which is how a custom store or observer is substituted.

## Targets and dependencies

Multi-targets `net8.0`, `net9.0`, and `net10.0`. The single package reference is `Microsoft.Extensions.Hosting.Abstractions`. Built with nullable reference types, `TreatWarningsAsErrors`, the latest analyzers, and a generated XML documentation file.
