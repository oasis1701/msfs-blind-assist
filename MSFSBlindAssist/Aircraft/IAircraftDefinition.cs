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
    /// <summary>Typical angle of attack (degrees) at the airframe's stabilized-approach trim
    /// point. Used as a FALLBACK in <c>VisualGuidanceManager.CalculateDesiredPitch</c> when the
    /// live AoA reading (SimConnect <c>INCIDENCE ALPHA</c>) is unavailable or outside the
    /// sanity band. With live AoA available (the normal case), this value is unused — the
    /// real measured AoA inherently encodes the current weight / flap / speed, so the nominal
    /// pitch baseline converges on whatever the airplane is actually trimmed for and the
    /// per-aircraft estimate becomes obsolete.</summary>
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

    /// <summary>Height (ft) by which VG's datum-referenced altitude (SimConnect PLANE ALTITUDE −
    /// threshold elevation) sits ABOVE the bare geometric distance×tan(3°) glidepath when the
    /// aircraft is correctly tracking the published ILS glideslope. It is the sum of two terms:
    /// the ILS reference datum height (~50 ft — ICAO Annex 10, the glideslope antenna's
    /// threshold crossing height), plus the airframe's SimConnect-datum-above-GS-antenna
    /// offset. Added to the ideal glidepath so VG's "on glideslope" coincides with the real
    /// ILS path instead of one ~50 ft TCH too low (which biased the commanded vertical speed
    /// into commanding extra descent). The 777 value (80) is measured against a coupled
    /// autoland; the A320 default is estimated (≈50 TCH + a smaller datum offset) and should
    /// be calibrated in-sim. Set to 0 to disable the correction.</summary>
    public double GlideslopeAltitudeBiasFt { get; init; } = 60.0;

    /// <summary>Height (ft) by which VG's datum-referenced altitude sits ABOVE true radio
    /// altitude (main-gear height over the runway) in the flare attitude. Subtracted from the
    /// measured altitude before the flare and touchdown phase thresholds are tested, so the
    /// flare cue fires at a real ~30 ft of gear height (Boeing/Airbus FCTM flare initiation)
    /// rather than ~20 ft late, and "touchdown" actually triggers (on a widebody the datum
    /// never gets within the 5 ft touchdown threshold of the runway, so the uncorrected check
    /// never fired at all). The 777 value (30) is measured against an actual flare attitude
    /// (pitches up further than approach attitude → larger datum-vs-gear offset than on the
    /// approach); the A320 default (12) is estimated and should be calibrated in-sim.</summary>
    public double FlareAltitudeBiasFt { get; init; } = 12.0;

    /// <summary>True main-gear height (ft) at which the flare phase is entered and the flare
    /// audio cue announces. Per-aircraft because manufacturer flare-initiation guidance and
    /// autoland behaviour vary: the A320 FCTM specifies a 30 ft initiation (default); the
    /// PMDG 777's autoland begins flaring at ~40 ft RA, so VG's cue is timed to match — a
    /// hand-flying pilot using VG to mirror autoland gets the cue at the same moment the
    /// autopilot would have started its flare. Keep within 20–50 ft.</summary>
    public double FlareTriggerWheelHeightFt { get; init; } = 30.0;

    /// <summary>Target pitch (degrees) commanded during the flare phase. Per-aircraft because
    /// manufacturer flare pitch attitudes vary: Boeing FCTM specifies a 2–3° pitch increase
    /// from approach attitude (777 ≈ +1.5° approach → ~+4–4.5° flare), while Airbus aircraft
    /// flare to ~+5–6° (default). Used directly by <c>CalculateDesiredPitch</c> in the Flare
    /// branch, rate-limited from the previous pitch by <c>MAX_FLARE_PITCH_RATE</c> so the
    /// desired-tone frequency doesn't step. Keep within 3–8°.</summary>
    public double FlareTargetPitchDeg { get; init; } = 6.0;
}

/// <summary>
/// Per-aircraft tunables for the synthetic Waypoint Flight Director (en-route hand-fly to the
/// tracked Shift+F slots). Defaults are A320 numbers; heavier/faster jets roll more slowly and
/// cover ground faster, so they use a gentler roll gain, larger capture radius and a longer
/// rate-lead. Override <see cref="IAircraftDefinition.GetWaypointFlightDirectorProfile"/> on those.
/// All fields feed <c>WaypointFlightDirectorGeometry</c> and the dual-tone renderer; the feature is
/// otherwise 100% stock-SimVar and aircraft-agnostic (no per-airframe code).
/// </summary>
public sealed class WaypointFlightDirectorProfile
{
    /// <summary>Degrees of commanded bank per degree of lateral track error (proportional roll
    /// law). Heavier jets roll slower → smaller gain to avoid overshoot. Capped by <see cref="MaxBankDeg"/>.</summary>
    public double KRollDegPerDegTrack { get; init; } = 1.1;

    /// <summary>Maximum commanded bank (degrees). A real FD caps around 25–30°.</summary>
    public double MaxBankDeg { get; init; } = 25.0;

