using System.IO.Compression;
using System.Text.Json;
namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// Fetches apt.dat taxi data from the X-Plane Gateway (gateway.x-plane.com).
/// Returns null on any failure so callers can fall back to other sources.
/// </summary>
public sealed class XplaneAptDatSource : ITaxiDataSource
{
    public string Id => "aptdat";
    private const string BaseUrl = "https://gateway.x-plane.com/apiv1";
    private readonly HttpClient _http;

    public XplaneAptDatSource(HttpClient http) => _http = http;

    public async Task<AirportTaxiData?> FetchAsync(
        string icao, double airportLat, double airportLon, CancellationToken ct)
    {
        try
        {
            // Step 1: get the recommended scenery ID for the airport
            var airportJson = await _http.GetStringAsync($"{BaseUrl}/airport/{icao}", ct);
            using var airportDoc = JsonDocument.Parse(airportJson);
            if (!airportDoc.RootElement.TryGetProperty("airport", out var airportEl)) return null;
            if (!airportEl.TryGetProperty("recommendedSceneryId", out var sceneryIdEl)) return null;
            long sceneryId = sceneryIdEl.GetInt64();

            // Step 2: get the scenery blob
            var sceneryJson = await _http.GetStringAsync($"{BaseUrl}/scenery/{sceneryId}", ct);
            using var sceneryDoc = JsonDocument.Parse(sceneryJson);
            if (!sceneryDoc.RootElement.TryGetProperty("scenery", out var sceneryEl)) return null;
            if (!sceneryEl.TryGetProperty("masterZipBlob", out var blobEl)) return null;
            var blobBase64 = blobEl.GetString();
            if (string.IsNullOrEmpty(blobBase64)) return null;

            // Step 3: base64 decode → unzip → find the .dat file
            var zipBytes = Convert.FromBase64String(blobBase64);
            using var zipStream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            var datEntry = archive.Entries.FirstOrDefault(e =>
                e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));
            if (datEntry == null) return null;

            // Step 4: parse the apt.dat text
            string text;
            using (var reader = new System.IO.StreamReader(datEntry.Open()))
                text = await reader.ReadToEndAsync(ct);

            return AptDatParser.Parse(text);
        }
        catch
        {
            // Return null on any network error, format error, or parse failure
            return null;
        }
    }
}
