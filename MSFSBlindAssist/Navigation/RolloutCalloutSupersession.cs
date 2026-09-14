namespace MSFSBlindAssist.Navigation;

/// <summary>
/// "Will this rollout milestone come due while a one-shot sentence is still being spoken?"
/// Shared by the crossing-decline utterance (<see cref="RolloutRunwayReCrossing.DeclineSupersedesCallout"/>)
/// and the touchdown runway-correction sentence (<see cref="TouchdownCallout"/>). When true, the
/// caller marks the milestone announced and folds in whatever it uniquely adds, instead of letting
/// it fire an <c>AnnounceImmediate</c> that cuts the sentence off.
///
/// <para>Two halves, both needed. "Already inside" (<c>distance &lt;= trigger</c>), and a lead
/// that is a TIME converted to distance at the aircraft's own ground speed — so it self-scales with
/// speed, and a stopped aircraft supersedes nothing ahead of it. It is not a speech mute: nothing
/// here silences speech.</para>
/// </summary>
public static class RolloutCalloutSupersession
{
    /// <summary>Feet per second in one knot. Matches <c>GroundTrafficMonitor</c>'s own.</summary>
    private const double FeetPerSecondPerKnot = 1.6878;

    public static bool Supersedes(
        double distanceAheadFeet, double calloutTriggerFeet,
        double groundSpeedKts, double leadSeconds)
    {
        if (distanceAheadFeet <= calloutTriggerFeet) return true;
        if (groundSpeedKts <= 0.0 || leadSeconds <= 0.0) return false;
        return distanceAheadFeet - calloutTriggerFeet
            <= groundSpeedKts * FeetPerSecondPerKnot * leadSeconds;
    }
}
