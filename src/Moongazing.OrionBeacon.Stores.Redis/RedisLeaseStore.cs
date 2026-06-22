using Moongazing.OrionBeacon.Leasing;

using StackExchange.Redis;

namespace Moongazing.OrionBeacon.Stores.Redis;

/// <summary>
/// A Redis-backed <see cref="ILeaseStore"/> that elects a leader across a cluster. Acquire-or-renew
/// is one atomic server-side Lua script, never a read-then-write, so two candidates can never both
/// acquire. A persistent Redis counter holds the highest fencing token ever issued for a resource,
/// advanced inside the same script on every new acquisition, so the token is strictly increasing
/// across leadership changes (including takeover after a dead leader's lease expires) and is stable
/// across a renew.
/// </summary>
/// <remarks>
/// <para>
/// Two keys back each resource. The lease itself is a hash at <c>{prefix}{resource}</c> with fields
/// <c>holder</c>, <c>token</c>, and <c>acquired</c> (unix milliseconds), and it carries the lease
/// TTL: when the holder stops renewing, Redis expires the hash and the lease lapses, letting a
/// follower take over. The fencing token is a separate counter at <c>{prefix}{resource}:fence</c>
/// that is never deleted, so the token a takeover receives is strictly greater than any term before
/// it even though the lease hash came and went.
/// </para>
/// <para>
/// Atomicity is the Redis server's: a Lua script runs to completion without interleaving other
/// commands, so the holder check, the token advance, and the TTL write are one indivisible step.
/// This store needs only a Redis connection; it does not assume Redis is replicated and makes the
/// same single-store trust assumption the in-memory store does within one process.
/// </para>
/// </remarks>
public sealed class RedisLeaseStore : ILeaseStore
{
    // KEYS[1] = lease hash, KEYS[2] = fence counter.
    // ARGV[1] = candidateId, ARGV[2] = ttl milliseconds, ARGV[3] = now (unix ms).
    // Returns, for acquire/renew: { outcome, token, acquiredUnixMs }; for denial: { "denied", holder }.
    private const string AcquireOrRenewScript = @"
local holder = redis.call('HGET', KEYS[1], 'holder')
if holder == false then
    local token = redis.call('INCR', KEYS[2])
    redis.call('HSET', KEYS[1], 'holder', ARGV[1], 'token', token, 'acquired', ARGV[3])
    redis.call('PEXPIRE', KEYS[1], ARGV[2])
    return { 'acquired', token, ARGV[3] }
end
if holder == ARGV[1] then
    redis.call('PEXPIRE', KEYS[1], ARGV[2])
    local token = redis.call('HGET', KEYS[1], 'token')
    local acquired = redis.call('HGET', KEYS[1], 'acquired')
    return { 'renewed', token, acquired }
end
return { 'denied', holder }";

    // KEYS[1] = lease hash. ARGV[1] = candidateId. Returns 1 if released by the holder, else 0.
    // The fence counter is intentionally left in place so the next acquisition still advances past
    // this term's token, matching the in-memory store's behaviour across a handover.
    private const string ReleaseScript = @"
if redis.call('HGET', KEYS[1], 'holder') == ARGV[1] then
    return redis.call('DEL', KEYS[1])
end
return 0";

    private readonly IConnectionMultiplexer multiplexer;
    private readonly RedisLeaseStoreOptions options;

    /// <summary>Create the store over an existing Redis connection.</summary>
    /// <param name="multiplexer">The shared Redis connection.</param>
    /// <param name="options">Key-naming and database options, or null for the defaults.</param>
    public RedisLeaseStore(IConnectionMultiplexer multiplexer, RedisLeaseStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        this.multiplexer = multiplexer;
        this.options = options ?? new RedisLeaseStoreOptions();
    }

    private IDatabase Db => multiplexer.GetDatabase(options.Database);

    private RedisKey LeaseKey(string resource) => options.KeyPrefix + resource;

    private RedisKey FenceKey(string resource) => options.KeyPrefix + resource + ":fence";

    /// <inheritdoc />
    public async Task<LeaseAcquisition> TryAcquireOrRenewAsync(
        string resource, string candidateId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Lease duration must be positive.");
        }

        var ttlMs = (long)duration.TotalMilliseconds;
        if (ttlMs <= 0)
        {
            // A sub-millisecond positive duration would round to a zero PEXPIRE, which Redis rejects;
            // floor it at the smallest TTL Redis can honour so the lease still expires on its own.
            ttlMs = 1;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = await Db.ScriptEvaluateAsync(
            AcquireOrRenewScript,
            new[] { LeaseKey(resource), FenceKey(resource) },
            new RedisValue[] { candidateId, ttlMs, nowMs }).ConfigureAwait(false);

        var values = (RedisValue[]?)result
            ?? throw new InvalidOperationException("The Redis acquire-or-renew script returned no result.");
        var outcome = (string?)values[0];

        switch (outcome)
        {
            case "acquired":
            case "renewed":
                {
                    var token = (long)values[1];
                    var acquiredMs = (long)values[2];
                    var acquiredAt = DateTimeOffset.FromUnixTimeMilliseconds(acquiredMs);
                    var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(nowMs) + duration;
                    var lease = new Lease(resource, candidateId, token, acquiredAt, expiresAt);
                    return outcome == "acquired"
                        ? LeaseAcquisition.Acquired(lease)
                        : LeaseAcquisition.Renewed(lease);
                }

            case "denied":
                {
                    var holderId = (string?)values[1]
                        ?? throw new InvalidOperationException("The Redis acquire-or-renew script denied without a holder.");
                    return LeaseAcquisition.Denied(holderId);
                }

            default:
                throw new InvalidOperationException(
                    $"The Redis acquire-or-renew script returned an unexpected outcome '{outcome}'.");
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string resource, string candidateId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);

        await Db.ScriptEvaluateAsync(
            ReleaseScript,
            new[] { LeaseKey(resource) },
            new RedisValue[] { candidateId }).ConfigureAwait(false);
    }
}
