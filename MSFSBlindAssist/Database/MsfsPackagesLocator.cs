using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Where the simulator keeps its installed packages, read from UserCfg.opt's
/// <c>InstalledPackagesPath</c>: the Roaming config first, then the Store build's LocalCache
/// (<c>Microsoft.FlightSimulator_8wekyb3d8bbwe</c> for FS2020, <c>Microsoft.Limitless_8wekyb3d8bbwe</c>
/// for FS2024). Moved here from NavdataReaderBuilder so the scenery census can share it. Whether an
/// <c>InstalledPackagesPathNextBoot</c> line counts is the caller's explicit choice (<see cref="ValueOnLine"/>).
/// </summary>
public static class MsfsPackagesLocator
{
    /// <summary>The first <c>InstalledPackagesPath "…"</c> value, NextBoot lines included; pins how one
    /// value is parsed.</summary>
    internal static string? ParseInstalledPackagesPath(IEnumerable<string> userCfgLines)
        => ParseInstalledPackagesPaths(userCfgLines, includeNextBoot: true).FirstOrDefault();

    /// <summary>Every <c>InstalledPackagesPath "…"</c> value, in file order; the reader takes the first
    /// that exists on disk.</summary>
    internal static IEnumerable<string> ParseInstalledPackagesPaths(IEnumerable<string> userCfgLines, bool includeNextBoot)
    {
        foreach (var line in userCfgLines)
        {
            string? value = ValueOnLine(line, includeNextBoot);
            if (value != null) yield return value;
        }
    }

    /// <summary>
    /// The quoted value on one line (up to its own closing quote), or null. The simulator writes the
    /// NextBoot key as soon as the pilot picks a new folder in-sim, so the navdata build (which always
    /// has) counts it, while the census — reading while the simulator runs — takes the active key only,
    /// as AircraftCfgCatalog, GsxAirplaneProfile and EFBModPackageManager do.
    /// </summary>
    private static string? ValueOnLine(string line, bool includeNextBoot)
    {
        int key = line.IndexOf("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase);
        if (key < 0) return null;
        if (!includeNextBoot && line.AsSpan(key).StartsWith("InstalledPackagesPathNextBoot", StringComparison.OrdinalIgnoreCase)) return null;
        int open = line.IndexOf('"', key);
        int close = open < 0 ? -1 : line.IndexOf('"', open + 1);
        return close > open + 1 ? line.Substring(open + 1, close - open - 1) : null;
    }

    /// <summary>The packages root for "FS2020" or "FS2024", or null — the navdata build's reading,
    /// NextBoot included.</summary>
    public static string? TryGetInstalledPackagesPath(string simulatorVersion)
        => TryGetInstalledPackagesPath(simulatorVersion, RoamingAppData(), LocalAppData(), includeNextBoot: true, out _);

    /// <summary>Test seam: the two roots Windows would otherwise supply.</summary>
    internal static string? TryGetInstalledPackagesPath(string simulatorVersion, string roamingAppData, string localAppData)
        => TryGetInstalledPackagesPath(simulatorVersion, roamingAppData, localAppData, includeNextBoot: true, out _);

    /// <summary>
    /// The packages root, with <paramref name="readFailed"/> true when a UserCfg.opt that exists could
    /// not be read (or the lookup threw) — not the same as "no packages root", which the surroundings
    /// catalog must not mistake it for. It still reads on past a failed read, as the navdata build
    /// always has. Every caller states <paramref name="includeNextBoot"/>; there is no default.
    /// </summary>
    internal static string? TryGetInstalledPackagesPath(string simulatorVersion, string roamingAppData, string localAppData,
                                                        bool includeNextBoot, out bool readFailed)
    {
        readFailed = false;
        try
        {
            string configFileName = simulatorVersion == "FS2024"
                ? "Microsoft Flight Simulator 2024"
                : "Microsoft Flight Simulator";

            // Check AppData\Roaming location first
            string? basePath = TryReadUserCfg(Path.Combine(roamingAppData, configFileName, "UserCfg.opt"), includeNextBoot, out bool roamingFailed);
            readFailed |= roamingFailed;
            if (basePath != null)
            {
                Log.Debug("Database", $"Found {simulatorVersion} base path from UserCfg.opt: {basePath}");
                return basePath;
            }

            // The Store build's own copy; an unrecognised version reads only the Roaming location.
            string? storePackage = simulatorVersion switch
            {
                "FS2020" => "Microsoft.FlightSimulator_8wekyb3d8bbwe",
                "FS2024" => "Microsoft.Limitless_8wekyb3d8bbwe",
                _ => null,
            };
            if (storePackage != null)
            {
                basePath = TryReadUserCfg(Path.Combine(localAppData, "Packages", storePackage, "LocalCache", "UserCfg.opt"), includeNextBoot, out bool storeFailed);
                readFailed |= storeFailed;
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
            readFailed = true;
            return null;
        }
    }

    /// <summary>The Community folder the RUNNING simulator loads, or null; <paramref name="readFailed"/>
    /// as above. The NextBoot line never counts here, and there is no parameter to make it.</summary>
    public static string? TryGetCommunityPath(string simulatorVersion, out bool readFailed)
        => Community(TryGetInstalledPackagesPath(simulatorVersion, RoamingAppData(), LocalAppData(), includeNextBoot: false, out readFailed));

    /// <summary>Test seam: the two roots Windows would otherwise supply.</summary>
    internal static string? TryGetCommunityPath(string simulatorVersion, string roamingAppData, string localAppData, out bool readFailed)
        => Community(TryGetInstalledPackagesPath(simulatorVersion, roamingAppData, localAppData, includeNextBoot: false, out readFailed));

    private static string RoamingAppData() => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string LocalAppData() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string? Community(string? root)
    {
        if (root == null) return null;
        string community = Path.Combine(root, "Community");
        return Directory.Exists(community) ? community : null;
    }

    /// <summary>The first packages root this config names that exists on disk. <paramref name="readFailed"/>
    /// is true only when the file exists and reading it threw.</summary>
    private static string? TryReadUserCfg(string configPath, bool includeNextBoot, out bool readFailed)
    {
        readFailed = false;
        try
        {
            if (!File.Exists(configPath))
            {
                Log.Debug("Database", $"UserCfg.opt not found at: {configPath}");
                return null;
            }

            // Shared for write and delete, released before the first Directory.Exists: the census reads
            // the simulator's own config while it runs, and must never make its write fail.
            var lines = new List<string>();
            using (var reader = new StreamReader(new FileStream(configPath, FileMode.Open, FileAccess.Read,
                                                                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan)))
            {
                string? line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
            }

            foreach (string path in ParseInstalledPackagesPaths(lines, includeNextBoot))
            {
                if (Directory.Exists(path)) return path;
                Log.Debug("Database", $"InstalledPackagesPath found but directory doesn't exist: {path}");
            }
            return null;
        }
        catch (Exception ex)
        {
            // Exists but unreadable: not "no packages root". Warn — it degrades the catalog.
            readFailed = true;
            Log.Warn("Database", $"Could not read UserCfg.opt at {configPath}: {ex.Message}");
            return null;
        }
    }
}
