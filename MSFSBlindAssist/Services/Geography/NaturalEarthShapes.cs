using System.Buffers.Binary;
using System.Text.Json;

namespace MSFSBlindAssist.Services.Geography;

/// <summary>The Natural Earth layers the location database keeps. The byte value is stored in
/// the database; never renumber a member without bumping the schema version.</summary>
public enum NaturalEarthLayer : byte
{
    /// <summary>Named oceans, seas, bays, straits (ne_10m_geography_marine_polys).</summary>
    Marine = 1,
    Lake = 2,
    /// <summary>The sea coastline, as open lines (ne_10m_coastline).</summary>
    Coastline = 3,
    /// <summary>Mountain ranges, deserts, plateaus… (ne_10m_geography_regions_polys).</summary>
    Region = 4,
    /// <summary>States and provinces (ne_10m_admin_1_states_provinces).</summary>
    State = 5,
}

/// <summary>One stored shape: a polygon PART (outer ring first, then holes) or a chunk of a line.
/// <see cref="Extra"/> is the country of a state, the spoken kind of a region, or null.</summary>
internal sealed record ShapeRecord(
    NaturalEarthLayer Layer, string Name, string? Extra, bool IsLine, IReadOnlyList<double[]> Rings,
    double MinLon, double MaxLon, double MinLat, double MaxLat);

/// <summary>
/// Reads Natural Earth GeoJSON into <see cref="ShapeRecord"/>s and packs their coordinates into
/// compact blobs. A multi-part feature becomes one record per part: Natural Earth splits shapes
/// at ±180°, so a whole feature's bounding box (Fiji, Chukotka) would span the globe and be
/// decoded on every query at its latitude. Lines are cut into chunks of at most
/// <see cref="LineChunkVertices"/> vertices that share their joining vertex, so the nearest-coast
/// search only decodes the chunks near the aircraft. Property names differ per layer (lower case
/// on marine, lakes and states; upper case on regions), so each layer is mapped explicitly.
/// </summary>
internal static class NaturalEarthShapes
{
    internal const int LineChunkVertices = 64;

    /// <summary>The pinned release every download uses.</summary>
    internal const string Release = "v5.1.2";

    internal static readonly IReadOnlyDictionary<NaturalEarthLayer, string> LayerFiles = new Dictionary<NaturalEarthLayer, string>
    {
        [NaturalEarthLayer.Marine] = "ne_10m_geography_marine_polys",
        [NaturalEarthLayer.Lake] = "ne_10m_lakes",
        [NaturalEarthLayer.Coastline] = "ne_10m_coastline",
        [NaturalEarthLayer.Region] = "ne_10m_geography_regions_polys",
        [NaturalEarthLayer.State] = "ne_10m_admin_1_states_provinces",
    };

