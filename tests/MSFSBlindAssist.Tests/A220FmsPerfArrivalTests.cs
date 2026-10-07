using MSFSBlindAssist.Aircraft.A220;
using static MSFSBlindAssist.Aircraft.A220.A220FmsScreenParsing;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// PERF ▸ ARR page, from the live LGAV scrape of 2026-09-24. The "°C" unit beside
/// OAT used to act as a label and steal the RWY WIND box, so the page read the wind
/// as "°C" and QNH/ALT as "°C QNH"/"°C ALT".
/// </summary>
public class A220FmsPerfArrivalTests
{
    private static FmsModel Parse()
    {
        var tokens = new List<WinToken>
        {
            new("FMS1", 8, 29, "magenta"),
            new("PERF", 537, 26, "white", Clickable: true),
            new("ARR", 637, 101, "white", Clickable: true),
            new("TRANS", 8, 152, "gray"),
            new("STAR", 117, 152, "gray"),
            new("NEME1Q", 117, 188, "white"),
            new("TRANS", 227, 152, "gray"),
            new("DDM", 227, 188, "white"),
            new("APPR", 352, 152, "gray"),
            new("ILS Y 03R", 352, 188, "white"),
            new("AIRPORT (PERF)", 8, 296, "gray"),
            new("LGAV", 24, 333, "gray"),
            new("OAT", 8, 394, "gray"),
            new("+18", 19, 432, "white"),
            new("°C", 81, 436, "gray"),
            new("RWY WIND", 182, 394, "gray"),
            new("---T/---", 191, 431, "white"),
            new("/", 354, 434, "white"),
            new("QNH", 524, 394, "gray"),
            new("30.15", 534, 431, "white"),
            new("ALT", 632, 394, "gray"),
            new("97", 641, 434, "white"),
            new("VREF", 277, 590, "gray"),
            new("125", 292, 631, "magenta", Min: 100, Max: 200),
            new("+", 351, 632, "white"),
            new("--", 398, 628, "white", Min: 0, Max: 50),
        };
        var boxes = new List<WinBox>
        {
            new("vbox", 10, 427, "", 63, 33),
            new("vbox", 184, 426, "", 139, 33),
            new("vbox", 525, 426, "", 86, 33),
            new("vbox", 634, 426, "", 93, 33),
            new("vbox", 279, 623, "", 63, 33),
            new("vbox", 372, 623, "", 63, 33),
        };
        return ParseFms(tokens, boxes);
    }

    [Fact]
    public void UnitNeverStealsTheNextFieldsBox()
    {
        var m = Parse();
        Assert.Equal("+18 °C", m.Fields.Single(f => f.Label == "OAT").Value);
        Assert.Equal("---T/---", m.Fields.Single(f => f.Name == "RWY WIND").Value);
        Assert.Equal("30.15", m.Fields.Single(f => f.Name == "QNH").Value);
        Assert.Equal("97", m.Fields.Single(f => f.Name == "ALT").Value);
        Assert.DoesNotContain(m.Fields, f => f.Name.Contains("°C"));
    }

    [Fact]
    public void TransitionsAreNamedAndEmptyOneSaysNone()
    {
        var m = Parse();
        Assert.Equal("DDM", m.Fields.Single(f => f.Name == "approach transition").Value);
        Assert.Contains("STAR transition: none", m.OrphanLines);
    }

    [Fact]
    public void VrefAdditiveAndAirportReadAsOneRowEach()
    {
        var m = Parse();
        Assert.Equal("--", m.Fields.Single(f => f.Name == "VREF additive").Value);
        Assert.Contains("AIRPORT (PERF): LGAV", m.OrphanLines);
        Assert.DoesNotContain(m.OrphanLines, l => l.Trim() is "+" or "/" or "LGAV");
    }
}
