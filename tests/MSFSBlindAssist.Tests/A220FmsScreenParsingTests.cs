using MSFSBlindAssist.Aircraft.A220;
using static MSFSBlindAssist.Aircraft.A220.A220FmsScreenParsing;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the FMS/ECL window parsing against fixtures captured from the LIVE
/// Synaptic A220 DisplayUnits scrape (2026-07-29 recon: FPLN INIT page after the
/// EGLL commit test, and the Preflight checklist). Geometry rules under test:
/// gray label + value 10-60 px below at the same x; 28 px left-edge checkbox rects
/// (black fill = unchecked); dot-leader challenge normalization.
/// </summary>
public class A220FmsScreenParsingTests
{
    // ---- FMS ---------------------------------------------------------------

    private static List<WinToken> FmsInitFixture() => new()
    {
        new("FMS1", 20, 18, "magenta"),
        new("MOD", 80, 20, "gray"),
        new("DBASE", 10, 40, "white"),
        new("POS", 130, 40, "white"),
        new("FPLN", 240, 40, "white"),
        new("PERF", 350, 40, "white"),
        new("ROUTE", 460, 40, "white"),
        new("INIT", 12, 95, "white"),
        new("FPLN UPLINK…", 500, 95, "white"),
        new("RTE", 20, 140, "gray"),
        new("----------", 22, 174, "dim"),
        new("FLT NUMBER", 300, 140, "gray"),
        new("--------", 302, 174, "white"),
        new("ORIGIN", 20, 220, "gray"),
        new("EGLL", 29, 254, "white"),
        new("DEST", 242, 220, "gray"),
        new("▯▯▯▯", 252, 254, "white"),
        new("TRANS", 20, 320, "gray"),
        new("TRANS", 242, 320, "gray"),
        new("DEPARTURES…", 500, 320, "white"),
        new("COPY TO SEC", 20, 1000, "white"),
        new("EXEC", 600, 1000, "white"),
        new("MSG…", 680, 1000, "amber"),
    };

    [Fact]
    public void ParseFms_ExtractsHeaderTilesFieldsAndButtons()
    {
        var m = ParseFms(FmsInitFixture());

        Assert.Equal("FMS1", m.Side);
        Assert.Equal("MOD", m.Mode);
        Assert.Equal(new[] { "DBASE", "POS", "FPLN", "PERF", "ROUTE" }, m.Tiles);
        Assert.True(m.ExecAvailable);

        var origin = Assert.Single(m.Fields, f => f.Label == "ORIGIN");
        Assert.Equal("EGLL", origin.Value);
        var dest = Assert.Single(m.Fields, f => f.Label == "DEST");
        Assert.Equal("▯▯▯▯", dest.Value);

        // Repeated labels carry their occurrence for the click call.
        var trans = m.Fields.Where(f => f.Label == "TRANS").ToList();
        Assert.True(trans.Count <= 2);
        if (trans.Count == 2) Assert.Equal(new[] { 0, 1 }, trans.Select(t => t.Occurrence).ToArray());

        Assert.Contains("FPLN UPLINK…", m.Buttons);
        Assert.Contains("DEPARTURES…", m.Buttons);
        Assert.Contains("COPY TO SEC", m.Buttons);
        // A field VALUE never doubles as a button.
        Assert.DoesNotContain("EGLL", m.Buttons);
    }

    /// <summary>
    /// With clickability markers present (current agent), a token becomes a BUTTON
    /// row only when its own component owns an onClick — a DBASE table cell
    /// ("BD-500-1A11") or a date range is data, and "…, button" on it was the
    /// reported soup. Editability flips the same way: markers present + no vbox
    /// rects = a page with genuinely NOTHING editable, never "everything editable".
    /// Fixtures WITHOUT markers (dialog scrapes, older captures) keep both
    /// fallbacks — pinned by every other test in this file.
    /// </summary>
    [Fact]
    public void ParseFms_WithClickInfo_OnlyPressablesAreButtons_AndNoBoxesMeansReadOnly()
    {
        var m = ParseFms(new List<WinToken>
        {
            new("FMS1", 20, 18, "magenta"),
            new("ACT", 80, 20, "gray"),
            new("STATUS", 81, 101, "white", Clickable: true),
            new("A/C VARIANT", 180, 267, "gray"),
            new("CS-300", 179, 302, "white"),
            new("06AUG26 02SEP26", 217, 610, "green"),
            new("BD-500-1A11", 180, 815, "white"),
            new("THRUST…", 23, 1060, "white", Clickable: true),
        });

        Assert.Equal(new[] { "STATUS", "THRUST…" }, m.Buttons);
        Assert.DoesNotContain("BD-500-1A11", m.Buttons);
        Assert.DoesNotContain("06AUG26 02SEP26", m.Buttons);
        // The data rows still reach the user — as plain lines, not buttons.
        Assert.Contains(m.OrphanLines, l => l.Contains("BD-500-1A11"));

        var variant = Assert.Single(m.Fields, f => f.Label == "A/C VARIANT");
        Assert.False(variant.Editable);   // no vbox anywhere + click info = read-only page
        Assert.Equal(267, variant.Y);     // reading-order interleave needs the label's y
    }

