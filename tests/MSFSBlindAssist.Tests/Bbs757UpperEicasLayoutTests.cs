using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The upper EICAS layout record round-trips through the generic layout file format, byte-identical under any
/// culture (WASM-21), a file lacking its reader or one of its members is refused (WASM-26), and the module lists every
/// 757 reader in order.</summary>
public class Bbs757UpperEicasLayoutTests
{
    private static readonly ModuleDescription Module = Bbs757Module.Description;

    /// <summary>This build's values, as the plan's measurements recorded them (MaxTextLength is a stand-in).</summary>
    internal static Bbs757UpperEicasLayout Sample() => new(
        SourceFile: "",
        AnchorAddress: 0x636930,
        Anchor: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        ManagerBase: 0xf1334,
        ManagerSpan: 0x100,
        UpperEicasVptr: 0x133e0,
        ToComputer: 132,
        ToEngineLeft: 136,
        ToEngineRight: 140,
        ToLower: 272,
        ToPneumatic: 276,
        ToFms: 284,
        ToFire: 288,
        ToReverserLeft: 308,
        ToReverserRight: 312,
        ToTmc: 316,
        ToTat: 328,
        Powered: 4,
        Failed: 5,
        TimedBlank: 64,
        Compact: 416,
        EgtRed: 192,
        EgtAmber: 196,
        N1Red: 208,
        N1Amber: 220,
        MaxN1Left: 336,
        MaxN1Right: 340,
        MaxEgtLeft: 344,
        MaxEgtRight: 348,
        EnvelopeRows: 420,
        EnvelopeRowStride: 36,
        EnvelopeRowCount: 3,
        EnvelopeRowFl: 0,
        EnvelopeRowMin: 4,
        EnvelopeRowMax: 8,
        MainList: 0xf0a64,
        CommunicationList: 0xf0a84,
        EntryText: 0,
        EntryLength: 4,
        EntryId: 12,
        EntryCancelled: 15,
        EntryLevel: 48,
        MaxMessages: 278,
        MaxTextLength: 46,
        Levels: new Bbs757WordTable(new Dictionary<int, string>
        {
            [4] = "warning", [3] = "caution", [2] = "advisory", [1] = "communication", [0] = "communication",
        }),
        LinesPerPage: 10,
        CommunicationLinesBase: 11,
        ComputerPage: 344,
        ComputerRecallTime: 352,
        ComputerBlank: 364,
        Clock: 0x1244e8,
        RecallShowSeconds: 0.5,
        PageWord: "PAGE",
        RecallWord: "RECALL",
        EngineBlock: 3576,
        BlockEgt: 0,
        BlockN1: 8,
        BlockEpr: 56,
        EngineN1Alternate: 4104,
        EngineN1AlternateFlags: new[] { 6922, 6960, 6992 },
        EngineValid: 1944,
        EngineInvalidValue: 2,
        EngineType: 3768,
        EngineTypePw: 1,
        EngineCombustion: 3992,
        PwBlankBelow: 5.0,
        BlockN2Pw: 16,
        ThrustMode: 452,
        ThrustNumber: 456,
        Derate: 3092,
        DtoFlags: new[] { 3096, 3228 },
        DerateShown: 3016,
        ReferenceEpr: 480,
        ManualEprLeft: 2976,
        ManualEprRight: 2984,
        TmcFailed: 408,
        TmcChannelLeft: 440,
        TmcChannelRight: 441,
        ThrustWords: new Bbs757WordTable(new Dictionary<int, string>
        {
            [0] = "TO", [4] = "CLB", [5] = "CLB1", [6] = "CLB2", [7] = "CRZ", [8] = "CRZ", [9] = "CRZ",
            [10] = "CON", [11] = "G/A",
        }),
        DtoWord: "D-TO",
        ManualWord: "MAN",
        NumberedModes: new[] { 0, 7, 8, 9 },
        ThrustNumbers: new Bbs757WordTable(new Dictionary<int, string> { [1] = "1", [2] = "2" }),
        InhibitModes: new[] { 0, 1, 2, 3, 11 },
        TatValue: 344,
        ReverserState: 664,
        ReverserMoving: 1,
        ReverserDeployed: 2,
        RevWord: "REV",
        PneumaticComponents: 1932,
        ComponentStride: 184,
        ComponentId: 4,
        ComponentPosition: 72,
        ComponentState: 52,
        TaiLeftId: 16,
        TaiRightId: 22,
        TaiMinOpen: 7.0,
        TaiStates: new[] { 0, 1 },
        TaiWord: "TAI",
        MaxComponents: 64,
        FmsOnGround: 1513,
        FireSwitchLeft: 400,
        FireSwitchRight: 401,
        LowerMode: 576,
        EnvelopeLowerMode: 1,
        EnvelopeTitle: "IN-FLT START",
        EnvelopeFl: "FL",
        EnvelopeKts: "KTS");

