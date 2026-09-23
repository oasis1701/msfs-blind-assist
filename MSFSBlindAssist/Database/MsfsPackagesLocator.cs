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
///
/// Whether an <c>InstalledPackagesPathNextBoot</c> line counts is the CALLER's choice, passed down
/// explicitly with no default: the navdata database build keeps it
/// (<see cref="TryGetInstalledPackagesPath(string)"/>), the scenery census never takes it
/// (<see cref="TryGetCommunityPath(string, out bool)"/>) — see <see cref="ValueOnLine"/>.
/// </summary>
public static class MsfsPackagesLocator
{
    /// <summary>The first <c>InstalledPackagesPath "…"</c> value in the file, or null — NextBoot lines
    /// included, as the navdata build reads them. It pins how ONE value is parsed; the readers below
    /// walk <see cref="ParseInstalledPackagesPaths"/> and say which lines count.</summary>
    internal static string? ParseInstalledPackagesPath(IEnumerable<string> userCfgLines)
        => ParseInstalledPackagesPaths(userCfgLines, includeNextBoot: true).FirstOrDefault();

    /// <summary>Every <c>InstalledPackagesPath "…"</c> value the file names, in file order —
    /// what <see cref="TryReadUserCfg"/> walks, because it takes the first that is a folder on
    /// disk rather than the first that is written down. <paramref name="includeNextBoot"/>: whether
    /// an <c>InstalledPackagesPathNextBoot</c> line counts (see <see cref="ValueOnLine"/>).</summary>
    internal static IEnumerable<string> ParseInstalledPackagesPaths(IEnumerable<string> userCfgLines, bool includeNextBoot)
    {
        foreach (var line in userCfgLines)
        {
            string? value = ValueOnLine(line, includeNextBoot);
            if (value != null) yield return value;
        }
    }

    /// <summary>
    /// The quoted value on one line, or null when the line does not carry the key or carries it
    /// unquoted. Deliberately matched with IndexOf rather than a prefix test: that is what the
    /// method this replaced did, and it is also how the key is found on an indented line.
    ///
    /// <c>InstalledPackagesPathNextBoot</c> starts with the same text, and whether it counts is the
    /// caller's to say (<paramref name="includeNextBoot"/>). The simulator writes that key as soon as
    /// the pilot PICKS a new packages folder in-sim, and the folder normally exists already — so on
    /// the first-existing rule a NextBoot line above the active one wins. The navdata database build
    /// has always resolved its base path that way and keeps doing so (true). The scenery census reads
    /// while the simulator RUNS, and a folder the running simulator is not loading is the wrong one
    /// to scan, so it takes the active key only (false, review SI-5) — the rule
    /// <see cref="Services.AircraftCfgCatalog"/>, <see cref="Services.Gsx.GsxAirplaneProfile"/> and
    /// <see cref="Patching.EFBModPackageManager"/> already apply.
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

    /// <summary>The packages root for "FS2020" or "FS2024", or null when nothing names one that exists
    /// — the NAVDATA BUILD's question, so an <c>InstalledPackagesPathNextBoot</c> line counts, as it
    /// always has there (see <see cref="ValueOnLine"/>).</summary>
    public static string? TryGetInstalledPackagesPath(string simulatorVersion)
        => TryGetInstalledPackagesPath(simulatorVersion, RoamingAppData(), LocalAppData(), includeNextBoot: true, out _);

    /// <summary>Test seam: the two roots Windows would otherwise supply. The navdata build's reading,
    /// NextBoot included, like the overload above.</summary>
    internal static string? TryGetInstalledPackagesPath(string simulatorVersion, string roamingAppData, string localAppData)
        => TryGetInstalledPackagesPath(simulatorVersion, roamingAppData, localAppData, includeNextBoot: true, out _);

    /// <summary>
    /// The packages root, as above — and <paramref name="readFailed"/> true when a UserCfg.opt that
    /// EXISTS could not be READ, or the lookup itself threw. That is not the same fact as "nothing
    /// names a packages root": the answer may be missing (or be the Store copy's instead of the one
    /// that would have won) for a reason that has nothing to do with the install, and the
    /// surroundings catalog must not mistake it for "no Community folder" (review item SI-2). A
    /// config that is absent, names no path, or names a folder that is not on disk is NOT a
    /// failure. It still reads on past a failed read, exactly as before: the navdata build has
    /// always taken the next location's answer. <paramref name="includeNextBoot"/>: whether an
    /// <c>InstalledPackagesPathNextBoot</c> line counts — true for the navdata build, false for the
    /// scenery census (see <see cref="ValueOnLine"/>, review SI-5). Every caller states it; there is
    /// no default.
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

    /// <summary>The Community folder under the packages root the RUNNING simulator loads, or null
    /// when there is none — with <paramref name="readFailed"/> true when that answer rests on a
    /// UserCfg.opt that could not be read (see the five-argument TryGetInstalledPackagesPath). "Could
    /// not read the config" is not "there is no Community folder", and the surroundings catalog
    /// degrades on the first. The scenery census asks this while the simulator runs, so an
    /// <c>InstalledPackagesPathNextBoot</c> line NEVER counts here (review SI-5) — and deliberately
    /// there is no parameter to make it count: no caller can take the next boot's folder by
    /// accident.</summary>
    public static string? TryGetCommunityPath(string simulatorVersion, out bool readFailed)
        => Community(TryGetInstalledPackagesPath(simulatorVersion, RoamingAppData(), LocalAppData(), includeNextBoot: false, out readFailed));

    /// <summary>Test seam: the two roots Windows would otherwise supply. The active key only, like
    /// the overload above.</summary>
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

    /// <summary>
    /// The first packages root this config names that is really on disk. It keeps reading past a
    /// value that is not a folder, exactly as the method this replaced did: the file can carry
    /// more than one key-shaped line (see <see cref="ValueOnLine"/>), and
    /// <paramref name="includeNextBoot"/> says whether an <c>InstalledPackagesPathNextBoot</c> one is
    /// among them. <paramref name="readFailed"/> is true only when the file EXISTS and reading it
    /// threw.
    /// </summary>
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

            // Shared for write and delete, and released before the first Directory.Exists: this is
            // the SIMULATOR's own config, and since the scenery census it is read while the
            // simulator is running (on the navdata-build path it never was). A reader that permits
            // no writer can make the simulator's own write to it fail.
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
            // It EXISTS (File.Exists said so) and could not be read — the simulator holding its own
            // config exclusively for a moment, an access error. Not "no packages root": say so, and
            // Warn, because it now degrades the surroundings catalog and this line is its only trace.
            readFailed = true;
            Log.Warn("Database", $"Could not read UserCfg.opt at {configPath}: {ex.Message}");
            return null;
        }
    }
}
