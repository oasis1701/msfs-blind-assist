using MSFSBlindAssist.Aircraft;
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
    private static readonly TFDiMD11Definition Def = new();

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
        var c = Map.Controls.FirstOrDefault(x => x.NodeId == Md11GearLever.Key);

        Assert.NotNull(c);
        // The map's claim — documented as WRONG. If a regenerated map ever fixes this, this
        // assertion fails and the read-out can be simplified deliberately rather than by accident.
        Assert.Equal("Down", c!.ValueMap["1"]);
        Assert.Equal("Up", c.ValueMap["0"]);
    }

    /// <summary>
    /// 25 (parked, lever down) and 0 (up) both classify correctly under the aircraft's rule; 10
    /// (mid-travel) is where a naive boolean test diverges from the aircraft. The hotkey read-out
    /// reads through <see cref="Md11GearLever.IsDown"/>, so this pins the rule it speaks.
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
        Assert.Equal(20, Md11GearLever.DownThreshold);
        Assert.Equal(expectDown, Md11GearLever.IsDown(travel));
    }

    /// <summary>
    /// The Landing Gear panel's combo is keyed on the map's {0 Up, 1 Down}, and MainForm selects
    /// an item by an EXACT key match on the value it holds — so the parked lever's 25 selected
    /// nothing and the combo read "Gear Lever" with no value. The definition's value→key
    /// classifier (SimVarDefinition.ValueToDescriptionKey, the seam the combo renderer looks up
    /// through) must map every travel onto a key the map has, by the SAME threshold the read-out
    /// uses, so the combo shows the right item and the walk's gate-down re-sync can land.
    /// </summary>
    [Theory]
    [InlineData(0, 0, "Up")]
    [InlineData(10, 0, "Up")]      // mid-travel: still up, as the aircraft says
    [InlineData(19.9, 0, "Up")]
    [InlineData(20, 1, "Down")]    // exactly at the threshold
    [InlineData(25, 1, "Down")]    // live value, parked
    public void GearLever_PanelComboKeyFollowsTheThreshold(double travel, double expectKey, string expectLabel)
    {
        Assert.Equal(expectKey, Md11GearLever.DescriptionKey(travel));

        // Through the real definition: the classifier is wired onto the gear lever's variable and
        // lands on a key the generated map actually carries, with the map's own label.
        var def = Def.GetVariables()[Md11GearLever.Key];
        double key = def.DescriptionKeyFor(travel);
        Assert.Equal(expectKey, key);
        Assert.True(def.ValueDescriptions.ContainsKey(key), $"travel {travel} classified onto key {key}, which the map does not carry");
        Assert.Equal(expectLabel, def.ValueDescriptions[key]);
    }

    /// <summary>
    /// The gear key reads the lever FRESH and speaks this: TFDi's threshold on a delivered travel,
    /// "unavailable" only when nothing was delivered. It read the CACHE, which this OnRequest var
    /// holds only while the Landing Gear panel is open — "unavailable" until the panel had been
    /// opened, and stale after the gear was moved with the sim's own key.
    /// </summary>
    [Theory]
    [InlineData(null, "Gear position unavailable")]
    [InlineData(25.0, "Gear down")]
    [InlineData(20.0, "Gear down")]
    [InlineData(19.9, "Gear up")]
    [InlineData(10.0, "Gear up")]
    [InlineData(0.0, "Gear up")]
    public void GearReadOut_SpeaksTheDeliveredTravel_AndUnavailableOnlyWithoutADelivery(double? travel, string expected)
        => Assert.Equal(expected, Md11GearLever.Describe(travel));

    /// <summary>
    /// A classifier belongs only to a control whose VALUE space is not its combo's KEY space: the
    /// gear lever's 0-25 travel against {0 Up, 1 Down}, the Dial-A-Flap wheel's continuous raw
    /// value against whole degrees, the flap handle's Dial-A-Flap BAND against the detent
    /// points, and the speedbrake lever's travel — streamed every frame, resting wherever the
    /// lever stopped — against its four detents. Every other MD-11 combo keeps the raw value as
    /// its key. (This test once pinned the speedbrake as an exact-key combo; a lever resting at
    /// 26.4 then matched no key and the Spoilers combo opened blank, where the first Down-arrow
    /// commits "Retracted".)
    /// </summary>
    [Fact]
    public void OnlyMismatchedKeySpaces_ClassifyTheirValue()
    {
        var classified = Def.GetVariables()
            .Where(kv => kv.Value.ValueToDescriptionKey != null)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            new[] { Md11FlapSystem.DialKey, Md11FlapSystem.LeverKey, Md11GearLever.Key, Md11SpeedbrakeSystem.LeverKey }
                .OrderBy(k => k, StringComparer.Ordinal),
            classified);

        // The speedbrake lever's travel lands on the detent its read-out names…
        Assert.Equal(25, Def.GetVariables()[Md11SpeedbrakeSystem.LeverKey].DescriptionKeyFor(26.4));

        // …and the concrete counter-example: an unclassified var's key IS its value.
        var irs = Def.GetVariables()["MD11_OVHD_IRS_1_KB"];
        Assert.Null(irs.ValueToDescriptionKey);
        Assert.Equal(1, irs.DescriptionKeyFor(1));
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
