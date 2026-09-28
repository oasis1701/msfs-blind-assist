using System.Globalization;
using System.Text;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// The plain-text TAXI ROUTES block the AI receives (appended to the SimBrief flight data for one
/// Describe Route call). Every line is data for the prompt's section 7: it names the tier a leg came
/// from, keeps taxiway/exit/stand names exactly as the source spells them, and states every caveat.
/// InvariantCulture throughout; "\n" line ends; no markdown. EVERY distance, length and width is in ONE unit, the
/// pilot's ground distance setting, and the block's second line names it: live KMEM→KATL (2026-09-26) the taxi
/// distances were kilometres and the exit distances feet, and the AI read "2.2 kilometers" beside "6,025 feet".
/// </summary>
public static class TaxiBriefingRenderer
{
    public const string Header = "TAXI ROUTES (computed by MSFS Blind Assist; each leg names its data source, and taxiway names are that source's own)";
    public const string OsmLabel = "OpenStreetMap, planning only — taxi guidance cannot use this";
    /// <summary>The exits list names the nearest this many and counts the rest.</summary>
    public const int MaxListedExits = 12;

    /// <param name="unit">The pilot's ground distance setting (<see cref="DistanceFormatter.UnitProvider"/>).</param>
    public static string Render(TaxiBriefing b, DistanceUnit unit)
    {
        var lines = new List<string> { Header, UnitLine(unit), AircraftLine(b.Aircraft, unit) };
        RenderTaxiOut(b.TaxiOut, b.Aircraft, unit, lines);
        RenderTaxiIn(b.TaxiIn, b.Aircraft, unit, lines);
        return string.Join("\n", lines);
    }

    /// <summary>The line the prompt tells the AI to take its unit from.</summary>
    public static string UnitLine(DistanceUnit unit)
    {
        string word = unit == DistanceUnit.Feet ? "feet" : "metres";
        return $"Distance unit: {word} (the pilot's setting); every distance below is in {word}";
    }

    public static string TierLabel(BriefingTier tier) => tier switch
    {
        BriefingTier.Navdata => "scenery navdata",
        BriefingTier.OpenStreetMap => OsmLabel,
        _ => "",
    };

    /// <summary>A taxi distance. Feet: whole feet ("7,218 ft"). Metres: metres under a kilometre, else kilometres to one
    /// decimal — the switch is at 999.5 m, where whole metres would round up to "1000 m".</summary>
    public static string FormatDistance(double metres, DistanceUnit unit)
    {
        if (unit == DistanceUnit.Feet) return $"{Whole(metres * DistanceFormatter.FeetPerMetre)} ft";
        return metres < 999.5
            ? $"{Math.Round(metres).ToString("0", CultureInfo.InvariantCulture)} m"
            : $"{(metres / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} km";
    }

    /// <summary>A distance along the runway, measured in feet by the exit finder: whole feet, or whole metres
    /// ("1,890 m" — never kilometres, as a runway distance is read).</summary>
    public static string FormatAlongRunway(double feet, DistanceUnit unit) => unit == DistanceUnit.Feet
        ? $"{Whole(feet)} ft"
        : $"{Whole(feet * DistanceFormatter.MetresPerFoot)} m";

    /// <summary>A wingspan or a width: metres to one decimal, or whole feet.</summary>
    public static string FormatSize(double metres, DistanceUnit unit) => unit == DistanceUnit.Feet
        ? $"{Whole(metres * DistanceFormatter.FeetPerMetre)} ft"
        : $"{metres.ToString("0.0", CultureInfo.InvariantCulture)} m";

    /// <summary>What the block calls a taxiway or exit that has no name — LandingExit.ToString()'s own word.</summary>
    public const string Unnamed = "(unnamed)";

    /// <summary>An exit's taxiway name, or <see cref="Unnamed"/> — never a blank.</summary>
    public static string ExitName(LandingExit exit) =>
        string.IsNullOrEmpty(exit.TaxiwayName) ? Unnamed : exit.TaxiwayName;

