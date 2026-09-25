using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

internal static partial class GroundTrafficLogic
{
    private static readonly Regex RxCallsign = new(@"^([A-Z]{2,4})(\d{1,5}[A-Z]{0,2})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> SpokenTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A19N"] = "A319", ["A20N"] = "A320", ["A21N"] = "A321",
        ["A332"] = "A330", ["A333"] = "A330", ["A338"] = "A330", ["A339"] = "A330",
        ["A342"] = "A340", ["A343"] = "A340", ["A345"] = "A340", ["A346"] = "A340",
        ["A359"] = "A350", ["A35K"] = "A350", ["A388"] = "A380",
        ["BCS1"] = "A220", ["BCS3"] = "A220",
        ["B37M"] = "737 MAX", ["B38M"] = "737 MAX", ["B39M"] = "737 MAX", ["B3XM"] = "737 MAX",
        ["B736"] = "737", ["B737"] = "737", ["B738"] = "737", ["B739"] = "737",
        ["B744"] = "747", ["B748"] = "747", ["B74F"] = "747",
        ["B752"] = "757", ["B753"] = "757",
        ["B762"] = "767", ["B763"] = "767", ["B764"] = "767",
        ["B772"] = "777", ["B773"] = "777", ["B77L"] = "777", ["B77W"] = "777", ["B778"] = "777", ["B779"] = "777",
        ["B788"] = "787", ["B789"] = "787", ["B78X"] = "787",
        ["MD11"] = "MD-11", ["CRJ7"] = "CRJ", ["CRJ9"] = "CRJ", ["CRJX"] = "CRJ",
        ["E170"] = "Embraer 170", ["E75L"] = "Embraer 175", ["E190"] = "Embraer 190", ["E195"] = "Embraer 195",
        ["AT72"] = "ATR 72", ["AT76"] = "ATR 72", ["DH8D"] = "Dash 8", ["C172"] = "Cessna 172",
    };

    /// <summary>An aircraft type the way a pilot says it ("B77W" → "777").</summary>
    public static string SpokenType(string? rawType)
    {
        string icao = Forms.TcasForm.ShortenAircraftType(rawType ?? "");
        if (string.IsNullOrEmpty(icao)) return "";
        if (SpokenTypes.TryGetValue(icao, out var spoken)) return spoken;
        // A320 / A321 / B747 style: already speakable; drop the Boeing B.
        if (Regex.IsMatch(icao, @"^B7\d7$", RegexOptions.CultureInvariant)) return icao[1..];
        return icao;
    }

    /// <summary>
    /// A callsign spaced for speech ("DAL123" → "DAL 123", "EZY45MR" → "EZY 45MR"). The ONE callsign
    /// formatter — <c>TcasForm.FormatCallsign</c> delegates here (PR #247 review L8). Registrations
    /// (N12345), and anything already containing a space or a hyphen, come back trimmed but unchanged;
    /// null or blank comes back "".
    /// </summary>
    public static string SpokenCallsign(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw.Trim();
        if (raw.Contains(' ') || raw.Contains('-')) return raw;
        var m = RxCallsign.Match(raw.ToUpperInvariant());
        return m.Success ? $"{m.Groups[1].Value} {m.Groups[2].Value}" : raw;
    }

    /// <summary>
    /// How to name an aircraft in a callout, the way ATC would: "Delta A320" when the airline is
    /// known, else "DAL 123, A320", else the type alone, else "traffic".
    /// </summary>
    public static string SpokenName(string? airline, string? callsign, string? rawType)
    {
        string type = SpokenType(rawType);
        string air = (airline ?? "").Trim();
        if (air.Length > 0)
            return type.Length > 0 ? $"{air} {type}" : $"{air} traffic";
        string cs = SpokenCallsign(callsign);
        if (cs.Length > 0)
            return type.Length > 0 ? $"{cs}, {type}" : cs;
        return type.Length > 0 ? type : "traffic";
    }

    /// <summary>
    /// The Alt+G summary's name: the callout name plus the spaced callsign when the airline hid it
    /// ("Delta A320, DAL 1234") — at a hub several "Delta A320"s are otherwise indistinguishable, and
    /// ATC addresses them by callsign (PR #247 review L3). Callouts keep the short name.
    /// </summary>
    public static string SpokenNameWithCallsign(string? airline, string? callsign, string? rawType)
    {
        string name = SpokenName(airline, callsign, rawType);
        string cs = SpokenCallsign(callsign);
        return (airline ?? "").Trim().Length > 0 && cs.Length > 0 ? $"{name}, {cs}" : name;
    }

    /// <summary>
    /// Rebuild a name that has no type once a type is known — the VATSIM feed loads lazily, so the
    /// first lookup for a new callsign often returns "" (PR #247 review L3).
    /// </summary>
    public static bool NameNeedsRefresh(bool nameHasType, string? resolvedType)
        => !nameHasType && SpokenType(resolvedType).Length > 0;
}
