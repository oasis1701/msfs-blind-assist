using System;

namespace FBWBA.Settings
{
    /// <summary>
    /// User settings for FBWBA application.
    /// Stores all user preferences in a version-independent location.
    /// </summary>
    public class UserSettings
    {
        // Accessibility Settings
        public string AnnouncementMode { get; set; } = "ScreenReader";

        // Simulator Settings
        public string SimulatorVersion { get; set; } = "FS2020";

        // GeoNames API Settings
        public string GeoNamesApiUsername { get; set; } = "";

        // SimBrief Settings
        public string SimbriefUsername { get; set; } = "";

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

        // Electronic Flight Bag Window State
        public int EFBTabIndex { get; set; } = 0;
        public int EFBNavigationRowIndex { get; set; } = -1;
        public int EFBNavigationColumnIndex { get; set; } = 0;
        public int EFBWindowWidth { get; set; } = 1200;
        public int EFBWindowHeight { get; set; } = 700;
        public int EFBWindowX { get; set; } = -1;  // -1 means center on screen
        public int EFBWindowY { get; set; } = -1;  // -1 means center on screen

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
                AnnouncementMode = this.AnnouncementMode,
                SimulatorVersion = this.SimulatorVersion,
                GeoNamesApiUsername = this.GeoNamesApiUsername,
                SimbriefUsername = this.SimbriefUsername,
                NearbyCitiesRange = this.NearbyCitiesRange,
                RegionalCitiesRange = this.RegionalCitiesRange,
                MajorCitiesRange = this.MajorCitiesRange,
                LandmarksRange = this.LandmarksRange,
                AirportsRange = this.AirportsRange,
                TerrainRange = this.TerrainRange,
                WaterBodiesRange = this.WaterBodiesRange,
                TouristLandmarksRange = this.TouristLandmarksRange,
                MaxNearbyPlacesToShow = this.MaxNearbyPlacesToShow,
                MaxMajorCitiesToShow = this.MaxMajorCitiesToShow,
                MaxAirportsToShow = this.MaxAirportsToShow,
                MaxTerrainFeaturesToShow = this.MaxTerrainFeaturesToShow,
                MaxWaterBodiesToShow = this.MaxWaterBodiesToShow,
                MaxTouristLandmarksToShow = this.MaxTouristLandmarksToShow,
                MajorCityPopulationThreshold = this.MajorCityPopulationThreshold,
                MajorCityAPIThreshold = this.MajorCityAPIThreshold,
                DistanceUnits = this.DistanceUnits,
                EFBTabIndex = this.EFBTabIndex,
                EFBNavigationRowIndex = this.EFBNavigationRowIndex,
                EFBNavigationColumnIndex = this.EFBNavigationColumnIndex,
                EFBWindowWidth = this.EFBWindowWidth,
                EFBWindowHeight = this.EFBWindowHeight,
                EFBWindowX = this.EFBWindowX,
                EFBWindowY = this.EFBWindowY
            };
        }
    }
}
