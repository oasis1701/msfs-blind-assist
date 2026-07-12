// Route-advisory parsing + announce-decision rules (spec 2026-07-12): the parser is
// DELIBERATELY defensive — the route variant's hit format is only partially known, so
// unknown text renders verbatim as its own block rather than being dropped. The
// tracker is baseline-first; ClearAnnouncedKeys() keeps the baseline (15-minute
// reminder semantics, matching _announcedSigmetKeys); Reset() re-baselines fully.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteAdvisoriesTests
{
    private const string NoHit = "No airmet/sigmet affecting currently loaded flight plan route";
    private const string OneBlock =
        "CONVECTIVE SIGMET 18E\r\nValid until: 2000z\r\nAREA TS MOV FROM 26015KT. TOPS TO FL420.";
    private const string TwoBlocks =
        "CONVECTIVE SIGMET 18E\r\nValid until: 2000z\r\n\r\nSIGMET B4\r\nSEV TURB FL250-FL380";

    // --- ParseRouteAdvisories -----------------------------------------------------------

    [Theory]
    [InlineData(NoHit)]
    [InlineData("no airmet/sigmet affecting currently loaded flight plan route")]  // case-insensitive
    [InlineData("  No airmet/sigmet affecting anything at all  ")]                 // prefix match
    public void No_hit_sentence_parses_to_empty(string raw)
        => Assert.Empty(ActiveSkyFormatting.ParseRouteAdvisories(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_parses_to_empty(string raw)
        => Assert.Empty(ActiveSkyFormatting.ParseRouteAdvisories(raw));

    [Fact]
    public void Single_block_keeps_lines_and_keys_on_first_line()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(OneBlock);
        var a = Assert.Single(advisories);
        Assert.Equal("CONVECTIVE SIGMET 18E", a.Key);
        Assert.Equal(3, a.Lines.Count);
        Assert.Equal("AREA TS MOV FROM 26015KT. TOPS TO FL420.", a.Lines[2]);
    }

    [Fact]
    public void Blank_lines_split_blocks()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(TwoBlocks);
        Assert.Equal(2, advisories.Count);
        Assert.Equal("CONVECTIVE SIGMET 18E", advisories[0].Key);
        Assert.Equal("SIGMET B4", advisories[1].Key);
    }

    [Fact]
    public void Unknown_free_text_renders_verbatim_as_one_block()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories("No flight plan is currently loaded");
        var a = Assert.Single(advisories);
        Assert.Equal("No flight plan is currently loaded", a.Key);
    }

    [Fact]
    public void Lf_only_newlines_parse_identically()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "SIGMET B4\nSEV TURB FL250-FL380\n\nAIRMET T1\nMOD TURB");
        Assert.Equal(2, advisories.Count);
        Assert.Equal("AIRMET T1", advisories[1].Key);
    }

    [Fact]
    public void Whitespace_only_separator_line_does_not_split_the_block()
    {
        // A blank-line block separator requires "\r\n\r\n"/"\n\n" — a line containing only
        // spaces is neither. Known merged-block limitation: the split doesn't happen, so
        // "SIGMET B4"/"SEV TURB" and "AIRMET T1"/"MOD TURB" stay one block. The spaces-only
        // line itself is TrimEnd'ed to empty by the per-line filter and dropped, so it
        // never surfaces as a blank Lines entry either.
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "SIGMET B4\r\nSEV TURB\r\n   \r\nAIRMET T1\r\nMOD TURB");
        var a = Assert.Single(advisories);
        Assert.Equal("SIGMET B4", a.Key);
        Assert.Equal(new[] { "SIGMET B4", "SEV TURB", "AIRMET T1", "MOD TURB" }, a.Lines);
    }

    // --- Live capture (2026-07-12, /GetActiveSigmetsAt, port 19285) -------------------
    // Real hit-path format: each advisory is exactly 3 lines (header / Valid until /
    // body) separated by single CRLF — NO blank lines — and ActiveSky repeats the same
    // advisory once per route-segment intersection (MHTG J5 appeared 7×). Spacing
    // irregularities in the MHTG body are verbatim from the capture.
    private const string LiveMhtgBody =
        "MHCC CENTRAL AMERICAN FIR EMBD TS OBS AT 1830Z WI N1121 W10027 - N1258 W09506 - N1403 W09304- N1127 W09031  - N0950 W09306 - N0923 W09619 - N0904 W09940 TOP FL520 MOV W 05KT NC=";
    private const string LiveYmmmBody =
        "YMMM MELBOURNE FIR SEV TURB FCST WI S3640 E14800 - S3340 E15000 - S3410 E15100 - S3740 E14940 - S3820 E14550 - S3730 E14520 SFC/8000FT STNR NC=";
    private const string LiveMhtgBlock =
        "MHTG SIGMET J5 EMBD TS\r\nValid until: 2200z\r\n" + LiveMhtgBody + "\r\n";
    private const string LiveYmmmBlock =
        "YMMM SIGMET T07 TURB\r\nValid until: 2300z\r\n" + LiveYmmmBody + "\r\n";

    private static string LiveCapture()
        => string.Concat(Enumerable.Repeat(LiveMhtgBlock, 7)) + LiveYmmmBlock;

    [Fact]
    public void Live_capture_single_crlf_blocks_split_and_dedup_to_two()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture());
        Assert.Equal(2, advisories.Count);
        Assert.Equal("MHTG SIGMET J5 EMBD TS", advisories[0].Key);
        Assert.Equal("YMMM SIGMET T07 TURB", advisories[1].Key);
        Assert.Equal(new[] { "MHTG SIGMET J5 EMBD TS", "Valid until: 2200z", LiveMhtgBody },
            advisories[0].Lines);
    }

    [Fact]
    public void Header_line_starts_a_new_block_without_blank_separators()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "MHTG SIGMET J5 EMBD TS\r\nValid until: 2200z\r\nBODY A=\r\n"
            + "YMMM AIRMET T07 TURB\r\nValid until: 2300z\r\nBODY B=");
        Assert.Equal(2, advisories.Count);
        Assert.Equal("YMMM AIRMET T07 TURB", advisories[1].Key);
        Assert.Equal(new[] { "YMMM AIRMET T07 TURB", "Valid until: 2300z", "BODY B=" },
            advisories[1].Lines);
    }

    [Fact]
    public void Duplicate_blocks_dedup_case_insensitively_keeping_first()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "MHTG SIGMET J5 EMBD TS\r\nBody 1\r\nmhtg sigmet j5 embd ts\r\nBody 2");
        var a = Assert.Single(advisories);
        Assert.Equal("MHTG SIGMET J5 EMBD TS", a.Key);
        Assert.Equal(new[] { "MHTG SIGMET J5 EMBD TS", "Body 1" }, a.Lines);
    }

    [Fact]
    public void Leading_free_text_before_first_header_is_its_own_block()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "Some preamble text\r\nMHTG SIGMET J5 EMBD TS\r\nValid until: 2200z\r\nBODY=");
        Assert.Equal(2, advisories.Count);
        Assert.Equal("Some preamble text", advisories[0].Key);
        Assert.Equal("MHTG SIGMET J5 EMBD TS", advisories[1].Key);
    }

    // --- Field decoding (spec 2026-07-12-route-advisory-decoding §3.2) ----------------

    [Fact]
    public void Live_mhtg_advisory_decodes_all_fields()
    {
        var a = ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture())[0];
        Assert.Equal("MHTG SIGMET J5", a.Identity);
        Assert.Equal("embedded thunderstorms", a.Hazard);
        Assert.Equal("observed at 1830Z", a.ObsFcst);
        Assert.Equal("tops FL520", a.VerticalExtent);
        Assert.Equal("moving west at 5 knots", a.Movement);
        Assert.Equal("no change expected", a.Trend);
        Assert.Equal("2200Z", a.ValidUntil);
    }

    [Fact]
    public void Live_ymmm_advisory_decodes_all_fields()
    {
        var a = ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture())[1];
        Assert.Equal("YMMM SIGMET T07", a.Identity);
        Assert.Equal("severe turbulence", a.Hazard);
        Assert.Equal("forecast", a.ObsFcst);
        Assert.Equal("surface to 8,000 feet", a.VerticalExtent);
        Assert.Equal("stationary", a.Movement);
        Assert.Equal("no change expected", a.Trend);
        Assert.Equal("2300Z", a.ValidUntil);
    }

    [Fact]
    public void Undecodable_block_has_null_fields()
    {
        var a = Assert.Single(ActiveSkyFormatting.ParseRouteAdvisories(
            "No flight plan is currently loaded"));
        Assert.Null(a.Identity);
        Assert.Null(a.Hazard);
        Assert.Null(a.ObsFcst);
        Assert.Null(a.VerticalExtent);
        Assert.Null(a.Movement);
        Assert.Null(a.Trend);
        Assert.Null(a.ValidUntil);
    }

    // --- BuildRouteAdvisoryAnnouncement -----------------------------------------------

    [Fact]
    public void Announcement_speaks_identity_hazard_and_extent()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture());
        Assert.Equal("MHTG SIGMET J5, embedded thunderstorms, tops FL520",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(advisories[0]));
        Assert.Equal("YMMM SIGMET T07, severe turbulence, surface to 8,000 feet",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(advisories[1]));
    }

    [Fact]
    public void Announcement_falls_back_to_raw_key_when_nothing_decodes()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "No flight plan is currently loaded");
        Assert.Equal("No flight plan is currently loaded",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(advisories[0]));
    }

    // --- BuildRouteAdvisoriesText ---------------------------------------------------------

    [Fact]
    public void Empty_list_reads_no_advisories()
        => Assert.Equal("No advisories on route.",
            ActiveSkyFormatting.BuildRouteAdvisoriesText(
                ActiveSkyFormatting.ParseRouteAdvisories(NoHit)));

    [Fact]
    public void Blocks_render_with_blank_separator_rows()
    {
        string text = ActiveSkyFormatting.BuildRouteAdvisoriesText(
            ActiveSkyFormatting.ParseRouteAdvisories(TwoBlocks));
        string[] rows = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Assert.Equal(new[]
        {
            "CONVECTIVE SIGMET 18E",
            "Valid until: 2000z",
            "",
            "SIGMET B4",
            "SEV TURB FL250-FL380",
        }, rows);
    }

    // --- RouteAdvisoryTracker ---------------------------------------------------------------

    [Fact]
    public void First_read_baselines_silently_even_with_advisories_present()
    {
        var t = new RouteAdvisoryTracker();
        Assert.Empty(t.Observe(new[] { "SIGMET B4" }));
        Assert.Empty(t.Observe(new[] { "SIGMET B4" }));          // unchanged: silent
    }

    [Fact]
    public void New_key_is_reported_once()
    {
        var t = new RouteAdvisoryTracker();
        t.Observe(new string[0]);                                 // baseline: clear route
        Assert.Equal(new[] { "SIGMET B4" }, t.Observe(new[] { "SIGMET B4" }));
        Assert.Empty(t.Observe(new[] { "SIGMET B4" }));
    }

    [Fact]
    public void Duplicate_key_within_one_observe_call_announces_once()
    {
        var t = new RouteAdvisoryTracker();
        t.Observe(new string[0]);                                 // baseline: clear route
        Assert.Equal(new[] { "SIGMET B4" }, t.Observe(new[] { "SIGMET B4", "SIGMET B4" }));
    }

    [Fact]
    public void Keys_persist_across_gaps()
    {
        // The caller never Observes on failed fetches — keys simply persist, so a
        // reconnect with the same advisories stays silent.
        var t = new RouteAdvisoryTracker();
        t.Observe(new string[0]);
        t.Observe(new[] { "SIGMET B4" });                         // announced
        Assert.Empty(t.Observe(new[] { "SIGMET B4" }));           // after a gap: silent
    }

    [Fact]
    public void ClearAnnouncedKeys_keeps_baseline_and_reannounces_active_advisories()
    {
        // 15-minute reminder semantics, matching _announcedSigmetKeys: after the
        // periodic clear, a still-active advisory announces again.
        var t = new RouteAdvisoryTracker();
        t.Observe(new string[0]);
        t.Observe(new[] { "SIGMET B4" });
        t.ClearAnnouncedKeys();
        Assert.Equal(new[] { "SIGMET B4" }, t.Observe(new[] { "SIGMET B4" }));
    }

    [Fact]
    public void Reset_rebaselines_silently()
    {
        var t = new RouteAdvisoryTracker();
        t.Observe(new string[0]);
        t.Observe(new[] { "SIGMET B4" });
        t.Reset();
        Assert.Empty(t.Observe(new[] { "SIGMET B4", "AIRMET T1" }));   // first read after reset
        Assert.Equal(new[] { "SIGMET Z9" }, t.Observe(new[] { "SIGMET B4", "SIGMET Z9" }));
    }
}
