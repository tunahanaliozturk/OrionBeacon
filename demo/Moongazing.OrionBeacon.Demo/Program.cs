namespace Moongazing.OrionBeacon.Demo;

/// <summary>
/// Runnable tour of OrionBeacon's leader election, driven entirely in memory and in process.
///
/// Every scenario shares one <see cref="Moongazing.OrionBeacon.Leasing.InMemoryLeaseStore"/> across
/// its candidates so they genuinely compete for the same lease, and drives the state machine by
/// calling <c>TryElectAsync</c> / <c>ResignAsync</c> directly. The hosted background loop
/// (<c>AddOrionBeacon</c>) is deliberately NOT started, because it never terminates; driving cycles
/// manually is exactly how the library's own tests exercise election, and it lets this demo run to
/// completion.
/// </summary>
internal static class Program
{
    private static async Task Main()
    {
        Console.WriteLine("OrionBeacon demo: leader election with renewable leases and fencing tokens.");
        Console.WriteLine("All scenarios run in memory, in process, driving election cycles by hand.");

        await ElectionDemo.RunAsync();
        await RenewalDemo.RunAsync();
        await ResignationDemo.RunAsync();
        await FencingDemo.RunAsync();

        DemoConsole.Banner("Demo complete");
        Console.WriteLine("  All scenarios ran to completion.");
    }
}
