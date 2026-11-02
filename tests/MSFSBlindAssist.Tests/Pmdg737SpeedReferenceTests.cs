// The speed reference value: a typed value set by turning the speed reference inner knob until PMDG's own value for
// the selector's position reads it. Positions, steps and limits come from the layout (V1 and VR 80 to 450, VREF and BUG 60 to 450, a knot a click; WT 70000 to 200000, 1000 a click); SET and AUTO set
// nothing. The setter is closed-loop against a fake knob here; the knob's spacing is measured at ground session 2.

using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.Pmdg737PfdTestMemory;

namespace MSFSBlindAssist.Tests;

public class Pmdg737SpeedReferenceTests
{
    private static readonly PfdSpeedReferenceFields F = M.Layout.SpeedReference;

    /// <summary>The SDK selector's positions as the window's combo names them.</summary>
    private static readonly string[] Names = { "SET", "AUTO", "V1", "VR", "WT", "VREF", "BUG" };

    private static readonly SpeedReferenceKnob Knob = SpeedReferenceKnob.Ng3;

    private static PfdKnobPosition At(int position) => F.Positions.Single(p => p.Position == position);

    private static (bool Ok, double Value, string? Error) Parse(string typed, int position)
    {
        bool ok = Pmdg737SpeedReference.TryParse(typed, position, F, Names, out double value, out string? error);
        return (ok, value, error);
    }

    [Fact]
    public void Parses_a_speed_in_a_speed_position()
    {
        Assert.Equal((true, 142.0, (string?)null), Parse("142", 5));
        Assert.Equal((true, 152.0, (string?)null), Parse(" 152 ", 2));
        Assert.Equal((true, 147000.0, (string?)null), Parse("147000", 4));
        Assert.Equal((true, 143.0, (string?)null), Parse("142.6", 6)); // whole knots
    }

    [Fact]
    public void Rejects_outside_the_derived_limits()
    {
        Assert.Equal((false, "Enter a VREF between 60 and 450"), (Parse("59", 5).Ok, Parse("59", 5).Error));
        Assert.Equal((false, "Enter a V1 between 80 and 450"), (Parse("451", 2).Ok, Parse("451", 2).Error));
        Assert.Equal((false, "Enter a WT between 70000 and 200000"), (Parse("60000", 4).Ok, Parse("60000", 4).Error));
        Assert.Equal((false, "Enter a VR between 80 and 450"), (Parse("abc", 3).Ok, Parse("abc", 3).Error));
        Assert.False(Parse("", 3).Ok);
    }

    [Fact]
    public void Rejects_a_position_without_a_value()
    {
        const string sentence = "Set the speed reference selector to V1, VR, WT, VREF or BUG first";
        Assert.Equal((false, sentence), (Parse("142", 1).Ok, Parse("142", 1).Error));
        Assert.Equal((false, sentence), (Parse("142", 0).Ok, Parse("142", 0).Error));
    }

    [Theory]
    [InlineData("sv-SE", "142,0", 5, 142.0)]
    [InlineData("de-DE", "142,4", 5, 142.0)]
    [InlineData("sv-SE", "147000", 4, 147000.0)]
    [InlineData("de-DE", "152.0", 2, 152.0)]
    public void Speed_reference_parses_typed_numbers_under_sv_SE(string culture, string typed, int position, double expected)
    {
        var (ok, value, _) = TestCulture.Run(culture, () => Parse(typed, position));
        Assert.True(ok);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(130.0, 142.0, 1.0, 2, 2)]
    [InlineData(142.0, 130.0, 1.0, -2, -2)]
    [InlineData(142.0, 142.0, 1.0, 0, 0)]
    [InlineData(140.0, 143.0, 1.0, 0, 3)]
    [InlineData(147850.0, 150000.0, 1000.0, 0, 2)] // the FMC's weight is off the knob's 1000 grid: as near as it reaches
    [InlineData(70000.0, 147000.0, 1000.0, 15, 2)]
    public void Clicks_to_a_target(double current, double target, double step, int coarse, int fine) =>
        Assert.Equal((coarse, fine), Pmdg737SpeedReference.ClicksTo(current, target, step, Knob.CoarseSteps));

    [Fact]
    public void Readback_is_the_position_and_its_value_in_any_culture()
    {
        Assert.Equal("VREF 142", Pmdg737SpeedReference.Readback(5, 142, Names));
        Assert.Equal("WT 147850", TestCulture.Run("de-DE", () => Pmdg737SpeedReference.Readback(4, 147850, Names)));
    }

    // ---- the setter against a fake knob ----

    /// <summary>The knob as swept: a click 5 steps, a wheel turn 1, held in the position's limits.</summary>
    private sealed class FakeKnob
    {
        public double Value { get; set; } = 130;
        public double Step { get; init; } = 1;
        public double Min { get; init; } = 60;
        public double Max { get; init; } = 450;
        public int Clicks { get; private set; }
        public bool Stuck { get; set; }
        /// <summary>Steps a wheel turn moves; PMDG's kilogram option moves WT 2000 where the derived step is 1000.</summary>
        public int FineSteps { get; init; } = 1;
        /// <summary>The session's first click moved V1 one step, not five (ground session 2).</summary>
        public bool FirstClickShort { get; set; }
        public long NowMs { get; set; }
        public List<SpeedReferenceClick> Log { get; } = new();

        public double? Read() => Value;

