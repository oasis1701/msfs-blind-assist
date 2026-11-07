using System.Globalization;
using MSFSBlindAssist.Models;

namespace MSFSBlindAssist.Services.Geography;

internal enum WaterKind { None, Sea, Lake }

/// <summary>The nearest populated place to a position.</summary>
internal sealed record PlaceHit(string Name, string State, string Country, double DistanceNm, double BearingTrue);

/// <summary>What the Natural Earth shapes and the nearest GeoNames places say about a position.</summary>
internal sealed record WhereYouAre
{
    public WaterKind Water { get; init; }
    public string? WaterName { get; init; }
    /// <summary>At sea: the distance to the nearest land. Over a lake: to its shore (its own outline,
    /// islands included). Over land: to the sea coast. Null when none was found in range.</summary>
    public double? EdgeNm { get; init; }
    public double EdgeBearingTrue { get; init; }
    /// <summary>Over land: the named sea or bay at that coast ("English Channel"), when Natural
    /// Earth names it.</summary>
    public string? CoastWater { get; init; }
    /// <summary>The country's first-level division (state, province, canton, region…) in GeoNames'
    /// spelling, the one the C sentence uses: the area you are IN over land, the area you are
    /// NEAR over water.</summary>
    public string? State { get; init; }
    public string? Country { get; init; }
    public string? Region { get; init; }
    /// <summary>Spoken kind of the region, e.g. "mountain range".</summary>
    public string? RegionKind { get; init; }
    public string? TimeZone { get; init; }
}

/// <summary>
/// Everything Location Info and Output > C say, composed in one place. Over water the water
/// comes first and the distance is to the nearest land at sea ("Over Chesapeake Bay, nearest land
/// 5.2 miles west. Near Dares Beach, …") and to the shore over a lake ("Over Lake Michigan, shore
/// 24 miles east"); over land the sentence is the familiar "Near …" one, with
/// the sea coast added, named when Natural Earth names its water ("English Channel coast 1.2
/// miles south"), whenever the service found one: it searches over land only within the
/// pilot's Coast Range (<see cref="LocationQuerySettings.CoastRange"/>), so these phrases never
/// filter by distance themselves. The area line says "In:" over land and "Near:" over water,
/// never "State:", which fits few countries outside the US. Numbers are invariant: a pilot on a
/// German Windows still hears "5.4 miles".
/// </summary>
internal static class LocationPhrases
{
    internal const double MilesPerNm = 1.1507794480235425;
    internal const double KmPerNm = 1.852;

    private static readonly string[] Directions =
        { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };

    /// <summary>Output C or Location Info pressed with no usable location data installed: a
    /// download or build in progress (never send the pilot back to a button already pressed), a
    /// build waiting for the next start, data from an older version, or none.</summary>
    internal static string Unavailable(bool needsUpdate, DownloadJobPhase job = DownloadJobPhase.Idle, bool readyAtNextStart = false) => job switch
    {
        DownloadJobPhase.Downloading => "Location data is still downloading.",
        DownloadJobPhase.Building => "Location data is still being built.",
        _ when readyAtNextStart => LocationDataStatusText.ReadyAtNextStart,
        _ => needsUpdate
            ? "Location data needs an update. Update it in Settings, GeoNames tab."
            : "Location data not downloaded. Download it in Settings, GeoNames tab.",
    };

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

    /// <summary>A nearest-land, coast or major-city distance: one decimal under 10, whole numbers above ("368 miles").</summary>
    internal static string EdgeDistance(double nm, bool kilometres)
    {
        double v = ToUnit(nm, kilometres);
        return v < 9.95
            ? Inv($"{v:0.0} {UnitWord(kilometres)}")
            : Inv($"{Math.Round(v):0} {UnitWord(kilometres)}");
    }

    /// <summary>"Near Dares Beach, Maryland, United States, 5.4 miles west".</summary>
    internal static string PlaceClause(PlaceHit place, bool kilometres)
    {
        var s = "Near " + place.Name;
        if (!string.IsNullOrEmpty(place.State)) s += ", " + place.State;
        if (!string.IsNullOrEmpty(place.Country)) s += ", " + place.Country;
        return s + ", " + PlaceDistance(place.DistanceNm, kilometres) + " " + Direction(place.BearingTrue);
    }

