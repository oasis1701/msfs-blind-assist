using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Simulation: the Crew Seat setting (which touchscreens the windows open on and which
/// altimeter the readouts use) and the vendor EFB's option switches — every one a plain
/// L:var the Settings page writes. Not here: the ACARS provider (a StoredData key, not an
/// L:var — the EFB window's Settings page owns it) and the ANC mode, whose enum order the EFB
/// page names (read it there).
/// </summary>
public partial class SkywardC680Definition
{
    private static Dictionary<string, SimVarDefinition> BuildSimulationVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();
        AddSelector(v, "C680_SEAT", "C680_SEAT", "Crew Seat", new[] { "Pilot", "Copilot" },
            "Which touchscreens the windows open on and which altimeter the readouts use. Saved between sessions.");
        v["C680_SEAT"].UpdateFrequency = UpdateFrequency.OnRequest;
        v["C680_SEAT"].IsAnnounced = false;

        AddSwitch(v, "C680_OPT_REALISTIC_PB", "SW_SOV_REALISTIC_PB", "Realistic Parking Brake");
        AddSwitch(v, "C680_OPT_SIMPLE_STATIC", "SW_SOV_CONFIG_Simple_Static", "Simple Static Clickspots");
        AddSwitch(v, "C680_OPT_SIMPLE_OPENINGS", "SW_SOV_CONFIG_Simple_Openings", "Simple Opening Clickspots");
        AddSwitch(v, "C680_OPT_ACS", "SW_SOV_CONFIG_ACS", "Advanced Camera System");
        AddSwitch(v, "C680_OPT_YOKE_HIDE", "SW_SOV_CONFIG_AUTO_YOKE_HIDE", "Auto Yoke Hiding");
        AddSwitch(v, "C680_OPT_YOKE_LOWER", "SW_SOV_CONFIG_YOKE_LOWER", "Lower Yoke");
        AddSwitch(v, "C680_OPT_GLASS", "SW_SOV_CONFIG_Glass", "Hide Display Glass");
        AddSwitch(v, "C680_OPT_PAUSE_TOD", "SW_SOV_PAUSE_AT_TOD", "Pause at Top of Descent");
        AddSwitch(v, "C680_OPT_SIMBRIEF", "SW_SOV_CONFIG_SIMBRIEF", "SimBrief Integration");
        AddSwitch(v, "C680_OPT_DYN_REG", "SW_SOV_CONFIG_Dynamic_Reg", "Dynamic Registration");
        AddSwitch(v, "C680_OPT_TESTKIT", "SW_SOV_Testkit_Installed", "Test Kit Installed");
        AddSwitch(v, "C680_OPT_CALKIT", "SW_SOV_Calibrationkit_Installed", "Calibration Kit Installed");
        AddSwitch(v, "C680_OPT_WIFI", "SW_SOV_Wifi_available", "Cabin WiFi");
        AddSwitch(v, "C680_OPT_EFB_BRT_AUTO", "SW_SOV_CONFIG_EFB_BRT_AUTO", "EFB Auto Brightness");
        AddKnob(v, "C680_OPT_EFB_BRT", "SW_SOV_CONFIG_EFB_BRT_MAN", "EFB Manual Brightness");
        AddSwitch(v, "C680_OPT_PAX_STYLE", "SW_SOV_PAX_VIP_Style", "Passenger Display Style", "Colours", "Logo");
        AddSwitch(v, "C680_OPT_PAX_DIST", "SW_SOV_PAX_VIP_UNIT_DST", "Passenger Display Distance", "Nautical miles", "Kilometres");
        AddSwitch(v, "C680_OPT_PAX_TEMP", "SW_SOV_PAX_VIP_UNIT_TEMP", "Passenger Display Temperature", "Celsius", "Fahrenheit");
        return v;
    }

    private static readonly List<string> SeatControls = new() { "C680_SEAT" };
    private static readonly List<string> EfbOptionsControls = new()
    {
        "C680_OPT_REALISTIC_PB", "C680_OPT_SIMPLE_STATIC", "C680_OPT_SIMPLE_OPENINGS", "C680_OPT_ACS", "C680_OPT_YOKE_HIDE", "C680_OPT_YOKE_LOWER",
        "C680_OPT_GLASS", "C680_OPT_PAUSE_TOD", "C680_OPT_SIMBRIEF", "C680_OPT_DYN_REG", "C680_OPT_TESTKIT", "C680_OPT_CALKIT", "C680_OPT_WIFI",
        "C680_OPT_EFB_BRT_AUTO", "C680_OPT_EFB_BRT", "C680_OPT_PAX_STYLE", "C680_OPT_PAX_DIST", "C680_OPT_PAX_TEMP"
    };

    private bool HandleSimulationSet(string varKey, double value, SimConnectManager sc)
    {
        if (varKey == "C680_SEAT")
        {
            Settings.SettingsManager.Current.C680CrewSeat = value > 0.5 ? 2 : 1;
            Settings.SettingsManager.Save();
            sc.RequestVariable("C680_SEAT", forceUpdate: true);
            return true;
        }
        if (varKey.StartsWith("C680_OPT_", StringComparison.Ordinal) && GetVariables().TryGetValue(varKey, out var def))
        {
            sc.SetLVar(def.Name, value);
            return true;
        }
        return false;
    }

    private bool TrySimulationDisplay(string varKey, out string displayText)
    {
        if (varKey == "C680_SEAT")
        {
            displayText = CurrentSeat == C680Seat.Side.Copilot ? "Copilot" : "Pilot";
            return true;
        }
        displayText = string.Empty;
        return false;
    }
}
