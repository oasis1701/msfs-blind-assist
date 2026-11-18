using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The EHSI layout record round-trips through the generic layout file format, byte-identical under any
/// culture (WASM-21), a file lacking its reader or one of its members is refused (WASM-26), and its equality covers
/// every field but the source file. Plan 28's optional parts: Bbs757EhsiLayoutPartsTests.cs.</summary>
public partial class Bbs757EhsiLayoutTests
{
    private static readonly ModuleDescription Module = Bbs757Module.Description;

    /// <summary>This build's values, as the plan's measurements recorded them (the words are the screen's own).</summary>
    internal static Bbs757EhsiLayout Sample() => new(
        SourceFile: "",
        AnchorAddress: 0x636930,
        Anchor: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        ManagerBase: 0xf1334,
        ManagerSpan: 0x100,
        NdVptr: 79876,
        Powered: 4,
        Failed: 5,
        Blanked: 64,
        NdToEfis: 888,
        NdToSource: 892,
        NdToMcp: 844,
        NdToRoute: 1244,
        SourceToIrs: 712,
        EfisMode: 344,
        EfisCentered: 348,
        Modes: new Bbs757WordTable(new Dictionary<int, string> { [0] = "VOR", [1] = "ILS", [2] = "MAP", [3] = "PLAN" }),
        Range: 704,
        RangeSelected: 296,
        RangeChanging: 804,
        Valid: 805,
        Aligning: 1272,
        Number: 720,
        TrackUp: 756,
        TrueReference: 760,
        Width: 40,
        Height: 44,
        Longitude: 1160,
        Latitude: 1168,
        CenterLatitude: 1176,
        CenterLongitude: 1184,
        Rotation: 1208,
        ScreenX: 1192,
        ScreenY: 1200,
        DataSwitch: 209,
        ExcessCount: 1276,
        ExcessLimit: 100,
        OriginAirport: 1096,
        DestinationAirport: 1128,
        AirportLongitude: 16,
        AirportLatitude: 24,
        McpHeading: 848,
        IrsGroundSpeed: 3952,
        Clock: 1197328,
        RadiusFactor: 0.6775,
        AircraftYFactor: 0.8175,
        ClipInset: 66,
        MarkerMargin: 10,
        MinEtaGroundSpeed: 100,
        RouteLegs: 668,
        RouteFlownLegs: 680,
        LegStride: 1632,
        MaxLegs: 200,
        RouteActive: 1235,
        RouteModified: 1620,
        RouteModification: 1624,
        RouteId: 1700,
        LegLabel: 1608,
        LegKind: 72,
        DiscontinuityKind: 3,
        LegPositions: new[]
        {
            new Bbs757EhsiLegPosition(1, 832, 840),
            new Bbs757EhsiLegPosition(2, 832, 840),
            new Bbs757EhsiLegPosition(5, 1040, 1048),
            new Bbs757EhsiLegPosition(6, 1040, 1048),
        },
        LegLatitude: 80,
        LegLongitude: 88,
        LegTerminator: 24,
        NoSymbolTerminators: new[] { 0, 9, 19 },
        ManualTerminators: new[] { 9, 19 },
        LegConstraintKind: 372,
        LegConstraintFirst: 376,
        LegConstraintSecond: 380,
        LegEta: 1480,
        PathRegistry: 1197208,
        PathRegistryFirst: 8,
        PathNodeKey: 8,
        PathNodeValue: 12,
        TopOfClimbFlag: 4000,
        TopOfClimbLatitude: 3856,
        TopOfClimbLongitude: 3864,
        TopOfDescentFlagA: 4048,
        TopOfDescentFlagB: 4049,
        TopOfDescentLatitude: 4040,
        TopOfDescentLongitude: 4032,
        EndOfDescentLeg: 3924,
        HdgWord: "HDG",
        TrkWord: "TRK",
        MagWord: "M",
        TrueWord: "TRU",
        MapWord: "MAP",
        NmWord: "NM",
        EtaSuffix: "z",
        TopOfClimbWord: "T/C",
        TopOfDescentWord: "T/D",
        EndOfDescentWord: "E/D",
        ExcessWord: "EXCESS DATA");

    private static LayoutFile FileWithAll() => new(
        GeneratorVersion: 1,
        CodeHash: new string('0', 64),
        DataRanges: new[] { new DataRange(0x400, 64) },
        DataHash: new string('1', 64),
        Searches: Array.Empty<DataSearch>(),
        Readers: new[]
        {
            new ReaderResult(Bbs757Module.Fma, Bbs757LayoutFileTests.SampleFma(), null),
            new ReaderResult(Bbs757Module.Efb, Bbs757LayoutFileTests.SampleEfb(), null),
            new ReaderResult(Bbs757Module.LowerEicas, Bbs757LowerEicasLayoutTests.Sample(), null),
            new ReaderResult(Bbs757Module.UpperEicas, Bbs757UpperEicasLayoutTests.Sample(), null),
            new ReaderResult(Bbs757Module.Ehsi, Sample(), null),
            new ReaderResult(Bbs757Module.Adi, Bbs757AdiLayoutTests.Sample(), null),
        });

