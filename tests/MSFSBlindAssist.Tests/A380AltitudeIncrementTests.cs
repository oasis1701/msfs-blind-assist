// FBW #10855 made the A380's altitude increment the FCU's own L:A32NX_FCU_ALT_INCREMENT_1000 (0 = 100 ft,
// 1 = 1000 ft), set through A32NX.FCU_ALT_INCREMENT_SET 100/1000. MSFSBA reads it back as an announced
// combo, so the Altitude window's Increment 100/1000 buttons came back about a second after the press
// as "Altitude Increment: N" — over the button the screen reader had just read, which the
// announcement rules forbid. The buttons now arm the same time-boxed mute SetFCUAltitudeValue arms for
// its own side effect.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class A380AltitudeIncrementTests
{
    [Fact]
    public void The_altitude_window_buttons_are_not_echoed()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();

        def.SetAltIncrement(1000, new SimConnectManager(IntPtr.Zero));   // never connected: sends nothing

        // Consumed by the definition, so MainForm's generic monitor never speaks it.
        Assert.True(def.ProcessSimVarUpdate("A32NX_FCU_ALT_INCREMENT_1000", 1, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_change_nobody_asked_for_is_left_to_the_monitor()
    {
        // The cockpit knob, or a hardware panel: a background change, spoken by the generic monitor.
        var def = new FlyByWireA380Definition();

        Assert.False(def.ProcessSimVarUpdate("A32NX_FCU_ALT_INCREMENT_1000", 1, new SpeechCapture()));
    }

    [Fact]
    public void The_increment_is_the_fcu_s_own_input()
    {
        var def = new FlyByWireA380Definition().GetVariables()["A32NX_FCU_ALT_INCREMENT_1000"];

        Assert.Equal("A32NX_FCU_ALT_INCREMENT_1000", def.Name);
        Assert.Equal("100", def.ValueDescriptions![0]);
        Assert.Equal("1000", def.ValueDescriptions![1]);
    }
}
