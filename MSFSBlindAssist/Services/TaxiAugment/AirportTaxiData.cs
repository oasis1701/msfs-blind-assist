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
    public override string ToString() =>
        $"{Icao} navNamed={NavNamedTaxiways} navUnnamed={NavUnnamedSegments} " +
        $"+osm={NamesAdoptedFromOsm} +aptdat={NamesAdoptedFromAptDat} " +
        $"disagree={OsmAptDatDisagreements} parkFill={ParkingFilled}";
}

public sealed class MergeOptions
{
    public double MatchMaxMidpointMeters { get; init; } = 30.0;
    public double MatchMaxBearingDeg { get; init; } = 25.0;
}
