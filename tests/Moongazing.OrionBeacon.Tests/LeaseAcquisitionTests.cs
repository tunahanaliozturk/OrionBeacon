namespace Moongazing.OrionBeacon.Tests;

using Moongazing.OrionBeacon.Leasing;

using Xunit;

// LeaseAcquisition's Acquired/Renewed/Denied factories are public so an out-of-assembly ILeaseStore
// (for example the Redis store) can build a result; these tests still exercise them through the
// public InMemoryLeaseStore so the assertions hold on real, reachable behavior rather than a
// hand-built result.
public sealed class LeaseAcquisitionTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task An_acquired_outcome_carries_the_lease_its_holder_and_is_held()
    {
        var store = new InMemoryLeaseStore();

        var acquisition = await store.TryAcquireOrRenewAsync("res", "a", Lease);

        Assert.Equal(LeaseOutcome.Acquired, acquisition.Outcome);
        Assert.NotNull(acquisition.Lease);
        Assert.Equal("a", acquisition.HolderId);
        Assert.Equal("a", acquisition.Lease!.HolderId);
        Assert.True(acquisition.IsHeld);
    }

    [Fact]
    public async Task A_renewed_outcome_carries_the_lease_its_holder_and_is_held()
    {
        var store = new InMemoryLeaseStore();
        await store.TryAcquireOrRenewAsync("res", "a", Lease);

        var acquisition = await store.TryAcquireOrRenewAsync("res", "a", Lease);

        Assert.Equal(LeaseOutcome.Renewed, acquisition.Outcome);
        Assert.NotNull(acquisition.Lease);
        Assert.Equal("a", acquisition.HolderId);
        Assert.True(acquisition.IsHeld);
    }

    [Fact]
    public async Task A_denied_outcome_carries_the_rival_holder_has_no_lease_and_is_not_held()
    {
        var store = new InMemoryLeaseStore();
        await store.TryAcquireOrRenewAsync("res", "a", Lease);

        var acquisition = await store.TryAcquireOrRenewAsync("res", "b", Lease);

        Assert.Equal(LeaseOutcome.Denied, acquisition.Outcome);
        Assert.Null(acquisition.Lease);
        Assert.Equal("a", acquisition.HolderId);
        Assert.False(acquisition.IsHeld);
    }
}
