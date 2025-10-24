using System.Text.Json;

namespace MSFSBlindAssist.Settings;

/// <summary>
/// Manages application settings with version-independent storage.
/// Settings are stored in %APPDATA%\Roaming\MSFSBlindAssist\settings.json
/// </summary>
public static class SettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MSFSBlindAssist"
    );

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    // Legacy path for migration from FBWBA
    private static readonly string LegacySettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FBWBA"
    );
    private static readonly string LegacySettingsFilePath = Path.Combine(LegacySettingsDirectory, "settings.json");

    private static UserSettings? _currentSettings;
    private static readonly object _lock = new object();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

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
                // Migrate from legacy FBWBA folder if exists
                MigrateLegacySettings();

                // Ensure directory exists
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                // If settings file doesn't exist, create with defaults
                if (!File.Exists(SettingsFilePath))
                {
                    System.Diagnostics.Debug.WriteLine("[SettingsManager] Settings file not found, using defaults");
                    UserSettings defaultSettings = new UserSettings();
                    Save(defaultSettings);
                    return defaultSettings;
                }

                // Load from JSON file
                string json = File.ReadAllText(SettingsFilePath);
                UserSettings? settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);

                if (settings == null)
                {
                    Debug.WriteLine("[SettingsManager] Failed to deserialize settings, using defaults");
                    settings = new UserSettings();
                    Save(settings);
                }
                else
                {
                    Debug.WriteLine("[SettingsManager] Settings loaded successfully from JSON");
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
                    string json = JsonSerializer.Serialize(settings, JsonOptions);

                    // Write to file
                    File.WriteAllText(SettingsFilePath, json);

                    // Update current settings reference
                    _currentSettings = settings;

                    Debug.WriteLine($"[SettingsManager] Settings saved to {SettingsFilePath}");
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

        /// <summary>
        /// Migrates settings from the legacy FBWBA folder to the new MSFSBlindAssist folder.
        /// </summary>
        private static void MigrateLegacySettings()
        {
            try
            {
                // Only migrate if legacy settings exist and new settings don't
                if (File.Exists(LegacySettingsFilePath) && !File.Exists(SettingsFilePath))
                {
                    System.Diagnostics.Debug.WriteLine("[SettingsManager] Migrating settings from FBWBA to MSFSBlindAssist");

                    // Ensure new directory exists
                    if (!Directory.Exists(SettingsDirectory))
                    {
                        Directory.CreateDirectory(SettingsDirectory);
                    }

                    // Copy the settings file
                    File.Copy(LegacySettingsFilePath, SettingsFilePath);
                    System.Diagnostics.Debug.WriteLine("[SettingsManager] Settings migration complete");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsManager] Error migrating legacy settings: {ex.Message}");
                // Migration failure is non-fatal; app will create new settings
            }
        }
    }
