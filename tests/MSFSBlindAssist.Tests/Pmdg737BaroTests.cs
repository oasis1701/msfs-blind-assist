// Characterization tests for Pmdg737Baro (Aircraft/Pmdg737Baro.cs): setting the PMDG 737
// captain's altimeter by turning its EFIS baro knob (Ctrl+B). The NG3 SDK has no STD flag and STD
// reads 29.92 inHg exactly as a local 29.92 does, so a reading of 29.92 is told apart by the knob:
// a click moves the reading when STD is off, and only the preselect when STD is on.
//
// FakeBaro models the baro as the dialog meets it: with STD on the reading is 29.92 and a click
// moves the preselect; pushing STD on keeps the setting as the preselect, pushing it off brings the
// preselect up. A read answers what the aircraft shows after the last action, as a fresh delivery
// does, never a value cached from before it.

namespace MSFSBlindAssist.Tests;

using MSFSBlindAssist.Aircraft;

public class Pmdg737BaroTests
{
    private const double Hpa = 1 / Pmdg737Baro.HpaPerInHg;

    private sealed class FakeBaro : IPmdg737BaroKnob
    {
        public bool Std { get; set; }
        public double Setting { get; set; }      // inHg; the reading while STD is off
        public double Preselect { get; set; }    // inHg; shown under STD while it is on
        public bool HpaDisplay { get; init; }
        public int StdPushes { get; private set; }
        public bool Delivers { get; init; } = true;

        public double Reading => Std ? Pmdg737Baro.StandardInHg : Setting;

        public Task TurnAsync(int clicks)
        {
            double step = HpaDisplay ? Hpa : 0.01;
            if (Std) Preselect += clicks * step;
            else Setting += clicks * step;
            return Task.CompletedTask;
        }

        public Task PushStdAsync()
        {
            StdPushes++;
            if (Std) { Std = false; Setting = Preselect; }
            else { Std = true; Preselect = Setting; }
            return Task.CompletedTask;
        }

        public Task<double?> ReadSettledAsync() => Task.FromResult<double?>(Delivers ? Reading : null);
    }

    [Fact]
    public async Task ALocal2992_IsNotTakenForStd()
    {
        // The pilot set 29.92 as the local altimeter; STD is off. The old guard pushed STD here,
        // which turned it ON and hid every click after it behind STD.
        var baro = new FakeBaro { Std = false, Setting = 29.92 };

        bool delivered = await Pmdg737Baro.SetFromStandardAsync(baro, baro.Reading, 30.05);

        Assert.True(delivered);
        Assert.False(baro.Std);
        Assert.Equal(0, baro.StdPushes);
        Assert.Equal(30.05, baro.Reading, 3);
    }

    [Fact]
    public async Task StdOn_IsPushedOff_AndTheClicksCountFromTheReadingAfterThePush()
    {
        // STD on over a 30.12 preselect. The old path counted its clicks from the 29.92 cached
        // before the push (a 1 Hz delivery 200 ms later rarely carries the new value) and landed
        // 13 clicks above the preselect, at 30.25.
        var baro = new FakeBaro { Std = true, Setting = 30.12, Preselect = 30.12 };

        bool delivered = await Pmdg737Baro.SetFromStandardAsync(baro, baro.Reading, 30.05);

        Assert.True(delivered);
        Assert.False(baro.Std);
        Assert.Equal(1, baro.StdPushes);
        Assert.Equal(30.05, baro.Reading, 3);
    }

    [Fact]
    public async Task StdOn_OverA2992Preselect_StillComesOff()
    {
        var baro = new FakeBaro { Std = true, Setting = 29.92, Preselect = 29.92 };

        await Pmdg737Baro.SetFromStandardAsync(baro, baro.Reading, 29.85);

        Assert.False(baro.Std);
        Assert.Equal(29.85, baro.Reading, 3);
    }

