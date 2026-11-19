using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Plan 12 task 8: the 757's panels show only the loaded engine type's controls. The owner found the Engines panel
/// "cluttered" with both engine types' fuel control switches. The aircraft title names the type ("BlueBird 757-200 RR",
/// read 2026-10-28); the map says which controls one type alone has (<see cref="Bbs757Control.Variant"/>). What is pinned:
/// the title parsing (<see cref="Bbs757Variant"/>), the filter, an unknown type showing every control as before, a panel
/// built after a change of type holding the new type's controls with no section change (IsPanelControlVisible decides, from
/// the live title; the control lists stay whole), and a change rewriting only the section lists, in place, while the
/// variables and status rows stay as they are.
/// </summary>
public class Bbs757VariantTests
{
    private const string Battery = "B757_OVHD_BATTERY_BUTTON", Ignition = "B757_OVHD_ENG_START_SWITCH";
    private const string FuelPw = "BBS_fuelcut_PW1", FuelRr = "BBS_fuelcut_RR1";
    private const string EecGuard = "B757_GAURD_1_SWITCH", Eec = "B757_OVHD_L_ELEC-ENG-CONT_BUTTON";
    private const string EecPanel = "Electronic engine control (RR)", Engines = "Engines";
    private const string RrTitle = "BlueBird 757-200 RR", PwTitle = "BlueBird 757-200 PW";

    /// <summary>Two panels with both types' controls and one with the RR's alone, as in the shipped map, and a status row.</summary>
    private static Bbs757ControlMap Map() => Bbs757ControlMap.Parse($$"""
        {
          "controls": [
            { "node": "{{Battery}}", "section": "Overhead", "panel": "Electrical", "label": "Battery", "kind": "toggle", "variant": null },
            { "node": "{{EecGuard}}", "section": "Overhead", "panel": "{{EecPanel}}", "label": "Elec Eng Cont 1 Guard (RR)", "kind": "guard", "variant": "rr" },
            { "node": "{{Eec}}", "section": "Overhead", "panel": "{{EecPanel}}", "label": "L ELEC ENG Cont (RR)", "kind": "toggle", "variant": "rr" },
            { "node": "{{Ignition}}", "section": "Pedestal", "panel": "{{Engines}}", "label": "Ignition", "kind": "switch", "variant": null },
            { "node": "{{FuelPw}}", "section": "Pedestal", "panel": "{{Engines}}", "label": "L Fuel Control (PW)", "kind": "switch", "variant": "pw" },
            { "node": "{{FuelRr}}", "section": "Pedestal", "panel": "{{Engines}}", "label": "L Fuel Control (RR)", "kind": "switch", "variant": "rr" }
          ],
          "status": [ { "key": "B757_APU_STATE", "panel": "Status", "label": "APU", "announce": true } ]
        }
        """);

    /// <summary>A definition whose aircraft title is whatever <see cref="Title"/> holds when it is asked.</summary>
    private sealed class Rig
    {
        public string? Title;
        public readonly BBS757Definition Def;

        public Rig(Bbs757ControlMap? map = null, string? title = null)
        {
            Title = title;
            Def = new BBS757Definition(map ?? Map()) { AircraftTitle = () => Title };
        }

        /// <summary>The pilot opens a section: MainForm lists its panels from GetPanelStructure.</summary>
        public List<string> Panels(string section) => Def.GetPanelStructure()[section];

        /// <summary>A panel's control list as the definition hands it out: whole, whatever the engine type.</summary>
        public List<string> Controls(string panel) => Def.GetPanelControls()[panel];

        /// <summary>What MainForm asks of each control as it builds the panel.</summary>
        public bool Shown(string key) => Def.IsPanelControlVisible(key, null);

        /// <summary>
        /// The controls MainForm builds for a panel (MainForm.PanelBuilder's loop): its GetPanelControls list, each control
        /// asked of IsPanelControlVisible. No GetPanelStructure: re-opening a panel of the section the pilot is in lists nothing.
        /// </summary>
        public List<string> Built(string panel) => Def.GetPanelControls()[panel].Where(Shown).ToList();
    }

