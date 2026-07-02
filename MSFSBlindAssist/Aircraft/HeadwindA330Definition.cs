namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// HeadwindSim A330-900neo ("A339X") accessibility definition.
///
/// The Headwind A330neo is a fork of the FlyByWire A32NX — it shares the entire
/// <c>A32NX_</c> L-var surface (≈900 vars), the same systems model, FCU, ECAM/E-WD,
/// MCDU (broadcast over the FBW SimBridge relay), and the shared <c>fbw-common</c>
/// flyPad EFB (Coherent "- EFB" view). It is modelled with the A32NX 2-spool engine
/// vars (<c>A32NX_ENGINE_N1/N2:1|2</c>), NOT the A380's 4-engine / N3 surface — so the
/// A320 definition is the correct, near-complete base.
///
/// Consequently this class INHERITS the full FlyByWire A320 definition (variables,
/// panels, hotkeys, FCU value-entry windows, EWD/SD decode, MCDU, flyPad EFB, taxi/
/// visual-guidance profiles) and overrides only what genuinely differs on the A330:
///   • identity (name / code / ICAO),
///   • the Coherent MCDU view needle used by the D / Shift+D flight-info readout
///     (A339X-named instruments instead of A32NX-named),
///   • a widebody visual-guidance profile (heavier airframe, higher Vref).
///
/// MainForm routes MCDU / flyPad / monitor-manager / checklist / hotkey-guide for
/// AircraftCode "HW_A330" exactly as it does for "A320" (see the dispatch sites that
/// test for both codes). The A330 reuses the A32NX monitor-manager form and the
/// <c>A32NXDisabledMonitorVariables</c> setting because the monitored var set is identical.
///
/// LIVE-VERIFICATION TODO (this is an alpha airframe; names taken from the A32NX fork):
///   • Confirm the MCDU broadcasts on SimBridge ws://localhost:8380/interfaces/v1/mcdu
///     (it shares FBW's sendUpdate path, so the existing FlyByWireMCDUService should work).
///   • Confirm the E/WD memo codes still live on A32NX_Ewd_LOWER_* (the inherited
///     BuildEwdWindowTextAsync degrades to a placeholder if they don't).
///   • Calibrate the glidepath / flare biases below against a coupled ILS autoland.
/// </summary>
public class HeadwindA330Definition : FlyByWireA320Definition
{
    public override string AircraftName => "Headwind Airbus A330-900neo";
    public override string AircraftCode => "HW_A330";

    // The A330's Coherent instruments are A339X-named; the <a339x-mcdu> custom element
    // lives in this view. coherent-a32nx-flightinfo.js queries both element names, so the
    // only thing that changes is which view CoherentEvalClient evaluates against.
    public override string FlightInfoMcduView => "A339X_MCDU";

    // Visual-guidance profile — A330-900neo (widebody). Heavier and faster on approach
    // than the A320, but the same Airbus FBW law and ~3° standard glideslope. AoA / Vref
    // bumped for the widebody; rate caps softened for the larger inertia. The glidepath
    // and flare biases are ESTIMATES pending an in-sim coupled-ILS-autoland calibration
    // (same status as the A320's — do not treat as measured). Flare initiation per the
    // A330 FCTM (~40 ft RA, ~2° pitch increase from the ~3° approach attitude).
    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg     = 4.5,    // widebody approach AoA (lower than the A320's 6°)
        ReferenceVrefKnots        = 145.0,  // typical A330neo Vref
        MaxPitchRateDegPerSec     = 2.0,    // larger inertia → gentler pitch authority
        MaxBankRateDegPerSec      = 2.5,
        GlideslopeAltitudeBiasFt  = 70.0,   // estimate — calibrate vs a coupled ILS autoland
        FlareAltitudeBiasFt       = 30.0,   // estimate
        FlareTriggerWheelHeightFt = 40.0,   // A330 FCTM: flare initiation ~40 ft RA
        FlareTargetPitchDeg       = 5.0     // ~2° increase from the ~3° widebody approach pitch
    };

    // Slightly longer turn-rollout lead than the A320 (1.6 s): the A330's longer
    // wheelbase and slower yaw response sit between the A320 and the A380 (+1.8 override).
    // Conservative single-step bump; tune in-sim if rollouts run long/short.
    public override double TaxiTurnLeadSeconds => 1.7;
}
