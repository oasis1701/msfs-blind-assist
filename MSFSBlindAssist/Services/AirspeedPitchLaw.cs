namespace MSFSBlindAssist.Services;

/// <summary>
/// The pitch law behind visual guidance's AIRSPEED MODE: the pitch that holds a target
/// indicated airspeed, for approaches where the throttle is not the pilot's to manage.
///
/// Visual guidance's normal vertical law commands the pitch that holds the glidepath and
/// assumes the pilot holds speed with power. On a fixed-power approach — the MSFS 2024
/// career landing lesson locks the throttle; an engine-out glide has no power at all —
/// pitch is the only speed control, so every nose-up the glidepath law asks for trades
/// airspeed for altitude with nothing to buy it back. Measured live in a C172 on
/// 2026-09-22: three lesson attempts, each arriving over the threshold slow and nose-high
/// after the glidepath law asked for +3 to +6.6° inside the last mile.
///
/// This law is the classic light-aircraft technique instead: pitch for speed, power for
/// path. Fast → nose UP; slow → nose DOWN. It is written relative to the CURRENT pitch
/// so the commanded tone always sits a bounded nudge away from the pilot's own tone —
/// the pilot converges on it by ear the same way they do in glidepath mode — and a trend
/// term leads the error so the pilot is not chasing a number that has already overshot.
///
/// Pure and stateless (the trend estimator is the one stateful helper, kept beside it) so
/// the whole thing is pinned by <c>AirspeedPitchLawTests</c> without a simulator.
/// </summary>
public static class AirspeedPitchLaw
{
    /// <summary>
    /// Degrees of pitch per knot of airspeed error. A trimmed 172 on approach changes
    /// about 3–4 kt per degree of pitch, so 0.25°/kt asks for roughly the full
    /// correction in one step, bounded by <see cref="MaxOffsetDeg"/>.
    /// </summary>
    public const double DegPerKnot = 0.25;

    /// <summary>
    /// Degrees of pitch per knot-per-second of airspeed trend. A 2 kt/s acceleration
    /// adds 1° of nose-up ahead of the error it is about to produce.
    /// </summary>
    public const double TrendDampingDegPerKnotPerSec = 0.5;

    /// <summary>
    /// The commanded pitch never sits more than this far from the current pitch, so the
    /// desired tone is always a nudge the pilot can follow and never a lurch. This is the
    /// same role the glidepath law's ±12° absolute clamp plays; the per-aircraft pitch
    /// RATE limit is applied by the caller, after this law, exactly as for glidepath mode.
    /// </summary>
    public const double MaxOffsetDeg = 6.0;

    /// <summary>Lowest target the nudge keys can reach. Below this no fixed-wing aircraft flies.</summary>
    public const double MinTargetKnots = 40.0;

    /// <summary>Highest target the nudge keys can reach.</summary>
    public const double MaxTargetKnots = 200.0;

    /// <summary>What the minus / equals keys move the target by.</summary>
    public const double NudgeKnots = 5.0;

    /// <summary>
    /// The pitch that holds <paramref name="targetKnots"/>, relative to the pitch the
    /// aircraft is at now.
    /// </summary>
    /// <param name="currentPitchDeg">The aircraft's pitch now, standard convention (nose up positive).</param>
    /// <param name="iasKnots">Indicated airspeed now.</param>
    /// <param name="iasTrendKtPerSec">Rate of change of indicated airspeed (positive = accelerating).</param>
    /// <param name="targetKnots">The speed to hold.</param>
    public static double DesiredPitch(double currentPitchDeg, double iasKnots, double iasTrendKtPerSec, double targetKnots)
    {
        double offset = (iasKnots - targetKnots) * DegPerKnot
                      + iasTrendKtPerSec * TrendDampingDegPerKnotPerSec;
        offset = Math.Clamp(offset, -MaxOffsetDeg, MaxOffsetDeg);
        return currentPitchDeg + offset;
    }

    /// <summary>
    /// Clamps a requested target into the flyable range. NaN (an unset or corrupt
    /// reference) lands on the floor rather than propagating into the tone.
    /// </summary>
    public static double ClampTarget(double knots)
    {
        if (double.IsNaN(knots)) return MinTargetKnots;
        return Math.Clamp(knots, MinTargetKnots, MaxTargetKnots);
    }

    /// <summary>
    /// Deviation band inside which the F readout says "on speed" rather than a number.
    /// </summary>
    public const double OnSpeedBandKnots = 1.0;

    /// <summary>
    /// The spoken speed half of the F readout in airspeed mode: "airspeed 62, 3 slow",
    /// "airspeed 65, on speed", or "airspeed unavailable" before the first sample.
    /// </summary>
    public static string DescribeSpeed(double? iasKnots, double targetKnots)
    {
        if (!iasKnots.HasValue) return "airspeed unavailable";
        double error = iasKnots.Value - targetKnots;
        if (Math.Abs(error) < OnSpeedBandKnots)
            return $"airspeed {iasKnots.Value:F0}, on speed";
        return $"airspeed {iasKnots.Value:F0}, {Math.Abs(error):F0} {(error < 0 ? "slow" : "fast")}";
    }

    /// <summary>
    /// The glidepath word appended to the mile callouts in airspeed mode, so a glide that
    /// will not reach the runway is heard early even though the tone no longer says so:
    /// "on profile" inside <paramref name="onProfileBandFt"/>, otherwise the error rounded
    /// to ten feet — "200 high", "150 low".
    /// </summary>
    public static string DescribeProfile(double altitudeErrorFt, double onProfileBandFt)
    {
        if (Math.Abs(altitudeErrorFt) < onProfileBandFt) return "on profile";
        double rounded = Math.Round(Math.Abs(altitudeErrorFt) / 10.0) * 10.0;
        return $"{rounded:F0} {(altitudeErrorFt > 0 ? "high" : "low")}";
    }

    /// <summary>
    /// Smoothed rate of change of indicated airspeed, fed one sample per frame. The
    /// smoothing is an exponential moving average over the raw per-sample slope, heavy
    /// enough to swallow the sim's per-frame airspeed jitter and light enough to settle
    /// on a real 1 kt/s trend within a second at frame rate.
    /// </summary>
    public sealed class TrendEstimator
    {
        private const double SmoothingFactor = 0.9;

        private double? _lastKnots;
        private double? _lastSeconds;
        private double _trend;

        /// <summary>Feeds one sample and returns the smoothed trend in knots per second.</summary>
        /// <param name="iasKnots">Indicated airspeed for this sample.</param>
        /// <param name="seconds">Monotonic timestamp of the sample, in seconds.</param>
        public double Feed(double iasKnots, double seconds)
        {
            if (_lastKnots.HasValue && _lastSeconds.HasValue)
            {
                double dt = seconds - _lastSeconds.Value;
                if (dt > 0.001)
                {
                    double slope = (iasKnots - _lastKnots.Value) / dt;
                    _trend = SmoothingFactor * _trend + (1.0 - SmoothingFactor) * slope;
                }
                else
                {
                    // Same frame twice (or a clock that did not advance): no slope exists.
                    // Keep the previous trend rather than divide by nothing.
                    return _trend;
                }
            }

            _lastKnots = iasKnots;
            _lastSeconds = seconds;
            return _trend;
        }

        /// <summary>Forgets every sample; the next <see cref="Feed"/> starts a new series.</summary>
        public void Reset()
        {
            _lastKnots = null;
            _lastSeconds = null;
            _trend = 0.0;
        }
    }
}