    // ---- The title ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("BlueBird 757-200 RR", Bbs757EngineType.Rr)]
    [InlineData("BlueBird 757-200 PW", Bbs757EngineType.Pw)]
    [InlineData("bluebird 757-200 rr", Bbs757EngineType.Rr)]   // a whole word in any case
    [InlineData("BlueBird 757-200 pw n/w awe", Bbs757EngineType.Pw)]
    [InlineData("BlueBird 757-200", Bbs757EngineType.Unknown)]
    [InlineData("Boeing 757 PWR RRX", Bbs757EngineType.Unknown)]   // only a whole word counts
    [InlineData("BlueBird 757-200 PW RR", Bbs757EngineType.Unknown)]   // both: neither is believed
    [InlineData("", Bbs757EngineType.Unknown)]
    [InlineData("   ", Bbs757EngineType.Unknown)]
    [InlineData(null, Bbs757EngineType.Unknown)]   // no title yet
    public void The_title_names_the_engine_type(string? title, Bbs757EngineType expected)
    {
        Assert.Equal(expected, Bbs757Variant.FromTitle(title));
    }

    [Theory]
    // Every title the package ships (its aircraft.cfg files, checked 2026-10-28; Bbs757FoStateEvaluator.TitleVariant's list).
    [InlineData("BlueBird 757-200 PW", Bbs757EngineType.Pw)]
    [InlineData("BlueBird 757-200 PW AWE", Bbs757EngineType.Pw)]
    [InlineData("BlueBird 757-200 PW NO CABIN", Bbs757EngineType.Pw)]
    [InlineData("BlueBird 757-200 PW N/W", Bbs757EngineType.Pw)]
    [InlineData("BlueBird 757-200 PW N/W AWE", Bbs757EngineType.Pw)]
    [InlineData("BlueBird 757-200 RR", Bbs757EngineType.Rr)]
    [InlineData("BlueBird 757-200 RR AWE", Bbs757EngineType.Rr)]
    [InlineData("BlueBird 757-200 RR N/W", Bbs757EngineType.Rr)]
    [InlineData("BlueBird 757-200 RR N/W 4 Door", Bbs757EngineType.Rr)]
    [InlineData("BlueBird 757-200 RR Scimitar/W", Bbs757EngineType.Rr)]
    public void Every_shipped_title_names_its_engine_type(string title, Bbs757EngineType expected)
    {
        Assert.Equal(expected, Bbs757Variant.FromTitle(title));
    }

    // ---- The filter -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, Bbs757EngineType.Pw, true)]
    [InlineData(null, Bbs757EngineType.Rr, true)]
    [InlineData("pw", Bbs757EngineType.Pw, true)]
    [InlineData("pw", Bbs757EngineType.Rr, false)]
    [InlineData("rr", Bbs757EngineType.Rr, true)]
    [InlineData("rr", Bbs757EngineType.Pw, false)]
    [InlineData("pw", Bbs757EngineType.Unknown, true)]   // an unknown type shows every control, as before the field
    [InlineData("rr", Bbs757EngineType.Unknown, true)]
    public void A_control_is_left_out_only_on_the_other_engine_type(string? variant, Bbs757EngineType engines, bool fitted)
    {
        Assert.Equal(fitted, Bbs757Variant.IsFitted(variant, engines));
    }

    [Fact]
    public void RR_hides_the_PW_rows_PW_hides_the_RR_rows_and_unknown_hides_nothing()
    {
        var controls = Map().Controls;

        Assert.Equal(new[] { FuelPw }, Bbs757Variant.HiddenNodes(controls, Bbs757EngineType.Rr));
        Assert.Equal(new[] { EecGuard, Eec, FuelRr }.Order(StringComparer.Ordinal),
            Bbs757Variant.HiddenNodes(controls, Bbs757EngineType.Pw).Order(StringComparer.Ordinal));
        Assert.Empty(Bbs757Variant.HiddenNodes(controls, Bbs757EngineType.Unknown));
    }

