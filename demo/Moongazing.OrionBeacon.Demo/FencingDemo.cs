namespace Moongazing.OrionBeacon.Demo;

using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// Why fencing tokens matter. A downstream resource records the highest token it has accepted and
/// rejects any write carrying a lower one. When leadership changes hands the new leader's term has
/// a higher token, so a stale leader that resumed after a stop-the-world pause (still carrying its
/// old token) is fenced out. This closes the classic split-brain gap.
/// </summary>
internal static class FencingDemo
{
    public static async Task RunAsync()
    {
        DemoConsole.Banner("4. Fencing: a downstream store rejects writes from a stale leader");

        var store = new InMemoryLeaseStore();
        using var diagnostics = new LeaderElectionDiagnostics();
        var fencedResource = new FencedResource();

        var oldLeader = new LeaderElector(
            store,
            new LeaderElectionOptions { ResourceName = "ledger", CandidateId = "old-leader" },
            diagnostics);

        var newLeader = new LeaderElector(
            store,
            new LeaderElectionOptions { ResourceName = "ledger", CandidateId = "new-leader" },
            diagnostics);

        await oldLeader.TryElectAsync();
        long oldToken = oldLeader.Lease!.FencingToken;
        DemoConsole.Step($"old-leader elected, term token = {oldToken}");
        DemoConsole.Note($"old-leader writes with token {oldToken}: {Describe(fencedResource.TryWrite("entry-1", oldToken))}");

        // old-leader "freezes" past its lease and resigns; new-leader takes over with a higher term.
        await oldLeader.ResignAsync();
        await newLeader.TryElectAsync();
        long newToken = newLeader.Lease!.FencingToken;
        DemoConsole.Step($"handover complete, new-leader term token = {newToken}");
        DemoConsole.Note($"new-leader writes with token {newToken}: {Describe(fencedResource.TryWrite("entry-2", newToken))}");

        DemoConsole.Step("old-leader thaws and tries to write as if still in charge");
        bool accepted = fencedResource.TryWrite("stale-entry", oldToken);
        DemoConsole.Note($"old-leader writes with stale token {oldToken}: {Describe(accepted)}");

        DemoConsole.Note(accepted
            ? "Stale write was accepted (split-brain not prevented)."
            : "Stale write was fenced out: the resource kept only the writes from the current term.");
        DemoConsole.Note($"Committed entries: {string.Join(", ", fencedResource.Committed)}");
    }

    private static string Describe(bool accepted) => accepted ? "ACCEPTED" : "REJECTED";

    /// <summary>
    /// A toy downstream resource that enforces fencing: it remembers the highest token it has seen
    /// and refuses any write carrying a strictly lower token.
    /// </summary>
    private sealed class FencedResource
    {
        private long highestSeen;

        public List<string> Committed { get; } = [];

        public bool TryWrite(string payload, long fencingToken)
        {
            if (fencingToken < highestSeen)
            {
                return false;
            }

            highestSeen = fencingToken;
            Committed.Add(payload);
            return true;
        }
    }
}
