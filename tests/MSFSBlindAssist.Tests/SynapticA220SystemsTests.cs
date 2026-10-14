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

    // ---- V-speeds (A22X.L PFD Data, proven live after SET VSPEEDS) -------------------

    [Fact]
    public void Fixture_VSpeeds_ReadAsTheBugsShowThem()
    {
        var p = Store<PfdBlock>("A22X.L PFD Data");
        Assert.Equal("V1 131 knots", VSpeedReadout(p, "V1"));
        Assert.Equal("VR 135 knots", VSpeedReadout(p, "VR"));
        Assert.Equal("V2 139 knots", VSpeedReadout(p, "V2"));
        Assert.Equal("VREF not set", VSpeedReadout(p, "VREF"));
        Assert.Equal("VAC not set", VSpeedReadout(p, "VAC"));
        Assert.Equal("Flap speeds F1 168 knots", FormatFlapSpeeds(p));
        Assert.Equal("V-speeds not available — display link not connected", VSpeedReadout(null, "V1"));
        Assert.Equal("No flap speed bugs set", FormatFlapSpeeds(new PfdBlock()));
    }

    [Fact]
    public void RollWithoutVr_WarnsOncePerRoll_OnlyWithoutVr()
    {
        var gate = new RollWithoutVrGate();
        Assert.False(gate.Observe(20, onGround: true, vrKnown: false));     // taxi
        Assert.True(gate.Observe(41, true, false));                         // roll started, no VR
        Assert.False(gate.Observe(60, true, false));                        // once per roll
        Assert.False(gate.Observe(30, true, false));                        // rejected: re-arms
        Assert.True(gate.Observe(45, true, false));
        var withVr = new RollWithoutVrGate();
        withVr.Observe(20, true, true);
        Assert.False(withVr.Observe(41, true, true));
        var airborne = new RollWithoutVrGate();
        airborne.Observe(20, false, false);
        Assert.False(airborne.Observe(41, false, false));                   // a landing never warns
    }

    // ---- Transponder (CNS TransponderControl.tsx) ------------------------------------

    [Fact]
    public void Fixture_Atc_ReadsSquawkAndMode()
    {
        var a = Store<AtcBlock>("A22X.ATC Data");
        Assert.Equal("squawk 2000, AUTO", FormatAtc(a));
        Assert.Equal("Squawk 2000, transponder AUTO", FormatSquawkReadout(a, SquawkToBcd16(1200)));   // the bus wins
        Assert.Equal("Squawk 1200", FormatSquawkReadout(null, SquawkToBcd16(1200)));                   // stock word is BCD16
        a.mode = 2; a.tss_limits_sel = 3; a.ident = true;
        Assert.Equal("squawk 2000, TA/RA, altitude limit Above and Below, ident", FormatAtc(a));
        Assert.Equal(NotAvailable, FormatAtc(null));
    }

    // The A220 registers TRANSPONDER CODE:1 with units "number"; every other add-on that reads
    // that SimVar documents the raw BCD16 word (squawk 2000 reads 8192, FlyByWireA380Definition.cs:2405,
    // MD-11 read 0x5473 live), so the stock fallback decodes nibbles instead of printing the word.
    [Fact]
    public void StockSquawk_IsTheBcd16Word_NeverTheDecimal()
    {
        Assert.Equal("Squawk 2000", FormatSquawkReadout(null, 8192));
        Assert.Equal("Squawk 0400", FormatSquawkReadout(null, 0x0400));
        Assert.Equal("Squawk 0000", FormatSquawkReadout(null, 0));
        Assert.Equal("Squawk " + NotAvailable, FormatSquawkReadout(null, 2000));   // 0x7D0 is no octal word: no guess
        Assert.Equal("Squawk " + NotAvailable, FormatSquawkReadout(null, double.NaN));
        Assert.Equal("Squawk " + NotAvailable, FormatSquawkReadout(null, -1));
        Assert.Equal(2000, Bcd16ToSquawk(8192));
        Assert.Equal(7777, Bcd16ToSquawk(0x7777));
        Assert.Null(Bcd16ToSquawk(0x7778));
        Assert.Null(Bcd16ToSquawk(0x17000));
        Assert.Null(Bcd16ToSquawk(8192.5));
        // What a later XPNDR_SET read-back compares: the code we sent against the word the sim reports.
        Assert.True(StockCodeLanded(1200, SquawkToBcd16(1200)));
        Assert.False(StockCodeLanded(1200, SquawkToBcd16(7000)));
        Assert.False(StockCodeLanded(1200, 1200));
        Assert.False(StockCodeLanded(1200, double.NaN));
    }

    [Theory]
    [InlineData(7000, true, "7000")]
    [InlineData(400, true, "0400")]
    [InlineData(1200, true, "1200")]
    [InlineData(7800, false, "")]
    [InlineData(1239, false, "")]
    [InlineData(12.5, false, "")]
    [InlineData(-1, false, "")]
    public void Squawk_IsFourOctalDigits(double value, bool ok, string text)
    {
        Assert.Equal(ok, TryParseSquawk(value, out _, out string t));
        Assert.Equal(text, t);
        Assert.False(TryParseSquawk(double.NaN, out _, out _));
    }

    [Fact]
    public void Squawk_Bcd16_And_Payloads_AreInvariant() => UnderCulture("de-DE", () =>
    {
        Assert.Equal(0x7000u, SquawkToBcd16(7000));
        Assert.Equal(0x1234u, SquawkToBcd16(1234));
        Assert.Equal("{\"type\":\"SetCode\",\"code\":7000}", AtcSetCodeJson(7000));
        Assert.Equal("{\"type\":\"SetMode\",\"mode\":2}", AtcSetModeJson(2));
        Assert.Equal("{\"type\":\"SetTssLimits\",\"limits\":1}", AtcSetLimitsJson(1));
        Assert.Equal("{\"type\":\"Ident\"}", AtcIdentJson());
        Assert.Equal("{\"type\":\"SetAtcId\",\"id\":\"DAL123\"}", AtcSetIdJson("DAL123"));
    });

    [Fact]
    public void Transponder_Failures_NameTheStateTheAircraftKept()
    {
        var a = Store<AtcBlock>("A22X.ATC Data");
        Assert.Null(XpdrModeFailure(0, a));
        Assert.Equal("Transponder mode did not change — still AUTO.", XpdrModeFailure(2, a));
        Assert.Equal("Transponder mode did not change — still unknown.", XpdrModeFailure(2, null));
        Assert.Equal("TCAS altitude limit did not change — still Normal.", AltLimitFailure(1, a));
    }

    [Theory]
    [InlineData("dal123", true, "DAL123")]
    [InlineData("", true, "00000000")]
    [InlineData("  ", true, "00000000")]
    [InlineData("A", false, "")]
    [InlineData("TOOLONG123", false, "")]
    [InlineData("DAL-12", false, "")]
    public void FlightId_IsTwoToEightLettersOrDigits_EmptyClears(string input, bool ok, string id)
    {
        Assert.Equal(ok, TryParseFlightId(input, out string got));
        Assert.Equal(id, got);
    }

    [Fact]
    public void FlightId_Cleared_AcceptsAnyEmptyReadBack()
    {
        Assert.True(FlightIdLanded("DAL123", "DAL123"));
        Assert.True(FlightIdLanded("00000000", ""));
        Assert.True(FlightIdLanded("00000000", null));
        Assert.True(FlightIdLanded("00000000", "00000000"));
        Assert.False(FlightIdLanded("DAL123", ""));
    }

    // ---- CTP altimeter and minimums ([A220-33]) -------------------------------------

    [Theory]
    [InlineData(30.13, true)]
    [InlineData(1013, true)]
    [InlineData(27.0, true)]
    [InlineData(32.0, true)]
    [InlineData(940, true)]
    [InlineData(1090, true)]
    [InlineData(26.99, false)]
    [InlineData(939, false)]
    [InlineData(500, false)]
    public void QnhInput_AcceptsTheDialogRanges(double input, bool ok)
        => Assert.Equal(ok, TryParseQnhInput(input, out _));

    [Fact]
    public void Qnh_IsSentInTheSidesOwnUnit_AndInvariant() => UnderCulture("de-DE", () =>
    {
        Assert.True(TryParseQnhInput(30.13, out double hpa));
        Assert.Equal(new QnhSetting("InHg", 30.13), QnhFor(hpa, unitHpa: false));
        Assert.Equal(new QnhSetting("Hpa", 1020), QnhFor(hpa, unitHpa: true));
        Assert.True(TryParseQnhInput(1013, out double std));
        Assert.Equal(new QnhSetting("InHg", 29.91), QnhFor(std, false));
        Assert.Equal("{\"qnh\":{\"type\":\"InHg\",\"value\":30.13}}", CtpQnhJson(new QnhSetting("InHg", 30.13)));
        Assert.Equal("{\"qnh_std\":true}", CtpStdJson(true));
        Assert.Equal("{\"minimums_mode\":1}", CtpMinimumsModeJson(1));
        Assert.Equal("{\"minimums\":1210}", CtpMinimumsJson(1210));
    });

    [Fact]
    public void Fixture_Ctp_ReadBack_MatchesTheLiveSet() => UnderCulture("de-DE", () =>
    {
        var ctp = Store<CtpBlock>("A22X.L CTP Data");            // qnh InHg 30.130001 (live set of 30.13)
        Assert.True(QnhLanded(ctp, new QnhSetting("InHg", 30.13)));
        Assert.False(QnhLanded(ctp, new QnhSetting("InHg", 29.92)));
        Assert.False(QnhLanded(ctp, new QnhSetting("Hpa", 1020)));
        Assert.Equal("30.13 inches", FormatQnh(ctp.qnh));
        Assert.Equal("30.13 inches", SideAltimeter(null, ctp));
        Assert.Equal("baro 1210 feet", FormatMinimums(ctp));
        Assert.Equal("30.13", QnhEntryText(ctp));
        Assert.False(UnitIsHpa(ctp, null));
        Assert.Equal(1020.32, CurrentHpa(ctp)!.Value, 1);
    });

    [Fact]
    public void Altimeter_StdAndUnit_ComeFromThePfdsAdsBlock() => UnderCulture("de-DE", () =>
    {
        var ctp = Store<CtpBlock>("A22X.L CTP Data");
        Assert.Equal("standard", SideAltimeter(new AdsBlock { qnh_std = true, qnh_unit = 0, qnh_inhg = 30.13 }, ctp));
        Assert.Equal("1020 hectopascals", SideAltimeter(new AdsBlock { qnh_std = false, qnh_unit = 1, qnh_hpa = 1020.3 }, ctp));
        Assert.Equal("29.92 inches", SideAltimeter(new AdsBlock { qnh_std = false, qnh_unit = 0, qnh_inhg = 29.92 }, null));
        Assert.Null(SideAltimeter(null, null));
        Assert.True(IsStd(null, new CtpBlock { qnh_std = true }));
        Assert.False(IsStd(new AdsBlock { qnh_std = false }, new CtpBlock { qnh_std = true }));   // the PFD's truth wins
        Assert.Null(IsStd(null, ctp));                                                             // fixture: absent
    });

    [Fact]
    public void AltimeterReadout_NamesAFirstOfficerMismatch() => UnderCulture("de-DE", () =>
    {
        var l = new AdsBlock { qnh_unit = 0, qnh_inhg = 30.13, qnh_std = false };
        var r = new AdsBlock { qnh_unit = 0, qnh_inhg = 29.92, qnh_std = false };
        Assert.Equal("Altimeter 30.13 inches", FormatAltimeterReadout(l, null, l, null));
        Assert.Equal("Altimeter 30.13 inches, first officer 29.92 inches", FormatAltimeterReadout(l, null, r, null));
        Assert.Null(FormatAltimeterReadout(null, null, null, null));
        Assert.Equal("Altimeter 29.92 inches, 1013 hectopascals. Standard not known — the display link is not connected.",
            FormatAltimeterFallback(29.92, 1013.2));
    });

    [Fact]
    public void Ctp_Results_SayWhatLandedOnEachSide() => UnderCulture("de-DE", () =>
    {
        var landed = new SystemsBlocks
        {
            lctp = new CtpBlock { qnh = new QnhValue { type = "InHg", value = 30.13 }, minimums_mode = 0, minimums = 1210 },
            rctp = new CtpBlock { qnh = new QnhValue { type = "InHg", value = 29.92 }, minimums_mode = 0, minimums = 1210 },
        };
        var want = new QnhSetting("InHg", 30.13);
        Assert.Equal("Captain altimeter 30.13 inches",
            QnhResult(new Dictionary<int, QnhSetting> { [1] = want }, landed));
        Assert.Equal("First officer altimeter did not take the setting — reads 29.92 inches.",
            QnhResult(new Dictionary<int, QnhSetting> { [1] = want, [2] = want }, landed));
        Assert.Equal("Minimums 1210 feet, both sides", MinimumsResult(new[] { 1, 2 }, 1210, landed));
        Assert.Equal("Captain minimums baro 1210 feet", MinimumsResult(new[] { 1 }, 1210, landed));
        Assert.Equal("First officer minimums did not take 200 feet — reads baro 1210 feet.",
            MinimumsResult(new[] { 2 }, 200, landed));
        Assert.Null(MinimumsModeFailure(new[] { 1, 2 }, 0, landed));
        Assert.Equal("Minimums mode did not change — captain still baro 1210 feet.",
            MinimumsModeFailure(new[] { 1 }, 1, landed));
        Assert.Equal("Captain altimeter 30.13 inches", UnitResult(new[] { 1 }, wantHpa: false, landed));
        Assert.Equal("The A220 did not change the altimeter unit — still inches.", UnitResult(new[] { 1 }, true, landed));
    });

    [Fact]
    public void Ctp_StdResult_ReadsTheAdsTruth()
    {
        var std = new SystemsBlocks { lads = new AdsBlock { qnh_std = true }, rads = new AdsBlock { qnh_std = true } };
        Assert.Equal("Altimeter standard, both sides", StdResult(new[] { 1, 2 }, true, std));
        var qnh = new SystemsBlocks
        {
            lads = new AdsBlock { qnh_std = false },
            lctp = new CtpBlock { qnh = new QnhValue { type = "InHg", value = 30.13 } }
        };
        Assert.Equal("Captain altimeter 30.13 inches", StdResult(new[] { 1 }, false, qnh));
        Assert.Equal("Captain baro did not change — still QNH.", StdResult(new[] { 1 }, true, qnh));
    }

    [Theory]
    [InlineData(0, true, 0)]
    [InlineData(41000, true, 41000)]
    [InlineData(1210.4, true, 1210)]
    [InlineData(41001, false, 0)]
    [InlineData(-5, false, 0)]
    public void Minimums_AreZeroTo41000Feet(double v, bool ok, int feet)
    {
        Assert.Equal(ok, TryParseMinimums(v, out int got));
        Assert.Equal(feet, got);
    }

    // ---- Autobrake ([A220-34]: 0 RTO, 1 OFF, 2 LO, 3 MED, 4 HI) -----------------------

    [Fact]
    public void Autobrake_StepsOneDetentAtATime()
    {
        Assert.Equal(new[] { "RTO", "OFF", "LO", "MED", "HI" }, AutobrakeLabels);
        Assert.Equal("INCREASE_AUTOBRAKE_CONTROL", AutobrakeStepEvent(1, 3));
        Assert.Equal("DECREASE_AUTOBRAKE_CONTROL", AutobrakeStepEvent(1, 0));
        Assert.Null(AutobrakeStepEvent(2, 2));
        Assert.Equal(2, AutobrakeNextStep(1, 4));
        Assert.Equal(0, AutobrakeNextStep(1, 0));
        Assert.Equal("Autobrake LO not available on the ground — still OFF", AutobrakeRefusal(1, 2, onGround: true));
        Assert.Equal("Autobrake HI not available — still MED", AutobrakeRefusal(3, 4, onGround: false));
    }

    // ---- External power: one automatic retry ----------------------------------------

    // ExtPowerVerdict is internal and a public test method cannot take it, so the
    // expectation travels as its int value: 0 Done, 1 Retry, 2 Fail.
    [Theory]
    [InlineData(true, 1.0, true, false, 0)]
    [InlineData(true, 0.0, true, false, 1)]
    [InlineData(true, 0.0, true, true, 2)]
    [InlineData(true, 0.0, false, false, 2)]   // no GPU: the reason, not a retry
    [InlineData(false, 1.0, true, false, 1)]   // the live "first press lost"
    [InlineData(false, 0.0, true, false, 0)]
    [InlineData(true, -1.0, true, false, 0)]   // unreadable: nothing honest to add
    public void ExtPower_RetriesOnce(bool wantOn, double state, bool gpu, bool retried, int expected)
        => Assert.Equal((ExtPowerVerdict)expected, ExtPowerCheck(wantOn, state, gpu, retried));

    // ---- Flight plan distances and the ND waypoint ------------------------------------

    [Theory]
    [InlineData(2460, "41 minutes")]
    [InlineData(60, "1 minute")]
    [InlineData(3600, "1 hour")]
    [InlineData(3900, "1 hour 5 minutes")]
    [InlineData(7260, "2 hours 1 minute")]
    public void Durations_ReadAsWords(double seconds, string expected)
        => Assert.Equal(expected, FormatDuration(seconds));

    [Fact]
    public void FlightPlanDistances_ReadOrSayWhyNot() => UnderCulture("de-DE", () =>
    {
        var p = new FplnProgress { ok = true, dtg = 245.4, ete = 2460, tod = 118.6, legs = 14 };
        Assert.Equal("Destination 245 miles, 41 minutes", FormatDistanceToDestination(p));
        Assert.Equal("Top of descent in 119 miles", FormatDistanceToTod(p));
        Assert.Equal("No top of descent in the active flight plan", FormatDistanceToTod(new FplnProgress { ok = true, dtg = 30 }));
        Assert.Equal("Top of descent passed", FormatDistanceToTod(new FplnProgress { ok = true, dtg = 80, tod = -3.2 }));
        Assert.StartsWith("Distance to destination not available", FormatDistanceToDestination(new FplnProgress { ok = false }));
        Assert.StartsWith("Distance to destination not available", FormatDistanceToDestination(null));
        Assert.StartsWith("Distance to top of descent not available", FormatDistanceToTod(null));
    });

    [Fact]
    public void NdWaypoint_ReadsTheFmsToWaypoint() => UnderCulture("de-DE", () =>
    {
        NavBlock Nav(string json) => JsonSerializer.Deserialize<NavBlock>(json)!;
        Assert.Equal("Next waypoint WALET, 12.3 miles", FormatNdWaypoint(Nav("{\"to\":\"WALET\",\"dtg\":12.34,\"source\":0}")));
        Assert.Equal("Next waypoint WALET", FormatNdWaypoint(Nav("{\"to\":\"WALET\",\"dtg\":null,\"source\":0}")));
        Assert.Equal("No active waypoint", FormatNdWaypoint(Nav("{\"to\":null,\"source\":0}")));
        Assert.Equal("The captain's navigation source is VOR 1 — no FMS waypoint", FormatNdWaypoint(Nav("{\"to\":true,\"source\":2}")));
        Assert.Equal("Next waypoint not available — display link not connected", FormatNdWaypoint(null));
    });
}
