using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the Synaptic A220 definition's pure structure:
/// the aural-exclusion and lamp invariants from docs/a220-plan.md, panel/variable
/// consistency, the NAV standby-only rule, and the heading-wrap walk math.
/// </summary>
public class SynapticA220DefinitionTests
{
    private static readonly SynapticA220Definition Def = new();
    private static readonly Dictionary<string, SimVarDefinition> Vars = Def.GetVariables();

    [Fact]
    public void Identity_IsPinned()
    {
        Assert.Equal("SYNAPTIC_A220", Def.AircraftCode);
        Assert.Equal(FCUControlType.IncrementDecrement, Def.GetAltitudeControlType());
        Assert.Equal(FCUControlType.IncrementDecrement, Def.GetHeadingControlType());
        Assert.Equal(FCUControlType.IncrementDecrement, Def.GetSpeedControlType());
        Assert.Equal(FCUControlType.IncrementDecrement, Def.GetVerticalSpeedControlType());
    }

    /// <summary>
    /// The EXEC prompt must be a SPOKEN monitor, not a cache-only quiet var. A
    /// pending flight-plan modification is shown to a sighted pilot as a lamp on the
    /// MKP EXEC key (whose SEQ2_CODE is this very L:var) plus a MOD flag on the
    /// display — a blind pilot has neither, so if this var stops being announced the
    /// EXEC prompt becomes completely invisible again. It also stays IN the Ctrl+M
    /// monitor manager so it can be switched off deliberately.
    /// </summary>
    [Fact]
    public void FlightPlanModified_IsAnnouncedAndUserSilenceable()
    {
        var v = Assert.Contains("A22X_FPLN_MODIFIED", (IDictionary<string, SimVarDefinition>)Vars);
        Assert.Equal("A22X Flight Plan Modified", v.Name);
        Assert.Equal(SimVarType.LVar, v.Type);
        Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
        Assert.True(v.IsAnnounced, "the EXEC prompt must be announced");
        Assert.False(v.ExcludeFromMonitorManager, "must remain switchable in Ctrl+M");
    }

