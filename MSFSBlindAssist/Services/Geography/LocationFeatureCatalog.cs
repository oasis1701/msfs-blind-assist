namespace MSFSBlindAssist.Services.Geography;

/// <summary>What a stored GeoNames row is, as Location Info groups it. The byte value is the
/// first key of the clustered <c>place</c> table, so never renumber an existing member: a
/// renumbering silently misfiles every row of an already-built database.</summary>
public enum LocationCategory : byte
{
    /// <summary>A populated place of 500+ people, or a seat of administration (PPLA*, PPLC).</summary>
    Town = 1,
    /// <summary>Every other populated place, however small.</summary>
    Village = 2,
    Airport = 3,
    /// <summary>Mountains, hills, valleys, capes, peninsulas and islands.</summary>
    Terrain = 4,
    /// <summary>Lakes, reservoirs, bays, rivers, beaches, gulfs, straits, sounds, coves and reefs.</summary>
    Water = 5,
    /// <summary>Tourist landmarks: monuments, museums, towers, lighthouses, parks, stadiums.</summary>
    Landmark = 6,
    /// <summary>Feature class U (seamounts, trenches, canyons). Stored for later use; not shown yet.</summary>
    Undersea = 7,
}

/// <summary>One of our own kinds of place: what the Location Info window calls it, singular in the table's
/// Type column ("Island") and plural in the tree ("Islands"). <see cref="Key"/> is how it is saved in the
/// settings and stored in the database, so never change one.</summary>
internal sealed record PlaceKind(string Key, string Singular, string Plural);

/// <summary>
/// Which GeoNames rows the offline database keeps, in which <see cref="LocationCategory"/>, and as which of
/// our own <see cref="PlaceKind"/>s. One table on purpose: widening it is a code change plus "Update
/// location data", never a second list elsewhere. GeoNames' own English names ("bank(s)", "israeli
/// settlement") are never shown, and several of its codes can share one kind of ours (ISL and ISLS are both
/// Islands). The codes are GeoNames' own, as its featureCodes_en.txt lists them: a code it does not publish
/// keeps nothing (VLY, STAD, RSVR and PRKS were listed here until 2026-11-06 and matched no row).
/// </summary>
internal static class LocationFeatureCatalog
{
    /// <summary>Population at or above which a populated place is a <see cref="LocationCategory.Town"/>.
    /// Matches the 500 floor the GeoNames tab enforces for the Major Cities threshold, so the
    /// major-city lookup never needs <see cref="LocationCategory.Village"/> rows.</summary>
    internal const int TownPopulation = 500;

    // ---- our kinds, in the order the window's tree lists them (the order they are declared) ----

    private static readonly List<PlaceKind> AllKinds = new();

    private static PlaceKind K(string key, string singular, string plural)
    {
        var kind = new PlaceKind(key, singular, plural);
        AllKinds.Add(kind);
        return kind;
    }

    internal static readonly PlaceKind Capital = K("capital", "Capital", "Capitals");
    internal static readonly PlaceKind RegionalCapital = K("regional-capital", "Regional capital", "Regional capitals");
    internal static readonly PlaceKind Town = K("town", "Town", "Towns");
    internal static readonly PlaceKind Village = K("village", "Village", "Villages");
    /// <summary>GeoNames' sections of a populated place (PPLX): East Village in Manhattan, South Ozone Park at
    /// JFK. They have their own populations and are never a major city (the owner's choice, 2026-10-15).</summary>
    internal static readonly PlaceKind Neighborhood = K("neighborhood", "Neighborhood", "Neighborhoods");

    private static readonly PlaceKind Airport = K("airport", "Airport", "Airports");
    private static readonly PlaceKind Airfield = K("airfield", "Airfield", "Airfields");
    private static readonly PlaceKind Heliport = K("heliport", "Heliport", "Heliports");