    [Fact]
    public void The_section_rewrite_keeps_its_lists_drops_only_a_panel_the_hiding_empties_and_never_touches_a_control_list()
    {
        var allStructure = new Dictionary<string, List<string>>
        {
            ["Overhead"] = new() { "Electrical", EecPanel, "Empty", "With app field" },
            ["Pedestal"] = new() { Engines },
            ["Status"] = new() { "Status" },
        };
        var controls = new Dictionary<string, List<string>>
        {
            ["Electrical"] = new() { Battery }, [EecPanel] = new() { EecGuard, Eec }, ["Empty"] = new(),
            ["With app field"] = new() { Eec, "MSFSBA_APP_FIELD" }, [Engines] = new() { Ignition, FuelPw, FuelRr },
            ["Status"] = new(),
        };
        var allControls = controls.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        var structure = allStructure.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        var overhead = structure["Overhead"];
        var noDisplays = new Dictionary<string, List<string>>();

        Bbs757Variant.RewriteSections(allStructure, controls, noDisplays, structure, new HashSet<string> { EecGuard, Eec, FuelRr });

        Assert.Same(overhead, structure["Overhead"]);
        // one only hidden controls are on goes; one with no control to begin with, or with an app field, stays
        Assert.Equal(new[] { "Electrical", "Empty", "With app field" }, overhead);
        Assert.Equal(new[] { Engines }, structure["Pedestal"]);
        Assert.Equal(new[] { "Status" }, structure["Status"]);
        // the control lists stay whole: the panel build decides which of them show
        Assert.Equal(allControls, controls);

        // a panel that still has a status display stays though its controls are hidden
        var displays = new Dictionary<string, List<string>> { [EecPanel] = new() { "B757_X_STATE" } };
        Bbs757Variant.RewriteSections(allStructure, controls, displays, structure, new HashSet<string> { EecGuard, Eec });
        Assert.Equal(new[] { "Electrical", EecPanel, "Empty", "With app field" }, overhead);

        // hiding nothing brings back every section as it was built
        Bbs757Variant.RewriteSections(allStructure, controls, noDisplays, structure, new HashSet<string> { EecGuard, Eec });
        Bbs757Variant.RewriteSections(allStructure, controls, noDisplays, structure, new HashSet<string>());
        Assert.Equal(allStructure["Overhead"], overhead);
        Assert.Same(overhead, structure["Overhead"]);
        Assert.Equal(allControls, controls);
    }

    // ---- The definition ---------------------------------------------------------------------------

    [Fact]
    public void An_RR_shows_the_RR_controls_only()
    {
        var rig = new Rig(title: RrTitle);

        Assert.Equal(new[] { Ignition, FuelRr }, rig.Built(Engines));
        Assert.Equal(new[] { Ignition, FuelPw, FuelRr }, rig.Controls(Engines));   // the list stays whole
        Assert.Contains(EecPanel, rig.Panels("Overhead"));
        Assert.Equal(new[] { EecGuard, Eec }, rig.Built(EecPanel));
        Assert.False(rig.Shown(FuelPw));
        Assert.All(new[] { FuelRr, Eec, EecGuard, Battery, Ignition, "B757_APU_STATE" }, key => Assert.True(rig.Shown(key), key));
    }

    [Fact]
    public void A_PW_shows_the_PW_controls_only_and_no_RR_engine_control_panel()
    {
        var rig = new Rig(title: PwTitle);

        Assert.Equal(new[] { Ignition, FuelPw }, rig.Built(Engines));
        Assert.DoesNotContain(EecPanel, rig.Panels("Overhead"));
        Assert.Contains("Electrical", rig.Panels("Overhead"));
        Assert.Empty(rig.Built(EecPanel));
        Assert.Equal(new[] { EecGuard, Eec }, rig.Controls(EecPanel));   // the list stays whole
        Assert.All(new[] { FuelRr, Eec, EecGuard }, key => Assert.False(rig.Shown(key), key));
        Assert.All(new[] { FuelPw, Battery, Ignition }, key => Assert.True(rig.Shown(key), key));
    }

    [Fact]
    public void An_unknown_engine_type_shows_every_control_as_before()
    {
        var rig = new Rig(title: "BlueBird 757-200");

        Assert.Equal(new[] { Ignition, FuelPw, FuelRr }, rig.Built(Engines));
        Assert.Contains(EecPanel, rig.Panels("Overhead"));
        Assert.All(new[] { FuelPw, FuelRr, Eec, EecGuard }, key => Assert.True(rig.Shown(key), key));

        // and so does a definition no title reaches: no SimConnectManager yet, or one whose aircraft has none
        var bare = new BBS757Definition(Map());
        Assert.Equal(new[] { Ignition, FuelPw, FuelRr }, bare.GetPanelControls()[Engines]);
        Assert.True(bare.IsPanelControlVisible(FuelPw, null));
        Assert.True(bare.IsPanelControlVisible(FuelRr, new SimConnectManager(IntPtr.Zero)));   // never connected: no title
    }