    /// <summary>For C: "nearest land 5.2 miles west" at sea, "shore 24 miles east" over a lake,
    /// "English Channel coast 1.2 miles south" (or plain "coast …" when the water is unnamed) over
    /// land; null when there is no edge.</summary>
    internal static string? EdgeClause(WhereYouAre where, bool kilometres)
    {
        if (where.EdgeNm is not double nm) return null;
        string what = IsOverWater(where) ? EdgeWord(where).ToLowerInvariant()
            : string.IsNullOrEmpty(where.CoastWater) ? "coast" : where.CoastWater + " coast";
        return what + " " + EdgeDistance(nm, kilometres) + " " + Direction(where.EdgeBearingTrue);
    }

    /// <summary>What the distance is to over water: a lake's "Shore", at sea the "Nearest land"
    /// (owner, 2026-10-10: "coast" over the sea said neither of what nor to what).</summary>
    private static string EdgeWord(WhereYouAre where) => where.Water == WaterKind.Lake ? "Shore" : "Nearest land";

    private static bool IsOverWater(WhereYouAre where) =>
        where.Water != WaterKind.None && !string.IsNullOrEmpty(where.WaterName);

    /// <summary>Output > C.</summary>
    internal static string NearestCitySentence(WhereYouAre where, PlaceHit? place, bool kilometres, int nearbyRange)
    {
        string? edge = EdgeClause(where, kilometres);
        if (IsOverWater(where))
        {
            string lead = "Over " + where.WaterName + (edge is null ? "" : ", " + edge);
            return place is null ? lead : lead + ". " + PlaceClause(place, kilometres);
        }

        // Over land the service found a coast only within the pilot's Coast Range.
        string coast = edge is null ? "" : ", " + edge;
        if (place is not null)
            return PlaceClause(place, kilometres) + coast;

        var s = Inv($"No town within {nearbyRange} {UnitWord(kilometres)}");
        string? area = StateAndCountry(where);
        if (area is not null) s += ", " + area;
        return s + coast;
    }

    /// <summary>
    /// The Volanta traffic list's "Location:", where another aircraft is: the place C would name, without
    /// C's distance, direction or coast (the owner's choice, 2026-10-15). Over land the nearest town with its
    /// state and country ("Pike Lake, Wisconsin, United States"), or the state and country alone with no town
    /// in range; over water the water, with the nearest town when one is in range ("over Lake Michigan, near
    /// Milwaukee, Wisconsin, United States"). Null when nothing is known.
    /// </summary>
    internal static string? TrafficPlace(WhereYouAre where, PlaceHit? place)
    {
        string? town = place is null ? null : JoinNonEmpty(place.Name, place.State, place.Country);
        if (IsOverWater(where)) return "over " + where.WaterName + (town is null ? "" : ", near " + town);
        return town ?? StateAndCountry(where);
    }

    /// <summary>
    /// The Volanta traffic list's "Location:" with "Report location to nearest major city" ticked: how far and
    /// which way the aircraft is from the nearest major city, since that city can be far ("92 miles west of
    /// Wichita, Kansas, United States"; the owner's choice, 2026-10-15), or the city alone within a mile or
    /// kilometre of it. Over water the water first ("over Lake Michigan, 23 miles east of Milwaukee, …"). With
    /// no major city in range, as <see cref="TrafficPlace"/> with no town. <paramref name="city"/>'s distance
    /// and bearing are from the aircraft to the city, as in every <see cref="PlaceHit"/>: the direction said is
    /// the reverse, from the city.
    /// </summary>
    internal static string? TrafficCityPlace(WhereYouAre where, PlaceHit? city, bool kilometres)
    {
        string? named = city is null ? null : JoinNonEmpty(city.Name, city.State, city.Country);
        bool atCity = city is not null && ToUnit(city.DistanceNm, kilometres) < 1.0;
        string? fromCity = named is null || atCity ? named
            : EdgeDistance(city!.DistanceNm, kilometres) + " " + Direction(city.BearingTrue + 180.0) + " of " + named;
        if (IsOverWater(where))
            return "over " + where.WaterName + (fromCity is null ? "" : ", " + (atCity ? "near " : "") + fromCity);
        return fromCity ?? StateAndCountry(where);
    }

