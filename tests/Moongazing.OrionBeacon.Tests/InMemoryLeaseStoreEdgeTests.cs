namespace Moongazing.OrionBeacon.Tests;

using Moongazing.OrionBeacon.Leasing;

using Xunit;

/// <summary>
/// Edge, error, and invariant coverage for <see cref="InMemoryLeaseStore"/> beyond the happy-path
/// suite in <c>InMemoryLeaseStoreTests</c>: argument guards, cancellation, resource isolation, and
/// fencing-token monotonicity across repeated handovers.
/// </summary>
public sealed class InMemoryLeaseStoreEdgeTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(15);

    [Fact]
    public void The_internal_clock_constructor_rejects_a_null_clock()
    {
        Assert.Throws<ArgumentNullException>(() => new InMemoryLeaseStore((Func<DateTimeOffset>)null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Acquire_rejects_a_null_or_empty_resource(string? resource)
    {
        var store = new InMemoryLeaseStore();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => store.TryAcquireOrRenewAsync(resource!, "a", Lease));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Acquire_rejects_a_null_or_empty_candidate(string? candidate)
    {
        var store = new InMemoryLeaseStore();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => store.TryAcquireOrRenewAsync("res", candidate!, Lease));
    }

    [Fact]
    public async Task Acquire_rejects_a_whitespace_resource()
    {
        // The store now guards with ArgumentException.ThrowIfNullOrWhiteSpace, so a whitespace-only
        // resource is rejected as an invalid identity rather than silently leased.
        var store = new InMemoryLeaseStore();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => store.TryAcquireOrRenewAsync("   ", "a", Lease));
    }

    [Fact]
    public async Task Acquire_rejects_a_whitespace_candidate()
    {
        var store = new InMemoryLeaseStore();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => store.TryAcquireOrRenewAsync("res", "   ", Lease));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Acquire_rejects_a_non_positive_duration(int seconds)
    {
        var store = new InMemoryLeaseStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.TryAcquireOrRenewAsync("res", "a", TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Release_rejects_a_null_or_empty_resource(string? resource)
    {
        var store = new InMemoryLeaseStore();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => store.ReleaseAsync(resource!, "a"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Release_rejects_a_null_or_empty_candidate(string? candidate)
    {
        var store = new InMemoryLeaseStore();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => store.ReleaseAsync("res", candidate!));
    }

    [Fact]
    public async Task Candidate_identity_is_compared_case_sensitively()
    {
        // HolderId equality uses StringComparison.Ordinal, so "A" is a different candidate than "a"
        // and is denied while "a" holds the lease.
        var store = new InMemoryLeaseStore();
        await store.TryAcquireOrRenewAsync("res", "a", Lease);

        var rival = await store.TryAcquireOrRenewAsync("res", "A", Lease);

        Assert.Equal(LeaseOutcome.Denied, rival.Outcome);
        Assert.Equal("a", rival.HolderId);
    }

    [Fact]
    public async Task Distinct_resources_are_leased_independently()
    {
        var store = new InMemoryLeaseStore();

        var first = await store.TryAcquireOrRenewAsync("res-1", "a", Lease);
        var second = await store.TryAcquireOrRenewAsync("res-2", "b", Lease);

        Assert.Equal(LeaseOutcome.Acquired, first.Outcome);
        Assert.Equal(LeaseOutcome.Acquired, second.Outcome);
        // Each resource starts its own fencing sequence at 1.
        Assert.Equal(1, first.Lease!.FencingToken);
        Assert.Equal(1, second.Lease!.FencingToken);
    }

    [Fact]
    public async Task The_fencing_token_climbs_monotonically_across_repeated_handovers()
    {
        var clock = new MutableClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var store = new InMemoryLeaseStore(() => clock.Now);

        long previous = 0;
        string[] holders = ["a", "b", "c", "a", "b"];
        foreach (var holder in holders)
        {
            var acquired = await store.TryAcquireOrRenewAsync("res", holder, Lease);
            Assert.Equal(LeaseOutcome.Acquired, acquired.Outcome);
            Assert.True(acquired.Lease!.FencingToken > previous,
                $"token {acquired.Lease.FencingToken} should exceed {previous}");
            previous = acquired.Lease.FencingToken;

            // Let the lease lapse so the next holder takes a fresh term.
            clock.Advance(TimeSpan.FromSeconds(16));
        }

        Assert.Equal(holders.Length, previous);
    }

    [Fact]
    public async Task Release_then_reacquire_by_the_same_holder_still_advances_the_token()
    {
        // The release leaves an expired tombstone so the token keeps climbing rather than resetting,
        // even when the same candidate immediately reacquires.
        var store = new InMemoryLeaseStore();
        var first = await store.TryAcquireOrRenewAsync("res", "a", Lease);

        await store.ReleaseAsync("res", "a");
        var reacquired = await store.TryAcquireOrRenewAsync("res", "a", Lease);

        Assert.Equal(LeaseOutcome.Acquired, reacquired.Outcome);
        Assert.Equal(first.Lease!.FencingToken + 1, reacquired.Lease!.FencingToken);
    }

    [Fact]
    public async Task Renewing_preserves_both_the_token_and_the_original_acquired_timestamp()
    {
        var clock = new MutableClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var store = new InMemoryLeaseStore(() => clock.Now);
        var first = await store.TryAcquireOrRenewAsync("res", "a", Lease);

        clock.Advance(TimeSpan.FromSeconds(5));
        var renewed = await store.TryAcquireOrRenewAsync("res", "a", Lease);

        Assert.Equal(LeaseOutcome.Renewed, renewed.Outcome);
        Assert.Equal(first.Lease!.FencingToken, renewed.Lease!.FencingToken);
        Assert.Equal(first.Lease.AcquiredAt, renewed.Lease.AcquiredAt);
        Assert.True(renewed.Lease.ExpiresAt > first.Lease.ExpiresAt);
    }

    [Fact]
    public async Task A_lease_exactly_at_its_expiry_instant_is_treated_as_lapsed()
    {
        // Expiry uses a strict ExpiresAt > now comparison, so the boundary instant counts as expired
        // and a rival takes over there.
        var clock = new MutableClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var store = new InMemoryLeaseStore(() => clock.Now);
        await store.TryAcquireOrRenewAsync("res", "a", Lease);

        clock.Advance(Lease); // now == ExpiresAt
        var rival = await store.TryAcquireOrRenewAsync("res", "b", Lease);

        Assert.Equal(LeaseOutcome.Acquired, rival.Outcome);
        Assert.Equal("b", rival.Lease!.HolderId);
    }

    [Fact]
    public async Task Acquire_honors_an_already_cancelled_token()
    {
        // The store now checks the token at the start of the operation, so an already-cancelled
        // token throws rather than silently completing the acquire.
        var store = new InMemoryLeaseStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.TryAcquireOrRenewAsync("res", "a", Lease, cts.Token));
    }

    [Fact]
    public async Task Release_honors_an_already_cancelled_token()
    {
        var store = new InMemoryLeaseStore();
        await store.TryAcquireOrRenewAsync("res", "a", Lease);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.ReleaseAsync("res", "a", cts.Token));
    }

    [Fact]
    public async Task An_expired_lease_fails_over_to_a_waiting_candidate_via_an_injected_TimeProvider()
    {
        // Drive failover purely by time: the holder acquires, the controllable clock advances past
        // the lease duration with no real delay, and the next cycle lets the waiting rival win with
        // an incremented fencing token. This exercises the public TimeProvider constructor.
        var time = new ControllableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var store = new InMemoryLeaseStore(time);

        var held = await store.TryAcquireOrRenewAsync("res", "leader", Lease);
        Assert.Equal(LeaseOutcome.Acquired, held.Outcome);

        // While the lease is live the rival is a follower.
        var denied = await store.TryAcquireOrRenewAsync("res", "rival", Lease);
        Assert.Equal(LeaseOutcome.Denied, denied.Outcome);
        Assert.Equal("leader", denied.HolderId);

        // The leader goes dark; the lease lapses purely by the clock moving forward.
        time.Advance(Lease + TimeSpan.FromTicks(1));

        var failover = await store.TryAcquireOrRenewAsync("res", "rival", Lease);
        Assert.Equal(LeaseOutcome.Acquired, failover.Outcome);
        Assert.Equal("rival", failover.Lease!.HolderId);
        Assert.Equal(held.Lease!.FencingToken + 1, failover.Lease.FencingToken);
    }

    [Fact]
    public void The_TimeProvider_constructor_rejects_a_null_provider()
    {
        Assert.Throws<ArgumentNullException>(() => new InMemoryLeaseStore((TimeProvider)null!));
    }
}
