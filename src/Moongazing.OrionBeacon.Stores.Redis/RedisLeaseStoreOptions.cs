namespace Moongazing.OrionBeacon.Stores.Redis;

/// <summary>
/// Options for <see cref="RedisLeaseStore"/>: which logical Redis database to use and how the lease
/// keys are named. Every candidate competing for the same leadership must use the same prefix and
/// database so they contend over the same keys.
/// </summary>
public sealed class RedisLeaseStoreOptions
{
    /// <summary>
    /// The prefix applied to every key the store writes. A resource named <c>jobs</c> with the
    /// default prefix is stored at <c>orionbeacon:lease:jobs</c> (the lease hash) and
    /// <c>orionbeacon:lease:jobs:fence</c> (the fencing-token counter). Default
    /// <c>orionbeacon:lease:</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "orionbeacon:lease:";

    /// <summary>
    /// The Redis logical database index the store reads and writes. Default <c>-1</c>, which uses
    /// the connection's configured default database.
    /// </summary>
    public int Database { get; set; } = -1;
}
