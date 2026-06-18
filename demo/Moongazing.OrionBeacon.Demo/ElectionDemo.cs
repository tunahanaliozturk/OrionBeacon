namespace Moongazing.OrionBeacon.Demo;

using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// Two candidates compete for one lease over the same resource, sharing one in-memory store. The
/// first to run a cycle acquires and becomes leader; the second is denied and stays a follower.
/// This is the core "only one node should do this" guarantee.
/// </summary>
internal static class ElectionDemo
{
    public static async Task RunAsync()
    {
        DemoConsole.Banner("1. Election: two candidates, one leader");

        // A single store shared by both candidates is what makes them compete for the SAME lease.
        var store = new InMemoryLeaseStore();
        using var diagnostics = new LeaderElectionDiagnostics();

        var optionsA = new LeaderElectionOptions
        {
            ResourceName = "nightly-report",
            CandidateId = "node-A",
            LeaseDuration = TimeSpan.FromSeconds(30),
            RenewInterval = TimeSpan.FromSeconds(10),
        };
        var optionsB = new LeaderElectionOptions
        {
            ResourceName = "nightly-report",
            CandidateId = "node-B",
            LeaseDuration = TimeSpan.FromSeconds(30),
            RenewInterval = TimeSpan.FromSeconds(10),
        };

        var candidateA = new LeaderElector(store, optionsA, diagnostics, new ConsoleLeadershipObserver("node-A"));
        var candidateB = new LeaderElector(store, optionsB, diagnostics, new ConsoleLeadershipObserver("node-B"));

        DemoConsole.Step("node-A runs an election cycle first");
        bool aLeads = await candidateA.TryElectAsync();
        DemoConsole.Note($"node-A.IsLeader = {aLeads}, fencing token = {candidateA.Lease?.FencingToken}");

        DemoConsole.Step("node-B runs an election cycle against the same lease");
        bool bLeads = await candidateB.TryElectAsync();
        DemoConsole.Note($"node-B.IsLeader = {bLeads} (denied: node-A already holds an unexpired lease)");

        DemoConsole.Step("Outcome");
        DemoConsole.Note($"Leader   : {(candidateA.IsLeader ? "node-A" : "node-B")}");
        DemoConsole.Note($"Follower : {(candidateA.IsLeader ? "node-B" : "node-A")}");
    }
}
