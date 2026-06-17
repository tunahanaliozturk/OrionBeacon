# Roadmap

Ideas under consideration, not commitments. There are no dates here on purpose: these are directions that fit the library, weighed against keeping the core small and dependency-light. If one matters to you, an issue saying so is the best way to move it up.

## Storage backends

The whole point of `ILeaseStore` is that the core stays storage-agnostic, so the obvious extensions are separate, opt-in stores rather than core changes:

- A Redis-backed `ILeaseStore`, with the atomic acquire-or-renew expressed as a single server-side script.
- A relational `ILeaseStore` (Postgres, SQL Server) using a single leader row and conditional updates.

Each would ship as its own package so the core keeps its single dependency.

## Multiple independent elections in one process

Today registration assumes one elected resource per application. Electing several independent resources side by side (each its own `ResourceName`, options, and elector) would suit apps that coordinate more than one "only one node does this" responsibility.

## Richer observability

- A `System.Diagnostics.ActivitySource` so an acquire-or-renew cycle can show up as a span alongside the existing metrics.
- A current-term gauge or counter exposing the fencing token, useful when correlating fenced writes downstream.

## Ergonomic helpers

- A small `IsLeaderAsync`-style awaitable or wrapper that runs a delegate only while leadership is held, for the common "do this work only as leader" pattern.
- Optional logging out of the box behind an opt-in, for users who do not wire an `ILeadershipObserver`.

## Out of scope

- A consensus protocol of our own. OrionBeacon is lease-based election over a store you trust to be atomic; it deliberately leans on the store (and your infrastructure) for that guarantee rather than reimplementing Raft or Paxos.
- A built-in distributed store. The core stays dependency-light; clustered stores belong in their own packages.
