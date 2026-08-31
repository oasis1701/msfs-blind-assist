using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;
using A330Exec = MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Pins the five places the HeadwindSim A339X airframe diverges from the FlyByWire
/// A32NX, each measured against the installed packages. These tests exist so a blind
/// re-copy from the A320 profile fails loudly instead of shipping a silent no-op.
/// See docs/superpowers/specs/2026-08-30-headwind-a330-first-officer-design.md.
/// </summary>
public class HwA330DivergenceTests
{
    // --- Divergence 1: nav & logo -------------------------------------------------
    // A32NX_LIGHTS_NAV_LOGO does not exist in the A339X package (A32NX: 14
    // occurrences, A339X: 0). A330_NEO_INTERIOR.xml:2054-2069 binds
    // SWITCH_OVHD_EXTLT_NAVLOGO to stock LIGHT LOGO / LIGHT NAV at index 0.

    [Fact]
    public void A330_nav_logo_state_reads_the_stock_simvar()
    {
        var v = new HeadwindA330Definition().GetVariables()["A32NX_LIGHTS_NAV_LOGO"];
        Assert.Equal("LIGHT NAV", v.Name);
        Assert.Equal(SimVarType.SimVar, v.Type);
    }

    [Fact]
    public void A320_nav_logo_state_still_reads_the_fbw_lvar()
    {
        var v = new FlyByWireA320Definition().GetVariables()["A32NX_LIGHTS_NAV_LOGO"];
        Assert.Equal("A32NX_LIGHTS_NAV_LOGO", v.Name);
        Assert.Equal(SimVarType.LVar, v.Type);
    }

    [Fact]
    public void A330_nav_logo_labels_are_two_position()
    {
        var v = new HeadwindA330Definition().GetVariables()["A32NX_LIGHTS_NAV_LOGO"];
        Assert.Equal("Off", v.ValueDescriptions[0]);
        Assert.Equal("On", v.ValueDescriptions[1]);
        Assert.False(v.ValueDescriptions.ContainsKey(2),
            "The A330 switch is two-position — there is no SYS1/SYS2 concept.");
    }

    // --- Divergence 3: seat-belt sign ---------------------------------------------
    // A330_NEO_INTERIOR.xml:1817-1823 — 0=ON, 1=AUTO, 2=OFF (three positions).
    // A320_NEO_INTERIOR.xml:1756-1762 — 1=ON, 0=OFF. The encoding is INVERTED.

    [Fact]
    public void A330_registers_the_seatbelt_switch_position()
    {
        var v = new HeadwindA330Definition().GetVariables()["SEATBELT_SIGN_POSITION"];
        Assert.Equal("XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position", v.Name);
        Assert.Equal(SimVarType.LVar, v.Type);
        Assert.Equal("On",   v.ValueDescriptions[0]);
        Assert.Equal("Auto", v.ValueDescriptions[1]);
        Assert.Equal("Off",  v.ValueDescriptions[2]);
    }

    // --- Divergence 4: landing lights ---------------------------------------------
    // A330_NEO_INTERIOR.xml:2022-2034 — ONE two-position switch on LIGHT LANDING
    // indices 2 and 3. The A32NX has two Retractable switches on L:LIGHTING_LANDING_2/_3.

    [Fact]
    public void A330_registers_the_stock_landing_light_state()
    {
        var v = new HeadwindA330Definition().GetVariables()["LIGHT LANDING:2"];
        Assert.Equal("LIGHT LANDING:2", v.Name);
        Assert.Equal(SimVarType.SimVar, v.Type);
    }

    // Registering the read-back is only half of it: the First Officer was fixed to use it,
    // but the inherited Exterior Lighting PANEL still listed the two A32NX Retractable
    // positions, so a pilot driving the panel by hand could still do the exact harm the FO
    // fix removed — a combo showing a frozen position (live-measured 2026-08-31:
    // L:LIGHTING_LANDING_2 read 0 with the lights ON and 0 with them OFF) whose RETRACT
    // option commands a detent this airframe does not have.

    [Fact]
    public void A330_exterior_lighting_panel_replaces_the_dead_retractable_keys()
    {
        var panel = new HeadwindA330Definition().GetPanelControls()["Exterior Lighting"];

        Assert.DoesNotContain("LIGHTING_LANDING_2", panel);
        Assert.DoesNotContain("LIGHTING_LANDING_3", panel);
        Assert.Contains("LIGHT LANDING:2", panel);
    }

