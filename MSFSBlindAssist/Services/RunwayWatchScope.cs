using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>How the pilot relates to the watched runway(s); decides whether a runway event interrupts.</summary>
public enum RunwayWatchMode { None, Holding, OnRunway, LiningUp, TakeoffWait }

/// <summary>
/// One watched runway: <see cref="Designator"/> is the end the pilot is using (what is spoken);
/// <see cref="Key"/> is the runway itself, both ends ("09R/27L") — its identity across the departure.
/// </summary>
public readonly record struct WatchedRunway(string Designator, string Key);

/// <summary>
/// The runway watch's scope for one evaluation. <see cref="PositionOnly"/>: every runway in it is
/// watched only because the aircraft is on it — no hold, progressive hold, backtrack, lineup or
/// takeoff wait behind it.
/// </summary>
public sealed record RunwayWatch(IReadOnlyList<WatchedRunway> Runways, RunwayWatchMode Mode, bool PositionOnly = false)
{
    public static readonly RunwayWatch None = new(Array.Empty<WatchedRunway>(), RunwayWatchMode.None);

    /// <summary>
    /// The watch's identity: the watched runways' keys, sorted. Unchanged from the hold through a
    /// backtrack, the lineup and the takeoff wait on the same runway, so none of those hand-overs
    /// restarts the watch.
    /// </summary>
    public string Key => string.Join(",", Runways.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal));

    public bool IsActive => Runways.Count > 0;

    /// <summary>On the runway (backtrack, crossing, lineup, takeoff wait) a new occupant or a short final interrupts; at a hold it queues.</summary>
    public bool RunwayEventsInterrupt =>
        Mode is RunwayWatchMode.OnRunway or RunwayWatchMode.LiningUp or RunwayWatchMode.TakeoffWait;

    /// <summary>
    /// The first status interrupts only when the pilot is on the runway for a REASON (backtrack, lineup,
    /// takeoff wait): a watch started purely because the aircraft is on a runway — typically turning off
    /// after landing, while taxi guidance speaks the exit — gives its first status in turn. New events
    /// after it still follow <see cref="RunwayEventsInterrupt"/>.
    /// </summary>
    public bool FirstStatusInterrupts => RunwayEventsInterrupt && !PositionOnly;
}

/// <summary>Everything <see cref="RunwayWatchScopes.Resolve"/> needs, as plain values.</summary>
public readonly record struct RunwayWatchInputs(
    TaxiGuidanceState State,
    string? HeldLabel,
    string? ProgressiveRunway,
    string? DestinationName,
    bool IsRunwayLineup,
    IReadOnlyList<string> RunwaysUnderAircraft,
    string? TakeoffAssistRunway,
    IReadOnlyList<TaxiGraph.RunwayCenterline> Runways);

/// <summary>
/// Which runway(s) the ground-traffic runway watch covers. PR #247 review R2/R3: the watch was
/// populated from a field nothing assigned, skipped the backtrack and a crossing in progress, and
/// changed identity at the hold → lineup hand-over, so the lineup restarted it and recorded whatever
/// was on final as already announced. Here every source contributes, the key is the runway itself,
/// and a designator the graph has no centerline for is never watched.
/// </summary>
public static class RunwayWatchScopes
{
    /// <summary>A context is local when the aircraft is within this of any runway end…</summary>
    public const double LocalRunwayRangeM = 8000.0;

    /// <summary>…or within this of any route point (L6: a progressive hold left over from a hand-flown departure).</summary>
    public const double LocalRouteRangeM = 5000.0;

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
    /// supplies its spoken designator; the strongest mode wins): takeoff-assist runway (TakeoffWait),
    /// runway lineup (LiningUp), backtrack departure (OnRunway), HoldShort label (Holding),
    /// progressive hold runway (Holding), runways under the aircraft in any state (OnRunway). The
    /// watch is <see cref="RunwayWatch.PositionOnly"/> when that last source is the only one that
    /// contributed a runway (a source naming a runway the graph lacks contributes nothing).
    /// </summary>
    public static RunwayWatch Resolve(RunwayWatchInputs input)
    {
        var runways = input.Runways ?? Array.Empty<TaxiGraph.RunwayCenterline>();
        var watched = new List<WatchedRunway>();
        var mode = RunwayWatchMode.None;
        bool anyIntentSource = false;

        void Watch(IEnumerable<string> designators, RunwayWatchMode sourceMode, bool intent = true)
        {
            foreach (string d in designators)
            {
                if (RouteRunwayCrossings.FindCenterlineForDesignator(runways, d) == null) continue;
                string key = RunwayKey(runways, d);
                if (!watched.Any(w => w.Key == key)) watched.Add(new WatchedRunway(d, key));
                if (Rank(sourceMode) > Rank(mode)) mode = sourceMode;
                if (intent) anyIntentSource = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(input.TakeoffAssistRunway))
            Watch(Designators(input.TakeoffAssistRunway), RunwayWatchMode.TakeoffWait);
        if (input.State == TaxiGuidanceState.LiningUp && input.IsRunwayLineup)
            Watch(Designators(input.DestinationName), RunwayWatchMode.LiningUp);
        if (input.State == TaxiGuidanceState.BacktrackDeparture)
            Watch(Designators(input.DestinationName), RunwayWatchMode.OnRunway);
        if (input.State == TaxiGuidanceState.HoldShort)
            Watch(Designators(input.HeldLabel), RunwayWatchMode.Holding);
        if (input.State == TaxiGuidanceState.ProgressiveHold)
            Watch(Designators(input.ProgressiveRunway), RunwayWatchMode.Holding);
        Watch(input.RunwaysUnderAircraft ?? Array.Empty<string>(), RunwayWatchMode.OnRunway, intent: false);

        return watched.Count == 0 ? RunwayWatch.None : new RunwayWatch(watched, mode, PositionOnly: !anyIntentSource);
    }

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
        RunwayWatchMode.TakeoffWait => 4,
        RunwayWatchMode.LiningUp => 3,
        RunwayWatchMode.OnRunway => 2,
        RunwayWatchMode.Holding => 1,
        _ => 0,
    };
}