    private static LayoutFile FileWithAll(params string[] without) => new(
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
            new ReaderResult(Bbs757Module.UpperEicas, Sample(), null),
            new ReaderResult(Bbs757Module.Ehsi, Bbs757EhsiLayoutTests.Sample(), null),
            new ReaderResult(Bbs757Module.Adi, Bbs757AdiLayoutTests.Sample(), null),
        }.Where(r => !without.Contains(r.Reader)).ToArray());

    [Fact]
    public void The_upper_eicas_layout_round_trips_byte_identical()
    {
        var expected = Sample() with { SourceFile = "" };
        string text = LayoutFileFormat.Write(Module, FileWithAll());
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757UpperEicasLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.UpperEicas).Layout!;
        Assert.Equal(expected, loaded);
        Assert.Equal(expected.GetHashCode(), loaded.GetHashCode());
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            Assert.Equal(text, TestCulture.Run(culture, () => LayoutFileFormat.Write(Module, FileWithAll())));
            Assert.True(TestCulture.Run(culture, () =>
                LayoutFileFormat.TryRead(Module, text, out var f2, out _)
                && ((Bbs757UpperEicasLayout)f2!.Readers.Single(r => r.Reader == Bbs757Module.UpperEicas).Layout!).Equals(expected)),
                culture);
        }
        Assert.Equal(0.5, loaded.RecallShowSeconds);
        Assert.Equal(7.0, loaded.TaiMinOpen);
        Assert.Equal("warning", loaded.Levels.Lookup(4));
        Assert.Contains(Bbs757LayoutDescriptions.DescribeUpperEicas(loaded),
            line => line.Contains("recallShowSeconds 0.5", StringComparison.Ordinal));
        Assert.Contains(Bbs757LayoutDescriptions.DescribeUpperEicas(loaded),
            line => line.Contains("envelopeRowFl 0, envelopeRowMin 4, envelopeRowMax 8", StringComparison.Ordinal));
        Assert.Equal((0, 4, 8), (loaded.EnvelopeRowFl, loaded.EnvelopeRowMin, loaded.EnvelopeRowMax));
        Assert.Equal((1, 2, "REV", "TAI"), (loaded.ReverserMoving, loaded.ReverserDeployed, loaded.RevWord, loaded.TaiWord));
        Assert.Equal("2", loaded.ThrustNumbers.Lookup(2));
        Assert.Contains(Bbs757LayoutDescriptions.DescribeUpperEicas(loaded),
            line => line.Contains("reverserMoving 1, reverserDeployed 2, revWord REV", StringComparison.Ordinal));
        Assert.Contains(Bbs757LayoutDescriptions.DescribeUpperEicas(loaded),
            line => line.Contains("taiWord TAI", StringComparison.Ordinal));
        Assert.Contains(Bbs757LayoutDescriptions.DescribeUpperEicas(loaded),
            line => line.EndsWith("upper EICAS thrust number words: 1=1, 2=2", StringComparison.Ordinal));

        // Equality is content equality over every field but the source file.
        Assert.Equal(Sample(), Sample() with
        {
            SourceFile = "C:\\elsewhere.json",
            Anchor = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            DtoFlags = new[] { 3096, 3228 },
        });
        Assert.NotEqual(Sample(), Sample() with { DtoFlags = new[] { 3096, 3229 } });
        Assert.NotEqual(Sample(), Sample() with { Anchor = new byte[] { 1, 2, 3, 4, 5, 6, 7, 9 } });
        Assert.NotEqual(Sample(), Sample() with { TaiMinOpen = 7.5 });
        Assert.NotEqual(Sample(), Sample() with { EnvelopeKts = "KT" });
        Assert.NotEqual(Sample(), Sample() with
        {
            Levels = new Bbs757WordTable(new Dictionary<int, string> { [4] = "warning" }),
        });
    }

    [Fact]
    public void A_file_without_the_upper_eicas_reader_reads_as_missing()
    {
        // A file written before this reader has no entry for it: refused whole, never read with zeros (WASM-26).
        string text = LayoutFileFormat.Write(Module, FileWithAll());
        var root = JsonNode.Parse(text)!;
        Assert.True(root["readers"]!.AsObject().Remove(Bbs757Module.UpperEicas));
        string old = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        using (JsonDocument.Parse(old)) { }   // still JSON: refused for the missing reader, not its syntax
        Assert.False(LayoutFileFormat.TryRead(Module, old, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));

        // A layout lacking one of its members is refused too.
        var root2 = JsonNode.Parse(text)!;
        Assert.True(root2["readers"]![Bbs757Module.UpperEicas]!["layout"]!.AsObject().Remove("toTmc"));
        Assert.False(LayoutFileFormat.TryRead(Module, root2.ToJsonString(), out _, out string? failure2));
        Assert.False(string.IsNullOrEmpty(failure2));
    }

    [Fact]
    public void Bbs757UpperEicasLayout_equality_covers_every_field_but_the_source_file()
    {
        var sample = Sample();
        var ctor = typeof(Bbs757UpperEicasLayout).GetConstructors().Single();
        var parameters = ctor.GetParameters();
        object?[] Values() => parameters.Select(p => typeof(Bbs757UpperEicasLayout).GetProperty(p.Name!)!.GetValue(sample)).ToArray();

        // Only the source file differing: equal, same hash.
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
                int[] a => ((int[])a.Clone()).Also(c => c[^1] += 1),
                Bbs757WordTable t => new Bbs757WordTable(t.Words.ToDictionary(kv => kv.Key, kv => kv.Value)
                    .Also(d => d[d.Keys.Min()] = d[d.Keys.Min()] + "x")),
                _ => throw new InvalidOperationException($"{name}: no mutation for {args[i]?.GetType()}"),
            };
            var changed = (Bbs757UpperEicasLayout)ctor.Invoke(args);
            Assert.False(sample.Equals(changed), $"Equals ignores {name}");
            Assert.False(changed.Equals(sample), $"Equals ignores {name} (reversed)");
            Assert.NotEqual(sample.GetHashCode(), changed.GetHashCode());
            checkedFields++;
        }
        Assert.Equal(parameters.Length - 1, checkedFields);
    }

    [Fact]
    public void Every_757_reader_is_in_the_module_in_order()
    {
        Assert.Equal(new[] { "fma", "efb", "lowerEicas", "upperEicas", "ehsi", "adi" }, Module.Readers.Select(r => r.Id).ToArray());
    }
}

internal static class Bbs757UpperEicasMutation
{
    /// <summary>Applies <paramref name="change"/> to the value and returns it (a mutation inside an expression).</summary>
    public static T Also<T>(this T value, Action<T> change)
    {
        change(value);
        return value;
    }
}