    [Fact]
    public void A330_offers_the_dead_retractable_keys_on_no_panel_list_at_all()
    {
        var a330 = new HeadwindA330Definition();

        foreach (var (listName, lists) in new (string, Dictionary<string, List<string>>)[]
                 {
                     ("control", a330.GetPanelControls()),
                     ("display", a330.GetPanelDisplayVariables()),
                 })
        foreach (var (panel, keys) in lists)
        foreach (var dead in new[] { "LIGHTING_LANDING_2", "LIGHTING_LANDING_3" })
            Assert.False(keys.Contains(dead),
                $"The A330 {listName} list for panel '{panel}' still offers {dead}, which this "
                + "airframe never writes — its landing lights are ONE ganged two-position switch "
                + "on stock LIGHT LANDING:2/:3.");
    }

    [Fact]
    public void A330_landing_light_row_is_a_read_back_beside_the_working_actuator()
    {
        var a330 = new HeadwindA330Definition();
        var panel = a330.GetPanelControls()["Exterior Lighting"];

        // Nothing in the app writes "LIGHT LANDING:2" — the actuator is the pair of
        // momentary buttons already on this panel (the same one the FO fires). So the
        // replacement row must be a read-only status field, or it is a settable combo
        // that silently does nothing: the dead control this fix exists to remove.
        Assert.True(a330.GetVariables()["LIGHT LANDING:2"].RenderAsReadOnlyStatus);
        Assert.Contains("LANDING_LIGHTS_ON_THIRD_PARTY", panel);
        Assert.Contains("LANDING_LIGHTS_OFF_THIRD_PARTY", panel);
    }

    [Fact]
    public void A320_exterior_lighting_panel_keeps_both_retractable_switches()
    {
        // The two Retractable switches are CORRECT on the A32NX — the fix above must not
        // reach the airframe that really has them.
        var panel = new FlyByWireA320Definition().GetPanelControls()["Exterior Lighting"];

        Assert.Contains("LIGHTING_LANDING_2", panel);
        Assert.Contains("LIGHTING_LANDING_3", panel);
        Assert.DoesNotContain("LIGHT LANDING:2", panel);
    }

    // --- Divergence 2: ECAM SD page indices ---------------------------------------
    // A339X SD bundle: Eng 0, Bleed 1, Press 2, ElecAC 3, ElecDC 4, Hyd 5, Apu 6,
    // Cond 7, Door 8, Wheel 9, Fctl 10, Fuel 11, Crz 12, Status 13, CB 14.
    // The A32NX table maps STS=12, which is CRUISE on the A330.

    [Fact]
    public void A330_ecam_status_page_is_13_not_12()
    {
        Assert.Equal(13, MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor
            .EcamPageIndexMap["ECAM_PAGE_STS"]);
    }

    [Fact]
    public void A330_ecam_hyd_and_fuel_pages_are_shifted()
    {
        var map = MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.EcamPageIndexMap;
        Assert.Equal(5,  map["ECAM_PAGE_HYD"]);
        Assert.Equal(11, map["ECAM_PAGE_FUEL"]);
    }

    [Fact]
    public void A330_ecam_pages_the_first_officer_uses_are_unchanged()
    {
        var map = MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.EcamPageIndexMap;
        Assert.Equal(0, map["ECAM_PAGE_ENG"]);
        Assert.Equal(6, map["ECAM_PAGE_APU"]);
        Assert.Equal(8, map["ECAM_PAGE_DOOR"]);
    }

    // --- Divergence 5: cockpit-lighting potentiometers ----------------------------
    // Pot 10 = CEILING_LIGHT_CS, pot 11 = MAP_LIGHT_CS on the A339X — the Captain's
    // ceiling and map lights, both binary click-toggles. The A320 scene writes 50.

    [Fact]
    public void A330_lighting_scene_does_not_write_the_glareshield_flood_pots()
    {
        var keys = MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.CockpitLightingKeys;
        Assert.DoesNotContain("BRIGHT_GLARESHIELD_CAPT_SET", keys);
        Assert.DoesNotContain("BRIGHT_GLARESHIELD_FO_SET", keys);
    }

