using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The A220 panels against the aircraft's own cockpit (Plan B, docs/a220.md "Panels and
/// hotkeys"). Every guard label and every pushbutton kind is derived from the cockpit
/// behaviour XML inventory, tools/a220-gen/reference/cockpit-controls.csv ([A220-41],
/// [A220-42]); the new combos keep the cockpit's positions; the structure follows the flow.
/// </summary>
public class A220PanelStructureTests
{
    private static readonly SynapticA220Definition Def = new();
    private static readonly Dictionary<string, SimVarDefinition> Vars = Def.GetVariables();

    private sealed record CockpitControl(string Node, string Kind, bool Guarded, string Var);

    private static readonly Lazy<List<CockpitControl>> Inventory = new(LoadInventory);

    private static List<CockpitControl> LoadInventory()
    {
        string path = Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
            "tools", "a220-gen", "reference", "cockpit-controls.csv");
        string[] lines = File.ReadAllLines(path);
        List<string> header = SplitCsv(lines[0]);
        int node = header.IndexOf("node"), kind = header.IndexOf("kind");
        int guarded = header.IndexOf("guarded"), var = header.IndexOf("var");
        return lines.Skip(1).Where(l => l.Length > 0).Select(SplitCsv)
            .Select(f => new CockpitControl(f[node], f[kind], f[guarded] == "guarded", f[var]))
            .ToList();
    }

    /// <summary>One CSV line: quoted fields with "" escapes (the generator quotes every field).</summary>
    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cur.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        fields.Add(cur.ToString());
        return fields;
    }

    /// <summary>Each inventory L:var control paired with every definition that names that L:var.</summary>
    private static IEnumerable<(CockpitControl Control, string Key, SimVarDefinition Def)> Matched()
    {
        foreach (var c in Inventory.Value.Where(c => c.Var.StartsWith("L:", StringComparison.Ordinal)))
        {
            string bare = c.Var[2..];
            foreach (var (key, def) in Vars)
                if (def.Type == SimVarType.LVar && def.Name == bare) yield return (c, key, def);
        }
    }

    [Fact]
    public void Inventory_IsTheVendoredCockpitXmlAudit()
        => Assert.True(Inventory.Value.Count >= 540, $"only {Inventory.Value.Count} inventory rows");

    /// <summary>[A220-41] "(guarded)" means the cockpit has a guard there — both ways.</summary>
    [Fact]
    public void GuardLabels_FollowTheCockpitXml()
    {
        var problems = Matched()
            .Where(m => m.Def.DisplayName.Contains("(guarded)", StringComparison.Ordinal) != m.Control.Guarded)
            .Select(m => $"{m.Key} ({m.Control.Node}): cockpit is {(m.Control.Guarded ? "guarded" : "unguarded")}, "
                + $"label is \"{m.Def.DisplayName}\"")
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>[A220-42] The cockpit's pushbutton template decides the write.</summary>
    [Fact]
    public void Pushbuttons_WriteTheWayTheCockpitDoes()
    {
        var problems = new List<string>();
        foreach (var (c, key, def) in Matched())
        {
            string? why = null;
            if (c.Kind.StartsWith("momentary korry pushbutton", StringComparison.Ordinal))
            {
                if (!def.RenderAsButton || def.IsMomentary) why = "a latch-free korry must be a write-1 button (LPress)";
            }
            else if (c.Kind == "momentary pushbutton" || c.Kind == "pushbutton (held while pressed)")
            {
                if (!def.RenderAsButton || !def.IsMomentary) why = "a momentary or held push must pulse 1 then 0 (LMomentary)";
            }
            else if (c.Kind == "latched korry pushbutton")
            {
                if (def.RenderAsButton || def.ValueDescriptions.Count != 2) why = "a latched korry must be a two-position combo";
            }
            if (why != null) problems.Add($"{key} ({c.Node}, {c.Kind}): {why}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [InlineData("A22X_COOLING_INLET", "A22X Cooling Inlet", "Off|Auto")]
    [InlineData("A22X_COOLING_EXHAUST", "A22X Cooling Exhaust", "Valve Only|Auto|On")]
    [InlineData("A22X_APU_FIRE", "A22X APU Fire", "Normal|Pushed")]
    [InlineData("A22X_CARGO_FWD_FIRE", "A22X Cargo Fwd Fire", "Normal|Pushed")]
    [InlineData("A22X_CARGO_AFT_FIRE", "A22X Cargo Aft Fire", "Normal|Pushed")]
    [InlineData("A22X_L_ENG_FIRE_BOTTLE_1", "A22X L Eng Fire Bottle 1", "Normal|Pushed")]
    [InlineData("A22X_L_ENG_FIRE_BOTTLE_2", "A22X L Eng Fire Bottle 2", "Normal|Pushed")]
    [InlineData("A22X_APU_BOTTLE_1", "A22X APU Bottle 1", "Normal|Pushed")]
    [InlineData("A22X_R_ENG_FIRE_BOTTLE_1", "A22X R Eng Fire Bottle 1", "Normal|Pushed")]
    [InlineData("A22X_R_ENG_FIRE_BOTTLE_2", "A22X R Eng Fire Bottle 2", "Normal|Pushed")]
    [InlineData("A22X_CARGO_FIRE_BOTTLE", "A22X Cargo Fire Bottle", "Normal|Pushed")]
    [InlineData("A22X_SERVICE_INTERCOM", "A22X Service Intercom", "Off|On")]
    [InlineData("A22X_WIPER_L", "A22X_WIPER_L", "Off|Intermittent|Slow|Fast")]
    [InlineData("A22X_WIPER_R", "A22X_WIPER_R", "Off|Intermittent|Slow|Fast")]
    [InlineData("A22X_SPOILER_LEVER", "A22X Spoiler Lever", "Retracted|Quarter|Half|Three quarters|Full|Max (emergency)")]
    [InlineData("A22X_TX_CHANNEL", "A22X Tx Channel", "None|VHF 1|VHF 2|VHF 3")]
    public void NewCombos_KeepTheCockpitPositions(string key, string name, string labels)
    {
        Assert.True(Vars.TryGetValue(key, out var def), $"{key} is not defined");
        Assert.Equal(name, def!.Name);
        Assert.Equal(SimVarType.LVar, def.Type);
        Assert.Equal(labels, string.Join("|", def.ValueDescriptions.OrderBy(kv => kv.Key).Select(kv => kv.Value)));
    }

    [Theory]
    [InlineData("A22X_PROBE_HEAT", "A22X Probe Heat", "Probe Heat Ground Test")]
    [InlineData("A22X_L_ENG_FIRE", "A22X L Eng Fire", "Left Engine Fire (guarded)")]
    [InlineData("A22X_R_ENG_FIRE", "A22X R Eng Fire", "Right Engine Fire (guarded)")]
    public void LatchFreeKorrys_AreWriteOneButtons(string key, string name, string label)
    {
        var def = Vars[key];
        Assert.Equal(name, def.Name);
        Assert.Equal(label, def.DisplayName);
        Assert.True(def.RenderAsButton);
        Assert.False(def.IsMomentary);
        Assert.Empty(def.ValueDescriptions);
        Assert.Equal(UpdateFrequency.Never, def.UpdateFrequency);
    }

    [Theory]
    [InlineData("A22X_CVR_TEST", "A22X CVR Test", "CVR Test")]
    [InlineData("A22X_CVR_ERASE", "A22X CVR Erase", "CVR Erase")]
    [InlineData("A22X_MECH_CALL", "A22X Mech Call", "Mechanic Call")]
    [InlineData("A22X_ACP_VHF1_TX", "A22X ACP VHF1 Tx", "VHF 1 Transmit")]
    [InlineData("A22X_ACP_VHF2_TX", "A22X ACP VHF2 Tx", "VHF 2 Transmit")]
    [InlineData("A22X_ACP_VHF3_TX", "A22X ACP VHF3 Tx", "VHF 3 Transmit")]
    public void MomentaryPushes_PulseLikeTheCockpit(string key, string name, string label)
    {
        var def = Vars[key];
        Assert.Equal(name, def.Name);
        Assert.Equal(label, def.DisplayName);
        Assert.True(def.RenderAsButton && def.IsMomentary);
    }

    [Fact]
    public void SpeedbrakeLever_ShowsTheLeverTravelAsADetent()
    {
        var def = Vars["A22X_SPOILER_LEVER"];
        Assert.NotNull(def.ValueToDescriptionKey);
        Assert.Equal("Half", def.ValueDescriptions[def.DescriptionKeyFor(0.41)]);
        Assert.Equal("Retracted", def.ValueDescriptions[def.DescriptionKeyFor(0.0)]);
    }

    [Fact]
    public void PressConfirmations_AreStatelessButtonsWithoutALamp()
    {
        var lamps = SynapticA220SimVarData.LampVars.ToHashSet(StringComparer.Ordinal);
        foreach (string key in A220Panels.PressConfirmKeys)
        {
            var def = Vars[key];
            Assert.True(def.RenderAsButton, key);
            Assert.Equal(SimVarType.LVar, def.Type);
            Assert.DoesNotContain($"L:{def.Name} Lamp", lamps);
        }
        // Probe heat's lamp is batch-monitored, so the lamp is its confirmation.
        Assert.DoesNotContain("A22X_PROBE_HEAT", A220Panels.PressConfirmKeys);
        Assert.Contains("L:A22X Probe Heat Lamp", lamps);
    }

    [Theory]
    [InlineData("A22X_ENG_MASTER_1", "Left Engine Run")]
    [InlineData("A22X_ENG_MASTER_2", "Right Engine Run")]
    [InlineData("A22X_GRAVITY_TRANSFER", "Gravity Transfer")]
    [InlineData("A22X_CABIN_POWER_OFF", "Cabin Power (guarded)")]
    [InlineData("A22X_EVAC", "Evacuation Command (guarded)")]
    public void RelabelledControls_SayWhatTheCockpitSays(string key, string label)
        => Assert.Equal(label, Vars[key].DisplayName);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AcpReceiveAndVolume_AreTheKnobsOwnVars(int n)
    {
        var rx = Vars[$"A22X_ACP_VHF{n}_RX"];
        Assert.Equal($"A22X ACP VHF{n} Rx", rx.Name);
        Assert.Equal($"VHF {n} Receiver", rx.DisplayName);
        Assert.Equal(new[] { "Off", "On" }, rx.ValueDescriptions.OrderBy(kv => kv.Key).Select(kv => kv.Value));
        Assert.Equal(UpdateFrequency.OnRequest, rx.UpdateFrequency);

        var vol = Vars[$"A22X_ACP_VHF{n}_VOLUME"];
        Assert.Equal($"A22X ACP VHF{n} Volume", vol.Name);
        Assert.Equal($"VHF {n} Volume", vol.DisplayName);
        Assert.Equal(SimVarType.LVar, vol.Type);
        Assert.Equal(UpdateFrequency.OnRequest, vol.UpdateFrequency);
        Assert.Equal(29, vol.ValueDescriptions.Count);
        Assert.NotNull(vol.ValueToDescriptionKey);
        Assert.Equal("50%", vol.ValueDescriptions[vol.DescriptionKeyFor(0.5)]);       // the L:var's 0-1 level, shown as its detent
        Assert.Equal("100%", vol.ValueDescriptions[vol.DescriptionKeyFor(1.0)]);
    }
}
