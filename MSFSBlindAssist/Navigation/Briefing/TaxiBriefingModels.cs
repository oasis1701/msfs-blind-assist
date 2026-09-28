// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>Where a leg's ground data came from. The block names it on every leg.</summary>
public enum BriefingTier { Navdata, OpenStreetMap, None }

/// <summary>The aircraft's own position when SimConnect reported one for this briefing.</summary>
public sealed record OwnPosition(double Lat, double Lon, bool OnGround);

/// <summary>Where SayIntentions published the arrival gate: its flight file's <c>assigned_gate</c>, or — when the file
/// has none yet — its SAPI parking service (<c>getParking</c>), the fallback MSFS Blind Assist's SayIntentions window
/// and Taxi Assist's import already use. SAPI does not say whether that service means the arrival gate or the
/// aircraft's current parking, so the planner refuses a parking-service gate whose position is not at the arrival
/// airport; one with no position is looked up by name in the arrival airport's scenery (live, it named the arrival
/// gate: see TaxiBriefingPlanner.PlanTaxiIn).</summary>
public enum SayIntentionsGateSource { FlightFile, ParkingService }

/// <summary>SayIntentions' ARRIVAL gate — the label it published and, when it did, the stand's position, both from the
/// same <see cref="Source"/>. Only ever built by <see cref="SayIntentionsArrivalGate"/>, which checks the flight matches
/// this OFP.</summary>
public sealed record SayIntentionsGateHint(string Label, GeoPoint? Position,
    SayIntentionsGateSource Source = SayIntentionsGateSource.FlightFile);

public enum StandChoiceSource { SayIntentions, AirlineMatch, Category, Any }

/// <summary>The stand a leg routes to and how it was chosen; <see cref="Notes"/> are pilot-readable caveats.</summary>
public sealed record StandChoice(ParkingSpot Spot, StandChoiceSource Source, IReadOnlyList<string> Notes);

/// <param name="OriginRunwayNote">Where the departure runway came from (<see cref="BriefingRunwayChoice"/>); the leg's
/// first note when set.</param>
/// <param name="DestinationRunwayNote">Where the arrival runway came from; the leg's first note when set.</param>
/// <param name="Unit">The pilot's ground distance setting, for the distances the planner writes into notes.</param>
public sealed record TaxiBriefingRequest(
    string OriginIcao, string OriginRunway, string DestinationIcao, string DestinationRunway,
    AircraftProfile Aircraft, string? AirlineIcao, OwnPosition? Own, SayIntentionsGateHint? ArrivalGate,
    string? OriginRunwayNote = null, string? DestinationRunwayNote = null,
    DistanceUnit Unit = DistanceUnit.Metres);

/// <summary>A hold-short point on the route: the runway it protects, the taxiway it is on, and whether it is
/// the hold before entering the departure runway (true) or a crossing (false).</summary>
public sealed record HoldShortNote(string Runway, string Taxiway, bool BeforeEntering);

/// <summary>Advisory only: navdata says this taxiway is narrower than the Annex 14 minimum for the aircraft's code letter.</summary>
public sealed record NarrowTaxiwayNote(string Taxiway, double WidthMetres, double MinimumMetres);

public sealed class TaxiLegBriefing
{
    public required string Icao { get; init; }
    public required string Runway { get; init; }
    /// <summary>Where the leg's ground data came from — required: the block names it on every leg, and a leg built
    /// without one would silently claim the scenery navdata (the enum's default).</summary>
    public required BriefingTier Tier { get; init; }
    /// <summary>Non-null when no route could be computed: the pilot-readable reason.</summary>
    public string? Unavailable { get; init; }
    /// <summary>Taxi-out: where the route STARTS ("current position, stand N12 (Ramp Cargo)" / "representative stand …").
    /// Taxi-in: the STAND it ends at ("SayIntentions assigned gate 52A" / "representative stand …").</summary>
    public string EndpointDescription { get; init; } = "";
    public StandChoice? Stand { get; init; }
    public IReadOnlyList<string> Taxiways { get; init; } = Array.Empty<string>();
    /// <summary>The turn onto each of <see cref="Taxiways"/>, aligned with it (<see cref="BriefingTurns.TaxiwayTurns"/>):
    /// null for the first taxiway and wherever no turn could be measured.</summary>
    public IReadOnlyList<string?> TaxiwayTurns { get; init; } = Array.Empty<string?>();
    /// <summary>Taxi-in only: the turn from the last taxiway into the stand (<see cref="BriefingTurns.StandTurn"/>), or null.</summary>
    public string? StandTurn { get; init; }
    public double DistanceMetres { get; init; }
    public IReadOnlyList<HoldShortNote> HoldShorts { get; init; } = Array.Empty<HoldShortNote>();
    /// <summary>The designators of the runway entries and crossings the automatic hold-short pass could not hold
    /// (<see cref="TaxiRouteRunwayEvent.Held"/> false) — the same events that produce the "no hold short point
    /// could be placed" notes.</summary>
    public IReadOnlyList<string> UnheldRunways { get; init; } = Array.Empty<string>();
    public ExitChoice? Exit { get; init; }
    /// <summary>Every exit that gets clear of the landing runway, nearest the threshold first.</summary>
    public IReadOnlyList<LandingExit> VacatingExits { get; init; } = Array.Empty<LandingExit>();
    /// <summary>
    /// Whether the taxi-in's exit search ran — true once its runway was found. An unavailable taxi-in lists the exits
    /// only then, so "none found" is said of a search that found none and never of one that was not made (no
    /// database, a timeout, a runway not in the database). A planned taxi-in always searched.
    /// </summary>
    public bool ExitsSearched { get; init; }
    public IReadOnlyList<NarrowTaxiwayNote> NarrowTaxiways { get; init; } = Array.Empty<NarrowTaxiwayNote>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    /// <summary>Every taxiway name on the graph the leg was planned on (<see cref="TaxiBriefingPlanner.AirportTaxiwayNames"/>),
    /// sorted; empty for a leg never planned on a graph (no database, no ground data, a timeout). The prompt holds any
    /// taxiway the AI names to this list and the route, so its own additions use real names (owner, 2026-09-26).</summary>
    public IReadOnlyList<string> AirportTaxiways { get; internal set; } = Array.Empty<string>();

