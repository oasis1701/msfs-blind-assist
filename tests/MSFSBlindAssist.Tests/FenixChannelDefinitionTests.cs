// Characterization tests for the Fenix definition's side of the display channel
// (FenixA320Definition.DisplayChannel.cs). No sim and no socket: FenixTestHost turns the background seams off for the
// whole test assembly, so no monitor starts here (the monitor, the gate and the key dispatch have tests of their
// own). Starting and stopping the channel and the key hook in HandleHotkeyAction are sim-facing ([CORE-5]), so they
// are checked in the sim.
//
// The contract: the channel asks SimConnect for nothing. The gate compares none of its values with SimConnect's
// ([FNX-22]), so the keys that once carried them on the 1 Hz batch (the heading, the fuel, the true altitude and the
// annunciator light switch) stay gone, and the flight director lights go straight to the Fenix's own handling.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDefinitionTests
{
    [Theory]
    [InlineData("FENIX_CHANNEL_HEADING")]
    [InlineData("FENIX_CHANNEL_FUEL")]
    [InlineData("FENIX_CHANNEL_ALTITUDE")]
    [InlineData("FENIX_CHANNEL_ANN_LT")]
    public void The_channel_registers_no_simconnect_key_to_compare_with(string key)
    {
        Assert.False(new FenixA320Definition().GetVariables().ContainsKey(key), $"{key} is registered again");
    }

    [Fact]
    public void No_variable_belongs_to_the_channel()
    {
        Assert.DoesNotContain(
            new FenixA320Definition().GetVariables(),
            kv => (kv.Value.DisplayName ?? "").StartsWith("Fenix display channel", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("I_FCU_EFIS1_FD")]
    [InlineData("I_FCU_EFIS2_FD")]
    public void The_flight_director_lights_carry_on_to_their_own_handling(string key)
    {
        // The Fenix's own handling leaves them to MainForm's generic announcement (false).
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.False(definition.ProcessSimVarUpdate(key, 1, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void No_monitor_starts_in_the_tests()
    {
        // FenixTestHost keeps every background seam off, so the channel's monitor (which would open
        // 127.0.0.1:18083) never starts here.
        Assert.False(FenixA320Definition.BackgroundSeamsEnabled, "FenixTestHost's module initializer did not run");
        var definition = new FenixA320Definition();

        definition.ProcessSimVarUpdate("I_FCU_EFIS1_FD", 1, new SpeechCapture());

        Assert.Null(definition.DisplayChannelMonitor);
        Assert.False(definition.BackgroundStarted);
    }
}
