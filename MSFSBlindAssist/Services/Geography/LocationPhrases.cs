using System.Globalization;

namespace MSFSBlindAssist.Services.Geography;

internal enum WaterKind { None, Sea, Lake }

/// <summary>The nearest populated place to a position.</summary>
internal sealed record PlaceHit(string Name, string State, string Country, double DistanceNm, double BearingTrue);

/// <summary>What the Natural Earth shapes (and, as a fallback, the nearest place) say about a position.</summary>
internal sealed record WhereYouAre
{
    public WaterKind Water { get; init; }
    public string? WaterName { get; init; }
    /// <summary>Distance to the sea coast (over sea or land) or to the lake's shore (over a lake);
    /// null when none was found in range.</summary>
    public double? EdgeNm { get; init; }
    public double EdgeBearingTrue { get; init; }
    public string? State { get; init; }
    public string? Country { get; init; }
    /// <summary>True when the state came from the nearest place, not from a state polygon
    /// containing the position (Natural Earth's states stop at the shore of a bay).</summary>
    public bool StateIsNearest { get; init; }
    public string? Region { get; init; }
    /// <summary>Spoken kind of the region, e.g. "mountain range".</summary>
    public string? RegionKind { get; init; }
    public string? TimeZone { get; init; }
}

/// <summary>
/// Everything Location Info and Output > C say, composed in one place. Over water the water
/// comes first ("Over Chesapeake Bay, coast 5.2 miles west. Near Dares Beach, …"); over land the
/// sentence is the familiar "Near …" one, with the coast added only when it is within 50 miles
/// (80 km). Over a lake the edge is its "shore", never the sea coast. Numbers are invariant:
/// a pilot on a German Windows still hears "5.4 miles".
/// </summary>
internal static class LocationPhrases
{
    internal const double MilesPerNm = 1.1507794480235425;
    internal const double KmPerNm = 1.852;
    internal const double CoastOverLandMiles = 50;
    internal const double CoastOverLandKm = 80;

    private static readonly string[] Directions =
        { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };

    /// <summary>Output C or Location Info pressed with no usable location data installed.</summary>
    internal static string Unavailable(bool needsUpdate) => needsUpdate
        ? "Location data needs an update. Update it in Settings, GeoNames tab."
        : "Location data not downloaded. Download it in Settings, GeoNames tab.";

    internal static double ToUnit(double nm, bool kilometres) => kilometres ? nm * KmPerNm : nm * MilesPerNm;

    /// <summary>The unit word as the settings store it and the online lookups spoke it.</summary>
    internal static string UnitWord(bool kilometres) => kilometres ? "kilometers" : "miles";

    /// <summary>Eight-point compass word, the same mapping the online lookups used.</summary>
    internal static string Direction(double bearingTrue)
    {
        double b = (bearingTrue % 360.0 + 360.0) % 360.0;
        return Directions[(int)Math.Round(b / 45.0) % 8];
    }

    /// <summary>A place's distance, one decimal as C has always said it ("5.4 miles").</summary>
    internal static string PlaceDistance(double nm, bool kilometres) =>
        Inv($"{ToUnit(nm, kilometres):0.0} {UnitWord(kilometres)}");

    /// <summary>A coast or shore distance: one decimal under 10, whole numbers above ("368 miles").</summary>
    internal static string EdgeDistance(double nm, bool kilometres)
    {
        double v = ToUnit(nm, kilometres);
        return v < 9.95
            ? Inv($"{v:0.0} {UnitWord(kilometres)}")
            : Inv($"{Math.Round(v):0} {UnitWord(kilometres)}");
    }

    internal static bool CoastCloseOverLand(double nm, bool kilometres) =>
        ToUnit(nm, kilometres) <= (kilometres ? CoastOverLandKm : CoastOverLandMiles);

    /// <summary>"Near Dares Beach, Maryland, United States, 5.4 miles west".</summary>
    internal static string PlaceClause(PlaceHit place, bool kilometres)
    {
        var s = "Near " + place.Name;
        if (!string.IsNullOrEmpty(place.State)) s += ", " + place.State;
        if (!string.IsNullOrEmpty(place.Country)) s += ", " + place.Country;
        return s + ", " + PlaceDistance(place.DistanceNm, kilometres) + " " + Direction(place.BearingTrue);
    }

