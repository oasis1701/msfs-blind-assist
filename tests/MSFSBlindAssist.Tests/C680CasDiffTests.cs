using MSFSBlindAssist.Aircraft.Citation680;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The Sovereign+ CAS diff and phrasing, against rows the live agent produced 2026-09-10.</summary>
public class C680CasDiffTests
{
    private static List<(string cls, string text)> L(params string[] items)
        => items.Select(s => (s.Split(':')[0], s.Split(':')[1])).ToList();

    [Fact]
    public void PostedAndClearedAreTheSetDifferences()
    {
        var (posted, cleared) = C680CasDiff.Diff(L("caution:PARK BRAKE LOW PRESS", "advisory:NO TAKEOFF"), L("caution:PARK BRAKE LOW PRESS", "warning:ENGINE FIRE L"));
        Assert.Single(posted); Assert.Equal(("warning", "ENGINE FIRE L"), posted[0]);
        Assert.Single(cleared); Assert.Equal(("advisory", "NO TAKEOFF"), cleared[0]);
    }

    [Fact]
    public void AClassChangeOnTheSameTextIsAPostAndAClear()
    {
        var (posted, cleared) = C680CasDiff.Diff(L("advisory:FUEL LEVEL LOW L-R"), L("caution:FUEL LEVEL LOW L-R"));
        Assert.Equal(("caution", "FUEL LEVEL LOW L-R"), posted.Single());
        Assert.Equal(("advisory", "FUEL LEVEL LOW L-R"), cleared.Single());
    }

    [Fact]
    public void PhrasesLeadWithTheClass()
    {
        Assert.Equal("Warning: ENGINE FIRE L", C680CasDiff.Phrase("warning", "ENGINE FIRE L", posted: true));
        Assert.Equal("Caution cleared: PARK BRAKE ON", C680CasDiff.Phrase("caution", "PARK BRAKE ON", posted: false));
        Assert.Equal("Advisory: NO TAKEOFF", C680CasDiff.Phrase("advisory", "NO TAKEOFF", posted: true));
    }

    [Fact]
    public void DuplicatesInOneSnapshotCountOnce()
    {
        var (posted, _) = C680CasDiff.Diff(L(), L("status:P/S HEAT ON", "status:P/S HEAT ON"));
        Assert.Single(posted);
    }

    [Fact]
    public void AgentRowsParseAndOrderBySeverity()
    {
        Assert.Equal(("caution", "FUEL IMBALANCE"), C680CasDiff.ParseRow("caution: FUEL IMBALANCE"));
        Assert.Equal(("status", "CHECK CAS"), C680CasDiff.ParseRow("CHECK CAS"));
        var lines = C680CasDiff.Lines(L("advisory:NO TAKEOFF", "warning:ENGINE FIRE L", "caution:FUEL IMBALANCE"));
        Assert.Equal(new[] { "Warning: ENGINE FIRE L", "Caution: FUEL IMBALANCE", "Advisory: NO TAKEOFF" }, lines);
        Assert.Equal(new[] { "No CAS messages" }, C680CasDiff.Lines(L()));
    }
}