    [Fact]
    public void A330_lighting_scene_keeps_the_four_shared_potentiometers()
    {
        var keys = MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.CockpitLightingKeys;
        Assert.Contains("BRIGHT_GLARESHIELD_INTEG_SET", keys);
        Assert.Contains("BRIGHT_OVERHEAD_INTEG_SET", keys);
        Assert.Contains("BRIGHT_MAINPANEL_SET", keys);
        Assert.Contains("BRIGHT_PEDESTAL_SET", keys);
    }

    // CockpitLightingKeys used to be a hand-written array that NO production code read —
    // SetCockpitLighting looped over its own inline copy — so the two tests above passed
    // with both glareshield pots restored to the write loop, i.e. with divergence 5
    // completely reverted. They implied coverage and provided none. The keys are now
    // DERIVED from CockpitLightingPlan, which SetCockpitLighting is the sole consumer of,
    // and the two tests below are what hold the derivation together: the first pins that
    // the tested list is the write path, the second pins the sequence actually written.

    [Fact]
    public void A330_lighting_key_list_is_derived_from_the_scene_write_path()
    {
        // Every scene writes the same keys in the same order, and that order IS
        // CockpitLightingKeys — so a pot added to the plan shows up in the list the
        // glareshield test reads, and a pot added to the list alone cannot exist.
        foreach (var scene in Enum.GetValues<A330Exec.CockpitLightScene>())
            Assert.Equal(A330Exec.CockpitLightingKeys,
                A330Exec.CockpitLightingPlan(scene).Select(p => p.Key).ToList());
    }

    [Fact]
    public void A330_lighting_scene_plan_is_the_seven_shared_keys_with_the_measured_values()
    {
        // The exact ordered (key, value) sequence SetCockpitLighting sends per scene.
        // Re-adding a glareshield pot lengthens the actual sequence and fails here; a
        // changed scene value fails here too. Values are unchanged from the A320 scene.
        AssertPlan(A330Exec.CockpitLightScene.DayPrep,       1, 100, 1, 100, 50);
        AssertPlan(A330Exec.CockpitLightScene.DimFlight,     2, 20,  1, 50,  30);
        AssertPlan(A330Exec.CockpitLightScene.ParkingBright, 1, 100, 1, 100, 50);
        AssertPlan(A330Exec.CockpitLightScene.Off,           1, 0,   0, 0,   0);

        static void AssertPlan(A330Exec.CockpitLightScene scene,
            int ann, int dome, int compass, int integ, int flood)
            => Assert.Equal(
                new[]
                {
                    ("A32NX_OVHD_INTLT_ANN", ann),
                    ("A32NX_OVHD_INTLT_DOME", dome),
                    ("A32NX_STBY_COMPASS_LIGHT_TOGGLE", compass),
                    ("BRIGHT_GLARESHIELD_INTEG_SET", integ),
                    ("BRIGHT_OVERHEAD_INTEG_SET", integ),
                    ("BRIGHT_MAINPANEL_SET", flood),
                    ("BRIGHT_PEDESTAL_SET", flood),
                },
                A330Exec.CockpitLightingPlan(scene));
    }

    // Keeping the two pots out of the FIRST OFFICER's scene was only half of it: the
    // inherited Interior Lighting PANEL still listed both as brightness combos, so a
    // pilot driving the panel by hand could still do the exact harm the FO exclusion
    // removes. DEMONSTRATED LIVE 2026-08-31: writing LIGHT POTENTIOMETER:10 = 50 lit the
    // Captain's ceiling light while L:A339X_CEILING_LIGHT_CAPTAIN still read 0 — the lamp
    // on, its own state var saying off, at a brightness a binary switch cannot produce,
    // and nothing in the cockpit able to resolve the disagreement.

    [Fact]
    public void A330_interior_lighting_panel_drops_the_two_repurposed_pots()
    {
        var panel = new HeadwindA330Definition().GetPanelControls()["Interior Lighting"];

        Assert.DoesNotContain("BRIGHT_GLARESHIELD_CAPT_SET", panel);
        Assert.DoesNotContain("BRIGHT_GLARESHIELD_FO_SET", panel);
    }

