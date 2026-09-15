using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Avionics panels: the radio, transponder and baro readouts with direct-set entries beside
/// the two touchscreen windows, the FMS summary rows, and the display reversion buttons.
/// Stock SimVars and stock K: events throughout (the G5000 is the stock G3000 avionics).
/// Not here, and why: the ADF entry (the ADF_COMPLETE_SET BCD encoding is unverified; tune it
/// on the touchscreen), the minimums (no L:var in the package; the touchscreen page owns them)
/// the next-waypoint ident (a string SimVar the app cannot register), and the transponder MODE
/// (XPNDR_STATE_SET left TRANSPONDER STATE:1 unchanged live; the touchscreen page sets it).
/// </summary>
public partial class SkywardC680Definition
{
    private static Dictionary<string, SimVarDefinition> BuildAvionicsVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- Pilot Touchscreen (readouts + set entries; the window is Input Ctrl+Shift+R)
        AddSimReadout(v, "C680_COM1_ACT", "COM ACTIVE FREQUENCY:1", "COM 1 Active", "MHz", "F3");
        AddSimReadout(v, "C680_COM1_STBY", "COM STANDBY FREQUENCY:1", "COM 1 Standby", "MHz", "F3");
        AddSimReadout(v, "C680_COM2_ACT", "COM ACTIVE FREQUENCY:2", "COM 2 Active", "MHz", "F3");
        AddSimReadout(v, "C680_COM2_STBY", "COM STANDBY FREQUENCY:2", "COM 2 Standby", "MHz", "F3");
        AddSimReadout(v, "C680_NAV1_ACT", "NAV ACTIVE FREQUENCY:1", "NAV 1 Active", "MHz", "F2");
        AddSimReadout(v, "C680_NAV2_ACT", "NAV ACTIVE FREQUENCY:2", "NAV 2 Active", "MHz", "F2");
        AddSimReadout(v, "C680_ADF_ACT", "ADF ACTIVE FREQUENCY:1", "ADF Active kHz", "KHz", "F1");
        AddSimReadout(v, "C680_XPDR_CODE", "TRANSPONDER CODE:1", "Squawk", "BCO16", "F0");
        AddStateReadout(v, "C680_XPDR_STATE", "TRANSPONDER STATE:1", "Transponder Mode",
            new Dictionary<double, string> { [0] = "Off", [1] = "Standby", [2] = "Test", [3] = "On", [4] = "Alt", [5] = "Ground" }, simvar: true, units: "enum");
        AddSimReadout(v, "C680_BARO_1", "KOHLSMAN SETTING MB:1", "Pilot Baro", "millibars", "F0");
        AddSimReadout(v, "C680_BARO_2", "KOHLSMAN SETTING MB:2", "Copilot Baro", "millibars", "F0");
        AddTyped(v, "C680_COM1_STBY_SET", "COM 1 Standby", "MHz", "118.000 to 136.990", currentKey: "C680_COM1_STBY");
        AddTyped(v, "C680_COM2_STBY_SET", "COM 2 Standby", "MHz", "118.000 to 136.990", currentKey: "C680_COM2_STBY");
        AddButton(v, "C680_COM1_SWAP", "COM 1 Swap");
        AddButton(v, "C680_COM2_SWAP", "COM 2 Swap");
        AddTyped(v, "C680_NAV1_SET", "NAV 1 Active", "MHz", "108.00 to 117.95", currentKey: "C680_NAV1_ACT");
        AddTyped(v, "C680_NAV2_SET", "NAV 2 Active", "MHz", "108.00 to 117.95", currentKey: "C680_NAV2_ACT");
        AddTyped(v, "C680_XPDR_SET", "Squawk", "code", "0000 to 7777", currentKey: "C680_XPDR_CODE");
        AddTyped(v, "C680_BARO_SET", "Both Baros", "millibars or inHg", "1013 or 29.92 style; both PFDs", currentKey: "C680_BARO_1");
        AddButton(v, "C680_BARO_STD", "Both Baros to Standard");

        // ---- MFD Touchscreen (FMS summary rows; the window is Input Shift+M)
        AddSimReadout(v, "C680_FMS_DIST", "GPS WP DISTANCE", "Distance to Next Waypoint", "nautical miles", "F1");
        AddSimReadout(v, "C680_FMS_ETE", "GPS WP ETE", "Time to Next Waypoint", "seconds", "F0");
        AddSimReadout(v, "C680_FMS_DEST_ETE", "GPS ETE", "Time to Destination", "seconds", "F0");
        AddFlag(v, "C680_FMS_ACTIVE", "GPS IS ACTIVE FLIGHT PLAN", "Flight Plan", "None", "Active", simvar: true);
        AddSimReadout(v, "C680_FMS_GS", "GPS GROUND SPEED", "Ground Speed", "knots", "F0");

        // ---- Displays
        AddButton(v, "C680_DISPLAY_REV_L", "Left Display Reversion");
        AddButton(v, "C680_DISPLAY_REV_R", "Right Display Reversion");
        AddSelector(v, "C680_BARO_SYNC", "SW_SOV_CONFIG_Baro_Sync", "Baro Sync", new[] { "Off", "PFDs", "PFDs and Standby" });

