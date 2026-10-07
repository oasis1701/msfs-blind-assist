using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.Forms.A220;
using static MSFSBlindAssist.Aircraft.A220.A220FmsScreenParsing;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the 2026-09-24 FMS review fixes against geometry captured LIVE from the
/// Synaptic A220 (EGNT, cold-and-dark FMS, FUEL page / DEPARTURES / Direct-To /
/// SELECT WPT dialogs). Each test names the user-visible failure it guards.
/// </summary>
public class A220FmsFuelAndDialogTests
{
    private static WinBox Box(double x, double y, double w, double h) => new("vbox", x, y, "", w, h);

    // FUEL page, ZFW entered (live capture). Column headings WT(LB)/CG(%MAC) and
    // FUEL PLANNING(LB) sit ~44 units above the first row of boxes.
    private static (List<WinToken>, List<WinBox>) FuelPage() => (new()
    {
        new("FMS1", 8, 29, "magenta"),
        new("FUEL", 430, 101, "white", Clickable: true),
        new("WT(LB)", 91, 398, "gray"),
        new("CG(%MAC)", 204, 398, "gray"),
        new("ZFW", 34, 445, "gray"),
        new("GWT", 34, 482, "gray"),
        new("TOW", 34, 519, "gray"),
        new("LW", 46, 556, "gray"),
        new("101000", 89, 442, "white", Min: 81750, Max: 128000),
        new("▯▯.▯", 220, 442, "white", Min: 20, Max: 40),
        new("FUEL PLANNING(LB)", 530, 398, "gray"),
        new("BLOCK", 461, 445, "gray"),
        new("TAXI", 472, 482, "gray"),
        new("TRIP", 472, 519, "gray"),
        new("RESERVE/", 425, 545, "gray"),
        new("CONTINGENCY", 390, 566, "gray"),
        new("%", 707, 556, "gray"),
        new("▯▯▯▯▯", 539, 442, "white", Min: 0, Max: 38350),
        new("100", 554, 479, "white", Min: 0, Max: 9999),
        new("▯▯▯▯", 539, 552, "white", ReadOnly: true),
        new("▯▯.▯", 632, 552, "white", Min: 0, Max: 15),
        new("NUMBER OF PAX", 8, 666, "gray"),
        new("▯▯▯", 177, 663, "white", Min: 0, Max: 160),
    }, new()
    {
        Box(82, 437, 104, 33), Box(213, 437, 66, 33), Box(532, 437, 89, 33), Box(532, 474, 74, 33),
        Box(532, 547, 74, 33), Box(624, 547, 67, 33), Box(170, 658, 59, 33),
    });

    [Fact]
    public void FuelPage_EachBoxReadsOnce_ColumnHeadingsAreNotFields()
    {
        // Was: "WT(LB), edit box: 101000" AND "ZFW, edit box: 101000" (and the same
        // for FUEL PLANNING / BLOCK) — two rows, two names, one box.
        var (t, b) = FuelPage();
        var m = ParseFms(t, b);
        Assert.DoesNotContain(m.Fields, f => f.Label is "WT(LB)" or "FUEL PLANNING(LB)");
        Assert.Single(m.Fields, f => f.Label == "ZFW");
        Assert.Single(m.Fields, f => f.Label == "BLOCK");
        Assert.Equal("101000", m.Fields.Single(f => f.Label == "ZFW").Value);
    }

    [Fact]
    public void FuelPage_TableCellNamedByRowAndColumn_ReadAfterItsRow()
    {
        var (t, b) = FuelPage();
        var m = ParseFms(t, b);
        var cg = m.Fields.Single(f => f.Label == "CG(%MAC)");
        Assert.Equal("ZFW CG(%MAC)", cg.Name);
        var zfw = m.Fields.Single(f => f.Label == "ZFW");
        Assert.Equal(zfw.Y, cg.Y);
        Assert.True(cg.X > zfw.X);
    }

