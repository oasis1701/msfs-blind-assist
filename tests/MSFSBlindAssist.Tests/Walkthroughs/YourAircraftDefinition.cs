// The walkthrough template. docs/adding-features.md and docs/QUICK-REFERENCE.md show the
// regions marked below word for word, and the test project compiles this file, so a walkthrough
// example that stops compiling fails the build. When a region changes, WalkthroughTemplateTests
// names each doc block to update and prints the text to paste. A region marked "(collapsed)"
// shows as one "// ..." line inside a larger region's block.
using System.Windows.Forms; // The app project imports this implicitly; the test project does not.

// doc-region: aircraft-file
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Aircraft;

public class YourAircraftDefinition : BaseAircraftDefinition
{
    public override string AircraftName => "Your Aircraft Full Name";
    public override string AircraftCode => "YOUR_AIRCRAFT";

    protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables()
    {
        // Start from the variables every aircraft shares (SIM ON GROUND and others).
        var variables = GetBaseVariables();

        var aircraftVariables = new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // A panel control (Workflow 1):
            // doc-region: panel-variable (collapsed)
            ["NEW_CONTROL_VAR"] = new SimConnect.SimVarDefinition
            {
                Name = "A32NX_NEW_CONTROL", // An L:var's name, without "L:"
                DisplayName = "New Control",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            // doc-region-end: panel-variable

            // Background monitoring (Workflow 2):
            // doc-region: monitoring-variable (collapsed)
            ["A32NX_NEW_STATUS"] = new SimConnect.SimVarDefinition
            {
                Name = "A32NX_NEW_STATUS",
                DisplayName = "New Status",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Status inactive",
                    [1] = "Status active"
                }
            },
            // doc-region-end: monitoring-variable

            // An H-variable button (Workflow 3):
            // doc-region: h-variable (collapsed)
            ["BUTTON_KEY"] = new SimConnect.SimVarDefinition
            {
                Name = "BUTTON_KEY",
                DisplayName = "Button Label",
                Type = SimConnect.SimVarType.HVar,
                UseMobiFlight = true,
                PressEvent = "YOUR_BUTTON_PRESSED", // An H: event's name, without "H:"
                ReleaseEvent = "YOUR_BUTTON_RELEASED",
                PressReleaseDelay = 200, // Optional, defaults to 200 ms
                UpdateFrequency = SimConnect.UpdateFrequency.Never
            },
            // doc-region-end: h-variable
        };

        foreach (var kvp in aircraftVariables)
            variables[kvp.Key] = kvp.Value;

        return variables;
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Your Section"] = new List<string> { "Your Panel" }
        };
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            // doc-region: panel-controls
            ["Your Panel"] = new List<string>
            {
                "NEW_CONTROL_VAR",
                "BUTTON_KEY"
            }
            // doc-region-end: panel-controls
        };
    }

    // Values a panel shows as read-only text; none here.
    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();

    // doc-region: button-state-mapping
    // Starts empty, as on every aircraft but the FBW A320, Headwind A330 and FBW A380. An entry is
    // CORE-7's button read-back: about 300 ms after a press of the mapped event, the app speaks the
    // state variable once. Add one only as the owner's deliberate choice, for a button whose effect
    // the pilot cannot otherwise hear, never to echo a press.
    public override Dictionary<string, string> GetButtonStateMapping() => new();
    // doc-region-end: button-state-mapping

    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    // doc-region: hotkey-map
    protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
    {
        return new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.ToggleAutopilot1] = "YOUR_AP_TOGGLE_EVENT",
            [HotkeyAction.FCUHeadingPush] = "YOUR_HDG_PUSH_EVENT"
        };
    }
    // doc-region-end: hotkey-map

    // A hotkey with its own dialog (Workflow 6, Method 2):
    // doc-region: custom-hotkey-handler (collapsed)
    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        if (action == HotkeyAction.FCUSetAltitude)
        {
            ShowFCUInputDialog("Set Altitude", "Altitude", "100-49000 feet",
                "YOUR_ALT_SET_EVENT", simConnect, announcer, parentForm,
                validator: (input) =>
                {
                    if (double.TryParse(input, out double val) && val >= 100 && val <= 49000)
                        return (true, "");
                    return (false, "Altitude must be 100-49000");
                },
                valueConverter: (val) => (uint)Math.Round(val / 100) * 100
            );
            return true;
        }
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }
    // doc-region-end: custom-hotkey-handler
}
// doc-region-end: aircraft-file
