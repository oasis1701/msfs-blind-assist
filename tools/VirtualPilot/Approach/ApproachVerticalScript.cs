namespace VirtualPilot.Approach;

/// <summary>
/// A pilot obeying ATC's "descend and maintain": from the start the pilot ignores the pitch tone and
/// flies a descent of <paramref name="VsFpm"/> (negative) until <paramref name="LevelAtFt"/> above
/// the threshold, then levels there and follows the tones again. The scenario holds only this
/// description; each run flies its own <see cref="ApproachVerticalScriptRun"/> (<see cref="Start"/>),
/// since the scenario list is built once and flown many times.
/// </summary>
sealed record ApproachVerticalScript(double VsFpm, double LevelAtFt)
{
    /// <summary>A fresh run of this script, for one approach.</summary>
    public ApproachVerticalScriptRun Start() => new(this);
}

/// <summary>
/// One approach's run of an <see cref="ApproachVerticalScript"/>. The pilot holds the pitch that
/// flies the descent rate (the trim angle of attack plus the flight path angle) until the level-off
/// lead: the height the flight-path lag still loses once the nose comes up, |VS| × τγ. From there
/// the pilot maintains the level as a pilot does by the altimeter: the trim angle of attack, which
/// flies level, plus <see cref="CorrectionDegPerFt"/> of the height still to go, at most
/// <see cref="MaxCorrectionDeg"/> either way (the lead leaves the pilot's own pitch-up time out, so a
/// jet levels about 40 ft low without it). Once the aircraft has been within
/// <see cref="LevelBandFt"/> of the level and under <see cref="LevelVsFpm"/> for
/// <see cref="LevelHeldS"/>, the script is done for good and the pilot follows the tones.
/// </summary>
sealed class ApproachVerticalScriptRun
{
    public const double LevelBandFt = 20.0, LevelVsFpm = 50.0, LevelHeldS = 2.0;
    public const double CorrectionDegPerFt = 0.02, MaxCorrectionDeg = 1.0;
    private const double FpmPerKt = 101.269;

    private readonly ApproachVerticalScript _script;
    private bool _levelling, _done;
    private double? _levelSince;

    public ApproachVerticalScriptRun(ApproachVerticalScript script) { _script = script; }

    /// <summary>
    /// The pitch (nose up, degrees) the pilot flies at <paramref name="t"/>, or null once the pilot
    /// follows the tones again. <paramref name="aglAboveThresholdFt"/> is the datum's height above
    /// the threshold, <paramref name="vsFpm"/> the aircraft's vertical speed.
    /// </summary>
    public double? PitchFor(double t, double aglAboveThresholdFt, double vsFpm, double alphaTrimDeg, double tasKt, double tauGammaS)
    {
        if (_done) return null;
        double leadFt = Math.Abs(_script.VsFpm) / 60.0 * tauGammaS;
        if (!_levelling && aglAboveThresholdFt > _script.LevelAtFt + leadFt)
            return alphaTrimDeg + Math.Asin(Math.Clamp(_script.VsFpm / (tasKt * FpmPerKt), -1, 1)) * 180 / Math.PI;
        _levelling = true;
        double toGo = _script.LevelAtFt - aglAboveThresholdFt;
        if (Math.Abs(toGo) <= LevelBandFt && Math.Abs(vsFpm) < LevelVsFpm)
        {
            _levelSince ??= t;
            if (t - _levelSince.Value >= LevelHeldS - 1e-9)
            {
                _done = true;
                return null;
            }
        }
        else _levelSince = null;
        return alphaTrimDeg + Math.Clamp(CorrectionDegPerFt * toGo, -MaxCorrectionDeg, MaxCorrectionDeg);
    }
}
