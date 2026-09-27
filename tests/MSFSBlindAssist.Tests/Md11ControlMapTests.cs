using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Integrity of the generated control map — the traps that do not throw.
///
/// The map is produced by tools/md11-gen/generate_md11_map.py from TFDi's ModelBehaviorDefs. Its
/// tooltip parser is a heuristic, and a heuristic that guesses wrong here fails SILENTLY: a control
/// pointed at the wrong state var reads a plausible-looking number forever and every attempt to set
/// it reports "did not move". These tests pin the cases where that has actually happened.
/// </summary>
public class Md11ControlMapTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();

    private static Md11Control? Find(string nodeId) => Map.Controls.FirstOrDefault(
        c => string.Equals(c.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// MD11_EFB_IS_CARGO is the FREIGHTER/PASSENGER split — it describes the airframe, never a
    /// control's position.
    ///
    /// The cabin-temperature knobs' tooltips reference it only to choose their WORDING ("Courier
    /// Cabin" / "Main Cargo Deck" on the MD-11F where the passenger jet says "Forward Cabin" /
    /// "Middle Cabin"). The generator's "first L:var in the tooltip is the state var" rule took it
    /// as their state, which made an 8-position temperature selector read a 0/1 flag: verified on a
    /// live freighter, IS_CARGO reads 1 while the knobs sit at 4. A walk to set them could never
    /// converge, so every selection would have announced "did not move".
    /// </summary>
    [Theory]
    [InlineData("MD11_OVHD_PNEU_FWD_CAB_TEMP")]
    [InlineData("MD11_OVHD_PNEU_MID_CAB_TEMP")]
    [InlineData("MD11_OVHD_PNEU_AFT_CAB_TEMP")]
    public void CabinTemperatureKnobs_ReadTheirOwnPositionNotTheVariantFlag(string nodeId)
    {
        var c = Find(nodeId);

        Assert.NotNull(c);
        Assert.Equal(nodeId, c!.StateVar);
        Assert.Equal(8, c.NumStates);
    }

    /// <summary>
    /// Nothing at all may take the variant flag as its state. Stated broadly rather than per-knob:
    /// the same tooltip shape could name it on any future control, and the failure is invisible.
    /// </summary>
    [Fact]
    public void NoControl_UsesTheCargoVariantFlagAsItsState()
    {
        var offenders = Map.Controls
            .Where(c => string.Equals(c.StateVar, "MD11_EFB_IS_CARGO", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.NodeId)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// A control that can be WALKED must read a state var that is not simply its own click events'
    /// echo — but more importantly, every walkable control needs SOME state var, or the walker has
    /// nothing to close the loop against and gives up immediately.
    /// </summary>
    [Fact]
    public void EveryWalkableControl_HasAStateVar()
    {
        var walkable = new[]
        {
            Md11Kinds.Switch, Md11Kinds.Knob, Md11Kinds.KnobPush, Md11Kinds.KnobPushPull,
            Md11Kinds.Guard, Md11Kinds.Lever, Md11Kinds.Handle,
        };

        var missing = Map.Controls
            .Where(c => walkable.Contains(c.Kind) && string.IsNullOrWhiteSpace(c.StateVar))
            .Select(c => c.NodeId)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// A guard cover's position is its OWN L:var (the node id TFDi animates the cover on), never
    /// the control it covers. Four covers' tooltips read the covered button/switch for their
    /// Open/Closed wording (Fuel Dump, Fuel Dump Emergency Stop, Center Gear Uplock, Main Cargo
    /// Door Arm) and the generator's "first L:var in the tooltip" rule took that as the cover's
    /// state: the auto-open then read "valve closed" as "cover closed" and lowered an OPEN cover
    /// onto the press. The generator now pins every guard to its own node id; this pins the map.
    /// </summary>
    [Fact]
    public void EveryGuard_ReadsItsOwnCover_NotTheControlItCovers()
    {
        var offenders = Map.Controls
            .Where(c => c.Kind == Md11Kinds.Guard && !string.Equals(c.StateVar, c.NodeId, StringComparison.Ordinal))
            .Select(c => $"{c.NodeId} reads {c.StateVar}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The MD-11F is what is loaded most often for cargo ops, and its cabin/cargo controls are the
    /// ones most likely to differ. This pins that the freighter-side controls the map claims are
    /// actually present, so a variant-specific panel cannot quietly become empty.
    /// </summary>
    [Fact]
    public void FreighterControls_ArePresent()
    {
        Assert.NotNull(Find("MD11_EXT_DOOR_CRG_MAIN_ARM_GRD"));
        Assert.True(Map.Controls.Count(c => c.NodeId.Contains("CRGSMK", StringComparison.OrdinalIgnoreCase)) > 0,
            "cargo smoke detection controls are missing");
    }

    /// <summary>
    /// The aircraft has THREE IRS switches — IRS 1, IRS 2 and the one TFDi call "Auxiliary IRS" —
    /// and all three are Off/Nav. The third shipped with no positions because TFDi's tooltip for
    /// it has a stray '%' ('%{if}%Nav') that the generator's if/else parser tripped over, so the
    /// app showed a read-only numeric field named "Auxiliary IRS" and a pilot counted two IRS
    /// switches (a position control with no value map takes MainForm's read-only TextBox branch —
    /// see Md11DefinitionStateTests.IrsSwitch_RendersAsAnOffNavCombo).
    /// </summary>
    [Theory]
    [InlineData("MD11_OVHD_IRS_1_KB", "IRS 1")]
    [InlineData("MD11_OVHD_IRS_2_KB", "IRS 2")]
    [InlineData("MD11_OVHD_IRS_3_KB", "Auxiliary IRS")]
    public void EveryIrsSwitch_IsAnOffNavSelector(string nodeId, string label)
    {
        var c = Find(nodeId);

        Assert.NotNull(c);
        Assert.Equal(label, c!.Label);
        Assert.Equal(Md11Kinds.Switch, c.Kind);
        Assert.Equal("Nav", c.ValueMap["1"]);
        Assert.Equal("Off", c.ValueMap["0"]);
        Assert.Equal(2, c.ValueMap.Count);
    }

    /// <summary>
    /// The AIR panel temperature selectors carry curated positions (TFDi's tooltips name none):
    /// numbered, with the guide's "full cold" / "full hot" at the ends. Two of them used to carry
    /// the freighter/pax wording ("Courier Cabin" / "Forward Cabin") as their positions — a
    /// tooltip-parsing leak that turned an 8-position knob into a two-item combo.
    /// </summary>
    [Theory]
    [InlineData("MD11_OVHD_PNEU_COCKPIT_TEMP", 8)]
    [InlineData("MD11_OVHD_PNEU_FWD_CAB_TEMP", 8)]
    [InlineData("MD11_OVHD_PNEU_MID_CAB_TEMP", 8)]
    [InlineData("MD11_OVHD_PNEU_AFT_CAB_TEMP", 8)]
    [InlineData("MD11_OVHD_PNEU_FWD_CARGO_TEMP", 3)]
    [InlineData("MD11_OVHD_PNEU_AFT_CARGO_TEMP", 7)]
    public void TemperatureKnobs_CarryNumberedPositions_ColdToHot(string nodeId, int positions)
    {
        var c = Find(nodeId);

        Assert.NotNull(c);
        Assert.Equal(positions, c!.NumStates);
        Assert.Equal(positions, c.ValueMap.Count);
        Assert.Equal("1 (full cold)", c.ValueMap["0"]);
        Assert.Equal($"{positions} (full hot)", c.ValueMap[(positions - 1).ToString()]);
    }

    /// <summary>No operable control may carry the airframe variant's wording as a position.</summary>
    [Fact]
    public void NoControl_HasTheCargoVariantWordingAsAPosition()
    {
        var offenders = Map.Controls
            .Where(c => c.ValueMap.Values.Any(v => v.Contains("Courier Cabin", StringComparison.OrdinalIgnoreCase)
                                                 || v.Contains("Main Cargo Deck", StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.NodeId).ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void FireTestButton_IsNamedForEveryLoopItTests()
    {
        var c = Find("MD11_AOVHD_FIRETEST_BT");
        Assert.NotNull(c);
        Assert.Equal("Engine and APU Fire Test", c!.Label);
    }

    /// <summary>
    /// The EFIS minimums caps' tooltips read their own value and then the mode SWITCH's var for the
    /// Baro/Radio word. The generator once lifted that word as the cap's positions, giving a
    /// 0-15000 ft value knob a {Radio, Baro} map. The words belong to the switch, which keeps them.
    /// </summary>
    [Theory]
    [InlineData("MD11_LECP_MINIMUMS_CAP", "MD11_LECP_MINIMUMS_KB")]
    [InlineData("MD11_RECP_MINIMUMS_CAP", "MD11_RECP_MINIMUMS_KB")]
    public void MinimumsCap_CarriesNoPositions_TheModeWordsBelongToTheSwitch(string cap, string modeSwitch)
    {
        var c = Find(cap);
        var s = Find(modeSwitch);

        Assert.NotNull(c);
        Assert.NotNull(s);
        Assert.Empty(c!.ValueMap);
        Assert.Equal(2, s!.ValueMap.Count);
        Assert.Equal("Radio", s.ValueMap["0"]);
        Assert.Equal("Baro", s.ValueMap["1"]);
    }
}
