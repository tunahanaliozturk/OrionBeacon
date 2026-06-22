# OrionBeacon.Stores.Redis

A Redis-backed `ILeaseStore` for [OrionBeacon](https://github.com/tunahanaliozturk/OrionBeacon) leader election. Run the same service on several instances against a shared Redis and OrionBeacon keeps exactly one elected across the cluster, not one per process as the in-memory store does.

## Why a server-side script

Leader election is only safe if acquire-or-renew is atomic: two candidates checking "is the lease free?" and then both taking it is the split-brain this library exists to prevent. This store expresses the whole decision as one Lua script, so it runs to completion on the Redis server without interleaving another candidate's commands. There is no read-then-write window.

The script does exactly what the contract requires:

- No current leader (the lease key is absent or has expired): the caller becomes leader and the fencing token advances.
- The caller is already the leader: the lease is renewed (its TTL is extended) and the fencing token does not change.
- A different candidate holds a live lease: the caller is denied and told who holds it.

## Fencing tokens that strictly increase

The fencing token is a Redis counter at `{prefix}{resource}:fence`, advanced with `INCR` inside the acquire path of the same script. That counter is never deleted, so even after a dead leader's lease expires and the lease key vanishes, the next candidate to take over receives a token strictly greater than every term before it. A renew reads the stored token back unchanged, so the token is stable for the life of a single leadership term and only moves when leadership does. Pass it to downstream stores to fence out a leader whose lease lapsed during a stop-the-world pause.

## Lease expiry and takeover

The lease is a Redis hash carrying a TTL. A healthy leader renews within the lease window and the TTL is pushed forward each time. If the leader dies and stops renewing, Redis expires the key, the lease lapses, and the next candidate's acquire succeeds with a higher token. Release is fencing-checked: only the candidate that holds the lease can release it, so a stale caller cannot hand the resource to someone else.

## Install

```
dotnet add package OrionBeacon.Stores.Redis
```

## Usage

Register the Redis store before `AddOrionBeacon()`. `AddOrionBeacon()` only adds the in-process store if no `ILeaseStore` is already registered, so registering Redis first makes election span the cluster.

```csharp
builder.Services.AddOrionBeaconRedisStore("localhost:6379");
builder.Services.AddOrionBeacon(o =>
{
    o.ResourceName = "nightly-report";
    o.LeaseDuration = TimeSpan.FromSeconds(15);
    o.RenewInterval = TimeSpan.FromSeconds(5);
});
```

If the application already manages its own `IConnectionMultiplexer`, use the overload that reuses it:

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));
builder.Services.AddOrionBeaconRedisStore(o => o.KeyPrefix = "myapp:lease:");
builder.Services.AddOrionBeacon(o => o.ResourceName = "jobs");
```

Every candidate competing for the same leadership must use the same `KeyPrefix` and `Database` so they contend over the same keys.

## License

MIT, the same as OrionBeacon.