    [Fact]
    public void ParseFms_WithoutClickInfo_KeepsTheEditableFallback()
    {
        var m = ParseFms(new List<WinToken>
        {
            new("FMS1", 20, 18, "magenta"),
            new("ORIGIN", 20, 220, "gray"), new("EGLL", 29, 254, "white"),
        });
        Assert.True(Assert.Single(m.Fields).Editable);
    }

    [Fact]
    public void ParseFms_ButtonRowsCarryTheirScreenY()
    {
        var m = ParseFms(FmsInitFixture());
        var uplink = Assert.Single(m.ButtonRows, b => b.Text == "FPLN UPLINK…");
        Assert.Equal(95, uplink.Y);
    }

    /// <summary>
    /// A pending modification is the MODE flag reading "MOD" — NOT an on-screen
    /// "EXEC" token. The A220's EXEC is a physical MKP key with its own lamp and the
    /// FMS window never renders the word, so the original `Any(text == "EXEC")` test
    /// was permanently false and the EXEC cue could never fire (the user reported
    /// exactly that). These two cases pin the real rule in both directions.
    /// </summary>
    [Fact]
    public void ExecAvailable_TracksModModeNotAnOnScreenExecToken()
    {
        // Mode MOD with NO "EXEC" text anywhere -> a mod IS pending.
        var modNoToken = ParseFms(new List<WinToken>
        {
            new("FMS1", 20, 18, "magenta"),
            new("MOD", 80, 20, "gray"),
            new("ORIGIN", 20, 220, "gray"), new("EGLL", 29, 254, "white"),
        });
        Assert.Equal("MOD", modNoToken.Mode);
        Assert.True(modNoToken.ExecAvailable);

        // Mode ACT even though an "EXEC" token exists -> NOTHING is pending.
        var actWithToken = ParseFms(new List<WinToken>
        {
            new("FMS1", 20, 18, "magenta"),
            new("ACT", 80, 20, "gray"),
            new("ORIGIN", 20, 220, "gray"), new("EGLL", 29, 254, "white"),
            new("EXEC", 600, 1000, "white"),
        });
        Assert.Equal("ACT", actWithToken.Mode);
        Assert.False(actWithToken.ExecAvailable);
    }

    [Fact]
    public void ParseFms_NoValueBoxes_DefaultsAllFieldsEditable()
    {
        var m = ParseFms(FmsInitFixture());
        Assert.All(m.Fields, f => Assert.True(f.Editable));
    }

    [Fact]
    public void ParseFms_EditableOnlyWhenValueSitsInsideAValueBox()
    {
        // vbox frames around the ORIGIN and DEST values only — RTE / FLT NUMBER
        // values sit outside every box and must NOT read as edit boxes.
        var boxes = new List<WinBox>
        {
            new("vbox", 20, 240, "", 180, 34),
            new("vbox", 245, 240, "", 180, 34),
        };
        var m = ParseFms(FmsInitFixture(), boxes);
        Assert.True(Assert.Single(m.Fields, f => f.Label == "ORIGIN").Editable);
        Assert.True(Assert.Single(m.Fields, f => f.Label == "DEST").Editable);
        Assert.False(Assert.Single(m.Fields, f => f.Label == "FLT NUMBER").Editable);
        Assert.False(Assert.Single(m.Fields, f => f.Label == "RTE").Editable);
    }

    /// <summary>
    /// The renderer CENTERS a short constraint-type label over its value box
    /// instead of left-aligning it. Live capture 2026-07-30 (CROSSING - KEA,
    /// ALTITUDE type set to plain "AT"): the gray "AT" label sat at x=413 over
    /// the altitude box spanning x=342..445 while the "8000" value sat at x=349
    /// — dx 64, beyond the plain FieldMaxDx test — so the one edit box the
    /// pilot needed to set an exact crossing altitude vanished from the rows
    /// (the "no edit box for the AT option" report). A label and value inside
    /// the SAME vbox are one field regardless of their dx.
    /// </summary>
    [Fact]
    public void ParseFms_CenteredLabelOverSharedValueBox_StillPairs()
    {
        var tokens = new List<WinToken>
        {
            new("FMS1", 0, 0, "magenta"),
            new("MOD", 622, 12, "gray"),
            new("DBASE", 0, 40, "white"), new("POS", 60, 40, "white"),
            new("FPLN", 120, 40, "white"), new("PERF", 180, 40, "white"), new("ROUTE", 240, 40, "white"),
            // CROSSING - KEA dialog body, verbatim geometry from the live dump.
            new("ALTITUDE", 152, 725, "gray"),
            new("AT", 168, 764, "white", Dropdown: true, Options: 4),
            new("AT", 413, 725, "gray"),        // centered over the 342..445 box
            new("8000", 349, 764, "green"),
            new("FLT PHASE", 152, 825, "gray"),
            new("DES", 168, 864, "white", Dropdown: true, Options: 2),
            new("SPD", 467, 825, "gray"),
            new("---", 477, 864, "green"),
            new("DONE", 646, 988, "white"),
        };
        var boxes = new List<WinBox>
        {
            new("vbox", 342, 759, "", 103, 33),
            new("vbox", 467, 859, "", 65, 33),
        };
        var m = ParseFms(tokens, boxes);

        // The ALTITUDE dropdown pairs as before…
        var alt = Assert.Single(m.Fields, f => f.Label == "ALTITUDE");
        Assert.True(alt.IsDropdown);
        // …and the centered "AT" label still claims its 8000 edit box.
        var at = Assert.Single(m.Fields, f => f.Label == "AT");
        Assert.Equal("8000", at.Value);
        Assert.True(at.Editable);
        Assert.False(at.IsDropdown);
        // The SPD pairing (ordinary near-aligned label) is unaffected.
        var spd = Assert.Single(m.Fields, f => f.Label == "SPD");
        Assert.Equal("---", spd.Value);
        // 8000 must not leak into the orphan lines as an unclaimed number.
        Assert.DoesNotContain(m.OrphanLines, l => l.Contains("8000"));
    }

