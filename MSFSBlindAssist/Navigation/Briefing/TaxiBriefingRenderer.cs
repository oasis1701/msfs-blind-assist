using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// The plain-text TAXI ROUTES block the AI receives (appended to the SimBrief flight data for one
/// Describe Route call). Every line is data for the prompt's section 7: it names the tier a leg came
/// from, keeps taxiway/exit/stand names exactly as the source spells them, and states every caveat.
/// InvariantCulture throughout; "\n" line ends; no markdown.
/// </summary>
public static class TaxiBriefingRenderer
{
    public const string Header = "TAXI ROUTES (computed by MSFS Blind Assist; each leg names its data source, and taxiway names are that source's own)";
    public const string OsmLabel = "OpenStreetMap, planning only — taxi guidance cannot use this";
    /// <summary>The exits list names the nearest this many and counts the rest.</summary>
    public const int MaxListedExits = 12;

    public static string Render(TaxiBriefing b)
    {
        var lines = new List<string> { Header, AircraftLine(b.Aircraft) };
        RenderTaxiOut(b.TaxiOut, b.Aircraft, lines);
        RenderTaxiIn(b.TaxiIn, b.Aircraft, lines);
        return string.Join("\n", lines);
    }

    public static string TierLabel(BriefingTier tier) => tier switch
    {
        BriefingTier.Navdata => "scenery navdata",
        BriefingTier.OpenStreetMap => OsmLabel,
        _ => "",
    };

    public static string FormatDistance(double metres) => metres < 1000
        ? $"{Math.Round(metres).ToString("0", CultureInfo.InvariantCulture)} m"
        : $"{(metres / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} km";

    private static string AircraftLine(AircraftProfile a)
    {
        string size = a.CodeLetter == IcaoCodeLetter.Unknown ? "size class unknown" : $"size class {a.CodeLetter}";
        string span = a.WingspanMetres is double m
            ? $"wingspan {m.ToString("0.0", CultureInfo.InvariantCulture)} m"
            : "wingspan unknown (aircraft type not recognised)";
        string role = a.IsFreighter ? "freighter: cargo stands preferred" : "passenger";
        string code = string.IsNullOrEmpty(a.TypeCode) ? "unknown" : a.TypeCode;
        return $"Aircraft: {a.DisplayName} (SimBrief type {code}), {size}, {span}, {role}";
    }

    private static void RenderTaxiOut(TaxiLegBriefing leg, AircraftProfile aircraft, List<string> lines)
    {
        if (leg.Unavailable != null)
        {
            lines.Add($"TAXI OUT at {leg.Icao}: taxi route unavailable — {leg.Unavailable}");
            if (leg.EndpointDescription.Length > 0) lines.Add($"  Start: {leg.EndpointDescription}");
        }
        else
        {
            lines.Add($"TAXI OUT at {leg.Icao} ({TierLabel(leg.Tier)}): from {leg.EndpointDescription} to runway {leg.Runway}");
            lines.Add($"  Taxiways: {JoinNames(leg.Taxiways)} ({FormatDistance(leg.DistanceMetres)})");
            lines.Add(HoldLine(leg.HoldShorts));
            foreach (var n in leg.NarrowTaxiways) lines.Add(NarrowLine(n, aircraft.CodeLetter));
        }
        foreach (var note in leg.Notes) lines.Add($"  Note: {note}");
    }

