using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Cessna 172 Skyhawk with Garmin G1000 glass cockpit.
/// Adds standby battery system and G1000-specific monitoring.
/// </summary>
public class CessnaC172G1000Definition : CessnaC172BaseDefinition
{
    public override string AircraftName => "Cessna 172 Skyhawk (G1000)";
    public override string AircraftCode => "C172_G1000";

    protected override Dictionary<string, SimConnect.SimVarDefinition> GetC172Variables()
    {
        var variables = base.GetC172Variables();

        // Standby battery system (G1000 only)
        variables["C172G_STBY_BATTERY_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "ELECTRICAL MASTER BATTERY:2",
            DisplayName = "Standby Battery",
            Type = SimConnect.SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };
        variables["C172G_STBY_BATTERY_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "TOGGLE_MASTER_BATTERY",
            DisplayName = "Standby Battery Toggle",
            Type = SimConnect.SimVarType.Event,
            EventParam = 2, // Battery index 2
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        };
        variables["C172G_STBY_BATTERY_TEST"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_STBYBattery_Test",
            DisplayName = "Standby Battery Test",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Test" }
        };

        return variables;
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        var structure = base.GetPanelStructure();
        // Add Standby Battery panel to Electrical section
        structure["Electrical"].Add("Standby Battery");
        return structure;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = base.BuildPanelControls();
        controls["Standby Battery"] = new List<string>
        {
            "C172G_STBY_BATTERY_STATE", "C172G_STBY_BATTERY_TOGGLE",
            "C172G_STBY_BATTERY_TEST"
        };
        return controls;
    }

    public override Dictionary<string, string> GetButtonStateMapping()
    {
        var mapping = base.GetButtonStateMapping();
        mapping["C172G_STBY_BATTERY_TOGGLE"] = "C172G_STBY_BATTERY_STATE";
        return mapping;
    }

    /// <summary>
    /// Override NAV mode engagement so the panel NAV button turns off HDG first.
    /// Confirmed via live testing 2026-05-31:
    ///   - KAP140 rejects AP_NAV1_HOLD while AUTOPILOT HEADING LOCK is active
    ///   - Must disable HDG, wait, then send AP_NAV1_HOLD
    ///   - AP_PANEL_NAV1_HOLD does nothing on this aircraft
    /// All other variables fall through to base class.
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value,
        SimVarDefinition varDef, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (varKey == "C172_AP_NAV_STATE")
        {
            // Read current HDG state; if on, turn it off before engaging NAV
            bool hdgOn = false;
            try
            {
                double? cached = simConnect.GetCachedVariableValue("C172_AP_HDG_STATE");
                hdgOn = cached.HasValue && cached.Value > 0.5;
            }
            catch { }

            if (hdgOn)
            {
                // Turn off HDG first, then NAV after a brief delay — fire and forget
                simConnect.SendEvent("AP_PANEL_HEADING_HOLD");
                _ = EngageNavAfterHeadingOffAsync(simConnect, announcer);
                announcer.AnnounceImmediate("Turning off HDG then engaging NAV…");
            }
            else
            {
                // HDG already off — engage NAV directly
                simConnect.SendEvent("AP_NAV1_HOLD");
                announcer.AnnounceImmediate("NAV mode toggled");
            }
            return true;
        }

        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
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

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    /// <summary>
    /// Sends <c>AP_NAV1_HOLD</c> half a second after heading hold was turned off, then reads the NAV
    /// state back. Started from the panel on the UI thread and awaited there (no <c>Task.Run</c>, no
    /// <c>ConfigureAwait(false)</c>), so the send stays on the UI thread as SIM-20 requires.
    /// </summary>
    private static async Task EngageNavAfterHeadingOffAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        await Task.Delay(500);
        simConnect.SendEvent("AP_NAV1_HOLD");
        await Task.Delay(400);
        bool navOn = (simConnect.GetCachedVariableValue("C172_AP_NAV_STATE") ?? 0) > 0.5;
        announcer.Announce($"NAV mode: {(navOn ? "ON" : "OFF — try again")}");
    }
}
