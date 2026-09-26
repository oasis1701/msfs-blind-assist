// The FBW FMSes clear a V-speed to a sentinel, not to nothing: FBW #10855 (a380x 1bbd304, the
// A32NX half) changed the A32NX's cleared V1/VR from 0 to -1 (V2 stays 0), and the A380 FMS writes
// the same -1/0/-1 on an FMS reset and whenever a departure-runway change sends the speeds back
// for confirmation. The A32NX clears all three every time the flight phase passes TAKEOFF, so every
// climb-out was announced as "V1: -1 knots, VR: -1 knots, V2: 0 knots".

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FbwVSpeedNotSetTests
{
    public static TheoryData<string> Airframes() => new() { "A380", "A320" };

    private static IAircraftDefinition Def(string airframe) =>
        airframe == "A380" ? new FlyByWireA380Definition() : new FlyByWireA320Definition();

    private static string Spoken(string airframe, string key, double value) =>
        new SimConnectManager(IntPtr.Zero).FormatVariableValue(key, Def(airframe).GetVariables()[key], value);

    [Theory]
    [MemberData(nameof(Airframes))]
    public void A_cleared_v_speed_is_spoken_as_not_set(string airframe)
    {
        Assert.Equal("V1: not set", Spoken(airframe, "PFD_V1", -1));
        Assert.Equal("VR: not set", Spoken(airframe, "PFD_VR", -1));
        Assert.Equal("V2: not set", Spoken(airframe, "PFD_V2", 0));
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void A_sentinel_that_came_back_through_a_unit_conversion_is_still_not_set(string airframe)
    {
        // Written in knots, stored in the sim's base unit and read back in knots: never assume the
        // double survives bit-exact, which is why this is a threshold and not a ValueDescriptions key.
        Assert.Equal("V1: not set", Spoken(airframe, "PFD_V1", -0.9999999999));
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void An_entered_v_speed_is_still_spoken_in_knots(string airframe)
    {
        Assert.Equal("V1: 150 knots", Spoken(airframe, "PFD_V1", 150));
        Assert.Equal("V2: 158 knots", Spoken(airframe, "PFD_V2", 158));
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void The_status_box_and_the_readout_share_one_not_set_test(string airframe)
    {
        // MainForm's panel formatter asks IsNotSet too: it showed "V1: -1" after the readout had
        // learned to say "not set".
        var v1 = Def(airframe).GetVariables()["PFD_V1"];
        Assert.True(v1.IsNotSet(-1));
        Assert.True(v1.IsNotSet(0));
        Assert.False(v1.IsNotSet(150));
        Assert.False(new SimVarDefinition().IsNotSet(-1));   // no threshold, no sentinel
    }

    [Theory]
    [InlineData(2)]   // Climb, delivered first
    [InlineData(1)]   // Takeoff: the phase rides batch 1 and the speeds batch 2, so the clear can
                      // arrive before the Climb phase does — never with anything below Takeoff
    public void The_a32nx_post_takeoff_clear_is_not_spoken(int phaseWhenTheClearArrives)
    {
        // A32NX_FMCMainDisplay.ts clears all three once the phase is past TAKEOFF — on every
        // climb-out, the busiest minute of the flight, with nothing for the pilot to do about it.
        var def = new FlyByWireA320Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("A32NX_FMGC_FLIGHT_PHASE", phaseWhenTheClearArrives, speech);
        speech.All.Clear();

        Assert.True(def.ProcessSimVarUpdate("PFD_V1", -1, speech));    // consumed: never reaches the monitor
        Assert.True(def.ProcessSimVarUpdate("PFD_VR", -1, speech));
        Assert.True(def.ProcessSimVarUpdate("PFD_V2", 0, speech));
        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData(0)]    // Preflight: a runway change, the FMS's only other clear — re-enter them
    [InlineData(-1)]   // phase unknown
    public void A_clear_before_takeoff_thrust_is_still_spoken(int phase)
    {
        var def = new FlyByWireA320Definition();
        if (phase >= 0) def.ProcessSimVarUpdate("A32NX_FMGC_FLIGHT_PHASE", phase, new SpeechCapture());

        Assert.False(def.ProcessSimVarUpdate("PFD_V1", -1, new SpeechCapture()));   // left to the monitor
    }

    [Fact]
    public void An_entered_speed_in_flight_is_still_spoken()
    {
        var def = new FlyByWireA320Definition();
        def.ProcessSimVarUpdate("A32NX_FMGC_FLIGHT_PHASE", 2, new SpeechCapture());

        Assert.False(def.ProcessSimVarUpdate("PFD_V1", 140, new SpeechCapture()));
    }
}
