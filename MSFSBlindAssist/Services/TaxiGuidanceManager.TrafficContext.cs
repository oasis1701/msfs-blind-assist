using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What the ground traffic monitor needs to know about the taxi in progress: the route ahead
/// (to tell traffic ON the route from traffic beside it, and to count the departure queue), the
/// airport's runways, and the runway(s) the aircraft is holding short of or lining up on.
/// A snapshot — safe to read on another thread after it is returned.
/// </summary>
public sealed class GroundTrafficRouteContext
{
    public required IReadOnlyList<TaxiGraph.RunwayCenterline> Runways { get; init; }

    /// <summary>Bare designators ("27L") of the runway(s) being held short of or lined up on; empty otherwise.</summary>
    public required IReadOnlyList<string> WatchedRunways { get; init; }

    /// <summary>True while lining up on the destination runway (past the hold).</summary>
    public bool IsLiningUp { get; init; }

    /// <summary>
    /// True while guidance is deliberately holding the aircraft: short of a runway, at the end of
    /// a Progressive Taxi leg, or lined up. NOT the same as "stopped" — the pilot is stopped under
    /// an instruction, and the traffic monitor must not prompt them to move up out of it. It is
    /// broader than <see cref="WatchedRunways"/> on purpose: a progressive leg that ends at a
    /// runway hold, or at the end of a taxiway, populates no watched runway but is just as much a
    /// place where "Move up" would be telling a blind pilot to do something ATC has not cleared.
    /// </summary>
    public bool IsHolding { get; init; }

    /// <summary>True when the route ends at a runway (a departure), so a queue can form.</summary>
    public bool IsDepartureRoute { get; init; }

    /// <summary>The route from the current segment onward, capped at <see cref="RouteAheadMaxMetres"/>.</summary>
    public required IReadOnlyList<GroundTrafficRoutePoint> RouteAhead { get; init; }

    /// <summary>
    /// Along-route metres from the aircraft to the END of the route — for a departure that
    /// is the runway holding point, because the route is truncated there. NULL when
    /// <see cref="RouteAheadMaxMetres"/> cut the walk short, i.e. the end is further than
    /// the context looks, which is itself the answer to "is this queue at the runway?".
    /// </summary>
    public double? RouteEndMetres { get; init; }

    public const double RouteAheadMaxMetres = 2500.0;
}

public partial class TaxiGuidanceManager
{
    // The hold label of the runway(s) the aircraft is stopped short of — set by the three hold entries
    // (crossing hold, start hold, destination hold) and cleared by SetState on leaving HoldShort.
    private string? _heldRunwayLabel;

    /// <summary>
    /// The runway a Progressive Taxi leg holds short of, or null when this is not such a leg.
    ///
    /// Two shapes reach the same place. A "hold short of runway" terminator names the runway
    /// outright. A "hold at named holding point" terminator names only the POINT — but a published
    /// holding point usually IS a runway holding point ("taxi to holding point A1" at EGLL is a
    /// 27R departure), so the runway comes from THE NODE'S OWN <c>HoldShortName</c>, the answer
    /// Build already worked out for every hold node, read back with the same
    /// <see cref="RouteRunwayCrossings.ExtractRunwayDesignators"/> the HoldShort branch above uses.
    ///
    /// Reading the label rather than re-deriving the geometry is not tidiness — it is the only
    /// thing that WORKS here. A second <see cref="TaxiGraph.MatchHoldShortRunwayName"/> call at the
    /// 150 m naming tolerance resolves EGLL's A1 (139 m from the 27R centreline) and A4 (138 m) but
    /// silently NOT A3 (158 m) or A2 (186 m) — CAT III holds sit a long way back, and half the
    /// holding points at the airport this exists for would have reported nothing. Build's own
    /// threshold-distance fallback already covers exactly those, so its label names 27R for all
    /// four. The geometry call is kept only for a point resolved onto a node that carries no label
    /// at all (a projection inserted on an edge by NamedHoldingPointResolver), where a nearby
    /// centreline is better than nothing; a point that is genuinely not a runway hold falls
    /// through both and returns null.
    ///
    /// Computed on demand rather than stored: it is derived entirely from the terminator, the
    /// route and the graph, all of which LoadRoute's Fail snapshot already restores together, so
    /// there is no fourth piece of state to leave stale behind a failed route load.
    /// </summary>
    private string? ProgressiveHoldRunway()
    {
        if (_progressiveTerminator is not { } term || _graph == null) return null;

        if (term.Type == ProgressiveTerminatorType.HoldShortRunway)
            return string.IsNullOrWhiteSpace(term.Target) ? null : term.Target;

        if (term.Type != ProgressiveTerminatorType.HoldAtNamedPoint) return null;
        if (_route == null || _route.Segments.Count == 0) return null;

        var point = _route.Segments[^1].ToNode;
        var named = RouteRunwayCrossings.ExtractRunwayDesignators(point.HoldShortName);
        if (named.Count > 0) return named[0];

        return TaxiGraph.MatchHoldShortRunwayName(
            point.Latitude, point.Longitude, _graph.RunwayCenterlines,
            TaxiGraph.HOLDSHORT_RUNWAY_MATCH_M);
    }

