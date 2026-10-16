using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Cessna 172 Skyhawk with Garmin G1000 glass cockpit and its GFC 700 autopilot.
///
/// EVERY SWITCH IS WRITTEN THROUGH ITS COCKPIT INPUT EVENT, WITH AN EXPLICIT VALUE. Measured live on
/// MSFS 2024's stock C172 G1000 (2026-10-16, docs/c172.md): the toggles the base class sends turned a
/// misread into a switch moved the wrong way, and several stock events and SimVars do not reach this
/// cockpit at all. `TOGGLE_MASTER_BATTERY` with no index flips BOTH batteries; `AVIONICS_MASTER_n_SET`
/// moves only `AVIONICS MASTER SWITCH:n` and leaves the avionics switches and buses where they were;
/// `FUEL_SELECTOR_SET` does nothing. Setting the input event is what the cockpit switch itself does.
///
/// THE READS ARE WHAT THE SWITCHES DRIVE: the main battery is index 2 (index 1 is the standby, the
/// reverse of what the port assumed), the fuel pump, valve and selector are the new fuel system's
/// (`FUELSYSTEM ...`), pitot heat is `PITOT HEAT SWITCH:1`. Three switches drive nothing a SimVar
/// reports (the avionics buses and the standby battery), and the magneto key has no SimVar for its
/// position, so those four are read from the input event itself (<see cref="SimVarType.InputEvent"/>).
///
/// THE GFC 700 KEYS ARE PRESSED, NOT EMULATED: one set of `AS1000_AUTOPILOT_&lt;KEY&gt;_PFD` to 1 is one
/// press of the bezel key, so every mode behaves as the real key does (AP engages in ROL and PIT, FD is
/// refused while the AP flies, FLC is refused until an altitude has been selected).
/// </summary>
public class CessnaC172G1000Definition : CessnaC172BaseDefinition
{
    public override string AircraftName => "Cessna 172 Skyhawk (G1000)";
    public override string AircraftCode => "C172_G1000";

    private const string AutopilotPanel = "GFC 700";

    /// <summary>Panel switches and the cockpit input event each one sets, with the combo's value.</summary>
    internal static readonly IReadOnlyDictionary<string, string> SwitchInputEvents = new Dictionary<string, string>
    {
        ["C172_BATTERY_STATE"] = "ELECTRICAL_BATTERY_1",
        ["C172G_STBY_BATTERY_STATE"] = "ELECTRICAL_BATTERY_STBY_2",
        ["C172_ALTERNATOR_STATE"] = "ELECTRICAL_ALTERNATOR_1",
        ["C172_AVIONICS1_STATE"] = "ELECTRICAL_LINE_BUS_1_TO_AVIONICS_BUS_1",
        ["C172_AVIONICS2_STATE"] = "ELECTRICAL_LINE_BUS_2_TO_AVIONICS_BUS_2",
        ["C172_MAGNETO"] = "ENGINE_MAGNETO_1",
        ["C172_FUEL_PUMP_STATE"] = "FUEL_PUMP_1",
        ["C172_FUEL_VALVE_STATE"] = "FUEL_VALVE_SHUTOFFVALVE",
        ["C172_NAV_LIGHT_STATE"] = "LIGHTING_NAV_0",
        ["C172_BEACON_STATE"] = "LIGHTING_BEACON_1",
        ["C172_STROBE_STATE"] = "LIGHTING_STROBE_0",
        ["C172_LANDING_LIGHT_STATE"] = "LIGHTING_LANDING_1",
        ["C172_TAXI_LIGHT_STATE"] = "LIGHTING_TAXI_1",
        ["C172_PITOT_HEAT_STATE"] = "DEICE_PITOT_1",
        ["C172_PARKING_BRAKE_STATE"] = "LANDING_GEAR_PARKINGBRAKE",
        ["C172G_ALT_STATIC_STATE"] = "SAFETY_ALTERNATE_STATIC_AIR",
    };

