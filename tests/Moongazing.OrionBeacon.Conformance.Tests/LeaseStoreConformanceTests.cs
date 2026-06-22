using Moongazing.OrionBeacon.Leasing;

namespace Moongazing.OrionBeacon.Conformance.Tests;

/// <summary>
/// A reusable behavioural contract that every <see cref="ILeaseStore"/> must satisfy. Derive a
/// fixture from this class and return the store under test from <see cref="CreateStore"/>; xUnit
/// discovers these facts through the derived class, so the same suite runs against any
/// implementation. The in-memory store passing this suite is what validates the suite itself; the
/// Redis store passing it is what proves the distributed store is correct.
/// </summary>
/// <remarks>
/// <para>
/// The four contract rules under test:
/// </para>
/// <list type="number">
/// <item>Exactly one leader at a time: under concurrent acquires for distinct candidates, exactly
/// one is granted the lease and everyone else is denied.</item>
/// <item>The fencing token strictly increases on each leadership change and is stable across a
/// renew by the same holder.</item>
/// <item>A lease expires on its own, so a dead leader's lease lets a new candidate take over.</item>
/// <item>Release is fencing-checked: only the candidate that holds the lease can release it.</item>
/// </list>
/// <para>
/// The suite is deliberately clock-agnostic. It cannot inject a fake clock, because a real store
/// (Redis) expires leases against its own wall clock with no virtual time. Expiry is therefore
/// expressed with a short real lease and a deterministic poll for the reclaimed state rather than a
/// fixed sleep, so the suite is honest about timing on every backing store.
/// </para>
/// </remarks>
public abstract class LeaseStoreConformanceTests
{
    /// <summary>A lease long enough that it will not lapse mid-test on a loaded runner.</summary>
    private static readonly TimeSpan LongLease = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A short lease used by the expiry tests. Kept well above timer granularity so it is reliable,
    /// and the poll that waits for takeover allows many multiples of it before giving up.
    /// </summary>
    private static readonly TimeSpan ShortLease = TimeSpan.FromMilliseconds(300);

    /// <summary>Create the store under test. Each call may return a fresh instance.</summary>
    protected abstract ILeaseStore CreateStore();

    /// <summary>
    /// A resource name unique to each test so cases never alias one another, which matters when one
    /// real backing store (a shared Redis container) serves the whole class.
    /// </summary>
    private static string NewResource() => "res-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Polls <paramref name="probe"/> until it returns true or the timeout lapses, so a test can
    /// await an expiry-driven takeover deterministically instead of sleeping a guessed slack period.
    /// </summary>
    private static async Task<bool> EventuallyAsync(Func<Task<bool>> probe, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await probe().ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        return await probe().ConfigureAwait(false);
    }

    // ---- Rule 1: exactly one leader at a time ----------------------------------------------------

    [Fact]
    public async Task A_free_resource_is_acquired_with_a_held_lease()
    {
        var store = CreateStore();
        var resource = NewResource();

        var result = await store.TryAcquireOrRenewAsync(resource, "a", LongLease);

        Assert.Equal(LeaseOutcome.Acquired, result.Outcome);
        Assert.True(result.IsHeld);
        Assert.NotNull(result.Lease);
        Assert.Equal("a", result.Lease!.HolderId);
        Assert.Equal(resource, result.Lease.Resource);
    }

    [Fact]
    public async Task A_second_candidate_is_denied_while_the_lease_is_held()
    {
        var store = CreateStore();
        var resource = NewResource();
        await store.TryAcquireOrRenewAsync(resource, "a", LongLease);

        var denied = await store.TryAcquireOrRenewAsync(resource, "b", LongLease);

        Assert.Equal(LeaseOutcome.Denied, denied.Outcome);
        Assert.False(denied.IsHeld);
        Assert.Null(denied.Lease);
        Assert.Equal("a", denied.HolderId);
    }

    [Fact]
    public async Task Under_concurrent_acquires_exactly_one_candidate_wins()
    {
        var store = CreateStore();
        var resource = NewResource();
        const int candidates = 50;

        var attempts = Enumerable.Range(0, candidates)
            .Select(i => store.TryAcquireOrRenewAsync(resource, $"cand-{i}", LongLease))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        var winners = results.Where(r => r.Outcome == LeaseOutcome.Acquired).ToArray();
        Assert.Single(winners);

        // Everyone who did not acquire must have been denied (never a second acquire or a renew of a
        // lease they never held), and all denials must name the single winner as the holder.
        var winnerId = winners[0].Lease!.HolderId;
        var losers = results.Where(r => r.Outcome != LeaseOutcome.Acquired).ToArray();
        Assert.Equal(candidates - 1, losers.Length);
        Assert.All(losers, r =>
        {
            Assert.Equal(LeaseOutcome.Denied, r.Outcome);
            Assert.Equal(winnerId, r.HolderId);
        });
    }

    // ---- Rule 2: fencing token monotonic across changes, stable across renew --------------------

