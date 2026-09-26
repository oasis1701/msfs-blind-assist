// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Computes the route briefing's expected taxi-out and taxi-in legs on a graph already built for
/// the airport (<see cref="GraphBundle"/>) — the pure half. Uses the same code taxi guidance flies
/// with (TaxiRouter, RouteRunwayCrossings, GetLandingExits, RunwayLineupTarget) so the taxiways it
/// names are the ones Taxi Assist offers later; it never touches TaxiGuidanceManager's state.
/// </summary>
public static partial class TaxiBriefingPlanner
{
    /// <summary>The aircraft counts as "parked at the origin" within this distance of the airport reference point.</summary>
    public const double OwnPositionMaxAirportDistanceMetres = 5000.0;
    /// <summary>…and only when a graph node lies this close to it.</summary>
    public const double OwnPositionMaxNodeDistanceMetres = 150.0;
    /// <summary>A stand resolves to its nearest graph node within this distance (Taxi Assist's gate rule).</summary>
    public const double StandNodeMaxDistanceMetres = 100.0;

    public static TaxiLegBriefing PlanTaxiOut(TaxiBriefingRequest r, GraphBundle g)
    {
        string icao = r.OriginIcao;
        var notes = new List<string>();
        if (g.Note != null) notes.Add(g.Note);

        var rwy = FindRunway(g.Runways, r.OriginRunway);
        if (rwy == null)
            return TaxiLegBriefing.UnavailableLeg(icao, r.OriginRunway, g.Tier,
                $"runway {r.OriginRunway} is not in the navigation database for {icao}", notes: notes);

        int startNode = -1;
        string endpoint = "";
        StandChoice? stand = null;

        if (r.Own is { OnGround: true } own && g.Airport != null &&
            TaxiGraph.FastDistanceMeters(own.Lat, own.Lon, g.Airport.Latitude, g.Airport.Longitude) <= OwnPositionMaxAirportDistanceMetres)
        {
            // A route START from a SENSED position: bridge-only stand stubs excluded, as
            // TaxiGuidanceManager.LoadRoute does, so the position is never snapped onto one.
            var node = g.Graph.FindNearestNode(own.Lat, own.Lon, excludeBridgeOnlyStandStubs: true);
            if (node != null && TaxiGraph.FastDistanceMeters(own.Lat, own.Lon, node.Latitude, node.Longitude) <= OwnPositionMaxNodeDistanceMetres)
            {
                startNode = node.NodeId;
                endpoint = IsStandNode(node) ? $"current position, stand {node.ParkingName}" : "current position";
            }
        }

        if (startNode < 0)
        {
            // SayIntentions never assigns a departure gate — no hint here by design.
            stand = BriefingStandPicker.Pick(g.Spots, r.Aircraft, r.AirlineIcao, siGate: null, s => StandNode(g.Graph, s) != null);
            if (stand == null)
                return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                    $"no stand at {icao} connects to the taxiway network", notes: notes);
            notes.AddRange(stand.Notes);
            startNode = StandNode(g.Graph, stand.Spot)!.NodeId;
            endpoint = $"representative stand {DescribeStand(stand, r.AirlineIcao)}";
        }

