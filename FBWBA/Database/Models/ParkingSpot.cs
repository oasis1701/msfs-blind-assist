using System;

namespace FBWBA.Database.Models
{
    public class ParkingSpot
    {
        public int Id { get; set; }
        public string AirportICAO { get; set; }
        public string Name { get; set; }
        public int Number { get; set; }
        public int Type { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Heading { get; set; }
        public double Radius { get; set; }

        public ParkingSpot()
        {
            AirportICAO = string.Empty;
            Name = string.Empty;
        }

        public string GetParkingType()
        {
            switch (Type)
            {
                case 1: return "None";
                case 2: return "Ramp GA";
                case 3: return "Ramp GA Small";
                case 4: return "Ramp GA Medium";
                case 5: return "Ramp GA Large";
                case 6: return "Ramp Cargo";
                case 7: return "Ramp Military Cargo";
                case 8: return "Ramp Military Combat";
                case 9: return "Gate Small";
                case 10: return "Gate Medium";
                case 11: return "Gate Large";
                case 12: return "Dock GA";
                default: return "Parking";
            }
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Name) && Number > 0)
                return $"{Name} {Number} - {GetParkingType()}";
            else if (!string.IsNullOrEmpty(Name))
                return $"{Name} - {GetParkingType()}";
            else if (Number > 0)
                return $"Spot {Number} - {GetParkingType()}";
            else
                return $"Parking - {GetParkingType()}";
        }
    }
}