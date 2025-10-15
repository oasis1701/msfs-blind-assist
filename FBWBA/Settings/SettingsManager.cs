using System;
using System.IO;
using Newtonsoft.Json;

namespace FBWBA.Settings
{
    /// <summary>
    /// Manages application settings with version-independent storage.
    /// Settings are stored in %APPDATA%\Roaming\FBWBA\settings.json
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FBWBA"
        );

        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

        private static UserSettings _currentSettings;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the current settings instance.
        /// </summary>
        public static UserSettings Current
        {
            get
            {
                lock (_lock)
                {
                    if (_currentSettings == null)
                    {
                        _currentSettings = Load();
                    }
                    return _currentSettings;
                }
            }
        }

        /// <summary>
        /// Loads settings from disk. Creates default settings if file doesn't exist.
        /// </summary>
        public static UserSettings Load()
        {
            try
            {
                // Ensure directory exists
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                // If settings file doesn't exist, try to migrate from old system
                if (!File.Exists(SettingsFilePath))
                {
                    System.Diagnostics.Debug.WriteLine("[SettingsManager] Settings file not found, attempting migration from old system");
                    UserSettings migratedSettings = TryMigrateFromOldSettings();

                    if (migratedSettings != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[SettingsManager] Migration successful, saving migrated settings");
                        Save(migratedSettings);
                        return migratedSettings;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[SettingsManager] No old settings found, using defaults");
                        UserSettings defaultSettings = new UserSettings();
                        Save(defaultSettings);
                        return defaultSettings;
                    }
                }

                // Load from JSON file
                string json = File.ReadAllText(SettingsFilePath);
                UserSettings settings = JsonConvert.DeserializeObject<UserSettings>(json);

                if (settings == null)
                {
                    System.Diagnostics.Debug.WriteLine("[SettingsManager] Failed to deserialize settings, using defaults");
                    settings = new UserSettings();
                    Save(settings);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[SettingsManager] Settings loaded successfully from JSON");
                }

                return settings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsManager] Error loading settings: {ex.Message}");
                // Return default settings on error
                return new UserSettings();
            }
        }

        /// <summary>
        /// Saves settings to disk.
        /// </summary>
        public static void Save(UserSettings settings)
        {
            lock (_lock)
            {
                try
                {
                    // Ensure directory exists
                    if (!Directory.Exists(SettingsDirectory))
                    {
                        Directory.CreateDirectory(SettingsDirectory);
                    }

                    // Serialize to JSON with formatting
                    string json = JsonConvert.SerializeObject(settings, Formatting.Indented);

                    // Write to file
                    File.WriteAllText(SettingsFilePath, json);

                    // Update current settings reference
                    _currentSettings = settings;

                    System.Diagnostics.Debug.WriteLine($"[SettingsManager] Settings saved to {SettingsFilePath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SettingsManager] Error saving settings: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Saves the current settings instance to disk.
        /// </summary>
        public static void Save()
        {
            Save(Current);
        }

        /// <summary>
        /// Attempts to migrate settings from the old .NET user settings system.
        /// </summary>
        private static UserSettings TryMigrateFromOldSettings()
        {
            try
            {
                // Try to load from old Properties.Settings.Default
                var oldSettings = Properties.Settings.Default;

                // Check if any non-default values exist (basic validation)
                if (oldSettings == null)
                {
                    return null;
                }

                System.Diagnostics.Debug.WriteLine("[SettingsManager] Found old settings, beginning migration");

                // Create new settings with values from old system
                UserSettings newSettings = new UserSettings
                {
                    AnnouncementMode = oldSettings.AnnouncementMode ?? "ScreenReader",
                    SimulatorVersion = oldSettings.SimulatorVersion ?? "FS2020",
                    GeoNamesApiUsername = oldSettings.GeoNamesApiUsername ?? "",
                    NearbyCitiesRange = oldSettings.NearbyCitiesRange,
                    RegionalCitiesRange = oldSettings.RegionalCitiesRange,
                    MajorCitiesRange = oldSettings.MajorCitiesRange,
                    LandmarksRange = oldSettings.LandmarksRange,
                    AirportsRange = oldSettings.AirportsRange,
                    TerrainRange = oldSettings.TerrainRange,
                    WaterBodiesRange = oldSettings.WaterBodiesRange,
                    TouristLandmarksRange = oldSettings.TouristLandmarksRange,
                    MaxNearbyPlacesToShow = oldSettings.MaxNearbyPlacesToShow,
                    MaxMajorCitiesToShow = oldSettings.MaxMajorCitiesToShow,
                    MaxAirportsToShow = oldSettings.MaxAirportsToShow,
                    MaxTerrainFeaturesToShow = oldSettings.MaxTerrainFeaturesToShow,
                    MaxWaterBodiesToShow = oldSettings.MaxWaterBodiesToShow,
                    MaxTouristLandmarksToShow = oldSettings.MaxTouristLandmarksToShow,
                    MajorCityPopulationThreshold = oldSettings.MajorCityPopulationThreshold,
                    MajorCityAPIThreshold = oldSettings.MajorCityAPIThreshold ?? "cities15000",
                    DistanceUnits = oldSettings.DistanceUnits ?? "miles"
                };

                System.Diagnostics.Debug.WriteLine("[SettingsManager] Migration completed successfully");
                System.Diagnostics.Debug.WriteLine($"[SettingsManager] Migrated values - AnnouncementMode: {newSettings.AnnouncementMode}, SimulatorVersion: {newSettings.SimulatorVersion}");

                return newSettings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsManager] Error during migration: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resets settings to default values.
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _currentSettings = new UserSettings();
                Save(_currentSettings);
                System.Diagnostics.Debug.WriteLine("[SettingsManager] Settings reset to defaults");
            }
        }

        /// <summary>
        /// Gets the full path to the settings file.
        /// </summary>
        public static string GetSettingsFilePath()
        {
            return SettingsFilePath;
        }
    }
}
