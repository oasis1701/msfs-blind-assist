// Characterization tests for the Fenix definition's side of the display channel
// (FenixA320Definition.DisplayChannel.cs): the two SimConnect values the trust gate cross-checks the channel
// against, and what ProcessSimVarUpdate does with them. No sim and no socket: FenixTestHost turns the background
// seams off for the whole test assembly, so no monitor starts here (the monitor, the gate and the fallback switch
// have tests of their own). Starting and stopping the channel and the display-key hook are sim-facing ([CORE-5]),
// so they are checked in the sim.
//
// The contract: the heading and the total fuel ride the 1 Hz continuous batch under keys of the channel's own, are
// the only batched key with their SimConnect name ([VAR-7]), keep the default change tolerance ([SIM-7]) and stay
// out of Ctrl+M ([VAR-9]). ProcessSimVarUpdate consumes them silently, with a monitor or without one, so neither
// reaches MainForm's generic announcement; the flight director lights the channel also compares are only peeked, so
// they carry on to their own handling.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDefinitionTests
{
    private const string HeadingName = "PLANE HEADING DEGREES MAGNETIC";
    private const string FuelName = "FUEL TOTAL QUANTITY WEIGHT";

    // ---- The two keys ---------------------------------------------------------------------------

    [Theory]
    [InlineData(FenixChannelMonitor.HeadingKey, HeadingName, "degrees")]
    [InlineData(FenixChannelMonitor.FuelKey, FuelName, "pounds")]
    public void The_channel_variables_ride_the_batch_and_stay_out_of_ctrl_m(string key, string name, string units)
    {
        var variables = new FenixA320Definition().GetVariables();

        Assert.True(variables.ContainsKey(key), $"{key} is not registered");
        SimVarDefinition def = variables[key];
        Assert.Equal(name, def.Name);
        Assert.Equal(units, def.Units);
        Assert.Equal(SimVarType.SimVar, def.Type);
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
    public void Each_channel_name_has_one_batched_key(string name, string key)
    {
        // [VAR-7]: two continuous batched vars on one Name shift every later struct slot. The Fenix's own
        // heading and fuel keys stay on request, so the channel's key is the only batched one.
        var batched = new FenixA320Definition().GetVariables()
            .Where(kv => kv.Value.Name == name && ContinuousBatchLayout.RidesBatch(kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        Assert.Equal(new[] { key }, batched);
    }

    [Theory]
    [InlineData(FenixChannelMonitor.HeadingKey)]
    [InlineData(FenixChannelMonitor.FuelKey)]
    public void The_channel_variables_keep_the_default_tolerance_and_the_one_second_batch(string key)
    {
        // [SIM-7]: the gate treats the last value SimConnect delivered as its current one, so a tolerance wider
        // than a second's drift would leave a slowly moving heading or fuel undelivered, and the gate would read
        // the stale value against the channel's live one: a false disagreement, which latches. They also stay
        // on the 1 Hz batch, whose lag the gate absorbs by comparing time-aligned.
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

        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void The_channel_keys_are_consumed_whatever_value_arrives(double value)
    {
        // Consumption never depends on the value: a heading or a fuel of 0, or one that is not a number, must
        // not fall through to MainForm's generic announcement either.
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.True(definition.ProcessSimVarUpdate(FenixChannelMonitor.HeadingKey, value, speech));
        Assert.True(definition.ProcessSimVarUpdate(FenixChannelMonitor.FuelKey, value, speech));

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
        // 127.0.0.1:18083) never starts here, and the peek still consumes the two keys without one.
        Assert.False(FenixA320Definition.BackgroundSeamsEnabled, "FenixTestHost's module initializer did not run");
        var definition = new FenixA320Definition();

        definition.ProcessSimVarUpdate(FenixChannelMonitor.HeadingKey, 271.5, new SpeechCapture());
        definition.ProcessSimVarUpdate(FenixChannelMonitor.FuelKey, 6480, new SpeechCapture());
        definition.ProcessSimVarUpdate("I_FCU_EFIS1_FD", 1, new SpeechCapture());

        Assert.Null(definition.DisplayChannelMonitor);
        Assert.False(definition.BackgroundStarted);
    }
}