    private static void RenderTaxiIn(TaxiLegBriefing leg, AircraftProfile aircraft, List<string> lines)
    {
        if (leg.Unavailable != null)
        {
            lines.Add($"TAXI IN at {leg.Icao}: taxi route unavailable — {leg.Unavailable}");
            if (leg.Exit != null) lines.Add(ExitLine(leg.Exit, aircraft));
            if (leg.EndpointDescription.Length > 0) lines.Add($"  Stand: {leg.EndpointDescription}");
            lines.Add(ExitsListLine(leg));
        }
        else
        {
            lines.Add($"TAXI IN at {leg.Icao} ({TierLabel(leg.Tier)}), landing runway {leg.Runway}");
            if (leg.Exit != null) lines.Add(ExitLine(leg.Exit, aircraft));
            lines.Add($"  Stand: {leg.EndpointDescription}");
            lines.Add($"  Taxiways from the exit: {JoinNames(leg.Taxiways)} ({FormatDistance(leg.DistanceMetres)})");
            lines.Add(HoldLine(leg.HoldShorts));
            foreach (var n in leg.NarrowTaxiways) lines.Add(NarrowLine(n, aircraft.CodeLetter));
            lines.Add(ExitsListLine(leg));
        }
        foreach (var note in leg.Notes) lines.Add($"  Note: {note}");
    }

    private static string HoldLine(IReadOnlyList<HoldShortNote> holds)
    {
        if (holds.Count == 0) return "  No runway crossings on this route.";
        var parts = holds.Select(h =>
            $"runway {h.Runway} on taxiway {h.Taxiway} ({(h.BeforeEntering ? "before entering" : "crossing")})");
        return "  Hold short: " + string.Join("; ", parts);
    }

    private static string NarrowLine(NarrowTaxiwayNote n, IcaoCodeLetter letter) =>
        $"  Taxiway width note: taxiway {n.Taxiway} is {n.WidthMetres.ToString("0.0", CultureInfo.InvariantCulture)} m in the navdata, " +
        $"below the {n.MinimumMetres.ToString("0.0", CultureInfo.InvariantCulture)} m code {letter} minimum";

    private static string ExitLine(ExitChoice choice, AircraftProfile aircraft)
    {
        var e = choice.Exit;
        var sb = new StringBuilder();
        sb.Append($"  Expected exit: taxiway {e.TaxiwayName}, {e.ExitType.ToLowerInvariant()}, {SideUpper(e.ExitSide)}, {Feet(e.DistanceFromThresholdFeet)} ft from the threshold.");
        sb.Append(choice.NextExit is { } n
            ? $" Next exit if missed: {n.TaxiwayName}, {SideLower(n.ExitSide)}, {Feet(n.DistanceFromThresholdFeet)} ft"
            : " No later exit is mapped.");
        if (!choice.ComfortablyReachable)
            sb.Append($" This runway is short for this aircraft: no exit is comfortably reachable at {aircraft.TouchdownSpeedKts.ToString("0", CultureInfo.InvariantCulture)} kt; the last exit is briefed.");
        return sb.ToString();
    }

    private static string ExitsListLine(TaxiLegBriefing leg)
    {
        if (leg.VacatingExits.Count == 0) return $"  Exits on {leg.Runway} that get clear of the runway: none found";
        var shown = leg.VacatingExits.Take(MaxListedExits)
            .Select(e => $"{e.TaxiwayName} ({Feet(e.DistanceFromThresholdFeet)} ft, {SideLowerBare(e.ExitSide)}, {e.ExitType.ToLowerInvariant()})");
        string list = string.Join(", ", shown);
        int more = leg.VacatingExits.Count - MaxListedExits;
        if (more > 0) list += $", … and {more} more";
        return $"  Exits on {leg.Runway} that get clear of the runway: {list}";
    }

    private static string JoinNames(IReadOnlyList<string> names) => names.Count == 0 ? "(unnamed)" : string.Join(", ", names);
    private static string Feet(double ft) => Math.Round(ft).ToString("N0", CultureInfo.InvariantCulture);
    private static string SideUpper(string side) => string.IsNullOrEmpty(side) ? "side unknown" : $"{side.ToUpperInvariant()} side";
    private static string SideLower(string side) => string.IsNullOrEmpty(side) ? "side unknown" : $"{side.ToLowerInvariant()} side";
    private static string SideLowerBare(string side) => string.IsNullOrEmpty(side) ? "side unknown" : side.ToLowerInvariant();
}
