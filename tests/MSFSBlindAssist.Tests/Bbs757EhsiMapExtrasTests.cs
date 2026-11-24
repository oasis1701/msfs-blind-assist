// The 757 EHSI's map extras (plan 28): TERR (its word, the two elevations with their colors, the picture's presence),
// the wind, the runways and the deceleration points, and the leg position rule for a navaid leg (plan 26's fix). Nothing
// here is spoken. The values are plan 28's live ones (TERR 3123 / 485 ft against 396.9 ft; the cruise wind 70 kt from
// 287; RW16L; the two green rings; TCM's capability set {9, 19, 18, 3}), placed through the test layout.

using MSFSBlindAssist.Services.BBS757;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757EhsiReaderTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiMapExtrasTests
{
    private static readonly Bbs757EhsiLayout P = Bbs757EhsiApproachTests.P;
    private static Bbs757EhsiMapExtras X => P.MapExtras!;

    // Where this test's memory puts the map extras' objects (beside the other parts').
    internal const uint Adc = 0x31000, StartPoints = 0x32000, EndPoints = 0x32100, Nodes = 0x33000;

    /// <summary>The nd's ground speed as measured (nd+736): BlueBird's wind draw tests it and folds the result into the show
    /// byte; the layout holds no field for it, as the reader never reads it.</summary>
    private const uint NdGroundSpeed = 736;

    /// <summary>The scene's route path (System.IO's Path shadows the reader tests' name).</summary>
    private const uint PathAt = Bbs757EhsiReaderTests.Path;

    /// <summary>One runway record: its name and its two ends.</summary>
    internal sealed record Runway(string Name, (double Lat, double Lon) First, (double Lat, double Lon) Second);

    /// <summary>The map extras' memory as BlueBird keeps it, written over a <see cref="Scene"/> through its Tweak.</summary>
    internal sealed class Extras
    {
        public bool Terr, Picture, HasAdc = true, WindShown;
        public int Side = 6;
        public short Highest = 3123, Lowest = 485, OtherHighest = 1500, OtherLowest = 100;
        public double Altitude = 396.9, VerticalSpeed, GroundSpeed = 250;
        public Runway? Departure, Arrival;
        public (double Lat, double Lon)[] Starts = Array.Empty<(double, double)>(), Ends = Array.Empty<(double, double)>();
        public Action<byte[]>? Tweak;

        public void Write(byte[] b)
        {
            F64(b, Nd + X.TerrHandle + X.HandleValue, Terr ? 1 : 0);
            Flag(b, Nd + X.TerrainPicture, Picture);
            I32(b, Nd + X.DisplaySide, Side);
            long record = X.TerrainStatic + X.TerrainRecords;
            I16(b, record + X.TerrainHighest, Highest);
            I16(b, record + X.TerrainLowest, Lowest);
            I16(b, record + X.TerrainSideStride + X.TerrainHighest, OtherHighest);
            I16(b, record + X.TerrainSideStride + X.TerrainLowest, OtherLowest);
            I32(b, Source + X.SourceToAdc, HasAdc ? Adc : 0);
            F64(b, Adc + X.AdcAltitude, Altitude);
            F64(b, Adc + X.AdcVerticalSpeed, VerticalSpeed);
            Flag(b, X.WindShown, WindShown);
            F64(b, Nd + NdGroundSpeed, GroundSpeed);   // the nd's own ground speed: never read (the show byte decides)
            PutRunway(b, Nd + (uint)X.DepartureRunway, Departure);
            PutRunway(b, Nd + (uint)X.ArrivalRunway, Arrival);
            PutPoints(b, PathAt + (uint)X.DecelerationStart, StartPoints, Starts);
            PutPoints(b, PathAt + (uint)X.DecelerationEnd, EndPoints, Ends);
            Tweak?.Invoke(b);
        }

        private static void PutRunway(byte[] b, uint at, Runway? r)
        {
            if (r is null)
            {
                // An empty name with the record's stale positions: BlueBird draws nothing.
                F64(b, at + X.RunwayFirstLatitude, 999);
                return;
            }
            F64(b, at + X.RunwayFirstLongitude, r.First.Lon);
            F64(b, at + X.RunwayFirstLatitude, r.First.Lat);
            F64(b, at + X.RunwaySecondLongitude, r.Second.Lon);
            F64(b, at + X.RunwaySecondLatitude, r.Second.Lat);
            for (int i = 0; i < r.Name.Length; i++) b[at + X.RunwayName + i] = (byte)r.Name[i];
            b[at + X.RunwayName + 11] = (byte)r.Name.Length;
        }

        private static void PutPoints(byte[] b, uint header, uint at, (double Lat, double Lon)[] points)
        {
            int bytes = points.Length * X.DecelerationStride;
            I32(b, header, bytes == 0 ? 0 : at);
            I32(b, header + 4, bytes == 0 ? 0 : at + bytes);
            I32(b, header + 8, bytes == 0 ? 0 : at + bytes);
            for (int i = 0; i < points.Length; i++)
            {
                F64(b, at + i * X.DecelerationStride + X.DecelerationLongitude, points[i].Lon);
                F64(b, at + i * X.DecelerationStride + X.DecelerationLatitude, points[i].Lat);
            }
        }
    }

    private static void I16(byte[] b, long at, short v) => System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(checked((int)at)), v);

    /// <summary>The simulator's inputs for one reading: the wind and the gear (null: not delivered).</summary>
    private static Bbs757SimValues Sim(double? windSpeed = 70, double? windDirection = 287, double? gear = 0)
    {
        var values = new Dictionary<string, double>();
        if (windSpeed is { } s) values[Bbs757EhsiReader.WindSpeedKey] = s;
        if (windDirection is { } d) values[Bbs757EhsiReader.WindDirectionKey] = d;
        if (gear is { } g) values[Bbs757EhsiReader.GearKey] = g;
        return new Bbs757SimValues(values);
    }

    internal static Bbs757EhsiReading Read(Extras extras, int mode = 2, double range = 160, bool full = true,
        Bbs757SimValues? sim = null, Action<Scene>? scene = null, Bbs757EhsiLayout? layout = null)
    {
        var s = new Scene { Mode = mode, Range = range, Tweak = extras.Write };
        scene?.Invoke(s);
        using var r = Reader(s.Build(), full, layout: layout ?? P);
        Assert.True(r.TryRead(sim ?? Sim(), out var reading));
        return reading;
    }

    private static string[] Rows(Bbs757EhsiReading r) => Bbs757EhsiText.Rows(r).ToArray();

    private static string[] Starting(Bbs757EhsiReading r, string prefix) =>
        Rows(r).Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

    // ---- TERR ----

    [Fact]
    public void Terr_reads_its_word_and_two_elevations()
    {
        // The live parked capture: 3123 and 485 ft against the adc's 396.9 ft: "031" red over "004" amber, the picture on.
        var live = new Extras { Terr = true, Picture = true };
        var reading = Read(live);
        Assert.False(reading.Corrupt);
        var rows = Rows(reading);
        int terr = Array.IndexOf(rows, "Top right: TERR");
        Assert.True(terr > 0);
        Assert.StartsWith("Top right: ", rows[terr - 1], StringComparison.Ordinal);   // the top line's ETA, then TERR
        Assert.Equal("Top right: terrain 031 red, 004 amber", rows[terr + 1]);
        Assert.Equal("Map: terrain shown, not read", rows[terr + 2]);
        Assert.Equal(new Bbs757EhsiTerrainNumber(31, Bbs757EhsiTerrainColor.Red), reading.MapExtras!.Highest);

        // The First Officer's side reads its own record (1500 and 100 ft: both amber, the gear up).
        Assert.Contains("Top right: terrain 015 amber, 001 amber", Rows(Read(new Extras { Terr = true, Side = 7 })));

        // The word and the numbers in every mode while the IRS is valid; the picture not in PLAN and not centered.
        foreach (int mode in new[] { 0, 1, 2, 3 })
        {
            var on = Rows(Read(live, mode));
            Assert.Contains("Top right: TERR", on);
            Assert.Contains("Top right: terrain 031 red, 004 amber", on);
            // MAP has a map to show it on; VOR and ILS have none, so their row names the picture alone.
            Assert.Equal(mode == 2, on.Contains("Map: terrain shown, not read"));
            Assert.Equal(mode is 0 or 1, on.Contains("Terrain picture shown, not read"));
            Assert.DoesNotContain(Rows(Read(live, mode, scene: s => s.Centered = true)), r => r.Contains("shown, not read", StringComparison.Ordinal));
        }
        // The button off, or the IRS not valid: nothing.
        Assert.Empty(Starting(Read(new Extras { Picture = true }), "Top right: T"));
        Assert.DoesNotContain(Rows(Read(new Extras { Picture = true })), r => r.Contains("terrain", StringComparison.Ordinal));
        Assert.DoesNotContain(Rows(Read(live, scene: s => s.Valid = false)), r => r.Contains("TERR", StringComparison.Ordinal) || r.Contains("terrain", StringComparison.Ordinal));
        // The picture byte clear: the word and numbers only.
        Assert.DoesNotContain("Map: terrain shown, not read", Rows(Read(new Extras { Terr = true })));

        // Three digits as drawn: the feet ÷ 100 (integer division), never below 0.
        Assert.Contains("Top right: terrain 000 green, 000 green", Rows(Read(new Extras { Terr = true, Highest = -320, Lowest = 99, Altitude = 5000 })));
        Assert.Contains("Top right: terrain 125 red, 009 green", Rows(Read(new Extras { Terr = true, Highest = 12599, Lowest = 999, Altitude = 3000 })));

        // No air data computer: no color word.
        Assert.Contains("Top right: terrain 031, 004", Rows(Read(new Extras { Terr = true, HasAdc = false })));
        // TERR is never spoken.
        Assert.Empty(Bbs757EhsiText.Changes(Read(new Extras()), Read(live), true));
    }

    [Theory]
    // d = feet - altitude: above 2000 red; above -500 (gear up) or -250 (gear down) amber; else green.
    [InlineData(2500, 0, 0.0, "red")]
    [InlineData(1500, 0, 0.0, "amber")]
    [InlineData(-400, 0, 0.0, "amber")]
    [InlineData(-400, 0, 1.0, "green")]
    [InlineData(-200, 0, 1.0, "amber")]
    [InlineData(-600, 0, 0.0, "green")]
    // The gear not delivered yet: told where both thresholds agree, no color word between them.
    [InlineData(-400, 0, null, "")]
    [InlineData(-100, 0, null, "amber")]
    [InlineData(-600, 0, null, "green")]
    [InlineData(2500, 0, null, "red")]
    // Descending faster than 1000 ft/min: the altitude 30 s ahead (1000 - 2000 / 60 × 30 = 0): 1900 above is then 2900.
    [InlineData(1900, -2000, 0.0, "red")]
    [InlineData(1900, -1000, 0.0, "amber")]
    public void Terrain_colors_follow_the_draws_thresholds(int feetAboveThousand, double verticalSpeed, double? gear, string color)
    {
        var extras = new Extras { Terr = true, Highest = (short)(1000 + feetAboveThousand), Altitude = 1000, VerticalSpeed = verticalSpeed };
        string highest = Starting(Read(extras, sim: Sim(gear: gear)), "Top right: terrain ").Single();
        string number = Math.Max(0, (1000 + feetAboveThousand) / 100).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("Top right: terrain " + number + (color.Length > 0 ? " " + color : "") + ",", highest, StringComparison.Ordinal);
    }

    [Fact]
    public void The_look_ahead_never_goes_below_its_floor()
    {
        // 100 ft at -3000 ft/min: 100 - 1500 = -1400, floored at -100: 700 ft is then 800 above (amber), not 2100 (red).
        var extras = new Extras { Terr = true, Highest = 700, Altitude = 100, VerticalSpeed = -3000 };
        Assert.StartsWith("Top right: terrain 007 amber,", Starting(Read(extras), "Top right: terrain ").Single(), StringComparison.Ordinal);
        Assert.Equal(Bbs757EhsiTerrainColor.Amber, Bbs757EhsiReader.TerrainNumber(700, -100, 0, X).Color);
        Assert.Equal(Bbs757EhsiTerrainColor.Red, Bbs757EhsiReader.TerrainNumber(700, -1400, 0, X).Color);
    }

    [Fact]
    public void A_terr_value_that_is_not_a_number_is_corrupt()
    {
        Assert.True(Read(new Extras { Terr = true, Altitude = double.NaN }).Corrupt);
        Assert.True(Read(new Extras { Terr = true, VerticalSpeed = double.PositiveInfinity }).Corrupt);
        Assert.True(Read(new Extras { Tweak = b => F64(b, Nd + X.TerrHandle + X.HandleValue, double.NaN) }).Corrupt);
        Assert.True(Read(new Extras { Terr = true, Tweak = b => I32(b, Source + X.SourceToAdc, Outside) }).Corrupt);
    }

    [Fact]
    public void Terr_off_never_reads_the_air_data_computer()
    {
        // The colors are the only use of the adc's altitude and vertical speed: with the button off they are not read, so
        // a value that is not a number, or an adc pointer outside the module, is not torn. In PLAN, where the map guides'
        // altitude range arc (which reads the same adc) is not read.
        Assert.False(Read(new Extras { Altitude = double.NaN }, mode: 3).Corrupt);
        Assert.False(Read(new Extras { VerticalSpeed = double.PositiveInfinity }, mode: 3).Corrupt);
        Assert.False(Read(new Extras { Tweak = b => I32(b, Source + X.SourceToAdc, Outside) }, mode: 3).Corrupt);
        Assert.Equal(Bbs757EhsiTerrainColor.None, Read(new Extras { Altitude = double.NaN }, mode: 3).MapExtras!.Highest.Color);
        // The button on: the same values are torn.
        Assert.True(Read(new Extras { Terr = true, Altitude = double.NaN }, mode: 3).Corrupt);
    }

    // ---- the wind ----

    // Review Focus: with the flag clear the row reads dashes, never a stale speed. BlueBird's draw clears the flag under
    // 100 kt of ground speed itself, so the flag alone decides.
    [Fact]
    public void Wind_reads_dashes_when_the_screen_does()
    {
        // Plan 26's cruise: the flag set, 70 kt from 287° true against the map's rotation 134.43: 153° right.
        var shown = new Extras { WindShown = true };
        Assert.Equal(new[] { "Bottom left: wind 70 kt from 153° right" }, Starting(Read(shown), "Bottom left: wind"));
        // The speed truncated as drawn.
        Assert.Equal(new[] { "Bottom left: wind 70 kt from 153° right" }, Starting(Read(shown, sim: Sim(windSpeed: 70.9)), "Bottom left: wind"));
        // From ahead and from behind.
        Assert.Equal(new[] { "Bottom left: wind 70 kt from ahead" }, Starting(Read(shown, sim: Sim(windDirection: 134.43)), "Bottom left: wind"));
        Assert.Equal(new[] { "Bottom left: wind 70 kt from behind" }, Starting(Read(shown, sim: Sim(windDirection: 314.43)), "Bottom left: wind"));
        Assert.Equal(new[] { "Bottom left: wind 70 kt from 30° left" }, Starting(Read(shown, sim: Sim(windDirection: 104.43)), "Bottom left: wind"));

        // The flag clear (the live parked case: 2 kt from 40°; or under 100 kt of ground speed, where BlueBird's draw
        // clears it): the dashes, whatever the simulator's wind says.
        Assert.Equal(new[] { "Bottom left: wind ---" }, Starting(Read(new Extras(), sim: Sim(windSpeed: 2, windDirection: 40)), "Bottom left: wind"));
        Assert.Equal(new[] { "Bottom left: wind ---" }, Starting(Read(new Extras { GroundSpeed = 99.9 }), "Bottom left: wind"));
        Assert.Equal(new[] { "Bottom left: wind ---" }, Starting(Read(new Extras(), sim: Sim(null, null)), "Bottom left: wind"));
        // The flag set: the wind, whatever the nd's ground speed holds (the reader never redoes the draw's test).
        Assert.Equal(new[] { "Bottom left: wind 70 kt from 153° right" }, Starting(Read(new Extras { WindShown = true, GroundSpeed = 99.9 }), "Bottom left: wind"));
        var nan = Read(new Extras { WindShown = true, GroundSpeed = double.NaN });
        Assert.False(nan.Corrupt);
        Assert.Equal(new[] { "Bottom left: wind 70 kt from 153° right" }, Starting(nan, "Bottom left: wind"));

        // Shown, but an input not delivered yet: not read yet, never a speed.
        foreach (var sim in new[] { Sim(windSpeed: null), Sim(windDirection: null), Sim(null, null), Sim(windSpeed: double.NaN) })
            Assert.Equal(new[] { "Bottom left: wind not read yet" }, Starting(Read(shown, sim: sim), "Bottom left: wind"));
        // Read without the simulator's values at all: the same.
        using (var r = Reader(new Scene { Tweak = shown.Write }.Build(), true, layout: P))
        {
            Assert.True(r.TryRead(out var bare));
            Assert.Equal(new[] { "Bottom left: wind not read yet" }, Starting(bare, "Bottom left: wind"));
        }

        // MAP, VOR and ILS while the IRS is valid; never PLAN; in the light reading too.
        foreach (int mode in new[] { 0, 1, 2 })
        {
            Assert.Single(Starting(Read(shown, mode), "Bottom left: wind"));
            Assert.Single(Starting(Read(shown, mode, full: false), "Bottom left: wind"));
            Assert.Empty(Starting(Read(shown, mode, scene: s => s.Valid = false), "Bottom left: wind"));
        }
        Assert.Empty(Starting(Read(shown, mode: 3), "Bottom left: wind"));

        // Never spoken.
        Assert.Empty(Bbs757EhsiText.Changes(Read(new Extras()), Read(shown), true));
        Assert.Empty(Bbs757EhsiText.Changes(Read(shown), Read(shown, sim: Sim(windSpeed: 12, windDirection: 10)), true));
    }

    // ---- the runways ----

    [Fact]
    public void A_runway_is_listed_by_its_nearer_end()
    {
        // RW16L at range 10: its threshold 2.0 NM ahead-left, its far end 3.9 NM on.
        var near = Bbs757EhsiTcasTests.At(2, -15);
        var far = Bbs757EhsiTcasTests.At(3.9, -14);
        var reading = Read(new Extras
        {
            Departure = new Runway("16L", near, far),
            Arrival = new Runway("10L", (45.59480, -122.59561), (45.58169, -122.56199)),   // KPDX: far off the screen
        }, range: 10);
        Assert.False(reading.Corrupt);
        Assert.Equal(new[] { "Map: runway 16L, 2.0 NM, 15° left" }, Starting(reading, "Map: runway"));
        Assert.Equal(2, reading.MapExtras!.Runways!.Count);

        // The nearer end is named even when only the far end is on the screen (the nearer one behind, past the bottom).
        var behind = Read(new Extras { Departure = new Runway("16L", Bbs757EhsiTcasTests.At(3, 180), Bbs757EhsiTcasTests.At(8, 0)) }, range: 10);
        Assert.Equal(new[] { "Map: runway 16L, 3.0 NM, behind" }, Starting(behind, "Map: runway"));
        // Either end listed by the nearer, whichever is end 1.
        var reversed = Read(new Extras { Departure = new Runway("34R", far, near) }, range: 10);
        Assert.Equal(new[] { "Map: runway 34R, 2.0 NM, 15° left" }, Starting(reversed, "Map: runway"));
        // Both ends off the screen: nothing.
        Assert.Empty(Starting(Read(new Extras { Departure = new Runway("16L", Bbs757EhsiTcasTests.At(30, 0), Bbs757EhsiTcasTests.At(31, 0)) }, range: 10), "Map: runway"));
        // An empty name: no runway, its stale positions never checked.
        var empty = Read(new Extras(), range: 10);
        Assert.False(empty.Corrupt);
        Assert.Empty(empty.MapExtras!.Runways!);

        // PLAN places it from the plan's center; VOR draws no map; the light reading reads no runway.
        Assert.Equal(new[] { "Map: runway 16L, 2.0 NM south-east of the center" },
            Starting(Read(new Extras { Departure = new Runway("16L", near, far) }, mode: 3, range: 10), "Map: runway"));
        Assert.Empty(Starting(Read(new Extras { Departure = new Runway("16L", near, far) }, mode: 0, range: 10), "Map: runway"));
        var light = Read(new Extras { Departure = new Runway("16L", near, far) }, range: 10, full: false);
        Assert.Null(light.MapExtras!.Runways);

        // All or nothing: a position out of range or a torn name is corrupt.
        Assert.True(Read(new Extras { Departure = new Runway("16L", (91, 0), far) }, range: 10).Corrupt);
        Assert.True(Read(new Extras { Departure = new Runway("16L", near, (double.NaN, 0)) }, range: 10).Corrupt);
        Assert.True(Read(new Extras
        {
            Departure = new Runway("16L", near, far),
            Tweak = b => { I32(b, Nd + X.DepartureRunway + X.RunwayName, Outside); I32(b, Nd + X.DepartureRunway + X.RunwayName + 4, 3); b[Nd + X.DepartureRunway + X.RunwayName + 11] = 0x80; },
        }, range: 10).Corrupt);
        Assert.True(Read(new Extras { Departure = new Runway("16L", near, far), Tweak = b => b[Nd + X.DepartureRunway + X.RunwayName + 1] = 7 }, range: 10).Corrupt);
    }

    // ---- the deceleration points ----

    // The plan's older text had "Unnamed_points_are_counted_not_invented"; the controller ruled the green rings ARE named
    // from BlueBird's structure: the start and the end of the next deceleration.
    [Fact]
    public void Deceleration_points_are_listed_where_drawn()
    {
        var extras = new Extras
        {
            Starts = new[] { Bbs757EhsiTcasTests.At(6, 10) },
            Ends = new[] { Bbs757EhsiTcasTests.At(9, 20) },
        };
        var reading = Read(extras, range: 20);
        Assert.False(reading.Corrupt);
        // In screen order: the end, farther ahead, is higher on the screen.
        Assert.Equal(new[] { "Map: deceleration ends, 9.0 NM, 20° right", "Map: deceleration starts, 6.0 NM, 10° right" },
            Starting(reading, "Map: deceleration"));
        // Off the screen: not listed (still read).
        var off = Read(new Extras { Starts = new[] { Bbs757EhsiTcasTests.At(30, 10) }, Ends = extras.Ends }, range: 20);
        Assert.Equal(new[] { "Map: deceleration ends, 9.0 NM, 20° right" }, Starting(off, "Map: deceleration"));
        Assert.Single(off.MapExtras!.DecelerationStarts!);
        // In PLAN too; never in VOR; never spoken.
        Assert.Equal(2, Starting(Read(extras, mode: 3, range: 20), "Map: deceleration").Length);
        Assert.Empty(Starting(Read(extras, mode: 0, range: 20), "Map: deceleration"));
        Assert.Empty(Bbs757EhsiText.Changes(Read(new Extras(), range: 20), reading, true));
        // None, or no path for the route: nothing, not corrupt.
        Assert.Empty(Starting(Read(new Extras(), range: 20), "Map: deceleration"));
        var noPath = Read(extras, range: 20, scene: s => s.HasRegistry = false);
        Assert.False(noPath.Corrupt);
        Assert.Empty(noPath.MapExtras!.DecelerationStarts!);

        // All or nothing: more than the cap, a torn vector, a point out of range, a vector outside the module.
        Assert.True(Read(new Extras { Starts = Enumerable.Repeat(Bbs757EhsiTcasTests.At(6, 10), X.MaxDecelerationPoints + 1).ToArray() }, range: 20).Corrupt);
        Assert.False(Read(new Extras { Starts = Enumerable.Repeat(Bbs757EhsiTcasTests.At(6, 10), X.MaxDecelerationPoints).ToArray() }, range: 20).Corrupt);
        Assert.True(Read(new Extras { Starts = extras.Starts, Tweak = b => I32(b, PathAt + X.DecelerationStart + 4, StartPoints + 8) }, range: 20).Corrupt);
        Assert.True(Read(new Extras { Ends = new[] { (95.0, 0.0) } }, range: 20).Corrupt);
        Assert.True(Read(new Extras { Ends = extras.Ends, Tweak = b => { I32(b, PathAt + X.DecelerationEnd, Outside); I32(b, PathAt + X.DecelerationEnd + 4, Outside + 16); } }, range: 20).Corrupt);
    }

    // ---- the leg position rule (plan 26's fix) ----

    internal static readonly (double Lat, double Lon) Primary = (47.2, -122.6), Second = (47.14767, -122.47504);

    /// <summary>A route whose first leg is a navaid leg (kind 2) with both positions and a capability set; null values
    /// leave the set as <paramref name="nodes"/> chains it (for the torn cases).</summary>
    internal static Scene NavaidLeg(int[] capabilities, (double Lat, double Lon)? primary = null, (double Lat, double Lon)? second = null,
        Action<byte[]>? tweak = null)
    {
        var p = primary ?? Primary;
        var s = second ?? Second;
        var scene = new Scene();
        scene.RouteLegs = new[] { new TLeg("TCM", 2, p.Lat, p.Lon, 5), scene.RouteLegs[^1] };
        scene.Tweak = b =>
        {
            F64(b, Legs + 832, p.Lat);
            F64(b, Legs + 840, p.Lon);
            F64(b, Legs + 880, s.Lat);
            F64(b, Legs + 888, s.Lon);
            I32(b, Legs + X.LegCapabilityFirst, capabilities.Length == 0 ? 0 : Nodes);
            I32(b, Legs + X.LegCapabilityCount, capabilities.Length);
            for (int i = 0; i < capabilities.Length; i++)
            {
                uint node = Nodes + (uint)(i * 16);
                I32(b, node + X.CapabilityNodeNext, i + 1 < capabilities.Length ? node + 16 : 0);
                I32(b, node + 4, capabilities[i] * 31);   // the hash, never read
                I32(b, node + X.CapabilityNodeValue, capabilities[i]);
            }
            tweak?.Invoke(b);
        };
        return scene;
    }

    private static (double Lat, double Lon) First(Scene scene, Bbs757EhsiLayout? layout = null, bool full = true)
    {
        var reading = Bbs757EhsiReaderTests.Read(scene, full, layout ?? P);
        Assert.False(reading.Corrupt);
        var leg = full ? reading.Route![0] : reading.Active!.Value;
        return (leg.Latitude, leg.Longitude);
    }

    [Fact]
    public void A_tacan_only_leg_reads_its_second_position()
    {
        // FIX INFO's TCM, measured: the set {9, 19, 18, 3}, the primary position (0, 0), the second the right one.
        var tcm = NavaidLeg(new[] { 9, 19, 18, 3 }, primary: (0, 0));
        Assert.Equal(Second, First(tcm));
        Assert.Equal(Second, First(NavaidLeg(new[] { 9, 19, 18, 3 }, primary: (0, 0)), full: false));   // the active leg too
        // Without the map extras in the layout, plan 26's rule stands: the kind's own offsets.
        Assert.Equal((0.0, 0.0), First(NavaidLeg(new[] { 9, 19, 18, 3 }, primary: (0, 0)), P with { MapExtras = null, MapExtrasMissing = "not in this layout file" }));
    }

    [Theory]
    [InlineData(new[] { 0 }, false)]
    [InlineData(new[] { 1 }, false)]
    [InlineData(new[] { 5 }, true)]
    [InlineData(new[] { 19 }, true)]
    [InlineData(new[] { 13 }, true)]
    [InlineData(new[] { 2 }, true)]
    [InlineData(new[] { 3 }, false)]
    [InlineData(new[] { 9 }, false)]
    [InlineData(new int[0], false)]
    [InlineData(new[] { 3, 5 }, true)]    // in the rule's order, not the set's: 5 before 3
    [InlineData(new[] { 2, 0 }, false)]   // 0 first
    public void Each_branch_of_the_leg_position_rule(int[] capabilities, bool second)
    {
        Assert.Equal(second ? Second : Primary, First(NavaidLeg(capabilities)));
    }

    [Fact]
    public void A_capability_of_19_or_13_falls_back_when_the_second_position_is_zero()
    {
        Assert.Equal(Primary, First(NavaidLeg(new[] { 19 }, second: (0, 0))));
        Assert.Equal(Primary, First(NavaidLeg(new[] { 13 }, second: (0, 0))));
        // 5 and 2 take the second whatever it holds.
        Assert.Equal((0.0, 0.0), First(NavaidLeg(new[] { 5 }, second: (0, 0))));
    }

    [Fact]
    public void A_torn_capability_set_is_corrupt()
    {
        static bool Torn(Scene s) => Bbs757EhsiReaderTests.Read(s, true, P).Corrupt;
        // The size says three, the list ends after two.
        Assert.True(Torn(NavaidLeg(new[] { 9, 19 }, tweak: b => I32(b, Legs + X.LegCapabilityCount, 3))));
        // The list goes on past the size.
        Assert.True(Torn(NavaidLeg(new[] { 9, 19, 18 }, tweak: b => I32(b, Legs + X.LegCapabilityCount, 2))));
        // A node outside the module.
        Assert.True(Torn(NavaidLeg(new[] { 9, 19 }, tweak: b => I32(b, Nodes + X.CapabilityNodeNext, Outside))));
        // More than the cap.
        Assert.True(Torn(NavaidLeg(new[] { 9 }, tweak: b => I32(b, Legs + X.LegCapabilityCount, X.MaxCapabilities + 1))));
        Assert.True(Torn(NavaidLeg(new[] { 9 }, tweak: b => I32(b, Legs + X.LegCapabilityCount, -1))));
        // A leg of another kind never reads the set, torn or not.
        var other = NavaidLeg(new[] { 9 }, tweak: b => I32(b, Legs + X.LegCapabilityCount, 5));
        other.RouteLegs = new[] { other.RouteLegs[0] with { Kind = 1 }, other.RouteLegs[1] };
        Assert.False(Torn(other));
    }
}