    internal static IEnumerable<ShapeRecord> Read(NaturalEarthLayer layer, Stream geoJson)
    {
        using var doc = JsonDocument.Parse(geoJson, new JsonDocumentOptions { MaxDepth = 256 });
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
                continue;
            feature.TryGetProperty("properties", out var props);
            if (!Describe(layer, props, out string name, out string? extra)) continue;

            string type = geometry.GetProperty("type").GetString() ?? "";
            var coords = geometry.GetProperty("coordinates");
            switch (type)
            {
                case "Polygon":
                    if (Polygon(layer, name, extra, coords) is { } p) yield return p;
                    break;
                case "MultiPolygon":
                    foreach (var part in coords.EnumerateArray())
                        if (Polygon(layer, name, extra, part) is { } mp) yield return mp;
                    break;
                case "LineString":
                    foreach (var chunk in LineChunks(layer, name, extra, coords)) yield return chunk;
                    break;
                case "MultiLineString":
                    foreach (var line in coords.EnumerateArray())
                        foreach (var chunk in LineChunks(layer, name, extra, line)) yield return chunk;
                    break;
            }
        }
    }

    /// <summary>The name and extra text a layer's feature is stored under; false to skip it
    /// (an unnamed sea or lake cannot be spoken; continents are not regions worth saying).</summary>
    private static bool Describe(NaturalEarthLayer layer, JsonElement props, out string name, out string? extra)
    {
        extra = null;
        switch (layer)
        {
            case NaturalEarthLayer.Coastline:
                name = "";
                return true;
            case NaturalEarthLayer.Region:
                string? cls = Prop(props, "FEATURECLA", "featurecla");
                name = Prop(props, "NAME_EN", "name_en", "NAME", "name") ?? "";
                if (!LocationPhrases.IsRegionShown(cls) || string.IsNullOrWhiteSpace(name)) return false;
                name = LocationPhrases.TidyName(name.Trim());
                extra = LocationPhrases.RegionKind(cls!);
                return true;
            case NaturalEarthLayer.State:
                // Stored as "gn_a1_code|iso_a2|country". Lookups use the ISO code to say the country
                // in GeoNames' spelling ("United States"). The gn_a1_code is kept in the stored format
                // but never used: French department numbers collide with GeoNames' region numbers
                // (LOC-9), so the area comes from the nearest GeoNames town inside this outline.
                name = (Prop(props, "name", "NAME", "name_en") ?? "").Trim();
                string country = (Prop(props, "admin", "ADMIN", "geonunit") ?? "").Trim();
                string iso = (Prop(props, "iso_a2", "ISO_A2") ?? "").Trim();
                string geoNamesCode = (Prop(props, "gn_a1_code", "GN_A1_CODE") ?? "").Trim();
                extra = geoNamesCode + "|" + iso + "|" + country;
                return name.Length > 0 || country.Length > 0;
            default: // Marine, Lake
                name = (Prop(props, "name", "NAME", "name_en") ?? "").Trim();
                return name.Length > 0;
        }
    }

    private static string? Prop(JsonElement props, params string[] keys)
    {
        if (props.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in keys)
            if (props.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                string? s = v.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        return null;
    }

    private static ShapeRecord? Polygon(NaturalEarthLayer layer, string name, string? extra, JsonElement polygon)
    {
        var rings = new List<double[]>();
        foreach (var ring in polygon.EnumerateArray())
        {
            var flat = Flatten(ring);
            if (flat.Length >= 6) rings.Add(flat);
        }
        if (rings.Count == 0) return null;
        var (minLon, maxLon, minLat, maxLat) = Bounds(rings[0]);
        return new ShapeRecord(layer, name, extra, false, rings, minLon, maxLon, minLat, maxLat);
    }

    private static IEnumerable<ShapeRecord> LineChunks(NaturalEarthLayer layer, string name, string? extra, JsonElement line)
    {
        var flat = Flatten(line);
        int n = flat.Length / 2;
        if (n < 2) yield break;
        for (int start = 0; start < n - 1; start += LineChunkVertices - 1)
        {
            int count = Math.Min(LineChunkVertices, n - start);
            var chunk = flat.AsSpan(start * 2, count * 2).ToArray();
            var (minLon, maxLon, minLat, maxLat) = Bounds(chunk);
            yield return new ShapeRecord(layer, name, extra, true, new[] { chunk }, minLon, maxLon, minLat, maxLat);
        }
    }

    private static double[] Flatten(JsonElement positions)
    {
        var list = new List<double>(positions.GetArrayLength() * 2);
        foreach (var p in positions.EnumerateArray())
        {
            if (p.GetArrayLength() < 2) continue;
            list.Add(p[0].GetDouble());
            list.Add(p[1].GetDouble());
        }
        return list.ToArray();
    }

    internal static (double MinLon, double MaxLon, double MinLat, double MaxLat) Bounds(ReadOnlySpan<double> flat)
    {
        double minLon = double.MaxValue, maxLon = double.MinValue, minLat = double.MaxValue, maxLat = double.MinValue;
        for (int i = 0; i + 1 < flat.Length; i += 2)
        {
            minLon = Math.Min(minLon, flat[i]); maxLon = Math.Max(maxLon, flat[i]);
            minLat = Math.Min(minLat, flat[i + 1]); maxLat = Math.Max(maxLat, flat[i + 1]);
        }
        return (minLon, maxLon, minLat, maxLat);
    }

    /// <summary>Packs rings as little-endian [ring count][per ring: vertex count, then lon/lat as
    /// float32]. float32 keeps about two metres at ±180°, far finer than the 1:10m data.</summary>
    internal static byte[] Encode(IReadOnlyList<double[]> rings)
    {
        int size = 4;
        foreach (var r in rings) size += 4 + (r.Length / 2) * 8;
        var bytes = new byte[size];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, rings.Count);
        int o = 4;
        foreach (var r in rings)
        {
            int n = r.Length / 2;
            BinaryPrimitives.WriteInt32LittleEndian(span[o..], n);
            o += 4;
            for (int i = 0; i < n * 2; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(span[o..], (float)r[i]);
                o += 4;
            }
        }
        return bytes;
    }

    internal static List<double[]> Decode(ReadOnlySpan<byte> blob)
    {
        var rings = new List<double[]>();
        if (blob.Length < 4) return rings;
        int count = BinaryPrimitives.ReadInt32LittleEndian(blob);
        int o = 4;
        for (int k = 0; k < count && o + 4 <= blob.Length; k++)
        {
            int n = BinaryPrimitives.ReadInt32LittleEndian(blob[o..]);
            o += 4;
            if (n < 0 || o + n * 8 > blob.Length) break;
            var ring = new double[n * 2];
            for (int i = 0; i < n * 2; i++)
            {
                ring[i] = BinaryPrimitives.ReadSingleLittleEndian(blob[o..]);
                o += 4;
            }
            rings.Add(ring);
        }
        return rings;
    }
}
