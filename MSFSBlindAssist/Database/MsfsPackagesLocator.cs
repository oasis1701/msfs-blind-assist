using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Where the simulator keeps its installed packages, read from UserCfg.opt's
/// <c>InstalledPackagesPath</c>. Four locations, two per simulator: the Roaming config
/// (<c>Microsoft Flight Simulator</c> / <c>Microsoft Flight Simulator 2024</c>) and the Store
/// build's LocalCache (<c>Microsoft.FlightSimulator_8wekyb3d8bbwe</c> for FS2020,
/// <c>Microsoft.Limitless_8wekyb3d8bbwe</c> for FS2024 — NOT "FlightSimulator2024").
///
/// This was <c>NavdataReaderBuilder.GetMSFSBasePath</c>/<c>TryParseUserCfgForBasePath</c>, moved
/// here unchanged so the scenery census can ask the same question without reaching into the
/// database builder. The only deliberate difference is that the quoted value now ends at its OWN
/// closing quote rather than at the line's last one, so a trailing comment carrying quotes is not
/// swallowed into the path.
/// </summary>
public static class MsfsPackagesLocator
{
    /// <summary>The first <c>InstalledPackagesPath "…"</c> value in the file, or null.</summary>
    internal static string? ParseInstalledPackagesPath(IEnumerable<string> userCfgLines)
    {
        foreach (var line in userCfgLines)
        {
            string? value = ValueOnLine(line);
            if (value != null) return value;
        }
        return null;
    }

    /// <summary>
    /// The quoted value on one line, or null when the line does not carry the key or carries it
    /// unquoted. Deliberately matched with IndexOf rather than a prefix test: that is what the
    /// method this replaced did, and it is also how the key is found on an indented line.
    /// NOTE that it therefore also matches <c>InstalledPackagesPathNextBoot</c> — a relocation
    /// that has not happened yet — which <see cref="Services.AircraftCfgCatalog"/> and
    /// <see cref="Services.Gsx.GsxAirplaneProfile"/> both exclude. It stays matched here because
    /// the caller below skips any value that is not a folder on disk and keeps reading, and a
    /// NextBoot path that has not been moved to yet does not exist.
    /// </summary>
    private static string? ValueOnLine(string line)
    {
        int key = line.IndexOf("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase);
        if (key < 0) return null;
        int open = line.IndexOf('"', key);
        int close = open < 0 ? -1 : line.IndexOf('"', open + 1);
        return close > open + 1 ? line.Substring(open + 1, close - open - 1) : null;
    }

    /// <summary>The packages root for "FS2020" or "FS2024", or null when nothing names one that exists.</summary>
    public static string? TryGetInstalledPackagesPath(string simulatorVersion)
        => TryGetInstalledPackagesPath(simulatorVersion,
                                       Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                       Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Test seam: the two roots Windows would otherwise supply.</summary>
    internal static string? TryGetInstalledPackagesPath(string simulatorVersion, string roamingAppData, string localAppData)
    {
        try
        {
            string configFileName = simulatorVersion == "FS2024"
                ? "Microsoft Flight Simulator 2024"
                : "Microsoft Flight Simulator";

            // Check AppData\Roaming location first
            string? basePath = TryReadUserCfg(Path.Combine(roamingAppData, configFileName, "UserCfg.opt"));
            if (basePath != null)
            {
                Log.Debug("Database", $"Found {simulatorVersion} base path from UserCfg.opt: {basePath}");
                return basePath;
            }

            // The Store build keeps its own copy under LocalCache. Matched by == against each known
            // version, so an unrecognised version string reads the Roaming location above and
            // nothing else — the shape the method this replaced had.
            string? storePackage = simulatorVersion switch
            {
                "FS2020" => "Microsoft.FlightSimulator_8wekyb3d8bbwe",
                "FS2024" => "Microsoft.Limitless_8wekyb3d8bbwe",
                _ => null,
            };
            if (storePackage != null)
            {
                basePath = TryReadUserCfg(Path.Combine(localAppData, "Packages", storePackage, "LocalCache", "UserCfg.opt"));
                if (basePath != null)
                {
                    Log.Debug("Database", $"Found {simulatorVersion} base path from Store UserCfg.opt: {basePath}");
                    return basePath;
                }
            }

            Log.Debug("Database", $"Could not find base path for {simulatorVersion}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("Database", $"Error getting MSFS base path: {ex.Message}");
            return null;
        }
    }

    /// <summary>The Community folder under the packages root, or null when there is none.</summary>
    public static string? TryGetCommunityPath(string simulatorVersion)
        => Community(TryGetInstalledPackagesPath(simulatorVersion));

    /// <summary>Test seam: the two roots Windows would otherwise supply.</summary>
    internal static string? TryGetCommunityPath(string simulatorVersion, string roamingAppData, string localAppData)
        => Community(TryGetInstalledPackagesPath(simulatorVersion, roamingAppData, localAppData));

    private static string? Community(string? root)
    {
        if (root == null) return null;
        string community = Path.Combine(root, "Community");
        return Directory.Exists(community) ? community : null;
    }

    /// <summary>
    /// The first packages root this config names that is really on disk. It keeps reading past a
    /// value that is not a folder, exactly as the method this replaced did: the file can carry
    /// more than one key-shaped line (see <see cref="ValueOnLine"/>).
    /// </summary>
    private static string? TryReadUserCfg(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                Log.Debug("Database", $"UserCfg.opt not found at: {configPath}");
                return null;
            }

            foreach (string line in File.ReadLines(configPath))
            {
                string? path = ValueOnLine(line);
                if (path == null) continue;
                if (Directory.Exists(path)) return path;
                Log.Debug("Database", $"InstalledPackagesPath found but directory doesn't exist: {path}");
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("Database", $"Error parsing UserCfg.opt: {ex.Message}");
            return null;
        }
    }
}