    [Fact]
    public void FuelPage_TwoLineLabel_And_UnlabelledPercentBox_AreReachable()
    {
        // Was: "RESERVE/, edit box" + a stray "CONTINGENCY" line, and the % box
        // glued into "LW ▯▯.▯ %" with no way to commit to it.
        var (t, b) = FuelPage();
        var m = ParseFms(t, b);
        var reserve = m.Fields.Single(f => f.Label == "RESERVE/");
        Assert.Equal("RESERVE/CONTINGENCY", reserve.Name);
        Assert.False(reserve.Editable);                  // computed read-out, no submit handler
        var pct = m.Fields.Single(f => f.ClickX != null);
        Assert.Equal("RESERVE/CONTINGENCY %", pct.Name);
        Assert.Equal(632, pct.ClickX);
        Assert.Equal(552, pct.ClickY);
        Assert.True(pct.Editable);
        Assert.Equal(15, pct.Max);
        Assert.DoesNotContain(m.OrphanLines, l => l.Contains("CONTINGENCY") || l.Contains('%'));
    }

    [Fact]
    public void FuelPage_EntryRangesTravelWithTheField()
    {
        var (t, b) = FuelPage();
        var m = ParseFms(t, b);
        var zfw = m.Fields.Single(f => f.Label == "ZFW");
        Assert.Equal(81750, zfw.Min);
        Assert.Equal(128000, zfw.Max);
    }

    [Fact]
    public void ParseAgentTokens_ReadsRangeAndReadOnlyFlags()
    {
        var w = ParseAgentTokens(
            "{\"ok\":true,\"tokens\":[{\"t\":\"1\",\"x\":1,\"y\":2,\"c\":\"white\",\"mn\":5,\"mx\":9,\"ro\":1}],\"boxes\":[]}")!;
        Assert.Equal(5, w.Tokens[0].Min);
        Assert.Equal(9, w.Tokens[0].Max);
        Assert.True(w.Tokens[0].ReadOnly);
    }

    [Fact]
    public void Dialog_WordedKeyIsNeverAFieldValue()
    {
        // SELECT WPT picker: the gray header sits ~59 units above the first SELECT
        // key and paired with it as "SELECT WPT - TLA, edit box: SELECT".
        var t = new List<WinToken>
        {
            new("SELECT WPT - TLA", 133, 599, "gray"),
            new("SELECT", 153, 658, "white", Clickable: true),
            new("CNCL", 646, 988, "white", Clickable: true),
        };
        var m = ParseFms(t);
        Assert.Empty(m.Fields);
    }

    [Fact]
    public void Dialog_PressableReadOutStillAField()
    {
        // Direct-To ALT SEL "---" is pressable but a READ-OUT, not a worded key.
        var t = new List<WinToken>
        {
            new("ALT SEL", 372, 483, "gray"),
            new("---", 500, 481, "cyan", Clickable: true),
        };
        var m = ParseFms(t, new List<WinBox> { Box(462, 475, 90, 33) });
        Assert.Single(m.Fields, f => f.Label == "ALT SEL");
    }

    [Fact]
    public void SelectWaypoint_EachOptionNamedByItsOwnBand()
    {
        // Was: option 1 named "LRFPLN" (region + gray tag); option 3 picked up the
        // dialog's CNCL on its row.
        var t = new List<WinToken>
        {
            new("SELECT", 153, 658, "white", Clickable: true),
            new("LR", 283, 663, "white"),
            new("FPLN", 622, 667, "gray"),
            new("VOR/DME", 134, 703, "white"),
            new("114.80", 134, 739, "white"),
            new("TULCEA", 135, 772, "white"),
            new("SELECT", 153, 818, "white", Clickable: true),
            new("LQ", 283, 823, "white"),
            new("110.10", 134, 899, "white"),
            new("SELECT", 153, 978, "white", Clickable: true),
            new("EG", 283, 983, "white"),
            new("CNCL", 646, 988, "white", Clickable: true),
            new("SELECTION REQUIRED", 134, 995, "white"),
            new("113.80", 134, 1059, "white"),
            new("TALLA", 135, 1092, "white"),
        };
        Assert.Equal("LR VOR/DME 114.80 TULCEA", DialogChoiceValueFor(t, "SELECT", 0));
        Assert.Equal("LQ 110.10", DialogChoiceValueFor(t, "SELECT", 1));
        Assert.Equal("EG 113.80 TALLA", DialogChoiceValueFor(t, "SELECT", 2));
    }

    [Fact]
    public void SelectConstraint_RowConstraintStillNamesTheOption()
    {
        var t = new List<WinToken>
        {
            new("SELECT", 20, 100, "white", Clickable: true),
            new("↓", 200, 100, "green"),
            new("/8000A", 215, 100, "green"),
        };
        Assert.Equal("descend, 8000 or above", DialogChoiceValueFor(t, "SELECT", 0));
    }

