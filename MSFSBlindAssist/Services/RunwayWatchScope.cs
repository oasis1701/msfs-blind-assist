using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>How the pilot relates to the watched runway(s); decides whether a runway event interrupts.</summary>
public enum RunwayWatchMode
{
    None,
    Holding,
    OnRunway,
    LiningUp,
    TakeoffWait,
    /// <summary>
    /// On the runway just landed on, turning off it on the landing-exit route: runway traffic is queued
    /// while taxi guidance speaks the exit.
    /// </summary>
    Vacating,
}

/// <summary>
/// One watched runway: <see cref="Designator"/> is the end the pilot is using (what is spoken);
/// <see cref="Key"/> is the runway itself, both ends ("09R/27L") — its identity across the departure.
/// </summary>
public readonly record struct WatchedRunway(string Designator, string Key);

/// <summary>
/// The runway watch's scope for one evaluation. <see cref="Runways"/> is what is scanned;
/// <see cref="IdentityKey"/> — set by <see cref="RunwayWatchScopes.Resolve"/> whenever a REASON
/// (takeoff wait, lineup, backtrack, hold, progressive hold) contributed — is the sorted join of the
/// keys that reason named, and is the watch's <see cref="Key"/>.
/// </summary>
public sealed record RunwayWatch(IReadOnlyList<WatchedRunway> Runways, RunwayWatchMode Mode, string? IdentityKey = null)
{
    public static readonly RunwayWatch None = new(Array.Empty<WatchedRunway>(), RunwayWatchMode.None);

    /// <summary>
    /// The watch's identity: <see cref="IdentityKey"/>, the runways its reason names — or, for a watch
    /// with no reason (the aircraft merely on a runway's pavement), the watched runways' keys, sorted.
    /// Unchanged from the hold through a backtrack, the lineup and the takeoff wait on the same runway,
    /// and through another runway's pavement crossed or stood on meanwhile (which only widens what is
    /// scanned), so none of those restarts the watch (PR #247 final review H3).
    /// </summary>
    public string Key => IdentityKey ?? string.Join(",", Runways.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal));

    public bool IsActive => Runways.Count > 0;

    /// <summary>
    /// On the runway (backtrack, crossing, lineup, takeoff wait, or stopped on it) the first status and
    /// every new occupant or short final interrupt when there is traffic to report; at a hold, and while
    /// vacating after landing (<see cref="RunwayWatchMode.Vacating"/> — taxi guidance is speaking the
    /// exit), they are queued.
    /// </summary>
    public bool RunwayEventsInterrupt =>
        Mode is RunwayWatchMode.OnRunway or RunwayWatchMode.LiningUp or RunwayWatchMode.TakeoffWait;
}

/// <summary>
/// Everything <see cref="RunwayWatchScopes.Resolve"/> needs, as plain values. <see cref="IsLandingExit"/>
/// (taxi guidance is steering a landing-exit route) and <see cref="OwnGroundSpeedKts"/> (the pilot's
/// ground speed, null when not known) tell turning off the runway just landed on
/// (<see cref="RunwayWatchMode.Vacating"/>) from stopping on it; <see cref="VacatingRunwayKey"/> — the
/// key (<see cref="RunwayWatchScopes.RunwayKey"/>) of the runway that was Vacating on the previous
/// evaluation, or null — gives that mode hysteresis, scoped to that one runway only, so an ordinary
/// deceleration through the turn does not flip it tick by tick, and a different runway entered right
/// after the exit is never mistaken for the one that was vacating.
/// </summary>
public readonly record struct RunwayWatchInputs(
    TaxiGuidanceState State,
    string? HeldLabel,
    string? ProgressiveRunway,
    string? DestinationName,
    bool IsRunwayLineup,
    IReadOnlyList<string> RunwaysUnderAircraft,
    string? TakeoffAssistRunway,
    IReadOnlyList<TaxiGraph.RunwayCenterline> Runways,
    bool IsLandingExit = false,
    double? OwnGroundSpeedKts = null,
    string? VacatingRunwayKey = null);

/// <summary>
/// Which runway(s) the ground-traffic runway watch covers. PR #247 review R2/R3: the watch was
/// populated from a field nothing assigned, skipped the backtrack and a crossing in progress, and
/// changed identity at the hold → lineup hand-over, so the lineup restarted it and recorded whatever
/// was on final as already announced. Here every source contributes, the key is the runway itself
/// (the runway the watch's REASON names, when it has one — PR #247 final review H3), and a designator
/// the graph has no centerline for is never watched.
/// </summary>
public static class RunwayWatchScopes
{
    /// <summary>A context is local when the aircraft is within this of any runway end…</summary>
    public const double LocalRunwayRangeM = 8000.0;

    /// <summary>…or within this of any route point (L6: a progressive hold left over from a hand-flown departure).</summary>
    public const double LocalRouteRangeM = 5000.0;

