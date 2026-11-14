using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Decides whether a path is a clean left or right turn, so a Turn pick is only
/// treated as already satisfied by a charted route that really turns that way.
/// </summary>
internal static class TurnShape
{
    /// <summary>The path must end at least this far round to the picked side.</summary>
    public const double MinEndTurnDeg = 20.0;

    /// <summary>A swing toward the other side up to this much is tolerated (a connector's wiggle).</summary>
    public const double MaxContraryDeg = 25.0;

    /// <summary>A single heading change above this is a reversal or hairpin, never a clean turn.</summary>
    public const double MaxSingleChangeDeg = 120.0;

    /// <summary>How far past the start of the row's taxiway the path is judged.</summary>
    public const double WalkPastJoinMeters = 80.0;

    /// <summary>
    /// legs: the path from A onward, each (true bearing, length in metres), in travel order.
    /// joinAtMeters: path distance from A to the start of the first edge on the row's taxiway.
    /// </summary>
    public static bool IsCleanTurn(double incomingBearing,
        IReadOnlyList<(double Bearing, double LengthMeters)> legs, double joinAtMeters, TurnSide side)
    {
        if (legs.Count == 0) return false;

        // Mirror Right onto Left: work with a sign where a turn to the picked side is negative.
        double sign = side == TurnSide.Left ? 1.0 : -1.0;
        double limit = joinAtMeters + WalkPastJoinMeters;

        double running = Normalize(legs[0].Bearing - incomingBearing);
        double maxContrary = sign * running;
        double walked = legs[0].LengthMeters;

        for (int k = 1; k < legs.Count && walked < limit; k++)
        {
            double change = Normalize(legs[k].Bearing - legs[k - 1].Bearing);
            if (Math.Abs(change) > MaxSingleChangeDeg) return false;
            running += change;
            maxContrary = Math.Max(maxContrary, sign * running);
            walked += legs[k].LengthMeters;
        }

        return sign * running <= -MinEndTurnDeg && maxContrary <= MaxContraryDeg;
    }

    /// <summary>Normalizes an angle in degrees into (-180, 180].</summary>
    private static double Normalize(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        else if (deg <= -180.0) deg += 360.0;
        return deg;
    }
}
