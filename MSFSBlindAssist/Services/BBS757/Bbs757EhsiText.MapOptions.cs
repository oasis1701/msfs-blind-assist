namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The map options' rows (plan 28), from <see cref="Bbs757EhsiReading.MapOptions"/>, as BlueBird draws them: the tuned
/// stations and the items its loop draws, each inside the screen's clip among the map's items by its place; and the VOR
/// course lines after them. None of it is spoken; EXCESS DATA stays plan 26's message.
/// </summary>
public static partial class Bbs757EhsiText
{
    // The app's names for what the screen draws as symbols with a label (BlueBird prints no kind word).
    private const string AirportWord = "airport", WaypointWord = "waypoint", NdbWord = "NDB", NavaidWord = "navaid",
        TunedWord = "tuned";

    // The app's names for the course lines' receivers: the screen prints only the course by the line.
    private const string VorLeftName = "VOR L", VorRightName = "VOR R";

    /// <summary>The tuned stations ("Map: VORTAC SEA, tuned, 0.5 NM, 173° left") and the loop's items ("Map: VOR/DME PAE,
    /// …", "Map: airport KBFI, …", "Map: waypoint HAVHO, …", "Map: NDB FHR, …"), each only inside the screen's clip: one the
    /// loop counted but the clip cuts is not listed.</summary>
    private static void AddMapOptions(List<MapItem> items, Bbs757EhsiReading r, string? center)
    {
        if (r.MapOptions is not { } o) return;
        foreach (var (item, tuned) in o.Tuned.Select(t => (t, true)).Concat(o.Drawn.Select(d => (d, false))))
        {
            if (Where(r, item.Latitude, item.Longitude, center) is not { } at) continue;
            items.Add(new MapItem(at.Y, at.X, "Map: " + OptionName(item, o.Kinds) + (tuned ? ", " + TunedWord : "") + ", " + at.Place));
        }
    }

    /// <summary>An item's name: its kind's word, then its label when it has one (an unnamed point is not given a name).</summary>
    private static string OptionName(Bbs757EhsiMapOption item, Bbs757EhsiNavaidKinds kinds)
    {
        string word = item.Kind switch
        {
            Bbs757EhsiMapItemKind.Navaid => NavaidKindWord(item.NavaidKind, kinds),
            Bbs757EhsiMapItemKind.Airport => AirportWord,
            Bbs757EhsiMapItemKind.Waypoint => WaypointWord,
            _ => NdbWord,
        };
        return item.Name.Length > 0 ? word + " " + item.Name : word;
    }

    private static string NavaidKindWord(int kind, Bbs757EhsiNavaidKinds k) =>
        kind == k.Vor ? "VOR"
        : kind == k.VorDme ? "VOR/DME"
        : kind == k.Vortac ? "VORTAC"
        : kind == k.Tacan ? "TACAN"
        : kind == k.Dme ? "DME"
        : NavaidWord;

    /// <summary>A course line is sampled every this many pixels along it.</summary>
    private const double CourseLineSamplePixels = 5;

    /// <summary>The most samples along one course line.</summary>
    private const int MaxCourseLineSamples = 1024;

    /// <summary>
    /// The VOR course lines, "Map: VOR L course 187 through SEA" (the course as its label prints it), after the map's
    /// items, each only where it crosses the screen's clip (<see cref="CourseLineOnScreen"/>).
    /// </summary>
    private static IEnumerable<string> CourseLineRows(Bbs757EhsiReading r) =>
        (r.MapOptions?.CourseLines ?? Array.Empty<Bbs757EhsiCourseLine>()).Where(line => CourseLineOnScreen(r, line)).Select(line =>
            "Map: " + (line.Left ? VorLeftName : VorRightName) + " course " + ThreeDigits(line.Course) + " through " + line.Station);

    /// <summary>
    /// Whether a course line shows inside the clip. BlueBird draws it through its station both ways along the course plus
    /// the magnetic variation at the station (f23494), clipped to the screen. In MAP the screen's up is the heading number
    /// (<see cref="Bbs757EhsiReading.Number"/>, magnetic or true as the screen's own number), so the line runs at the course
    /// minus that number from the station's pixel: sampled along the stretch of the line within half the screen's diagonal
    /// of the point nearest the screen's center, which covers every pixel of it the screen can show, both ways. In PLAN
    /// (true north up) the reader has no magnetic variation to turn the line with, and the line runs both ways through the
    /// station and usually crosses the screen, so it is listed whenever its conditions hold, with no on-screen test
    /// (measured 2026-11-12: the line was drawn through SEA with SEA outside the clip). In MAP, not on the screen for a range not above 0 or any input that is not a finite number. The samples
    /// are counted by an index up to <see cref="MaxCourseLineSamples"/>, never by adding a step to a far coordinate (at a
    /// tiny range the station's pixel is so far out that adding a few pixels no longer changes it).
    /// </summary>
    private static bool CourseLineOnScreen(Bbs757EhsiReading r, Bbs757EhsiCourseLine line)
    {
        var c = r.Constants;
        var map = r.Map;
        if (!(r.Range > 0) || !double.IsFinite(r.Range)) return false;
        var (x, y, _, _) = Bbs757EhsiGeometry.Project(map, r.Range, c.RadiusFactor, line.Latitude, line.Longitude);
        if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
        bool Inside(double px, double py) =>
            Bbs757EhsiGeometry.OnScreen(map, r.Centered, px, py, c.RadiusFactor, c.AircraftYFactor, c.ClipInset);
        // PLAN: the line is listed whenever its conditions hold (see the summary).
        if (Is(r, Bbs757EhsiMode.Plan)) return double.IsFinite(line.Course);
        double angle = (line.Course - r.Number) * Math.PI / 180.0;
        if (!double.IsFinite(angle) || !double.IsFinite(map.Width) || !double.IsFinite(map.Height)) return false;
        double sin = Math.Sin(angle), cos = Math.Cos(angle);
        double half = Math.Sqrt(map.Width * map.Width + map.Height * map.Height) / 2;
        // Along the line t pixels from the station: (x + t sin, y - t cos); nearest the screen's center at t0.
        double t0 = (map.Width / 2 - x) * sin - (map.Height / 2 - y) * cos;
        if (!double.IsFinite(half) || !double.IsFinite(t0)) return false;
        // At most the cap of samples, spread wider for a screen size no build has (a torn width).
        int samples = (int)Math.Clamp(Math.Ceiling(2 * half / CourseLineSamplePixels), 1, MaxCourseLineSamples);
        double step = 2 * half / samples;
        for (int i = 0; i <= samples; i++)
        {
            double t = t0 - half + i * step;
            if (Inside(x + t * sin, y - t * cos)) return true;
        }
        return false;
    }
}
