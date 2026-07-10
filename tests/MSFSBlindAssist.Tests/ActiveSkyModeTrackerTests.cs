// Pins the baseline-first mode-change announcement rules (spec 2026-07-10 §3.3):
// silent on first sight, silent on unchanged, announce once per genuine change,
// failed reads are never a change, baseline SURVIVES unreachable gaps so a
// reconnect in a different mode announces.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyModeTrackerTests
{
    private const string Live = "Live Real time mode (Active) (2026/7/10 1935z)";
    private const string LiveLater = "Live Real time mode (Active) (2026/7/10 2050z)";
    private const string Custom = "Custom static mode (Active) (2026/7/10 2100z)";

    [Fact]
    public void First_successful_read_baselines_silently()
        => Assert.Null(new ActiveSkyModeTracker().Observe(Live));

    [Fact]
    public void Unchanged_mode_stays_silent_even_as_weather_clock_advances()
    {
        var t = new ActiveSkyModeTracker();
        t.Observe(Live);
        Assert.Null(t.Observe(LiveLater));
    }

    [Fact]
    public void Mode_change_announces_once_then_goes_silent()
    {
        var t = new ActiveSkyModeTracker();
        t.Observe(Live);
        Assert.Equal("ActiveSky weather mode changed to Custom static mode.", t.Observe(Custom));
        Assert.Null(t.Observe(Custom));
    }

    [Fact]
    public void Change_back_announces_again()
    {
        var t = new ActiveSkyModeTracker();
        t.Observe(Live);
        t.Observe(Custom);
        Assert.Equal("ActiveSky weather mode changed to Live Real time mode.", t.Observe(Live));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Failed_or_empty_reads_are_never_a_change_and_never_baseline(string? bad)
    {
        var t = new ActiveSkyModeTracker();
        Assert.Null(t.Observe(bad));       // no baseline consumed
        Assert.Null(t.Observe(Live));      // first REAL read still baselines silently
        Assert.Null(t.Observe(bad));       // mid-session bad read: silent
        Assert.Equal("ActiveSky weather mode changed to Custom static mode.", t.Observe(Custom));
    }

    [Fact]
    public void Reconnect_in_a_different_mode_announces()
    {
        // Simulates: baseline Live → AS closed (no Observe calls happen while
        // unreachable; the monitor only calls Observe after IsRunningAsync()==true)
        // → AS reopened in Custom. The baseline survived, so this is a change.
        var t = new ActiveSkyModeTracker();
        t.Observe(Live);
        Assert.Equal("ActiveSky weather mode changed to Custom static mode.", t.Observe(Custom));
    }

    [Fact]
    public void Unparseable_mode_text_is_never_a_change_and_never_baselines()
    {
        // Non-blank, but ParseModeText strips it down to "" -> ModeName "unknown".
        // Observe must treat this the same as a failed/empty read: silent, and
        // must not consume the baseline slot — a following real mode read still
        // baselines silently.
        const string unparseable = "(Active) (2026/7/10 2100z)";
        var t = new ActiveSkyModeTracker();
        Assert.Null(t.Observe(unparseable));
        Assert.Null(t.Observe(Live)); // first REAL read still baselines silently
    }
}