    [Fact]
    public void A330_interior_lighting_panel_keeps_the_four_shared_pots()
    {
        // Pots 76 / 83 / 85 / 86 all exist on the A339X and mean there what they mean on
        // the A320 — the same four the FO scene writes. Dropping them too would be an
        // over-broad fix that takes away working controls.
        var panel = new HeadwindA330Definition().GetPanelControls()["Interior Lighting"];

        Assert.Contains("BRIGHT_PEDESTAL_SET", panel);          // pot 76
        Assert.Contains("BRIGHT_GLARESHIELD_INTEG_SET", panel); // pot 83
        Assert.Contains("BRIGHT_MAINPANEL_SET", panel);         // pot 85
        Assert.Contains("BRIGHT_OVERHEAD_INTEG_SET", panel);    // pot 86

        // …and the panel's three non-potentiometer rows, which this fix does not touch.
        Assert.Contains("A32NX_OVHD_INTLT_ANN", panel);
        Assert.Contains("A32NX_OVHD_INTLT_DOME", panel);
        Assert.Contains("A32NX_STBY_COMPASS_LIGHT_TOGGLE", panel);
    }

    [Fact]
    public void A330_offers_the_repurposed_pots_on_no_panel_list_at_all()
    {
        var a330 = new HeadwindA330Definition();

        foreach (var (listName, lists) in new (string, Dictionary<string, List<string>>)[]
                 {
                     ("control", a330.GetPanelControls()),
                     ("display", a330.GetPanelDisplayVariables()),
                 })
        foreach (var (panel, keys) in lists)
        foreach (var repurposed in new[] { "BRIGHT_GLARESHIELD_CAPT_SET", "BRIGHT_GLARESHIELD_FO_SET" })
            Assert.False(keys.Contains(repurposed),
                $"The A330 {listName} list for panel '{panel}' still offers {repurposed}. On this "
                + "airframe that potentiometer is a BINARY click-toggle for a Captain's ceiling / "
                + "map light (A330_NEO_INTERIOR.xml:271-283), not a glareshield flood knob — "
                + "setting it to a level lights an unrelated lamp and desyncs it from its own "
                + "L:A339X_*_LIGHT_CAPTAIN state var.");
    }

    [Fact]
    public void A320_interior_lighting_panel_keeps_all_six_pots()
    {
        // Pots 10 and 11 really are the Captain's and F/O's glareshield floods on the
        // A32NX — the fix above must not reach the airframe where the keys are correct.
        var panel = new FlyByWireA320Definition().GetPanelControls()["Interior Lighting"];

        Assert.Contains("BRIGHT_GLARESHIELD_CAPT_SET", panel);
        Assert.Contains("BRIGHT_GLARESHIELD_FO_SET", panel);
        Assert.Contains("BRIGHT_PEDESTAL_SET", panel);
        Assert.Contains("BRIGHT_GLARESHIELD_INTEG_SET", panel);
        Assert.Contains("BRIGHT_MAINPANEL_SET", panel);
        Assert.Contains("BRIGHT_OVERHEAD_INTEG_SET", panel);
    }

    [Fact]
    public void A330_seatbelt_on_is_position_zero_and_off_is_two()
    {
        Assert.Equal(0, MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.SeatbeltPositionOn);
        Assert.Equal(2, MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.SeatbeltPositionOff);
    }

    // The seat-belt divergence is about the WRITE, not the read. An earlier test here
    // asserted only that the checklist items detect on the sign lamp — a StateFieldName
    // that is byte-identical in the A320 profile, so it passed whether or not the
    // divergence had been applied, and it passed while all three FLOW steps were still
    // firing the A320's bare toggle. The three below pin the write.

    [Fact]
    public void A330_seatbelt_items_read_the_sign_lamp_but_write_the_switch_position()
    {
        var items = MSFSBlindAssist.FirstOfficer.HWA330.HwA330ChecklistDefinitions.Build()
            .SelectMany(g => g.Items)
            .Where(i => i.Id is "BS_SEATBELTS" or "DC_SEATBELTS" or "SD_SEATBELTS_OFF")
            .ToList();

        // Detection stays on the sign LAMP, never the switch position — the A380 invariant.
        Assert.Equal(3, items.Count);
        foreach (var i in items)
            Assert.Equal("CABIN SEATBELTS ALERT SWITCH", i.StateFieldName);

        // ...but the write selects the three-position switch. A bare toggle is fought back
        // within half a second whenever the switch sits in AUTO(1), whose 500 ms block
        // re-drives the stock simvar. The A320 has no AUTO and so no analogue to this.
        Assert.Equal(("SEATBELT_SIGN_POSITION", 0),
            MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.SeatbeltWritePlan(true));
        Assert.Equal(("SEATBELT_SIGN_POSITION", 2),
            MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.SeatbeltWritePlan(false));
    }

