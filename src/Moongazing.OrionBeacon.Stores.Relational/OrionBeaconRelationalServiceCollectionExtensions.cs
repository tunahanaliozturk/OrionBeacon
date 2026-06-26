using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moongazing.OrionBeacon.Leasing;

using Npgsql;

namespace Moongazing.OrionBeacon.Stores.Relational;

/// <summary>
/// Registration helpers that back OrionBeacon leader election with the relational
/// <see cref="ILeaseStore"/> over PostgreSQL or SQL Server. Call one of these before
/// <c>AddOrionBeacon()</c>: that method only adds the in-process store if no
/// <see cref="ILeaseStore"/> is already registered, so registering a relational store first makes
/// election span the cluster.
/// </summary>
public static class OrionBeaconRelationalServiceCollectionExtensions
{
    /// <summary>
    /// Register the relational <see cref="ILeaseStore"/> over PostgreSQL, opening a short-lived
    /// <c>NpgsqlConnection</c> per operation from <paramref name="connectionString"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Npgsql connection string.</param>
    /// <param name="configure">Optional store options (table name, command timeout).</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddOrionBeaconPostgresStore(
        this IServiceCollection services,
        string connectionString,
        Action<RelationalLeaseStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = BuildOptions(RelationalProvider.Postgres, configure);
        services.TryAddSingleton<ILeaseStore>(
            _ => new RelationalLeaseStore(() => new NpgsqlConnection(connectionString), options));
        return services;
    }

    /// <summary>
    /// Register the relational <see cref="ILeaseStore"/> over SQL Server, opening a short-lived
    /// <c>SqlConnection</c> per operation from <paramref name="connectionString"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Microsoft.Data.SqlClient connection string.</param>
    /// <param name="configure">Optional store options (table name, command timeout).</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddOrionBeaconSqlServerStore(
        this IServiceCollection services,
        string connectionString,
        Action<RelationalLeaseStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = BuildOptions(RelationalProvider.SqlServer, configure);
        services.TryAddSingleton<ILeaseStore>(
            _ => new RelationalLeaseStore(() => new SqlConnection(connectionString), options));
        return services;
    }

    private static RelationalLeaseStoreOptions BuildOptions(
        RelationalProvider provider, Action<RelationalLeaseStoreOptions>? configure)
    {
        var options = new RelationalLeaseStoreOptions();
        configure?.Invoke(options);
        // The provider is fixed by the entry point, so a caller cannot point the SQL Server dialect at
        // an Npgsql connection by leaving Provider at its default.
        options.Provider = provider;
        return options;
    }
}
