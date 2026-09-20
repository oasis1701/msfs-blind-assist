using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.Surroundings;

namespace MSFSBlindAssist.Tests;

public class OsmFeatureSourceTests
{
    [Fact]
    public void The_area_query_is_scoped_to_the_icao_tagged_aerodrome_and_asks_for_full_geometry()
    {
        string q = OsmFeatureSource.BuildAreaQuery("ktiw\";x");
        Assert.Contains("area[\"aeroway\"=\"aerodrome\"][\"icao\"=\"KTIWX\"]->.ad;", q);   // sanitised, upper-cased
        Assert.Contains("(area.ad)", q);
        Assert.EndsWith(");out tags geom;", q);
        Assert.DoesNotContain("center", q);      // Overpass honours only the LAST geometry modifier
        Assert.DoesNotContain("\"amenity\"~\"^(fuel", q);   // road fuel is not asked for
    }

    [Fact]
    public void The_fallback_query_is_a_3_km_radius_without_the_generic_named_building_clauses()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            string q = OsmFeatureSource.BuildFallbackQuery(47.2679, -122.5781);
            Assert.Contains("(around:3000,47.2679,-122.5781)", q);
            Assert.DoesNotContain("[\"office\"][\"name\"]", q);
            Assert.DoesNotContain("[\"building\"][\"name\"]", q);
            Assert.EndsWith(");out tags geom;", q);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void A_real_response_parses_into_features()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "osm-features-area-ktiw.json"));
        Assert.Equal(25, OsmFeatureSource.Parse(json).Count);
    }

    private static AirportFeature At(double lat, double lon) => new() { Kind = FeatureKind.Hangar, Lat = lat, Lon = lon, Source = FeatureSource.Osm };

    [Fact]
    public void Fallback_results_are_kept_only_inside_the_airport_box_plus_margin_and_dropped_without_a_box()
    {
        var box = new AirportFacilities { Icao = "KTIW", LeftLon = -122.579353, RightLon = -122.573303, TopLat = 47.274742, BottomLat = 47.260826 };
        var inside = At(47.2712, -122.5731);      // the tower, 15 m outside the bare box
        var chevron = At(47.2712, -122.5500);     // a road filling station ~1.7 km east
        var kept = OsmFeatureSource.KeepInsideBox(new[] { inside, chevron }, box);
        Assert.Same(inside, Assert.Single(kept));
        Assert.Empty(OsmFeatureSource.KeepInsideBox(new[] { inside, chevron }, null));   // fail closed
    }
}