    /// <summary>
    /// Fusion's SECOND field layout: the label sits INLINE, left of its value box
    /// on the same text row — "OFFSET [---.-] NM" and "CRS [255]°" in the live
    /// Direct-To dialog capture (2026-07-30). The below-the-label rule alone
    /// dropped both, hiding the intercept-course entry — the one field a pilot
    /// needs to adjust the course to the final approach. The inline form pairs
    /// ONLY when the value sits in a vbox that starts at/after the label (the box
    /// gate keeps table cells and chrome pairs from false-pairing).
    /// </summary>
    [Fact]
    public void ParseFms_InlineLabelWithBoxedValue_Pairs()
    {
        var tokens = new List<WinToken>
        {
            new("FMS1", 0, 0, "magenta"),
            new("ACT", 622, 12, "gray"),
            new("DBASE", 0, 40, "white"), new("POS", 60, 40, "white"),
            new("FPLN", 120, 40, "white"), new("PERF", 180, 40, "white"), new("ROUTE", 240, 40, "white"),
            // Direct-To dialog body, verbatim geometry from the live dump
            // (y values dialog-relative in the capture; offsets preserved).
            new("OFFSET", 52, 423, "gray"),
            new("---.-", 139, 419, "white"),
            new("NM", 225, 423, "gray"),
            new("CRS", 135, 936, "gray"),
            new("255", 187, 932, "white"),
            new("°", 247, 928, "gray"),
            new("DONE", 646, 988, "white"),
        };
        var boxes = new List<WinBox>
        {
            new("vbox", 132, 414, "", 82, 33),
            new("vbox", 178, 927, "", 63, 33),
        };
        var m = ParseFms(tokens, boxes);

        var crs = Assert.Single(m.Fields, f => f.Label == "CRS");
        Assert.StartsWith("255", crs.Value);
        Assert.True(crs.Editable);
        var offset = Assert.Single(m.Fields, f => f.Label == "OFFSET");
        Assert.StartsWith("---.-", offset.Value);
        // A same-row white token with NO box under it must not pair: "NM" is a
        // trailing unit, not a field, and must not become "NM: DONE"-style noise.
        Assert.DoesNotContain(m.Fields, f => f.Label == "NM");
    }

