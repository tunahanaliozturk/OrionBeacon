namespace Moongazing.OrionBeacon.Tests;

using Microsoft.Extensions.DependencyInjection;

using Moongazing.OrionBeacon;
using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;
using Moongazing.OrionBeacon.Observers;

using Xunit;

/// <summary>
/// Wiring guarantees of <see cref="OrionBeaconServiceCollectionExtensions.AddOrionBeacon"/> beyond
/// the basics: argument guards, that a pre-registered store/options survive (TryAdd semantics),
/// singleton lifetimes, observer injection into the elector, and eager options validation.
/// </summary>
public sealed class OrionBeaconRegistrationEdgeTests
{
    [Fact]
    public void AddOrionBeacon_rejects_a_null_service_collection()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() => services.AddOrionBeacon());
    }

    [Fact]
    public void A_custom_lease_store_registered_first_is_preserved()
    {
        var custom = new CustomStore();
        var services = new ServiceCollection();
        services.AddSingleton<ILeaseStore>(custom);

        services.AddOrionBeacon();

        using var provider = services.BuildServiceProvider();
        Assert.Same(custom, provider.GetRequiredService<ILeaseStore>());
    }

    [Fact]
    public void The_diagnostics_and_options_are_registered_as_singletons()
    {
        var services = new ServiceCollection();
        services.AddOrionBeacon();

        using var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetRequiredService<LeaderElectionDiagnostics>(),
            provider.GetRequiredService<LeaderElectionDiagnostics>());
        Assert.Same(
            provider.GetRequiredService<LeaderElectionOptions>(),
            provider.GetRequiredService<LeaderElectionOptions>());
    }

    [Fact]
    public void The_elector_is_registered_as_a_singleton()
    {
        var services = new ServiceCollection();
        services.AddOrionBeacon();

        using var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetRequiredService<ILeaderElector>(),
            provider.GetRequiredService<ILeaderElector>());
    }

    [Fact]
    public async Task A_registered_observer_receives_election_callbacks_from_the_resolved_elector()
    {
        var observer = new CountingObserver();
        var services = new ServiceCollection();
        services.AddSingleton<ILeadershipObserver>(observer);
        services.AddOrionBeacon(o => o.ResourceName = "wired");

        using var provider = services.BuildServiceProvider();
        var elector = provider.GetRequiredService<ILeaderElector>();

        await elector.TryElectAsync();

        Assert.True(elector.IsLeader);
        Assert.Equal(1, observer.Elected);
    }

    [Fact]
    public void Configuration_overrides_are_applied_before_validation()
    {
        var services = new ServiceCollection();
        services.AddOrionBeacon(o =>
        {
            o.ResourceName = "jobs";
            o.CandidateId = "node-7";
            o.LeaseDuration = TimeSpan.FromSeconds(30);
            o.RenewInterval = TimeSpan.FromSeconds(10);
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<LeaderElectionOptions>();
        Assert.Equal("jobs", options.ResourceName);
        Assert.Equal("node-7", options.CandidateId);
        Assert.Equal(TimeSpan.FromSeconds(30), options.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), options.RenewInterval);
    }

    [Fact]
    public void Invalid_configuration_is_rejected_eagerly_at_registration_time()
    {
        var services = new ServiceCollection();

        // Validation happens inside AddOrionBeacon, not lazily at resolve time.
        Assert.Throws<ArgumentException>(() =>
            services.AddOrionBeacon(o => o.ResourceName = ""));
    }

    private sealed class CustomStore : ILeaseStore
    {
        public Task<LeaseAcquisition> TryAcquireOrRenewAsync(
            string resource, string candidateId, TimeSpan duration, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not exercised");

        public Task ReleaseAsync(string resource, string candidateId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CountingObserver : ILeadershipObserver
    {
        public int Elected { get; private set; }

        public void OnElected(Lease lease) => Elected++;

        public void OnDeposed(string resource)
        {
        }
    }
}
