namespace Moongazing.OrionBeacon.Demo;

using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// A graceful handover. The leader resigns (as the hosted loop does on shutdown) and the lease is
/// released at once, so the waiting follower wins the very next cycle rather than waiting for the
/// lease to expire. The new term gets a strictly higher fencing token: leadership changed hands.
/// </summary>
internal static class ResignationDemo
{
    public static async Task RunAsync()
    {
        DemoConsole.Banner("3. Resignation: graceful handover bumps the fencing token");

        var store = new InMemoryLeaseStore();
        using var diagnostics = new LeaderElectionDiagnostics();

        var first = new LeaderElector(
            store,
            new LeaderElectionOptions { ResourceName = "leader-only-work", CandidateId = "node-1" },
            diagnostics,
            new ConsoleLeadershipObserver("node-1"));

        var second = new LeaderElector(
            store,
            new LeaderElectionOptions { ResourceName = "leader-only-work", CandidateId = "node-2" },
            diagnostics,
            new ConsoleLeadershipObserver("node-2"));

        await first.TryElectAsync();
        long firstTermToken = first.Lease!.FencingToken;
        DemoConsole.Step($"node-1 is leader, token = {firstTermToken}");

        bool secondBefore = await second.TryElectAsync();
        DemoConsole.Step($"node-2 tries while node-1 holds: IsLeader = {secondBefore} (denied)");

        DemoConsole.Step("node-1 resigns (releases the lease immediately)");
        await first.ResignAsync();
        DemoConsole.Note($"node-1.IsLeader = {first.IsLeader}");

        bool secondAfter = await second.TryElectAsync();
        long secondTermToken = second.Lease!.FencingToken;
        DemoConsole.Step($"node-2 runs the next cycle: IsLeader = {secondAfter}, token = {secondTermToken}");

        DemoConsole.Note(secondTermToken > firstTermToken
            ? $"New term token {secondTermToken} is strictly higher than the previous term ({firstTermToken})."
            : "Token did not advance across the handover.");
    }
}
