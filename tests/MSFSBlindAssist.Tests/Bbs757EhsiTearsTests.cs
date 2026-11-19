// The 757 EHSI reader's part tears (the whole-plan review's I1): a part that tears is a corrupt reading only until it has
// torn the reader's limit of readings in a row; from then on, while it keeps tearing, that part alone reads "not read:
// unexpected values" and the rest of the EHSI (the other parts, the route, Ctrl+W) keeps working, so the shared monitor's
// ten-corrupt stop never comes from one optional part's lasting state. A layout value that can never change at run time is
// checked once, when the reader is built: its part is missing, naming the field.

using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757EhsiReaderTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiTearsTests
{
    private static readonly Bbs757EhsiLayout P = Bbs757EhsiApproachTests.P;

    /// <summary>One reader over memory the test keeps changing, and the lines it logs.</summary>
    private sealed class Session : IDisposable
    {
        public readonly byte[] Bytes;
        public readonly List<string> Log = new();
        private readonly Bbs757EhsiReader _reader;

        public Session(Scene scene, Bbs757EhsiLayout? layout = null, bool full = true)
        {
            Bytes = scene.Build();
            var src = Backing(Bytes);
            _reader = new Bbs757EhsiReader(new ModuleMemory(src, Base), src, layout ?? P, Log.Add) { FullWanted = () => full };
        }

        public Bbs757EhsiReading Read()
        {
            Assert.True(_reader.TryRead(out var reading));
            return reading;
        }

        /// <summary>The scene's memory written again (the part reads well again).</summary>
        public void Restore(Scene scene) => scene.Build().CopyTo(Bytes, 0);

        public void Dispose() => _reader.Dispose();
    }

    private static Scene Traffic(bool torn) => new()
    {
        Range = 20,
        Tweak = new Bbs757EhsiTcasTests.Tcas
        {
            Traffic = torn
                ? new[] { new Bbs757EhsiTcasTests.Entry(4, 0, 100, 47.45, -122.3) }      // a kind outside BlueBird's four
                : new[] { new Bbs757EhsiTcasTests.Entry(2, 0, 100, 47.45, -122.3) },
        }.Write,
    };

    private static string[] NotRead(Bbs757EhsiReading r) =>
        Bbs757EhsiText.Rows(r).Where(row => row.Contains(": not read: ", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void A_part_torn_once_is_corrupt_and_at_the_limit_reads_alone_as_not_read()
    {
        using var s = new Session(Traffic(torn: true));
        for (int i = 1; i < Bbs757EhsiReader.TearLimit; i++)
            Assert.True(s.Read().Corrupt, $"tear {i} of {Bbs757EhsiReader.TearLimit - 1}: corrupt, all or nothing");
        Assert.Empty(s.Log);

        // At the limit, and on while it keeps tearing: the part alone, one row; the route, the other parts and Ctrl+W work.
        for (int i = 0; i < 3; i++)
        {
            var r = s.Read();
            Assert.False(r.Corrupt);
            Assert.Null(r.Tcas);
            Assert.Equal(Bbs757EhsiReader.UnexpectedValues, r.TcasMissing);
            Assert.Equal(new[] { "TCAS: not read: unexpected values" }, NotRead(r));
            Assert.NotEmpty(r.Route!);
            Assert.NotNull(r.Active);
            Assert.NotNull(r.Approach);
            Assert.NotNull(r.MapExtras);
            Assert.NotNull(r.MapGuides);
            Assert.NotNull(r.MapOptions);
            Assert.StartsWith("KOLM, ", Bbs757EhsiText.WaypointLine(r), StringComparison.Ordinal);
        }
        // Logged once for the episode.
        Assert.Single(s.Log);
        Assert.Contains("Tcas", s.Log[0], StringComparison.Ordinal);

        // The first good read brings it back; a new tear starts the count again.
        s.Restore(Traffic(torn: false));
        var good = s.Read();
        Assert.False(good.Corrupt);
        Assert.NotNull(good.Tcas);
        Assert.Null(good.TcasMissing);
        Assert.Empty(NotRead(good));
        s.Restore(Traffic(torn: true));
        Assert.True(s.Read().Corrupt);
    }

    [Fact]
    public void Each_part_counts_its_own_tears()
    {
        // TCAS tears three times; the FIX page then tears once beside it: TCAS is at its limit, the FIX page is not, so the
        // reading is corrupt; once the FIX page reads again only TCAS is not read.
        var guides = new Bbs757EhsiMapGuidesTests.Guides { Fix1 = Bbs757EhsiMapGuidesTests.Tcm with { Entries = new[] { (361, -1) } } };
        Scene Both(bool fixTorn) => new()
        {
            Range = 20,
            Tweak = b =>
            {
                Traffic(torn: true).Tweak!(b);
                (fixTorn ? guides : new Bbs757EhsiMapGuidesTests.Guides { Fix1 = Bbs757EhsiMapGuidesTests.Tcm }).Write(b);
            },
        };
        using var s = new Session(Both(fixTorn: false));
        for (int i = 1; i < Bbs757EhsiReader.TearLimit; i++) Assert.True(s.Read().Corrupt);
        s.Restore(Both(fixTorn: true));
        Assert.True(s.Read().Corrupt);
        s.Restore(Both(fixTorn: false));
        var r = s.Read();
        Assert.False(r.Corrupt);
        Assert.Equal(new[] { "TCAS: not read: unexpected values" }, NotRead(r));
        Assert.NotNull(r.MapGuides);
    }

    [Fact]
    public void A_torn_capability_set_on_a_route_leg_counts_against_the_map_extras()
    {
        // A navaid leg whose set says three but holds two: corrupt below the limit; at it, the map extras alone are not read
        // and the legs fall back to plan 26's position rule (the kind's own offsets), as with the part missing.
        var torn = Bbs757EhsiMapExtrasTests.NavaidLeg(new[] { 9, 19 },
            tweak: b => I32(b, Legs + P.MapExtras!.LegCapabilityCount, 3));
        using var s = new Session(torn);
        for (int i = 1; i < Bbs757EhsiReader.TearLimit; i++) Assert.True(s.Read().Corrupt);
        var r = s.Read();
        Assert.False(r.Corrupt);
        Assert.Null(r.MapExtras);
        Assert.Equal(Bbs757EhsiReader.UnexpectedValues, r.MapExtrasMissing);
        Assert.Equal(new[] { "TERR, wind and runways: not read: unexpected values" }, NotRead(r));
        Assert.Equal(Bbs757EhsiMapExtrasTests.Primary, (r.Route![0].Latitude, r.Route[0].Longitude));
        Assert.Single(s.Log);

        // The set reading again brings the rule back: the second position, as BlueBird's rule picks for {9, 19}.
        s.Restore(Bbs757EhsiMapExtrasTests.NavaidLeg(new[] { 9, 19 }));
        var good = s.Read();
        Assert.NotNull(good.MapExtras);
        Assert.Equal(Bbs757EhsiMapExtrasTests.Second, (good.Route![0].Latitude, good.Route[0].Longitude));
    }

    [Fact]
    public void The_route_walk_still_tears_every_time()
    {
        // Plan 26's own walks are unchanged: a torn route is corrupt however long it lasts.
        using var s = new Session(new Scene { Tweak = b => I32(b, Route + P.RouteLegs, Outside) });
        for (int i = 0; i < 2 * Bbs757EhsiReader.TearLimit; i++) Assert.True(s.Read().Corrupt);
    }

    public static TheoryData<string, Func<Bbs757EhsiLayout, Bbs757EhsiLayout>, string> BadLayoutValues => new()
    {
        { "TCAS", l => l with { Tcas = l.Tcas! with { TrafficStride = 0 } }, nameof(Bbs757EhsiTcas.TrafficStride) },
        { "TCAS", l => l with { Tcas = l.Tcas! with { MaxTraffic = -1 } }, nameof(Bbs757EhsiTcas.MaxTraffic) },
        { "TERR, wind and runways", l => l with { MapExtras = l.MapExtras! with { TerrainDivisor = 0 } }, nameof(Bbs757EhsiMapExtras.TerrainDivisor) },
        { "TERR, wind and runways", l => l with { MapExtras = l.MapExtras! with { DecelerationStride = 0 } }, nameof(Bbs757EhsiMapExtras.DecelerationStride) },
        { "TERR, wind and runways", l => l with { MapExtras = l.MapExtras! with { MaxCapabilities = -1 } }, nameof(Bbs757EhsiMapExtras.MaxCapabilities) },
        { "FIX, descent and altitude guides", l => l with { MapGuides = l.MapGuides! with { FixEntryStride = 0 } }, nameof(Bbs757EhsiMapGuides.FixEntryStride) },
        { "FIX, descent and altitude guides", l => l with { MapGuides = l.MapGuides! with { RadialStart = double.NaN } }, nameof(Bbs757EhsiMapGuides.RadialStart) },
        { "FIX, descent and altitude guides", l => l with { MapGuides = l.MapGuides! with { RadialStart = -1e6 } }, nameof(Bbs757EhsiMapGuides.RadialStart) },
        { "FIX, descent and altitude guides", l => l with { MapGuides = l.MapGuides! with { RadialDashStep = double.PositiveInfinity } }, nameof(Bbs757EhsiMapGuides.RadialDashStep) },
        { "FIX, descent and altitude guides", l => l with { MapGuides = l.MapGuides! with { RadialDashStep = 1e9 } }, nameof(Bbs757EhsiMapGuides.RadialDashStep) },
        { "FIX, descent and altitude guides", l => l with { MapGuides = l.MapGuides! with { RadialDashLength = 1e15 } }, nameof(Bbs757EhsiMapGuides.RadialDashLength) },
        { "FIX, descent and altitude guides", l => l with { MapGuides = l.MapGuides! with { RadialDashLength = double.NaN } }, nameof(Bbs757EhsiMapGuides.RadialDashLength) },
        { "FIX, descent and altitude guides", l => l with { MapGuides = l.MapGuides! with { RadialDashes = Bbs757EhsiRadialDashes.MaxCount + 1 } }, nameof(Bbs757EhsiMapGuides.RadialDashes) },
        { "VOR and ILS", l => l with { Approach = l.Approach! with { StationIdentLength = 0 } }, nameof(Bbs757EhsiApproach.StationIdentLength) },
        { "Map options", l => l with { MapOptions = l.MapOptions! with { NavaidIdentLength = 0 } }, nameof(Bbs757EhsiMapOptions.NavaidIdentLength) },
        { "Map options", l => l with { MapOptions = l.MapOptions! with { AirportStride = 0 } }, nameof(Bbs757EhsiMapOptions.AirportStride) },
        { "Map options", l => l with { MapOptions = l.MapOptions! with { MaxItems = -1 } }, nameof(Bbs757EhsiMapOptions.MaxItems) },
    };

    [Theory]
    [MemberData(nameof(BadLayoutValues))]
    public void A_bad_layout_value_makes_its_part_missing_at_once(string part, Func<Bbs757EhsiLayout, Bbs757EhsiLayout> bad, string field)
    {
        // Never torn on a read: the part is missing from the first reading, its reason naming the field; nothing else goes.
        using var s = new Session(new Scene { Range = 20 }, bad(P));
        var r = s.Read();
        Assert.False(r.Corrupt);
        var row = Assert.Single(NotRead(r));
        Assert.StartsWith(part + ": not read: ", row, StringComparison.Ordinal);
        Assert.Contains(field, row, StringComparison.Ordinal);
        Assert.NotEmpty(r.Route!);
    }
}
