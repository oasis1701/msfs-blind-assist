namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The map extras' rows (plan 28), from <see cref="Bbs757EhsiReading.MapExtras"/>, as BlueBird draws them: TERR's word,
/// its two elevations with their colors and the terrain picture's presence at the top right; the wind at the bottom left;
/// the runways and the deceleration points among the map's items, each only where the screen's clip lets it be drawn.
/// Nothing here is spoken.
/// </summary>
public static partial class Bbs757EhsiText
{
    /// <summary>
    /// TERR, while the IRS is valid and the button is on, in every mode: "Top right: TERR", then "Top right: terrain 031
    /// red, 004 amber" (the highest, then the lowest, three digits as drawn, the color as drawn; no color word where the
    /// reader cannot tell), then, while BlueBird draws the terrain picture (not centered, not in PLAN), "Map: terrain
    /// shown, not read" in MAP, "Terrain picture shown, not read" in VOR and ILS (no map there): a picture, which the
    /// reader cannot list.
    /// </summary>
    private static void TerrRows(List<string> rows, Bbs757EhsiReading r)
    {
        if (r.MapExtras is not { Terr: true } x || !r.Valid) return;
        rows.Add("Top right: " + Word(x.TerrWord));
        rows.Add("Top right: terrain " + TerrainText(x.Highest) + ", " + TerrainText(x.Lowest));
        if (x.TerrainPicture && !r.Centered && !Is(r, Bbs757EhsiMode.Plan))
            rows.Add(Is(r, Bbs757EhsiMode.Map) ? "Map: terrain shown, not read" : "Terrain picture shown, not read");
    }

    private static string TerrainText(Bbs757EhsiTerrainNumber n) =>
        n.Shown.ToString("D3", Inv) + n.Color switch
        {
            Bbs757EhsiTerrainColor.Red => " red",
            Bbs757EhsiTerrainColor.Amber => " amber",
            Bbs757EhsiTerrainColor.Green => " green",
            _ => "",
        };

    /// <summary>
    /// The wind, in MAP, VOR and ILS while the IRS is valid (never PLAN): "Bottom left: wind 70 kt from 30° left" (the
    /// speed truncated as drawn; where it comes from against the top of the map: the wind direction minus the map's
    /// rotation, "from ahead", "from behind"), or the screen's dashes, "Bottom left: wind ---", while BlueBird's show flag
    /// is clear (the reader takes the flag as BlueBird left it: the draw itself clears it under its minimum ground speed, and
    /// the reader never redoes that test or the speed's hysteresis). Shown but with a wind input not delivered yet: "Bottom
    /// left: wind not read yet", never a stale speed.
    /// </summary>
    private static void WindRow(List<string> rows, Bbs757EhsiReading r)
    {
        if (r.MapExtras is not { } x || !r.Valid || !(Is(r, Bbs757EhsiMode.Map) || Is(r, Bbs757EhsiMode.Vor) || Is(r, Bbs757EhsiMode.Ils))) return;
        if (!x.WindShown)
        {
            rows.Add("Bottom left: wind " + Word(x.NoWindWord));
            return;
        }
        if (x.WindSpeed is not { } speed || x.WindDirection is not { } direction)
        {
            rows.Add("Bottom left: wind not read yet");
            return;
        }
        rows.Add("Bottom left: wind " + Math.Truncate(speed).ToString("F0", Inv) + " kt from "
            + Bbs757EhsiGeometry.Angle(Bbs757EhsiGeometry.OffTop(direction, r.Map.Rotation)));
    }

    /// <summary>The runways ("Map: runway 16L, 2.0 NM, 15° left": the place of its end nearer the map's center, listed when
    /// either end is inside the clip) and the deceleration points ("Map: deceleration starts, …", "Map: deceleration ends,
    /// …", each inside the clip), among the map's items.</summary>
    private static void AddMapExtras(List<MapItem> items, Bbs757EhsiReading r, string? center)
    {
        if (r.MapExtras is not { } x) return;
        foreach (var runway in x.Runways ?? Array.Empty<Bbs757EhsiRunway>())
        {
            var first = Locate(r, runway.FirstLatitude, runway.FirstLongitude, center);
            var second = Locate(r, runway.SecondLatitude, runway.SecondLongitude, center);
            if (!first.OnScreen && !second.OnScreen) continue;
            var near = first.Nm <= second.Nm ? first : second;
            // Ordered by where it shows: its nearer end when that is on the screen, else the other.
            var shown = near.OnScreen ? near : (first.OnScreen ? first : second);
            items.Add(new MapItem(shown.Y, shown.X, "Map: runway " + runway.Name + ", " + near.Place));
        }
        foreach (var (points, word) in new[] { (x.DecelerationStarts, "starts"), (x.DecelerationEnds, "ends") })
            foreach (var p in points ?? Array.Empty<Bbs757EhsiMapPoint>())
                if (Where(r, p.Latitude, p.Longitude, center) is { } at)
                    items.Add(new MapItem(at.Y, at.X, "Map: deceleration " + word + ", " + at.Place));
    }

    /// <summary>A point's pixel, distance from the map's center, place in words and whether the clip lets it be drawn
    /// (<see cref="Where"/> without the clip's refusal).</summary>
    private static (double X, double Y, double Nm, string Place, bool OnScreen) Locate(Bbs757EhsiReading r, double lat, double lon,
        string? center)
    {
        var c = r.Constants;
        var (x, y, nm, bearing) = Bbs757EhsiGeometry.Project(r.Map, r.Range, c.RadiusFactor, lat, lon);
        bool onScreen = Bbs757EhsiGeometry.OnScreen(r.Map, r.Centered, x, y, c.RadiusFactor, c.AircraftYFactor, c.ClipInset);
        string place = center is not null
            ? Bbs757EhsiGeometry.PlanPlace(nm, bearing, center)
            : Bbs757EhsiGeometry.Place(nm, Bbs757EhsiGeometry.OffTop(bearing, r.Map.Rotation));
        return (x, y, nm, place, onScreen);
    }
}