    public static TaxiLegBriefing UnavailableLeg(string icao, string runway, BriefingTier tier, string reason,
        StandChoice? stand = null, string endpoint = "", IReadOnlyList<string>? notes = null,
        IReadOnlyList<LandingExit>? vacatingExits = null, ExitChoice? exit = null, bool exitsSearched = false) => new()
    {
        Icao = icao, Runway = runway, Tier = tier, Unavailable = reason, Stand = stand,
        EndpointDescription = endpoint, Notes = notes ?? Array.Empty<string>(),
        VacatingExits = vacatingExits ?? Array.Empty<LandingExit>(), Exit = exit, ExitsSearched = exitsSearched,
    };
}

public sealed record TaxiBriefing(AircraftProfile Aircraft, TaxiLegBriefing TaxiOut, TaxiLegBriefing TaxiIn)
{
    /// <summary>Both legs unavailable for one reason (no database, planner failure) — still rendered, never silent.
    /// Each leg keeps its OWN runway note (<see cref="TaxiBriefingRequest.OriginRunwayNote"/> /
    /// <see cref="TaxiBriefingRequest.DestinationRunwayNote"/>), so a leg that could not be computed still says
    /// why SayIntentions' runway was used.</summary>
    public static TaxiBriefing Unavailable(AircraftProfile aircraft, string originIcao, string originRunway,
        string destinationIcao, string destinationRunway, string reason,
        string? originRunwayNote = null, string? destinationRunwayNote = null) => new(aircraft,
        TaxiLegBriefing.UnavailableLeg(originIcao, originRunway, BriefingTier.None, reason,
            notes: originRunwayNote is { Length: > 0 } ? new[] { originRunwayNote } : null),
        TaxiLegBriefing.UnavailableLeg(destinationIcao, destinationRunway, BriefingTier.None, reason,
            notes: destinationRunwayNote is { Length: > 0 } ? new[] { destinationRunwayNote } : null));
}

/// <summary>One airport's graph and the data it was built from. <see cref="Note"/> is a caveat the tier
/// carries (OSM: stand types unknown). <see cref="IsAtAirport"/> is whether a point is at this airport —
/// <c>CurrentAirport.Resolve</c>'s answer — or null when the database cannot say, when the 5 km circle round
/// <see cref="Airport"/> decides instead (<see cref="TaxiBriefingPlanner.AtAirport"/>).</summary>
public sealed record GraphBundle(TaxiGraph Graph, BriefingTier Tier, IReadOnlyList<Runway> Runways,
    IReadOnlyList<StartPosition> Starts, IReadOnlyList<ParkingSpot> Spots, string? Note, Airport? Airport,
    Func<double, double, bool>? IsAtAirport = null);

/// <summary>What the EFB needs from MainForm to compute the taxi section: a provider GETTER (the instance is
/// swapped on a database switch), the gate source and SayIntentions' status — the flight file, and its parking service
/// when the file has no gate (<c>SayIntentionsService.GetAssignedStatusAsync</c>, the SayIntentions window's own call).
/// Null in tests.</summary>
public sealed record RouteBriefingDependencies(
    Func<IAirportDataProvider?> Provider,
    Func<GateDataSource?> GateSource,
    Func<Task<SayIntentionsStatusResult>> SayIntentions);
