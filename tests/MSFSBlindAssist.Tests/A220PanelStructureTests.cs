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
    [InlineData("A22X_COOLING_INLET", "A22X Cooling Inlet", "Auto|Off")]          // 1 = OFF, measured live 2026-10-16
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

    [Fact]
    public void StandbyAltimeter_IsAWalkedEntryAndAStdPush()
    {
        var set = Vars["A22X_ISI_BARO_SET"];
        Assert.Equal("Standby Baro (hPa or inches)", set.DisplayName);
        Assert.Equal(SimVarType.Event, set.Type);
        Assert.Equal(UpdateFrequency.Never, set.UpdateFrequency);
        Assert.True(set.UnparseableTextAsNaN);
        var std = Vars["A22X_ISI_BARO_STD"];
        Assert.Equal("A22X ISI Altimeter STD", std.Name);
        Assert.Equal("Standby Baro STD", std.DisplayName);
        Assert.True(std.RenderAsButton && std.IsMomentary);
    }

    /// <summary>[A220-43]: both read back from the stock altimeter 3 the aircraft keeps for the ISI
    /// (live 2026-10-16) — stock SimVars, never L:vars ([VAR-2]), read on demand, never spoken by the
    /// generic path and never a Ctrl+M row ([VAR-9]).</summary>
    [Theory]
    [InlineData("A22X_ISI_KOHLSMAN_MB", "KOHLSMAN SETTING MB:3", "millibars")]
    [InlineData("A22X_ISI_STD", "KOHLSMAN SETTING STD:3", "bool")]
    public void StandbyAltimeter_ReadsBackTheStockAltimeter3(string key, string name, string units)
    {
        var def = Vars[key];
        Assert.Equal(name, def.Name);
        Assert.Equal(units, def.Units);
        Assert.Equal(SimVarType.SimVar, def.Type);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        Assert.False(def.IsAnnounced);
        Assert.True(def.ExcludeFromMonitorManager);
    }

    /// <summary>CORE-7: the STD push reads altimeter 3 back once and never says "pressed"; with no
    /// delivered read (this manager never connects) it says so once, never silence and never a
    /// claimed state.</summary>
    [Fact]
    public void StandbyStdPush_SpeaksOneReadBack_NeverPressed()
    {
        var def = new SynapticA220Definition();
        var speech = new SpeechCapture();
        var mgr = new SimConnectManager(IntPtr.Zero);       // never connected: no read is delivered
        Assert.True(def.HandleUIVariableSet("A22X_ISI_BARO_STD", 1, Vars["A22X_ISI_BARO_STD"], mgr, speech));
        Assert.True(SpinWait.SpinUntil(() => speech.All.Count > 0, 5000), "the STD push never spoke");
        Thread.Sleep(300);                                   // nothing else follows
        Assert.Equal(new[] { A220Panels.IsiNoReadingText }, speech.All);
    }

    // ---- Structure (B1) -------------------------------------------------------------------

    /// <summary>Sections and panels in the A220 flow (spec B1).</summary>
    private static readonly (string Section, string[] Panels)[] ExpectedStructure =
    {
        ("Overhead", new[]
        {
            "Electrical", "Hydraulics", "APU", "Equipment Cooling", "Fire Protection", "Fuel",
            "Air Conditioning", "Bleed and Packs", "Anti-Ice", "Pressurization", "Emergency",
            "Window and Probe Heat", "Flight Controls and Recorder", "Calls", "TAWS", "Wipers",
            "Interior Lighting", "Exterior Lights", "Signs"
        }),
        ("Glareshield", new[] { "Captain Side Panel", "CTP Captain", "Transponder and TCAS", "Flight Directors", "CTP First Officer" }),
        ("Center Panel", new[] { "Standby Instrument", "Gear and Brakes" }),
        ("Pedestal", new[] { "Engines", "Flaps and Speedbrake", "Trim and Parking Brake", "Audio Control Panel" }),
        ("Utilities", new[] { "Simulation Options", "Engine Data", "Doors", "IRS" }),
    };

    /// <summary>Each panel's controls with the everyday ones first, in the order a normal flight
    /// reaches them (the aircraft's normal checklists and the pilot-guide flows, power-on to
    /// power-off); the rest follow in cockpit order and guarded switches come last. Fire
    /// Protection reads per fire (push, then its bottles) and the Audio Control Panel per radio
    /// (transmit, volume, receiver). Cockpit positions for the rest:
    /// tools/a220-gen/reference/cockpit-layout.txt.</summary>
    public static TheoryData<string, string[]> ExpectedControls() => new()
    {
        { "Electrical", new[] { "A22X_BATTERY_1", "A22X_BATTERY_2", "A22X_GPU_AVAIL", "A22X_EXT_PWR", "A22X_APU_GEN_OFF",
            "A22X_L_GEN_OFF", "A22X_R_GEN_OFF", "A22X_BUS_ISOLATION", "A22X_CABIN_POWER_OFF", "A22X_RAT_GEN",
            "A22X_L_GEN_DISC", "A22X_R_GEN_DISC" } },
        { "Hydraulics", new[] { "A22X_PTU", "A22X_ACMP_3A", "A22X_ACMP_3B", "A22X_ACMP_2B", "A22X_HYD_1_SOV", "A22X_HYD_2_SOV" } },
        { "APU", new[] { "A22X_APU_SWITCH" } },
        { "Equipment Cooling", new[] { "A22X_COOLING_INLET", "A22X_COOLING_EXHAUST" } },
        { "Fire Protection", new[] { "A22X_L_ENG_FIRE", "A22X_L_ENG_FIRE_BOTTLE_1", "A22X_L_ENG_FIRE_BOTTLE_2",
            "A22X_R_ENG_FIRE", "A22X_R_ENG_FIRE_BOTTLE_1", "A22X_R_ENG_FIRE_BOTTLE_2",
            "A22X_APU_FIRE", "A22X_APU_BOTTLE_1", "A22X_CARGO_FWD_FIRE", "A22X_CARGO_AFT_FIRE", "A22X_CARGO_FIRE_BOTTLE" } },
        { "Fuel", new[] { "A22X_L_BOOST_PUMP", "A22X_R_BOOST_PUMP", "A22X_MANUAL_TRANSFER", "A22X_GRAVITY_TRANSFER" } },
        { "Air Conditioning", new[] { "A22X_TRIM_AIR_OFF", "A22X_COCKPIT_AIR", "A22X_FWD_CABIN_AIR", "A22X_AFT_CABIN_AIR",
            "A22X_FWD_CARGO_AIR", "A22X_AFT_CARGO_AIR", "A22X_PACK_FLOW", "A22X_RECIRC_OFF", "A22X_MANUAL_TEMP", "A22X_RAM_AIR" } },
        { "Bleed and Packs", new[] { "A22X_APU_BLEED_OFF", "A22X_L_PACK_OFF", "A22X_R_PACK_OFF",
            "A22X_L_BLEED_OFF", "A22X_R_BLEED_OFF", "A22X_CROSSBLEED" } },
        { "Anti-Ice", new[] { "A22X_L_COWL_AI", "A22X_WING_AI", "A22X_R_COWL_AI" } },
        { "Pressurization", new[] { "A22X_MAN_PRESS", "A22X_MANUAL_RATE", "A22X_EMER_DEPRESS", "A22X_DITCHING", "A22X_PAX_OXYGEN" } },
        { "Emergency", new[] { "A22X_EMERGENCY_LIGHTS", "A22X_ELT", "A22X_EVAC" } },
        { "Window and Probe Heat", new[] { "A22X_PROBE_HEAT", "A22X_L_WSHLD_HEAT_OFF", "A22X_R_WSHLD_HEAT_OFF",
            "A22X_L_WINDOW_HEAT_OFF", "A22X_R_WINDOW_HEAT_OFF" } },
        { "Flight Controls and Recorder", new[] { "A22X_CVR_TEST", "A22X_CVR_ERASE", "A22X_PFCC_1_OFF", "A22X_PFCC_2_OFF", "A22X_PFCC_3_OFF" } },
        { "Calls", new[] { "A22X_SERVICE_INTERCOM", "A22X_MECH_CALL" } },
        { "TAWS", new[] { "A22X_TAWS_GS_INHIBIT", "A22X_TAWS_GEAR_INHIBIT", "A22X_TAWS_TERRAIN_INHIBIT",
            "A22X_TAWS_FLAPS_INHIBIT", "A22X_AURAL_INHIBIT" } },
        { "Wipers", new[] { "A22X_WIPER_L", "A22X_WIPER_R" } },
        { "Interior Lighting", new[] { "A22X_DOME_LIGHTS", "A22X_ANNUN_LIGHTS" } },
        { "Exterior Lights", new[] { "A22X_NAV_LIGHTS", "A22X_BEACON_LIGHTS", "A22X_TAXI_LIGHTS", "A22X_STROBE_LIGHTS",
            "A22X_L_LANDING_LIGHTS", "A22X_R_LANDING_LIGHTS", "A22X_NOSE_LANDING_LIGHTS", "A22X_LOGO_LIGHTS", "A22X_WING_INSP_LIGHTS" } },
        { "Signs", new[] { "A22X_SEAT_BELT_LIGHTS", "A22X_NO_PED_LIGHTS" } },
        { "Captain Side Panel", new[] { "A22X_L_CHRONO", "A22X_MASTER_CW_CANCEL" } },
        { "CTP Captain", new[] { "A22X_L_QNH_SET", "A22X_L_BARO_STD", "A22X_L_BARO_UNIT", "A22X_L_MINIMUMS_SET",
            "A22X_L_MINIMUMS_MODE", "A22X_COM1_STBY_SET", "A22X_COM1_SWAP", "A22X_COM2_STBY_SET", "A22X_COM2_SWAP",
            "A22X_NAV1_STBY_SET", "A22X_NAV2_STBY_SET", "A22X_L_NAV_SOURCE", "A22X_L_CROSSTUNE" } },
        { "Transponder and TCAS", new[] { "A22X_XPDR_CODE_SET", "A22X_XPDR_MODE", "A22X_XPDR_IDENT", "A22X_TCAS_ALT_LIMIT", "A22X_XPDR_FLIGHT_ID" } },
        { "Flight Directors", new[] { "A22X_L_FD", "A22X_R_FD" } },
        { "CTP First Officer", new[] { "A22X_R_QNH_SET", "A22X_R_BARO_STD", "A22X_R_BARO_UNIT", "A22X_R_MINIMUMS_SET",
            "A22X_R_MINIMUMS_MODE", "A22X_R_NAV_SOURCE", "A22X_R_CROSSTUNE", "A22X_R_CHRONO" } },
        { "Standby Instrument", new[] { "A22X_ISI_BARO_SET", "A22X_ISI_BARO_STD" } },
        { "Gear and Brakes", new[] { "A22X_GEAR_LEVER", "A22X_AUTOBRAKE", "A22X_NOSE_STEER_OFF", "A22X_GEAR_AURAL_CANCEL",
            "A22X_ALTERNATE_GEAR", "A22X_ALTERNATE_BRAKE" } },
        { "Engines", new[] { "A22X_ENG_START_MODE", "A22X_ENG_MASTER_1", "A22X_ENG_MASTER_2", "A22X_CONT_IGNITION" } },
        { "Flaps and Speedbrake", new[] { "A22X_FLAP_LEVER", "A22X_SPOILER_LEVER", "A22X_ALTERNATE_FLAP" } },
        { "Trim and Parking Brake", new[] { "A22X_PARKING_BRAKE", "A22X_STAB_TRIM_SET", "RUDDER_TRIM_LEFT", "RUDDER_TRIM_RIGHT" } },
        { "Audio Control Panel", new[] { "A22X_ACP_VHF1_TX", "A22X_ACP_VHF1_VOLUME", "A22X_ACP_VHF1_RX",
            "A22X_ACP_VHF2_TX", "A22X_ACP_VHF2_VOLUME", "A22X_ACP_VHF2_RX",
            "A22X_ACP_VHF3_TX", "A22X_ACP_VHF3_VOLUME", "A22X_ACP_VHF3_RX" } },   // per radio: transmit, volume, receiver
        { "Simulation Options", new[] { "A22X_TOD_PAUSE", "A22X_TOD_DISTANCE_SET" } },
        { "Engine Data", Array.Empty<string>() },
        { "Doors", Array.Empty<string>() },
        { "IRS", Array.Empty<string>() },
    };

    [Fact]
    public void Sections_AndPanels_FollowTheCockpitFlow()
    {
        var structure = Def.GetPanelStructure();
        Assert.Equal(ExpectedStructure.Select(s => s.Section), structure.Keys);
        foreach (var (section, panels) in ExpectedStructure)
            Assert.Equal(panels, structure[section]);
    }

    [Theory]
    [MemberData(nameof(ExpectedControls))]
    public void PanelControls_ReadEverydayFirst(string panel, string[] keys)
        => Assert.Equal(keys, Def.GetPanelControls()[panel]);

    [Fact]
    public void ExpectedControls_CoverEveryPanel()
    {
        var pinned = ExpectedControls().Select(row => (string)row[0]).ToHashSet();
        Assert.Equal(ExpectedStructure.SelectMany(s => s.Panels).OrderBy(p => p), pinned.OrderBy(p => p));
    }

    /// <summary>[HS-5] A display-only panel still needs a BuildPanelControls entry (even an empty
    /// one), or MainForm's panel build early-returns and renders nothing.</summary>
    [Fact]
    public void EveryPanel_HasABuildPanelControlsEntry()
    {
        var controls = Def.GetPanelControls();
        var missing = Def.GetPanelStructure().SelectMany(s => s.Value.Select(p => (s.Key, Panel: p)))
            .Where(x => !controls.ContainsKey(x.Panel)).Select(x => $"{x.Key}/{x.Panel}").ToList();
        Assert.True(missing.Count == 0, "no BuildPanelControls entry: " + string.Join(", ", missing));
    }

    /// <summary>[VAR-1] No two rows of one panel (controls and status rows) share a name.</summary>
    [Fact]
    public void NoPanel_RepeatsADisplayName()
    {
        var displays = Def.GetPanelDisplayVariables();
        var problems = new List<string>();
        foreach (var (panel, keys) in Def.GetPanelControls())
        {
            IEnumerable<string> rows = keys.Concat(displays.TryGetValue(panel, out var d) ? d : new List<string>());
            foreach (var g in rows.GroupBy(k => Vars[k].DisplayName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                problems.Add($"{panel}: \"{g.Key}\" on {string.Join(", ", g)}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void NoControl_SitsOnTwoPanels()
    {
        var problems = Def.GetPanelControls().SelectMany(kv => kv.Value.Select(k => (Key: k, Panel: kv.Key)))
            .GroupBy(x => x.Key).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Panel))}").ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Plan A's controls, moved to where the cockpit has them.</summary>
    [Theory]
    [InlineData("A22X_AUTOBRAKE", "Gear and Brakes")]
    [InlineData("A22X_XPDR_MODE", "Transponder and TCAS")]
    [InlineData("A22X_TCAS_ALT_LIMIT", "Transponder and TCAS")]
    [InlineData("A22X_XPDR_CODE_SET", "Transponder and TCAS")]
    [InlineData("A22X_XPDR_IDENT", "Transponder and TCAS")]
    [InlineData("A22X_XPDR_FLIGHT_ID", "Transponder and TCAS")]
    [InlineData("A22X_L_QNH_SET", "CTP Captain")]
    [InlineData("A22X_L_BARO_UNIT", "CTP Captain")]
    [InlineData("A22X_L_BARO_STD", "CTP Captain")]
    [InlineData("A22X_L_MINIMUMS_MODE", "CTP Captain")]
    [InlineData("A22X_L_MINIMUMS_SET", "CTP Captain")]
    [InlineData("A22X_R_QNH_SET", "CTP First Officer")]
    [InlineData("A22X_R_BARO_UNIT", "CTP First Officer")]
    [InlineData("A22X_R_BARO_STD", "CTP First Officer")]
    [InlineData("A22X_R_MINIMUMS_MODE", "CTP First Officer")]
    [InlineData("A22X_R_MINIMUMS_SET", "CTP First Officer")]
    public void PlanAControls_SitWhereTheCockpitHasThem(string key, string panel)
    {
        Assert.True(Vars.ContainsKey(key), $"{key} is not defined (Plan A merged?)");
        var holders = Def.GetPanelControls().Where(kv => kv.Value.Contains(key)).Select(kv => kv.Key).ToList();
        Assert.Equal(new[] { panel }, holders);
    }

    [Fact]
    public void StatusRows_SitOnTheirPanels()
    {
        var d = Def.GetPanelDisplayVariables();
        // Plan A's systems rows, each on the panel the cockpit shows it on.
        Assert.Equal(new[] { "A22X_CAS_STATE" }, d["Captain Side Panel"]);
        Assert.Equal(new[] { "A22X_L_ALTIMETER_STATE", "A22X_L_MINIMUMS_STATE" }, d["CTP Captain"]);
        Assert.Equal(new[] { "A22X_XPDR_STATE" }, d["Transponder and TCAS"]);
        Assert.Equal(new[] { "A22X_R_ALTIMETER_STATE", "A22X_R_MINIMUMS_STATE" }, d["CTP First Officer"]);
        Assert.Equal(new[] { "A22X_GEAR_STATE", "A22X_BRAKE_TEMPS" }, d["Gear and Brakes"]);
        Assert.Equal(new[] { "A22X_IRS_STATE" }, d["IRS"]);
        Assert.Equal(new[] { "A22X_STAB_TRIM", "A22X_RUDDER_TRIM_POS" }, d["Trim and Parking Brake"]);
        Assert.Equal(new[] { "A22X_TX_CHANNEL" }, d["Audio Control Panel"]);
        Assert.Equal("A22X_DOORS_STATE", d["Doors"][0]);
        foreach (var door in MSFSBlindAssist.Aircraft.A220.A220Systems.Doors)
            Assert.Contains(SynapticA220Definition.DoorKey(door.Id), d["Doors"]);
        foreach (string k in new[] { "A22X_ENG1_STATE", "A22X_ENG2_STATE", "A22X_ENG1_N1", "A22X_ENG1_N2", "A22X_ENG1_EGT", "A22X_ENG2_N1", "A22X_ENG2_N2", "A22X_ENG2_EGT" })
            Assert.Contains(k, d["Engine Data"]);
        Assert.False(d.ContainsKey("Flaps and Speedbrake"), "the raw lever row is the speedbrake combo now");
        var panels = Def.GetPanelStructure().Values.SelectMany(p => p).ToHashSet();
        foreach (string panel in d.Keys) Assert.Contains(panel, panels);
    }

    [Theory]
    [InlineData("A22X_LAMP_TEST")]
    [InlineData("A22X_SPOILERS_RETRACT")]
    [InlineData("A22X_SPOILERS_DEPLOY")]
    [InlineData("A22X_SPOILER_LEVER_POS")]
    public void RetiredControls_AreGone(string key) => Assert.False(Vars.ContainsKey(key), key);

    /// <summary>B3: no cockpit control writes the lamp test, so no MSFSBA control does.</summary>
    [Fact]
    public void NoControl_WritesTheLampTest()
        => Assert.DoesNotContain(Vars.Values, d => d.Name == "A22X Lamp Test");
}
