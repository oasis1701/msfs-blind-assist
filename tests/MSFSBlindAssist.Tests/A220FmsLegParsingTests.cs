using MSFSBlindAssist.Aircraft.A220;
using static MSFSBlindAssist.Aircraft.A220.A220FmsLegParsing;
using Tok = MSFSBlindAssist.Aircraft.A220.A220FmsScreenParsing.WinToken;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the ROUTE ▸ LEGS grouping against a slice of the LIVE scrape captured
/// 2026-07-29 (Synaptic A220, LCLK→LGAV, SEC LEGS page). The bug this guards
/// against: every leg fragment ("266° 29.9", "GENOS", "2.00", "/-----") reading
/// as its own separate row, which is what made the flight-plan page unusable.
/// </summary>
public class A220FmsLegParsingTests
{
    /// <summary>
    /// Verbatim geometry from the live capture: two enroute legs, a leg with an
    /// altitude constraint, a discontinuity triple, the MISSED APPROACH header,
    /// a hold, and the pinned chrome that overlays the list.
    /// </summary>
    private static List<Tok> LegsFixture() => new()
    {
        // page chrome / headers (must never become leg rows)
        new("FMS1", 8, 29, "magenta"),
        new("SEC", 83, 26, "white"),
        new("ROUTE", 639, 26, "white"),
        new("UTC", 180, 157, "white"),
        new("SPD/ALT VPA+RNP", 310, 157, "white"),
        new("RNP AUTO", 488, 235, "gray"),
        // two plain enroute legs
        new("266° 29.9", 10, 468, "white"),
        new("RNP", 578, 468, "gray"),
        new("2.00", 619, 468, "white"),
        new("GENOS", 17, 497, "white"),
        new("/-----", 365, 497, "green"),
        new("301° 59.2", 10, 545, "white"),
        new("RNP", 578, 545, "gray"),
        new("2.00", 619, 545, "white"),
        new("PEDER", 17, 574, "white"),
        new("/-----", 365, 574, "green"),
        // discontinuity triple
        new("THEN", 15, 1553, "white"),
        new("▯▯▯▯▯", 17, 1582, "white"),
        new("DISCONTINUITY", 315, 1583, "white"),
        // leg with a descent constraint and NO track row (follows the discontinuity)
        new("RNP", 578, 1630, "gray"),
        new("1.00", 619, 1630, "white"),
        new("BADEL", 17, 1659, "white"),
        new("↓/6000A", 303, 1659, "green"),
        // missed approach section + a radial leg
        new("MISSED APPROACH", 10, 2597, "gray"),
        new("R172° 33.8", 10, 2715, "white"),
        new("RNP", 578, 2715, "gray"),
        new("1.00", 619, 2715, "white"),
        new("KEA", 17, 2744, "white"),
        new("↑220/5000A", 303, 2744, "green"),
        // hold on the SAME waypoint ident (occurrence must disambiguate the click)
        new("HOLD AT", 10, 2793, "white"),
        new("RNP", 578, 2793, "gray"),
        new("1.00", 619, 2793, "white"),
        new("KEA", 17, 2822, "white"),
        new("↑230/5000A", 303, 2822, "green"),
        // pinned soft keys drawn OVER the list — their y interleaves with the legs
        new("→…", 21, 881, "white"),
        new("HOLD…", 218, 881, "white"),
        new("FIX…", 338, 881, "white"),
        new("ACTIVATE SEC", 541, 881, "white"),
        new("MSG…", 646, 1060, "amber"),
    };

    [Fact]
    public void IsLegsPage_TrueForLegsTable_FalseForAFormPage()
    {
        Assert.True(IsLegsPage(LegsFixture()));
        Assert.False(IsLegsPage(new List<Tok>
        {
            new("ORIGIN", 8, 305, "gray"), new("LCLK", 17, 339, "white"),
            new("DEST", 230, 305, "gray"), new("LGAV", 240, 339, "white"),
        }));
    }

