namespace MSFSBlindAssist.Services.TaxiAugment;

public sealed class NamedTaxiSegment
{
    public string Name { get; init; } = "";
    public double Lat1 { get; init; }
    public double Lon1 { get; init; }
    public double Lat2 { get; init; }
    public double Lon2 { get; init; }
}

public sealed class AirportTaxiData
{
    public string Source { get; init; } = "";
    public List<NamedTaxiSegment> Taxiways { get; } = new();
    public List<(string Name, double Lat, double Lon)> Parking { get; } = new();

    /// <summary>
    /// NAMED holding points (OSM <c>aeroway=holding_position</c> nodes that carry a
    /// ref/name — VIKAS, N2E, A11…). Kind is the OSM <c>holding_position:type</c> tag
    /// ("runway", "ILS", "intermediate") or "" when untagged. Unnamed painted hold
    /// lines are never collected. Consumed alias-style by
    /// <c>Navigation.NamedHoldingPointResolver</c>: the name attaches to the nearest
    /// navdata graph node; the online coordinate itself is NEVER routed to (anti-grass
    /// rule — we only steer on navdata geometry).
    /// </summary>
    public List<(string Name, double Lat, double Lon, string Kind)> HoldingPoints { get; } = new();
}

public sealed class CoverageReport
{
    public string Icao { get; init; } = "";
    public int NavNamedTaxiways { get; init; }
    public int NavUnnamedSegments { get; init; }
    public int NamesAdoptedFromOsm { get; init; }
    public int NamesAdoptedFromAptDat { get; init; }
    public int OsmAptDatDisagreements { get; init; }
    public int ParkingFilled { get; init; }
    /// <summary>
    /// Number of alias entries added to already-named navdata segments whose
    /// normalized online name differs from the segment's canonical navdata name.
    /// </summary>
    public int AliasesAdded { get; init; }
    public override string ToString() =>
        $"{Icao} navNamed={NavNamedTaxiways} navUnnamed={NavUnnamedSegments} " +
        $"+osm={NamesAdoptedFromOsm} +aptdat={NamesAdoptedFromAptDat} " +
        $"aliases={AliasesAdded} disagree={OsmAptDatDisagreements} parkFill={ParkingFilled}";
}

public sealed class MergeOptions
{
    public double MatchMaxMidpointMeters { get; init; } = 30.0;
    public double MatchMaxBearingDeg { get; init; } = 25.0;

    /// <summary>
    /// Ambiguity guard: if a second online segment with a DIFFERENT (normalized) name sits within
    /// this factor × the best match's distance, the match is treated as ambiguous and NO name is
    /// adopted. Protects against mis-naming where two parallel taxiways are both within tolerance.
    /// </summary>
    public double MatchAmbiguityFactor { get; init; } = 1.5;

    /// <summary>
    /// Additive companion to <see cref="MatchAmbiguityFactor"/> (metres). Ensures the ambiguity
    /// guard still fires when the best match is essentially ON the segment (bestDist ≈ 0), where a
    /// purely multiplicative threshold (bestDist × factor) would collapse to ~0 and never trip.
    /// </summary>
    public double MatchAmbiguityEpsilonMeters { get; init; } = 2.0;
}