    private static readonly PlaceKind Atoll = K("atoll", "Atoll", "Atolls");
    private static readonly PlaceKind Cape = K("cape", "Cape", "Capes");
    private static readonly PlaceKind Hill = K("hill", "Hill", "Hills");
    private static readonly PlaceKind Island = K("island", "Island", "Islands");
    private static readonly PlaceKind Islet = K("islet", "Islet", "Islets");
    private static readonly PlaceKind Mountain = K("mountain", "Mountain", "Mountains");
    private static readonly PlaceKind Peak = K("peak", "Peak", "Peaks");
    private static readonly PlaceKind Peninsula = K("peninsula", "Peninsula", "Peninsulas");
    private static readonly PlaceKind Point = K("point", "Point", "Points");
    private static readonly PlaceKind Valley = K("valley", "Valley", "Valleys");

    private static readonly PlaceKind Bay = K("bay", "Bay", "Bays");
    private static readonly PlaceKind Beach = K("beach", "Beach", "Beaches");
    private static readonly PlaceKind Bight = K("bight", "Bight", "Bights");
    private static readonly PlaceKind Channel = K("channel", "Channel", "Channels");
    private static readonly PlaceKind Cove = K("cove", "Cove", "Coves");
    private static readonly PlaceKind Estuary = K("estuary", "Estuary", "Estuaries");
    private static readonly PlaceKind Fjord = K("fjord", "Fjord", "Fjords");
    private static readonly PlaceKind Gulf = K("gulf", "Gulf", "Gulfs");
    private static readonly PlaceKind Harbor = K("harbor", "Harbor", "Harbors");
    private static readonly PlaceKind Inlet = K("inlet", "Inlet", "Inlets");
    private static readonly PlaceKind Lagoon = K("lagoon", "Lagoon", "Lagoons");
    private static readonly PlaceKind Lake = K("lake", "Lake", "Lakes");
    private static readonly PlaceKind Ocean = K("ocean", "Ocean", "Oceans");
    private static readonly PlaceKind Reef = K("reef", "Reef", "Reefs");
    private static readonly PlaceKind Reservoir = K("reservoir", "Reservoir", "Reservoirs");
    // GeoNames files every river as a stream (STM): the Thames is one.
    private static readonly PlaceKind River = K("river", "River or stream", "Rivers and streams");
    private static readonly PlaceKind Sea = K("sea", "Sea", "Seas");
    private static readonly PlaceKind Shoal = K("shoal", "Shoal or bank", "Shoals and banks");
    private static readonly PlaceKind Sound = K("sound", "Sound", "Sounds");
    private static readonly PlaceKind Strait = K("strait", "Strait", "Straits");

    private static readonly PlaceKind Amphitheater = K("amphitheater", "Amphitheater", "Amphitheaters");
    private static readonly PlaceKind Lighthouse = K("lighthouse", "Lighthouse", "Lighthouses");
    private static readonly PlaceKind Monument = K("monument", "Monument", "Monuments");
    private static readonly PlaceKind Museum = K("museum", "Museum", "Museums");
    private static readonly PlaceKind Park = K("park", "Park", "Parks");
    private static readonly PlaceKind Pier = K("pier", "Pier", "Piers");
    private static readonly PlaceKind Stadium = K("stadium", "Stadium", "Stadiums");
    private static readonly PlaceKind Tower = K("tower", "Tower", "Towers");
    private static readonly PlaceKind Zoo = K("zoo", "Zoo", "Zoos");

    internal static readonly PlaceKind Undersea = K("undersea", "Undersea feature", "Undersea features");

    /// <summary>Every kind of ours, in the tree's order.</summary>
    internal static IReadOnlyList<PlaceKind> Kinds => AllKinds;

    private static readonly Dictionary<string, int> KindOrder =
        AllKinds.Select((k, i) => (k.Key, i)).ToDictionary(t => t.Key, t => t.i, StringComparer.Ordinal);

    /// <summary>A kind's place in the tree's order; an unknown key sorts last.</summary>
    internal static int OrderOf(string kindKey) => KindOrder.GetValueOrDefault(kindKey, int.MaxValue);

