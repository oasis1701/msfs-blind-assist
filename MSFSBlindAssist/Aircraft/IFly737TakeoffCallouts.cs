namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Pure state machine for the iFly 737 MAX8 takeoff V-speed callouts ("V1",
/// "Rotate", "V2") — the spoken equivalent of the aural callouts other addons
/// (PMDG) play natively and the iFly does not. Fed from the definition's
/// ProcessSimVarUpdate: V-speed targets from the iFly WASM's plain L:vars
/// (IFLY_V1/VR/V2), airspeed samples from the high-frequency AIRSPEED INDICATED
/// subscription (IFLY_IAS), air/ground from the base SIM_ON_GROUND var.
///
/// Behavior contract (pinned by IFly737TakeoffCalloutsTests):
/// - ARMS only on the ground below <see cref="ArmBelowKnots"/> with V1 and VR
///   both set — so connecting mid-roll or mid-flight stays silent, and a landing
///   rollout can never fire (the aircraft reaches the ground already fast, and
///   deceleration crossings are downward anyway; crossings fire on the UPWARD
///   edge only).
/// - "V1" and "Rotate" are ground-roll calls; "V2" may also complete just after
///   liftoff (a normal rotation is airborne before V2).
/// - Each callout fires once per roll. Decelerating back below the arm threshold
///   on the ground (a rejected takeoff) re-arms with fresh flags for the next
///   attempt.
/// - V-speeds below the arm threshold are treated as unset (real 737 V-speeds
///   are 90+ kt; a sub-40 "threshold" could re-fire inside the arm band).
/// - Clearing V1 or VR (FMC route wipe) disarms immediately and silently.
/// </summary>
public sealed class IFly737TakeoffCallouts
{
    /// <summary>Arm/re-arm ceiling: the machine arms only on the ground below this
    /// IAS. Well above taxi jitter, well below any real V-speed.</summary>
    public const double ArmBelowKnots = 40.0;

    /// <summary>Airborne disarm margin past V2 — covers a V2 crossing the sampler
    /// never saw as an upward edge (e.g. first airborne sample already past it).</summary>
    private const double V2DisarmMarginKnots = 20.0;

    private double _v1, _vr, _v2;          // 0 = unset (sanitized)
    private double _lastIas = double.NaN;  // NaN = no sample yet
    private bool _armed;
    private bool _firedV1, _firedVR, _firedV2;

    // The iFly WASM publishes -1 for a V-speed the FMC hasn't computed
    // (live-verified 2026-07-24); Sanitize folds that — and any other sub-40
    // garbage — to "unset".
    public void SetV1(double knots) => _v1 = Sanitize(knots);
    public void SetVR(double knots) => _vr = Sanitize(knots);
    public void SetV2(double knots) => _v2 = Sanitize(knots);

    private static double Sanitize(double knots) =>
        double.IsNaN(knots) || knots < ArmBelowKnots ? 0 : knots;

    /// <summary>
    /// Feed one airspeed sample. Returns the callouts crossed by this sample in
    /// speaking order (usually empty, at most all three after a sample gap).
    /// </summary>
    public IReadOnlyList<string> ProcessSample(double iasKnots, bool onGround)
    {
        if (double.IsNaN(iasKnots) || iasKnots < 0)
            return Array.Empty<string>();

        double last = _lastIas;
        _lastIas = iasKnots;

        if (_v1 <= 0 || _vr <= 0)
        {
            // Speeds unset or cleared mid-roll: silent, and nothing can fire
            // until a fresh arm with speeds present.
            _armed = false;
            _firedV1 = _firedVR = _firedV2 = false;
            return Array.Empty<string>();
        }

        if (onGround && iasKnots < ArmBelowKnots)
        {
            // Arms for the roll; also re-arms fresh after a rejected takeoff
            // decelerates back below the threshold.
            _armed = true;
            _firedV1 = _firedVR = _firedV2 = false;
        }

        List<string>? fired = null;
        if (_armed && !double.IsNaN(last))
        {
            // Ascending-threshold speaking order (V1 <= VR <= V2 on any sane FMC
            // load). V1/VR only make sense with wheels on the runway.
            if (onGround && !_firedV1 && Crossed(last, iasKnots, _v1)) { _firedV1 = true; Add(ref fired, "V1"); }
            if (onGround && !_firedVR && Crossed(last, iasKnots, _vr)) { _firedVR = true; Add(ref fired, "Rotate"); }
            if (!_firedV2 && Crossed(last, iasKnots, _v2)) { _firedV2 = true; Add(ref fired, "V2"); }
        }

        // Airborne and nothing left to call: the roll is over. (A momentary
        // on-ground bounce below V2 keeps the machine armed so the remaining
        // calls still fire — only a genuinely finished takeoff disarms.)
        if (_armed && !onGround && (_v2 <= 0 || _firedV2 || iasKnots > _v2 + V2DisarmMarginKnots))
            _armed = false;

        return (IReadOnlyList<string>?)fired ?? Array.Empty<string>();
    }

    private static bool Crossed(double last, double now, double threshold) =>
        threshold > 0 && last < threshold && now >= threshold;

    private static void Add(ref List<string>? list, string callout) =>
        (list ??= new List<string>(3)).Add(callout);
}
