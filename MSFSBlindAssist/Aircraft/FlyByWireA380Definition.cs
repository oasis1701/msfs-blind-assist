using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition for the FlyByWire A380X (development build).
///
/// Scope of this initial integration:
/// - Identity + selectable aircraft entry in the Aircraft menu.
/// - FCU readouts wired to A32NX-prefixed FCU L:vars (the A380X currently
///   shares the FCU display L:var names with the A32NX FBW codebase).
/// - MCDU + flyPad EFB exposed through the EFB bridge server (see
///   <c>FBWA380MCDUForm</c> / <c>FBWA380EFBForm</c>). The JS bridge that
///   posts the live screen rows to the server lives in
///   <c>Resources/fbw-a380-bridge.js</c> and must be installed into the
///   FlyByWire A380X package by hand for now.
///
/// Out of scope for this branch: full panel/section structure with
/// per-switch L:vars. The accompanying brief explicitly leaves controls
/// to a follow-up — the aircraft can be spawned on the runway (powered
/// up) for end-to-end MCDU + EFB verification.
/// </summary>
public class FlyByWireA380Definition : BaseAircraftDefinition,
    ISupportsBridgedMCDU,
    ISupportsBridgedEFB
{
    public override string AircraftName => "FlyByWire A380X (Dev)";
    public override string AircraftCode => "FBW_A380";

    /// <summary>
    /// Bridge JS execution stage, as written by Resources/fbw-a380-bridge.js
    /// into L:MSFSBA_FBWA380_STAGE. Used by the MCDU form's status label
    /// to surface bridge bring-up problems without MSFS dev mode:
    ///   0 = bridge JS never ran (override not picked up, syntax error,
    ///       or HTML patch missing)
    ///   1 = JS executed inside the VCockpit but hasn't reached the
    ///       server (fetch in flight, or server not started yet)
    ///   2 = fetch threw — Coherent GT CSP / network policy is blocking
    ///       localhost from this gauge context
    ///   3 = bridge connected and posting state to the server
    /// </summary>
    public int BridgeStage { get; private set; }

    // A380 FCU uses the same direct-set dialog pattern as the A320.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, SimVarDefinition> GetVariables()
    {
        // Common base vars (SIM_ON_GROUND, altitude, trim, visual/hand-fly
        // sources) — gives the universal MSFSBA features a working dataset
        // even without aircraft-specific panel L:vars wired up yet.
        var vars = GetBaseVariables();

        // Bridge stage diagnostic — written by Resources/fbw-a380-bridge.js
        // every time it advances. Continuously monitored so the MCDU
        // form's status label reflects bring-up state in real time.
        vars["MSFSBA_FBWA380_STAGE"] = new SimVarDefinition
        {
            Name = "L:MSFSBA_FBWA380_STAGE",
            DisplayName = "A380 Bridge Stage",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false  // Diagnostic only; form's status timer reads it.
        };
        return vars;
    }

    /// <summary>
    /// Captures bridge-stage updates from the continuous SimVar monitor.
    /// Returning true suppresses the default announcement (we don't want
    /// MSFSBA to speak "A380 Bridge Stage 1" on every change — the MCDU
    /// form surfaces this through its status label instead).
    /// </summary>
    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        if (varName == "MSFSBA_FBWA380_STAGE")
        {
            BridgeStage = (int)value;
            return true;
        }
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        // No panels exposed yet — see class header. An empty dictionary
        // gives the user an empty Sections list, which is the correct
        // signal until per-system panels are added in a follow-up.
        return new Dictionary<string, List<string>>();
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>();
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        return new Dictionary<string, List<string>>();
    }

    public override Dictionary<string, string> GetButtonStateMapping()
    {
        return new Dictionary<string, string>();
    }

    // ---- FCU readout overrides ----------------------------------------
    // The A380X dev build reuses the A32NX FCU display L:var names for its
    // glareshield FCU. If a future build renames any of these (e.g. to
    // A380X_FCU_AFS_DISPLAY_*), this is the single place to update.

    public override void RequestFCUHeading(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!simConnect.IsConnected) return;
        try
        {
            simConnect.RequestSingleValue(300, "L:A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE", "number", "FCU_HEADING");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FBWA380] Error requesting FCU heading: {ex.Message}");
        }
    }

    public override void RequestFCUSpeed(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!simConnect.IsConnected) return;
        try
        {
            simConnect.RequestSingleValue(301, "L:A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE", "number", "FCU_SPEED");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FBWA380] Error requesting FCU speed: {ex.Message}");
        }
    }

    public override void RequestFCUAltitude(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!simConnect.IsConnected) return;
        try
        {
            simConnect.RequestSingleValue(302, "L:A32NX_FCU_AFS_DISPLAY_ALT_VALUE", "number", "FCU_ALTITUDE");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FBWA380] Error requesting FCU altitude: {ex.Message}");
        }
    }

    public override void RequestFCUVerticalSpeed(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!simConnect.IsConnected) return;
        try
        {
            simConnect.RequestSingleValue(303, "L:A32NX_FCU_AFS_DISPLAY_VS_VALUE", "number", "FCU_VERTICAL_SPEED");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FBWA380] Error requesting FCU vertical speed: {ex.Message}");
        }
    }

    // No aircraft-specific variable post-processing yet. Falling through to
    // BaseAircraftDefinition.ProcessSimVarUpdate gives the universal
    // altitude-crossing and trim announcements; the FCU readout dialogs
    // above announce their own values when the response arrives.
}
