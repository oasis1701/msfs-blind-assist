using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Geography;

internal enum DownloadJobPhase { Idle, Downloading, Building, Ready, Failed, Cancelled }

/// <summary>The job's state for the Settings tab's status box. <see cref="Percent"/> is -1
/// when the phase has no percentage.</summary>
internal sealed record DownloadStatus(DownloadJobPhase Phase, string Text, int Percent = -1)
{
    internal bool IsRunning => Phase is DownloadJobPhase.Downloading or DownloadJobPhase.Building;
}

/// <summary>A sentence the job wants spoken. Immediate for the press confirmation, errors and
/// "ready"; queued for progress and stalls so it never cuts the pilot off.</summary>
internal sealed record DownloadSpeech(string Text, bool Immediate);

/// <summary>
/// Downloads GeoNames' dump and Natural Earth's shapes from their publishers and builds the
/// location database, one job at a time. Owned by MainForm so it keeps running when Settings
/// closes; it holds no announcer and touches no UI: it raises <see cref="StatusChanged"/> and
/// <see cref="Speak"/> on worker threads and the listeners marshal (the announcer is UI-thread
/// only). The 402 MB dump resumes from a <c>.part</c> file with <c>If-Range</c>, so a dump the
/// server rebuilt in the meantime restarts instead of being spliced. A stalled read never
/// returns, so a watchdog cancels the attempt and the retry resumes.
/// </summary>
internal sealed class LocationDataDownloader : IDisposable
{
    internal const string GeoNamesBaseUrl = "https://download.geonames.org/export/dump/";
    internal const string NaturalEarthBaseUrl = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/" + NaturalEarthShapes.Release + "/geojson/";
    /// <summary>Used for the first size estimate before the server has said the real size.</summary>
    internal const long GeoNamesZipEstimateBytes = 422L * 1024 * 1024;
    internal const long NaturalEarthDownloadEstimateBytes = 20L * 1024 * 1024;

