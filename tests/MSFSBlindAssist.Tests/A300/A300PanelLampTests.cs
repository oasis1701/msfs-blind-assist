using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The cockpit lights read through their own generated rule (<see cref="A300LampRule"/>): each named from
/// its button and legend, on its system's panel, spoken as the cockpit lights it and shown in its panel's
/// status box, with its own Ctrl+M row (design decision 2026-10-09: every light that can be read is spoken).
/// </summary>
public class A300PanelLampTests
{
    private static readonly A300Placement Placement = A300PanelLayout.Place(A300ControlMap.Load());

    private static A300PanelLamp Lamp(string node) => A300PanelLamps.All.Single(l => l.Node == node);

    [Fact]
    public void Every_named_lamp_is_a_lamp_the_cockpit_draws()
    {
        var nodes = A300ControlMap.Load().Lamps.Select(l => l.Node).ToHashSet();
        Assert.All(A300PanelLamps.All, l => Assert.Contains(l.Node, nodes));
    }

    [Fact]
    public void Every_named_lamp_is_distinct_named_and_on_a_real_panel()
    {
        var panels = Placement.Structure.Values.SelectMany(p => p).ToHashSet();
        Assert.Equal(A300PanelLamps.All.Count, A300PanelLamps.All.Select(l => l.Node).Distinct().Count());
        var names = A300PanelLamps.All.Select(l => l.Name).Concat(A300Announcements.Lamps.Select(l => l.Name)).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(A300PanelLamps.All, l =>
        {
            Assert.EndsWith(" light", l.Name);
            Assert.Contains(l.Panel, panels);
        });
    }

    [Fact]
    public void No_named_lamp_repeats_a_light_already_read()
    {
        var read = A300FaultLights.All.Select(l => "L:" + l.Var).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(A300PanelLamps.Resolved, l => Assert.DoesNotContain(l.Primary.Id, read));
    }

    [Theory]
    [InlineData("EXT_PWR_SEQ1_LIGHT", "External power available light")]
    [InlineData("AC_BUS_1_OFF_LIGHT", "AC bus 1 off light")]
    [InlineData("AC_ESS_BUS_OFF_LIGHT", "AC essential bus off light")]
    [InlineData("AC_EMER_ON_INV_LIGHT", "AC emergency bus on inverter light")]
    [InlineData("DC_NORM_BUS_LIGHT", "DC normal bus off light")]
    [InlineData("DC_ESS_ONBAT_LIGHT", "DC essential bus on battery light")]
    [InlineData("IDG_1_SEQ1_LIGHT", "IDG 1 disconnected light")]
    [InlineData("OVR_SUP_2_SEQ2_LIGHT", "Override supply 2 flow light")]
    public void The_electrical_lights_are_named_from_their_buttons(string node, string name)
    {
        Assert.Equal(name, Lamp(node).Name);
        Assert.Equal("Electrical", Lamp(node).Panel);
    }

