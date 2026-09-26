using System.Globalization;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Database.Models;

public readonly record struct ComFrequency(string Type, int FrequencyHz, string Name);

/// <summary>What the surroundings window lists about the airport itself
/// (<see cref="AirportFacilities.DescribeFacts"/>): the fuel line ("" when navdata says none),
/// and one row per COM frequency.</summary>
public sealed record AirportFacts(string Fuel, IReadOnlyList<string> Frequencies)
{
    public static readonly AirportFacts None = new("", Array.Empty<string>());
    public bool IsEmpty => Fuel.Length == 0 && Frequencies.Count == 0;
}

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

    /// <summary>A row naming a ramp or delivery position rather than the controller (KMIA's first G
    /// row is "MIAMI GATES"): listed after the controller's own rows of its kind.</summary>
    private static readonly System.Text.RegularExpressions.Regex NotThePrimaryFrequency = new(
        @"\b(apron|ramp|gates?|delivery|clearance)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The navdata COM types that are listed, and what each is called, in the order a pilot
    /// uses them: information, clearance, ground, tower, departure, approach, then the field's own
    /// and weather frequencies. A type not named here is not listed.</summary>
    private static readonly (string Type, string Label)[] FrequencyOrder =
    {
        ("ATIS", "ATIS"), ("C", "Clearance delivery"), ("CPT", "Clearance pre-taxi"), ("G", "Ground"), ("T", "Tower"),
        ("D", "Departure"), ("A", "Approach"), ("CTR", "Center"), ("CTAF", "CTAF"), ("UC", "UNICOM"),
        ("MC", "MULTICOM"), ("FSS", "Flight service"), ("AWOS", "AWOS"), ("ASOS", "ASOS"),
    };

    /// <summary>
    /// The fuel line and one row per COM frequency, for the surroundings window's lists — every
    /// frequency, so a pilot can arrow to the one they need (the one-line summary this replaced
    /// read only the first of each kind: "Tower 118.3 (3 listed)" at KMEM, whose other two are
    /// named exactly the same, and left clearance delivery out altogether).
    /// <para>Both fuel flags together say only "fuel": on fs2024 they are all-or-nothing (not one
    /// airport carries a single flag), so "Avgas and jet fuel" claimed jet fuel at 1,147 grass strips.
    /// An MSFS 2020 build sets them independently, so one flag alone still names its grade.</para>
    /// </summary>
    public AirportFacts DescribeFacts()
    {
        string fuel = (HasAvgas, HasJetFuel) switch
        {
            (true, true) => "Fuel available",
            (true, false) => "Avgas available",
            (false, true) => "Jet fuel available",
            _ => "",
        };
        return new AirportFacts(fuel, DescribeFrequencies());
    }

    /// <summary>
    /// "Tower 118.3", one row per frequency, each starting with its kind so a list's first-letter
    /// search finds it. VHF COM band only (navdata also lists VOR-broadcast ATIS). A row carries its
    /// navdata name only where the rows of its kind do NOT all share one — then the name is what
    /// tells them apart ("Ground 129.25, RAMP CONTROL" at KATL, "Ground 131.375, DELTA" at KJFK);
    /// where they all share it (KMEM: every one "MEMPHIS") it says nothing.
    /// </summary>
    public List<string> DescribeFrequencies()
    {
        var rows = new List<string>();
        foreach (var (type, label) in FrequencyOrder)
        {
            var ofType = Coms.Where(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase)
                                         && c.FrequencyHz >= 118_000_000 && c.FrequencyHz < 137_000_000).ToList();
            if (ofType.Count == 0) continue;
            bool namesDiffer = ofType.Select(c => (c.Name ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            // OrderBy is stable, so each group keeps navdata's own order.
            foreach (var c in ofType.OrderBy(c => NotThePrimaryFrequency.IsMatch(c.Name ?? "") ? 1 : 0))
            {
                string name = (c.Name ?? "").Trim();
                string row = namesDiffer && name.Length > 0
                    ? $"{label} {FormatMhz(c.FrequencyHz)}, {name}"
                    : $"{label} {FormatMhz(c.FrequencyHz)}";
                if (!rows.Contains(row)) rows.Add(row);
            }
        }
        return rows;
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
