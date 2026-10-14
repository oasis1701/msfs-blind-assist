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
}
