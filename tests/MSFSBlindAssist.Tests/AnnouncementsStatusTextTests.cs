using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms.Settings;

namespace MSFSBlindAssist.Tests;

/// <summary>What the announcer does with the announcement mode, and the two lines of the Settings status box.</summary>
public class AnnouncementsStatusTextTests
{
    private static readonly string Nl = Environment.NewLine;

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
    public void A_detected_reader_in_use_is_named_as_the_voice()
    {
        // Screen Reader is already chosen here, so the box does not ask the pilot to choose it.
        Assert.Equal($"Screen reader detected: ZDSR{Nl}BA speaks through ZDSR.",
            ScreenReaderAnnouncer.DetectionStatusText("ZDSR", prismStarted: true, sapiChosen: false, sapiAvailable: true));
    }

    [Fact]
    public void A_detected_reader_with_SAPI_chosen_says_Windows_speech_is_used()
    {
        // Before Prism a running reader overrode a saved SAPI choice; a pilot updating with SAPI still saved learns
        // here why the Windows voice speaks.
        Assert.Equal($"Screen reader detected: NVDA{Nl}SAPI is chosen, so BA speaks with Windows speech. Choose 'Screen Reader' to use NVDA.",
            ScreenReaderAnnouncer.DetectionStatusText("NVDA", prismStarted: true, sapiChosen: true, sapiAvailable: true));
    }

    [Fact]
    public void With_SAPI_chosen_but_broken_the_box_says_the_reader_speaks()
    {
        // The announcer uses the reader whatever the mode when Windows speech could not start (TriesScreenReader),
        // so "BA speaks with Windows speech" would be the opposite of what the pilot hears.
        Assert.Equal($"Screen reader detected: NVDA{Nl}SAPI is chosen, but Windows speech could not start, so BA speaks through NVDA.",
            ScreenReaderAnnouncer.DetectionStatusText("NVDA", prismStarted: true, sapiChosen: true, sapiAvailable: false));
    }

    [Fact]
    public void No_reader_recommends_SAPI()
    {
        Assert.Equal($"No screen reader detected.{Nl}SAPI mode recommended for speech feedback.",
            ScreenReaderAnnouncer.DetectionStatusText(null, prismStarted: true, sapiChosen: false, sapiAvailable: true));
    }

    [Fact]
    public void No_reader_and_no_Windows_speech_does_not_recommend_SAPI()
    {
        Assert.Equal($"No screen reader detected, and Windows speech could not start.{Nl}BA cannot speak until a screen reader is running.",
            ScreenReaderAnnouncer.DetectionStatusText(null, prismStarted: true, sapiChosen: true, sapiAvailable: false));
    }

    [Fact]
    public void A_detection_that_could_not_start_says_so()
    {
        Assert.Equal($"Unable to start screen reader detection.{Nl}SAPI mode will be used as fallback.",
            ScreenReaderAnnouncer.DetectionStatusText(null, prismStarted: false, sapiChosen: false, sapiAvailable: true));
    }

    [Fact]
    public void A_detection_that_could_not_start_without_Windows_speech_promises_no_fallback()
    {
        Assert.Equal($"Unable to start screen reader detection, and Windows speech could not start.{Nl}BA cannot speak.",
            ScreenReaderAnnouncer.DetectionStatusText(null, prismStarted: false, sapiChosen: false, sapiAvailable: false));
    }

    /// <summary>Every status the box can show fits inside it, for the longest reader name Prism has. Measured
    /// 2026-11-24: the box was two lines high and the SAPI-chosen text wraps to three, so its last words, the ones
    /// telling the pilot what to choose, were cut off for anyone reading the screen.</summary>
    [Theory]
    [InlineData("NVDA", true, true)]
    [InlineData("Boy PC Reader", true, true)]
    [InlineData("Boy PC Reader", true, false)]
    [InlineData("Boy PC Reader", false, true)]
    [InlineData(null, true, false)]
    [InlineData(null, false, false)]
    public void Every_status_fits_in_the_status_box(string? reader, bool sapiChosen, bool sapiAvailable)
    {
        foreach (bool prismStarted in new[] { true, false })
        {
            string text = ScreenReaderAnnouncer.DetectionStatusText(reader, prismStarted, sapiChosen, sapiAvailable);
            int bottom = 0, room = 0;
            var thread = new Thread(() =>
            {
                using var box = AnnouncementsPanel.BuildStatusBox();
                box.Text = text;
                _ = box.Handle;
                bottom = box.GetPositionFromCharIndex(text.Length - 1).Y + box.Font.Height;
                room = box.ClientSize.Height;
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.True(bottom <= room, $"\"{text}\" needs {bottom} px and the box shows {room}");
        }
    }
}