    [Fact]
    public void A_load_that_changes_the_engine_type_shows_the_new_pair_on_the_next_panel_built_without_a_section_change()
    {
        // Fix round 1 (the reviewer's case): the pilot listed the Pedestal on the RR, a flight load brings the PW, and the
        // pilot opens Engines again from the same section (MainForm re-lists no section it is in, so GetPanelStructure is
        // not called). The panel built must hold the PW pair.
        var rig = new Rig(title: RrTitle);
        rig.Panels("Pedestal");
        Assert.Equal(new[] { Ignition, FuelRr }, rig.Built(Engines));

        rig.Title = PwTitle;
        Assert.Equal(new[] { Ignition, FuelPw }, rig.Built(Engines));

        rig.Title = "BlueBird 757-200";   // a load onto a title naming neither type: both pairs
        Assert.Equal(new[] { Ignition, FuelPw, FuelRr }, rig.Built(Engines));
    }

    [Fact]
    public void On_the_shipped_map_a_load_from_RR_to_PW_brings_the_PW_pair_without_a_section_change()
    {
        var rig = new Rig(Bbs757ControlMap.Load(), RrTitle);
        rig.Panels("Pedestal");
        string[] pw = { "BBS_fuelcut_PW1", "BBS_fuelcut_PW2" }, rr = { "BBS_fuelcut_RR1", "BBS_fuelcut_RR2" };
        Assert.Equal((false, true), (pw.Any(rig.Built(Engines).Contains), rr.All(rig.Built(Engines).Contains)));

        rig.Title = PwTitle;
        Assert.Equal((true, false), (pw.All(rig.Built(Engines).Contains), rr.Any(rig.Built(Engines).Contains)));

        rig.Title = null;   // no title: both pairs
        Assert.True(pw.Concat(rr).All(rig.Built(Engines).Contains));
    }

    [Fact]
    public void On_the_shipped_map_the_Engines_panel_lists_each_engines_start_and_fuel_control_together_on_either_type()
    {
        // Plan 12 task 10 (the owner: "I wanted engine 1 start then fuel, then engine 2's controls"): the pair for the
        // loaded type keeps the order engine by engine, the other type's switches simply absent.
        var map = Bbs757ControlMap.Load();
        var rig = new Rig(map, RrTitle);
        var label = map.Controls.ToDictionary(c => c.Node, c => c.Label, StringComparer.Ordinal);

        Assert.Equal(new[] { "Ignition", "L ENG Start", "L Fuel Control (RR)", "R ENG Start", "R Fuel Control (RR)" },
            rig.Built(Engines).Where(label.ContainsKey).Select(k => label[k]));
        Assert.Equal(
            new[]
            {
                "B757_OVHD_ENG_START_SWITCH", "B757_OVHD_L_ENG_START_SWITCH", "BBS_fuelcut_RR1",
                "B757_OVHD_R_ENG_START_SWITCH", "BBS_fuelcut_RR2",
            },
            rig.Built(Engines).Where(label.ContainsKey));

        rig.Title = PwTitle;
        Assert.Equal(new[] { "Ignition", "L ENG Start", "L Fuel Control (PW)", "R ENG Start", "R Fuel Control (PW)" },
            rig.Built(Engines).Where(label.ContainsKey).Select(k => label[k]));

        // a title naming neither type: both pairs, still engine by engine
        rig.Title = null;
        Assert.Equal(
            new[]
            {
                "Ignition", "L ENG Start", "L Fuel Control (PW)", "L Fuel Control (RR)", "R ENG Start",
                "R Fuel Control (PW)", "R Fuel Control (RR)",
            },
            rig.Built(Engines).Where(label.ContainsKey).Select(k => label[k]));
    }

    [Fact]
    public void A_panel_build_hands_the_definition_its_SimConnectManager()
    {
        // MainForm asks IsPanelControlVisible of every control as it builds a panel: the first moment after a connection
        // that the definition is handed the manager whose title it reads, before the pilot presses anything.
        var def = new BBS757Definition(Map());
        var sim = new SimConnectManager(IntPtr.Zero);

        def.IsPanelControlVisible(Battery, sim);

        Assert.Same(sim, def.Sim);
    }