    /// <summary>Maximum commanded pitch (degrees), nose-up positive. Clamps the FPA+AoA command.</summary>
    public double MaxPitchDeg { get; init; } = 12.0;

    /// <summary>Capture radius (NM) at which a fix counts as reached and the FD sequences to the
    /// next slot. Scales with speed (a fast jet covers more ground per second).</summary>
    public double CaptureRadiusNm { get; init; } = 0.5;

    /// <summary>Ground-speed floor (knots) below which GPS ground track is unreliable; the FD
    /// falls back to heading / holds the lateral command (taxi, very slow flight).</summary>
    public double LowSpeedFloorKts { get; init; } = 40.0;

    /// <summary>Rate-lead time (seconds) for the roll command: the track error is projected this
    /// far ahead by the yaw rate so turns roll out cleanly instead of overshooting. Mirrors the
    /// per-aircraft taxi-tone turn lead; heavier jets need more.</summary>
    public double BankRateLeadSec { get; init; } = 1.0;

    /// <summary>Fallback approach AoA (degrees) used only if the live INCIDENCE ALPHA reading is
    /// unavailable / out of band. Live AoA (the normal case) makes this obsolete.</summary>
    public double TypicalApproachAoaDeg { get; init; } = 6.0;

    /// <summary>Course-tracking (option 3): maximum intercept angle (degrees) when far off a course
    /// line — the FD never commands a track more than this off the course while capturing.</summary>
    public double MaxInterceptDeg { get; init; } = 40.0;

    /// <summary>Course-tracking: intercept angle added per NM of cross-track error, capped by
    /// <see cref="MaxInterceptDeg"/>. 20°/NM → full intercept at 2 NM off, ~10° at 0.5 NM.</summary>
    public double InterceptDegPerNm { get; init; } = 20.0;

    /// <summary>Slew cap (deg/sec) on the commanded BANK between frames — stops the pan fluctuating
    /// on every track/heading wiggle. Larger = snappier but jumpier.</summary>
    public double MaxBankRateDegPerSec { get; init; } = 5.0;

    /// <summary>Slew cap (deg/sec) on the commanded PITCH between frames (anti-fluctuation).</summary>
    public double MaxPitchRateDegPerSec { get; init; } = 3.0;

    /// <summary>Heading/altitude bug mode: altitude-capture vertical speed per foot of error (fpm/ft),
    /// clamped by <see cref="MaxCaptureVsFpm"/>. 8 fpm/ft → starts tapering within ~190 ft of target.</summary>
    public double AltCaptureVsPerFt { get; init; } = 8.0;

    /// <summary>Heading/altitude bug mode: max commanded climb/descent rate (fpm) during a capture.</summary>
    public double MaxCaptureVsFpm { get; init; } = 1500.0;

    // Dual-tone mapping — same idiom and defaults as VisualGuidanceProfile so the FD's tones
    // sound consistent with Visual Guidance (the pilot zero-beats desired vs current).
    public float ToneMinFrequencyHz { get; init; } = 200f;
    public float ToneMaxFrequencyHz { get; init; } = 800f;
    public double TonePitchRangeDeg { get; init; } = 6.0;
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

    /// <summary>
    /// Called after a panel Event-type button is pressed, for an aircraft-specific
    /// post-press read-out (e.g. FCU knob push/pull buttons speak their value).
    /// </summary>
    void OnPanelButtonFired(string varKey, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer);

    /// <summary>
    /// Called once after a panel is built/shown, so an aircraft with a multi-page
    /// status box (driven by a page combo) can populate the box with the combo's
    /// CURRENT page immediately, without the user having to cycle the combo.
    /// </summary>
    void OnDisplayPanelShown(string panelKey, SimConnect.SimConnectManager simConnect);

    /// <summary>
    /// Lets an aircraft override the PANEL-DISPLAY string for a variable whose raw
    /// numeric value isn't directly presentable (e.g. an ARINC429 word that reads
    /// as ~14 billion, or a value that needs unit-aware decoding). Return true and
    /// set <paramref name="displayText"/> to use it; return false to fall through
    /// to the default ValueDescriptions / numeric formatting.
    /// </summary>
    bool TryGetDisplayOverride(string varKey, double value, out string displayText);

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

    /// <summary>
    /// Returns the per-aircraft Waypoint Flight Director tunables (roll gain, bank/pitch caps,
    /// capture radius, low-speed floor, rate-lead, tone mapping). Default in
    /// <c>BaseAircraftDefinition</c> is the A320 baseline; heavier/faster jets override.
    /// </summary>
    WaypointFlightDirectorProfile GetWaypointFlightDirectorProfile();

    /// <summary>
    /// Taxi-turn rollout-anticipation lead, seconds. The steering tone's
    /// heading error is projected this far ahead by the yaw rate so the tone
    /// centres BEFORE the nose reaches the new heading (pilot reaction time +
    /// airframe yaw inertia). Per-aircraft because sim steering response
    /// differs sharply: FBW Airbuses overshoot chronically; PMDG Boeings
    /// barely at all. 0 disables the projection.
    /// </summary>
    double TaxiTurnLeadSeconds { get; }
}
