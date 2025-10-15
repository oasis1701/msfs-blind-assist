using System;
using System.IO;
using FBWBA.Settings;

namespace FBWBA.Database
{
    /// <summary>
    /// Selects the appropriate database provider based on user configuration.
    /// Uses only navdatareader-generated databases (fs2020.sqlite or fs2024.sqlite).
    /// </summary>
    public static class DatabaseSelector
    {
        /// <summary>
        /// Selects and creates the appropriate airport data provider based on settings.
        /// Uses navdatareader-generated database for the configured simulator version.
        /// </summary>
        /// <returns>IAirportDataProvider implementation or null if no database available</returns>
        public static IAirportDataProvider SelectProvider()
        {
            var settings = SettingsManager.Current;
            string simulatorVersion = settings.SimulatorVersion ?? "FS2020";

            // Get the navdatareader database path for the selected simulator
            string navdataPath = NavdataReaderBuilder.GetDefaultDatabasePath(simulatorVersion);

            if (!File.Exists(navdataPath))
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseSelector] Database not found for {simulatorVersion}: {navdataPath}");
                return null;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseSelector] Using navdatareader database for {simulatorVersion}: {navdataPath}");
                return new LittleNavMapProvider(navdataPath, simulatorVersion);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseSelector] Failed to load database: {ex.Message}");
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
}
