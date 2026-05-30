
namespace MSFSBlindAssist.Database;
/// <summary>
/// Resolves database file paths for navdatareader-generated databases.
///
/// Two locations exist for historical reasons:
///   - **Canonical** (new): %APPDATA%\MSFSBlindAssist\databases\
///   - **Legacy**:          %APPDATA%\FBWBA\databases\
///
/// The app was renamed from FBWBA → MSFSBlindAssist; some users still have
/// their built database in the legacy folder. New builds always go to the
/// canonical location; reads check the canonical location first and fall
/// back to the legacy one. Without this fallback, an existing legacy DB
/// silently disappears for the user — the EFB airport lookup, landing-exit
/// planner, and `DatabaseSelector` all reported "not found" even though the
/// file was sitting in the FBWBA folder.
/// </summary>
public static class DatabasePathResolver
{
    /// <summary>
    /// Canonical (new) folder name. New builds always write here.
    /// </summary>
    public const string CanonicalFolderName = "MSFSBlindAssist";

    /// <summary>
    /// Legacy folder name from the FBWBA era. Reads fall back here when the
    /// canonical location is empty.
    /// </summary>
    public const string LegacyFolderName = "FBWBA";

    /// <summary>
    /// Gets the canonical (new) database path for a simulator version.
    /// Always returns the MSFSBlindAssist\databases path — does NOT check
    /// the legacy FBWBA location. Use this for build targets and any place
    /// that should write to the canonical location.
    /// </summary>
    public static string GetCanonicalDatabasePath(string simulatorVersion)
    {
        return BuildPath(CanonicalFolderName, simulatorVersion);
    }

    /// <summary>
    /// Gets the legacy (FBWBA) database path for a simulator version.
    /// </summary>
    public static string GetLegacyDatabasePath(string simulatorVersion)
    {
        return BuildPath(LegacyFolderName, simulatorVersion);
    }

    /// <summary>
    /// Resolves the path to an *existing* database file.
    ///
    /// Checks the canonical location first; if missing there, falls back to
    /// the legacy FBWBA location. If the database does not exist at either
    /// location, returns the canonical path (so error messages show the
    /// location the user *should* have).
    ///
    /// Use this for every read/connect path. Use <see cref="GetCanonicalDatabasePath"/>
    /// for write/build targets.
    /// </summary>
    public static string ResolveExistingDatabasePath(string simulatorVersion)
    {
        string canonical = GetCanonicalDatabasePath(simulatorVersion);
        if (File.Exists(canonical))
            return canonical;

        string legacy = GetLegacyDatabasePath(simulatorVersion);
        if (File.Exists(legacy))
            return legacy;

        // Neither exists — return canonical so error messages reference the
        // location the user should build into.
        return canonical;
    }

    /// <summary>
    /// True if a database exists at *either* the canonical or legacy location.
    /// </summary>
    public static bool ExistsAnywhere(string simulatorVersion)
    {
        return File.Exists(GetCanonicalDatabasePath(simulatorVersion)) ||
               File.Exists(GetLegacyDatabasePath(simulatorVersion));
    }

    /// <summary>
    /// Gets the path for navdatareader-generated database (FS2020).
    /// Returns the existing file location (canonical preferred, legacy fallback).
    /// </summary>
    public static string GetNavdataReaderFS2020Path()
    {
        return ResolveExistingDatabasePath("FS2020");
    }

    /// <summary>
    /// Gets the path for navdatareader-generated database (FS2024).
    /// Returns the existing file location (canonical preferred, legacy fallback).
    /// </summary>
    public static string GetNavdataReaderFS2024Path()
    {
        return ResolveExistingDatabasePath("FS2024");
    }

    /// <summary>
    /// Checks if a database file exists at the specified path.
    /// </summary>
    public static bool DatabaseExists(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    /// <summary>
    /// Gets a user-friendly display name for a database path (just the filename).
    /// </summary>
    public static string GetDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "(not configured)";

        return Path.GetFileName(path);
    }

    private static string BuildPath(string folderName, string simulatorVersion)
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string databaseFolder = Path.Combine(appDataPath, folderName, "databases");

        string filename = simulatorVersion?.ToUpper() == "FS2024"
            ? "fs2024.sqlite"
            : "fs2020.sqlite";

        return Path.Combine(databaseFolder, filename);
    }
}