    /// <summary>
    /// The Direct-To dialog's structure parser, pinned against the live capture
    /// (2026-07-30, LGAV plan): rows are [→][ident-in-box] [VERT →][alt-in-box];
    /// the typed-entry row is the one whose arrow is GRAY; duplicate idents carry
    /// occurrences (KEA repeats); VERT is disabled (gray) on legs without a
    /// crossing altitude. The generic pass rendered this as glyph/orphan soup —
    /// the "confusing dialog" report this parser exists to fix.
    /// </summary>
    [Fact]
    public void ParseDirectTo_GroupsLegsEntryAndFields()
    {
        var tokens = new List<WinToken>
        {
            new("→", 53, 375, "gray"),          // dialog header glyph
            new("ACT", 622, 376, "white"),
            new("FPLN", 673, 380, "gray"),
            new("OFFSET", 52, 423, "gray"),
            new("---.-", 139, 419, "white"),
            new("NM", 225, 423, "gray"),
            new("ALT SEL", 372, 483, "gray"),
            new("---", 500, 481, "cyan"),
            new("→", 152, 543, "gray"),          // typed-entry row (gray arrow)
            new("-----", 203, 541, "white"),
            new("PHA", 203, 623, "magenta"),     // active leg
            new("→", 152, 623, "white"),
            new("-----", 466, 623, "green"),
            new("VERT →", 353, 623, "gray"),
            new("GENOS", 203, 705, "white"),
            new("→", 152, 705, "white"),
            new("-----", 466, 705, "green"),
            new("VERT →", 353, 705, "gray"),
            new("KEA", 203, 1443, "white"),
            new("→", 152, 1443, "white"),
            new("8000", 466, 1443, "green"),
            new("VERT →", 353, 1443, "white"),
            new("KEA", 203, 1525, "white"),
            new("→", 152, 1525, "white"),
            new("5000", 466, 1525, "green"),
            new("VERT →", 353, 1525, "white"),
            new("D190T", 203, 1607, "white"),
            new("→", 152, 1607, "white"),
            new("-----", 466, 1607, "green"),
            new("VERT →", 353, 1607, "gray"),
            new("CI03L", 203, 1771, "white"),
            new("→", 152, 1771, "white"),
            new("3000", 466, 1771, "green"),
            new("VERT →", 353, 1771, "white"),
            new("CRS", 135, 936, "gray"),
            new("255", 187, 932, "white"),
            new("°", 247, 928, "gray"),
            new("DONE", 646, 988, "white"),
        };
        var boxes = new List<WinBox>
        {
            new("vbox", 132, 414, "", 82, 33),     // OFFSET
            new("vbox", 462, 475, "", 90, 33),     // ALT SEL
            new("vbox", 196, 536, "", 118, 33),    // typed entry
            new("vbox", 196, 618, "", 118, 33), new("vbox", 459, 618, "", 102, 33),
            new("vbox", 196, 700, "", 118, 33), new("vbox", 459, 700, "", 102, 33),
            new("vbox", 196, 1438, "", 118, 33), new("vbox", 459, 1438, "", 102, 33),
            new("vbox", 196, 1520, "", 118, 33), new("vbox", 459, 1520, "", 102, 33),
            new("vbox", 196, 1602, "", 118, 33), new("vbox", 459, 1602, "", 102, 33),
            new("vbox", 196, 1766, "", 118, 33), new("vbox", 459, 1766, "", 102, 33),
            new("vbox", 178, 927, "", 63, 33),     // CRS
        };
        var m = ParseDirectTo(tokens, boxes);

        Assert.True(m.HasEntry);
        Assert.Equal(new[] { "PHA", "GENOS", "KEA", "KEA", "D190T", "CI03L" },
            m.Legs.Select(l => l.Ident).ToArray());
        Assert.True(m.Legs[0].Active);
        Assert.Equal(0, m.Legs[2].IdentOcc);
        Assert.Equal(1, m.Legs[3].IdentOcc);
        // VERT: disabled where the aircraft grays it, enabled with its altitude
        // elsewhere; occurrences count ALL VERT tokens in reading order.
        Assert.False(m.Legs[4].VertEnabled);                 // D190T
        var ci = m.Legs[5];
        Assert.True(ci.VertEnabled);
        Assert.Equal("3000", ci.VertAlt);
        Assert.Equal(5, ci.VertOcc);
        // Scalar fields survive (inline layout): CRS and OFFSET at least.
        Assert.Contains(m.Fields, f => f.Label == "CRS");
        Assert.Contains(m.Fields, f => f.Label == "OFFSET");
    }

    /// <summary>
    /// SELECT WPT / SELECT AIRWAY choice naming: those dialogs draw repeated
    /// identical SELECT buttons whose describing content (region, frequency,
    /// coordinates; airway endpoints) sits in the BAND BELOW each button, not on
    /// its row (bundle-verified 2026-07-30, child pitch 160/111). Each button must
    /// be named from its own band — and never from the NEXT candidate's band.
    /// </summary>
    [Fact]
    public void DialogChoiceValueFor_NamesButtonsFromTheBandBelow()
    {
        var tokens = new List<WinToken>
        {
            new("SELECT", 12, 57, "white"),
            new("LGSO", 161, 95, "white"),
            new("VOR 108.20", 12, 135, "white"),
            new("37°45.2N 026°50.1E", 159, 171, "white"),
            new("SELECT", 12, 217, "white"),
            new("LGKR", 161, 255, "white"),
            new("NDB 403", 12, 295, "white"),
        };
        string? first = DialogChoiceValueFor(tokens, "SELECT", 0);
        Assert.NotNull(first);
        Assert.Contains("LGSO", first);
        Assert.Contains("VOR 108.20", first);
        Assert.DoesNotContain("LGKR", first);
        string? second = DialogChoiceValueFor(tokens, "SELECT", 1);
        Assert.NotNull(second);
        Assert.Contains("LGKR", second);
        Assert.DoesNotContain("LGSO", second);
    }

    [Fact]
    public void ParseFms_OrphanLines_CarryOnlyUnclaimedTokens()
    {
        var m = ParseFms(FmsInitFixture());
        // The two unpaired TRANS labels are claimed by no field/button row — they
        // must surface as an orphan line; claimed tokens must not duplicate there.
        Assert.Contains(m.OrphanLines, l => l.Contains("TRANS"));
        Assert.DoesNotContain(m.OrphanLines, l => l.Contains("EGLL"));
        Assert.DoesNotContain(m.OrphanLines, l => l.Contains("DBASE"));
        Assert.DoesNotContain(m.OrphanLines, l => l.Contains("DEPARTURES"));
    }