    /// <summary>SHA-256 of each pinned v5.1.2 file as written to disk (after gzip transfer decoding).</summary>
    internal static readonly IReadOnlyDictionary<NaturalEarthLayer, string> NaturalEarthSha256 = new Dictionary<NaturalEarthLayer, string>
    {
        [NaturalEarthLayer.Marine] = "53f865e8ffa966cdd402145c82c5cd14ee7ce974cd0eb9a3f59f03a4cfd2d66c",
        [NaturalEarthLayer.Coastline] = "6f75ae0e0de157b14946e2255eb1f5486d9a13819032e26d4610852d296788f6",
        [NaturalEarthLayer.Lake] = "2d036f53dedec578001c5c30c2959ee7d4eebc1306900fa4367c49929ec8f2d9",
        [NaturalEarthLayer.Region] = "b7b26e50ea917d3696aec87f932def2bf5f890f5770e441d59c162c6f4c92a77",
        [NaturalEarthLayer.State] = "22d0e3ad85eb3e27f17cabf8ba2d50e554fbc27a87796ff891d958185da62fb5",
    };

    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);
    private const int SmallFileAttempts = 3;

    private readonly LocationDataStore _store;
    private readonly HttpClient _zipClient;   // no transfer compression: Range offsets are file offsets
    private readonly HttpClient _fileClient;  // gzip transfer for the GeoJSON and name files
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _job;
    private volatile bool _silentCancel;
    private DownloadStatus _status;

    private readonly IReadOnlyDictionary<NaturalEarthLayer, string> _naturalEarthSha256;
    private readonly Func<string, long> _freeBytes;

    /// <param name="zipHandler">Test seam: the handler for the dump download.</param>
    /// <param name="fileHandler">Test seam: the handler for the small files.</param>
    /// <param name="naturalEarthSha256">Test seam: hashes for fixture shape files.</param>
    /// <param name="freeBytes">Test seam: free bytes on a drive root.</param>
    internal LocationDataDownloader(LocationDataStore store, HttpMessageHandler? zipHandler = null, HttpMessageHandler? fileHandler = null,
        IReadOnlyDictionary<NaturalEarthLayer, string>? naturalEarthSha256 = null, Func<string, long>? freeBytes = null)
    {
        _store = store;
        _zipClient = CreateClient(zipHandler ?? CreateHandler(DecompressionMethods.None));
        _fileClient = CreateClient(fileHandler ?? CreateHandler(DecompressionMethods.All));
        _naturalEarthSha256 = naturalEarthSha256 ?? NaturalEarthSha256;
        _freeBytes = freeBytes ?? (root => new DriveInfo(root).AvailableFreeSpace);
        _status = IdleStatus();
    }

    /// <summary>Test seam: the build's sanity floors (the real dump has ~13.5 M rows, ~8.4 M kept).</summary>
    internal long MinimumPlaces { get; init; } = 7_000_000;
    internal long MinimumRows { get; init; } = 12_000_000;

    /// <summary>Test seam: the running (or last) job.</summary>
    internal Task Job { get { lock (_gate) return _job ?? Task.CompletedTask; } }

    /// <summary>Raised on a worker thread whenever the status text changes (phase change or whole percent).</summary>
    internal event EventHandler<DownloadStatus>? StatusChanged;

    /// <summary>Raised on a worker thread with a sentence to speak.</summary>
    internal event EventHandler<DownloadSpeech>? Speak;

    /// <summary>Raised on a worker thread after a new database went live.</summary>
    internal event EventHandler? Completed;

    internal DownloadStatus Status => Volatile.Read(ref _status);

    internal bool IsRunning => Status.IsRunning;

    /// <summary>True when some database is installed (current or needing an update): the
    /// button then says "Update location data".</summary>
    internal bool HasData => _store.IsAvailable || _store.NeedsUpdate;

    /// <summary>Starts a download (or an update, when data exists). Ignored while a job runs.</summary>
    internal void Start()
    {
        lock (_gate)
        {
            if (_job is { IsCompleted: false }) return;
            _silentCancel = false;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            SetStatus(new DownloadStatus(DownloadJobPhase.Downloading, "Starting the location data download.", 0));
            _job = Task.Run(() => RunAsync(ct));
        }
    }

    /// <summary>The pilot's Cancel: spoken, and the partial download is kept for next time.</summary>
    internal void Cancel()
    {
        lock (_gate) _cts?.Cancel();
    }

    /// <summary>App close: cancels without speaking and waits briefly for the build to let go of its files.</summary>
    internal void Shutdown(TimeSpan wait)
    {
        Task? job;
        lock (_gate)
        {
            _silentCancel = true;
            _cts?.Cancel();
            job = _job;
        }
        try { job?.Wait(wait); }
        catch (AggregateException) { }
        LocationDataStore.DeleteQuietly(_store.StagingPath);
        LocationDataStore.DeleteQuietly(_store.BuildingPath);
    }

    public void Dispose()
    {
        _zipClient.Dispose();
        _fileClient.Dispose();
        _cts?.Dispose();
    }

    /// <summary>The status when no job runs: installed data, an update needed, a download waiting
    /// to be built or resumed, or nothing yet.</summary>
    internal DownloadStatus IdleStatus() => new(DownloadJobPhase.Idle,
        LocationDataStatusText.Idle(_store.Info, LocationDataStore.SchemaVersion, PartialBytes(), CompleteDump() is not null));

    private string ZipPath => Path.Combine(_store.DownloadFolder, "allCountries.zip");

    private long PartialBytes()
    {
        try
        {
            var part = new FileInfo(ZipPath + ".part");
            return part.Exists ? part.Length : 0;
        }
        catch (IOException) { return 0; }
    }

    /// <summary>How long a whole dump left by a cancelled or failed build is reused. Places hardly
    /// change in days, and an Update pressed weeks later should still fetch the current dump.</summary>
    private static readonly TimeSpan CompleteDumpReuse = TimeSpan.FromDays(3);

    /// <summary>A whole dump a cancelled or failed build left behind, with the validator saved
    /// when it was downloaded; null when there is none, it does not match its saved size, or it is
    /// older than <see cref="CompleteDumpReuse"/>. Reused so the next press builds without fetching
    /// 400 MB again.</summary>
    private PartMeta? CompleteDump()
    {
        try
        {
            var meta = LoadMeta(ZipPath + ".json", GeoNamesBaseUrl + "allCountries.zip");
            if (meta is null || !File.Exists(ZipPath)) return null;
            var zip = new FileInfo(ZipPath);
            return zip.Length == meta.Total && DateTime.UtcNow - zip.LastWriteTimeUtc < CompleteDumpReuse ? meta : null;
        }
        catch (IOException) { return null; }
    }

    // ---- the job ----

    private async Task RunAsync(CancellationToken ct)
    {
        bool update = _store.IsAvailable;
        try
        {
            Directory.CreateDirectory(_store.DownloadFolder);
            long partial = PartialBytes();
            bool haveDump = CompleteDump() is not null;
            long remaining = (haveDump ? 0 : Math.Max(0, GeoNamesZipEstimateBytes - partial)) + NaturalEarthDownloadEstimateBytes;
            CheckSpace(DiskSpaceRequirement.BeforeDownload(remaining));

            string verb = update ? "Updating" : partial > 0 ? "Resuming" : "Downloading";
            Say(haveDump
                ? $"{(update ? "Updating" : "Building")} location data from the earlier download."
                : $"{verb} location data, about {DiskSpaceRequirement.Spoken(remaining)} to download.", immediate: true);

            var inputs = await DownloadAllAsync(ct).ConfigureAwait(false);

            CheckSpace(DiskSpaceRequirement.BeforeBuild());
            Say("Download finished. Building location data; this takes about a minute.", immediate: false);
            SetStatus(new DownloadStatus(DownloadJobPhase.Building, "Building location data: 0 percent", 0));
            var info = await RunBuildAsync(inputs, ct).ConfigureAwait(false);

            DeleteDownloads();
            string places = LocationDataStatusText.Millions(info.PlaceCount);
            if (File.Exists(_store.ReadyPath))
            {
                // Something (usually an antivirus scan) held the old file through every retry.
                const string later = "Location data is ready and will be used from the next start of MSFS Blind Assist.";
                SetStatus(new DownloadStatus(DownloadJobPhase.Ready, later));
                Say(later, immediate: true);
                return;
            }
            SetStatus(new DownloadStatus(DownloadJobPhase.Ready, LocationDataStatusText.Idle(_store.Info ?? info, LocationDataStore.SchemaVersion, 0)));
            Say($"Location data ready, {places} places.", immediate: true);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            long kept = PartialBytes();
            string text = kept > 0
                ? $"Location data download cancelled. Partial download kept: {DiskSpaceRequirement.Spoken(kept)}."
                : "Location data download cancelled.";
            SetStatus(new DownloadStatus(DownloadJobPhase.Cancelled, text + Environment.NewLine + IdleStatus().Text));
            if (!_silentCancel) Say(text, immediate: true);
            Log.Info("LocationData", "Download cancelled" + (_silentCancel ? " (app closing)" : ""));
        }
        catch (Exception ex)
        {
            string reason = ex switch
            {
                LocationDownloadException lde => lde.Message,
                InvalidDataException ide => ide.Message,
                HttpRequestException hre when hre.StatusCode is { } code => $"the server answered {(int)code} {code}.",
                HttpRequestException => "the download server could not be reached. Check the internet connection.",
                IOException io => "a file could not be written: " + io.Message,
                UnauthorizedAccessException ua => "a file could not be written: " + ua.Message,
                _ => ex.Message,
            };
            string text = "Location data download failed: " + reason;
            Log.Error("LocationData", text, ex);
            SetStatus(new DownloadStatus(DownloadJobPhase.Failed, text + Environment.NewLine + IdleStatus().Text));
            if (!_silentCancel) Say(text, immediate: true);
        }
    }

    private void CheckSpace(long needed)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(_store.DatabasePath))!;
        long free = _freeBytes(root);
        if (free < needed)
            throw new LocationDownloadException(
                $"not enough disk space on {root.TrimEnd('\\')}. It needs {DiskSpaceRequirement.Spoken(needed)} free and has {DiskSpaceRequirement.Spoken(free)}.");
    }

    private async Task<LocationBuildInputs> DownloadAllAsync(CancellationToken ct)
    {
        string folder = _store.DownloadFolder;
        string country = Path.Combine(folder, "countryInfo.txt");
        string admin1 = Path.Combine(folder, "admin1CodesASCII.txt");
        SetStatus(new DownloadStatus(DownloadJobPhase.Downloading, "Downloading GeoNames country and state names.", 0));
        await DownloadSmallFileAsync(GeoNamesBaseUrl + "countryInfo.txt", country, null, ct).ConfigureAwait(false);
        await DownloadSmallFileAsync(GeoNamesBaseUrl + "admin1CodesASCII.txt", admin1, null, ct).ConfigureAwait(false);

        var shapes = new Dictionary<NaturalEarthLayer, string>();
        int n = 0;
        foreach (var (layer, file) in NaturalEarthShapes.LayerFiles)
        {
            n++;
            SetStatus(new DownloadStatus(DownloadJobPhase.Downloading,
                $"Downloading Natural Earth shapes, file {n} of {NaturalEarthShapes.LayerFiles.Count}.", 0));
            string path = Path.Combine(folder, file + ".geojson");
            await DownloadSmallFileAsync(NaturalEarthBaseUrl + file + ".geojson", path, _naturalEarthSha256[layer], ct).ConfigureAwait(false);
            shapes[layer] = path;
        }

        var (zip, date) = await DownloadGeoNamesZipAsync(ct).ConfigureAwait(false);
        return new LocationBuildInputs(zip, country, admin1, shapes, date)
        {
            MinimumPlaces = MinimumPlaces,
            MinimumRows = MinimumRows,
        };
    }

    /// <summary>A small file, fetched whole (with gzip transfer) and retried whole. A known
    /// SHA-256 is checked; a file already on disk with that hash is not fetched again.</summary>
    private async Task DownloadSmallFileAsync(string url, string path, string? sha256, CancellationToken ct)
    {
        if (sha256 is not null && File.Exists(path) && await HashAsync(path, ct).ConfigureAwait(false) == sha256) return;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var response = await SendAsync(_fileClient, new HttpRequestMessage(HttpMethod.Get, url), attemptCts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                string temp = path + ".tmp";
                await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                await using (var body = await response.Content.ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false))
                    await CopyWithStallWatchAsync(body, file, null, new StallDetector(DateTime.UtcNow), attemptCts).ConfigureAwait(false);
                if (sha256 is not null && await HashAsync(temp, ct).ConfigureAwait(false) != sha256)
                    throw new InvalidDataException($"{Path.GetFileName(path)} did not match its published checksum.");
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < SmallFileAttempts && !ct.IsCancellationRequested && IsRetryable(ex))
            {
                Log.Warn("LocationData", $"{url} attempt {attempt} failed: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct).ConfigureAwait(false);
            }
        }
    }

    private sealed record PartMeta
    {
        public string Url { get; set; } = "";
        public string? ETag { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public long Total { get; set; }
    }

    /// <summary>The dump's date ("2026-10-10") from its Last-Modified, for the status box.</summary>
    private static string DumpDate(PartMeta? meta) =>
        (meta?.LastModified ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The 402 MB dump: resumable, stall-watched, retried while it makes progress.</summary>
    private async Task<(string Path, string Date)> DownloadGeoNamesZipAsync(CancellationToken ct)
    {
        string url = GeoNamesBaseUrl + "allCountries.zip";
        string final = ZipPath;
        string part = final + ".part";
        string sidecar = part + ".json";

        if (CompleteDump() is { } earlier)
        {
            Log.Info("LocationData", "Reusing the complete GeoNames dump from an earlier attempt");
            return (final, DumpDate(earlier));
        }

        var meta = LoadMeta(sidecar, url);
        var gate = new DownloadProgressGate();
        var budget = new RetryBudget();
        // One detector across attempts, so data after a stall-and-retry still says "Download resumed".
        var stall = new StallDetector(DateTime.UtcNow);
        bool retried = false;

        while (true)
        {
            long partLength = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (partLength > 0 && meta is null)
            {
                // A partial file without its validator cannot be resumed safely: start clean.
                LocationDataStore.DeleteQuietly(part);
                partLength = 0;
            }
            if (partLength == 0) meta = null;
            long bytesThisAttempt = 0;
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.Restart(DateTime.UtcNow);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.AcceptEncoding.ParseAdd("identity");
                if (partLength > 0 && meta is not null)
                {
                    request.Headers.Range = new RangeHeaderValue(partLength, null);
                    if (meta.ETag is { } etag && !etag.StartsWith("W/", StringComparison.Ordinal))
                        request.Headers.IfRange = new RangeConditionHeaderValue(etag);
                    else if (meta.LastModified is { } lm)
                        request.Headers.IfRange = new RangeConditionHeaderValue(lm);
                }

                using var response = await SendAsync(_zipClient, request, attemptCts.Token).ConfigureAwait(false);
                var range = response.Content.Headers.ContentRange;
                var outcome = ResumeDecision.Decide((int)response.StatusCode, partLength, range?.From, range?.Length, meta?.Total);
                Log.Info("LocationData", $"GeoNames request: HTTP {(int)response.StatusCode}, part {partLength} bytes, outcome {outcome}");

                switch (outcome)
                {
                    case ResumeOutcome.AlreadyComplete:
                        break;
                    case ResumeOutcome.Fail:
                        response.EnsureSuccessStatusCode();
                        throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
                    case ResumeOutcome.Fresh:
                    case ResumeOutcome.RestartChanged:
                        if (outcome == ResumeOutcome.RestartChanged)
                            Say("The GeoNames file changed on the server; starting the download again.", immediate: false);
                        partLength = 0;
                        meta = new PartMeta
                        {
                            Url = url,
                            ETag = response.Headers.ETag?.ToString(),
                            LastModified = response.Content.Headers.LastModified,
                            Total = response.Content.Headers.ContentLength ?? 0,
                        };
                        SaveMeta(sidecar, meta);
                        gate.Restart(0, meta.Total);
                        break;
                    case ResumeOutcome.Append:
                        gate.Restart(partLength, meta!.Total);
                        break;
                }

                if (outcome != ResumeOutcome.AlreadyComplete)
                {
                    long total = meta!.Total;
                    await using var file = new FileStream(part, partLength == 0 ? FileMode.Create : FileMode.Append,
                        FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                    await using var body = await response.Content.ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
                    long done = partLength;
                    int lastPercent = -1;
                    await CopyWithStallWatchAsync(body, file, read =>
                    {
                        bytesThisAttempt += read;
                        done += read;
                        if (stall.OnBytes(DateTime.UtcNow) | retried)
                        {
                            retried = false;
                            Say("Download resumed.", immediate: false);
                        }
                        int percent = DownloadProgressGate.WholePercent(done, total);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            SetStatus(new DownloadStatus(DownloadJobPhase.Downloading,
                                $"Downloading GeoNames: {percent} percent, {done / (1024 * 1024)} of {total / (1024 * 1024)} MB.", percent));
                        }
                        if (gate.Observe(done, total) is int milestone)
                            Say($"Location data download {milestone} percent.", immediate: false);
                    }, stall, attemptCts).ConfigureAwait(false);
                }

                long length = new FileInfo(part).Length;
                if (meta is { Total: > 0 } && length != meta.Total)
                    throw new IOException($"the download ended at {length} of {meta.Total} bytes");
                File.Move(part, final, overwrite: true);
                // The validator stays beside the whole file, so a build that fails or is cancelled
                // can reuse it (CompleteDump) instead of fetching 400 MB again.
                if (meta is not null) SaveMeta(final + ".json", meta with { Total = length });
                LocationDataStore.DeleteQuietly(sidecar);
                return (final, DumpDate(meta));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && IsRetryable(ex))
            {
                bool stalled = ex is OperationCanceledException;
                if (!budget.AttemptFailed(bytesThisAttempt))
                    throw new LocationDownloadException(stalled
                        ? "the download kept stalling. The partial download is kept; try again later."
                        : "the download server could not be reached. Check the internet connection; the partial download is kept.");
                string why = stalled ? "No data for a minute." : "Connection lost.";
                Log.Warn("LocationData", $"GeoNames attempt failed ({ex.Message}); retry {budget.RetryNumber} of {RetryBudget.MaxNoProgressRetries}");
                Say($"{why} Retrying, attempt {budget.RetryNumber} of {RetryBudget.MaxNoProgressRetries}.", immediate: false);
                SetStatus(new DownloadStatus(DownloadJobPhase.Downloading,
                    $"{why} Retrying, attempt {budget.RetryNumber} of {RetryBudget.MaxNoProgressRetries}.", Status.Percent));
                retried = true;
                await Task.Delay(budget.Backoff, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Copies a body, cancelling the attempt when the stall detector says so (a stalled
    /// read never returns by itself). Speaks the stall once at 20 seconds. <paramref name="onRead"/>
    /// must feed <see cref="StallDetector.OnBytes"/> itself when given.</summary>
    private async Task CopyWithStallWatchAsync(Stream body, Stream file, Action<int>? onRead,
        StallDetector stall, CancellationTokenSource attemptCts)
    {
        var ct = attemptCts.Token;
        using var watchdog = new System.Threading.Timer(_ =>
        {
            switch (stall.Check(DateTime.UtcNow))
            {
                case StallVerdict.AnnounceStall:
                    Say("Location data download stalled, no data for 20 seconds.", immediate: false);
                    SetStatus(Status with { Text = "Stalled: no data for 20 seconds. Waiting, then retrying." });
                    break;
                case StallVerdict.Abort:
                    try { attemptCts.Cancel(); } catch (ObjectDisposedException) { }
                    break;
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        var buffer = new byte[1 << 16];
        int read;
        while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            if (onRead is null) stall.OnBytes(DateTime.UtcNow);
            else onRead(read);
        }
    }

    private Task<LocationDataInfo> RunBuildAsync(LocationBuildInputs inputs, CancellationToken ct)
    {
        var done = new TaskCompletionSource<LocationDataInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                int lastPercent = -1;
                var info = new LocationDataBuilder(_store).Build(inputs, p =>
                {
                    if (p.Percent == lastPercent) return;
                    lastPercent = p.Percent;
                    SetStatus(new DownloadStatus(DownloadJobPhase.Building, $"Building location data: {p.Percent} percent", p.Percent));
                }, ct);
                done.SetResult(info);
            }
            catch (OperationCanceledException oce) { done.SetCanceled(oce.CancellationToken); }
            catch (Exception ex) { done.SetException(ex); }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal, // the pilot may be flying while it builds
            Name = "Location data build",
        };
        thread.Start();
        return done.Task;
    }

    private void DeleteDownloads()
    {
        try
        {
            if (Directory.Exists(_store.DownloadFolder)) Directory.Delete(_store.DownloadFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("LocationData", $"Could not remove the download folder: {ex.Message}");
        }
    }

    // ---- helpers ----

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headers.CancelAfter(HeaderTimeout);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
    }

    private static bool IsRetryable(Exception ex) => ex switch
    {
        HttpRequestException hre => hre.StatusCode is null || (int)hre.StatusCode >= 500 || hre.StatusCode == HttpStatusCode.RequestTimeout,
        OperationCanceledException => true, // a stall abort or the header timeout (the pilot's cancel is filtered by the callers)
        IOException => true,
        InvalidDataException => true,       // a checksum mismatch: fetch again
        _ => false,
    };

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    private static PartMeta? LoadMeta(string path, string url)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var meta = JsonSerializer.Deserialize<PartMeta>(File.ReadAllText(path));
            return meta is not null && meta.Url == url && meta.Total > 0 ? meta : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static void SaveMeta(string path, PartMeta meta) => File.WriteAllText(path, JsonSerializer.Serialize(meta));

    private void SetStatus(DownloadStatus status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(this, status);
    }

    private void Say(string text, bool immediate) => Speak?.Invoke(this, new DownloadSpeech(text, immediate));

    private static SocketsHttpHandler CreateHandler(DecompressionMethods decompression) => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AutomaticDecompression = decompression,
        DefaultProxyCredentials = CredentialCache.DefaultCredentials,
    };

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        string version = typeof(LocationDataDownloader).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MSFSBlindAssist/{version} (location-data)");
        return client;
    }
}

/// <summary>A failure whose message is already the sentence the pilot should hear.</summary>
internal sealed class LocationDownloadException(string message) : Exception(message);
