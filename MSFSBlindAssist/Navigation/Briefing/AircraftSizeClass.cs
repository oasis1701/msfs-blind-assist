using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>ICAO Annex 14 aerodrome reference code letter, by wingspan.</summary>
public enum IcaoCodeLetter { Unknown, A, B, C, D, E, F }

/// <summary>
/// What the route briefing knows about the aircraft, from the SimBrief OFP ALONE (owner's
/// choice — never the loaded aircraft definition, never the sim's WING SPAN). A type the table
/// does not know keeps a null wingspan and the Unknown letter; nothing is guessed.
/// </summary>
public sealed record AircraftProfile(
    string TypeCode, string DisplayName, double? WingspanMetres,
    IcaoCodeLetter CodeLetter, bool IsFreighter, double TouchdownSpeedKts);

public static class AircraftSizeClass
{
    /// <summary>Wingspan in metres by ICAO/SimBrief type code. A missing type is a one-line addition.</summary>
    private static readonly Dictionary<string, double> WingspanByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A388"] = 79.75, ["A35K"] = 64.75, ["A359"] = 64.75, ["A346"] = 63.45, ["A345"] = 63.45,
        ["A343"] = 60.3, ["A342"] = 60.3, ["A339"] = 64.0, ["A338"] = 64.0, ["A333"] = 60.3, ["A332"] = 60.3,
        ["A33F"] = 60.3, ["A306"] = 44.84, ["A3ST"] = 44.84, ["A310"] = 43.9,
        ["A21N"] = 35.8, ["A20N"] = 35.8, ["A19N"] = 35.8, ["A321"] = 34.1, ["A320"] = 34.1, ["A319"] = 34.1, ["A318"] = 34.1,
        ["BCS1"] = 35.1, ["BCS3"] = 35.1, ["C919"] = 35.8,
        ["B748"] = 68.4, ["B74F"] = 68.4, ["B744"] = 64.44, ["B741"] = 59.64, ["B742"] = 59.64, ["B743"] = 59.64,
        ["B77W"] = 64.8, ["B77L"] = 64.8, ["B77F"] = 64.8, ["B773"] = 60.93, ["B772"] = 60.93,
        ["B78X"] = 60.12, ["B789"] = 60.12, ["B788"] = 60.12,
        ["B764"] = 51.92, ["B763"] = 47.57, ["B762"] = 47.57, ["B76F"] = 47.57, ["B753"] = 38.05, ["B752"] = 38.05,
        ["B3XM"] = 35.92, ["B39M"] = 35.92, ["B38M"] = 35.92, ["B37M"] = 35.92,
        ["B739"] = 35.79, ["B738"] = 35.79, ["B737"] = 35.79, ["B736"] = 35.79,
        ["B735"] = 28.88, ["B734"] = 28.88, ["B733"] = 28.88, ["B732"] = 28.35, ["B722"] = 32.92, ["B712"] = 28.45,
        ["MD11"] = 51.7, ["MD1F"] = 51.7, ["DC10"] = 50.4, ["DC1F"] = 50.4,
        ["MD90"] = 32.87, ["MD88"] = 32.87, ["MD83"] = 32.87, ["MD82"] = 32.87, ["L101"] = 47.34,
        ["E295"] = 35.12, ["E290"] = 33.72, ["E195"] = 28.72, ["E190"] = 28.72, ["E75L"] = 28.65,
        ["E175"] = 26.0, ["E75S"] = 26.0, ["E170"] = 26.0,
        ["CRJX"] = 26.18, ["CRJ9"] = 24.85, ["CRJ7"] = 23.25, ["CRJ2"] = 21.21, ["DH8D"] = 28.42,
        ["AT76"] = 27.05, ["AT75"] = 27.05, ["AT72"] = 27.05, ["AT46"] = 24.57, ["AT45"] = 24.57, ["AT43"] = 24.57,
        ["B463"] = 26.34, ["B462"] = 26.34, ["B461"] = 26.34, ["F100"] = 28.08, ["F70"] = 28.08, ["SU95"] = 27.8,
        ["A124"] = 73.3, ["A225"] = 88.4, ["IL96"] = 60.1, ["C130"] = 40.4, ["C30J"] = 40.4, ["A400"] = 42.4, ["C17"] = 51.75,
        ["GL7T"] = 31.7, ["GLF6"] = 30.36, ["F900"] = 19.33, ["B350"] = 17.65, ["C56X"] = 17.17, ["C25C"] = 16.26,
        ["PC12"] = 16.28, ["C208"] = 15.88, ["TBM9"] = 12.68, ["C172"] = 11.0,
    };

    /// <summary>SimBrief-style codes that are freighters by definition (belt-and-braces beside the name test).</summary>
    private static readonly HashSet<string> FreighterTypes = new(StringComparer.OrdinalIgnoreCase)
        { "MD1F", "B74F", "B76F", "B77F", "A33F", "DC1F" };

    // "Boeing 777F", "747-8F", "MD-11F", "737-800BCF", "757-200PF", "A330-200F", "767-300 Freighter".
    // The letter group must follow a digit, dash or space so "Fokker 100" and "F100" never match.
    private static readonly Regex FreighterName = new(
        @"(?:\d|-|\s)(?:F|BCF|BDSF|SF|PF|PCF|ERF|LRF)\b|\bfreighter\b|\bcargo\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static AircraftProfile Resolve(string? typeCode, string? name, int? maxPassengers)
    {
        string code = (typeCode ?? "").Trim().ToUpperInvariant();
        string display = !string.IsNullOrWhiteSpace(name) ? name.Trim()
                       : code.Length > 0 ? code : "unknown aircraft";
        double? span = TryGetWingspanMetres(code, out double metres) ? metres : null;
        var letter = span is double m ? LetterForWingspan(m) : IcaoCodeLetter.Unknown;
        return new AircraftProfile(code, display, span, letter,
            LooksLikeFreighter(code, name, maxPassengers), TouchdownSpeedKts(letter));
    }

    public static bool TryGetWingspanMetres(string? typeCode, out double metres)
    {
        metres = 0;
        return !string.IsNullOrWhiteSpace(typeCode) && WingspanByType.TryGetValue(typeCode.Trim(), out metres);
    }

    /// <summary>Annex 14 code letter: A &lt; 15 m, B &lt; 24, C &lt; 36, D &lt; 52, E &lt; 65, F otherwise.</summary>
    public static IcaoCodeLetter LetterForWingspan(double metres) => metres switch
    {
        < 15.0 => IcaoCodeLetter.A,
        < 24.0 => IcaoCodeLetter.B,
        < 36.0 => IcaoCodeLetter.C,
        < 52.0 => IcaoCodeLetter.D,
        < 65.0 => IcaoCodeLetter.E,
        _ => IcaoCodeLetter.F,
    };

    /// <summary>Typical touchdown ground speed used to judge which landing exit is comfortably reachable.</summary>
    public static double TouchdownSpeedKts(IcaoCodeLetter letter) => letter switch
    {
        IcaoCodeLetter.A => 70.0,
        IcaoCodeLetter.B => 115.0,
        IcaoCodeLetter.C => 130.0,
        IcaoCodeLetter.D => 135.0,
        IcaoCodeLetter.E => 140.0,
        IcaoCodeLetter.F => 140.0,
        _ => 130.0,
    };

    /// <summary>Annex 14 minimum straight taxiway width per code letter; 0 = no advisory for an unknown type.</summary>
    public static double MinTaxiwayWidthMetres(IcaoCodeLetter letter) => letter switch
    {
        IcaoCodeLetter.A => 7.5,
        IcaoCodeLetter.B => 10.5,
        IcaoCodeLetter.C => 15.0,
        IcaoCodeLetter.D => 18.0,
        IcaoCodeLetter.E => 23.0,
        IcaoCodeLetter.F => 25.0,
        _ => 0.0,
    };

    public static bool LooksLikeFreighter(string? typeCode, string? name, int? maxPassengers)
    {
        if (maxPassengers == 0) return true;
        if (!string.IsNullOrWhiteSpace(typeCode) && FreighterTypes.Contains(typeCode.Trim())) return true;
        return !string.IsNullOrWhiteSpace(name) && FreighterName.IsMatch(name);
    }
}