    [Fact]
    public void ParseFms_ValueMustSitBelowItsLabel()
    {
        // A white token 100 px below the label is NOT its value (next row).
        var m = ParseFms(new List<WinToken>
        {
            new("FMS1", 0, 0, "magenta"),
            new("ACT", 40, 0, "gray"),
            new("DBASE", 0, 40, "white"), new("POS", 60, 40, "white"),
            new("FPLN", 120, 40, "white"), new("PERF", 180, 40, "white"), new("ROUTE", 240, 40, "white"),
            new("CRZ ALT", 20, 200, "gray"),
            new("350", 22, 340, "white"),
        });
        Assert.DoesNotContain(m.Fields, f => f.Label == "CRZ ALT");
    }

    [Fact]
    public void ComposeLines_GroupsByRowAndSortsByX()
    {
        var lines = ComposeLines(new List<WinToken>
        {
            new("DEST", 242, 220, "gray"),
            new("ORIGIN", 20, 222, "gray"),
            new("EGLL", 29, 254, "white"),
        });
        Assert.Equal(new[] { "ORIGIN  DEST", "EGLL" }, lines);
    }

    // ---- ECL ---------------------------------------------------------------

    private static (List<WinToken>, List<WinBox>) EclPreflightFixture()
    {
        var tokens = new List<WinToken>
        {
            new("SUMMARY", 18, 25, "white"),
            new("NORMAL", 163, 25, "white"),
            new("NON-NORMAL", 298, 25, "white"),
            new("PROC", 511, 25, "white"),
            new("FCTN", 628, 25, "white"),
            new("Preflight", 273, 80, "white"),
            new("First flight of the day:", 49, 183, "white"),
            new("YES", 243, 227, "white"),
            new("NO", 490, 227, "white"),
            new("* Ice detector test․․․․․․․․․․․․Complete", 49, 413, "gray"),
            new("Operations Engineering Bulletins", 49, 689, "gray"),
            new("(OEB)․․․․․․․․․․․․․․․․․․․․․․․․․․․Checked", 49, 735, "gray"),
            new("Emergency equipment․․․․․․․․․․․․․Checked", 49, 781, "gray"),
            new("ECL_BCS3_SYN_072826_KG", 205, 1068, "white"),
        };
        var boxes = new List<WinBox>
        {
            new("radio", 211, 228, "rgb(0, 0, 0)"),
            new("radio", 458, 228, "rgb(0, 0, 0)"),
            new("box", 12, 409, "rgb(0, 0, 0)"),
            new("box", 12, 685, "rgb(0, 0, 0)"),
            new("box", 12, 777, "rgb(0, 191, 79)"),
        };
        return (tokens, boxes);
    }

    [Fact]
    public void ParseEcl_PairsBoxesItemsAndContinuationLines()
    {
        var (tokens, boxes) = EclPreflightFixture();
        var m = ParseEcl(tokens, boxes);

        Assert.Equal(new[] { "SUMMARY", "NORMAL", "NON-NORMAL", "PROC", "FCTN" }, m.Tiles);
        Assert.Equal("Preflight", m.Title);
        Assert.Equal("ECL_BCS3_SYN_072826_KG", m.PackName);

        var ice = Assert.Single(m.Items, i => i.Text.StartsWith("* Ice detector", StringComparison.Ordinal));
        Assert.False(ice.Checked);

        // Two-line item: continuation appended to the checkbox line.
        var oeb = Assert.Single(m.Items, i => i.Text.StartsWith("Operations Engineering", StringComparison.Ordinal));
        Assert.Contains("(OEB)", oeb.Text);
        Assert.False(oeb.Checked);

        // Painted box = sensed complete.
        var emergency = Assert.Single(m.Items, i => i.Text.StartsWith("Emergency equipment", StringComparison.Ordinal));
        Assert.True(emergency.Checked);

        // Boxless rows survive as activatable rows (question + YES/NO options).
        Assert.Contains(m.Items, i => i.Checked == null && i.Text.Contains("First flight of the day"));
        Assert.Contains(m.Items, i => i.Checked == null && i.Text.Contains("YES"));
    }

    [Fact]
    public void ParseEcl_SummaryListing_HasNoTitleAndBoxlessNames()
    {
        var m = ParseEcl(new List<WinToken>
        {
            new("SUMMARY", 18, 25, "white"),
            new("NORMAL", 163, 25, "white"),
            new("NON-NORMAL", 298, 25, "white"),
            new("PROC", 511, 25, "white"),
            new("FCTN", 628, 25, "white"),
            new("Power-on", 56, 86, "white"),
            new("Preflight", 56, 142, "white"),
        }, new List<WinBox>());
        Assert.Null(m.Title); // x=56 rows are list entries, not a centered title
        Assert.Equal(2, m.Items.Count);
        Assert.All(m.Items, i => Assert.Null(i.Checked));
    }

    [Fact]
    public void IsBoxChecked_BlackAndTransparentAreUnchecked()
    {
        Assert.False(IsBoxChecked("rgb(0, 0, 0)"));
        Assert.False(IsBoxChecked("rgba(0, 0, 0, 0)"));
        Assert.False(IsBoxChecked("none"));
        Assert.False(IsBoxChecked(""));
        Assert.True(IsBoxChecked("rgb(0, 191, 79)"));
    }