    /// <summary>Every code we keep that is not a populated place, with its category and kind.</summary>
    private static readonly Dictionary<string, (LocationCategory Category, PlaceKind Kind)> Codes = new(StringComparer.Ordinal)
    {
        // Airports
        ["AIRP"] = (LocationCategory.Airport, Airport), ["AIRF"] = (LocationCategory.Airport, Airfield),
        ["AIRH"] = (LocationCategory.Airport, Heliport),
        // Terrain, including the land side of the coast
        ["ATOL"] = (LocationCategory.Terrain, Atoll), ["CAPE"] = (LocationCategory.Terrain, Cape),
        ["HLL"] = (LocationCategory.Terrain, Hill), ["HLLS"] = (LocationCategory.Terrain, Hill),
        ["ISL"] = (LocationCategory.Terrain, Island), ["ISLS"] = (LocationCategory.Terrain, Island),
        ["ISLET"] = (LocationCategory.Terrain, Islet),
        ["MT"] = (LocationCategory.Terrain, Mountain), ["MTS"] = (LocationCategory.Terrain, Mountain),
        ["PK"] = (LocationCategory.Terrain, Peak), ["PKS"] = (LocationCategory.Terrain, Peak),
        ["PEN"] = (LocationCategory.Terrain, Peninsula),
        ["PT"] = (LocationCategory.Terrain, Point), ["PTS"] = (LocationCategory.Terrain, Point),
        ["VAL"] = (LocationCategory.Terrain, Valley), ["VALS"] = (LocationCategory.Terrain, Valley),
        // Water bodies (beaches sit here because the online lookups listed them with water)
        ["BAY"] = (LocationCategory.Water, Bay), ["BAYS"] = (LocationCategory.Water, Bay),
        ["BCH"] = (LocationCategory.Water, Beach), ["BCHS"] = (LocationCategory.Water, Beach),
        ["BGHT"] = (LocationCategory.Water, Bight), ["CHN"] = (LocationCategory.Water, Channel),
        ["COVE"] = (LocationCategory.Water, Cove), ["ESTY"] = (LocationCategory.Water, Estuary),
        ["FJD"] = (LocationCategory.Water, Fjord), ["GULF"] = (LocationCategory.Water, Gulf),
        ["HBR"] = (LocationCategory.Water, Harbor), ["INLT"] = (LocationCategory.Water, Inlet),
        ["LGN"] = (LocationCategory.Water, Lagoon), ["LGNS"] = (LocationCategory.Water, Lagoon),
        ["LK"] = (LocationCategory.Water, Lake), ["LKS"] = (LocationCategory.Water, Lake),
        ["OCN"] = (LocationCategory.Water, Ocean), ["RF"] = (LocationCategory.Water, Reef),
        ["RSV"] = (LocationCategory.Water, Reservoir),
        ["STM"] = (LocationCategory.Water, River), ["STMS"] = (LocationCategory.Water, River),
        ["SEA"] = (LocationCategory.Water, Sea),
        ["SHOL"] = (LocationCategory.Water, Shoal), ["BNK"] = (LocationCategory.Water, Shoal),
        ["SD"] = (LocationCategory.Water, Sound), ["STRT"] = (LocationCategory.Water, Strait),
        // Tourist landmarks
        ["AMTH"] = (LocationCategory.Landmark, Amphitheater), ["LTHSE"] = (LocationCategory.Landmark, Lighthouse),
        ["MNMT"] = (LocationCategory.Landmark, Monument), ["MUS"] = (LocationCategory.Landmark, Museum),
        ["PRK"] = (LocationCategory.Landmark, Park), ["PIER"] = (LocationCategory.Landmark, Pier),
        ["STDM"] = (LocationCategory.Landmark, Stadium), ["TOWR"] = (LocationCategory.Landmark, Tower),
        ["ZOO"] = (LocationCategory.Landmark, Zoo),
    };

