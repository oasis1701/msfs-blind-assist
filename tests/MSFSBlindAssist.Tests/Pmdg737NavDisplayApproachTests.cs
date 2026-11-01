// The ND's APP and VOR modes: an optional part of the layout (PMDG draws the ILS/VOR block and the course
// pointer with its deviation bar in APP or VOR, the glideslope in APP; -800, 2026-10-28). A layout file without
// the part keeps MAP and PLN whole: the part is null with its reason, and the APP and VOR lists say the data is
// not read. These tests check the reading, the rows and the messages on the golden layout.

using System.Globalization;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;
using M = MSFSBlindAssist.Tests.NdTestMemory;
using T = MSFSBlindAssist.Services.PMDG.Pmdg737NavDisplayText;

namespace MSFSBlindAssist.Tests;

public class Pmdg737NavDisplayApproachTests
{
    private static readonly Pmdg737NavDisplayLayout L = M.Layout;

    /// <summary>A layout whose APP/VOR part did not derive.</summary>
    private static Pmdg737NavDisplayLayout Broken { get; } = L with
    {
        Approach = null,
        ApproachFailure = "APP/VOR: draw() draws CSymbolGenerator_ND::drawReferenceILSVORData() in no mode, not in two",
    };

    private static M InMode(int mode)
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Mode), mode);
        return m;
    }

    // ---- the part left out ----

    [Fact]
    public void A_broken_approach_part_leaves_map_and_pln_whole()
    {
        // The rows: with the part left out, MAP and PLN are unchanged.
        var modes = L.Geometry.Modes;
        foreach (int mode in new[] { modes.Map, modes.Plan })
        {
            var m = InMode(mode);
            Assert.Equal(T.Lines(m.Read(), L), T.Lines(m.Read(Broken), Broken));
        }

        var app = T.Lines(InMode(modes.App).Read(Broken), Broken).ToList();
        Assert.Contains("ILS data: not read: PMDG file not recognised", app);
        Assert.True(app.IndexOf("ILS data: not read: PMDG file not recognised") < app.IndexOf("Message: TCAS OFF"));
        Assert.Contains("VOR data: not read: PMDG file not recognised", T.Lines(InMode(modes.Vor).Read(Broken), Broken));
    }

    [Fact]
    public void Describe_names_an_underived_approach_part()
    {
        Assert.Contains(Pmdg737LayoutDescriptions.Describe(Broken), l => l.StartsWith("  approach: not derived: ", StringComparison.Ordinal));
        Assert.Contains(Pmdg737LayoutDescriptions.Describe(L), l => l.StartsWith("  approach: VorFlag 97, AppFlag 98, Block { NavIndex 92448/4,", StringComparison.Ordinal));
    }

    [Fact]
    public void An_underived_approach_part_is_another_layout()
    {
        Assert.NotEqual(L, Broken);
    }

    // ---- the sample and the rows ----

    [Fact]
    public void Approach_rows_follow_the_sample_s_mode()
    {
        var modes = L.Geometry.Modes;
        var map = InMode(modes.Map).Read();
        Assert.Null(map.Approach);
        Assert.DoesNotContain(T.Lines(map, L), r => r.StartsWith("Centre:", StringComparison.Ordinal) || r.Contains("data: not read", StringComparison.Ordinal));
        Assert.Null(InMode(modes.Plan).Read().Approach);

        foreach (int mode in new[] { modes.App, modes.Vor })
        {
            var s = InMode(mode).Read();
            Assert.NotNull(s.Approach);
            Assert.DoesNotContain(T.Lines(s, L), r => r.Contains("data: not read", StringComparison.Ordinal));
        }
    }

    // ---- the ILS/VOR block ----

    private static NdApproachBlockFields K => L.Approach!.Block;

    /// <summary>Tunes the receiver the Captain's block shows: its index, frequency (10 kHz units), ident and
    /// frequency text.</summary>
    private static void Tune(M m, int nav, int frequency, string ident = "", string frequencyText = "")
    {
        m.I(M.At(M.Efis, K.NavIndex), nav);
        uint station = M.Sim + (uint)(K.Stations + nav * K.StationStride);
        m.I(station + (uint)K.Frequency, frequency);
        m.Text(station + (uint)K.Ident, ident);
        m.Text(station + (uint)K.FrequencyText, frequencyText);
    }

    private static void ApproachIdent(M m, int record, string ident) =>
        m.Text(M.Glass + (uint)(K.ApproachIdents + record * K.ApproachIdentStride), ident);

    private static void CourseAndDme(M m, int nav, double course, double dme)
    {
        m.D(M.At(M.Glass, K.Course, nav), course);
        m.D(M.At(M.Glass, K.Dme, nav), dme);
    }

    private static string TopRight(M m) => Assert.Single(T.Lines(m.Read(), L), r => r.StartsWith("Top right:", StringComparison.Ordinal));

    [Fact]
    public void Top_right_in_app_is_pmdg_s_block()
    {
        var m = InMode(L.Geometry.Modes.App);
        Tune(m, 0, 11030);
        ApproachIdent(m, 0, "IATL");
        // PMDG prints the course truncated, not rounded.
        CourseAndDme(m, 0, 268.7, 12.34);
        var rows = T.Lines(m.Read(), L).ToList();
        Assert.Equal("Top right: ILS 1, IATL, CRS 268, DME 12.3", rows[3]);
        Assert.Equal("Top centre: HDG 079 MAG", rows[2]);
    }

    [Fact]
    public void App_and_vor_are_heading_up()
    {
        // PMDG sets the track-up flag only in MAP: APP and VOR are heading-up whatever the option. A crab
        // (heading 075, track 079.4): the top centre prints the heading, and the course 079 points 4° right of it.
        foreach (int mode in new[] { L.Geometry.Modes.App, L.Geometry.Modes.Vor })
        {
            var m = InMode(mode);
            m.D(M.At(M.Glass, L.Orientation.Heading), 75);
            PointerCourse(m, 79.6);
            var rows = T.Lines(m.Read(), L).ToList();
            Assert.Equal("Top centre: HDG 075 MAG", rows[2]);
            Assert.Contains("Centre: course pointer 079, 4° right", rows);
        }
    }

    [Fact]
    public void The_heading_bug_in_app_and_vor()
    {
        // The ND draws the heading bug in every mode but PLN, on the compass, while the readout is valid.
        foreach (int mode in new[] { L.Geometry.Modes.App, L.Geometry.Modes.Vor })
        {
            var m = InMode(mode);
            m.I(M.At(M.Glass, L.Symbols.SelectedHeading), 109);
            Assert.Equal("Centre: heading bug, 30° right", Centre(m, "heading bug"));
            m.B(M.At(M.Panel, L.Panel.Centered), true);
            Assert.Equal("Centre: heading bug, 30° right", Centre(m, "heading bug"));
            m.B(M.At(M.Glass, L.Orientation.ReadoutValid), false);
            Assert.Null(Centre(m, "heading bug"));
        }
        Assert.DoesNotContain(T.Lines(InMode(L.Geometry.Modes.Plan).Read(), L), r => r.Contains("heading bug", StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_received_prints_pmdg_s_dashes()
    {
        // In APP a VOR frequency (113.20) is no ILS: PMDG prints its dashes; the DME none (" ---").
        var m = InMode(L.Geometry.Modes.App);
        Tune(m, 0, 11320, "CAG");
        ApproachIdent(m, 0, "IATL");
        CourseAndDme(m, 0, 268, -1);
        Assert.Equal("Top right: ILS 1, ------, CRS 268, DME ---", TopRight(m));
    }

    [Fact]
    public void A_dme_that_is_not_a_number_prints_pmdg_s_dashes()
    {
        // PMDG prints the DME's number only while it is 0 or more; NaN fails that test and prints " ---", so it
        // is no corrupt reading (WASM-14). An infinite DME passes it and would be printed: corrupt.
        var m = InMode(L.Geometry.Modes.App);
        Tune(m, 0, 11320, "CAG");
        CourseAndDme(m, 0, 268, double.NaN);
        Assert.Equal("Top right: ILS 1, ------, CRS 268, DME ---", TopRight(m));
        CourseAndDme(m, 0, 268, double.PositiveInfinity);
        using var sampler = m.Sampler();
        Assert.False(sampler.TryRead(out _, out var corrupt));
        Assert.Contains("DME", corrupt, StringComparison.Ordinal);
    }

    [Fact]
    public void Vor_mode_prints_ident_course_dme_and_to_or_from()
    {
        var m = InMode(L.Geometry.Modes.Vor);
        Tune(m, 0, 11320, "CAG", "113.20");
        CourseAndDme(m, 0, 90, 23.44);
        uint toFrom = M.At(M.Glass, K.ToFrom, 0);
        m.I(toFrom, K.ToValue);
        var rows = T.Lines(m.Read(), L).ToList();
        Assert.Equal("Top right: VOR 1, CAG, CRS 090, DME 23.4", rows[3]);
        // TO/FROM sits below the course pointer, last of the centre's rows.
        Assert.Equal("Centre: TO", rows.Last(r => r.StartsWith("Centre:", StringComparison.Ordinal)));
        Assert.True(rows.IndexOf("Centre: TO") > rows.FindIndex(r => r.StartsWith("Centre: course pointer", StringComparison.Ordinal)));

        static bool ToOrFrom(string r) => r is "Centre: TO" or "Centre: FROM";
        m.I(toFrom, K.FromValue);
        Assert.Contains("Centre: FROM", T.Lines(m.Read(), L));
        m.I(toFrom, 0);
        Assert.DoesNotContain(T.Lines(m.Read(), L), ToOrFrom);
        m.I(toFrom, K.ToValue);
        m.B(M.At(M.Glass, K.VorFailed, 0), true);
        Assert.DoesNotContain(T.Lines(m.Read(), L), ToOrFrom);
    }

    [Fact]
    public void Vor_mode_without_an_ident_prints_the_frequency_and_with_an_ils_dashes()
    {
        var m = InMode(L.Geometry.Modes.Vor);
        Tune(m, 0, 11320, "", "113.20");
        Assert.StartsWith("Top right: VOR 1, 113.20, ", TopRight(m));
        Tune(m, 0, 11030, "IATL", "110.30");
        Assert.StartsWith("Top right: VOR 1, ------, ", TopRight(m));
    }

    [Fact]
    public void An_fmc_source_shows_the_fmc_label_and_its_record()
    {
        var m = InMode(L.Geometry.Modes.App);
        Tune(m, 0, 11320);
        m.I(M.At(M.Glass, K.FmcSources[1], 0), K.FmcSourceValue);
        m.I(M.At(M.Efis, K.FmcIndex, 0), 1);
        ApproachIdent(m, 1, "R08L");
        CourseAndDme(m, 0, 84, 7);
        Assert.Equal("Top right: FMC L, R08L, CRS 084, DME 7.0", TopRight(m));
    }

    [Fact]
    public void The_second_receiver_and_a_failed_dme()
    {
        var m = InMode(L.Geometry.Modes.App);
        Tune(m, 1, 10950);
        ApproachIdent(m, 1, "IMLE");
        CourseAndDme(m, 1, 300, 5);
        m.B(M.At(M.Glass, K.DmeFailed, 1), true);
        Assert.Equal("Top right: ILS 2, IMLE, CRS 300, DME 2 (amber, boxed)", TopRight(m));
    }

    [Fact]
    public void Dme_from_100_nm_is_whole_miles()
    {
        var m = InMode(L.Geometry.Modes.Vor);
        Tune(m, 0, 11320, "CAG");
        CourseAndDme(m, 0, 90, 99.4);
        Assert.EndsWith(", DME 99.4", TopRight(m));
        CourseAndDme(m, 0, 90, 99.6);
        Assert.EndsWith(", DME 100", TopRight(m));
    }

    [Fact]
    public void An_inboard_map_page_has_no_course_and_the_short_failed_dme()
    {
        var m = InMode(L.Geometry.Modes.App);
        var u = L.Units;
        m.I(M.Efis + (uint)u.PageOffset, u.MapPages[0]);
        Tune(m, 0, 11030);
        ApproachIdent(m, 0, "IATL");
        CourseAndDme(m, 0, 268, 12.34);
        Assert.Equal("Top right: ILS 1, IATL, DME 12.3", TopRight(m));
        m.B(M.At(M.Glass, K.DmeFailed, 0), true);
        Assert.Equal("Top right: ILS 1, IATL, DME (amber, boxed)", TopRight(m));
    }

    [Fact]
    public void Reads_the_block_only_in_app_or_vor_and_each_text_only_where_printed()
    {
        // A NAV index out of range: corrupt in APP, never read in MAP.
        var m = InMode(L.Geometry.Modes.Map);
        Tune(m, 0, 11030);
        m.I(M.At(M.Efis, K.NavIndex), 99);
        Assert.Null(m.Read().Approach);
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.App);
        using (var sampler = m.Sampler())
        {
            Assert.False(sampler.TryRead(out _, out var corrupt));
            Assert.Contains("NAV", corrupt);
        }

        // An unprintable APP ident: printed (so read) only in APP with an ILS frequency or an FMC source.
        m = InMode(L.Geometry.Modes.Vor);
        Tune(m, 0, 11030);
        ApproachIdent(m, 0, "I\u0001TL");
        Assert.Null(m.Read().Approach!.ApproachIdent);
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.App);
        using (var sampler = m.Sampler())
        {
            Assert.False(sampler.TryRead(out _, out var corrupt));
            Assert.Contains("ident", corrupt);
        }
        Tune(m, 0, 11320);
        Assert.Null(m.Read().Approach!.ApproachIdent);
    }

    // ---- the course pointer and the deviation bar ----

    private static NdCdiFields C => L.Approach!.Cdi;

    private static void PointerCourse(M m, double course) => m.D(M.At(M.Glass, C.PointerCourse, 0), course);

    /// <summary>The receiver receiving (its state at PMDG's lowest drawn one) and the mode's deviation.</summary>
    private static void Deviation(M m, bool app, int record, double deviation, int nav = 0)
    {
        m.I(M.At(M.Glass, C.ReceiverState, nav), C.ReceiverStateFrom);
        m.D(M.At(M.Glass, app ? C.ApproachDeviation : C.VorDeviation, record), deviation);
    }

    private static string? Centre(M m, string what) =>
        T.Lines(m.Read(), L).SingleOrDefault(r => r.StartsWith("Centre: " + what, StringComparison.Ordinal));

    [Fact]
    public void Course_pointer_row()
    {
        // The heading 079.4 is up (APP is heading-up): a course of 079 (PMDG truncates 79.6) points ahead, 081
        // 2° right of the top.
        var m = InMode(L.Geometry.Modes.App);
        PointerCourse(m, 79.6);
        Assert.Equal("Centre: course pointer 079, ahead", Centre(m, "course pointer"));
        PointerCourse(m, 81);
        Assert.Equal("Centre: course pointer 081, 2° right", Centre(m, "course pointer"));
        PointerCourse(m, 268);
        Assert.Equal("Centre: course pointer 268, 171° left", Centre(m, "course pointer"));
        // No course pointer or bar while the readout is invalid; the track line needs the track alone
        // (straight up without the readout).
        m.B(M.At(M.Glass, L.Orientation.ReadoutValid), false);
        Assert.Equal("Centre: track line, straight ahead, marks at 5, 10 and 15 NM", Centre(m, ""));
    }

    [Fact]
    public void Deviation_bar_rows()
    {
        var m = InMode(L.Geometry.Modes.App);
        Deviation(m, app: true, 0, -0.3);
        Assert.Equal("Centre: deviation bar 0.6 dots left, filled", Centre(m, "deviation bar"));
        Deviation(m, app: true, 0, 0.5);
        Assert.Equal("Centre: deviation bar 1.0 dots right, filled", Centre(m, "deviation bar"));
        // VOR mode: the VOR deviation of the receiver; PMDG draws the bar's outline there.
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Vor);
        Deviation(m, app: false, 0, 0.25);
        Assert.Equal("Centre: deviation bar 0.5 dots right, outlined", Centre(m, "deviation bar"));
    }

    [Fact]
    public void Deviation_words_at_zero_and_full_scale()
    {
        var m = InMode(L.Geometry.Modes.App);
        Deviation(m, app: true, 0, 0);
        Assert.Equal("Centre: deviation bar centred, filled", Centre(m, "deviation bar"));
        // -0.04 dots rounds to no tenth: centred, never "-0.0".
        Deviation(m, app: true, 0, -0.02);
        Assert.Equal("Centre: deviation bar centred, filled", Centre(m, "deviation bar"));
        // At and past PMDG's clamp (±1.25, 2.5 dots) the bar is pinned, and outlined (filled only below 1.18, 2.36 dots).
        Deviation(m, app: true, 0, 1.25);
        Assert.Equal("Centre: deviation bar at full scale, right, outlined", Centre(m, "deviation bar"));
        Deviation(m, app: true, 0, -2);
        Assert.Equal("Centre: deviation bar at full scale, left, outlined", Centre(m, "deviation bar"));
        Deviation(m, app: true, 0, 1.2);
        Assert.Equal("Centre: deviation bar 2.4 dots right, outlined", Centre(m, "deviation bar"));
        Deviation(m, app: true, 0, 1.17);
        Assert.Equal("Centre: deviation bar 2.3 dots right, filled", Centre(m, "deviation bar"));
    }

    [Fact]
    public void No_deviation_row_while_pmdg_draws_no_bar()
    {
        var m = InMode(L.Geometry.Modes.App);
        Deviation(m, app: true, 0, 0.3);
        Assert.NotNull(Centre(m, "deviation bar"));
        m.I(M.At(M.Glass, C.ReceiverState, 0), C.ReceiverStateFrom - 1);
        Assert.Null(Centre(m, "deviation bar"));
        m.I(M.At(M.Glass, C.ReceiverState, 0), C.ReceiverStateFrom);
        m.B(M.At(M.Glass, K.VorFailed, 0), true);
        Assert.Null(Centre(m, "deviation bar"));
        // With the FMC source the VOR fail flag no longer hides the bar.
        m.I(M.At(M.Glass, C.FmcSource, 0), K.FmcSourceValue);
        Assert.NotNull(Centre(m, "deviation bar"));
    }

    [Fact]
    public void An_fmc_source_takes_the_deviation_of_its_record()
    {
        var m = InMode(L.Geometry.Modes.App);
        m.I(M.At(M.Glass, C.FmcSource, 0), K.FmcSourceValue);
        m.I(M.At(M.Efis, K.FmcIndex, 0), 1);
        Deviation(m, app: true, 0, -1);
        Deviation(m, app: true, 1, 0.4);
        Assert.Equal("Centre: deviation bar 0.8 dots right, filled", Centre(m, "deviation bar"));
    }

    // ---- the glideslope ----

    private static NdGlideslopeFields Gs => L.Approach!.Glideslope;

    /// <summary>The receiver receiving the glideslope (its state the one PMDG draws the pointer in) at a deviation.</summary>
    private static void GlideslopeAt(M m, double deviation, int? state = null)
    {
        m.I(M.At(M.Glass, C.ReceiverState, 0), state ?? Gs.PointerState);
        m.D(M.At(M.Glass, Gs.Deviation, 0), deviation);
    }

    private static string? RightSide(M m) =>
        T.Lines(m.Read(), L).SingleOrDefault(r => r.StartsWith("Right side:", StringComparison.Ordinal));

    [Fact]
    public void Glideslope_rows()
    {
        var m = InMode(L.Geometry.Modes.App);
        // PMDG draws the pointer (d + d) × the dot spacing down the screen: plus is below.
        GlideslopeAt(m, 0.6);
        Assert.Equal("Right side: glideslope pointer 1.2 dots below", RightSide(m));
        var rows = T.Lines(m.Read(), L).ToList();
        // After the compass's rows: the course pointer, the deviation bar, the heading bug (selected 080) and its line.
        Assert.Equal(rows.FindIndex(r => r.StartsWith("Centre: dashed line to the heading bug", StringComparison.Ordinal)) + 1, rows.IndexOf("Right side: glideslope pointer 1.2 dots below"));
        GlideslopeAt(m, -0.6);
        Assert.Equal("Right side: glideslope pointer 1.2 dots above", RightSide(m));
        GlideslopeAt(m, 0);
        Assert.Equal("Right side: glideslope pointer centred", RightSide(m));
        GlideslopeAt(m, 1.25);
        Assert.Equal("Right side: glideslope pointer at full scale, below", RightSide(m));
        GlideslopeAt(m, -3);
        Assert.Equal("Right side: glideslope pointer at full scale, above", RightSide(m));
        // The scale with no pointer: the receiver not in the pointer's state.
        GlideslopeAt(m, 0.6, state: C.ReceiverStateFrom);
        Assert.Equal("Right side: glideslope scale, no pointer", RightSide(m));
    }

    [Fact]
    public void Glideslope_flag_row()
    {
        var m = InMode(L.Geometry.Modes.App);
        GlideslopeAt(m, 0.6);
        m.B(M.At(M.Glass, Gs.Failed, 0), true);
        Assert.Equal("Right side: G/S (amber, boxed)", RightSide(m));
        // With FMC source B PMDG draws the scale whatever the fail flag.
        m.I(M.At(M.Glass, Gs.FmcSource, 0), K.FmcSourceValue);
        Assert.Equal("Right side: glideslope pointer 1.2 dots below", RightSide(m));
    }

    [Fact]
    public void No_glideslope_in_vor_mode()
    {
        var m = InMode(L.Geometry.Modes.Vor);
        GlideslopeAt(m, 0.6);
        Assert.Null(RightSide(m));
        // Not read where PMDG does not draw it.
        Assert.Equal(0, m.Read().Approach!.Glideslope);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Approach_rows_are_invariant_under_comma_cultures(string culture)
    {
        var m = InMode(L.Geometry.Modes.App);
        Tune(m, 0, 11030);
        ApproachIdent(m, 0, "IATL");
        CourseAndDme(m, 0, 268.7, 12.34);
        PointerCourse(m, 268.7);
        Deviation(m, app: true, 0, -0.3);
        GlideslopeAt(m, 0.6);
        var invariant = T.Lines(m.Read(), L);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(invariant, T.Lines(m.Read(), L));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
        Assert.Contains("Top right: ILS 1, IATL, CRS 268, DME 12.3", invariant);
        Assert.Contains("Centre: deviation bar 0.6 dots left, filled", invariant);
        Assert.Contains("Right side: glideslope pointer 1.2 dots below", invariant);
    }

    // ---- the messages and traffic (the failure flags, the ND's traffic gate) ----

    [Fact]
    public void Efis_mode_nav_freq_disagree_in_app_with_a_vor_tuned()
    {
        var m = InMode(L.Geometry.Modes.App);
        Tune(m, 0, 11030);
        var before = m.Read();
        Assert.DoesNotContain("Message: EFIS MODE/NAV FREQ DISAGREE", T.Lines(before, L));
        Tune(m, 0, 11320);
        var after = m.Read();
        Assert.Contains("Message: EFIS MODE/NAV FREQ DISAGREE", T.Lines(after, L));
        Assert.Contains("ND message: EFIS MODE/NAV FREQ DISAGREE", Pmdg737NavDisplaySpeech.Changes(before, after, L));
        // VOR mode the other way round: an ILS frequency disagrees.
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Vor);
        Assert.DoesNotContain("Message: EFIS MODE/NAV FREQ DISAGREE", T.Lines(m.Read(), L));
        Tune(m, 0, 11030);
        Assert.Contains("Message: EFIS MODE/NAV FREQ DISAGREE", T.Lines(m.Read(), L));
    }

    [Fact]
    public void A_failed_localizer_or_vor_is_a_message()
    {
        var m = InMode(L.Geometry.Modes.App);
        Tune(m, 0, 11030);
        m.B(M.At(M.Glass, L.Approach!.Messages.LocFlag.Failed, 0), true);
        Assert.Contains("Message: LOC 1", T.Lines(m.Read(), L));
        // The inboard map page (half format) prints the short text.
        m.I(M.Efis + (uint)L.Units.PageOffset, L.Units.MapPages[0]);
        Assert.Contains("Message: LOC", T.Lines(m.Read(), L));

        m = InMode(L.Geometry.Modes.Vor);
        Tune(m, 1, 11320, "CAG");
        m.B(M.At(M.Glass, K.VorFailed, 1), true);
        Assert.Contains("Message: VOR 2", T.Lines(m.Read(), L));
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.App);
        Assert.DoesNotContain(T.Lines(m.Read(), L), r => r.StartsWith("Message: VOR", StringComparison.Ordinal));
    }

    [Fact]
    public void The_glideslope_flag_is_spoken_and_listed_once_at_its_place()
    {
        var m = InMode(L.Geometry.Modes.App);
        Tune(m, 0, 11030);
        var before = m.Read();
        m.B(M.At(M.Glass, Gs.Failed, 0), true);
        var after = m.Read();
        var rows = T.Lines(after, L);
        Assert.Contains("Right side: G/S (amber, boxed)", rows);
        Assert.DoesNotContain("Message: G/S", rows);
        Assert.Contains("G/S", T.Messages(after, L));
        Assert.Contains("ND message: G/S", Pmdg737NavDisplaySpeech.Changes(before, after, L));
    }

    [Fact]
    public void Traffic_rows_in_app_and_vor()
    {
        var m = InMode(L.Geometry.Modes.App);
        var t = L.Traffic;
        m.U(M.Tcas + (uint)t.List, M.TrafficList);
        m.B(M.TrafficList + (uint)t.ListShown, true);
        m.B(M.At(M.Tcas, t.Powered), true);
        m.Bytes[M.TrafficList + (uint)t.Count] = 1;
        uint target = M.TrafficList + 0x1000;
        m.U(M.TrafficList + (uint)t.Targets, target);
        m.F(target + (uint)t.RelativeAltitude, 1200);
        m.F(target + (uint)t.Range, 6.2f);
        m.F(target + (uint)t.Bearing, (float)(30 * Math.PI / 180));
        m.Bytes[target + (uint)t.ThreatAndTrend] = (byte)((2 << t.ThreatShift) | 1);
        m.B(target + (uint)t.Present, true);

        Assert.Contains("Centre: traffic advisory, 6.2 NM, 30° right, +12 climbing", T.Lines(m.Read(), L));
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Vor);
        Assert.Contains("Centre: traffic advisory, 6.2 NM, 30° right, +12 climbing", T.Lines(m.Read(), L));
        // The ND's traffic gate: not centred.
        m.B(M.At(M.Panel, L.Panel.Centered), true);
        Assert.DoesNotContain(T.Lines(m.Read(), L), r => r.Contains("traffic advisory", StringComparison.Ordinal));
    }

    [Fact]
    public void The_session_logs_an_underived_approach_part()
    {
        var log = new List<string>();
        ModuleSession.Analyse(new[] { "nd" }, new FakeLayoutSource(f => new LayoutLookup(Pmdg737TestBuilds.Of(f, nd: Broken), null)), log.Add);
        Assert.Contains(log, l => l.StartsWith("file analysis: nd: navigation display APP/VOR not recognised: ", StringComparison.Ordinal)
            && l.Contains("drawReferenceILSVORData", StringComparison.Ordinal));
    }
}
