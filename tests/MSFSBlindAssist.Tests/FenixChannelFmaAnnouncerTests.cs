// Characterization tests for FenixChannelFmaAnnouncer: the settle, the silent baseline, trust gaps, reconnects and mute
// of the FMA announcer, with the clock passed in as a plain long. Pure logic: slots are written as the display sends them.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelFmaAnnouncerTests
{
    private static readonly FenixDisplayVerdict Good = new(true, "");
    private static FenixDisplayVerdict Bad(string reason) => new(false, reason);

    private static List<FenixFmaSlot> Blank() =>
        Enumerable.Range(0, FenixChannelFields.FmaEntry.SlotCount).Select(_ => new FenixFmaSlot("", false)).ToList();

    private static List<FenixFmaSlot> Slots(params (int Slot, string Text)[] texts) => Slots(false, texts);

    private static List<FenixFmaSlot> Slots(bool changeBox, params (int Slot, string Text)[] texts)
    {
        List<FenixFmaSlot> slots = Blank();
        foreach ((int slot, string text) in texts) slots[slot] = new FenixFmaSlot(text, changeBox);
        return slots;
    }

    private static string? Sample(FenixChannelFmaAnnouncer a, List<FenixFmaSlot>? slots, long now,
        int connection = 1, bool muted = false) =>
        a.Sample(Good, slots, connection, now, muted);

    [Fact]
    public void The_first_trusted_reading_is_silent()
    {
        var a = new FenixChannelFmaAnnouncer();
        Assert.Null(Sample(a, Slots((0, "gTHR CLB"), (1, "gCLB")), 0));
        Assert.Null(Sample(a, Slots((0, "gTHR CLB"), (1, "gCLB")), 1000));
    }

    [Fact]
    public void A_change_is_spoken_once_it_has_held_300_ms()
    {
        var a = new FenixChannelFmaAnnouncer();
        Assert.Null(Sample(a, Slots((1, "gALT*")), 9_000));
        Assert.Null(Sample(a, Slots((1, "gALT")), 10_000));
        Assert.Null(Sample(a, Slots((1, "gALT")), 10_100));
        Assert.Null(Sample(a, Slots((1, "gALT")), 10_299));
        Assert.Equal("FMA: Vertical: ALT, green", Sample(a, Slots((1, "gALT")), 10_300));
        Assert.Null(Sample(a, Slots((1, "gALT")), 10_400));
        Assert.Null(Sample(a, Slots((1, "gALT")), 12_000));
    }

    [Fact]
    public void A_change_that_reverts_inside_the_settle_says_nothing()
    {
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Slots((1, "gALT*")), 0);
        Assert.Null(Sample(a, Slots((1, "gALT")), 100));
        Assert.Null(Sample(a, Slots((1, "gALT*")), 200));
        Assert.Null(Sample(a, Slots((1, "gALT*")), 400));
        Assert.Null(Sample(a, Slots((1, "gALT*")), 1000));
        // A later different change starts its own timer from its own first sample.
        Assert.Null(Sample(a, Slots((1, "gALT")), 2000));
        Assert.Null(Sample(a, Slots((1, "gALT")), 2299));
        Assert.Equal("FMA: Vertical: ALT, green", Sample(a, Slots((1, "gALT")), 2300));
    }

    [Fact]
    public void Two_changes_inside_the_settle_are_one_announcement()
    {
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Blank(), -100);
        Assert.Null(Sample(a, Slots((0, "gTHR CLB")), 0));
        Assert.Null(Sample(a, Slots((0, "gTHR CLB"), (1, "gCLB")), 200));
        Assert.Equal("FMA: Thrust: THR CLB, green; Vertical: CLB, green",
            Sample(a, Slots((0, "gTHR CLB"), (1, "gCLB")), 300));
        Assert.Null(Sample(a, Slots((0, "gTHR CLB"), (1, "gCLB")), 600));
    }

    [Fact]
    public void An_armed_mode_becoming_active_is_heard_once()
    {
        // One push.
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Slots((2, "gNAV"), (7, "cLOC")), 0);
        Assert.Null(Sample(a, Slots(true, (2, "gLOC*")), 1000));
        Assert.Equal("FMA: Lateral: LOC*, green", Sample(a, Slots(true, (2, "gLOC*")), 1300));
        Assert.Null(Sample(a, Slots(false, (2, "gLOC*")), 11_300));
        Assert.Null(Sample(a, Slots(false, (2, "gLOC*")), 12_000));

        // Two pushes, row 2 clearing first.
        var b = new FenixChannelFmaAnnouncer();
        Sample(b, Slots((2, "gNAV"), (7, "cLOC")), 0);
        Assert.Null(Sample(b, Slots((2, "gNAV")), 1000));
        Assert.Null(Sample(b, Slots(true, (2, "gLOC*")), 1100));
        Assert.Null(Sample(b, Slots(true, (2, "gLOC*")), 1299));
        Assert.Equal("FMA: Lateral: LOC*, green", Sample(b, Slots(true, (2, "gLOC*")), 1300));
        Assert.Null(Sample(b, Slots(false, (2, "gLOC*")), 11_300));
        Assert.Null(Sample(b, Slots(false, (2, "gLOC*")), 12_000));
    }

    [Fact]
    public void A_change_box_or_flash_alone_says_nothing()
    {
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Slots((0, "gTHR CLB"), (10, "wfLVR CLB")), 0);
        for (long t = 100; t <= 20_000; t += 100)
        {
            bool on = (t / 100) % 2 == 0;
            List<FenixFmaSlot> slots = Slots(on, (0, "gTHR CLB"), (10, on ? "wfLVR CLB" : "wLVR CLB"));
            Assert.Null(Sample(a, slots, t));
        }
    }

    [Fact]
    public void Silent_while_the_pfd_is_not_trusted()
    {
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Slots((1, "gALT*")), 0);
        for (long t = 100; t <= 3000; t += 100)
        {
            Assert.Null(a.Sample(Bad("display off"), Slots((1, t % 200 == 0 ? "gALT" : "gCLB")), 1, t, false));
        }
    }

    [Fact]
    public void The_first_good_reading_after_a_trust_gap_is_silent()
    {
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Slots((1, "gALT")), 0);
        for (long t = 100; t <= 3000; t += 100) Assert.Null(a.Sample(Bad("channel not answering"), null, 1, t, false));
        Assert.Null(Sample(a, Slots((1, "gCLB")), 3100));
        Assert.Null(Sample(a, Slots((1, "gCLB")), 4000));
        Assert.Null(Sample(a, Slots((1, "gOP CLB")), 5000));
        Assert.Equal("FMA: Vertical: OP CLB, green", Sample(a, Slots((1, "gOP CLB")), 5300));
    }

    [Fact]
    public void A_trust_gap_drops_a_pending_change()
    {
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Blank(), -100);
        Assert.Null(Sample(a, Slots((1, "gALT")), 0));
        Assert.Null(a.Sample(Bad("stream stalled"), Slots((1, "gALT")), 1, 200, false));
        Assert.Null(Sample(a, Slots((1, "gALT")), 300));
        Assert.Null(Sample(a, Slots((1, "gALT")), 600));
        Assert.Null(Sample(a, Slots((1, "gALT")), 2000));
    }

    [Fact]
    public void A_missing_frame_or_a_short_fma_is_a_trust_gap()
    {
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Slots((1, "gALT")), 0);
        Assert.Null(Sample(a, null, 100));
        Assert.Null(Sample(a, Slots((1, "gCLB")), 200));
        Assert.Null(Sample(a, Slots((1, "gCLB")), 1000)); // baseline retaken silently, so no change after the gap

        var b = new FenixChannelFmaAnnouncer();
        Sample(b, Slots((1, "gALT")), 0);
        Assert.Null(Sample(b, Slots((1, "gCLB")).Take(15).ToList(), 100));
        Assert.Null(Sample(b, Slots((1, "gCLB")), 200));
        Assert.Null(Sample(b, Slots((1, "gCLB")), 1000));
    }

    [Fact]
    public void A_new_connection_is_a_trust_gap()
    {
        var a = new FenixChannelFmaAnnouncer();
        Assert.Null(Sample(a, Slots((1, "gALT")), 0, 1));
        Assert.Null(Sample(a, Slots((1, "gCLB")), 100, 1)); // pending on connection 1
        Assert.Null(Sample(a, Slots((1, "gOP CLB")), 200, 2)); // B is the new baseline
        Assert.Null(Sample(a, Slots((1, "gOP CLB")), 1000, 2));
        Assert.Null(Sample(a, Slots((1, "gALT")), 2000, 2));
        Assert.Equal("FMA: Vertical: ALT, green", Sample(a, Slots((1, "gALT")), 2300, 2));
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Blank(), 0, muted: true);
        Assert.Null(Sample(a, Slots((1, "gALT")), 100, muted: true));
        Assert.Null(Sample(a, Slots((1, "gALT")), 400, muted: true));
        Assert.Null(Sample(a, Slots((1, "gALT")), 1000, muted: true));
    }

    [Fact]
    public void Tracking_goes_on_while_muted()
    {
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Slots((1, "gALT")), 0);
        Sample(a, Slots((1, "gCLB")), 100, muted: true);
        Assert.Null(Sample(a, Slots((1, "gCLB")), 400, muted: true));
        Assert.Null(Sample(a, Slots((1, "gCLB")), 500));
        Assert.Null(Sample(a, Slots((1, "gOP CLB")), 600));
        Assert.Equal("FMA: Vertical: OP CLB, green", Sample(a, Slots((1, "gOP CLB")), 900));
    }

    [Fact]
    public void Unmuting_replays_nothing()
    {
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Slots((1, "gALT")), 0);
        Sample(a, Slots((1, "gCLB")), 100, muted: true);
        Assert.Null(Sample(a, Slots((1, "gCLB")), 400, muted: true));
        for (long t = 500; t <= 2500; t += 100) Assert.Null(Sample(a, Slots((1, "gCLB")), t));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void An_announcement_is_the_same_in_sv_SE(string culture)
    {
        string? heard = TestCulture.Run(culture, () =>
        {
            var a = new FenixChannelFmaAnnouncer();
            Sample(a, Blank(), 0);
            Sample(a, Slots((0, "wbMAN"), (5, "FLX c+56")), 100);
            return Sample(a, Slots((0, "wbMAN"), (5, "FLX c+56")), 400);
        });

        Assert.Equal("FMA: Thrust: MAN FLX white, +56 cyan", heard);
    }
}
