using System.Globalization;

namespace MSFSBlindAssist.Models;

/// <summary>The Location Info window's lists, filled by
/// <c>Services/Geography/OfflineLocationService</c> from the offline location data.</summary>
public class LocationData
    {
        public List<NearbyPlace> NearbyPlaces { get; set; }
        public List<NearbyPlace> MajorCities { get; set; }
        public RegionalContext Regional { get; set; }
        public CardinalDirections Directions { get; set; }
        public Dictionary<string, List<Landmark>> CategorizedLandmarks { get; set; }

        public LocationData()
        {
            NearbyPlaces = new List<NearbyPlace>();
            MajorCities = new List<NearbyPlace>();
            CategorizedLandmarks = new Dictionary<string, List<Landmark>>();
            Regional = new RegionalContext();
            Directions = new CardinalDirections();
        }
    }

    public class NearbyPlace
    {
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        public string Country { get; set; } = "";
        public int Population { get; set; }
        /// <summary>In <see cref="Unit"/>.</summary>
        public double Distance { get; set; }
        /// <summary>"miles" or "kilometers", as the GeoNames tab sets it.</summary>
        public string Unit { get; set; } = "miles";
        public string Direction { get; set; } = "";
        public string Type { get; set; } = ""; // "city", "town", "major_city"

        public override string ToString()
        {
            var location = string.IsNullOrEmpty(State) ? Name : $"{Name}, {State}";
            if (!string.IsNullOrEmpty(Country))
            {
                location += $", {Country}";
            }

            // Invariant: the window reads "1,234 people" and "12 miles" on any Windows region.
            if (Population > 0 && (Type == "city" || Type == "major_city"))
            {
                return string.Create(CultureInfo.InvariantCulture, $"{location} (pop. {Population:N0}) - {Distance:F0} {Unit} {Direction}");
            }
            return string.Create(CultureInfo.InvariantCulture, $"{location} - {Distance:F0} {Unit} {Direction}");
        }
    }

    public class RegionalContext
    {
        public string State { get; set; } = "";
        public string Country { get; set; } = "";
    }

    public class CardinalDirections
    {
        public string North { get; set; } = "";
        public string NorthDistance { get; set; } = "";
        public string East { get; set; } = "";
        public string EastDistance { get; set; } = "";
        public string South { get; set; } = "";
        public string SouthDistance { get; set; } = "";
        public string West { get; set; } = "";
        public string WestDistance { get; set; } = "";

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
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        public string Country { get; set; } = "";
        /// <summary>In <see cref="Unit"/>.</summary>
        public double Distance { get; set; }
        /// <summary>"miles" or "kilometers", as the GeoNames tab sets it.</summary>
        public string Unit { get; set; } = "miles";
        public string Direction { get; set; } = "";
        public string Type { get; set; } = ""; // the LocationCategory, lower case

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
            return string.Create(CultureInfo.InvariantCulture, $"{location} - {Distance:F0} {Unit} {Direction}");
        }
}
