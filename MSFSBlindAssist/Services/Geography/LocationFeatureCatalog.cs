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
    /// <summary>Lakes, reservoirs, bays, streams, beaches, gulfs, straits, sounds, coves and reefs.</summary>
    Water = 5,
    /// <summary>Tourist landmarks: monuments, museums, towers, lighthouses, parks, stadiums.</summary>
    Landmark = 6,
    /// <summary>Feature class U (seamounts, trenches, canyons). Stored for later use; not shown yet.</summary>
    Undersea = 7,
}

/// <summary>
/// Which GeoNames rows the offline database keeps, and in which <see cref="LocationCategory"/>.
/// One table on purpose: widening it is a code change plus "Update location data", never a
/// second list elsewhere. Covers every feature code the retired online lookups asked for,
/// plus sea and coast names and the undersea class.
/// </summary>
internal static class LocationFeatureCatalog
{
    /// <summary>Population at or above which a populated place is a <see cref="LocationCategory.Town"/>.
    /// Matches the 500 floor the GeoNames tab enforces for the Major Cities threshold, so the
    /// major-city lookup never needs <see cref="LocationCategory.Village"/> rows.</summary>
    internal const int TownPopulation = 500;

    private static readonly Dictionary<string, LocationCategory> Codes = new(StringComparer.Ordinal)
    {
        // Airports
        ["AIRP"] = LocationCategory.Airport, ["AIRF"] = LocationCategory.Airport, ["AIRH"] = LocationCategory.Airport,
        // Terrain, including the land side of the coast
        ["MT"] = LocationCategory.Terrain, ["MTS"] = LocationCategory.Terrain,
        ["PK"] = LocationCategory.Terrain, ["PKS"] = LocationCategory.Terrain,
        ["HLL"] = LocationCategory.Terrain, ["HLLS"] = LocationCategory.Terrain,
        ["VLY"] = LocationCategory.Terrain,
        ["CAPE"] = LocationCategory.Terrain, ["PT"] = LocationCategory.Terrain, ["PTS"] = LocationCategory.Terrain,
        ["PEN"] = LocationCategory.Terrain,
        ["ISL"] = LocationCategory.Terrain, ["ISLS"] = LocationCategory.Terrain, ["ISLET"] = LocationCategory.Terrain,
        ["ATOL"] = LocationCategory.Terrain,
        // Water bodies (BCH/BCHS sit here because the online lookups listed beaches with water)
        ["LK"] = LocationCategory.Water, ["LKS"] = LocationCategory.Water,
        ["RSV"] = LocationCategory.Water, ["RSVR"] = LocationCategory.Water,
        ["BAY"] = LocationCategory.Water, ["BAYS"] = LocationCategory.Water,
        ["STM"] = LocationCategory.Water, ["STMS"] = LocationCategory.Water,
        ["BCH"] = LocationCategory.Water, ["BCHS"] = LocationCategory.Water,
        ["OCN"] = LocationCategory.Water, ["SEA"] = LocationCategory.Water, ["GULF"] = LocationCategory.Water,
        ["SD"] = LocationCategory.Water, ["STRT"] = LocationCategory.Water, ["CHN"] = LocationCategory.Water,
        ["COVE"] = LocationCategory.Water, ["INLT"] = LocationCategory.Water, ["FJD"] = LocationCategory.Water,
        ["ESTY"] = LocationCategory.Water, ["LGN"] = LocationCategory.Water, ["LGNS"] = LocationCategory.Water,
        ["BGHT"] = LocationCategory.Water, ["HBR"] = LocationCategory.Water,
        ["RF"] = LocationCategory.Water, ["SHOL"] = LocationCategory.Water, ["BNK"] = LocationCategory.Water,
        // Tourist landmarks
        ["MNMT"] = LocationCategory.Landmark, ["MUS"] = LocationCategory.Landmark, ["TOWR"] = LocationCategory.Landmark,
        ["LTHSE"] = LocationCategory.Landmark, ["PRK"] = LocationCategory.Landmark, ["PRKS"] = LocationCategory.Landmark,
        ["STAD"] = LocationCategory.Landmark, ["AMTH"] = LocationCategory.Landmark, ["ZOO"] = LocationCategory.Landmark,
        ["PIER"] = LocationCategory.Landmark,
    };

    private static readonly Dictionary<string, LocationCategory>.AlternateLookup<ReadOnlySpan<char>> CodesBySpan =
        Codes.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Historical, abandoned and destroyed places: never spoken as "near".</summary>
    private static readonly HashSet<string> ExcludedPopulated = new(StringComparer.Ordinal) { "PPLH", "PPLQ", "PPLW", "PPLCH" };

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> ExcludedPopulatedBySpan =
        ExcludedPopulated.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Decides from the feature class and code alone whether a row is kept, without
    /// allocating. A populated place returns <see cref="LocationCategory.Town"/> provisionally:
    /// call <see cref="PopulatedCategory"/> once the population is parsed.</summary>
    internal static bool TryClassify(ReadOnlySpan<char> featureClass, ReadOnlySpan<char> featureCode,
        out LocationCategory category, out bool populated)
    {
        populated = false;
        category = default;
        if (featureClass.Length != 1) return false;
        switch (featureClass[0])
        {
            case 'P':
                if (featureCode.IsEmpty || ExcludedPopulatedBySpan.Contains(featureCode)) return false;
                populated = true;
                category = LocationCategory.Town;
                return true;
            case 'U':
                category = LocationCategory.Undersea;
                return true;
            default:
                return CodesBySpan.TryGetValue(featureCode, out category);
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
}
