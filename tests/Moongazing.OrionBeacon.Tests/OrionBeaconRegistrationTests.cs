namespace Moongazing.OrionBeacon.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Moongazing.OrionBeacon;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;

using Xunit;

public sealed class OrionBeaconRegistrationTests
{
    [Fact]
    public void AddOrionBeacon_resolves_an_elector()
    {
        var services = new ServiceCollection();
        services.AddOrionBeacon();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ILeaderElector>());
    }

    [Fact]
    public void AddOrionBeacon_registers_the_in_memory_store_by_default()
    {
        var services = new ServiceCollection();
        services.AddOrionBeacon();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<InMemoryLeaseStore>(provider.GetService<ILeaseStore>());
    }

    [Fact]
    public void AddOrionBeacon_registers_the_hosted_election_loop()
    {
        var services = new ServiceCollection();
        services.AddOrionBeacon();

        using var provider = services.BuildServiceProvider();
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is LeaderElectionService);
    }

    [Fact]
    public void AddOrionBeacon_honours_configured_options()
    {
        var services = new ServiceCollection();
        services.AddOrionBeacon(o => o.ResourceName = "jobs");

        using var provider = services.BuildServiceProvider();
        Assert.Equal("jobs", provider.GetRequiredService<LeaderElectionOptions>().ResourceName);
    }

    [Fact]
    public void AddOrionBeacon_rejects_a_renew_interval_that_is_not_shorter_than_the_lease()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddOrionBeacon(o =>
            {
                o.LeaseDuration = TimeSpan.FromSeconds(10);
                o.RenewInterval = TimeSpan.FromSeconds(10);
            }));
    }
}
