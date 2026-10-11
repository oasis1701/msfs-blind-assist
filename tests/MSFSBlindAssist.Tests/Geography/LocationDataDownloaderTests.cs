using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

/// <summary>
/// The whole download-and-build job against a fake server serving the
/// <see cref="GeographyFixtureWorld"/>: fresh download, resume with If-Range, a dump rebuilt on
/// the server mid-download, a full disk and the pilot's cancel.
/// </summary>
public sealed class LocationDataDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-dl-" + Guid.NewGuid().ToString("N"));
    private readonly LocationDataStore _store;
    private readonly FakeServer _server = new();
    private readonly List<string> _spoken = new();

    public LocationDataDownloaderTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new LocationDataStore(Path.Combine(_dir, "location-data.sqlite"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static IReadOnlyDictionary<NaturalEarthLayer, string> Hashes(IReadOnlyDictionary<NaturalEarthLayer, string> shapes) =>
        shapes.ToDictionary(kv => kv.Key, kv => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(kv.Value))));

    private static readonly IReadOnlyDictionary<NaturalEarthLayer, string> FixtureHashes = Hashes(GeographyFixtureWorld.Shapes);

    private LocationDataDownloader Downloader(long freeBytes = long.MaxValue)
    {
        var d = new LocationDataDownloader(_store, _server, _server, Hashes(_server.Shapes), _ => freeBytes)
        {
            MinimumPlaces = 1,
            MinimumRows = 1,
        };
        d.Speak += (_, s) => { lock (_spoken) _spoken.Add(s.Text); };
        return d;
    }

    private string PartPath => Path.Combine(_store.DownloadFolder, "allCountries.zip.part");

    [Fact(Timeout = 30_000)]
    public async Task A_fresh_download_builds_installs_and_cleans_up()
    {
        using var d = Downloader();
        bool completed = false;
        d.Completed += (_, _) => completed = true;
        d.Start();
        await d.Job;

        Assert.True(completed);
        Assert.True(_store.IsAvailable);
        Assert.Equal(DownloadJobPhase.Ready, d.Status.Phase);
        Assert.Equal("2026-10-09", _store.Info!.GeoNamesDate); // from Last-Modified
        Assert.False(Directory.Exists(_store.DownloadFolder));
        Assert.StartsWith("Downloading location data, about ", _spoken[0]);
        Assert.Contains(_spoken, s => s.StartsWith("Download finished. Building location data"));
        Assert.Equal("Location data ready, " + GeographyFixtureWorld.KeptPlaces + " places.", _spoken[^1]);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_partial_download_resumes_with_if_range_and_appends_the_tail()
    {
        WritePartial(half: true, etag: FakeServer.ZipETag);
        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.True(_store.IsAvailable);
        var zipRequest = Assert.Single(_server.ZipRequests);
        Assert.Equal(FakeServer.Zip.Length / 2, zipRequest.Range?.Ranges.Single().From);
        Assert.Equal(FakeServer.ZipETag, zipRequest.IfRange?.EntityTag?.ToString());
        Assert.StartsWith("Resuming location data", _spoken[0]);
        Assert.DoesNotContain(_spoken, s => s.Contains("changed on the server"));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_dump_rebuilt_on_the_server_restarts_instead_of_splicing()
    {
        WritePartial(half: true, etag: "\"yesterday\"");
        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.True(_store.IsAvailable); // the 200 full body replaced the stale half
        Assert.Contains(_spoken, s => s == "The GeoNames file changed on the server; starting the download again.");
    }

    [Fact(Timeout = 30_000)]
    public async Task A_complete_download_left_by_a_failed_build_is_built_without_fetching_it_again()
    {
        Directory.CreateDirectory(_store.DownloadFolder);
        string zip = Path.Combine(_store.DownloadFolder, "allCountries.zip");
        File.WriteAllBytes(zip, FakeServer.Zip);
        File.WriteAllText(zip + ".json",
            $"{{\"Url\":\"{LocationDataDownloader.GeoNamesBaseUrl}allCountries.zip\",\"ETag\":\"\\\"dump-v1\\\"\",\"Total\":{FakeServer.Zip.Length}}}");
        using var d = Downloader();
        Assert.StartsWith("Downloaded but not built yet.", d.IdleStatus().Text);

        d.Start();
        await d.Job;

        Assert.Empty(_server.ZipRequests);
        Assert.True(_store.IsAvailable);
        Assert.Equal("Building location data from the earlier download.", _spoken[0]);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_dump_the_build_rejected_is_deleted_so_the_next_press_downloads_afresh()
    {
        using var d = new LocationDataDownloader(_store, _server, _server, FixtureHashes, _ => long.MaxValue)
        {
            MinimumPlaces = 1,
            MinimumRows = 1_000, // the fixture dump is too short: the build rejects it after the download
        };
        d.Start();
        await d.Job;

        Assert.Equal(DownloadJobPhase.Failed, d.Status.Phase);
        Assert.False(_store.IsAvailable);
        // Kept, it would fail the same way on every press for three days (LOC-4).
        Assert.False(File.Exists(Path.Combine(_store.DownloadFolder, "allCountries.zip")));
        Assert.False(File.Exists(Path.Combine(_store.DownloadFolder, "allCountries.zip.json")));
        Assert.DoesNotContain("Downloaded but not built yet.", d.Status.Text);
        Assert.StartsWith("Not downloaded.", d.IdleStatus().Text);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_build_the_pilot_cancels_keeps_the_whole_download_for_the_next_press()
    {
        using var d = Downloader();
        // Cancel as soon as the job says it is building: the build then stops at its first check.
        d.StatusChanged += (_, s) => { if (s.Phase == DownloadJobPhase.Building) d.Cancel(); };
        d.Start();
        await d.Job;

        Assert.Equal(DownloadJobPhase.Cancelled, d.Status.Phase);
        Assert.False(_store.IsAvailable);
        Assert.True(File.Exists(Path.Combine(_store.DownloadFolder, "allCountries.zip")));
        Assert.StartsWith("Downloaded but not built yet.", d.IdleStatus().Text);
    }

    [Theory(Timeout = 30_000)]
    [InlineData(HttpStatusCode.PartialContent)]    // a 206 from the wrong offset
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable)] // a 416 that disagrees about the size
    public async Task A_resume_answer_that_is_not_the_tail_is_dropped_unread_and_the_download_starts_over(HttpStatusCode answer)
    {
        WritePartial(half: true, etag: FakeServer.ZipETag);
        _server.BadResumeAnswer = answer;
        using var d = Downloader();
        d.Start();
        await d.Job;

        // Had the body been written as the whole file, the build would have failed on it.
        Assert.Equal(DownloadJobPhase.Ready, d.Status.Phase);
        Assert.True(_store.IsAvailable);
        Assert.Equal(2, _server.ZipRequests.Count);
        Assert.NotNull(_server.ZipRequests[0].Range);
        Assert.Null(_server.ZipRequests[1].Range); // the retry asks for the whole file
        Assert.Contains(_spoken, s => s == "The partial download could not be resumed; starting the download again.");
    }

    [Fact(Timeout = 30_000)]
    public async Task Too_little_disk_space_fails_before_downloading_anything()
    {
        using var d = Downloader(freeBytes: 100L * 1024 * 1024);
        d.Start();
        await d.Job;

        Assert.Equal(DownloadJobPhase.Failed, d.Status.Phase);
        Assert.Empty(_server.ZipRequests);
        Assert.StartsWith("Location data download failed: not enough disk space", _spoken.Single());
        Assert.False(_store.IsAvailable);
    }

    [Fact(Timeout = 30_000)]
    public async Task Cancel_is_spoken_keeps_the_partial_file_and_installs_nothing()
    {
        _server.HoldZipBody = new TaskCompletionSource();
        using var d = Downloader();
        d.Start();
        await _server.ZipBodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        d.Cancel();
        await d.Job;

        Assert.Equal(DownloadJobPhase.Cancelled, d.Status.Phase);
        Assert.Contains(_spoken, s => s.StartsWith("Location data download cancelled."));
        Assert.True(File.Exists(PartPath));
        Assert.False(_store.IsAvailable);
    }

    [Fact(Timeout = 30_000)]
    public async Task App_shutdown_cancels_without_speaking()
    {
        _server.HoldZipBody = new TaskCompletionSource();
        using var d = Downloader();
        d.Start();
        await _server.ZipBodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        int before;
        lock (_spoken) before = _spoken.Count;
        d.Shutdown(TimeSpan.FromSeconds(5));
        await d.Job;
        Assert.Equal(before, _spoken.Count);
    }

    [Fact]
    public void The_idle_status_describes_what_will_happen()
    {
        using var d = Downloader();
        Assert.Equal(DownloadJobPhase.Idle, d.Status.Phase);
        Assert.StartsWith("Not downloaded. About ", d.Status.Text);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_dump_served_without_a_length_still_resumes_after_a_dropped_connection()
    {
        // A scanning proxy re-chunks the 200, so no Content-Length arrives; the line then drops.
        _server.DropFirstZipBodyWithoutLength = true;
        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.True(_store.IsAvailable);
        Assert.Equal(2, _server.ZipRequests.Count);
        Assert.NotNull(_server.ZipRequests[1].Range); // the tail, not the whole file again
        Assert.DoesNotContain(_spoken, s => s.Contains("could not be resumed"));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_complete_download_is_built_offline_from_the_files_already_on_disk()
    {
        // "Nothing is downloaded again": the name files and shapes of the earlier attempt are reused too.
        WriteCompleteDownload(FakeServer.Zip);
        File.WriteAllText(Path.Combine(_store.DownloadFolder, "countryInfo.txt"), GeographyFixtureWorld.CountryInfo);
        File.WriteAllText(Path.Combine(_store.DownloadFolder, "admin1CodesASCII.txt"), GeographyFixtureWorld.Admin1);
        foreach (var (layer, file) in NaturalEarthShapes.LayerFiles)
            File.WriteAllText(Path.Combine(_store.DownloadFolder, file + ".geojson"), GeographyFixtureWorld.Shapes[layer]);
        _server.Offline = true;
        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.True(_store.IsAvailable);
        Assert.Equal("Building location data from the earlier download.", _spoken[0]);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_build_failure_that_is_not_the_dumps_fault_keeps_the_download()
    {
        // A Natural Earth layer with no shapes fails the build, but the GeoNames dump is fine:
        // deleting it would cost the next press 400 MB for nothing.
        _server.Shapes = new Dictionary<NaturalEarthLayer, string>(GeographyFixtureWorld.Shapes)
        {
            [NaturalEarthLayer.Marine] = "{\"type\":\"FeatureCollection\",\"features\":[]}",
        };
        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.Equal(DownloadJobPhase.Failed, d.Status.Phase);
        Assert.True(File.Exists(Path.Combine(_store.DownloadFolder, "allCountries.zip")));
        Assert.StartsWith("Downloaded but not built yet.", d.IdleStatus().Text);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_damaged_zip_is_deleted_like_any_dump_the_build_rejected()
    {
        string zip = WriteCompleteDownload(FakeServer.Zip[..^20]); // its end-of-central-directory record is cut off
        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.Equal(DownloadJobPhase.Failed, d.Status.Phase);
        Assert.False(File.Exists(zip));
        Assert.False(File.Exists(zip + ".json"));
    }

    [Fact]
    public void A_whole_download_past_its_reuse_window_is_removed_at_startup()
    {
        string zip = WriteCompleteDownload(FakeServer.Zip);
        using var d = Downloader();
        d.SweepExpiredDownload();
        Assert.True(File.Exists(zip)); // within 3 days: kept for the next press

        File.SetLastWriteTimeUtc(zip, DateTime.UtcNow.AddDays(-4));
        d.SweepExpiredDownload();
        Assert.False(Directory.Exists(_store.DownloadFolder)); // never built from again: about 466 MB freed
    }

    [Fact]
    public void A_build_waiting_for_the_next_start_is_not_offered_as_a_fresh_download()
    {
        new LocationDataBuilder(_store).Build(GeographyFixtureWorld.WriteInputs(_dir), null, CancellationToken.None);
        File.Move(_store.DatabasePath, _store.ReadyPath); // the swap was put off: an antivirus held the file
        var store = new LocationDataStore(_store.DatabasePath);
        using var d = new LocationDataDownloader(store, _server, _server, FixtureHashes, _ => long.MaxValue);

        Assert.True(d.HasData); // "Update location data", never "Download"
        Assert.Equal(LocationDataStatusText.ReadyAtNextStart, d.IdleStatus().Text);
    }

    [Fact]
    public void A_network_share_skips_the_disk_space_check_instead_of_failing()
    {
        // A redirected %APPDATA% (\\server\share\...) has a root DriveInfo rejects.
        Assert.Equal(long.MaxValue, LocationDataDownloader.FreeBytes(@"\\server\share\"));
    }

    [Fact]
    public void A_timeout_is_a_server_that_did_not_answer_never_a_cancel()
    {
        Assert.Equal("the download server did not answer. Check the internet connection.",
            LocationDataDownloader.FailureReason(new TaskCanceledException("The operation was canceled.")));
    }

    [Fact]
    public void Network_and_disk_failures_are_told_apart()
    {
        const string network = "the connection to the download server was lost. Check the internet connection.";
        Assert.Equal(network, LocationDataDownloader.FailureReason(new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely.")));
        Assert.Equal(network, LocationDataDownloader.FailureReason(
            new IOException("Unable to read data from the transport connection.", new System.Net.Sockets.SocketException(10054))));
        Assert.True(LocationDataDownloader.IsRetryable(new HttpIOException(HttpRequestError.ResponseEnded)));

        var full = new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
        Assert.Equal("a file could not be written: There is not enough space on the disk.", LocationDataDownloader.FailureReason(full));
        Assert.False(LocationDataDownloader.IsRetryable(full)); // retrying the network never frees the disk
    }

    /// <summary>A whole dump with its validator, as a cancelled or failed build leaves it.</summary>
    private string WriteCompleteDownload(byte[] bytes)
    {
        Directory.CreateDirectory(_store.DownloadFolder);
        string zip = Path.Combine(_store.DownloadFolder, "allCountries.zip");
        File.WriteAllBytes(zip, bytes);
        File.WriteAllText(zip + ".json",
            $"{{\"Url\":\"{LocationDataDownloader.GeoNamesBaseUrl}allCountries.zip\",\"ETag\":\"\\\"dump-v1\\\"\",\"Total\":{bytes.Length}}}");
        return zip;
    }

    private void WritePartial(bool half, string etag)
    {
        Directory.CreateDirectory(_store.DownloadFolder);
        File.WriteAllBytes(PartPath, FakeServer.Zip.AsSpan(0, half ? FakeServer.Zip.Length / 2 : FakeServer.Zip.Length).ToArray());
        File.WriteAllText(PartPath + ".json",
            $"{{\"Url\":\"{LocationDataDownloader.GeoNamesBaseUrl}allCountries.zip\",\"ETag\":\"{etag.Replace("\"", "\\\"")}\",\"Total\":{FakeServer.Zip.Length}}}");
        // Natural Earth files from an earlier attempt are reused when their hash matches; the server still serves them anyway.
    }

    /// <summary>Serves the fixture world like download.geonames.org and GitHub raw would.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        internal static readonly byte[] Zip = GeographyFixtureWorld.DumpZip();
        internal const string ZipETag = "\"dump-v1\"";
        private static readonly DateTimeOffset ZipModified = new(2026, 10, 9, 1, 58, 39, TimeSpan.Zero);

        internal readonly List<HttpRequestHeaders> ZipRequests = new();
        internal TaskCompletionSource? HoldZipBody;
        internal readonly TaskCompletionSource ZipBodyStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>A misbehaving answer to a Range request: 206 with the tail from the wrong offset,
        /// or 416 with an error page. A request without a Range is served normally.</summary>
        internal HttpStatusCode? BadResumeAnswer;
        /// <summary>No network at all: every request fails as an unreachable host does.</summary>
        internal bool Offline;
        /// <summary>The first whole-file answer has no Content-Length (a re-chunking proxy) and its
        /// connection drops halfway.</summary>
        internal bool DropFirstZipBodyWithoutLength;
        internal IReadOnlyDictionary<NaturalEarthLayer, string> Shapes = GeographyFixtureWorld.Shapes;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("No such host is known.");
            string url = request.RequestUri!.ToString();
            if (url == LocationDataDownloader.GeoNamesBaseUrl + "countryInfo.txt") return Text(GeographyFixtureWorld.CountryInfo);
            if (url == LocationDataDownloader.GeoNamesBaseUrl + "admin1CodesASCII.txt") return Text(GeographyFixtureWorld.Admin1);
            foreach (var (layer, file) in NaturalEarthShapes.LayerFiles)
                if (url == LocationDataDownloader.NaturalEarthBaseUrl + file + ".geojson")
                    return Text(Shapes[layer]);
            if (url != LocationDataDownloader.GeoNamesBaseUrl + "allCountries.zip")
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            lock (ZipRequests) ZipRequests.Add(request.Headers);
            HttpResponseMessage response;
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            bool validatorMatches = request.Headers.IfRange?.EntityTag?.ToString() == ZipETag;
            if (range?.From is long bad && BadResumeAnswer is { } status)
            {
                await Task.Yield();
                if (status == HttpStatusCode.PartialContent)
                {
                    long wrong = bad + 7;
                    var tail = new HttpResponseMessage(status) { Content = new ByteArrayContent(Zip[(int)wrong..]) };
                    tail.Content.Headers.ContentRange = new ContentRangeHeaderValue(wrong, Zip.Length - 1, Zip.Length);
                    tail.Headers.ETag = new EntityTagHeaderValue(ZipETag);
                    return tail;
                }
                var page = new HttpResponseMessage(status) { Content = new StringContent("<html>Requested Range Not Satisfiable</html>") };
                page.Content.Headers.ContentRange = new ContentRangeHeaderValue(Zip.Length);
                return page;
            }
            if (range?.From is long from && validatorMatches)
            {
                response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Zip[(int)from..]) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, Zip.Length - 1, Zip.Length);
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK);
                if (DropFirstZipBodyWithoutLength)
                {
                    DropFirstZipBodyWithoutLength = false;
                    // A stream that cannot seek gives StreamContent no length to send.
                    response.Content = new StreamContent(new DroppingStream(Zip));
                    response.Headers.ETag = new EntityTagHeaderValue(ZipETag);
                    response.Content.Headers.LastModified = ZipModified;
                    await Task.Yield();
                    return response;
                }
                if (HoldZipBody is { } hold)
                {
                    // Send half, then wait (a stalled or slow line) until the test releases it.
                    // StreamContent hands the stream straight to the reader, as network content does.
                    response.Content = new StreamContent(new HeldStream(Zip, hold, ZipBodyStarted));
                }
                else response.Content = new ByteArrayContent(Zip);
            }
            response.Headers.ETag = new EntityTagHeaderValue(ZipETag);
            response.Content.Headers.LastModified = ZipModified;
            response.Content.Headers.ContentLength = response.StatusCode == HttpStatusCode.OK ? Zip.Length : Zip.Length - range!.From!.Value;
            await Task.Yield();
            return response;
        }

        private static HttpResponseMessage Text(string body) =>
            new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
    }

    /// <summary>Gives the first half of the bytes, then fails as a reset connection does.</summary>
    private sealed class DroppingStream(byte[] bytes) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            int half = bytes.Length / 2;
            if (_position >= half) throw new IOException("Unable to read data from the transport connection.");
            int n = Math.Min(count, half - _position);
            bytes.AsSpan(_position, n).CopyTo(buffer.AsSpan(offset, n));
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Gives the first half of the bytes, then blocks (honouring the reader's token) until released.</summary>
    private sealed class HeldStream(byte[] bytes, TaskCompletionSource hold, TaskCompletionSource started) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            int half = bytes.Length / 2;
            if (_position >= half && !hold.Task.IsCompleted)
            {
                started.TrySetResult();
                await hold.Task.WaitAsync(ct);
            }
            int limit = _position < half ? half : bytes.Length;
            int n = Math.Min(buffer.Length, limit - _position);
            bytes.AsMemory(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
