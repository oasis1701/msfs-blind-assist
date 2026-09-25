using MSFSBlindAssist.Aircraft.Citation680;
using System.Windows.Forms;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The touchscreen window's row model and key map, against rows the live agent produced 2026-09-10.</summary>
public class C680GtcRowsTests
{
    private static readonly string[] SpeedBugs =
    {
        "Page: Speed Bugs", "Takeoff", "Landing", "Pilot COM1 Volume | COM1 Freq Push:1-2 Hold:", "[Audio & Radios]", "[Intercom] (disabled)",
        "[All On]", "[All Off] (disabled)", "[V1]", "[110 KT]", "[Back]", "[Home]", "Knobs: COM1 Freq Push:1-2 Hold: / Pilot COM1 Volume"
    };

    [Fact]
    public void ParsesTitleTextButtonsAndKnobs()
    {
        var rows = C680GtcRows.Parse(SpeedBugs);
        Assert.Equal(C680GtcRows.Kind.Title, rows[0].Kind);
        Assert.Equal("Speed Bugs", rows[0].Label);
        Assert.Equal("Speed Bugs", C680GtcRows.TitleOf(rows));
        Assert.Equal(C680GtcRows.Kind.Text, rows[1].Kind);
        var allOff = rows.Single(r => r.Label == "All Off");
        Assert.Equal(C680GtcRows.Kind.Button, allOff.Kind);
        Assert.False(allOff.Enabled);
        Assert.Equal(3, allOff.ButtonIndex);   // fourth [ ] row → index 3 in the agent's button list
        Assert.Equal(C680GtcRows.Kind.Knobs, rows[^1].Kind);
        Assert.Equal("COM1 Freq Push:1-2 Hold: / Pilot COM1 Volume", rows[^1].Label);
    }

    [Fact]
    public void KeyboardPageIsRecognisedByItsLetters()
    {
        var kb = C680GtcRows.Parse(new[] { "Page: Add Origin", "[A]", "[B]", "[C]", "[Backspace]", "[Enter]" });
        Assert.True(C680GtcRows.IsKeyboardPage(kb));
        var pad = C680GtcRows.Parse(new[] { "Page: Transponder", "[0]", "[9]", "[Enter]" });
        Assert.True(C680GtcRows.IsKeyboardPage(pad));
        Assert.False(C680GtcRows.IsKeyboardPage(C680GtcRows.Parse(SpeedBugs)));
    }

    [Fact]
    public void MinimumsKeypadWithBkspAndNoEnterIsAKeypad()
    {
        var mins = C680GtcRows.Parse(new[] { "Page: Minimums", "[0]", "[1]", "[9]", "[BKSP]", "[Minimums Baro]" });
        Assert.True(C680GtcRows.IsKeyboardPage(mins));
        Assert.Equal("BKSP", C680GtcRows.KeyToButtonLabel(Keys.Back, keyboardUp: true, mins));
        var kb = C680GtcRows.Parse(new[] { "Page: Add Origin", "[A]", "[B]", "[C]", "[Backspace]", "[Enter]" });
        Assert.Equal("Backspace", C680GtcRows.KeyToButtonLabel(Keys.Back, keyboardUp: true, kb));
    }

    [Fact]
    public void BarsCanBeHiddenWithoutMovingButtonIndices()
    {
        var rows = C680GtcRows.Parse(new[] { "Page: Services", "[Music] (disabled)", "[ACARS]", "Radio bar:", "[Audio & Radios]", "[COM1 124.850]", "Bottom bar:", "[Back]", "[Home]", "Knobs: a / b" });
        var kept = C680GtcRows.WithoutBars(rows);
        Assert.Equal(new[] { "Page: Services", "[Music] (disabled)", "[ACARS]", "Knobs: a / b" }, kept.Select(r => r.Raw).ToArray());
        Assert.Equal(1, kept.Single(r => r.Label == "ACARS").ButtonIndex);
        Assert.Equal(4, rows.Single(r => r.Label == "Back").ButtonIndex);
    }

    [Fact]
    public void APressThatRelabelsItsButtonSpeaksTheNewLabel()
    {
        var after = C680GtcRows.Parse(new[] { "Page: PFD Home", "[Nav Source LOC1]", "[Bearing 1 OFF]" });
        Assert.Equal("Nav Source LOC1", C680GtcRows.SpokenAfterPress(after, 1, "Nav Source FMS"));
        Assert.Equal("Bearing 1 OFF", C680GtcRows.SpokenAfterPress(after, 2, "Bearing 1 OFF"));
        Assert.Equal("Home", C680GtcRows.SpokenAfterPress(after, -1, "Home"));
    }

    [Fact]
    public void TypedKeysMapToOnScreenLabels()
    {
        Assert.Equal("K", C680GtcRows.KeyToButtonLabel(Keys.K, keyboardUp: true));
        Assert.Equal("7", C680GtcRows.KeyToButtonLabel(Keys.D7, keyboardUp: true));
        Assert.Equal("7", C680GtcRows.KeyToButtonLabel(Keys.NumPad7, keyboardUp: true));
        Assert.Equal("Backspace", C680GtcRows.KeyToButtonLabel(Keys.Back, keyboardUp: true));
        Assert.Equal("Enter", C680GtcRows.KeyToButtonLabel(Keys.Enter, keyboardUp: true));
        Assert.Null(C680GtcRows.KeyToButtonLabel(Keys.K, keyboardUp: false));
        Assert.Null(C680GtcRows.KeyToButtonLabel(Keys.Control | Keys.K, keyboardUp: true));   // chords are never typing
    }

