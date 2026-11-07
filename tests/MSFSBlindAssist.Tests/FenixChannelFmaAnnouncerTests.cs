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
        // The second part restarts the settle: the change is spoken once the reading it ends in has held 300 ms.
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Blank(), -100);
        Assert.Null(Sample(a, Slots((0, "gTHR CLB")), 0));
        Assert.Null(Sample(a, Slots((0, "gTHR CLB"), (1, "gCLB")), 200));
        Assert.Null(Sample(a, Slots((0, "gTHR CLB"), (1, "gCLB")), 300));
        Assert.Null(Sample(a, Slots((0, "gTHR CLB"), (1, "gCLB")), 499));
        Assert.Equal("FMA: Thrust: THR CLB, green; Vertical: CLB, green",
            Sample(a, Slots((0, "gTHR CLB"), (1, "gCLB")), 500));
        Assert.Null(Sample(a, Slots((0, "gTHR CLB"), (1, "gCLB")), 800));
    }

    [Fact]
    public void A_reading_that_passes_inside_the_settle_is_never_spoken()
    {
        // ALT* to ALT to ALT CST 250 ms later, then back to ALT: ALT CST held only 50 ms when 300 ms had passed since the
        // first change, so it is not spoken; ALT is, once it has held the whole settle.
        var a = new FenixChannelFmaAnnouncer();
        Sample(a, Slots((1, "gALT*")), 0);
        Assert.Null(Sample(a, Slots((1, "gALT")), 1000));
        Assert.Null(Sample(a, Slots((1, "gALT CST")), 1250));
        Assert.Null(Sample(a, Slots((1, "gALT")), 1300));
        Assert.Null(Sample(a, Slots((1, "gALT")), 1599));
        Assert.Equal("FMA: Vertical: ALT, green", Sample(a, Slots((1, "gALT")), 1600));
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

        // Two pushes, row 2 clearing first: the second starts the settle over, so LOC* is heard 300 ms after it came.
        var b = new FenixChannelFmaAnnouncer();
        Sample(b, Slots((2, "gNAV"), (7, "cLOC")), 0);
        Assert.Null(Sample(b, Slots((2, "gNAV")), 1000));
        Assert.Null(Sample(b, Slots(true, (2, "gLOC*")), 1100));
        Assert.Null(Sample(b, Slots(true, (2, "gLOC*")), 1300));
        Assert.Null(Sample(b, Slots(true, (2, "gLOC*")), 1399));
        Assert.Equal("FMA: Lateral: LOC*, green", Sample(b, Slots(true, (2, "gLOC*")), 1400));
        Assert.Null(Sample(b, Slots(false, (2, "gLOC*")), 11_400));
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

    // ---- The recorded flight ----------------------------------------------------------------------

    private const bool B = true; // the slot carries the change box
    private const bool N = false;

    private static List<FenixFmaSlot> Moment(params (int Slot, string Text, bool Box)[] texts)
    {
        List<FenixFmaSlot> slots = Blank();
        foreach ((int slot, string text, bool box) in texts) slots[slot] = new FenixFmaSlot(text, box);
        return slots;
    }

    // One reading for each moment of the 2026-11-04 flight (3.0.0.7613), settled: pushes under 300 ms apart are one reading.
    // Index 0 is the baseline. Each is the slot texts as the display sent them.
    private static List<List<FenixFmaSlot>> RecordedFlight() =>
    [
        /* 0 14:18:09 */ Moment((6, "cCLB", N), (7, "cNAV", N), (9, "w1   2", N)),
        /* 1 14:37:50.048 */ Moment((1, "gSRS", B), (6, "cCLB", N), (7, "cNAV", N), (9, "w1   2", N), (14, "cA/THR", B)),
        /* 2 14:37:50.830 */ Moment((0, "wbMAN", B), (1, "gSRS", B), (5, "FLX c+52", N), (6, "cCLB", N), (7, "cNAV", N), (9, "w1   2", N), (14, "cA/THR", B)),
        /* 3 14:38:00.852 */ Moment((0, "wbMAN", N), (1, "gSRS", N), (5, "FLX c+52", N), (6, "cCLB", N), (7, "cNAV", N), (9, "w1   2", N), (14, "cA/THR", N)),
        /* 4 14:38:32.182 */ Moment((0, "wbMAN", N), (1, "gSRS", N), (2, "gNAV", B), (5, "FLX c+52", N), (6, "cCLB", N), (9, "w1   2", N), (14, "cA/THR", N)),
        /* 5 14:38:42.505 */ Moment((0, "wbMAN", N), (1, "gSRS", N), (2, "gNAV", N), (4, "wAP1", B), (5, "FLX c+52", N), (6, "cCLB", N), (9, "w1   2", N), (14, "cA/THR", N)),
        /* 6 14:39:05.671 */ Moment((0, "wbMAN", N), (1, "gCLB", B), (2, "gNAV", N), (4, "wAP1", N), (5, "FLX c+52", N), (6, "mALT", N), (9, "w1   2", N), (10, "wfLVR CLB", N), (14, "cA/THR", N)),
        /* 7 14:39:06.810 */ Moment((0, "gTHR CLB", B), (1, "gCLB", B), (2, "gNAV", N), (4, "wAP1", N), (6, "mALT", N), (9, "w1   2", N), (14, "wA/THR", B)),
        /* 8 14:46:18.120 */ Moment((0, "gSPEED", B), (1, "gALT CST*", B), (2, "gNAV", N), (4, "wAP1", N), (6, "cCLB", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 9 14:47:05.227 */ Moment((0, "gSPEED", N), (1, "gALT CST", B), (2, "gNAV", N), (4, "wAP1", N), (6, "cCLB", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 10 14:47:25.091 */ Moment((0, "gSPEED", N), (1, "gOP CLB", B), (2, "gNAV", N), (4, "wAP1", N), (6, "cALT", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 11 14:47:25.332 */ Moment((0, "gTHR CLB", B), (1, "gOP CLB", B), (2, "gNAV", N), (4, "wAP1", N), (6, "cALT", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 12 14:52:43.390 */ Moment((0, "gTHR CLB", N), (1, "gALT*", B), (2, "gNAV", N), (4, "wAP1", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 13 14:52:43.810 */ Moment((0, "gMACH", B), (1, "gALT*", B), (2, "gNAV", N), (4, "wAP1", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 14 14:53:15.076 */ Moment((0, "gMACH", N), (1, "gALT!CRZ", B), (2, "gNAV", N), (4, "wAP1", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 15 16:19:45.557 */ Moment((0, "gMACH", N), (1, "gALT!CRZ", N), (2, "gNAV", N), (4, "wAP1", N), (9, "w1   2", N), (13, "RADIOdc⠀⠀ 200", N), (14, "wA/THR", N)),
        /* 16 16:21:25.951 */ Moment((0, "gMACH", N), (1, "gDES", B), (2, "gNAV", N), (4, "wAP1", N), (6, "cALT", N), (9, "w1   2", N), (13, "RADIOdc⠀⠀ 200", N), (14, "wA/THR", N)),
        /* 17 16:21:38.071 */ Moment((0, "gTHR IDLE", B), (1, "gDES", N), (2, "gNAV", N), (4, "wAP1", N), (6, "cALT", N), (9, "w1   2", N), (13, "RADIOdc⠀⠀ 200", N), (14, "wA/THR", N)),
        /* 18 16:22:41.025 */ Moment((0, "gTHR IDLE", N), (1, "gDES", N), (2, "gNAV", N), (4, "wAP1", N), (6, "cALT", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 19 16:31:53.460 */ Moment((0, "gTHR IDLE", N), (1, "gDES", N), (2, "gNAV", N), (4, "wAP1", N), (6, "cALT", N), (9, "w1   2", N), (11, "MORE DRAG", N), (14, "wA/THR", N)),
        /* 20 16:39:05.401 */ Moment((0, "gSPEED", B), (1, "gALT*", B), (2, "gNAV", N), (4, "wAP1", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 21 16:39:30.546 */ Moment((0, "gSPEED", N), (1, "gALT", B), (2, "gNAV", N), (4, "wAP1", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 22 16:43:30.645 */ Moment((0, "gSPEED", N), (1, "gALT", N), (2, "gHDG", B), (4, "wAP1", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 23 16:54:37.800 */ Moment((0, "gSPEED", N), (1, "gALT", N), (2, "gHDG", N), (3, "CAT3", B), (4, "wAP1", N), (6, " cG/S", N), (7, "cLOC", N), (8, "SINGLE", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 24 16:54:46.194 */ Moment((0, "gSPEED", N), (1, "gALT", N), (2, "gHDG", N), (3, "CAT3", B), (4, "wAP1+2", B), (6, " cG/S", N), (7, "cLOC", N), (8, "DUAL", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 25 16:55:47.168 */ Moment((0, "gSPEED", N), (1, "gALT", N), (2, "gLOC *", B), (3, "CAT3", N), (4, "wAP1+2", N), (6, " cG/S", N), (8, "DUAL", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 26 16:56:27.434 */ Moment((0, "gSPEED", N), (1, "gALT", N), (2, "gLOC", B), (3, "CAT3", N), (4, "wAP1+2", N), (6, " cG/S", N), (8, "DUAL", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 27 16:57:25.947 */ Moment((0, "gSPEED", N), (1, "gG/S*", B), (2, "gLOC", N), (3, "CAT3", N), (4, "wAP1+2", N), (8, "DUAL", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 28 16:57:43.351 */ Moment((0, "gSPEED", N), (1, "gG/S", B), (2, "gLOC", N), (3, "CAT3", N), (4, "wAP1+2", N), (8, "DUAL", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 29 16:59:56.519 */ Moment((0, "gSPEED", N), (1, "gLAND", B), (2, "gLOC", N), (3, "CAT3", N), (4, "wAP1+2", N), (8, "DUAL", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 30 17:00:22.866 */ Moment((0, "gSPEED", N), (1, "gFLARE", B), (2, "gLOC", N), (3, "CAT3", N), (4, "wAP1+2", N), (8, "DUAL", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 31 17:00:24.066 */ Moment((0, "gTHR IDLE", B), (1, "gFLARE", B), (2, "gLOC", N), (3, "CAT3", N), (4, "wAP1+2", N), (8, "DUAL", N), (9, "w1   2", N), (14, "wA/THR", N)),
        /* 32 17:00:26.407 */ Moment((0, "", B), (1, "gFLARE", B), (2, "gLOC", N), (3, "CAT3", N), (4, "wAP1+2", N), (8, "DUAL", N), (9, "w1   2", N)),
        /* 33 17:00:28.387 */ Moment((0, "", B), (1, "gROLL OUT", B), (2, "gLOC", N), (3, "CAT3", N), (4, "wAP1+2", N), (8, "DUAL", N), (9, "w1   2", N)),
        /* 34 17:00:52.994 */ Moment((1, "gROLL OUT", N), (2, "gLOC", N), (3, "CAT1", B), (4, "", B), (9, "w1   2", N)),
        /* 35 17:00:58.453 */ Moment((1, "gROLL OUT", N), (2, "gLOC *", B), (3, "CAT1", B), (4, "", B), (9, "w1   2", N)),
        /* 36 17:01:01.874 */ Moment((1, "", B), (2, "", B), (3, "", B), (9, "w1   2", N)),
    ];

    private static readonly string[] RecordedAnnouncements =
    [
        "FMA: Vertical: SRS green, CLB cyan",
        "FMA: Thrust: MAN FLX white, +52 cyan",
        "FMA: Lateral: NAV, green",
        "FMA: Vertical: CLB green, ALT magenta; FMA message: LVR CLB, white",
        "FMA: Thrust: THR CLB, green",
        "FMA: Thrust: SPEED, green; Vertical: ALT CST* green, CLB cyan",
        "FMA: Vertical: ALT CST green, CLB cyan",
        "FMA: Vertical: OP CLB green, ALT cyan",
        "FMA: Thrust: THR CLB, green",
        "FMA: Vertical: ALT*, green",
        "FMA: Thrust: MACH, green",
        "FMA: Vertical: ALT CRZ, green",
        "FMA: Approach: RADIO white, 200 cyan",
        "FMA: Vertical: DES green, ALT cyan",
        "FMA: Thrust: THR IDLE, green",
        "FMA message: MORE DRAG",
        "FMA: Thrust: SPEED, green; Vertical: ALT*, green",
        "FMA: Vertical: ALT, green",
        "FMA: Lateral: HDG, green",
        "FMA: Vertical: ALT green, G/S cyan; Lateral: HDG green, LOC cyan; Approach: CAT3 SINGLE",
        "FMA: Approach: CAT3 DUAL",
        "FMA: Lateral: LOC *, green",
        "FMA: Lateral: LOC, green",
        "FMA: Vertical: G/S*, green",
        "FMA: Vertical: G/S, green",
        "FMA: Vertical: LAND, green",
        "FMA: Vertical: FLARE, green",
        "FMA: Thrust: THR IDLE, green",
        "FMA: Vertical: ROLL OUT, green",
        "FMA: Approach: CAT1",
    ];

    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    public void A_recorded_flight_is_announced_as_expected(string culture)
    {
        List<string> heard = TestCulture.Run(culture, () =>
        {
            var a = new FenixChannelFmaAnnouncer();
            var all = new List<string>();
            long t = 0;
            foreach (List<FenixFmaSlot> reading in RecordedFlight())
            {
                for (int i = 0; i < 4; i++, t += 100)
                {
                    string? said = Sample(a, reading, t);
                    if (said is not null) all.Add(said);
                }
            }

            return all;
        });

        Assert.Equal(RecordedAnnouncements, heard);
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
