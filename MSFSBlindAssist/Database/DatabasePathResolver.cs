
namespace MSFSBlindAssist.Database;
/// <summary>
/// Resolves database file paths for navdatareader-generated databases
/// </summary>
public static class DatabasePathResolver
{
    /// <summary>
    /// Gets the path for navdatareader-generated database (FS2020)
    /// </summary>
    /// <returns>Full path to fs2020.sqlite</returns>
    public static string GetNavdataReaderFS2020Path()
    {
        return NavdataReaderBuilder.GetDefaultDatabasePath("FS2020");
    }

    /// <summary>
    /// Gets the path for navdatareader-generated database (FS2024)
    /// </summary>
    /// <returns>Full path to fs2024.sqlite</returns>
    public static string GetNavdataReaderFS2024Path()
    {
        return NavdataReaderBuilder.GetDefaultDatabasePath("FS2024");
    }

    /// <summary>
    /// Checks if a database file exists at the specified path
    /// </summary>
    /// <param name="path">Path to check</param>
    /// <returns>True if file exists, false otherwise</returns>
    public static bool DatabaseExists(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    /// <summary>
    /// Gets a user-friendly display name for a database path
    /// </summary>
    /// <param name="path">Full path to database file</param>
    /// <returns>Display name (just filename)</returns>
    public static string GetDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "(not configured)";

        return Path.GetFileName(path);
    }
}
