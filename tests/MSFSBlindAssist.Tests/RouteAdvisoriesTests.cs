// Route-advisory parsing + announce-decision rules (spec 2026-07-12): the hit format is
// live-verified (2026-07-12 capture, pinned below as fixtures — 3-line advisories,
// single-CRLF separators, per-route-segment duplicates), but the parser REMAINS
// deliberately defensive for unrecognized shapes — unknown text renders verbatim as its
// own block rather than being dropped. The tracker is baseline-first; ClearAnnouncedKeys()
// keeps the baseline (15-minute reminder semantics, matching _announcedSigmetKeys);
// Reset() re-baselines fully.

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
        // The split rule is line-based: a truly-empty line closes the current block, but a
        // spaces-only line does not — it is TrimEnd'ed to empty by the per-line filter and
        // silently dropped (never surfaces as a blank Lines entry, and never splits).
        // "AIRMET T1" also isn't a recognized header line (bare "SIGMET"/"AIRMET" tokens are
        // 6/6 chars, too long for the \S{3,4} header prefix), so nothing here triggers a
        // split — "SIGMET B4"/"SEV TURB" and "AIRMET T1"/"MOD TURB" stay one block.
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
    public void Consecutive_convective_sigmet_headers_split_without_blank_lines()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "CONVECTIVE SIGMET 18E\r\nValid until: 2000z\r\nAREA TS MOV FROM 26015KT. TOPS TO FL420.\r\n"
            + "CONVECTIVE SIGMET 21C\r\nValid until: 2000z\r\nAREA TS TOPS TO FL400.");
        Assert.Equal(2, advisories.Count);
        Assert.Equal("CONVECTIVE SIGMET 18E", advisories[0].Key);
        Assert.Equal("CONVECTIVE SIGMET 21C", advisories[1].Key);
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
        Assert.Equal("Central American FIR", a.FirName);
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
        Assert.Equal("Melbourne FIR", a.FirName);
        Assert.Equal("severe turbulence", a.Hazard);
        Assert.Equal("forecast", a.ObsFcst);
        Assert.Equal("surface to 8,000 feet", a.VerticalExtent);
        Assert.Equal("stationary", a.Movement);
        Assert.Equal("no change expected", a.Trend);
        Assert.Equal("2300Z", a.ValidUntil);
    }

    [Fact]
    public void Convective_sigmet_identity_decodes()
    {
        // US convective SIGMET headers ("CONVECTIVE SIGMET 18E") don't fit the ICAO
        // "<FIR> SIGMET <id>" shape — the identity regex must accept both, matching
        // the header shapes AdvisoryHeaderLine already splits on.
        var a = Assert.Single(ActiveSkyFormatting.ParseRouteAdvisories(OneBlock));
        Assert.Equal("CONVECTIVE SIGMET 18E", a.Identity);
        Assert.Equal("thunderstorms", a.Hazard);
        Assert.Equal("2000Z", a.ValidUntil);
    }

    [Fact]
    public void Convective_sigmet_announcement_speaks_decoded_hazard()
    {
        // With identity decoded, the announcement is the decoded phrase — not the
        // raw-key fallback the null identity used to force.
        var a = Assert.Single(ActiveSkyFormatting.ParseRouteAdvisories(OneBlock));
        Assert.Equal("CONVECTIVE SIGMET 18E, thunderstorms",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(a));
    }

    [Fact]
    public void Undecodable_block_has_null_fields()
    {
        var a = Assert.Single(ActiveSkyFormatting.ParseRouteAdvisories(
            "No flight plan is currently loaded"));
        Assert.Null(a.Identity);
        Assert.Null(a.FirName);
        Assert.Null(a.Hazard);
        Assert.Null(a.ObsFcst);
        Assert.Null(a.VerticalExtent);
        Assert.Null(a.Movement);
        Assert.Null(a.Trend);
        Assert.Null(a.ValidUntil);
    }

    // --- BuildRouteAdvisoryAnnouncement -----------------------------------------------

    [Fact]
    public void Announcement_speaks_identity_fir_hazard_and_extent()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture());
        Assert.Equal("MHTG SIGMET J5, Central American FIR, embedded thunderstorms, tops FL520",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(advisories[0]));
        Assert.Equal("YMMM SIGMET T07, Melbourne FIR, severe turbulence, surface to 8,000 feet",
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

    [Fact]
    public void Announcement_appends_the_location_phrase()
    {
        var a = ActiveSkyFormatting.ParseRouteAdvisories(OneBlock)[0];
        Assert.Equal("CONVECTIVE SIGMET 18E, thunderstorms, 123 nautical miles ahead",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(a, "123 nautical miles ahead"));
        Assert.Equal("CONVECTIVE SIGMET 18E, thunderstorms",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(a));                 // unchanged default
    }

    [Fact]
    public void Raw_key_fallback_also_carries_the_location()
    {
        var a = ActiveSkyFormatting.ParseRouteAdvisories("No flight plan is currently loaded")[0];
        Assert.Equal("No flight plan is currently loaded, at your position",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(a, "at your position"));
    }

    // --- BuildRouteAdvisoriesText ---------------------------------------------------------

    [Fact]
    public void Empty_list_reads_no_advisories()
        => Assert.Equal("No advisories on route.",
            ActiveSkyFormatting.BuildRouteAdvisoriesText(
                ActiveSkyFormatting.ParseRouteAdvisories(NoHit), decode: false));

    [Fact]
    public void Blocks_render_with_blank_separator_rows()
    {
        string text = ActiveSkyFormatting.BuildRouteAdvisoriesText(
            ActiveSkyFormatting.ParseRouteAdvisories(TwoBlocks), decode: false);
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

    [Fact]
    public void Location_line_renders_in_raw_and_decoded_modes()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(OneBlock);
        var locations = new Dictionary<string, string> { ["CONVECTIVE SIGMET 18E"] = "123 nm ahead" };

        string raw = ActiveSkyFormatting.BuildRouteAdvisoriesText(advisories, decode: false, locations);
        Assert.EndsWith("Location: 123 nm ahead", raw);
        Assert.StartsWith("CONVECTIVE SIGMET 18E", raw);          // verbatim block untouched above

        string decoded = ActiveSkyFormatting.BuildRouteAdvisoriesText(advisories, decode: true, locations);
        Assert.EndsWith("Location: 123 nm ahead", decoded);
    }

    [Fact]
    public void Missing_location_key_renders_exactly_as_today()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(OneBlock);
        Assert.Equal(
            ActiveSkyFormatting.BuildRouteAdvisoriesText(advisories, decode: false),
            ActiveSkyFormatting.BuildRouteAdvisoriesText(advisories, decode: false,
                new Dictionary<string, string>()));
    }

    // --- Decoded vs raw rendering (decode checkbox, design §3.3) ----------------------

    [Fact]
    public void Decoded_rendering_shows_summary_and_validity()
    {
        string text = ActiveSkyFormatting.BuildRouteAdvisoriesText(
            ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture()), decode: true);
        string[] rows = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Assert.Equal(new[]
        {
            "MHTG SIGMET J5: Central American FIR, embedded thunderstorms, observed at 1830Z, tops FL520, moving west at 5 knots, no change expected.",
            "Valid until 2200Z.",
            "",
            "YMMM SIGMET T07: Melbourne FIR, severe turbulence, forecast, surface to 8,000 feet, stationary, no change expected.",
            "Valid until 2300Z.",
        }, rows);
    }

    [Fact]
    public void Raw_rendering_keeps_original_lines_with_blank_separators()
    {
        string text = ActiveSkyFormatting.BuildRouteAdvisoriesText(
            ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture()), decode: false);
        string[] rows = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Assert.Equal(new[]
        {
            "MHTG SIGMET J5 EMBD TS",
            "Valid until: 2200z",
            LiveMhtgBody,
            "",
            "YMMM SIGMET T07 TURB",
            "Valid until: 2300z",
            LiveYmmmBody,
        }, rows);
    }

    [Fact]
    public void Undecodable_block_renders_verbatim_even_when_decoding()
    {
        string text = ActiveSkyFormatting.BuildRouteAdvisoriesText(
            ActiveSkyFormatting.ParseRouteAdvisories("No flight plan is currently loaded"),
            decode: true);
        Assert.Equal("No flight plan is currently loaded", text);
    }

    [Fact]
    public void Tropical_cyclone_hazard_decodes()
    {
        var a = Assert.Single(ActiveSkyFormatting.ParseRouteAdvisories(
            "NFFF SIGMET 3 TC\r\nValid until: 1800z\r\nNFFF NADI FIR TC MAWAR OBS AT 1200Z TOP FL540 MOV NW 10KT INTSF="));
        Assert.Equal("tropical cyclone", a.Hazard);
        Assert.Equal("intensifying", a.Trend);
        Assert.Equal("Nadi FIR", a.FirName);
    }

    [Fact]
    public void Multi_word_fir_name_is_title_cased()
    {
        var a = Assert.Single(ActiveSkyFormatting.ParseRouteAdvisories(
            "NZZO SIGMET 2 SEV TURB\r\nValid until: 0600z\r\n"
            + "NZZO AUCKLAND OCEANIC FIR SEV TURB FCST WI S3000 W16000 - S3200 W15800 FL280/380 MOV E 20KT WKN="));
        Assert.Equal("Auckland Oceanic FIR", a.FirName);
        Assert.Equal("NZZO SIGMET 2, Auckland Oceanic FIR, severe turbulence, FL280 to FL380",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(a));
    }

    // Live capture 2026-07-13 (route endpoint, KMIA→KJFK plan): the first US convective
    // advisory observed on the ROUTE variant — same 3-line/single-CRLF/no-blank-line shape
    // as the ICAO capture, repeated once per route-segment intersection (5×), and a
    // wind-style movement group ("MOV FROM 27010KT") instead of the compass form.
    private const string LiveConvectiveBody =
        "AREA TS MOV FROM 27010KT. TOPS ABV FL450. REF INTL SIGMET ECHO SERIES. OUTLOOK VALID 131755-132155 FROM 40ENE CVG-RIC-180ESE ECG-150SSE ILM-140SE MIA-EYW-100WSW PIE-170SE LEV-70WNW BNA-40ENE CVG WST ISSUANCES EXPD. REFER TO MOST RECENT ACUS01 KWNS FROM STORM PREDICTION CENTER FOR SYNOPSIS AND METEOROLOGICAL DETAILS.";
    private const string LiveConvectiveBlock =
        "CONVECTIVE SIGMET 54E\r\nValid until: 1700z\r\n" + LiveConvectiveBody + "\r\n";

    [Fact]
    public void Live_route_convective_capture_dedups_and_decodes()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            string.Concat(Enumerable.Repeat(LiveConvectiveBlock, 5)));
        var a = Assert.Single(advisories);
        Assert.Equal("CONVECTIVE SIGMET 54E", a.Key);
        Assert.Equal("CONVECTIVE SIGMET 54E", a.Identity);
        Assert.Equal("thunderstorms", a.Hazard);
        Assert.Equal("tops above FL450", a.VerticalExtent);
        Assert.Equal("moving from 270 degrees at 10 knots", a.Movement);
        Assert.Equal("1700Z", a.ValidUntil);
        Assert.Equal("CONVECTIVE SIGMET 54E, thunderstorms, tops above FL450",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(a));
    }

    [Fact]
    public void Mixed_feet_flightlevel_extent_decodes()
    {
        // Live capture 2026-07-13 (route endpoint, Melbourne FIR mountain-wave SIGMET):
        // the vertical extent arrives as "3000FT/FL300" — feet floor, flight-level top.
        var a = Assert.Single(ActiveSkyFormatting.ParseRouteAdvisories(
            "YMMM SIGMET B10 TURB\r\nValid until: 1800z\r\n"
            + "YMMM MELBOURNE FIR SEV MTW FCST WI S3620 E14750 - S3420 E15000 - S3420 E15110 - S3630 E15010 - S3820 E14550 - S3730 E14520 3000FT/FL300 STNR NC="));
        Assert.Equal("severe mountain waves", a.Hazard);
        Assert.Equal("3,000 feet to FL300", a.VerticalExtent);
        Assert.Equal("stationary", a.Movement);
        Assert.Equal("no change expected", a.Trend);
    }

    [Fact]
    public void Body_without_fir_declaration_has_null_firname()
    {
        var a = Assert.Single(ActiveSkyFormatting.ParseRouteAdvisories(OneBlock));
        Assert.Null(a.FirName);
    }

    [Fact]
    public void Out_of_vocabulary_hazard_renders_verbatim_even_when_decoding()
    {
        // GR (hail alone) is not in the vocabulary: extent/movement/trend decode but
        // Hazard stays null, and the summary path must NOT hide the phenomenon —
        // the block renders verbatim despite decode:true.
        const string raw = "LFFF SIGMET 9 GR\r\nValid until: 1900z\r\nLFFF PARIS FIR GR OBS AT 1400Z TOP FL300 MOV E 10KT NC=";
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(raw);
        var a = Assert.Single(advisories);
        Assert.Null(a.Hazard);
        Assert.Equal("tops FL300", a.VerticalExtent);   // fields decoded…
        string text = ActiveSkyFormatting.BuildRouteAdvisoriesText(advisories, decode: true);
        Assert.Equal(new[] { "LFFF SIGMET 9 GR", "Valid until: 1900z",
            "LFFF PARIS FIR GR OBS AT 1400Z TOP FL300 MOV E 10KT NC=" },
            text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));  // …but render is verbatim
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
