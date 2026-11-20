using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ A CONTROL IS NAMED AS THE COCKPIT NAMES IT. The source is the model's own tooltip for
/// the control (COWS_DA40*_IN.xml), never a paraphrase of what the control does. Where the two
/// airframes' cockpits differ, the names differ with them.
/// </summary>
public class CowsDA40CockpitNameTests
{
    private static string Name(DA40Variant v, string key) => new CowsDA40Definition(v).GetVariables()[key].DisplayName;

    [Fact]
    public void TheMastersCarryEachCockpitsOwnPlacard()
    {
        // The NG's key detent reads ELECTRIC MASTER; the XLS has a split rocker.
        Assert.Equal("Electric Master", Name(DA40Variant.NG, "DA40_ELEC_MASTER_BATTERY"));
        Assert.Equal("Battery Master", Name(DA40Variant.XLS, "DA40_ELEC_MASTER_BATTERY"));
        Assert.Equal("Alternator Master", Name(DA40Variant.XLS, "DA40_ELEC_ALT_MASTER"));
    }

    [Fact]
    public void TheAlternatorMasterIsAnXlsControlAndNothingOnTheNg()
    {
        var xls = new CowsDA40Definition(DA40Variant.XLS);
        Assert.Contains("DA40_ELEC_ALT_MASTER", xls.GetPanelControls()["Electrical"]);
        Assert.Equal("GENERAL ENG MASTER ALTERNATOR:1", xls.GetVariables()["DA40_ELEC_ALT_MASTER"].Name);

        // On the NG that SimVar is the ENGINE master, which lives on Engine Start.
        var ng = new CowsDA40Definition(DA40Variant.NG);
        Assert.False(ng.GetVariables().ContainsKey("DA40_ELEC_ALT_MASTER"));
        Assert.DoesNotContain("DA40_ELEC_ALT_MASTER", ng.GetPanelControls()["Electrical"]);
    }

    [Fact]
    public void TheXlsMastersInterlockFromTheStateReadBeforeToggling()
    {
        // Battery OFF takes the alternator with it, and the alternator's state is READ before
        // either toggle — a read after the toggle sees the old value (measured live), which is
        // why the cockpit click code's own interlock never fires.
        string batOff = CowsDA40Definition.XlsBatteryMasterCode(false);
        Assert.True(batOff.IndexOf("(A:GENERAL ENG MASTER ALTERNATOR:1, Bool) if{", System.StringComparison.Ordinal)
                    < batOff.IndexOf("(>K:TOGGLE_MASTER_BATTERY)", System.StringComparison.Ordinal));

        // Alternator ON brings the battery with it, the battery read before any toggle.
        string altOn = CowsDA40Definition.XlsAlternatorMasterCode(true);
        Assert.True(altOn.IndexOf("(A:ELECTRICAL MASTER BATTERY:1, Bool) !", System.StringComparison.Ordinal)
                    < altOn.IndexOf("(>K:TOGGLE_ALTERNATOR1)", System.StringComparison.Ordinal));

        // The other two directions touch only their own half.
        Assert.DoesNotContain("TOGGLE_ALTERNATOR1", CowsDA40Definition.XlsBatteryMasterCode(true));
        Assert.DoesNotContain("TOGGLE_MASTER_BATTERY", CowsDA40Definition.XlsAlternatorMasterCode(false));

        // ALTERNATOR1_SET was measured inert on this aircraft.
        Assert.DoesNotContain("ALTERNATOR1_SET", altOn);

        // Each is guarded by a read-compare, so picking the position already set does nothing.
        Assert.StartsWith("(A:GENERAL ENG MASTER ALTERNATOR:1, Bool) 0 ==", altOn);
        Assert.StartsWith("(A:ELECTRICAL MASTER BATTERY:1, Bool) 1 ==", batOff);
    }

    [Theory]
    [InlineData("DA40_DOOR_CANOPY", "Canopy")]
    [InlineData("DA40_DOOR_REAR", "Rear Canopy")]
    [InlineData("DA40_DOOR_STORM_L", "Left Window")]
    [InlineData("DA40_DOOR_STORM_R", "Right Window")]
    [InlineData("DA40_ELEC_ESS_BUS", "ESS BUS")]
    [InlineData("DA40_TRIM_AP_DISC", "AP DISC")]
    public void SharedControlsCarryTheirTooltip(string key, string expected)
    {
        Assert.Equal(expected, Name(DA40Variant.NG, key));
        Assert.Equal(expected, Name(DA40Variant.XLS, key));
    }

    [Fact]
    public void TheEssBusSwitchHasItsOwnTwoPositionsNotAnExplanation()
    {
        var d = new CowsDA40Definition(DA40Variant.NG).GetVariables()["DA40_ELEC_ESS_BUS"];
        Assert.Equal(new[] { "Off", "On" }, d.ValueDescriptions.OrderBy(k => k.Key).Select(k => k.Value));
    }
}
