namespace Moongazing.OrionBeacon.Demo;

using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// Time-driven failover. Unlike a graceful resignation, the leader here just stops renewing (as a
/// crashed or partitioned node would). A controllable <see cref="TimeProvider"/> drives the clock
/// past the lease's expiry, so the follower wins the next cycle once the lease has lapsed. The new
/// term gets a strictly higher fencing token. This uses the v0.2 public
/// <see cref="InMemoryLeaseStore(TimeProvider)"/> constructor to advance time with no real delay.
/// </summary>
internal static class FailoverDemo
{
    public static async Task RunAsync()
    {
        DemoConsole.Banner("4. Failover: an expired lease lets a follower take over");

        var clock = new DemoTimeProvider(DateTimeOffset.UnixEpoch);
        var store = new InMemoryLeaseStore(clock);
        using var diagnostics = new LeaderElectionDiagnostics();

        var leaseDuration = TimeSpan.FromSeconds(15);
        var holder = new LeaderElector(
            store,
            new LeaderElectionOptions
            {
                ResourceName = "jobs",
                CandidateId = "node-1",
                LeaseDuration = leaseDuration,
                RenewInterval = TimeSpan.FromSeconds(5),
            },
            diagnostics,
            new ConsoleLeadershipObserver("node-1"));

        var follower = new LeaderElector(
            store,
            new LeaderElectionOptions
            {
                ResourceName = "jobs",
                CandidateId = "node-2",
                LeaseDuration = leaseDuration,
                RenewInterval = TimeSpan.FromSeconds(5),
            },
            diagnostics,
            new ConsoleLeadershipObserver("node-2"));

        await holder.TryElectAsync();
        long firstTermToken = holder.Lease!.FencingToken;
        DemoConsole.Step($"node-1 is leader, token = {firstTermToken}");

        bool followerBefore = await follower.TryElectAsync();
        DemoConsole.Step($"node-2 tries while the lease is live: IsLeader = {followerBefore} (denied)");

        DemoConsole.Step("node-1 stops renewing (crash or partition); advance the clock past the lease");
        clock.Advance(leaseDuration + TimeSpan.FromSeconds(1));

        bool followerAfter = await follower.TryElectAsync();
        long secondTermToken = follower.Lease!.FencingToken;
        DemoConsole.Step($"node-2 runs the next cycle: IsLeader = {followerAfter}, token = {secondTermToken}");

        DemoConsole.Note(secondTermToken > firstTermToken
            ? $"New term token {secondTermToken} is strictly higher than the lapsed term ({firstTermToken})."
            : "Token did not advance across the failover.");
    }
}
