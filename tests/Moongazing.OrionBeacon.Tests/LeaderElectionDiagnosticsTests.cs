namespace Moongazing.OrionBeacon.Tests;

using System.Diagnostics.Metrics;

using Moongazing.OrionBeacon.Diagnostics;

using Xunit;

/// <summary>
/// Verifies the OpenTelemetry instrumentation: the meter name, the attempt and transition counters
/// with their outcome/direction tags, and the is_leader observable gauge driven by transitions.
/// A <see cref="MeterListener"/> scoped to this instance captures measurements deterministically.
/// </summary>
/// <remarks>
/// Runs in the non-parallel <see cref="DiagnosticsMeterObservers"/> collection. All diagnostics instances share
/// the single meter name "Moongazing.OrionBeacon", so a MeterListener cannot tell one live instance's
/// observable gauge from another's. Serialising these tests keeps the gauge reading attributable to
/// the instance under test; the rest of the suite still runs in parallel.
/// </remarks>
[Collection(DiagnosticsMeterObservers.Name)]
public sealed class LeaderElectionDiagnosticsTests
{
    [Fact]
    public void The_meter_name_is_the_published_constant()
    {
        Assert.Equal("Moongazing.OrionBeacon", LeaderElectionDiagnostics.MeterName);
    }

    [Fact]
    public void Recording_an_attempt_increments_the_attempts_counter_with_the_outcome_tag()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        using var capture = new Capture(diagnostics);

        diagnostics.RecordAttempt("acquired");
        diagnostics.RecordAttempt("denied");

        Assert.Equal(2, capture.CountFor("orionbeacon.attempts"));
        Assert.Contains(capture.Measurements,
            m => m.Instrument == "orionbeacon.attempts" && m.Tag("outcome") == "acquired");
        Assert.Contains(capture.Measurements,
            m => m.Instrument == "orionbeacon.attempts" && m.Tag("outcome") == "denied");
    }

    [Fact]
    public void Recording_an_election_increments_transitions_with_the_elected_direction()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        using var capture = new Capture(diagnostics);

        diagnostics.RecordTransition(elected: true);

        Assert.Contains(capture.Measurements,
            m => m.Instrument == "orionbeacon.transitions" && m.Tag("direction") == "elected" && m.Value == 1);
    }

    [Fact]
    public void Recording_a_deposition_increments_transitions_with_the_deposed_direction()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        using var capture = new Capture(diagnostics);

        diagnostics.RecordTransition(elected: false);

        Assert.Contains(capture.Measurements,
            m => m.Instrument == "orionbeacon.transitions" && m.Tag("direction") == "deposed");
    }

    [Fact]
    public void The_is_leader_gauge_reads_one_after_an_election()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        using var capture = new Capture(diagnostics);
        diagnostics.RecordTransition(elected: true);

        capture.RecordObservable();

        Assert.Equal(1, capture.LatestGauge());
    }

    [Fact]
    public void The_is_leader_gauge_reads_zero_after_a_deposition()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        using var capture = new Capture(diagnostics);
        diagnostics.RecordTransition(elected: true);
        diagnostics.RecordTransition(elected: false);

        capture.RecordObservable();

        Assert.Equal(0, capture.LatestGauge());
    }

    [Fact]
    public void The_gauge_starts_at_zero_before_any_transition()
    {
        using var diagnostics = new LeaderElectionDiagnostics();
        using var capture = new Capture(diagnostics);

        capture.RecordObservable();

        Assert.Equal(0, capture.LatestGauge());
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var diagnostics = new LeaderElectionDiagnostics();
        diagnostics.Dispose();
        // A second dispose must not throw.
        diagnostics.Dispose();
    }

    private readonly record struct Sample(string Instrument, long Value, IReadOnlyDictionary<string, string?> Tags)
    {
        public string? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>A MeterListener scoped to a single diagnostics instance's meter.</summary>
    private sealed class Capture : IDisposable
    {
        private readonly MeterListener listener;
        private readonly List<Sample> measurements = [];
        private readonly List<long> gaugeReadings = [];
        private readonly object gate = new();

        public Capture(LeaderElectionDiagnostics diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);

            // Diagnostics instances all share the meter name "Moongazing.OrionBeacon", so name alone
            // cannot isolate one instance's instruments from another's that may be live in a parallel
            // test. Enable only instruments belonging to THIS instance's meter, identified by the
            // private 'meter' field, so RecordObservableInstruments reads only our gauge.
            var meterField = typeof(LeaderElectionDiagnostics).GetField(
                "meter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("LeaderElectionDiagnostics.meter field not found.");
            var ownMeter = (Meter)(meterField.GetValue(diagnostics)
                ?? throw new InvalidOperationException("LeaderElectionDiagnostics.meter was null."));

            listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (ReferenceEquals(instrument.Meter, ownMeter))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };

            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var tag in tags)
                {
                    dict[tag.Key] = tag.Value?.ToString();
                }

                lock (gate)
                {
                    measurements.Add(new Sample(instrument.Name, value, dict));
                    if (instrument.Name == "orionbeacon.is_leader")
                    {
                        gaugeReadings.Add(value);
                    }
                }
            });

            listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            {
                var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var tag in tags)
                {
                    dict[tag.Key] = tag.Value?.ToString();
                }

                lock (gate)
                {
                    measurements.Add(new Sample(instrument.Name, value, dict));
                    if (instrument.Name == "orionbeacon.is_leader")
                    {
                        gaugeReadings.Add(value);
                    }
                }
            });

            listener.Start();
        }

        public IReadOnlyList<Sample> Measurements
        {
            get { lock (gate) { return [.. measurements]; } }
        }

        public int CountFor(string instrument)
        {
            lock (gate)
            {
                return measurements.Count(m => m.Instrument == instrument);
            }
        }

        public void RecordObservable() => listener.RecordObservableInstruments();

        public long LatestGauge()
        {
            lock (gate)
            {
                Assert.NotEmpty(gaugeReadings);
                return gaugeReadings[^1];
            }
        }

        public void Dispose() => listener.Dispose();
    }
}
