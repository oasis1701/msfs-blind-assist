using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Plan 28's five optional parts of the EHSI layout: each round-trips byte-identical under any culture
/// (WASM-21), a missing one keeps its reason, a part is null exactly when its reason is not, and a file written before
/// the parts existed still loads with every part missing, every other member still required (WASM-26).</summary>
public partial class Bbs757EhsiLayoutTests
{
    private static LayoutFile FileWith(Bbs757EhsiLayout ehsi) => FileWithAll() with
    {
        Readers = FileWithAll().Readers.Select(r => r.Reader == Bbs757Module.Ehsi ? r with { Layout = ehsi } : r).ToArray(),
    };

    private static Bbs757EhsiLayout ReadEhsi(string text)
    {
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        return (Bbs757EhsiLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Ehsi).Layout!;
    }

    /// <summary>The names of the five optional parts and their reasons as the file holds them.</summary>
    private static readonly string[] PartMembers =
    {
        "approach", "approachMissing", "tcas", "tcasMissing", "mapOptions", "mapOptionsMissing",
        "mapExtras", "mapExtrasMissing", "mapGuides", "mapGuidesMissing",
    };

    [Fact]
    public void Each_part_round_trips_byte_identical()
    {
        // Every field of every part distinct and not its type's default (fractions in the doubles, leading spaces in
        // the words), and the build's own values with the screen's words.
        var counter = new int[1];
        var distinct = Sample() with
        {
            Approach = Distinct<Bbs757EhsiApproach>(counter),
            ApproachMissing = null,
            Tcas = Distinct<Bbs757EhsiTcas>(counter),
            TcasMissing = null,
            MapOptions = Distinct<Bbs757EhsiMapOptions>(counter),
            MapOptionsMissing = null,
            MapExtras = Distinct<Bbs757EhsiMapExtras>(counter),
            MapExtrasMissing = null,
            MapGuides = Distinct<Bbs757EhsiMapGuides>(counter),
            MapGuidesMissing = null,
        };
        foreach (var layout in new[] { distinct, SampleWithParts() })
        {
            string text = LayoutFileFormat.Write(Module, FileWith(layout));
            var loaded = ReadEhsi(text);
            Assert.Equal(layout, loaded);
            Assert.Equal(layout.GetHashCode(), loaded.GetHashCode());
            Assert.Equal(layout.Approach, loaded.Approach);
            Assert.Equal(layout.Tcas, loaded.Tcas);
            Assert.Equal(layout.MapOptions, loaded.MapOptions);
            Assert.Equal(layout.MapExtras, loaded.MapExtras);
            Assert.Equal(layout.MapGuides, loaded.MapGuides);
            Assert.Null(loaded.ApproachMissing);
            Assert.Null(loaded.TcasMissing);
            Assert.Null(loaded.MapOptionsMissing);
            Assert.Null(loaded.MapExtrasMissing);
            Assert.Null(loaded.MapGuidesMissing);
            Assert.Equal(text, LayoutFileFormat.Write(Module, FileWith(loaded)));
            foreach (string culture in new[] { "de-DE", "sv-SE" })
            {
                Assert.Equal(text, TestCulture.Run(culture, () => LayoutFileFormat.Write(Module, FileWith(layout))));
                Assert.Equal(layout, TestCulture.Run(culture, () => ReadEhsi(text)));
            }
        }

        var sample = ReadEhsi(LayoutFileFormat.Write(Module, FileWith(SampleWithParts())));
        Assert.Equal("  ---", sample.Approach!.NoDmeWord);
        Assert.Equal("TEST", sample.Tcas!.TestWord);
        Assert.Equal(2.2, sample.Approach.GlideslopePin);
        Assert.Equal(0.1, sample.MapGuides!.ArcMinVerticalSpeed);
        Assert.Equal(new[] { 0, 1, 2, 3, 6 }, sample.MapOptions!.DrawnKinds);
        Assert.Equal(new[] { 0, 1, 2, 5 }, sample.MapOptions.CourseLineKinds);
        Assert.Equal((0, 1, 2, 3, 6), (sample.MapOptions.KindVor, sample.MapOptions.KindVorDme, sample.MapOptions.KindVortac,
            sample.MapOptions.KindTacan, sample.MapOptions.KindDme));
        Assert.Equal(new[] { 0, 1, 2, 5 }, sample.Approach.VorKinds);
        Assert.Equal((4, 4), (sample.MapOptions.NavaidIdentLength, sample.MapOptions.AirportLabelLength));
        Assert.Equal((4, 16, 372, 780, 776, 24, 32), (sample.Approach.StationIdentLength, sample.Approach.StationFrequency,
            sample.Approach.PanelIlsStation, sample.Approach.PanelManualWhole, sample.Approach.PanelManualFraction,
            sample.Approach.AdfLongitude, sample.Approach.AdfLatitude));
        Assert.Equal(7, sample.MapExtras!.CapabilityChoices.Count);
        Assert.Equal(1448, sample.MapGuides.OffpathLatitude);

        // Each part and each reason is part of the layout's equality.
        Assert.NotEqual(SampleWithParts(), SampleWithParts() with { Approach = SampleWithParts().Approach! with { IlsCourse = 233 } });
        Assert.NotEqual(SampleWithParts(), SampleWithParts() with { Tcas = SampleWithParts().Tcas! with { TestWord = "TEST " } });
        Assert.NotEqual(SampleWithParts(), SampleWithParts() with { MapOptions = SampleWithParts().MapOptions! with { DrawnKinds = new[] { 0, 1, 2, 3 } } });
        Assert.NotEqual(SampleWithParts(), SampleWithParts() with { MapOptions = SampleWithParts().MapOptions! with { CourseLineKinds = new[] { 0, 1, 2 } } });
        Assert.NotEqual(SampleWithParts(), SampleWithParts() with { Approach = SampleWithParts().Approach! with { VorKinds = new[] { 0, 1, 2 } } });
        Assert.NotEqual(SampleWithParts(), SampleWithParts() with { MapExtras = SampleWithParts().MapExtras! with { CapabilityChoices = new[] { new Bbs757EhsiCapabilityChoice(0, 832, 840, false) } } });
        Assert.NotEqual(SampleWithParts(), SampleWithParts() with { MapGuides = SampleWithParts().MapGuides! with { DeviationClamp = 400.5 } });
        Assert.NotEqual(Sample(), Sample() with { TcasMissing = "another reason" });
        Assert.Equal(SampleWithParts(), SampleWithParts() with
        {
            Approach = SampleWithParts().Approach! with { VorKinds = new[] { 0, 1, 2, 5 } },
            MapOptions = SampleWithParts().MapOptions! with { DrawnKinds = new[] { 0, 1, 2, 3, 6 }, CourseLineKinds = new[] { 0, 1, 2, 5 } },
            MapExtras = SampleWithParts().MapExtras! with { CapabilityChoices = SampleWithParts().MapExtras!.CapabilityChoices.ToArray() },
        });

        // The dump names every part, or why it is missing.
        var lines = Bbs757LayoutDescriptions.DescribeEhsi(sample);
        Assert.Contains(lines, l => l.Contains("EHSI VOR and ILS:", StringComparison.Ordinal) && l.Contains("glideslopePin 2.2", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("EHSI TCAS:", StringComparison.Ordinal) && l.Contains("trafficStride 40", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("EHSI map options:", StringComparison.Ordinal) && l.Contains("drawnKinds [0, 1, 2, 3, 6]", StringComparison.Ordinal)
            && l.Contains("courseLineKinds [0, 1, 2, 5], kinds vor 0 vorDme 1 vortac 2 tacan 3 dme 6", StringComparison.Ordinal)
            && l.Contains("(ident 0 length 4, frequency 16", StringComparison.Ordinal)
            && l.Contains("(name 0 label 4, longitude 16", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("EHSI VOR and ILS:", StringComparison.Ordinal) && l.Contains("vorKinds [0, 1, 2, 5]", StringComparison.Ordinal)
            && l.Contains("panelStation 368 ils 372 manual 780/776", StringComparison.Ordinal)
            && l.Contains("station ident 0 length 4 frequency 16", StringComparison.Ordinal)
            && l.Contains("adf 0/0 longitude 24 latitude 32", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("EHSI TERR, wind and runways:", StringComparison.Ordinal) && l.Contains("terrainStatic 0x11df30", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("EHSI FIX, descent and altitude guides:", StringComparison.Ordinal) && l.Contains("fixEntryStride 8", StringComparison.Ordinal));
        Assert.Equal(TestCulture.Run("de-DE", () => Bbs757LayoutDescriptions.DescribeEhsi(sample)), lines);
    }

    [Fact]
    public void A_missing_part_keeps_its_reason()
    {
        var layout = SampleWithParts() with { Tcas = null, TcasMissing = "tcas: the traffic vector's stride is not 40", MapGuides = null, MapGuidesMissing = "fix: no fms" };
        var loaded = ReadEhsi(LayoutFileFormat.Write(Module, FileWith(layout)));
        Assert.Equal(layout, loaded);
        Assert.Null(loaded.Tcas);
        Assert.Equal("tcas: the traffic vector's stride is not 40", loaded.TcasMissing);
        Assert.Null(loaded.MapGuides);
        Assert.Equal("fix: no fms", loaded.MapGuidesMissing);
        Assert.NotNull(loaded.Approach);
        Assert.Null(loaded.ApproachMissing);
        Assert.Contains(Bbs757LayoutDescriptions.DescribeEhsi(loaded),
            l => l.EndsWith("EHSI: TCAS missing: tcas: the traffic vector's stride is not 40", StringComparison.Ordinal));

        // A part is null exactly when its reason is not: a layout holding both, or neither, is never written ...
        Assert.ThrowsAny<JsonException>(() => LayoutFileFormat.Write(Module, FileWith(SampleWithParts() with { TcasMissing = "both" })));
        Assert.ThrowsAny<JsonException>(() => LayoutFileFormat.Write(Module, FileWith(Sample() with { MapExtrasMissing = null })));

        // ... and a file holding both, or neither, is refused.
        string text = LayoutFileFormat.Write(Module, FileWith(SampleWithParts()));
        var both = JsonNode.Parse(text)!;
        both["readers"]![Bbs757Module.Ehsi]!["layout"]!["approachMissing"] = "both";
        Assert.False(LayoutFileFormat.TryRead(Module, both.ToJsonString(), out _, out string? failure));
        Assert.Contains("approach", failure, StringComparison.Ordinal);
        var neither = JsonNode.Parse(LayoutFileFormat.Write(Module, FileWith(Sample())))!;
        neither["readers"]![Bbs757Module.Ehsi]!["layout"]!["mapOptionsMissing"] = null;
        Assert.False(LayoutFileFormat.TryRead(Module, neither.ToJsonString(), out _, out string? failure2));
        Assert.Contains("mapOptions", failure2, StringComparison.Ordinal);

        // A present part still needs every one of its own members.
        var lacking = JsonNode.Parse(text)!;
        Assert.True(lacking["readers"]![Bbs757Module.Ehsi]!["layout"]!["tcas"]!.AsObject().Remove("trafficStride"));
        Assert.False(LayoutFileFormat.TryRead(Module, lacking.ToJsonString(), out _, out _));
    }

    [Fact]
    public void A_file_without_the_parts_reads_as_missing()
    {
        // A file written before the parts existed has none of their members: it loads, every plan 23-26 field as
        // written, each part null with the reason "not in this layout file".
        string text = LayoutFileFormat.Write(Module, FileWithAll());
        var root = JsonNode.Parse(text)!;
        var layoutNode = root["readers"]![Bbs757Module.Ehsi]!["layout"]!.AsObject();
        foreach (string member in PartMembers) Assert.True(layoutNode.Remove(member), member);
        string old = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        Assert.True(LayoutFileFormat.TryRead(Module, old, out var file, out string? failure), failure);
        Assert.Equal(6, file!.Readers.Count(r => r.Layout is not null));
        var loaded = (Bbs757EhsiLayout)file.Readers.Single(r => r.Reader == Bbs757Module.Ehsi).Layout!;
        Assert.Equal(Sample(), loaded);
        Assert.Null(loaded.Approach);
        Assert.Null(loaded.Tcas);
        Assert.Null(loaded.MapOptions);
        Assert.Null(loaded.MapExtras);
        Assert.Null(loaded.MapGuides);
        Assert.Equal(Bbs757EhsiLayout.NotInFile, loaded.ApproachMissing);
        Assert.Equal(Bbs757EhsiLayout.NotInFile, loaded.TcasMissing);
        Assert.Equal(Bbs757EhsiLayout.NotInFile, loaded.MapOptionsMissing);
        Assert.Equal(Bbs757EhsiLayout.NotInFile, loaded.MapExtrasMissing);
        Assert.Equal(Bbs757EhsiLayout.NotInFile, loaded.MapGuidesMissing);
        Assert.Equal("not in this layout file", Bbs757EhsiLayout.NotInFile);
        Assert.Contains(Bbs757LayoutDescriptions.DescribeEhsi(loaded),
            l => l.EndsWith("EHSI: VOR and ILS missing: not in this layout file", StringComparison.Ordinal));

        // A layout record built without the parts holds the same: written, it saves the parts as null with that reason.
        Assert.Equal(Bbs757EhsiLayout.NotInFile, Sample().MapExtrasMissing);
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));

        // Only those members are optional: the rest of the layout is still refused when one is missing.
        var root2 = JsonNode.Parse(old)!;
        Assert.True(root2["readers"]![Bbs757Module.Ehsi]!["layout"]!.AsObject().Remove("legStride"));
        Assert.False(LayoutFileFormat.TryRead(Module, root2.ToJsonString(), out _, out _));
    }

    [Fact]
    public void Bbs757Ehsi_parts_equality_covers_every_field()
    {
        var counter = new int[1];
        AssertEveryFieldCounts(Distinct<Bbs757EhsiApproach>(counter));
        AssertEveryFieldCounts(Distinct<Bbs757EhsiTcas>(counter));
        AssertEveryFieldCounts(Distinct<Bbs757EhsiMapOptions>(counter));
        AssertEveryFieldCounts(Distinct<Bbs757EhsiMapExtras>(counter));
        AssertEveryFieldCounts(Distinct<Bbs757EhsiMapGuides>(counter));
    }

    private static void AssertEveryFieldCounts<T>(T sample) where T : notnull
    {
        var ctor = typeof(T).GetConstructors().Single();
        var parameters = ctor.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            object?[] args = parameters.Select(p => typeof(T).GetProperty(p.Name!)!.GetValue(sample)).ToArray();
            args[i] = args[i] switch
            {
                uint u => u + 1,
                int n => n + 1,
                double d => d + 1.5,
                bool b => !b,
                string s => s + "x",
                IReadOnlyList<int> a => a.Select(x => x + 1).ToArray(),
                IReadOnlyList<Bbs757EhsiCapabilityChoice> c => c.Select(x => x with { Capability = x.Capability + 1 }).ToArray(),
                _ => throw new InvalidOperationException($"{typeof(T).Name}.{parameters[i].Name}: no mutation for {args[i]?.GetType()}"),
            };
            var changed = (T)ctor.Invoke(args);
            Assert.False(sample.Equals(changed), $"{typeof(T).Name}.Equals ignores {parameters[i].Name}");
        }
        object?[] same = parameters.Select(p => typeof(T).GetProperty(p.Name!)!.GetValue(sample) switch
        {
            IReadOnlyList<int> a => a.ToArray(),
            IReadOnlyList<Bbs757EhsiCapabilityChoice> c => c.ToArray(),
            var v => v,
        }).ToArray();
        Assert.Equal(sample, (T)ctor.Invoke(same));
        // Equal parts hash alike (the contract); unequal ones may collide, so their hashes are not compared.
        Assert.Equal(sample.GetHashCode(), ((T)ctor.Invoke(same)).GetHashCode());
    }

    /// <summary>A part with every constructor argument distinct and not its type's default: whole numbers from the
    /// counter, doubles with a fraction, words with leading spaces, lists of two.</summary>
    private static T Distinct<T>(int[] counter)
    {
        object Value(Type type)
        {
            int n = ++counter[0];
            if (type == typeof(int)) return 1000 + n;
            if (type == typeof(uint)) return (uint)(0x100000 + n);
            if (type == typeof(double)) return n + 0.375;
            if (type == typeof(bool)) return true;
            if (type == typeof(string)) return $"  w{n}";
            if (type == typeof(IReadOnlyList<int>)) return new[] { n, n + 500 };
            if (type == typeof(IReadOnlyList<Bbs757EhsiCapabilityChoice>))
                return new[] { Distinct<Bbs757EhsiCapabilityChoice>(counter), Distinct<Bbs757EhsiCapabilityChoice>(counter) };
            throw new InvalidOperationException($"{typeof(T).Name}: no value for {type}");
        }
        var ctor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        return (T)ctor.Invoke(ctor.GetParameters().Select(p => Value(p.ParameterType)).ToArray());
    }

    /// <summary><see cref="Sample"/> with the five parts at this build's values, as plan 28's measurements recorded them
    /// (the words are the screen's own; TEST trimmed of its leading spaces).</summary>
    internal static Bbs757EhsiLayout SampleWithParts() => Sample() with
    {
        Approach = new Bbs757EhsiApproach(
            VorCourse: 224, VorDeviation: 248, VorDeviationValid: 265, ToFrom: 268, ToValue: 1, FromValue: -1,
            VorDme: 256, VorDmeValid: 264,
            IlsCourse: 232, LocalizerDeviation: 240, LocalizerBar: 272, IlsDots: 273, IlsTest: 274,
            IlsFrequency: 280, NoIlsFrequency: -1, LocFlag: 288, GlideslopeFlag: 289,
            DisplaySide: 88, CaptainSide: 6,
            NdToPfd: 872, PfdToIls: 136, GlideslopeDeviation: 16, GlideslopeScale: 33, GlideslopePointer: 35, GlideslopeTest: 39,
            NdToDme: 856, DmePowered: 4, DmeFailed: 5, DmeStation: 352, DmeDistance: 360, StationKind: 32, IlsStationKind: 5,
            VorKinds: new[] { 0, 1, 2, 5 },
            SourceToIlsReceiver: 736, ReceiverToSignal: 364, SignalBlock: 448, LocalizerReceived: 80,
            RmiLeftHandle: 1112416, RmiRightHandle: 1112488, HandleValue: 16, RmiVor: 0, RmiAdf: 1,
            NdToVorLeft: 860, NdToVorRight: 864, PanelStation: 368, PanelIlsStation: 372,
            PanelManualFraction: 776, PanelManualWhole: 780,
            StationIdent: 0, StationIdentLength: 4, StationFrequency: 16, StationLongitude: 40, StationLatitude: 48,
            NdToAdf: 868, AdfPositions: false, AdfLeft: 0, AdfRight: 0, AdfLongitude: 24, AdfLatitude: 32,
            VorLeftWord: "VOR L", VorRightWord: "VOR R", IlsWord: "ILS", ToWord: "TO", FromWord: "FROM", DmeWord: "DME",
            NoFrequencyWord: "------", NoDmeWord: "  ---", LocWord: "LOC", GlideslopeWord: "G/S",
            DeviationFullScale: 1.0, DotsPerUnit: 2.0, GlideslopePin: 2.2),
        ApproachMissing = null,
        Tcas = new Bbs757EhsiTcas(
            NdToTcas: 848, TcasPowered: 4, TcasFailed: 5, TcasLevel: 800, TrafficAdvisory: 889, ResolutionAdvisory: 890,
            TcasTest: 891, TrafficSwitch: 304, MessagesAllowed: 1273,
            TrafficList: 736, TrafficStride: 40, MaxTraffic: 30,
            TrafficKind: 0, TrafficTrend: 4, TrafficAltitude: 8, TrafficLongitude: 24, TrafficLatitude: 32,
            KindOther: 0, KindProximate: 1, KindAdvisory: 2, KindResolution: 3, TrendUp: 1, TrendDown: 2,
            TaOnlyLevel: 1, TaRaLevel: 2, TagDivisor: 100,
            TcasWord: "TCAS", FailWord: "FAIL", OffWord: "OFF", TfcWord: "TFC", TaOnlyWord: "TA ONLY", TestWord: "TEST",
            TrafficWord: "TRAFFIC"),
        TcasMissing = null,
        MapOptions = new Bbs757EhsiMapOptions(
            NavAidSwitch: 208, AirportSwitch: 210, WaypointSwitch: 211, TunedLeft: 212, TunedRight: 216,
            NdToSuper: 1216, SuperToNavdb: 584, NdToFms: 908,
            Navaids: 80, NavaidIdent: 0, NavaidIdentLength: 4, NavaidFrequency: 16, NavaidKind: 32, NavaidClass: 33, NavaidLongitude: 40, NavaidLatitude: 48,
            Airports: 9072, AirportStride: 48, AirportName: 0, AirportLabelLength: 4, AirportRecordLongitude: 16, AirportRecordLatitude: 24,
            Waypoints: 344, WaypointLongitude: 0, WaypointLatitude: 8, WaypointIdent: 16,
            Ndbs: 256, NdbIdent: 0, NdbLongitude: 24, NdbLatitude: 32,
            FmsToVorLeft: 7884, FmsToVorRight: 7888, PanelAutotune: 740, PanelStation: 368, PanelCourse: 784,
            PanelManualFraction: 776, PanelManualWhole: 780,
            DrawnKinds: new[] { 0, 1, 2, 3, 6 }, CourseLineKinds: new[] { 0, 1, 2, 5 },
            KindVor: 0, KindVorDme: 1, KindVortac: 2, KindTacan: 3, KindDme: 6, HighClass: 2, HighClassAbove: 40, NdbsAbove: 40, NdbDistance: 80,
            RoundLimit: 500, MaxItems: 4096),
        MapOptionsMissing = null,
        MapExtras = new Bbs757EhsiMapExtras(
            TerrHandle: 376, HandleValue: 16, TerrainPicture: 778, DisplaySide: 88, CaptainSide: 6,
            TerrainStatic: 1171248, TerrainRecords: 272, TerrainSideStride: 6, TerrainHighest: 0, TerrainLowest: 2,
            TerrainDivisor: 100, SourceToAdc: 716, AdcAltitude: 416, AdcVerticalSpeed: 424,
            RedAbove: 2000, AmberAbove: -500, AmberAboveGearDown: -250, GearDown: 1,
            LookAheadBelow: -1000, LookAheadSeconds: 30, LookAheadFloor: -100,
            WindShown: 1008842,
            DepartureRunway: 968, ArrivalRunway: 1032, RunwayFirstLongitude: 0, RunwayFirstLatitude: 8,
            RunwaySecondLongitude: 16, RunwaySecondLatitude: 24, RunwayName: 32,
            DecelerationStart: 4276, DecelerationEnd: 4288, DecelerationStride: 16, DecelerationLongitude: 0,
            DecelerationLatitude: 8, MaxDecelerationPoints: 8,
            LegCapabilityFirst: 792, LegCapabilityCount: 796, CapabilityNodeNext: 0, CapabilityNodeValue: 8, MaxCapabilities: 64,
            CapabilityLegKind: 2,
            CapabilityChoices: new[]
            {
                new Bbs757EhsiCapabilityChoice(0, 832, 840, false),
                new Bbs757EhsiCapabilityChoice(1, 832, 840, false),
                new Bbs757EhsiCapabilityChoice(5, 880, 888, false),
                new Bbs757EhsiCapabilityChoice(19, 880, 888, true),
                new Bbs757EhsiCapabilityChoice(13, 880, 888, true),
                new Bbs757EhsiCapabilityChoice(2, 880, 888, false),
                new Bbs757EhsiCapabilityChoice(3, 832, 840, false),
            },
            TerrWord: "TERR", NoWindWord: "---"),
        MapExtrasMissing = null,
        MapGuides = new Bbs757EhsiMapGuides(
            NdToFms: 908, FmsFix1On: 7224, FmsFix2On: 7225, FmsFix1: 5560, FmsFix2: 5564,
            FmsFix1Entries: 7280, FmsFix2Entries: 7292, FixEntryStride: 8, FixEntryRadial: 0, FixEntryDistance: 4,
            FixEntryNone: -1, FixEntries: 3, LegName: 0, LegNavaidName: 816, NavaidLegKinds: new[] { 1, 2 },
            RadialStart: 27, RadialDashes: 18, RadialDashStep: 46, RadialDashLength: 30,
            OffpathOn: 1420, OffpathValidA: 1424, OffpathValidB: 1425, OffpathLatitude: 1448, OffpathLongitude: 1456,
            OffpathCleanRadius: 1488, OffpathSpeedbrakeRadius: 1496,
            McpAltitude: 856, SourceToAdc: 716, AdcIndicatedAltitude: 400, AdcVerticalSpeed: 424,
            NdToAirGround: 900, AirGroundOnGround: 476, IrsTrueTrack: 3920,
            ArcMinAltitudeDifference: 50, ArcMinVerticalSpeed: 0.1,
            PathPhase: 3916, DescentPhase: 4, PathDeviation: 3936, DeviationClamp: 400),
        MapGuidesMissing = null,
    };
}
