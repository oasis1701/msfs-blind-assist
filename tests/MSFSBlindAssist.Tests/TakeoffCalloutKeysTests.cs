// The take-off roll callouts (TakeoffVSpeedCallouts) are fed and muted the same way on every airframe
// that uses them — the MD-11, the iFly 737 MAX8 and the FBW A380: each call is muted by its speed's
// Ctrl+M row, an unknown call by none (fail open). One mapping, TakeoffCalloutKeys, instead of a
// hand-kept copy per airframe (found by review, 2026-09-25).

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class TakeoffCalloutKeysTests
{
    private static readonly TakeoffCalloutKeys Keys = new("IAS", "SPEED_V1", "SPEED_VR", "SPEED_V2");

    [Theory]
    [InlineData("V1", "SPEED_V1")]
    [InlineData("Rotate", "SPEED_VR")]
    [InlineData("V2", "SPEED_V2")]
    public void Each_call_is_muted_by_its_speeds_row(string callout, string row)
    {
        Assert.Equal(row, Keys.MuteKeyFor(callout));
        Assert.True(Keys.IsMuted(callout, new HashSet<string> { row }));
        Assert.False(Keys.IsMuted(callout, new HashSet<string>()));
    }

    [Fact]
    public void An_unknown_call_has_no_row_and_is_never_muted()
    {
        var everything = new HashSet<string> { "SPEED_V1", "SPEED_VR", "SPEED_V2", "" };

        Assert.Equal("", Keys.MuteKeyFor("V3"));
        Assert.False(Keys.IsMuted("V3", everything));
    }

    [Theory]
    [InlineData("SPEED_V1", true)]
    [InlineData("SPEED_VR", true)]
    [InlineData("SPEED_V2", true)]
    [InlineData("IAS", false)]
    public void Only_the_three_speeds_arm_the_machine(string key, bool isSpeed)
    {
        Assert.Equal(isSpeed, Keys.IsVSpeedKey(key));
    }

    [Fact]
    public void Feed_hands_each_speed_to_its_slot_and_ignores_anything_else()
    {
        var machine = new TakeoffVSpeedCallouts();
        Keys.Feed(machine, "SPEED_V1", 140);
        Keys.Feed(machine, "SPEED_VR", 145);
        Keys.Feed(machine, "SPEED_V2", 150);
        Keys.Feed(machine, "IAS", 999);   // not a speed: ignored

        machine.ProcessSample(20, onGround: true);   // arms
        Assert.Equal(new[] { "V1" }, machine.ProcessSample(141, onGround: true));
        Assert.Equal(new[] { "Rotate" }, machine.ProcessSample(146, onGround: true));
        Assert.Equal(new[] { "V2" }, machine.ProcessSample(151, onGround: false));
    }
}