    [Fact]
    public void A330_seatbelt_flow_steps_route_through_the_seatbelt_sign_pseudo_key()
    {
        var steps = MSFSBlindAssist.FirstOfficer.HWA330.HwA330FlowDefinitions.Build()
            .SelectMany(f => f.Steps)
            .Where(s => s.Id is "BS_SEATBELTS" or "DC_SEATBELTS" or "SD_SEATBELTS_OFF")
            .ToDictionary(s => s.Id);

        Assert.Equal(3, steps.Count);

        // The pseudo-key DispatchCoreAsync claims, so flow, checklist and phase monitor
        // converge on SetSeatbeltSign — the one path that moves the switch out of AUTO.
        Assert.Equal("SEATBELT_SIGN",
            MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.SeatbeltSignKey);
        foreach (var s in steps.Values)
            Assert.Equal(MSFSBlindAssist.FirstOfficer.HWA330.HwA330ActionExecutor.SeatbeltSignKey,
                s.EventName);

        Assert.Equal(1, steps["BS_SEATBELTS"].TargetValue);
        Assert.Equal(1, steps["DC_SEATBELTS"].TargetValue);
        Assert.Equal(0, steps["SD_SEATBELTS_OFF"].TargetValue);
    }

    [Fact]
    public void No_A330_flow_step_fires_the_bare_stock_seatbelt_toggle_event()
    {
        const string BareToggle = "CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE";

        var offenders = MSFSBlindAssist.FirstOfficer.HWA330.HwA330FlowDefinitions.Build()
            .SelectMany(f => f.Steps)
            .Where(s => s.EventName == BareToggle
                     || s.MultiActions.Any(m => m.EventName == BareToggle))
            .Select(s => s.Id)
            .Order()
            .ToList();

        // That key is Event-typed with no HandleUIVariableSet branch, so ApplyUIVariable
        // returns false and ApplySilent's fallback writes a bogus L:var literally named
        // CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE — then reports success, so the step ticks
        // its checklist item having done nothing to the aircraft.
        Assert.True(offenders.Count == 0,
            "These A330 flow steps still fire the bare stock seat-belt toggle, which reaches "
            + "no write branch and silently no-ops: " + string.Join(", ", offenders));
    }

    // The four seat-belt tests above pin the CONSTANTS (0/2), the checklist items'
    // StateFieldName, the pure SeatbeltWritePlan and the flow steps' pseudo-key — but not
    // one of them observes that SetSeatbeltSignCoreAsync actually PERFORMS the position
    // write. A verification pass reduced that method to the bare A320 form (guarded
    // CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE, no plan call) and the whole suite stayed green
    // at 4245/0: the plan was still declared, still correct and still asserted over — just
    // no longer used by anything. That is the CockpitLightingKeys flaw one level deeper,
    // and here it is the actual airframe bug: the A339X switch is three-position
    // 0=On/1=Auto/2=Off and its AUTO block re-drives the stock simvar every 500 ms, so a
    // bare toggle is undone within half a second and the cockpit switch never moves.
    //
    // The executor cannot be exercised to catch it. DispatchCoreAsync early-returns unless
    // IsAvailable, which needs a live SimConnectManager AND a real ScreenReaderAnnouncer
    // (heavyweight, no parameterless ctor, must never be second-instanced) — and no
    // instrumented SimConnectManager could see the position write either, because that
    // guard swallows it before _sc is touched, leaving the correct implementation and the
    // bare toggle observationally identical. Nor does making the plan "the real write path"
    // help the way it did for CockpitLightingPlan: a plan can only pin what is written IF
    // it is written, and the whole regression is that it stops being.
    //
    // So the consumption is pinned at the source level — the same instrument
    // FoPr160ProcedureFixTests uses for the Fenix APU skip predicates, which are opaque for
    // the same reason. Only the method's own body is read, with comments stripped, so
    // neither prose about the plan nor its declaration elsewhere in the file can pass for a
    // call to it.

