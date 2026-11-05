// The Captain's minimums setter: closed-loop from the PFD reader. Ground session 2 (2026-10-30) found the old setter's
// two guesses wrong: MINS RST never zeroes the minimums (it toggles their display) and a knob click is 1 ft in BARO as
// in RADIO; every click lands, 200 back to back in about a second. So the setter reads PMDG's own value, turns the
// difference, reads again, and says what the PFD then holds. Against a fake knob here.

using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737PfdMinimumsTests
{
    private static readonly MinimumsKnob Knob = MinimumsKnob.Ng3;

    private sealed class FakeKnob
    {
        public PfdMinimums Minimums { get; set; } = new(Baro: true, Value: 215, Shown: true);
        public int Clicks { get; private set; }
        public bool Stuck { get; set; }
        /// <summary>Every Nth click is lost (0: none).</summary>
        public int DropEvery { get; set; }
        public List<long> Delays { get; } = new();

        public PfdMinimums? Read() => Minimums;

        public void Click(int direction)
        {
            Clicks++;
            if (Stuck || (DropEvery > 0 && Clicks % DropEvery == 0)) return;
            Minimums = Minimums with { Value = Minimums.Value + direction };
        }

        public Task Delay(long ms)
        {
            Delays.Add(ms);
            return Task.CompletedTask;
        }
    }

    private static Task<string> Set(FakeKnob knob, int target, Func<PfdMinimums?>? read = null) =>
        new Pmdg737MinimumsSetter(Knob).SetAsync(target, read ?? knob.Read, knob.Click, knob.Delay);

    [Fact]
    public void Readback_says_the_mode_the_value_and_whether_the_pfd_shows_it()
    {
        Assert.Equal("Minimums: BARO 300", Pmdg737PfdMinimums.Readback(new PfdMinimums(true, 300, true)));
        Assert.Equal("Minimums: RADIO 50, not shown", Pmdg737PfdMinimums.Readback(new PfdMinimums(false, 50, false)));
        Assert.Equal("Minimums: BARO 1100", TestCulture.Run("de-DE", () => Pmdg737PfdMinimums.Readback(new PfdMinimums(true, 1100, true))));
    }

    [Fact]
    public async Task Sets_by_turning_the_difference()
    {
        var knob = new FakeKnob();
        Assert.Equal("Minimums: BARO 300", await Set(knob, 300));
        Assert.Equal(85, knob.Clicks);
        // In batches, then a settle before the reading that confirms it.
        Assert.Contains(Knob.BatchGapMs, knob.Delays);
        Assert.Equal(Knob.SettleMs, knob.Delays[^1]);
    }

    [Fact]
    public async Task Turns_down_as_well()
    {
        var knob = new FakeKnob { Minimums = new PfdMinimums(false, 250, true) };
        Assert.Equal("Minimums: RADIO 200", await Set(knob, 200));
        Assert.Equal(50, knob.Clicks);
    }

    [Fact]
    public async Task Already_there_says_so_without_a_click()
    {
        var knob = new FakeKnob { Minimums = new PfdMinimums(true, 300, false) };
        Assert.Equal("Minimums: BARO 300, not shown", await Set(knob, 300));
        Assert.Equal(0, knob.Clicks);
    }

    [Fact]
    public async Task Lost_clicks_are_made_up_after_reading_again()
    {
        var knob = new FakeKnob { DropEvery = 10 };
        Assert.Equal("Minimums: BARO 300", await Set(knob, 300));
        Assert.True(knob.Clicks > 85);
    }

    [Fact]
    public async Task Gives_up_after_its_rounds_saying_so()
    {
        var knob = new FakeKnob { Stuck = true };
        Assert.Equal("Minimums: BARO 215, could not reach 300", await Set(knob, 300));
        Assert.Equal(85 * Knob.MaxRounds, knob.Clicks);
    }

    [Fact]
    public async Task Without_the_reader_says_so()
    {
        var knob = new FakeKnob();
        Assert.Equal("Minimums need the PFD reader", await Set(knob, 300, read: () => null));
        Assert.Equal(0, knob.Clicks);
    }
}
