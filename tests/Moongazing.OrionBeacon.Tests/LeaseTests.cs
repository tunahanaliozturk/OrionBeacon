namespace Moongazing.OrionBeacon.Tests;

using Moongazing.OrionBeacon.Leasing;

using Xunit;

public sealed class LeaseTests
{
    private static readonly DateTimeOffset Acquired = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void Constructor_exposes_all_supplied_values()
    {
        var expires = Acquired + TimeSpan.FromSeconds(15);
        var lease = new Lease("res", "holder", 7, Acquired, expires);

        Assert.Equal("res", lease.Resource);
        Assert.Equal("holder", lease.HolderId);
        Assert.Equal(7, lease.FencingToken);
        Assert.Equal(Acquired, lease.AcquiredAt);
        Assert.Equal(expires, lease.ExpiresAt);
    }

    [Fact]
    public void A_null_resource_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new Lease(null!, "holder", 1, Acquired, Acquired));
    }

    [Fact]
    public void An_empty_resource_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new Lease("", "holder", 1, Acquired, Acquired));
    }

    [Fact]
    public void A_null_holder_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new Lease("res", null!, 1, Acquired, Acquired));
    }

    [Fact]
    public void An_empty_holder_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new Lease("res", "", 1, Acquired, Acquired));
    }

    [Fact]
    public void A_whitespace_resource_is_rejected()
    {
        // The Lease guard uses ArgumentException.ThrowIfNullOrWhiteSpace, so a whitespace-only
        // resource is an invalid identity and is rejected just like null or empty.
        Assert.Throws<ArgumentException>(() =>
            new Lease("   ", "holder", 1, Acquired, Acquired));
    }

    [Fact]
    public void A_whitespace_holder_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new Lease("res", "   ", 1, Acquired, Acquired));
    }

    [Fact]
    public void A_negative_fencing_token_is_not_rejected_by_the_constructor()
    {
        // The constructor performs no range check on the fencing token. The store is responsible
        // for only ever emitting positive, increasing tokens; the type itself does not enforce it.
        var lease = new Lease("res", "holder", -5, Acquired, Acquired);
        Assert.Equal(-5, lease.FencingToken);
    }
}