    /// <summary>Gotcha #5: the aircraft plays its own aural callouts — MSFSBA must
    /// never register any of the ~91 "L:A22X Aural *" flags.</summary>
    [Fact]
    public void NoNativeAuralFlag_IsEverRegistered()
    {
        var auralBareNames = SynapticA220SimVarData.AuralVars
            .Select(n => n.StartsWith("L:") ? n.Substring(2) : n)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, def) in Vars)
            Assert.False(auralBareNames.Contains(def.Name),
                $"{key} registers native aural flag {def.Name}");
    }

    /// <summary>Every documented lamp is a batch-covered monitor var (Continuous +
    /// IsAnnounced, no ExcludeFromBatch = zero individual data defs) and never a
    /// panel control (A380 invariant: panels are for operating, not scanning).</summary>
    [Fact]
    public void Lamps_AreBatchMonitorsAndNotPanelControls()
    {
        var lampBareNames = SynapticA220SimVarData.LampVars
            .Select(n => n.StartsWith("L:") ? n.Substring(2) : n).ToHashSet();
        var lampKeys = Vars.Where(kv => lampBareNames.Contains(kv.Value.Name))
            .Select(kv => kv.Key).ToHashSet();
        Assert.Equal(SynapticA220SimVarData.LampVars.Length, lampKeys.Count);

        foreach (var key in lampKeys)
        {
            var def = Vars[key];
            Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
            Assert.True(def.IsAnnounced, $"{key} lamp must be announced");
            Assert.False(def.ExcludeFromBatch, $"{key} lamp must stay batch-covered");
        }

        var allPanelKeys = Def.GetPanelControls().Values.SelectMany(x => x).ToHashSet();
        Assert.Empty(lampKeys.Intersect(allPanelKeys));
    }

    [Fact]
    public void EveryPanelControlKey_HasAVariableDefinition()
    {
        foreach (var (panel, keys) in Def.GetPanelControls())
            foreach (var key in keys)
                Assert.True(Vars.ContainsKey(key), $"panel '{panel}' references unknown key {key}");
        foreach (var (panel, keys) in Def.GetPanelDisplayVariables())
            foreach (var key in keys)
                Assert.True(Vars.ContainsKey(key), $"display '{panel}' references unknown key {key}");
    }

    [Fact]
    public void EveryStructurePanel_HasControlsOrDisplays()
    {
        var controls = Def.GetPanelControls();
        var displays = Def.GetPanelDisplayVariables();
        foreach (var (section, panels) in Def.GetPanelStructure())
            foreach (var panel in panels)
                Assert.True(controls.ContainsKey(panel) || displays.ContainsKey(panel),
                    $"section '{section}' panel '{panel}' has no controls and no display entry");
    }

    /// <summary>Combo labels must be the documented enum labels verbatim.</summary>
    [Theory]
    [InlineData("A22X_APU_SWITCH", "A22X APU Switch")]
    [InlineData("A22X_TAXI_LIGHTS", "A22X Taxi Lights")]
    [InlineData("A22X_ENG_START_MODE", "A22X Eng Start Mode")]
    [InlineData("A22X_MANUAL_TRANSFER", "A22X Manual Transfer")]
    [InlineData("A22X_FWD_CARGO_AIR", "A22X Fwd Cargo Air")]
    [InlineData("A22X_BUS_ISOLATION", "A22X Bus Isolation Mode")]
    public void ComboLabels_MatchDocumentedEnums(string key, string bareName)
    {
        var doc = SynapticA220SimVarData.Vars.Single(x => x.Name == "L:" + bareName);
        var def = Vars[key];
        Assert.NotNull(doc.EnumLabels);
        Assert.Equal(doc.EnumLabels!.Length, def.ValueDescriptions.Count);
        for (int i = 0; i < doc.EnumLabels.Length; i++)
            Assert.Equal(doc.EnumLabels[i], def.ValueDescriptions[i]);
    }

    /// <summary>NAV-to-NAV transfer protection: no NAV active-set and no NAV swap
    /// control may exist — standby set only (A220-FOT-22-30-012).</summary>
    [Fact]
    public void NavRadios_AreStandbyOnly()
    {
        foreach (var (key, def) in Vars)
        {
            Assert.False(def.Name.Contains("NAV1_RADIO_SET", StringComparison.OrdinalIgnoreCase), key);
            Assert.False(def.Name.Contains("NAV2_RADIO_SET", StringComparison.OrdinalIgnoreCase), key);
            Assert.False(def.Name.Contains("NAV1_RADIO_SWAP", StringComparison.OrdinalIgnoreCase), key);
            Assert.False(def.Name.Contains("NAV2_RADIO_SWAP", StringComparison.OrdinalIgnoreCase), key);
        }
    }

    /// <summary>No spoiler-arm control may be exposed — the aircraft masks the stock
    /// arm events (no arm function exists on the A220).</summary>
    [Fact]
    public void NoSpoilerArmControl()
    {
        foreach (var (key, def) in Vars)
            Assert.False(def.Name.Contains("SPOILERS_ARM", StringComparison.OrdinalIgnoreCase), key);
    }

    [Theory]
    [InlineData(340, 20, 40)]
    [InlineData(20, 340, -40)]
    [InlineData(10, 350, -20)]
    [InlineData(180, 180, 0)]
    [InlineData(0, 180, 180)]
    public void HeadingWalk_TakesTheShortWayAround(double current, double target, double expectedDelta)
    {
        Assert.Equal(expectedDelta, SynapticA220Definition.WrapHeadingDelta(target - current), 3);
    }

    /// <summary>Space-containing L:var names are the A220 norm — the definition must
    /// carry them bare (no "L:" prefix; the read/write layers add it).</summary>
    [Fact]
    public void LvarNames_AreBareAndSpaceContaining()
    {
        foreach (var (key, def) in Vars)
        {
            if (def.Type != SimVarType.LVar) continue;
            Assert.False(def.Name.StartsWith("L:", StringComparison.Ordinal),
                $"{key} carries an L: prefix — reads/writes would double it");
        }
        Assert.Contains(Vars.Values, d => d.Type == SimVarType.LVar && d.Name.Contains(' '));
    }
}
