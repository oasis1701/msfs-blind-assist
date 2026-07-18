using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// iFly 737 MAX8 — forward panel + pedestal sections: landing gear, autobrake,
/// display select, GPWS, EFIS (both sides), radios, fire protection, cargo fire,
/// trim, control stand, door lock. All field names are SDK struct field names
/// (arrays flattened "Field_i"; index 0 = Captain/left, 1 = FO/right; RTP arrays
/// 0/1/2 = RTP 1/2/3). All write commands and Value2 encodings verified against
/// the SDK key_command.h docs — see the inline comments for the encoding traps.
/// </summary>
public partial class IFly737MAXDefinition
{
    // =========================================================================
    // Landing Gear
    // =========================================================================

    private void RegisterLandingGear()
    {
        const string P = "Landing Gear";

        // GEAR_LEVER_SET: Value2 0 = UP, 1 = DN — matches Gear_Lever_Status directly.
        Sw(P, "Gear_Lever_Status", "Landing Gear Lever",
            IFlyKeyCommand.GEAR_LEVER_SET, new[] { "Up", "Down" });
        Btn(P, "BTN_GEAR_LOCK_OVRD", "Landing Gear Lever Lock Override",
            IFlyKeyCommand.GEAR_LEVER_LOCK_SWITCH);

        // Gear position lights: red = in transit / disagree, green = down and locked.
        Annun(P, "NOSE_GEAR_RedLight_Status", "Nose Gear red light");
        Annun(P, "NOSE_GEAR_GreenLight_Status", "Nose Gear green light");
        Annun(P, "LEFT_GEAR_RedLight_Status", "Left Gear red light");
        Annun(P, "LEFT_GEAR_GreenLight_Status", "Left Gear green light");
        Annun(P, "RIGHT_GEAR_RedLight_Status", "Right Gear red light");
        Annun(P, "RIGHT_GEAR_GreenLight_Status", "Right Gear green light");

        // GEAR_PARKING_BRAKE_SET: Value2 0 = RELEASE, 1 = SET (matches status);
        // Value3 1 = ignore the brake-pedal position and set the lever directly
        // (without it the SDK requires held pedals, which a hotkey user can't do).
        // Fleet parity: every aircraft announces a single "Parking brake on/off"
        // (A380 OnOff helper, Fenix, PMDG 737 — see FlyByWireA320Definition.cs:1806).
        // The lever is the one announcing source; the brake LIGHT is a quiet
        // read-only row so one brake action never speaks twice. (PR #163, M6.)
        Sw(P, "Parking_Brake_Lever_Status", "Parking brake",
            IFlyKeyCommand.GEAR_PARKING_BRAKE_SET, new[] { "off", "on" }, value3: 1);
        SwD(P, "Parking_Brake_Light_Status", "Parking Brake light", set: null,
            descriptions: new Dictionary<double, string> { [0] = "off", [1] = "on", [2] = "on" }, announced: false);

        Disp(P, "Hydraulic_Brake_Pressure_Status", "Hydraulic Brake Pressure PSI");

        // Guarded switch: combo state from the _Mode field (1 = ALT, 2 = NORM);
        // GEAR_STEERING_SWITCH_SET Value2 is 0 = ALT, 1 = NORM → map v-1.
        Sw(P, "Nose_Wheel_Steering_Mode", "Nose Wheel Steering",
            IFlyKeyCommand.GEAR_STEERING_SWITCH_SET, new[] { "Alternate", "Normal" },
            map: v => v - 1, valueBase: 1);
    }

    // =========================================================================
    // Autobrake
    // =========================================================================

    private void RegisterAutobrake()
    {
        const string P = "Autobrake";

        // GEAR_AUTOBRAKE_SET: Value2 0 RTO / 1 OFF / 2..4 = 1..3 / 5 MAX — matches status.
        Sw(P, "Autobrake_Selector_Status", "Autobrake Selector",
            IFlyKeyCommand.GEAR_AUTOBRAKE_SET,
            new[] { "RTO", "Off", "1", "2", "3", "Max Auto" });

        Annun(P, "AUTO_BRAKE_DISARM_Light_Status", "Auto Brake Disarm light");
        Annun(P, "ANTISKID_INOP_Light_Status", "Antiskid Inoperative light");
        Annun(P, "BRAKE_TEMP_Light_Status", "Brake Temperature light");
        Annun(P, "TIRE_PRESSURE_Light_Status", "Tire Pressure light");
    }

    // =========================================================================
    // Display Select
    // =========================================================================

