# OrionBeacon.Stores.Relational

A relational `ILeaseStore` for OrionBeacon leader election, over PostgreSQL or SQL Server. Run the same service on several instances against a shared database and OrionBeacon keeps exactly one of them elected across the cluster, not one per process as the in-memory store does.

![OrionBeacon packages: the app registers AddOrionBeacon; LeaderElector calls ILeaseStore, backed by InMemoryLeaseStore, RedisLeaseStore or RelationalLeaseStore](https://raw.githubusercontent.com/tunahanaliozturk/OrionBeacon/main/docs/diagrams/overview.png)

## Install

    dotnet add package OrionBeacon.Stores.Relational

Plugs into the `OrionBeacon` package (referenced automatically). Brings `Npgsql` and `Microsoft.Data.SqlClient`.

## Quick start

Register the relational store before `AddOrionBeacon()`. `AddOrionBeacon()` only adds the in-process store when no `ILeaseStore` is registered, so registering this store first makes election span the cluster.

PostgreSQL:

```csharp
using Moongazing.OrionBeacon;
using Moongazing.OrionBeacon.Stores.Relational;

builder.Services.AddOrionBeaconPostgresStore("Host=localhost;Database=app;Username=app;Password=secret");
builder.Services.AddOrionBeacon(o =>
{
    o.ResourceName = "nightly-report";
    o.LeaseDuration = TimeSpan.FromSeconds(15);
    o.RenewInterval = TimeSpan.FromSeconds(5);
});
```

SQL Server:

```csharp
builder.Services.AddOrionBeaconSqlServerStore(
    "Server=localhost;Database=app;User Id=sa;Password=secret;TrustServerCertificate=True",
    o => o.TableName = "leader_leases");
builder.Services.AddOrionBeacon(o => o.ResourceName = "jobs");
```

The store opens a short-lived connection per operation and lets the provider's pool recycle it. The leader table is created on first use if it does not exist.

## Options (`RelationalLeaseStoreOptions`)

| Option | Default | Meaning |
|--------|---------|---------|
| `TableName` | `orionbeacon_leases` | Unquoted table name, validated against `^[A-Za-z_][A-Za-z0-9_]*$` and quoted for the engine. |
| `CommandTimeout` | 30 seconds | Timeout for every command; must be positive. |
| `Provider` | `Unspecified` | SQL dialect. Set for you by `AddOrionBeaconPostgresStore` / `AddOrionBeaconSqlServerStore`; required when you construct `RelationalLeaseStore` yourself. |

Point every candidate at the same database and table so they contend over the same rows.

## How it works

- Atomic acquire-or-renew: one conditional upsert, no read-then-write window. PostgreSQL uses `INSERT ... ON CONFLICT (resource) DO UPDATE ... WHERE (holder = @me OR expired) RETURNING`, SQL Server `MERGE ... WITH (HOLDLOCK) ... OUTPUT`. The race to insert the first row is resolved by the primary key on `resource`.
- Fencing tokens that strictly increase: the token is a `bigint` column, advanced by one on each new term and unchanged on renew. Release does not delete the row; it sets the expiry into the past, so the next term still gets a higher token.
- Database clock: liveness is judged by `now()` / `SYSUTCDATETIME()`, so candidates with skewed clocks still agree on whether a lease is live.
- Limits: lease durations are honoured to the millisecond and must not exceed `int.MaxValue` milliseconds (about 24.8 days).

## Related packages

- `OrionBeacon` - the elector, hosted loop, options and telemetry this store plugs into.
- `OrionBeacon.Stores.Redis` - the same contract over Redis.

## Links

- Documentation and full README: https://github.com/tunahanaliozturk/OrionBeacon
- Changelog: https://github.com/tunahanaliozturk/OrionBeacon/blob/main/CHANGELOG.md
- License: MIT
