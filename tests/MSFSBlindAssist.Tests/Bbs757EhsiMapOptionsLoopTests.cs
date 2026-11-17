// BlueBird's map option loop (23406) replayed as a pure function: one item from each switched-on list per round, the range
// + 10 NM test, the kind, class, tuned, origin and destination, route and NDB-distance skips, the stop at the count limit
// at a round's end, when every list is used up, or after the round limit. The lists are shaped like plan 28's live ones at
// the KSEA gate (nearest first; the idents and distances the evidence lists), placed on this test's map frame. One airport
// is made up: KRNT is not in the evidence's list, added to reach the measured count of airports within range + 10 (6).

using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiMapOptionsLoopTests
{
    private const double CenterLat = 47.442946, CenterLon = -122.303648;

    private static readonly Bbs757EhsiMap Frame = new(CenterLat, CenterLon, 134.43, 352, 702.2, 704, 859);

    /// <summary>The measured build's constants (the layout's).</summary>
    internal static readonly Bbs757EhsiLoopRules Rules = new(new[] { 0, 1, 2, 3, 6 }, HighClass: 2, HighClassAbove: 40,
        NdbsAbove: 40, NdbDistance: 80, Margin: 10, CountLimit: 100, RoundLimit: 500);

    private const uint SeaAddress = 0x2954f520;

    /// <summary>Each navaid's record address when the test names none: distinct, never a tuned one.</summary>
    private static int _nextAddress;

    private static (double Lat, double Lon) At(double nm, double off) => Bbs757EhsiTcasTests.At(nm, off);

    private static Bbs757EhsiLoopNavaid? Navaid(string ident, int kind, int cls, double nm, double off = 0, uint address = 0)
    {
        var (lat, lon) = At(nm, off);
        return new Bbs757EhsiLoopNavaid(address == 0 ? 0x30000000u + (uint)Interlocked.Increment(ref _nextAddress) * 0x48 : address, cls,
            new Bbs757EhsiMapOption(Bbs757EhsiMapItemKind.Navaid, ident, kind, lat, lon));
    }

    private static Bbs757EhsiLoopAirport Airport(string name, double nm, double off = 0)
    {
        var (lat, lon) = At(nm, off);
        return new Bbs757EhsiLoopAirport(name, new Bbs757EhsiMapOption(Bbs757EhsiMapItemKind.Airport, name, -1, lat, lon));
    }

    private static Bbs757EhsiMapOption? Point(string ident, double nm, double off = 0, Bbs757EhsiMapItemKind kind = Bbs757EhsiMapItemKind.Waypoint)
    {
        var (lat, lon) = At(nm, off);
        return new Bbs757EhsiMapOption(kind, ident, -1, lat, lon);
    }

    private static Bbs757EhsiLoopScene Scene(double range, IEnumerable<(double, double)>? route = null,
        (double Lat, double Lon)? aircraft = null) =>
        new(Frame, range, aircraft?.Lat ?? CenterLat, aircraft?.Lon ?? CenterLon, new[] { "KSEA", "KPDX" },
            new HashSet<(double, double)>(route ?? Array.Empty<(double, double)>()));

    private static Bbs757EhsiLoopLists Lists(
        IEnumerable<Bbs757EhsiLoopNavaid?>? navaids = null, IEnumerable<Bbs757EhsiLoopAirport>? airports = null,
        IEnumerable<Bbs757EhsiMapOption?>? waypoints = null, IEnumerable<Bbs757EhsiMapOption?>? ndbs = null,
        bool navAid = true, bool airport = true, bool waypoint = true, uint tunedLeft = SeaAddress, uint tunedRight = 0) =>
        new(navAid, airport, waypoint, (navaids ?? Array.Empty<Bbs757EhsiLoopNavaid?>()).ToArray(),
            (airports ?? Array.Empty<Bbs757EhsiLoopAirport>()).ToArray(), (waypoints ?? Array.Empty<Bbs757EhsiMapOption?>()).ToArray(),
            (ndbs ?? Array.Empty<Bbs757EhsiMapOption?>()).ToArray(), tunedLeft, tunedRight);

    private static string[] Names(IReadOnlyList<Bbs757EhsiMapOption> drawn) => drawn.Select(d => d.Name).ToArray();

    /// <summary>The live navaid list, nearest first: ILS/LOC DMEs (kind 5), SEA (the tuned VORTAC), TCM, PAE, OLM, CVV,
    /// NUW, then further ones to 97 entries.</summary>
    private static Bbs757EhsiLoopNavaid?[] LiveNavaids() => new[]
    {
        Navaid("ISNQ", 5, 0, 0.6), Navaid("ISEA", 5, 0, 0.6), Navaid("SEA", 2, 2, 0.5, address: SeaAddress),
        Navaid("ITUC", 5, 0, 1.4), Navaid("ISZI", 5, 0, 2.0), Navaid("ICJL", 5, 0, 2.2), Navaid("IBEJ", 5, 0, 2.8),
        Navaid("ICHJ", 5, 0, 4.3), Navaid("IBFI", 5, 0, 5.0), Navaid("TCM", 3, 0, 19.1, -160), Navaid("IGRF", 5, 0, 25.1),
        Navaid("PAE", 1, 1, 28.6, 40), Navaid("OLM", 2, 2, 37.4, -130), Navaid("CVV", 1, 1, 51.0), Navaid("NUW", 3, 2, 56.5, 20),
    }.Concat(Enumerable.Range(0, 82).Select(i => Navaid("F" + i, 1, 1, 60 + i))).ToArray();

    /// <summary>The live airport list (the FMS's nearest suitable, nearest first), KSEA the origin.</summary>
    private static Bbs757EhsiLoopAirport[] LiveAirports() => new[]
    {
        Airport("KSEA", 0.5), Airport("KBFI", 5.2), Airport("KRNT", 6.1), Airport("KPWT", 18.9), Airport("KTCM", 19.6),
        Airport("KGRF", 24.6), Airport("KPAE", 27.8), Airport("KNUW", 56.3), Airport("KCLM", 63.0), Airport("KBLI", 81.5),
        Airport("CYYJ", 85.1), Airport("KEAT", 85.2), Airport("KYKM", 89.1), Airport("CYXX", 94.9), Airport("CZBB", 101.8),
    }.Concat(Enumerable.Range(0, 26).Select(i => Airport("X" + i, 110 + i * 7))).ToArray();

    // The measured case: range 20, NAV AID, APRT and WPT on. Within range + 10: 2 countable navaids (TCM, PAE; the ILS
    // DMEs are not drawn and SEA is the tuned one), 6 airports (KSEA is the origin), and far more than 92 waypoints. The
    // loop stops at the end of the round where the count reaches 100: 2 + 6 + 92.
    [Fact]
    public void Bbs757Ehsi_loop_stops_at_the_count_as_measured_at_range_20()
    {
        var waypoints = Enumerable.Range(1, 1525).Select(i => Point("W" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), Math.Min(29.9, i * 0.25), i * 7)).ToArray();
        var (drawn, count) = Bbs757EhsiMapOptionsLoop.Run(Lists(LiveNavaids(), LiveAirports(), waypoints), Scene(20), Rules);
        Assert.Equal(100, count);
        Assert.Equal(count, drawn.Count);
        // Interleaved one from each list per round, in draw order: round 1's navaid (ISNQ) is not drawn and its airport
        // (KSEA) is the origin; round 10 draws TCM, round 12 PAE.
        Assert.Equal(new[] { "W0001", "KBFI", "W0002", "KRNT", "W0003", "KPWT", "W0004", "KTCM", "W0005", "KGRF", "W0006", "KPAE", "W0007" },
            Names(drawn).Take(13));
        Assert.Equal(new[] { "W0009", "TCM", "W0010", "W0011", "PAE", "W0012" }, Names(drawn).Skip(14).Take(6));
        Assert.Equal("W0092", drawn[^1].Name);
        Assert.DoesNotContain(drawn, d => d.Name is "SEA" or "KSEA" or "ISNQ" or "OLM");
    }

    // Range 80: only high-class navaids (OLM, NUW and two more; the tuned SEA not counted), 12 airports within 90 NM, and
    // the NDBs within 80 NM of the aircraft (FHR only): every list used up at 17.
    [Fact]
    public void Bbs757Ehsi_loop_counts_17_as_measured_at_range_80()
    {
        var navaids = LiveNavaids().Take(15)
            .Append(Navaid("HQM", 2, 2, 70)).Append(Navaid("YKM", 0, 2, 85)).Append(Navaid("BTG", 2, 2, 95)).ToArray();
        var airports = LiveAirports();
        var ndbs = new[] { Point("FHR", 79.5, -90, Bbs757EhsiMapItemKind.Ndb), Point("BKE", 80.5, 0, Bbs757EhsiMapItemKind.Ndb),
            Point("LKV", 150, 0, Bbs757EhsiMapItemKind.Ndb) };
        var (drawn, count) = Bbs757EhsiMapOptionsLoop.Run(Lists(navaids, airports, ndbs: ndbs), Scene(80), Rules);
        Assert.Equal(17, count);
        Assert.Equal(new[] { "OLM", "NUW", "HQM", "YKM" }, drawn.Where(d => d.Kind == Bbs757EhsiMapItemKind.Navaid).Select(d => d.Name));
        Assert.Equal(12, drawn.Count(d => d.Kind == Bbs757EhsiMapItemKind.Airport));
        Assert.Equal(new[] { "FHR" }, drawn.Where(d => d.Kind == Bbs757EhsiMapItemKind.Ndb).Select(d => d.Name));
        // WPT at a range up to 40 takes the waypoints, above it the NDBs.
        Assert.Empty(Bbs757EhsiMapOptionsLoop.Run(Lists(ndbs: ndbs, navAid: false, airport: false), Scene(40), Rules).Drawn);
    }

    [Fact]
    public void Bbs757Ehsi_loop_draws_only_within_the_range_and_its_margin()
    {
        var (drawn, count) = Bbs757EhsiMapOptionsLoop.Run(
            Lists(waypoints: new[] { Point("IN", 29.99), Point("OUT", 30.01), Point("BACK", 25, 180) }), Scene(20), Rules);
        Assert.Equal(new[] { "IN", "BACK" }, Names(drawn));
        Assert.Equal(2, count);
    }

    [Fact]
    public void Bbs757Ehsi_loop_skips_tuned_stations_undrawn_kinds_and_low_classes()
    {
        var navaids = new[]
        {
            Navaid("SEA", 2, 2, 1, address: SeaAddress), Navaid("PAE", 1, 1, 2, address: 0x1234),
            Navaid("V0", 0, 0, 3), Navaid("V4", 4, 2, 3), Navaid("V5", 5, 2, 3), Navaid("V7", 7, 2, 3), Navaid("V8", 8, 2, 3),
            Navaid("V3", 3, 2, 3), Navaid("V6", 6, 2, 3),
        };
        // The tuned stations (either receiver) are drawn apart, never by the loop; kinds 4, 5, 7 and 8 are not drawn.
        Assert.Equal(new[] { "V0", "V3", "V6" },
            Names(Bbs757EhsiMapOptionsLoop.Run(Lists(navaids, tunedLeft: SeaAddress, tunedRight: 0x1234), Scene(20), Rules).Drawn));
        Assert.Equal(new[] { "SEA", "PAE", "V0", "V3", "V6" },
            Names(Bbs757EhsiMapOptionsLoop.Run(Lists(navaids, tunedLeft: 0), Scene(20), Rules).Drawn));
        // Above range 40 only the high class (2); at 40 every class.
        Assert.Equal(new[] { "V3", "V6" }, Names(Bbs757EhsiMapOptionsLoop.Run(Lists(navaids), Scene(80), Rules).Drawn));
        Assert.Equal(new[] { "PAE", "V0", "V3", "V6" }, Names(Bbs757EhsiMapOptionsLoop.Run(Lists(navaids), Scene(40), Rules).Drawn));
    }

    [Fact]
    public void Bbs757Ehsi_loop_skips_the_origin_and_destination_airports()
    {
        var airports = new[] { Airport("KSEA", 1), Airport("KBFI", 2), Airport("KPDX", 3), Airport("KSEAX", 4) };
        Assert.Equal(new[] { "KBFI", "KSEAX" }, Names(Bbs757EhsiMapOptionsLoop.Run(Lists(airports: airports), Scene(20), Rules).Drawn));
    }

    [Fact]
    public void Bbs757Ehsi_loop_skips_waypoints_on_the_route()
    {
        var on = Point("ONRTE", 5)!.Value;
        var near = on with { Name = "NEAR", Latitude = on.Latitude + 1e-9 };
        var waypoints = new Bbs757EhsiMapOption?[] { on, near, Point("OFF", 6) };
        var (drawn, count) = Bbs757EhsiMapOptionsLoop.Run(Lists(waypoints: waypoints), Scene(20, new[] { (on.Latitude, on.Longitude) }), Rules);
        // Exact equality only: a point a hair away is not the route's.
        Assert.Equal(new[] { "NEAR", "OFF" }, Names(drawn));
        Assert.Equal(2, count);
        // An NDB on the route too.
        var ndb = on with { Kind = Bbs757EhsiMapItemKind.Ndb };
        Assert.Empty(Bbs757EhsiMapOptionsLoop.Run(Lists(ndbs: new Bbs757EhsiMapOption?[] { ndb }), Scene(80, new[] { (on.Latitude, on.Longitude) }), Rules).Drawn);
    }

    // An NDB is measured from the aircraft, not the map's center: here the center is 60 NM from the aircraft.
    [Fact]
    public void Bbs757Ehsi_loop_skips_ndbs_beyond_their_distance_from_the_aircraft()
    {
        var aircraft = At(60, 180);
        var ndbs = new[] { Point("AHEAD", 30, 0, Bbs757EhsiMapItemKind.Ndb), Point("CENTER", 1, 0, Bbs757EhsiMapItemKind.Ndb) };
        // AHEAD is 90 NM from the aircraft (skipped although near the center), CENTER 61 NM (drawn).
        Assert.Equal(new[] { "CENTER" }, Names(Bbs757EhsiMapOptionsLoop.Run(Lists(ndbs: ndbs), Scene(80, aircraft: aircraft), Rules).Drawn));
        Assert.Equal(new[] { "AHEAD", "CENTER" }, Names(Bbs757EhsiMapOptionsLoop.Run(Lists(ndbs: ndbs), Scene(80), Rules).Drawn));
    }

    // A skipped NDB or a null waypoint or NDB pointer restarts the round: it still counts a round, but the end-of-round
    // check is not made. With a count limit of 2: round 1 draws N1 and A1 (2), then the NDB is skipped, so the loop does
    // not stop and round 2 draws N2 and the next NDB.
    [Fact]
    public void Bbs757Ehsi_loop_restarts_the_round_without_its_end_check()
    {
        var rules = Rules with { CountLimit = 2 };
        var navaids = new[] { Navaid("N1", 0, 2, 1), Navaid("N2", 0, 2, 2) };
        var airports = new[] { Airport("A1", 1), Airport("A2", 2) };
        var far = Point("FAR", 85, 180, Bbs757EhsiMapItemKind.Ndb);
        var near = Point("NEAR", 2, 0, Bbs757EhsiMapItemKind.Ndb);
        Assert.Equal(new[] { "N1", "A1", "N2", "A2", "NEAR" },
            Names(Bbs757EhsiMapOptionsLoop.Run(Lists(navaids, airports, ndbs: new[] { far, near }), Scene(80), rules).Drawn));
        Assert.Equal(new[] { "N1", "A1", "N2", "A2", "NEAR" },
            Names(Bbs757EhsiMapOptionsLoop.Run(Lists(navaids, airports, ndbs: new[] { null, near }), Scene(80), rules).Drawn));
        var wpt = Point("W", 2);
        Assert.Equal(new[] { "N1", "A1", "N2", "A2", "W" },
            Names(Bbs757EhsiMapOptionsLoop.Run(Lists(navaids, airports, new[] { null, wpt }), Scene(20), rules).Drawn));
        // Without a skip the round's end stops it: three in round 1 (one over the limit).
        Assert.Equal(new[] { "N1", "A1", "NEAR" },
            Names(Bbs757EhsiMapOptionsLoop.Run(Lists(navaids, airports, ndbs: new[] { near, far }), Scene(80), rules).Drawn));
        // A null navaid entry draws nothing and does not restart: the round ends and stops at the limit.
        Assert.Equal(new[] { "A1", "W" },
            Names(Bbs757EhsiMapOptionsLoop.Run(Lists(new[] { null, navaids[1] }, airports, new[] { wpt }), Scene(20), rules).Drawn));
    }

    [Fact]
    public void Bbs757Ehsi_loop_stops_after_the_round_limit()
    {
        var waypoints = new[] { Point("F1", 50), Point("F2", 50), Point("F3", 50), Point("NEAR", 1) };
        Assert.Empty(Bbs757EhsiMapOptionsLoop.Run(Lists(waypoints: waypoints), Scene(20), Rules with { RoundLimit = 3 }).Drawn);
        Assert.Equal(new[] { "NEAR" }, Names(Bbs757EhsiMapOptionsLoop.Run(Lists(waypoints: waypoints), Scene(20), Rules with { RoundLimit = 4 }).Drawn));
    }

    [Fact]
    public void Bbs757Ehsi_loop_stops_when_every_switched_on_list_is_used_up()
    {
        var navaids = new[] { Navaid("N1", 0, 2, 1) };
        var airports = Enumerable.Range(0, 5).Select(i => Airport("A" + i, 1 + i)).ToArray();
        // NAV AID used up after one round; ARPT goes on; WPT switched off counts as used up, its items never drawn.
        var (drawn, count) = Bbs757EhsiMapOptionsLoop.Run(Lists(navaids, airports, new[] { Point("W", 1) }, waypoint: false), Scene(20), Rules);
        Assert.Equal(new[] { "N1", "A0", "A1", "A2", "A3", "A4" }, Names(drawn));
        Assert.Equal(6, count);
        // Every switch off: nothing.
        Assert.Empty(Bbs757EhsiMapOptionsLoop.Run(Lists(navaids, airports, navAid: false, airport: false, waypoint: false), Scene(20), Rules).Drawn);
    }
}
