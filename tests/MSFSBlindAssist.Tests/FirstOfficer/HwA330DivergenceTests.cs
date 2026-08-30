using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;

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

    [Fact]
    public void A330_nav_logo_item_accepts_the_stock_bool()
    {
        var item = MSFSBlindAssist.FirstOfficer.HWA330.HwA330ChecklistDefinitions.Build()
            .SelectMany(g => g.Items).First(i => i.Id == "EPU_NAVLOGO");

        Assert.Equal("A32NX_LIGHTS_NAV_LOGO", item.StateFieldName);
        Assert.True(item.StateCondition!(1),
            "The A330 switch is a stock Bool — ON is 1, not the A320's SYS2 value of 2.");
        Assert.False(item.StateCondition!(0));
    }

    [Fact]
    public void A330_evaluator_polls_the_stock_landing_light_state()
    {
        Assert.Contains("LIGHT LANDING:2",
            new MSFSBlindAssist.FirstOfficer.HWA330.HwA330StateEvaluator().OnRequestPollFields);
    }
}
