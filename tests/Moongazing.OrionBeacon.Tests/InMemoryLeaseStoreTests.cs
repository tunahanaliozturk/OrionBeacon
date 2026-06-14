namespace Moongazing.OrionBeacon.Tests;

using Moongazing.OrionBeacon.Leasing;

using Xunit;

public sealed class InMemoryLeaseStoreTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task A_free_resource_is_acquired_with_the_first_fencing_token()
    {
        var store = new InMemoryLeaseStore();
        var result = await store.TryAcquireOrRenewAsync("res", "a", Lease);

        Assert.Equal(LeaseOutcome.Acquired, result.Outcome);
        Assert.Equal(1, result.Lease!.FencingToken);
        Assert.Equal("a", result.Lease.HolderId);
    }

    [Fact]
    public async Task The_holder_renews_with_the_same_token_and_a_later_expiry()
    {
        var clock = new MutableClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var store = new InMemoryLeaseStore(() => clock.Now);
        var first = await store.TryAcquireOrRenewAsync("res", "a", Lease);

        clock.Advance(TimeSpan.FromSeconds(5));
        var renewed = await store.TryAcquireOrRenewAsync("res", "a", Lease);

        Assert.Equal(LeaseOutcome.Renewed, renewed.Outcome);
        Assert.Equal(first.Lease!.FencingToken, renewed.Lease!.FencingToken);
        Assert.True(renewed.Lease.ExpiresAt > first.Lease.ExpiresAt);
    }

    [Fact]
    public async Task Another_candidate_is_denied_while_the_lease_is_held()
    {
        var store = new InMemoryLeaseStore();
        await store.TryAcquireOrRenewAsync("res", "a", Lease);

        var denied = await store.TryAcquireOrRenewAsync("res", "b", Lease);

        Assert.Equal(LeaseOutcome.Denied, denied.Outcome);
        Assert.Equal("a", denied.HolderId);
    }

    [Fact]
    public async Task After_expiry_another_candidate_takes_over_and_the_token_increments()
    {
        var clock = new MutableClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var store = new InMemoryLeaseStore(() => clock.Now);
        await store.TryAcquireOrRenewAsync("res", "a", Lease);

        clock.Advance(TimeSpan.FromSeconds(16));
        var takeover = await store.TryAcquireOrRenewAsync("res", "b", Lease);

        Assert.Equal(LeaseOutcome.Acquired, takeover.Outcome);
        Assert.Equal("b", takeover.Lease!.HolderId);
        Assert.Equal(2, takeover.Lease.FencingToken);
    }

    [Fact]
    public async Task Releasing_lets_a_follower_take_over_immediately_with_a_higher_token()
    {
        var store = new InMemoryLeaseStore();
        await store.TryAcquireOrRenewAsync("res", "a", Lease);

        await store.ReleaseAsync("res", "a");
        var takeover = await store.TryAcquireOrRenewAsync("res", "b", Lease);

        Assert.Equal(LeaseOutcome.Acquired, takeover.Outcome);
        Assert.Equal(2, takeover.Lease!.FencingToken);
    }

    [Fact]
    public async Task Releasing_a_lease_you_do_not_hold_is_a_noop()
    {
        var store = new InMemoryLeaseStore();
        await store.TryAcquireOrRenewAsync("res", "a", Lease);

        await store.ReleaseAsync("res", "b");
        var denied = await store.TryAcquireOrRenewAsync("res", "b", Lease);

        Assert.Equal(LeaseOutcome.Denied, denied.Outcome);
        Assert.Equal("a", denied.HolderId);
    }
}
