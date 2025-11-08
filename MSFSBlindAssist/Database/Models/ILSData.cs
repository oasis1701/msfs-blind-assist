namespace MSFSBlindAssist.Database.Models
{
    /// <summary>
    /// Represents ILS (Instrument Landing System) data for a specific runway.
    /// Contains localizer and glideslope information retrieved from the navigation database.
    /// </summary>
    public class ILSData
    {
        /// <summary>
        /// ILS identifier code (e.g., "IHIQ")
        /// </summary>
        public string Ident { get; set; } = string.Empty;

        /// <summary>
        /// ILS frequency in MHz (e.g., 110.9)
        /// </summary>
        public double Frequency { get; set; }

        /// <summary>
        /// Localizer effective range in nautical miles
        /// </summary>
        public int Range { get; set; }

        /// <summary>
        /// Glideslope effective range in nautical miles
        /// </summary>
        public int GlideslopeRange { get; set; }

        /// <summary>
        /// Glideslope pitch angle in degrees (typically 3.0)
        /// </summary>
        public double GlideslopePitch { get; set; }

        /// <summary>
        /// Localizer true heading in degrees (0-360)
        /// </summary>
        public double LocalizerHeading { get; set; }

        /// <summary>
        /// Localizer beam width in degrees
        /// </summary>
        public double LocalizerWidth { get; set; }

        /// <summary>
        /// ILS antenna latitude in degrees
        /// </summary>
        public double AntennaLatitude { get; set; }

        /// <summary>
        /// ILS antenna longitude in degrees
        /// </summary>
        public double AntennaLongitude { get; set; }

        /// <summary>
        /// ILS antenna altitude in feet MSL
        /// </summary>
        public int AntennaAltitude { get; set; }

        /// <summary>
        /// Glideslope antenna latitude in degrees (nullable if not available)
        /// </summary>
        public double? GlideslopeLatitude { get; set; }

        /// <summary>
        /// Glideslope antenna longitude in degrees (nullable if not available)
        /// </summary>
        public double? GlideslopeLongitude { get; set; }

        /// <summary>
        /// Glideslope antenna altitude in feet MSL (nullable if not available)
        /// </summary>
        public int? GlideslopeAltitude { get; set; }
    }
}
