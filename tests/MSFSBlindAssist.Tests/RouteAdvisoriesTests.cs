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
