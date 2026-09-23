namespace MSFSBlindAssist.Services;

/// <summary>Why a lingering runway watch was released.</summary>
public enum RunwayLingerVerdict { Keep, ClearFarSide, TurnedAway, TimedOut }

/// <summary>
/// Keeps a runway watch alive across the gap a runway CROSSING leaves between its sources (PR #247 B1
/// review): Continue at a crossing hold ends the Holding source at the hold line, and the
/// on-the-runway source only starts at the pavement edge, so the watch used to stop and restart,
/// speaking a second full first status mid-crossing. When a single-runway watch loses every source it
/// LINGERS — same key, Holding mode (it does not interrupt), never restarted — while the aircraft is
/// still crossing: approaching the runway from where the linger began, on the pavement, or not yet
/// clear on the far side. It is released once the aircraft is clear on the far side, has moved away
/// on the side it started from, or <see cref="MaxLingerSeconds"/> have passed. Lateral offsets are
/// signed metres from the runway centreline (<see cref="Navigation.RunwayShape.Project"/>).
/// </summary>
public static class RunwayWatchLinger
{
    /// <summary>A watch lost farther than this from the centreline does not linger (it was not a crossing).</summary>
    public const double MaxStartLateralM = 250.0;
    /// <summary>On the far side, clear once this far beyond the pavement edge.</summary>
    public const double FarSideClearMarginM = 60.0;
    /// <summary>On the starting side, released once this much farther out than where the linger began.</summary>
    public const double TurnAwayMarginM = 30.0;
    /// <summary>Hard ceiling: a stopped aircraft does not keep a watch alive forever.</summary>
    public const double MaxLingerSeconds = 60.0;

    public readonly record struct Anchor(double StartLateralM, DateTime StartUtc);

    public static bool CanBegin(double lateralM) => Math.Abs(lateralM) <= MaxStartLateralM;

    public static RunwayLingerVerdict Evaluate(Anchor anchor, double lateralM, double halfWidthM, DateTime now)
    {
        if ((now - anchor.StartUtc).TotalSeconds > MaxLingerSeconds) return RunwayLingerVerdict.TimedOut;
        bool farSide = anchor.StartLateralM != 0.0 && lateralM != 0.0
                       && Math.Sign(lateralM) != Math.Sign(anchor.StartLateralM);
        if (farSide)
            return Math.Abs(lateralM) > halfWidthM + FarSideClearMarginM
                ? RunwayLingerVerdict.ClearFarSide : RunwayLingerVerdict.Keep;
        return Math.Abs(lateralM) > Math.Abs(anchor.StartLateralM) + TurnAwayMarginM
            ? RunwayLingerVerdict.TurnedAway : RunwayLingerVerdict.Keep;
    }
}
