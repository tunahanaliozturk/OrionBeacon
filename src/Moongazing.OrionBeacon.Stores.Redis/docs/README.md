# OrionBeacon.Stores.Redis

A Redis-backed `ILeaseStore` for OrionBeacon leader election. Run the same service on several instances against a shared Redis and OrionBeacon keeps exactly one of them elected across the cluster, not one per process as the in-memory store does.

![OrionBeacon packages: the app registers AddOrionBeacon; LeaderElector calls ILeaseStore, backed by InMemoryLeaseStore, RedisLeaseStore or RelationalLeaseStore](https://raw.githubusercontent.com/tunahanaliozturk/OrionBeacon/main/docs/diagrams/overview.png)

## Install

    dotnet add package OrionBeacon.Stores.Redis

Plugs into the `OrionBeacon` package (referenced automatically).

## Quick start

Register the Redis store before `AddOrionBeacon()`. `AddOrionBeacon()` only adds the in-process store when no `ILeaseStore` is registered, so registering Redis first makes election span the cluster.

```csharp
using Moongazing.OrionBeacon;
using Moongazing.OrionBeacon.Stores.Redis;

builder.Services.AddOrionBeaconRedisStore("localhost:6379");
builder.Services.AddOrionBeacon(o =>
{
    o.ResourceName = "nightly-report";
    o.LeaseDuration = TimeSpan.FromSeconds(15);
    o.RenewInterval = TimeSpan.FromSeconds(5);
});
```

If the application already registers its own `IConnectionMultiplexer`, use the overload without a connection string; it reuses that connection:

```csharp
using StackExchange.Redis;

builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddOrionBeaconRedisStore(o => o.KeyPrefix = "myapp:lease:");
builder.Services.AddOrionBeacon(o => o.ResourceName = "jobs");
```

## Options (`RedisLeaseStoreOptions`)

| Option | Default | Meaning |
|--------|---------|---------|
| `KeyPrefix` | `orionbeacon:lease:` | Prefix of every key. Keep it free of `{` and `}`. |
| `Database` | `-1` | Redis logical database; `-1` uses the connection's default. |

Every candidate competing for the same leadership must use the same `KeyPrefix` and `Database`.

## How it works

- Atomic acquire-or-renew: the whole decision is one Lua script, so it runs on the Redis server without another candidate's commands in between. No current holder: the caller acquires and the fencing token advances. Same holder: the TTL is extended and the token stays. Another holder: the caller is denied and told who holds it.
- Fencing tokens that strictly increase: the token is a counter key that is never deleted, advanced with `INCR` in the same script. A takeover after a dead leader's lease expired still gets a higher token than every earlier term.
- Lease expiry: the lease is a hash with a TTL (`PEXPIRE`). A leader that stops renewing loses the lease when Redis expires it. Release only deletes the lease when the caller is the holder.
- Redis Cluster: the resource is wrapped in a hash tag, so for resource `jobs` the keys are `orionbeacon:lease:{jobs}` (lease hash) and `orionbeacon:lease:{jobs}:fence` (token counter), and both land on the same slot. A resource name containing `{` or `}` is rejected with `ArgumentException`.

## Related packages

- `OrionBeacon` - the elector, hosted loop, options and telemetry this store plugs into.
- `OrionBeacon.Stores.Relational` - the same contract over PostgreSQL or SQL Server.

## Links

- Documentation and full README: https://github.com/tunahanaliozturk/OrionBeacon
- Changelog: https://github.com/tunahanaliozturk/OrionBeacon/blob/main/CHANGELOG.md
- License: MIT