    /// <summary>
    /// An exit's type in the block's words. "End" is GetLandingExits' class for an exit in the last 15 % of the runway
    /// OR one turning more than 110°, and a bare "end" read as the former only.
    /// </summary>
    public static string ExitTypeText(LandingExit exit) =>
        string.Equals(exit.ExitType, "End", StringComparison.OrdinalIgnoreCase)
            ? "end-of-runway or sharp-angle"
            : exit.ExitType.ToLowerInvariant();

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

    private static void RenderTaxiOut(TaxiLegBriefing leg, AircraftProfile aircraft, DistanceUnit unit, List<string> lines)
    {
        if (leg.Unavailable != null)
        {
            string toRunway = string.IsNullOrWhiteSpace(leg.Runway) ? "" : $" to runway {leg.Runway}";
            lines.Add($"TAXI OUT at {leg.Icao}{toRunway}: taxi route unavailable — {leg.Unavailable}");
            if (leg.EndpointDescription.Length > 0) lines.Add($"  Start: {leg.EndpointDescription}");
        }
        else
        {
            lines.Add($"TAXI OUT at {leg.Icao} ({TierLabel(leg.Tier)}): from {leg.EndpointDescription} to runway {leg.Runway}");
            lines.Add($"  Taxiways: {RouteText(leg)} ({FormatDistance(leg.DistanceMetres, unit)})");
            lines.Add(HoldLine(leg.HoldShorts, leg.UnheldRunways));
            foreach (var n in leg.NarrowTaxiways) lines.Add(NarrowLine(n, aircraft.CodeLetter, unit));
        }
        AddAirportTaxiways(leg, lines);
        foreach (var note in leg.Notes) lines.Add($"  Note: {note}");
    }

    private static void RenderTaxiIn(TaxiLegBriefing leg, AircraftProfile aircraft, DistanceUnit unit, List<string> lines)
    {
        if (leg.Unavailable != null)
        {
            string landing = string.IsNullOrWhiteSpace(leg.Runway) ? "" : $", landing runway {leg.Runway}";
            lines.Add($"TAXI IN at {leg.Icao}{landing}: taxi route unavailable — {leg.Unavailable}");
            if (leg.Exit != null) lines.Add(ExitLine(leg.Exit, aircraft, unit));
            if (leg.EndpointDescription.Length > 0) lines.Add($"  Stand: {leg.EndpointDescription}");
            // Only a search that ran can have found none (no database, a timeout or an unknown runway ran none).
            if (leg.ExitsSearched) lines.Add(ExitsListLine(leg, unit));
        }
        else
        {
            lines.Add($"TAXI IN at {leg.Icao} ({TierLabel(leg.Tier)}), landing runway {leg.Runway}");
            if (leg.Exit != null) lines.Add(ExitLine(leg.Exit, aircraft, unit));
            lines.Add($"  Stand: {leg.EndpointDescription}");
            // An exit whose route begins on the stand's own node leads straight onto it: a route with no taxiways.
            lines.Add(leg.Taxiways.Count == 0 && leg.DistanceMetres < 1.0
                ? "  Taxiways from the exit: none (the exit leads straight to the stand)"
                : $"  Taxiways from the exit: {RouteText(leg)} ({FormatDistance(leg.DistanceMetres, unit)})");
            lines.Add(HoldLine(leg.HoldShorts, leg.UnheldRunways));
            foreach (var n in leg.NarrowTaxiways) lines.Add(NarrowLine(n, aircraft.CodeLetter, unit));
            lines.Add(ExitsListLine(leg, unit));
        }
        AddAirportTaxiways(leg, lines);
        foreach (var note in leg.Notes) lines.Add($"  Note: {note}");
    }

