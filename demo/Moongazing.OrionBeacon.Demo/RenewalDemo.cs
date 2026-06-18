namespace Moongazing.OrionBeacon.Demo;

using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Election;
using Moongazing.OrionBeacon.Leasing;

/// <summary>
/// The holder keeps its leadership by renewing on every cycle. A renewal extends the same term, so
/// the fencing token does NOT change: it identifies the term, not the renewal count. Meanwhile a
/// follower that retries each cycle is denied for as long as the holder keeps renewing.
/// </summary>
internal static class RenewalDemo
{
    public static async Task RunAsync()
    {
        DemoConsole.Banner("2. Renewal: the holder keeps leadership, token is stable across renewals");

        var store = new InMemoryLeaseStore();
        using var diagnostics = new LeaderElectionDiagnostics();

        var holderOptions = new LeaderElectionOptions
        {
            ResourceName = "jobs",
            CandidateId = "holder",
            LeaseDuration = TimeSpan.FromSeconds(30),
            RenewInterval = TimeSpan.FromSeconds(10),
        };
        var followerOptions = new LeaderElectionOptions
        {
            ResourceName = "jobs",
            CandidateId = "follower",
            LeaseDuration = TimeSpan.FromSeconds(30),
            RenewInterval = TimeSpan.FromSeconds(10),
        };

        var holder = new LeaderElector(store, holderOptions, diagnostics);
        var follower = new LeaderElector(store, followerOptions, diagnostics);

        await holder.TryElectAsync();
        long initialToken = holder.Lease!.FencingToken;
        DemoConsole.Step($"holder acquired leadership, fencing token = {initialToken}");

        for (int cycle = 1; cycle <= 3; cycle++)
        {
            await holder.TryElectAsync();
            bool followerLeads = await follower.TryElectAsync();
            DemoConsole.Step(
                $"cycle {cycle}: holder renewed (token still {holder.Lease!.FencingToken}), " +
                $"follower.IsLeader = {followerLeads}");
        }

        DemoConsole.Note(initialToken == holder.Lease!.FencingToken
            ? "Token unchanged across renewals: same leadership term."
            : "Token changed unexpectedly.");
    }
}
