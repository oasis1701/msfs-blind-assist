namespace MSFSBlindAssist.Services.Geography;

/// <summary>A kind of place the Location Info window filters on: a category and one of our kinds in it
/// (<see cref="PlaceKind"/>), with the kind's plural as the tree shows it ("Terrain", "island", "Islands").
/// Neighborhoods appear under both Town and Village.</summary>
internal sealed record PlaceType(LocationCategory Category, string Kind, string Name)
{
    /// <summary>"Terrain:island": how a type is saved in the settings.</summary>
    internal string Key => KeyOf(Category, Kind);

    internal static string KeyOf(LocationCategory category, string kind) => category + ":" + kind;
}

/// <summary>One place found by a Location Info search, before any distance is worked out: the window
/// measures it from both the map position and the aircraft. <see cref="TypeName"/> is our kind's singular
/// ("Island"), as the table's Type column shows it.</summary>
internal sealed record PlaceResult(
    int Id, string Name, double Lat, double Lon, LocationCategory Category, string Kind, string TypeName,
    int Population, string State, string Country)
{
    internal string TypeKey => PlaceType.KeyOf(Category, Kind);

    /// <summary>Folded once (<see cref="SearchText.Fold"/>) when the results are loaded, so typing in the
    /// search box filters without folding every name again.</summary>
    internal string FoldedName { get; init; } = "";

    /// <summary>For an airport: its alternate names from GeoNames (codes among them), for matching it to
    /// the navigation database. Empty for every other place.</summary>
    internal string AlternateNames { get; init; } = "";
}

/// <summary>What a whole-world name search found: the nearest <see cref="Places"/> to the search point
/// and how many matched in all.</summary>
internal sealed record WorldSearchOutcome(IReadOnlyList<PlaceResult> Places, int TotalMatches);

/// <summary>The window's filters, applied in memory to the loaded places (pure, unit-tested).</summary>
internal static class PlaceFilter
{
    /// <summary>The direction filter's choices: the eight compass words <see cref="LocationPhrases.Direction"/>
    /// speaks, in compass order from north. Each covers the 45° that word is said for.</summary>
    internal static IReadOnlyList<string> Directions { get; } =
        Enumerable.Range(0, 8).Select(i => LocationPhrases.Direction(i * 45.0)).ToList();

    /// <summary>
    /// The places that pass every filter, nearest to the map position first. <paramref name="hiddenTypes"/>
    /// holds unticked type keys; <paramref name="foldedQuery"/> is matched anywhere in the folded name;
    /// <paramref name="direction"/> is one of <see cref="Directions"/> or null for all;
    /// <paramref name="maxNm"/> limits the distance from the map position (null: no limit, the whole-world
    /// search).
    /// </summary>
    internal static List<int> Apply(IReadOnlyList<PlaceResult> places, double mapLat, double mapLon,
        IReadOnlySet<string> hiddenTypes, string foldedQuery, string? direction, double? maxNm)
    {
        var kept = new List<(int Index, double Nm)>();
        for (int i = 0; i < places.Count; i++)
        {
            var p = places[i];
            if (hiddenTypes.Contains(p.TypeKey)) continue;
            if (foldedQuery.Length > 0 && !p.FoldedName.Contains(foldedQuery, StringComparison.Ordinal)) continue;
            double nm = LocationGeometry.GreatCircleNm(mapLat, mapLon, p.Lat, p.Lon);
            if (maxNm is { } max && nm > max) continue;
            // A place at the map position itself has no direction, so a direction filter leaves it out.
            if (direction is not null && (nm < 0.01
                || LocationPhrases.Direction(LocationGeometry.BearingTrue(mapLat, mapLon, p.Lat, p.Lon)) != direction)) continue;
            kept.Add((i, nm));
        }
        kept.Sort((a, b) => a.Nm.CompareTo(b.Nm));
        return kept.Select(k => k.Index).ToList();
    }
}
