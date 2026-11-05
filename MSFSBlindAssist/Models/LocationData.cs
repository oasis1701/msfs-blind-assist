using System.Globalization;

namespace MSFSBlindAssist.Models;

/// <summary>The Location Info window's Major Cities, filled by
/// <c>Services/Geography/OfflineLocationService</c> from the offline location data.</summary>
public class LocationData
    {
        public List<NearbyPlace> MajorCities { get; set; } = new();
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
