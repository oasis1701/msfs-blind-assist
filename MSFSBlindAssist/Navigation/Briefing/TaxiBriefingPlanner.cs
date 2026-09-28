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
    /// <summary>A SayIntentions parking-service gate is briefed only when its position lies within this distance of the
    /// arrival airport's reference point — the same "at this airport" line as <see cref="OwnPositionMaxAirportDistanceMetres"/>.</summary>
    public const double ParkingServiceMaxAirportDistanceMetres = OwnPositionMaxAirportDistanceMetres;

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

        int startNode = -1;
        string endpoint = "";
        StandChoice? stand = null;
        int network = NetworkComponentId(g.Graph);

        if (r.Own is { OnGround: true } own && g.Airport != null &&
            TaxiGraph.FastDistanceMeters(own.Lat, own.Lon, g.Airport.Latitude, g.Airport.Longitude) <= OwnPositionMaxAirportDistanceMetres)
        {
            // A route START from a SENSED position: on the taxi network, and bridge-only stand stubs excluded, as
            // TaxiGuidanceManager.LoadRoute does, so the position is never snapped onto an island or a stub.
            var node = g.Graph.FindNearestNode(own.Lat, own.Lon, requiredComponentId: network, excludeBridgeOnlyStandStubs: true);
            if (node != null && TaxiGraph.FastDistanceMeters(own.Lat, own.Lon, node.Latitude, node.Longitude) <= OwnPositionMaxNodeDistanceMetres)
            {
                startNode = node.NodeId;
                endpoint = IsStandNode(node) ? $"current position, stand {node.ParkingName}" : "current position";
            }
        }

        if (startNode < 0)
        {
            // The stand is the route's START: on the network, never a bridge-only stand stub.
            bool HasStandNode(ParkingSpot s) => StandNode(g.Graph, s, network, asRouteStart: true) != null;
            // SayIntentions never assigns a departure gate — no hint here by design.
            stand = BriefingStandPicker.Pick(g.Spots, r.Aircraft, r.AirlineIcao, siGate: null, HasStandNode, r.Unit);
            if (stand == null)
                return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                    BriefingStandPicker.NoStandReason(g.Spots, HasStandNode, icao), notes: notes);
            AddStandNotes(notes, stand, g.Note);
            startNode = StandNode(g.Graph, stand.Spot, network, asRouteStart: true)!.NodeId;
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

        // A parking-service gate whose position is published is refused when that position is beyond
        // ParkingServiceMaxAirportDistanceMetres of this airport: it then names somewhere else. With no position it is
        // looked up by NAME in this airport's scenery, as a flight-file gate is (owner, 2026-09-26, reversing that
        // morning's refusal): live KMEM→KATL, getParking answered "B3" with no position and the flight file said
        // "Gate B3" nine seconds later with the aircraft at KMEM Gate 17 — the service meant the ARRIVAL gate, and
        // refusing it briefed a representative stand the pilot was never assigned.
        var arrivalGate = r.ArrivalGate;
        bool matchedByNameOnly = false;
        if (arrivalGate is { Source: SayIntentionsGateSource.ParkingService })
        {
            if (arrivalGate.Position is not GeoPoint pin)
                matchedByNameOnly = true;
            else if (g.Airport != null &&
                     TaxiGraph.FastDistanceMeters(pin.Latitude, pin.Longitude, g.Airport.Latitude, g.Airport.Longitude) > ParkingServiceMaxAirportDistanceMetres)
            {
                notes.Add($"SayIntentions' parking service named {arrivalGate.Label}, but its position is not at {icao}; using a representative stand instead");
                arrivalGate = null;
            }
        }

        // The stand is the route's DESTINATION: on the network, and a bridged stand stays reachable.
        int network = NetworkComponentId(g.Graph);
        bool HasStandNode(ParkingSpot s) => StandNode(g.Graph, s, network, asRouteStart: false) != null;
        var stand = BriefingStandPicker.Pick(g.Spots, r.Aircraft, r.AirlineIcao, arrivalGate, HasStandNode, r.Unit);
        if (matchedByNameOnly && stand?.Source == StandChoiceSource.SayIntentions)
            notes.Add($"SayIntentions' parking service gave no position for {arrivalGate!.Label}, so it was matched by name in this scenery");
        if (stand != null) AddStandNotes(notes, stand, g.Note);
        string endpoint = stand == null ? "" : DescribeArrivalStand(stand, r.AirlineIcao);

        IReadOnlyList<LandingExit> exits = g.Graph.GetLandingExits(rwy);
        LandingExitVacateScreen.Mark(g.Graph, exits, rwy);
        var routeStarts = BriefableExitRouteStarts(g.Graph, exits, rwy);

        // Each candidate's way in is planned at most once: the picker asks about the exits in its preference order
        // and stops at the first that stays clear of the runway just landed on, and the chosen one is then briefed
        // from the same plan.
        var standNode = stand == null ? null : StandNode(g.Graph, stand.Spot, network, asRouteStart: false);
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
                choice = PickExit();
            }
        }
        var vacating = exits.Where(e => e.VacatesRunway).OrderBy(e => e.DistanceFromThresholdFeet).ToList();
        // From here on the exits have been searched: every leg says so, "none found" included.
        if (choice == null)
            return TaxiLegBriefing.UnavailableLeg(icao, rwy.RunwayID, g.Tier,
                $"no exit taxiway is mapped clear of runway {rwy.RunwayID} in this scenery", stand, endpoint, notes, vacating,
                exitsSearched: true);
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
        foreach (var e in events)
        {
            if (e.Held) continue;
            notes.Add($"no hold short point could be placed for runway {e.Designator}; cross with care");
            unheldRunways?.Add(e.Designator);
        }
        return holds;
    }

    /// <summary>
    /// Advisory: taxiways on the route whose navdata width is below the code letter's Annex 14
    /// minimum. Width 0 (unknown; every OpenStreetMap edge) never produces a note.
    ///
    /// <para>Judged at the block's own precision (<see cref="WidthPrecisionMetres"/>): a width that
    /// prints as the minimum is not below it. The most common taxiway width in navdata is 82 ft,
    /// 24.99 m (62 % of fs2024 taxiway rows, 65 % of fs2020's), and compared raw it put
    /// "24.99 m" — printed "25.0 m" — below the 25.0 m code F minimum on almost every taxiway of an
    /// A380's route.</para>
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
            double widthM = group.Min(s => s.PathWidth) * 0.3048;
            if (widthM < min - WidthPrecisionMetres / 2.0) result.Add(new NarrowTaxiwayNote(group.Key, widthM, min));
        }
        return result;
    }

    /// <summary>The precision the block states a taxiway width to ("24.9 m").</summary>
    private const double WidthPrecisionMetres = 0.1;

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
    /// The taxi network a leg is planned on: the graph's largest connected component. An island — an isolated taxiway
    /// (GCLP S5) or a stand whose lead-in meets nothing (KTUL G 19, a 3-node island 72 m from the network) — is never a
    /// stand's node or a route's start, as Taxi Assist's LoadRoute keeps its start in the destination's component. −1
    /// for an empty graph (no node matches it).
    /// </summary>
    internal static int NetworkComponentId(TaxiGraph graph) => graph.Nodes.Count == 0 ? -1
        : graph.Nodes.Values.GroupBy(n => n.ComponentId).OrderByDescending(c => c.Count()).ThenBy(c => c.Key).First().Key;

    /// <summary>
    /// The node a stand is left from or reached at: the nearest node of the network (<see cref="NetworkComponentId"/>)
    /// within <see cref="StandNodeMaxDistanceMetres"/>. As the taxi-out's route START it is never a bridge-only stand
    /// stub — the rule every route start follows (<see cref="TaxiGraph.IsBridgeOnlyStandStub"/>); as the taxi-in's
    /// DESTINATION it may be one, so a bridged stand stays reachable.
    /// </summary>
    internal static TaxiNode? StandNode(TaxiGraph graph, ParkingSpot spot, int networkComponentId, bool asRouteStart)
    {
        var node = graph.FindNearestNode(spot.Latitude, spot.Longitude, requiredComponentId: networkComponentId,
                                         excludeBridgeOnlyStandStubs: asRouteStart);
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

    /// <summary>The turns only when they line up one-for-one with the taxiway names; otherwise none, so a future drift in
    /// how either groups a route loses the directions instead of attaching them to the wrong taxiways.</summary>
    private static IReadOnlyList<string?> AlignedTurns(IReadOnlyList<string> taxiways, IReadOnlyList<string?> turns) =>
        turns.Count == taxiways.Count ? turns : Array.Empty<string?>();

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
