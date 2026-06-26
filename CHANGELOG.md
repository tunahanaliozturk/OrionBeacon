<!-- markdownlint-disable MD024 -->

# Changelog

All notable changes to OrionBeacon are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/) and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.0] - 2026-06-27

### Added

- New package **OrionBeacon.Stores.Relational** (`Moongazing.OrionBeacon.Stores.Relational`): a
  relational `ILeaseStore` over PostgreSQL or SQL Server that elects a leader across a cluster from a
  single leader row per resource. Acquire-or-renew is one atomic conditional upsert, never a
  read-then-write, so two candidates can never both acquire: on PostgreSQL an
  `INSERT ... ON CONFLICT (resource) DO UPDATE ... WHERE (holder = @me OR expired) RETURNING`, and on
  SQL Server a `MERGE ... WITH (HOLDLOCK) ... OUTPUT`. If there is no current leader (no row, or the
  row's lease has expired) the caller becomes leader and the fencing token advances by one; if the
  caller already holds the lease it is renewed and the token does not change; if a different candidate
  holds a live lease the caller is denied and told who holds it. The fencing token is a `bigint` column
  carried on the row and is never reset, so it is strictly increasing across leadership changes
  (including takeover after a dead leader's lease expires) and stable across a renew. Liveness is judged
  by the database clock (`now()` / `SYSUTCDATETIME()`), so candidates with skewed wall clocks still
  agree on whether a lease is live. The lease has an expiry timestamp so a dead leader's lease lapses
  and a follower takes over, release is fencing-checked so only the holder can release, and the initial
  insert race between two nodes creating the first row is resolved by the primary key inside the atomic
  upsert. Register it with `AddOrionBeaconPostgresStore(...)` or `AddOrionBeaconSqlServerStore(...)`
  before `AddOrionBeacon()`. Depends only on `Npgsql`, `Microsoft.Data.SqlClient`, and
  `Microsoft.Extensions.DependencyInjection.Abstractions`; the core's single dependency is unchanged.
- The shared **`ILeaseStore` conformance suite** now runs the relational store against a real
  PostgreSQL and a real SQL Server via Testcontainers, alongside the existing in-memory and Redis runs,
  so both relational dialects prove the same contract: exactly one leader under concurrent acquires, a
  fencing token that strictly increases on each leadership change and is stable across a renew, lease
  expiry letting a new leader take over, and fencing-checked release.

[0.4.0]: https://github.com/tunahanaliozturk/OrionBeacon/releases/tag/v0.4.0

## [0.3.0] - 2026-06-22

### Added

- New package **OrionBeacon.Stores.Redis** (`Moongazing.OrionBeacon.Stores.Redis`): a Redis-backed
  `ILeaseStore` that elects a leader across a cluster. Acquire-or-renew is one atomic server-side Lua
  script, never a read-then-write, so two candidates can never both acquire. If there is no current
  leader (the lease key is absent or has expired) the caller becomes leader and the fencing token
  advances; if the caller already holds the lease it is renewed and the token does not change; if a
  different candidate holds a live lease the caller is denied. The fencing token is a persistent
  Redis counter advanced inside the same script, so it is strictly increasing across leadership
  changes (including takeover after a dead leader's lease expires) and stable across a renew. The
  lease is a hash carrying a TTL, so a dead leader's lease lapses and a follower takes over, and
  release is fencing-checked so only the holder can release. Register it with
  `AddOrionBeaconRedisStore(...)` before `AddOrionBeacon()`. Depends only on `StackExchange.Redis`
  and `Microsoft.Extensions.DependencyInjection.Abstractions`; the core's single dependency is
  unchanged.
- A shared **`ILeaseStore` conformance suite** (`LeaseStoreConformanceTests`): a reusable abstract
  xUnit base that asserts the contract every store must satisfy: exactly one leader under concurrent
  acquires, a fencing token that strictly increases on each leadership change and is stable across a
  renew, lease expiry letting a new leader take over, and fencing-checked release. Both the in-memory
  store and the Redis store run through it; the in-memory store passing it validates the suite, and
  the Redis run executes against a real Redis via Testcontainers.
- `LeaseAcquisition.Acquired`, `Renewed`, and `Denied` are now public, so an `ILeaseStore`
  implemented outside the core assembly (such as the Redis store, or a third-party store) can build
  a result. The factories validate their arguments. No existing behaviour changes.

[0.3.0]: https://github.com/tunahanaliozturk/OrionBeacon/releases/tag/v0.3.0

## [0.2.1] - 2026-06-20

### Changed

- The diagnostics meter version now derives automatically from the package version. The
  `Moongazing.OrionBeacon` meter previously carried a hardcoded version literal that could drift from
  the published package version; it now reads the assembly informational version (flowed from
  `<Version>`) once at startup, so the two can no longer diverge.

[0.2.1]: https://github.com/tunahanaliozturk/OrionBeacon/releases/tag/v0.2.1

## [0.2.0] - 2026-06-19

### Added

- Time-driven automatic failover: `InMemoryLeaseStore` now accepts a `TimeProvider` (defaulting to
  `TimeProvider.System`) through a new public constructor. Inject a controllable provider to advance
  a lease past its expiry with no real delay, so a rival candidate wins the next acquire cycle. This
  makes expiry-driven failover, previously only reachable through graceful `ResignAsync`,
  demonstrable and testable. The existing constructors are unchanged.

### Fixed

- `InMemoryLeaseStore` now honors the `CancellationToken`: `TryAcquireOrRenewAsync` and
  `ReleaseAsync` throw at the start when the token is already cancelled instead of completing the
  operation regardless.
- Identity validation now rejects whitespace-only values. `Lease`, `InMemoryLeaseStore`, and
  `LeaderElectionOptions.Validate()` use `ArgumentException.ThrowIfNullOrWhiteSpace` for
  `ResourceName` / `CandidateId` / holder, so a whitespace-only identity no longer passes the guards
  that previously only rejected null or empty.

[0.2.0]: https://github.com/tunahanaliozturk/OrionBeacon/releases/tag/v0.2.0

## [0.1.0] - 2026-06-14

### Added

Initial release. Leader election.

- `ILeaderElector` / `LeaderElector`: an acquire-or-renew state machine that tracks `IsLeader` and
  the current `Lease`, firing an election when it gains the lease and a deposition when it loses
  it. Driven one cycle at a time, so it is fully testable without real timers.
- `LeaderElectionService`: a hosted background loop that renews on the configured interval and
  resigns on shutdown, surviving transient store faults.
- `ILeaseStore` with an in-process `InMemoryLeaseStore` (atomic claim, strictly increasing fencing
  token); swap in a shared store to elect across a cluster.
- `Lease` with a fencing token for safe writes under stop-the-world pauses.
- `LeaderElectionOptions`: resource name, candidate id, lease duration, renew interval; validated
  on registration (renew interval must be shorter than the lease).
- `ILeadershipObserver`: fault-safe elected/deposed hook.
- `LeaderElectionDiagnostics`: `Moongazing.OrionBeacon` meter with attempt and transition counters
  and an is-leader gauge.
- `AddOrionBeacon()` DI extension wiring the elector, store, and hosted loop.

### Tests

17 tests across the lease store (acquire, renew, deny, takeover, release, fencing), the elector
(election, renewal, deposition, resignation, follower, observer fault isolation), and registration.

[0.1.0]: https://github.com/tunahanaliozturk/OrionBeacon/releases/tag/v0.1.0
