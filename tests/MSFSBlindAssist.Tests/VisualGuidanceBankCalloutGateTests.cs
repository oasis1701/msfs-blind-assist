using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When a bank callout is spoken (VG-21): an error the tones can show after it has held 0.25 s, a
/// larger one once the bank has stopped changing for 1 s, "matched" entered at 0.4° and left at
/// 1.0°, a steady error under 1° after 1.5 s, at least 1 s between callouts, and a callout due in
/// that gap dropped, never queued. Each test drives the gate at 30 Hz.
/// </summary>
public class VisualGuidanceBankCalloutGateTests
{
    private const int Hz = 30;
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Feeds the gate (by default a new one with the airliners' 5° tone range) one call every 1/30 s
    /// from <paramref name="from"/> for <paramref name="seconds"/>; returns what it said and when.
    /// </summary>
    private static List<(double T, string Phrase)> Fly(Func<double, (double Desired, double Actual)> at, double seconds,
        Func<double, bool>? maySpeak = null, VisualGuidanceBankCalloutGate? gate = null, double from = 0)
    {
        gate ??= new VisualGuidanceBankCalloutGate(5.0);
        var said = new List<(double, string)>();
        int steps = (int)Math.Round(seconds * Hz);
        for (int i = 0; i <= steps; i++)
        {
            double t = from + (double)i / Hz;
            var (desired, actual) = at(t);
            string? phrase = gate.Next(Start.AddTicks((long)Math.Round(t * TimeSpan.TicksPerSecond)), desired, actual, maySpeak?.Invoke(t) ?? true);
            if (phrase != null) said.Add((t, phrase));
        }
        return said;
    }

    private static string Show(List<(double T, string Phrase)> said)
        => string.Join(", ", said.Select(s => $"{s.T:0.000} {s.Phrase}"));

    [Fact]
    public void Steady_sub_degree_error_is_spoken_once_after_one_and_a_half_seconds()
    {
        var said = Fly(_ => (-0.7, 0), 5);
        var only = Assert.Single(said);
        Assert.Equal("1 left", only.Phrase);
        Assert.InRange(only.T, 1.5, 1.55);
    }

    [Fact]
    public void Error_hovering_around_half_a_degree_gives_one_callout()
    {
        var said = Fly(t => (3, 3 - (0.5 + 0.3 * Math.Sin(2 * Math.PI * t / 1.3))), 8);
        var only = Assert.Single(said);
        Assert.Equal("matched", only.Phrase);
    }

    [Fact]
    public void A_clean_roll_to_matched_is_silent_then_matched_once()
    {
        var said = Fly(t => (5, Math.Min(5, 5 * t)), 4);
        Assert.Equal(new[] { "matched" }, said.Select(s => s.Phrase));
    }

    [Fact]
    public void A_large_error_waits_for_the_bank_to_stop_changing()
    {
        var said = Fly(t => (10, Math.Min(4, 2 * t)), 6);
        Assert.NotEmpty(said);
        Assert.Equal("6 right", said[0].Phrase);
        Assert.InRange(said[0].T, 2.7, 3.1);
    }

    [Fact]
    public void Callouts_are_at_least_one_second_apart()
    {
        var said = Fly(t => ((int)Math.Floor(t / 0.3 + 1e-9) % 2 == 0 ? -1.2 : -1.7, 0), 6);
        Assert.True(said.Count >= 2, Show(said));
        for (int i = 1; i < said.Count; i++)
            Assert.True(said[i].T - said[i - 1].T >= 1.0 - 1e-9, Show(said));
    }

    [Fact]
    public void A_callout_due_in_the_gap_is_dropped_and_the_next_speaks_the_error_then()
    {
        var said = Fly(t => (t < 0.5 ? -1.2 : t < 0.9 ? -3.0 : -2.2, 0), 4);
        Assert.Equal(2, said.Count);
        Assert.Equal("1 left", said[0].Phrase);
        Assert.InRange(said[0].T, 0.25, 0.3);
        Assert.Equal("2 left", said[1].Phrase);
        Assert.InRange(said[1].T, 1.25, 1.3);
    }

    [Fact]
    public void A_roll_in_progress_at_activation_stays_silent_until_the_bank_is_steady()
    {
        var said = Fly(t => (20, Math.Min(6, 3 * t)), 5);
        Assert.NotEmpty(said);
        Assert.True(said[0].T >= 2.75, Show(said));
        Assert.Equal("14 right", said[0].Phrase);
    }

    [Fact]
    public void Callouts_held_back_by_a_manual_query_speak_the_current_error_when_it_ends()
    {
        var said = Fly(_ => (-1.5, 0), 5, maySpeak: t => t >= 3.0 - 1e-9);
        Assert.NotEmpty(said);
        Assert.Equal("2 left", said[0].Phrase);
        Assert.InRange(said[0].T, 3.0, 3.04);
    }

    [Fact]
    public void Switching_between_matched_and_on_commanded_path_never_repeats()
    {
        var said = Fly(t => (1.0 - t / 4, 1.0 - t / 4 - 0.1), 4);
        Assert.Single(said);
    }

    [Fact]
    public void Reset_forgets_what_was_spoken()
    {
        var gate = new VisualGuidanceBankCalloutGate(5.0);
        var first = Fly(_ => (-1.2, 0), 2, gate: gate);
        Assert.Equal(new[] { "1 left" }, first.Select(s => s.Phrase));
        gate.Reset();
        var again = Fly(_ => (-1.2, 0), 2, gate: gate, from: 2 + 1.0 / Hz);
        Assert.Equal(new[] { "1 left" }, again.Select(s => s.Phrase));
    }

    [Fact]
    public void A_two_degree_knock_is_spoken_within_a_quarter_second_of_settling()
    {
        var said = Fly(t => (0, t <= 2 ? 0 : t >= 2.3 ? -2.2 : -2.2 * (t - 2) / 0.3), 4);
        var after = said.Where(s => s.T > 2).ToList();
        Assert.NotEmpty(after);
        Assert.Equal("2 right", after[0].Phrase);
        Assert.InRange(after[0].T, 2.45, 2.6);
    }

    [Theory]
    [InlineData(10.0, 0.25, 0.3)]
    [InlineData(5.0, 1.0, 1.05)]
    public void Errors_the_tones_can_show_are_spoken_after_a_quarter_second(double toneRangeDeg, double from, double to)
    {
        var said = Fly(_ => (7, 0), 3, gate: new VisualGuidanceBankCalloutGate(toneRangeDeg));
        Assert.NotEmpty(said);
        Assert.Equal("7 right", said[0].Phrase);
        Assert.InRange(said[0].T, from, to);
    }
}
