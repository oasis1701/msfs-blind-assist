// The manual N1: a typed value set by turning the N1 SET inner knob until PMDG's own manual N1 reads it.
// The knob was swept live (2026-10-24): a click moves 0.5, a wheel turn 0.1 when 300 ms apart; PMDG keeps
// the value in 0 to 105; "1" sets engine 1, "2" engine 2, BOTH both. The setter is closed-loop against a
// fake knob here: clicks for the bulk, wheel turns for the rest, reading back after each batch.

using System.Globalization;
using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737ManualN1Tests
{
    private static readonly ManualN1Knob Knob = ManualN1Knob.Ng3;

    [Theory]
    [InlineData(104.0, 95.5, -17, 0)]
    [InlineData(104.0, 95.4, -17, -1)]
    [InlineData(95.0, 95.3, 0, 3)]
    [InlineData(95.0, 97.4, 4, 4)]
    [InlineData(95.0, 95.0, 0, 0)]
    public void Clicks_to_target_round_to_the_step(double current, double target, int coarse, int fine) =>
        Assert.Equal((coarse, fine), Pmdg737ManualN1.ClicksTo(current, target, Knob));

    [Theory]
    [InlineData(0, new[] { 1 })]     // "2": engine 2
    [InlineData(1, new[] { 0 })]     // "1": engine 1
    [InlineData(3, new[] { 0, 1 })]  // BOTH
    [InlineData(2, new int[0])]      // AUTO
    [InlineData(7, new int[0])]
    public void Engines_follow_the_n1_set_position(int position, int[] engines) =>
        Assert.Equal(engines, Pmdg737ManualN1.Engines(position));

    [Fact]
    public void Readback_one_value_or_both_engines_differing()
    {
        Assert.Equal("N1 reference 95.5", Pmdg737ManualN1.Readback(new[] { 95.5 }));
        Assert.Equal("N1 reference 95.5", Pmdg737ManualN1.Readback(new[] { 95.5, 95.49999 }));
        Assert.Equal("N1 reference 95.5, 95.0", Pmdg737ManualN1.Readback(new[] { 95.5, 95.0 }));
    }

    [Theory]
    [InlineData("de-DE", "95,5", 95.5)]
    [InlineData("de-DE", "95.5", 95.5)]
    [InlineData("sv-SE", "95,5", 95.5)]
    [InlineData("en-US", "105", 105.0)]
    public void Typed_values_parse_with_TryParseTyped_under_comma_cultures(string culture, string typed, double expected)
    {
        var (ok, value, _) = TestCulture.Run(culture, () =>
        {
            bool parsed = Pmdg737ManualN1.TryParse(typed, Knob, out double v, out string? error);
            return (parsed, v, error);
        });
        Assert.True(ok);
        Assert.Equal(expected, value, 6);
    }

    [Theory]
    [InlineData("106")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public void Values_outside_the_knob_are_refused(string typed)
    {
        Assert.False(Pmdg737ManualN1.TryParse(typed, Knob, out _, out string? error));
        Assert.Equal("Enter an N1 between 0 and 105", error);
    }

    // ---- the setter against a fake knob ----

    private sealed class FakeKnob
    {
        public int Position { get; set; } = 3;
        public double[] Values { get; } = { 104.0, 104.0 };
        public int Clicks { get; private set; }
        public bool Stuck { get; set; }
        public long NowMs { get; set; }
        public List<ManualN1Click> Log { get; } = new();

        public ManualN1Reading? Read() => new ManualN1Reading(Position, Values.ToArray());

        public void Click(ManualN1Click c)
        {
            Clicks++;
            Log.Add(c);
            if (Stuck) return;
            double step = c switch { ManualN1Click.CoarseUp => 0.5, ManualN1Click.CoarseDown => -0.5, ManualN1Click.FineUp => 0.1, _ => -0.1 };
            foreach (int e in Pmdg737ManualN1.Engines(Position))
                Values[e] = Math.Clamp(Math.Round(Values[e] + step, 1), 0, 105);
        }

        public Task Delay(long ms)
        {
            NowMs += ms;
            return Task.CompletedTask;
        }
    }

    private static Task<string?> Set(Pmdg737ManualN1Setter setter, FakeKnob knob, double target, Func<ManualN1Reading?>? read = null) =>
        setter.SetAsync(target, read ?? knob.Read, knob.Click, () => knob.NowMs, knob.Delay);

    [Fact]
    public async Task Setter_reaches_the_target()
    {
        var knob = new FakeKnob();
        Assert.Equal("N1 reference 95.5", await Set(new Pmdg737ManualN1Setter(Knob), knob, 95.5));
        Assert.Equal(new[] { 95.5, 95.5 }, knob.Values);
        Assert.Equal(17, knob.Clicks); // all coarse
        Assert.All(knob.Log, c => Assert.Equal(ManualN1Click.CoarseDown, c));
    }

    [Fact]
    public async Task Setter_finishes_with_fine_turns_and_sets_only_the_selected_engine()
    {
        var knob = new FakeKnob { Position = 1 };
        Assert.Equal("N1 reference 96.8", await Set(new Pmdg737ManualN1Setter(Knob), knob, 96.8));
        Assert.Equal(new[] { 96.8, 104.0 }, knob.Values);
        Assert.Equal(new[] { ManualN1Click.FineDown, ManualN1Click.FineDown }, knob.Log.TakeLast(2));
    }

    [Fact]
    public async Task Setter_stops_at_max_clicks_and_reports()
    {
        var knob = new FakeKnob { Stuck = true };
        Assert.Equal("N1 reference 104.0, could not reach 95.5", await Set(new Pmdg737ManualN1Setter(Knob), knob, 95.5));
        Assert.True(knob.Clicks <= Knob.MaxClicks);
    }

    [Fact]
    public async Task Setter_stops_when_n1_set_leaves_manual()
    {
        var knob = new FakeKnob();
        int reads = 0;
        string? result = await Set(new Pmdg737ManualN1Setter(Knob), knob, 95.5, () =>
        {
            if (++reads == 2) knob.Position = 2; // the pilot turned N1 SET to AUTO meanwhile
            return knob.Read();
        });
        Assert.Equal("Set N1 SET to 1, 2 or BOTH first", result);
    }

    [Fact]
    public async Task Setter_without_the_reader_says_so()
    {
        var knob = new FakeKnob();
        Assert.Equal("Manual N1 needs the engine display reader", await Set(new Pmdg737ManualN1Setter(Knob), knob, 95.5, () => null));
        Assert.Equal(0, knob.Clicks);
    }

    [Fact]
    public async Task A_new_target_replaces_the_running_one()
    {
        var knob = new FakeKnob();
        var setter = new Pmdg737ManualN1Setter(Knob);
        var gate = new TaskCompletionSource();
        bool hold = true;
        Task Delay(long ms)
        {
            knob.NowMs += ms;
            return hold ? gate.Task : Task.CompletedTask;
        }

        var first = setter.SetAsync(95.5, knob.Read, knob.Click, () => knob.NowMs, Delay); // waits after its first click
        hold = false;
        var second = setter.SetAsync(100.0, knob.Read, knob.Click, () => knob.NowMs, Delay);
        gate.SetResult();

        Assert.Equal("N1 reference 100.0", await second);
        Assert.Null(await first); // replaced: says nothing
        Assert.Equal(new[] { 100.0, 100.0 }, knob.Values);
    }

    [Fact]
    public void Readback_is_invariant_under_a_comma_culture() =>
        Assert.Equal("N1 reference 95.5, 95.0", TestCulture.Run("de-DE", () => Pmdg737ManualN1.Readback(new[] { 95.5, 95.0 })));
}
