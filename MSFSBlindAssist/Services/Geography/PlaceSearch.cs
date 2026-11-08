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

/// <summary>One checkable item at the bottom of the window's tree: its name and the type keys it ticks
/// (Neighborhoods covers "Town:neighborhood" and "Village:neighborhood"). The Major cities item has no type
/// keys: it is saved as <see cref="PlaceFilter.MajorCitiesKey"/>.</summary>
internal sealed record PlaceTreeItem(string Name, IReadOnlyList<string> TypeKeys)
{
    internal bool IsMajorCities => TypeKeys.Count == 0;
}

/// <summary>A heading of the window's tree and its items.</summary>
internal sealed record PlaceTreeGroup(string Name, IReadOnlyList<PlaceTreeItem> Items);

/// <summary>The window's tree, from the database's own list of kinds (pure, unit-tested).</summary>
internal static class PlaceTree
{
    /// <summary>The headings in category order (<see cref="LocationFeatureCatalog.TreeGroup"/>), each kind once
    /// under its heading in the catalog's order, whatever categories hold it; Major cities first under the
    /// populated places, named <paramref name="majorCitiesName"/>.</summary>
    internal static List<PlaceTreeGroup> Build(IEnumerable<PlaceType> types, string majorCitiesName)
    {
        var groups = new List<PlaceTreeGroup>();
        foreach (var group in types.OrderBy(t => t.Category).GroupBy(t => LocationFeatureCatalog.TreeGroup(t.Category)))
        {
            var items = group.GroupBy(t => t.Kind)
                .OrderBy(k => LocationFeatureCatalog.OrderOf(k.Key))
                .Select(k => new PlaceTreeItem(k.First().Name, k.Select(t => t.Key).ToList()))
                .ToList();
            if (group.Any(t => t.Category == LocationCategory.Town))
                items.Insert(0, new PlaceTreeItem(majorCitiesName, Array.Empty<string>()));
            groups.Add(new PlaceTreeGroup(group.Key, items));
        }
        return groups;
    }
}

/// <summary>The Major cities filter: the GeoNames tab's Minimum Population for Major Cities, and its range in
/// nautical miles from the map position (null: no limit, the whole-world search).</summary>
internal sealed record MajorCityFilter(int MinPopulation, double? MaxNm);

/// <summary>The window's filters, applied in memory to the loaded places (pure, unit-tested).</summary>
internal static class PlaceFilter
{
    /// <summary>How the Major cities item is saved: among the hidden type keys when unticked, among Output C's
    /// kinds when picked for C (C then names the nearest major city within the Major Cities Range). No colon,
    /// so <see cref="OfflineLocationService.ParseTypeKey"/> never reads it as a kind.</summary>
    internal const string MajorCitiesKey = "MajorCities";

    /// <summary>The direction filter's choices: the eight compass words <see cref="LocationPhrases.Direction"/>
    /// speaks, in compass order from north. Each covers the 45° that word is said for.</summary>
    internal static IReadOnlyList<string> Directions { get; } =
        Enumerable.Range(0, 8).Select(i => LocationPhrases.Direction(i * 45.0)).ToList();

    /// <summary>A major city, as the Volanta list and the first box count one: a town of at least the minimum
    /// population that is not a neighborhood (the owner's choice, 2026-10-15).</summary>
    internal static bool IsMajorCity(PlaceResult p, int minPopulation) =>
        p.Category == LocationCategory.Town && p.Kind != LocationFeatureCatalog.Neighborhood.Key && p.Population >= minPopulation;

    /// <summary>
    /// The places that pass every filter, nearest to the map position first. <paramref name="hiddenTypes"/>
    /// holds unticked type keys; <paramref name="foldedQuery"/> is matched anywhere in the folded name;
    /// <paramref name="direction"/> is one of <see cref="Directions"/> or null for all;
    /// <paramref name="maxNm"/> limits the distance from the map position (null: no limit, the whole-world
    /// search). With <paramref name="majorCities"/> (Major cities ticked) a major city is kept within its own
    /// range even when its type is unticked or it lies past <paramref name="maxNm"/>.
    /// </summary>
    internal static List<int> Apply(IReadOnlyList<PlaceResult> places, double mapLat, double mapLon,
        IReadOnlySet<string> hiddenTypes, string foldedQuery, string? direction, double? maxNm,
        MajorCityFilter? majorCities = null)
    {
        var kept = new List<(int Index, double Nm)>();
        for (int i = 0; i < places.Count; i++)
        {
            var p = places[i];
            bool typeShown = !hiddenTypes.Contains(p.TypeKey);
            bool major = majorCities is { } m && IsMajorCity(p, m.MinPopulation);
            if (!typeShown && !major) continue;
            if (foldedQuery.Length > 0 && !p.FoldedName.Contains(foldedQuery, StringComparison.Ordinal)) continue;
            double nm = LocationGeometry.GreatCircleNm(mapLat, mapLon, p.Lat, p.Lon);
            bool inRange = typeShown && (maxNm is not { } max || nm <= max);
            bool inMajorRange = major && (majorCities!.MaxNm is not { } majorMax || nm <= majorMax);
            if (!inRange && !inMajorRange) continue;
            // A place at the map position itself has no direction, so a direction filter leaves it out.
            if (direction is not null && (nm < 0.01
                || LocationPhrases.Direction(LocationGeometry.BearingTrue(mapLat, mapLon, p.Lat, p.Lon)) != direction)) continue;
            kept.Add((i, nm));
        }
        kept.Sort((a, b) => a.Nm.CompareTo(b.Nm));
        return kept.Select(k => k.Index).ToList();
    }
}
