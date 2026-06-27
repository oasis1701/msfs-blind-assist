
namespace MSFSBlindAssist.Database.Models;

public class Runway
{
        public int Id { get; set; }
        public string AirportICAO { get; set; }
        public string RunwayID { get; set; }
        public double Heading { get; set; }
        public double HeadingMag { get; set; }
        public double StartLat { get; set; }
        public double StartLon { get; set; }
        public double EndLat { get; set; }
        public double EndLon { get; set; }
        public double Length { get; set; }
        public double Width { get; set; }
        public int Surface { get; set; }
        public double ILSFreq { get; set; }
        public double ILSHeading { get; set; }
        public double ThresholdOffset { get; set; }

        /// <summary>Threshold elevation in feet MSL, sourced from runway_end.altitude.
        /// At airports with sloped runways or different elevations between runway ends
        /// (KASE, LSZS, KSEA 16L/34R), this is more accurate than the airport's published
        /// field elevation. 0 means unknown — callers fall back to airport.Altitude.</summary>
        public double ThresholdElevation { get; set; }

        /// <summary>Published ILS glideslope angle in degrees, sourced from ils.gs_pitch.
        /// Most ILSes are 3.0°, but some are not — London City 5.5°, Aspen 6.59°, Innsbruck
        /// 3.8°. 0 means the runway has no ILS or the navdata doesn't carry the angle —
        /// callers fall back to 3.0°.</summary>
        public double GlideslopeAngleDeg { get; set; }

        // Operational status flags read from runway_end. Defaults are PERMISSIVE
        // (closed=false, can-land=true, can-takeoff=true) because most user DBs
        // — including the test build this app was developed against — populate
        // every row with the permissive value. Third-party scenery and some
        // Navigraph merges DO set these, so reading + filtering on them gives
        // broader compatibility without breaking the common case.
        public bool IsClosed { get; set; } = false;
        public bool IsLanding { get; set; } = true;
        public bool IsTakeoff { get; set; } = true;

        public Runway()
        {
            AirportICAO = string.Empty;
            RunwayID = string.Empty;
        }

        public string GetSurfaceType()
        {
            switch (Surface)
            {
                case 0: return "Concrete";
                case 1: return "Grass";
                case 2: return "Water";
                case 4: return "Asphalt";
                case 7: return "Clay";
                case 8: return "Snow";
                case 9: return "Ice";
                case 12: return "Dirt";
                case 13: return "Coral";
                case 14: return "Gravel";
                case 15: return "Oil-treated";
                case 16: return "Mats";
                case 17: return "Bituminous";
                case 18: return "Brick";
                case 19: return "Macadam";
                case 20: return "Planks";
                case 21: return "Sand";
                case 22: return "Shale";
                case 23: return "Tarmac";
                default: return "Unknown";
            }
        }

        public override string ToString()
        {
            string len = MSFSBlindAssist.Services.DistanceFormatter.FromFeet(Length, shortForm: true, round: false);
            string baseInfo;
            if (Width > 0)
            {
                string wid = MSFSBlindAssist.Services.DistanceFormatter.FromFeet(Width, shortForm: true, round: false);
                baseInfo = $"Runway {RunwayID} - {len} long, {wid} wide - {GetSurfaceType()}";
            }
            else
            {
                baseInfo = $"Runway {RunwayID} - {len} {GetSurfaceType()}";
            }

            if (ILSFreq > 0)
            {
                baseInfo += $" - ILS {ILSFreq:F2} ({ILSHeading:000}°)";
            }

            return baseInfo;
        }
}