        public void Click(SpeedReferenceClick c)
        {
            Clicks++;
            Log.Add(c);
            if (Stuck) return;
            int steps = c switch
            {
                SpeedReferenceClick.CoarseUp => 5 * FineSteps,
                SpeedReferenceClick.CoarseDown => -5 * FineSteps,
                SpeedReferenceClick.FineUp => FineSteps,
                _ => -FineSteps,
            };
            if (FirstClickShort && Clicks == 1) steps = Math.Sign(steps);
            Value = Math.Clamp(Value + steps * Step, Min, Max);
        }

        public Task Delay(long ms)
        {
            NowMs += ms;
            return Task.CompletedTask;
        }
    }

    private static Task<string?> Set(Pmdg737SpeedReferenceSetter setter, FakeKnob knob, double target, int position = 5, Func<double?>? read = null) =>
        setter.SetAsync(target, At(position), Names, read ?? knob.Read, knob.Click, () => knob.NowMs, knob.Delay);

    [Fact]
    public async Task Sets_by_turning_until_pmdg_reads_it()
    {
        var knob = new FakeKnob();
        Assert.Equal("VREF 142", await Set(new Pmdg737SpeedReferenceSetter(Knob), knob, 142));
        Assert.Equal(142, knob.Value);
        // The clicks for the bulk, then (read again) the wheel turns for the rest.
        Assert.Equal(new[] { SpeedReferenceClick.CoarseUp, SpeedReferenceClick.CoarseUp, SpeedReferenceClick.FineUp, SpeedReferenceClick.FineUp }, knob.Log);

        var weight = new FakeKnob { Value = 147850, Step = 1000, Min = 70000, Max = 200000 };
        Assert.Equal("WT 149850", await Set(new Pmdg737SpeedReferenceSetter(Knob), weight, 150000, position: 4));
    }

    [Fact]
    public async Task A_click_that_moves_less_is_made_up_after_reading_again()
    {
        var knob = new FakeKnob { FirstClickShort = true };
        Assert.Equal("VREF 142", await Set(new Pmdg737SpeedReferenceSetter(Knob), knob, 142));
        Assert.Equal(142, knob.Value);
    }

    [Fact]
    public async Task Without_the_reader_says_so()
    {
        var knob = new FakeKnob();
        Assert.Equal("Speed reference needs the PFD reader", await Set(new Pmdg737SpeedReferenceSetter(Knob), knob, 142, read: () => null));
        Assert.Equal(0, knob.Clicks);
    }

    [Fact]
    public async Task Gives_up_after_the_click_limit_saying_so()
    {
        var knob = new FakeKnob { Stuck = true };
        Assert.Equal("VREF 130, could not reach 142", await Set(new Pmdg737SpeedReferenceSetter(Knob), knob, 142));
        Assert.True(knob.Clicks <= Knob.MaxClicks);
    }

    [Fact]
    public async Task A_turn_that_comes_no_closer_stops_saying_so()
    {
        // A knob whose step is twice the derived one (WT under PMDG's kilogram option, not in the layout) cannot land on
        // 149000: it would hunt 148000 / 150000 until the 90 s limit. The round that comes no closer ends it.
        var weight = new FakeKnob { Value = 140000, Step = 1000, Min = 70000, Max = 200000, FineSteps = 2 };
        Assert.Equal("WT 148000, could not reach 149000", await Set(new Pmdg737SpeedReferenceSetter(Knob), weight, 149000, position: 4));
        Assert.Equal(2, weight.Clicks);
        Assert.True(weight.NowMs < 5_000, $"stopped after {weight.NowMs} ms");

        var stuck = new FakeKnob { Stuck = true };
        Assert.Equal("VREF 130, could not reach 142", await Set(new Pmdg737SpeedReferenceSetter(Knob), stuck, 142));
        Assert.Equal(2, stuck.Clicks);
    }

    [Fact]
    public async Task A_newer_value_replaces_the_turn()
    {
        var knob = new FakeKnob();
        var setter = new Pmdg737SpeedReferenceSetter(Knob);
        var gate = new TaskCompletionSource();
        bool hold = true;
        Task Delay(long ms)
        {
            knob.NowMs += ms;
            return hold ? gate.Task : Task.CompletedTask;
        }

        var first = setter.SetAsync(142, At(5), Names, knob.Read, knob.Click, () => knob.NowMs, Delay); // waits after its first click
        hold = false;
        var second = setter.SetAsync(150, At(5), Names, knob.Read, knob.Click, () => knob.NowMs, Delay);
        gate.SetResult();

        Assert.Equal("VREF 150", await second);
        Assert.Null(await first); // replaced: says nothing
        Assert.Equal(150, knob.Value);
    }

    [Fact]
    public async Task Closing_the_window_cancels_the_turn()
    {
        var knob = new FakeKnob();
        var setter = new Pmdg737SpeedReferenceSetter(Knob);
        var gate = new TaskCompletionSource();
        Task Delay(long ms)
        {
            knob.NowMs += ms;
            return gate.Task;
        }

        var turn = setter.SetAsync(142, At(5), Names, knob.Read, knob.Click, () => knob.NowMs, Delay);
        int clicksAtClose = knob.Clicks;
        setter.Cancel();
        gate.SetResult();

        Assert.Null(await turn); // the window is gone: nothing to say
        Assert.Equal(clicksAtClose, knob.Clicks); // and no click after the close
    }
}