    private static string? JoinNonEmpty(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrEmpty(p)).ToList();
        return kept.Count == 0 ? null : string.Join(", ", kept);
    }

    /// <summary>The "Where you are" box of the Location Info window, one fact per line. Over water:
    /// "Over …", "Nearest land: …" (at sea) or "Shore: …" (over a lake), "Near: Maryland, United
    /// States". Over land: "In: Bavaria, Germany", the region, "Coast: English Channel, 1.2 miles
    /// south" when the service found a coast within the Coast Range. Then the time zone.</summary>
    internal static string WhereYouAreText(WhereYouAre where, bool kilometres)
    {
        var lines = new List<string>();
        bool overWater = IsOverWater(where);
        string? distance = where.EdgeNm is double nm
            ? EdgeDistance(nm, kilometres) + " " + Direction(where.EdgeBearingTrue)
            : null;
        string? area = StateAndCountry(where);

        if (overWater)
        {
            lines.Add("Over " + where.WaterName);
            if (distance is not null) lines.Add(EdgeWord(where) + ": " + distance);
            if (area is not null) lines.Add("Near: " + area);
        }
        else if (area is not null)
        {
            lines.Add("In: " + area);
        }
        if (!string.IsNullOrEmpty(where.Region))
            lines.Add("Region: " + where.Region + (string.IsNullOrEmpty(where.RegionKind) ? "" : ", " + where.RegionKind));
        if (!overWater && distance is not null)
            lines.Add("Coast: " + (string.IsNullOrEmpty(where.CoastWater) ? "" : where.CoastWater + ", ") + distance);
        if (!string.IsNullOrEmpty(where.TimeZone))
            lines.Add("Time zone: " + where.TimeZone);

        return lines.Count == 0 ? "No details for this position." : string.Join("\r\n", lines);
    }

    /// <summary>
    /// The Location Info window's first box: the aircraft's "Where you are" lines; when the map position does
    /// not follow the aircraft, a line saying where it is. The major cities are not listed here: they are the
    /// tree's Major cities item, in the table (the owner's choice, 2026-11-07: never in two places).
    /// <paramref name="mapName"/> is null while it follows the aircraft, empty for the spot where the aircraft
    /// was when following was turned off, else the place picked. The line reads "Map position: Krokholmen,
    /// 23 miles northwest of the aircraft" or "Map position: Krokholmen, where the aircraft is"; for the spot,
    /// "Map position: where the aircraft was, now 2 miles south of it" or "Map position: the aircraft's
    /// position, not following it". (Before 2026-11-06 the spot was named "Where the aircraft was" and read
    /// "Where the aircraft was, at the aircraft".)
    /// </summary>
    internal static string LocationInfoText(WhereYouAre aircraft, string? mapName, double mapNmFromAircraft,
        double mapBearingFromAircraft, bool kilometres)
    {
        var text = WhereYouAreText(aircraft, kilometres);
        if (mapName is not null)
        {
            bool atAircraft = ToUnit(mapNmFromAircraft, kilometres) < 0.05;
            string away = DistanceAndDirection(mapNmFromAircraft, mapBearingFromAircraft, kilometres);
            string line = mapName.Length == 0
                ? atAircraft ? "the aircraft's position, not following it" : "where the aircraft was, now " + away + " of it"
                : mapName + ", " + (atAircraft ? "where the aircraft is" : away + " of the aircraft");
            text += "\r\nMap position: " + line;
        }
        return text;
    }

    /// <summary>"23 miles northwest": a result's distance and direction, as the window's columns show it.</summary>
    internal static string DistanceAndDirection(double nm, double bearingTrue, bool kilometres) =>
        ToUnit(nm, kilometres) < 0.05 ? "here" : EdgeDistance(nm, kilometres) + " " + Direction(bearingTrue);

    /// <summary>"57.93333, 11.66667": decimal degrees, invariant, as the window shows and copies them.</summary>
    internal static string Coordinates(double lat, double lon) => Inv($"{lat:0.00000}, {lon:0.00000}");

    /// <summary>A result's region column: "Västra Götaland, Sweden".</summary>
    internal static string Region(string state, string country) => JoinNonEmpty(state, country) ?? "";

    /// <summary>The window's Details column: a town's population; an airport's ident and whether it was found
    /// in the simulator's navigation database (a likely match, not a certain one); nothing for other places.</summary>
    internal static string PlaceDetails(LocationCategory category, int population, string? simulatorIdent)
    {
        if (category == LocationCategory.Airport)
            return simulatorIdent is null ? "Not found in simulator" : simulatorIdent + ", in simulator";
        return population > 0 ? Inv($"Population {population:N0}") : "";
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
