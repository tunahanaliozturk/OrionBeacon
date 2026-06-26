# OrionBeacon.Stores.Relational

A relational `ILeaseStore` for [OrionBeacon](https://github.com/tunahanaliozturk/OrionBeacon) leader election, over PostgreSQL or SQL Server. Run the same service on several instances against a shared database and OrionBeacon keeps exactly one elected across the cluster, not one per process as the in-memory store does.

## Why one atomic statement

Leader election is only safe if acquire-or-renew is atomic: two candidates checking "is the lease free?" and then both taking it is the split-brain this library exists to prevent. This store expresses the whole decision as one conditional upsert, so there is no read-then-write window for a rival to slip through:

- PostgreSQL: `INSERT ... ON CONFLICT (resource) DO UPDATE ... WHERE (holder = @me OR expired) RETURNING`.
- SQL Server: `MERGE ... WITH (HOLDLOCK) ... OUTPUT`.

The statement does exactly what the contract requires:

- No current leader (no row, or the row's lease has expired): the caller becomes leader and the fencing token advances.
- The caller is already the leader: the lease is renewed (its expiry is extended) and the fencing token does not change.
- A different candidate holds a live lease: the caller is denied and told who holds it.

## Fencing tokens that strictly increase

The fencing token is a `bigint` column on the leader row. On a new term it is set to the previous row's token plus one; on a renew it is left untouched. A release does not delete the row, it tombstones it (sets the expiry into the past), so the stored token survives the handover and the next acquirer advances strictly past it, even after a dead leader's lease lapsed and the row sat idle. A renew returns the same token, so it is stable for the life of a single leadership term and only moves when leadership does. Pass it to downstream stores to fence out a leader whose lease lapsed during a stop-the-world pause.

## Lease expiry and the database clock

The lease has an expiry timestamp and liveness is judged by the **database** clock (`now()` / `SYSUTCDATETIME()`), not the candidate's, so nodes with skewed wall clocks still agree on whether a lease is live. A healthy leader renews within the lease window and the expiry is pushed forward each time. If the leader dies and stops renewing, the row's expiry lapses and the next candidate's acquire succeeds with a higher token. Release is fencing-checked: only the candidate that holds the lease can release it.

The initial race between two nodes inserting the very first leader row is resolved by the primary key on `resource` inside the atomic upsert, so the loser converts to the conditional-update branch rather than failing on a duplicate key.

## Install

```
dotnet add package OrionBeacon.Stores.Relational
```

## Usage

Register the relational store before `AddOrionBeacon()`. `AddOrionBeacon()` only adds the in-process store if no `ILeaseStore` is already registered, so registering the relational store first makes election span the cluster.

PostgreSQL:

```csharp
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
builder.Services.AddOrionBeaconSqlServerStore("Server=localhost;Database=app;User Id=sa;Password=secret;TrustServerCertificate=True");
builder.Services.AddOrionBeacon(o => o.ResourceName = "jobs");
```

The store opens a short-lived connection per operation from the connection string and lets the provider's pool recycle it, which suits the elector's renew loop. The leader table (`orionbeacon_leases` by default) is created on first use; point every candidate at the same database and table name so they contend over the same rows.

## License

MIT, the same as OrionBeacon.
