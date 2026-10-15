using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>Which pilot choice an arming of the rollout centerline cue came from.</summary>
public enum RolloutCueSource { Destination, ExitPlan }

/// <summary>
/// The destination runway dialog's choice: the runway, its airport's ICAO and runway ends, whether the
/// "Manual landing assist" checkbox was ticked (flare and rollout tones, rather than the rollout alone), and
/// the dialog's own airport record.
/// </summary>
public sealed record RolloutCueDestination(Runway Runway, string Icao, IReadOnlyList<Runway> Runways, bool ManualLanding,
    Airport? Airport = null);

/// <summary>
/// The landing exit plan's runway, its airport's ICAO and runway ends, and that airport's record, looked up once
/// when the plan was set (null when the database has none).
/// </summary>
public sealed record RolloutCueExitPlan(Runway Runway, string Icao, IReadOnlyList<Runway> Runways, Airport? Airport = null);

/// <summary>
/// Arm the landing assist on <see cref="Runway"/> at <see cref="Icao"/>, with its flare phase when
/// <see cref="IncludeFlare"/>, or for the rollout alone; <see cref="Airport"/> is the chosen record's airport
/// (null: not in the database, so nothing can be armed).
/// </summary>
public sealed record RolloutCueArmingDecision(RolloutCueSource Source, Runway Runway, string Icao,
    IReadOnlyList<Runway> Runways, bool IncludeFlare, Airport? Airport = null);

/// <summary>
/// Which runway arms the landing assist's rollout centerline cue, and whether with its flare phase.
///
/// <para>The cue plays Takeoff Assist's centerline tone from touchdown to the exit. KSEA 16R, 2026-10-15:
/// an A300 autoland held the centerline to 96 kt, then yawed off the runway with no cue at all, because
/// the cue existed only behind the destination dialog's manual-landing checkbox. With
/// <c>RolloutCenterlineToneEnabled</c> a destination runway or a landing exit plan arms it for the rollout
/// alone; the checkbox still adds the flare tones. With the setting off, the checkbox is the only thing
/// that arms anything, exactly as before.</para>
///
/// <para>Both choices at the same airport arm the destination (and a ticked checkbox keeps its flare
/// tones): the touchdown runway check moves the cue to whichever runway the aircraft actually lands on,
/// so the two runways need not agree. At different airports the pilot's latest choice wins, since only one
/// of the two landings can happen next. Pure — <c>RolloutCueArmingTests</c>.</para>
/// </summary>
public static class RolloutCueArming
{
    public static RolloutCueArmingDecision? Decide(bool settingEnabled, RolloutCueDestination? destination,
        RolloutCueExitPlan? exitPlan, RolloutCueSource latest)
    {
        if (!settingEnabled)
            return destination is { ManualLanding: true } ? FromDestination(destination) : null;

        if (destination != null && exitPlan != null
            && !string.Equals(destination.Icao, exitPlan.Icao, StringComparison.OrdinalIgnoreCase)
            && latest == RolloutCueSource.ExitPlan)
        {
            return FromExitPlan(exitPlan);
        }

        if (destination != null) return FromDestination(destination);
        if (exitPlan != null) return FromExitPlan(exitPlan);
        return null;
    }

    /// <summary>
    /// The same arming: both null, or the same airport (ICAO ignoring case), the same runway end
    /// (<see cref="LandingRunwayMatch.IsSameEnd"/>) and the same flare choice, whichever choice it came
    /// from. MainForm re-arms only when this is false, because arming stops a cue in progress.
    /// </summary>
    public static bool SameArming(RolloutCueArmingDecision? a, RolloutCueArmingDecision? b)
    {
        if (a == null || b == null) return a == null && b == null;
        return string.Equals(a.Icao, b.Icao, StringComparison.OrdinalIgnoreCase)
               && LandingRunwayMatch.IsSameEnd(a.Runway, b.Runway)
               && a.IncludeFlare == b.IncludeFlare;
    }

    /// <summary>
    /// What to say when an arming change the destination dialog did not make (a landing exit plan set or
    /// cleared, or the setting switched) moves the manual landing assist's flare tones: "Manual landing assist
    /// off" when they go, typically to an exit plan for another airport chosen after the checkbox, and "Manual
    /// landing assist armed for {ICAO} runway {id}" when they come back; nothing otherwise. In flare mode
    /// silence means "on profile", so a pilot who ticked the box must never lose the flare tones unheard, nor
    /// get them back unannounced. The dialog speaks its own confirmation. Pure — <c>RolloutCueArmingTests</c>.
    /// </summary>
    public static string? ManualLandingChange(RolloutCueArmingDecision? before, RolloutCueArmingDecision? after)
    {
        bool had = before?.IncludeFlare == true;
        bool has = after?.IncludeFlare == true;
        if (had == has) return null;
        return has
            ? $"Manual landing assist armed for {after!.Icao} runway {after.Runway.RunwayID}"
            : "Manual landing assist off";
    }

    private static RolloutCueArmingDecision FromDestination(RolloutCueDestination d)
        => new(RolloutCueSource.Destination, d.Runway, d.Icao, d.Runways, d.ManualLanding, d.Airport);

    private static RolloutCueArmingDecision FromExitPlan(RolloutCueExitPlan p)
        => new(RolloutCueSource.ExitPlan, p.Runway, p.Icao, p.Runways, IncludeFlare: false, p.Airport);
}
