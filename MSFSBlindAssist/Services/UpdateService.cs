using System.Net.Http;
using MSFSBlindAssist.Settings;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;
public class UpdateService
{
    // The LIST endpoint, not /releases/latest. /latest by definition never returns a
    // pre-release, so it cannot serve the preview channel; the list returns both kinds in
    // one call and the selector decides. 30 is GitHub's default page size and far more
    // than this repository will ever need — there is exactly one rolling preview.
    private const string GITHUB_API_URL =
        "https://api.github.com/repos/oasis1701/msfs-blind-assist/releases?per_page=30";
    private readonly HttpClient httpClient;

    public event EventHandler<UpdateProgressEventArgs>? ProgressChanged;
    public event EventHandler<string>? StatusChanged;

    public UpdateService()
    {
        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "MSFSBlindAssist-Updater");
    }

    /// <summary>
    /// The running version, read from AssemblyInformationalVersion via <see cref="AppVersion"/>.
    /// NOT AssemblyVersion: that cannot carry a pre-release identifier, so 8.0.1-pre.42 and
    /// 8.0.1-pre.7 would be indistinguishable and the preview channel could never work.
    /// </summary>
    public SemanticVersion? GetCurrentVersion() => AppVersion.Current;

    /// <summary>
    /// Fetches the release list and asks <see cref="UpdateCandidateSelector"/> which one to
    /// offer for the given channel.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(UpdateChannel channel)
    {
        var currentVersion = GetCurrentVersion();

        try
        {
            StatusChanged?.Invoke(this, "Checking for updates...");

            using var response = await httpClient.GetAsync(GITHUB_API_URL);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    CurrentVersion = currentVersion,
                    ErrorMessage = DescribeHttpFailure(response)
                };
            }

            var body = await response.Content.ReadAsStringAsync();
            var releases = JArray.Parse(body);

            var candidates = releases.Select(ToCandidate).Where(c => c is not null).Select(c => c!).ToList();
            var selection = UpdateCandidateSelector.Select(candidates, channel, currentVersion);

            if (selection.Release is null)
            {
                return new UpdateCheckResult
                {
                    Verdict = UpdateVerdict.NoCandidate,
                    CurrentVersion = currentVersion
                };
            }

            // Only an offer needs a download; being up to date needs no asset at all.
            if (selection.Verdict is UpdateVerdict.UpdateAvailable or UpdateVerdict.DowngradeAvailable
                && string.IsNullOrEmpty(selection.Release.ZipDownloadUrl))
            {
                return new UpdateCheckResult
                {
                    CurrentVersion = currentVersion,
                    ErrorMessage =
                        $"Version {selection.Version} is available, but no ZIP file is attached to " +
                        "that GitHub release. Please contact the developer."
                };
            }

            return new UpdateCheckResult
            {
                Verdict = selection.Verdict,
                CurrentVersion = currentVersion,
                LatestVersion = selection.Version,
                DownloadUrl = selection.Release.ZipDownloadUrl,
                ReleaseName = selection.Release.Name,
                ReleaseNotes = selection.Release.Body,
                TagName = selection.Release.TagName
            };
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = currentVersion,
                ErrorMessage = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = currentVersion,
                ErrorMessage = $"Error checking for updates: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Turns a failed response into something a pilot can act on. The rate limit is the one
    /// worth naming: unauthenticated GitHub API access is 60 requests per hour per IP, so a
    /// shared address can hit it even though this app makes one call per check.
    /// </summary>
    private static string DescribeHttpFailure(HttpResponseMessage response)
    {
        var rateLimited =
            response.StatusCode == System.Net.HttpStatusCode.Forbidden &&
            response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0";

        if (rateLimited)
        {
            return "GitHub's hourly request limit was reached. Please try again later.";
        }

        return $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.";
    }

    /// <summary>Flattens one release from the API JSON. Returns null if it has no tag name.</summary>
    private static ReleaseCandidate? ToCandidate(JToken release)
    {
        var tagName = release["tag_name"]?.ToString();
        if (string.IsNullOrEmpty(tagName)) return null;

        return new ReleaseCandidate(
            TagName: tagName,
            Name: release["name"]?.ToString(),
            Body: release["body"]?.ToString(),
            IsPrerelease: release["prerelease"]?.Value<bool?>() ?? false,
            IsDraft: release["draft"]?.Value<bool?>() ?? false,
            ZipDownloadUrl: FindZipAssetUrl(release));
    }

    /// <summary>
    /// The first .zip asset on a release, or null. Each release carries exactly one — the
    /// full release ships MSFSBA.zip and the rolling preview ships MSFSBA-preview.zip.
    /// </summary>
    private static string? FindZipAssetUrl(JToken release)
    {
        if (release["assets"] is not JArray assets) return null;

        foreach (var asset in assets)
        {
            var fileName = asset["name"]?.ToString();
            var downloadUrl = asset["browser_download_url"]?.ToString();

            if (!string.IsNullOrEmpty(fileName) &&
                !string.IsNullOrEmpty(downloadUrl) &&
                fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return downloadUrl;
            }
        }

        return null;
    }

    /// <summary>
    /// Downloads the update ZIP file to a temporary location
    /// </summary>
    public async Task<string> DownloadUpdateAsync(string downloadUrl)
    {
        try
        {
            StatusChanged?.Invoke(this, "Downloading update...");

            string tempPath = Path.Combine(Path.GetTempPath(), "MSFSBlindAssist_Update.zip");

            using (var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (totalBytes.HasValue)
                        {
                            int percentComplete = (int)((totalRead * 100) / totalBytes.Value);
                            ProgressChanged?.Invoke(this, new UpdateProgressEventArgs
                            {
                                PercentComplete = percentComplete,
                                BytesDownloaded = totalRead,
                                TotalBytes = totalBytes.Value
                            });
                        }
                    }
                }
            }

            StatusChanged?.Invoke(this, "Download complete");
            return tempPath;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Download failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Launches the updater executable to replace files and restart the application
    /// </summary>
    public void LaunchUpdater(string zipPath)
    {
        try
        {
            string updaterPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MSFSBlindAssistUpdater.exe");

            if (!File.Exists(updaterPath))
            {
                throw new FileNotFoundException("Updater executable not found", updaterPath);
            }

            // Use Environment.ProcessPath to get the actual executable path (not DLL path)
            // On modern .NET, Assembly.Location returns the DLL path, which cannot be launched
            string appPath = Environment.ProcessPath ?? throw new InvalidOperationException("Could not determine application executable path");
            // Trim trailing backslashes to prevent the \" escaping bug in Windows command-line parsing
            // BaseDirectory always ends with a backslash, which would escape the closing quote
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

            // Arguments: <zipPath> <appDirectory> <appExecutablePath>
            // Quote each argument to handle paths with spaces
            string arguments = $"\"{zipPath}\" \"{appDirectory}\" \"{appPath}\"";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = arguments,
                UseShellExecute = true  // Use shell execute for reliable argument passing
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to launch updater: {ex.Message}", ex);
        }
    }
}

public class UpdateCheckResult
{
    public UpdateVerdict Verdict { get; set; } = UpdateVerdict.NoCandidate;

    /// <summary>True for BOTH a newer version and an offered downgrade — either way there is something to install.</summary>
    public bool IsUpdateAvailable =>
        Verdict is UpdateVerdict.UpdateAvailable or UpdateVerdict.DowngradeAvailable;

    /// <summary>The offered version is OLDER than the one running (preview user returning to the release channel).</summary>
    public bool IsDowngrade => Verdict == UpdateVerdict.DowngradeAvailable;

    public SemanticVersion? CurrentVersion { get; set; }
    public SemanticVersion? LatestVersion { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ReleaseName { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? TagName { get; set; }
    public string? ErrorMessage { get; set; }
}

public class UpdateProgressEventArgs : EventArgs
{
    public int PercentComplete { get; set; }
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
}
