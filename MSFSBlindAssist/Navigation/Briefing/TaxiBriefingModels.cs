// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingModels.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>The aircraft's own position when SimConnect reported one for this briefing.</summary>
public sealed record OwnPosition(double Lat, double Lon, bool OnGround);

/// <summary>Where SayIntentions published the arrival gate: its flight file's <c>assigned_gate</c>, or — when the file
/// has none yet — its SAPI parking service (<c>getParking</c>), the fallback MSFS Blind Assist's SayIntentions window
/// and Taxi Assist's import already use. SAPI does not say whether that service means the arrival gate or the
/// aircraft's current parking, so the briefing refuses a parking-service gate whose position is not at the arrival
/// airport (<see cref="TaxiPlanningBlock.ResolveArrivalGate"/>); one with no position is used by name (live, it named
/// the arrival gate).</summary>
public enum SayIntentionsGateSource { FlightFile, ParkingService }

/// <summary>SayIntentions' ARRIVAL gate — the label it published and, when it did, the stand's position, both from the
/// same <see cref="Source"/>. Only ever built by <see cref="SayIntentionsArrivalGate"/>, which checks the flight matches
/// this OFP.</summary>
public sealed record SayIntentionsGateHint(string Label, GeoPoint? Position,
    SayIntentionsGateSource Source = SayIntentionsGateSource.FlightFile);

/// <summary>What the EFB needs from MainForm for the briefing's taxi section: a provider GETTER (the instance is
/// swapped on a database switch), the gate source and SayIntentions' status — the flight file, and its parking service
/// when the file has no gate (<c>SayIntentionsService.GetAssignedStatusAsync</c>, the SayIntentions window's own call).
/// Null in tests.</summary>
public sealed record RouteBriefingDependencies(
    Func<IAirportDataProvider?> Provider,
    Func<GateDataSource?> GateSource,
    Func<Task<SayIntentionsStatusResult>> SayIntentions);