    [Fact]
    public void ParseLegs_GroupsEachLegIntoOneRow()
    {
        var legs = ParseLegs(LegsFixture(), out _);

        var genos = Assert.Single(legs, l => l.Waypoint == "GENOS");
        Assert.Equal("266", genos.Track);
        Assert.Equal("29.9", genos.Distance);
        Assert.Equal("2.00", genos.Rnp);
        Assert.Equal("", genos.Constraint);        // "/-----" is NOT a constraint
        Assert.Equal(LegKind.Leg, genos.Kind);

        // The whole leg is ONE line — the fragments never appear separately.
        Assert.Equal("GENOS, 266 degrees, 29.9 miles, RNP 2.00", Describe(genos));
    }

    [Fact]
    public void ParseLegs_LegAfterDiscontinuityHasNoTrackButKeepsConstraint()
    {
        var legs = ParseLegs(LegsFixture(), out _);
        var badel = Assert.Single(legs, l => l.Waypoint == "BADEL");
        Assert.Equal("", badel.Track);
        Assert.Equal("", badel.Distance);
        Assert.Equal("1.00", badel.Rnp);
        Assert.Equal("BADEL, descend, 6000 or above, RNP 1.00", Describe(badel));
    }

    [Fact]
    public void ParseLegs_EmitsOneDiscontinuityRowAndTheSectionHeader()
    {
        var legs = ParseLegs(LegsFixture(), out _);
        var disco = Assert.Single(legs, l => l.Kind == LegKind.Discontinuity);
        // The row must SAY it is actionable — a blind pilot gets no visual hint.
        Assert.Equal("Flight plan discontinuity, Delete to remove", Describe(disco));
        Assert.Equal(0, disco.DiscoIndex);
        var header = Assert.Single(legs, l => l.Kind == LegKind.Header);
        Assert.Equal("MISSED APPROACH", Describe(header));
        Assert.Equal(-1, header.DiscoIndex);
    }

    [Fact]
    public void ParseLegs_NumbersEachDiscontinuitySoTheRightOneIsDeleted()
    {
        // Three discontinuities (the real LCLK→LGAV route has four) must come back
        // as 0,1,2 — the agent targets the nth DISCONTINUITY marker by that index,
        // so a shared or missing index would delete the wrong part of the route.
        var t = new List<Tok>
        {
            new("266° 29.9", 10, 468, "white"), new("GENOS", 17, 497, "white"),
            new("301° 59.2", 10, 545, "white"), new("PEDER", 17, 574, "white"),
            new("THEN", 15, 700, "white"), new("▯▯▯▯▯", 17, 729, "white"),
            new("DISCONTINUITY", 315, 730, "white"),
            new("THEN", 15, 900, "white"), new("▯▯▯▯▯", 17, 929, "white"),
            new("DISCONTINUITY", 315, 930, "white"),
            new("THEN", 15, 1100, "white"), new("▯▯▯▯▯", 17, 1129, "white"),
            new("DISCONTINUITY", 315, 1130, "white"),
        };
        var legs = ParseLegs(t, out _);
        var discos = legs.Where(l => l.Kind == LegKind.Discontinuity).ToList();
        Assert.Equal(3, discos.Count);
        Assert.Equal(new[] { 0, 1, 2 }, discos.Select(d => d.DiscoIndex));
        // Real waypoints never carry a discontinuity index.
        Assert.All(legs.Where(l => l.Kind == LegKind.Leg), l => Assert.Equal(-1, l.DiscoIndex));
    }

