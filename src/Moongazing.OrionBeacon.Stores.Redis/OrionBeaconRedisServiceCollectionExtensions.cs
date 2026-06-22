using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moongazing.OrionBeacon.Leasing;

using StackExchange.Redis;

namespace Moongazing.OrionBeacon.Stores.Redis;

/// <summary>
/// Registration helpers that back OrionBeacon leader election with the Redis
/// <see cref="ILeaseStore"/>. Call one of these before <c>AddOrionBeacon()</c>: that method only
/// adds the in-process store if no <see cref="ILeaseStore"/> is already registered, so registering
/// the Redis store first makes election span the cluster.
/// </summary>
public static class OrionBeaconRedisServiceCollectionExtensions
{
    /// <summary>
    /// Register the Redis <see cref="ILeaseStore"/> over a connection opened from
    /// <paramref name="connectionString"/>. The connection is added as a singleton
    /// <see cref="IConnectionMultiplexer"/> only if one is not already registered, so a
    /// consumer-supplied connection wins.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The StackExchange.Redis connection string.</param>
    /// <param name="configure">Optional store options (key prefix, database).</param>
    public static IServiceCollection AddOrionBeaconRedisStore(
        this IServiceCollection services,
        string connectionString,
        Action<RedisLeaseStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(connectionString));

        return services.AddOrionBeaconRedisStore(configure);
    }

    /// <summary>
    /// Register the Redis <see cref="ILeaseStore"/> over an already-registered
    /// <see cref="IConnectionMultiplexer"/>. Use this overload when the application already manages
    /// its own Redis connection. The store is added with <c>TryAddSingleton</c>, so a
    /// consumer-supplied <see cref="ILeaseStore"/> registered earlier wins.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional store options (key prefix, database).</param>
    public static IServiceCollection AddOrionBeaconRedisStore(
        this IServiceCollection services,
        Action<RedisLeaseStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RedisLeaseStoreOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<ILeaseStore>(
            sp => new RedisLeaseStore(sp.GetRequiredService<IConnectionMultiplexer>(), options));

        return services;
    }
}
