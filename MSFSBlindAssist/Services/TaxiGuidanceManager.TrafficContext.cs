using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What the ground traffic monitor needs to know about the taxi in progress, taken under the
/// manager's state lock: the route ahead, the airport's runways, and the facts the runway watch
/// (<see cref="RunwayWatchScopes"/>) and the queue (<c>GroundTrafficLogic.ReadQueue</c>) are derived
/// from. <see cref="Runways"/> is the graph's own list (never mutated once the graph is built);
/// everything else is copied, so the snapshot is safe to read after it is returned.
/// </summary>
public sealed class GroundTrafficRouteContext
{
    public required IReadOnlyList<TaxiGraph.RunwayCenterline> Runways { get; init; }

    /// <summary>The airport the route and runways belong to.</summary>
    public required string AirportIcao { get; init; }

    public required TaxiGuidanceState State { get; init; }

    /// <summary>In HoldShort: the held runway label (<see cref="Navigation.HeldRunwayLabel"/>); otherwise null.</summary>
    public string? HeldRunwayLabel { get; init; }

    /// <summary>The runway a Progressive Taxi leg holds short of, for the whole leg, or null.</summary>
    public string? ProgressiveRunway { get; init; }

    /// <summary>The destination label ("Runway 27L" for a runway destination).</summary>
    public string DestinationName { get; init; } = "";

    /// <summary>The route ends at the takeoff runway — the only route whose queue is the "departure queue".</summary>
    public bool IsRunwayDestination { get; init; }

    /// <summary>A queue can form ahead: a runway destination, or a Progressive leg to a runway hold.</summary>
    public bool IsQueueRoute { get; init; }

    /// <summary>Taxiing on a route the aircraft has joined — the only place a "Move up" prompt may be given.</summary>
    public bool AllowsQueuePrompt { get; init; }

    /// <summary>The route from the START of the current segment onward, capped at <see cref="RouteAheadMaxMetres"/>.</summary>
    public required IReadOnlyList<GroundTrafficRoutePoint> RouteAhead { get; init; }

    /// <summary>
    /// Along-route metres from the FIRST route point — the start of the current segment, NOT the
    /// aircraft — to the route end; the monitor subtracts its own route position (PR #247 review R10:
    /// the two were compared unconverted). NULL when <see cref="RouteAheadMaxMetres"/> cut the walk
    /// short, which is itself the answer to "is this queue at the runway?".
    /// </summary>
    public double? RouteEndMetres { get; init; }

    public const double RouteAheadMaxMetres = 2500.0;
}

public partial class TaxiGuidanceManager
{
    /// <summary>
    /// The runway a Progressive Taxi leg holds short of (<see cref="ProgressiveHoldRunwayResolver"/>),
    /// computed on demand from the terminator, the route and the graph — which LoadRoute's Fail
    /// snapshot restores together, so there is no separate state to leave stale. Caller holds _stateLock.
    /// </summary>
    private string? ProgressiveHoldRunway()
    {
        if (_progressiveTerminator is not { } term || _graph == null) return null;
        var last = _route is { Segments.Count: > 0 } r ? r.Segments[^1].ToNode : null;
        return ProgressiveHoldRunwayResolver.Resolve(term, last?.HoldShortName,
            last?.Latitude, last?.Longitude, _graph.RunwayCenterlines);
    }

    /// <summary>A snapshot for <see cref="GroundTrafficMonitor"/>, or null when no route is loaded.</summary>
    public GroundTrafficRouteContext? GetGroundTrafficContext()
    {
        lock (_stateLock)
        {
            if (_route == null || _graph == null || _state == TaxiGuidanceState.Inactive) return null;

            // The ONE held-runway derivation, shared with the status readout (PR #247 review R2 —
            // this used to read a field nothing ever assigned).
            string? held = _state == TaxiGuidanceState.HoldShort
                ? HeldRunwayLabel.Resolve(_holdShortAtDestination, _destinationName, _currentSegmentIndex,
                    _route.StartHoldRunway, _route.Segments.Select(s => s.HoldShortRunway).ToList())
                : null;
            string? progressive = ProgressiveHoldRunway();

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
                AirportIcao = _icao ?? "",
                State = _state,
                HeldRunwayLabel = held,
                ProgressiveRunway = progressive,
                DestinationName = _destinationName ?? "",
                IsRunwayDestination = _isRunwayLineup,
                IsQueueRoute = _isRunwayLineup || progressive != null,
                AllowsQueuePrompt = _state == TaxiGuidanceState.Taxiing && _hasJoinedRoute,
                RouteAhead = ahead,
                RouteEndMetres = routeEndMetres,
            };
        }
    }
}
