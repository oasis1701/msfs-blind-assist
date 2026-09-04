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
}
