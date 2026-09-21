namespace MSFSBlindAssist.Navigation.Surroundings;

public enum FeatureKind { Terminal, Concourse, Fbo, Hangar, Tower, Fuel, Cargo, FireStation, Helipad, Apron, DeicePad, Office, Other }

/// <summary>Where a feature came from. NOT the merge tie-break order — that is
/// AirportFeatureCatalog.Rank, which also weighs whether the name is real or synthesized.</summary>
public enum FeatureSource { Navdata, Gsx, Osm, Scenery }

public readonly record struct LatLon(double Lat, double Lon);

/// <summary>
/// One thing on the airport a pilot might want to know is beside them. READOUT ONLY: never a
/// graph node, never a routing input, never a hold-short input (spec invariant). Name is what
/// is SPOKEN, verbatim — a source must hand over human text, never a raw model or tag string.
/// </summary>
public sealed class AirportFeature
{
    public required FeatureKind Kind { get; init; }
    public string Name { get; init; } = "";
    public required double Lat { get; init; }
    public required double Lon { get; init; }
    /// <summary>Closed polygon (OSM apron/terminal way) or null for a point feature.</summary>
    public IReadOnlyList<LatLon>? Footprint { get; init; }
    public required FeatureSource Source { get; init; }
    /// <summary>Short qualifier spoken after the name in the window: "Delta gates", "operator Jackson Hole Aviation".</summary>
    public string? Detail { get; init; }
    /// <summary>Stand/placement positions of a stand- or placement-derived feature (e.g. a concourse
    /// synthesized from its gates). When set, SurroundingsGeometry.Nearest measures to the nearest
    /// member instead of the centroid in Lat/Lon.</summary>
    public IReadOnlyList<LatLon>? Members { get; init; }
    /// <summary>True when Name is a synthesized label ("Fuel", "GA ramp", "Helipad 2"), not a proper
    /// name a source actually gave this feature.</summary>
    public bool NameIsGeneric { get; init; }

    public bool HasName => !string.IsNullOrWhiteSpace(Name);
    public string SpokenName => HasName ? Name.Trim() : FeatureKindWords.Generic(Kind);
    /// <summary>HasName and NOT a synthesized label — a real name a source gave this feature.</summary>
    public bool HasProperName => HasName && !NameIsGeneric;
}

public static class FeatureKindWords
{
    /// <summary>What an UNNAMED feature of this kind is called. Other → "" (an unnamed Other is dropped upstream).</summary>
    public static string Generic(FeatureKind kind) => kind switch
    {
        FeatureKind.Terminal => "Terminal",
        FeatureKind.Concourse => "Concourse",
        FeatureKind.Fbo => "FBO",
        FeatureKind.Hangar => "Hangar",
        FeatureKind.Tower => "Control tower",
        FeatureKind.Fuel => "Fuel",
        FeatureKind.Cargo => "Cargo ramp",
        FeatureKind.FireStation => "Fire station",
        FeatureKind.Helipad => "Helipad",
        FeatureKind.Apron => "Apron",
        FeatureKind.DeicePad => "De-ice pad",
        FeatureKind.Office => "Airport office",
        _ => "",
    };
}
