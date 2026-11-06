namespace MSFSBlindAssist.Services;

/// <summary>
/// The aircraft's own trim, learned (visual guidance redesign, piece 4): its pitch minus its flight
/// path angle, smoothed over about 6 s, so a push or a pull barely moves it. The vertical cue is
/// desired pitch = this trim − the target descent angle (VG-25): never the vertical speed's
/// instantaneous rate of change, never the live angle of attack. The flight path angle is
/// atan(vertical speed / ground speed), smoothed over about 1 s.
///
/// Starts from the profile's <c>TypicalApproachAoaDeg</c>; the first sample (ground speed 40 kt or
/// more, pitch minus flight path between −5° and 20°) replaces it outright (plan decision 2).
/// </summary>
internal sealed class VisualGuidanceTrimLearner
{
    /// <summary>A sample outside this band (pitch minus flight path, degrees) is a sim glitch and skipped.</summary>
    public const double MinValidDeg = -5.0, MaxValidDeg = 20.0;

    private readonly VisualGuidanceVerticalTuning _tuning;
    private double? _pathAngle;

    public VisualGuidanceTrimLearner(double startDeg, VisualGuidanceVerticalTuning tuning)
    {
        TrimDeg = startDeg;
        _tuning = tuning;
    }

    /// <summary>The learned trim, degrees: pitch minus flight path.</summary>
    public double TrimDeg { get; private set; }

    /// <summary>The smoothed flight path angle, degrees, + climbing; 0 before the first sample.</summary>
    public double PathAngleDeg => _pathAngle ?? 0.0;

    /// <summary>True once a sample has replaced the starting guess.</summary>
    public bool HasData { get; private set; }

    /// <summary>One update; skipped under <see cref="VisualGuidanceVertical.MinGroundSpeedKt"/> or with no number.</summary>
    public void Update(double pitchDeg, double vsFpm, double gsKt, double frameSeconds)
    {
        if (!(gsKt >= VisualGuidanceVertical.MinGroundSpeedKt) || !double.IsFinite(pitchDeg) || !double.IsFinite(vsFpm)) return;
        double dt = double.IsFinite(frameSeconds) ? Math.Max(frameSeconds, 0) : 0;
        _pathAngle = VisualGuidanceTiming.Smooth(_pathAngle, VisualGuidanceVertical.FlightPathAngleDeg(vsFpm, gsKt),
            Math.Exp(-1.0 / _tuning.PathAngleTimeConstantS), dt);
        double sample = pitchDeg - _pathAngle.Value;
        if (sample < MinValidDeg || sample > MaxValidDeg) return;
        TrimDeg = HasData ? VisualGuidanceTiming.Smooth(TrimDeg, sample, Math.Exp(-1.0 / _tuning.TrimTimeConstantS), dt) : sample;
        HasData = true;
    }
}
