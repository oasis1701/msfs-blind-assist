using System.Text.Json;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// One Overpass element → one AirportFeature, or null. STRICT on purpose: a named building is a
/// feature only when its name says aviation. The earlier "any named building is an office" rule
/// turned EGLL's car parks, bus station and escape shafts — and 174 numbered buildings at EDDF —
/// into spoken, routable places. Road fuel (amenity=fuel) is not aircraft fuel; a bare ref number
/// is not a name.
/// </summary>
public static class OsmFeatureClassifier
{
    public static AirportFeature? Classify(JsonElement el)
    {
        if (!el.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object) return null;
        string aeroway = Tag(tags, "aeroway"), building = Tag(tags, "building");
        if (aeroway is "taxiway" or "parking_position" or "gate" or "holding_position" or "runway") return null;

        string name = Tag(tags, "name"), op = Tag(tags, "operator");
        if (name.Length == 0 && (aeroway is "apron" or "terminal" || building == "terminal"))
        {
            string r = Tag(tags, "ref");
            if (r.Any(char.IsLetter) && !r.Contains(';')) name = r;
        }
        string nameAndOp = (name + " " + op).Trim();

        FeatureKind? kind = aeroway switch
        {
            "terminal" => TerminalKind(tags, name, nameAndOp),
            "hangar" => FeatureKind.Hangar,
            "apron" => FeatureLexicon.Deice.IsMatch(name) ? FeatureKind.DeicePad : FeatureKind.Apron,
            "tower" or "control_tower" => FeatureKind.Tower,
            "fuel" => FeatureKind.Fuel,
            "helipad" => FeatureKind.Helipad,
            _ => null,
        };
        if (kind == null)
        {
            if (building == "hangar") kind = FeatureKind.Hangar;
            else if (building == "terminal") kind = TerminalKind(tags, name, nameAndOp);
            else if (Tag(tags, "man_made") == "tower" && Tag(tags, "tower:type") == "aircraft_control") kind = FeatureKind.Tower;
            else if (Tag(tags, "amenity") == "fire_station") kind = FeatureKind.FireStation;
            else if (name.Length > 0 && (Tag(tags, "office").Length > 0 || building.Length > 0)
                     && !FeatureLexicon.NotAirside.IsMatch(name))
            {
                if (FeatureLexicon.Fbo.IsMatch(nameAndOp)) kind = FeatureKind.Fbo;
                else if (FeatureLexicon.Cargo.IsMatch(name)) kind = FeatureKind.Cargo;
            }
        }
        if (kind == null) return null;

        IReadOnlyList<LatLon>? footprint =
            kind is FeatureKind.Apron or FeatureKind.DeicePad or FeatureKind.Terminal or FeatureKind.Concourse ? Footprint(el) : null;
        if (!TryPoint(el, footprint, out double lat, out double lon)) return null;

        return new AirportFeature
        {
            Kind = kind.Value, Name = name.Trim(), Lat = lat, Lon = lon, Footprint = footprint,
            Source = FeatureSource.Osm, Detail = op.Length > 0 && kind == FeatureKind.Fbo ? $"operator {op}" : null,
        };
    }

    private static FeatureKind TerminalKind(JsonElement tags, string name, string nameAndOp)
        => FeatureLexicon.Concourse.IsMatch(name) ? FeatureKind.Concourse
         : Tag(tags, "terminal:type") == "general_aviation" || FeatureLexicon.Fbo.IsMatch(nameAndOp) ? FeatureKind.Fbo
         : FeatureLexicon.Cargo.IsMatch(name) ? FeatureKind.Cargo
         : FeatureKind.Terminal;

    /// <summary>Node position → footprint centroid (when it lies inside) → centre of `bounds`
    /// (what `out tags geom;` gives every way and relation) → the stand-line midpoint helper.</summary>
    private static bool TryPoint(JsonElement el, IReadOnlyList<LatLon>? footprint, out double lat, out double lon)
    {
        lat = 0; lon = 0;
        if (el.TryGetProperty("lat", out var la) && el.TryGetProperty("lon", out var lo))
        { lat = la.GetDouble(); lon = lo.GetDouble(); return true; }
        if (footprint != null && footprint.Count >= 3)
        {
            var c = SurroundingsGeometry.Centroid(footprint);
            if (SurroundingsGeometry.Contains(footprint, c.Lat, c.Lon)) { lat = c.Lat; lon = c.Lon; return true; }
        }
        if (el.TryGetProperty("bounds", out var b)
            && b.TryGetProperty("minlat", out var a1) && b.TryGetProperty("maxlat", out var a2)
            && b.TryGetProperty("minlon", out var o1) && b.TryGetProperty("maxlon", out var o2))
        { lat = (a1.GetDouble() + a2.GetDouble()) / 2.0; lon = (o1.GetDouble() + o2.GetDouble()) / 2.0; return true; }
        return OsmTaxiSource.TryRepresentativePoint(el, out lat, out lon);
    }

    private static string Tag(JsonElement tags, string key)
        => tags.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    /// <summary>A closed way's vertices (closing duplicate dropped), or null. Relations (multipolygons) arrive with `center` only.</summary>
    private static IReadOnlyList<LatLon>? Footprint(JsonElement el)
    {
        if (!el.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Array) return null;
        var pts = new List<LatLon>();
        foreach (var g in geom.EnumerateArray())
            if (g.TryGetProperty("lat", out var la) && g.TryGetProperty("lon", out var lo))
                pts.Add(new LatLon(la.GetDouble(), lo.GetDouble()));
        if (pts.Count >= 2 && pts[0] == pts[^1]) pts.RemoveAt(pts.Count - 1);
        return pts.Count >= 3 ? pts : null;
    }
}