    [Fact]
    public void NormalizeChallenge_StripsDotLeaderAndResponse()
    {
        Assert.Equal("* ICE DETECTOR TEST",
            NormalizeChallenge("* Ice detector test․․․․․․․․․․․․Complete"));
        Assert.Equal("PARK BRAKE", NormalizeChallenge("PARK BRAKE․․․ON"));
        Assert.Equal("NO DOTS HERE", NormalizeChallenge("No dots  here"));
    }

    // ---- agent JSON --------------------------------------------------------

    [Fact]
    public void ParseAgentTokens_RoundTripsTokensAndBoxes()
    {
        var win = ParseAgentTokens(
            "{\"ok\":true,\"tokens\":[{\"t\":\"ORIGIN\",\"x\":20,\"y\":220,\"c\":\"gray\"}]," +
            "\"boxes\":[{\"k\":\"box\",\"x\":12,\"y\":409,\"f\":\"rgb(0, 0, 0)\"}]}");
        Assert.NotNull(win);
        Assert.True(win!.Ok);
        var tok = Assert.Single(win.Tokens);
        Assert.Equal(("ORIGIN", 20.0, 220.0, "gray"), (tok.Text, tok.X, tok.Y, tok.Color));
        var box = Assert.Single(win.Boxes);
        Assert.Equal(("box", "rgb(0, 0, 0)"), (box.Kind, box.Fill));
    }

    [Fact]
    public void ParseAgentTokens_NoWindow_ReportsReason()
    {
        var win = ParseAgentTokens("{\"ok\":false,\"reason\":\"no_fms_window\"}");
        Assert.NotNull(win);
        Assert.False(win!.Ok);
        Assert.Equal("no_fms_window", win.Reason);
        Assert.Null(ParseAgentTokens("garbage"));
    }

    // ---- checklists.json pack ---------------------------------------------

    private const string PackJson = """
    {
      "name": "Test Pack",
      "partNumber": "ECL_TEST_1",
      "normal": [
        {
          "name": "Preflight",
          "items": [
            {
              "type": "conditional",
              "challenge": "First flight of the day:",
              "paths": {
                "YES": [
                  { "type": "free-text", "text": "\nFirst flight of the day:\n" },
                  { "type": "action", "challenge": "* Ice detector test", "response": "Complete" }
                ],
                "NO": [
                  { "type": "action", "challenge": "Gear pins", "response": "On board" }
                ]
              }
            },
            { "type": "action", "challenge": "PARK BRAKE", "response": "ON", "sensed": "PARK_BRAKE_ON" }
          ]
        }
      ],
      "non_normal": [],
      "procedure": []
    }
    """;

    [Fact]
    public void ChecklistPack_ParsesConditionalPathsAndSensedNames()
    {
        var pack = A220ChecklistPack.Parse(PackJson);
        Assert.NotNull(pack);
        Assert.Equal("ECL_TEST_1", pack!.PartNumber);
        var preflight = Assert.Single(pack.Normal);
        Assert.Equal("Preflight", preflight.Name);

        var park = preflight.Items.Single(i => i.Challenge == "PARK BRAKE");
        Assert.Equal("PARK_BRAKE_ON", park.Sensed);
        Assert.Null(park.Path);

        var ice = preflight.Items.Single(i => i.Challenge.StartsWith("* Ice", StringComparison.Ordinal));
        Assert.Equal("YES", ice.Path);
        Assert.Null(ice.Sensed);
    }

    [Fact]
    public void ChecklistPack_FindItem_MatchesNormalizedScrapedLine()
    {
        var pack = A220ChecklistPack.Parse(PackJson)!;
        var item = pack.FindItem("Preflight",
            NormalizeChallenge("PARK BRAKE․․․․․․․․․․․․․․․․․․․․ON"));
        Assert.NotNull(item);
        Assert.Equal("PARK_BRAKE_ON", item!.Sensed);

        // Wrapped scrape line (challenge truncated) still prefix-matches.
        var ice = pack.FindItem("Preflight", NormalizeChallenge("* Ice detector"));
        Assert.NotNull(ice);
        Assert.Null(ice!.Sensed);

        Assert.Null(pack.FindItem("Preflight", NormalizeChallenge("Nonexistent item")));
        Assert.Null(pack.FindItem("Shutdown", NormalizeChallenge("PARK BRAKE")));
    }
}

/// <summary>
/// Pins the dialog clip that makes a Fusion dialog (CROSSING…, HOLD…, FIX…,
/// DEP/ARR, the duplicate-fix picker) read MODALLY. Without the clip, the dialog's
/// own fields interleave with the page rows behind it and the pilot edits a
/// constraint while reading the page underneath — the clunkiness reported
/// 2026-07-29. Geometry is window-relative pixels, exactly as the agent reports
/// both tokens and the dialog rect.
/// </summary>
public class A220FmsDialogClipTests
{
    // A CROSSING dialog (590x360 units) over a LEGS page: two page rows above it,
    // the dialog's own ALTITUDE label/value inside, one page row below.
    private static List<WinToken> Mixed() => new()
    {
        new("GENOS", 17, 120, "white"),           // page, above the dialog
        new("266°", 10, 91, "white"),             // page, above the dialog
        new("CROSSING - GENOS", 152, 690, "gray"),// dialog header
        new("ALTITUDE", 152, 745, "gray"),        // dialog field label
        new("6000", 152, 775, "green"),           // dialog field value
        new("FLT PHASE", 152, 845, "gray"),
        new("CLB", 152, 875, "white"),
        new("DONE", 640, 990, "white"),           // dialog dismiss
        new("THRUST…", 600, 1050, "white"),       // page chrome, below the dialog
    };

