using Moongazing.OrionBeacon.Leasing;
using Moongazing.OrionBeacon.Stores.Redis;

using StackExchange.Redis;

namespace Moongazing.OrionBeacon.Conformance.Tests;

/// <summary>
/// Runs the shared <see cref="LeaseStoreConformanceTests"/> against the real
/// <see cref="RedisLeaseStore"/> over a Redis container (Testcontainers). Passing the same suite the
/// in-memory store passes is what proves the distributed store honours the contract: atomic
/// acquire-or-renew, a strictly increasing fencing token across leadership changes, lease expiry,
/// and fencing-checked release.
/// </summary>
/// <remarks>
/// Requires Docker. Every fixture uses a unique key prefix so a reused container cannot leak lease
/// or fencing-counter state between runs, and every test already uses a unique resource name.
/// </remarks>
public sealed class RedisLeaseStoreConformanceTests : LeaseStoreConformanceTests, IClassFixture<RedisContainerFixture>
{
    private readonly IConnectionMultiplexer mux;
    private readonly string keyPrefix = "ob-conf:" + Guid.NewGuid().ToString("N") + ":";

    public RedisLeaseStoreConformanceTests(RedisContainerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        mux = fixture.Mux;
    }

    /// <inheritdoc />
    protected override ILeaseStore CreateStore()
        => new RedisLeaseStore(mux, new RedisLeaseStoreOptions { KeyPrefix = keyPrefix });
}