    [Fact]
    public void A330_seatbelt_core_write_dispatches_the_position_from_SeatbeltWritePlan()
    {
        string body = ExecutorMethodBody("SetSeatbeltSignCoreAsync");

        Assert.True(body.Contains("SeatbeltWritePlan", StringComparison.Ordinal),
            "SetSeatbeltSignCoreAsync no longer consults SeatbeltWritePlan, so the "
            + "three-position switch is never written. A bare stock toggle cannot move this "
            + "switch: the A339X AUTO position re-drives CABIN SEATBELTS ALERT SWITCH every "
            + "500 ms, so the toggle is undone within half a second while the cockpit switch "
            + "sits in AUTO. Body was:\n" + body);

        var dispatch = Regex.Match(body, @"DispatchCoreAsync\s*\(\s*(?<args>[^)]*?)\s*\)");
        Assert.True(dispatch.Success,
            "SetSeatbeltSignCoreAsync consults SeatbeltWritePlan but never dispatches it. "
            + "The plan is only a description; DispatchCoreAsync is what reaches the "
            + "aircraft. Body was:\n" + body);

        // The dispatched arguments must be forwarded values, not literals re-stating the
        // key and position — a hardcoded copy would drift away from SeatbeltWritePlan
        // silently, which is the decoration this test exists to prevent.
        string args = dispatch.Groups["args"].Value;
        Assert.True(Regex.IsMatch(args, @"^[A-Za-z_]\w*(?:\.\w+)*(?:\s*,\s*[A-Za-z_]\w*(?:\.\w+)*)*$"),
            "SetSeatbeltSignCoreAsync dispatches literals (" + args + ") rather than the "
            + "values it bound from SeatbeltWritePlan, so the plan no longer decides what is "
            + "written. Body was:\n" + body);
    }

    [Fact]
    public void A330_seatbelt_core_write_positions_the_switch_before_reconciling_the_sign_lamp()
    {
        string body = ExecutorMethodBody("SetSeatbeltSignCoreAsync");

        int plan   = body.IndexOf("SeatbeltWritePlan", StringComparison.Ordinal);
        int toggle = body.IndexOf("CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", StringComparison.Ordinal);

        Assert.True(plan >= 0,
            "The switch-POSITION half of the seat-belt write is gone — see the sibling test. "
            + "Body was:\n" + body);
        Assert.True(toggle >= 0,
            "The belt-and-braces lamp reconciliation is gone. Whether the switch template's "
            + "CODE_POS blocks fire on an external L:var write cannot be settled by reading "
            + "it, so the guarded stock toggle is what lights the sign if they do not. "
            + "Body was:\n" + body);
        Assert.True(plan < toggle,
            "The seat-belt write runs in the wrong order: the switch POSITION must be "
            + "written first, because that is what takes the airframe out of AUTO. Toggling "
            + "the sign while still in AUTO is what the 500 ms AUTO block undoes. Body "
            + "was:\n" + body);
    }

