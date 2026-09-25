using System.Diagnostics;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.Citation680;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Every interaction node the Sovereign+ model declares is EITHER a control in the definition OR
/// a named omission — never silently missing. <see cref="Surface"/> maps a model node to the
/// control key that drives it; <see cref="Omitted"/> names the nodes left out and why. The first
/// test pins that every mapped key exists; the second (only where the package is installed) runs
/// tools/c680-gen/enumerate-controls.js and pins that its node set is fully accounted for.
/// </summary>
public class C680InteractionSurfaceTests
{
    /// <summary>Model node (NODE_ID, ANIM_NAME, ID, or "(template)") → the definition key that drives it.</summary>
    public static readonly Dictionary<string, string> Surface = new(StringComparer.Ordinal)
    {
        // Electrical
        ["ELECTRICAL_Switch_Battery_Master_1"] = "C680_BATT_L", ["ELECTRICAL_Switch_Battery_Master_2"] = "C680_BATT_R",
        ["ELECTRICAL_Switch_Battery_STBY_1"] = "C680_STBY_PWR",
        ["ELECTRICAL_Switch_Connection_MAIN_1"] = "C680_AVN_L", ["ELECTRICAL_Switch_Connection_MAIN_2"] = "C680_AVN_R",
        ["ELECTRICAL_Switch_Connection_ELEC_1"] = "C680_ELEC_L", ["ELECTRICAL_Switch_Connection_ELEC_2"] = "C680_ELEC_R",
        ["ELECTRICAL_Switch_Alternator_4"] = "C680_TRU_L", ["ELECTRICAL_Switch_Alternator_5"] = "C680_TRU_R",
        ["1"] = "C680_GEN_L", ["2"] = "C680_GEN_R",
        ["ELECTRICAL_Switch_ExternalPower_1"] = "C680_EXT_PWR", ["(ASOBO_GT_Switch_3States)"] = "C680_APU_GEN",
        ["ELECTRICAL_Push_BusTie"] = "C680_BUS_TIE", ["ELECTRICAL_Push_Cabin"] = "C680_INTERIOR",
        // APU, start, anti-ice
        ["(ASOBO_ELECTRICAL_Switch_APU_Starter_Template)"] = "C680_APU_KNOB", ["PRESSURIZATION_Push_Bleed_APU"] = "C680_APU_BLEED",
        ["Bleed_Push_Max_Cool"] = "C680_MAX_COOL", ["Bleed_Push_Bag_Heat"] = "C680_BAG_HEAT",
        ["(SW_SOV_ENGINE_Push_Starter_Disengage_Template)"] = "C680_STARTER_DISENG",
        ["FADEC_RESET_L"] = "C680_FADEC_RESET_L", ["FADEC_RESET_R"] = "C680_FADEC_RESET_R",
        ["L_TR_EMER_STOW"] = "C680_TR_STOW_L", ["R_TR_EMER_STOW"] = "C680_TR_STOW_R",
        ["DEICE_Push_Pitot_L"] = "C680_PITOT_L", ["DEICE_Push_Pitot_R"] = "C680_PITOT_R",
        ["DEICE_Push_Engine_1"] = "C680_AI_ENG_L", ["DEICE_Push_Engine_2"] = "C680_AI_ENG_R",
        ["DEICE_Push_Wing_L"] = "C680_AI_WING_L", ["DEICE_Push_Wing_R"] = "C680_AI_WING_R",
        ["DEICE_Push_WING_XFLOW"] = "C680_WING_XFLOW", ["DEICE_Push_WS_FAN"] = "C680_WS_FAN",
        // Lights
        ["LIGHTING_Switch_Landing_1"] = "C680_LDG_L", ["LIGHTING_Switch_Landing_2"] = "C680_LDG_R", ["LIGHTING_Switch_Taxi"] = "C680_TAXI",
        ["LIGHTING_Switch_Recognition"] = "C680_RECOG", ["LIGHTING_Switch_Pulse"] = "C680_PULSE", ["LIGHTING_Switch_Beacon"] = "C680_BEACON",
        ["LIGHTING_Switch_Wing"] = "C680_WING_LT", ["LIGHTING_Switch_Logo"] = "C680_LOGO",
        ["LIGHTING_Knob_Panel"] = "C680_KNOB_PANEL", ["LIGHTING_Knob_Flood"] = "C680_KNOB_FLOOD", ["LIGHTING_Knob_Aux"] = "C680_KNOB_AUX",
        ["LIGHTING_Knob_Map_1"] = "C680_KNOB_MAP_L", ["LIGHTING_Knob_Map_2"] = "C680_KNOB_MAP_R",
        ["Cockpit_Light_L"] = "C680_KNOB_CKPT_L", ["Cockpit_Light_R"] = "C680_KNOB_CKPT_R",
        ["LIGHTING_Knob_PFD_1"] = "C680_KNOB_PFD_L", ["LIGHTING_Knob_PFD_2"] = "C680_KNOB_PFD_R", ["LIGHTING_Knob_MFD"] = "C680_KNOB_MFD",
        ["LIGHTING_Knob_PFD_GTC_1"] = "C680_KNOB_GTC_L", ["LIGHTING_Knob_MFD_GTCs"] = "C680_KNOB_GTC_MFD", ["LIGHTING_Knob_PFD_GTC_2"] = "C680_KNOB_GTC_R",
        ["LIGHTING_Push_Light_Safety"] = "C680_PAX_SAFETY", ["LIGHTING_Push_Light_SeatBelt"] = "C680_SEAT_BELTS",
        ["Lights_Vanity"] = "C680_LIGHT_VANITY", ["Baggage_Bay_Light_Switch"] = "C680_LIGHT_BAG",
        // Right tilt
        ["knob_pressSource"] = "C680_PRESS_SRC", ["knob_l_eng_bld_air"] = "C680_BLEED_L", ["knob_r_eng_bld_air"] = "C680_BLEED_R",
        ["PRESSURIZATION_Switch_CabinAlt"] = "C680_CABIN_ALT_SW", ["PRESSURIZATION_Switch_CabinAlt_Rate"] = "C680_PRESS_RATE",
        ["HYDRAULICS_Switch_1"] = "C680_HYD_SW_1", ["HYDRAULICS_Switch_2"] = "C680_HYD_SW_2", ["HYDRAULICS_Switch_AUX"] = "C680_HYD_AUX",
        ["FUEL_Switch_Pump_1"] = "C680_BOOST_L", ["FUEL_Switch_Pump_2"] = "C680_BOOST_R", ["FUEL_Switch_Crossfeed_1"] = "C680_CROSSFEED",
        ["Oxygen_Mask_Selector"] = "C680_PASS_OXY", ["Mask_Oxygen_L"] = "C680_MASK_L", ["Mask_Oxygen_R"] = "C680_MASK_R",
        ["Oxygen_L_Test"] = "C680_OXY_TEST_L", ["Oxygen_R_Test"] = "C680_OXY_TEST_R",
        ["SW_SOV_CVR_headset_button"] = "C680_CVR_HEADSET", ["SW_SOV_CVR_test_button"] = "C680_CVR_TEST", ["SW_SOV_CVR_erase_button"] = "C680_CVR_ERASE",
        ["SAFETY_Switch_ELT"] = "C680_ELT",
        // Glareshield
        ["SAFETY_Push_Warning_1"] = "C680_MASTER_WARN_ACK", ["SAFETY_Push_Warning_2"] = "C680_MASTER_WARN_ACK",
        ["SAFETY_Push_Caution_1"] = "C680_MASTER_CAUT_ACK", ["SAFETY_Push_Caution_2"] = "C680_MASTER_CAUT_ACK",
        ["SAFETY_Push_Extinguisher_1_Cover"] = "C680_FIRE_L_COVER", ["SAFETY_Push_Extinguisher_1"] = "C680_FIRE_L", ["SAFETY_Push_Extinguisher_Arm_1"] = "C680_BOTTLE_L",
        ["SAFETY_Push_Extinguisher_2_Cover"] = "C680_FIRE_R_COVER", ["SAFETY_Push_Extinguisher_2"] = "C680_FIRE_R", ["SAFETY_Push_Extinguisher_Arm_2"] = "C680_BOTTLE_R",
        ["SAFETY_Push_Extinguisher_APU_Cover"] = "C680_FIRE_APU_COVER", ["SAFETY_Push_Extinguisher_APU"] = "C680_FIRE_APU",
        ["SAFETY_Push_Baggage_Fire_Cover"] = "C680_BAG_FIRE_COVER", ["SAFETY_Push_Baggage_Fire"] = "C680_BAG_FIRE",
        ["SAFETY_Push_Sec_Bag_Bottle_Cover"] = "C680_BAG_BOTTLE_COVER", ["SAFETY_Push_Sec_Bag_Bottle"] = "C680_BAG_BOTTLE",
        ["(ASOBO_AUTOPILOT_Composite_Template)"] = "C680_AP_MASTER", ["AUTOPILOT_Push_YawDamper1"] = "C680_AP_YD",
        ["(ASOBO_AUTOPILOT_Push_Altitude_Template)"] = "C680_AP_ALT", ["(ASOBO_AUTOPILOT_Push_Approach_Template)"] = "C680_AP_APR",
        ["(ASOBO_AUTOPILOT_Push_BackCourse_Template)"] = "C680_AP_BC", ["(ASOBO_AUTOPILOT_Push_VerticalSpeed_Template)"] = "C680_AP_VS",
        ["(ASOBO_AUTOPILOT_Push_AutoThrottle_Template)"] = "C680_AT_ARM", ["ENGINE_Push_AT_1"] = "C680_AT_ARM", ["ENGINE_Push_AT_2"] = "C680_AT_ARM",
        ["ENGINE_Push_AT_Disc_1"] = "C680_AT_DISC", ["ENGINE_Push_AT_Disc_2"] = "C680_AT_DISC",
        ["(WT_G3000_Knob_Altitude_Template)"] = "C680_AP_ALT_SET", ["(WT_G3000_Knob_Heading_Template)"] = "C680_AP_HDG_SET",
        ["(WT_G3000_Knob_Speed_Template)"] = "C680_AP_SPD_SET", ["AUTOPILOT_Knob_VerticalSpeed_Visual"] = "C680_AP_VS_SET",
        ["AUTOPILOT_Push_CWS_1"] = "C680_CWS", ["AUTOPILOT_Push_CWS_2"] = "C680_CWS",
        ["AUTOPILOT_Push_Ident_1"] = "C680_IDENT", ["AUTOPILOT_Push_Ident_2"] = "C680_IDENT",
        ["SAI_AUTOPILOT_Knob_Baro"] = "C680_BARO_SET", ["SAI_Btn_Main_Panel_B_26"] = "C680_SAI_BL_MODE",
        // Pedestal
        ["(ASOBO_HANDLING_Lever_Flaps_Template)"] = "C680_FLAPS", ["HANDLING_Push_FlapsReset"] = "C680_FLAPS_RESET",
        ["HANDLING_Switch_Trim"] = "C680_TRIM_NU", ["(ASOBO_HANDLING_Switch_ElevatorTrim_Template)"] = "C680_TRIM_NU",
        ["HANDLING_YOKE_Switch_ElevatorTrim_1"] = "C680_TRIM_NU", ["HANDLING_YOKE_Switch_ElevatorTrim_2"] = "C680_TRIM_NU",
        ["HANDLING_Switch_AileronTrim_LR"] = "C680_TRIM_AIL_L", ["(ASOBO_HANDLING_Switch_RudderTrim_Template)"] = "C680_TRIM_RUD_L",
        ["HANDLING_Push_ElevatorTrim_Secondary_Cover"] = "C680_SEC_TRIM_COVER", ["HANDLING_Push_ElevatorTrim_Secondary"] = "C680_SEC_TRIM",
        ["HANDLING_Push_RudderBias_Cover"] = "C680_RUDDER_BIAS_COVER", ["HANDLING_Push_RudderBias"] = "C680_RUDDER_BIAS",
        ["LANDING_GEAR_Lever_Gear"] = "C680_GEAR", ["LANDING_GEAR_Switch_ParkingBrake"] = "C680_PARK_BRAKE", ["SW_SOV_EMER_BRAKE"] = "C680_EMER_BRAKE",
        ["Handle_Gravity_Gear_Main"] = "C680_GRAV_GEAR_MAIN", ["SW_SOV_HANDLING_Gravity_Gear_Nose"] = "C680_GRAV_GEAR_NOSE", ["SW_SOV_HANDLING_GEAR_BLOWDOWN"] = "C680_GEAR_BLOWDOWN",
        ["SW_CSF_Disconnect"] = "C680_CSF_DISC", ["SW_CSF_Lock"] = "C680_CONTROL_LOCK",
        ["INSTRUMENT_Push_Microphone_1"] = "C680_MIC_L", ["INSTRUMENT_Push_Microphone_2"] = "C680_MIC_R",
        // Cabin and ground
        ["door_clickspot_inside"] = "C680_DOOR_MAIN", ["door_clickspot_outside"] = "C680_DOOR_MAIN", ["door_Handle_2"] = "C680_DOOR_MAIN_HANDLE",
        ["door_inside_handle"] = "C680_DOOR_MAIN_HANDLE", ["door_inside_handle_inv"] = "C680_DOOR_MAIN_HANDLE", ["door_outside_handle"] = "C680_DOOR_MAIN_HANDLE",
        ["door_outside_pusher"] = "C680_PANEL_PUSHER", ["Baggage_Door_Clickspot"] = "C680_DOOR_BAG", ["Baggage_Handle"] = "C680_DOOR_BAG_HANDLE",
        ["Avionics_door_L"] = "C680_PANEL_AVN_L", ["Avionics_door_R"] = "C680_PANEL_AVN_R",
        ["BAT_DISC_L_2"] = "C680_BAT_DISC_L", ["BAT_DISC_R_2"] = "C680_BAT_DISC_R",
        ["Bat_Disc_L_door_clickspot"] = "C680_PANEL_BAT_L", ["Bat_Disc_R_door_clickspot"] = "C680_PANEL_BAT_R",
        ["External_Power_Door_clickspot"] = "C680_PANEL_GPU", ["Hydraulic_Door_clickspot"] = "C680_PANEL_HYD", ["Oxygen_Door_clickspot"] = "C680_PANEL_OXY",
        ["Loo_drain_door_clickspot"] = "C680_PANEL_LOO", ["Singlepoint_refuel_clickspot"] = "C680_PANEL_FUEL", ["Singlepoint_refuel_lid"] = "C680_FUEL_LID",
        ["Fuel_Switch_1"] = "C680_FUEL_SW_1", ["Fuel_Switch_2"] = "C680_FUEL_SW_2",
        ["Water_tank_sink_lid"] = "C680_WATER_LID", ["Water_tank_sink_water"] = "C680_WATER_FILL",
        ["Fuel_Connector"] = "C680_FUEL_TRUCK", ["Oxy_fill_L"] = "C680_OXY_CART", ["Oxy_fill_R"] = "C680_OXY_CART",
        ["PLUG_1"] = "C680_GPU_CART", ["PLUG_1_HANDLE"] = "C680_GPU_CART", ["PLUG_2"] = "C680_HYD_CART", ["PLUG_2_HANDLE"] = "C680_HYD_CART",
        ["ELECTRICAL_Switch_Cabin_Internet"] = "C680_OPT_WIFI",
    };

