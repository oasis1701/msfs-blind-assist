namespace VirtualPilot.Approach;

/// <summary>
/// An airliner slowing down through a visual approach, as the owner flies the A320 (2026-11-06):
/// "usually when we're near a visual approach clearance, aircraft is around 210 to 240 knots already,
/// and then it slows to 200, 190, 180, where usually the turns happen. at final, usually aircraft is at
/// 180, then goes to 140 or lower in the final." Pressed at the scenario's TAS, the aircraft slows to
/// <see cref="TurnsKt"/> for the turns, to <see cref="FinalKt"/> once on final, and from
/// <see cref="SlowFromNm"/> to <see cref="SlowToNm"/> from the threshold to <see cref="ApproachKt"/>; at no
/// more than <see cref="DecelKtS"/>, and never faster again. The model aircraft keeps its trim angle of
/// attack at every speed, so this checks the steering and the plan, not the nose attitude flaps change.
/// </summary>
sealed record ApproachSlowing(double TurnsKt = 190, double FinalKt = 180, double ApproachKt = 140,
    double SlowFromNm = 6.0, double SlowToNm = 4.0, double DecelKtS = 0.75)
{
    /// <summary>On final: on the approach side, within this of the centreline and 30° of the course.</summary>
    public const double OnFinalCrossNm = 0.3;
    public const double OnFinalTrackDeg = 30;

    /// <summary>The speed the pilot is slowing to at this point (TAS, kt).</summary>
    public double TargetKt(double alongNm, double crossNm, double relTrackDeg)
    {
        bool onFinal = alongNm > 0 && Math.Abs(crossNm) <= OnFinalCrossNm && Math.Abs(relTrackDeg) <= OnFinalTrackDeg;
        if (!onFinal) return TurnsKt;
        double f = Math.Clamp((alongNm - SlowToNm) / (SlowFromNm - SlowToNm), 0, 1);
        return ApproachKt + (FinalKt - ApproachKt) * f;
    }

    /// <summary>One step from <paramref name="tasKt"/> toward <paramref name="targetKt"/>: slowing at the rate, never speeding up.</summary>
    public double Step(double tasKt, double targetKt, double dt)
        => tasKt > targetKt ? Math.Max(targetKt, tasKt - DecelKtS * dt) : tasKt;
}