    [Fact]
    public void ParseLegs_RepeatedWaypointGetsDistinctOccurrences()
    {
        var legs = ParseLegs(LegsFixture(), out _);
        var keas = legs.Where(l => l.Waypoint == "KEA").ToList();
        Assert.Equal(2, keas.Count);
        Assert.Equal(new[] { 0, 1 }, keas.Select(k => k.Occurrence));
        // The missed-approach leg is a RADIAL; the second KEA is the hold.
        Assert.Equal("KEA, radial 172 degrees, 33.8 miles, climb, 220 knots, 5000 or above, RNP 1.00",
            Describe(keas[0]));
        Assert.Equal(LegKind.Hold, keas[1].Kind);
        // "Hold at KEA" reads as a phrase — never "Hold at, KEA" with a comma,
        // which a screen reader renders as two disconnected fragments.
        Assert.StartsWith("Hold at KEA,", Describe(keas[1]));
    }

    [Fact]
    public void ParseLegs_DirAnnotationIsALabelRowNeverAClickableWaypoint()
    {
        // Live capture 2026-07-30 (active direct-to): a cyan "(DIR)" annotation
        // row sits above the direct-to leg. It has no fix-symbol icon in the
        // cockpit, so it can never open a revision menu — it must parse as a
        // Header (plain text) row, and the leg below it stays a normal leg.
        var t = new List<Tok>
        {
            new("(DIR)", 17, 264, "cyan"),
            new("261° 55.1", 10, 313, "magenta"),
            new("RNP", 578, 313, "gray"), new("2.00", 619, 313, "white"),
            new("PHA", 17, 342, "white"), new("/-----", 365, 342, "green"),
            new("266° 29.9", 10, 390, "white"),
            new("GENOS", 17, 419, "white"), new("/-----", 365, 419, "green"),
        };
        var legs = ParseLegs(t, out _);
        var dir = Assert.Single(legs, l => l.Kind == LegKind.Header);
        Assert.Equal("Direct to", Describe(dir));
        var pha = Assert.Single(legs, l => l.Waypoint == "PHA");
        Assert.Equal(LegKind.Leg, pha.Kind);
        Assert.Equal("261", pha.Track);
    }

    [Fact]
    public void ParseLegs_NeverTreatsPinnedChromeOrHeadersAsLegs()
    {
        var legs = ParseLegs(LegsFixture(), out _);
        foreach (string junk in new[]
                 { "→…", "HOLD…", "FIX…", "ACTIVATE SEC", "MSG…", "UTC", "RNP AUTO",
                   "SPD/ALT VPA+RNP", "RNP", "THEN", "FMS1", "SEC", "ROUTE" })
            Assert.DoesNotContain(legs, l => l.Waypoint == junk);
    }

    [Fact]
    public void ParseLegs_MarksItsTokensConsumedSoTheGenericPassDropsThem()
    {
        var tokens = LegsFixture();
        ParseLegs(tokens, out var consumed);
        // Every fragment of the GENOS leg is claimed — that is what stops the
        // generic pass re-emitting "266° 29.9" and "2.00" as their own rows.
        foreach (string frag in new[] { "266° 29.9", "GENOS", "2.00", "/-----" })
        {
            int idx = tokens.FindIndex(t => t.Text == frag);
            Assert.True(consumed.Contains(idx), $"expected '{frag}' to be consumed by a leg row");
        }
    }

    [Fact]
    public void ParseFms_OnALegsPage_DoesNotAlsoEmitLegFragmentsAsButtons()
    {
        var m = A220FmsScreenParsing.ParseFms(LegsFixture());
        Assert.NotEmpty(m.Legs);
        foreach (string frag in new[] { "GENOS", "PEDER", "BADEL", "266° 29.9", "2.00" })
            Assert.DoesNotContain(frag, m.Buttons);
        // The pinned soft keys are still reachable.
        Assert.Contains("ACTIVATE SEC", m.Buttons);
    }

