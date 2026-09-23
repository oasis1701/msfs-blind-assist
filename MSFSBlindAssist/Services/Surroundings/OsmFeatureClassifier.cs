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

        string name = PreferredName(tags), op = Tag(tags, "operator");
        bool nameIsRef = false;
        if (name.Length == 0 && (aeroway is "apron" or "terminal" || building == "terminal"))
        {
            string r = Tag(tags, "ref");
            if (r.Any(char.IsLetter) && !r.Contains(';')) { name = r; nameIsRef = true; }
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
            Kind = kind.Value, Name = SpeakableName(name.Trim(), nameIsRef, kind.Value), Lat = lat, Lon = lon, Footprint = footprint,
            Source = FeatureSource.Osm, Detail = op.Length > 0 && kind == FeatureKind.Fbo ? $"operator {op}" : null,
        };
    }

    /// <summary>
    /// A designator borrowed from `ref` is given the word for what it is: "Apron A", "Terminal T2".
    /// A ref is a REFERENCE, and spoken bare it reached the pilot as "On the A." and "A, to the
    /// left, 100 metres" — which names nothing they can look for. It stays a PROPER name (it is
    /// OSM's own designator for this apron, and must still outrank an unnamed neighbour in the
    /// catalog merge), so only the wording changes.
    ///
    /// <para>Two refs are left alone: one carrying WHITESPACE, which is prose somebody put in the
    /// wrong tag rather than a designator (the live "De-icing pad"), and one that already says the
    /// kind's own word. A real `name` is never touched at all.</para>
    /// </summary>
    private static string SpeakableName(string name, bool fromRef, FeatureKind kind)
    {
        if (!fromRef || name.Any(char.IsWhiteSpace)) return name;
        string word = FeatureKindWords.Generic(kind);
        return word.Length == 0 || name.Contains(word, StringComparison.OrdinalIgnoreCase) ? name : $"{word} {name}";
    }

    private static FeatureKind TerminalKind(JsonElement tags, string name, string nameAndOp)
        => FeatureLexicon.Concourse.IsMatch(name) ? FeatureKind.Concourse
         : Tag(tags, "terminal:type") == "general_aviation" || FeatureLexicon.Fbo.IsMatch(nameAndOp) ? FeatureKind.Fbo
         : FeatureLexicon.Cargo.IsMatch(name) ? FeatureKind.Cargo
         : FeatureKind.Terminal;

    /// <summary>Node position → footprint centroid (when it lies inside) → centre of `bounds` (which
    /// the `geom` modifier gives every way and relation, so a relation with no outer ring to join, or
    /// of a kind that keeps no footprint, is placed here) → the stand-line midpoint helper.</summary>
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

    /// <summary>
    /// The name to SPEAK and to CLASSIFY by: <c>name:en</c> when OSM carries one, else <c>name</c>
    /// (review OV-4). OSM's <c>name</c> is the LOCAL name — Haneda's terminals are 第1旅客ターミナル,
    /// Narita's cargo sheds 第3貨物ビル — which a screen reader either spells out or reads in a
    /// language the pilot may not have, while <c>name:en</c> ("Terminal 1", "Cargo Building No.3") is
    /// what English signage and charts say. Classification reads the SAME name, never one for each:
    /// the kind words (cargo, pier, concourse…) live in the English one, so read by <c>name</c> alone
    /// Narita's cargo buildings were not features at all.
    /// </summary>
    private static string PreferredName(JsonElement tags)
    {
        string english = Tag(tags, "name:en");
        return english.Length > 0 ? english : Tag(tags, "name");
    }

    /// <summary>
    /// The outline, or null. A WAY: its own vertices, closing duplicate dropped. A RELATION (a
    /// multipolygon terminal or apron) has no geometry of its own — under <c>out body geom;</c> each
    /// member way carries its own <c>geometry</c>, and the outline is the largest ring its OUTER
    /// members close into (<see cref="RelationOutline"/>). Under the old <c>out tags geom;</c> a
    /// relation arrived with <c>bounds</c> and tags and NO members at all (measured live 2026-09-22,
    /// KATL relation 10189710 "Domestic Terminal"), so every multipolygon was measured to the centre
    /// of its bounding box. Both read their vertices through <see cref="Vertices"/>, so a way with a
    /// gap has no outline either and is placed at its bounds centre.
    /// </summary>
    private static IReadOnlyList<LatLon>? Footprint(JsonElement el)
    {
        if (el.TryGetProperty("members", out var members) && members.ValueKind == JsonValueKind.Array)
            return RelationOutline(members);
        if (!el.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Array) return null;
        var pts = Vertices(geom);
        if (pts == null) return null;
        if (pts.Count >= 2 && pts[0] == pts[^1]) pts.RemoveAt(pts.Count - 1);
        return pts.Count >= 3 ? pts : null;
    }

    /// <summary>
    /// The largest ring a relation's OUTER member ways close into
    /// (<see cref="OsmRingAssembler.LargestRing"/>), or null. Inner members — a courtyard, a grass
    /// island — are never part of an outline, and neither is a member way with a gap in its geometry
    /// (<see cref="Vertices"/>).
    /// </summary>
    private static IReadOnlyList<LatLon>? RelationOutline(JsonElement members)
    {
        var outerWays = new List<IReadOnlyList<LatLon>>();
        foreach (var m in members.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object || Tag(m, "type") != "way" || Tag(m, "role") != "outer") continue;
            if (!m.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Array) continue;
            var pts = Vertices(geom);
            if (pts != null && pts.Count >= 2) outerWays.Add(pts);
        }
        return OsmRingAssembler.LargestRing(outerWays);
    }

    /// <summary>
    /// A <c>geometry</c> array's vertices in order — the ONE vertex reader a way and a relation
    /// member share — or null when it has a GAP: an entry that is not an object carrying both
    /// <c>lat</c> and <c>lon</c>. Overpass prints a null vertex for a node outside an
    /// <c>out … (bbox)</c> clip, which these queries never use, and a shape is never joined across a
    /// gap: with one, a way has no outline and a relation member is left out of the join. (The way's
    /// own loop used to ask a null vertex for its <c>lat</c>, which throws, and that failed the whole
    /// airport's buildings fetch.) A coordinate that is not a number still throws, and the source
    /// turns that into a failed fetch.
    /// </summary>
    private static List<LatLon>? Vertices(JsonElement geom)
    {
        var pts = new List<LatLon>();
        foreach (var g in geom.EnumerateArray())
        {
            if (g.ValueKind != JsonValueKind.Object || !g.TryGetProperty("lat", out var la) || !g.TryGetProperty("lon", out var lo))
                return null;
            pts.Add(new LatLon(la.GetDouble(), lo.GetDouble()));
        }
        return pts;
    }
}
