using System.Globalization;
using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.Hotkeys;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class A220FcpKeysTests
{
    [Theory]
    [InlineData(HotkeyAction.FCUSpeedPush, A220FcpKeys.Act.SpeedFms)]
    [InlineData(HotkeyAction.FCUSpeedPull, A220FcpKeys.Act.SpeedManual)]
    [InlineData(HotkeyAction.FCUHeadingPush, A220FcpKeys.Act.HeadingSync)]
    [InlineData(HotkeyAction.FCUHeadingPull, A220FcpKeys.Act.HeadingMode)]
    [InlineData(HotkeyAction.FCUAltitudePush, A220FcpKeys.Act.Vnav)]
    [InlineData(HotkeyAction.FCUAltitudePull, A220FcpKeys.Act.Flc)]
    [InlineData(HotkeyAction.FCUVSPush, A220FcpKeys.Act.LevelOff)]
    [InlineData(HotkeyAction.FCUVSPull, A220FcpKeys.Act.VsMode)]
    public void EveryPushPullKey_MapsToTheA220ControlDoingItsJob(HotkeyAction key, A220FcpKeys.Act act)
        => Assert.Equal(act, A220FcpKeys.For(key));

    [Fact]
    public void OtherKeys_AreNotFcpKeys()
    {
        Assert.Null(A220FcpKeys.For(HotkeyAction.FCUSetSpeed));
        Assert.Null(A220FcpKeys.For(HotkeyAction.ToggleAutopilot1));
    }

    [Theory]
    [InlineData(A220FcpKeys.Act.HeadingMode, "A22X_FG_HEADING")]
    [InlineData(A220FcpKeys.Act.Vnav, "A22X_FG_VNAV")]
    [InlineData(A220FcpKeys.Act.Flc, "A22X_FG_FLC")]
    [InlineData(A220FcpKeys.Act.LevelOff, "A22X_FG_ALT")]
    [InlineData(A220FcpKeys.Act.VsMode, "A22X_FG_VS")]
    public void ModeKeys_AreTheFgAnnunciators(A220FcpKeys.Act act, string key)
        => Assert.Equal(key, A220FcpKeys.ModeKey(act));

    [Theory]
    [InlineData(A220FcpKeys.Act.SpeedFms)]
    [InlineData(A220FcpKeys.Act.SpeedManual)]
    [InlineData(A220FcpKeys.Act.HeadingSync)]
    public void RingAndSync_AreNotModeButtons(A220FcpKeys.Act act) => Assert.Null(A220FcpKeys.ModeKey(act));

    /// <summary>The mode buttons TOGGLE: a key names a target, so it never presses an engaged
    /// mode (that would turn it OFF). Unknown state presses; the read-back speaks the truth.</summary>
    [Theory]
    [InlineData(A220FcpKeys.Act.Flc, 1.0, false)]
    [InlineData(A220FcpKeys.Act.Flc, 0.0, true)]
    [InlineData(A220FcpKeys.Act.Flc, null, true)]
    [InlineData(A220FcpKeys.Act.Flc, -1.0, true)]
    [InlineData(A220FcpKeys.Act.Vnav, 1.0, false)]
    [InlineData(A220FcpKeys.Act.HeadingSync, 1.0, true)]
    [InlineData(A220FcpKeys.Act.SpeedFms, 1.0, true)]
    public void ShouldPress_NeverTogglesAnEngagedModeOff(A220FcpKeys.Act act, double? state, bool press)
        => Assert.Equal(press, A220FcpKeys.ShouldPress(act, state));

    [Fact]
    public void SpeedRing_WritesTheTargetNotAToggle()
    {
        Assert.Equal(0, A220FcpKeys.SpeedRingValue(A220FcpKeys.Act.SpeedFms));
        Assert.Equal(1, A220FcpKeys.SpeedRingValue(A220FcpKeys.Act.SpeedManual));
    }

    [Theory]
    [InlineData(A220FcpKeys.Act.SpeedFms, "Already FMS speed.")]
    [InlineData(A220FcpKeys.Act.SpeedManual, "Already manual speed.")]
    [InlineData(A220FcpKeys.Act.Flc, "FLC already engaged.")]
    [InlineData(A220FcpKeys.Act.LevelOff, "Altitude hold already engaged.")]
    public void AlreadyText_SaysTheTargetIsInPlace(A220FcpKeys.Act act, string text)
        => Assert.Equal(text, A220FcpKeys.AlreadyText(act));

    [Theory]
    [InlineData(true, true, null, null, false, "FMS speed")]
    [InlineData(false, false, 250.0, null, false, "Manual speed, 250 knots")]
    [InlineData(false, false, null, 0.78, true, "Manual speed, Mach 0.78")]
    [InlineData(false, true, 250.0, null, false, "Manual speed did not engage — still FMS speed")]
    [InlineData(true, null, null, null, false, "Speed mode not readable")]
    public void SpeedResult_ReadsTheRingBack(bool wantFms, bool? isFms, double? kt, double? mach, bool inMach, string text)
        => Assert.Equal(text, A220FcpKeys.SpeedResult(wantFms, isFms, kt, mach, inMach));

    [Fact]
    public void SpeedResult_MachIsInvariantUnderACommaCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Manual speed, Mach 0.78", A220FcpKeys.SpeedResult(false, false, null, 0.78, true));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Theory]
    [InlineData(245.0, "Heading synced, 245")]
    [InlineData(5.0, "Heading synced, 005")]
    [InlineData(null, "Heading synced to the current heading")]
    public void HeadingSyncResult_SpeaksTheNewSelection(double? hdg, string text)
        => Assert.Equal(text, A220FcpKeys.HeadingSyncResult(hdg));
}
