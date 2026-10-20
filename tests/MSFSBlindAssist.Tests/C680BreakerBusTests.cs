using MSFSBlindAssist.Aircraft.Citation680;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The breakers are grouped by the bus each one hangs off, read from its electrical line.</summary>
public class C680BreakerBusTests
{
    [Theory]
    [InlineData("LH_ELEC_BUS_3_HZ107_To_L_TR_EMER_STOW_HC019_CB", "LH_ELEC_BUS_3")]
    [InlineData("STBY_EQUIP_BUS_HZ301_To_STBY_HC100_CB", "STBY_EQUIP_BUS")]
    [InlineData("RH_AVN_BUS_2_HZ208_To_COM_2_HC150_CB", "RH_AVN_BUS_2")]
    public void BusIsTheLineBeforeItsJunction(string line, string bus)
        => Assert.Equal(bus, C680BreakerBuses.BusOf(line));

    [Theory]
    [InlineData("LH_ELEC_BUS_3", "Left ELEC Bus 3 Breakers")]
    [InlineData("RH_EMER_BUS_2", "Right EMER Bus 2 Breakers")]
    [InlineData("STBY_EQUIP_BUS", "STBY EQUIP Bus Breakers")]
    public void PanelTitlesNameTheBus(string bus, string title)
        => Assert.Equal(title, C680BreakerBuses.PanelTitle(bus));

    [Fact]
    public void EveryBreakerIsInExactlyOneBusPanel()
    {
        var panels = C680BreakerBuses.Panels();
        var all = panels.SelectMany(p => p.Keys).ToList();
        Assert.Equal(C680BreakerTable.Rows.Count, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(11, panels.Count);
    }

    [Fact]
    public void PanelsFollowTheModelsOrder()
        => Assert.Equal("Left ELEC Bus 3 Breakers", C680BreakerBuses.Panels()[0].Title);
}