    private void RegisterDisplaySelect()
    {
        const string P = "Display Select";

        // CAPT_DISP_SEL_SET: Value2 0 OUTBD / 1 NORMAL / 2 INBD — matches the CAPT status.
        Sw(P, "CAPT_Display_Selector_Switch_Status", "Captain Display Selector",
            IFlyKeyCommand.INSTRUMENT_CAPT_DISP_SEL_SET, new[] { "Outboard", "Normal", "Inboard" });

        // ⚠ ENCODING TRAP: the FO STATUS field is 0 INBD / 1 NORMAL / 2 OUTBD, but
        // FO_DISP_SEL_SET Value2 is 0 OUTBD / 1 NORMAL / 2 INBD (same order as the
        // captain command, NOT the FO status) → map 2 - v.
        Sw(P, "FO_Display_Selector_Switch_Status", "First Officer Display Selector",
            IFlyKeyCommand.INSTRUMENT_FO_DISP_SEL_SET, new[] { "Inboard", "Normal", "Outboard" },
            map: v => 2 - v);

        Btn(P, "BTN_MFD_ENG", "MFD Engine", IFlyKeyCommand.INSTRUMENT_MFD_ENG);
        Btn(P, "BTN_MFD_SYS", "MFD System", IFlyKeyCommand.INSTRUMENT_MFD_SYS);
        Btn(P, "BTN_MFD_INFO", "MFD Information", IFlyKeyCommand.INSTRUMENT_MFD_INFO);
        Btn(P, "BTN_MFD_CR", "MFD Cancel Recall", IFlyKeyCommand.INSTRUMENT_MFD_CR);
        Btn(P, "BTN_ENG_TFR", "Engine Display Transfer", IFlyKeyCommand.INSTRUMENT_ENG_TFR);

        Sw(P, "Source_Switch_Status", "Displays Source Select",
            IFlyKeyCommand.INSTRUMENT_DISPLAYS_SOURCE_SET, new[] { "All on 1", "Auto", "All on 2" });
        Sw(P, "Control_Panel_Switch_Status", "Displays Control Panel Select",
            IFlyKeyCommand.INSTRUMENT_CONTROL_PANEL_SET, new[] { "Both on 1", "Normal", "Both on 2" });
    }

    // =========================================================================
    // GPWS
    // =========================================================================

    private void RegisterGpws()
    {
        const string P = "GPWS";

        // Guarded inhibit switches. Status: 0 cover CLOSED / 1 open+INHIBIT / 2 open+NORM.
        // WARNING_GPWS_*_INHIBIT_SET Value2: 0 = INHIBIT, 1 = NORMAL (no guard-ignore
        // Value3 documented; the SET operates the switch regardless of the cover).
        // Selecting "Guard closed, normal" commands NORMAL (the cover state itself
        // can't be commanded).
        void Inhibit(string field, string display, IFlyKeyCommand set) =>
            SwD(P, field, display, set,
                new Dictionary<double, string>
                {
                    [0] = "Guard closed, normal",
                    [1] = "Inhibit",
                    [2] = "Normal",
                },
                map: v => v == 1 ? 0 : 1);

        Inhibit("Flap_Inhibit_Switch_Status", "Flap Inhibit", IFlyKeyCommand.WARNING_GPWS_FLAP_INHIBIT_SET);
        Inhibit("Gear_Inhibit_Switch_Status", "Gear Inhibit", IFlyKeyCommand.WARNING_GPWS_GEAR_INHIBIT_SET);
        Inhibit("Terr_Inhibit_Switch_Status", "Terrain Inhibit", IFlyKeyCommand.WARNING_GPWS_TERR_INHIBIT_SET);
        Inhibit("Runway_Inhibit_Switch_Status", "Runway Inhibit", IFlyKeyCommand.WARNING_GPWS_RUNWAY_INHIBIT_SET);

        Btn(P, "BTN_GPWS_SYS_TEST", "Ground Proximity System Test", IFlyKeyCommand.WARNING_GPWS_SYS_TEST);

        Annun(P, "GPWS_INOP_Light_Status", "GPWS Inoperative light");
        Annun(P, "RUNWAY_INOP_Light_Status", "Runway Inoperative light");
    }

    // =========================================================================
    // EFIS (Captain + First Officer)
    // =========================================================================

    private void RegisterEfis()
    {
        RegisterEfisSide(capt: true);
        RegisterEfisSide(capt: false);
    }

