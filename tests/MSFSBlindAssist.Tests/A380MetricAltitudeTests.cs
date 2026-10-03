// FBW #10855 moved the A380's metric-altitude (MTRS) button onto the FCU: it fires
// A32NX.FCU_METRIC_ALT_TOGGLE_PUSH, and the mode is the PRIM's — FG discrete word 5 bit 14, what
// the PFD altitude tape itself reads (AltitudeIndicator.tsx). L:A32NX_METRIC_ALT_TOGGLE, which
// MSFSBA wrote and read, has had no reader since: a pick changed nothing, and MSFSBA's own
// metric flag followed its own write — so after one MSFSBA toggle the Altitude window took a
// typed altitude as METRES while the FCU was still in feet.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class A380MetricAltitudeTests
{
    // A PRIM FG discrete word as FBW packs it: the bitfield converted to a float NUMERICALLY,
    // that float's IEEE-754 bits in the low 32, the SSM above them (Arinc429WordTests).
    private static double FgWord(uint ssm, uint bitfield) =>
        (double)(((ulong)ssm << 32) | BitConverter.SingleToUInt32Bits((float)bitfield));
    private const uint NormalOperation = 0b11, NoComputedData = 0b01;
    private const uint Bit14 = 1u << 13;

    [Theory]
    [InlineData(Bit14, true)]
    [InlineData(0u, false)]
    [InlineData((1u << 11) | (1u << 12), false)]   // bits 12/13 (TRK/FPA, Mach) are not metric
    public void The_mode_is_prim_fg_word_5_bit_14(uint bits, bool metric)
    {
        Assert.Equal(metric, A380MetricAltitude.IsActive(FgWord(NormalOperation, bits)));
    }

    [Fact]
    public void A_word_without_data_says_nothing()
    {
        Assert.Null(A380MetricAltitude.IsActive(FgWord(NoComputedData, Bit14)));
    }

    [Theory]
    [InlineData(1.0, false, true)]
    [InlineData(1.0, true, false)]
    [InlineData(0.0, true, true)]
    [InlineData(0.0, false, false)]
    public void A_set_presses_mtrs_only_when_the_mode_differs(double desired, bool current, bool presses)
    {
        Assert.Equal(presses ? "A32NX.FCU_METRIC_ALT_TOGGLE_PUSH" : null, A380MetricAltitude.Command(desired, current));
    }

    [Fact]
    public void An_unknown_mode_presses()
    {
        Assert.Equal("A32NX.FCU_METRIC_ALT_TOGGLE_PUSH", A380MetricAltitude.Command(1, null));
    }

    [Theory]
    [InlineData(0.0, 0.0)]   // the combo's own keys, as MainForm caches a pick until the next read
    [InlineData(1.0, 1.0)]
    public void The_combo_classifier_passes_its_own_keys_through(double valueOrKey, double key)
    {
        Assert.Equal(key, A380MetricAltitude.DescriptionKey(valueOrKey));
    }

    [Theory]
    [InlineData(Bit14, 1.0)]
    [InlineData(0u, 0.0)]
    public void The_combo_classifier_decodes_a_delivered_word(uint bits, double key)
    {
        Assert.Equal(key, A380MetricAltitude.DescriptionKey(FgWord(NormalOperation, bits)));
    }

    [Fact]
    public void The_combo_reads_the_prim_word_and_is_never_batched()
    {
        // FMA_FG_ALERTS already carries this word in the continuous batch; a second Continuous key
        // on the same Name would shift every later slot (VarNameCollisionTests).
        var def = new FlyByWireA380Definition().GetVariables()[A380MetricAltitude.ControlKey];
        Assert.Equal("A32NX_PRIM_1_FG_DISCRETE_WORD_5", def.Name);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        Assert.Equal("On", def.ValueDescriptions[def.DescriptionKeyFor(FgWord(NormalOperation, Bit14))]);
    }

    [Fact]
    public void The_altitude_units_follow_the_prim_not_msfsba_s_own_write()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, Bit14), speech);
        Assert.True(def.MetricAlt);
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, 0), speech);
        Assert.False(def.MetricAlt);
    }

    [Fact]
    public void A_background_mtrs_change_is_spoken_once_and_the_first_word_is_a_baseline()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, 0), speech);
        Assert.Empty(speech.All);
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, Bit14), speech);
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, Bit14), speech);
        Assert.Equal(new[] { "Metric altitude: on" }, speech.All);
    }

    [Fact]
    public void A_pick_or_an_mtrs_press_is_not_spoken_back()
    {
        // Both callers are the pilot's own UI: the screen reader read the combo pick, and the Altitude
        // window's MTRS button carries the new state in its accessible name the moment it is pressed.
        // The PRIM confirming either is the echo.
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, 0), speech);

        Assert.NotNull(def.MetricAltitudeCommand(1));
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, Bit14), speech);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_reconnect_keeps_the_unit_and_the_next_change_is_spoken()
    {
        // ResetAnnouncementBaselines runs on every reconnect, AFTER the reconnect's first batch has
        // re-fired every var (the ordering trap CLAUDE.md records under the MD-11). It must keep the
        // unit — or a typed altitude is taken as feet on a metric FCU — and must NOT clear the
        // call-out's baseline, or the first real MTRS change after the reconnect is swallowed as
        // one. No reset clears it: the FCU settle a context reset begins absorbs the new context's
        // first words instead (see the flight-load tests below).
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, Bit14), speech);   // baseline

        def.ResetAnnouncementBaselines();
        Assert.True(def.MetricAlt);

        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, 0), speech);
        Assert.Equal(new[] { "Metric altitude: off" }, speech.All);
    }

    [Fact]
    public void A_new_context_keeps_the_last_unit_but_treats_it_as_unknown()
    {
        // After a flight load or a dropped connection the last word may no longer be true: the
        // read-outs keep its unit until the next word, but a pick presses MTRS (unknown is not "same").
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, Bit14), speech);

        def.OnSimContextReset();

        Assert.True(def.MetricAlt);
        Assert.Equal("A32NX.FCU_METRIC_ALT_TOGGLE_PUSH", def.MetricAltitudeCommand(1));
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, 0), speech);   // a baseline again
        Assert.Empty(speech.All);
    }

    // A flight load re-delivers only CHANGED vars: a word 5 that is the same in the new flight never
    // arrives. The FCU settle (BaseAircraftDefinition, OnSimContextReset) ends after 30 batch-1
    // deliveries at most, whatever the aircraft publishes.
    private static void SettleWithoutTheWord(FlyByWireA380Definition def)
    {
        for (int i = 0; i < 30; i++) def.OnContinuousBatchDelivered(1);
    }

    [Fact]
    public void After_a_flight_load_with_an_unchanged_word_the_next_mtrs_change_is_spoken()
    {
        // Clearing the call-out's baseline on the context reset left it cleared for good when the word
        // was not re-delivered, so the next REAL change was recorded as a baseline and never spoken.
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, 0), speech);   // baseline, feet

        def.OnSimContextReset();
        SettleWithoutTheWord(def);

        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, Bit14), speech);
        Assert.Equal(new[] { "Metric altitude: on" }, speech.All);
    }

    [Fact]
    public void After_a_flight_load_with_an_unchanged_word_the_mode_is_known_again()
    {
        // The aircraft has published and gone quiet with no new word: the last word stands, so a pick
        // of the mode already in force presses nothing (a press would toggle MTRS the wrong way).
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(NormalOperation, 0), speech);

        def.OnSimContextReset();
        SettleWithoutTheWord(def);

        Assert.Null(def.MetricAltitudeCommand(0));
    }

    [Fact]
    public void A_just_commanded_mode_is_what_the_altitude_window_sees()
    {
        // Pressing MTRS in the Altitude window relabels its button and sets the unit a typed altitude
        // is taken in straight away, before the PRIM's next word arrives.
        var def = new FlyByWireA380Definition();

        Assert.Equal("A32NX.FCU_METRIC_ALT_TOGGLE_PUSH", def.MetricAltitudeCommand(1));
        Assert.True(def.MetricAlt);
        Assert.Null(def.MetricAltitudeCommand(1));   // a second "on": no press
    }
}
