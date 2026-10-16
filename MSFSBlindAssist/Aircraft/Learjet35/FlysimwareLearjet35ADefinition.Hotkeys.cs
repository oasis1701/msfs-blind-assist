using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// The Learjet's hotkeys, in jet terms. Output mode P is N1 and N2, E is N1 with fuel flow
/// and lever position, Shift+O is ITT and oil. Alt+N and Alt+M open the GNS 530 window,
/// Alt+P the GNS 430. Input mode Ctrl+A/H/S/V set the FC-530 through the same dialogs the
/// other aircraft use; Ctrl+B sets both altimeters.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private readonly Dictionary<string, Form> _windows = new(StringComparer.Ordinal);

    private void ShowWindow(string id, Func<Form> factory)
    {
        if (_windows.TryGetValue(id, out var existing) && !existing.IsDisposed)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var w = factory();
        _windows[id] = w;
        w.FormClosed += (_, _) => _windows.Remove(id);
        w.Show();
    }

    private static string N(double? v, string format = "0") => v == null ? "not read" : v.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    private bool HandleLj35Hotkey(HotkeyAction action, SimConnectManager sc, ScreenReaderAnnouncer ann,
        Form parent, HotkeyManager hotkeys)
    {
        switch (action)
        {
            case HotkeyAction.ReadAltimeter:
                ann.AnnounceImmediate(
                    $"Pilot altimeter {N(ReadNow(sc, "LJ35_BARO_1_MB"))} hectopascals, {N(ReadNow(sc, "LJ35_BARO_1_HG"), "0.00")} inches. " +
                    $"Copilot {N(ReadNow(sc, "LJ35_BARO_2_MB"))} hectopascals, {N(ReadNow(sc, "LJ35_BARO_2_HG"), "0.00")} inches.");
                return true;

            case HotkeyAction.ReadAltitude: RequestFCUAltitude(sc, ann); return true;
            case HotkeyAction.ReadHeading: RequestFCUHeading(sc, ann); return true;
            case HotkeyAction.ReadSpeed:
                ann.AnnounceImmediate(
                    (ReadNow(sc, "LJ35_AP_SPD_UNIT") ?? 0) > 0.5
                        ? $"Mach target {N(ReadNow(sc, "LJ35_AP_MACH_VAR"), "0.00")}"
                        : $"Speed target {N(ReadNow(sc, "LJ35_AP_IAS_VAR"))} knots");
                return true;
            // V is what the aeroplane is DOING (MainForm's generic vertical-speed readout, so
            // it is deliberately not handled here); Shift+V is the FC-530's target.
            case HotkeyAction.ReadFCUVerticalSpeedFPA: RequestFCUVerticalSpeed(sc, ann); return true;

            // ---- characteristic speeds (Shift+1..6): the Airbus set has no meaning here, so
            // the keys carry the 35A's — see Lj35Speeds for which are computed at weight and
            // which are published limitations.
            case HotkeyAction.ReadSpeedGD: ann.AnnounceImmediate(Lj35Speeds.ComposeTakeoff(ReadNow(sc, "LJ35_TOTAL_WEIGHT"))); return true;
            case HotkeyAction.ReadSpeedS: ann.AnnounceImmediate(Lj35Speeds.ComposeBarberPole()); return true;
            case HotkeyAction.ReadSpeedF: ann.AnnounceImmediate(Lj35Speeds.ComposeFlapLimits()); return true;
            case HotkeyAction.ReadSpeedVLS: ann.AnnounceImmediate(Lj35Speeds.ComposeVref(ReadNow(sc, "LJ35_TOTAL_WEIGHT"))); return true;
            case HotkeyAction.ReadSpeedVS: ann.AnnounceImmediate(Lj35Speeds.ComposeStall(ReadNow(sc, "LJ35_TOTAL_WEIGHT"))); return true;
            case HotkeyAction.ReadSpeedVFE: ann.AnnounceImmediate(Lj35Speeds.ComposeGearLimits()); return true;

            // ---- the GNS's flight plan, off the stock GPS SimVars the Working Title unit
            // writes (D, Shift+D, Ctrl+W). Read from the standing one-second frame, never
            // re-requested: see SimConnectManager.LastGpsWaypoint.
            case HotkeyAction.ReadDistanceToDest:
            {
                var last = sc.LastGpsWaypoint;
                ann.AnnounceImmediate(last == null
                    ? "Destination information not available yet."
                    : Services.GpsWaypointSequencer.ComposeDestination(last.Value));
                return true;
            }
            case HotkeyAction.ReadNDWaypoint:
            {
                var last = sc.LastGpsWaypoint;
                ann.AnnounceImmediate(last == null
                    ? "Waypoint information not available yet."
                    : Services.GpsWaypointSequencer.ComposeReadout(Services.GpsWaypointSequencer.Read(last.Value, null)));
                return true;
            }
            case HotkeyAction.ReadDistanceToTOD:
            {
                var last = sc.LastGpsWaypoint;
                double? routeNm = null;
                if (last != null && last.Value.IsActiveFlightPlan > 0.5 && last.Value.RouteEteSeconds >= 1 && last.Value.GroundSpeedKnots > 1)
                    routeNm = last.Value.RouteEteSeconds * last.Value.GroundSpeedKnots / 3600.0;
                double alt = ReadNow(sc, "LJ35_ALT_MSL") ?? 0;
                ann.AnnounceImmediate(Lj35Descent.ComposeTopOfDescent(alt, ReadNow(sc, "LJ35_GPS_TARGET_ALT"), routeNm));
                return true;
            }

            case HotkeyAction.ReadEngineRpm:
                ann.AnnounceImmediate(
                    $"N1 {N(ReadNow(sc, "LJ35_N1_L"), "0.0")} and {N(ReadNow(sc, "LJ35_N1_R"), "0.0")}. " +
                    $"N2 {N(ReadNow(sc, "LJ35_N2_L"), "0.0")} and {N(ReadNow(sc, "LJ35_N2_R"), "0.0")} percent.");
                return true;
            case HotkeyAction.ReadEnginePower:
                ann.AnnounceImmediate(
                    $"N1 {N(ReadNow(sc, "LJ35_N1_L"), "0.0")} and {N(ReadNow(sc, "LJ35_N1_R"), "0.0")} percent. " +
                    $"Fuel flow {N(ReadNow(sc, "LJ35_FF_L"))} and {N(ReadNow(sc, "LJ35_FF_R"))} pounds per hour. " +
                    $"Levers {N(ReadNow(sc, "LJ35_THR_L"))} and {N(ReadNow(sc, "LJ35_THR_R"))} percent.");
                return true;
            case HotkeyAction.ReadEngineTemps:
                ann.AnnounceImmediate(
                    $"ITT {N(ReadNow(sc, "LJ35_ITT_L"))} and {N(ReadNow(sc, "LJ35_ITT_R"))} degrees. " +
                    $"Oil pressure {N(ReadNow(sc, "LJ35_OIL_P_L"))} and {N(ReadNow(sc, "LJ35_OIL_P_R"))} psi. " +
                    $"Oil temperature {N(ReadNow(sc, "LJ35_OIL_T_L"))} and {N(ReadNow(sc, "LJ35_OIL_T_R"))} degrees.");
                return true;

            case HotkeyAction.ReadFlaps:
            {
                double l = ReadNow(sc, "LJ35_FLAP_L") ?? 0, r = ReadNow(sc, "LJ35_FLAP_R") ?? 0;
                ann.AnnounceImmediate(Math.Abs(l - r) > 2
                    ? $"Flaps asymmetric: left {l:0}, right {r:0} degrees"
                    : $"Flaps {l:0} degrees");
                return true;
            }
            case HotkeyAction.ReadGear:
            {
                double n = ReadNow(sc, "LJ35_GEAR_NOSE") ?? 0, l = ReadNow(sc, "LJ35_GEAR_LEFT") ?? 0, r = ReadNow(sc, "LJ35_GEAR_RIGHT") ?? 0;
                ann.AnnounceImmediate(n >= 99 && l >= 99 && r >= 99 ? "Gear down, three green"
                    : n <= 1 && l <= 1 && r <= 1 ? "Gear up"
                    : $"Gear in transit: nose {n:0}, left {l:0}, right {r:0} percent");
                return true;
            }
            case HotkeyAction.ReadFuelQuantity:
            case HotkeyAction.ReadFuelInfo:
                ann.AnnounceImmediate(
                    $"Total {Lj35Fuel.Describe(ReadNow(sc, "LJ35_FUEL_TOTAL") ?? 0)}. " +
                    $"Wings {N(ReadNow(sc, "LJ35_FUEL_WING_L"))} and {N(ReadNow(sc, "LJ35_FUEL_WING_R"))}, " +
                    $"tips {N(ReadNow(sc, "LJ35_FUEL_TIP_L"))} and {N(ReadNow(sc, "LJ35_FUEL_TIP_R"))}, " +
                    $"fuselage {N(ReadNow(sc, "LJ35_FUEL_FUS"))} pounds. " +
                    Lj35Fuel.TransferAdvice(ReadNow(sc, "LJ35_FUEL_TIP_L") ?? 0, ReadNow(sc, "LJ35_FUEL_TIP_R") ?? 0, ReadNow(sc, "LJ35_FUEL_FUS") ?? 0) + ".");
                return true;

            case HotkeyAction.ReadDisplayND:
            case HotkeyAction.ReadDisplayMFD:
                hotkeys?.ExitOutputHotkeyMode();
                ShowWindow("GNS530", () => new Forms.Learjet35.Lj35GnsDisplayForm("GNS 530", "AS530", "AS530", ann));
                return true;
            case HotkeyAction.ReadDisplayPFD:
                hotkeys?.ExitOutputHotkeyMode();
                ShowWindow("GNS430", () => new Forms.Learjet35.Lj35GnsDisplayForm("GNS 430", "AS430", "AS430", ann));
                return true;

            case HotkeyAction.FCUSetBaro:
                hotkeys?.ExitInputHotkeyMode();
                return ShowBaroDialog(sc, ann, parent);

            case HotkeyAction.FCUSetAltitude:
            case HotkeyAction.FCUSetSpeed:
            case HotkeyAction.FCUSetHeading:
            case HotkeyAction.FCUSetVS:
                hotkeys?.ExitInputHotkeyMode();
                return ShowApValueDialog(action, sc, ann, parent);
            case HotkeyAction.FCUSetAutopilot:
                hotkeys?.ExitInputHotkeyMode();
                return ShowApButtonsDialog(sc, ann, parent);
        }
        return false;
    }

    private static string ModeState(SimConnectManager sc, string key)
        => (sc.GetCachedVariableValue(key) ?? 0) > 0.5 ? "On" : "Off";

    private ToggleButtonDef ModeToggle(string label, string key, SimConnectManager sc)
        => new(label, () => ModeState(sc, key), () =>
        {
            bool on = (sc.GetCachedVariableValue(key) ?? 0) > 0.5;
            HandleAutopilotSet(key, on ? 0 : 1, sc);
        });

    private bool ShowApValueDialog(HotkeyAction action, SimConnectManager sc, ScreenReaderAnnouncer ann, Form parent)
    {
        if (!sc.IsConnected) { ann.AnnounceImmediate("Not connected to simulator."); return true; }
        string title, kind, range, setKey;
        double lo, hi;
        List<ToggleButtonDef> toggles;
        switch (action)
        {
            case HotkeyAction.FCUSetAltitude:
                title = "Altitude Alerter"; kind = "altitude"; range = "0 to 99900 feet, whole hundreds";
                setKey = "LJ35_AP_PRESELECT_SET"; lo = 0; hi = 99900;
                toggles = new() { ModeToggle("Altitude &select (arm capture)", "LJ35_AP_ALT_SEL", sc), ModeToggle("Altitude &hold", "LJ35_AP_ALT_HLD", sc) };
                break;
            case HotkeyAction.FCUSetSpeed:
                title = "Speed Target"; kind = "airspeed"; range = "80 to 350 knots";
                setKey = "LJ35_AP_IAS_SET"; lo = 80; hi = 350;
                toggles = new() { ModeToggle("&Speed mode", "LJ35_AP_SPD", sc), ModeToggle("&Mach units", "LJ35_AP_SPD_UNIT", sc) };
                break;
            case HotkeyAction.FCUSetHeading:
                title = "Heading Bug"; kind = "heading"; range = "0 to 359 degrees";
                setKey = "LJ35_AP_HDG_SET"; lo = 0; hi = 359;
                toggles = new() { ModeToggle("&Heading mode", "LJ35_AP_HDG", sc), ModeToggle("&NAV mode", "LJ35_AP_NAV", sc), ModeToggle("&Approach mode", "LJ35_AP_APR", sc) };
                break;
            case HotkeyAction.FCUSetVS:
                title = "Vertical Speed Target"; kind = "vertical speed"; range = "minus 6000 to 6000 feet per minute";
                setKey = "LJ35_AP_VS_SET"; lo = -6000; hi = 6000;
                toggles = new() { ModeToggle("&Vertical speed mode", "LJ35_AP_VS", sc), ModeToggle("Altitude &hold", "LJ35_AP_ALT_HLD", sc) };
                break;
            default: return false;
        }

        var dialog = new ValueInputForm(title, kind, range, ann,
            input => double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= lo && v <= hi
                ? (true, "") : (false, $"Enter a value between {lo:0} and {hi:0}"),
            toggles,
            input =>
            {
                if (double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    HandleAutopilotSet(setKey, v, sc);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parent);
        return true;
    }

    private bool ShowApButtonsDialog(SimConnectManager sc, ScreenReaderAnnouncer ann, Form parent)
    {
        if (!sc.IsConnected) { ann.AnnounceImmediate("Not connected to simulator."); return true; }
        var toggles = new List<ToggleButtonDef>
        {
            ModeToggle("&Engage autopilot", "LJ35_AP_ENG", sc),
            ModeToggle("&Heading", "LJ35_AP_HDG", sc),
            ModeToggle("&NAV", "LJ35_AP_NAV", sc),
            ModeToggle("&Approach", "LJ35_AP_APR", sc),
            ModeToggle("&Back course", "LJ35_AP_BC", sc),
            ModeToggle("&Wing level", "LJ35_AP_LVL", sc),
            ModeToggle("&Vertical speed", "LJ35_AP_VS", sc),
            ModeToggle("Altitude ho&ld", "LJ35_AP_ALT_HLD", sc),
            ModeToggle("Altitude &select", "LJ35_AP_ALT_SEL", sc),
            ModeToggle("&Speed", "LJ35_AP_SPD", sc),
        };
        var dialog = new ValueInputForm("FC-530 Autopilot", "mode", "the mode buttons", ann, _ => (false, "Use the buttons."), toggles, null);
        dialog.ShowCancelButton = false;
        dialog.Show(parent);
        return true;
    }

    private bool ShowBaroDialog(SimConnectManager sc, ScreenReaderAnnouncer ann, Form parent)
    {
        if (!sc.IsConnected) { ann.AnnounceImmediate("Not connected to simulator."); return true; }
        var dialog = new ValueInputForm("Altimeters", "altimeter setting", "hectopascals such as 1013, or inches such as 29.92", ann,
            input => double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 && v < 1100
                ? (true, "") : (false, "Enter hectopascals or inches"),
            new List<ToggleButtonDef>
            {
                new("Both to &standard", () => "", () => { HandleTypedBaro("LJ35_BARO_1_STD", 1, sc); HandleTypedBaro("LJ35_BARO_2_STD", 1, sc); })
            },
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return;
                HandleTypedBaro("LJ35_BARO_1_SET", v, sc);
                HandleTypedBaro("LJ35_BARO_2_SET", v, sc);
                ann.Announce($"Both altimeters {(v < 40 ? v.ToString("0.00") + " inches" : v.ToString("0") + " hectopascals")}");
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parent);
        return true;
    }

    private static void HandleTypedBaro(string key, double value, SimConnectManager sc) => HandlePilotPanelSet(key, value, sc);
}