    // Rows the agent produced on the live Active Flight Plan page (EGNX–EKCH), 2026-09-15.
    private static readonly string[] FlightPlan =
    {
        "Page: Active Flight Plan", "EGNX / EKCH | ALT | FPA/SPD", "[PROC]", "[Standby Flight Plan] (disabled)",
        "[Departure – EGNX–RW27.TNT2N]", "[RW27] {alt=-1;spd=54}",
        "[EME01, active leg, fly-over, at or above 810 feet, climb] {alt=55;spd=56}",
        "[ABEGI, at or below 4000 feet, angle -3.00 degrees, at 220 knots] {alt=95;spd=96}",
        "Bottom bar:", "[Back]", "Knobs: a / b"
    };

    [Fact]
    public void AFlightPlanLegRowCarriesItsBoxIndicesAndHidesTheSuffix()
    {
        var rows = C680GtcRows.Parse(FlightPlan);
        var eme = rows.Single(r => r.Label.StartsWith("EME01", StringComparison.Ordinal));
        Assert.Equal("EME01, active leg, fly-over, at or above 810 feet, climb", eme.Label);
        Assert.Equal("[EME01, active leg, fly-over, at or above 810 feet, climb]", eme.Display);
        Assert.True(eme.IsFlightPlanLeg);
        Assert.Equal(55, eme.AltButtonIndex);
        Assert.Equal(56, eme.SpeedButtonIndex);
        Assert.Equal(4, eme.ButtonIndex);   // the leg button keeps its place among the listed buttons

        var rwy = rows.Single(r => r.Label == "RW27");
        Assert.Equal(-1, rwy.AltButtonIndex);   // a runway leg has no altitude box
        Assert.Equal(54, rwy.SpeedButtonIndex);

        var proc = rows.Single(r => r.Label == "PROC");
        Assert.False(proc.IsFlightPlanLeg);
        Assert.Equal("[PROC]", proc.Display);
        Assert.False(rows.Single(r => r.Label == "Standby Flight Plan").Enabled);
    }

    [Fact]
    public void ADisabledLegRowStillParsesItsIndices()
    {
        var row = C680GtcRows.Parse(new[] { "[MANSEQ] (disabled) {alt=-1;spd=97}" }).Single();
        Assert.Equal("MANSEQ", row.Label);
        Assert.False(row.Enabled);
        Assert.Equal(97, row.SpeedButtonIndex);
        Assert.Equal("[MANSEQ] (disabled)", row.Display);
    }

    [Fact]
    public void AAndSPressTheLegBoxesOnlyOffKeyboardPages()
    {
        Assert.Equal("altitude", C680GtcRows.KeyToLegBox(Keys.A, keyboardUp: false));
        Assert.Equal("speed", C680GtcRows.KeyToLegBox(Keys.S, keyboardUp: false));
        Assert.Null(C680GtcRows.KeyToLegBox(Keys.A, keyboardUp: true));   // letters type on a keyboard page
        Assert.Null(C680GtcRows.KeyToLegBox(Keys.Shift | Keys.A, keyboardUp: false));
        Assert.Null(C680GtcRows.KeyToLegBox(Keys.D, keyboardUp: false));
    }

    [Fact]
    public void PageSummaryReadsTheTitleAndTextButNotTheBars()
    {
        // Weight and Fuel, Landing tab, 2026-09-15.
        var rows = C680GtcRows.Parse(new[]
        {
            "Page: Weight and Fuel", "Est. Landing Weight | blank | LB", "minus | 0 | LB", "[Landing tab, selected]", "[Fuel Reserves 0 GAL]",
            "Radio bar:", "[COM1 124.850]", "Bottom bar:", "[Back]", "Knobs: a / b"
        });
        Assert.Equal("Weight and Fuel. Est. Landing Weight, blank, LB. minus, 0, LB", C680GtcRows.PageSummary(rows));
    }

    [Fact]
    public void PageSummaryOfAButtonOnlyPageCountsItsOwnButtons()
    {
        var rows = C680GtcRows.Parse(new[] { "Page: Services", "[Music] (disabled)", "[ACARS]", "Bottom bar:", "[Back]", "[Home]", "Knobs: a / b" });
        Assert.Equal("Services. 2 buttons", C680GtcRows.PageSummary(rows));
    }

    [Fact]
    public void KnobChordsMapToTheVerticalGtcEvents()
    {
        Assert.Equal("RightKnob_Small_INC", C680GtcRows.KeyToKnob(Keys.Control | Keys.Right));
        Assert.Equal("RightKnob_Large_DEC", C680GtcRows.KeyToKnob(Keys.Control | Keys.Down));
        Assert.Equal("RightKnob_Push", C680GtcRows.KeyToKnob(Keys.Control | Keys.Enter));
        Assert.Equal("RightKnob_Push_Long", C680GtcRows.KeyToKnob(Keys.Control | Keys.Shift | Keys.Enter));
        Assert.Equal("MiddleKnob_INC", C680GtcRows.KeyToKnob(Keys.Alt | Keys.Up));
        Assert.Equal("MiddleKnob_Push", C680GtcRows.KeyToKnob(Keys.Alt | Keys.Enter));
        Assert.Equal("Joystick_Left", C680GtcRows.KeyToKnob(Keys.Control | Keys.Shift | Keys.Left));
        Assert.Null(C680GtcRows.KeyToKnob(Keys.Right));
        Assert.Equal("right knob small increase", C680GtcRows.DescribeKnob("RightKnob_Small_INC"));
    }
}
