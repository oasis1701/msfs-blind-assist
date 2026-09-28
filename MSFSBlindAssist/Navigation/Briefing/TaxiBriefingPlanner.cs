// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.cs
using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Utils.Logging;

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
    /// <summary>How far down the runway from where a full-length departure begins the route's entrance must be before the
    /// leg says a full-length departure means backtracking: past connector geometry's slop, well short of any real
    /// intersection departure.</summary>
    public const double BacktrackNoteMinMetres = 150.0;
    /// <summary>"current position, stand X" names a stand only when the aircraft is this close to its node: the reach
    /// <see cref="TaxiGraph.DescribeLocation"/> names a stand within (its <c>PARKING_RADIUS_M</c>). The parking pass
    /// stamps a stand's name on a node up to 100 m from it, and the route start may be 150 m from the aircraft.</summary>
    public const double OwnPositionStandMaxMetres = 40.0;

    /// <summary>Whether a point is at the leg's airport: the app's one answer (<see cref="GraphBundle.IsAtAirport"/>,
    /// CurrentAirport.Resolve), else within <see cref="OwnPositionMaxAirportDistanceMetres"/> of the reference point.
    /// The circle alone missed 53 fs2024 stands beyond 5 km (OMDW: 21; UNNT G35 at 5,028 m).</summary>
    internal static bool AtAirport(GraphBundle g, double lat, double lon) =>
        g.IsAtAirport?.Invoke(lat, lon)
        ?? (g.Airport != null && TaxiGraph.FastDistanceMeters(lat, lon, g.Airport.Latitude, g.Airport.Longitude) <= OwnPositionMaxAirportDistanceMetres);

    public static TaxiLegBriefing PlanTaxiOut(TaxiBriefingRequest r, GraphBundle g) =>
        WithAirportTaxiways(PlanTaxiOutLeg(r, g), g);

    public static TaxiLegBriefing PlanTaxiIn(TaxiBriefingRequest r, GraphBundle g) =>
        WithAirportTaxiways(PlanTaxiInLeg(r, g), g);

    /// <summary>Every taxiway name on the graph a leg is planned on — exactly the names its route could use: the
    /// scenery's, with OpenStreetMap-filled names, or the OpenStreetMap graph's on that tier. Edge names only, never
    /// the graph's online alias labels. Sorted ignoring case.</summary>
    internal static IReadOnlyList<string> AirportTaxiwayNames(TaxiGraph graph) =>
        graph.Adjacency.Values.SelectMany(edges => edges)
            .Select(e => e.TaxiwayName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static TaxiLegBriefing WithAirportTaxiways(TaxiLegBriefing leg, GraphBundle g)
    {
        leg.AirportTaxiways = AirportTaxiwayNames(g.Graph);
        return leg;
    }

    private static TaxiLegBriefing PlanTaxiOutLeg(TaxiBriefingRequest r, GraphBundle g)
    {
        string icao = r.OriginIcao;
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.OriginRunwayNote)) notes.Add(r.OriginRunwayNote);
        if (g.Note != null) notes.Add(g.Note);

        if (string.IsNullOrWhiteSpace(r.OriginRunway))
            return TaxiLegBriefing.UnavailableLeg(icao, r.OriginRunway, g.Tier, "the flight plan names no departure runway", notes: notes);
        var rwy = FindRunway(g.Runways, r.OriginRunway);
        if (rwy == null)
            return TaxiLegBriefing.UnavailableLeg(icao, r.OriginRunway, g.Tier,
                $"runway {r.OriginRunway} is not in the navigation database for {icao}", notes: notes);
        if (rwy.IsClosed)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier, $"runway {rwy.RunwayID} is marked closed in this scenery", notes: notes);

        int startNode = -1;
        string endpoint = "";
        StandChoice? stand = null;
        int network = NetworkComponentId(g.Graph);
        // A piece of the taxi network other than the largest is a leg's start when it reaches this runway itself — the
        // entrance resolved from one of its nodes lies on it, AND on the runway pavement (LegEndNode, EntersTheRunway) —
        // judged once per piece.
        var reachesByPiece = new Dictionary<int, bool>();
        bool ReachesTheRunway(TaxiNode anchor)
        {
            if (!reachesByPiece.TryGetValue(anchor.ComponentId, out bool reaches))
            {
                var (target, entry) = RunwayEntry(g, rwy, anchor);
                reachesByPiece[anchor.ComponentId] = reaches =
                    entry != null && entry.ComponentId == anchor.ComponentId && EntersTheRunway(rwy, target, entry);
            }
            return reaches;
        }

        if (r.Own is { OnGround: true } own && AtAirport(g, own.Lat, own.Lon))
        {
            // A route START from a SENSED position: on the leg's taxi network, and bridge-only stand stubs excluded, as
            // TaxiGuidanceManager.LoadRoute does, so the position is never snapped onto an island or a stub.
            var node = LegEndNode(g.Graph, own.Lat, own.Lon, OwnPositionMaxNodeDistanceMetres, network, asRouteStart: true, ReachesTheRunway);
            if (node != null)
            {
                startNode = node.NodeId;
                endpoint = IsStandNode(node) &&
                           TaxiGraph.FastDistanceMeters(own.Lat, own.Lon, node.Latitude, node.Longitude) <= OwnPositionStandMaxMetres
                    ? $"current position, stand {node.ParkingName}" : "current position";
            }
        }

        if (startNode < 0)
        {
            // The stand is the route's START: on the network, never a bridge-only stand stub.
            bool HasStandNode(ParkingSpot s) => StandNode(g.Graph, s, network, asRouteStart: true, ReachesTheRunway) != null;
            // SayIntentions never assigns a departure gate — no hint here by design.
            stand = BriefingStandPicker.Pick(g.Spots, r.Aircraft, r.AirlineIcao, siGate: null, HasStandNode, r.Unit);
            if (stand == null)
                return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                    BriefingStandPicker.NoStandReason(g.Spots, HasStandNode, icao), notes: notes);
            AddStandNotes(notes, stand, g.Note);
            startNode = StandNode(g.Graph, stand.Spot, network, asRouteStart: true, ReachesTheRunway)!.NodeId;
            endpoint = $"representative stand {DescribeStand(stand, r.AirlineIcao)}";
        }

        var startNodeObj = g.Graph.Nodes[startNode];
        var (target, entry) = RunwayEntry(g, rwy, startNodeObj);
        var frame = RunwayFrame.For(rwy, rwy.StartLat);
        if (entry == null)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no taxiway reaches runway {rwy.RunwayID} in this scenery", stand, endpoint, notes);
        // The aircraft already stands on the node the route would END on: there is nothing to route, and the
        // zero-length "route" must not read as "no taxi route connects current position to runway …".
        if (stand == null && startNode == entry.NodeId)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"the aircraft is already at the runway {rwy.RunwayID} entrance", stand, endpoint, notes);

        var route = new TaxiRouter(g.Graph).FindShortestPath(startNode, entry.NodeId);
        if (route == null || (route.Segments.Count == 0 && startNode != entry.NodeId))
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no taxi route connects {endpoint} to runway {rwy.RunwayID} in this scenery", stand, endpoint, notes);

        // The route enters the runway well down from where a full-length departure begins: say so, or the block reads as
        // a full-length departure from the first taxiway to the threshold. Written before the route with no taxiways
        // returns (re-review N-4): a stand whose node IS a downfield entrance backtracks too.
        double entryAlong = frame.Along(entry.Latitude, entry.Longitude) - frame.Along(target.LineupLat, target.LineupLon);
        if (entryAlong > BacktrackNoteMinMetres)
        {
            string entering = LastNamedTaxiway(route) is { Length: > 0 } via ? $"the route enters it on taxiway {via}" : "the route enters it";
            notes.Add($"no taxiway meets runway {rwy.RunwayID} where a full-length departure begins in this scenery: {entering}, " +
                      $"{TaxiBriefingRenderer.FormatAlongRunway(entryAlong / 0.3048, r.Unit)} along, so a full-length departure " +
                      "means backtracking on the runway");
        }
        // A stand whose node IS the runway entrance leads straight onto the runway: a route with no taxiways, not "no
        // route" — the taxi-in's rule for an exit leading straight onto its stand. (The aircraft standing there
        // returned "already at the entrance" above.) Only the hold before entering is on it.
        if (route.Segments.Count == 0)
            return new TaxiLegBriefing
            {
                Icao = icao, Runway = rwy.RunwayID, Tier = g.Tier, EndpointDescription = endpoint, Stand = stand,
                HoldShorts = new[] { new HoldShortNote(rwy.RunwayID, "", BeforeEntering: true) }, Notes = notes,
            };

        // The aircraft is the route's first point (RouteRunwayCrossings' contract): a route from its nearest node, which can
        // lie ON a runway, otherwise opens on that runway and never reports crossing it. A stand start has no sensed
        // position, so it keeps the pass's position-free rules.
        RouteRunwayCrossings.AircraftPosition? sensed = stand == null && r.Own is { } at
            ? new RouteRunwayCrossings.AircraftPosition(at.Lat, at.Lon) : null;
        var events = RouteRunwayCrossings.InsertRunwayHoldShorts(route, g.Graph.RunwayCenterlines, $"Runway {rwy.RunwayID}", aircraft: sensed);
        var unheldRunways = new List<string>();
        var holds = CollectHoldShorts(route, events, notes, unheldRunways);
        // The route ENDS on the departure runway, and the automatic pass never places a stop for a route's
        // own arrival onto its destination strip — so nothing CollectHoldShorts found is this hold (a hold it
        // found for this runway is a CROSSING of it on the way to the threshold). The hold before entering is
        // always real and always the last leg: add it unconditionally.
        holds.Add(new HoldShortNote(rwy.RunwayID, LastNamedTaxiway(route), BeforeEntering: true));

        var taxiways = RouteTaxiwaySequence.DistinctConsecutive(route.Segments);
        return new TaxiLegBriefing
        {
            Icao = icao, Runway = rwy.RunwayID, Tier = g.Tier, EndpointDescription = endpoint, Stand = stand,
            Taxiways = taxiways,
            TaxiwayTurns = AlignedTurns(taxiways, BriefingTurns.TaxiwayTurns(route.Segments)),
            DistanceMetres = route.TotalDistanceMeters, HoldShorts = holds, UnheldRunways = unheldRunways,
            NarrowTaxiways = NarrowTaxiways(route, r.Aircraft), Notes = notes,
        };
    }

    private static TaxiLegBriefing PlanTaxiInLeg(TaxiBriefingRequest r, GraphBundle g)
    {
        string icao = r.DestinationIcao;
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.DestinationRunwayNote)) notes.Add(r.DestinationRunwayNote);
        if (g.Note != null) notes.Add(g.Note);

        if (string.IsNullOrWhiteSpace(r.DestinationRunway))
            return TaxiLegBriefing.UnavailableLeg(icao, r.DestinationRunway, g.Tier, "the flight plan names no arrival runway", notes: notes);
        var rwy = FindRunway(g.Runways, r.DestinationRunway);
        if (rwy == null)
            return TaxiLegBriefing.UnavailableLeg(icao, r.DestinationRunway, g.Tier,
                $"runway {r.DestinationRunway} is not in the navigation database for {icao}", notes: notes);
        if (rwy.IsClosed)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier, $"runway {rwy.RunwayID} is marked closed in this scenery", notes: notes);

        // A parking-service gate whose position is published is refused when that position is not
        // AtAirport(g, …): it then names somewhere else. With no position it is
        // looked up by NAME in this airport's scenery, as a flight-file gate is (owner, 2026-09-26, reversing that
        // morning's refusal): live KMEM→KATL, getParking answered "B3" with no position and the flight file said
        // "Gate B3" nine seconds later with the aircraft at KMEM Gate 17 — the service meant the ARRIVAL gate, and
        // refusing it briefed a representative stand the pilot was never assigned.
        var arrivalGate = r.ArrivalGate;
        bool matchedByNameOnly = false;
        string? refused = null;
        if (arrivalGate is { Source: SayIntentionsGateSource.ParkingService })
        {
            if (arrivalGate.Position is not GeoPoint pin)
                matchedByNameOnly = true;
            else if (!AtAirport(g, pin.Latitude, pin.Longitude))
            {
                refused = $"SayIntentions' parking service named {arrivalGate.Label}, but its position is not at {icao}";
                arrivalGate = null;
            }
        }

        IReadOnlyList<LandingExit> exits = g.Graph.GetLandingExits(rwy);
        LandingExitVacateScreen.Mark(g.Graph, exits, rwy);
        var routeStarts = BriefableExitRouteStarts(g.Graph, exits, rwy);

        // The stand is the route's DESTINATION: on the leg's taxi network, and a bridged stand stays reachable. A piece
        // other than the largest is the leg's when a briefable exit's route begins on it (LegEndNode).
        int network = NetworkComponentId(g.Graph);
        var exitPieces = routeStarts.Values.Select(n => g.Graph.Nodes[n].ComponentId).ToHashSet();
        bool AnExitReaches(TaxiNode node) => exitPieces.Contains(node.ComponentId);
        bool HasStandNode(ParkingSpot s) => StandNode(g.Graph, s, network, asRouteStart: false, AnExitReaches) != null;
        var stand = BriefingStandPicker.Pick(g.Spots, r.Aircraft, r.AirlineIcao, arrivalGate, HasStandNode, r.Unit);
        // "Using a representative stand instead" only when one is: 205 fs2024 airports have taxi paths and no parking.
        if (refused != null) notes.Add(stand != null ? refused + "; using a representative stand instead" : refused);
        if (matchedByNameOnly && stand?.Source == StandChoiceSource.SayIntentions)
            notes.Add($"SayIntentions' parking service gave no position for {arrivalGate!.Label}, so it was matched by name in this scenery");
        if (stand != null) AddStandNotes(notes, stand, g.Note);
        string endpoint = stand == null ? "" : DescribeArrivalStand(stand, r.AirlineIcao);

        // Each candidate's way in is planned at most once: the picker asks about the exits in its preference order
        // and stops at the first that stays clear of the runway just landed on, and the chosen one is then briefed
        // from the same plan.
        var standNode = stand == null ? null : StandNode(g.Graph, stand.Spot, network, asRouteStart: false, AnExitReaches);
        var inbound = new Dictionary<LandingExit, InboundRoute?>();
        InboundRoute? WayIn(LandingExit exit)
        {
            if (!inbound.TryGetValue(exit, out var planned))
                inbound[exit] = planned = PlanInbound(g.Graph, routeStarts[exit], standNode!.NodeId, rwy);
            return planned;
        }

        double aim = BriefingExitPicker.AimPointFeet(rwy);
        ExitChoice? PickExit() =>
            BriefingExitPicker.Pick(exits.Where(routeStarts.ContainsKey).ToList(), r.Aircraft.TouchdownSpeedKts,
                standNode == null ? null : e => WayIn(e) switch
                {
                    null => ExitRoute.None,
                    { CrossesLandingRunway: true } => ExitRoute.CrossesLandingRunway,
                    _ => ExitRoute.Clear,
                }, aim);
        var choice = PickExit();

        // GetLandingExits is lossy on purpose — one entry per name, and one marked hold short switches its geometric
        // fallback off for the whole runway — so before the block says no exit is comfortable, or that none follows the
        // briefed one, the graph is asked, as the rollout asks it before "Missed last exit" (FindDownfieldExits, merged
        // by RolloutExitGate.MergeRescueExits). KCOS: the list ended at 821 ft with 10,172 ft and taxiway H still ahead.
        double? rescueFrom = choice == null || !choice.ComfortablyReachable ? aim
                           : choice.NextExit == null ? choice.Exit.DistanceFromThresholdFeet : null;
        if (rescueFrom is double from)
        {
            var rescued = g.Graph.FindDownfieldExits(rwy, from);
            if (rescued.Count > 0)
            {
                exits = RolloutExitGate.MergeRescueExits(exits, rescued);
                LandingExitVacateScreen.Mark(g.Graph, exits, rwy);
                var previousStarts = routeStarts;
                routeStarts = BriefableExitRouteStarts(g.Graph, exits, rwy);
                // MergeRescueExits keeps the known instances, but a known exit's route start is resolved against the
                // whole list, so a way in planned from a start that has since moved is planned again.
                foreach (var known in inbound.Keys.ToList())
                    if (!routeStarts.TryGetValue(known, out int now) || previousStarts[known] != now)
                        inbound.Remove(known);
                // A comfortable choice asked only what follows it: the rescued exits answer that and nothing else, so a
                // rescued exit inside the preference window (a high-speed one, say) never replaces the exit already
                // briefed. A choice that was not comfortable, or that the merged list no longer briefs, is made again.
                choice = choice is { ComfortablyReachable: true } kept && routeStarts.ContainsKey(kept.Exit)
                    ? kept with { NextExit = BriefingExitPicker.NextExit(exits.Where(routeStarts.ContainsKey).ToList(), kept.Exit) }
                    : PickExit();
            }
        }
        var vacating = exits.Where(e => e.VacatesRunway).OrderBy(e => e.DistanceFromThresholdFeet).ToList();
        // From here on the exits have been searched: every leg says so, "none found" included.
        if (choice == null)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                NoExitReason(icao, rwy.RunwayID, vacating.Count, routeStarts.Count, AirportTaxiwayNames(g.Graph).Count > 0),
                stand, endpoint, notes, vacating, exitsSearched: true);
        choice = WithReachableExitsSetAside(choice, vacating, routeStarts.Keys, r.Aircraft.TouchdownSpeedKts, aim);
        if (!choice.ComfortablyReachable)
            choice = choice with
            {
                RunwayLength = rwy.Length <= 0 ? UnreachableRunway.LengthUnknown
                    : RunwayLongEnoughToStop(rwy, r.Aircraft.TouchdownSpeedKts, aim) ? UnreachableRunway.LongEnoughToBacktrack
                    : UnreachableRunway.Short,
            };
        if (stand == null)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                BriefingStandPicker.NoStandReason(g.Spots, HasStandNode, icao), null, endpoint, notes, vacating, choice, exitsSearched: true);

        if (WayIn(choice.Exit) is not { } way)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no taxi route connects exit {TaxiBriefingRenderer.ExitName(choice.Exit)} to {DescribeStand(stand, r.AirlineIcao)} in this scenery",
                stand, endpoint, notes, vacating, choice, exitsSearched: true);

        notes.AddRange(way.UnheldNotes);
        if (TaxiwayLeavingTheRunway(g.Graph, choice.Exit, routeStarts[choice.Exit], rwy) is string leaving)
            notes.Add($"the mapped route leaves the runway on taxiway {leaving}");

        var taxiways = RouteTaxiwaySequence.DistinctConsecutive(way.Route.Segments);
        return new TaxiLegBriefing
        {
            Icao = icao, Runway = rwy.RunwayID, Tier = g.Tier, EndpointDescription = endpoint, Stand = stand,
            Taxiways = taxiways,
            TaxiwayTurns = AlignedTurns(taxiways, BriefingTurns.TaxiwayTurns(way.Route.Segments)),
            StandTurn = BriefingTurns.StandTurn(way.Route.Segments),
            DistanceMetres = way.Route.TotalDistanceMeters, HoldShorts = way.Holds, UnheldRunways = way.UnheldRunways,
            Exit = choice, VacatingExits = vacating,
            ExitsSearched = true, NarrowTaxiways = NarrowTaxiways(way.Route, r.Aircraft), Notes = notes,
        };
    }

    /// <summary>
    /// Why no exit is briefed, naming what is really missing: the scenery names no taxiway at all (the exits may exist,
    /// unnamed); no exit gets clear of the runway; or every one that does was set aside by
    /// <see cref="BriefableExitRouteStarts"/> (its route leaves by the other side), while the block's exits line lists them.
    /// </summary>
    internal static string NoExitReason(string icao, string runwayId, int vacatingCount, int briefableCount, bool airportNamesTaxiways)
    {
        if (!airportNamesTaxiways)
            return $"this scenery names none of {icao}'s taxiways, so no exit off runway {runwayId} can be named";
        if (vacatingCount > 0 && briefableCount == 0)
            return $"every exit clear of runway {runwayId} in this scenery has its mapped route leave the runway on the other side " +
                   "from the one it turns toward, so none is briefed";
        return $"no exit taxiway is mapped clear of runway {runwayId} in this scenery";
    }

    /// <summary>A way in from one exit to the stand: the route with its hold-short points placed, what the block
    /// says about them, the designators of any it could not hold, and whether it crosses the runway just landed
    /// on.</summary>
    private sealed record InboundRoute(TaxiRoute Route, List<HoldShortNote> Holds, List<string> UnheldNotes,
                                       List<string> UnheldRunways, bool CrossesLandingRunway);

    /// <summary>
    /// The shortest route from an exit's route start to the stand, with its holds placed exactly as the leg briefs
    /// them, or null when none connects. It crosses the runway just landed on when any hold on it, or any runway
    /// passage the automatic pass reported (held or not), names that runway at either end — every briefed exit
    /// gets clear of that runway, so the route starts clear of it and a passage naming it can only be a crossing.
    /// An exit whose route begins ON the stand's node is a route with no taxiways, never "no route".
    /// </summary>
    private static InboundRoute? PlanInbound(TaxiGraph graph, int from, int to, Runway landing)
    {
        if (graph.Nodes[from].ComponentId != graph.Nodes[to].ComponentId) return null;   // no A* drain across components
        var route = new TaxiRouter(graph).FindShortestPath(from, to);
        if (route == null) return null;
        // The exit's route begins ON the stand's node: the exit leads straight onto the stand — a route with no
        // taxiways, not "no route" (N16 exit A to stand P 1).
        if (route.Segments.Count == 0)
            return new InboundRoute(route, new List<HoldShortNote>(), new List<string>(), new List<string>(), CrossesLandingRunway: false);
        var events = RouteRunwayCrossings.InsertRunwayHoldShorts(route, graph.RunwayCenterlines, "", aircraft: null);
        var unheldNotes = new List<string>();
        var unheldRunways = new List<string>();
        var holds = CollectHoldShorts(route, events, unheldNotes, unheldRunways);
        bool crosses = holds.Any(h => SameRunway(h.Runway, landing.RunwayID)) ||
                       events.Any(e => SameRunway(e.Designator, landing.RunwayID));
        return new InboundRoute(route, holds, unheldNotes, unheldRunways, crosses);
    }

    /// <summary>
    /// The exits the briefing may name, each with the node its taxi-in route begins at — the node the rollout
    /// hands over at (<see cref="LandingExitDestination.Resolve"/>, the same resolution the vacate screen judged).
    ///
    /// <para>An exit is left out when that node lies on the OTHER side of the runway from the side the exit turns
    /// toward. The resolution can hand an exit another exit's clear-of-runway node — the furthest same-named exit
    /// further down, or the corridor search's first node clear of the strip — and that node can be across the
    /// runway: KLAX 25L briefed "A7, LEFT side" with the route starting on H6 north of the runway, and 33 of 428
    /// hub arrivals measured the same way. The pilot is told to repeat the side exactly, so a briefed side must be
    /// the side the route leaves by. (The resolution itself is shared with the rollout and is not changed here.)
    /// An exit that names no side takes the side its route leaves by; one whose side cannot be told — its route
    /// does not begin clear of the runway pavement — is left out.</para>
    ///
    /// <para>Only exits that get clear of the runway are offered (<see cref="LandingExit.VacatesRunway"/>).</para>
    /// </summary>
    internal static Dictionary<LandingExit, int> BriefableExitRouteStarts(TaxiGraph graph, IReadOnlyList<LandingExit> exits, Runway rwy)
    {
        var starts = new Dictionary<LandingExit, int>();
        foreach (var exit in exits)
        {
            if (!exit.VacatesRunway) continue;
            int node = LandingExitDestination.Resolve(graph, exit, exits, rwy, rwy.Heading, out _, out _, out _);
            string? side = SideOfRunway(graph, node, rwy);
            if (side == null) continue;
            if (string.IsNullOrEmpty(exit.ExitSide)) exit.ExitSide = side;
            else if (!string.Equals(exit.ExitSide, side, StringComparison.OrdinalIgnoreCase)) continue;
            starts[exit] = node;
        }
        return starts;
    }

    /// <summary>
    /// A choice that is not comfortably reachable, with the comfortably reachable exits that
    /// <see cref="BriefableExitRouteStarts"/> set aside attached (<see cref="ExitChoice.ReachableExitsSetAside"/>): with
    /// any of those the runway is not short, and the block says why they are not briefed instead. KPHL 17 and KSFO
    /// 01L: every exit a 737 can make comfortably leads off the other side in the scenery (2 of 428 hub arrivals).
    /// </summary>
    internal static ExitChoice WithReachableExitsSetAside(ExitChoice choice, IReadOnlyList<LandingExit> vacating,
                                                          ICollection<LandingExit> briefable, double touchdownSpeedKts,
                                                          double aimFeet)
    {
        if (choice.ComfortablyReachable) return choice;
        var setAside = vacating
            .Where(e => !briefable.Contains(e) && e.ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg &&
                        BriefingExitPicker.IsComfortablyReachable(e, touchdownSpeedKts, aimFeet))
            .ToList();
        return setAside.Count == 0 ? choice : choice with { ReachableExitsSetAside = setAside };
    }

    /// <summary>Whether the aircraft can stop on the runway with comfortable braking from its typical touchdown speed:
    /// the landing distance available covers the aim point plus the comfortable lead to a 90° exit's turn-off speed
    /// (RolloutExitGate.ComfortableExitLeadFeet, the touchdown re-plan's own rule) — 20 kt, where the rest of the stop
    /// is a few feet. Such a runway is not "short" when its exits all lie behind the touchdown: the aircraft stops and
    /// backtracks (CYYQ 15: 9,167 ft, its only taxiway at 113–600 ft).</summary>
    internal static bool RunwayLongEnoughToStop(Runway rwy, double touchdownSpeedKts, double aimFeet) =>
        rwy.Length > 0 &&
        rwy.Length - rwy.ThresholdOffset >= aimFeet + RolloutExitGate.ComfortableExitLeadFeet(touchdownSpeedKts, RolloutExitGate.MaxUsableExitTurnDeg);

    /// <summary>"Right" or "Left" of the landing direction — <see cref="LandingExit.ExitSide"/>'s own words and
    /// convention — for a node clear of the runway pavement; null for a node on it (or unknown), whose side says
    /// nothing about how a route leaves the runway.</summary>
    internal static string? SideOfRunway(TaxiGraph graph, int nodeId, Runway rwy)
    {
        if (!graph.Nodes.TryGetValue(nodeId, out var node)) return null;
        double leftOfCentreline = RunwayFrame.For(rwy, rwy.StartLat).SignedCrossTrack(node.Latitude, node.Longitude);
        if (!RunwayVacateResolver.IsOffPavement(Math.Abs(leftOfCentreline), rwy)) return null;
        return leftOfCentreline < 0 ? "Right" : "Left";
    }

    /// <summary>
    /// The taxiway the mapped route really leaves the runway on, when it is a named taxiway other than the exit's
    /// own; otherwise null. It is the taxiway of the first segment, on the way from the exit's junction to the node
    /// the route begins at, that ends clear of the runway pavement — the segment that takes the aircraft off it.
    ///
    /// <para>Neither simpler reading is right. The route's own first taxiway is usually NOT it: the route begins
    /// clear of the runway, often where the exit meets a parallel taxiway (exit C, route "A" at the synthetic TEST
    /// airport; KMEM 36L's M7, route "M, N, A, S"). Nor is the first NAMED taxiway from the junction: at KCLT 01R
    /// the way from exit V4's junction runs 100 m down the runway centreline on edges named E6 before turning off
    /// on V4 itself. Where the two taxiways genuinely differ it is worth saying — KMSP 17's junction sends L3 to
    /// the right and K3 to the left, and the exit briefed on the left is K3's.</para>
    /// </summary>
    internal static string? TaxiwayLeavingTheRunway(TaxiGraph graph, LandingExit exit, int routeStart, Runway rwy)
    {
        // A route that begins at the junction itself leaves nothing between the two to judge by, and reading the route's
        // own first taxiway is the reading rejected above: say nothing.
        if (routeStart == exit.NodeId) return null;
        var lead = new TaxiRouter(graph).FindShortestPath(exit.NodeId, routeStart);
        if (lead == null) return null;
        var frame = RunwayFrame.For(rwy, rwy.StartLat);
        foreach (var seg in lead.Segments)
        {
            double lateral = Math.Abs(frame.SignedCrossTrack(seg.ToNode.Latitude, seg.ToNode.Longitude));
            if (!RunwayVacateResolver.IsOffPavement(lateral, rwy)) continue;
            string name = seg.TaxiwayName;
            return string.IsNullOrEmpty(name) || string.Equals(name, exit.TaxiwayName, StringComparison.OrdinalIgnoreCase)
                ? null : name;
        }
        return null;
    }

    /// <summary>
    /// One hold-short note per stop the automatic pass placed — the route's start hold, then every flagged
    /// segment — in route order. An event the pass could not hold becomes a note, never a silent gap.
    ///
    /// <para>Every stop is briefed, each on its own. On a briefing route the pass is the only thing that
    /// flags a stop (the router flags none), one per held crossing, so two stops naming one runway (either
    /// end, or the start hold and a later stop) are two crossings, and the pilot must hear both. The runway
    /// just landed on is no exception: every briefed exit vacates it (<see cref="BriefingExitPicker"/> takes
    /// only <see cref="LandingExit.VacatesRunway"/> exits), so the way in starts clear of it, and the pass
    /// never holds for LEAVING a runway — a stop naming it, the start hold included, is the route crossing it
    /// again with landing traffic behind. A start hold is what such a crossing becomes when nothing lies
    /// between the vacate node and the runway.</para>
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
                                                          List<string> notes, List<string>? unheldRunways = null)
    {
        var holds = new List<HoldShortNote>();
        if (route.StartHoldRunway is string startHold)
        {
            foreach (var d in RouteRunwayCrossings.ExtractRunwayDesignators(startHold))
                holds.Add(new HoldShortNote(d, NamedTaxiwayAt(route, 0), BeforeEntering: false));
        }
        for (int i = 0; i < route.Segments.Count; i++)
        {
            var seg = route.Segments[i];
            if (!seg.IsHoldShortPoint || string.IsNullOrEmpty(seg.HoldShortRunway)) continue;
            foreach (var d in RouteRunwayCrossings.ExtractRunwayDesignators(seg.HoldShortRunway))
                holds.Add(new HoldShortNote(d, NamedTaxiwayAt(route, i), BeforeEntering: false));
        }
        // One note per runway PAVEMENT: 10L and 28R are one runway, and the pilot hears it once.
        var seen = new List<string>();
        foreach (var e in events)
        {
            if (e.Held) continue;
            string designator = RouteRunwayCrossings.NormalizeDesignator(e.Designator);
            if (seen.Any(d => SameRunway(d, designator))) continue;
            seen.Add(designator);
            notes.Add($"no hold short point could be placed for runway {designator}; cross with care");
            unheldRunways?.Add(designator);
        }
        return holds;
    }

    /// <summary>
    /// Advisory: taxiways on the route whose navdata width is below the code letter's Annex 14
    /// minimum. Width 0 (unknown; every OpenStreetMap edge) never produces a note.
    ///
    /// <para>Judged in whole feet, the unit navdata stores widths in: the minimum's own whole-foot
    /// value is the minimum, never "below" it. The most common taxiway width in navdata is 82 ft,
    /// 24.99 m (62 % of fs2024 taxiway rows, 65 % of fs2020's), which compared raw was below the
    /// 25 m code F minimum on almost every taxiway of an A380's route; and 49 ft, the third
    /// commonest width and code C's 15 m in whole feet, judged at the metres display's 0.1 m told
    /// a 737 in feet mode "49 ft in the navdata, below the 49 ft code C minimum".</para>
    /// </summary>
    internal static List<NarrowTaxiwayNote> NarrowTaxiways(TaxiRoute route, AircraftProfile aircraft)
    {
        var result = new List<NarrowTaxiwayNote>();
        double min = AircraftSizeClass.MinTaxiwayWidthMetres(aircraft.CodeLetter);
        if (min <= 0) return result;
        foreach (var group in route.Segments
                     .Where(s => !string.IsNullOrEmpty(s.TaxiwayName) && s.PathWidth > 0)
                     .GroupBy(s => s.TaxiwayName, StringComparer.OrdinalIgnoreCase))
        {
            double widthFeet = group.Min(s => s.PathWidth);
            // Whole feet, the unit navdata stores widths in: the minimum's own whole-foot value (49 ft for 15 m, 75 ft for
            // 23 m, 82 ft for 25 m) IS the minimum. Judged at the metres display's 0.1 m, a feet-mode block said
            // "49 ft in the navdata, below the 49 ft code C minimum".
            if (Math.Round(widthFeet) < Math.Round(min / 0.3048))
                result.Add(new NarrowTaxiwayNote(group.Key, widthFeet * 0.3048, min));
        }
        return result;
    }

    /// <summary>One runway pavement: the same designator (<see cref="RunwayIdsMatch"/>) or its reciprocal.</summary>
    internal static bool SameRunway(string a, string b) =>
        RunwayIdsMatch(a, b) || RunwayIdsMatch(RouteRunwayCrossings.Reciprocal(a), b);

    /// <summary>A leading "Runway", "RWY" or "RW", with or without a space before the designator ("RW09L", "rwy27"):
    /// with a word boundary after it, the glued forms kept their prefix and matched nothing (review M-2). No runway
    /// designator begins with an R, so the bare "rw" can never eat part of one.</summary>
    private static readonly Regex RunwayWord = new(@"^\s*(?:runway|rwy|rw)\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>"9" and "09" are one runway, and so are "Runway 9L", "RW09L" and "09L"; any suffix is part of the identity — "16W"
    /// (a water lane) is not "16", and "22A" is not "22". CleanRunway, which reads only L/C/R, made both the land
    /// runway: W55's "16" found the water lane, and fs2020 BGGH's "22" lined up at a closed "22A" start row.</summary>
    private static string Canon(string d) => RouteRunwayCrossings.NormalizeDesignator(RunwayWord.Replace(d, ""));

    /// <summary>The same runway END, by <see cref="Canon"/> — the ONE identity rule both this and
    /// <see cref="SameRunway"/> use, so a compass-point end ("N", "NE") is found as well as a numbered one.</summary>
    internal static bool RunwayIdsMatch(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(Canon(a), Canon(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The runway end by <see cref="RunwayIdsMatch"/>, an open record before a closed one of the same name.</summary>
    internal static Runway? FindRunway(IReadOnlyList<Runway> runways, string id) =>
        runways.Where(r => RunwayIdsMatch(r.RunwayID, id)).OrderBy(r => r.IsClosed ? 1 : 0).FirstOrDefault();

    /// <summary>
    /// The graph's largest connected component — the taxi network a leg falls back to (<see cref="LegEndNode"/>). An
    /// island — an isolated taxiway (GCLP S5) or a stand whose lead-in meets nothing (KTUL G 19, a 3-node island 72 m
    /// from the network) — is never a stand's node or a route's start, as Taxi Assist's LoadRoute keeps its start in
    /// the destination's component. −1 for an empty graph (no node matches it).
    /// </summary>
    internal static int NetworkComponentId(TaxiGraph graph) => graph.Nodes.Count == 0 ? -1
        : graph.Nodes.Values.GroupBy(n => n.ComponentId).OrderByDescending(c => c.Count()).ThenBy(c => c.Key).First().Key;

    /// <summary>
    /// The node one end of a leg — a stand or the aircraft — is at: the nearest node within <paramref name="maxMetres"/>
    /// when its piece of the taxi network also holds the leg's OTHER end (<paramref name="reachesOtherEnd"/>: the
    /// taxi-out's runway entrance, a briefable exit's route start on the taxi-in) AND either no node of the largest
    /// component (<paramref name="network"/>) is within that reach or the nearer node is a stand's or a stand lead-in's;
    /// otherwise the nearest node of the largest component within that reach, or null. The second condition (re-review
    /// N-5): an aircraft on the main network beside a narrow runway, between two nodes, can be nearer a node of the piece
    /// ACROSS the runway — snapped to it, it was briefed from the other side. A stand is where the aircraft is parked,
    /// so its own piece stays the answer however near the main network runs.
    /// <para>The graph has no runway edges, so taxiway pieces that meet only across a runway are separate components
    /// (LFBP, VIJU, ENAT, UKHH, KPRC). Measured on the real fs2024 database by the taxi-out's rule (2026-09-28): 1,677
    /// airports have two or more pieces that each hold stands and reach a runway entrance, and 7,987 stands sit on such a
    /// piece off the largest one — 6,967 of them with no node of the largest within 100 m, so each was briefed "does not
    /// connect" or replaced by a representative stand, and 1,020 briefed from a node of the largest piece, the wrong side.
    /// A piece that reaches neither end (KTUL G 19's island) still never is a leg's end.</para>
    /// <para>As a route START the node is never a bridge-only stand stub — the rule every route start follows
    /// (<see cref="TaxiGraph.IsBridgeOnlyStandStub"/>); as a DESTINATION it may be one, so a bridged stand stays
    /// reachable.</para>
    /// </summary>
    internal static TaxiNode? LegEndNode(TaxiGraph graph, double lat, double lon, double maxMetres, int network,
                                         bool asRouteStart, Func<TaxiNode, bool>? reachesOtherEnd)
    {
        bool InReach(TaxiNode? n) => n != null && TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude) <= maxMetres;
        var onNetwork = graph.FindNearestNode(lat, lon, requiredComponentId: network, excludeBridgeOnlyStandStubs: asRouteStart);
        bool networkInReach = InReach(onNetwork);
        if (reachesOtherEnd != null)
        {
            var nearest = graph.FindNearestNode(lat, lon, excludeBridgeOnlyStandStubs: asRouteStart);
            if (InReach(nearest) && nearest!.ComponentId != network &&
                (!networkInReach || IsStandOrLeadInNode(graph, nearest)) && reachesOtherEnd(nearest)) return nearest;
        }
        return networkInReach ? onNetwork : null;
    }

    /// <summary>A stand's own node (<see cref="IsStandNode"/>) or a node on a stand lead-in (navdata path type "P").</summary>
    private static bool IsStandOrLeadInNode(TaxiGraph graph, TaxiNode node) =>
        IsStandNode(node) ||
        (graph.Adjacency.TryGetValue(node.NodeId, out var edges) && edges.Any(TaxiGraph.IsParkingLeadIn));

    /// <summary>
    /// The node a stand is left from or reached at: <see cref="LegEndNode"/> within <see cref="StandNodeMaxDistanceMetres"/>,
    /// or — with no <paramref name="reachesOtherEnd"/> — the nearest node of the largest component only.
    /// </summary>
    internal static TaxiNode? StandNode(TaxiGraph graph, ParkingSpot spot, int networkComponentId, bool asRouteStart,
                                        Func<TaxiNode, bool>? reachesOtherEnd = null) =>
        LegEndNode(graph, spot.Latitude, spot.Longitude, StandNodeMaxDistanceMetres, networkComponentId, asRouteStart, reachesOtherEnd);

    /// <summary>
    /// Where a taxi-out from <paramref name="anchor"/> meets the departure runway: the entrance <see cref="RunwayLineupTarget"/>
    /// resolves. With no entrance at or behind the lineup point that is the plain nearest node, kept for guidance's reach
    /// warning (RUNWAY_REACH_MAX_CROSS_M — LTAC 21L: 931 m off the centreline); the briefing asks Taxi Assist's own
    /// backtrack search (anchored on <paramref name="anchor"/>, whose piece of the taxi network it keeps to) for the
    /// entrance a full-length departure backtracks from instead, and has none when that finds none. The plain nearest
    /// node can also lie on ANOTHER piece of the network from the anchor's — a piece meeting the rest only across the
    /// runway: the anchor's own piece is then searched for an entrance at or behind the lineup point first
    /// (<see cref="TaxiGraph.FindRunwayLineupEntryNode"/> with no plain-node shortcut, anchored on
    /// <paramref name="anchor"/>) — the plain node lies within the 120 m that shortcut returns it for, so Resolve never
    /// searched, and the backtrack search skips an entrance under 40 m along, which told a piece with its own
    /// full-length entrance 20 m in to backtrack from 1,000 m down (re-review N-1). Then the backtrack search, and the
    /// plain node kept when it finds nothing, so the leg still says no taxi route connects.
    /// </summary>
    private static (RunwayLineupTarget.Result Target, TaxiNode? Entry) RunwayEntry(GraphBundle g, Runway rwy, TaxiNode anchor)
    {
        var startsForRunway = g.Starts.Where(s => RunwayIdsMatch(s.RunwayName, rwy.RunwayID)).ToList();
        var target = RunwayLineupTarget.Resolve(g.Graph, rwy, startsForRunway, anchor.Latitude, anchor.Longitude);
        var entry = target.EntryNode;
        if (entry == null) return (target, null);
        var frame = RunwayFrame.For(rwy, rwy.StartLat);
        bool offTheRunway = Math.Abs(frame.SignedCrossTrack(entry.Latitude, entry.Longitude)) > Services.TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M;
        if (offTheRunway || entry.ComponentId != anchor.ComponentId)
        {
            double halfWidthM = RunwayHalfWidthMetres(rwy);
            if (!offTheRunway)
            {
                // Beyond 120 m Resolve already searched the anchor's piece this way; within it, it returned the plain node.
                var own = g.Graph.FindRunwayLineupEntryNode(target.LineupLat, target.LineupLon,
                    rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, halfWidthM, maxAcceptableCrossM: -1,
                    anchor.Latitude, anchor.Longitude);
                if (own != null && own.ComponentId == anchor.ComponentId) return (target, own);
            }
            var backtrack = g.Graph.FindBacktrackEntryNode(rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, halfWidthM,
                                                           anchor.Latitude, anchor.Longitude);
            if (backtrack != null || offTheRunway) entry = backtrack;
        }
        return (target, entry);
    }

    /// <summary>Half the runway's width in metres, 150 ft assumed when the row carries none (RunwayLineupTarget's own
    /// fallback).</summary>
    private static double RunwayHalfWidthMetres(Runway rwy) => (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;

    /// <summary>
    /// Whether <paramref name="entry"/> lies ON the runway pavement: within half the width plus the 5 m both entrance
    /// searches allow, and along the runway from where a departure begins (the pavement end, or the lineup point when
    /// that lies behind it — a starter extension) to its far end. The lineup search returns its plain nearest node
    /// anywhere within 120 m of the centreline, so a piece whose nearest node is a run-up pad or an unconnected lead-in
    /// beside the runway would otherwise "reach" it (re-review N-2). Asked only of a piece other than the largest.
    /// </summary>
    private static bool EntersTheRunway(Runway rwy, RunwayLineupTarget.Result target, TaxiNode entry)
    {
        const double SlopMetres = 5.0;
        var frame = RunwayFrame.For(rwy, rwy.StartLat);
        double along = frame.Along(entry.Latitude, entry.Longitude);
        double from = Math.Min(0.0, frame.Along(target.LineupLat, target.LineupLon)) - SlopMetres;
        return Math.Abs(frame.SignedCrossTrack(entry.Latitude, entry.Longitude)) <= RunwayHalfWidthMetres(rwy) + SlopMetres &&
               along >= from && along <= frame.LengthM + SlopMetres;
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

    /// <summary>"SayIntentions assigned gate B 6", or "SayIntentions assigned Gate 5" for a label that already says
    /// what it is — never "gate Gate 5".</summary>
    private static string DescribeArrivalStand(StandChoice stand, string? airlineIcao)
    {
        if (stand.Source != StandChoiceSource.SayIntentions) return $"representative stand {DescribeStand(stand, airlineIcao)}";
        string label = BriefingStandPicker.IdentityLabel(stand.Spot);
        return BriefingStandPicker.LabelNamesItsKind(label) ? $"SayIntentions assigned {label}" : $"SayIntentions assigned gate {label}";
    }

    /// <summary>
    /// The stand picker's notes, said once with the graph's own caveat: on the OpenStreetMap tier both say the stand
    /// types are unknown, and the picker's also says what the stand may not be, so it replaces the graph's.
    /// </summary>
    private static void AddStandNotes(List<string> notes, StandChoice stand, string? graphNote)
    {
        static bool TypesUnknown(string n) => n.StartsWith(BriefingStandPicker.StandTypesUnknown, StringComparison.Ordinal);
        if (graphNote != null && TypesUnknown(graphNote) && stand.Notes.Any(TypesUnknown)) notes.Remove(graphNote);
        notes.AddRange(stand.Notes);
    }

    /// <summary>
    /// Whether a node's <see cref="TaxiNode.ParkingName"/> names a STAND. TaxiGraph.Build also writes
    /// "Runway 27" into it for a node near a runway start row, which is no stand; the test is the one
    /// <see cref="TaxiGraph.DescribeLocation"/> applies before it names a gate.
    /// </summary>
    private static bool IsStandNode(TaxiNode node) =>
        node.Type == TaxiNodeType.Parking &&
        !string.IsNullOrEmpty(node.ParkingName) &&
        !node.ParkingName.StartsWith("Runway", StringComparison.OrdinalIgnoreCase);

    /// <summary>The turns only when they line up one-for-one with the taxiway names; otherwise none, logged, so a future
    /// drift in how either groups a route loses the directions visibly instead of attaching them to the wrong taxiways
    /// (BriefingTurnsTests pins the two groupings together).</summary>
    private static IReadOnlyList<string?> AlignedTurns(IReadOnlyList<string> taxiways, IReadOnlyList<string?> turns)
    {
        if (turns.Count == taxiways.Count) return turns;
        Log.Warn(LogCategory, $"turns dropped: {turns.Count} turns for {taxiways.Count} taxiways [{string.Join(",", taxiways)}]");
        return Array.Empty<string?>();
    }

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
