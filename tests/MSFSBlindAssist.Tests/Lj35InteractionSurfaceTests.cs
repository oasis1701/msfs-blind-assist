using MSFSBlindAssist.Aircraft.Learjet35;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Every interactive component the Flysimware Learjet 35A model declares (the NODE_ID of each
/// UseTemplate of a vendor switch, knob, lever or button template — 225 in v1.8.1, listed by
/// tools/lj35-gen/enumerate-controls.ps1), paired with the MSFSBA control that covers it or
/// the reason it is left out. A vendor update that adds a node fails the live comparison on a
/// machine with the package installed; a control key that no longer exists fails everywhere.
/// </summary>
public class Lj35InteractionSurfaceTests
{
    /// <summary>NODE_ID → MSFSBA control key, or "- reason" for a deliberate omission.</summary>
    public static readonly Dictionary<string, string> Surface = new(StringComparer.Ordinal)
    {
        ["ADF_Active"] = "LJ35_ADF1_SET", // the "active" button swaps; MSFSBA sets the active frequency directly
        ["ADF_ACTIVE2"] = "LJ35_ADF2_SET",
        ["ADF1_FRACT_TUNER"] = "LJ35_ADF1_SET",
        ["ADF1_TEST_1"] = "LJ35_ADF1_TEST",
        ["ADF1_WHOLE_TUNER"] = "LJ35_ADF1_SET",
        ["ADF2_FRACT_TUNER"] = "LJ35_ADF2_SET",
        ["ADF2_TEST_1"] = "LJ35_ADF2_TEST",
        ["ADF2_WHOLE_TUNER"] = "LJ35_ADF2_SET",
        ["ALERTER_DIAL_DIGITAL_CUSTOM_L"] = "LJ35_AP_PRESELECT_SET",
        ["ALERTER_DIAL_DIGITAL_CUSTOM_R"] = "LJ35_AP_PRESELECT_SET",
        ["ATTITUDE_BARS_KNOB"] = "LJ35_STBY_CAGE",
        ["BENDIX_RADAR_MODE"] = "LJ35_RADAR_MODE",
        ["BENDIX_RADAR_RANGE"] = "LJ35_RADAR_RANGE",
        ["BREAKER_AC_TIE"] = "LJ35_TIE_AC",
        ["BREAKER_ESS_TIE"] = "LJ35_TIE_ESS",
        ["BREAKER_MAIN_TIE"] = "LJ35_TIE_MAIN",
        ["CABIN_AIR_MODE"] = "LJ35_CABIN_AIR",
        ["COLLINS_ADF_POWER1"] = "LJ35_ADF1_POWER",
        ["COLLINS_ADF_POWER2"] = "LJ35_ADF2_POWER",
        ["COLLINS_COM_POWER2"] = "LJ35_COM2_POWER",
        ["COLLINS_NAV_POWER2"] = "LJ35_NAV2_POWER",
        ["Com2_Active"] = "LJ35_COM2_SWAP",
        ["COM2_FRACT_TUNER"] = "LJ35_COM2_STBY_SET",
        ["COM2_TEST_1"] = "LJ35_COM2_TEST",
        ["COM2_WHOLE_TUNER"] = "LJ35_COM2_STBY_SET",
        ["DAVTRON_SWITCH_DIM"] = "LJ35_CLOCK_DIM",
        ["DAVTRON_SWITCH_FT"] = "LJ35_CLOCK_FT",
        ["DAVTRON_SWITCH_SET"] = "LJ35_CLOCK_SET",
        ["DAVTRON_SWITCH_STOP"] = "LJ35_CLOCK_STOP",
        ["DME_MODE1"] = "LJ35_DME1_SOURCE",
        ["DME_MODE1A"] = "LJ35_DME1_DISPLAY",
        ["DME_MODE2"] = "LJ35_DME2_SOURCE",
        ["DME_MODE2A"] = "LJ35_DME2_DISPLAY",
        ["DME_TEST_1"] = "LJ35_DME_TEST_1",
        ["DME_TEST1_1"] = "LJ35_DME_TEST_2",
        ["ELT_TRANSMITTER"] = "LJ35_ELT",
        ["GEAR_BRT_KNOB"] = "LJ35_GEAR_BRT",
        ["H1060_Headset"] = "LJ35_HEADSET",
        ["HEADSET_FILTER"] = "LJ35_HEADSET",
        ["HSI_MODE_SELECTOR_1"] = "LJ35_HSI1_MODE",
        ["HSI_MODE_SELECTOR_2"] = "LJ35_HSI2_MODE",
        ["INSTRUMENT_Knob_Altimeter_1"] = "LJ35_BARO_1_SET",
        ["INSTRUMENT_Knob_Altimeter_2"] = "LJ35_BARO_2_SET",
        ["KNOB_DECISION_HEIGHT"] = "LJ35_DH_SET",
        ["L35A_SWITCH_ANUNNCIATOR_TEST_1"] = "LJ35_ANN_TEST",
        ["L35A_SWITCH_ANUNNCIATOR_TEST_2"] = "- the copilot's annunciator test button; same lamps, one control",
        ["LEAR_ADC1_ADC2"] = "LJ35_ADC_SELECT",
        ["LEAR_AIRSPEED_KNOB_L"] = "LJ35_ASI_BUG_L_SET",
        ["LEAR_AIRSPEED_KNOB_R"] = "LJ35_ASI_BUG_R_SET",
        ["LEAR_AIRSTART_L_1"] = "LJ35_AIRSTART_L",
        ["LEAR_AIRSTART_R_1"] = "LJ35_AIRSTART_R",
        ["LEAR_ALERTER_DAY"] = "LJ35_ALERTER_DAY_NIGHT",
        ["LEAR_ALERTER_TEST"] = "LJ35_ALERTER_TEST",
        ["LEAR_ALT_MARKER_HAND_L1"] = "- the two bug hands are set by the airspeed knob; one control",
        ["LEAR_ALT_MARKER_HAND_L2"] = "- the two bug hands are set by the airspeed knob; one control",
        ["LEAR_ALT_MARKER_HAND_R1"] = "- the two bug hands are set by the airspeed knob; one control",
        ["LEAR_ALT_MARKER_HAND_R2"] = "- the two bug hands are set by the airspeed knob; one control",
        ["LEAR_AP_ALTHLD_1"] = "LJ35_AP_ALT_HLD",
        ["LEAR_AP_ALTSEL_1"] = "LJ35_AP_ALT_SEL",
        ["LEAR_AP_BC_1"] = "LJ35_AP_BC",
        ["LEAR_AP_BNC_1"] = "LJ35_AP_HALF_BANK",
        ["LEAR_AP_ENG_1"] = "LJ35_AP_ENG",
        ["LEAR_AP_GS_1"] = "LJ35_AP_APR",
        ["LEAR_AP_HDG_1"] = "LJ35_AP_HDG",
        ["LEAR_AP_LVL_1"] = "LJ35_AP_LVL",
        ["LEAR_AP_NAV_1"] = "LJ35_AP_NAV",
        ["LEAR_AP_SFT_1"] = "LJ35_AP_SFT",
        ["LEAR_AP_SPD_1"] = "LJ35_AP_SPD",
        ["LEAR_AP_TST_1"] = "LJ35_AP_TST",
        ["LEAR_AP_VS_1"] = "LJ35_AP_VS",
        ["LEAR_BATT1"] = "LJ35_BATT1",
        ["LEAR_BATT2"] = "LJ35_BATT2",
        ["LEAR_BOTTLE1_ENG1"] = "LJ35_BOTTLE1_ENG1",
        ["LEAR_BOTTLE1_ENG2"] = "LJ35_BOTTLE1_ENG2",
        ["LEAR_BOTTLE2_ENG1"] = "LJ35_BOTTLE2_ENG1",
        ["LEAR_BOTTLE2_ENG2"] = "LJ35_BOTTLE2_ENG2",
        ["LEAR_CABIN_AUTO_SW"] = "LJ35_CABIN_AUTO",
        ["LEAR_CABIN_CLIMB_RATE"] = "LJ35_CABIN_RATE_SET",
        ["LEAR_CABIN_PRESS_ROCKER"] = "LJ35_CABIN_ROCKER",
        ["LEAR_CABIN_PRESSURE_KNOB"] = "LJ35_CABIN_ALT_SET",
        ["LEAR_CURTAIN1"] = "LJ35_SHADES",
        ["LEAR_CURTAIN2"] = "LJ35_SHADES",
        ["LEAR_CURTAIN3"] = "LJ35_SHADES",
        ["LEAR_CURTAIN4"] = "LJ35_SHADES",
        ["LEAR_CURTAIN5"] = "LJ35_SHADES",
        ["LEAR_CURTAIN6"] = "LJ35_SHADES",
        ["LEAR_CURTAIN7"] = "LJ35_SHADES",
        ["LEAR_CURTAIN8"] = "LJ35_SHADES",
        ["LEAR_CURTAIN9"] = "LJ35_SHADES",
        ["LEAR_CURTAIN10"] = "LJ35_SHADES",
        ["LEAR_CURTAIN11"] = "LJ35_SHADES",
        ["LEAR_DOOR_DIVIDER_BUTTON"] = "LJ35_COCKPIT_DIVIDER",
        ["LEAR_DOOR_DIVIDER_HANDLE"] = "LJ35_COCKPIT_DIVIDER",
        ["LEAR_DOORHANDLE_MOTOR"] = "LJ35_DOOR_MOTOR",
        ["LEAR_EMER_BRAKE"] = "LJ35_EMER_BRAKE_SET",
        ["LEAR_EMER_GEAR"] = "LJ35_EMER_GEAR",
        ["LEAR_FIRE_HANDLE1"] = "LJ35_FIRE_HANDLE_L",
        ["LEAR_FIRE_HANDLE2"] = "LJ35_FIRE_HANDLE_R",
        ["LEAR_FUEL_JTSN_1"] = "LJ35_JETTISON",
        ["LEAR_FUEL_MAINPUMP1_1"] = "LJ35_JET_PUMP_L",
        ["LEAR_FUEL_MAINPUMP2_1"] = "LJ35_JET_PUMP_R",
        ["LEAR_FUEL_RESET"] = "LJ35_FUEL_RESET",
        ["LEAR_FUEL_SECPUMP1_1"] = "LJ35_STBY_PUMP_L",
        ["LEAR_FUEL_SECPUMP2_1"] = "LJ35_STBY_PUMP_R",
        ["LEAR_FUEL_SEL"] = "LJ35_FUEL_SEL",
        ["LEAR_FUEL_XFEED_1"] = "LJ35_XFLOW",
        ["LEAR_FUEL_XFER"] = "LJ35_XFER_FILL",
        ["LEAR_GO_AROUND"] = "LJ35_GO_AROUND",
        ["LEAR_INV_PRI_1"] = "LJ35_INV_PRI",
        ["LEAR_INV_SEC_1"] = "LJ35_INV_SEC",
        ["LEAR_LTS_EL_L"] = "LJ35_LT_EL_L",
        ["LEAR_LTS_EL_R"] = "LJ35_LT_EL_R",
        ["LEAR_LTS_FLOOD_L"] = "LJ35_LT_FLOOD",
        ["LEAR_LTS_HSI"] = "LJ35_LT_HSI",
        ["LEAR_LTS_MAP_L"] = "LJ35_LT_MAP_L",
        ["LEAR_LTS_MAP_R"] = "LJ35_LT_MAP_R",
        ["LEAR_LTS_PEDESTAL"] = "LJ35_LT_PEDESTAL",
        ["LEAR_LTS_PNL_L"] = "LJ35_LT_PNL_L",
        ["LEAR_LTS_PNL_R"] = "LJ35_LT_PNL_R",
        ["LEAR_N1_REMINDER_1"] = "LJ35_N1_REM_SET",
        ["LEAR_N1_REMINDER_10"] = "LJ35_N1_REM_SET",
        ["LEAR_N1_REMINDER_100"] = "LJ35_N1_REM_SET",
        ["LEAR_NAVGPS_COPILOT"] = "LJ35_NAVGPS_COPILOT",
        ["LEAR_NAVGPS_PILOT"] = "LJ35_NAVGPS_PILOT",
        ["LEAR_PITOT_L"] = "LJ35_PITOT_L",
        ["LEAR_PITOT_R"] = "LJ35_PITOT_R",
        ["LEAR_SHUTOFF1"] = "LJ35_CUTOFF_L",
        ["LEAR_SHUTOFF2"] = "LJ35_CUTOFF_R",
        ["LEAR_SPOILER_SW_1"] = "LJ35_SPOILERS",
        ["LEAR_SPOILERON_RESET"] = "LJ35_SPOILERON_RESET",
        ["LEAR_STALL_L_1"] = "LJ35_STALL_L",
        ["LEAR_STALL_R_1"] = "LJ35_STALL_R",
        ["LEAR_STARTER_L"] = "LJ35_STARTER_L",
        ["LEAR_STARTER_R"] = "LJ35_STARTER_R",
        ["LEAR_STEER_LOCK_1"] = "LJ35_STEER_LOCK",
        ["LEAR_STEER_ON_COPILOT_1"] = "- the copilot's MSW; same function as the pilot's, one control",
        ["LEAR_STEER_ON_PILOT_1"] = "LJ35_YOKE_MSW",
        ["LEAR_SW_AC_BUS"] = "LJ35_AC_BUS",
        ["LEAR_SW_ANTISKID_1"] = "LJ35_ANTISKID",
        ["LEAR_SW_AUDIO_ADF1"] = "LJ35_AUD_ADF1",
        ["LEAR_SW_AUDIO_ADF2"] = "LJ35_AUD_ADF2",
        ["LEAR_SW_AUDIO_DME1"] = "LJ35_AUD_DME",
        ["LEAR_SW_AUDIO_MKR1"] = "LJ35_AUD_MKR",
        ["LEAR_SW_AUDIO_MSTR_VOL1"] = "LJ35_AUD_MASTER_VOL",
        ["LEAR_SW_AUDIO_NAV1"] = "LJ35_AUD_NAV1",
        ["LEAR_SW_AUDIO_NAV2"] = "LJ35_AUD_NAV2",
        ["LEAR_SW_AUDIO_PASS_SPKR1"] = "LJ35_AUD_PASS_SPKR",
        ["LEAR_SW_AUDIO_PHONE1"] = "LJ35_AUD_PHONE",
        ["LEAR_SW_AUDIO_SPKR_VOL1"] = "LJ35_AUD_PASS_VOL",
        ["LEAR_SW_AUDIO_TRANS1"] = "LJ35_AUD_TRANSMIT",
        ["LEAR_SW_AUDIO_VHF1"] = "LJ35_AUD_COM1",
        ["LEAR_SW_AUDIO_VHF2"] = "LJ35_AUD_COM2",
        ["LEAR_SW_AUTOPILOT"] = "LJ35_AP_MASTER",
        ["LEAR_SW_BLEED_L"] = "LJ35_BLEED_L",
        ["LEAR_SW_BLEED_R"] = "LJ35_BLEED_R",
        ["LEAR_SW_COMPLEFT_1"] = "LJ35_FUEL_COMP_L",
        ["LEAR_SW_COMPRIGHT_1"] = "LJ35_FUEL_COMP_R",
        ["LEAR_SW_EMER_BATT"] = "LJ35_EMER_BATT",
        ["LEAR_SW_GEAR_TEST"] = "LJ35_GEAR_HORN",
        ["LEAR_SW_HORN_SILENCE_1"] = "LJ35_HORN_SILENCE",
        ["LEAR_SW_HYD_1"] = "LJ35_HYD_PUMP",
        ["LEAR_SW_LTS_BAGGAGE"] = "LJ35_LT_BAGGAGE",
        ["LEAR_SW_LTS_CABIN"] = "LJ35_LT_CABIN",
        ["LEAR_SW_LTS_DIM"] = "LJ35_LT_CABIN_DIM",
        ["LEAR_SW_LTS_PASS1"] = "LJ35_LT_PASS1",
        ["LEAR_SW_LTS_PASS2"] = "LJ35_LT_PASS2",
        ["LEAR_SW_LTS_PASS3"] = "LJ35_LT_PASS3",
        ["LEAR_SW_LTS_PASS4"] = "LJ35_LT_PASS4",
        ["LEAR_SW_LTS_PASS5"] = "LJ35_LT_PASS5",
        ["LEAR_SW_LTS_PASS6"] = "LJ35_LT_PASS6",
        ["LEAR_SW_LTS_PASS7"] = "LJ35_LT_PASS7",
        ["LEAR_SW_LTS_PASS8"] = "LJ35_LT_PASS8",
        ["LEAR_SW_LTS_PASS9"] = "LJ35_LT_PASS9",
        ["LEAR_SW_LTS_PASS10"] = "LJ35_LT_PASS10",
        ["LEAR_SW_LTS_PASS11"] = "LJ35_LT_PASS11",
        ["LEAR_SW_LTS_PASS12"] = "LJ35_LT_PASS12",
        ["LEAR_SW_LTS_STEP"] = "LJ35_LT_STEP",
        ["LEAR_SW_MARKER_HI"] = "LJ35_MARKER_SENS",
        ["LEAR_SW_MARKER_TEST_1"] = "LJ35_MARKER_TEST",
        ["LEAR_SW_MASTER_RESET1_1"] = "LJ35_MASTER_RESET_PILOT",
        ["LEAR_SW_MASTER_RESET2_1"] = "LJ35_MASTER_RESET_COPILOT",
        ["LEAR_SW_RADIO_ALT_1"] = "LJ35_RADIO_ALT_PWR",
        ["LEAR_SW_REV_L"] = "LJ35_REV_L",
        ["LEAR_SW_REV_R"] = "LJ35_REV_R",
        ["LEAR_SW_SLAVE_FREE1"] = "LJ35_SLAVE_PILOT",
        ["LEAR_SW_SLAVE_FREE2"] = "LJ35_SLAVE_COPILOT",
        ["LEAR_SW_SLAVE_LR1"] = "LJ35_SLAVE_DIR_PILOT",
        ["LEAR_SW_SLAVE_LR2"] = "LJ35_SLAVE_DIR_COPILOT",
        ["LEAR_SW_SMOKING"] = "LJ35_SMOKING_BELTS",
        ["LEAR_SW_SPR"] = "LJ35_SPR",
        ["LEAR_SW_STATIC_SOURCE"] = "LJ35_STATIC_SOURCE",
        ["LEAR_SW_TURB_1"] = "LJ35_ENG_SYNC_TURB",
        ["LEAR_TABLE_FOLD_1"] = "LJ35_TABLE_1",
        ["LEAR_TABLE_FOLD_2"] = "LJ35_TABLE_2",
        ["LEAR_TABLE_FOLD_3"] = "LJ35_TABLE_3",
        ["LEAR_TAXI_LAND_L"] = "LJ35_TAXI_LAND_L",
        ["LEAR_TAXI_LAND_R"] = "LJ35_TAXI_LAND_R",
        ["LEAR_TEMP_AUTO_1"] = "LJ35_TEMP_AUTO",
        ["LEAR_TEMP_CONTROL"] = "LJ35_TEMP_KNOB",
        ["LEAR_TEMP_FAN"] = "LJ35_COOL_FAN",
        ["LEAR_TEST_BUTTON"] = "LJ35_TEST_BUTTON",
        ["LEAR_TEST_KNOB"] = "LJ35_TEST_KNOB",
        ["LEAR_TRIM_PRI_SEC"] = "LJ35_TRIM_PRI_SEC",
        ["LEAR_WSHLD_HEAT"] = "LJ35_WSHLD_HEAT",
        ["LEAR_WSHLD_RADOME"] = "LJ35_WSHLD_RADOME",
        ["LEAR_YAW_PRI_ENG"] = "LJ35_YD_PRI_ENG",
        ["LEAR_YAW_PRI_PWR"] = "LJ35_YD_PRI_PWR",
        ["LEAR_YAW_SEC_ENG"] = "LJ35_YD_SEC_ENG",
        ["LEAR_YAW_SEC_PWR"] = "LJ35_YD_SEC_PWR",
        ["LEAR_YAW_TEST"] = "LJ35_YD_TEST",
        ["LEVER_PARKING_BRAKE_1"] = "LJ35_PARKING_BRAKE",
        ["Manuv_RP_Switch_CoPilot_1"] = "- the copilot's manoeuvre switch; same function as the pilot's, one control",
        ["Manuv_RP_Switch_Pilot_1"] = "LJ35_YOKE_MANUV",
        ["Nav2_Active"] = "LJ35_NAV2_SWAP",
        ["NAV2_FRACT_TUNER"] = "LJ35_NAV2_STBY_SET",
        ["NAV2_TEST_1"] = "LJ35_NAV2_TEST",
        ["NAV2_WHOLE_TUNER"] = "LJ35_NAV2_STBY_SET",
        ["Pitch_Sync_Switch_CoPilot_1"] = "- the copilot's pitch sync; same function as the pilot's, one control",
        ["Pitch_Sync_Switch_Pilot_1"] = "LJ35_YOKE_PITCH_SYNC",
        ["RADAR_TEST_SW_1"] = "LJ35_RADAR_TEST",
        ["RMI_BUTTON1_1"] = "LJ35_RMI_PILOT_1",
        ["RMI_BUTTON2_1"] = "LJ35_RMI_PILOT_2",
        ["RMI_BUTTON3_1"] = "LJ35_RMI_COPILOT_1",
        ["RMI_BUTTON4_1"] = "LJ35_RMI_COPILOT_2",
        ["WING_INSPECTION_SWITCH"] = "LJ35_WING_INSPECTION",
    };

    [Fact]
    public void TheSurfaceHasEveryNodeOfVersion181() => Assert.Equal(225, Surface.Count);

    [Fact]
    public void EveryCoveringKeyIsARegisteredControl()
    {
        var def = new FlysimwareLearjet35ADefinition();
        var vars = def.GetVariables();
        var inPanels = new HashSet<string>(def.GetPanelControls().SelectMany(p => p.Value), StringComparer.Ordinal);
        var bad = Surface.Where(kv => !kv.Value.StartsWith("- ", StringComparison.Ordinal))
            .Where(kv => !vars.ContainsKey(kv.Value) || !inPanels.Contains(kv.Value))
            .Select(kv => kv.Key + " -> " + kv.Value).ToList();
        Assert.True(bad.Count == 0, "Not a panel control: " + string.Join(", ", bad));
    }

    [Fact]
    public void EveryOmissionCarriesAReason()
    {
        var bad = Surface.Where(kv => kv.Value.StartsWith("- ", StringComparison.Ordinal) && kv.Value.Length < 12).ToList();
        Assert.True(bad.Count == 0, "Reasonless omissions: " + string.Join(", ", bad.Select(b => b.Key)));
    }

    /// <summary>Live comparison against the installed package; silently passes where it is absent (CI).</summary>
    [Fact]
    public void TheInstalledPackageDeclaresExactlyTheseNodes()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\Packages\Community\flysimware-aircraft-learjet-35a\ModelBehaviorDefs\Flysimware_L35A\Custom");
        if (!Directory.Exists(dir)) return;

        var interactive = new HashSet<string>(new[]
        {
            "Flysimware_GENERIC_SWITCH_Template", "Flysimware_3Way_Momentary_Switch_Template",
            "Flysimware_Knob_ROTARY_Template", "Flysimware_GENERIC_FINITE_KNOB_Template",
            "Flysimware_GENERIC_INFINITE_KNOB_Template", "Flysimware_GENERIC_CUSTOM_FINITE_KNOB_Template",
            "Flysimware_GENERIC_CUSTOM_INFINITE_KNOB_Template", "Flysimware_GENERIC_FINITE_LEVER_Template",
            "Flysimware_GENERIC_INFINITE_LEVER_Template", "Flysimware_GENERIC_DRAGGING_AXIS_Template",
            "Flysimware_GENERIC_SIMVAR_TOGGLE_SWITCH_Template", "Flysimware_GENERIC_SIMVAR_SET_SWITCH_Template",
            "Flysimware_Knob_PUSH_MOMENTARY_Template", "Flysimware_LVAR_LR_PUSH_BUTTON_Template",
            "FLYSIMWARE_INSTRUMENT_Baro_Template", "FLYSIMWARE_INSTRUMENT_Knob_AttitudeCage_Template"
        }, StringComparer.Ordinal);

        var found = new HashSet<string>(StringComparer.Ordinal);
        var use = new System.Text.RegularExpressions.Regex("<UseTemplate Name=\"([^\"]+)\">(.*?)</UseTemplate>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        var node = new System.Text.RegularExpressions.Regex("<NODE_ID>([^<#]+)</NODE_ID>");
        foreach (var file in Directory.GetFiles(dir, "*.xml"))
        {
            foreach (System.Text.RegularExpressions.Match m in use.Matches(File.ReadAllText(file)))
            {
                if (!interactive.Contains(m.Groups[1].Value)) continue;
                var n = node.Match(m.Groups[2].Value);
                if (n.Success) found.Add(n.Groups[1].Value.Trim());
            }
        }

        var added = found.Except(Surface.Keys).OrderBy(x => x).ToList();
        var removed = Surface.Keys.Except(found).OrderBy(x => x).ToList();
        Assert.True(added.Count == 0 && removed.Count == 0,
            "Vendor package differs. Added: " + string.Join(", ", added) + ". Removed: " + string.Join(", ", removed));
    }
}
