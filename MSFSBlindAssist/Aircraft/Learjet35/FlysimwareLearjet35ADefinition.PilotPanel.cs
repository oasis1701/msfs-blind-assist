using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Pilot Panel → Pilot Flight Instruments, Standby Attitude, Davtron Clock, Landing Gear.
/// Copilot Panel → Copilot Flight Instruments. Both altimeters live with the pilot's.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string PilotInstrumentsPanel = "Pilot Flight Instruments";
    private const string StandbyPanel = "Standby Attitude";
    private const string ClockPanel = "Davtron Clock";
    private const string GearPanel = "Landing Gear";
    private const string CopilotInstrumentsPanel = "Copilot Flight Instruments";

    private static readonly string[] DmeSource = { "NAV 1", "Hold", "NAV 2" };
    private static readonly string[] DmeDisplay = { "Off", "Minutes", "Knots" };
    private static readonly string[] HsiMode = { "Distance", "Time to go", "Ground speed" };

    private static Dictionary<string, SimVarDefinition> BuildPilotPanelVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- pilot instruments ----
        AddTyped(v, "LJ35_ASI_BUG_L_SET", "Pilot Airspeed Bug", "knots", "0 to 400.", "LJ35_ASI_BUG_L");
        AddReadout(v, "LJ35_ASI_BUG_L", "GENERIC_LEAR_AIRSPEED_KNOB_L", "Pilot Airspeed Bug", "knots", "F0");
        AddSwitch(v, "LJ35_RMI_PILOT_1", "RMI_BUTTON1_1", "Pilot RMI Needle 1", "VOR", "ADF");
        AddSwitch(v, "LJ35_RMI_PILOT_2", "RMI_BUTTON2_1", "Pilot RMI Needle 2", "VOR", "ADF");
        AddSimSwitch(v, "LJ35_NAVGPS_PILOT", "GPS DRIVES NAV1", "Pilot NAV/GPS", "NAV", "GPS",
            "GPS steers the HSI and autopilot from the GNS 530; NAV from the NAV 1 receiver.");
        AddRotary(v, "LJ35_DME1_SOURCE", "DME_MODE1", "DME 1 Source", DmeSource);
        AddRotary(v, "LJ35_DME1_DISPLAY", "DME_MODE1A", "DME 1 Display", DmeDisplay);
        AddRotary(v, "LJ35_HSI1_MODE", "HSI_MODE_SELECTOR_1", "Pilot HSI Readout", HsiMode);
        AddTyped(v, "LJ35_DH_SET", "Decision Height", "feet", "0 to 2500.", "LJ35_DH");
        AddSimReadout(v, "LJ35_DH", "DECISION HEIGHT", "Decision Height", "feet", "F0");
        AddButton(v, "LJ35_MARKER_TEST", "GENERIC_LEAR_SW_MARKER_TEST_1", "Marker Beacon Test");
        AddSwitch(v, "LJ35_ADC_SELECT", "LEAR_ADC1_ADC2", "Air Data Computer", "ADC 1", "ADC 2");
        AddDimmer(v, "LJ35_GEAR_BRT", "XMLVAR_GEAR_BRT_KNOB_Position", "Gear Light Brightness");
        AddSimReadout(v, "LJ35_IAS", "AIRSPEED INDICATED", "Airspeed", "knots", "F0");
        AddSimReadout(v, "LJ35_MACH", "AIRSPEED MACH", "Mach", "number", "F2");
        AddSimReadout(v, "LJ35_ALT_IND_1", "INDICATED ALTITUDE:1", "Pilot Altimeter", "feet", "F0");
        AddSimReadout(v, "LJ35_VSI", "VERTICAL SPEED", "Vertical Speed", "feet per minute", "F0");
        AddSimReadout(v, "LJ35_HDG", "PLANE HEADING DEGREES MAGNETIC", "Heading", "degrees", "F0");
        AddSimReadout(v, "LJ35_HSI_HDG", "HEADING INDICATOR", "HSI Compass Card", "degrees", "F0");
        AddSimReadout(v, "LJ35_HSI_COURSE", "NAV OBS:1", "HSI Course", "degrees", "F0");
        AddSimReadout(v, "LJ35_HSI_CDI", "HSI CDI NEEDLE", "Course Deviation", "number", "F0");
        AddSimReadout(v, "LJ35_HSI_GSI", "HSI GSI NEEDLE", "Glideslope Deviation", "number", "F0");
        AddFlag(v, "LJ35_HSI_TO_FROM", "HSI TF FLAGS", "HSI Flag", "From", "To", simvar: true);
        AddSimReadout(v, "LJ35_DME1_NM", "NAV DME:1", "DME 1 Distance", "nautical miles", "F1");
        AddSimReadout(v, "LJ35_DME1_KT", "NAV DMESPEED:1", "DME 1 Ground Speed", "knots", "F0");
        AddSimReadout(v, "LJ35_RADALT", "RADIO HEIGHT", "Radio Altitude", "feet", "F0");
        AddFlag(v, "LJ35_MKR_OUTER", "OUTER MARKER", "Outer Marker", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_MKR_MIDDLE", "MIDDLE MARKER", "Middle Marker", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_MKR_INNER", "INNER MARKER", "Inner Marker", "Off", "On", simvar: true);
        AddSimReadout(v, "LJ35_AOA", "ANGLE OF ATTACK INDICATOR", "Angle of Attack Indicator", "degrees", "F0");
        Cache(v, "LJ35_IAS"); Cache(v, "LJ35_MACH"); Cache(v, "LJ35_ALT_IND_1"); Cache(v, "LJ35_VSI"); Cache(v, "LJ35_HDG");
        Cache(v, "LJ35_RADALT"); Cache(v, "LJ35_DH"); Cache(v, "LJ35_ASI_BUG_L");

        // ---- altimeters, both sides ----
        AddTyped(v, "LJ35_BARO_1_SET", "Pilot Altimeter Setting", "hPa or inHg", "Type 1013 or 29.92.", "LJ35_BARO_1_MB");
        AddTyped(v, "LJ35_BARO_2_SET", "Copilot Altimeter Setting", "hPa or inHg", "Type 1013 or 29.92.", "LJ35_BARO_2_MB");
        AddButton(v, "LJ35_BARO_1_STD", "BARO_STD_1", "Pilot Altimeter Standard");
        AddButton(v, "LJ35_BARO_2_STD", "BARO_STD_2", "Copilot Altimeter Standard");
        AddLVarState(v, "LJ35_BARO_1_UNITS", "ALTIMETER_BARO_SWAP_1_MODE", "Pilot Altimeter Units",
            new Dictionary<double, string> { [0] = "Inches", [1] = "Hectopascals" });
        AddLVarState(v, "LJ35_BARO_2_UNITS", "ALTIMETER_BARO_SWAP_2_MODE", "Copilot Altimeter Units",
            new Dictionary<double, string> { [0] = "Inches", [1] = "Hectopascals" });
        AddSimReadout(v, "LJ35_BARO_1_MB", "KOHLSMAN SETTING MB:1", "Pilot Altimeter hPa", "millibars", "F0");
        AddSimReadout(v, "LJ35_BARO_1_HG", "KOHLSMAN SETTING HG:1", "Pilot Altimeter inHg", "inHg", "F2");
        AddSimReadout(v, "LJ35_BARO_2_MB", "KOHLSMAN SETTING MB:2", "Copilot Altimeter hPa", "millibars", "F0");
        AddSimReadout(v, "LJ35_BARO_2_HG", "KOHLSMAN SETTING HG:2", "Copilot Altimeter inHg", "inHg", "F2");
        Cache(v, "LJ35_BARO_1_MB"); Cache(v, "LJ35_BARO_2_MB"); Cache(v, "LJ35_BARO_1_HG"); Cache(v, "LJ35_BARO_2_HG");

        // ---- standby attitude ----
        AddButton(v, "LJ35_STBY_CAGE", "ATTITUDE_CAGE", "Cage Standby Attitude", "Hold to erect the standby gyro.");
        AddSimReadout(v, "LJ35_STBY_PITCH", "ATTITUDE INDICATOR PITCH DEGREES", "Standby Pitch", "degrees", "F1");
        AddSimReadout(v, "LJ35_STBY_BANK", "ATTITUDE INDICATOR BANK DEGREES", "Standby Bank", "degrees", "F1");
        AddSimReadout(v, "LJ35_PITCH", "PLANE PITCH DEGREES", "Aircraft Pitch", "degrees", "F1");
        AddSimReadout(v, "LJ35_BANK", "PLANE BANK DEGREES", "Aircraft Bank", "degrees", "F1");
        AddFlag(v, "LJ35_STBY_CAGED", "ATTITUDE CAGE", "Standby Gyro", "Free", "Caged", simvar: true);

        // ---- Davtron clock ----
        AddThreeWay(v, "LJ35_CLOCK_SET", "DAVTRON_SWITCH_SET", "Clock Set", "Up", "Set", "Day");
        AddThreeWay(v, "LJ35_CLOCK_DIM", "DAVTRON_SWITCH_DIM", "Clock Dim", "Bright", "Dim", "One hour up");
        AddThreeWay(v, "LJ35_CLOCK_FT", "DAVTRON_SWITCH_FT", "Clock Display", "Time", "Flight time", "Elapsed time");
        AddThreeWay(v, "LJ35_CLOCK_STOP", "DAVTRON_SWITCH_STOP", "Clock Timer", "Reset", "Stop", "Run");
        AddLVarState(v, "LJ35_CLOCK_24H", "DAVTRON_TWENTYFOUR_TWELVE", "Clock Format",
            new Dictionary<double, string> { [0] = "12 hour", [1] = "24 hour" });
        AddSimReadout(v, "LJ35_ZULU_H", "ZULU TIME", "Zulu Time", "seconds", "F0");
        AddSimReadout(v, "LJ35_LOCAL_H", "LOCAL TIME", "Local Time", "seconds", "F0");

        // ---- landing gear ----
        AddSimState(v, "LJ35_GEAR", "GEAR HANDLE POSITION", "Gear Lever",
            new Dictionary<double, string> { [0] = "Up", [1] = "Down" }, units: "bool");
        AddButton(v, "LJ35_EMER_GEAR", "GENERIC_LEAR_EMER_GEAR", "Emergency Gear Release", "Blows the gear down with emergency air.");
        AddThreeWay(v, "LJ35_GEAR_HORN", "LEAR_SW_GEAR_TEST", "Gear Horn", "Test", "Off", "Mute", "Test and Mute spring back.");
        AddSimReadout(v, "LJ35_GEAR_NOSE", "GEAR CENTER POSITION", "Nose Gear", "percent", "F0");
        AddSimReadout(v, "LJ35_GEAR_LEFT", "GEAR LEFT POSITION", "Left Gear", "percent", "F0");
        AddSimReadout(v, "LJ35_GEAR_RIGHT", "GEAR RIGHT POSITION", "Right Gear", "percent", "F0");
        AddFlag(v, "LJ35_EMER_GEAR_USED", "LEAR_EMER_GEAR_ON", "Emergency Gear", "Not used", "Blown down");
        AddFlag(v, "LJ35_GEAR_UNSAFE", "UNSAFE_LIGHT", "Gear Unsafe Light", "Out", "Lit");
        AddFlag(v, "LJ35_GEAR_HORN_MUTED", "GEAR_MUTE", "Gear Horn", "Sounding or off", "Muted");
        Cache(v, "LJ35_GEAR_NOSE"); Cache(v, "LJ35_GEAR_LEFT"); Cache(v, "LJ35_GEAR_RIGHT");

        // ---- copilot instruments ----
        AddTyped(v, "LJ35_ASI_BUG_R_SET", "Copilot Airspeed Bug", "knots", "0 to 400.", "LJ35_ASI_BUG_R");
        AddReadout(v, "LJ35_ASI_BUG_R", "GENERIC_LEAR_AIRSPEED_KNOB_R", "Copilot Airspeed Bug", "knots", "F0");
        AddSwitch(v, "LJ35_RMI_COPILOT_1", "RMI_BUTTON3_1", "Copilot RMI Needle 1", "VOR", "ADF");
        AddSwitch(v, "LJ35_RMI_COPILOT_2", "RMI_BUTTON4_1", "Copilot RMI Needle 2", "VOR", "ADF");
        AddSwitch(v, "LJ35_NAVGPS_COPILOT", "LEAR_NAVGPS_COPILOT", "Copilot NAV/GPS", "NAV", "GPS");
        AddRotary(v, "LJ35_DME2_SOURCE", "DME_MODE2", "DME 2 Source", DmeSource);
        AddRotary(v, "LJ35_DME2_DISPLAY", "DME_MODE2A", "DME 2 Display", DmeDisplay);
        AddRotary(v, "LJ35_HSI2_MODE", "HSI_MODE_SELECTOR_2", "Copilot HSI Readout", HsiMode);
        AddButton(v, "LJ35_DME_TEST_1", "GENERIC_DME_TEST_1", "DME 1 Test");
        AddButton(v, "LJ35_DME_TEST_2", "GENERIC_DME_TEST1_1", "DME 2 Test");
        AddButton(v, "LJ35_ALERTER_TEST", "GENERIC_LEAR_ALERTER_TEST", "Altitude Alerter Test");
        AddButton(v, "LJ35_RADALT_TEST", "GENERIC_RADAR_TEST_SW_1", "Radio Altimeter Test");
        AddSwitch(v, "LJ35_ELT", "ELT_TRANSMITTER", "ELT", "Armed", "Transmitting");
        AddSimReadout(v, "LJ35_ALT_IND_2", "INDICATED ALTITUDE:2", "Copilot Altimeter", "feet", "F0");
        AddSimReadout(v, "LJ35_HSI2_COURSE", "NAV OBS:2", "Copilot HSI Course", "degrees", "F0");
        AddSimReadout(v, "LJ35_DME2_NM", "NAV DME:2", "DME 2 Distance", "nautical miles", "F1");
        AddSimReadout(v, "LJ35_DME2_KT", "NAV DMESPEED:2", "DME 2 Ground Speed", "knots", "F0");
        AddSimReadout(v, "LJ35_ADF1_BRG", "ADF RADIAL:1", "ADF 1 Bearing", "degrees", "F0");
        AddSimReadout(v, "LJ35_ADF2_BRG", "ADF RADIAL:2", "ADF 2 Bearing", "degrees", "F0");
        AddSimReadout(v, "LJ35_VOR1_RADIAL", "NAV RADIAL:1", "VOR 1 Radial", "degrees", "F0");
        AddSimReadout(v, "LJ35_VOR2_RADIAL", "NAV RADIAL:2", "VOR 2 Radial", "degrees", "F0");
        Cache(v, "LJ35_ASI_BUG_R"); Cache(v, "LJ35_ALT_IND_2");
        return v;
    }

    private static readonly List<string> PilotInstrumentsControls = new()
    {
        "LJ35_ASI_BUG_L_SET", "LJ35_BARO_1_SET", "LJ35_BARO_1_STD", "LJ35_BARO_1_UNITS", "LJ35_NAVGPS_PILOT",
        "LJ35_RMI_PILOT_1", "LJ35_RMI_PILOT_2", "LJ35_DME1_SOURCE", "LJ35_DME1_DISPLAY", "LJ35_HSI1_MODE",
        "LJ35_DH_SET", "LJ35_MARKER_TEST", "LJ35_ADC_SELECT", "LJ35_GEAR_BRT"
    };
    private static readonly List<string> PilotInstrumentsDisplay = new()
    {
        "LJ35_IAS", "LJ35_MACH", "LJ35_ASI_BUG_L", "LJ35_ALT_IND_1", "LJ35_BARO_1_MB", "LJ35_BARO_1_HG", "LJ35_VSI",
        "LJ35_HDG", "LJ35_HSI_HDG", "LJ35_HSI_COURSE", "LJ35_HSI_CDI", "LJ35_HSI_GSI", "LJ35_HSI_TO_FROM",
        "LJ35_DME1_NM", "LJ35_DME1_KT", "LJ35_RADALT", "LJ35_DH", "LJ35_MKR_OUTER", "LJ35_MKR_MIDDLE", "LJ35_MKR_INNER", "LJ35_AOA"
    };
    private static readonly List<string> StandbyControls = new() { "LJ35_STBY_CAGE" };
    private static readonly List<string> StandbyDisplay = new()
    {
        "LJ35_STBY_CAGED", "LJ35_STBY_PITCH", "LJ35_STBY_BANK", "LJ35_PITCH", "LJ35_BANK"
    };
    private static readonly List<string> ClockControls = new()
    {
        "LJ35_CLOCK_FT", "LJ35_CLOCK_STOP", "LJ35_CLOCK_SET", "LJ35_CLOCK_DIM", "LJ35_CLOCK_24H"
    };
    private static readonly List<string> ClockDisplay = new() { "LJ35_ZULU_H", "LJ35_LOCAL_H" };
    private static readonly List<string> GearControls = new() { "LJ35_GEAR", "LJ35_EMER_GEAR", "LJ35_GEAR_HORN" };
    private static readonly List<string> GearDisplay = new()
    {
        "LJ35_GEAR_NOSE", "LJ35_GEAR_LEFT", "LJ35_GEAR_RIGHT", "LJ35_GEAR_UNSAFE", "LJ35_EMER_GEAR_USED", "LJ35_GEAR_HORN_MUTED"
    };
    private static readonly List<string> CopilotInstrumentsControls = new()
    {
        "LJ35_ASI_BUG_R_SET", "LJ35_BARO_2_SET", "LJ35_BARO_2_STD", "LJ35_BARO_2_UNITS", "LJ35_NAVGPS_COPILOT",
        "LJ35_RMI_COPILOT_1", "LJ35_RMI_COPILOT_2", "LJ35_DME2_SOURCE", "LJ35_DME2_DISPLAY", "LJ35_HSI2_MODE",
        "LJ35_DME_TEST_1", "LJ35_DME_TEST_2", "LJ35_ALERTER_TEST", "LJ35_RADALT_TEST", "LJ35_ELT"
    };
    private static readonly List<string> CopilotInstrumentsDisplay = new()
    {
        "LJ35_ASI_BUG_R", "LJ35_ALT_IND_2", "LJ35_BARO_2_MB", "LJ35_BARO_2_HG", "LJ35_HSI2_COURSE",
        "LJ35_DME2_NM", "LJ35_DME2_KT", "LJ35_VOR1_RADIAL", "LJ35_VOR2_RADIAL", "LJ35_ADF1_BRG", "LJ35_ADF2_BRG"
    };

    /// <summary>hPa or inHg by magnitude: anything under 40 is inches.</summary>
    private static double BaroToMillibars(double typed) => typed < 40 ? typed * 33.8639 : typed;

    private static bool HandlePilotPanelSet(string varKey, double value, SimConnectManager sc)
    {
        switch (varKey)
        {
            case "LJ35_ASI_BUG_L_SET": sc.SetLVar("GENERIC_LEAR_AIRSPEED_KNOB_L", Math.Clamp(Math.Round(value), 0, 400)); return true;
            case "LJ35_ASI_BUG_R_SET": sc.SetLVar("GENERIC_LEAR_AIRSPEED_KNOB_R", Math.Clamp(Math.Round(value), 0, 400)); return true;
            case "LJ35_RMI_PILOT_1": sc.SetLVar("GENERIC_RMI_BUTTON1_1", value); return true;
            case "LJ35_RMI_PILOT_2": sc.SetLVar("GENERIC_RMI_BUTTON2_1", value); return true;
            case "LJ35_RMI_COPILOT_1": sc.SetLVar("GENERIC_RMI_BUTTON3_1", value); return true;
            case "LJ35_RMI_COPILOT_2": sc.SetLVar("GENERIC_RMI_BUTTON4_1", value); return true;
            case "LJ35_NAVGPS_PILOT": ToggleTo(sc, "GPS DRIVES NAV1", "Bool", value > 0.5, "TOGGLE_GPS_DRIVES_NAV1"); return true;
            case "LJ35_NAVGPS_COPILOT": sc.SetLVar("GENERIC_LEAR_NAVGPS_COPILOT", value); return true;
            case "LJ35_DME1_SOURCE": sc.SetLVar("XMLVAR_DME_MODE1_Position", value); return true;
            case "LJ35_DME1_DISPLAY": sc.SetLVar("XMLVAR_DME_MODE1A_Position", value); return true;
            case "LJ35_DME2_SOURCE": sc.SetLVar("XMLVAR_DME_MODE2_Position", value); return true;
            case "LJ35_DME2_DISPLAY": sc.SetLVar("XMLVAR_DME_MODE2A_Position", value); return true;
            case "LJ35_HSI1_MODE": sc.SetLVar("XMLVAR_HSI_MODE_SELECTOR_1_Position", value); return true;
            case "LJ35_HSI2_MODE": sc.SetLVar("XMLVAR_HSI_MODE_SELECTOR_2_Position", value); return true;
            case "LJ35_DH_SET":
            {
                // The stock knob events step 10 ft; walk from the cached present value.
                double target = Math.Clamp(Math.Round(value / 10) * 10, 0, 2500);
                double now = ReadNow(sc, "LJ35_DH") ?? 0;
                int steps = (int)Math.Round((target - now) / 10);
                string ev = steps > 0 ? "INCREASE_DECISION_HEIGHT" : "DECREASE_DECISION_HEIGHT";
                for (int i = 0; i < Math.Abs(steps); i++) sc.SendEvent(ev);
                return true;
            }
            case "LJ35_MARKER_TEST": Pulse(sc, "GENERIC_LEAR_SW_MARKER_TEST_1", 1500); return true;
            case "LJ35_ADC_SELECT": sc.SetLVar("GENERIC_LEAR_ADC1_ADC2", value); return true;
            case "LJ35_GEAR_BRT": sc.SetLVar("XMLVAR_GEAR_BRT_KNOB_Position", value); return true;

            case "LJ35_BARO_1_SET": sc.ExecuteCalculatorCode($"{Rpn(BaroToMillibars(value) * 16)} 1 (>K:2:KOHLSMAN_SET)"); return true;
            case "LJ35_BARO_2_SET": sc.ExecuteCalculatorCode($"{Rpn(BaroToMillibars(value) * 16)} 2 (>K:2:KOHLSMAN_SET)"); return true;
            case "LJ35_BARO_1_STD": sc.ExecuteCalculatorCodeUnique("1 (>K:BAROMETRIC_STD_PRESSURE)"); return true;
            case "LJ35_BARO_2_STD": sc.ExecuteCalculatorCodeUnique("2 (>K:BAROMETRIC_STD_PRESSURE)"); return true;
            case "LJ35_BARO_1_UNITS": sc.SetLVar("ALTIMETER_BARO_SWAP_1_MODE", value); return true;
            case "LJ35_BARO_2_UNITS": sc.SetLVar("ALTIMETER_BARO_SWAP_2_MODE", value); return true;

            case "LJ35_STBY_CAGE": sc.ExecuteCalculatorCodeUnique("(>K:ATTITUDE_CAGE_BUTTON)"); return true;

            case "LJ35_CLOCK_SET": SetInputEvent(sc, "Momentary_DAVTRON_SWITCH_SET", value); return true;
            case "LJ35_CLOCK_DIM": SetInputEvent(sc, "Momentary_DAVTRON_SWITCH_DIM", value); return true;
            case "LJ35_CLOCK_FT": SetInputEvent(sc, "Momentary_DAVTRON_SWITCH_FT", value); return true;
            case "LJ35_CLOCK_STOP": SetInputEvent(sc, "Momentary_DAVTRON_SWITCH_STOP", value); return true;
            case "LJ35_CLOCK_24H": sc.SetLVar("DAVTRON_TWENTYFOUR_TWELVE", value); return true;

            case "LJ35_GEAR": sc.SendEvent(value > 0.5 ? "GEAR_DOWN" : "GEAR_UP"); return true;
            case "LJ35_EMER_GEAR": Pulse(sc, "GENERIC_LEAR_EMER_GEAR"); return true;
            case "LJ35_GEAR_HORN": sc.SetLVar("GENERIC_Momentary_LEAR_SW_GEAR_TEST", value); return true;

            case "LJ35_DME_TEST_1": Pulse(sc, "GENERIC_DME_TEST_1", 1500); return true;
            case "LJ35_DME_TEST_2": Pulse(sc, "GENERIC_DME_TEST1_1", 1500); return true;
            case "LJ35_ALERTER_TEST": Pulse(sc, "GENERIC_LEAR_ALERTER_TEST", 1500); return true;
            case "LJ35_RADALT_TEST": Pulse(sc, "GENERIC_RADAR_TEST_SW_1", 1500); return true;
            case "LJ35_ELT": sc.SetLVar("GENERIC_ELT_TRANSMITTER", value); return true;
        }
        return false;
    }

    private static string ClockText(double seconds)
    {
        int s = (int)Math.Round(seconds) % 86400;
        return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
    }
}