    [Fact]
    public async Task StdOn_OnAnHpaDisplay_LandsOnTheTypedHectopascals()
    {
        var baro = new FakeBaro { HpaDisplay = true, Std = true, Setting = 1020 * Hpa, Preselect = 1020 * Hpa };

        await Pmdg737Baro.SetFromStandardAsync(baro, baro.Reading, 1008);

        Assert.False(baro.Std);
        Assert.Equal(1008, baro.Reading * Pmdg737Baro.HpaPerInHg, 3);
    }

    [Fact]
    public async Task ATargetZeroClicksAway_StillLeavesStd()
    {
        // 1013 typed on an hPa display reads 1013 against STD's 1013.25: no click separates them,
        // yet the pilot asked for a QNH. The check turns up one click and the rest comes back.
        var baro = new FakeBaro { HpaDisplay = true, Std = true, Setting = 1020 * Hpa, Preselect = 1020 * Hpa };

        await Pmdg737Baro.SetFromStandardAsync(baro, baro.Reading, 1013);

        Assert.False(baro.Std);
        Assert.Equal(1013, baro.Reading * Pmdg737Baro.HpaPerInHg, 3);
    }

    [Fact]
    public async Task ATargetZeroClicksAway_WithStdOff_EndsWhereItWas()
    {
        // The same 1013 with STD off over a local 29.92 (1013.2 hPa): the check's click up comes back.
        var baro = new FakeBaro { HpaDisplay = true, Std = false, Setting = 29.92 };

        await Pmdg737Baro.SetFromStandardAsync(baro, baro.Reading, 1013);

        Assert.False(baro.Std);
        Assert.Equal(0, baro.StdPushes);
        Assert.Equal(29.92, baro.Reading, 3);
    }

    [Fact]
    public async Task NothingDelivered_GivesUpWithoutPushingStd()
    {
        var baro = new FakeBaro { Std = false, Setting = 29.92, Delivers = false };

        bool delivered = await Pmdg737Baro.SetFromStandardAsync(baro, baro.Reading, 30.05);

        Assert.False(delivered);
        Assert.Equal(0, baro.StdPushes);
    }

    [Theory]
    [InlineData(29.92, true)]
    [InlineData(29.9213, true)]    // 1013.25 hPa, the standard pressure itself
    [InlineData(29.9142, false)]   // 1013 hPa, a local setting on an hPa display
    [InlineData(29.93, false)]
    [InlineData(29.91, false)]
    public void ReadsStandard_IsTheReadingAlone(double inHg, bool expected)
    {
        Assert.Equal(expected, Pmdg737Baro.ReadsStandard(inHg));
    }

    [Theory]
    [InlineData(29.92, 30.05, true)]
    [InlineData(29.92, 1008, true)]
    [InlineData(29.92, 1013, true)]     // 1013 hPa is a QNH, not STD's 1013.25
    [InlineData(29.92, 29.92, false)]   // nothing to change
    [InlineData(30.01, 30.05, false)]   // a reading off 29.92 is not STD
    public void NeedsStdCheck_OnlyWhenTheReadingIs2992AndTheTargetIsNot(double reading, double typed, bool expected)
    {
        Assert.Equal(expected, Pmdg737Baro.NeedsStdCheck(reading, typed));
    }

    [Theory]
    [InlineData(29.92, 30.05, false, 13)]    // 0.01 inHg per click
    [InlineData(30.05, 29.92, false, -13)]
    [InlineData(29.92, 1008, false, -15)]    // typed hPa on an inches display: 29.766 -> 29.77
    [InlineData(29.92, 1008, true, -5)]      // 1 hPa per click: 1013 -> 1008
    [InlineData(29.92, 29.77, true, -5)]     // typed inches on an hPa display: 1008.1 -> 1008
    [InlineData(30.05, 30.05, false, 0)]
    public void ClicksTo_CountsInTheDisplayedUnit(double reading, double typed, bool hpaDisplay, int expected)
    {
        Assert.Equal(expected, Pmdg737Baro.ClicksTo(reading, typed, hpaDisplay));
    }
}