    private void RegisterEfisSide(bool capt)
    {
        string P = capt ? "EFIS Captain" : "EFIS First Officer";
        string sfx = capt ? "_0" : "_1"; // SDK array index: 0 = Captain, 1 = First Officer
        string tag = capt ? "CAPT" : "FO";
        IFlyKeyCommand C(IFlyKeyCommand l, IFlyKeyCommand r) => capt ? l : r;

        // ND mode. MODE_SET Value2: 0 APP / 1 VOR / 2 MAP / 3 PLN — matches status.
        Sw(P, $"ND_Mode_Status{sfx}", "Navigation Display Mode",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_MODE_SET, IFlyKeyCommand.INSTRUMENT_EFIS_R_MODE_SET),
            new[] { "Approach", "VOR", "Map", "Plan" });
        Btn(P, $"BTN_EFIS_{tag}_CTR", "Center Display",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_CTR, IFlyKeyCommand.INSTRUMENT_EFIS_R_CTR));

        // ⚠ ENCODING TRAP: the ND_Range_Status struct comment says "0~2", but the
        // RANGE_SET command doc is authoritative: Value2 0..10 = 0.5 / 1 / 2 / 5 /
        // 10 / 20 / 40 / 80 / 160 / 320 / 640 nautical miles. Positions follow it.
        Sw(P, $"ND_Range_Status{sfx}", "Navigation Display Range",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_RANGE_SET, IFlyKeyCommand.INSTRUMENT_EFIS_R_RANGE_SET),
            new[] { "0.5 miles", "1 mile", "2 miles", "5 miles", "10 miles", "20 miles",
                    "40 miles", "80 miles", "160 miles", "320 miles", "640 miles" });
        Btn(P, $"BTN_EFIS_{tag}_TFC", "Traffic",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_TFC, IFlyKeyCommand.INSTRUMENT_EFIS_R_TFC));

        // VOR/ADF bearing pointer selectors. VORADF_SET Value2: 0 VOR / 1 OFF / 2 ADF — matches status.
        Sw(P, $"VORADF_L_Status{sfx}", "Left VOR ADF Selector",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_LEFT_VORADF_SET, IFlyKeyCommand.INSTRUMENT_EFIS_R_LEFT_VORADF_SET),
            new[] { "VOR", "Off", "ADF" });
        Sw(P, $"VORADF_R_Status{sfx}", "Right VOR ADF Selector",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_RIGHT_VORADF_SET, IFlyKeyCommand.INSTRUMENT_EFIS_R_RIGHT_VORADF_SET),
            new[] { "VOR", "Off", "ADF" });

        // Map option momentary buttons.
        Btn(P, $"BTN_EFIS_{tag}_WXR", "Weather Radar Overlay",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_WXR, IFlyKeyCommand.INSTRUMENT_EFIS_R_WXR));
        Btn(P, $"BTN_EFIS_{tag}_STA", "Stations Overlay",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_STA, IFlyKeyCommand.INSTRUMENT_EFIS_R_STA));
        Btn(P, $"BTN_EFIS_{tag}_WPT", "Waypoints Overlay",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_WPT, IFlyKeyCommand.INSTRUMENT_EFIS_R_WPT));
        Btn(P, $"BTN_EFIS_{tag}_ARPT", "Airports Overlay",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_ARPT, IFlyKeyCommand.INSTRUMENT_EFIS_R_ARPT));
        Btn(P, $"BTN_EFIS_{tag}_DATA", "Data Overlay",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_DATA, IFlyKeyCommand.INSTRUMENT_EFIS_R_DATA));
        Btn(P, $"BTN_EFIS_{tag}_POS", "Position Overlay",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_POS, IFlyKeyCommand.INSTRUMENT_EFIS_R_POS));
        Btn(P, $"BTN_EFIS_{tag}_TERR", "Terrain Overlay",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_TERR, IFlyKeyCommand.INSTRUMENT_EFIS_R_TERR));
        Btn(P, $"BTN_EFIS_{tag}_FPV", "Flight Path Vector",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_FPV, IFlyKeyCommand.INSTRUMENT_EFIS_R_FPV));
        Btn(P, $"BTN_EFIS_{tag}_MTRS", "Meters",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_MTRS, IFlyKeyCommand.INSTRUMENT_EFIS_R_MTRS));
        Btn(P, $"BTN_EFIS_{tag}_VSD", "Vertical Situation Display",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_VSD, IFlyKeyCommand.INSTRUMENT_EFIS_R_VSD));

        // Minimums reference: no SET command exists for BARO_RADIO_Status (only the
        // two absolute selects) → read-only status combo + two buttons.
        Sw(P, $"BARO_RADIO_Status{sfx}", "Minimums Reference", set: null,
            new[] { "Radio", "Baro" });
        Btn(P, $"BTN_EFIS_{tag}_MINS_RADIO", "Minimums Reference Radio",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_MINS_REF_RADIO, IFlyKeyCommand.INSTRUMENT_EFIS_R_MINS_REF_RADIO));
        Btn(P, $"BTN_EFIS_{tag}_MINS_BARO", "Minimums Reference Baro",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_MINS_REF_BARO, IFlyKeyCommand.INSTRUMENT_EFIS_R_MINS_REF_BARO));

        // MINS_SET Value2 doc says only "altitude" (no explicit range documented);
        // 0..15000 feet covers radio and high-elevation baro minimums.
        NumSet(P, $"EFIS_{tag}_MINS_SET", "Set Minimums",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_MINS_SET, IFlyKeyCommand.INSTRUMENT_EFIS_R_MINS_SET),
            0, 15000, units: "feet");
        Btn(P, $"BTN_EFIS_{tag}_MINS_RST", "Minimums Reset",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_MINS_RST, IFlyKeyCommand.INSTRUMENT_EFIS_R_MINS_RST));

        // Baro standard toggle + units. There is no baro-units SET command (only the
        // absolute REF_IN / REF_HPA selects) → read-only status combo + two buttons.
        // The absolute baro VALUE has no SET either (INC/DEC only — the Ctrl+B dialog
        // in the core file sets it via the stock KOHLSMAN_SET event instead).
        Btn(P, $"BTN_EFIS_{tag}_BARO_STD", "Baro Standard",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_BARO_STD, IFlyKeyCommand.INSTRUMENT_EFIS_R_BARO_STD));
        Sw(P, $"Baro_Select_Status{sfx}", "Baro Units", set: null,
            new[] { "Inches", "Hectopascals" });
        Btn(P, $"BTN_EFIS_{tag}_BARO_IN", "Baro Inches",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_BARO_REF_IN, IFlyKeyCommand.INSTRUMENT_EFIS_R_BARO_REF_IN));
        Btn(P, $"BTN_EFIS_{tag}_BARO_HPA", "Baro Hectopascals",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_BARO_REF_HPA, IFlyKeyCommand.INSTRUMENT_EFIS_R_BARO_REF_HPA));
        Btn(P, $"BTN_EFIS_{tag}_BARO_INC", "Baro Increase",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_BARO_INC, IFlyKeyCommand.INSTRUMENT_EFIS_R_BARO_INC));
        Btn(P, $"BTN_EFIS_{tag}_BARO_DEC", "Baro Decrease",
            C(IFlyKeyCommand.INSTRUMENT_EFIS_L_BARO_DEC, IFlyKeyCommand.INSTRUMENT_EFIS_R_BARO_DEC));
    }

    // =========================================================================
    // Radios (RTP 1 Captain / RTP 2 First Officer / RTP 3 overhead)
    // =========================================================================

    private void RegisterRadios()
    {
        const string P = "Radios";

        void Rtp(int n, IFlyKeyCommand transfer, IFlyKeyCommand off,
                 IFlyKeyCommand vhf1, IFlyKeyCommand vhf2, IFlyKeyCommand vhf3)
        {
            int i = n - 1; // RTP array index

            // Frequency windows — client-composed synthetics from the per-digit
            // RTP_Left/Right_Num_* display fields (see IFlySdkClient).
            Disp(P, $"SYN_RTP{n}_ACTIVE", $"RTP {n} Active Frequency");
            Disp(P, $"SYN_RTP{n}_STANDBY", $"RTP {n} Standby Frequency");

            // PLACEHOLDER COMMAND: there is no absolute standby-frequency SET in the
            // iFly SDK (WHOLE/FRACT INC/DEC rotaries only). The core file special-cases
            // the "RTP{n}_STANDBY_SET" keys in HandleUIVariableSet BEFORE the generic
            // write dispatch and keys the rotaries to the target — the TRANSFER command
            // registered here is never fired for these keys.
            NumSet(P, $"RTP{n}_STANDBY_SET", $"RTP {n} Set Standby Frequency",
                transfer, 118, 136.975, units: "megahertz");

            Btn(P, $"BTN_RTP{n}_TRANSFER", $"RTP {n} Frequency Transfer", transfer);
            Btn(P, $"BTN_RTP{n}_VHF1", $"RTP {n} Select VHF 1", vhf1);
            Btn(P, $"BTN_RTP{n}_VHF2", $"RTP {n} Select VHF 2", vhf2);
            Btn(P, $"BTN_RTP{n}_VHF3", $"RTP {n} Select VHF 3", vhf3);
            Btn(P, $"BTN_RTP{n}_OFF", $"RTP {n} Off", off);

            Annun(P, $"RTP_VHF1_Radio_Light_Status_{i}", $"RTP {n} VHF 1 selected light");
            Annun(P, $"RTP_VHF2_Radio_Light_Status_{i}", $"RTP {n} VHF 2 selected light");
            Annun(P, $"RTP_VHF3_Radio_Light_Status_{i}", $"RTP {n} VHF 3 selected light");
        }

        Rtp(1, IFlyKeyCommand.COMMUNICATION_RTP_1_TRANSFER, IFlyKeyCommand.COMMUNICATION_RTP_1_OFF,
            IFlyKeyCommand.COMMUNICATION_RTP_1_VHF1, IFlyKeyCommand.COMMUNICATION_RTP_1_VHF2,
            IFlyKeyCommand.COMMUNICATION_RTP_1_VHF3);
        Rtp(2, IFlyKeyCommand.COMMUNICATION_RTP_2_TRANSFER, IFlyKeyCommand.COMMUNICATION_RTP_2_OFF,
            IFlyKeyCommand.COMMUNICATION_RTP_2_VHF1, IFlyKeyCommand.COMMUNICATION_RTP_2_VHF2,
            IFlyKeyCommand.COMMUNICATION_RTP_2_VHF3);
        Rtp(3, IFlyKeyCommand.COMMUNICATION_RTP_3_TRANSFER, IFlyKeyCommand.COMMUNICATION_RTP_3_OFF,
            IFlyKeyCommand.COMMUNICATION_RTP_3_VHF1, IFlyKeyCommand.COMMUNICATION_RTP_3_VHF2,
            IFlyKeyCommand.COMMUNICATION_RTP_3_VHF3);

        // NAV control panels (aft pedestal, left = NAV 1 / right = NAV 2): keypad
        // frequency entry into the standby window, TFR swaps active<->standby.
        void Nav(int n, IFlyKeyCommand tfr, IFlyKeyCommand test,
                 IFlyKeyCommand modeUp, IFlyKeyCommand modeDn)
        {
            // Windows are client-composed synthetics from the per-digit
            // NAV_num_* display fields + the ILS/VOR/GLS mode flag.
            Disp(P, $"SYN_NAV{n}_ACTIVE", $"NAV {n} Active Frequency");
            Disp(P, $"SYN_NAV{n}_STANDBY", $"NAV {n} Standby Frequency");

            // PLACEHOLDER COMMAND (RTP pattern): entry is keyed digit-by-digit on
            // the panel keypad — HandleUIVariableSet intercepts "NAV{n}_STANDBY_SET"
            // before the generic write dispatch; the TFR registered here never
            // fires for this key. Accepts 108-117.95 (VOR/ILS) or a 5-digit GLS channel.
            NumSet(P, $"NAV{n}_STANDBY_SET", $"NAV {n} Set Standby Frequency",
                tfr, 108, 39999, units: "megahertz");

            Btn(P, $"BTN_NAV{n}_TRANSFER", $"NAV {n} Frequency Transfer", tfr);
            Btn(P, $"BTN_NAV{n}_TEST", $"NAV {n} Test", test);
            Btn(P, $"BTN_NAV{n}_MODE_UP", $"NAV {n} Mode Up", modeUp);
            Btn(P, $"BTN_NAV{n}_MODE_DOWN", $"NAV {n} Mode Down", modeDn);
        }

        Nav(1, IFlyKeyCommand.FMS_NAV_1_TFR, IFlyKeyCommand.FMS_NAV_1_TEST,
            IFlyKeyCommand.FMS_NAV_1_MODE_UP, IFlyKeyCommand.FMS_NAV_1_MODE_DN);
        Nav(2, IFlyKeyCommand.FMS_NAV_2_TFR, IFlyKeyCommand.FMS_NAV_2_TEST,
            IFlyKeyCommand.FMS_NAV_2_MODE_UP, IFlyKeyCommand.FMS_NAV_2_MODE_DN);
    }

    // =========================================================================
    // Fire Protection
    // =========================================================================

    /// <summary>Fire-switch 0-11 state labels: position group (NORM / pulled UP /
    /// rotated LEFT / rotated RIGHT) × fire light (off / dim / bright).</summary>
    private static Dictionary<double, string> FireSwitchStates(string leftDischarge, string rightDischarge)
    {
        string[] positions =
        {
            "Normal", "Pulled up",
            $"Rotated left, {leftDischarge}", $"Rotated right, {rightDischarge}",
        };
        string[] lights = { "", ", fire light dim", ", fire light bright" };
        var d = new Dictionary<double, string>();
        for (int p = 0; p < 4; p++)
            for (int l = 0; l < 3; l++)
                d[p * 3 + l] = positions[p] + lights[l];
        return d;
    }

    private void RegisterFire()
    {
        const string P = "Fire Protection";

        // Fire switches: status 0-11 read-only combo + Pull / Rotate Left / Rotate Right
        // buttons. The FIRE_*_FIRE_SWITCH_SET command exists (Value2 0 NORM / 1 UP /
        // 2 rotate LEFT / 3 rotate RIGHT — note its doc misprints position 3 as
        // "rotate LEFT" twice) but the read-only-combo-plus-buttons shape matches the
        // discrete pull/rotate actions a pilot actually performs.
        // PULL sends Value2 1 = override, so the handle unlocks even with no fire
        // (the real panel's override-and-pull action).
        void FireHandle(string field, string display, Dictionary<double, string> states,
                        string btnTag, IFlyKeyCommand pull, IFlyKeyCommand rotL, IFlyKeyCommand rotR)
        {
            SwD(P, field, display, set: null, states);
            Btn(P, $"BTN_FIRE_{btnTag}_PULL", $"{display} Pull", pull, value2: 1);
            Btn(P, $"BTN_FIRE_{btnTag}_ROT_L", $"{display} Rotate Left", rotL);
            Btn(P, $"BTN_FIRE_{btnTag}_ROT_R", $"{display} Rotate Right", rotR);
        }

        FireHandle("Engine_Fire_Switch_Status_0", "Engine 1 Fire Switch",
            FireSwitchStates("bottle 1 discharged", "bottle 2 discharged"), "ENG1",
            IFlyKeyCommand.FIRE_ENG_1_FIRE_SWITCH_PULL,
            IFlyKeyCommand.FIRE_ENG_1_FIRE_SWITCH_DEC, IFlyKeyCommand.FIRE_ENG_1_FIRE_SWITCH_INC);
        FireHandle("Engine_Fire_Switch_Status_1", "Engine 2 Fire Switch",
            FireSwitchStates("bottle 1 discharged", "bottle 2 discharged"), "ENG2",
            IFlyKeyCommand.FIRE_ENG_2_FIRE_SWITCH_PULL,
            IFlyKeyCommand.FIRE_ENG_2_FIRE_SWITCH_DEC, IFlyKeyCommand.FIRE_ENG_2_FIRE_SWITCH_INC);
        FireHandle("APU_Fire_Switch_Status", "APU Fire Switch",
            FireSwitchStates("bottle discharged", "bottle discharged"), "APU",
            IFlyKeyCommand.FIRE_APU_FIRE_SWITCH_PULL,
            IFlyKeyCommand.FIRE_APU_FIRE_SWITCH_DEC, IFlyKeyCommand.FIRE_APU_FIRE_SWITCH_INC);

        Btn(P, "BTN_FIRE_BELL_CUTOUT", "Fire Warning Bell Cutout", IFlyKeyCommand.FIRE_BELL_CUTOUT);

        // Spring-loaded 3-position test switches, driven by their SET commands
        // (EXT_TEST_SET / FIRE_TEST_SET Value2: 0 / 1 neutral / 2). If the switch
        // turns out to latch in the test position in-sim, add a "release" press
        // (SET Value2 1) or move to the DEC/INC click commands.
        Btn(P, "BTN_FIRE_EXT_TEST_1", "Extinguisher Test 1", IFlyKeyCommand.FIRE_EXT_TEST_SET, value2: 0);
        Btn(P, "BTN_FIRE_EXT_TEST_2", "Extinguisher Test 2", IFlyKeyCommand.FIRE_EXT_TEST_SET, value2: 2);
        Btn(P, "BTN_FIRE_TEST_FAULT", "Fire Test Fault Inoperative", IFlyKeyCommand.FIRE_TEST_SET, value2: 0);
        Btn(P, "BTN_FIRE_TEST_OVHT", "Fire Test Overheat Fire", IFlyKeyCommand.FIRE_TEST_SET, value2: 2);

        // Overheat detector loop selectors. LEFT = engine 1, RIGHT = engine 2 per the
        // command docs. SET Value2: 0 A / 1 NORMAL / 2 B — matches status.
        Sw(P, "OVHT_DET_1_Switch_Status", "Engine 1 Overheat Detector",
            IFlyKeyCommand.FIRE_LEFT_OVHT_DET_SET, new[] { "A", "Normal", "B" });
        Sw(P, "OVHT_DET_2_Switch_Status", "Engine 2 Overheat Detector",
            IFlyKeyCommand.FIRE_RIGHT_OVHT_DET_SET, new[] { "A", "Normal", "B" });

        Annun(P, "L_BOTTLE_DISCHARGE_Light_Status", "Left Bottle Discharge light");
        Annun(P, "R_BOTTLE_DISCHARGE_Light_Status", "Right Bottle Discharge light");
        Annun(P, "APU_BOTTLE_DISCHARGE_Light_Status", "APU Bottle Discharge light");
        Annun(P, "APU_DET_INOP_Light_Status", "APU Detector Inoperative light");
        Annun(P, "ENG1_OVERHEAT_Light_Status", "Engine 1 Overheat light");
        Annun(P, "ENG2_OVERHEAT_Light_Status", "Engine 2 Overheat light");
        Annun(P, "Extinguisher_Test_Light_L_Status", "Left Extinguisher Test light");
        Annun(P, "Extinguisher_Test_Light_R_Status", "Right Extinguisher Test light");
        Annun(P, "Extinguisher_Test_Light_APU_Status", "APU Extinguisher Test light");
        Annun(P, "WHEEL_WELL_Light_Status", "Wheel Well Fire light");
        Annun(P, "OverheatDetector_FAULT_Light_Status", "Overheat Detector Fault light");
    }

    // =========================================================================
    // Cargo Fire
    // =========================================================================

    private void RegisterCargoFire()
    {
        const string P = "Cargo Fire";

        // Arm switch status 0-5: 0-2 OFF × light, 3-5 ON (armed) × light.
        // ARM_SET Value2: 0 = OFF, 1 = ON → map v >= 3 ? 1 : 0.
        var armStates = new Dictionary<double, string>
        {
            [0] = "Off",
            [1] = "Off, light dim",
            [2] = "Off, light bright",
            [3] = "Armed",
            [4] = "Armed, light dim",
            [5] = "Armed, light bright",
        };
        SwD(P, "FWD_Cargo_FIRE_Switch_Status", "Forward Cargo Fire Arm",
            IFlyKeyCommand.FIRE_FWD_CARGO_FIRE_ARM_SET, armStates, map: v => v >= 3 ? 1 : 0);
        SwD(P, "AFT_Cargo_FIRE_Switch_Status", "Aft Cargo Fire Arm",
            IFlyKeyCommand.FIRE_AFT_CARGO_FIRE_ARM_SET, armStates, map: v => v >= 3 ? 1 : 0);

        // Discharge switch status 0-11: 0-5 switch OFF, 6-11 switch ON, each ×
        // (cover closed/open) × (light off/dim/bright). DISCH_SET Value2: 0 OFF /
        // 1 ON → map v >= 6 ? 1 : 0; Value3 1 = ignore the guard.
        var dischStates = new Dictionary<double, string>();
        for (int on = 0; on < 2; on++)
            for (int l = 0; l < 3; l++)
                for (int open = 0; open < 2; open++)
                {
                    string s = (on == 1 ? "Discharged" : "Off")
                             + (open == 1 ? ", guard open" : ", guard closed")
                             + (l == 1 ? ", light dim" : l == 2 ? ", light bright" : "");
                    dischStates[on * 6 + l * 2 + open] = s;
                }
        SwD(P, "CARGO_FIRE_Discharge_Switch_Status", "Cargo Fire Discharge",
            IFlyKeyCommand.FIRE_CARGO_FIRE_DISCH_SET, dischStates,
            map: v => v >= 6 ? 1 : 0, value3: 1);

        Btn(P, "BTN_CARGO_FIRE_TEST", "Cargo Fire Test", IFlyKeyCommand.FIRE_CARGO_FIRE_TEST);

        // DET SELECT 1 = FWD loop, 2 = AFT loop (FWD/AFT per the command docs).
        // SET Value2: 0 A / 1 NORM / 2 B — matches status.
        Sw(P, "DET_Select_1_Switch_Status", "Forward Detector Select",
            IFlyKeyCommand.FIRE_FWD_DET_SELECT_SET, new[] { "A", "Normal", "B" });
        Sw(P, "DET_Select_2_Switch_Status", "Aft Detector Select",
            IFlyKeyCommand.FIRE_AFT_DET_SELECT_SET, new[] { "A", "Normal", "B" });

        Annun(P, "Cargo_Fire_Test_Light_FWD_Status", "Forward Cargo Fire Test light");
        Annun(P, "Cargo_Fire_Test_Light_AFT_Status", "Aft Cargo Fire Test light");
        Annun(P, "DETECTOR_FAULT_Light_Status", "Cargo Detector Fault light");
    }

    // =========================================================================
    // Trim
    // =========================================================================

    private void RegisterTrim()
    {
        const string P = "Trim";

        // Guarded cutout/override switches: combo state from the _Mode fields
        // (1-based), commands' Value2 is 0-based in the same order → map v-1.
        //   STAB_TRIM_PRI_SET / BU_SET:   Value2 0 NORMAL / 1 CUTOFF (no guard Value3 documented)
        //   STAB_TRIM_OVERRIDE_SET:       Value2 0 OVERRIDE / 1 NORMAL, Value3 1 = ignore guard
        Sw(P, "Stab_Trim_Primary_Mode", "Stabilizer Trim Primary Cutout",
            IFlyKeyCommand.FLTCTRL_STAB_TRIM_PRI_SET, new[] { "Normal", "Cutoff" },
            map: v => v - 1, valueBase: 1);
        Sw(P, "Stab_Trim_Backup_Mode", "Stabilizer Trim Backup Cutout",
            IFlyKeyCommand.FLTCTRL_STAB_TRIM_BU_SET, new[] { "Normal", "Cutoff" },
            map: v => v - 1, valueBase: 1);
        Sw(P, "Stabilizer_Trim_Override_Mode", "Stabilizer Trim Override",
            IFlyKeyCommand.FLTCTRL_STAB_TRIM_OVERRIDE_SET, new[] { "Override", "Normal" },
            map: v => v - 1, valueBase: 1, value3: 1);

        // Spring-loaded 3-position trim switches — one click per press.
        Btn(P, "BTN_AILERON_TRIM_LEFT", "Aileron Trim Left", IFlyKeyCommand.FLTCTRL_AILERON_TRIM_DEC);
        Btn(P, "BTN_AILERON_TRIM_RIGHT", "Aileron Trim Right", IFlyKeyCommand.FLTCTRL_AILERON_TRIM_INC);
        Btn(P, "BTN_RUDDER_TRIM_LEFT", "Rudder Trim Left", IFlyKeyCommand.FLTCTRL_RUDDER_TRIM_DEC);
        Btn(P, "BTN_RUDDER_TRIM_RIGHT", "Rudder Trim Right", IFlyKeyCommand.FLTCTRL_RUDDER_TRIM_INC);

        // Rudder trim indicator: -1.0 full left / 0 center / +1.0 full right.
        Disp(P, "Rudder_Trim_Pointer_Status", "Rudder Trim Indicator");
        // Stabilizer trim indicator: 0-17 units.
        Disp(P, "Stabilizer_Trim_Pointer_Status", "Stabilizer Trim Units");

        Annun(P, "STAB_OUT_TRIM_Light_Status", "Stabilizer Out of Trim light");
        // 0/1 flag (0 = flag hidden, 1 = flag shown) — Annun handles 0/1 fine.
        Annun(P, "Rudder_Trim_OFF_Light_Status", "Rudder Trim Off flag");
    }

    // =========================================================================
    // Control Stand
    // =========================================================================

    private void RegisterControlStand()
    {
        const string P = "Control Stand";

        // Engine start levers: status 0-2 CUTOFF × fire light, 3-5 IDLE × fire light.
        // ⚠ ENCODING TRAP: ENGAPU_ENG_n_START_LEVER_SET Value2 is 0 = IDLE, 1 = CUTOFF
        // (INVERTED vs. the intuitive order) → map v >= 3 ? 0 : 1.
        var startLeverStates = new Dictionary<double, string>
        {
            [0] = "Cutoff",
            [1] = "Cutoff, fire light dim",
            [2] = "Cutoff, fire light bright",
            [3] = "Idle",
            [4] = "Idle, fire light dim",
            [5] = "Idle, fire light bright",
        };
        SwD(P, "Engine_Start_Lever_Status_0", "Engine 1 Start Lever",
            IFlyKeyCommand.ENGAPU_ENG_1_START_LEVER_SET, startLeverStates, map: v => v >= 3 ? 0 : 1);
        SwD(P, "Engine_Start_Lever_Status_1", "Engine 2 Start Lever",
            IFlyKeyCommand.ENGAPU_ENG_2_START_LEVER_SET, startLeverStates, map: v => v >= 3 ? 0 : 1);

        // FLTCTRL_FLAP_SET Value2 0-8 = lever detents UP..40 — matches FLAP_Status.
        Sw(P, "FLAP_Status", "Flap Lever",
            IFlyKeyCommand.FLTCTRL_FLAP_SET,
            new[] { "Up", "1", "2", "5", "10", "15", "25", "30", "40" });

        // Speedbrake lever raw position (int 0-225):
        //   0 = DOWN, 35 = ARMED, 149 = FLIGHT DETENT, 224 = UP.
        Disp(P, "Spoiler_Lever_Status", "Speedbrake Lever Position");

        Annun(P, "SPEED_BRAKE_ARMED_Light_Status", "Speed Brake Armed light");
        Annun(P, "SPEED_BRAKE_DO_NOT_ARM_Light_Status", "Speed Brake Do Not Arm light");
        Annun(P, "SPEEDBRAKES_EXTENDED_Light_Status", "Speedbrakes Extended light");

        // Flap / slat position lights — multi-state read-only combos (no control).
        // SILENT (announced: false): every flap selection walks each light through
        // transit -> extended -> full per SIDE (plus dim/bright flips), up to ~16
        // announcements per lever movement — pure spam (user report 2026-07). The
        // flap LEVER announce above covers state changes; the L readout hotkey
        // reads position + "in transit" (from these lights) on demand.
        var flapLightStates = new Dictionary<double, string>
        {
            [0] = "All lights off",
            [1] = "Transit, dim",
            [2] = "Transit, bright",
            [3] = "Full extension, dim",
            [4] = "Full extension, bright",
            [5] = "All lights on, dim",
            [6] = "All lights on, bright",
        };
        SwD(P, "Flap_Left_Light_Status", "Left Flap lights", set: null, flapLightStates, announced: false);
        SwD(P, "Flap_Right_Light_Status", "Right Flap lights", set: null, flapLightStates, announced: false);

        var slatLightStates = new Dictionary<double, string>
        {
            [0] = "All lights off",
            [1] = "Transit, dim",
            [2] = "Transit, bright",
            [3] = "Extended, dim",
            [4] = "Extended, bright",
            [5] = "Full extension, dim",
            [6] = "Full extension, bright",
            [7] = "All lights on, dim",
            [8] = "All lights on, bright",
        };
        SwD(P, "Slat_Left_Light_Status", "Left Slat lights", set: null, slatLightStates, announced: false);
        SwD(P, "Slat_Right_Light_Status", "Right Slat lights", set: null, slatLightStates, announced: false);
    }

    // =========================================================================
    // Door Lock
    // =========================================================================

    private void RegisterDoorLock()
    {
        const string P = "Door Lock";

        // GENERAL_DOOR_LOCK_SET Value2: 0 UNLKD / 1 AUTO / 2 DENY — matches status.
        Sw(P, "Door_Lock_Selector_Status", "Flight Deck Door Lock",
            IFlyKeyCommand.GENERAL_DOOR_LOCK_SET, new[] { "Unlocked", "Auto", "Deny" });

        Annun(P, "LOCK_FAIL_Light_Status", "Door Lock Fail light");
        Annun(P, "AUTO_UNLK_Light_Status", "Door Auto Unlock light");
    }
}
