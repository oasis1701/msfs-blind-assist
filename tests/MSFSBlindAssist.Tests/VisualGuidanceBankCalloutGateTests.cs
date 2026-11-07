using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When a bank callout is spoken (VG-21): at once, the update the phrase changes, the error rounded
/// to a degree, "matched" entered at 0.4° and left at 1.0°. No hold, no gap between callouts, no wait
/// for the bank to stop changing (the owner, 2026-11-06: "before it said it instantly. we need to make
/// it instant again"). Each test drives the gate at 30 Hz.
/// </summary>
public class VisualGuidanceBankCalloutGateTests
{
    private const int Hz = 30;
    private const double Frame = 1.0 / Hz;

    /// <summary>
    /// Feeds the gate (by default a new one) one call every 1/30 s from <paramref name="from"/> for
    /// <paramref name="seconds"/>; returns what it said and when.
    /// </summary>
    private static List<(double T, string Phrase)> Fly(Func<double, (double Desired, double Actual)> at, double seconds,
        Func<double, bool>? maySpeak = null, VisualGuidanceBankCalloutGate? gate = null, double from = 0)
    {
        gate ??= new VisualGuidanceBankCalloutGate();
        var said = new List<(double, string)>();
        int steps = (int)Math.Round(seconds * Hz);
        for (int i = 0; i <= steps; i++)
        {
            double t = from + (double)i / Hz;
            var (desired, actual) = at(t);
            string? phrase = gate.Next(desired, actual, maySpeak?.Invoke(t) ?? true);
            if (phrase != null) said.Add((t, phrase));
        }
        return said;
    }

    private static string Show(List<(double T, string Phrase)> said)
        => string.Join(", ", said.Select(s => $"{s.T:0.000} {s.Phrase}"));

    [Fact]
    public void An_error_is_spoken_on_the_first_update_and_never_repeated()
    {
        var said = Fly(_ => (1.2, 0), 10);
        var only = Assert.Single(said);
        Assert.Equal("1 right", only.Phrase);
        Assert.Equal(0, only.T);
    }

    [Fact]
    public void A_sub_degree_error_is_spoken_at_once_as_one_degree()
    {
        var said = Fly(_ => (-0.7, 0), 10);
        var only = Assert.Single(said);
        Assert.Equal("1 left", only.Phrase);
        Assert.Equal(0, only.T);
    }

    // On final, drifting a degree off after "matched" is spoken the update the error reaches 1°.
    [Fact]
    public void Drifting_a_degree_off_after_matched_is_spoken_when_it_reaches_a_degree()
    {
        var said = Fly(t => (0, t < 2 ? 0 : -Math.Min(1.3, 0.5 * (t - 2))), 6);
        Assert.Equal(new[] { "Matched, level", "1 right" }, said.Select(s => s.Phrase));
        Assert.InRange(said[1].T, 4.0 - 1e-9, 4.0 + Frame + 1e-9);
    }

    [Fact]
    public void Every_degree_of_a_roll_is_spoken_as_it_changes()
    {
        var said = Fly(t => (10, Math.Min(10, 4 * t)), 4);
        Assert.Equal(new[] { "10 right", "9 right", "8 right", "7 right", "6 right", "5 right", "4 right",
            "3 right", "2 right", "1 right", "matched" }, said.Select(s => s.Phrase));
        // "9 right" the first update the error rounds to 9: 10 − 4t under 9.5, t past 0.125 s.
        Assert.InRange(said[1].T, 0.125, 0.125 + Frame + 1e-9);
    }

    [Fact]
    public void A_large_error_is_spoken_without_waiting_for_the_bank_to_settle()
    {
        var said = Fly(t => (10, Math.Min(4, 2 * t)), 6);
        Assert.Equal((0.0, "10 right"), said[0]);
        Assert.Equal(new[] { "10 right", "9 right", "8 right", "7 right", "6 right" }, said.Select(s => s.Phrase));
        // "6 right" the first update the error rounds to 6: 10 − 2t under 6.5, t past 1.75 s.
        Assert.InRange(said[^1].T, 1.75, 1.75 + Frame + 1e-9);
    }

    [Fact]
    public void Quick_changes_are_each_spoken_with_no_gap()
    {
        var said = Fly(t => ((int)Math.Floor(t / 0.3 + 1e-9) % 2 == 0 ? -1.2 : -1.7, 0), 3);
        Assert.True(said.Count >= 10, Show(said));
        for (int i = 0; i < said.Count; i++)
            Assert.Equal(i % 2 == 0 ? "1 left" : "2 left", said[i].Phrase);
        for (int i = 1; i < said.Count; i++)
            Assert.InRange(said[i].T - said[i - 1].T, 0.3 - Frame, 0.3 + Frame);
    }

    // The matched band is entered at 0.4° and left at 1.0°, so an error hovering around half a degree
    // settles on "matched" instead of flipping with "1 right" many times a second.
    [Fact]
    public void Error_hovering_around_half_a_degree_settles_on_matched()
    {
        var said = Fly(t => (3, 3 - (0.5 + 0.3 * Math.Sin(2 * Math.PI * t / 1.3))), 8);
        Assert.Equal(new[] { "1 right", "matched" }, said.Select(s => s.Phrase));
    }

    [Fact]
    public void Callouts_held_back_by_a_manual_query_speak_the_current_error_when_it_ends()
    {
        var said = Fly(_ => (-1.5, 0), 5, maySpeak: t => t >= 3.0 - 1e-9);
        var only = Assert.Single(said);
        Assert.Equal("2 left", only.Phrase);
        Assert.InRange(only.T, 3.0 - 1e-9, 3.0 + Frame);
    }

    [Fact]
    public void Switching_between_matched_and_matched_level_never_repeats()
    {
        var said = Fly(t => (1.0 - t / 4, 1.0 - t / 4 - 0.1), 4);
        Assert.Single(said);
    }

    [Fact]
    public void Reset_forgets_what_was_spoken()
    {
        var gate = new VisualGuidanceBankCalloutGate();
        var first = Fly(_ => (-1.2, 0), 2, gate: gate);
        Assert.Equal(new[] { "1 left" }, first.Select(s => s.Phrase));
        gate.Reset();
        var again = Fly(_ => (-1.2, 0), 2, gate: gate, from: 2 + Frame);
        Assert.Equal(new[] { "1 left" }, again.Select(s => s.Phrase));
    }

    [Fact]
    public void A_two_degree_knock_is_spoken_as_it_builds()
    {
        var said = Fly(t => (0, t <= 2 ? 0 : t >= 2.3 ? -2.2 : -2.2 * (t - 2) / 0.3), 4);
        var after = said.Where(s => s.T > 2).ToList();
        Assert.Equal(new[] { "1 right", "2 right" }, after.Select(s => s.Phrase));
        // The error reaches 1° at 2.136 s and rounds to 2 from 1.5°, at 2.205 s.
        Assert.InRange(after[0].T, 2.136, 2.136 + Frame);
        Assert.InRange(after[1].T, 2.205, 2.205 + Frame);
    }
}
