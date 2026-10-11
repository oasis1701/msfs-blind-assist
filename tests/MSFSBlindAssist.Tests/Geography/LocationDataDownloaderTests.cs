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

    private static readonly IReadOnlyDictionary<NaturalEarthLayer, string> FixtureHashes =
        GeographyFixtureWorld.Shapes.ToDictionary(kv => kv.Key,
            kv => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(kv.Value))));

    private LocationDataDownloader Downloader(long freeBytes = long.MaxValue)
    {
        var d = new LocationDataDownloader(_store, _server, _server, FixtureHashes, _ => freeBytes)
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
    public async Task A_complete_download_is_kept_for_reuse_when_the_build_fails()
    {
        using var d = new LocationDataDownloader(_store, _server, _server, FixtureHashes, _ => long.MaxValue)
        {
            MinimumPlaces = 1,
            MinimumRows = 1_000, // the fixture dump is too short: the build fails after the download
        };
        d.Start();
        await d.Job;

        Assert.Equal(DownloadJobPhase.Failed, d.Status.Phase);
        Assert.False(_store.IsAvailable);
        Assert.Contains("Downloaded but not built yet.", d.Status.Text);
        Assert.True(File.Exists(Path.Combine(_store.DownloadFolder, "allCountries.zip")));
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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string url = request.RequestUri!.ToString();
            if (url == LocationDataDownloader.GeoNamesBaseUrl + "countryInfo.txt") return Text(GeographyFixtureWorld.CountryInfo);
            if (url == LocationDataDownloader.GeoNamesBaseUrl + "admin1CodesASCII.txt") return Text(GeographyFixtureWorld.Admin1);
            foreach (var (layer, file) in NaturalEarthShapes.LayerFiles)
                if (url == LocationDataDownloader.NaturalEarthBaseUrl + file + ".geojson")
                    return Text(GeographyFixtureWorld.Shapes[layer]);
            if (url != LocationDataDownloader.GeoNamesBaseUrl + "allCountries.zip")
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            lock (ZipRequests) ZipRequests.Add(request.Headers);
            HttpResponseMessage response;
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            bool validatorMatches = request.Headers.IfRange?.EntityTag?.ToString() == ZipETag;
            if (range?.From is long from && validatorMatches)
            {
                response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Zip[(int)from..]) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, Zip.Length - 1, Zip.Length);
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK);
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
