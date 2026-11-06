namespace MSFSBlindAssist.Services;

/// <summary>
/// When a joining aircraft counts as level (visual guidance redesign, piece 4, "Waiting"). While
/// joining, the altitude is ATC's or the pilot's: while the aircraft climbs or descends the cue sits
/// on its own pitch. Once it has been under <see cref="VisualGuidanceVerticalTuning.LevelVsFpm"/> for
/// <see cref="VisualGuidanceVerticalTuning.LevelHoldS"/>, this holds the altitude of that update, and
/// the cue asks the pilot back from small drifts. A pitch more than
/// <see cref="VisualGuidanceVerticalTuning.HoldBreakPitchDeg"/> away from both the cue the hold asks for
/// (plan decision 9) and the learned level attitude (the spec's "held pitch") ends it at once: following
/// the cue never ends it, nor does the air carrying a pilot who keeps the level pitch off the altitude
/// (the stage-5 final review), while pitching away on purpose does, so a descent ATC clears is never
/// fought. A later level-off is held the same way.
/// </summary>
internal sealed class VisualGuidanceLevelHold
{
    private readonly VisualGuidanceVerticalTuning _tuning;
    private DateTime? _levelSince;

    public VisualGuidanceLevelHold(VisualGuidanceVerticalTuning tuning) { _tuning = tuning; }

    /// <summary>The altitude held (MSL, ft), or null while not holding.</summary>
    public double? HeldAltitudeFt { get; private set; }

    /// <summary>
    /// One update. <paramref name="desiredPitchDeg"/> is the cue the pilot heard: the previous
    /// update's desired pitch; <paramref name="levelAttitudeDeg"/> the learned level attitude (the trim).
    /// </summary>
    public void Update(DateTime now, double altMslFt, double vsFpm, double pitchDeg, double desiredPitchDeg, double levelAttitudeDeg)
    {
        if (HeldAltitudeFt != null)
        {
            if (Math.Abs(pitchDeg - desiredPitchDeg) > _tuning.HoldBreakPitchDeg
                && Math.Abs(pitchDeg - levelAttitudeDeg) > _tuning.HoldBreakPitchDeg) Reset();
            return;
        }
        if (Math.Abs(vsFpm) < _tuning.LevelVsFpm && double.IsFinite(altMslFt))
        {
            _levelSince ??= now;
            if ((now - _levelSince.Value).TotalSeconds >= _tuning.LevelHoldS - 1e-9) HeldAltitudeFt = altMslFt;
        }
        else _levelSince = null;
    }

    /// <summary>Forgets the hold and the level timer (a new decision).</summary>
    public void Reset()
    {
        HeldAltitudeFt = null;
        _levelSince = null;
    }
}
