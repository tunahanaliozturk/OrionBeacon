<!-- markdownlint-disable MD024 -->

# Changelog

All notable changes to OrionBeacon are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/) and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
