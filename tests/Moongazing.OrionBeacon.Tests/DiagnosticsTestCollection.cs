namespace Moongazing.OrionBeacon.Tests;

using Xunit;

/// <summary>
/// Non-parallel xUnit collection marker for tests that observe the shared
/// <c>Moongazing.OrionBeacon</c> meter. Every <c>LeaderElectionDiagnostics</c> instance registers
/// the same meter name; serialising the meter-observing tests is a second line of defence on top of
/// the per-instance meter filtering in the listener, so observable-gauge readings stay deterministic.
/// Behavioral tests that do not read meter values stay in the default parallel pool.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DiagnosticsMeterObservers
{
    /// <summary>The collection name shared by meter-observing test classes.</summary>
    public const string Name = "OrionBeacon meter observers";
}