    private static readonly (double X, double Y, double W, double H) CrossingRect = (142, 676, 590, 360);

    [Fact]
    public void ClipTo_KeepsOnlyDialogTokens()
    {
        var (tokens, _) = ClipTo(Mixed(), new List<WinBox>(),
            CrossingRect.X, CrossingRect.Y, CrossingRect.W, CrossingRect.H);
        var texts = tokens.Select(t => t.Text).ToList();

        Assert.Contains("CROSSING - GENOS", texts);
        Assert.Contains("ALTITUDE", texts);
        Assert.Contains("6000", texts);
        Assert.Contains("DONE", texts);
        // The page behind the dialog must be gone — that is the whole point.
        Assert.DoesNotContain("GENOS", texts);
        Assert.DoesNotContain("266°", texts);
        Assert.DoesNotContain("THRUST…", texts);
    }

    [Fact]
    public void ClipTo_ClippedTokensStillParseAsFieldsAndButtons()
    {
        var (tokens, boxes) = ClipTo(Mixed(), new List<WinBox>(),
            CrossingRect.X, CrossingRect.Y, CrossingRect.W, CrossingRect.H);
        var model = ParseFms(tokens, boxes);

        // The dialog's label/value pairs survive the clip as real fields.
        var alt = model.Fields.FirstOrDefault(f => f.Label == "ALTITUDE");
        Assert.NotNull(alt);
        Assert.Equal("6000", alt!.Value);
        var phase = model.Fields.FirstOrDefault(f => f.Label == "FLT PHASE");
        Assert.NotNull(phase);
        Assert.Equal("CLB", phase!.Value);
        // DONE is a button row (the form renders it as the dismiss control).
        Assert.Contains("DONE", model.Buttons);
    }

    [Fact]
    public void ClipTo_FiltersBoxesToo()
    {
        var boxes = new List<WinBox>
        {
            new("vbox", 152, 770, "", 103, 33),    // inside the dialog
            new("vbox", 29, 250, "", 89, 33),      // page field behind it
        };
        var (_, clipped) = ClipTo(new List<WinToken>(), boxes,
            CrossingRect.X, CrossingRect.Y, CrossingRect.W, CrossingRect.H);

        Assert.Single(clipped);
        Assert.Equal(152, clipped[0].X);
    }

    [Fact]
    public void ClipTo_EdgeTokenIsKept()
    {
        // A token drawn ON the dialog frame belongs to the dialog (small pad).
        var onEdge = new List<WinToken> { new("HDR", CrossingRect.X - 3, CrossingRect.Y - 3, "gray") };
        var (tokens, _) = ClipTo(onEdge, new List<WinBox>(),
            CrossingRect.X, CrossingRect.Y, CrossingRect.W, CrossingRect.H);
        Assert.Single(tokens);
    }
}

public class A220FmsSpokenValueTests
{
    [Theory]
    [InlineData("EGLL", "EGLL")]
    [InlineData("▯▯▯▯", "blank")]
    [InlineData("□□", "blank")]
    [InlineData("120/▯▯▯", "120/blank")]
    [InlineData("", "blank")]
    [InlineData("  ", "blank")]
    public void SpokenFieldValue_CollapsesEnterableSlots(string raw, string expected)
    {
        Assert.Equal(expected, MSFSBlindAssist.Aircraft.A220.A220FmsScreenParsing.SpokenFieldValue(raw));
    }
}

