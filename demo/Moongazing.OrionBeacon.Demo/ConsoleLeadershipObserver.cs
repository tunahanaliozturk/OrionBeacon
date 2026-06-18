namespace Moongazing.OrionBeacon.Demo;

using Moongazing.OrionBeacon.Leasing;
using Moongazing.OrionBeacon.Observers;

/// <summary>
/// An <see cref="ILeadershipObserver"/> that writes leadership transitions to the console. The
/// elector calls <see cref="OnElected"/> when this candidate gains the lease and
/// <see cref="OnDeposed"/> when it loses it. Observers are observability only and must not throw;
/// the elector swallows any fault so an observer outage never disrupts election.
/// </summary>
internal sealed class ConsoleLeadershipObserver(string label) : ILeadershipObserver
{
    public void OnElected(Lease lease) =>
        DemoConsole.Note($"[observer] {label}: ELECTED on '{lease.Resource}' with fencing token {lease.FencingToken}");

    public void OnDeposed(string resource) =>
        DemoConsole.Note($"[observer] {label}: DEPOSED from '{resource}'");
}
