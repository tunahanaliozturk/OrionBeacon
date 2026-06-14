namespace Moongazing.OrionBeacon;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// Registration helpers for OrionBeacon.
/// </summary>
public static class OrionBeaconServiceCollectionExtensions
{
    /// <summary>
    /// Register the elector, its options and diagnostics, an <see cref="InMemoryLeaseStore"/>, and
    /// the hosted election loop. To elect across a cluster, register your own
    /// <see cref="ILeaseStore"/> over a shared backing store before this call; the in-memory store
    /// is only added if none is present. Register an
    /// <see cref="Observers.ILeadershipObserver"/> to receive leadership-change events.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional election configuration.</param>
    public static IServiceCollection AddOrionBeacon(
        this IServiceCollection services,
        Action<LeaderElectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new LeaderElectionOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton<LeaderElectionDiagnostics>();
        services.TryAddSingleton<ILeaseStore, InMemoryLeaseStore>();

        services.TryAddSingleton<ILeaderElector>(sp => new LeaderElector(
            sp.GetRequiredService<ILeaseStore>(),
            sp.GetRequiredService<LeaderElectionOptions>(),
            sp.GetRequiredService<LeaderElectionDiagnostics>(),
            sp.GetService<Observers.ILeadershipObserver>()));

        services.AddHostedService<LeaderElectionService>();

        return services;
    }
}
