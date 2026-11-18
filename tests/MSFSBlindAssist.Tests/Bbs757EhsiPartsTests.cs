// What every one of plan 28's five EHSI parts shares: a part the layout lacks (or this build does not read yet) costs
// only its own rows, one "not read" row where they would start, in the modes where it would show something; and every
// row the parts add formats the same on a comma-decimal PC.

using MSFSBlindAssist.Services.BBS757;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757EhsiReaderTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiPartsTests
{
    private static readonly Bbs757EhsiLayout WithParts = Bbs757EhsiLayoutTests.SampleWithParts();

    private static string[] Rows(Bbs757EhsiLayout layout, int mode, bool full = true, bool valid = true)
    {
        var reading = Bbs757EhsiReaderTests.Read(new Scene { Mode = mode, Valid = valid }, full, layout);
        Assert.False(reading.Corrupt);
        return Bbs757EhsiText.Rows(reading).ToArray();
    }

    private static string[] NotRead(string[] rows) => rows.Where(r => r.Contains(": not read: ", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void A_missing_part_lists_not_read_with_its_reason()
    {
        // A layout file from before plan 28: every part missing with the file's reason, in MAP each where its rows start
        // (TERR's beside the top right, TCAS's left block, the map's own parts before the map's items, the bearing pointers
        // before the heading bug).
        var old = Bbs757EhsiLayoutTests.Sample();
        var map = Rows(old, mode: 2);
        Assert.Equal(new[]
        {
            "TERR, wind and runways: not read: not in this layout file",
            "TCAS: not read: not in this layout file",
            "Map options: not read: not in this layout file",
            "FIX, descent and altitude guides: not read: not in this layout file",
            "VOR and ILS: not read: not in this layout file",
        }, NotRead(map));
        Assert.StartsWith("Top right: ", map[Array.IndexOf(map, "TERR, wind and runways: not read: not in this layout file") - 1], StringComparison.Ordinal);
        Assert.Equal("TCAS: not read: not in this layout file", map[Array.IndexOf(map, "TERR, wind and runways: not read: not in this layout file") + 1]);
        Assert.StartsWith("Heading bug: ", map[Array.IndexOf(map, "VOR and ILS: not read: not in this layout file") + 1], StringComparison.Ordinal);

        // In VOR and ILS the approach's row replaces its whole picture, from the top left; no map options or guides. The
        // top right ETA and the heading bug are plan 26's and stay.
        foreach (int mode in new[] { 0, 1 })
        {
            var rows = Rows(old, mode);
            Assert.Equal(new[]
            {
                mode == 0 ? "EHSI: VOR, range 160" : "EHSI: ILS, range 160",
                "Top: HDG 119 M",
                "VOR and ILS: not read: not in this layout file",
                rows[3],
                "TERR, wind and runways: not read: not in this layout file",
                "TCAS: not read: not in this layout file",
                "Heading bug: 147, 28° right",
            }, rows);
            Assert.StartsWith("Top right: ", rows[3], StringComparison.Ordinal);
        }

        // PLAN draws no bearing pointers; MAP with the IRS not valid draws no map and no pointers.
        Assert.Equal(new[]
        {
            "TERR, wind and runways: not read: not in this layout file",
            "TCAS: not read: not in this layout file",
            "Map options: not read: not in this layout file",
            "FIX, descent and altitude guides: not read: not in this layout file",
        }, NotRead(Rows(old, mode: 3)));
        Assert.Equal(new[]
        {
            "TERR, wind and runways: not read: not in this layout file",
            "TCAS: not read: not in this layout file",
        }, NotRead(Rows(old, mode: 2, valid: false)));

        // Each part with its own reason: the recipe's failing step. The parts read from a layout that has them list no
        // "not read" row.
        var missing = WithParts with
        {
            Approach = null, ApproachMissing = "course field not found",
            Tcas = null, TcasMissing = "traffic vector not found",
            MapOptions = null, MapOptionsMissing = "navaid list not found",
            MapExtras = null, MapExtrasMissing = "terrain static not found",
            MapGuides = null, MapGuidesMissing = "fix: no fms",
        };
        Assert.Equal(new[]
        {
            "TERR, wind and runways: not read: terrain static not found",
            "TCAS: not read: traffic vector not found",
            "Map options: not read: navaid list not found",
            "FIX, descent and altitude guides: not read: fix: no fms",
            "VOR and ILS: not read: course field not found",
        }, NotRead(Rows(missing, mode: 2)));
        // The map extras missing cost only their own rows: no TERR, wind, runway or deceleration row, in any mode.
        foreach (int mode in new[] { 0, 1, 2, 3 })
        {
            var rows = Rows(missing, mode);
            Assert.Contains("TERR, wind and runways: not read: terrain static not found", rows);
            Assert.DoesNotContain(rows, r => new[] { "Bottom left: wind", "Top right: TERR", "Top right: terrain", "Map: runway", "Map: deceleration" }
                .Any(p => r.StartsWith(p, StringComparison.Ordinal)));
        }
        // Every part present: no "not read" row in any mode.
        foreach (int mode in new[] { 0, 1, 2, 3 })
            Assert.Empty(NotRead(Rows(WithParts, mode)));

        // The light reading carries the reasons too.
        Assert.Equal(NotRead(Rows(missing, mode: 2)), NotRead(Rows(missing, mode: 2, full: false)));
        // A reading with neither a part nor a reason (dark, corrupt, or built by hand) lists no "not read" row.
        Assert.Empty(NotRead(Bbs757EhsiText.Rows(Bbs757EhsiTextTests.Ehsi()).ToArray()));
    }

    // What the light and full readings read is only what the mode can show: a torn value no row could show is never read,
    // so it never turns a reading corrupt.
    [Fact]
    public void A_torn_value_the_mode_cannot_show_is_never_read()
    {
        var P = Bbs757EhsiApproachTests.P;
        bool Torn(int mode, Action<byte[]> write, bool valid = true, bool full = true) =>
            Bbs757EhsiReaderTests.Read(new Scene { Mode = mode, Range = 20, Valid = valid, Tweak = write }, full, P).Corrupt;

        // The VOR and ILS picture: only in VOR and ILS (a torn PFD pointer); the bearing pointers in every mode but PLAN,
        // while the IRS is valid (a torn VOR panel pointer).
        void Pfd(byte[] b) { Bbs757EhsiApproachTests.VorSea().Write(b); I32(b, Nd + P.Approach!.NdToPfd, Outside); }
        void Panel(byte[] b) { Bbs757EhsiApproachTests.VorSea().Write(b); I32(b, Nd + P.Approach!.NdToVorLeft, Outside); }
        Assert.True(Torn(1, Pfd));
        Assert.True(Torn(0, Pfd));
        Assert.False(Torn(2, Pfd));
        Assert.False(Torn(3, Pfd));
        Assert.True(Torn(2, Panel));
        Assert.True(Torn(0, Panel));
        Assert.False(Torn(3, Panel));
        Assert.False(Torn(2, Panel, valid: false));

        // The traffic: only in MAP while the IRS is valid.
        var tcas = new Bbs757EhsiTcasTests.Tcas { Traffic = new[] { new Bbs757EhsiTcasTests.Entry(4, 0, 100, 47.45, -122.3) } };
        Assert.True(Torn(2, tcas.Write));
        Assert.False(Torn(0, tcas.Write));
        Assert.False(Torn(3, tcas.Write));
        Assert.False(Torn(2, tcas.Write, valid: false));

        // The map options, the runways, the FIX pages and the off-path circles: only while the map is drawn (MAP with the
        // IRS valid, or PLAN).
        void Options(byte[] b) => I32(b, Nd + P.MapOptions!.NdToSuper, Outside);
        void Runway(byte[] b) => b[Nd + P.MapExtras!.DepartureRunway + P.MapExtras.RunwayName + 11] = 11;
        var fix = new Bbs757EhsiMapGuidesTests.Guides
        {
            Fix1 = Bbs757EhsiMapGuidesTests.Tcm,
            Tweak = b => I32(b, Bbs757EhsiMapOptionsTests.Fms + P.MapGuides!.FmsFix1, Outside),
        };
        var offpath = new Bbs757EhsiMapGuidesTests.Guides { OffpathOn = true, Clean = double.NaN };
        foreach (var write in new Action<byte[]>[] { Options, Runway, fix.Write, offpath.Write })
        {
            Assert.True(Torn(2, write));
            Assert.True(Torn(3, write));
            Assert.False(Torn(2, write, valid: false));
            Assert.False(Torn(0, write));
        }
    }

    // Re-review finding 4 (B757-49): TERR is drawn in every mode but only while the IRS is valid; the wind in MAP, VOR and
    // ILS while the IRS is valid, never in PLAN. A torn value there is never read anywhere else.
    [Fact]
    public void Terr_and_the_wind_are_read_only_where_they_are_drawn()
    {
        var P = Bbs757EhsiApproachTests.P;
        var x = P.MapExtras!;
        bool Torn(int mode, Action<byte[]>? write, bool valid = true, Bbs757EhsiLayout? layout = null, bool full = true) =>
            Bbs757EhsiReaderTests.Read(new Scene { Mode = mode, Range = 20, Valid = valid, Tweak = write }, full, layout ?? P).Corrupt;

        // TERR's button value not a number: torn in every mode while the IRS is valid, never read while it is not.
        void Terr(byte[] b) => F64(b, Nd + x.TerrHandle + x.HandleValue, double.NaN);
        foreach (int mode in new[] { 0, 1, 2, 3 })
        {
            Assert.True(Torn(mode, Terr));
            Assert.False(Torn(mode, Terr, valid: false));
            Assert.False(Torn(mode, Terr, valid: false, full: false));
        }

        // The wind's show byte where no module memory is: torn in MAP, VOR and ILS while the IRS is valid; never read in
        // PLAN or while the IRS is not valid.
        var wind = P with { MapExtras = x with { WindShown = Outside } };
        foreach (int mode in new[] { 0, 1, 2 })
        {
            Assert.True(Torn(mode, null, layout: wind));
            Assert.False(Torn(mode, null, valid: false, layout: wind));
        }
        Assert.False(Torn(3, null, layout: wind));
        Assert.False(Torn(3, null, layout: wind, full: false));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Rows_format_under_de_DE_and_sv_SE(string culture)
    {
        var vor = Bbs757EhsiApproachTests.Read(Bbs757EhsiApproachTests.VorSea(), mode: 0);
        var ils = Bbs757EhsiApproachTests.Read(Bbs757EhsiApproachTests.IlsIsnq(), mode: 1);
        var unpinned = Bbs757EhsiApproachTests.IlsIsnq();
        unpinned.LocalizerDeviation = -0.6;
        unpinned.Glideslope = -1.24;
        var ilsOff = Bbs757EhsiApproachTests.Read(unpinned, mode: 1);
        var traffic = Bbs757EhsiReaderTests.Read(new Scene
        {
            Range = 20,
            Tweak = b =>
            {
                Bbs757EhsiApproachTests.VorSea().Write(b);
                new Bbs757EhsiTcasTests.Tcas
                {
                    Tfc = true, Ta = true,
                    Traffic = new[] { new Bbs757EhsiTcasTests.Entry(1, 1, 1065, Bbs757EhsiTcasTests.At(3.76, -10.9).Lat, Bbs757EhsiTcasTests.At(3.76, -10.9).Lon) },
                }.Write(b);
            },
        }, true, Bbs757EhsiApproachTests.P);

        var options = Bbs757EhsiMapOptionsTests.Read(new Bbs757EhsiMapOptionsTests.Options
        {
            NavAid = true, Airport = true, Waypoint = true, TunedLeft = 0,
            NavaidList = new Bbs757EhsiMapOptionsTests.Nav?[] { Bbs757EhsiMapOptionsTests.Sea, Bbs757EhsiMapOptionsTests.Pae with { Nm = 12.34 } },
            AirportList = new[] { new Bbs757EhsiMapOptionsTests.Place("KBFI", 5.26, -10.4) },
            WaypointList = new Bbs757EhsiMapOptionsTests.Place?[] { new Bbs757EhsiMapOptionsTests.Place("HAVHO", 3.06, 30) },
            Left = new Bbs757EhsiMapOptionsTests.Panel(false, Bbs757EhsiMapOptionsTests.Sea, 186.6),
        });
        var plan = Bbs757EhsiMapOptionsTests.Read(new Bbs757EhsiMapOptionsTests.Options
        {
            NavAid = true, NavaidList = new Bbs757EhsiMapOptionsTests.Nav?[] { Bbs757EhsiMapOptionsTests.Pae with { Nm = 12.34 } },
        }, mode: 3);

        // The map extras: TERR, the wind (70.9 kt truncated, from 287.4° true), RW16L and the two green rings at range 20.
        var extras = Bbs757EhsiMapExtrasTests.Read(new Bbs757EhsiMapExtrasTests.Extras
        {
            Terr = true, Picture = true, WindShown = true,
            Departure = new Bbs757EhsiMapExtrasTests.Runway("16L", Bbs757EhsiTcasTests.At(2.04, -15), Bbs757EhsiTcasTests.At(3.9, -14)),
            Starts = new[] { Bbs757EhsiTcasTests.At(6.06, 10) },
            Ends = new[] { Bbs757EhsiTcasTests.At(9.04, 20) },
        }, range: 20, sim: new Bbs757SimValues(new Dictionary<string, double>
        {
            [Bbs757EhsiReader.WindSpeedKey] = 70.9, [Bbs757EhsiReader.WindDirectionKey] = 287.4, [Bbs757EhsiReader.GearKey] = 0,
        }));

        // The map guides: TCM with its circle and radial, the off-path circles (45.5 NM reads 46), the range arc and the
        // VNAV deviation, at range 160; and PLAN's places.
        var guides = new Bbs757EhsiMapGuidesTests.Guides
        {
            Fix1 = Bbs757EhsiMapGuidesTests.Tcm, OffpathOn = true, Clean = 45.5, Selected = 10000, Altitude = 5000, VerticalSpeed = 1500,
            Phase = 4, Deviation = -120.6,
        };
        var guided = Bbs757EhsiMapGuidesTests.Read(guides, range: 160);
        var guidedPlan = Bbs757EhsiMapGuidesTests.Read(guides, mode: 3, range: 160);

        foreach (var reading in new[] { vor, ils, ilsOff, traffic, options, plan, extras, guided, guidedPlan })
            Assert.Equal(TestCulture.Run("en-US", () => Bbs757EhsiText.Rows(reading)), TestCulture.Run(culture, () => Bbs757EhsiText.Rows(reading)));

        var rows = TestCulture.Run(culture, () => Bbs757EhsiText.Rows(vor).Concat(Bbs757EhsiText.Rows(ils)).Concat(Bbs757EhsiText.Rows(ilsOff))
            .Concat(Bbs757EhsiText.Rows(traffic)).Concat(Bbs757EhsiText.Rows(options)).Concat(Bbs757EhsiText.Rows(plan))
            .Concat(Bbs757EhsiText.Rows(extras)).Concat(Bbs757EhsiText.Rows(guided)).Concat(Bbs757EhsiText.Rows(guidedPlan)).ToArray());
        Assert.Contains("Map: fix TCM, 19.1 NM, 67° right", rows);
        Assert.Contains("Map: fix TCM, circle 15 NM", rows);
        Assert.Contains("Map: fix TCM, radial 300", rows);
        Assert.Contains("Map: altitude range arc, 13.9 NM, ahead", rows);
        Assert.Contains("Right side: VNAV path deviation 121 ft below the path", rows);
        Assert.Contains("Map: off-path descent circles, 46 NM and 38 NM around 111.6 NM, 52° right", rows);
        Assert.Contains("Map: off-path descent circles, 46 NM and 38 NM around 111.6 NM south of the center", rows);
        Assert.Contains("Top right: TERR", rows);
        Assert.Contains("Top right: terrain 031 red, 004 amber", rows);
        Assert.Contains("Bottom left: wind 70 kt from 153° right", rows);
        Assert.Contains("Map: runway 16L, 2.0 NM, 15° left", rows);
        Assert.Contains("Map: deceleration starts, 6.1 NM, 10° right", rows);
        Assert.Contains("Map: deceleration ends, 9.0 NM, 20° right", rows);
        Assert.Contains("Map: VORTAC SEA, tuned, 0.5 NM, 173° left", rows);
        Assert.Contains("Map: VOR/DME PAE, 12.3 NM, 40° right", rows);
        Assert.Contains("Map: airport KBFI, 5.3 NM, 10° left", rows);
        Assert.Contains("Map: waypoint HAVHO, 3.1 NM, 30° right", rows);
        Assert.Contains("Map: VOR L course 187 through SEA", rows);
        Assert.Contains("Map: VOR/DME PAE, 12.3 NM south of the center", rows);
        Assert.Contains("Top left: DME 0.5", rows);
        Assert.Contains("Center: deviation 0.5 dots right", rows);
        Assert.Contains("Bearing pointer 1: VOR L, SEA, 74° right", rows);
        // VOR's top right ETA, as MAP's prints it.
        string mapEta = Bbs757EhsiText.Rows(Bbs757EhsiApproachTests.Read(Bbs757EhsiApproachTests.VorSea(), mode: 2))
            .Single(r => r.StartsWith("Top right: ", StringComparison.Ordinal));
        Assert.Contains(mapEta, rows);
        Assert.Contains("Top left: DME 0.6", rows);
        Assert.Contains("Bottom right: 110.30", rows);
        Assert.Contains("Center: deviation 1.2 dots left", rows);
        Assert.Contains("Right side: glideslope 1.2 dots below", rows);
        Assert.Contains("Map: proximate traffic, 3.8 NM, 11° left, +10 climbing", rows);
        Assert.Contains("Right side: TRAFFIC, amber", rows);
    }
}