        foreach (var k in new[] { "C680_COM1_ACT", "C680_COM1_STBY", "C680_COM2_ACT", "C680_COM2_STBY", "C680_NAV1_ACT", "C680_NAV2_ACT",
            "C680_XPDR_CODE", "C680_XPDR_STATE", "C680_BARO_1", "C680_BARO_2", "C680_FMS_DIST", "C680_FMS_ETE", "C680_FMS_DEST_ETE" })
            Cache(v, k);
        return v;
    }

    private static readonly List<string> PilotGtcControls = new() { "C680_COM1_STBY_SET", "C680_COM1_SWAP", "C680_COM2_STBY_SET", "C680_COM2_SWAP", "C680_NAV1_SET", "C680_NAV2_SET", "C680_XPDR_SET", "C680_BARO_SET", "C680_BARO_STD" };
    private static readonly List<string> PilotGtcDisplay = new() { "C680_COM1_ACT", "C680_COM1_STBY", "C680_COM2_ACT", "C680_COM2_STBY", "C680_NAV1_ACT", "C680_NAV2_ACT", "C680_ADF_ACT", "C680_XPDR_CODE", "C680_XPDR_STATE", "C680_BARO_1", "C680_BARO_2" };
    private static readonly List<string> MfdGtcControls = new();
    private static readonly List<string> MfdGtcDisplay = new() { "C680_FMS_ACTIVE", "C680_FMS_DIST", "C680_FMS_ETE", "C680_FMS_DEST_ETE", "C680_FMS_GS" };
    private static readonly List<string> DisplaysControls = new() { "C680_DISPLAY_REV_L", "C680_DISPLAY_REV_R", "C680_BARO_SYNC" };

    private bool HandleAvionicsSet(string varKey, double value, SimConnectManager sc)
    {
        switch (varKey)
        {
            case "C680_COM1_STBY_SET": sc.ExecuteCalculatorCode($"{Rpn(Math.Round(value * 1000) * 1000)} (>K:COM_STBY_RADIO_SET_HZ)"); return true;
            case "C680_COM2_STBY_SET": sc.ExecuteCalculatorCode($"{Rpn(Math.Round(value * 1000) * 1000)} (>K:COM2_STBY_RADIO_SET_HZ)"); return true;
            case "C680_COM1_SWAP": sc.ExecuteCalculatorCodeUnique("(>K:COM_STBY_RADIO_SWAP)"); return true;
            case "C680_COM2_SWAP": sc.ExecuteCalculatorCodeUnique("(>K:COM2_RADIO_SWAP)"); return true;
            case "C680_NAV1_SET": sc.ExecuteCalculatorCode($"{Rpn(Math.Round(value * 100) * 10000)} (>K:NAV1_RADIO_SET_HZ)"); return true;
            case "C680_NAV2_SET": sc.ExecuteCalculatorCode($"{Rpn(Math.Round(value * 100) * 10000)} (>K:NAV2_RADIO_SET_HZ)"); return true;
            case "C680_XPDR_SET":
            {
                int code = Math.Clamp((int)Math.Round(value), 0, 7777);
                uint bcd = Convert.ToUInt32(code.ToString("0000", System.Globalization.CultureInfo.InvariantCulture), 16);
                sc.ExecuteCalculatorCode($"{bcd} (>K:XPNDR_SET)");
                return true;
            }
            case "C680_BARO_SET":
            {
                double mb = value < 100 ? value * 33.8639 : value;           // 29.92-style entries are inHg
                string v16 = Rpn(Math.Round(mb * 16));
                sc.ExecuteCalculatorCode($"1 {v16} (>K:2:KOHLSMAN_SET) 2 {v16} (>K:2:KOHLSMAN_SET)"); // index, then value: the last pushed is the event value
                return true;
            }
            case "C680_BARO_STD": sc.ExecuteCalculatorCode("1 (>K:BAROMETRIC_STD_PRESSURE) 2 (>K:BAROMETRIC_STD_PRESSURE)"); return true;
            case "C680_DISPLAY_REV_L": Pulse(sc, "INSTRUMENT_Push_DisplayReverse_1", 300); return true;
            case "C680_DISPLAY_REV_R": Pulse(sc, "INSTRUMENT_Push_DisplayReverse_2", 300); return true;
            case "C680_BARO_SYNC": sc.SetLVar("SW_SOV_CONFIG_Baro_Sync", value); return true;
        }
        return false;
    }

    /// <summary>Renders the BCD squawk and the ETE seconds; false for anything else.</summary>
    private static bool TryAvionicsDisplay(string varKey, double value, out string displayText)
    {
        switch (varKey)
        {
            case "C680_XPDR_CODE":
                displayText = ((int)Math.Round(value)).ToString("X4");
                return true;
            case "C680_FMS_ETE":
            case "C680_FMS_DEST_ETE":
            {
                if (value <= 0) { displayText = "--"; return true; }
                var t = TimeSpan.FromSeconds(value);
                displayText = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}" : $"{t.Minutes} min";
                return true;
            }
        }
        displayText = string.Empty;
        return false;
    }
}