    /// <summary>
    /// On a landing-exit route, the runway under the aircraft is <see cref="RunwayWatchMode.Vacating"/>
    /// only while the pilot is moving at least this fast; stopped on it, it is
    /// <see cref="RunwayWatchMode.OnRunway"/>.
    /// </summary>
    public const double VacatingMinGsKts = 3.0;

    /// <summary>
    /// Once vacating, the mode holds down to this speed, so decelerating through the turn does not flip
    /// it; below it the pilot has stopped on the runway and runway traffic interrupts again.
    /// </summary>
    public const double VacatingHoldGsKts = 1.0;

    /// <summary>
    /// The runway designators a label names: every "runway X" in it (hold labels, "Runway 27L"),
    /// else the label itself as a bare designator ("27L" from takeoff assist or a progressive target).
    /// </summary>
    public static IReadOnlyList<string> Designators(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return Array.Empty<string>();
        var named = RouteRunwayCrossings.ExtractRunwayDesignators(label);
        if (named.Count > 0) return named;
        string bare = RouteRunwayCrossings.StripRunwayPrefix(label);
        return bare.Length == 0
            ? Array.Empty<string>()
            : new[] { RouteRunwayCrossings.NormalizeDesignator(bare) };
    }

    /// <summary>The runway's identity: both ends' normalized names sorted ("09/27"), or the normalized designator when the graph has no such centerline.</summary>
    public static string RunwayKey(IReadOnlyList<TaxiGraph.RunwayCenterline> runways, string designator)
    {
        string d = RouteRunwayCrossings.NormalizeDesignator(RouteRunwayCrossings.StripRunwayPrefix(designator));
        var cl = RouteRunwayCrossings.FindCenterlineForDesignator(runways, d);
        if (cl == null) return d;
        string a = RouteRunwayCrossings.NormalizeDesignator(cl.Name1 ?? "");
        string b = RouteRunwayCrossings.NormalizeDesignator(cl.Name2 ?? "");
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}/{b}" : $"{b}/{a}";
    }

    /// <summary>
    /// The watch for one evaluation. Sources, in precedence order (the first source to add a runway
    /// supplies its spoken designator; the strongest mode wins — TakeoffWait, LiningUp, OnRunway,
    /// Vacating, Holding): takeoff-assist runway (TakeoffWait), runway lineup (LiningUp), backtrack
    /// departure (OnRunway), HoldShort label (Holding), progressive hold runway (Holding), and the
    /// runways under the aircraft, judged PER RUNWAY — <see cref="RunwayWatchMode.Vacating"/> while taxi
    /// guidance steers a landing-exit route (<see cref="RunwayWatchInputs.IsLandingExit"/>) and the pilot
    /// is moving at <see cref="VacatingMinGsKts"/> or more (turning off the runway just landed on), or —
    /// for that SAME runway, once it was already vacating (<see cref="RunwayWatchInputs.VacatingRunwayKey"/>
    /// equals its key) — at <see cref="VacatingHoldGsKts"/> or more (hysteresis: an ordinary deceleration
    /// through the turn does not flip the mode tick by tick), otherwise
    /// <see cref="RunwayWatchMode.OnRunway"/>: a crossing no hold could be placed for, a stray onto a
    /// runway, a DIFFERENT runway entered right after the exit (the hysteresis never carries over to it),
    /// or stopping on the runway after landing.
    ///
    /// <para>The watch's identity (<see cref="RunwayWatch.IdentityKey"/>) comes from its REASON: the
    /// sorted join of the keys the intent sources (takeoff wait, lineup, backtrack, hold, progressive
    /// hold) contributed, whenever any did; null for a watch made only of runways under the aircraft,
    /// which keeps the join of all its runways. A runway added only because the aircraft is on its
    /// pavement still joins <see cref="RunwayWatch.Runways"/> (the scan widens) and still sets the mode
    /// by rank, but never changes the key — so backtracking through an intersection, or lining up or
    /// waiting inside another runway's pavement, no longer restarts the watch (PR #247 final review H3).</para>
    /// </summary>
    public static RunwayWatch Resolve(RunwayWatchInputs input)
    {
        var runways = input.Runways ?? Array.Empty<TaxiGraph.RunwayCenterline>();
        var watched = new List<WatchedRunway>();
        var intentKeys = new SortedSet<string>(StringComparer.Ordinal);
        var mode = RunwayWatchMode.None;

        void Watch(IEnumerable<string> designators, RunwayWatchMode sourceMode, bool intent)
        {
            foreach (string d in designators)
            {
                if (RouteRunwayCrossings.FindCenterlineForDesignator(runways, d) == null) continue;
                string key = RunwayKey(runways, d);
                if (!watched.Any(w => w.Key == key)) watched.Add(new WatchedRunway(d, key));
                if (intent) intentKeys.Add(key);
                if (Rank(sourceMode) > Rank(mode)) mode = sourceMode;
            }
        }

        if (!string.IsNullOrWhiteSpace(input.TakeoffAssistRunway))
            Watch(Designators(input.TakeoffAssistRunway), RunwayWatchMode.TakeoffWait, intent: true);
        if (input.State == TaxiGuidanceState.LiningUp && input.IsRunwayLineup)
            Watch(Designators(input.DestinationName), RunwayWatchMode.LiningUp, intent: true);
        if (input.State == TaxiGuidanceState.BacktrackDeparture)
            Watch(Designators(input.DestinationName), RunwayWatchMode.OnRunway, intent: true);
        if (input.State == TaxiGuidanceState.HoldShort)
            Watch(Designators(input.HeldLabel), RunwayWatchMode.Holding, intent: true);
        if (input.State == TaxiGuidanceState.ProgressiveHold)
            Watch(Designators(input.ProgressiveRunway), RunwayWatchMode.Holding, intent: true);
        // Judged per designator (not one shared mode for the whole set): the hysteresis in
        // VacatingRunwayKey names ONE runway, so a different runway entered right after the exit must
        // not inherit it (PR #247 B4 review Important 2).
        foreach (string d in input.RunwaysUnderAircraft ?? Array.Empty<string>())
        {
            bool vacating = input.IsLandingExit && input.OwnGroundSpeedKts is double gs
                && (gs >= VacatingMinGsKts
                    || (gs >= VacatingHoldGsKts && RunwayKey(runways, d) == input.VacatingRunwayKey));
            Watch(new[] { d }, vacating ? RunwayWatchMode.Vacating : RunwayWatchMode.OnRunway, intent: false);
        }

        string? identity = intentKeys.Count > 0 ? string.Join(",", intentKeys) : null;
        return watched.Count == 0 ? RunwayWatch.None : new RunwayWatch(watched, mode, identity);
    }

    /// <summary>
    /// A watch moving, under the same key, from a mode whose runway lines are queued (Holding, Vacating) into
    /// one whose lines interrupt (OnRunway, LiningUp, TakeoffWait): the queued status may have been cut off
    /// by the very instruction that moved the pilot (Continue at a hold; stopping after a landing exit), so it
    /// is re-armed once, and spoken only if something is on the runway or on short final.
    /// </summary>
    public static bool ShouldRearmOnModeChange(RunwayWatchMode from, RunwayWatchMode to)
        => from is RunwayWatchMode.Holding or RunwayWatchMode.Vacating
           && to is RunwayWatchMode.OnRunway or RunwayWatchMode.LiningUp or RunwayWatchMode.TakeoffWait;

    /// <summary>The runways whose pavement holds the point, each named by its nearer end.</summary>
    public static IReadOnlyList<string> RunwaysUnder(IReadOnlyList<TaxiGraph.RunwayCenterline> runways, double lat, double lon)
    {
        var found = new List<string>();
        if (runways == null) return found;
        foreach (var cl in runways)
        {
            var shape = RunwayShape.For(cl);
            if (shape.IsDegenerate) continue;
            var (along, lateral) = shape.Project(lat, lon);
            if (!shape.ContainsAlongLateral(along, lateral, 0.0)) continue;
            string name = shape.NameAt(along);
            if (name.Length > 0) found.Add(RouteRunwayCrossings.NormalizeDesignator(name));
        }
        return found;
    }

    /// <summary>
    /// True when the aircraft is plausibly at the context's airport: within <see cref="LocalRouteRangeM"/>
    /// of a route point or <see cref="LocalRunwayRangeM"/> of a runway end. False for empty inputs.
    /// </summary>
    public static bool IsLocal(IReadOnlyList<GroundTrafficRoutePoint>? route,
        IReadOnlyList<TaxiGraph.RunwayCenterline>? runways, double lat, double lon)
    {
        if (route != null)
            foreach (var p in route)
                if (DistanceMetres(lat, lon, p.Lat, p.Lon) <= LocalRouteRangeM) return true;
        if (runways != null)
            foreach (var cl in runways)
                if (DistanceMetres(lat, lon, cl.Lat1, cl.Lon1) <= LocalRunwayRangeM
                    || DistanceMetres(lat, lon, cl.Lat2, cl.Lon2) <= LocalRunwayRangeM)
                    return true;
        return false;
    }

    private static double DistanceMetres(double lat1, double lon1, double lat2, double lon2)
        => NavigationCalculator.CalculateDistance(lat1, lon1, lat2, lon2) * GroundTrafficLogic.MetresPerNm;

    private static int Rank(RunwayWatchMode mode) => mode switch
    {
        RunwayWatchMode.TakeoffWait => 5,
        RunwayWatchMode.LiningUp => 4,
        RunwayWatchMode.OnRunway => 3,
        RunwayWatchMode.Vacating => 2,
        RunwayWatchMode.Holding => 1,
        _ => 0,
    };
}
