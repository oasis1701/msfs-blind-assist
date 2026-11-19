// The 757 EHSI's window rows, spoken changes and Ctrl+W line, as BlueBird draws its screen: each row only where the
// screen draws it (BlueBird's own clip), top to bottom, in BlueBird's words and formats, culture-invariant.

using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiTextTests
{
    /// <summary>The sample layout's map constants.</summary>
    internal static readonly Bbs757EhsiConstants K = new(0.6775, 0.8175, 66, 10, 100, 100);

    /// <summary>The measured MAP frame (not centered): the aircraft at KSEA's gate, true heading 134.43.</summary>
    internal static readonly Bbs757EhsiMap MapFrame = new(47.442946, -122.303648, 134.43, 352, 702.2, 704, 859);

    /// <summary>The measured PLAN frame: north up, centered on KOLM.</summary>
    internal static readonly Bbs757EhsiMap PlanFrame = new(46.96941, -122.90254, 0, 352, 411.2, 704, 859);

    internal static Bbs757EhsiLeg Leg(string label, double lat, double lon, int terminator = 5, int cKind = 0, int c1 = 0,
        int c2 = 0, double eta = -1) =>
        new(label, lat, lon, false, terminator, terminator is 9 or 19, terminator is not (0 or 9 or 19), cKind, c1, c2, eta);

    internal static readonly Bbs757EhsiLeg Kolm = Leg("KOLM", 46.96941, -122.90254, 5, 1, 2300, 0, 1518.48);
    internal static readonly Bbs757EhsiLeg Disc = new("", 0, 0, true, 0, false, false, 0, 0, 0, -1);
    internal static readonly Bbs757EhsiLeg Btg = Leg("BTG", 45.7477, -122.5923, 23);
    internal static readonly Bbs757EhsiLeg Kpdx = Leg("KPDX", 45.5887, -122.5969, 1);

    internal static readonly Bbs757EhsiPoint TopOfClimb = new("T/C", false, 47.13824, -122.68684);
    internal static readonly Bbs757EhsiPoint TopOfDescent = new("T/D", false, 46.6855, -122.8291);
    internal static readonly Bbs757EhsiPoint Ksea = new("KSEA", true, 47.4499, -122.3118);
    internal static readonly Bbs757EhsiPoint KpdxAirport = new("KPDX", true, 45.5887, -122.5969);

    /// <summary>The measured scene in a full reading: MAP at 160, the IRS aligned, parked, the route KOLM, a
    /// discontinuity, BTG, KPDX active, T/C, T/D and both airports.</summary>
    /// <summary>The sample layout's words (BlueBird's own, as the screen prints them).</summary>
    internal static readonly Bbs757EhsiWords W = new("HDG", "TRK", "M", "TRU", "MAP", "NM", "z", "T/C", "T/D", "E/D", "EXCESS DATA");

    internal static Bbs757EhsiReading Ehsi() => new(
        Powered: true, Mode: "MAP", ModeValue: 2, Centered: false, Range: 160, RangeChanging: false, Valid: true,
        Aligning: false, Number: 119.39, TrackUp: false, TrueReference: false, HeadingBug: 147, Latitude: 47.442946,
        Longitude: -122.303648, GroundSpeed: 0, Clock: 1512.38, Map: MapFrame, Constants: K, Words: W, Active: Kolm,
        ActiveIndex: 0, RouteActive: true, DataSwitch: false, ExcessCount: 0, Route: new[] { Kolm, Disc, Btg, Kpdx },
        Modification: null, Points: new[] { TopOfClimb, TopOfDescent, Ksea, KpdxAirport }, Corrupt: false);

    /// <summary>The same scene in a light reading (no route, modification or points).</summary>
    internal static Bbs757EhsiReading Light() => Ehsi() with { Route = null, Points = null, ActiveIndex = -1 };

    internal static Bbs757EhsiReading Dark() => new(false, null, -1, false, 0, false, false, false, 0, false, false, 0, 0, 0, 0,
        0, default, K, W, null, -1, false, false, 0, null, null, null, Corrupt: false);

    internal static Bbs757EhsiReading Corrupt() => new(true, null, -1, false, 0, false, false, false, 0, false, false, 0, 0, 0, 0,
        0, default, K, W, null, -1, false, false, 0, Array.Empty<Bbs757EhsiLeg>(), null, Array.Empty<Bbs757EhsiPoint>(),
        Corrupt: true);

    /// <summary><paramref name="r"/> in the mode the layout's word table names <paramref name="word"/>, with BlueBird's
    /// value for it (0 VOR, 1 ILS, 2 MAP, 3 PLAN).</summary>
    internal static Bbs757EhsiReading InMode(Bbs757EhsiReading r, string word) =>
        r with { Mode = word, ModeValue = Array.IndexOf(new[] { "VOR", "ILS", "MAP", "PLAN" }, word) };

    private static Bbs757EhsiReading Plan() => InMode(Ehsi(), "PLAN") with { Range = 80, Map = PlanFrame };

    private static string[] MapRows(Bbs757EhsiReading r) => Bbs757EhsiText.Rows(r).Where(row => row.StartsWith("Map", StringComparison.Ordinal)).ToArray();

    private static string[] Texts(IReadOnlyList<Bbs757SpokenLine> lines)
    {
        Assert.All(lines, l => Assert.Equal("MSFSBA_BBS757_EHSI_MESSAGES", l.MuteKey));
        return lines.Select(l => l.Text).ToArray();
    }

    // ---- the rows ----

    [Fact]
    public void Rows_follow_the_screen_top_to_bottom()
    {
        // The header, the top line (middle, left, right), the map's items by their place on the screen from the top down
        // (left to right on one height, legs before points), the heading bug, the messages.
        Assert.Equal(new[]
        {
            "EHSI: MAP, range 160",
            "Top: HDG 119 M",
            "Top left: 37.5 NM",
            "Top right: no ETA",
            "Map: KPDX, 112.0 NM, 52° right",
            "Map: KPDX airport, 112.0 NM, 52° right",
            "Map: BTG, 102.5 NM, 52° right",
            "Map: T/D, 50.3 NM, 71° right",
            "Map: KOLM, active, 37.5 NM, 86° right",
            "Map: T/C, 24.0 NM, 86° right",
            "Map: KSEA airport, 0.5 NM, 173° left",
            "Heading bug: 147, 28° right",
            "Messages: none",
        }, Bbs757EhsiText.Rows(Ehsi()));
    }

    [Fact]
    public void Only_items_on_the_map_are_listed()
    {
        // Range 40: KOLM (37.5 NM sideways) is past the screen's edge, T/C (24.0 NM) just inside it; T/D (50.3 NM) is past
        // the points' range + 10 NM. Range 10: the gate's airport alone.
        Assert.Equal(new[] { "Map: T/C, 24.0 NM, 86° right", "Map: KSEA airport, 0.5 NM, 173° left" }, MapRows(Ehsi() with { Range = 40 }));
        Assert.Equal(new[] { "Map: KSEA airport, 0.5 NM, 173° left" }, MapRows(Ehsi() with { Range = 10 }));

        // A light reading has no route: one "Map: reading" row where the map rows go, the top line still read.
        Assert.Equal(new[] { "Map: reading" }, MapRows(Light()));
        Assert.Contains("Top left: 37.5 NM", Bbs757EhsiText.Rows(Light()));

        // MAP needs the IRS valid (the map fields are zeroed); PLAN does not.
        Assert.Empty(MapRows(Ehsi() with { Valid = false }));
        Assert.NotEmpty(MapRows(Plan() with { Valid = false }));

        // A point beyond range + 10 NM is not drawn even inside the clip: T/D at 50.3 NM with range 40.2 + 10 = 50.2.
        Assert.DoesNotContain(MapRows(Ehsi() with { Range = 40.2 }), r => r.StartsWith("Map: T/D", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("VOR")]
    [InlineData("ILS")]
    public void Vor_and_ils_modes_list_their_part_never_the_map(string mode)
    {
        // The picture is plan 28's approach part (Bbs757EhsiApproachTests). A reading holding neither the part nor a reason
        // lists the header, the heading line, the top right ETA (expanded only) and the heading bug: never MAP's
        // distance, map items or messages.
        Assert.Equal(new[] { $"EHSI: {mode}, range 160", "Top: HDG 119 M", "Top right: no ETA", "Heading bug: 147, 28° right" },
            Bbs757EhsiText.Rows(InMode(Ehsi(), mode)));
        Assert.Contains("Top right: 1521.4z", Bbs757EhsiText.Rows(InMode(Ehsi(), mode) with { GroundSpeed = 250 }));
        // The full rose draws no range label: the header names no range there.
        Assert.Equal(new[] { $"EHSI: {mode}, centered", "Flag: HDG" },
            Bbs757EhsiText.Rows(InMode(Ehsi(), mode) with { Centered = true, Valid = false }));
        Assert.Equal(new[] { $"EHSI: {mode}, centered", "Top: HDG 119 M", "Heading bug: 147, 28° right" },
            Bbs757EhsiText.Rows(InMode(Ehsi(), mode) with { Centered = true }));
        // The part missing: its one row where its picture starts (the top left).
        Assert.Equal(new[]
            {
                $"EHSI: {mode}, range 160", "Top: HDG 119 M", "VOR and ILS: not read: not in this layout file", "Top right: no ETA",
                "Heading bug: 147, 28° right",
            },
            Bbs757EhsiText.Rows(InMode(Ehsi(), mode) with { ApproachMissing = "not in this layout file" }));
    }

    [Fact]
    public void The_mode_is_branched_on_by_its_value_and_shown_by_its_word()
    {
        // The word is the layout's; what the screen does follows BlueBird's value: a renamed word still reads its mode.
        Assert.Equal(new[] { "EHSI: V0R, range 160", "Top: HDG 119 M", "VOR and ILS: not read: x" },
            Bbs757EhsiText.Rows(Ehsi() with { Mode = "V0R", ModeValue = 0, ApproachMissing = "x" }).Take(3));
        Assert.Contains("Map: T/C, 13.4 NM north-east of KOLM", MapRows(Plan() with { Mode = "PLN" }));
        Assert.Equal("KOLM, EHSI in L0C mode", Bbs757EhsiText.WaypointLine(Light() with { Mode = "L0C", ModeValue = 1 }));
        // A value outside the four: no map, no VOR row, no MAP flag.
        var other = Ehsi() with { ModeValue = 7, Valid = false, ApproachMissing = "x" };
        Assert.DoesNotContain(Bbs757EhsiText.Rows(other), r => r.StartsWith("Map", StringComparison.Ordinal) || r.StartsWith("VOR and ILS", StringComparison.Ordinal));
        Assert.DoesNotContain("Flag: MAP", Bbs757EhsiText.Rows(other));
    }

    [Fact]
    public void Every_word_the_screen_prints_is_the_layouts()
    {
        // A BlueBird update that renames its words: the rows, the spoken lines and Ctrl+W follow the layout.
        var words = new Bbs757EhsiWords("HDG2", "TRK2", "MAG", "TRUE", "MAP2", "nm", "Z9", "TC", "TD", "ED", "EXCESS  DATA2");
        var r = Ehsi() with { Words = words, GroundSpeed = 250, ExcessCount = 100 };
        var rows = Bbs757EhsiText.Rows(r);
        Assert.Contains("Top: HDG2 119 MAG", rows);
        Assert.Contains("Top left: 37.5 nm", rows);
        Assert.Contains("Top right: 1521.4Z9", rows);
        Assert.Contains("Message: EXCESS  DATA2", rows);
        Assert.Contains("Top: TRK2 119 TRUE", Bbs757EhsiText.Rows(r with { TrackUp = true, TrueReference = true }));
        Assert.Equal(new[] { "EHSI: MAP, range 160", "Flag: TRK2", "Flag: MAP2" },
            Bbs757EhsiText.Rows(r with { Valid = false, TrackUp = true }).Take(3));
        Assert.Equal("KOLM, 37.5 nm, 1521.4Z9", Bbs757EhsiText.WaypointLine(r));
        Assert.Equal(new[] { "EHSI message: EXCESS  DATA2", "EHSI flag: HDG2, MAP2", "EHSI: KOLM, 37 nm" },
            Texts(Bbs757EhsiText.Changes(r with { ExcessCount = 0, Active = Btg }, r with { Valid = false }, true)));

        // A default reading's words are null: printed empty, never a throw.
        var none = Ehsi() with { Words = default, ExcessCount = 100, GroundSpeed = 250 };
        Assert.Contains("Top:  119 ", Bbs757EhsiText.Rows(none));
        Assert.Contains("Top left: 37.5 ", Bbs757EhsiText.Rows(none));
        Assert.Contains("Message: ", Bbs757EhsiText.Rows(none));
        Assert.Equal("KOLM, 37.5 , 1521.4", Bbs757EhsiText.WaypointLine(none));
        Assert.NotEmpty(Bbs757EhsiText.Changes(none with { ExcessCount = 0 }, none with { Valid = false }, true));
    }

    [Fact]
    public void The_top_line_is_from_the_readings_position_not_the_maps_center()
    {
        // The reading's Latitude and Longitude are the simulator's (the sampler's inputs); the map's center is the nd's
        // IRS copy. The top line and Ctrl+W take the first, the map rows the second.
        var moved = Ehsi() with { Latitude = 47.2, Longitude = -122.6 };
        string expected = Bbs757EhsiGeometry.TopLineText(Bbs757EhsiGeometry.TopLineDistance(47.2, -122.6, Kolm.Latitude, Kolm.Longitude));
        Assert.NotEqual("37.5", expected);
        Assert.Contains("Top left: " + expected + " NM", Bbs757EhsiText.Rows(moved));
        Assert.Equal("KOLM, " + expected + " NM, no ETA", Bbs757EhsiText.WaypointLine(moved));
        Assert.Contains("Map: KOLM, active, 37.5 NM, 86° right", MapRows(moved));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Numbers_and_times_format_as_drawn_under_de_DE_and_sv_SE(string culture)
    {
        var moving = Ehsi() with { GroundSpeed = 250, DataSwitch = true, Range = 80.5 };
        var rows = TestCulture.Run(culture, () => Bbs757EhsiText.Rows(moving));
        Assert.Contains("EHSI: MAP, range 80.5", rows);
        Assert.Contains("Top left: 37.5 NM", rows);
        Assert.Contains("Top right: 1521.4z", rows);
        Assert.Contains("Map: KOLM, active, 37.5 NM, 86° right, 2300, 1518Z", rows);
        Assert.Contains("Map: T/C, 24.0 NM, 86° right", rows);
        Assert.Equal("KOLM, 37.5 NM, 1521.4z", TestCulture.Run(culture, () => Bbs757EhsiText.WaypointLine(moving)));
        var line = TestCulture.Run(culture, () => Bbs757EhsiText.Changes(moving with { Active = Btg }, moving, withPrefix: true));
        Assert.Equal(new[] { "EHSI: KOLM, 37 NM" }, Texts(line));
        // Over 100 NM the top line drops its decimal, truncated.
        var far = moving with { Active = Kpdx };
        Assert.Contains("Top left: 111 NM", TestCulture.Run(culture, () => Bbs757EhsiText.Rows(far)));
        Assert.StartsWith("KPDX, 111 NM, ", TestCulture.Run(culture, () => Bbs757EhsiText.WaypointLine(far)));
    }

    [Fact]
    public void A_dark_screen_is_one_row()
    {
        Assert.Equal(new[] { "EHSI: dark" }, Bbs757EhsiText.Rows(Dark()));
    }

    [Fact]
    public void While_the_range_changes_only_the_header_and_heading_line_show()
    {
        Assert.Equal(new[] { "EHSI: MAP, range 160", "Top: HDG 119 M" }, Bbs757EhsiText.Rows(Ehsi() with { RangeChanging = true }));
        Assert.Equal(new[] { "EHSI: PLAN, range 80, centered", "Flag: HDG" },
            Bbs757EhsiText.Rows(Plan() with { RangeChanging = true, Centered = true, Valid = false }));
    }

    [Fact]
    public void The_heading_line_says_hdg_or_trk_and_m_or_tru()
    {
        string Top(Bbs757EhsiReading r) => Bbs757EhsiText.Rows(r)[1];
        Assert.Equal("Top: HDG 119 M", Top(Ehsi()));
        Assert.Equal("Top: TRK 079 M", Top(Ehsi() with { TrackUp = true, Number = 79.4 }));
        Assert.Equal("Top: HDG 134 TRU", Top(Ehsi() with { TrueReference = true, Number = 134.43 }));
        // round % 360, three digits: 359.6 reads 000.
        Assert.Equal("Top: HDG 000 M", Top(Ehsi() with { Number = 359.6 }));
        Assert.Equal("Top: HDG 005 M", Top(Ehsi() with { Number = 4.5 }));
    }

    [Fact]
    public void A_heading_not_valid_shows_its_flags()
    {
        // Not valid: the HDG (or TRK) flag, and in MAP the MAP flag; nothing at the top unless aligning ("---").
        Assert.Equal(new[] { "EHSI: MAP, range 160", "Flag: HDG", "Flag: MAP" }, Bbs757EhsiText.Rows(Ehsi() with { Valid = false }).Take(3));
        Assert.DoesNotContain(Bbs757EhsiText.Rows(Ehsi() with { Valid = false }), r => r.StartsWith("Top: ", StringComparison.Ordinal));
        Assert.Equal(new[] { "EHSI: MAP, range 160", "Top: ---", "Flag: HDG", "Flag: MAP" },
            Bbs757EhsiText.Rows(Ehsi() with { Valid = false, Aligning = true }).Take(4));
        Assert.Equal(new[] { "EHSI: MAP, range 160", "Flag: TRK", "Flag: MAP" },
            Bbs757EhsiText.Rows(Ehsi() with { Valid = false, TrackUp = true }).Take(3));
        // PLAN: the heading flag alone.
        var plan = Bbs757EhsiText.Rows(Plan() with { Valid = false });
        Assert.Equal(new[] { "EHSI: PLAN, range 80", "Flag: HDG", "Top left: 37.5 NM" }, plan.Take(3));
        Assert.DoesNotContain("Flag: MAP", plan);
        // The heading bug is drawn only while valid.
        Assert.DoesNotContain(plan, r => r.StartsWith("Heading bug", StringComparison.Ordinal));
    }

    [Fact]
    public void The_top_line_draws_dashes_as_the_screen_does()
    {
        string[] TopLine(Bbs757EhsiReading r) => Bbs757EhsiText.Rows(r).Where(x => x.StartsWith("Top ", StringComparison.Ordinal)).ToArray();
        // No route: no distance, no ETA.
        Assert.Equal(new[] { "Top left: no distance", "Top right: no ETA" }, TopLine(Ehsi() with { Active = null, Route = Array.Empty<Bbs757EhsiLeg>() }));
        // Leg 0 a VM or FM leg: dashes, whatever the ground speed.
        var vm = Leg("(800)", 47.5, -122.3, 19);
        Assert.Equal(new[] { "Top left: no distance", "Top right: no ETA" }, TopLine(Ehsi() with { Active = vm, GroundSpeed = 300 }));
        // Under 100 kt no ETA; at 100 kt or more the clock plus the time to go.
        Assert.Equal(new[] { "Top left: 37.5 NM", "Top right: no ETA" }, TopLine(Ehsi() with { GroundSpeed = 99.9 }));
        Assert.Equal(new[] { "Top left: 37.5 NM", "Top right: 1521.4z" }, TopLine(Ehsi() with { GroundSpeed = 250 }));
        // A discontinuity as leg 0 is at 0° N 0° E, and the screen prints the distance there: so do the rows.
        var toNowhere = TopLine(Ehsi() with { Active = Disc });
        Assert.Equal("Top left: " + Bbs757EhsiGeometry.TopLineText(Bbs757EhsiGeometry.TopLineDistance(47.442946, -122.303648, 0, 0)) + " NM",
            toNowhere[0]);
    }

    [Fact]
    public void Legs_say_active_inactive_route_or_modification()
    {
        // An inactive route: every leg "Map, inactive route: ", none active.
        var inactive = MapRows(Ehsi() with { RouteActive = false });
        Assert.Contains("Map, inactive route: KOLM, 37.5 NM, 86° right", inactive);
        Assert.Contains("Map, inactive route: BTG, 102.5 NM, 52° right", inactive);
        Assert.DoesNotContain(inactive, r => r.Contains("active,", StringComparison.Ordinal) && !r.Contains("inactive", StringComparison.Ordinal));
        // Points are not the route's: plain "Map: ".
        Assert.Contains("Map: T/C, 24.0 NM, 86° right", inactive);

        // A pending modification: its legs "Map, modification: ", none marked active, and the route's drawn as well, its
        // own leg 0 still "active" (measured: the active route stays magenta with its leg 0 magenta, the modification
        // white dashed beside it).
        var modified = Ehsi() with { Active = Kolm, Modification = new[] { Kolm, Btg, Kpdx } };
        var rows = MapRows(modified);
        Assert.Contains("Map, modification: KOLM, 37.5 NM, 86° right", rows);
        Assert.Contains("Map: KOLM, active, 37.5 NM, 86° right", rows);
        Assert.Contains("Map, modification: BTG, 102.5 NM, 52° right", rows);
        Assert.Contains("Map: BTG, 102.5 NM, 52° right", rows);
        Assert.Equal(1, rows.Count(r => r.Contains(", active,", StringComparison.Ordinal)));
        Assert.DoesNotContain(rows, r => r.StartsWith("Map, modification: ", StringComparison.Ordinal) && r.Contains(", active,", StringComparison.Ordinal));
        // An inactive route with a modification pending: nothing active.
        Assert.DoesNotContain(MapRows(modified with { RouteActive = false }), r => r.Contains(", active,", StringComparison.Ordinal));

        // Only the active leg says "active": BTG later in the route is not, even when it is the same waypoint twice.
        var hold = MapRows(Ehsi() with { Active = Btg, Route = new[] { Btg, Kpdx, Btg } });
        Assert.Equal(1, hold.Count(r => r.Contains("BTG, active,", StringComparison.Ordinal)));
        Assert.Equal(2, hold.Count(r => r.Contains("BTG", StringComparison.Ordinal)));

        // A flown copy of the active leg in front: "active" goes by the reading's index (the route's own leg 0), never
        // by value.
        var flown = MapRows(Ehsi() with { Route = new[] { Kolm, Kolm, Kpdx }, ActiveIndex = 1, Points = Array.Empty<Bbs757EhsiPoint>() });
        Assert.Equal(new[] { "Map: KPDX, 112.0 NM, 52° right", "Map: KOLM, 37.5 NM, 86° right", "Map: KOLM, active, 37.5 NM, 86° right" }, flown);
        // No active index (-1) or one past the list: nothing active.
        Assert.DoesNotContain(MapRows(Ehsi() with { ActiveIndex = -1 }), r => r.Contains(", active,", StringComparison.Ordinal));
        Assert.DoesNotContain(MapRows(Ehsi() with { ActiveIndex = 9 }), r => r.Contains(", active,", StringComparison.Ordinal));
    }

    [Fact]
    public void A_discontinuity_gives_no_row_and_no_symbol_legs_none()
    {
        var rows = MapRows(Ehsi());
        Assert.DoesNotContain(rows, r => r.StartsWith("Map: ,", StringComparison.Ordinal));
        // KOLM, the discontinuity, BTG, KPDX: three leg rows.
        Assert.Equal(new[] { "Map: KPDX", "Map: BTG", "Map: KOLM" },
            rows.Where(r => !r.Contains("airport", StringComparison.Ordinal) && !r.Contains("T/", StringComparison.Ordinal))
                .Select(r => r[..r.IndexOf(',')]));
        // A VM leg draws no symbol: no row.
        var vm = Leg("(800)", 47.45, -122.31, 19);
        Assert.DoesNotContain(MapRows(Ehsi() with { Route = new[] { Kolm, vm } }), r => r.Contains("(800)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_light_reading_lists_map_reading_where_the_map_rows_would_be()
    {
        const string Reading = "Map: reading";
        // MAP while valid, and PLAN (valid or not): the one row, after the top line, before the heading bug.
        var map = Bbs757EhsiText.Rows(Light());
        Assert.Equal(new[] { "Top right: no ETA", Reading, "Heading bug: 147, 28° right" },
            map.SkipWhile(r => r != "Top right: no ETA").Take(3));
        Assert.Contains(Reading, Bbs757EhsiText.Rows(InMode(Light(), "PLAN")));
        Assert.Contains(Reading, Bbs757EhsiText.Rows(InMode(Light(), "PLAN") with { Valid = false }));

        // Not where the screen draws no map: VOR, ILS, dark, the range changing, MAP while not valid.
        Assert.DoesNotContain(Reading, Bbs757EhsiText.Rows(InMode(Light(), "VOR")));
        Assert.DoesNotContain(Reading, Bbs757EhsiText.Rows(InMode(Light(), "ILS")));
        Assert.DoesNotContain(Reading, Bbs757EhsiText.Rows(Dark()));
        Assert.DoesNotContain(Reading, Bbs757EhsiText.Rows(Light() with { RangeChanging = true }));
        Assert.DoesNotContain(Reading, Bbs757EhsiText.Rows(Light() with { Valid = false }));

        // A full reading never shows it, even with nothing on the map.
        Assert.DoesNotContain(Reading, Bbs757EhsiText.Rows(Ehsi()));
        Assert.Empty(MapRows(Ehsi() with { Route = Array.Empty<Bbs757EhsiLeg>(), Points = Array.Empty<Bbs757EhsiPoint>() }));
        Assert.Empty(MapRows(Plan() with { Route = Array.Empty<Bbs757EhsiLeg>(), Points = Array.Empty<Bbs757EhsiPoint>() }));
    }

    [Theory]
    [InlineData(0, 2300, 0, "")]
    [InlineData(1, 2300, 0, ", 2300")]
    [InlineData(2, 2300, 0, ", 2300A")]
    [InlineData(3, 9000, 0, ", 9000B")]
    [InlineData(4, 9000, 5000, ", 5000A9000B")]
    [InlineData(5, 9000, 5000, ", 5000A")]
    [InlineData(6, 9000, 5000, "")]
    public void The_data_switch_adds_each_constraint_kind_as_drawn(int kind, int first, int second, string drawn)
    {
        var leg = Leg("KOLM", Kolm.Latitude, Kolm.Longitude, 5, kind, first, second);
        var reading = Ehsi() with { Active = leg, Route = new[] { leg }, Points = Array.Empty<Bbs757EhsiPoint>(), DataSwitch = true };
        Assert.Equal(new[] { "Map: KOLM, active, 37.5 NM, 86° right" + drawn }, MapRows(reading));
        // DATA off: none of it.
        Assert.Equal(new[] { "Map: KOLM, active, 37.5 NM, 86° right" }, MapRows(reading with { DataSwitch = false }));
    }

    [Fact]
    public void The_data_switch_adds_the_eta_truncated_to_four_digits()
    {
        Leg_with_eta(1518.48, ", 2300, 1518Z");
        Leg_with_eta(905.99, ", 2300, 0905Z");
        Leg_with_eta(-1, ", 2300");

        static void Leg_with_eta(double eta, string drawn)
        {
            var leg = Kolm with { Eta = eta };
            var reading = Ehsi() with { Active = leg, Route = new[] { leg }, Points = Array.Empty<Bbs757EhsiPoint>(), DataSwitch = true };
            Assert.Equal(new[] { "Map: KOLM, active, 37.5 NM, 86° right" + drawn }, MapRows(reading));
        }
    }

    [Fact]
    public void Points_and_airports_are_named_by_the_screens_words()
    {
        var reading = Ehsi() with { Route = Array.Empty<Bbs757EhsiLeg>(), Active = null,
            Points = new[] { TopOfClimb, new Bbs757EhsiPoint("E/D", false, 47.2, -122.5), Ksea } };
        var rows = MapRows(reading);
        Assert.Equal(3, rows.Length);
        Assert.Contains("Map: T/C, 24.0 NM, 86° right", rows);
        Assert.Contains(rows, r => r.StartsWith("Map: E/D, ", StringComparison.Ordinal));
        Assert.Contains("Map: KSEA airport, 0.5 NM, 173° left", rows);
    }

    [Fact]
    public void Plan_places_are_from_the_center_leg()
    {
        // PLAN at 80 centered on KOLM: KSEA is cut (37.5 NM at 41°, past the clip), T/C and T/D drawn.
        Assert.Equal(new[]
        {
            "Map: T/C, 13.4 NM north-east of KOLM",
            "Map: KOLM, active, at KOLM",
            "Map: T/D, 17.3 NM south of KOLM",
        }, MapRows(Plan()));
        // No leg at the center: "the center".
        var off = Plan() with { Map = PlanFrame with { CenterLatitude = 47.0 } };
        Assert.Contains(MapRows(off), r => r.StartsWith("Map: KOLM, active, 1.8 NM south of the center", StringComparison.Ordinal));
        // PLAN draws the heading line, the bug and the messages as MAP does.
        var rows = Bbs757EhsiText.Rows(Plan());
        Assert.Equal("Top: HDG 119 M", rows[1]);
        Assert.Equal("Heading bug: 147, 28° right", rows[^2]);
    }

    [Fact]
    public void The_heading_bug_is_relative_to_the_number_at_the_top()
    {
        string Bug(Bbs757EhsiReading r) => Bbs757EhsiText.Rows(r).Single(x => x.StartsWith("Heading bug", StringComparison.Ordinal));
        Assert.Equal("Heading bug: 147, 28° right", Bug(Ehsi()));
        Assert.Equal("Heading bug: 090, 29° left", Bug(Ehsi() with { HeadingBug = 90 }));
        Assert.Equal("Heading bug: 120, ahead", Bug(Ehsi() with { HeadingBug = 119.6 }));
        Assert.Equal("Heading bug: 000, 119° left", Bug(Ehsi() with { HeadingBug = 0 }));
        Assert.Equal("Heading bug: 000, 1° right", Bug(Ehsi() with { HeadingBug = 360, Number = 359 }));
        Assert.Equal("Heading bug: 299, behind", Bug(Ehsi() with { HeadingBug = 299.39 }));
    }

    [Fact]
    public void Excess_data_shows_at_the_limit_in_map_and_plan()
    {
        Assert.Equal("Messages: none", Bbs757EhsiText.Rows(Ehsi() with { ExcessCount = 99 })[^1]);
        Assert.Equal("Message: EXCESS DATA", Bbs757EhsiText.Rows(Ehsi() with { ExcessCount = 100 })[^1]);
        Assert.Equal("Message: EXCESS DATA", Bbs757EhsiText.Rows(Plan() with { ExcessCount = 250 })[^1]);
        Assert.Equal("Message: EXCESS DATA", Bbs757EhsiText.Rows(Light() with { ExcessCount = 100 })[^1]);
    }

    // ---- what is spoken ----

    [Fact]
    public void Messages_and_flags_are_spoken_new_and_gone_joined()
    {
        var lit = Light();
        Assert.Equal(new[] { "EHSI message: EXCESS DATA" }, Texts(Bbs757EhsiText.Changes(lit, lit with { ExcessCount = 120 }, true)));
        Assert.Equal(new[] { "EHSI message gone: EXCESS DATA" }, Texts(Bbs757EhsiText.Changes(lit with { ExcessCount = 120 }, lit, true)));
        Assert.Equal(new[] { "EHSI flag: HDG, MAP" }, Texts(Bbs757EhsiText.Changes(lit, lit with { Valid = false }, true)));
        Assert.Equal(new[] { "EHSI flag gone: HDG, MAP" }, Texts(Bbs757EhsiText.Changes(lit with { Valid = false }, lit, true)));
        Assert.Equal(new[] { "EHSI flag: TRK" }, Texts(Bbs757EhsiText.Changes(InMode(lit, "PLAN") with { TrackUp = true },
            InMode(lit, "PLAN") with { TrackUp = true, Valid = false }, true)));
        // Both at once, messages first; the words are the same after "resumed".
        var both = Texts(Bbs757EhsiText.Changes(lit, lit with { Valid = false, ExcessCount = 100 }, false));
        Assert.Equal(new[] { "EHSI message: EXCESS DATA", "EHSI flag: HDG, MAP" }, both);
        // The heading flag turning from HDG to TRK is the same flag: nothing said.
        Assert.Empty(Bbs757EhsiText.Changes(lit with { Valid = false }, lit with { Valid = false, TrackUp = true }, true));
    }

    [Fact]
    public void A_mode_change_never_speaks_the_flags_or_messages_the_mode_brings()
    {
        // MAP to PLAN with the IRS off: the MAP flag goes with the mode, not with the IRS; MAP to VOR: EXCESS DATA too.
        var off = Light() with { Valid = false, ExcessCount = 100 };
        Assert.Empty(Bbs757EhsiText.Changes(off, InMode(off, "PLAN"), true));
        Assert.Empty(Bbs757EhsiText.Changes(off, InMode(off, "VOR"), true));
        Assert.Empty(Bbs757EhsiText.Changes(InMode(off, "VOR"), off, true));
        // The heading flag is every mode's: still spoken across a mode change.
        Assert.Equal(new[] { "EHSI flag gone: HDG" }, Texts(Bbs757EhsiText.Changes(off, InMode(Light(), "PLAN") with { ExcessCount = 100 }, true)));
    }

    [Fact]
    public void The_active_waypoint_changing_is_spoken_with_its_whole_distance()
    {
        var lit = Light();
        Assert.Equal(new[] { "EHSI: KOLM, 37 NM" }, Texts(Bbs757EhsiText.Changes(lit with { Active = Btg }, lit, true)));
        Assert.Equal(new[] { "EHSI: no active waypoint" }, Texts(Bbs757EhsiText.Changes(lit, lit with { Active = null }, true)));
        Assert.Equal(new[] { "EHSI: KOLM, 37 NM" }, Texts(Bbs757EhsiText.Changes(lit with { Active = null }, lit, true)));
        // Drawn as dashes: no distance.
        var vm = Leg("(800)", 47.5, -122.3, 19);
        Assert.Equal(new[] { "EHSI: (800)" }, Texts(Bbs757EhsiText.Changes(lit, lit with { Active = vm }, true)));
        // A discontinuity: as drawn, to 0° N 0° E.
        var disc = Texts(Bbs757EhsiText.Changes(lit, lit with { Active = Disc }, true));
        Assert.Single(disc);
        Assert.StartsWith("EHSI: discontinuity, ", disc[0]);
        // In VOR and ILS the top line shows DME: the waypoint without a distance.
        Assert.Equal(new[] { "EHSI: KOLM" }, Texts(Bbs757EhsiText.Changes(InMode(lit, "VOR") with { Active = Btg }, InMode(lit, "VOR"), true)));
        // The same label at a new position (the aircraft moving, the leg re-read) is no change.
        Assert.Empty(Bbs757EhsiText.Changes(lit, lit with { Active = Kolm with { Latitude = 46.97 }, Latitude = 47.3 }, true));
    }

    [Fact]
    public void Nothing_else_is_spoken()
    {
        var lit = Ehsi();
        foreach (var now in new[]
        {
            InMode(lit, "PLAN") with { Map = PlanFrame }, lit with { Range = 40 }, lit with { Centered = true },
            lit with { DataSwitch = true }, lit with { Number = 200 }, lit with { HeadingBug = 10 }, lit with { TrackUp = true },
            lit with { TrueReference = true }, lit with { RouteActive = false }, lit with { RangeChanging = true },
            lit with { Route = new[] { Kolm, Kpdx } }, lit with { Modification = new[] { Kolm, Btg } },
            lit with { Points = Array.Empty<Bbs757EhsiPoint>() }, lit with { GroundSpeed = 300, Clock = 1600 },
            lit with { Aligning = true }, lit with { Latitude = 47.0, Longitude = -122.0 }, lit with { ExcessCount = 99 },
        })
            Assert.Empty(Bbs757EhsiText.Changes(lit, now, true));
    }

    // ---- Ctrl+W ----

    [Fact]
    public void The_waypoint_line_is_the_name_then_the_top_line()
    {
        Assert.Equal("KOLM, 37.5 NM, no ETA", Bbs757EhsiText.WaypointLine(Light()));
        Assert.Equal("KOLM, 37.5 NM, 1521.4z", Bbs757EhsiText.WaypointLine(Light() with { GroundSpeed = 250 }));
        Assert.Equal("(800), no distance, no ETA", Bbs757EhsiText.WaypointLine(Light() with { Active = Leg("(800)", 47.5, -122.3, 19) }));
        Assert.Equal("No active waypoint", Bbs757EhsiText.WaypointLine(Light() with { Active = null }));
        Assert.StartsWith("discontinuity, ", Bbs757EhsiText.WaypointLine(Light() with { Active = Disc }));
        Assert.StartsWith("discontinuity, ", Bbs757EhsiText.WaypointLine(Light() with { Active = Kolm with { Label = "" } }));
        Assert.Equal("KOLM, EHSI in VOR mode", Bbs757EhsiText.WaypointLine(InMode(Light(), "VOR")));
        Assert.Equal("KOLM, EHSI in ILS mode", Bbs757EhsiText.WaypointLine(InMode(Light(), "ILS")));
        Assert.Equal("No active waypoint", Bbs757EhsiText.WaypointLine(InMode(Light(), "ILS") with { Active = null }));
        Assert.Equal("EHSI waypoint unavailable: display dark", Bbs757EhsiText.WaypointLine(Dark()));
        // PLAN draws the same top line.
        Assert.Equal("KOLM, 37.5 NM, no ETA", Bbs757EhsiText.WaypointLine(InMode(Light(), "PLAN")));
    }

    [Fact]
    public void A_corrupt_reading_never_throws()
    {
        var corrupt = Corrupt();
        Assert.NotEmpty(Bbs757EhsiText.Rows(corrupt));
        Assert.NotNull(Bbs757EhsiText.WaypointLine(corrupt));
        Assert.NotNull(Bbs757EhsiText.Changes(corrupt, corrupt, true));
        Assert.NotNull(Bbs757EhsiText.Changes(Ehsi(), corrupt, true));
        Assert.NotNull(Bbs757EhsiText.Changes(corrupt, Ehsi(), false));
        // Wild values a torn read could still give never throw either.
        var wild = Ehsi() with { Range = 0, Number = double.NaN, HeadingBug = double.PositiveInfinity, Map = default,
            Constants = default, Words = default, Clock = double.NaN, GroundSpeed = 500, Mode = "XYZ", ModeValue = 99,
            ActiveIndex = 99 };
        Assert.NotEmpty(Bbs757EhsiText.Rows(wild));
        Assert.NotNull(Bbs757EhsiText.WaypointLine(wild));
        Assert.NotNull(Bbs757EhsiText.Changes(wild, Ehsi(), true));
    }
}