    /// <summary>Every taxiway name at the leg's airport: the prompt holds any taxiway the AI names to this line and the
    /// route (owner, 2026-09-26). Absent for a leg never planned on a graph.</summary>
    private static void AddAirportTaxiways(TaxiLegBriefing leg, List<string> lines)
    {
        if (leg.AirportTaxiways.Count > 0)
            lines.Add($"  Taxiway names at {leg.Icao}: {string.Join(", ", leg.AirportTaxiways)}");
    }

    /// <summary>One entry per hold; a hold on no named taxiway (the whole route unnamed) names the runway alone,
    /// never "on taxiway" followed by a blank. With no placed hold, an unheld runway (its own note is elsewhere in
    /// the block) must never read as "No runway crossings" — that would contradict the note.</summary>
    private static string HoldLine(IReadOnlyList<HoldShortNote> holds, IReadOnlyList<string> unheldRunways)
    {
        if (holds.Count == 0)
            return unheldRunways.Count == 0
                ? "  No runway crossings on this route."
                : "  Hold short: none could be placed; see the notes.";
        var parts = holds.Select(h =>
        {
            string kind = h.BeforeEntering ? "before entering" : "crossing";
            return string.IsNullOrWhiteSpace(h.Taxiway)
                ? $"runway {h.Runway} ({kind})"
                : $"runway {h.Runway} on taxiway {h.Taxiway} ({kind})";
        });
        return "  Hold short: " + string.Join("; ", parts);
    }

    private static string NarrowLine(NarrowTaxiwayNote n, IcaoCodeLetter letter, DistanceUnit unit) =>
        $"  Taxiway width note: taxiway {n.Taxiway} is {FormatSize(n.WidthMetres, unit)} in the navdata, " +
        $"below the {FormatSize(n.MinimumMetres, unit)} code {letter} minimum";

    private static string ExitLine(ExitChoice choice, AircraftProfile aircraft, DistanceUnit unit)
    {
        var e = choice.Exit;
        var sb = new StringBuilder();
        sb.Append($"  Expected exit: taxiway {ExitName(e)}, {ExitTypeText(e)}, {SideUpper(e.ExitSide)}, {FormatAlongRunway(e.DistanceFromThresholdFeet, unit)} from the threshold.");
        // The exit if missed is the next USABLE one (a briefable candidate: it turns no more than 90° and its route
        // leaves on its own side) on the SAME side at least NextExitMinSeparationFeet further along
        // (BriefingExitPicker), so "none" means none of those — never that no later exit exists at all: the exits list
        // can still show a later one on that side that turns further, or was set aside (named just below when
        // comfortably reachable).
        string minimum = FormatAlongRunway(BriefingExitPicker.NextExitMinSeparationFeet, unit);
        sb.Append(choice.NextExit is { } n
            ? $" Next exit if missed: {ExitName(n)}, {SideLower(n.ExitSide)}, {FormatAlongRunway(n.DistanceFromThresholdFeet, unit)}"
            : string.IsNullOrEmpty(e.ExitSide)
                ? $" No later usable exit is mapped at least {minimum} further along."
                : $" No later usable exit on the same side is mapped at least {minimum} further along.");
        if (!choice.ComfortablyReachable)
            sb.Append(UnreachableSentence(choice, aircraft.TouchdownSpeedKts.ToString("0", CultureInfo.InvariantCulture),
                aircraft.CodeLetter == IcaoCodeLetter.Unknown));
        return sb.ToString();
    }

