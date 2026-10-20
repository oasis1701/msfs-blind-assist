using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.PMDG737;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FO-16: the 737s' fixed 400 ft LNAV/VNAV push fires only on a climb through 400 ft the manager
/// watched (<see cref="LnavVnavPushHeight.Armed"/>), and its one-shot burns only on an informed
/// decision (FOB-21, <see cref="LnavVnavPushHeight.ShouldBurnLatch"/>, shared by the PMDG 737 and the
/// iFly). The push once fired on the first climbing sample above 400 ft wherever it came: the window
/// first opened at FL150 pressed LNAV and VNAV there and said "400 feet. LNAV and VNAV engaged."
/// </summary>
public class LnavVnavPushHeightTests
{
    [Fact]
    public void The_push_height_is_fixed_at_400_feet() => Assert.Equal(400, LnavVnavPushHeight.Feet);

    [Fact]
    public void A_sample_below_the_push_height_arms_it()
        => Assert.True(LnavVnavPushHeight.Armed(wasArmed: false, aglFt: 120, settingOn: true));

    [Fact]
    public void A_manager_that_first_sees_the_aircraft_above_the_height_never_arms()
        => Assert.False(LnavVnavPushHeight.Armed(wasArmed: false, aglFt: 15000, settingOn: true));

    [Fact]
    public void An_armed_push_stays_armed_above_the_height_with_the_setting_on()
        => Assert.True(LnavVnavPushHeight.Armed(wasArmed: true, aglFt: 450, settingOn: true));

    [Fact]
    public void Passing_above_the_height_with_the_setting_off_disarms_it()
        => Assert.False(LnavVnavPushHeight.Armed(wasArmed: true, aglFt: 450, settingOn: false));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_unread_height_changes_nothing(bool wasArmed)
        => Assert.Equal(wasArmed, LnavVnavPushHeight.Armed(wasArmed, double.NaN, settingOn: true));

    [Theory]
    [InlineData(true, false, double.NaN, double.NaN, true)]    // a push fired
    [InlineData(false, true, double.NaN, double.NaN, true)]
    [InlineData(false, false, 1.0, 1.0, true)]                 // both read, nothing to push: decided
    [InlineData(false, false, double.NaN, 1.0, false)]         // one unread and nothing pushed: decided nothing
    [InlineData(false, false, 1.0, double.NaN, false)]
    [InlineData(false, false, double.NaN, double.NaN, false)]
    public void The_one_shot_burns_only_on_an_informed_decision(
        bool pushedLnav, bool pushedVnav, double lnav, double vnav, bool expected)
    {
        Assert.Equal(expected, LnavVnavPushHeight.ShouldBurnLatch(pushedLnav, pushedVnav, lnav, vnav));
        // The iFly's rule is the same one, not a copy.
        Assert.Equal(expected, IFly737FOAutoManager.ShouldBurnLnavVnavLatch(pushedLnav, pushedVnav, lnav, vnav));
    }
}
