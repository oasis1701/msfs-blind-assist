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
///
/// <para>The conversion allows for BRAKING. It used to hold the touchdown ground speed flat across
/// the whole sentence, on the one phase of flight that decelerates hardest — overstating the
/// aircraft's reach by hundreds of feet at landing speed. Because each approach callout only fires
/// inside its own distance band, a callout written off that way is never heard at all: a
/// wrong-runway landing with the re-planned exit 2,600 ft ahead wrote off all three approach calls
/// and left the pilot one distance at touchdown and then silence until "turn now". Pure —
/// <c>RolloutCalloutSupersessionTests</c>.</para>
/// </summary>
public static class RolloutCalloutSupersession
{
    /// <summary>Feet per second in one knot. Matches <c>GroundTrafficMonitor</c>'s own.</summary>
    private const double FeetPerSecondPerKnot = 1.6878;
    private const double FeetPerMetre = 1.0 / 0.3048;

    /// <summary>
    /// How far the aircraft actually travels while the sentence is being spoken: decelerating from
    /// <paramref name="groundSpeedKts"/> at <see cref="RolloutExitGate.ComfortableDecelerationMps2"/>
    /// — the same rate the touchdown exit re-plan assumes — down to
    /// <see cref="RolloutExitGate.TaxiGroundSpeedKts"/>, and holding that for the rest of the
    /// sentence.
    ///
    /// <para>Braking toward taxi speed rather than to a stop matters at BOTH ends. At touchdown
    /// speed, holding the speed flat overstates the reach by hundreds of feet; at 22 kt on the
    /// crossing-decline path the aircraft is not braking hard at all, it is taxiing, and assuming
    /// otherwise would understate it. An aircraft already at or below taxi speed simply holds it.</para>
    /// </summary>
    public static double ReachFeet(double groundSpeedKts, double leadSeconds)
    {
        if (groundSpeedKts <= 0.0 || leadSeconds <= 0.0) return 0.0;

        double v = groundSpeedKts * FeetPerSecondPerKnot;
        double vTaxi = Math.Min(RolloutExitGate.TaxiGroundSpeedKts, groundSpeedKts) * FeetPerSecondPerKnot;
        double a = RolloutExitGate.ComfortableDecelerationMps2 * FeetPerMetre;

        double secondsToTaxiSpeed = (v - vTaxi) / a;
        if (leadSeconds <= secondsToTaxiSpeed)
            return v * leadSeconds - 0.5 * a * leadSeconds * leadSeconds;

        double whileBraking = (v * v - vTaxi * vTaxi) / (2.0 * a);
        return whileBraking + vTaxi * (leadSeconds - secondsToTaxiSpeed);
    }

    public static bool Supersedes(
        double distanceAheadFeet, double calloutTriggerFeet,
        double groundSpeedKts, double leadSeconds)
    {
        if (distanceAheadFeet <= calloutTriggerFeet) return true;
        if (groundSpeedKts <= 0.0 || leadSeconds <= 0.0) return false;
        return distanceAheadFeet - calloutTriggerFeet <= ReachFeet(groundSpeedKts, leadSeconds);
    }
}