    /// <summary>GFC 700 panel buttons and the bezel key each one presses.</summary>
    internal static readonly IReadOnlyDictionary<string, string> AutopilotKeys = new Dictionary<string, string>
    {
        ["C172_AP_MASTER_STATE"] = "AS1000_AUTOPILOT_AP_PFD",
        ["C172G_AP_FD_STATE"] = "AS1000_AUTOPILOT_FD_PFD",
        ["C172_AP_HDG_STATE"] = "AS1000_AUTOPILOT_HEADING_PFD",
        ["C172_AP_ALT_STATE"] = "AS1000_AUTOPILOT_ALTITUDE_PFD",
        ["C172_AP_NAV_STATE"] = "AS1000_AUTOPILOT_NAVIGATION_PFD",
        ["C172G_AP_VNV"] = "AS1000_AUTOPILOT_VERTICAL_NAVIGATION_PFD",
        ["C172_AP_APR_STATE"] = "AS1000_AUTOPILOT_APPROACH_PFD",
        ["C172_AP_BC_STATE"] = "AS1000_AUTOPILOT_BACKCOURSE_PFD",
        ["C172_AP_VS_STATE"] = "AS1000_AUTOPILOT_VERTICALSPEED_PFD",
        ["C172G_AP_FLC_STATE"] = "AS1000_AUTOPILOT_FLIGHTLEVELCHANGE_PFD",
        ["C172G_AP_NOSE_UP"] = "AS1000_AUTOPILOT_VERTICALSPEED_UP_PFD",
        ["C172G_AP_NOSE_DN"] = "AS1000_AUTOPILOT_VERTICALSPEED_DOWN_PFD",
    };

    /// <summary>Walkaround covers and the input event that toggles each one.</summary>
    internal static readonly IReadOnlyDictionary<string, string> CoverInputEvents = new Dictionary<string, string>
    {
        ["C172G_COVER_ENGINE"] = "UNKNOWN_COVER_ENGINE",
        ["C172G_COVER_PITOT"] = "UNKNOWN_COVER_PITOT",
        ["C172G_COVER_STATIC"] = "UNKNOWN_COVER_STATIC_PORT",
    };