    [Fact]
    public void The_ehsi_layout_round_trips_byte_identical()
    {
        var expected = Sample() with { SourceFile = "" };
        string text = LayoutFileFormat.Write(Module, FileWithAll());
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757EhsiLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Ehsi).Layout!;
        Assert.Equal(expected, loaded);
        Assert.Equal(expected.GetHashCode(), loaded.GetHashCode());
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            Assert.Equal(text, TestCulture.Run(culture, () => LayoutFileFormat.Write(Module, FileWithAll())));
            Assert.True(TestCulture.Run(culture, () =>
                LayoutFileFormat.TryRead(Module, text, out var f2, out _)
                && ((Bbs757EhsiLayout)f2!.Readers.Single(r => r.Reader == Bbs757Module.Ehsi).Layout!).Equals(expected)),
                culture);
        }
        Assert.Equal(0.6775, loaded.RadiusFactor);
        Assert.Equal(0.8175, loaded.AircraftYFactor);
        Assert.Equal(1632, loaded.LegStride);
        Assert.Equal("MAP", loaded.Modes.Lookup(2));
        Assert.Equal(4, loaded.LegPositions.Count);
        Assert.Equal(new Bbs757EhsiLegPosition(5, 1040, 1048), loaded.LegPositions[2]);
        Assert.Equal(new[] { 9, 19 }, loaded.ManualTerminators);
        Assert.Contains(Bbs757LayoutDescriptions.DescribeEhsi(loaded),
            line => line.Contains("radiusFactor 0.6775", StringComparison.Ordinal));
        Assert.Contains(Bbs757LayoutDescriptions.DescribeEhsi(loaded),
            line => line.Contains("legStride 1632", StringComparison.Ordinal));
        Assert.Contains(Bbs757LayoutDescriptions.DescribeEhsi(loaded),
            line => line.Contains("EHSI modes words: 0=VOR, 1=ILS, 2=MAP, 3=PLAN", StringComparison.Ordinal));

        // Equality is content equality over every field but the source file.
        Assert.Equal(Sample(), Sample() with
        {
            SourceFile = "C:\\elsewhere.json",
            Anchor = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            ManualTerminators = new[] { 9, 19 },
            LegPositions = Sample().LegPositions.ToArray(),
        });
        Assert.NotEqual(Sample(), Sample() with { ManualTerminators = new[] { 9, 20 } });
        Assert.NotEqual(Sample(), Sample() with { RadiusFactor = 0.68 });
    }

    [Fact]
    public void A_file_without_the_ehsi_reader_reads_as_missing()
    {
        // A file written before this reader has no entry for it: refused whole, never read with zeros (WASM-26).
        string text = LayoutFileFormat.Write(Module, FileWithAll());
        var root = JsonNode.Parse(text)!;
        Assert.True(root["readers"]!.AsObject().Remove(Bbs757Module.Ehsi));
        string old = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        using (JsonDocument.Parse(old)) { }   // still JSON: refused for the missing reader, not its syntax
        Assert.False(LayoutFileFormat.TryRead(Module, old, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));

        // A layout lacking one of its members is refused too.
        var root2 = JsonNode.Parse(text)!;
        Assert.True(root2["readers"]![Bbs757Module.Ehsi]!["layout"]!.AsObject().Remove("legStride"));
        Assert.False(LayoutFileFormat.TryRead(Module, root2.ToJsonString(), out _, out string? failure2));
        Assert.False(string.IsNullOrEmpty(failure2));

        // So is a leg position lacking one of its members.
        var root3 = JsonNode.Parse(text)!;
        Assert.True(root3["readers"]![Bbs757Module.Ehsi]!["layout"]!["legPositions"]![0]!.AsObject().Remove("longitude"));
        Assert.False(LayoutFileFormat.TryRead(Module, root3.ToJsonString(), out _, out string? failure3));
        Assert.False(string.IsNullOrEmpty(failure3));
    }

    [Fact]
    public void Bbs757EhsiLayout_equality_covers_every_field_but_the_source_file()
    {
        var sample = Sample();
        var ctor = typeof(Bbs757EhsiLayout).GetConstructors().Single();
        var parameters = ctor.GetParameters();
        object?[] Values() => parameters.Select(p => typeof(Bbs757EhsiLayout).GetProperty(p.Name!)!.GetValue(sample)).ToArray();

        var sourceOnly = sample with { SourceFile = "C:\\elsewhere.json" };
        Assert.Equal(sample, sourceOnly);
        Assert.Equal(sample.GetHashCode(), sourceOnly.GetHashCode());

        int checkedFields = 0;
        for (int i = 0; i < parameters.Length; i++)
        {
            string name = parameters[i].Name!;
            if (name == "SourceFile") continue;
            object?[] args = Values();
            args[i] = args[i] switch
            {
                uint u => u + 1,
                int n => n + 1,
                double d => d + 1.5,
                string s => s + "x",
                byte[] b => ((byte[])b.Clone()).Also(c => c[^1] ^= 0x55),
                IReadOnlyList<int> a => a.Select(x => x + 1).Append(0).ToArray(),
                IReadOnlyList<Bbs757EhsiLegPosition> p => p.Select(x => x with { Kind = x.Kind + 1 }).ToArray(),
                Bbs757WordTable t => new Bbs757WordTable(t.Words.ToDictionary(kv => kv.Key, kv => kv.Value)
                    .Also(d => d[d.Keys.Min()] = d[d.Keys.Min()] + "x")),
                _ => throw new InvalidOperationException($"{name}: no mutation for {args[i]?.GetType()}"),
            };
            var changed = (Bbs757EhsiLayout)ctor.Invoke(args);
            Assert.False(sample.Equals(changed), $"Equals ignores {name}");
            Assert.False(changed.Equals(sample), $"Equals ignores {name} (reversed)");
            Assert.NotEqual(sample.GetHashCode(), changed.GetHashCode());
            checkedFields++;
        }
        Assert.Equal(parameters.Length - 1, checkedFields);
    }

    [Fact]
    public void The_ehsi_reader_follows_the_upper_eicas_in_the_module()
    {
        // The module's last reader until plan 27 added the ADI's after it.
        Assert.Equal(new[] { "fma", "efb", "lowerEicas", "upperEicas", "ehsi", "adi" }, Module.Readers.Select(r => r.Id).ToArray());
    }
}
