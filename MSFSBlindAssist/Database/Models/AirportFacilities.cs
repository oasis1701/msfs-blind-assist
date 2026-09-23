using System.Globalization;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Database.Models;

public readonly record struct ComFrequency(string Type, int FrequencyHz, string Name);

/// <summary>
/// The airport-level navdata columns the surroundings feature reads and nothing else did:
/// fuel flags, helipads, COM frequencies, the airport bounding box (the OSM radius-fallback
/// filter), the reference point and scenery_local_path (which packages the scenery scan may
/// open). airport.tower_lonx/laty is NULL on an MSFS 2020 build, but present for ~1,950
/// airports on an MSFS 2024 build (1,952 of 84,278, measured 2026-09-21) — that is why
/// TowerLat/TowerLon are nullable rather than always absent. A position is not a tower, though:
/// see <see cref="HasTowerObject"/>.
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
    /// airport.has_tower_object — the scenery really places a control tower. Only then is
    /// <see cref="TowerLat"/>/<see cref="TowerLon"/> a building: 319 of the 1,952 fs2024 airports
    /// with a tower position carry 0 here and no tower frequency, because that position is the
    /// tower-VIEW camera point (KAST's is 319 ft above a 7 ft field). The other 1,633 all have a
    /// tower frequency (measured 2026-09-22). NULL reads as false.
    /// </summary>
    public bool HasTowerObject { get; init; }
    /// <summary>The airport reference point — where the online feature query is centred.</summary>
    public double RefLat { get; init; }
    public double RefLon { get; init; }
    /// <summary>navdatareader's comma-separated package list, e.g. "fs-base-genericairports, C:\...\Community\orbx-airport-ktiw-tacoma-narrows".</summary>
    public string SceneryLocalPath { get; init; } = "";

    /// <summary>
    /// The navdata box is the exact hull of the airport's OWN records (taxi paths, runway ends,
    /// stands): at KTIW its east edge IS the outermost taxi path, so the control tower sits 15 m
    /// outside it. Buildings stand beside the pavement, not on it — callers testing a building
    /// pass a margin, converted by <see cref="GrownBox"/>, the one conversion the scenery census and
    /// CurrentAirportResolver use too.
    /// </summary>
    public bool ContainsPoint(double lat, double lon, double marginMetres = 0) => Grown(marginMetres).Contains(lat, lon);

    /// <summary>This box grown by <paramref name="marginMetres"/>: build it once when testing many points.</summary>
    public GrownBox Grown(double marginMetres) => GrownBox.Of(TopLat, BottomLat, LeftLon, RightLon, marginMetres);

    /// <summary>A row whose name says it belongs to a ramp position rather than to the controller
    /// the label names. "gates" is one: KMIA lists nine G rows and the first is "MIAMI GATES" at
    /// 120.35, which read out as the ground controller's frequency.</summary>
    private static readonly System.Text.RegularExpressions.Regex NotThePrimaryFrequency = new(
        @"\b(apron|ramp|gates?|delivery|clearance)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>"Fuel available. Tower 118.5, Ground 121.8, ATIS 124.05, UNICOM 122.95." or "".
    ///
    /// <para>BOTH flags together say only that there is fuel. On an MSFS 2024 database they are
    /// ALL-OR-NOTHING — measured 2026-09-21: 17,079 airports carry both, 67,199 neither, not one
    /// carries a single flag — so "both" is the database saying nothing about grade, and reading
    /// it as "Avgas and jet fuel." claimed jet fuel at 1,147 fields with no hard runway and a
    /// longest runway under 2,500 ft (4II2 "Hangar Fly Ultralight Fly Club", 965 ft). A disk-built
    /// MSFS 2020 database sets the two independently, which is why one flag alone still names its
    /// grade.</para></summary>
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
            // Only the VHF COM band: navdata also lists VOR-broadcast ATIS (EGLL 113.75), which no
            // COM radio can tune. Several rows of one type are normal at a hub (KATL: 7 towers) —
            // say so rather than presenting the first as THE frequency.
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
