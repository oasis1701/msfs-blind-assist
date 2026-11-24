using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Tests;

/// <summary>What the announcer does with the announcement mode, and the two lines of the Settings status box.</summary>
public class AnnouncementsStatusTextTests
{
    [Fact]
    public void Readers_are_used_only_in_screen_reader_mode()
    {
        // SAPI mode keeps the Windows voice even with a screen reader running (the guide: choose SAPI "if your screen
        // reader is not working with BA"); before Prism a running NVDA or JAWS overrode it.
        Assert.True(ScreenReaderAnnouncer.UsesScreenReader(AnnouncementMode.ScreenReader));
        Assert.False(ScreenReaderAnnouncer.UsesScreenReader(AnnouncementMode.SAPI));
    }

    [Fact]
    public void A_detected_reader_is_named()
    {
        Assert.Equal($"Screen reader detected: ZDSR{Environment.NewLine}Choose 'Screen Reader' for best experience.",
            ScreenReaderAnnouncer.DetectionStatusText("ZDSR", prismStarted: true));
    }

    [Fact]
    public void No_reader_recommends_SAPI()
    {
        Assert.Equal($"No screen reader detected.{Environment.NewLine}SAPI mode recommended for speech feedback.",
            ScreenReaderAnnouncer.DetectionStatusText(null, prismStarted: true));
    }

    [Fact]
    public void A_detection_that_could_not_start_says_so()
    {
        Assert.Equal($"Unable to start screen reader detection.{Environment.NewLine}SAPI mode will be used as fallback.",
            ScreenReaderAnnouncer.DetectionStatusText(null, prismStarted: false));
    }
}
