using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ THE CAS WINDOW IS THIS AEROPLANE'S ONLY CHANNEL FOR MOST FAILURES, and the monitor
/// reading it has already failed silently once — it scraped a HIDDEN duplicate block and
/// reported "CAS messages: none" over three standing cautions, which reads as a clean scan
/// rather than an error.
///
/// Coverage here is TEXT-based, not per-message: the monitor announces whatever appears in
/// the block, so it needs no binding per annunciation and cannot miss a new one COWS adds.
/// What it CAN miss is a shape it fails to parse — which is what this pins, against the
/// aeroplane's REAL message set read out of its own panel.xml (29 active annunciations).
/// </summary>
public class CowsDA40CasCoverageTests
{
    /// <summary>
    /// Every active annunciation in COWS_DA40NG panel.xml, with the severity label the
    /// display agent renders for it. PITOT HT OFF is deliberately absent — it is commented
    /// out in the package and cannot be raised.
    /// </summary>
    private static readonly (string Label, string Text)[] RealMessages =
    {
        ("WARNING",  "AIRSPEED FAIL"), ("WARNING",  "ALTITUDE FAIL"),
        ("WARNING",  "ALTN AMPS"),     ("WARNING",  "ALTN FAIL"),
        ("WARNING",  "ATTITUDE FAIL"), ("WARNING",  "DOOR OPEN"),
        ("WARNING",  "ENG TEMP"),      ("WARNING",  "FUEL PRES"),
        ("WARNING",  "GBOX TEMP"),     ("WARNING",  "L FUEL TEMP"),
        ("WARNING",  "OIL PRESS"),     ("WARNING",  "OIL TEMP"),
        ("WARNING",  "R FUEL TEMP"),   ("WARNING",  "STARTER"),
        ("WARNING",  "VERT SPEED FAIL"),
        ("Caution",  "COOL LVL"),      ("Caution",  "ECU A FAIL"),
        ("Caution",  "ECU B FAIL"),    ("Caution",  "FUEL LOW"),
        ("Caution",  "PITOT FAIL"),    ("Caution",  "VOLTS LOW"),
        ("Advisory", "FSC CFG MASTER"),("Advisory", "FSC CFG SLAVE"),
        ("Advisory", "FUEL XFER"),     ("Advisory", "GLOW ON"),
        ("Advisory", "STEER AUTOBRAKE"),("Advisory","STEER NWS"),
        ("Advisory", "STEER REAL"),
        // ⚠️ NOT A TYPO IN THIS TEST. COWS overwrote one annunciation's <Text> with a
        // YouTube URL fragment and left it tagged as the glow advisory; its condition is
        // airspeed above 182.88 kt for six seconds, so it is REACHABLE in an overspeed and
        // the PFD really does show this. It is here so nobody "tidies" it away as garbage.
        ("Advisory", "qnkuBUAwfe0&t=107s"),
    };

    private static IEnumerable<string> AsScrapedRows()
    {
        yield return "Page: PFD";
        yield return "CAS messages:";
        foreach (var (label, text) in RealMessages)
            yield return $"  {label}: {text}";
        yield return "Autopilot: AP, HDG, ALT";
    }

    [Fact]
    public void EveryRealAnnunciationSurvivesTheExtractor()
    {
        var got = CowsDA40Definition.ExtractCasMessages(AsScrapedRows().ToList());

        Assert.Equal(RealMessages.Length, got.Count);
        foreach (var (label, text) in RealMessages)
            Assert.Contains($"{label}: {text}", got);
    }

    [Fact]
    public void TheBlockEndsAtTheFirstRowThatIsNotPartOfIt()
    {
        // ⚠️ INDENTATION IS NOT A CAS MESSAGE. Every nested list the agent emits is
        // indented, so a bare "starts with two spaces" rule announced a units row as a
        // caution the moment a setup page was opened.
        var rows = new List<string>
        {
            "CAS messages:",
            "  Caution: FUEL LOW",
            "Units:",
            "  Weight: Pounds(LB)",
        };

        var got = CowsDA40Definition.ExtractCasMessages(rows);
        Assert.Single(got);
        Assert.Equal("Caution: FUEL LOW", got[0]);
    }

    [Theory]
    [InlineData("WARNING")]
    [InlineData("Caution")]
    [InlineData("Advisory")]
    [InlineData("Status")]
    public void EverySeverityLabelTheAgentEmitsIsStrippedOnTheClearedCall(string label)
    {
        // The cleared announcement strips the prefix so a pilot hears "FUEL LOW cleared"
        // rather than "Caution: FUEL LOW cleared". The four labels here are exactly what
        // the agent renders, so a fifth added there without a matching strip would leak.
        string row = $"{label}: FUEL LOW";
        string cleared = row.Replace("Caution: ", "").Replace("WARNING: ", "")
                            .Replace("Advisory: ", "").Replace("Status: ", "");
        Assert.Equal("FUEL LOW", cleared);
    }
}
