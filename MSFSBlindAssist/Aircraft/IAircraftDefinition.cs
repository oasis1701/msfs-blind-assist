namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Defines the control type for FCU (Flight Control Unit) controls.
/// Different aircraft may use different interaction patterns.
/// </summary>
public enum FCUControlType
{
    /// <summary>
    /// Direct value input (e.g., A320 FCU with SET commands)
    /// </summary>
    SetValue,

    /// <summary>
    /// Increment/Decrement buttons (e.g., Boeing MCP with INC/DEC)
    /// </summary>
    IncrementDecrement
}

/// <summary>
/// Per-aircraft tunables for visual landing guidance. Defaults are A320 numbers; heavier
/// or smaller airframes should override <see cref="IAircraftDefinition.GetVisualGuidanceProfile"/>.
/// </summary>
public sealed class VisualGuidanceProfile
{
    /// <summary>Typical pitch attitude (degrees) the airframe sits at during a stabilized approach.
    /// Used to bias the commanded nominal pitch: nominalPitch = -3 (glideslope) + AoA.</summary>
    public double TypicalApproachAoaDeg { get; init; } = 6.0;

    /// <summary>Reference Vref (knots) used as the denominator in the lateral airspeed-compensation
    /// scaler sqrt(GS / Vref). Only matters when an airframe's approach speed differs markedly from A320.</summary>
    public double ReferenceVrefKnots { get; init; } = 140.0;

    /// <summary>Cap on commanded-pitch change rate (deg/sec). Heavier aircraft have slower pitch
    /// authority and benefit from a tighter cap so the audio tone does not chase impossible attitudes.</summary>
    public double MaxPitchRateDegPerSec { get; init; } = 2.5;

    /// <summary>Cap on commanded-bank change rate (deg/sec). Same rationale as the pitch cap.</summary>
    public double MaxBankRateDegPerSec { get; init; } = 3.0;

    /// <summary>Minimum frequency (Hz) of the dual-tone pitch mapping. Saturates here at full
    /// nose-down. Defaults match a transport-jet attitude envelope (200 Hz). Light aircraft or
    /// fighters with wider attitude envelopes may want a different range so the tone does not
    /// saturate during normal manoeuvring.</summary>
    public float ToneMinFrequencyHz { get; init; } = 200f;

    /// <summary>Maximum frequency (Hz) of the dual-tone pitch mapping. Saturates here at full
    /// nose-up. Centre frequency is computed as the midpoint of min and max.</summary>
    public float ToneMaxFrequencyHz { get; init; } = 800f;

    /// <summary>Pitch (degrees) at which the tone frequency saturates to the min/max. Default
    /// is ±6°, which covers the transport-jet approach envelope (-3° glideslope, +6° flare
    /// AoA at the saturation edge) and gives a **50 Hz/° matching slope** — 67% more sensitive
    /// than the AudioToneGenerator's native ±10° default (30 Hz/°). At this slope a 0.1° pitch
    /// error produces a 5 Hz beat (slow audible wobble); 0.5° produces a 25 Hz beat (clear
    /// fluttering). Wider envelopes (aerobatic, fighter) should raise this; tighter envelopes
    /// can lower it further for even finer resolution near zero (at the cost of earlier
    /// saturation outside the approach phase).</summary>
    public double TonePitchRangeDeg { get; init; } = 6.0;

    /// <summary>Bank (degrees) at which the tone pan saturates to ±1.0. Default is ±5°, which
    /// covers a stabilized approach (banks rarely exceed 5° once on centerline) and gives a
    /// 0.20 pan/° matching slope (vs the old 0.10 pan/° at ±10°) so small bank errors produce
    /// clearly noticeable stereo deltas. The PID can command up to 25° bank during intercept
    /// — that saturates the desired tone, but the spoken bank-guidance announcements
    /// ("3 left", "2 right", etc.) already handle the large-error regime where matching by
    /// ear is unnecessary. Raise for aircraft with wider habitual bank envelopes.</summary>
    public double ToneBankRangeDeg { get; init; } = 5.0;
}

/// <summary>
/// Interface for aircraft-specific definitions including variables, panels, and behavior.
/// Each supported aircraft should implement this interface to provide its configuration.
/// </summary>
public interface IAircraftDefinition
{
    /// <summary>
    /// Full display name of the aircraft (e.g., "FlyByWire Airbus A320neo")
    /// </summary>
    string AircraftName { get; }

    /// <summary>
    /// Short code for the aircraft (e.g., "A320", "B737")
    /// Used for settings persistence and internal identification
    /// </summary>
    string AircraftCode { get; }

    /// <summary>
    /// Gets the current flight phase for window title display.
    /// Returns null or empty string if aircraft doesn't track flight phases.
    /// Example: "TAKEOFF", "CLIMB", "CRUISE", "DESCENT", "APPROACH", "LANDING"
    /// </summary>
    string? CurrentFlightPhase { get; }

    /// <summary>
    /// Gets all simulator variables and controls for this aircraft.
    /// Maps variable keys to their definitions.
    /// </summary>
    Dictionary<string, SimConnect.SimVarDefinition> GetVariables();

    /// <summary>
    /// Gets the panel organization structure.
    /// Maps section names to lists of panel names within that section.
    /// Example: "Overhead Forward" -> ["ELEC", "ADIRS", "APU"]
    /// </summary>
    Dictionary<string, List<string>> GetPanelStructure();

    /// <summary>
    /// Gets the mapping of panels to their control variable keys.
    /// Maps panel names to lists of variable keys that appear in that panel.
    /// Example: "FCU" -> ["A32NX.FCU_HDG_SET", "A32NX.FCU_SPD_SET"]
    /// </summary>
    Dictionary<string, List<string>> GetPanelControls();

