# Roadmap

OrionBeacon is leader election for .NET: candidates compete for a renewable lease in a shared store, the holder is the leader, and fencing tokens keep a stale leader from writing as if it were still in charge.

Current release: **0.2.1**.

The version milestones below are directions, not commitments. Dates are targets and will move. The guiding constraint is unchanged: keep the core small and dependency-light, push anything that needs a database or a broker into its own opt-in package, and do not reimplement consensus. If an item matters to you, an issue saying so is the best way to move it up.

## Released

### 0.2.1 - 2026-06-20

- The diagnostics meter version derives automatically from the package version, so the `Moongazing.OrionBeacon` meter and the published package can no longer report different versions.

### 0.2.0 - 2026-06-19

- `InMemoryLeaseStore` accepts a `TimeProvider` through a new public constructor (defaulting to `TimeProvider.System`), so a test can advance the clock past a lease's expiry and exercise time-driven failover with no real delay. The existing constructors are unchanged.
- `TryAcquireOrRenewAsync` and `ReleaseAsync` honor the `CancellationToken`, throwing when it is already cancelled rather than completing the operation regardless.
- Identity validation rejects whitespace-only `ResourceName`, `CandidateId`, and holder values, not only null or empty.

### 0.1.0 - 2026-06-14

Initial release: the `ILeaderElector` acquire-or-renew state machine, the `LeaderElectionService` hosted loop, `ILeaseStore` with the in-process `InMemoryLeaseStore`, fencing tokens, validated `LeaderElectionOptions`, the `ILeadershipObserver` hook, and the `Moongazing.OrionBeacon` metrics meter.

## Next

### 0.3.0 - distributed lease stores (target 2026 Q3)

`ILeaseStore` exists so the core stays storage-agnostic; the obvious next step is shipping real shared stores as separate packages, each keeping the core's single dependency:

- A Redis-backed `ILeaseStore`, with atomic acquire-or-renew expressed as one server-side script so two candidates cannot both acquire.
- A relational `ILeaseStore` (Postgres, SQL Server) using a single leader row and conditional updates.

Each store must honor the two existing contract rules: `TryAcquireOrRenewAsync` is atomic, and a strictly increasing fencing token is assigned on every new acquisition. A shared conformance test suite would let any store, including a third-party one, prove it satisfies them.

### 0.4.0 - leadership-change events and readiness (target 2026 Q4)

- An async leadership hook. Today `ILeadershipObserver` is synchronous and observability-only. An opt-in async surface (or a "run this delegate only while leadership is held, and cancel it on deposition" wrapper) covers the common case of starting and stopping real work on a transition without each consumer writing the same plumbing.
- An `IHealthCheck` that reports this instance's leadership state, so leader-gated work can be surfaced to readiness probes and orchestrators without consumers polling `IsLeader` themselves.
- A read-only peek on `ILeaseStore` to report the current holder and fencing token without contending for the lease, which the health check and diagnostics can both read from.

### 0.5.0 - multiple independent elections in one process (target 2027 Q1)

Registration today assumes one elected resource per application: a single `LeaderElectionOptions`, one elector, one hosted loop. A keyed or named registration that elects several independent resources side by side, each with its own resource name, options, elector, and lease store, would suit an app that coordinates more than one "only one node does this" responsibility. This is an additive API; the single-resource `AddOrionBeacon()` stays as the simple default.

## Smaller refinements

Not tied to a milestone; folded in where they fit:

- Renew-interval jitter, so a fleet restarting together does not retry acquisition in lockstep and hammer the store on the same tick.
- A `System.Diagnostics.ActivitySource` so an acquire-or-renew cycle shows up as a span alongside the existing metrics.
- A current-term gauge exposing the fencing token, useful when correlating fenced writes downstream.
- Optional out-of-the-box logging behind an opt-in, for users who do not wire an `ILeadershipObserver`.

## Out of scope

- A consensus protocol of our own. OrionBeacon is lease-based election over a store you trust to be atomic; it leans on that store (and your infrastructure) for the guarantee rather than reimplementing Raft or Paxos.
- A distributed store bundled into the core. The core stays dependency-light; clustered stores ship as their own packages.
