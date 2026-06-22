using Moongazing.OrionBeacon.Stores.Redis;

using StackExchange.Redis;

namespace Moongazing.OrionBeacon.Conformance.Tests;

/// <summary>
/// White-box tests for the Redis key builders that make leader election work on a Redis Cluster. The
/// acquire-or-renew script is a two-key command over the lease hash and the fencing counter, and a
/// cluster only allows a multi-key command when every key maps to the same hash slot. These tests
/// assert the two builders wrap the resource in one shared Redis hash tag so both keys hash to the
/// same slot, while staying otherwise distinct, and that a resource which would corrupt the tag is
/// rejected rather than silently de-colocated.
/// </summary>
/// <remarks>
/// These assertions are about the key strings themselves, which are not observable through the
/// <c>ILeaseStore</c> surface, so they call the internal builders directly. They need no Docker: no
/// Redis command is issued, so this file runs everywhere the suite compiles, unlike the
/// container-backed conformance run.
/// </remarks>
public sealed class RedisLeaseKeyTests
{
    public static TheoryData<string, string> PrefixAndResource() => new()
    {
        { "orionbeacon:lease:", "jobs" },
        { "orionbeacon:lease:", "res-0123456789abcdef" },
        { string.Empty, "jobs" },
        { "team:billing:", "invoice-sweep" },
        { "p:", "a" },
    };

    /// <summary>
    /// Returns the hash-tag body Redis Cluster would hash for a key: the substring between the first
    /// <c>{</c> and the first <c>}</c> after it, when that body is non-empty; otherwise the whole key
    /// (Redis falls back to hashing the entire key when there is no non-empty tag). This mirrors the
    /// Redis key-hashing rule so the test reasons about slot placement exactly as the server would.
    /// </summary>
    private static string HashSlotInput(string key)
    {
        var open = key.IndexOf('{', StringComparison.Ordinal);
        if (open < 0)
        {
            return key;
        }

        var close = key.IndexOf('}', open + 1);
        if (close < 0 || close == open + 1)
        {
            // No closing brace, or an empty "{}" - Redis hashes the whole key in both cases.
            return key;
        }

        return key.Substring(open + 1, close - open - 1);
    }

    [Theory]
    [MemberData(nameof(PrefixAndResource))]
    public void The_lease_and_fence_keys_share_one_hash_tag_so_they_co_locate(string prefix, string resource)
    {
        string lease = RedisLeaseStore.BuildLeaseKey(prefix, resource)!;
        string fence = RedisLeaseStore.BuildFenceKey(prefix, resource)!;

        // The substring Redis Cluster hashes must be identical for both keys (this is what puts the
        // lease hash and the fencing counter on the same slot), and it must be exactly the resource -
        // proving only the resource, not the prefix, drives slot placement.
        string leaseTag = HashSlotInput(lease);
        string fenceTag = HashSlotInput(fence);

        Assert.Equal(leaseTag, fenceTag);
        Assert.Equal(resource, leaseTag);
    }

    [Theory]
    [MemberData(nameof(PrefixAndResource))]
    public void The_lease_and_fence_keys_are_otherwise_distinct(string prefix, string resource)
    {
        string lease = RedisLeaseStore.BuildLeaseKey(prefix, resource)!;
        string fence = RedisLeaseStore.BuildFenceKey(prefix, resource)!;

        // Same slot, but they must not be the same key, or the fencing counter and the lease hash
        // would collide. The fence key is the lease key with the counter suffix.
        Assert.NotEqual(lease, fence);
        Assert.Equal(lease + ":fence", fence);
    }

    [Fact]
    public void The_resource_is_wrapped_in_exactly_one_hash_tag()
    {
        string lease = RedisLeaseStore.BuildLeaseKey("orionbeacon:lease:", "jobs")!;

        // Exactly one braced section, and it is the resource: any second tag would change which
        // section Redis hashes and could split the two keys across slots.
        Assert.Equal("orionbeacon:lease:{jobs}", lease);
        Assert.Equal(1, lease.Count(c => c == '{'));
        Assert.Equal(1, lease.Count(c => c == '}'));
    }

    [Theory]
    [InlineData("jobs{evil")]
    [InlineData("jobs}evil")]
    [InlineData("{")]
    [InlineData("a{b}c")]
    public async Task A_resource_containing_a_brace_is_rejected_before_any_redis_call(string resource)
    {
        // A brace in the resource would inject a second (or empty) hash tag and silently break the
        // lease/fence co-location, so the store must reject it. Validation runs before any Redis
        // command, so an unconnected multiplexer is never contacted - the throw proves the guard
        // fires first.
        await using var mux = await ConnectionMultiplexer.ConnectAsync(
            new ConfigurationOptions { EndPoints = { "127.0.0.1:1" }, AbortOnConnectFail = false });
        var store = new RedisLeaseStore(mux);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.TryAcquireOrRenewAsync(resource, "candidate", TimeSpan.FromSeconds(30)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.ReleaseAsync(resource, "candidate"));
    }
}
