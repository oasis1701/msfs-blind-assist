namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Pure state machine for the takeoff V-speed callouts ("V1", "Rotate", "V2") —
/// the spoken equivalent of the aural callouts the PMDGs play natively and the
/// iFly 737 MAX8 and the TFDi MD-11 do not. Each definition feeds it from its
/// ProcessSimVarUpdate: V-speed targets from the aircraft's own vars
/// (IFLY_V1/VR/V2, MD11_V1/VR/V2), airspeed samples from a high-frequency
/// AIRSPEED INDICATED subscription (IFLY_IAS, MD11_IAS — a per-var SIM_FRAME
/// subscription; the 1 Hz batch would call "Rotate" up to a second late),
/// air/ground from the base SIM_ON_GROUND var. Born as IFly737TakeoffCallouts
/// (2026-07-24) and generalized unchanged for the MD-11 (2026-09-07).
///
/// Behavior contract (pinned by TakeoffVSpeedCalloutsTests):
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
/// - V-speeds below the arm threshold are treated as unset (real airliner
///   V-speeds run 90+ kt on a 737 and ~130-170 kt on the MD-11; a sub-40
///   "threshold" could re-fire inside the arm band).
/// - Clearing V1 or VR (FMC route wipe) disarms immediately and silently.
/// - <see cref="Reset"/> disarms and forgets the last sample but KEEPS the
///   speeds. Both definitions call it on a SimConnect reconnect, and on every
///   context reset as well — a flight load included, whose per-frame
///   airspeed lands before the 1 Hz SIM_ON_GROUND, so an arm kept from a
///   parked aircraft called all three at the loaded cruise. The "a landing
///   can never fire" guarantee above holds for a fresh or reset machine, and an
///   arm from before the drop would otherwise survive into a later landing
///   rollout. The speeds stay because the aircraft does not necessarily send
///   them again after a reconnect (the iFly's shared memory fires only on
///   change and its re-seed is an initial snapshot MainForm drops; the MD-11's
///   reset runs after its first batch has already delivered them) — clearing
///   them silenced every callout for the rest of the session.
/// - The callouts one sample crosses are spoken as ONE utterance
///   (<see cref="Compose"/>): both definitions speak them with
///   AnnounceImmediate, which interrupts, so spoken one by one the first was
///   cut off by the next — "V1" by "Rotate" on every take-off with V1 = VR.
/// </summary>
public sealed class TakeoffVSpeedCallouts
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
    // (live-verified 2026-07-24); the MD-11's exports read 0 or TFDi's -999
    // "dashed" sentinel before the FMS has them (which of the two is unmeasured).
    // Sanitize folds all of those — and any other sub-40 garbage — to "unset".
    public void SetV1(double knots) => _v1 = Sanitize(knots);
    public void SetVR(double knots) => _vr = Sanitize(knots);
    public void SetV2(double knots) => _v2 = Sanitize(knots);

    /// <summary>
    /// Forget the roll — the last sample, the arm and the fired flags — but not the speeds. For a
    /// SimConnect reconnect: a fresh arm needs a ground sample below <see cref="ArmBelowKnots"/>
    /// again, so nothing armed before the drop can fire on a later landing; the speeds are kept
    /// because a reconnect does not reliably redeliver them (see the class summary), and a
    /// machine that forgot them went silent for the session.
    /// </summary>
    public void Reset()
    {
        _lastIas = double.NaN;
        _armed = false;
        _firedV1 = _firedVR = _firedV2 = false;
    }

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

    /// <summary>
    /// The callouts one sample crossed (<see cref="ProcessSample"/>'s list, in speaking order) as
    /// ONE utterance: the ones <paramref name="isMuted"/> lets through, joined with ", " — "V1,
    /// Rotate" — or null when none remain (all muted, or none crossed). The definitions speak the
    /// roll calls with AnnounceImmediate, which interrupts: spoken one by one, a second call on the
    /// same sample cut the first off — V1 = VR is routine on a limiting runway, and a sample gap
    /// can span all three. <paramref name="isMuted"/> is the definition's Ctrl+M test for one call;
    /// a call it has no row for must answer false (fail open — a new call is spoken until it has one).
    /// </summary>
    public static string? Compose(IReadOnlyList<string> callouts, Func<string, bool> isMuted)
    {
        List<string>? spoken = null;
        foreach (var callout in callouts)
            if (!isMuted(callout)) (spoken ??= new List<string>(callouts.Count)).Add(callout);
        return spoken == null ? null : string.Join(", ", spoken);
    }

    private static bool Crossed(double last, double now, double threshold) =>
        threshold > 0 && last < threshold && now >= threshold;

    private static void Add(ref List<string>? list, string callout) =>
        (list ??= new List<string>(3)).Add(callout);
}