    [Fact]
    public void A330_seatbelt_dispatch_arm_still_routes_through_the_core_write()
    {
        // Guards the sibling hole: a body that survives the two tests above is worth
        // nothing if the dispatch arm stops calling it and fires the bare toggle itself.
        string body = ExecutorMethodBody("DispatchCoreAsync");

        var arm = Regex.Match(body, @"SeatbeltSignKey\s*=>(?<rhs>[^\r\n]*)");
        Assert.True(arm.Success,
            "DispatchCoreAsync no longer claims SeatbeltSignKey, so the pseudo-key the flow "
            + "steps and the checklist actions send falls through to ApplySilent's SetLVar "
            + "fallback: it writes a bogus L:var literally named SEATBELT_SIGN and reports "
            + "success. Body was:\n" + body);
        Assert.Contains("SetSeatbeltSignCoreAsync", arm.Groups["rhs"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A330_landing_light_items_read_the_stock_simvar_not_the_retractable_lvar()
    {
        var items = MSFSBlindAssist.FirstOfficer.HWA330.HwA330ChecklistDefinitions.Build()
            .SelectMany(g => g.Items)
            .Where(i => i.Id is "BT_LANDING_LT" or "AL_LANDING_OFF")
            .ToList();

        Assert.Equal(2, items.Count);
        foreach (var i in items)
            Assert.Equal("LIGHT LANDING:2", i.StateFieldName);
    }

    [Fact]
    public void A330_landing_light_conditions_are_two_position()
    {
        var items = MSFSBlindAssist.FirstOfficer.HWA330.HwA330ChecklistDefinitions.Build()
            .SelectMany(g => g.Items).ToDictionary(i => i.Id);

        // ON accepts 1 and rejects 0; OFF accepts 0 and rejects 1. There is no
        // RETRACT position on this airframe, so nothing may test for 2.
        Assert.True(items["BT_LANDING_LT"].StateCondition!(1));
        Assert.False(items["BT_LANDING_LT"].StateCondition!(0));
        Assert.True(items["AL_LANDING_OFF"].StateCondition!(0));
        Assert.False(items["AL_LANDING_OFF"].StateCondition!(1));
        Assert.False(items["AL_LANDING_OFF"].StateCondition!(2));
    }

    // The EPU_NAVLOGO checklist item used to be pinned here as though it were a
    // divergence. It is not: the A320's item is byte-identical (same key, same
    // v => v > 0.5), and that condition accepts the A320's SYS2 value of 2 just as
    // readily as 1 — so the assertion passed on both profiles and its failure message
    // said something untrue about the A320. What actually makes the item correct on
    // the A330 is that the KEY resolves to a stock simvar, which the two registration
    // tests at the top of this file pin. Deleted rather than restated; the write half
    // of the divergence, which had no test at all, is pinned below instead.

    // A K:2: event takes TWO stack operands, index then value. The A339X switch binds
    // SIMVAR_INDEX_1/2 = 0, so the index is 0 — but it still has to be PUSHED. The
    // original write copied the one-operand form out of FlyByWire's own a339x preset
    // procedure file, which left each event reading whatever happened to be beneath it
    // on the stack as its index. Both operands, in the base definition's proven
    // index-then-value order (FlyByWireA320Definition "0 1 (>K:2:LOGO_LIGHTS_SET)").

    [Fact]
    public void A330_nav_logo_write_pushes_both_operands_for_every_indexed_event()
    {
        foreach (bool on in new[] { true, false })
        {
            var tokens = HeadwindA330Definition.NavLogoRpn(on).Split(' ');

            var indexedEvents = tokens
                .Select((t, i) => (Token: t, Index: i))
                .Where(x => x.Token.StartsWith("(>K:2:", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(2, indexedEvents.Count);   // LOGO_LIGHTS_SET and NAV_LIGHTS_SET

            foreach (var (token, index) in indexedEvents)
            {
                int operands = 0;
                for (int i = index - 1; i >= 0 && IsNumericToken(tokens[i]); i--) operands++;

                Assert.True(operands == 2,
                    $"{token} (on={on}) is handed {operands} operand(s), not 2. A K:2: event "
                    + "pops index THEN value; supplying only the value makes it read a garbage "
                    + "index from whatever is left on the stack.");
            }
        }
    }

    [Fact]
    public void A330_nav_logo_write_keeps_the_direct_simvar_writes_and_index_then_value_order()
    {
        Assert.Equal(
            "1 (>A:LIGHT NAV) 1 (>A:LIGHT LOGO) 0 1 (>K:2:LOGO_LIGHTS_SET) 0 1 (>K:2:NAV_LIGHTS_SET)",
            HeadwindA330Definition.NavLogoRpn(true));

        Assert.Equal(
            "0 (>A:LIGHT NAV) 0 (>A:LIGHT LOGO) 0 0 (>K:2:LOGO_LIGHTS_SET) 0 0 (>K:2:NAV_LIGHTS_SET)",
            HeadwindA330Definition.NavLogoRpn(false));
    }

    [Fact]
    public void A330_evaluator_polls_the_stock_landing_light_state()
    {
        Assert.Contains("LIGHT LANDING:2",
            new MSFSBlindAssist.FirstOfficer.HWA330.HwA330StateEvaluator().OnRequestPollFields);
    }

    private static bool IsNumericToken(string t) =>
        double.TryParse(t, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _);

    // --- source-level helpers for the seat-belt write path ----------------------------
    // Used only by the three tests above; see the commentary there for why the executor
    // cannot be exercised instead. Path resolution follows FoPr160ProcedureFixTests
    // (CallerFilePath, resolved at compile time), one directory deeper.

    private static string ExecutorSourcePath([CallerFilePath] string thisTestFilePath = "")
    {
        string path = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisTestFilePath)!,
            "..", "..", "..", "MSFSBlindAssist", "FirstOfficer", "HWA330",
            "HwA330ActionExecutor.cs"));
        Assert.True(File.Exists(path),
            "HwA330ActionExecutor.cs was not found at " + path + ". If the file moved, "
            + "update this path — do not delete the tests that read it; they are the only "
            + "thing standing between the seat-belt write and a silent revert to the A320 "
            + "bare toggle.");
        return path;
    }

    /// <summary>
    /// The body of one HwA330ActionExecutor method, comments removed. Only declarations
    /// are matched (a call site is followed by <c>;</c> or <c>,</c>, never <c>{</c>), so
    /// the switch arm that CALLS SetSeatbeltSignCoreAsync is skipped in favour of the
    /// method itself.
    /// </summary>
    private static string ExecutorMethodBody(string methodName)
    {
        string src = StripCommentsKeepingLiterals(File.ReadAllText(ExecutorSourcePath()));

        foreach (Match m in Regex.Matches(src, $@"\b{Regex.Escape(methodName)}\s*\("))
        {
            int open = src.IndexOf('(', m.Index);
            int closeParen = MatchingBracket(src, open, '(', ')');
            if (closeParen < 0) continue;

            int j = closeParen + 1;
            while (j < src.Length && char.IsWhiteSpace(src[j])) j++;
            if (j >= src.Length || src[j] != '{') continue;   // a call, not the declaration

            int closeBrace = MatchingBracket(src, j, '{', '}');
            if (closeBrace < 0) continue;
            return src.Substring(j + 1, closeBrace - j - 1);
        }

        Assert.Fail($"HwA330ActionExecutor no longer declares a {methodName}(...) method with "
            + "a block body. If it was renamed, re-point these tests at the new name; if it "
            + "was inlined away, the seat-belt write path has lost its only guard.");
        return string.Empty;   // unreachable — Assert.Fail throws
    }

    /// <summary>
    /// Strips // and /* */ comments while leaving string and char literals intact (the
    /// assertions look for a literal event name). Without this, a comment naming
    /// SeatbeltWritePlan inside a reverted body would read as a call to it.
    /// </summary>
    private static string StripCommentsKeepingLiterals(string src)
    {
        var sb = new StringBuilder(src.Length);
        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];

            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n') i++;
                if (i < src.Length) sb.Append('\n');
                continue;
            }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                i++;                      // land on '/', the loop's i++ steps past it
                sb.Append(' ');
                continue;
            }
            if (c == '@' && i + 1 < src.Length && src[i + 1] == '"')
            {
                sb.Append(c).Append('"');
                i += 2;
                while (i < src.Length)
                {
                    if (src[i] == '"')
                    {
                        if (i + 1 < src.Length && src[i + 1] == '"') { sb.Append("\"\""); i += 2; continue; }
                        sb.Append('"');
                        break;            // i is ON the closing quote; the loop's i++ passes it
                    }
                    sb.Append(src[i]);
                    i++;
                }
                continue;
            }
            if (c == '"' || c == '\'')
            {
                sb.Append(c);
                i++;
                while (i < src.Length)
                {
                    if (src[i] == '\\' && i + 1 < src.Length)
                    {
                        sb.Append(src[i]).Append(src[i + 1]);
                        i += 2;
                        continue;
                    }
                    sb.Append(src[i]);
                    if (src[i] == c) break;
                    i++;
                }
                continue;
            }

            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Index of the bracket closing the one at <paramref name="start"/>, skipping
    /// string and char literals (comments are already gone).</summary>
    private static int MatchingBracket(string s, int start, char open, char close)
    {
        int depth = 0;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];

            if (c == '"' || c == '\'')
            {
                bool verbatim = c == '"' && i > 0 && s[i - 1] == '@';
                i++;
                while (i < s.Length)
                {
                    if (!verbatim && s[i] == '\\') { i += 2; continue; }
                    if (s[i] == c)
                    {
                        if (verbatim && i + 1 < s.Length && s[i + 1] == '"') { i += 2; continue; }
                        break;
                    }
                    i++;
                }
                continue;
            }

            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }
}
