using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// HTTP client for HiFi ActiveSky's local API (default port 19285). Used to
/// pull "real" ambient weather when ActiveSky is the active weather engine —
/// SimConnect's `AMBIENT_*` SimVars are well-known to be unreliable under
/// ActiveSky (precipitation type stuck, in-cloud flag jittery, wind values
/// lagging because MSFS interpolates on its own schedule). When ActiveSky is
/// running, its HTTP API is the source of truth for what the user is actually
/// flying through.
///
/// Detection is on-demand and cheap: <see cref="IsRunningAsync"/> does a 1-second
/// GET against /GetMode and returns true on success. Callers should cache the
/// result for a few seconds to avoid hammering the AS endpoint when the user
/// repeatedly refreshes the radar form.
///
/// Endpoint reference (Active_Sky_API.txt in the repo development folder):
/// - GetMode                  → returns mode string + active timestamp
/// - GetCurrentConditions     → JSON: ambient + surface + QNH at aircraft pos
/// - GetClosestStationWeather → JSON: same shape, at closest weather station
/// - GetMetarInfoAt?ICAO=…    → raw METAR text for an airport
/// - GetWeatherAreaJson?stations=A,B,C → conditions + per-station METARs/TAFs
/// </summary>
public class ActiveSkyClient
{
    // ActiveSky's default HTTP port. Users CAN change this in AS settings —
    // documented in Active_Sky_API.txt: "the port number used (19285 in the
    // example) can be changed through Active Sky settings. This configured
    // setting is shown in our log file(s) as well as our settings files in
    // the appdata and appdata\Options location for the product (e.g.
    // [appdata roaming]\HiFi\AS_FS \)."
    //
    // We try in order:
    //   1. The port from AS_FS's settings file (if AS is installed)
    //   2. 19285 (default)
    //   3. A small fallback list (in case the user has a non-default AS port
    //      AND no readable settings file)
    private const int DEFAULT_PORT = 19285;

    /// <summary>The port we successfully reached AS on, or null if we haven't.</summary>
    public int? LastSuccessfulPort { get; private set; }

    /// <summary>Last detection result reason — surfaced to the UI so the user can diagnose.</summary>
    public string LastStatus { get; private set; } = "not yet checked";

    private static readonly HttpClient _http;

    static ActiveSkyClient()
    {
        // Per-request timeout. Each candidate-port probe uses a CancellationToken
        // with this timeout (NOT the HttpClient.Timeout, which can be slow to
        // actually cancel because it's enforced via the response stream). 1.2 s
        // is more than enough for a localhost round-trip when AS is responsive,
        // and short enough that even if all candidate ports fail we hand control
        // back to the UI in well under a second total (probes run in parallel).
        _http = new HttpClient();
    }

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// Candidate HTTP ports for ActiveSky, in priority order. We hard-code the
    /// well-known defaults — scanning %APPDATA%\HiFi for a settings file was
    /// originally tried but caused massive UI-thread hangs because AS keeps
    /// gigabytes of weather logs/history in subdirectories and recursive file
    /// enumeration there blocked for many seconds. If a user has a custom port,
    /// we'll add an explicit setting later; meanwhile 99% of users run defaults
    /// and the parallel-probe strategy below makes a missing AS practically
    /// free (one timeout, in parallel with the others).
    /// </summary>
    private static readonly int[] CandidatePortList = { DEFAULT_PORT, 19286, 19287 };

    /// <summary>
    /// Result of a /GetCurrentConditions or /GetWeatherAreaJson query — the
    /// fields are populated from JSON. Properties match the API doc verbatim
    /// (note `SurfaceTemerature` typo in the AS API; we normalize the spelling).
    /// </summary>
    public sealed class Conditions
    {
        public double AmbientWindDirection { get; set; }
        public double AmbientWindSpeed { get; set; }     // knots
        public double AmbientTurbulence { get; set; }    // 1-100 (0 omitted by API)
        public double AmbientTemperature { get; set; }   // °C
        public double SurfaceWindDirection { get; set; }
        public double SurfaceWindSpeed { get; set; }     // knots
        public double SurfaceGustSpeed { get; set; }     // knots, 0 if none
        public double SurfaceTemperature { get; set; }   // °C
        public double SurfaceVisibility { get; set; }    // statute miles
        public double CloudCeilingFtAgl { get; set; }    // 0 if no broken/overcast below 8K
        public double QnhMb { get; set; }                // millibars
        // Unix epoch seconds — appears in the AS JSON response. Treated as the
        // "data freshness" timestamp by the weather monitor: when it advances,
        // we infer that AS has pulled new weather data, and the announcement
        // fires. If AS turns out to update this on every API call (request
        // time, not data time), the monitor falls back to METAR-content
        // comparison so behavior degrades gracefully.
        public long TimeStamp { get; set; }
        public string ClosestMetar { get; set; } = "";
        public List<string> Metars { get; set; } = new();
        public List<string> Tafs { get; set; } = new();
    }

