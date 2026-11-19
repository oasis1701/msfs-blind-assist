// Tests for MSFSBlindAssist.Navigation.HoldShortTargetList: the ONE list a pilot picks a
// "hold short of" target from, taxiways and named holding points together.
//
// LOWW, 2026-11-14: ATC said "taxi via L, hold short of L2". L2 is a painted intermediate
// holding point, not a taxiway, so it was in neither hold-short list; it sat under its own
// Progressive Taxi choice ("Hold at named holding point"), which the instruction's wording
// gave the pilot no reason to open.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class HoldShortTargetListTests
{
    private static NamedHoldingPoint Point(string name, string kind = "intermediate", int node = 1)
        => new() { Name = name, Kind = kind, NodeId = node };

    private static string[] Labels(IEnumerable<HoldShortTargetList.Entry> entries)
        => entries.Select(e => e.Label).ToArray();

    [Fact]
    public void Merge_PutsEachHoldingPointAmongTheTaxiwaysInNameOrder()
    {
        var merged = HoldShortTargetList.Merge(
            new[] { "A1", "L", "M" },
            new[] { Point("L2"), Point("M1"), Point("L1") });

        Assert.Equal(
            new[] { "A1", "L", "L1 (holding point)", "L2 (holding point)", "M", "M1 (holding point)" },
            Labels(merged));
    }

    // LOWW has both a taxiway A1 and a painted holding point A1 on it. They are different
    // stops (the junction with A1, and the line painted on A1), so both are listed, the
    // taxiway first.
    [Fact]
    public void Merge_ListsATaxiwayBeforeTheHoldingPointThatSharesItsName()
    {
        var merged = HoldShortTargetList.Merge(new[] { "A1", "A2" }, new[] { Point("A1", "runway") });

        Assert.Equal(new[] { "A1", "A1 (holding point)", "A2" }, Labels(merged));
    }

    // A taxiway is listed by its bare name, in every list: only a holding point is marked.
    // The per-row list read "Taxiway L" until 2026-11-14, which put every taxiway under T
    // for first-letter navigation; the name alone lets L land on L, L1, L2 (owner's call).
    [Fact]
    public void Merge_ListsATaxiwayByItsNameAlone()
    {
        var merged = HoldShortTargetList.Merge(new[] { "L" }, new[] { Point("L2") });

        Assert.Equal(new[] { "L", "L2 (holding point)" }, Labels(merged));
    }

    // The combos hold the entries themselves, so a list shows an entry by its label, and
    // the entry (never its text) says whether a pick is a taxiway or a holding point: the
    // per-row list also holds runway designators, and a compass-point runway "E" beside a
    // taxiway "E" reads the same.
    [Fact]
    public void AnEntryIsShownByItsLabel()
    {
        var merged = HoldShortTargetList.Merge(new[] { "L" }, new[] { Point("L2") });

        Assert.Equal("L", merged[0].ToString());
        Assert.Equal("L2 (holding point)", merged[1].ToString());
    }

    [Fact]
    public void Merge_KeepsTheTaxiwaysInTheOrderGiven()
    {
        var merged = HoldShortTargetList.Merge(new[] { "B", "A" }, Array.Empty<NamedHoldingPoint>());

        Assert.Equal(new[] { "B", "A" }, Labels(merged));
    }

    [Fact]
    public void Merge_EntriesSayWhichTheyAre()
    {
        var l2 = Point("L2", node: 1845);
        var merged = HoldShortTargetList.Merge(new[] { "L" }, new[] { l2 });

        Assert.Null(merged[0].HoldingPoint);
        Assert.Equal("L", merged[0].Name);
        Assert.Same(l2, merged[1].HoldingPoint);
        Assert.Equal("L2", merged[1].Name);
    }

    // A point read off an X-Plane sign has an approximate position ([HLD-18]); the label
    // has to keep saying so wherever the point is offered.
    [Fact]
    public void HoldingPointLabel_KeepsTheSignCaveat()
    {
        Assert.Equal(
            "KK (holding point, from sign, approximate)",
            HoldShortTargetList.HoldingPointLabel(Point("KK", NamedHoldingPointResolver.SignKind)));
    }

    [Fact]
    public void Merge_ListsAHoldingPointNameOnce()
    {
        var merged = HoldShortTargetList.Merge(
            Array.Empty<string>(), new[] { Point("L2", node: 1), Point("l2", node: 2) });

        Assert.Single(merged);
        Assert.Equal(1, merged[0].HoldingPoint!.NodeId);
    }

    [Fact]
    public void Merge_AcceptsNothing()
    {
        Assert.Empty(HoldShortTargetList.Merge(null, null));
    }
}
