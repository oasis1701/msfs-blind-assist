// Pins the A220's CommBus systems decoders (A220Systems) against the 2026-10-12 live capture
// (engines running at KATL, Fixtures/a220-2026-10-12-commbus-stores-engines-running.json) and
// against the enums decoded from the aircraft's own bundle (docs/a220.md "Systems over the
// CommBus"). Spoken numbers are checked under de-DE as well: CI runs en-US, so only a
// comma-decimal culture catches a "30,13" leaking into speech or a JSON payload.

using System.Globalization;
using System.Text.Json;
using MSFSBlindAssist.Aircraft.A220;
using static MSFSBlindAssist.Aircraft.A220.A220Systems;

namespace MSFSBlindAssist.Tests;

public class SynapticA220SystemsTests
{
    private static readonly Lazy<JsonDocument> FixtureDoc = new(() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "a220-2026-10-12-commbus-stores-engines-running.json"))));

    internal static T Store<T>(string name) =>
        JsonSerializer.Deserialize<T>(FixtureDoc.Value.RootElement.GetProperty(name).GetRawText())!;

    /// <summary>A CAS msg bitset (18 words, as the aircraft sends it) with the given indices set.</summary>
    internal static long[] Words(params int[] indices)
    {
        var w = new long[18];
        foreach (int i in indices) w[i >> 5] |= 1L << (i & 31);
        return w;
    }

    internal static void UnderCulture(string name, Action body)
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo(name); body(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // CAS table indices used below (SynapticA220EclData.CasMessages).
    private const int LEngFire = 0;            // Warning  "L ENG FIRE"
    private const int PerfDepConfig = 243;     // Caution  "FMS PERF DEP CONFIG"
    private const int EmerLtsOff = 526;        // Caution  "EMER LTS OFF"
    private const int ExtPwrAvail = 341;       // Advisory "EXT PWR AVAIL"
    private const int SeatBelts = 290;         // Status   "SEAT BELTS"

    // ---- CAS ---------------------------------------------------------------------

    [Fact]
    public void Fixture_CasBits_DecodeToTheEicasList()
    {
        var active = DecodeCasBits(Store<CasBlock>("A22X.CAS Data").msg);
        Assert.Equal(new[] { 142, 282, 284, 290, 341 }, active);
        Assert.Equal(new[] { "APU ON", "PARK BRAKE ON", "AUTOBRAKE RTO", "SEAT BELTS", "EXT PWR AVAIL" },
            active.Select(i => Line(i).Text));
    }

    [Fact]
    public void DecodeCasBits_HandlesTheTopBitOfAWord_AndNull()
    {
        Assert.Empty(DecodeCasBits(null));
        Assert.Equal(new[] { 31, 63 }, DecodeCasBits(new[] { 0x80000000L, 0x80000000L }));
    }

    [Fact]
    public void UnknownIndex_IsAQuietStatusLine_NeverDropped()
    {
        var line = Line(9999);
        Assert.Equal(A220CasLevel.Status, line.Level);
        Assert.Equal("CAS message 9999", line.Text);
    }

    [Fact]
    public void CasMonitor_FixtureBaseline_IsSilent_AndOrderedWarningsFirst()
    {
        var mon = new CasMonitor();
        var said = mon.Observe(Store<CasBlock>("A22X.CAS Data").msg, false, false);
        Assert.Empty(said.Immediate);
        Assert.Empty(said.Queued);                 // no warning or caution: no summary at all
        Assert.Equal("Advisory: EXT PWR AVAIL\nStatus: SEAT BELTS\nStatus: AUTOBRAKE RTO\nStatus: PARK BRAKE ON\nStatus: APU ON",
            FormatCasWindow(mon.Current));
        Assert.Equal("1 advisory, 4 status", FormatCasCounts(mon.Current));
    }

    [Fact]
    public void CasMonitor_ConnectSummary_ListsCautionsOnly_NewestFirst()
    {
        var said = new CasMonitor().Observe(Words(PerfDepConfig, EmerLtsOff, SeatBelts, ExtPwrAvail), false, false);
        Assert.Empty(said.Immediate);
        Assert.Equal(new[] { "2 cautions: EMER LTS OFF, FMS PERF DEP CONFIG" }, said.Queued);
    }

    [Fact]
    public void CasMonitor_ConnectSummary_PutsWarningsFirst()
    {
        var said = new CasMonitor().Observe(Words(EmerLtsOff, LEngFire), false, false);
        Assert.Equal(new[] { "1 warning, 1 caution: L ENG FIRE, EMER LTS OFF" }, said.Queued);
    }

    [Fact]
    public void CasMonitor_NewCautionInterrupts_AdvisoryQueues_StatusIsSilent()
    {
        var mon = new CasMonitor();
        mon.Observe(Words(), false, false);
        var said = mon.Observe(Words(EmerLtsOff, ExtPwrAvail, SeatBelts), false, false);
        Assert.Equal(new[] { "Caution: EMER LTS OFF" }, said.Immediate);
        Assert.Equal(new[] { "Advisory: EXT PWR AVAIL" }, said.Queued);
    }

    [Fact]
    public void CasMonitor_ClearedCautionSpeaks_ClearedStatusDoesNot()
    {
        var mon = new CasMonitor();
        mon.Observe(Words(EmerLtsOff, SeatBelts), false, false);
        var said = mon.Observe(Words(), false, false);
        Assert.Empty(said.Immediate);
        Assert.Equal(new[] { "EMER LTS OFF cleared" }, said.Queued);
    }

    [Fact]
    public void CasMonitor_MutedRows_SilenceTheirSeverity()
    {
        var mon = new CasMonitor();
        var baseline = mon.Observe(Words(EmerLtsOff), warningsMuted: true, advisoriesMuted: false);
        Assert.Empty(baseline.Queued);             // the summary belongs to the warnings row
        var said = mon.Observe(Words(LEngFire, ExtPwrAvail), warningsMuted: true, advisoriesMuted: true);
        Assert.Empty(said.Immediate);
        Assert.Empty(said.Queued);                 // no "EMER LTS OFF cleared" either
    }

    [Fact]
    public void CasMonitor_FloodIsCapped()
    {
        var mon = new CasMonitor();
        mon.Observe(Words(), false, false);
        var said = mon.Observe(Words(36, 37, 139, 242, 243, 526), false, false);   // six cautions
        Assert.Equal(4, said.Immediate.Count);
        Assert.Equal(new[] { "... and 2 more" }, said.Queued);
    }

    [Fact]
    public void CasMonitor_MissingStoreKeepsTheBaseline()
    {
        var mon = new CasMonitor();
        mon.Observe(Words(EmerLtsOff), false, false);
        var gap = mon.Observe(null, false, false);           // one stale poll
        Assert.Empty(gap.Immediate);
        Assert.Empty(gap.Queued);
        var back = mon.Observe(Words(EmerLtsOff, PerfDepConfig), false, false);
        Assert.Equal(new[] { "Caution: FMS PERF DEP CONFIG" }, back.Immediate);   // only the real change
        Assert.Empty(back.Queued);
    }

    [Fact]
    public void CasMonitor_ResetMakesTheNextObservationABaseline()
    {
        var mon = new CasMonitor();
        mon.Observe(Words(), false, false);
        mon.Reset();                                          // a flight load or reconnect
        var said = mon.Observe(Words(EmerLtsOff), false, false);
        Assert.Empty(said.Immediate);
        Assert.Equal(new[] { "1 caution: EMER LTS OFF" }, said.Queued);   // the summary, once
    }

    [Fact]
    public void CasWindow_And_Counts_WhenEmptyOrMissing()
    {
        Assert.Equal("No CAS messages", FormatCasWindow(new List<CasLine>()));
        Assert.Equal("no messages", FormatCasCounts(new List<CasLine>()));
        Assert.Equal(NotAvailable, FormatCasCounts(null));
    }

    // ---- Gear (EICAS Gear.tsx: icon 0 transit, 1 UP, 2 DN, null "--"; colour = `he`) ----

    private static GearBlock Gear(int? n, int? l, int? r, int colour = 3) => new()
    {
        nose_icon = n, left_icon = l, right_icon = r,
        nose_color = colour, left_color = colour, right_color = colour,
        left_outb_brake_temp = 1, left_inbd_brake_temp = 1, right_inbd_brake_temp = 1, right_outb_brake_temp = 1,
        left_outb_brake_color = 3, left_inbd_brake_color = 3, right_inbd_brake_color = 3, right_outb_brake_color = 3
    };

    [Fact]
    public void Fixture_Gear_IsDownAndLocked_WithBrakeTemps()
    {
        var g = Store<GearBlock>("A22X.Gear Data");
        Assert.Equal(GearPhase.Down, Phase(g));
        Assert.Equal("down and locked", FormatGear(g));
        Assert.Equal("1, 1, 1, 1", FormatBrakeTemps(g));
        Assert.Equal("Gear lever down. Gear down and locked. Brake temperatures 1, 1, 1, 1.", FormatGearReadout(1.0, g));
    }

    [Fact]
    public void Gear_NullIcons_ReadUnknown_NeverDown()
    {
        var g = Gear(null, null, null);
        Assert.Equal(GearPhase.Unknown, Phase(g));
        Assert.Equal("nose unknown, left unknown, right unknown", FormatGear(g));
        Assert.Equal(NotAvailable, FormatGear(null));
        Assert.Equal("Gear lever up. Gear position not available — display link not connected.", FormatGearReadout(0, null));
    }

    [Fact]
    public void Gear_AlertingColours_AreSpoken_GreenIsNot()
    {
        var g = Gear(2, 2, 2);
        g.left_color = 1;
        Assert.Equal("nose down, left down (amber), right down", FormatGear(g));
        g.left_inbd_brake_temp = 5; g.left_inbd_brake_color = 1;
        Assert.Equal("1, 5 (amber), 1, 1", FormatBrakeTemps(g));
    }

    [Fact]
    public void GearMonitor_SpeaksTransitions_BaselineFirst()
    {
        var mon = new GearMonitor();
        Assert.Null(mon.Observe(Gear(2, 2, 2)));                  // baseline
        Assert.Equal("Gear in transit", mon.Observe(Gear(0, 0, 2)));
        Assert.Null(mon.Observe(Gear(0, 0, 0)));                  // still in transit
        Assert.Equal("Gear up", mon.Observe(Gear(1, 1, 1, colour: 2)));
        Assert.Equal("Gear in transit", mon.Observe(Gear(0, 0, 0)));
        Assert.Equal("Gear down and locked", mon.Observe(Gear(2, 2, 2)));
    }

    [Fact]
    public void GearMonitor_StartingUnknown_IsABaselineToo()
    {
        var mon = new GearMonitor();
        Assert.Null(mon.Observe(Gear(null, null, null)));        // unpowered
        Assert.Null(mon.Observe(Gear(2, 2, 2)));                  // power-up: not a gear movement
        Assert.Equal("Gear in transit", mon.Observe(Gear(0, 2, 2)));
    }

    [Fact]
    public void GearMonitor_AnUnknownPoll_NeverReplacesAKnownBaseline()
    {
        var mon = new GearMonitor();
        Assert.Null(mon.Observe(Gear(2, 2, 2)));                  // baseline: down
        Assert.Null(mon.Observe(Gear(null, null, null)));         // one "--" poll (data hiccup)
        Assert.Equal("Gear in transit", mon.Observe(Gear(0, 2, 2)));   // the real change is still spoken
        Assert.Null(mon.Observe(Gear(null, 0, 0)));
        Assert.Equal("Gear up", mon.Observe(Gear(1, 1, 1, colour: 2)));

        var same = new GearMonitor();
        same.Observe(Gear(2, 2, 2));
        same.Observe(Gear(null, null, null));
        Assert.Null(same.Observe(Gear(2, 2, 2)));                 // back to the same phase: nothing moved
    }

    // ---- Doors (Synoptic Door.tsx: 3 closed, 1 open/filled, 0/2 by colour) ---------

    [Fact]
    public void Fixture_Doors_AreAllClosed_SlidesArmed()
    {
        var d = Store<DoorsBlock>("A22X.Doors Data");
        Assert.Equal("all closed", FormatDoorsSummary(d));
        Assert.Equal("closed, armed", FormatDoor(d, "fwd_pax"));
        Assert.Equal("closed", FormatDoor(d, "fwd_cargo"));
        Assert.Equal("closed", FormatDoor(d, "l_owee"));
        Assert.Equal(11, Doors.Length);
    }

    [Fact]
    public void Doors_ValuesTheBundleDoesNotName_AreSpokenByColour()
    {
        var d = Store<DoorsBlock>("A22X.Doors Data");
        d.fwd_pax = 1; d.fwd_pax_armed = false;
        d.aft_srv = 2;
        d.fwd_cargo = 0;
        d.mid_equip = null;
        Assert.Equal("open, disarmed", FormatDoor(d, "fwd_pax"));
        Assert.Equal("not closed (white)", FormatDoor(d, "aft_srv"));     // ARMED hidden at 2
        Assert.Equal("not closed (red)", FormatDoor(d, "fwd_cargo"));
        Assert.Equal("unknown", FormatDoor(d, "mid_equip"));
        Assert.Equal("4 not closed: forward passenger door, aft service door, forward cargo door, mid equipment bay door",
            FormatDoorsSummary(d));
        Assert.Equal(NotAvailable, FormatDoor(null, "fwd_pax"));
    }

    [Fact]
    public void DoorMonitor_SpeaksOpenCloseAndArming_BaselineFirst()
    {
        var mon = new DoorMonitor();
        var d = Store<DoorsBlock>("A22X.Doors Data");
        Assert.Empty(mon.Observe(d));
        var opened = Store<DoorsBlock>("A22X.Doors Data");
        opened.fwd_pax = 1; opened.fwd_pax_armed = false;
        Assert.Equal(new[] { "Forward passenger door open", "Forward passenger door disarmed" }, mon.Observe(opened));
        Assert.Equal(new[] { "Forward passenger door closed", "Forward passenger door armed" }, mon.Observe(d));
    }

    // ---- Engine start (EICAS Engines: START box; IGN 1 white, 2 green) --------------

    private static EngineBlock Eng2(bool start, int ign, double n2) => new()
    {
        eng1_n1 = 18.5, eng1_n2 = 64.9, eng1_start = false, eng1_ign = 0,
        eng2_n1 = 0, eng2_n2 = n2, eng2_start = start, eng2_ign = ign
    };

    [Fact]
    public void Fixture_Engines_AreRunning() => UnderCulture("de-DE", () =>
    {
        var e = Store<EngineBlock>("A22X.Engine Data");
        Assert.Equal("running, N1 18.5 percent", EngineState(e, 1));
        Assert.Equal("running, N1 18.5 percent", EngineState(e, 2));
        Assert.Equal(NotAvailable, EngineState(null, 1));
    });

    [Fact]
    public void EngineState_DuringAStart() => UnderCulture("de-DE", () =>
    {
        Assert.Equal("starting, N2 12 percent", EngineState(Eng2(true, 0, 12.2), 2));
        Assert.Equal("starting, ignition on, N2 19 percent", EngineState(Eng2(true, 2, 19.4), 2));
        Assert.Equal("N2 30 percent", EngineState(Eng2(false, 0, 30), 2));
        Assert.Equal("off", EngineState(Eng2(false, 0, 0), 2));
    });

    [Fact]
    public void EngineStartTracker_ThreeCallsPerStart_BaselineFirst()
    {
        var t = new EngineStartTracker();
        Assert.Empty(t.Observe(Eng2(false, 0, 0)));
        Assert.Equal(new[] { "Engine 2 starting" }, t.Observe(Eng2(true, 0, 5)));
        Assert.Empty(t.Observe(Eng2(true, 1, 12)));                       // white IGN: selected, not firing
        Assert.Equal(new[] { "Engine 2 ignition" }, t.Observe(Eng2(true, 2, 19)));
        Assert.Empty(t.Observe(Eng2(true, 2, 40)));
        Assert.Equal(new[] { "Engine 2 start complete" }, t.Observe(Eng2(false, 0, 64.7)));
    }

    [Fact]
    public void EngineStartTracker_AnAbortedStart_SaysWhereN2Stopped() => UnderCulture("de-DE", () =>
    {
        var t = new EngineStartTracker();
        t.Observe(Eng2(false, 0, 0));
        t.Observe(Eng2(true, 0, 12));
        Assert.Equal(new[] { "Engine 2 start ended, N2 30 percent" }, t.Observe(Eng2(false, 0, 30.4)));
    });

    [Fact]
    public void EngineStartTracker_ConnectingMidStart_IsSilent()
    {
        var t = new EngineStartTracker();
        Assert.Empty(t.Observe(Eng2(true, 2, 30)));                      // baseline mid-start
        Assert.Equal(new[] { "Engine 2 start complete" }, t.Observe(Eng2(false, 0, 64)));
    }

    // ---- IRS (POS IRS: mode 0 POWER UP, 1-2 ATTITUDE, 3 NAVIGATION; minutes) ---------

    private static FmsPositionBlock Pos(params (int? Mode, double? Minutes)[] units)
    {
        IrsSlot? Slot(int i) => i < units.Length ? new IrsSlot { irs = new IrsUnit { mode = units[i].Mode, time_to_align = units[i].Minutes } } : null;
        return new FmsPositionBlock { irs_1 = Slot(0), irs_2 = Slot(1), irs_3 = Slot(2) };
    }

    [Fact]
    public void Irs_NoData_IsUnknown()
    {
        var fixturePfd = Store<PfdBlock>("A22X.L PFD Data");     // align false, no FMS position
        Assert.Equal(IrsPhase.Unknown, Irs(fixturePfd, null).Phase);
        Assert.Equal(NotAvailable, FormatIrs(Irs(fixturePfd, null)));
        Assert.Equal(IrsPhase.Unknown, Irs(null, null).Phase);
        Assert.Equal(IrsPhase.Unknown, Irs(null, Pos((null, null))).Phase);
    }

    [Fact]
    public void Irs_Aligning_QuotesTheLongestTimeToNav() => UnderCulture("de-DE", () =>
    {
        var s = Irs(new PfdBlock { align = true, stationary_align = true }, Pos((1, 6.5), (0, 7.2), (3, null)));
        Assert.Equal(IrsPhase.Aligning, s.Phase);
        Assert.Equal("aligning, 7.2 minutes to navigation, do not taxi", FormatIrs(s));
    });

    [Fact]
    public void Irs_PfdFlagAlone_IsAligning_AllNavigation_IsAligned()
    {
        Assert.Equal("aligning", FormatIrs(Irs(new PfdBlock { align = true }, null)));
        Assert.Equal("aligned", FormatIrs(Irs(new PfdBlock { align = false }, Pos((3, null), (3, null), (3, null)))));
    }

    [Fact]
    public void IrsMonitor_SpeaksAligningThenAligned_BaselineFirst()
    {
        var mon = new IrsMonitor();
        var unknown = Irs(null, Pos((null, null)));
        var aligning = Irs(null, Pos((0, 9.0)));
        var aligned = Irs(null, Pos((3, null)));
        Assert.Null(mon.Observe(unknown, haveData: true));
        Assert.Equal("IRS aligning", mon.Observe(aligning, true));
        Assert.Null(mon.Observe(aligning, true));
        Assert.Null(mon.Observe(aligned, false));                 // a missing block changes nothing, even if the status differs
        Assert.Equal("IRS aligned", mon.Observe(aligned, true));  // so the change is still there to speak
    }

    [Fact]
    public void IrsMonitor_AnUnknownPoll_NeverReplacesAKnownBaseline()
    {
        // One block missing (e.g. fmspos stale, PFD align flag already clear): Irs() reads Unknown,
        // and that must not stand in for the Aligning baseline.
        var unknown = Irs(new PfdBlock { align = false }, null);
        Assert.Equal(IrsPhase.Unknown, unknown.Phase);
        var aligning = Irs(null, Pos((1, 4.0)));
        var aligned = Irs(null, Pos((3, null)));

        var mon = new IrsMonitor();
        Assert.Null(mon.Observe(aligning, true));                 // baseline: already aligning
        Assert.Null(mon.Observe(unknown, true));
        Assert.Equal("IRS aligned", mon.Observe(aligned, true));  // the one call that matters

        var again = new IrsMonitor();
        again.Observe(aligning, true);
        again.Observe(unknown, true);
        Assert.Null(again.Observe(aligning, true));               // still aligning: no second "IRS aligning"
    }

    [Fact]
    public void Monitors_IgnoreAMissingBlock()
    {
        // Each monitor is fed a CHANGED state after the null: a monitor that forgot its baseline
        // on the null would see a first observation and stay silent.
        var gear = new GearMonitor();
        gear.Observe(Gear(2, 2, 2));
        Assert.Null(gear.Observe(null));
        Assert.Equal("Gear in transit", gear.Observe(Gear(0, 2, 2)));

        var doors = new DoorMonitor();
        doors.Observe(Store<DoorsBlock>("A22X.Doors Data"));
        Assert.Empty(doors.Observe(null));
        var opened = Store<DoorsBlock>("A22X.Doors Data");
        opened.fwd_pax = 1; opened.fwd_pax_armed = false;
        Assert.Equal(new[] { "Forward passenger door open", "Forward passenger door disarmed" }, doors.Observe(opened));

        var eng = new EngineStartTracker();
        eng.Observe(Eng2(true, 2, 30));
        Assert.Empty(eng.Observe(null));
        Assert.Equal(new[] { "Engine 2 start complete" }, eng.Observe(Eng2(false, 0, 64)));
    }
}
