using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Settings;

/// <summary>
/// Hand fly feedback mode options.
/// </summary>
public enum HandFlyFeedbackMode
{
    /// <summary>Audio tones only (no screen reader announcements).</summary>
    TonesOnly,
    /// <summary>Screen reader announcements only (no audio tones).</summary>
    AnnouncementsOnly,
    /// <summary>Both audio tones and screen reader announcements.</summary>
    Both
}

/// <summary>
/// Wave type for audio tone generation.
/// </summary>
public enum HandFlyWaveType
{
    /// <summary>Sine wave (smoothest, most pleasing).</summary>
    Sine,
    /// <summary>Triangle wave (smooth but with slight harmonic content).</summary>
    Triangle,
    /// <summary>Sawtooth wave (brighter, more harmonics).</summary>
    Sawtooth,
    /// <summary>Rich sine wave (warm, smooth with subtle harmonic content).</summary>
    Square
}

/// <summary>
/// User settings for FBWBA application.
/// Stores all user preferences in a version-independent location.
/// </summary>
public class UserSettings
{
        // Accessibility Settings
        public string AnnouncementMode { get; set; } = "ScreenReader";

        // Hand Fly Settings
        public HandFlyFeedbackMode HandFlyFeedbackMode { get; set; } = HandFlyFeedbackMode.TonesOnly;
        public double HandFlyToneVolume { get; set; } = 0.05; // 0.0 to 1.0 (default 5%)
        public HandFlyWaveType HandFlyWaveType { get; set; } = HandFlyWaveType.Sine;
        public bool HandFlyMonitorHeading { get; set; } = true;
        public bool HandFlyMonitorVerticalSpeed { get; set; } = true;
        public int HandFlyAnnouncementIntervalMs { get; set; } = 1000; // Configurable interval for heading/VS announcements

        // Visual Guidance Settings
        public InterceptAngle VisualGuidanceInterceptAngle { get; set; } = InterceptAngle.Medium45;
        public HandFlyWaveType VisualGuidanceToneWaveform { get; set; } = HandFlyWaveType.Triangle;
        public double VisualGuidanceToneVolume { get; set; } = 0.05; // 0.0 to 1.0 (default 5%)

        // Simulator Settings
        public string SimulatorVersion { get; set; } = "FS2020";

        // Aircraft Settings
        public string LastAircraft { get; set; } = "A320";

        // GeoNames API Settings
        public string GeoNamesApiUsername { get; set; } = "";
        public int NearestCityAnnouncementInterval { get; set; } = 0; // 0 = Off, otherwise seconds

        // SimBrief Settings
        public string SimbriefUsername { get; set; } = "";

        // Gemini AI Settings
        public string GeminiApiKey { get; set; } = "";

        // Range Settings (in selected distance units)
        public int NearbyCitiesRange { get; set; } = 25;
        public int RegionalCitiesRange { get; set; } = 50;
        public int MajorCitiesRange { get; set; } = 185;
        public int LandmarksRange { get; set; } = 30;
        public int AirportsRange { get; set; } = 50;
        public int TerrainRange { get; set; } = 30;
        public int WaterBodiesRange { get; set; } = 20;
        public int TouristLandmarksRange { get; set; } = 25;

        // Display Limits
        public int MaxNearbyPlacesToShow { get; set; } = 10;
        public int MaxMajorCitiesToShow { get; set; } = 10;
        public int MaxAirportsToShow { get; set; } = 8;
        public int MaxTerrainFeaturesToShow { get; set; } = 8;
        public int MaxWaterBodiesToShow { get; set; } = 8;
        public int MaxTouristLandmarksToShow { get; set; } = 8;

        // City Filtering
        public int MajorCityPopulationThreshold { get; set; } = 50000;
        public string MajorCityAPIThreshold { get; set; } = "cities15000";

        // Units
        public string DistanceUnits { get; set; } = "miles";

        /// <summary>
        /// Creates a new UserSettings instance with default values.
        /// </summary>
        public UserSettings()
        {
            // All defaults are set via property initializers above
        }

    /// <summary>
    /// Creates a copy of this settings instance.
    /// </summary>
    public UserSettings Clone()
    {
        return new UserSettings
        {
            AnnouncementMode = AnnouncementMode,
            HandFlyFeedbackMode = HandFlyFeedbackMode,
            HandFlyToneVolume = HandFlyToneVolume,
            HandFlyWaveType = HandFlyWaveType,
            HandFlyMonitorHeading = HandFlyMonitorHeading,
            HandFlyMonitorVerticalSpeed = HandFlyMonitorVerticalSpeed,
            HandFlyAnnouncementIntervalMs = HandFlyAnnouncementIntervalMs,
            VisualGuidanceInterceptAngle = VisualGuidanceInterceptAngle,
            VisualGuidanceToneWaveform = VisualGuidanceToneWaveform,
            VisualGuidanceToneVolume = VisualGuidanceToneVolume,
            SimulatorVersion = SimulatorVersion,
            LastAircraft = LastAircraft,
            GeoNamesApiUsername = GeoNamesApiUsername,
            NearestCityAnnouncementInterval = NearestCityAnnouncementInterval,
            SimbriefUsername = SimbriefUsername,
            GeminiApiKey = GeminiApiKey,
            NearbyCitiesRange = NearbyCitiesRange,
            RegionalCitiesRange = RegionalCitiesRange,
            MajorCitiesRange = MajorCitiesRange,
            LandmarksRange = LandmarksRange,
            AirportsRange = AirportsRange,
            TerrainRange = TerrainRange,
            WaterBodiesRange = WaterBodiesRange,
            TouristLandmarksRange = TouristLandmarksRange,
            MaxNearbyPlacesToShow = MaxNearbyPlacesToShow,
            MaxMajorCitiesToShow = MaxMajorCitiesToShow,
            MaxAirportsToShow = MaxAirportsToShow,
            MaxTerrainFeaturesToShow = MaxTerrainFeaturesToShow,
            MaxWaterBodiesToShow = MaxWaterBodiesToShow,
            MaxTouristLandmarksToShow = MaxTouristLandmarksToShow,
            MajorCityPopulationThreshold = MajorCityPopulationThreshold,
            MajorCityAPIThreshold = MajorCityAPIThreshold,
            DistanceUnits = DistanceUnits
        };
    }
}
