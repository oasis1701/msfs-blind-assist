// The iFly 737 MAX speaks its stabiliser trim in UNITS, from its own SDK field, with the same rule
// and wording as the PMDG 737 (StabTrimUnitsCallout). It used to inherit the shared call-out, which
// reads the stock ELEVATOR TRIM POSITION in degrees: live 2026-09-30 it said "Trim down 2.3" while
// the indicator showed 5.6 units.

using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests.IFly;

public class IFly737StabTrimUnitsTests
{
    private static List<string> Feed(IAircraftDefinition def, string key, params double[] values)
    {
        var speech = new SpeechCapture();
        foreach (double v in values) def.ProcessSimVarUpdate(key, v, speech);
        return speech.All;
    }

    [Fact]
    public void The_iFly_no_longer_registers_the_degrees_call_out()
    {
        var vars = new IFly737MAXDefinition().GetVariables();
        Assert.False(vars.ContainsKey("MON_ElevatorTrim"));
        Assert.True(vars[IFly737MAXDefinition.StabTrimUnitsKey].IsAnnounced);
        Assert.False(vars[IFly737MAXDefinition.StabTrimUnitsKey].ExcludeFromMonitorManager);
    }

    [Fact]
    public void The_iFly_speaks_units_with_a_dot_after_a_silent_baseline()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            // The live cruise value, then jitter inside the step, then a real move.
            Assert.Equal(new[] { "Trim 6.0" },
                Feed(new IFly737MAXDefinition(), IFly737MAXDefinition.StabTrimUnitsKey, 5.58, 5.62, 5.57, 6.0));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(5.03, 5.06, 5.04, 5.06)]
    [InlineData(5.03, 5.06, 5.3)]
    [InlineData(4.0, 4.5, 3.9, 8.25, 8.26)]
    public void Both_737s_say_trim_the_same_way(params double[] units)
    {
        Assert.Equal(
            Feed(new PMDG737Definition(), "MON_PMDG737_StabTrim", units),
            Feed(new IFly737MAXDefinition(), IFly737MAXDefinition.StabTrimUnitsKey, units));
    }

    [Fact]
    public void A_context_reset_never_empties_the_baseline_so_the_next_move_is_spoken()
    {
        // The SDK's re-seed after a flight load arrives as an initial snapshot the call-out never
        // sees; emptying the baseline would swallow the pilot's first real move as the new one.
        // (With a live SDK the reset re-seeds from the snapshot; here there is none, so it keeps.)
        var def = new IFly737MAXDefinition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(IFly737MAXDefinition.StabTrimUnitsKey, 5.6, speech);   // baseline

        def.OnSimContextReset();

        def.ProcessSimVarUpdate(IFly737MAXDefinition.StabTrimUnitsKey, 6.5, speech);   // a real move
        Assert.Equal(new[] { "Trim 6.5" }, speech.All);
    }

    [Fact]
    public void A_seeded_baseline_speaks_the_first_sample_that_moves()
    {
        // The iFly seeds from the live snapshot on connect, because its opening value is never a
        // sample: the first sample the call-out sees is already a move.
        var callout = new StabTrimUnitsCallout();
        callout.Seed(5.58);
        Assert.Equal("Trim 6.0", callout.Next(6.0, 0.03));
        Assert.Null(callout.Next(6.02, 0.03));
    }

    [Fact]
    public void A_mute_on_the_old_degrees_row_moves_to_the_units_row()
    {
        var settings = new Settings.UserSettings();
        settings.IFlyDisabledMonitorVariables.Add("MON_ElevatorTrim");

        Assert.True(Settings.SettingsManager.CarryRenamedMonitorMutes(settings));
        Assert.Equal(new[] { IFly737MAXDefinition.StabTrimUnitsKey }, settings.IFlyDisabledMonitorVariables);
        Assert.False(Settings.SettingsManager.CarryRenamedMonitorMutes(settings));   // idempotent
    }

    [Fact]
    public void A_value_that_is_not_a_number_is_ignored_and_never_becomes_the_baseline()
    {
        var callout = new StabTrimUnitsCallout();
        Assert.Null(callout.Next(double.NaN, 0.03));
        Assert.Null(callout.Next(5.0, 0.03));           // the baseline, not the NaN
        Assert.Equal("Trim 5.5", callout.Next(5.5, 0.03));
    }
}
