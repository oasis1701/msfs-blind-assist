using System.Globalization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>SayIntentions' arrival gate as the briefing uses it: the label, or null with the reason it was not used.</summary>
public sealed record ArrivalGateChoice(string? Gate, string? Note);

/// <summary>Everything the TAXI PLANNING block states for one briefing.</summary>
/// <param name="DepartureStand">The stand the aircraft is parked at, at the origin, or null.</param>
public sealed record TaxiPlanningInputs(
    AircraftProfile Aircraft, string? AirlineIcao,
    string OriginIcao, BriefingRunway Departure, string? DepartureStand,
    string DestinationIcao, BriefingRunway Arrival, ArrivalGateChoice ArrivalGate);

/// <summary>
/// The plain-text TAXI PLANNING block appended to the SimBrief flight data for one Describe Route call: the facts the
/// AI needs to describe the TYPICAL REAL-WORLD taxi flow at each airport — the runways (SayIntentions' when it assigned
/// them for this flight, with a note naming both), the stand the aircraft is parked at, SayIntentions' arrival gate and
/// the aircraft. No route is computed from the simulator scenery (owner, 2026-09-26: the scenery-computed route was
/// dropped and only the real-world flow is briefed), which also keeps graph builds and the online taxiway-name wait
/// out of the briefing's time. InvariantCulture; "\n" line ends; no markdown.
/// </summary>
public static class TaxiPlanningBlock
{
    public const string Header = "TAXI PLANNING (no taxi route is computed; these are the facts for the typical real-world taxi flow)";

    /// <summary>The aircraft counts as "at the origin" within this distance of the airport reference point.</summary>
    public const double AtAirportMaxDistanceMetres = 5000.0;

    /// <summary>…and parked at a stand only within this distance of it — the radius the taxi graph uses to name a
    /// node after its stand.</summary>
    public const double ParkedAtStandMaxDistanceMetres = 100.0;

    private const string NoRunway = "not given in the flight plan";

    public static string Render(TaxiPlanningInputs b, DistanceUnit unit)
    {
        var lines = new List<string> { Header, UnitLine(unit), AircraftLine(b.Aircraft, unit) };
        if (!string.IsNullOrWhiteSpace(b.AirlineIcao)) lines.Add($"Airline: {b.AirlineIcao.Trim()}");

        lines.Add($"TAXI OUT at {b.OriginIcao}, runway {RunwayText(b.Departure)}");
        lines.Add(b.DepartureStand is { Length: > 0 } stand
            ? $"  Start: the aircraft is parked at stand {stand}"
            : "  Start: no stand known");
        AddNote(lines, b.Departure.Note);

        lines.Add($"TAXI IN at {b.DestinationIcao}, landing runway {RunwayText(b.Arrival)}");
        lines.Add(b.ArrivalGate.Gate is { Length: > 0 } gate
            ? $"  Gate: {gate} (assigned by SayIntentions)"
            : "  Gate: none assigned");
        AddNote(lines, b.Arrival.Note);
        AddNote(lines, b.ArrivalGate.Note);
        return string.Join("\n", lines);
    }

    /// <summary>The line the prompt tells the AI to take its unit from.</summary>
    public static string UnitLine(DistanceUnit unit)
    {
        string word = unit == DistanceUnit.Feet ? "feet" : "metres";
        return $"Distance unit: {word} (the pilot's setting); give every taxi distance in {word}";
    }

    /// <summary>
    /// The stand the aircraft is parked at: on the ground within <see cref="AtAirportMaxDistanceMetres"/> of the origin,
    /// the nearest stand within <see cref="ParkedAtStandMaxDistanceMetres"/>, named by its identity ("B 25", "Gate 17").
    /// Null otherwise. <paramref name="spots"/> is read only when the aircraft is on the ground at the origin.
    /// </summary>
    public static string? ResolveDepartureStand(OwnPosition? own, Airport? origin, Func<IReadOnlyList<ParkingSpot>> spots)
    {
        if (own is not { OnGround: true } || origin == null) return null;
        if (TaxiGraph.FastDistanceMeters(own.Lat, own.Lon, origin.Latitude, origin.Longitude) > AtAirportMaxDistanceMetres)
            return null;

        ParkingSpot? nearest = null;
        double best = double.MaxValue;
        foreach (var s in spots())
        {
            double d = TaxiGraph.FastDistanceMeters(own.Lat, own.Lon, s.Latitude, s.Longitude);
            if (d < best) { best = d; nearest = s; }
        }
        return nearest != null && best <= ParkedAtStandMaxDistanceMetres ? IdentityLabel(nearest) : null;
    }

    /// <summary>
    /// SayIntentions' arrival gate. A flight-file gate is used as published. A parking-service gate (SAPI does not say
    /// whether it means the arrival gate or the aircraft's current parking) is refused, with a note, when its published
    /// position lies beyond <see cref="AtAirportMaxDistanceMetres"/> of the arrival airport; one with no position is
    /// used by name (live KMEM→KATL: "B3" with no position WAS the arrival gate).
    /// </summary>
    public static ArrivalGateChoice ResolveArrivalGate(SayIntentionsGateHint? hint, string destinationIcao, Airport? destination)
    {
        if (hint == null || string.IsNullOrWhiteSpace(hint.Label)) return new ArrivalGateChoice(null, null);
        if (hint.Source == SayIntentionsGateSource.ParkingService && hint.Position is { } pin && destination != null &&
            TaxiGraph.FastDistanceMeters(pin.Latitude, pin.Longitude, destination.Latitude, destination.Longitude) > AtAirportMaxDistanceMetres)
            return new ArrivalGateChoice(null,
                $"SayIntentions' parking service named {hint.Label}, but its position is not at {destinationIcao}, so it is not used");
        return new ArrivalGateChoice(hint.Label, null);
    }

    /// <summary>"A 24A", "Gate 5" — the part of <see cref="ParkingSpot.Describe"/> before its first spaced dash.</summary>
    public static string IdentityLabel(ParkingSpot spot)
    {
        string d = spot.Describe();
        int cut = d.IndexOf(" - ", StringComparison.Ordinal);
        return cut > 0 ? d[..cut] : d;
    }

    private static string RunwayText(BriefingRunway r) => string.IsNullOrWhiteSpace(r.Runway) ? NoRunway : r.Runway;

    private static void AddNote(List<string> lines, string? note)
    {
        if (!string.IsNullOrWhiteSpace(note)) lines.Add($"  Note: {note}");
    }

    private static string AircraftLine(AircraftProfile a, DistanceUnit unit)
    {
        string size = a.CodeLetter == IcaoCodeLetter.Unknown ? "size class unknown" : $"size class {a.CodeLetter}";
        string span = a.WingspanMetres is double m
            ? $"wingspan {FormatSize(m, unit)}"
            : "wingspan unknown (aircraft type not recognised)";
        string role = a.IsFreighter ? "freighter: cargo stands preferred" : "passenger";
        string code = string.IsNullOrEmpty(a.TypeCode) ? "unknown" : a.TypeCode;
        return $"Aircraft: {a.DisplayName} (SimBrief type {code}), {size}, {span}, {role}";
    }

    /// <summary>A wingspan: metres to one decimal, or whole feet.</summary>
    private static string FormatSize(double metres, DistanceUnit unit) => unit == DistanceUnit.Feet
        ? $"{Math.Round(metres * DistanceFormatter.FeetPerMetre).ToString("N0", CultureInfo.InvariantCulture)} ft"
        : $"{metres.ToString("0.0", CultureInfo.InvariantCulture)} m";
}
