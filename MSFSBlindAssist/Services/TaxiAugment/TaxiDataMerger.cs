using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// A minimal, probe-friendly DTO representing one navdata taxi segment.
/// The main project maps TaxiPath → NavSegment → back to TaxiPath.
/// </summary>
public sealed class NavSegment
{
    public string Name { get; set; } = "";
    public double StartLat { get; set; }
    public double StartLon { get; set; }
    public double EndLat { get; set; }
    public double EndLon { get; set; }

    public NavSegment() { }

    public NavSegment(string name, double startLat, double startLon, double endLat, double endLon)
    {
        Name = name;
        StartLat = startLat;
        StartLon = startLon;
        EndLat = endLat;
        EndLon = endLon;
    }
}

/// <summary>
/// Pure geometric name-overlay: attaches real-world taxiway names from online sources
/// onto navdata segments. Navdata is AUTHORITATIVE — existing names are never overwritten.
/// Online-only taxiways with no matching navdata geometry are IGNORED (safety rule:
/// we only steer on navdata pavement, never invent geometry).
/// </summary>
public static class TaxiDataMerger
{
    /// <summary>
    /// Returns a NEW list of NavSegments where unnamed navdata segments adopt an online name
    /// when geometry matches within <paramref name="opt"/> tolerances. Navdata names are NEVER
    /// overwritten. Online-only taxiways with no matching navdata geometry are ignored.
    /// Cross-checks OSM vs apt.dat and counts disagreements (apt.dat wins on conflict).
    /// </summary>
    public static List<NavSegment> MergeNamesOntoNavData(
        IReadOnlyList<NavSegment> nav,
        IReadOnlyList<AirportTaxiData> sources,
        MergeOptions opt,
        string icao,
        out CoverageReport coverage)
    {
        int navNamed = nav.Count(p => !string.IsNullOrWhiteSpace(p.Name));
        int adoptOsm = 0, adoptApt = 0, disagree = 0, unnamed = 0;

        var osm    = sources.FirstOrDefault(s => s.Source == "osm");
        var aptdat = sources.FirstOrDefault(s => s.Source == "aptdat");

        var result = new List<NavSegment>(nav.Count);
        foreach (var p in nav)
        {
            // NEVER overwrite a navdata name — navdata is authoritative.
            if (!string.IsNullOrWhiteSpace(p.Name))
            {
                result.Add(p);
                continue;
            }

            unnamed++;

            // Midpoint of this navdata segment.
            double mLat = (p.StartLat + p.EndLat) / 2.0;
            double mLon = (p.StartLon + p.EndLon) / 2.0;
            double navBrg = TaxiGeo.BearingDeg(p.StartLat, p.StartLon, p.EndLat, p.EndLon);

            string? osmName = BestMatchName(osm, mLat, mLon, navBrg, opt);
            string? aptName = BestMatchName(aptdat, mLat, mLon, navBrg, opt);

            string? chosen = null;
            if (osmName != null && aptName != null)
            {
                if (string.Equals(osmName, aptName, StringComparison.OrdinalIgnoreCase))
                {
                    // Both sources agree — adopt; count as OSM.
                    chosen = osmName;
                    adoptOsm++;
                }
                else
                {
                    // Disagreement — prefer apt.dat's structured AIP name.
                    disagree++;
                    chosen = aptName;
                    adoptApt++;
                }
            }
            else if (aptName != null)
            {
                chosen = aptName;
                adoptApt++;
            }
            else if (osmName != null)
            {
                chosen = osmName;
                adoptOsm++;
            }

            if (chosen == null)
            {
                result.Add(p);
            }
            else
            {
                // Shallow copy with the adopted name.
                result.Add(new NavSegment(chosen, p.StartLat, p.StartLon, p.EndLat, p.EndLon));
            }
        }

        coverage = new CoverageReport
        {
            Icao                  = icao,
            NavNamedTaxiways      = navNamed,
            NavUnnamedSegments    = unnamed,
            NamesAdoptedFromOsm   = adoptOsm,
            NamesAdoptedFromAptDat = adoptApt,
            OsmAptDatDisagreements = disagree,
        };

        return result;
    }

    /// <summary>
    /// Fills parking spots ONLY when the navdata has ZERO parking (navdata parking wins
    /// when present). Projects online source parking into a list of (Name, Lat, Lon) tuples.
    /// Returns null (no fill) when navdata already has parking.
    /// </summary>
    public static List<(string Name, double Lat, double Lon)>? MergeParking(
        int navParkingCount,
        IReadOnlyList<AirportTaxiData> sources,
        out int parkingFilled)
    {
        parkingFilled = 0;

        // Navdata parking is authoritative — only fill when there is none.
        if (navParkingCount > 0)
            return null;

        // Collect from all sources; deduplicate by name+position.
        var filled = new List<(string Name, double Lat, double Lon)>();
        foreach (var src in sources)
        {
            foreach (var (name, lat, lon) in src.Parking)
            {
                filled.Add((name, lat, lon));
            }
        }

        parkingFilled = filled.Count;
        return filled.Count > 0 ? filled : null;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Internal helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the best-matching named online segment for a navdata midpoint + bearing.
    /// Returns the name of the closest-matching segment, or null if nothing is within
    /// the tolerance gates.
    /// </summary>
    private static string? BestMatchName(
        AirportTaxiData? source,
        double mLat, double mLon,
        double navBrg,
        MergeOptions opt)
    {
        if (source == null) return null;

        string? bestName = null;
        double bestDist = double.MaxValue;

        foreach (var seg in source.Taxiways)
        {
            // Bearing gate (undirected — taxiway lines have no preferred direction).
            double segBrg = TaxiGeo.BearingDeg(seg.Lat1, seg.Lon1, seg.Lat2, seg.Lon2);
            if (TaxiGeo.BearingDiffMod180(navBrg, segBrg) > opt.MatchMaxBearingDeg)
                continue;

            // Perpendicular distance from navdata midpoint to this online segment.
            double dist = TaxiGeo.PointToSegmentMeters(mLat, mLon, seg.Lat1, seg.Lon1, seg.Lat2, seg.Lon2);
            if (dist < opt.MatchMaxMidpointMeters && dist < bestDist)
            {
                bestDist = dist;
                bestName = seg.Name;
            }
        }

        return bestName;
    }
}