    private string BaseUrl(int port) => $"http://localhost:{port}/ActiveSky/API";

    /// <summary>
    /// Liveness check. Tries the cached working port first (instant when
    /// AS is still up between refreshes) — if that fails, probes ALL candidate
    /// ports in parallel with a 1.2-second timeout each. Total worst-case wait
    /// when AS is missing: ~1.2 seconds (parallel), not 4×1.2 seconds (serial).
    /// Records the outcome in <see cref="LastStatus"/> for UI display.
    /// </summary>
    public async Task<bool> IsRunningAsync()
    {
        // MASTER SWITCH (Weather settings tab): when the user has not opted into
        // ActiveSky, NO probe may run — the parallel probe has a ~1.2 s floor when
        // AS is absent, which every non-AS user would otherwise pay on each call
        // (output+I hotkey, radar open, monitor poll). This central gate covers
        // every call site, present and future.
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled)
        {
            LastSuccessfulPort = null;
            LastStatus = "disabled in settings";
            return false;
        }

        var sw = Stopwatch.StartNew();

        // Fast path: cached port. Single probe, short timeout.
        if (LastSuccessfulPort is int cached)
        {
            if (await ProbePortAsync(cached) is { } cachedStatus && cachedStatus.success)
            {
                LastStatus = $"detected on port {cached}";
                Log.Debug("ActiveSky", $"probe ok: {LastStatus} in {sw.ElapsedMilliseconds} ms");
                return true;
            }
            // Cached port failed (AS stopped or moved). Fall through to a
            // fresh parallel probe. Don't keep the stale cache.
            LastSuccessfulPort = null;
        }

        // Parallel probe of all candidate ports. WhenAny returns the first
        // successful response (or all-failed). Each probe carries its own
        // CancellationToken so the timeout actually cancels the underlying
        // HTTP call rather than letting it idle on a connect attempt.
        var tasks = CandidatePortList.Select(p => ProbePortAsync(p)).ToList();
        var results = new List<(int port, bool success, string err)>();

        while (tasks.Count > 0)
        {
            var done = await Task.WhenAny(tasks);
            tasks.Remove(done);
            var r = await done;
            if (r.success)
            {
                LastSuccessfulPort = r.port;
                LastStatus = $"detected on port {r.port}";
                Log.Debug("ActiveSky", $"probe ok: {LastStatus} in {sw.ElapsedMilliseconds} ms");
                return true;
            }
            results.Add(r);
        }

