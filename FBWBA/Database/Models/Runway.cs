using System;

namespace FBWBA.Database.Models
{
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
            string baseInfo = $"Runway {RunwayID} - {Length:0}m {GetSurfaceType()}";

            if (ILSFreq > 0)
            {
                baseInfo += $" - ILS {ILSFreq:F2} ({ILSHeading:000}°)";
            }

            return baseInfo;
        }
    }
}