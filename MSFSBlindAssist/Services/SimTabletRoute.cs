using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Models;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The pure half of the sim tablet's SimBrief import (<see cref="SimTabletSimBriefImport"/>): the
/// route text typed into the Flight Planner's "Enter route string" box, the reading of the route
/// the tablet then shows (the reader's <c>route.state()</c> JSON), and the sentence spoken at the end.
///
/// MEASURED LIVE 2026-10-19 (MSFS 2024, C172 G1000, OFP KFFC-KSAV): the box takes
/// <c>ORIGIN route DESTINATION</c>, "KFFC DCT SOONE DCT NOKIE DCT KSAV", and loads both airports
/// and the en-route fixes. A runway written into it ("KSAV/01") is IGNORED: the tablet picks its
/// own (it picked 10 where SimBrief planned 01), so the runways are set afterwards through the
/// airports' runway dropdowns.
///
/// READ FROM THE TABLET'S PARSER (atlasapp.js, EfbRouteParserService, 2026-10-19): the word right
/// after the departure airport is matched against that airport's departure procedures and the word
/// right before the destination against its arrivals, so a SID and a STAR are taken by name in
/// exactly those two places (their transitions and the approach are not: the approach is the one the
/// tablet picks for the arrival runway). A speed and level group after the departure airport
/// ("N0112F030") is parsed, but WRONG: its tryParseStepClimb takes the level from the SPEED digits
/// (measured: N0112F030 set the cruise box to FL112), so none is ever written. A jet OFP has not been
/// through the box live yet.
/// </summary>
public static class SimTabletRoute
{
    // The tablet's own STEP_CLIMB_REGEX: a speed and level group, never written (see above).
    private static readonly Regex SpeedLevelGroup = new(@"^[KNM]\d{3,4}[FSAM]\d{3,4}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// "ORIGIN [SID] route [STAR] DESTINATION", or null when the plan names no origin or destination.
    /// SimBrief's SID and STAR (<see cref="SimBriefOFP.SidIdent"/>, <see cref="SimBriefOFP.StarIdent"/>)
    /// are put where the tablet looks for them when the route does not already start or end with them;
    /// any speed and level group is dropped.
    /// </summary>
    public static string? Build(SimBriefOFP ofp)
    {
        string origin = (ofp.OriginIcao ?? "").Trim().ToUpperInvariant();
        string dest = (ofp.DestIcao ?? "").Trim().ToUpperInvariant();
        if (origin.Length == 0 || dest.Length == 0) return null;

        var words = Regex.Split((ofp.Route ?? "").Trim(), @"\s+")
            .Where(w => w.Length > 0 && !SpeedLevelGroup.IsMatch(w.ToUpperInvariant()))
            .ToList();
        string sid = (ofp.SidIdent ?? "").Trim().ToUpperInvariant();
        string star = (ofp.StarIdent ?? "").Trim().ToUpperInvariant();
        if (sid.Length > 0 && (words.Count == 0 || !words[0].Equals(sid, StringComparison.OrdinalIgnoreCase)))
            words.Insert(0, sid);
        if (star.Length > 0 && (words.Count == 0 || !words[^1].Equals(star, StringComparison.OrdinalIgnoreCase)))
            words.Add(star);

        return words.Count == 0 ? $"{origin} {dest}" : $"{origin} {string.Join(' ', words)} {dest}";
    }

    /// <summary>
    /// A runway designator with no leading zero and its side letter kept: SimBrief "01", the
    /// tablet's "runway 01" and "RW01" all give "1"; "16l" gives "16L". Empty when there is none.
    /// The reader's <c>route.rwyKey</c> is the same rule.
    /// </summary>
    public static string RunwayKey(string? s)
    {
        var m = Regex.Match((s ?? "").Trim(), @"(\d{1,2})\s*([LRClrc]?)\s*$");
        return m.Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + m.Groups[2].Value.ToUpperInvariant() : "";
    }

    /// <summary>The reader's <c>route.state()</c> JSON, or null when it is not a state.</summary>
    public static RouteState? ParseState(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var st = JsonSerializer.Deserialize<RouteState>(json, JsonOptions);
            return st is { ok: true } ? st : null;
        }
        catch (JsonException) { return null; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>True once the tablet shows the plan's two airports, origin first.</summary>
    public static bool HasAirports(RouteState st, string origin, string dest) =>
        st.airports.Count >= 2 &&
        string.Equals(st.airports[0].icao, origin, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(st.airports[1].icao, dest, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The sentence spoken once the route is in: both ends with their runways, how many en-route
    /// fixes, and every procedure the tablet chose, because it picks one by itself when a runway is
    /// set (arrival runway 01 at KSAV brought "ILS 1 via Direct"). <paramref name="notes"/> are the
    /// import's own remarks (a runway SimBrief planned that the tablet does not list), spoken last.
    /// </summary>
    public static string Summary(RouteState st, IReadOnlyList<string>? notes = null)
    {
        var sb = new StringBuilder("SimBrief plan loaded: ");
        sb.Append(End(st, 0)).Append(" to ").Append(End(st, 1));
        int n = st.waypoints.Count;
        sb.Append(", ").Append(n == 1 ? "1 waypoint" : $"{n} waypoints").Append('.');
        string[] names = { "Departure", "Arrival", "Approach" };
        for (int i = 0; i < names.Length && i < st.procedures.Count; i++)
        {
            string p = (st.procedures[i] ?? "").Trim();
            if (p.Length == 0 || p.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
            sb.Append(' ').Append(names[i]).Append(": ").Append(p).Append('.');
        }
        if (notes != null) foreach (var note in notes) sb.Append(' ').Append(note);
        sb.Append(" Use Send to avionics when you are ready.");
        return sb.ToString();
    }

    private static string End(RouteState st, int i)
    {
        if (i >= st.airports.Count) return "unknown";
        var a = st.airports[i];
        string rwy = RunwayKey(a.runway);
        return rwy.Length == 0 ? a.icao : $"{a.icao} runway {Designator(a.runway)}";
    }

    // The designator as the tablet shows it ("runway 01" -> "01"), so a leading zero is kept.
    private static string Designator(string runway)
    {
        var m = Regex.Match(runway ?? "", @"(\d{1,2}\s*[LRCLrc]?)\s*$");
        return m.Success ? m.Groups[1].Value.Replace(" ", "").ToUpperInvariant() : runway ?? "";
    }

    // The reader's route.state() shape. Lower-case names are the JSON's.
#pragma warning disable IDE1006
    public sealed class RouteState
    {
        public bool ok { get; set; }
        public bool atlas { get; set; }
        public bool routeShown { get; set; }
        public bool routeBoxOpen { get; set; }
        public List<Airport> airports { get; set; } = new();
        public List<string> procedures { get; set; } = new();
        public List<string> waypoints { get; set; } = new();
    }

    public sealed class Airport
    {
        public string text { get; set; } = "";
        public string icao { get; set; } = "";
        public string runway { get; set; } = "";
        public string runwayKey { get; set; } = "";
        public List<string> runways { get; set; } = new();
    }
#pragma warning restore IDE1006
}
