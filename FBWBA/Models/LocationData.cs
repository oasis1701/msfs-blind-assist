using System;
using System.Collections.Generic;

namespace FBWBA.Models
{
    public class LocationData
    {
        public string LocalTime { get; set; }
        public List<NearbyPlace> NearbyPlaces { get; set; }
        public List<NearbyPlace> MajorCities { get; set; }
        public RegionalContext Regional { get; set; }
        public CardinalDirections Directions { get; set; }
        public List<Landmark> Landmarks { get; set; }
        public Dictionary<string, List<Landmark>> CategorizedLandmarks { get; set; }
        public WeatherInfo Weather { get; set; }

        public LocationData()
        {
            NearbyPlaces = new List<NearbyPlace>();
            MajorCities = new List<NearbyPlace>();
            Landmarks = new List<Landmark>();
            CategorizedLandmarks = new Dictionary<string, List<Landmark>>();
            Regional = new RegionalContext();
            Directions = new CardinalDirections();
        }
    }

    public class NearbyPlace
    {
        public string Name { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public int Population { get; set; }
        public double Distance { get; set; }
        public string Direction { get; set; }
        public string Type { get; set; } // "city", "town", "airport", etc.

        public override string ToString()
        {
            var location = string.IsNullOrEmpty(State) ? Name : $"{Name}, {State}";
            if (!string.IsNullOrEmpty(Country))
            {
                location += $", {Country}";
            }

            if (Population > 0 && (Type == "city" || Type == "major_city"))
            {
                return $"{location} (pop. {Population:N0}) - {Distance:F0} miles {Direction}";
            }
            else
            {
                return $"{location} - {Distance:F0} miles {Direction}";
            }
        }
    }

    public class RegionalContext
    {
        public string State { get; set; }
        public string Country { get; set; }
        public string County { get; set; }
        public string MajorCity { get; set; }
        public double MajorCityDistance { get; set; }
        public string MajorCityDirection { get; set; }

        public override string ToString()
        {
            var result = $"State: {State}, {Country}";
            if (!string.IsNullOrEmpty(County))
            {
                result += $"\nCounty: {County}";
            }
            if (!string.IsNullOrEmpty(MajorCity))
            {
                result += $"\nMajor City: {MajorCity} - {MajorCityDistance:F0} miles {MajorCityDirection}";
            }
            return result;
        }
    }

    public class CardinalDirections
    {
        public string North { get; set; }
        public string NorthDistance { get; set; }
        public string East { get; set; }
        public string EastDistance { get; set; }
        public string South { get; set; }
        public string SouthDistance { get; set; }
        public string West { get; set; }
        public string WestDistance { get; set; }

        public override string ToString()
        {
            var result = "";
            if (!string.IsNullOrEmpty(North))
                result += $"North: {North} - {NorthDistance}\r\n";
            if (!string.IsNullOrEmpty(East))
                result += $"East: {East} - {EastDistance}\r\n";
            if (!string.IsNullOrEmpty(South))
                result += $"South: {South} - {SouthDistance}\r\n";
            if (!string.IsNullOrEmpty(West))
                result += $"West: {West} - {WestDistance}";
            return result.TrimEnd('\r', '\n');
        }
    }

    public class Landmark
    {
        public string Name { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public double Distance { get; set; }
        public string Direction { get; set; }
        public string Type { get; set; } // "tower", "stadium", "mall", "park", etc.

        public override string ToString()
        {
            var location = Name;
            if (!string.IsNullOrEmpty(State))
            {
                location += $", {State}";
            }
            if (!string.IsNullOrEmpty(Country))
            {
                location += $", {Country}";
            }
            return $"{location} - {Distance:F0} miles {Direction}";
        }
    }

    public class WeatherInfo
    {
        public double? Temperature { get; set; }
        public string Conditions { get; set; }
        public string WindSpeed { get; set; }
        public string WindDirection { get; set; }
        public bool IsAvailable { get; set; }

        public override string ToString()
        {
            if (!IsAvailable) return "";

            var result = "";
            if (Temperature.HasValue)
                result += $"Temperature: {Temperature:F0}°F\n";
            if (!string.IsNullOrEmpty(Conditions))
                result += $"Conditions: {Conditions}\n";
            if (!string.IsNullOrEmpty(WindSpeed) && !string.IsNullOrEmpty(WindDirection))
                result += $"Wind: {WindSpeed} from {WindDirection}";

            return result.TrimEnd('\n');
        }
    }
}