    /// <summary>Nodes deliberately left out, by pattern, with the reason a pilot would be given.</summary>
    public static readonly (Regex pattern, string reason)[] Omitted =
    {
        (new Regex(@"^(Armrest_|SeatSwivel|table_|galley_|Curtain_|Sunvisor_|Window_|Cockpit_Seat_|storage_door|loo_lid|toilet_door|Flush_loo|swith_sink|Lav_Light|business|Bruce_|Cow_|Model_IDK|Valcro|Orange_Thing)"), "cabin animation toy: a plain L:var toggle no pilot needs"),
        (new Regex(@"^(Ipad|Text_Station_|View_)"), "camera, view or tablet placement"),
        (new Regex(@"^HANDLING_(Yoke_Hider|Throttle_Hider)"), "model visibility toggles; the Simulation options cover them"),
        (new Regex(@"^(HANDLING_Wheel_Steering|\(ASOBO_HANDLING_RudderPedals_Template\))$"), "a control axis, not a switch"),
        (new Regex(@"^Cover_clickspot_"), "the ground-cover clickspots; the Ground Equipment panel places every cover"),
        (new Regex(@"^Fuel_wing_"), "over-wing refuel clickspots; the Payload panel sets the fuel"),
        (new Regex(@"^RADIO_PANEL_"), "the headset ANC jacks; the EFB Settings page owns ANC"),
        (new Regex(@"^NAVCOM_Push_COM_"), "a radio tune push; the PFD touchscreen tunes the radios"),
        (new Regex(@"^AUTOPILOT_Push_Transfer1$"), "the XFR button (which side flies the FD); read on the PFD"),
        (new Regex(@"^\(SW_SOV_SplitScreen_Course_Knobs_Template\)$"), "course knobs; the touchscreen sets the course"),
        (new Regex(@"^\(ASOBO_INSTRUMENT_Indicator_AOA_Template\)$"), "an indicator, not a control"),
        (new Regex(@"^HYDRAULICS_Switch_[12]_Cover$"), "the guard over a switch that has its own row"),
        (new Regex(@"^CB_J$"), "a breaker the model declares without an electrical line"),
    };