    [Theory]
    // The second and third mode panels' lamp nodes carry IRS 3 and IRS 2 in iniBuilds' numbering, in another
    // order: each light is named from the IRS its rule reads.
    [InlineData("IRS_1_009_LIGHT", "IRS 1 align light")]
    [InlineData("IRS_1_010_LIGHT", "IRS 1 on battery light")]
    [InlineData("IRS_1_011_LIGHT", "IRS 1 battery fault light")]
    [InlineData("IRS_1_012_LIGHT", "IRS 1 fault light")]
    [InlineData("IRS_2_009_LIGHT", "IRS 3 align light")]
    [InlineData("IRS_2_012_LIGHT", "IRS 3 fault light")]
    [InlineData("IRS_3_009_LIGHT", "IRS 2 fault light")]
    [InlineData("IRS_3_010_LIGHT", "IRS 2 battery fault light")]
    [InlineData("IRS_3_011_LIGHT", "IRS 2 on battery light")]
    [InlineData("IRS_3_012_LIGHT", "IRS 2 align light")]
    public void The_irs_lights_are_named_from_the_irs_they_read(string node, string name)
    {
        Assert.Equal(name, Lamp(node).Name);
        Assert.Equal("IRS", Lamp(node).Panel);
        int irs = name[4] - '0';
        Assert.Contains($"INI_IRS{irs}_", A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node).Primary.Name);
    }

    [Theory]
    [InlineData("APU_AVAIL_SEQ1_LIGHT", "APU available light")]
    [InlineData("APU_START_SEQ2_LIGHT", "APU start on light")]
    [InlineData("APU_START_SEQ1_LIGHT", "APU starting light")]
    [InlineData("APU_FUELSHUT_SEQ1_LIGHT", "APU fuel shutoff valve open light")]
    public void The_apu_lights_are_named_from_their_buttons(string node, string name)
    {
        Assert.Equal(name, Lamp(node).Name);
        Assert.Equal("APU", Lamp(node).Panel);
    }

    [Theory]
    [InlineData("FIRE_HANDLE_ENG1_LIGHT", "Engine 1 fire handle light", "INI_ENG1_FIRE_TEST")]
    [InlineData("FIRE_HANDLE_ENG2_LIGHT", "Engine 2 fire handle light", "INI_ENG2_FIRE_TEST")]
    [InlineData("FIRE_HANDLE_APU_LIGHT", "APU fire handle light", "INI_APU_FIRE_TEST")]
    [InlineData("FIRE_1_AG1_SEQ1_LIGHT", "Engine 1 agent 1 squib light", "INI_engine1_agent1_squib")]
    [InlineData("FIRE_1_AG2_SEQ1_LIGHT", "Engine 1 agent 2 squib light", "INI_engine1_agent2_squib")]
    [InlineData("FIRE_2_AG1_SEQ1_LIGHT", "Engine 2 agent 1 squib light", "INI_engine2_agent1_squib")]
    [InlineData("FIRE_2_AG2_SEQ1_LIGHT", "Engine 2 agent 2 squib light", "INI_engine2_agent2_squib")]
    [InlineData("FIRE_APU_AG_SEQ1_LIGHT", "APU agent squib light", "INI_apu_agent_squib")]
    public void The_fire_lights_are_named_from_their_buttons_on_the_dc_light_bus(string node, string name, string var)
    {
        Assert.Equal((name, "Fire"), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Dc), (lamp.Primary.Name, lamp.Power));
    }

    [Theory]
    [InlineData("OUT_TK_PMP_L1_SEQ1_LIGHT", "Left outer tank pump 1 low pressure light", "INI_OUTER_TANK1_LEFT_low_pressure")]
    [InlineData("OUT_TK_PMP_L2_SEQ1_LIGHT", "Left outer tank pump 2 low pressure light", "INI_OUTER_TANK2_LEFT_low_pressure")]
    [InlineData("OUT_TK_PMP_R1_SEQ1_LIGHT", "Right outer tank pump 1 low pressure light", "INI_OUTER_TANK1_RIGHT_low_pressure")]
    [InlineData("OUT_TK_PMP_R2_SEQ1_LIGHT", "Right outer tank pump 2 low pressure light", "INI_OUTER_TANK2_RIGHT_low_pressure")]
    [InlineData("INR_TK_PMP_L1_SEQ1_LIGHT", "Left inner tank pump 1 low pressure light", "INI_INNER_TANK1_LEFT_low_pressure")]
    [InlineData("INR_TK_PMP_L2_SEQ1_LIGHT", "Left inner tank pump 2 low pressure light", "INI_INNER_TANK2_LEFT_low_pressure")]
    [InlineData("INR_TK_PMP_R1_SEQ1_LIGHT", "Right inner tank pump 1 low pressure light", "INI_INNER_TANK1_RIGHT_low_pressure")]
    [InlineData("INR_TK_PMP_R2_SEQ1_LIGHT", "Right inner tank pump 2 low pressure light", "INI_INNER_TANK2_RIGHT_low_pressure")]
    // iniBuilds wires the left center pump's lamp to the right inner pump 1's flag: read as the cockpit lights it.
    [InlineData("CTR_TK_PMP_L_SEQ1_LIGHT", "Left center tank pump low pressure light", "INI_INNER_TANK1_RIGHT_low_pressure")]
    [InlineData("CTR_TK_PMP_R_SEQ1_LIGHT", "Right center tank pump low pressure light", "INI_CENTER_TANK2_low_pressure")]
    [InlineData("TRMTK_1_PMP_SEQ1_LIGHT", "Left trim tank pump low pressure light", "INI_trim_tank_pump1_low_pressure")]
    [InlineData("TRMTK_2_PMP_SEQ1_LIGHT", "Right trim tank pump low pressure light", "INI_trim_tank_pump2_low_pressure")]
    [InlineData("TRMTK_ISO_SEQ1_LIGHT", "Trim tank isolation valve flow bar light", "INI_TRIM_TANK_ISOL_VALVE_BAR")]
    public void The_fuel_lights_are_named_from_their_buttons_on_the_ac_light_bus(string node, string name, string var)
    {
        Assert.Equal((name, "Fuel"), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Ac), (lamp.Primary.Name, lamp.Power));
    }

    [Theory]
    // The pack flow bars show the pack valve itself: with no bleed air they go out while the buttons stay On
    // (measured 2026-10-10). The ram air light is the valve, which travels after its switch.
    [InlineData("PACK_1_VALVE_IND_003_LIGHT", "Pack 1 flow bar light", "INI_bleed_pack1_percent")]
    [InlineData("PACK_2_VALVE_IND_004_LIGHT", "Pack 2 flow bar light", "INI_bleed_pack2_percent")]
    [InlineData("RAM_AIR_SEQ1_LIGHT", "Ram air valve open light", "INI_bleed_ram_air_open")]
    public void The_air_conditioning_lights_are_named_from_their_buttons_on_the_dc_light_bus(string node, string name, string var)
    {
        Assert.Equal((name, "Air Conditioning"), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Dc), (lamp.Primary.Name, lamp.Power));
    }

    [Fact]
    public void The_bleed_panels_ground_bleed_valve_light_is_read()
    {
        // An indicator with no button of its own, on the AC light bus; it followed the APU bleed (2026-10-10).
        Assert.Equal(("Ground bleed valve light", "Bleed"), (Lamp("GND_BLEED_VLVE_SEQ1_LIGHT").Name, Lamp("GND_BLEED_VLVE_SEQ1_LIGHT").Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == "GND_BLEED_VLVE_SEQ1_LIGHT");
        Assert.Equal(("INI_GND_BLEED", A300LightPower.Ac), (lamp.Primary.Name, lamp.Power));
    }

    [Theory]
    [InlineData("COCKPIT_DOOR_STATUS_SEQ1_LIGHT", "Cockpit door open light", "Cockpit Door", "INI_COCKPIT_DOOR_OPEN")]
    // The smoke test lights all nine while held (sampled 2026-10-10); a detector's legend is in no file, so it is
    // named from its button.
    [InlineData("DECK_MID1_SMOKE_LIGHT", "Main deck mid 1 smoke light", "Cargo Smoke", "INI_MAIN_MID1_SMOKE")]
    [InlineData("DECK_MID2_SMOKE_LIGHT", "Main deck mid 2 smoke light", "Cargo Smoke", "INI_MAIN_MID2_SMOKE")]
    [InlineData("DECK_AFT_SMOKE_LIGHT", "Main deck aft smoke light", "Cargo Smoke", "INI_MAIN_AFT_SMOKE")]
    [InlineData("SMOKE_MID1_1_SEQ1_LIGHT", "Main deck smoke detector mid 1 left light", "Cargo Smoke", "INI_SMOKE_MID1_1_LOOP")]
    [InlineData("SMOKE_MID1_2_SEQ1_LIGHT", "Main deck smoke detector mid 1 right light", "Cargo Smoke", "INI_SMOKE_MID1_2_LOOP")]
    [InlineData("SMOKE_MID2_1_SEQ1_LIGHT", "Main deck smoke detector mid 2 left light", "Cargo Smoke", "INI_SMOKE_MID2_1_LOOP")]
    [InlineData("SMOKE_MID2_2_SEQ1_LIGHT", "Main deck smoke detector mid 2 right light", "Cargo Smoke", "INI_SMOKE_MID2_2_LOOP")]
    [InlineData("SMOKE_AFT_1_SEQ1_LIGHT", "Main deck smoke detector aft left light", "Cargo Smoke", "INI_SMOKE_AFT1_LOOP")]
    [InlineData("SMOKE_AFT_2_SEQ1_LIGHT", "Main deck smoke detector aft right light", "Cargo Smoke", "INI_SMOKE_AFT2_LOOP")]
    public void The_door_and_cargo_smoke_lights_are_on_the_dc_light_bus(string node, string name, string panel, string var)
    {
        Assert.Equal((name, panel), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Dc), (lamp.Primary.Name, lamp.Power));
    }

    [Theory]
    // The manual's "blue OPEN light" goes out when the start valve closes (about 49 percent N2, measured on the
    // engine 2 start 2026-10-10); the armed legend is lit for an engine armed for start and not yet running.
    [InlineData("ENG_1_START_SEQ1_LIGHT", "Engine 1 start valve open light", "INI_STARTER1_OPEN")]
    [InlineData("ENG_2_START_SEQ1_LIGHT", "Engine 2 start valve open light", "INI_STARTER2_OPEN")]
    [InlineData("ENG_1_START_SEQ2_LIGHT", "Engine 1 starter armed light", "INI_ENG1_STARTER_ARM")]
    [InlineData("ENG_2_START_SEQ2_LIGHT", "Engine 2 starter armed light", "INI_ENG2_STARTER_ARM")]
    public void The_engine_start_lights_are_on_the_ac_light_bus(string node, string name, string var)
    {
        Assert.Equal((name, "Engine Start"), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Ac), (lamp.Primary.Name, lamp.Power));
    }

    [Fact]
    public void A_pack_flow_bar_is_lit_from_half_open()
    {
        var bar = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == "PACK_1_VALVE_IND_003_LIGHT");
        Assert.False(A300LampBoard.IsLit(A300LampBoard.ById[bar.Lamp.Key], k => k == bar.Lamp.Key ? 0.32 : 1));
        Assert.True(A300LampBoard.IsLit(A300LampBoard.ById[bar.Lamp.Key], k => k == bar.Lamp.Key ? 1 : 1));
    }

    [Theory]
    [InlineData("PRESS_SYS_1_SEQ1_LIGHT", "Pressurization system 1 light", "INI_cabin_sys1", "A300_PRESS_SYS_1")]
    [InlineData("PRESS_SYS_2_SEQ1_LIGHT", "Pressurization system 2 light", "INI_cabin_sys2", "A300_PRESS_SYS_2")]
    public void A_pressurization_system_button_is_labelled_by_its_light(string node, string name, string var, string button)
    {
        // The two buttons pick one system or the other (a press of the picked one does nothing), as the TRP
        // buttons pick a mode: so they are buttons, and each one's light is spoken and labels it.
        Assert.Equal((name, "Pressurization"), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Ac), (lamp.Primary.Name, lamp.Power));
        Assert.Equal(lamp.Lamp.Key, A300PanelLamps.ByButton[button]);

        var cache = new Dictionary<string, double> { [lamp.Lamp.Key] = 1, [A300LampBoard.AcPowerKey] = 1 };
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        Assert.Contains(lamp.Lamp.Key, def.GetVariables()[button].StateVariables!);
        Assert.True(def.TryDescribeControlState(button, out var lit));
        cache[lamp.Lamp.Key] = 0;
        Assert.True(def.TryDescribeControlState(button, out var dark));
        Assert.Equal(("On", "Off"), (lit, dark));
    }

    [Theory]
    // Each EFIS panel's map filter, FPA, decision height and ATC message buttons are lit while selected: as the
    // pressurization system buttons, each is labeled by its own light, which also speaks a press.
    [InlineData("EFIS_CSTR_CPT_SEQ1_LIGHT", "Captain constraints light", "EFIS Captain", "A300_EFIS_CSTR_CPT")]
    [InlineData("EFIS_WPT_CPT_SEQ1_LIGHT", "Captain waypoints light", "EFIS Captain", "A300_EFIS_WPT_CPT")]
    [InlineData("EFIS_VOR_CPT_SEQ1_LIGHT", "Captain VORs light", "EFIS Captain", "A300_EFIS_VOR_CPT")]
    [InlineData("EFIS_NDB_CPT_SEQ1_LIGHT", "Captain NDBs light", "EFIS Captain", "A300_EFIS_NDB_CPT")]
    [InlineData("EFIS_ARPT_CPT_SEQ1_LIGHT", "Captain airports light", "EFIS Captain", "A300_EFIS_ARPT_CPT")]
    [InlineData("CPT_FPA_SEQ1_LIGHT", "Captain FPA light", "EFIS Captain", "A300_CPT_FPA")]
    [InlineData("DH_CPT_SEQ1_LIGHT", "Captain decision height light", "EFIS Captain", "A300_DH_CPT")]
    [InlineData("ATC_CPT_SEQ1_LIGHT", "Captain ATC message light", "EFIS Captain", "A300_ATC_CPT")]
    [InlineData("EFIS_FO_CSTR_SEQ1_LIGHT", "First officer constraints light", "EFIS First Officer", "A300_EFIS_FO_CSTR")]
    [InlineData("EFIS_FO_WPT_SEQ1_LIGHT", "First officer waypoints light", "EFIS First Officer", "A300_EFIS_FO_WPT")]
    [InlineData("EFIS_FO_VOR_SEQ1_LIGHT", "First officer VORs light", "EFIS First Officer", "A300_EFIS_FO_VOR")]
    [InlineData("EFIS_FO_NDB_SEQ1_LIGHT", "First officer NDBs light", "EFIS First Officer", "A300_EFIS_FO_NDB")]
    [InlineData("EFIS_FO_ARPT_SEQ1_LIGHT", "First officer airports light", "EFIS First Officer", "A300_EFIS_FO_ARPT")]
    [InlineData("FO_FPA_SEQ1_LIGHT", "First officer FPA light", "EFIS First Officer", "A300_FO_FPA")]
    [InlineData("FO_DH_SEQ1_LIGHT", "First officer decision height light", "EFIS First Officer", "A300_FO_DH")]
    [InlineData("ATC_FO_SEQ1_LIGHT", "First officer ATC message light", "EFIS First Officer", "A300_ATC_FO")]
    public void An_efis_button_is_labelled_by_its_light(string node, string name, string panel, string button)
    {
        Assert.Equal((name, panel), (Lamp(node).Name, Lamp(node).Panel));
        Assert.Equal(Lamp(node).Key, A300PanelLamps.ByButton[button]);
        Assert.Equal(panel, Placement.RowsByPanel.Single(p => p.Value.Any(r => r.Key == button)).Key);
    }

    [Theory]
    // Each switching button carries both pilots' legends, crossed; each pilot's own legend labels their button.
    [InlineData("CPT_ATT_HDG_SEQ2_LIGHT", "Captain attitude and heading to IRS 3 light", "A300_CPT_ATT_HDG", "INI_capt_switch_att_hdg_sys3")]
    [InlineData("CPT_ADC_INST_SEQ2_LIGHT", "Captain air data to system 2 light", "A300_CPT_ADC_INST", "INI_capt_switch_adc_inst_fo1")]
    [InlineData("CPT_FD_PUSH_SEQ2_LIGHT", "Captain flight director to system 2 light", "A300_CPT_FD_PUSH", "INI_capt_switch_fd_fo1")]
    [InlineData("CPT_EFIS_SGU_SEQ2_LIGHT", "Captain EFIS to SGU 3 light", "A300_CPT_EFIS_SGU", "INI_capt_switch_efis_sgu_fo3")]
    [InlineData("FO_SW_ATT_SEQ2_LIGHT", "First officer attitude and heading to IRS 3 light", "A300_FO_SW_ATT", "INI_fo_switch_att_hdg_sys3")]
    [InlineData("FO_SW_ADC_SEQ2_LIGHT", "First officer air data to system 2 light", "A300_FO_SW_ADC", "INI_fo_switch_adc_inst_fo1")]
    [InlineData("FO_SW_FD_SEQ2_LIGHT", "First officer flight director to system 2 light", "A300_FO_SW_FD", "INI_fo_switch_fd_fo1")]
    [InlineData("FO_SW_EFIS_SEQ2_LIGHT", "First officer EFIS to SGU 3 light", "A300_FO_SW_EFIS", "INI_fo_switch_efis_sgu_fo3")]
    public void A_source_switching_button_is_labelled_by_its_pilots_legend(string node, string name, string button, string var)
    {
        Assert.Equal((name, "Source Switching"), (Lamp(node).Name, Lamp(node).Panel));
        Assert.Equal(Lamp(node).Key, A300PanelLamps.ByButton[button]);
        Assert.Equal(var, A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node).Primary.Name);
    }

    [Theory]
    // Each ECAM page button is lit while its page is the one the lower ECAM shows, picked or automatic
    // (INI_ecam_active_page; ECAM_CURRENT_STATUS is 1 picked, 2 automatic, measured 2026-10-10): its light labels it.
    [InlineData("ECAM_ENG_SEQ2_LIGHT", "Engine page light", "A300_ECAM_ENG", 1)]
    [InlineData("ECAM_HYD_SEQ2_LIGHT", "Hydraulic page light", "A300_ECAM_HYD", 2)]
    [InlineData("ECAM_AC_SEQ2_LIGHT", "AC electrical page light", "A300_ECAM_AC", 3)]
    [InlineData("ECAM_DC_SEQ2_LIGHT", "DC electrical page light", "A300_ECAM_DC", 4)]
    [InlineData("ECAM_BLEED_SEQ2_LIGHT", "Bleed page light", "A300_ECAM_BLEED", 5)]
    [InlineData("ECAM_COND_SEQ2_LIGHT", "Air conditioning page light", "A300_ECAM_COND", 6)]
    [InlineData("ECAM_PRESS_SEQ2_LIGHT", "Pressurization page light", "A300_ECAM_PRESS", 7)]
    [InlineData("ECAM_FUEL_SEQ2_LIGHT", "Fuel page light", "A300_ECAM_FUEL", 8)]
    [InlineData("ECAM_APU_SEQ2_LIGHT", "APU page light", "A300_ECAM_APU", 9)]
    [InlineData("ECAM_FCTL_SEQ2_LIGHT", "Flight controls page light", "A300_ECAM_FCTL", 10)]
    [InlineData("ECAM_DOOR_SEQ2_LIGHT", "Doors page light", "A300_ECAM_DOOR", 11)]
    [InlineData("ECAM_WHEEL_SEQ2_LIGHT", "Wheels page light", "A300_ECAM_WHEEL", 12)]
    public void An_ecam_page_button_is_labelled_by_its_page_light(string node, string name, string button, int page)
    {
        Assert.Equal((name, "ECAM Control Panel"), (Lamp(node).Name, Lamp(node).Panel));
        Assert.Equal(Lamp(node).Key, A300PanelLamps.ByButton[button]);
        Assert.Equal("ECAM Control Panel", Placement.RowsByPanel.Single(p => p.Value.Any(r => r.Key == button)).Key);

        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        bool Lit(double shown, double status) => A300LampBoard.IsLit(A300LampBoard.ById[lamp.Lamp.Key],
            k => k == lamp.Lamp.Key ? shown : k.Contains("ECAM_CURRENT_STATUS") ? status : 1) == true;
        Assert.Equal((true, true, false, false), (Lit(page, 1), Lit(page, 2), Lit(page, 0), Lit(page == 1 ? 2 : 1, 1)));
    }

    [Theory]
    // Each audio panel's VHF transmit keys pick one radio to transmit on (XMLVAR_COM_PANEL1/2_Transmit_Channel 0, 1, 2,
    // measured 2026-10-10): the picked key is lit, and its light labels it.
    [InlineData("CPT_VHF1_LX_LIGHT", "Captain VHF 1 transmit light", "Audio Control Panel Captain", "A300_CPT_VHF1_PUSH", 0)]
    [InlineData("CPT_VHF2_LX_LIGHT", "Captain VHF 2 transmit light", "Audio Control Panel Captain", "A300_CPT_VHF2_PUSH", 1)]
    [InlineData("CPT_VHF3_LX_LIGHT", "Captain VHF 3 transmit light", "Audio Control Panel Captain", "A300_CPT_VHF3_PUSH", 2)]
    [InlineData("FO_VHF1_LX_LIGHT", "First officer VHF 1 transmit light", "Audio Control Panel First Officer", "A300_FO_VHF1_PUSH", 0)]
    [InlineData("FO_VHF2_LX_LIGHT", "First officer VHF 2 transmit light", "Audio Control Panel First Officer", "A300_FO_VHF2_PUSH", 1)]
    [InlineData("FO_VHF3_LX_LIGHT", "First officer VHF 3 transmit light", "Audio Control Panel First Officer", "A300_FO_VHF3_PUSH", 2)]
    public void A_vhf_transmit_key_is_labelled_by_its_light(string node, string name, string panel, string button, int channel)
    {
        Assert.Equal((name, panel), (Lamp(node).Name, Lamp(node).Panel));
        Assert.Equal(Lamp(node).Key, A300PanelLamps.ByButton[button]);
        Assert.Equal(panel, Placement.RowsByPanel.Single(p => p.Value.Any(r => r.Key == button)).Key);
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        bool Lit(double picked) => A300LampBoard.IsLit(A300LampBoard.ById[lamp.Lamp.Key], k => k == lamp.Lamp.Key ? picked : 1) == true;
        Assert.Equal((true, false), (Lit(channel), Lit((channel + 1) % 3)));
    }

    [Fact]
    public void The_ecam_clear_button_is_labelled_by_its_light()
    {
        // The CLR button is lit while there is something to clear: the fault light INI_ECAM_CLR_LIGHT.
        Assert.Equal("A300_LAMP_ECAM_CLR_LIGHT", A300PanelLamps.ByButton["A300_ECAM_CLR"]);
        var cache = new Dictionary<string, double> { ["A300_LAMP_ECAM_CLR_LIGHT"] = 1, [A300LampBoard.AcPowerKey] = 1 };
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        Assert.Contains("A300_LAMP_ECAM_CLR_LIGHT", def.GetVariables()["A300_ECAM_CLR"].StateVariables!);
        Assert.True(def.TryDescribeControlState("A300_ECAM_CLR", out var lit));
        cache["A300_LAMP_ECAM_CLR_LIGHT"] = 0;
        Assert.True(def.TryDescribeControlState("A300_ECAM_CLR", out var dark));
        Assert.Equal(("On", "Off"), (lit, dark));
    }

    [Theory]
    [InlineData("STOP_CAPT_SEQ1_LIGHT", "Captain stop rudder input light", "EFIS Captain")]
    [InlineData("STOP_FO_SEQ1_LIGHT", "First officer stop rudder input light", "EFIS First Officer")]
    public void The_stop_rudder_input_lights_are_read(string node, string name, string panel)
    {
        // Their own variable (INI_STOP_RUDDER_INPUT_BUTTON_LIGHT), not the test switch's row.
        Assert.Equal((name, panel), (Lamp(node).Name, Lamp(node).Panel));
        Assert.Equal("INI_STOP_RUDDER_INPUT_BUTTON_LIGHT", A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node).Primary.Name);
    }

    [Theory]
    // Design decision 2026-10-09: the gear lights are spoken. Stock gear index 0 is the nose, 1 left, 2 right;
    // iniBuilds' GEAR1 is the nose and GEAR2 the left, as its overhead lamps pair them with those indices.
    [InlineData("GEAR_2_UNLK_SEQ2_LIGHT", "Nose gear down light", "Gear", "GEAR POSITION:0")]
    [InlineData("GEAR_1_UNLK_SEQ2_LIGHT", "Left gear down light", "Gear", "GEAR POSITION:1")]
    [InlineData("GEAR_3_UNLK_SEQ2_LIGHT", "Right gear down light", "Gear", "GEAR POSITION:2")]
    [InlineData("INDICATOR_LOWER_UNLK2_LIGHT", "Nose gear unlocked light", "Gear", "INI_GEAR1_UNLK_LIGHT")]
    [InlineData("INDICATOR_LOWER_UNLK1_LIGHT", "Left gear unlocked light", "Gear", "INI_GEAR2_UNLK_LIGHT")]
    [InlineData("INDICATOR_LOWER_UNLK3_LIGHT", "Right gear unlocked light", "Gear", "INI_GEAR3_UNLK_LIGHT")]
    [InlineData("INDICATOR_LOWER_DOOR2_LIGHT", "Nose gear door open light", "Gear", "INI_landing_gear1_door_ratio")]
    [InlineData("INDICATOR_LOWER_DOOR1_LIGHT", "Left gear door open light", "Gear", "INI_landing_gear2_door_ratio")]
    [InlineData("INDICATOR_LOWER_DOOR3_LIGHT", "Right gear door open light", "Gear", "INI_landing_gear3_door_ratio")]
    [InlineData("INDICATOR_SPDBRK_LIGHT", "Speed brake light", "Flaps and Speed Brake", "INI_SPOILERS_HANDLE_POSITION")]
    [InlineData("INDICATOR_AUTOBRK_LIGHT", "Autobrake light", "Autobrake", "INI_AUTOBRK_LIGHT")]
    [InlineData("INDICATOR_BRKFAIL_LIGHT", "Brake fail light", "Autobrake", "INI_AUTOBRK_FAIL")]
    [InlineData("BRAKE_FAN_SEQ1_LIGHT", "Brakes hot light", "Autobrake", "INI_brakes_hot")]
    public void The_gear_and_brake_lights_are_on_the_ac_light_bus(string node, string name, string panel, string var)
    {
        Assert.Equal((name, panel), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Ac), (lamp.Primary.Name, lamp.Power));
    }

    [Fact]
    public void The_fire_handle_lights_are_the_lamps_the_cockpit_draws()
    {
        // MSFSBA read INI_fire_handle_*_light with no light power; the handle lamps draw INI_*_FIRE_TEST on the
        // DC light bus. Both light together in the fire test (sampled 2026-10-09).
        Assert.DoesNotContain(A300FaultLights.All, l => l.Var.StartsWith("INI_fire_handle_", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("INI_BAT1_light", "Battery 1 charge light")]
    [InlineData("INI_BAT3_light", "Battery 3 charge light")]
    public void The_battery_lights_are_charge_lights(string var, string name) =>
        // Design decision 2026-10-09: they follow the battery's charge current (measured in flight, 2026-10-06).
        Assert.Equal(name, A300FaultLights.All.Single(l => l.Var == var).Name);

    [Fact]
    public void External_power_available_reads_the_cockpits_own_rule()
    {
        var board = new A300LampBoard();
        var avail = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == "EXT_PWR_SEQ1_LIGHT");
        string externalPowerOn = A300PanelLamps.InputKey(avail.Rule.Inputs.Single(i => i.IsStock));
        // Its lamp has no light power term: the ground power unit lights it.
        Assert.Empty(board.Update(avail.Lamp.Key, 1));
        Assert.Empty(board.Update(externalPowerOn, 1));                      // on: AVAIL dark (baseline)
        Assert.Equal(new[] { "External power available light on" }, Said(board.Update(externalPowerOn, 0)));
        Assert.Equal(new[] { "External power available light off" }, Said(board.Update(avail.Lamp.Key, 0)));
    }

    [Fact]
    public void A_bus_off_light_needs_its_light_power_like_every_other_light()
    {
        var board = new A300LampBoard();
        string key = Lamp("AC_BUS_1_OFF_LIGHT").Key;
        board.Update(A300LampBoard.DcPowerKey, 0);
        board.Update(key, 0);
        Assert.Empty(board.Update(key, 1));                                   // dark bus: nothing to see
        board.Update(A300LampBoard.DcPowerKey, 1);
        Assert.True(A300LampBoard.IsLit(A300LampBoard.ById[key], k => k == key || k == A300LampBoard.DcPowerKey ? 1 : null));
        Assert.Equal(new[] { "AC bus 1 off light off" }, Said(board.Update(key, 0)));
    }

    [Fact]
    public void Each_lamp_streams_on_its_own_subscription_with_a_ctrl_m_row_and_a_status_box_line()
    {
        var def = new IniA300Definition();
        var vars = def.GetVariables();
        foreach (var lamp in A300PanelLamps.Resolved)
        {
            var v = vars[lamp.Lamp.Key];
            Assert.Equal(lamp.Primary.Name, v.Name);
            Assert.Equal(lamp.Lamp.Name, v.DisplayName);
            Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
            Assert.True(v.IsAnnounced);
            Assert.True(v.ExcludeFromBatch);                                  // [A300-9]: never the batch
            Assert.False(v.ExcludeFromMonitorManager);
            Assert.Contains(lamp.Lamp.Key, def.GetPanelDisplayVariables()[lamp.Lamp.Panel]);
            foreach (var input in lamp.Rule.Inputs.Where(i => i.Id != lamp.Primary.Id))
            {
                var shared = vars[A300PanelLamps.InputKey(input)];
                Assert.Equal((input.Name, true, true), (shared.Name, shared.ExcludeFromBatch, shared.ExcludeFromMonitorManager));
                Assert.Equal(input.IsStock ? SimVarType.SimVar : SimVarType.LVar, shared.Type);
            }
        }
        Assert.Equal("Bool", vars[A300PanelLamps.InputKey(new A300LampInput("EXTERNAL POWER ON:1", true, "Bool"))].Units);
    }

    [Fact]
    public void A_lamps_status_box_reads_what_the_cockpit_shows()
    {
        var cache = new Dictionary<string, double>();
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        string key = Lamp("AC_BUS_1_OFF_LIGHT").Key;
        cache[key] = 1;
        cache[A300LampBoard.DcPowerKey] = 0;
        Assert.True(def.TryDescribeControlState(key, out var dark));
        cache[A300LampBoard.DcPowerKey] = 1;
        Assert.True(def.TryDescribeControlState(key, out var lit));
        Assert.Equal(("Off", "On"), (dark, lit));
    }

    [Fact]
    public void A_lamps_status_list_line_reads_what_the_cockpit_shows()
    {
        // MainForm's status list composes a line through TryGetDisplayOverride (UpdateDisplayText), not
        // TryDescribeControlState: measured 2026-10-09, external power available read "On" with external
        // power on, the raw value of its first variable.
        var cache = new Dictionary<string, double>();
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        var avail = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == "EXT_PWR_SEQ1_LIGHT");
        cache[avail.Lamp.Key] = 1;
        cache[A300PanelLamps.InputKey(avail.Rule.Inputs.Single(i => i.IsStock))] = 1;   // external power on
        Assert.True(def.TryGetDisplayOverride(avail.Lamp.Key, 1, out var text));
        Assert.Equal("Off", text);

        string gen = A300FaultLights.All.Single(l => l.Var == "INI_elec_gen1_fault").Key;
        cache[gen] = 1;
        cache[A300LampBoard.DcPowerKey] = 0;
        Assert.True(def.TryGetDisplayOverride(gen, 1, out var dark));
        Assert.Equal("Off", dark);                                            // a fault on a dark bus is not lit
    }

    [Fact]
    public void A_lamp_change_is_spoken_by_the_definition_and_honours_its_ctrl_m_row()
    {
        var speech = new SpeechCapture();
        long now = 10_000;
        var muted = new HashSet<string>();
        var def = new IniA300Definition { Clock = () => now, IsMuted = k => muted.Contains(k) };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        string key = Lamp("DC_ESS_ONBAT_LIGHT").Key;
        void BatchEnd()
        {
            now += A300LampSpeech.GatherMs;
            def.OnContinuousBatchDelivered(1);
        }
        def.ProcessSimVarUpdate(A300LampBoard.DcPowerKey, 1, speech);
        def.ProcessSimVarUpdate(key, 0, speech);
        BatchEnd();
        def.ProcessSimVarUpdate(key, 1, speech);
        BatchEnd();
        Assert.Equal(new[] { "DC essential bus on battery light on" }, speech.All);
        muted.Add(key);
        def.ProcessSimVarUpdate(key, 0, speech);
        BatchEnd();
        Assert.Single(speech.All);
    }

    [Fact]
    public void A_pressed_buttons_own_light_is_left_to_its_label()
    {
        // The screen reader reads a focused button's new label ("Pressurization system 2: On"), so the
        // button's own light is not spoken again ([CORE-7]); the other light it puts out still is.
        var speech = new SpeechCapture();
        long now = 10_000;
        var def = new IniA300Definition
        {
            Clock = () => now, IsMuted = _ => false, CanLand = _ => true, Send = (_, _) => { },
            Cached = (_, _) => null, Delay = _ => Task.CompletedTask,
        };
        var sim = new SimConnectManager(IntPtr.Zero);
        def.Attach(sim);
        string sys1 = Lamp("PRESS_SYS_1_SEQ1_LIGHT").Key, sys2 = Lamp("PRESS_SYS_2_SEQ1_LIGHT").Key;
        void BatchEnd() { now += A300LampSpeech.GatherMs; def.OnContinuousBatchDelivered(1); }
        def.ProcessSimVarUpdate(A300LampBoard.AcPowerKey, 1, speech);
        def.ProcessSimVarUpdate(sys1, 1, speech);
        def.ProcessSimVarUpdate(sys2, 0, speech);
        BatchEnd();
        def.HandleUIVariableSet("A300_PRESS_SYS_2", 1, def.GetVariables()["A300_PRESS_SYS_2"], sim, speech);
        def.ProcessSimVarUpdate(sys2, 1, speech);
        def.ProcessSimVarUpdate(sys1, 0, speech);
        BatchEnd();
        now += A300LampSpeech.OffHoldMs;   // an off waits until the light has stayed dark
        BatchEnd();
        Assert.Equal(new[] { "Pressurization system 1 light off" }, speech.All);

        speech.All.Clear();
        now += 10_000;   // later, the aircraft changes it by itself: spoken
        def.ProcessSimVarUpdate(sys2, 0, speech);
        BatchEnd();
        now += A300LampSpeech.OffHoldMs;
        BatchEnd();
        Assert.Equal(new[] { "Pressurization system 2 light off" }, speech.All);
    }

    [Fact]
    public void A_lamp_relit_by_its_own_press_inside_an_off_hold_is_not_spoken_off()
    {
        // The ECAM page lights follow the page shown. Pressing HYD puts the Engine page light out (an off, held for
        // OffHoldMs), and pressing ENG 1.8 s later lights it again: that is its own button's light, so the change
        // is skipped, and the held off must go with it. It used to be spoken 3 s after the first press, while lit.
        var speech = new SpeechCapture();
        long now = 10_000;
        var def = new IniA300Definition
        {
            Clock = () => now, IsMuted = _ => false, CanLand = _ => true, Send = (_, _) => { },
            Cached = (_, _) => null, Delay = _ => Task.CompletedTask,
        };
        var sim = new SimConnectManager(IntPtr.Zero);
        def.Attach(sim);
        string eng = Lamp("ECAM_ENG_SEQ2_LIGHT").Key, hyd = Lamp("ECAM_HYD_SEQ2_LIGHT").Key;
        string engButton = A300PanelLamps.ByButton.Single(b => b.Value == eng).Key;
        string hydButton = A300PanelLamps.ByButton.Single(b => b.Value == hyd).Key;
        const string status = "A300_IN_L_ECAM_CURRENT_STATUS";   // 1 picked, 2 automatic: either lights the page
        Assert.Contains(status, A300LampBoard.ById[eng].Inputs);
        void BatchEnd(long ms) { now += ms; def.OnContinuousBatchDelivered(1); }
        void Page(double page)
        {
            def.ProcessSimVarUpdate(eng, page, speech);
            def.ProcessSimVarUpdate(hyd, page, speech);
        }
        void Press(string button) => def.HandleUIVariableSet(button, 1, def.GetVariables()[button], sim, speech);

        def.ProcessSimVarUpdate(A300LampBoard.AcPowerKey, 1, speech);
        def.ProcessSimVarUpdate(status, 1, speech);
        Page(1);                                  // Engine lit, Hydraulic dark: silent baselines
        BatchEnd(A300LampSpeech.GatherMs);
        Press(hydButton);
        Page(2);                                  // Engine goes out (held), Hydraulic lights (its own press)
        now += 1_800;
        Press(engButton);
        Page(1);                                  // Engine lights again (its own press); Hydraulic goes out (inside its own press window)
        for (int i = 0; i < 6; i++)
            BatchEnd(1_000);
        Assert.DoesNotContain("Engine page light off", speech.All);
        Assert.Empty(speech.All);
    }

    private static IEnumerable<string> Said(IReadOnlyList<A300BoardChange> changes) =>
        changes.Select(c => $"{c.Lamp.Name} {(c.On ? "on" : "off")}");
}
