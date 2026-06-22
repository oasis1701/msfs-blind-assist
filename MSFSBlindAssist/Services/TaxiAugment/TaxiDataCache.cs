using System.Text.Json;
using System.Text.Json.Serialization;

namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// Per-ICAO JSON cache for online taxi data (OSM + apt.dat sources).
/// Stores ALL fetched <see cref="AirportTaxiData"/> objects so the decorator can
/// re-merge with current navdata without refetching.
/// </summary>
public sealed class TaxiDataCache
{
    private readonly string _dir;
    private readonly int _ttlDays;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        IncludeFields = false,
        // Required so init-only properties and getter-only collections deserialize.
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };

    public TaxiDataCache(string dir, int ttlDays)
    {
        _dir = dir;
        _ttlDays = ttlDays;
    }

    /// <summary>
    /// Saves all source results for one ICAO to <c>&lt;dir&gt;/&lt;ICAO&gt;.json</c>.
    /// </summary>
    public void Save(string icao, IReadOnlyList<AirportTaxiData> sources)
    {
        Directory.CreateDirectory(_dir);
        var envelope = new CacheEnvelope
        {
            FetchedUtcTicks = DateTime.UtcNow.Ticks,
            Sources = sources.ToList(),
        };
        var path = FilePath(icao);
        var json = JsonSerializer.Serialize(envelope, _jsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Tries to load cached data for <paramref name="icao"/>.
    /// Returns <c>false</c> when the file is missing or the entry is older than <c>ttlDays</c>.
    /// </summary>
    public bool TryLoad(string icao, out IReadOnlyList<AirportTaxiData>? sources)
    {
        sources = null;
        var path = FilePath(icao);
        if (!File.Exists(path)) return false;

        try
        {
            var json = File.ReadAllText(path);
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(json, _jsonOptions);
            if (envelope == null) return false;

            var fetched = new DateTime(envelope.FetchedUtcTicks, DateTimeKind.Utc);
            if (_ttlDays >= 0 && (DateTime.UtcNow - fetched).TotalDays > _ttlDays)
                return false;

            sources = envelope.Sources;
            return sources != null && sources.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private string FilePath(string icao) =>
        Path.Combine(_dir, icao.ToUpperInvariant() + ".json");

    // ── Internal envelope ──────────────────────────────────────────────────

    private sealed class CacheEnvelope
    {
        public long FetchedUtcTicks { get; set; }
        public List<AirportTaxiData> Sources { get; set; } = new();
    }
}
