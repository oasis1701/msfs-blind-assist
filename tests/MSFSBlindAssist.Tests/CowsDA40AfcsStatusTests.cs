using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The AFCS self test and pre-flight test go back to "not started" only when the AFCS loses
/// power or resets. VCRI shutdown, 2026-10-19: the avionics master off said "Autopilot Self
/// Test: not started. Autopilot Pre-flight Test: not started" - two lines that read like test
/// results over something the pilot had just done. The power-up's "running" and its result
/// still speak (DA40A-5).
/// </summary>
public class CowsDA40AfcsStatusTests
{
    [Theory]
    [InlineData("DA40_AP_SELFTEST", 2, 0, false)]   // complete -> not started: power off
    [InlineData("DA40_AP_PFT", 3, 0, false)]        // passed -> not started: power off
    [InlineData("DA40_AP_SELFTEST", 0, 1, true)]    // power-up: running
    [InlineData("DA40_AP_SELFTEST", 1, 2, true)]    // complete
    [InlineData("DA40_AP_PFT", 1, -1, true)]        // FAILED
    [InlineData("DA40_AP_PFT", 2, 2, false)]        // unchanged
    [InlineData("DA40_AP_FAILED", 1, 0, true)]      // the failure flag keeps both edges
    public void NotStartedIsNeverNewsButEveryResultIs(string key, double was, double now, bool news)
        => Assert.Equal(news, CowsDA40Definition.AfcsStatusIsNews(key, was, now));
}

/// <summary>
/// A GFC 700 mode press the autopilot refuses leaves the FMA as it was, and nothing was said:
/// APR from HDG after a go-around, NAV and APR pressed in a quick run, AP in GA (MTL/VCRI,
/// 2026-10-19). A sighted pilot sees the annunciation not appear; the read-back is that.
/// </summary>
public class CowsDA40ApPressReadBackTests
{
    private static readonly System.DateTime T0 = new(2026, 10, 19, 15, 19, 0, System.DateTimeKind.Utc);

    [Fact]
    public void AnUnchangedFmaOnFreshRowsIsSaid()
        => Assert.True(CowsDA40Definition.ApPressUnanswered(4, 4, T0.AddSeconds(3), T0, rowMoved: false));

    [Fact]
    public void AnFmaThatMovedIsNot()
        => Assert.False(CowsDA40Definition.ApPressUnanswered(4, 5, T0.AddSeconds(3), T0, rowMoved: false));

    [Fact]
    public void WithoutRowsSinceThePressNothingIsKnownAndNothingIsSaid()
    {
        // The watcher not reading (no G1000 page, socket elsewhere): silence is honest, a
        // "no change" would be invented.
        Assert.False(CowsDA40Definition.ApPressUnanswered(4, 4, T0.AddMilliseconds(500), T0, rowMoved: false));
        Assert.False(CowsDA40Definition.ApPressUnanswered(4, 4, System.DateTime.MinValue, T0, rowMoved: false));
    }

    [Fact]
    public void AModeRowThatMovedIsAnAnswerEvenWhileTheFmaHolds()
        // AP OFF on the ground: the G1000 keeps "AP" flashing on the FMA for seconds after a
        // disconnect, so the FMA had not moved at the check, and "Autopilot: no change" was said
        // over an autopilot that had just let go (VCRI, 2026-10-19). The master row went 1 to 0.
        => Assert.False(CowsDA40Definition.ApPressUnanswered(4, 4, T0.AddSeconds(3), T0, rowMoved: true));

    [Fact]
    public void TheWordingIsTheControlAndTheFact()
        => Assert.Equal("Approach Mode: no change", CowsDA40Definition.ApPressNoChangeText("Approach Mode"));
}
