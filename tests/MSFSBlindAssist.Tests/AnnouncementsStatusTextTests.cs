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
    public void Without_Windows_speech_the_reader_is_tried_whatever_the_mode()
    {
        // A broken SAPI must never mean silence while a screen reader runs.
        Assert.True(ScreenReaderAnnouncer.TriesScreenReader(AnnouncementMode.SAPI, sapiAvailable: false));
        Assert.True(ScreenReaderAnnouncer.TriesScreenReader(AnnouncementMode.ScreenReader, sapiAvailable: false));
        Assert.True(ScreenReaderAnnouncer.TriesScreenReader(AnnouncementMode.ScreenReader, sapiAvailable: true));
        Assert.False(ScreenReaderAnnouncer.TriesScreenReader(AnnouncementMode.SAPI, sapiAvailable: true));
    }

    [Fact]
    public void A_detected_reader_is_named()
    {
        Assert.Equal($"Screen reader detected: ZDSR{Environment.NewLine}Choose 'Screen Reader' for best experience.",
            ScreenReaderAnnouncer.DetectionStatusText("ZDSR", prismStarted: true, sapiChosen: false));
    }

    [Fact]
    public void A_detected_reader_with_SAPI_chosen_says_Windows_speech_is_used()
    {
        // Before Prism a running reader overrode a saved SAPI choice; a pilot updating with SAPI still saved learns
        // here why the Windows voice speaks.
        Assert.Equal($"Screen reader detected: NVDA{Environment.NewLine}SAPI is chosen, so BA speaks with Windows speech. Choose 'Screen Reader' to use NVDA.",
            ScreenReaderAnnouncer.DetectionStatusText("NVDA", prismStarted: true, sapiChosen: true));
    }

    [Fact]
    public void No_reader_recommends_SAPI()
    {
        Assert.Equal($"No screen reader detected.{Environment.NewLine}SAPI mode recommended for speech feedback.",
            ScreenReaderAnnouncer.DetectionStatusText(null, prismStarted: true, sapiChosen: false));
    }

    [Fact]
    public void A_detection_that_could_not_start_says_so()
    {
        Assert.Equal($"Unable to start screen reader detection.{Environment.NewLine}SAPI mode will be used as fallback.",
            ScreenReaderAnnouncer.DetectionStatusText(null, prismStarted: false, sapiChosen: false));
    }
}