        // All ports failed — surface the first error (usually the most
        // informative; subsequent ports tend to fail with the same reason).
        string firstErr = results.FirstOrDefault().err ?? "no candidate ports tried";
        LastSuccessfulPort = null;
        LastStatus = $"not detected ({firstErr})";
        Log.Debug("ActiveSky", $"probe failed: {LastStatus} in {sw.ElapsedMilliseconds} ms");
        return false;
    }

    private async Task<(int port, bool success, string err)> ProbePortAsync(int port)
    {
        using var cts = new System.Threading.CancellationTokenSource(ProbeTimeout);
        try
        {
            using var resp = await _http.GetAsync($"{BaseUrl(port)}/GetMode", cts.Token);
            if (resp.IsSuccessStatusCode)
                return (port, true, "");
            return (port, false, $"port {port}: HTTP {(int)resp.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            return (port, false, $"port {port}: timeout");
        }
        catch (HttpRequestException ex)
        {
            return (port, false, $"port {port}: {ShortenHttpError(ex.Message)}");
        }
        catch (Exception ex)
        {
            return (port, false, $"port {port}: {ex.GetType().Name}");
        }
    }

    private static string ShortenHttpError(string msg)
    {
        // .NET HttpRequestException messages are noisy. Keep just the most
        // useful suffix.
        if (msg.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
            return "no listener";
        if (msg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "timeout";
        return msg.Length > 60 ? msg.Substring(0, 60) + "…" : msg;
    }

    /// <summary>
    /// Interpolated ambient + surface weather at the aircraft's current
    /// position. Returns null on any error (network, non-200, JSON parse).
    /// Uses the port discovered by <see cref="IsRunningAsync"/>; call that
    /// first so we know which port to talk to.
    ///
    /// IMPORTANT: We call /GetWeatherAreaJson, NOT /GetCurrentConditions —
    /// despite the API documentation, /GetCurrentConditions returns a
    /// METAR-style ASCII string ("@POS 070835Z 14707KT 9999 -RA FEW311
    /// 29/23 Q1006 RMK ADVANCED INTERPOLATION"), not the documented JSON
    /// format. /GetWeatherAreaJson does return the documented JSON shape
    /// even when called with an empty `stations` parameter, so it's the
    /// correct endpoint for the structured ambient data we need.
    /// </summary>
    public async Task<Conditions?> GetCurrentConditionsAsync()
    {
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled) return null;   // master switch — no AS I/O when off
        if (LastSuccessfulPort is not int port) return null;
        // 5 s is generous for a localhost call but bounded — without a
        // CancellationToken HttpClient.GetAsync inherits no timeout
        // (HttpClient.Timeout is unset on our shared instance), so a hung
        // AS process would hang the caller forever and freeze the
        // ActiveSkyWeatherMonitor's re-entry guard permanently.
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using var resp = await _http.GetAsync(
                $"{BaseUrl(port)}/GetWeatherAreaJson?stations=", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = await resp.Content.ReadAsStringAsync(cts.Token);
            return ParseConditionsJson(body);
        }
        catch
        {
            Log.Debug("ActiveSky", "GetCurrentConditions failed (timeout or connection error)");
            return null;
        }
    }

    /// <summary>
    /// /GetWeatherAreaJson?stations=… — interpolated conditions PLUS METARs
    /// and TAFs for the specified airports. Pass up to ~250 ICAOs comma-
    /// separated. Returns null on any error.
    /// </summary>
    public async Task<Conditions?> GetWeatherAreaAsync(IEnumerable<string> stations)
    {
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled) return null;   // master switch — no AS I/O when off
        if (LastSuccessfulPort is not int port) return null;
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            string list = string.Join(",", stations);
            string url = $"{BaseUrl(port)}/GetWeatherAreaJson?stations={Uri.EscapeDataString(list)}";
            using var resp = await _http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = await resp.Content.ReadAsStringAsync(cts.Token);
            return ParseConditionsJson(body);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// /GetCurrentConditions — returns a METAR-style string describing the
    /// interpolated weather at the AIRCRAFT's current position. Format is
    /// `@POS DDHHMMZ DIRSPDKT VIS WX CLOUDS T/D QXXXX RMK ADVANCED INTERPOLATION`,
    /// e.g. `@POS 070835Z 14707KT 9999 -RA FEW311 29/23 Q1006 RMK ADVANCED
    /// INTERPOLATION`. Despite the API doc, this endpoint does NOT return
    /// JSON — it returns plain METAR text. We use it specifically to get
    /// precipitation tokens at the aircraft position (not the closest
    /// station), since under ActiveSky the SimConnect AMBIENT_PRECIP_STATE
    /// bitmask is unreliable. Returns null on error.
    /// </summary>
    public async Task<string?> GetPositionMetarAsync()
    {
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled) return null;   // master switch — no AS I/O when off
        if (LastSuccessfulPort is not int port) return null;
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using var resp = await _http.GetAsync($"{BaseUrl(port)}/GetCurrentConditions", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// /GetClosestStationWeather — closest weather-station METAR text. Used by
    /// the weather-update announcer to label the announcement with a real
    /// airport ICAO ("at EGLL") rather than the position-METAR's `@POS`
    /// pseudo-station. Format is the same METAR-style ASCII as
    /// /GetCurrentConditions, just with a real ICAO at the head. Returns null
    /// on error or if the response doesn't look like a METAR (some AS
    /// versions or modes may return JSON or empty).
    /// </summary>
    public async Task<string?> GetClosestStationMetarAsync()
    {
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled) return null;   // master switch — no AS I/O when off
        if (LastSuccessfulPort is not int port) return null;
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using var resp = await _http.GetAsync($"{BaseUrl(port)}/GetClosestStationWeather", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// /GetMetarInfoAt?ICAO=… — raw METAR text. Returns null on error.
    /// timeoffset 0 = current; positive seconds = forecast offset from AS time.
    /// </summary>
    public async Task<string?> GetMetarAsync(string icao, int timeOffsetSec = 0)
    {
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled) return null;   // master switch — no AS I/O when off
        if (LastSuccessfulPort is not int port) return null;
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            string url = $"{BaseUrl(port)}/GetMetarInfoAt?ICAO={Uri.EscapeDataString(icao)}&timeoffset={timeOffsetSec}";
            using var resp = await _http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            return (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses the JSON returned by GetCurrentConditions / GetWeatherAreaJson.
    /// All fields come back as STRINGS in the JSON (per ActiveSky's API,
    /// invariant culture for floats). We tolerate missing fields and parse
    /// permissively — any field that's missing or unparseable defaults to 0
    /// or empty. Returns null only if the JSON itself is malformed.
    /// </summary>
    private static Conditions? ParseConditionsJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var c = new Conditions();

            c.TimeStamp             = ReadLong(root, "TimeStamp");
            c.AmbientWindDirection  = ReadDouble(root, "AmbientWindDirection");
            c.AmbientWindSpeed      = ReadDouble(root, "AmbientWindSpeed");
            c.AmbientTurbulence     = ReadDouble(root, "AmbientTurbulence");
            c.AmbientTemperature    = ReadDouble(root, "AmbientTemperature");
            c.SurfaceWindDirection  = ReadDouble(root, "SurfaceWindDirection");
            c.SurfaceWindSpeed      = ReadDouble(root, "SurfaceWindSpeed");
            c.SurfaceGustSpeed      = ReadDouble(root, "SurfaceGustSpeed");
            // Note: AS API field name has a typo: "SurfaceTemerature".
            // Try both — current and corrected — so we keep working if HiFi
            // ever fixes it.
            c.SurfaceTemperature    = ReadDouble(root, "SurfaceTemperature");
            if (c.SurfaceTemperature == 0)
                c.SurfaceTemperature = ReadDouble(root, "SurfaceTemerature");
            c.SurfaceVisibility     = ReadDouble(root, "SurfaceVisibility");
            c.CloudCeilingFtAgl     = ReadDouble(root, "CloudCeiling");
            c.QnhMb                 = ReadDouble(root, "QNH");

            // METARList is an array of objects with a "METAR" string field.
            // Same for TAFList. Filter the "NULL RMK NOT FOUND" sentinel that
            // GetWeatherAreaJson returns when called with an empty stations
            // parameter — we don't want it polluting the precipitation parse.
            if (root.TryGetProperty("METARList", out var metarArr) && metarArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in metarArr.EnumerateArray())
                {
                    if (m.TryGetProperty("METAR", out var s) && s.ValueKind == JsonValueKind.String)
                    {
                        string raw = s.GetString() ?? "";
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        // Sentinel from empty-stations request.
                        if (raw.Contains("NULL RMK NOT FOUND", StringComparison.OrdinalIgnoreCase)) continue;
                        c.Metars.Add(raw);
                    }
                }
                if (c.Metars.Count > 0) c.ClosestMetar = c.Metars[0];
            }
            if (root.TryGetProperty("TAFList", out var tafArr) && tafArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tafArr.EnumerateArray())
                {
                    if (t.TryGetProperty("TAF", out var s) && s.ValueKind == JsonValueKind.String)
                        c.Tafs.Add(s.GetString() ?? "");
                }
            }

            return c;
        }
        catch
        {
            return null;
        }
    }

    private static double ReadDouble(JsonElement obj, string field)
    {
        if (!obj.TryGetProperty(field, out var v)) return 0;
        // ActiveSky returns numbers as strings (with invariant decimal
        // separator). JsonElement.GetDouble works for native numbers but
        // throws for strings — handle both.
        if (v.ValueKind == JsonValueKind.Number)
            return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String)
        {
            if (double.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double d))
                return d;
        }
        return 0;
    }

    private static long ReadLong(JsonElement obj, string field)
    {
        if (!obj.TryGetProperty(field, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number)
            return v.TryGetInt64(out long n) ? n : (long)v.GetDouble();
        if (v.ValueKind == JsonValueKind.String)
        {
            if (long.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                              System.Globalization.CultureInfo.InvariantCulture, out long n))
                return n;
        }
        return 0;
    }
}
