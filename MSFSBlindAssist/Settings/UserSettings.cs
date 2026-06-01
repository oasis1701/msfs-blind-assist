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

        /// <summary>
        /// When true, the Output Z (local time) and Output Shift+Z (Zulu
        /// time) hotkeys speak HH:MM:SS instead of the default HH:MM.
        /// Configurable in Announcement Settings.
        /// </summary>
        public bool AnnounceTimeWithSeconds { get; set; } = false;

        // Hand Fly Settings
        public HandFlyFeedbackMode HandFlyFeedbackMode { get; set; } = HandFlyFeedbackMode.TonesOnly;
        public double HandFlyToneVolume { get; set; } = 0.05; // 0.0 to 1.0 (default 5%)
        public HandFlyWaveType HandFlyWaveType { get; set; } = HandFlyWaveType.Sine;
        public bool HandFlyMonitorHeading { get; set; } = true;
        public bool HandFlyMonitorVerticalSpeed { get; set; } = true;
        public int HandFlyAnnouncementIntervalMs { get; set; } = 1000; // Configurable interval for heading/VS announcements

        // Visual Guidance Settings
        public HandFlyWaveType VisualGuidanceToneWaveform { get; set; } = HandFlyWaveType.Triangle;
        public double VisualGuidanceToneVolume { get; set; } = 0.05; // 0.0 to 1.0 (default 5%)

        // Visual Guidance — "current attitude" follower tone. Always plays alongside the desired tone:
        // pilot matches the two pans (lateral) and zero-beats the two frequencies (vertical) by ear.
        // Waveform defaults to Sine so it stays timbrally distinct from the Triangle desired tone.
        public HandFlyWaveType VisualGuidanceCurrentToneWaveform { get; set; } = HandFlyWaveType.Sine;
        public double VisualGuidanceCurrentToneVolume { get; set; } = 0.05; // 0.0 to 1.0 (default 5%)

        // Visual Guidance — hard-pan mode. When ON, both tones snap to full left / full right
        // once bank exceeds ~1° (instead of proportional pan). For stereo-speaker setups where
        // partial pan blends with the centred case and direction is hard to tell. Headphones
        // generally do not need this. Default OFF.
        public bool VisualGuidanceHardPanTone { get; set; } = false;

        // Takeoff Assist Tone Settings
        public HandFlyWaveType TakeoffAssistToneWaveform { get; set; } = HandFlyWaveType.Sine;
        public double TakeoffAssistToneVolume { get; set; } = 0.05; // 0.0 to 1.0 (default 5%)
        public bool TakeoffAssistMuteCenterlineAnnouncements { get; set; } = false; // Mute centerline deviation announcements
        public bool TakeoffAssistInvertPanning { get; set; } = false; // Invert panning direction
        /// <summary>
        /// When true, the takeoff-assist centerline tone hard-pans to full
        /// ±1 instead of the proportional headingDiff / 5° curve. Useful
        /// for stereo-speaker users where partial pan is hard to tell from
        /// centred. Default off.
        /// </summary>
        public bool TakeoffAssistHardPanTone { get; set; } = false;
        public bool TakeoffAssistLegacyMode { get; set; } = false; // Legacy mode: heading-based instead of centerline tracking
        public int TakeoffAssistHeadingToneThreshold { get; set; } = 0; // 0 = Always, 1-5 = degrees threshold
        public bool TakeoffAssistEnableCallouts { get; set; } = true; // Enable speed callouts (80kt, 100kt, V1, rotate)

        /// <summary>
        /// When true (default), taxi guidance will automatically activate
        /// Takeoff Assist when the aircraft becomes lined up on the destination
        /// runway (lineup hysteresis met: heading less than 1 degree off,
        /// cross-track less than 10 feet). Opt-out — pilots who prefer to engage
        /// Takeoff Assist manually can disable this in Hand Fly Options.
        /// One-shot per route: if the pilot disables Takeoff Assist after
        /// auto-activation and stays lined up, it does not re-engage until the
        /// next taxi route is loaded.
        /// </summary>
        public bool TakeoffAssistAutoActivateOnLineup { get; set; } = true;

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
        public bool GeminiSearchGrounding { get; set; } = false;

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

        // Fenix Monitor Manager Settings
        public List<string> FenixDisabledMonitorVariables { get; set; } = new List<string>();

        // PMDG Announcement Monitor Settings — variable keys that the user has
        // unticked in PMDGAnnouncementMonitorForm. The MainForm continuous-
        // monitoring branch consults this list when the loaded aircraft's
        // AircraftCode starts with "PMDG_" and skips the announcement for any
        // variable whose key is here. Persisted across sessions so the user
        // doesn't have to re-tick on every launch.
        public List<string> PMDGDisabledMonitorVariables { get; set; } = new List<string>();

        // A380 Monitor Manager Settings — variable keys the user has unticked in
        // FBWA380MonitorManagerForm. Consulted (and ECAM-memo sentinel honoured)
        // when AircraftCode == "FBW_A380". Persisted across sessions.
        public List<string> A380DisabledMonitorVariables { get; set; } = new List<string>();

        // FlyByWire A32NX Monitor Manager — variable keys the user has un-checked in
        // FlyByWireA320MonitorManagerForm. Consulted (and ECAM-memo sentinel honoured)
        // when AircraftCode == "A320". Persisted across sessions.
        public List<string> A32NXDisabledMonitorVariables { get; set; } = new List<string>();

        // FMC settings — meaningful when a PMDG aircraft or the Fenix A320 is
        // loaded; the FMC Settings menu item is gated on
        // AircraftCode.StartsWith("PMDG_") || AircraftCode.StartsWith("FENIX_").

        /// <summary>
        /// When true, both the PMDG 777 CDU form and the Fenix A320 MCDU form
        /// remap their line-select keys from the default Ctrl+1..6 (L) /
        /// Alt+1..6 (R) to F1..F6 (L) / F7..F12 (R). Frees Ctrl/Alt for other
        /// shortcuts; matches TFM's "alternate keys" layout, which many
        /// returning TFM users prefer.
        /// </summary>
        public bool MCDUUseAlternateLSKKeys { get; set; } = false;

        /// <summary>
        /// When true, the Output D / Shift+D distance keys read the PMDG PROG
        /// page on the right CDU instead of the SimConnect/PMDG SDK FMC fields.
        /// Gives ETA in Z time and landing fuel for the destination key, plus
        /// distance/ETA to TOC, step climb, or TOD for the descent key. Falls
        /// back to the default offset-based readout when the PROG page can't be
        /// activated (e.g. CDU power off — retried every 30 s in the background).
        /// </summary>
        public bool PMDGEnhancedDistanceMode { get; set; } = false;

        // Taxi Guidance Settings
        public HandFlyWaveType TaxiGuidanceToneWaveform { get; set; } = HandFlyWaveType.Sine;
        public double TaxiGuidanceToneVolume { get; set; } = 0.05;

        /// <summary>
        /// When true, inverts the steering tone's stereo pan: a tone in the
        /// right ear means steer LEFT to centre it (rather than right).
        /// Mirrors <see cref="TakeoffAssistInvertPanning"/>. Default off
        /// (existing behaviour: pan in the direction the pilot should turn).
        /// </summary>
        public bool TaxiGuidanceInvertSteeringTone { get; set; } = false;

        /// <summary>
        /// When true, the steering tone hard-pans to full left (-1) or full
        /// right (+1) — no intermediate magnitudes. Speaker users hear the
        /// tone come out of exactly one speaker so there's no ambiguity
        /// between "slightly off" and "centred". Default off (proportional
        /// pan with the sqrt curve).
        /// </summary>
        public bool TaxiGuidanceHardPanTone { get; set; } = false;
        /// <summary>
        /// When true, taxi guidance announces named taxiways being crossed at intersections
        /// (e.g. "Crossing taxiway Link 53"). Default on; users can disable for quieter taxis.
        /// </summary>
        public bool TaxiGuidanceAnnounceCrossings { get; set; } = true;

        /// <summary>
        /// Ground-speed announcement interval, in knots. 0 disables the feature.
        /// When non-zero, the screen reader announces the current ground speed each
        /// time it crosses a multiple of this value (rising or falling). Useful for
        /// blind pilots monitoring taxi speed against the FAA / SOP caps (30 kt
        /// straight, 10 kt turns) and for the takeoff roll. Suggested values: 5 or
        /// 10 kt. Active during BOTH taxi guidance and takeoff assist phases — the
        /// callouts complement each other (10/20/30 kt taxi → 80/100/V1/rotate
        /// takeoff). Default 0 (off) so existing users see no behaviour change.
        /// </summary>
        public int TaxiGuidanceGroundSpeedAnnounceInterval { get; set; } = 0;

        // Ground traffic proximity distances in metres instead of feet.
        // Default false (feet) — aviation uses feet for taxiway spacing.
        public bool GroundTrafficUseMetres { get; set; } = false;

        // Weather Settings
        public bool WeatherAutoAnnounceEnabled { get; set; } = false;

        /// <summary>
        /// Minimum minutes between weather auto-announcements. 0 = follow
        /// ActiveSky's own download interval (no extra throttle; rely on the
        /// monitor's TimeStamp + content-change detection). Positive values
        /// add a hard floor — useful when teleporting / repositioning makes
        /// the position-specific METAR change spatially and the smart
        /// detection can't tell that from a real AS download.
        /// Allowed: 0, 5, 10, 15, 20, 30, 45, 60.
        /// </summary>
        public int WeatherAutoAnnounceIntervalMinutes { get; set; } = 0;
        public bool SigmetProximityAlertsEnabled { get; set; } = false;
        public bool PirepProximityAlertsEnabled { get; set; } = false;
        public int SigmetProximityRangeNm { get; set; } = 100;
        public bool DecodeWeatherAdvisories { get; set; } = false;

        // HS787 bridge — community folder override for non-standard installs
        public string? Hs787CommunityFolderOverride { get; set; } = null;
        // "FS2024" or "FS2020" — set when Hs787CommunityFolderOverride was entered manually
        public string? Hs787SimVersionOverride { get; set; } = null;

        /// <summary>
        /// When true, the AccessGSX service continues to announce GSX tooltip
        /// updates through the screen reader even after the AccessGSX form is
        /// closed (hidden). When false (default), tooltip speech is silenced
        /// while the form is hidden — the form is the only speech surface.
        /// </summary>
        public bool GsxBackgroundMonitoring { get; set; } = false;

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
            AnnounceTimeWithSeconds = AnnounceTimeWithSeconds,
            HandFlyFeedbackMode = HandFlyFeedbackMode,
            HandFlyToneVolume = HandFlyToneVolume,
            HandFlyWaveType = HandFlyWaveType,
            HandFlyMonitorHeading = HandFlyMonitorHeading,
            HandFlyMonitorVerticalSpeed = HandFlyMonitorVerticalSpeed,
            HandFlyAnnouncementIntervalMs = HandFlyAnnouncementIntervalMs,
            VisualGuidanceToneWaveform = VisualGuidanceToneWaveform,
            VisualGuidanceToneVolume = VisualGuidanceToneVolume,
            VisualGuidanceCurrentToneWaveform = VisualGuidanceCurrentToneWaveform,
            VisualGuidanceCurrentToneVolume = VisualGuidanceCurrentToneVolume,
            VisualGuidanceHardPanTone = VisualGuidanceHardPanTone,
            TakeoffAssistToneWaveform = TakeoffAssistToneWaveform,
            TakeoffAssistToneVolume = TakeoffAssistToneVolume,
            TakeoffAssistMuteCenterlineAnnouncements = TakeoffAssistMuteCenterlineAnnouncements,
            TakeoffAssistInvertPanning = TakeoffAssistInvertPanning,
            TakeoffAssistHardPanTone = TakeoffAssistHardPanTone,
            TakeoffAssistLegacyMode = TakeoffAssistLegacyMode,
            TakeoffAssistHeadingToneThreshold = TakeoffAssistHeadingToneThreshold,
            TakeoffAssistEnableCallouts = TakeoffAssistEnableCallouts,
            TakeoffAssistAutoActivateOnLineup = TakeoffAssistAutoActivateOnLineup,
            SimulatorVersion = SimulatorVersion,
            LastAircraft = LastAircraft,
            GeoNamesApiUsername = GeoNamesApiUsername,
            NearestCityAnnouncementInterval = NearestCityAnnouncementInterval,
            SimbriefUsername = SimbriefUsername,
            GeminiApiKey = GeminiApiKey,
            GeminiSearchGrounding = GeminiSearchGrounding,
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
            DistanceUnits = DistanceUnits,
            FenixDisabledMonitorVariables = new List<string>(FenixDisabledMonitorVariables),
            PMDGDisabledMonitorVariables = new List<string>(PMDGDisabledMonitorVariables),
            A380DisabledMonitorVariables = new List<string>(A380DisabledMonitorVariables),
            A32NXDisabledMonitorVariables = new List<string>(A32NXDisabledMonitorVariables),
            MCDUUseAlternateLSKKeys = MCDUUseAlternateLSKKeys,
            PMDGEnhancedDistanceMode = PMDGEnhancedDistanceMode,
            WeatherAutoAnnounceEnabled = WeatherAutoAnnounceEnabled,
            WeatherAutoAnnounceIntervalMinutes = WeatherAutoAnnounceIntervalMinutes,
            SigmetProximityAlertsEnabled = SigmetProximityAlertsEnabled,
            PirepProximityAlertsEnabled = PirepProximityAlertsEnabled,
            SigmetProximityRangeNm = SigmetProximityRangeNm,
            DecodeWeatherAdvisories = DecodeWeatherAdvisories,
            TaxiGuidanceToneWaveform = TaxiGuidanceToneWaveform,
            TaxiGuidanceToneVolume = TaxiGuidanceToneVolume,
            TaxiGuidanceInvertSteeringTone = TaxiGuidanceInvertSteeringTone,
            TaxiGuidanceHardPanTone = TaxiGuidanceHardPanTone,
            TaxiGuidanceAnnounceCrossings = TaxiGuidanceAnnounceCrossings,
            TaxiGuidanceGroundSpeedAnnounceInterval = TaxiGuidanceGroundSpeedAnnounceInterval,
            GroundTrafficUseMetres = GroundTrafficUseMetres,
            Hs787CommunityFolderOverride = Hs787CommunityFolderOverride,
            Hs787SimVersionOverride = Hs787SimVersionOverride,
            GsxBackgroundMonitoring = GsxBackgroundMonitoring
        };
    }
}