    /// <summary>
    /// Gets the mapping of panels to display-only variables.
    /// These variables update silently without triggering announcements.
    /// Optional - return empty dictionary if not used.
    /// </summary>
    Dictionary<string, List<string>> GetPanelDisplayVariables();

    /// <summary>
    /// Gets the button-to-state variable mapping for automatic announcements.
    /// Maps button event keys to their corresponding state variable keys.
    /// Example: "A32NX.FCU_AP_1_PUSH" -> "A32NX_FCU_AP_1_LIGHT_ON"
    /// </summary>
    Dictionary<string, string> GetButtonStateMapping();

    /// <summary>
    /// Gets the control type for altitude input in the FCU.
    /// </summary>
    FCUControlType GetAltitudeControlType();

    /// <summary>
    /// Gets the control type for heading input in the FCU.
    /// </summary>
    FCUControlType GetHeadingControlType();

    /// <summary>
    /// Gets the control type for speed input in the FCU.
    /// </summary>
    FCUControlType GetSpeedControlType();

    /// <summary>
    /// Gets the control type for vertical speed input in the FCU.
    /// </summary>
    FCUControlType GetVerticalSpeedControlType();

    /// <summary>
    /// Handles aircraft-specific hotkey actions.
    /// Allows aircraft to define custom behavior for hotkey inputs.
    /// </summary>
    /// <param name="action">The hotkey action to handle</param>
    /// <param name="simConnect">SimConnect manager for sending events and requesting data</param>
    /// <param name="announcer">Screen reader announcer for user feedback</param>
    /// <param name="parentForm">Parent form for showing dialogs</param>
    /// <param name="hotkeyManager">Hotkey manager for controlling hotkey modes</param>
    /// <returns>True if the action was handled, false if not supported by this aircraft</returns>
    bool HandleHotkeyAction(
        Hotkeys.HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm,
        Hotkeys.HotkeyManager hotkeyManager);

    // FCU/MCP Request Methods (Flight Control Unit / Mode Control Panel)
    // Aircraft without FCU can use default (do-nothing) implementations

    /// <summary>
    /// Requests and announces the current FCU/MCP heading value.
    /// Aircraft without FCU should use the default implementation (does nothing).
    /// </summary>
    void RequestFCUHeading(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer);

    /// <summary>
    /// Requests and announces the current FCU/MCP speed value.
    /// Aircraft without FCU should use the default implementation (does nothing).
    /// </summary>
    void RequestFCUSpeed(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer);

    /// <summary>
    /// Requests and announces the current FCU/MCP altitude value.
    /// Aircraft without FCU should use the default implementation (does nothing).
    /// </summary>
    void RequestFCUAltitude(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer);

    /// <summary>
    /// Requests and announces the current FCU/MCP vertical speed value.
    /// Aircraft without FCU should use the default implementation (does nothing).
    /// </summary>
    void RequestFCUVerticalSpeed(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer);

    // Variable Update Processing

    /// <summary>
    /// Processes aircraft-specific variable updates.
    /// Called for each variable update before generic processing in MainForm.
    /// Allows aircraft to implement custom logic for combining or interpreting multiple variables.
    /// Examples: A320 FCU display combining (value + managed mode), VS/FPA mode handling.
    /// </summary>
    /// <param name="varName">The variable name that was updated</param>
    /// <param name="value">The new value of the variable</param>
    /// <param name="announcer">Screen reader announcer for user feedback</param>
    /// <returns>True if the update was fully processed and no further generic processing needed, false otherwise</returns>
    bool ProcessSimVarUpdate(string varName, double value, Accessibility.ScreenReaderAnnouncer announcer);

    // UI Variable Setting (Panel Controls)

    /// <summary>
    /// Handles aircraft-specific variable setting from UI panel controls.
    /// Called when user changes a value in a panel control (ComboBox, TextBox+Button) before generic handling.
    /// Allows aircraft to implement custom validation, conversion, or multi-step logic for setting variables.
    /// Examples: A320 autobrake multi-event sending, baro conversion with unit detection, VS/FPA mode validation.
    /// </summary>
    /// <param name="varKey">The variable key being set</param>
    /// <param name="value">The value to set</param>
    /// <param name="varDef">The variable definition</param>
    /// <param name="simConnect">SimConnect manager for sending events and setting values</param>
    /// <param name="announcer">Screen reader announcer for user feedback</param>
    /// <returns>True if aircraft handled the set operation, false to continue with generic handling</returns>
    bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer);

    // Display System Monitoring (ECAM for Airbus, EICAS for Boeing, etc.)
    // Aircraft without these systems should use the default implementation (does nothing)

    /// <summary>
    /// Starts monitoring the aircraft's display system (ECAM, EICAS, etc.).
    /// This is called when the user enables display monitoring.
    /// Aircraft without display systems should use the default implementation (does nothing).
    /// </summary>
    void StartDisplayMonitoring(SimConnect.SimConnectManager simConnect);

    /// <summary>
    /// Stops monitoring the aircraft's display system (ECAM, EICAS, etc.).
    /// This is called when the user disables display monitoring.
    /// Aircraft without display systems should use the default implementation (does nothing).
    /// </summary>
    void StopDisplayMonitoring(SimConnect.SimConnectManager simConnect);

    // Visual Landing Guidance Profile

    /// <summary>
    /// Returns the per-aircraft visual-guidance tunables (approach AoA, reference Vref, pitch/bank rate caps).
    /// Default implementation in <c>BaseAircraftDefinition</c> returns A320 numbers; override on heavier
    /// or smaller airframes (e.g., 777, 747) to bias the nominal commanded pitch and rate limits.
    /// </summary>
    VisualGuidanceProfile GetVisualGuidanceProfile();
}