    [Fact]
    public void DialogColumn_ListEntriesNamedByHeading_LoneButtonIsNot()
    {
        var t = new List<WinToken>
        {
            new("ARRIVALS - EGPH", 64, 469, "gray"),
            new("STARS(5)", 291, 505, "gray"),
            new("APPR(8)", 548, 505, "gray"),
        };
        var buttons = new List<FmsButton>
        {
            new("AGPE1E", 581, 303), new("GIRV1E", 630, 303),
            new("ILS 06", 581, 560, "cyan"), new("ILS 24", 630, 560),
            new("DONE", 988, 646),
        };
        Assert.Equal("STARS", DialogColumnOf(t, buttons, buttons[0], 458));
        Assert.Equal("APPR", DialogColumnOf(t, buttons, buttons[2], 458));
        Assert.Equal("", DialogColumnOf(t, buttons, buttons[4], 458));
    }

    [Fact]
    public void DirectTo_TypedEntryBoxBelowItsArrow_ReadOnlyCrs_AltBoxes()
    {
        // Live 2026-09-24: the entry box sits 52 units BELOW its gray arrow (the
        // same-row search missed it, so the typed-waypoint row vanished); CRS is
        // plain text until a direct-to is chosen; each VERT altitude is an entry.
        var t = new List<WinToken>
        {
            new("→", 53, 375, "gray"),
            new("OFFSET", 52, 423, "gray"),
            new("---.-", 139, 419, "white"),
            new("NM", 225, 423, "gray"),
            new("ALT SEL", 372, 483, "gray"),
            new("---", 500, 481, "cyan", Clickable: true),
            new("→", 152, 516, "gray"),
            new("-----", 203, 568, "white"),
            new("(1100)", 203, 623, "magenta"),
            new("→", 152, 623, "white", Clickable: true),
            new("1100", 466, 623, "green"),
            new("VERT →", 353, 623, "white", Clickable: true),
            new("NTW03", 203, 705, "white"),
            new("→", 152, 705, "white", Clickable: true),
            new("-----", 466, 705, "green"),
            new("VERT →", 353, 705, "gray"),
            new("CRS", 135, 936, "gray"),
            new("246", 192, 936, "white"),
            new("DONE", 646, 988, "white", Clickable: true),
        };
        var boxes = new List<WinBox>
        {
            Box(132, 414, 82, 33), Box(462, 475, 90, 33), Box(196, 563, 118, 33),
            Box(196, 618, 118, 33), Box(459, 618, 102, 33), Box(196, 700, 118, 33), Box(459, 700, 102, 33),
        };
        var dt = ParseDirectTo(t, boxes);
        Assert.True(dt.HasEntry);
        Assert.Equal(2, dt.Legs.Count);
        Assert.True(dt.Legs[0].HasAltBox);
        Assert.Equal("1100", dt.Legs[0].VertAlt);
        Assert.True(dt.Legs[1].HasAltBox);          // empty, but typeable — enables VERT →
        Assert.False(dt.Legs[1].VertEnabled);
        Assert.Equal(1, dt.Legs[1].VertOcc);
        var crs = dt.Fields.Single(f => f.Label == "CRS");
        Assert.False(crs.Editable);
        Assert.Equal("246°", crs.Value);
        Assert.False(dt.Fields.Single(f => f.Label == "ALT SEL").Editable);
    }

    [Theory]
    [InlineData("↑~150~/1100A", "climb, predicted 150 knots, 1100 or above")]
    [InlineData("↑210/~7000~", "climb, 210 knots, predicted 7000")]
    [InlineData("↓/8000A", "descend, 8000 or above")]
    public void Constraint_SmallTypePredictionsAreSaidToBePredictions(string raw, string spoken)
        => Assert.Equal(spoken, A220FmsLegParsing.FormatConstraint(raw));

    [Theory]
    [InlineData("/9000A", true)]
    [InlineData("/9000B", true)]
    [InlineData("/9000", true)]
    [InlineData("/FL120", true)]
    [InlineData("250/", true)]
    [InlineData("250/FL100", true)]
    [InlineData("250/9000", true)]
    [InlineData("/", false)]
    [InlineData("KEA", false)]
    [InlineData("KEA/090/10", false)]
    [InlineData("5530N", false)]
    public void ConstraintEntry_DistinguishedFromWaypointEntry(string text, bool isConstraint)
        => Assert.Equal(isConstraint, A220FmsForm.IsConstraintEntry(text));
}