    private static readonly Dictionary<string, (LocationCategory Category, PlaceKind Kind)>.AlternateLookup<ReadOnlySpan<char>> CodesBySpan =
        Codes.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Every populated code we keep, and the kind it is as a town (null: a plain town or village,
    /// as its population decides). Anything else GeoNames files as populated is dropped: historical,
    /// abandoned and destroyed places (PPLH, PPLQ, PPLW, PPLCH) are never spoken as "near".</summary>
    private static readonly Dictionary<string, PlaceKind?> Populated = new(StringComparer.Ordinal)
    {
        ["PPLC"] = Capital, ["PPLG"] = Capital, ["PPLCD"] = Capital,
        ["PPLA"] = RegionalCapital,
        ["PPL"] = null, ["PPLA2"] = null, ["PPLA3"] = null, ["PPLA4"] = null, ["PPLA5"] = null,
        ["PPLF"] = null, ["PPLL"] = null, ["PPLR"] = null, ["PPLS"] = null,
        // GeoNames' "israeli settlement": a plain town or village like any other (the owner's choice, 2026-11-06).
        ["STLMT"] = null,
        ["PPLX"] = Neighborhood,
    };

    private static readonly Dictionary<string, PlaceKind?>.AlternateLookup<ReadOnlySpan<char>> PopulatedBySpan =
        Populated.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Decides from the feature class and code alone whether a row is kept, without
    /// allocating. A populated place returns <see cref="LocationCategory.Town"/> provisionally:
    /// call <see cref="PopulatedCategory"/> once the population is parsed, then <see cref="KindOf"/>.</summary>
    internal static bool TryClassify(ReadOnlySpan<char> featureClass, ReadOnlySpan<char> featureCode,
        out LocationCategory category, out bool populated)
    {
        populated = false;
        category = default;
        if (featureClass.Length != 1) return false;
        switch (featureClass[0])
        {
            case 'P':
                if (!PopulatedBySpan.ContainsKey(featureCode)) return false;
                populated = true;
                category = LocationCategory.Town;
                return true;
            case 'U':
                category = LocationCategory.Undersea;
                return true;
            default:
                if (!CodesBySpan.TryGetValue(featureCode, out var entry)) return false;
                category = entry.Category;
                return true;
        }
    }

    /// <summary>Town or Village for a populated place.</summary>
    internal static LocationCategory PopulatedCategory(ReadOnlySpan<char> featureCode, long population)
    {
        if (population >= TownPopulation) return LocationCategory.Town;
        if (featureCode.StartsWith("PPLA", StringComparison.Ordinal) || featureCode.SequenceEqual("PPLC"))
            return LocationCategory.Town;
        return LocationCategory.Village;
    }

    /// <summary>Our kind for a kept row, once its final category is known. Among villages every populated
    /// code but a neighborhood is a plain village: capitals and regional capitals are towns.</summary>
    internal static PlaceKind KindOf(LocationCategory category, ReadOnlySpan<char> featureCode)
    {
        switch (category)
        {
            case LocationCategory.Town:
            case LocationCategory.Village:
                PopulatedBySpan.TryGetValue(featureCode, out var kind);
                if (kind == Neighborhood) return Neighborhood;
                return category == LocationCategory.Village ? Village : kind ?? Town;
            case LocationCategory.Undersea:
                return Undersea;
            default:
                return CodesBySpan.TryGetValue(featureCode, out var entry) ? entry.Kind : Undersea;
        }
    }

    /// <summary>Whether the Location Info window lists a category: every one but the undersea class,
    /// which is stored for later and never shown.</summary>
    internal static bool IsShown(LocationCategory category) => category != LocationCategory.Undersea;

    /// <summary>The heading a category's kinds sit under in the Location Info window's tree. Towns and villages
    /// share one, so the tree lists Towns and Villages side by side and Neighborhoods once; no heading names one
    /// of its own items (the owner's choice, 2026-11-07).</summary>
    internal static string TreeGroup(LocationCategory category) => category switch
    {
        LocationCategory.Town or LocationCategory.Village => "Populated places",
        LocationCategory.Airport => "Aviation",
        LocationCategory.Terrain => "Terrain",
        LocationCategory.Water => "Water",
        LocationCategory.Landmark => "Landmarks",
        _ => category.ToString(),
    };
}
