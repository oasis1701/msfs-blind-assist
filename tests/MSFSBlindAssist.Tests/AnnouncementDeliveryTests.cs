using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Where one announcement goes (ScreenReaderAnnouncer.Route): the screen reader, Windows speech, or the other when
/// the first cannot take it, and what an interrupting announcement silences on the way.
/// </summary>
public class AnnouncementDeliveryTests
{
    private sealed class Rig
    {
        public readonly List<string> Events = new();
        public bool ReaderTakes = true;
        public bool SapiThrows;
        public bool CancelThrows;
        public bool HasSapi = true;

        public SpokenBy Route(string message, bool interrupt, AnnouncementMode mode) =>
            ScreenReaderAnnouncer.Route(message, interrupt, mode,
                (text, cut) => { Events.Add($"reader {(cut ? "interrupt" : "queue")} {text}"); return ReaderTakes; },
                HasSapi ? text => { Events.Add($"sapi speak {text}"); if (SapiThrows) throw new InvalidOperationException("no audio device"); } : null,
                HasSapi ? () => { Events.Add("sapi cancel"); if (CancelThrows) throw new InvalidOperationException("no audio device"); } : null);
    }

    [Fact]
    public void An_interrupt_the_reader_takes_also_silences_Windows_speech()
    {
        // While the reader was away its messages queued up in Windows speech; once it is back, an interrupting
        // announcement must stop that backlog, or the Windows voice talks over the reader with nothing to stop it.
        var rig = new Rig();

        Assert.Equal(SpokenBy.ScreenReader, rig.Route("Gear down", true, AnnouncementMode.ScreenReader));

        Assert.Equal(new[] { "reader interrupt Gear down", "sapi cancel" }, rig.Events);
    }

    [Fact]
    public void A_queued_message_the_reader_takes_leaves_Windows_speech_alone()
    {
        // What Windows speech still has queued was never heard: only an interrupt may drop it.
        var rig = new Rig();

        Assert.Equal(SpokenBy.ScreenReader, rig.Route("Flaps two", false, AnnouncementMode.ScreenReader));

        Assert.Equal(new[] { "reader queue Flaps two" }, rig.Events);
    }

    [Fact]
    public void A_failing_cancel_does_not_undo_what_the_reader_spoke()
    {
        var rig = new Rig { CancelThrows = true };

        Assert.Equal(SpokenBy.ScreenReader, rig.Route("Gear down", true, AnnouncementMode.ScreenReader));

        Assert.DoesNotContain(rig.Events, e => e.StartsWith("sapi speak"));
    }

    [Fact]
    public void SAPI_mode_never_asks_the_reader_and_an_interrupt_cancels_before_it_speaks()
    {
        var rig = new Rig();

        Assert.Equal(SpokenBy.WindowsSpeech, rig.Route("Gear down", true, AnnouncementMode.SAPI));

        Assert.Equal(new[] { "sapi cancel", "sapi speak Gear down" }, rig.Events);
    }

    [Fact]
    public void A_queued_message_in_SAPI_mode_does_not_cancel()
    {
        var rig = new Rig();

        Assert.Equal(SpokenBy.WindowsSpeech, rig.Route("Flaps two", false, AnnouncementMode.SAPI));

        Assert.Equal(new[] { "sapi speak Flaps two" }, rig.Events);
    }

    [Fact]
    public void A_message_no_reader_takes_goes_to_Windows_speech()
    {
        var rig = new Rig { ReaderTakes = false };

        Assert.Equal(SpokenBy.WindowsSpeech, rig.Route("Flaps two", false, AnnouncementMode.ScreenReader));

        Assert.Equal(new[] { "reader queue Flaps two", "sapi speak Flaps two" }, rig.Events);
    }

    [Fact]
    public void When_Windows_speech_throws_in_SAPI_mode_the_reader_speaks_after_all()
    {
        var rig = new Rig { SapiThrows = true };

        Assert.Equal(SpokenBy.ScreenReader, rig.Route("Flaps two", false, AnnouncementMode.SAPI));

        Assert.Equal(new[] { "sapi speak Flaps two", "reader queue Flaps two" }, rig.Events);
    }

    [Fact]
    public void Without_Windows_speech_the_reader_is_asked_once_whatever_the_mode()
    {
        var rig = new Rig { HasSapi = false };

        Assert.Equal(SpokenBy.ScreenReader, rig.Route("Flaps two", false, AnnouncementMode.SAPI));

        Assert.Equal(new[] { "reader queue Flaps two" }, rig.Events);
    }

    [Fact]
    public void A_reader_that_refused_is_not_asked_twice_and_nobody_spoke()
    {
        var rig = new Rig { ReaderTakes = false, SapiThrows = true };

        Assert.Equal(SpokenBy.Nobody, rig.Route("Flaps two", false, AnnouncementMode.ScreenReader));

        Assert.Equal(new[] { "reader queue Flaps two", "sapi speak Flaps two" }, rig.Events);
    }

    [Fact]
    public void A_reader_that_throws_counts_as_not_taking_the_message()
    {
        var events = new List<string>();

        var by = ScreenReaderAnnouncer.Route("Flaps two", false, AnnouncementMode.ScreenReader,
            (_, _) => throw new InvalidOperationException("prism"),
            text => events.Add($"sapi speak {text}"),
            () => events.Add("sapi cancel"));

        Assert.Equal(SpokenBy.WindowsSpeech, by);
        Assert.Equal(new[] { "sapi speak Flaps two" }, events);
    }
}
