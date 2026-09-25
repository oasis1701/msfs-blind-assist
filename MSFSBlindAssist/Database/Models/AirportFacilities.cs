using System.Globalization;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Database.Models;

public readonly record struct ComFrequency(string Type, int FrequencyHz, string Name);

/// <summary>
/// The airport-level navdata columns only the surroundings feature reads: fuel flags, helipads,
/// COM frequencies, bounding box, reference point, scenery_local_path, and the tower position
/// (NULL on an MSFS 2020 build; present for 1,952 of 84,278 fs2024 airports).
/// </summary>
public sealed class AirportFacilities
{
    public string Icao { get; init; } = "";
    public bool HasAvgas { get; init; }
    public bool HasJetFuel { get; init; }
    public List<LatLon> Helipads { get; } = new();
    public List<ComFrequency> Coms { get; } = new();
    public double LeftLon { get; init; }
    public double RightLon { get; init; }
    public double TopLat { get; init; }
    public double BottomLat { get; init; }
    public double? TowerLat { get; init; }
    public double? TowerLon { get; init; }
    /// <summary>
    /// The scenery really places a control tower; only then is the tower position a building. 319 of
    /// the 1,952 fs2024 tower positions are the tower-VIEW camera point (KAST's is 319 ft above the
    /// field), with this 0 and no tower frequency. NULL reads as false.
    /// </summary>
    public bool HasTowerObject { get; init; }
    /// <summary>The airport reference point (navdata's own). The online feature query is bounded by the
    /// box above, not centred here.</summary>
    public double RefLat { get; init; }
    public double RefLon { get; init; }
    /// <summary>navdatareader's comma-separated package list, e.g. "fs-base-genericairports, C:\...\Community\orbx-airport-ktiw-tacoma-narrows".</summary>
    public string SceneryLocalPath { get; init; } = "";

    /// <summary>
    /// The navdata box is the exact hull of the airport's own records (KTIW's tower sits 15 m outside
    /// it), so a building test passes a margin, converted by <see cref="GrownBox"/>.
    /// </summary>
    public bool ContainsPoint(double lat, double lon, double marginMetres = 0) => Grown(marginMetres).Contains(lat, lon);

    /// <summary>This box grown by <paramref name="marginMetres"/>: build it once when testing many points.</summary>
    public GrownBox Grown(double marginMetres) => GrownBox.Of(TopLat, BottomLat, LeftLon, RightLon, marginMetres);

    /// <summary>A row naming a ramp position rather than the controller (KMIA's first G row is
    /// "MIAMI GATES").</summary>
    private static readonly System.Text.RegularExpressions.Regex NotThePrimaryFrequency = new(
        @"\b(apron|ramp|gates?|delivery|clearance)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>"Fuel available. Tower 118.5, Ground 121.8, ATIS 124.05, UNICOM 122.95." or "".
    /// <para>Both fuel flags together say only "fuel": on fs2024 they are all-or-nothing (not one
    /// airport carries a single flag), so "Avgas and jet fuel" claimed jet fuel at 1,147 grass strips.
    /// An MSFS 2020 build sets them independently, so one flag alone still names its grade.</para></summary>
    public string DescribeFacts()
    {
        var parts = new List<string>();
        string fuel = (HasAvgas, HasJetFuel) switch
        {
            (true, true) => "Fuel available",
            (true, false) => "Avgas",
            (false, true) => "Jet fuel",
            _ => "",
        };
        if (fuel.Length > 0) parts.Add(fuel + ".");

        var freqs = new List<string>();
        foreach (var (type, label) in new[] { ("T", "Tower"), ("G", "Ground"), ("ATIS", "ATIS"), ("CTAF", "CTAF"), ("UC", "UNICOM"), ("AWOS", "AWOS"), ("ASOS", "ASOS") })
        {
            // VHF COM band only (navdata also lists VOR-broadcast ATIS); several rows of one type are
            // normal at a hub (KATL: 7 towers), so say how many.
            var rows = Coms.Where(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase)
                                       && c.FrequencyHz >= 118_000_000 && c.FrequencyHz < 137_000_000).ToList();
            if (rows.Count == 0) continue;
            var plain = rows.Where(c => !NotThePrimaryFrequency.IsMatch(c.Name ?? "")).ToList();
            var pick = plain.Count > 0 ? plain[0] : rows[0];
            freqs.Add(rows.Count > 1 ? $"{label} {FormatMhz(pick.FrequencyHz)} ({rows.Count} listed)"
                                     : $"{label} {FormatMhz(pick.FrequencyHz)}");
        }
        if (freqs.Count > 0) parts.Add(string.Join(", ", freqs) + ".");
        return string.Join(" ", parts);
    }

    /// <summary>118500000 → "118.5"; 124050000 → "124.05"; 122950000 → "122.95" (trailing zeros trimmed, at least one decimal).</summary>
    public static string FormatMhz(int hz)
    {
        double mhz = hz / 1_000_000.0;
        string s = mhz.ToString("0.000", CultureInfo.InvariantCulture).TrimEnd('0');
        if (s.EndsWith('.')) s += "0";
        return s;
    }
}
