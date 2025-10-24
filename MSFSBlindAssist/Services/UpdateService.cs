using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;
public class UpdateService
{
    private const string GITHUB_API_URL = "https://api.github.com/repos/oasis1701/msfs-blind-assist/releases/latest";
    private readonly HttpClient httpClient;

    public event EventHandler<UpdateProgressEventArgs>? ProgressChanged;
    public event EventHandler<string>? StatusChanged;

    public UpdateService()
    {
        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "MSFSBlindAssist-Updater");
    }

    /// <summary>
    /// Gets the current application version from assembly
    /// </summary>
    public Version? GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version;
    }

    /// <summary>
    /// Checks GitHub for the latest release and compares with current version
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        try
        {
            StatusChanged?.Invoke(this, "Checking for updates...");

            var response = await httpClient.GetStringAsync(GITHUB_API_URL);
            var releaseData = JObject.Parse(response);

            string? tagName = releaseData["tag_name"]?.ToString();
            string? releaseName = releaseData["name"]?.ToString();
            string? releaseNotes = releaseData["body"]?.ToString();

            if (string.IsNullOrEmpty(tagName))
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    ErrorMessage = "Could not parse release information from GitHub"
                };
            }

            // Parse version from tag (handles formats like "v0.2.2-alpha" or "0.2.2")
            Version remoteVersion = ParseVersion(tagName);
            Version? currentVersion = GetCurrentVersion();

            bool isUpdateAvailable = currentVersion != null && remoteVersion > currentVersion;

            // Only check for download URL if an update is actually available
            string? downloadUrl = null;
            if (isUpdateAvailable)
            {
                // Safely find the ZIP asset in the release
                var assetResult = FindZipAsset(releaseData);
                if (!assetResult.Success)
                {
                    return new UpdateCheckResult
                    {
                        IsUpdateAvailable = false,
                        ErrorMessage = assetResult.ErrorMessage
                    };
                }

                downloadUrl = assetResult.DownloadUrl;
            }

            return new UpdateCheckResult
            {
                IsUpdateAvailable = isUpdateAvailable,
                CurrentVersion = currentVersion,
                LatestVersion = remoteVersion,
                DownloadUrl = downloadUrl,
                ReleaseName = releaseName,
                ReleaseNotes = releaseNotes,
                TagName = tagName
            };
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                ErrorMessage = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                ErrorMessage = $"Error checking for updates: {ex.Message}"
            };
        }
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
            // In .NET 9, Assembly.Location returns the DLL path, which cannot be launched
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

    /// <summary>
    /// Safely finds and validates the ZIP file asset in a GitHub release
    /// </summary>
    private AssetSearchResult FindZipAsset(JObject releaseData)
    {
        try
        {
            // Check if assets property exists
            var assetsToken = releaseData["assets"];
            if (assetsToken == null)
            {
                return new AssetSearchResult
                {
                    Success = false,
                    ErrorMessage = "Release data does not contain an 'assets' field. Please contact the developer."
                };
            }

            // Check if assets is a valid array
            if (assetsToken is not JArray assetsArray)
            {
                return new AssetSearchResult
                {
                    Success = false,
                    ErrorMessage = "Release assets data is malformed. Please contact the developer."
                };
            }

            // Check if assets array is empty
            if (assetsArray.Count == 0)
            {
                return new AssetSearchResult
                {
                    Success = false,
                    ErrorMessage = "Update is available, but no download file is attached to the GitHub release. Please contact the developer."
                };
            }

            // Search for ZIP file in assets
            foreach (var asset in assetsArray)
            {
                string? fileName = asset["name"]?.ToString();
                string? downloadUrl = asset["browser_download_url"]?.ToString();

                if (!string.IsNullOrEmpty(fileName) &&
                    !string.IsNullOrEmpty(downloadUrl) &&
                    fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return new AssetSearchResult
                    {
                        Success = true,
                        DownloadUrl = downloadUrl
                    };
                }
            }

            // No ZIP file found in assets
            return new AssetSearchResult
            {
                Success = false,
                ErrorMessage = "Update is available, but no ZIP file was found in the release assets. Please contact the developer."
            };
        }
        catch (Exception ex)
        {
            return new AssetSearchResult
            {
                Success = false,
                ErrorMessage = $"Error parsing release assets: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Parses version from GitHub tag (handles v0.2.2-alpha, 0.2.2, etc.)
    /// </summary>
    private Version ParseVersion(string? tagName)
    {
        if (string.IsNullOrEmpty(tagName))
            return new Version(0, 0, 0, 0);

        // Remove 'v' prefix if present
        string versionString = tagName.TrimStart('v', 'V');

        // Remove any pre-release suffixes (-alpha, -beta, etc.)
        Match match = Regex.Match(versionString, @"^(\d+\.\d+\.\d+)");
        if (match.Success)
        {
            versionString = match.Groups[1].Value;
        }

        // Parse as Version (will add .0 if needed)
        if (Version.TryParse(versionString, out Version? version))
        {
            return version;
        }

        // Fallback: return 0.0.0.0 if parsing fails
        return new Version(0, 0, 0, 0);
    }
}

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; set; }
    public Version? CurrentVersion { get; set; }
    public Version? LatestVersion { get; set; }
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

/// <summary>
/// Result of searching for a ZIP asset in a GitHub release
/// </summary>
internal class AssetSearchResult
{
    public bool Success { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ErrorMessage { get; set; }
}