    /// <summary>"coast 5.2 miles west" / "shore 3.0 miles north"; null when there is no edge.</summary>
    internal static string? EdgeClause(WhereYouAre where, bool kilometres)
    {
        if (where.EdgeNm is not double nm) return null;
        string word = where.Water == WaterKind.Lake ? "shore" : "coast";
        return word + " " + EdgeDistance(nm, kilometres) + " " + Direction(where.EdgeBearingTrue);
    }

    /// <summary>Output > C.</summary>
    internal static string NearestCitySentence(WhereYouAre where, PlaceHit? place, bool kilometres, int nearbyRange)
    {
        string? edge = EdgeClause(where, kilometres);
        if (where.Water != WaterKind.None && !string.IsNullOrEmpty(where.WaterName))
        {
            string lead = "Over " + where.WaterName + (edge is null ? "" : ", " + edge);
            return place is null ? lead : lead + ". " + PlaceClause(place, kilometres);
        }

        bool coastClose = edge is not null && CoastCloseOverLand(where.EdgeNm!.Value, kilometres);
        if (place is not null)
            return PlaceClause(place, kilometres) + (coastClose ? ", " + edge : "");

        var s = Inv($"No town within {nearbyRange} {UnitWord(kilometres)}");
        string? area = StateAndCountry(where);
        if (area is not null) s += ", " + area;
        return s + (coastClose ? ", " + edge : "");
    }

    /// <summary>The "Where you are" box of the Location Info window, one fact per line.</summary>
    internal static string WhereYouAreText(WhereYouAre where, bool kilometres)
    {
        var lines = new List<string>();
        string? edge = EdgeDistanceLine(where, kilometres);
        bool overWater = where.Water != WaterKind.None && !string.IsNullOrEmpty(where.WaterName);
        if (overWater)
        {
            lines.Add("Over " + where.WaterName);
            if (edge is not null) lines.Add(edge);
        }

        string? area = StateAndCountry(where);
        if (area is not null)
        {
            string label = string.IsNullOrEmpty(where.State) ? "Country" : where.StateIsNearest ? "Nearest state" : "State";
            lines.Add(label + ": " + area);
        }
        if (!string.IsNullOrEmpty(where.Region))
            lines.Add("Region: " + where.Region + (string.IsNullOrEmpty(where.RegionKind) ? "" : ", " + where.RegionKind));
        if (!overWater && edge is not null && CoastCloseOverLand(where.EdgeNm!.Value, kilometres))
            lines.Add(edge);
        if (!string.IsNullOrEmpty(where.TimeZone))
            lines.Add("Time zone: " + where.TimeZone);

        return lines.Count == 0 ? "No details for this position." : string.Join("\r\n", lines);
    }

    /// <summary>"Coast: 5.2 miles west" / "Shore: …"; null when there is no edge.</summary>
    private static string? EdgeDistanceLine(WhereYouAre where, bool kilometres)
    {
        if (where.EdgeNm is not double nm) return null;
        string label = where.Water == WaterKind.Lake ? "Shore" : "Coast";
        return label + ": " + EdgeDistance(nm, kilometres) + " " + Direction(where.EdgeBearingTrue);
    }

    private static string? StateAndCountry(WhereYouAre where)
    {
        bool hasState = !string.IsNullOrEmpty(where.State), hasCountry = !string.IsNullOrEmpty(where.Country);
        if (hasState && hasCountry) return where.State + ", " + where.Country;
        if (hasState) return where.State;
        if (hasCountry) return where.Country;
        return null;
    }

    /// <summary>Natural Earth region classes worth saying; continents and broad "geoareas" are
    /// not (every press over the US would otherwise say "North America").</summary>
    internal static bool IsRegionShown(string? featureClass) =>
        !string.IsNullOrWhiteSpace(featureClass)
        && !featureClass.Equals("Continent", StringComparison.OrdinalIgnoreCase)
        && !featureClass.Equals("Geoarea", StringComparison.OrdinalIgnoreCase);

    /// <summary>Spoken words for a Natural Earth region class.</summary>
    internal static string RegionKind(string featureClass) => featureClass.Trim() switch
    {
        "Range/mtn" => "mountain range",
        "Pen/cape" => "peninsula",
        "Island group" => "island group",
        var other => other.ToLowerInvariant(),
    };

    /// <summary>"ALPS" → "Alps"; names already in mixed case are left alone.</summary>
    internal static string TidyName(string name)
    {
        bool hasLetter = false;
        foreach (char c in name)
        {
            if (!char.IsLetter(c)) continue;
            hasLetter = true;
            if (char.IsLower(c)) return name;
        }
        return hasLetter ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()) : name;
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
