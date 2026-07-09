using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

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
                    Log.Debug("Settings", "Settings file not found, using defaults");
                    UserSettings defaultSettings = new UserSettings();
                    SeedTakeoffAssistToneConvention(defaultSettings, freshInstall: true);
                    SeedFenixMonitorDefaults(defaultSettings); // sets flag + saves
                    return defaultSettings;
                }

                // Load from JSON file
                string json = File.ReadAllText(SettingsFilePath);
                UserSettings? settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);

                if (settings == null)
                {
                    Log.Warn("Settings", "Failed to deserialize settings, using defaults");
                    settings = new UserSettings();
                    Save(settings);
                }
                else
                {
                    Log.Debug("Settings", "Settings loaded successfully from JSON");
                }

                // SeedFenixMonitorDefaults only saves when ITS OWN flag was just set, so a
                // stored file that already has FenixMonitorDefaultsSeeded=true would leave
                // our migration unpersisted on disk if we didn't guard+save it ourselves here
                // (mirrors the Fenix helper's own save-only-when-newly-set pattern).
                if (!settings.TakeoffAssistToneConventionMigrated)
                {
                    SeedTakeoffAssistToneConvention(settings, freshInstall: false);
                    Save(settings);
                }
                SeedFenixMonitorDefaults(settings); // one-time: default-disable the noisy clock counters
                return settings;
            }
            catch (Exception ex)
            {
                Log.Error("Settings", "Error loading settings", ex);
                // Return default settings on error
                return new UserSettings();
            }
        }

        /// <summary>
        /// One-time seed for the PR #111 takeoff-tone changes (Robin-confirmed
        /// 2026-07-03). Two migrations under one flag:
        /// (a) Pan convention: the tone used to pan on the heading DEVIATION side
        ///     (steer AWAY at InvertPanning=false); it now pans on the STEER side.
        ///     Under the old semantics InvertPanning==true WAS the steer-toward
        ///     mapping, so seeding SteerTowardTone from it preserves each existing
        ///     user's experienced direction exactly.
        /// (b) Threshold: a stored 0 ("Always") becomes 1° so every user gets the
        ///     new silent-on-track behavior once; deliberate 1–5 values are kept,
        ///     and post-migration choices (incl. re-selecting "Always") stick.
        /// Fresh installs keep the class defaults (steer-toward, 1°).
        /// </summary>
        internal static void SeedTakeoffAssistToneConvention(UserSettings settings, bool freshInstall)
        {
            if (settings.TakeoffAssistToneConventionMigrated) return;
            if (!freshInstall)
            {
                settings.TakeoffAssistSteerTowardTone = settings.TakeoffAssistInvertPanning;
                if (settings.TakeoffAssistHeadingToneThreshold == 0)
                {
                    settings.TakeoffAssistHeadingToneThreshold = 1;
                }
            }
            settings.TakeoffAssistToneConventionMigrated = true;
        }

        /// <summary>
        /// One-time seed of Fenix vars that should be auto-monitored but NOT spoken by
        /// default — added to the Fenix disabled-monitor list (the combo/display still
        /// tracks them; only the spoken call-out is gated off):
        ///   * CLOCK CHRONO / ELAPSED / UTC and the FenixQuartz chrono/ET counters —
        ///     raw-seconds counters that tick every second.
        ///   * the four seat height/distance switches — Continuous so the combo can spring
        ///     to "Stop" at the travel limit, but silent (the user just wants the value).
        /// Runs once (guarded by FenixMonitorDefaultsSeeded) so a deliberate re-enable in
        /// the Ctrl+M monitor is never overwritten.
        /// </summary>
        internal static void SeedFenixMonitorDefaults(UserSettings s)
        {
            if (s.FenixMonitorDefaultsSeeded) return;
            s.FenixMonitorDefaultsSeeded = true;
            string[] defaultSilent =
            {
                "N_MIP_CLOCK_CHRONO", "N_MIP_CLOCK_ELAPSED", "N_MIP_CLOCK_UTC",
                "FNX2PLD_clockChr", "FNX2PLD_clockEt",
                "S_SEAT_HEIGHT_CAPT", "S_SEAT_DISTANCE_CAPT",
                "S_SEAT_HEIGHT_FO", "S_SEAT_DISTANCE_FO",
            };
            foreach (var key in defaultSilent)
            {
                if (!s.FenixDisabledMonitorVariables.Contains(key))
                    s.FenixDisabledMonitorVariables.Add(key);
            }
            Save(s);
        }

        /// <summary>
        /// Saves settings to disk.
        /// </summary>
        public static void Save(UserSettings settings)
        {
            // Hold _lock ONLY for the in-memory work: serializing the mutable,
            // shared settings object to a string and publishing the reference.
            // The disk write happens OUTSIDE the lock, against the already-built
            // string. _lock is the same lock the 30 Hz SimConnect position path
            // contends for via SettingsManager.Current; holding it across
            // File.WriteAllText (which can block for the full duration of an
            // antivirus-scanned %APPDATA% write) would stall audio-guidance frames
            // whenever an options dialog is OK'd mid-approach. The serialized
            // string is an immutable snapshot, so writing it after releasing the
            // lock is race-free even if another thread mutates the settings object
            // and re-saves concurrently — each Save writes its own snapshot.
            string json;
            try
            {
                lock (_lock)
                {
                    // Serialize to JSON with formatting (reads the mutable shared
                    // object — must be under the lock).
                    json = JsonSerializer.Serialize(settings, JsonOptions);

                    // Update current settings reference.
                    _currentSettings = settings;
                }

                // Ensure directory exists, then write — both outside the lock.
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                File.WriteAllText(SettingsFilePath, json);

                Log.Debug("Settings", $"Settings saved to {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error("Settings", "Error saving settings", ex);
                throw;
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
            // Mirror Save's own discipline (see its comment above): only the
            // in-memory publish of the new settings reference happens under
            // _lock. Save() itself takes _lock again (reentrant) for its
            // serialize+publish step and then writes the file OUTSIDE any
            // lock — but calling it from inside this lock would keep _lock
            // held for the full File.WriteAllText duration, stalling the
            // 30 Hz SimConnect position path that also contends on _lock via
            // SettingsManager.Current. So capture the new instance, release
            // the lock, then Save it.
            UserSettings newSettings;
            lock (_lock)
            {
                newSettings = new UserSettings();
                _currentSettings = newSettings;
            }

            Save(newSettings);
            Log.Debug("Settings", "Settings reset to defaults");
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
                    Log.Debug("Settings", "Migrating settings from FBWBA to MSFSBlindAssist");

                    // Ensure new directory exists
                    if (!Directory.Exists(SettingsDirectory))
                    {
                        Directory.CreateDirectory(SettingsDirectory);
                    }

                    // Copy the settings file
                    File.Copy(LegacySettingsFilePath, SettingsFilePath);
                    Log.Debug("Settings", "Settings migration complete");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Settings", "Error migrating legacy settings", ex);
                // Migration failure is non-fatal; app will create new settings
            }
        }
    }