    /// <summary>
    /// A snapshot for <see cref="GroundTrafficMonitor"/>, or null when no route is loaded.
    /// </summary>
    public GroundTrafficRouteContext? GetGroundTrafficContext()
    {
        lock (_stateLock)
        {
            if (_route == null || _graph == null || _state == TaxiGuidanceState.Inactive) return null;

            var watched = new List<string>();
            if (_state == TaxiGuidanceState.HoldShort && !string.IsNullOrWhiteSpace(_heldRunwayLabel))
            {
                watched.AddRange(RouteRunwayCrossings.ExtractRunwayDesignators(_heldRunwayLabel));
                if (watched.Count == 0)
                {
                    // A destination label is "Runway 27L" with no "runway" token after it.
                    string bare = RouteRunwayCrossings.StripRunwayPrefix(_heldRunwayLabel);
                    if (RouteRunwayCrossings.FindCenterlineForDesignator(_graph.RunwayCenterlines, bare) != null)
                        watched.Add(RouteRunwayCrossings.NormalizeDesignator(bare));
                }
            }
            // A Progressive Taxi leg that ends at a runway hold is a runway hold like any other:
            // the pilot is stopped at the line waiting for a crossing or a line-up, which is
            // exactly when what is on that runway and on final to it matters most. Only
            // HoldShort populated _heldRunwayLabel, so this was the one hold that reported
            // nothing. Watched at the hold only (not for the whole leg), matching the
            // HoldShort case.
            string? progressiveRunway = _state == TaxiGuidanceState.ProgressiveHold
                ? ProgressiveHoldRunway() : null;
            if (progressiveRunway != null)
            {
                string bare = RouteRunwayCrossings.StripRunwayPrefix(progressiveRunway);
                if (RouteRunwayCrossings.FindCenterlineForDesignator(_graph.RunwayCenterlines, bare) != null)
                    watched.Add(RouteRunwayCrossings.NormalizeDesignator(bare));
            }

            bool liningUp = _state == TaxiGuidanceState.LiningUp && _isRunwayLineup;
            bool holding = _state is TaxiGuidanceState.HoldShort
                                  or TaxiGuidanceState.ProgressiveHold
                                  or TaxiGuidanceState.LiningUp;
            if (liningUp)
            {
                string bare = RouteRunwayCrossings.StripRunwayPrefix(_destinationName);
                if (RouteRunwayCrossings.FindCenterlineForDesignator(_graph.RunwayCenterlines, bare) != null)
                    watched.Add(RouteRunwayCrossings.NormalizeDesignator(bare));
            }

            var ahead = new List<GroundTrafficRoutePoint>();
            double? routeEndMetres = null;
            var segs = _route.Segments;
            int start = Math.Clamp(_currentSegmentIndex, 0, Math.Max(0, segs.Count - 1));
            if (segs.Count > 0 && _state != TaxiGuidanceState.LiningUp)
            {
                double metres = 0;
                var first = segs[start].FromNode;
                ahead.Add(new GroundTrafficRoutePoint(first.Latitude, first.Longitude, segs[start].TaxiwayName, 0));
                int i = start;
                for (; i < segs.Count && metres < GroundTrafficRouteContext.RouteAheadMaxMetres; i++)
                {
                    metres += segs[i].DistanceMeters;
                    var to = segs[i].ToNode;
                    ahead.Add(new GroundTrafficRoutePoint(to.Latitude, to.Longitude, segs[i].TaxiwayName, metres));
                }
                // Only when the walk actually REACHED the end, not when the cap stopped it.
                if (i >= segs.Count) routeEndMetres = metres;
            }

            return new GroundTrafficRouteContext
            {
                Runways = _graph.RunwayCenterlines,
                WatchedRunways = watched,
                IsLiningUp = liningUp,
                IsHolding = holding,
                // A Progressive Taxi leg toward a runway hold forms the same departure queue as a
                // runway-destination route — at EGLL the pilot is typically given "hold A1" long
                // before "line up", and it is precisely then that "number 4 in the queue" is the
                // information they cannot see. Evaluated for the whole leg, not just at the hold.
                IsDepartureRoute = _isRunwayLineup || ProgressiveHoldRunway() != null,
                RouteAhead = ahead,
                RouteEndMetres = routeEndMetres,
            };
        }
    }
}
