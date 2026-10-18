using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Database;

/// <summary>
/// The active database file as it is on disk: the configured simulator version, its path, and the file's
/// size and last write time (zero and <see cref="DateTime.MinValue"/> when it does not exist). See
/// <see cref="DatabaseSelector.CurrentStamp"/>.
/// </summary>
public readonly record struct DatabaseStamp(string SimulatorVersion, string Path, long Length, DateTime LastWriteUtc);

/// <summary>
/// Selects the appropriate database provider based on user configuration.
/// Uses only navdatareader-generated databases (fs2020.sqlite or fs2024.sqlite).
/// </summary>
public static class DatabaseSelector
{
    /// <summary>
    /// The stamp of the database <see cref="SelectProvider"/> would load now. MainForm keeps the stamp of the
    /// database it loaded, so closing the Database Settings window reloads — and runs the database-switch
    /// reset that clears the landing exit plan and disarms the landing assist ([EXIT-11]) — only when the
    /// database actually changed: the setting switched, or the file was rebuilt or removed.
    /// </summary>
    public static DatabaseStamp CurrentStamp()
    {
        string simulatorVersion = SettingsManager.Current.SimulatorVersion ?? "FS2020";
        return StampOf(simulatorVersion, NavdataReaderBuilder.GetDefaultDatabasePath(simulatorVersion));
    }

    /// <summary>The stamp of the database file at <paramref name="path"/> — <c>DatabaseStampTests</c>.</summary>
    internal static DatabaseStamp StampOf(string simulatorVersion, string path)
    {
        var file = new FileInfo(path);
        return file.Exists
            ? new DatabaseStamp(simulatorVersion, path, file.Length, file.LastWriteTimeUtc)
            : new DatabaseStamp(simulatorVersion, path, 0, DateTime.MinValue);
    }

    /// <summary>
    /// Selects and creates the appropriate airport data provider based on settings.
    /// Uses navdatareader-generated database for the configured simulator version.
    /// </summary>
    /// <returns>IAirportDataProvider implementation or null if no database available</returns>
    public static IAirportDataProvider? SelectProvider()
    {
        var settings = SettingsManager.Current;
        string simulatorVersion = settings.SimulatorVersion ?? "FS2020";

        // Get the navdatareader database path for the selected simulator
        string navdataPath = NavdataReaderBuilder.GetDefaultDatabasePath(simulatorVersion);

        if (!File.Exists(navdataPath))
        {
            Log.Debug("Database", $"Database not found for {simulatorVersion}: {navdataPath}");
            return null;
        }

        try
        {
            Log.Debug("Database", $"Using navdatareader database for {simulatorVersion}: {navdataPath}");
            return new LittleNavMapProvider(navdataPath, simulatorVersion);
        }
        catch (Exception ex)
        {
            Log.Debug("Database", $"Failed to load database: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets information about the current database configuration status
    /// </summary>
    /// <returns>Tuple with (hasDatabase, databaseType, message)</returns>
    public static (bool hasDatabase, string databaseType, string message) GetDatabaseStatus()
    {
        var provider = SelectProvider();

        if (provider == null)
        {
            var settings = SettingsManager.Current;
            string simulatorVersion = settings.SimulatorVersion ?? "FS2020";
            return (false, "None", $"No database found for {simulatorVersion}. Open File menu to build database.");
        }

        if (!provider.DatabaseExists)
        {
            return (false, provider.DatabaseType, $"Database not found: {provider.DatabasePath}");
        }

        int airportCount = 0;
        try
        {
            airportCount = provider.GetAirportCount();
        }
        catch (Exception ex)
        {
            return (false, provider.DatabaseType, $"Database error: {ex.Message}");
        }

        var settings2 = SettingsManager.Current;
        string simVer = settings2.SimulatorVersion ?? "FS2020";
        string message = $"{simVer} - {airportCount:N0} airports";
        return (true, provider.DatabaseType, message);
    }
}
