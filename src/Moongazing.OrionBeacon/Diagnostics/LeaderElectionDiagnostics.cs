namespace Moongazing.OrionBeacon.Diagnostics;

using System.Diagnostics.Metrics;

/// <summary>
/// OpenTelemetry instrumentation for leader election. Exposes a <see cref="Meter"/> named
/// <c>Moongazing.OrionBeacon</c> with an acquisition-attempt counter, a leadership-transition
/// counter, and an observable gauge reporting whether this candidate is currently the leader.
/// Registered as a singleton; dispose it to release the meter.
/// </summary>
public sealed class LeaderElectionDiagnostics : IDisposable
{
    /// <summary>The meter name OpenTelemetry consumers subscribe to.</summary>
    public const string MeterName = "Moongazing.OrionBeacon";

    private readonly Meter meter;
    private volatile int isLeader;

    /// <summary>Create the meter and its instruments.</summary>
    public LeaderElectionDiagnostics()
    {
        meter = new Meter(MeterName, "0.1.0");

        Attempts = meter.CreateCounter<long>(
            "orionbeacon.attempts",
            unit: "{attempt}",
            description: "Lease acquisition attempts, tagged outcome (acquired/renewed/denied).");

        Transitions = meter.CreateCounter<long>(
            "orionbeacon.transitions",
            unit: "{transition}",
            description: "Leadership transitions, tagged direction (elected/deposed).");

        meter.CreateObservableGauge(
            "orionbeacon.is_leader",
            () => isLeader,
            unit: "{bool}",
            description: "1 when this candidate currently holds leadership, otherwise 0.");
    }

    /// <summary>Counts acquisition attempts by outcome.</summary>
    public Counter<long> Attempts { get; }

    /// <summary>Counts leadership transitions by direction.</summary>
    public Counter<long> Transitions { get; }

    /// <summary>Record an acquisition attempt.</summary>
    /// <param name="outcome">The outcome tag value.</param>
    public void RecordAttempt(string outcome) =>
        Attempts.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Record a leadership transition and update the gauge.</summary>
    /// <param name="elected">True for an election, false for a deposition.</param>
    public void RecordTransition(bool elected)
    {
        isLeader = elected ? 1 : 0;
        Transitions.Add(1, new KeyValuePair<string, object?>("direction", elected ? "elected" : "deposed"));
    }

    /// <inheritdoc />
    public void Dispose() => meter.Dispose();
}