    [Fact]
    public void EveryMappedControlIsARegisteredVariable()
    {
        var vars = new SkywardC680Definition().GetVariables();
        var missing = Surface.Where(kv => !vars.ContainsKey(kv.Value)).Select(kv => kv.Key + " -> " + kv.Value).ToList();
        Assert.True(missing.Count == 0, "Mapped to a key the definition does not register: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryModelNodeIsAControlOrANamedOmission()
    {
        string pkg = Environment.GetEnvironmentVariable("C680_PKG")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community", "skyward-cessna-citation-c680");
        string generator = FindRepoFile(Path.Combine("tools", "c680-gen", "enumerate-controls.js"));
        if (!Directory.Exists(pkg) || generator.Length == 0) return;   // only where the aircraft is installed

        var psi = new ProcessStartInfo("node", $"\"{generator}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.Environment["C680_PKG"] = pkg;
        using var p = Process.Start(psi);
        if (p == null) return;
        string output = p.StandardOutput.ReadToEnd(); p.WaitForExit();
        if (p.ExitCode != 0) return;   // no node on this machine: not this test's verdict

        var breakers = C680BreakerTable.Rows.Select(r => r.LVar).ToHashSet(StringComparer.Ordinal);
        var unaccounted = new List<string>();
        foreach (var raw in output.Split('\n'))
        {
            string node = raw.Trim(); if (node.Length == 0) continue;
            if (Surface.ContainsKey(node) || breakers.Contains(node)) continue;
            if (Omitted.Any(o => o.pattern.IsMatch(node))) continue;
            unaccounted.Add(node);
        }
        Assert.True(unaccounted.Count == 0, "Model nodes neither driven nor named as omitted: " + string.Join(", ", unaccounted));
    }

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return "";
    }
}