/// <summary>
/// Pins the FUEL page's field pairing against the live geometry (bundle
/// Fuel.tsx + the 2026-07-30 scrape). The page's entry tables are the hard case:
/// labels are RIGHT-ALIGNED into a column (ZFW/GWT/TOW/LW share an x, as do
/// BLOCK/TAXI/TRIP), each with its entry box INLINE on its own row, so the next
/// label down and the next box down are both near neighbours of any given label.
/// A label must pair with the box on ITS row — live, the click hunt picked the
/// neighbouring GWT/TAXI labels instead and the pilot's ZFW went nowhere.
/// Also pins "NUMBER OF PAX", whose box starts 162 units right of its label
/// (the 170-unit inline gate; 130 left it unreachable).
/// </summary>
public class A220FmsFuelPagePairingTests
{
    // Rect TOP-LEFT coordinates, as the agent reports them (window-relative).
    private static (List<A220FmsScreenParsing.WinToken>, List<A220FmsScreenParsing.WinBox>) FuelFixture()
    {
        var tokens = new List<A220FmsScreenParsing.WinToken>
        {
            new("FMS1", 20, 18, "magenta"),
            new("ACT", 80, 20, "gray"),
            // Sensed read-out: value right of its label but in NO box — must not pair.
            new("FUEL QTY", 470, 205, "gray"),
            new("15340", 610, 205, "white"),
            // WT / CG entry table. Labels right-aligned at x=70 (rect left ~30),
            // each row's inputs inline beside it; GWT below is a COMPUTED value.
            new("WT(LB)", 70, 400, "gray"),
            new("CG(%MAC)", 195, 400, "gray"),
            new("ZFW", 30, 437, "gray"),
            new("▯▯▯▯▯▯", 89, 442, "white"),      // in the ZFW weight box
            new("25.5", 220, 442, "white"),        // in the ZFW CG box
            new("GWT", 30, 474, "gray"),
            new("124610", 130, 479, "white"),      // computed, no box
            new("26.1", 275, 479, "white"),
            // Fuel-planning column, same shape: BLOCK's box on its row, TAXI below.
            new("FUEL PLANNING(LB)", 455, 400, "gray"),
            new("BLOCK", 465, 437, "gray"),
            new("▯▯▯▯▯", 539, 442, "white"),
            new("TAXI", 480, 474, "gray"),
            new("▯▯▯▯", 539, 479, "white"),
            // Pax: the box starts 162 units right of the label.
            new("NUMBER OF PAX", 8, 658, "gray"),
            new("▯▯▯", 177, 663, "white"),
        };
        var boxes = new List<A220FmsScreenParsing.WinBox>
        {
            new("vbox", 82, 437, "", 104, 33),   // ZFW weight
            new("vbox", 213, 437, "", 66, 33),   // ZFW CG
            new("vbox", 532, 437, "", 89, 33),   // BLOCK
            new("vbox", 532, 474, "", 74, 33),   // TAXI
            new("vbox", 170, 658, "", 59, 33),   // NUMBER OF PAX
        };
        return (tokens, boxes);
    }

    [Fact]
    public void ParseFms_FuelPage_PairsEachLabelWithTheBoxOnItsOwnRow()
    {
        var (tokens, boxes) = FuelFixture();
        var m = A220FmsScreenParsing.ParseFms(tokens, boxes);

        var zfw = Assert.Single(m.Fields, f => f.Label == "ZFW");
        Assert.Equal("▯▯▯▯▯▯", zfw.Value);      // its own 6-slot box, not GWT's value
        Assert.True(zfw.Editable);

        // BLOCK must take the box on its row, never TAXI's directly below —
        // both sit at the same x, which is what made this ambiguous live.
        var block = Assert.Single(m.Fields, f => f.Label == "BLOCK");
        Assert.Equal("▯▯▯▯▯", block.Value);
        Assert.True(block.Editable);

        var taxi = Assert.Single(m.Fields, f => f.Label == "TAXI");
        Assert.Equal("▯▯▯▯", taxi.Value);

        // The 162-unit inline gap (bundle: label x=8, input x=170).
        var pax = Assert.Single(m.Fields, f => f.Label == "NUMBER OF PAX");
        Assert.Equal("▯▯▯", pax.Value);
        Assert.True(pax.Editable);

        // A sensed value with no entry box must not read as a typeable field.
        Assert.DoesNotContain(m.Fields, f => f.Label == "FUEL QTY" && f.Editable);
    }

    [Fact]
    public void ParseFms_FuelPage_ComputedRowIsNotEditable()
    {
        var (tokens, boxes) = FuelFixture();
        var m = A220FmsScreenParsing.ParseFms(tokens, boxes);

        // GWT is the FMS's own computed gross weight — it has no entry box, so it
        // must never be offered as an edit box the pilot can type into.
        var gwt = m.Fields.FirstOrDefault(f => f.Label == "GWT");
        if (gwt != null) Assert.False(gwt.Editable);
    }
}

/// <summary>
/// Pins the MKP scratchpad-buffer prediction against the aircraft's own pushChar
/// semantics (MKP bundle): plain characters append with a 24-char cap; the
/// PLUS_MINUS key appends '-' first and TOGGLES a trailing sign after that. The
/// commit path verifies the typed entry against this before clicking — a stale
/// refused entry otherwise concatenates and every commit answers INVALID ENTRY.
/// </summary>
public class A220ScratchpadPredictionTests
{
    [Theory]
    [InlineData("109270", "109270")]
    [InlineData("25.5", "25.5")]
    [InlineData("egll", "EGLL")]
    [InlineData("-5", "-5")]        // PLUS_MINUS first press = minus
    [InlineData("+5", "-5")]        // '+' also sends PLUS_MINUS: first press = minus
    [InlineData("--5", "+5")]       // second press toggles the sign
    [InlineData("120/250", "120/250")]
    public void PredictScratchpadBuffer_MatchesMkpPushChar(string sent, string expected)
    {
        Assert.Equal(expected,
            MSFSBlindAssist.Aircraft.SynapticA220Definition.PredictScratchpadBuffer(sent));
    }

    [Fact]
    public void PredictScratchpadBuffer_CapsAt24Characters()
    {
        string typed = new string('1', 30);
        Assert.Equal(new string('1', 24),
            MSFSBlindAssist.Aircraft.SynapticA220Definition.PredictScratchpadBuffer(typed));
    }
}
