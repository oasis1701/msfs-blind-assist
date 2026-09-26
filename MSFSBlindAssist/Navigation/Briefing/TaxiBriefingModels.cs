// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>Where a leg's ground data came from. The block names it on every leg.</summary>
public enum BriefingTier { Navdata, OpenStreetMap, None }

/// <summary>The aircraft's own position when SimConnect reported one for this briefing.</summary>
public sealed record OwnPosition(double Lat, double Lon, bool OnGround);

/// <summary>SayIntentions' ARRIVAL gate — the label it published and, when it did, the stand's position.
/// Only ever built by <see cref="SayIntentionsArrivalGate"/>, which checks the flight matches this OFP.</summary>
public sealed record SayIntentionsGateHint(string Label, GeoPoint? Position);

public enum StandChoiceSource { SayIntentions, AirlineMatch, Category, Any }

/// <summary>The stand a leg routes to and how it was chosen; <see cref="Notes"/> are pilot-readable caveats.</summary>
public sealed record StandChoice(ParkingSpot Spot, StandChoiceSource Source, IReadOnlyList<string> Notes);

public sealed record TaxiBriefingRequest(
    string OriginIcao, string OriginRunway, string DestinationIcao, string DestinationRunway,
    AircraftProfile Aircraft, string? AirlineIcao, OwnPosition? Own, SayIntentionsGateHint? ArrivalGate);

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
    public double DistanceMetres { get; init; }
    public IReadOnlyList<HoldShortNote> HoldShorts { get; init; } = Array.Empty<HoldShortNote>();
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
    /// <summary>Both legs unavailable for one reason (no database, planner failure) — still rendered, never silent.</summary>
    public static TaxiBriefing Unavailable(AircraftProfile aircraft, string originIcao, string originRunway,
        string destinationIcao, string destinationRunway, string reason) => new(aircraft,
        TaxiLegBriefing.UnavailableLeg(originIcao, originRunway, BriefingTier.None, reason),
        TaxiLegBriefing.UnavailableLeg(destinationIcao, destinationRunway, BriefingTier.None, reason));
}

/// <summary>One airport's graph and the data it was built from. <see cref="Note"/> is a caveat the tier
/// carries (OSM: stand types unknown). <see cref="Airport"/> is the reference point for the 5 km own-position test.</summary>
public sealed record GraphBundle(TaxiGraph Graph, BriefingTier Tier, IReadOnlyList<Runway> Runways,
    IReadOnlyList<StartPosition> Starts, IReadOnlyList<ParkingSpot> Spots, string? Note, Airport? Airport);

/// <summary>What the EFB needs from MainForm to compute the taxi section: a provider GETTER (the instance is
/// swapped on a database switch), the gate source and the SayIntentions file reader. Null in tests.</summary>
public sealed record RouteBriefingDependencies(
    Func<IAirportDataProvider?> Provider,
    Func<GateDataSource?> GateSource,
    Func<Task<SayIntentionsFlightContext>> SayIntentions);
