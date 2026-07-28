namespace Moongazing.OrionBeacon.Diagnostics;

using System.Diagnostics.Metrics;

using Moongazing.Orion.Abstractions.Diagnostics;

/// <summary>
/// OpenTelemetry instrumentation for leader election. Built on the Orion family's
/// <see cref="OrionInstrumentation"/> spine, so it shares the family's naming and static-tag
/// conventions: a <see cref="Meter"/> named <c>Moongazing.OrionBeacon</c> (subscribe by that name)
/// exposing an acquisition-attempt counter <c>orion.beacon.attempts</c>, a leadership-transition
/// counter <c>orion.beacon.transitions</c>, and an observable gauge <c>orion.beacon.is_leader</c>
/// reporting whether this candidate currently holds leadership. Multi-tenant / multi-region labels
/// configured through <see cref="OrionInstrumentation.SetStaticTags"/> are stamped onto every
/// measurement. Registered as a singleton; dispose it to release the meter.
/// </summary>
public sealed class LeaderElectionDiagnostics : OrionInstrumentation
{
    /// <summary>The meter name OpenTelemetry consumers subscribe to.</summary>
    public const string MeterName = "Moongazing.OrionBeacon";

    private volatile int isLeader;

    /// <summary>Create the meter and its instruments.</summary>
    public LeaderElectionDiagnostics()
        : base(OrionTelemetry.ScopeName("OrionBeacon"), MeterVersion.Value)
    {
        Attempts = Meter.CreateCounter<long>(
            OrionTelemetry.MetricName("beacon", "attempts"),
            unit: "{attempt}",
            description: "Lease acquisition attempts, tagged outcome (acquired/renewed/denied).");

        Transitions = Meter.CreateCounter<long>(
            OrionTelemetry.MetricName("beacon", "transitions"),
            unit: "{transition}",
            description: "Leadership transitions, tagged direction (elected/deposed).");

        Meter.CreateObservableGauge(
            OrionTelemetry.MetricName("beacon", "is_leader"),
            () => new Measurement<int>(isLeader, StaticTags),
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
        Attempts.Add(1, Tag(new KeyValuePair<string, object?>(OrionTelemetry.Tags.Outcome, outcome)));

    /// <summary>Record a leadership transition and update the gauge.</summary>
    /// <param name="elected">True for an election, false for a deposition.</param>
    public void RecordTransition(bool elected)
    {
        isLeader = elected ? 1 : 0;
        Transitions.Add(1, Tag(new KeyValuePair<string, object?>("direction", elected ? "elected" : "deposed")));
    }
}