    [Fact]
    public void A_change_of_engine_type_rewrites_the_same_section_lists_in_place_and_leaves_the_control_lists_whole()
    {
        var rig = new Rig();   // no title yet: every control
        var structure = rig.Def.GetPanelStructure();
        var controls = rig.Def.GetPanelControls();
        var overhead = structure["Overhead"];
        var engines = controls[Engines];
        var eec = controls[EecPanel];
        var allOverhead = overhead.ToList();
        var allEngines = engines.ToList();

        rig.Title = RrTitle;
        Assert.Same(structure, rig.Def.GetPanelStructure());
        Assert.Equal(allOverhead, overhead);
        Assert.Equal(allEngines, engines);
        Assert.Equal(new[] { Ignition, FuelRr }, rig.Built(Engines));

        rig.Title = PwTitle;   // another aircraft loaded
        Assert.Same(structure, rig.Def.GetPanelStructure());
        Assert.DoesNotContain(EecPanel, overhead);
        Assert.Equal(allEngines, engines);
        Assert.Equal(new[] { EecGuard, Eec }, eec);
        Assert.Equal(new[] { Ignition, FuelPw }, rig.Built(Engines));
        Assert.Empty(rig.Built(EecPanel));

        rig.Title = null;   // a title that names neither
        Assert.Same(structure, rig.Def.GetPanelStructure());
        Assert.Equal(allOverhead, overhead);
        Assert.Equal(allEngines, engines);
        Assert.Equal(allEngines, rig.Built(Engines));
        Assert.Equal(new[] { EecGuard, Eec }, rig.Built(EecPanel));

        // the same instances throughout: the base class and MainForm keep these
        Assert.Same(controls, rig.Def.GetPanelControls());
        Assert.Same(overhead, rig.Def.GetPanelStructure()["Overhead"]);
        Assert.Same(engines, rig.Def.GetPanelControls()[Engines]);
        Assert.Same(eec, rig.Def.GetPanelControls()[EecPanel]);
    }

    [Fact]
    public void A_change_of_engine_type_leaves_the_variables_and_the_status_rows_alone()
    {
        var rig = new Rig();
        var vars = rig.Def.GetVariables();
        var keys = vars.Keys.ToList();
        var displays = rig.Def.GetPanelDisplayVariables();
        var shown = displays.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        var controls = rig.Def.GetPanelControls().ToDictionary(kv => kv.Key, kv => kv.Value.ToList());

        foreach (string? title in new[] { RrTitle, PwTitle, null })
        {
            rig.Title = title;
            rig.Def.GetPanelStructure();

            Assert.Equal(controls, rig.Def.GetPanelControls().ToDictionary(kv => kv.Key, kv => kv.Value.ToList()));
            Assert.Same(vars, rig.Def.GetVariables());
            Assert.Equal(keys, rig.Def.GetVariables().Keys);
            Assert.All(new[] { FuelPw, FuelRr, Eec, EecGuard }, key => Assert.Contains(key, vars.Keys));
            Assert.Equal(shown, rig.Def.GetPanelDisplayVariables().ToDictionary(kv => kv.Key, kv => kv.Value.ToList()));
            Assert.Equal("B757_APU_STATE", rig.Def.GetPanelDisplayVariables()["Status"][0]);   // the map's row, then the readouts'
        }
    }

    [Fact]
    public void On_the_shipped_map_an_RR_loses_the_two_PW_fuel_controls_and_a_PW_the_RR_engine_control_panel()
    {
        var map = Bbs757ControlMap.Load();
        var mapNodes = map.Controls.Select(c => c.Node).ToHashSet(StringComparer.Ordinal);
        var rig = new Rig(map);
        var allEngines = rig.Controls(Engines).ToList();
        var allOverhead = rig.Panels("Overhead").ToList();
        var vars = rig.Def.GetVariables();

        rig.Title = RrTitle;
        Assert.Equal(allOverhead, rig.Panels("Overhead"));
        Assert.Equal(allEngines.Where(k => k is not ("BBS_fuelcut_PW1" or "BBS_fuelcut_PW2")), rig.Built(Engines));
        // what the pilot hears on the Engines panel of an RR (the map's switches; the app's own fields are untouched)
        Assert.Equal(new[] { "Ignition", "L ENG Start", "L Fuel Control (RR)", "R ENG Start", "R Fuel Control (RR)" },
            rig.Built(Engines).Where(mapNodes.Contains).Select(k => vars[k].DisplayName));

        rig.Title = PwTitle;
        Assert.Equal(allOverhead.Where(p => p != EecPanel), rig.Panels("Overhead"));
        Assert.Equal(allEngines.Where(k => k is not ("BBS_fuelcut_RR1" or "BBS_fuelcut_RR2")), rig.Built(Engines));
        Assert.Equal(allEngines, rig.Controls(Engines));   // the list stays whole
    }
}