        var startNodeObj = g.Graph.Nodes[startNode];
        var startsForRunway = g.Starts.Where(s => RunwayIdsMatch(s.RunwayName, rwy.RunwayID)).ToList();
        var target = RunwayLineupTarget.Resolve(g.Graph, rwy, startsForRunway, startNodeObj.Latitude, startNodeObj.Longitude);
        if (target.EntryNode == null)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no taxiway reaches runway {rwy.RunwayID} in this scenery", stand, endpoint, notes);
        // The aircraft already stands on the node the route would END on: there is nothing to route, and the
        // zero-length "route" must not read as "no taxi route connects current position to runway …".
        if (stand == null && startNode == target.EntryNode.NodeId)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"the aircraft is already at the runway {rwy.RunwayID} entrance", stand, endpoint, notes);

        var route = new TaxiRouter(g.Graph).FindShortestPath(startNode, target.EntryNode.NodeId);
        if (route == null || route.Segments.Count == 0)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no taxi route connects {endpoint} to runway {rwy.RunwayID} in this scenery", stand, endpoint, notes);

        var events = RouteRunwayCrossings.InsertRunwayHoldShorts(route, g.Graph.RunwayCenterlines, $"Runway {rwy.RunwayID}", aircraft: null);
        var holds = CollectHoldShorts(route, events, landedRunway: null, notes);
        // The route ENDS on the departure runway, and the automatic pass never places a stop for a route's
        // own arrival onto its destination strip — so nothing CollectHoldShorts found is this hold (a hold it
        // found for this runway is a CROSSING of it on the way to the threshold). The hold before entering is
        // always real and always the last leg: add it unconditionally.
        holds.Add(new HoldShortNote(rwy.RunwayID, LastNamedTaxiway(route), BeforeEntering: true));

        return new TaxiLegBriefing
        {
            Icao = icao, Runway = rwy.RunwayID, Tier = g.Tier, EndpointDescription = endpoint, Stand = stand,
            Taxiways = RouteTaxiwaySequence.DistinctConsecutive(route.Segments),
            DistanceMetres = route.TotalDistanceMeters, HoldShorts = holds,
            NarrowTaxiways = NarrowTaxiways(route, r.Aircraft), Notes = notes,
        };
    }

    public static TaxiLegBriefing PlanTaxiIn(TaxiBriefingRequest r, GraphBundle g)
    {
        string icao = r.DestinationIcao;
        var notes = new List<string>();
        if (g.Note != null) notes.Add(g.Note);

        var rwy = FindRunway(g.Runways, r.DestinationRunway);
        if (rwy == null)
            return TaxiLegBriefing.UnavailableLeg(icao, r.DestinationRunway, g.Tier,
                $"runway {r.DestinationRunway} is not in the navigation database for {icao}", notes: notes);

        var stand = BriefingStandPicker.Pick(g.Spots, r.Aircraft, r.AirlineIcao, r.ArrivalGate, s => StandNode(g.Graph, s) != null);
        if (stand != null) notes.AddRange(stand.Notes);
        string endpoint = stand == null ? "" : DescribeArrivalStand(stand, r.AirlineIcao);

        var exits = g.Graph.GetLandingExits(rwy);
        LandingExitVacateScreen.Mark(g.Graph, exits, rwy);
        var vacating = exits.Where(e => e.VacatesRunway).OrderBy(e => e.DistanceFromThresholdFeet).ToList();
        var choice = BriefingExitPicker.Pick(exits, r.Aircraft.TouchdownSpeedKts);
        if (choice == null)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no exit taxiway is mapped clear of runway {rwy.RunwayID} in this scenery", stand, endpoint, notes, vacating);
        if (stand == null)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no stand at {icao} connects to the taxiway network", null, endpoint, notes, vacating, choice);

        int from = LandingExitDestination.Resolve(g.Graph, choice.Exit, exits, rwy, rwy.Heading, out _, out _, out _);
        int to = StandNode(g.Graph, stand.Spot)!.NodeId;
        var route = new TaxiRouter(g.Graph).FindShortestPath(from, to);
        if (route == null || route.Segments.Count == 0)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no taxi route connects exit {choice.Exit.TaxiwayName} to {DescribeStand(stand, r.AirlineIcao)} in this scenery",
                stand, endpoint, notes, vacating, choice);

        var events = RouteRunwayCrossings.InsertRunwayHoldShorts(route, g.Graph.RunwayCenterlines, "", aircraft: null);
        var holds = CollectHoldShorts(route, events, landedRunway: rwy.RunwayID, notes);

        return new TaxiLegBriefing
        {
            Icao = icao, Runway = rwy.RunwayID, Tier = g.Tier, EndpointDescription = endpoint, Stand = stand,
            Taxiways = RouteTaxiwaySequence.DistinctConsecutive(route.Segments),
            DistanceMetres = route.TotalDistanceMeters, HoldShorts = holds, Exit = choice, VacatingExits = vacating,
            NarrowTaxiways = NarrowTaxiways(route, r.Aircraft), Notes = notes,
        };
    }

    /// <summary>
    /// Hold-short notes from the route's flagged segments and its start hold. A START hold naming the
    /// runway just landed on (either end) is DISCARDED, as the route-briefing design (§5.6) specifies:
    /// the aircraft has just vacated it. Only the start hold: a hold further along the route naming that
    /// runway is the way in crossing it AGAIN, with landing traffic behind, and is briefed as a crossing
    /// like any other. An event the pass could not hold becomes a note, never a silent gap — a re-crossing
    /// of the landing runway included.
    ///
    /// <para>Every hold found here is a CROSSING (<see cref="HoldShortNote.BeforeEntering"/> false), the
    /// departure runway's included: the automatic pass never places a stop for a route's own arrival onto
    /// its destination strip, so the hold before entering the departure runway is never on the route —
    /// <see cref="PlanTaxiOut"/> adds it. A hold for the departure runway found here is the route crossing
    /// that runway on its way to the threshold, and calling it "before entering" once let it stand in for
    /// the real entry hold, which then went unbriefed — which is why this method is not told the departure
    /// runway at all.</para>
    /// </summary>
    internal static List<HoldShortNote> CollectHoldShorts(TaxiRoute route, IReadOnlyList<TaxiRouteRunwayEvent> events,
                                                          string? landedRunway, List<string> notes)
    {
        var holds = new List<HoldShortNote>();
        if (route.StartHoldRunway is string startHold)
        {
            foreach (var d in RouteRunwayCrossings.ExtractRunwayDesignators(startHold))
            {
                if (landedRunway != null && SameRunway(d, landedRunway)) continue;
                holds.Add(new HoldShortNote(d, NamedTaxiwayAt(route, 0), BeforeEntering: false));
            }
        }
        for (int i = 0; i < route.Segments.Count; i++)
        {
            var seg = route.Segments[i];
            if (!seg.IsHoldShortPoint || string.IsNullOrEmpty(seg.HoldShortRunway)) continue;
            foreach (var d in RouteRunwayCrossings.ExtractRunwayDesignators(seg.HoldShortRunway))
            {
                if (holds.Any(h => SameRunway(h.Runway, d))) continue;
                holds.Add(new HoldShortNote(d, NamedTaxiwayAt(route, i), BeforeEntering: false));
            }
        }
        foreach (var e in events)
        {
            if (e.Held) continue;
            notes.Add($"no hold short point could be placed for runway {e.Designator}; cross with care");
        }
        return holds;
    }

    /// <summary>Advisory: taxiways on the route whose navdata width is below the code letter's Annex 14
    /// minimum. Width 0 (unknown; every OpenStreetMap edge) never produces a note.</summary>
    internal static List<NarrowTaxiwayNote> NarrowTaxiways(TaxiRoute route, AircraftProfile aircraft)
    {
        var result = new List<NarrowTaxiwayNote>();
        double min = AircraftSizeClass.MinTaxiwayWidthMetres(aircraft.CodeLetter);
        if (min <= 0) return result;
        foreach (var group in route.Segments
                     .Where(s => !string.IsNullOrEmpty(s.TaxiwayName) && s.PathWidth > 0)
                     .GroupBy(s => s.TaxiwayName, StringComparer.OrdinalIgnoreCase))
        {
            double widthM = group.Min(s => s.PathWidth) * 0.3048;
            if (widthM < min) result.Add(new NarrowTaxiwayNote(group.Key, widthM, min));
        }
        return result;
    }

    /// <summary>One runway pavement: the same designator (<see cref="RunwayIdsMatch"/>) or its reciprocal.</summary>
    internal static bool SameRunway(string a, string b) =>
        RunwayIdsMatch(a, b) || RunwayIdsMatch(RouteRunwayCrossings.Reciprocal(a), b);

    /// <summary>"9" and "09" are one runway: CleanRunway pads the number; NormalizeDesignator is the fallback for a
    /// compass-point designator it cannot parse.</summary>
    private static string Canon(string d) =>
        SayIntentionsClearanceParser.CleanRunway(d) ?? RouteRunwayCrossings.NormalizeDesignator(d);

    /// <summary>The same runway END, by <see cref="Canon"/> — the ONE identity rule both this and
    /// <see cref="SameRunway"/> use, so a compass-point end ("N", "NE") is found as well as a numbered one.</summary>
    internal static bool RunwayIdsMatch(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(Canon(a), Canon(b), StringComparison.OrdinalIgnoreCase);

    internal static Runway? FindRunway(IReadOnlyList<Runway> runways, string id) =>
        runways.FirstOrDefault(r => RunwayIdsMatch(r.RunwayID, id));

    /// <summary>
    /// A DESTINATION lookup: bridge-only stand stubs must stay reachable, so no exclusion here.
    ///
    /// <para>It is also the taxi-out's START when that leg begins at a representative stand, and that is
    /// deliberate: the route then genuinely starts AT the stand, so the stand's own node — a bridged stub
    /// included — is the right first point (the lead-in and the bridge are unnamed, so the taxiways named do
    /// not change). The route-start exclusion (<see cref="TaxiGraph.IsBridgeOnlyStandStub"/>) guards a
    /// SENSED position being snapped onto a stub; the own-position start in <see cref="PlanTaxiOut"/> passes
    /// it. Excluding stubs here would make a bridged stand unusable in the briefing.</para>
    /// </summary>
    internal static TaxiNode? StandNode(TaxiGraph graph, ParkingSpot spot)
    {
        var node = graph.FindNearestNode(spot.Latitude, spot.Longitude);
        if (node == null) return null;
        return TaxiGraph.FastDistanceMeters(spot.Latitude, spot.Longitude, node.Latitude, node.Longitude) <= StandNodeMaxDistanceMetres ? node : null;
    }

    /// <summary>"C 1 (Ramp Cargo, UPS)" — identity, then the type when known, then the airline when it decided.</summary>
    internal static string DescribeStand(StandChoice stand, string? airlineIcao)
    {
        var parts = new List<string>();
        if (stand.Spot.Type > 1) parts.Add(stand.Spot.GetParkingType());
        if (stand.Source == StandChoiceSource.AirlineMatch && !string.IsNullOrWhiteSpace(airlineIcao)) parts.Add(airlineIcao.Trim().ToUpperInvariant());
        string label = BriefingStandPicker.IdentityLabel(stand.Spot);
        return parts.Count == 0 ? label : $"{label} ({string.Join(", ", parts)})";
    }

    private static string DescribeArrivalStand(StandChoice stand, string? airlineIcao) =>
        stand.Source == StandChoiceSource.SayIntentions
            ? $"SayIntentions assigned gate {BriefingStandPicker.IdentityLabel(stand.Spot)}"
            : $"representative stand {DescribeStand(stand, airlineIcao)}";

    /// <summary>
    /// Whether a node's <see cref="TaxiNode.ParkingName"/> names a STAND. TaxiGraph.Build also writes
    /// "Runway 27" into it for a node near a runway start row, which is no stand; the test is the one
    /// <see cref="TaxiGraph.DescribeLocation"/> applies before it names a gate.
    /// </summary>
    private static bool IsStandNode(TaxiNode node) =>
        node.Type == TaxiNodeType.Parking &&
        !string.IsNullOrEmpty(node.ParkingName) &&
        !node.ParkingName.StartsWith("Runway", StringComparison.OrdinalIgnoreCase);

    private static string NamedTaxiwayAt(TaxiRoute route, int index)
    {
        for (int i = Math.Min(index, route.Segments.Count - 1); i >= 0; i--)
            if (!string.IsNullOrEmpty(route.Segments[i].TaxiwayName)) return route.Segments[i].TaxiwayName;
        for (int i = index + 1; i < route.Segments.Count; i++)
            if (!string.IsNullOrEmpty(route.Segments[i].TaxiwayName)) return route.Segments[i].TaxiwayName;
        return "";
    }

    private static string LastNamedTaxiway(TaxiRoute route) => NamedTaxiwayAt(route, route.Segments.Count - 1);
}