    [Fact]
    public async Task The_holder_renews_with_the_same_token()
    {
        var store = CreateStore();
        var resource = NewResource();
        var first = await store.TryAcquireOrRenewAsync(resource, "a", LongLease);

        var renewed = await store.TryAcquireOrRenewAsync(resource, "a", LongLease);

        Assert.Equal(LeaseOutcome.Renewed, renewed.Outcome);
        Assert.Equal(first.Lease!.FencingToken, renewed.Lease!.FencingToken);
    }

    [Fact]
    public async Task The_token_strictly_increases_on_each_change_of_holder()
    {
        var store = CreateStore();
        var resource = NewResource();

        // a takes it, releases; b takes it, releases; c takes it. Each new term must carry a strictly
        // greater token than the one before, including across the release-and-reacquire handover.
        var a = await store.TryAcquireOrRenewAsync(resource, "a", LongLease);
        await store.ReleaseAsync(resource, "a");

        var b = await store.TryAcquireOrRenewAsync(resource, "b", LongLease);
        await store.ReleaseAsync(resource, "b");

        var c = await store.TryAcquireOrRenewAsync(resource, "c", LongLease);

        Assert.Equal(LeaseOutcome.Acquired, a.Outcome);
        Assert.Equal(LeaseOutcome.Acquired, b.Outcome);
        Assert.Equal(LeaseOutcome.Acquired, c.Outcome);
        Assert.True(b.Lease!.FencingToken > a.Lease!.FencingToken,
            $"expected b's token {b.Lease.FencingToken} to exceed a's {a.Lease.FencingToken}");
        Assert.True(c.Lease!.FencingToken > b.Lease.FencingToken,
            $"expected c's token {c.Lease.FencingToken} to exceed b's {b.Lease.FencingToken}");
    }

    [Fact]
    public async Task A_renew_does_not_advance_the_token_for_a_later_takeover()
    {
        var store = CreateStore();
        var resource = NewResource();

        var acquired = await store.TryAcquireOrRenewAsync(resource, "a", LongLease);
        // Several renews by the holder must not move the token...
        await store.TryAcquireOrRenewAsync(resource, "a", LongLease);
        await store.TryAcquireOrRenewAsync(resource, "a", LongLease);
        await store.ReleaseAsync(resource, "a");

        // ...so the next holder advances by exactly one term, not by the number of renews.
        var takeover = await store.TryAcquireOrRenewAsync(resource, "b", LongLease);

        Assert.Equal(acquired.Lease!.FencingToken + 1, takeover.Lease!.FencingToken);
    }

    // ---- Rule 3: a lease expires so a new leader can take over ----------------------------------

    [Fact]
    public async Task After_the_lease_expires_a_new_candidate_takes_over_with_a_higher_token()
    {
        var store = CreateStore();
        var resource = NewResource();

        var first = await store.TryAcquireOrRenewAsync(resource, "a", ShortLease);
        Assert.Equal(LeaseOutcome.Acquired, first.Outcome);

        // a never renews. Once its short lease lapses, b must win, and with a strictly greater token.
        LeaseAcquisition? takeover = null;
        var tookOver = await EventuallyAsync(
            async () =>
            {
                var attempt = await store.TryAcquireOrRenewAsync(resource, "b", LongLease);
                if (attempt.Outcome == LeaseOutcome.Acquired)
                {
                    takeover = attempt;
                    return true;
                }

                return false;
            },
            TimeSpan.FromSeconds(5));

        Assert.True(tookOver, "the lease did not lapse so the follower never took over");
        Assert.Equal("b", takeover!.Lease!.HolderId);
        Assert.True(takeover.Lease.FencingToken > first.Lease!.FencingToken,
            $"expected takeover token {takeover.Lease.FencingToken} to exceed {first.Lease.FencingToken}");
    }

    // ---- Rule 4: release is fencing-checked -----------------------------------------------------

    [Fact]
    public async Task Releasing_as_the_holder_lets_a_follower_take_over_immediately()
    {
        var store = CreateStore();
        var resource = NewResource();
        await store.TryAcquireOrRenewAsync(resource, "a", LongLease);

        await store.ReleaseAsync(resource, "a");
        var takeover = await store.TryAcquireOrRenewAsync(resource, "b", LongLease);

        Assert.Equal(LeaseOutcome.Acquired, takeover.Outcome);
        Assert.Equal("b", takeover.Lease!.HolderId);
    }

    [Fact]
    public async Task Releasing_a_lease_you_do_not_hold_is_a_noop()
    {
        var store = CreateStore();
        var resource = NewResource();
        await store.TryAcquireOrRenewAsync(resource, "a", LongLease);

        // b does not hold the lease, so its release must not depose a.
        await store.ReleaseAsync(resource, "b");
        var stillDenied = await store.TryAcquireOrRenewAsync(resource, "b", LongLease);

        Assert.Equal(LeaseOutcome.Denied, stillDenied.Outcome);
        Assert.Equal("a", stillDenied.HolderId);
    }
}
