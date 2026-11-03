// Characterization tests for the Fenix definition's side of the display channel
// (FenixA320Definition.DisplayChannel.cs): the three SimConnect values the trust gate cross-checks the channel
// against, the annunciator light switch that tells it when the FD lamps are under test, and what
// ProcessSimVarUpdate does with them. No sim and no socket: FenixTestHost turns the background seams off for the
// whole test assembly, so no monitor starts here (the monitor, the gate and the key dispatch have tests of their
// own). Starting and stopping the channel and the key hook in HandleHotkeyAction are sim-facing ([CORE-5]), so they
// are checked in the sim.
//
// The contract: the heading, the total fuel, the true altitude and the light switch ride the 1 Hz continuous batch
// under keys of the channel's own, are the only batched key with their SimConnect name ([VAR-7]), keep the default
// change tolerance ([SIM-7]) and stay out of Ctrl+M ([VAR-9]). ProcessSimVarUpdate consumes them silently, with a
// monitor or without one, so none reaches MainForm's generic announcement; the flight director lights the channel
// also compares are only peeked, so they carry on to their own handling.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDefinitionTests
{
    private const string HeadingName = "PLANE HEADING DEGREES MAGNETIC";
    private const string FuelName = "FUEL TOTAL QUANTITY WEIGHT";
    private const string AltitudeName = "PLANE ALTITUDE";
    private const string LightSwitchName = "S_OH_IN_LT_ANN_LT";

    // ---- The channel's keys ---------------------------------------------------------------------

    [Theory]
    [InlineData(FenixChannelMonitor.HeadingKey, HeadingName, "degrees", "Fenix display channel: heading check",
        SimVarType.SimVar)]
    [InlineData(FenixChannelMonitor.FuelKey, FuelName, "pounds", "Fenix display channel: fuel check",
        SimVarType.SimVar)]
    [InlineData(FenixChannelMonitor.AltitudeKey, AltitudeName, "feet", "Fenix display channel: altitude check",
        SimVarType.SimVar)]
    [InlineData(FenixChannelMonitor.LightTestKey, LightSwitchName, "number", "Fenix display channel: light test check",
        SimVarType.LVar)]
    public void The_channel_variables_ride_the_batch_and_stay_out_of_ctrl_m(string key, string name, string units,
        string displayName, SimVarType type)
    {
        var variables = new FenixA320Definition().GetVariables();

        Assert.True(variables.ContainsKey(key), $"{key} is not registered");
        SimVarDefinition def = variables[key];
        Assert.Equal(name, def.Name);
        Assert.Equal(units, def.Units);
        Assert.Equal(displayName, def.DisplayName);
        Assert.Equal(type, def.Type);
        Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
        // Nothing is spoken (see the silent-consumption tests below), but only an ANNOUNCED Continuous var is
        // subscribed: without it nothing would ever request the value.
        Assert.True(def.IsAnnounced, $"{key} must be announced-flagged or nothing ever reads it");
        Assert.True(def.ExcludeFromMonitorManager, $"{key} would earn a Ctrl+M row that mutes nothing");
        // The batch is what delivers it to ProcessSimVarUpdate, and a var past the five registered batches is
        // never requested.
        Assert.True(ContinuousBatchLayout.RidesBatch(def), $"{key} does not ride the continuous batch");
        int batch = ContinuousBatchLayout.BatchNumbers(variables).GetValueOrDefault(key);
        Assert.True(batch is >= 1 and <= 5, $"{key} lands in batch {batch}, outside the five that are registered");
    }

    [Theory]
    [InlineData(HeadingName, FenixChannelMonitor.HeadingKey)]
    [InlineData(FuelName, FenixChannelMonitor.FuelKey)]
    [InlineData(AltitudeName, FenixChannelMonitor.AltitudeKey)]
    [InlineData(LightSwitchName, FenixChannelMonitor.LightTestKey)]
    public void Each_channel_name_has_one_batched_key(string name, string key)
    {
        // [VAR-7]: two continuous batched vars on one Name shift every later struct slot. The Fenix's own
        // heading, fuel and light switch keys stay on request, and nothing else asks for PLANE ALTITUDE as a
        // variable, so the channel's key is the only batched one.
        var batched = new FenixA320Definition().GetVariables()
            .Where(kv => kv.Value.Name == name && ContinuousBatchLayout.RidesBatch(kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        Assert.Equal(new[] { key }, batched);
    }

    [Theory]
    [InlineData(FenixChannelMonitor.HeadingKey)]
    [InlineData(FenixChannelMonitor.FuelKey)]
    [InlineData(FenixChannelMonitor.AltitudeKey)]
    [InlineData(FenixChannelMonitor.LightTestKey)]
    public void The_channel_variables_keep_the_default_tolerance_and_the_one_second_batch(string key)
    {
        // [SIM-7]: the gate treats the last value SimConnect delivered as its current one, so a tolerance wider
        // than a second's drift would leave a slowly moving heading, fuel or altitude undelivered, and the gate
        // would read the stale value against the channel's live one: a false disagreement, which latches. They
        // also stay on the 1 Hz batches, whose lag the gate absorbs by comparing time-aligned. Today the light switch
        // and the FD lamps it explains fall in separate batches (adding variables can move them), in an order nothing
        // pins; both are 1 Hz requests read by the same message loop, so the gap between them stays under one period
        // plus any UI stall. A frame inside it can start an "FD ... disagrees" at the start of a test (the lamps lit
        // before the switch reads Test) or at its end (the switch back before the lamps go dark); neither latches
        // while the gap stays under MismatchHoldMs.
        SimVarDefinition def = new FenixA320Definition().GetVariables()[key];

        Assert.Null(def.ChangeTolerance);
        Assert.False(def.ExcludeFromBatch, $"{key} left the batch");
        Assert.False(def.HighFrequency, $"{key} asked for per-frame delivery");
    }

    // ---- What ProcessSimVarUpdate does with them ------------------------------------------------

    [Fact]
    public void The_channel_keys_are_consumed_silently()
    {
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        // True is "the definition handled it": MainForm then never speaks the generic "name: value" for it.
        Assert.True(definition.ProcessSimVarUpdate(FenixChannelMonitor.HeadingKey, 271.5, speech));
        Assert.True(definition.ProcessSimVarUpdate(FenixChannelMonitor.FuelKey, 6480, speech));
        Assert.True(definition.ProcessSimVarUpdate(FenixChannelMonitor.AltitudeKey, 356, speech));
        Assert.True(definition.ProcessSimVarUpdate(FenixChannelMonitor.LightTestKey, 2, speech));

        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    public void The_channel_keys_are_consumed_whatever_value_arrives(double value)
    {
        // Consumption never depends on the value: a heading, a fuel or an altitude of 0, a light switch at any
        // position, or a value that is not a number, must not fall through to MainForm's generic announcement either.
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.True(definition.ProcessSimVarUpdate(FenixChannelMonitor.HeadingKey, value, speech));
        Assert.True(definition.ProcessSimVarUpdate(FenixChannelMonitor.FuelKey, value, speech));
        Assert.True(definition.ProcessSimVarUpdate(FenixChannelMonitor.AltitudeKey, value, speech));
        Assert.True(definition.ProcessSimVarUpdate(FenixChannelMonitor.LightTestKey, value, speech));

        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData("I_FCU_EFIS1_FD")]
    [InlineData("I_FCU_EFIS2_FD")]
    public void The_flight_director_lights_are_only_peeked(string key)
    {
        // The lights feed the gate's FD cross-check but are not the channel's to consume: they carry on to the
        // Fenix's own handling, which leaves them to MainForm's generic announcement (false), as before.
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.False(definition.ProcessSimVarUpdate(key, 1, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void No_monitor_starts_in_the_tests()
    {
        // FenixTestHost keeps every background seam off, so the channel's monitor (which would open
        // 127.0.0.1:18083) never starts here, and the peek still consumes the channel's keys without one.
        Assert.False(FenixA320Definition.BackgroundSeamsEnabled, "FenixTestHost's module initializer did not run");
        var definition = new FenixA320Definition();

        definition.ProcessSimVarUpdate(FenixChannelMonitor.HeadingKey, 271.5, new SpeechCapture());
        definition.ProcessSimVarUpdate(FenixChannelMonitor.FuelKey, 6480, new SpeechCapture());
        definition.ProcessSimVarUpdate(FenixChannelMonitor.AltitudeKey, 356, new SpeechCapture());
        definition.ProcessSimVarUpdate(FenixChannelMonitor.LightTestKey, 2, new SpeechCapture());
        definition.ProcessSimVarUpdate("I_FCU_EFIS1_FD", 1, new SpeechCapture());

        Assert.Null(definition.DisplayChannelMonitor);
        Assert.False(definition.BackgroundStarted);
    }
}
