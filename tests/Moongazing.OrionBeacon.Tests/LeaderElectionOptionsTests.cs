namespace Moongazing.OrionBeacon.Tests;

using Moongazing.OrionBeacon.Election;

using Xunit;

// Validate() is internal; it is exercised through AddOrionBeacon and the LeaderElector/service
// constructors elsewhere. Here it is driven through AddOrionBeacon, the public surface that calls
// it, so each validation branch is covered without InternalsVisibleTo.
public sealed class LeaderElectionOptionsTests
{
    private static LeaderElectionOptions Valid() => new()
    {
        ResourceName = "res",
        CandidateId = "candidate",
        LeaseDuration = TimeSpan.FromSeconds(15),
        RenewInterval = TimeSpan.FromSeconds(5),
    };

    [Fact]
    public void Defaults_are_internally_consistent_and_pass_validation()
    {
        var options = new LeaderElectionOptions();

        Assert.Equal("orion-leader", options.ResourceName);
        Assert.False(string.IsNullOrWhiteSpace(options.CandidateId));
        Assert.Equal(TimeSpan.FromSeconds(15), options.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RenewInterval);
        Assert.True(options.RenewInterval < options.LeaseDuration);

        // The default-constructed options must be valid (they back AddOrionBeacon with no configure).
        Validate(options);
    }

    [Fact]
    public void A_fully_specified_valid_set_passes()
    {
        Validate(Valid());
    }

    [Fact]
    public void An_empty_resource_name_is_rejected()
    {
        var options = Valid();
        options.ResourceName = "";

        Assert.Throws<ArgumentException>(() => Validate(options));
    }

    [Fact]
    public void A_null_resource_name_is_rejected()
    {
        var options = Valid();
        options.ResourceName = null!;

        Assert.Throws<ArgumentNullException>(() => Validate(options));
    }

    [Fact]
    public void An_empty_candidate_id_is_rejected()
    {
        var options = Valid();
        options.CandidateId = "";

        Assert.Throws<ArgumentException>(() => Validate(options));
    }

    [Fact]
    public void A_null_candidate_id_is_rejected()
    {
        var options = Valid();
        options.CandidateId = null!;

        Assert.Throws<ArgumentNullException>(() => Validate(options));
    }

    [Fact]
    public void A_zero_lease_duration_is_rejected()
    {
        var options = Valid();
        options.LeaseDuration = TimeSpan.Zero;

        Assert.Throws<ArgumentOutOfRangeException>(() => Validate(options));
    }

    [Fact]
    public void A_negative_lease_duration_is_rejected()
    {
        var options = Valid();
        options.LeaseDuration = TimeSpan.FromSeconds(-1);

        Assert.Throws<ArgumentOutOfRangeException>(() => Validate(options));
    }

    [Fact]
    public void A_zero_renew_interval_is_rejected()
    {
        var options = Valid();
        options.RenewInterval = TimeSpan.Zero;

        Assert.Throws<ArgumentOutOfRangeException>(() => Validate(options));
    }

    [Fact]
    public void A_negative_renew_interval_is_rejected()
    {
        var options = Valid();
        options.RenewInterval = TimeSpan.FromSeconds(-1);

        Assert.Throws<ArgumentOutOfRangeException>(() => Validate(options));
    }

    [Fact]
    public void A_renew_interval_equal_to_the_lease_is_rejected()
    {
        var options = Valid();
        options.LeaseDuration = TimeSpan.FromSeconds(10);
        options.RenewInterval = TimeSpan.FromSeconds(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => Validate(options));
    }

    [Fact]
    public void A_renew_interval_longer_than_the_lease_is_rejected()
    {
        var options = Valid();
        options.LeaseDuration = TimeSpan.FromSeconds(10);
        options.RenewInterval = TimeSpan.FromSeconds(11);

        Assert.Throws<ArgumentOutOfRangeException>(() => Validate(options));
    }

    // Drives the internal Validate() through the LeaderElector constructor, which calls it and
    // surfaces the exact same exceptions. A throwing case leaves no elector; a valid case builds one.
    private static void Validate(LeaderElectionOptions options)
    {
        using var diagnostics = new Moongazing.OrionBeacon.Diagnostics.LeaderElectionDiagnostics();
        _ = new LeaderElector(new Moongazing.OrionBeacon.Leasing.InMemoryLeaseStore(), options, diagnostics);
    }
}
