using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// THE GFC 700 FROM THE KEYBOARD - input mode's Ctrl+A, S, H, V and P.
///
/// Every other aircraft in this app answers these five and the DA40 answered NONE of them.
/// The actions are app-wide (FCUSetAltitude and friends), so from the outside they LOOKED
/// implemented; a hotkey action existing says nothing about whether an aircraft handles it,
/// which is the same trap that had Ctrl+W reported as working on this aeroplane while doing
/// nothing at all. Only FCUSetBaro was ever wired.
///
/// ⚠️ ALTITUDE DOES NOT GO THROUGH AP_ALT_VAR_SET_ENGLISH. That event is an INCREMENT and
/// ignores its parameter - measured live with the preselect at 0, where
/// "5000 (>K:AP_ALT_VAR_SET_ENGLISH)" left it reading 100. The value setter writes the
/// SimVar directly instead, and these dialogs go through that same setter rather than
/// carrying a second copy of the write.
///
/// ⚠️ THE TOGGLES DO NOT PRETEND THE AEROPLANE IS SIMPLER THAN IT IS. Three things were
/// measured in flight and all three are visible here rather than smoothed over: a vertical
/// mode CAPTURES THE VALUE IT IS ENGAGED AT and discards a pre-set one, so the value box is
/// applied before any mode button; ALT HOLD holds where you ARE rather than the preselect,
/// so it is labelled for what it does; and NAV does not release HDG, so both can read
/// engaged at once with HDG flying. None of that is explained to the pilot in speech - the
/// app reports the aeroplane, it does not coach - it is simply not hidden.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>Reads a mode's engaged state for a toggle's label.</summary>
    private static string ModeState(SimConnectManager sc, string key)
        => (sc.GetCachedVariableValue(key) ?? 0) > 0.5 ? "Engaged" : "Off";

    /// <summary>
    /// One autopilot mode button. Goes through HandleAutopilotSet so the distinct ON/OFF
    /// event rule and the uniquifying calc wrapper apply here exactly as on the panel - a
    /// second writer would be a second place for both of those to be forgotten.
    /// </summary>
    private ToggleButtonDef ModeToggle(string label, string key,
        SimConnectManager sc, ScreenReaderAnnouncer ann)
        => new(label, () => ModeState(sc, key), () =>
        {
            bool on = (sc.GetCachedVariableValue(key) ?? 0) > 0.5;
            HandleAutopilotSet(key, on ? 0 : 1, sc, ann);
        });

    private bool ShowApValueDialog(HotkeyAction action, SimConnectManager sc,
        ScreenReaderAnnouncer ann, Form parent, HotkeyManager hotkeys)
    {
        if (!sc.IsConnected) { ann.AnnounceImmediate("Not connected to simulator."); return true; }
        hotkeys.ExitInputHotkeyMode();

        string title, kind, range, setKey;
        double lo, hi;
        List<ToggleButtonDef> toggles;

        switch (action)
        {
            case HotkeyAction.FCUSetAltitude:
                title = "Selected Altitude"; kind = "altitude"; range = "0 to 18000 feet";
                setKey = "DA40_AP_ALT_SET"; lo = 0; hi = 18000;
                toggles = new()
                {
                    // Named for what it DOES: ALT captures the altitude the aeroplane is AT,
                    // not the one in the box - measured in flight, where engaging it at
                    // 300 ft held 300 with 2000 selected.
                    ModeToggle("Hold &current altitude", "DA40_AP_ALT", sc, ann),
                    ModeToggle("&Flight level change", "DA40_AP_FLC", sc, ann),
                    ModeToggle("&Vertical speed", "DA40_AP_VS", sc, ann)
                };
                break;

            case HotkeyAction.FCUSetSpeed:
                title = "Selected Airspeed"; kind = "airspeed"; range = "60 to 172 knots";
                setKey = "DA40_AP_IAS_SET"; lo = 60; hi = 172;
                toggles = new() { ModeToggle("&Flight level change", "DA40_AP_FLC", sc, ann) };
                break;

            case HotkeyAction.FCUSetHeading:
                title = "Heading Bug"; kind = "heading"; range = "0 to 359 degrees";
                setKey = "DA40_AP_HDG_SET"; lo = 0; hi = 359;
                toggles = new()
                {
                    ModeToggle("&Heading mode", "DA40_AP_HDG", sc, ann),
                    // Selecting NAV does NOT release HDG - measured; both read engaged and
                    // HDG flies. Both buttons are here so a pilot can see that and release
                    // HDG themselves.
                    ModeToggle("&Navigation mode", "DA40_AP_NAV", sc, ann)
                };
                break;

            case HotkeyAction.FCUSetVS:
                title = "Selected Vertical Speed"; kind = "vertical speed";
                range = "minus 2000 to 2000 feet per minute";
                setKey = "DA40_AP_VS_SET"; lo = -2000; hi = 2000;
                toggles = new()
                {
                    ModeToggle("&Vertical speed mode", "DA40_AP_VS", sc, ann),
                    ModeToggle("Hold &current altitude", "DA40_AP_ALT", sc, ann)
                };
                break;

            default:
                return false;
        }

        var dialog = new ValueInputForm(title, kind, range, ann,
            input =>
            {
                if (double.TryParse(input, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v)
                    && v >= lo && v <= hi)
                    return (true, "");
                return (false, $"Enter a value between {lo:0} and {hi:0}");
            },
            toggles,
            input =>
            {
                if (double.TryParse(input, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                    HandleAutopilotSet(setKey, v, sc, ann);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parent);
        return true;
    }

    /// <summary>
    /// Ctrl+P - every GFC 700 button in one place, with no value box. The mode buttons are
    /// the whole content, so a numeric field would be a field for nothing.
    /// </summary>
    private bool ShowApButtonsDialog(SimConnectManager sc, ScreenReaderAnnouncer ann,
        Form parent, HotkeyManager hotkeys)
    {
        if (!sc.IsConnected) { ann.AnnounceImmediate("Not connected to simulator."); return true; }
        hotkeys.ExitInputHotkeyMode();

        var toggles = new List<ToggleButtonDef>
        {
            ModeToggle("&Autopilot", "DA40_AP_MASTER", sc, ann),
            ModeToggle("Flight &director", "DA40_AP_FD", sc, ann),
            ModeToggle("&Heading mode", "DA40_AP_HDG", sc, ann),
            ModeToggle("&Navigation mode", "DA40_AP_NAV", sc, ann),
            ModeToggle("A&pproach mode", "DA40_AP_APR", sc, ann),
            ModeToggle("&Backcourse mode", "DA40_AP_BC", sc, ann),
            ModeToggle("Hold current a&ltitude", "DA40_AP_ALT", sc, ann),
            ModeToggle("&Vertical speed mode", "DA40_AP_VS", sc, ann),
            ModeToggle("&Flight level change", "DA40_AP_FLC", sc, ann)
        };

        // The validator refuses everything: there is no value to type here, and a field that
        // silently accepts input it will never use is worse than one that says so.
        var dialog = new ValueInputForm("Autopilot", "nothing", "use the buttons", ann,
            _ => (false, "This window has no value to set. Use the buttons."),
            toggles, null);

        dialog.ShowCancelButton = false;
        dialog.Show(parent);
        return true;
    }
}
