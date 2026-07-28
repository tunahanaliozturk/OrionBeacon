// NativeAOT smoke test. Publishing this with PublishAot=true must produce zero trim/AOT warnings,
// and running it must exit 0 - OrionBeacon's AOT exit criterion. Runtime checks, not a framework:
// the point is to prove DI registration, the diagnostics meter, and the lease store survive
// trimming in a real native binary.
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionBeacon;
using Moongazing.OrionBeacon.Diagnostics;
using Moongazing.OrionBeacon.Leasing;

var services = new ServiceCollection();
services.AddOrionBeacon();

using var provider = services.BuildServiceProvider();

// Diagnostics reads the assembly informational version via reflection - exercise it under trimming.
_ = provider.GetRequiredService<LeaderElectionDiagnostics>();

var store = provider.GetRequiredService<ILeaseStore>();

// One candidate acquires; a second is denied while the first holds; the first renews.
var a = await store.TryAcquireOrRenewAsync("resource", "candidate-a", TimeSpan.FromSeconds(30));
Check(a.IsHeld, $"candidate-a should acquire, outcome was {a.Outcome}");

var b = await store.TryAcquireOrRenewAsync("resource", "candidate-b", TimeSpan.FromSeconds(30));
Check(!b.IsHeld, $"candidate-b should be denied while a holds, outcome was {b.Outcome}");

var renew = await store.TryAcquireOrRenewAsync("resource", "candidate-a", TimeSpan.FromSeconds(30));
Check(renew.IsHeld, "candidate-a should renew its own lease");

// After release, the follower can take over.
await store.ReleaseAsync("resource", "candidate-a");
var takeover = await store.TryAcquireOrRenewAsync("resource", "candidate-b", TimeSpan.FromSeconds(30));
Check(takeover.IsHeld, "candidate-b should acquire after candidate-a releases");

Console.WriteLine("OrionBeacon AOT smoke test passed.");
return 0;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        Console.Error.WriteLine($"AOT smoke test failed: {message}");
        Environment.Exit(1);
    }
}
