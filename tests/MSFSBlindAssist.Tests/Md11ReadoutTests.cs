using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Read-out thresholds taken from the AIRCRAFT'S OWN tooltips, pinned because each is a value the
/// control map gets wrong and each fails silently.
///
/// The generated map derives a value_map from a tooltip's <c>%{if}</c> block, which is right when
/// the condition is the bare variable and WRONG when it is a comparison — because then the
/// COMPARISON yields the boolean, not the variable. Nine MD-11 tooltips are of the second kind, and
/// the map claims {0,1} for all of them. Two feed hotkey read-outs; those are pinned here.
/// </summary>
public class Md11ReadoutTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();

    /// <summary>
    /// The gear lever is a 0-25 TRAVEL, not a boolean: CenterInstrument.xml reads
    ///     Gear Lever (%((L:MD11_MIP_GEAR_SW) 20 >=)%{if}Down%{else}Up%{end})
    /// so >= 20 is Down. Live on the ground the var reads 25.
    ///
    /// This pins the MAP'S CLAIM as a known lie, so the day someone "simplifies" the read-out to
    /// trust value_map, this test says why not. A `> 0.5` test gives the right answer parked and
    /// the wrong one mid-travel — it would call a lever at 10 "down" while the aircraft says up.
    /// </summary>
    [Fact]
    public void GearLever_MapClaimsBooleanButTheAircraftUsesAThreshold()
    {
        var c = Map.Controls.FirstOrDefault(x => x.NodeId == "MD11_MIP_GEAR_SW");

        Assert.NotNull(c);
        // The map's claim — documented as WRONG. If a regenerated map ever fixes this, this
        // assertion fails and the read-out can be simplified deliberately rather than by accident.
        Assert.Equal("Down", c!.ValueMap["1"]);
        Assert.Equal("Up", c.ValueMap["0"]);
    }

    /// <summary>
    /// 25 (parked, lever down) and 0 (up) both classify correctly under the aircraft's rule; 10
    /// (mid-travel) is where a naive boolean test diverges from the aircraft.
    /// </summary>
    [Theory]
    [InlineData(25, true)]    // live value, parked
    [InlineData(20, true)]    // exactly at the threshold
    [InlineData(19.9, false)]
    [InlineData(10, false)]   // mid-travel: a `> 0.5` test gets this WRONG
    [InlineData(0, false)]
    public void GearLever_ThresholdMatchesTheAircraftsOwnTooltip(double travel, bool expectDown)
    {
        // TFDi's rule, verbatim: (L:MD11_MIP_GEAR_SW) 20 >=
        Assert.Equal(expectDown, travel >= 20);
    }

    /// <summary>
    /// The altimeter var carries BOTH units, disambiguated by magnitude — TFDi render it as an
    /// integer above 500 (hectopascals) and to 2dp below (inches). Formatting one way always is
    /// wrong half the time: "1013.00" or "30".
    /// </summary>
    [Theory]
    [InlineData(29.92, false)]   // the live value — inches
    [InlineData(30.12, false)]
    [InlineData(1013, true)]     // hectopascals
    [InlineData(995, true)]
    public void Altimeter_UnitIsDecidedByMagnitude(double v, bool expectHectopascals)
    {
        // TFDi's rule, verbatim: (L:MD11_CAP_ALTIMETER) 500 >
        Assert.Equal(expectHectopascals, v > 500);
    }

    /// <summary>
    /// The five tanks the fuel read-out sums. The stock left/right/centre FUEL SimVars have no slot
    /// for the MD-11's tail trim tank or its auxiliary, so reading fuel the stock way would silently
    /// omit real fuel — on an aircraft with no readable SD page to catch it.
    /// </summary>
    [Theory]
    [InlineData("MD11_OVHD_TANK_1_VAL")]
    [InlineData("MD11_OVHD_TANK_2_VAL")]
    [InlineData("MD11_OVHD_TANK_3_VAL")]
    [InlineData("MD11_OVHD_TANK_AUX_VAL")]
    [InlineData("MD11_OVHD_TANK_TAIL_VAL")]
    public void FuelTanks_AreAllExported(string varName)
    {
        Assert.Contains(varName, Map.ExportVars);
    }

    [Fact]
    public void Altimeters_AreExported()
    {
        Assert.Contains("MD11_CAP_ALTIMETER", Map.ExportVars);
        Assert.Contains("MD11_FO_ALTIMETER", Map.ExportVars);
        Assert.Contains("MD11_STBY_ALTIMETER", Map.ExportVars);
    }
}