    /// <summary>Why the briefed exit is one the aircraft cannot comfortably make. When comfortably reachable exits were
    /// set aside for leading off the other side, that is what the pilot is told. Otherwise the planner's verdict on the
    /// runway (<see cref="ExitChoice.RunwayLength"/>) decides: short only when the aircraft cannot stop on it comfortably,
    /// a backtrack when it can and every exit lies behind the touchdown, and no claim at all when its length is unknown.
    /// An unrecognised SimBrief type has no measured touchdown speed — the 130 kt used is an assumption, not a
    /// runway-length fact, so it is never blamed on the runway being "short for this aircraft".</summary>
    private static string UnreachableSentence(ExitChoice choice, string kt, bool typeUnknown)
    {
        string ktPhrase = typeUnknown ? $"an assumed {kt} kt (the aircraft type is not recognised)" : $"{kt} kt";
        var setAside = choice.ReachableExitsSetAside;
        if (setAside.Count == 0)
            return choice.RunwayLength switch
            {
                UnreachableRunway.LongEnoughToBacktrack =>
                    $" No mapped exit is comfortably reachable at {ktPhrase}, but the runway is long enough to stop on: " +
                    "expect to backtrack on the runway to the last exit, which is briefed.",
                UnreachableRunway.Short when !typeUnknown =>
                    $" This runway is short for this aircraft: no exit is comfortably reachable at {ktPhrase}; the last exit is briefed.",
                _ => $" No exit is comfortably reachable at {ktPhrase}; the last exit is briefed.",
            };
        // Capped like the exits list: the first MaxListedExits, then how many more.
        string names = setAside.Count == 1 ? ExitName(setAside[0])
            : setAside.Count > MaxListedExits
                ? string.Join(", ", setAside.Take(MaxListedExits).Select(ExitName)) + $", … and {setAside.Count - MaxListedExits} more"
                : string.Join(", ", setAside.Take(setAside.Count - 1).Select(ExitName)) + " and " + ExitName(setAside[^1]);
        string tail = setAside.Count == 1
            ? $"{names} is comfortably reachable, but its mapped route leaves the runway on the other side."
            : $"{names} are comfortably reachable, but their mapped routes leave the runway on the other side.";
        return $" No exit whose mapped route leaves the runway on the side it turns toward is comfortably reachable at {ktPhrase}, " +
               $"so the last one that does is briefed; {tail}";
    }

    private static string ExitsListLine(TaxiLegBriefing leg, DistanceUnit unit)
    {
        if (leg.VacatingExits.Count == 0) return $"  Exits on {leg.Runway} that get clear of the runway: none found";
        var shown = leg.VacatingExits.Take(MaxListedExits)
            .Select(e => $"{ExitName(e)} ({FormatAlongRunway(e.DistanceFromThresholdFeet, unit)}, {SideLowerBare(e.ExitSide)}, {ExitTypeText(e)})");
        string list = string.Join(", ", shown);
        int more = leg.VacatingExits.Count - MaxListedExits;
        if (more > 0) list += $", … and {more} more";
        return $"  Exits on {leg.Runway} that get clear of the runway: {list}";
    }

    /// <summary>The taxiways in order with the turn onto each where one was measured ("N, left onto M, …") and, on the
    /// taxi-in, the turn into the stand ("…, then right into the stand"). A taxiway with no turn is named alone; a
    /// wholly unnamed route reads <see cref="Unnamed"/>.</summary>
    private static string RouteText(TaxiLegBriefing leg)
    {
        if (leg.Taxiways.Count == 0) return Unnamed;
        var parts = new List<string>(leg.Taxiways.Count + 1);
        for (int i = 0; i < leg.Taxiways.Count; i++)
        {
            string? turn = i < leg.TaxiwayTurns.Count ? leg.TaxiwayTurns[i] : null;
            parts.Add(turn == null ? leg.Taxiways[i] : $"{turn} onto {leg.Taxiways[i]}");
        }
        if (leg.StandTurn != null) parts.Add($"then {leg.StandTurn} into the stand");
        return string.Join(", ", parts);
    }

    private static string Whole(double value) => Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);
    private static string SideUpper(string side) => string.IsNullOrEmpty(side) ? "side unknown" : $"{side.ToUpperInvariant()} side";
    private static string SideLower(string side) => string.IsNullOrEmpty(side) ? "side unknown" : $"{side.ToLowerInvariant()} side";
    private static string SideLowerBare(string side) => string.IsNullOrEmpty(side) ? "side unknown" : side.ToLowerInvariant();
}