    /// <summary>The autopilot hotkeys and the key each presses (the base class sends untested K: events).</summary>
    private static readonly IReadOnlyDictionary<HotkeyAction, string> AutopilotHotkeys = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.ToggleAutopilot1] = "C172_AP_MASTER_STATE",
        [HotkeyAction.FCUHeadingPush] = "C172_AP_HDG_STATE",
        [HotkeyAction.FCUSpeedPush] = "C172G_AP_FLC_STATE",
        [HotkeyAction.ToggleApproachMode] = "C172_AP_APR_STATE",
    };

    protected override Dictionary<string, SimVarDefinition> GetC172Variables()
    {
        var v = base.GetC172Variables();

        // ---------- Electrical ----------
        v["C172_BATTERY_STATE"].Name = "ELECTRICAL MASTER BATTERY:2";
        v["C172G_STBY_BATTERY_STATE"] = InputEventState("ELECTRICAL_BATTERY_STBY_2", "Standby Battery",
            new() { [0] = "Arm", [1] = "Off" });
        v["C172_AVIONICS1_STATE"] = InputEventState("ELECTRICAL_LINE_BUS_1_TO_AVIONICS_BUS_1", "Avionics Bus 1",
            new() { [0] = "Off", [1] = "On" });
        v["C172_AVIONICS2_STATE"] = InputEventState("ELECTRICAL_LINE_BUS_2_TO_AVIONICS_BUS_2", "Avionics Bus 2",
            new() { [0] = "Off", [1] = "On" });

        // ---------- Engine and fuel ----------
        v["C172_MAGNETO"] = InputEventState("ENGINE_MAGNETO_1", "Magneto",
            new() { [0] = "Off", [1] = "Right", [2] = "Left", [3] = "Both" });
        v["C172_FUEL_PUMP_STATE"].Name = "FUELSYSTEM PUMP SWITCH:1";
        v["C172_FUEL_VALVE_STATE"].Name = "FUELSYSTEM VALVE SWITCH:1";
        v["C172_FUEL_SELECTOR"] = new SimVarDefinition
        {
            Name = "FUELSYSTEM JUNCTION SETTING:1",
            DisplayName = "Fuel Selector",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = FuelSelectorPositions()
        };

        // ---------- Lights ----------
        // The SWITCH, not the lamp: `LIGHT NAV ON` is 1 only while the lamp is lit, so with the
        // battery off a switched-on light read Off (measured); `LIGHT NAV` follows the switch.
        v["C172_NAV_LIGHT_STATE"].Name = "LIGHT NAV";
        v["C172_BEACON_STATE"].Name = "LIGHT BEACON";
        v["C172_STROBE_STATE"].Name = "LIGHT STROBE";
        v["C172_LANDING_LIGHT_STATE"].Name = "LIGHT LANDING";
        v["C172_TAXI_LIGHT_STATE"].Name = "LIGHT TAXI";

        // ---------- Anti-ice and instruments ----------
        v["C172_PITOT_HEAT_STATE"].Name = "PITOT HEAT SWITCH:1";
        v["C172G_ALT_STATIC_STATE"] = new SimVarDefinition
        {
            Name = "ALTERNATE STATIC SOURCE OPEN",
            DisplayName = "Alternate Static Air",
            Type = SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        };

        // ---------- Covers ----------
        // MSFS 2024 can park the aeroplane with its walkaround covers on, and the engine does not
        // run with its cover in: the sim's own autostart cranked for ten seconds with no fuel flow
        // (2026-10-16). Each cover's input event is a press that toggles it; COVER ON:n reads it
        // (engine 1, pitot 2, static port 3, measured one press at a time). Continuous and announced,
        // so the Starter can warn from the cache and a cover moved outside MSFSBA is spoken.
        v["C172G_COVER_ENGINE"] = Cover("COVER ON:1", "Engine Cover");
        v["C172G_COVER_PITOT"] = Cover("COVER ON:2", "Pitot Cover");
        v["C172G_COVER_STATIC"] = Cover("COVER ON:3", "Static Port Cover");

        // ---------- GFC 700 ----------
        // The modes the base class already reads (AP, HDG, ALT, NAV, APR, BC, VS) keep their keys and
        // SimVars; the GFC 700 adds the flight director, FLC, VNV and the nose wheel.
        v["C172G_AP_FD_STATE"] = AutopilotMode("AUTOPILOT FLIGHT DIRECTOR ACTIVE", "Flight Director");
        v["C172G_AP_FLC_STATE"] = AutopilotMode("AUTOPILOT FLIGHT LEVEL CHANGE", "FLC Mode");
        v["C172G_AP_VNV"] = KeyPress("AS1000_AUTOPILOT_VERTICAL_NAVIGATION_PFD", "VNAV");
        v["C172G_AP_NOSE_UP"] = KeyPress("AS1000_AUTOPILOT_VERTICALSPEED_UP_PFD", "Nose Up");
        v["C172G_AP_NOSE_DN"] = KeyPress("AS1000_AUTOPILOT_VERTICALSPEED_DOWN_PFD", "Nose Down");
        v["C172G_AP_IAS_VALUE"] = new SimVarDefinition
        {
            Name = "AUTOPILOT AIRSPEED HOLD VAR",
            DisplayName = "FLC Airspeed",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.OnRequest
        };

        return v;
    }

    /// <summary>
    /// The fuel selector's positions by `FUELSYSTEM JUNCTION SETTING:1`, which the selector's input
    /// value 0, 1, 2 sets to 1, 2, 3. Measured with the engine running at 1,500 rpm, 40 s a setting:
    /// on 1 only the left tank drained, on 2 both drained equally, on 3 only the right. The
    /// cold-and-dark flight starts on 1, LEFT; the sim's own autostart moves it to BOTH.
    /// </summary>
    internal static Dictionary<double, string> FuelSelectorPositions() =>
        new() { [1] = "Left", [2] = "Both", [3] = "Right" };

    private static SimVarDefinition InputEventState(string inputEvent, string displayName,
        Dictionary<double, string> positions) => new()
    {
        Name = inputEvent,
        DisplayName = displayName,
        Type = SimVarType.InputEvent,
        Units = "number",
        UpdateFrequency = UpdateFrequency.OnRequest,
        ValueDescriptions = positions
    };

    private static SimVarDefinition AutopilotMode(string simVar, string displayName) => new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimVarType.SimVar,
        Units = "Bool",
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        RenderAsButton = true,
        ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ON" }
    };

    private static SimVarDefinition Cover(string simVar, string displayName) => new()
    {
        Name = simVar,
        DisplayName = displayName,
        Type = SimVarType.SimVar,
        Units = "Bool",
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ValueDescriptions = new Dictionary<double, string> { [0] = "Removed", [1] = "Installed" }
    };

    /// <summary>A bezel key with no state of its own: never read, only pressed.</summary>
    private static SimVarDefinition KeyPress(string inputEvent, string displayName) => new()
    {
        Name = inputEvent,
        DisplayName = displayName,
        Type = SimVarType.InputEvent,
        UpdateFrequency = UpdateFrequency.Never,
        RenderAsButton = true
    };

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        var structure = base.GetPanelStructure();
        structure["Electrical"].Add("Standby Battery");
        structure["Autopilot"] = new List<string> { AutopilotPanel };
        structure["Walkaround"] = new List<string> { "Covers" };
        return structure;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = base.BuildPanelControls();
        controls["Standby Battery"] = new List<string> { "C172G_STBY_BATTERY_STATE" };

        controls.Remove("KAP140");
        controls[AutopilotPanel] = new List<string>
        {
            "C172_AP_MASTER_STATE", "C172G_AP_FD_STATE",
            "C172_AP_HDG_STATE", "C172_AP_NAV_STATE", "C172_AP_APR_STATE", "C172_AP_BC_STATE",
            "C172_AP_ALT_STATE", "C172_AP_VS_STATE", "C172G_AP_FLC_STATE", "C172G_AP_VNV",
            "C172G_AP_NOSE_UP", "C172G_AP_NOSE_DN",
            "C172_AP_HDG_VALUE", "C172_AP_ALT_VALUE", "C172_AP_VS_VALUE", "C172G_AP_IAS_VALUE"
        };

        controls["Anti-Ice"] = new List<string> { "C172_PITOT_HEAT_STATE", "C172G_ALT_STATIC_STATE" };
        controls["Covers"] = new List<string> { "C172G_COVER_ENGINE", "C172G_COVER_PITOT", "C172G_COVER_STATIC" };
        return controls;
    }

    public override bool HandleUIVariableSet(string varKey, double value,
        SimVarDefinition varDef, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (SwitchInputEvents.TryGetValue(varKey, out string? switchEvent))
        {
            SetInputEvent(switchEvent, value, varDef, simConnect, announcer);
            return true;
        }

        if (varKey == "C172_FUEL_SELECTOR")
        {
            // The combo holds the junction setting (1-3); the selector's input value is one less.
            SetInputEvent("FUEL_SELECTOR_1", value - 1, varDef, simConnect, announcer);
            return true;
        }

        if (varKey == "C172_STARTER_SET")
        {
            // START on the key. The cockpit springs it back to BOTH by itself after about 2 s, which
            // started the engine first time once the covers were off (measured).
            if ((simConnect.GetCachedVariableValue("C172G_COVER_ENGINE") ?? 0) > 0.5)
                announcer.AnnounceImmediate("Engine cover installed");
            SetInputEvent("ENGINE_MAGNETO_1", 4, varDef, simConnect, announcer);
            return true;
        }

        if (CoverInputEvents.TryGetValue(varKey, out string? coverEvent))
        {
            _ = SetCoverAsync(varKey, coverEvent, value >= 0.5, varDef, simConnect, announcer);
            return true;
        }

        if (AutopilotKeys.TryGetValue(varKey, out string? key))
        {
            if (!SetInputEvent(key, 1, varDef, simConnect, announcer)) return true;
            // The nose wheel and VNV have no state to read back, so say what they did: the nose
            // wheel's new target, VNV that it was pressed.
            if (varKey is "C172G_AP_NOSE_UP" or "C172G_AP_NOSE_DN")
                _ = AnnounceNoseWheelTargetAsync(varDef.DisplayName, simConnect, announcer);
            else if (varKey == "C172G_AP_VNV")
                announcer.Announce($"{varDef.DisplayName} pressed");
            return true;
        }

        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    /// <summary>
    /// Sets a cockpit input event, or says why not. The catalog arrives a few seconds after the
    /// aircraft loads; before it does no write can land, and the base class's events are the ones
    /// measured not to reach this cockpit, so the pilot hears a refusal rather than nothing. Once the
    /// catalog is in, a missing name means the event is not in this cockpit (another variant loaded
    /// under this profile, or an event renamed by a sim update), which waiting will not fix.
    /// </summary>
    private static bool SetInputEvent(string inputEvent, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (simConnect.HasInputEvent(inputEvent) && simConnect.TrySetInputEvent(inputEvent, value)) return true;
        announcer.AnnounceImmediate(simConnect.InputEventCatalogReady
            ? $"{varDef.DisplayName} is not in this aircraft's cockpit."
            : $"{varDef.DisplayName} is not available yet. Try again in a few seconds.");
        return false;
    }

    /// <summary>
    /// A cover's input event TOGGLES it, so it is pressed only when a fresh read says the cover is not
    /// already where the pilot asked: a press against a stale state would put it the other way.
    /// Awaited on the UI thread.
    /// </summary>
    private static async Task SetCoverAsync(string varKey, string inputEvent, bool install, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (await simConnect.ReadFreshAsync(varKey, 1500) is not double current)
        {
            announcer.AnnounceImmediate($"{varDef.DisplayName} could not be read. Try again.");
            return;
        }
        if ((current >= 0.5) == install) return;
        SetInputEvent(inputEvent, 1, varDef, simConnect, announcer);
    }

    /// <summary>
    /// The target the nose wheel just moved: vertical speed in VS mode, airspeed in FLC (where nose up
    /// slows the target). Read back after the key lands, on the UI thread.
    /// </summary>
    private static async Task AnnounceNoseWheelTargetAsync(string displayName, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        await Task.Delay(300);
        bool flc = (simConnect.GetCachedVariableValue("C172G_AP_FLC_STATE") ?? 0) > 0.5;
        bool vs = (simConnect.GetCachedVariableValue("C172_AP_VS_STATE") ?? 0) > 0.5;
        if (flc && await simConnect.ReadFreshAsync("C172G_AP_IAS_VALUE", 1500) is double kt)
            announcer.AnnounceImmediate($"Airspeed {kt:F0} knots");
        else if (vs && await simConnect.ReadFreshAsync("C172_AP_VS_VALUE", 1500) is double fpm)
            announcer.AnnounceImmediate($"Vertical speed {fpm:F0} feet per minute");
        else
            announcer.Announce($"{displayName} pressed");
    }

    /// <summary>
    /// The G1000 windows, from the COWS DA40 (#242): <see cref="Forms.DA40.CowsDA40DisplayForm"/> reads the
    /// stock Working Title G1000 over the Coherent debugger and presses its bezel keys, and the C172's
    /// G1000 is the same instrument on the same views (<c>AS1000_PFD</c>, <c>AS1000_MFD</c>; Marshall
    /// confirmed the MFD's title live on 2026-05-31). The keys are the DA40's: Alt+P opens the PFD;
    /// Alt+M, Alt+N and input-mode Shift+M open the MFD (<c>OpensMfdWindow</c>). No owner is passed:
    /// what the form hands its owner (CAS rows, display units, radio-tune notes) are DA40 callbacks
    /// the C172 does not have, so the CAS is read in the window but not announced by itself.
    /// The windows are tracked, so a second press focuses the open one and an aircraft switch
    /// closes both (<c>DisposeTrackedWindows</c>).
    /// </summary>
    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        if (action == HotkeyAction.ReadDisplayPFD)
        {
            hotkeyManager.ExitOutputHotkeyMode();
            ShowTrackedWindow("C172_PFD",
                () => new Forms.DA40.CowsDA40DisplayForm(
                    "G1000 PFD", "AS1000_PFD", "PFD", simConnect, announcer),
                w => w.Show());
            return true;
        }

        if (DA40.CowsDA40Definition.OpensMfdWindow(action))
        {
            if (action == HotkeyAction.ShowFenixMCDU)
                hotkeyManager.ExitInputHotkeyMode();
            else
                hotkeyManager.ExitOutputHotkeyMode();
            ShowTrackedWindow("C172_MFD",
                () => new Forms.DA40.CowsDA40DisplayForm(
                    "G1000 MFD", "AS1000_MFD", "MFD", simConnect, announcer),
                w => w.Show());
            return true;
        }

        if (AutopilotHotkeys.TryGetValue(action, out string? apKey))
        {
            var vars = GetVariables();
            SetInputEvent(AutopilotKeys[apKey], 1, vars[apKey], simConnect, announcer);
            return true;
        }

        if (action == HotkeyAction.FCUSetAltitude)
        {
            hotkeyManager.ExitInputHotkeyMode();
            return ShowSelectedAltitudeDialog(simConnect, announcer, parentForm);
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    /// <summary>
    /// The selected altitude, typed. `AP_ALT_VAR_SET_ENGLISH` ignores its value on the G1000 (measured:
    /// the first one set the current altitude rounded up, each later one added 1000 ft), so the value
    /// is written to the SimVar itself. The write alone does not count as SELECTING an altitude, and
    /// the GFC 700 refuses FLC until one is selected (DA40A-2), so one knob step goes first and the
    /// exact value is written after it lands. Both over SimConnect: no MobiFlight needed.
    /// </summary>
    private bool ShowSelectedAltitudeDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return false;
        }

        // #242's dialogs still parse with double.TryParse (NumberParsing reached main a day after
        // #242 branched), so this matches them. Switch to NumberParsing.TryParseTyped (CORE-17) once
        // this branch takes main.
        var dialog = new Forms.ValueInputForm("Set Selected Altitude", "Altitude", "0 to 25000 feet", announcer,
            input => double.TryParse(input, out double val) && val >= 0 && val <= 25000
                ? (true, string.Empty)
                : (false, "Enter an altitude between 0 and 25000 feet"));
        if (dialog.ShowDialog(parentForm) != DialogResult.OK || !dialog.IsValidInput
            || !double.TryParse(dialog.InputValue, out double typed))
            return false;

        int feet = (int)(Math.Round(typed / 100.0) * 100.0);
        simConnect.SendEvent("AP_ALT_VAR_INC");
        _ = WriteSelectedAltitudeAfterStepAsync(simConnect, feet);
        announcer.AnnounceImmediate($"Selected altitude {feet.ToString(CultureInfo.CurrentCulture)} feet");
        return true;
    }

    /// <summary>The exact altitude, half a second after the knob step, on the UI thread.</summary>
    private static async Task WriteSelectedAltitudeAfterStepAsync(SimConnectManager simConnect, int feet)
    {
        await Task.Delay(500);
        simConnect.SetSimVar("AUTOPILOT ALTITUDE LOCK VAR", feet, "feet");
    }
}
