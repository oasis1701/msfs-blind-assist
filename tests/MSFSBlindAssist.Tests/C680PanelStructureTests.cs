using MSFSBlindAssist.Aircraft.Citation680;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Structural checks for the Skyward Citation Sovereign+ panel tree. The load-bearing one is
/// <see cref="EveryPanelHasAControlsEntry"/>: a panel named in GetPanelStructure() but absent
/// from BuildPanelControls() renders COMPLETELY BLANK with no error anywhere.
/// </summary>
public class C680PanelStructureTests
{
    private static SkywardC680Definition Def() => new();

    [Fact]
    public void EveryPanelHasAControlsEntry()
    {
        var def = Def();
        var controls = def.GetPanelControls();
        var missing = def.GetPanelStructure().SelectMany(s => s.Value).Where(p => !controls.ContainsKey(p)).ToList();
        Assert.True(missing.Count == 0, "These panels would render blank: " + string.Join(", ", missing));
    }

    [Fact]
    public void PanelNamesAreUniqueAcrossSections()
    {
        var all = Def().GetPanelStructure().SelectMany(s => s.Value).ToList();
        var dupes = all.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "Duplicate panel names: " + string.Join(", ", dupes));
    }

    [Fact]
    public void SectionsFollowTheCockpit()
    {
        var expected = new[] { "Glareshield", "Left Tilt Panel", "Right Tilt Panel", "Pedestal", "Avionics", "Side Consoles", "Cabin and Ground", "Simulation" };
        Assert.Equal(expected, Def().GetPanelStructure().Keys.ToArray());
    }

    [Fact]
    public void IdentityIsReported()
    {
        Assert.Equal("SKYWARD_C680", Def().AircraftCode);
        Assert.Equal("Skyward Citation Sovereign+", Def().AircraftName);
    }

    /// <summary>
    /// A panel is EITHER built OR on this list, never silently empty. Building a panel takes it
    /// off the list; the mirror assertion fails when a built panel is still listed, so the list
    /// cannot go stale in either direction.
    /// </summary>
    public static readonly string[] NotBuiltYet = Array.Empty<string>();

    [Fact]
    public void EveryPanelIsBuiltOrKnownUnbuilt()
    {
        var def = Def();
        var controls = def.GetPanelControls();
        var display = def.GetPanelDisplayVariables();
        foreach (var panel in def.GetPanelStructure().SelectMany(s => s.Value))
        {
            bool built = (controls.TryGetValue(panel, out var c) && c.Count > 0) || (display.TryGetValue(panel, out var d) && d.Count > 0);
            bool listed = NotBuiltYet.Contains(panel);
            Assert.True(built != listed, built ? $"'{panel}' is built but still listed in NotBuiltYet" : $"'{panel}' is empty and not listed in NotBuiltYet");
        }
    }

    [Fact]
    public void SeatMapsToTheRightUnits()
    {
        Assert.Equal(1, C680Seat.GtcIndexFor(C680Seat.Side.Pilot, isMfd: false));
        Assert.Equal(4, C680Seat.GtcIndexFor(C680Seat.Side.Copilot, isMfd: false));
        Assert.Equal(2, C680Seat.GtcIndexFor(C680Seat.Side.Pilot, isMfd: true));
        Assert.Equal(3, C680Seat.GtcIndexFor(C680Seat.Side.Copilot, isMfd: true));
        Assert.Equal(2, C680Seat.PfdIndex(C680Seat.Side.Copilot));
        Assert.Equal(C680Seat.Side.Copilot, C680Seat.Other(C680Seat.Side.Pilot));
    }
}