    [Fact]
    public void ParseFms_CarriesClosedDropdownFlagOntoItsField()
    {
        // A CLOSED dropdown renders only its current value, so nothing in the text
        // distinguishes it from a data row — the agent marks it structurally (its
        // component holds options + onSelect) and the flag must survive the parse,
        // or the form cannot say "dropdown" and the pilot never learns the choice
        // exists. The plain field beside it must NOT be marked.
        var tokens = new List<Tok>
        {
            new("FLT PHASE", 20, 300, "gray"),
            new("CRUISE", 20, 335, "white", Dropdown: true, Options: 5),
            new("CRZ ALT", 300, 300, "gray"),
            new("FL350", 300, 335, "white"),
        };
        var m = A220FmsScreenParsing.ParseFms(tokens);

        var phase = Assert.Single(m.Fields, f => f.Label == "FLT PHASE");
        Assert.True(phase.IsDropdown);
        Assert.Equal(5, phase.OptionCount);
        Assert.Equal("CRUISE", phase.Value);

        var alt = Assert.Single(m.Fields, f => f.Label == "CRZ ALT");
        Assert.False(alt.IsDropdown);
        Assert.Equal(0, alt.OptionCount);
    }

    [Fact]
    public void DialogChoiceValueFor_NamesEachIdenticalSelectButtonByItsOwnConstraint()
    {
        // The live SELECT CONSTRAINT - VARIX dialog (2026-07-30), raised by
        // deleting a discontinuity between constrained legs: TWO identical
        // "SELECT" buttons, only one with a constraint drawn beside it. Spoken
        // as bare "SELECT" twice, a blind pilot is choosing blind.
        var tokens = new List<Tok>
        {
            new("SELECT CONSTRAINT - VARIX", 295, 784, "gray"),
            new("SELECT", 307, 850, "white"),          // option 1 — nothing on its row
            new("SELECT", 307, 924, "white"),          // option 2 — carries the constraint
            new("↓", 439, 925, "green"),
            new("/8000A", 461, 925, "green"),
            new("SELECTION REQUIRED", 296, 995, "white"),
            new("CNCL", 646, 988, "white"),
        };
        Assert.Null(A220FmsScreenParsing.DialogChoiceValueFor(tokens, "SELECT", 0));
        Assert.Equal("descend, 8000 or above",
            A220FmsScreenParsing.DialogChoiceValueFor(tokens, "SELECT", 1));
        // Out-of-range never throws — the renderer falls back to a position.
        Assert.Null(A220FmsScreenParsing.DialogChoiceValueFor(tokens, "SELECT", 2));
        // The dialog's own dismiss button is not a repeated choice.
        Assert.Null(A220FmsScreenParsing.DialogChoiceValueFor(tokens, "CNCL", 0));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/-----", "")]
    [InlineData("-----", "")]
    [InlineData("↓/6000A", "descend, 6000 or above")]
    [InlineData("↑220/5000A", "climb, 220 knots, 5000 or above")]
    [InlineData("↓/1900B", "descend, 1900 or below")]
    [InlineData("↓/309", "descend, 309")]
    public void FormatConstraint_ExpandsArrowsAndAtOrAboveBelowSuffixes(string raw, string expected)
        => Assert.Equal(expected, FormatConstraint(raw));

    // ---- AlignLegIndices — content pairing, never a positional zip ----------

    private static FmsLeg L(string wpt, LegKind kind = LegKind.Leg)
        => new() { Kind = kind, Waypoint = wpt };
    private static FiberLegRow F(int idx, string id) => new(idx, id);

    [Fact]
    public void AlignLegIndices_SurvivesGrouperOnlyLeadingRows_TheLiveShiftedPlan()
    {
        // The live 2026-07-30 evening failure: the grouper showed the origin and
        // runway rows, which the fiber walk omits — a positional zip shifted
        // every index by 2, so the discontinuity between the duplicate BPKs was
        // addressed as TOTRI (WRONG_ROW on every delete) and a click on the
        // first BPK would have acted on the second.
        var legs = new List<FmsLeg>
        {
            L("EGSS", LegKind.Origin), L("RW04"),          // grouper-only rows
            L("(590)"), L("(INTC)"), L("D268D"), L("D278F"), L("D304H"),
            L("D325H"), L("CHT"), L("BPK"),
            L("", LegKind.Discontinuity),
            L("BPK"), L("TOTRI"), L("MATCH"),
        };
        var fiber = new List<FiberLegRow>
        {
            F(0, "(590)"), F(1, "(INTC)"), F(2, "D268D"), F(3, "D278F"),
            F(4, "D304H"), F(5, "D325H"), F(6, "CHT"), F(7, "BPK"),
            F(8, ""), F(9, "BPK"), F(10, "TOTRI"), F(11, "MATCH"),
        };
        var aligned = AlignLegIndices(legs, fiber);
        Assert.Equal(
            new[] { -1, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, aligned);
    }

    [Fact]
    public void AlignLegIndices_DiscontinuityMayShareItsIdxWithANeighbour()
    {
        // The earlier live incident (2026-07-30 afternoon): the gap's own row
        // carries the SAME legIdx as the following waypoint. Content pairing
        // keeps them apart — the disco takes the placeholder row, the waypoint
        // its ident row — and deleteLegAt(idx, wantDisco) picks the right slot.
        var legs = new List<FmsLeg>
        {
            L("BPK"), L("", LegKind.Discontinuity), L("TOTRI"),
        };
        var fiber = new List<FiberLegRow> { F(7, "BPK"), F(8, ""), F(8, "TOTRI") };
        Assert.Equal(new[] { 7, 8, 8 }, AlignLegIndices(legs, fiber));
    }

    [Fact]
    public void AlignLegIndices_HeadersNeverPair_AndUnmatchedRowsDoNotDesyncTheRest()
    {
        var legs = new List<FmsLeg>
        {
            L("GENOS"),
            L("MISSED APPROACH", LegKind.Header),          // header — never a leg
            L("GHOST"),                                    // nowhere in the fiber list
            L("KEA"), L("KEA", LegKind.Hold),
        };
        var fiber = new List<FiberLegRow> { F(3, "GENOS"), F(4, "KEA"), F(5, "KEA") };
        Assert.Equal(new[] { 3, -1, -1, 4, 5 }, AlignLegIndices(legs, fiber));
    }

    [Fact]
    public void AlignLegIndices_SkipsFiberOnlyRows_ButOnlyWithinTheShortWindow()
    {
        // A fiber-only annotation row between two waypoints is skipped…
        var legs = new List<FmsLeg> { L("ABC"), L("DEF") };
        var fiber = new List<FiberLegRow> { F(0, "ABC"), F(1, "(220)"), F(2, "DEF") };
        Assert.Equal(new[] { 0, 2 }, AlignLegIndices(legs, fiber));

        // …but a repeat of the ident far ahead must NOT capture the cursor —
        // beyond the window the row stays unmatched rather than mispairing.
        var far = new List<FiberLegRow>
        {
            F(0, "X1"), F(1, "X2"), F(2, "X3"), F(3, "X4"), F(4, "X5"), F(5, "ABC"),
        };
        Assert.Equal(new[] { -1, -1 },
            AlignLegIndices(new List<FmsLeg> { L("ABC"), L("DEF") }, far));
    }

    [Fact]
    public void AlignLegIndices_AdjacentDiscontinuitiesPairInOrder()
    {
        var legs = new List<FmsLeg>
        {
            L("BPK"),
            L("", LegKind.Discontinuity), L("", LegKind.Discontinuity),
            L("TOTRI"),
        };
        var fiber = new List<FiberLegRow> { F(7, "BPK"), F(8, ""), F(9, ""), F(10, "TOTRI") };
        Assert.Equal(new[] { 7, 8, 9, 10 }, AlignLegIndices(legs, fiber));
    }

    [Fact]
    public void AlignLegIndices_EmptyFiberList_EverythingDegradesToMinusOne()
    {
        var legs = new List<FmsLeg> { L("GENOS"), L("", LegKind.Discontinuity) };
        Assert.Equal(new[] { -1, -1 },
            AlignLegIndices(legs, new List<FiberLegRow>()));
    }
}
