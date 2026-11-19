using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The lower EICAS layout record (plan 25, Task 2): it round-trips through the generic layout file format
/// byte-identical (WASM-21), a page part that is missing keeps its reason, and a file without the reader reads as
/// missing (WASM-26). Offsets are those measured from the module's memory on 2026-11-09, copied here.</summary>
public class Bbs757LowerEicasLayoutTests
{
    private static readonly ModuleDescription Module = Bbs757Module.Description;
    private const string Hash0 = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string Hash1 = "1111111111111111111111111111111111111111111111111111111111111111";

    public static Bbs757LowerEicasEnginePage SampleEnginePage() => new(
        N2: 16, N3: 24, FuelFlow: 88, OilPressure: 96, OilTemperature: 104, OilQuantity: 112, Vibration: 120,
        MaxN2Left: 560, MaxN2Right: 564, MaxN3Left: 568, MaxN3Right: 572,
        LowerToSettings: 288, PoundsFlag: 596,
        StartFlagA: 6922, StartFlagB: 6992, StartFlagC: 6960,
        HoldTimer: 6936, HoldDelay: 7104, HoldByteA: 6924, HoldByteB: 6925);

    public static Bbs757LowerEicasStatusPage SampleStatusPage() => new(
        ListAddress: 985716, ListBegin: 0, ListEnd: 4, ListStride: 4, ListCap: 164,
        RecordText: 0, RecordLength: 4, RecordSequence: 8,
        LowerToHydraulics: 276, HydraulicPressures: 2896, HydraulicPressureStride: 8,
        HydraulicQuantities: 2996, HydraulicQuantityStride: 40, HydraulicCapacity: 0, HydraulicQuantity: 8,
        LowerToApu: 272, ApuEgt: 1544, ApuRpm: 1528, ApuOilQuantity: 1600,
        LowerToOxygen: 284, OxygenShown: 946, OxygenCapacity: 440, OxygenActive: 456, OxygenFull: 464, OxygenLevel: 472,
        Rudder: 548, AileronLeft: 536, Elevator: 544, AileronRight: 540,
        LowerToComputer: 132, BrakeTemperatures: 584, BrakeWheels: 8);

    public static Bbs757LowerEicasLayout Sample() => new(
        SourceFile: "",
        AnchorAddress: 0x636930,
        Anchor: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        ManagerBase: 0xf1334,
        ManagerSpan: 0x100,
        LowerVptr: 79068,
        PowerSet: 4, PowerClear: 5, BlankClear: 64,
        Page: 576, EnginePage: 1, StatusPage: 2, EngineOff: 3, NLatch: 580,
        LeftEngine: 136, RightEngine: 140,
        EngineToData: 3576, EngineType: 3768, EngineInvalid: 1944, EngineInvalidValue: 2,
        HpSpoolRr: 24, HpSpoolPw: 16,
        Engine: SampleEnginePage(), EngineMissing: null,
        Status: SampleStatusPage(), StatusMissing: null);

    private static LayoutFile FileWith(Bbs757LowerEicasLayout lower) => new(
        GeneratorVersion: 1,
        CodeHash: Hash0,
        DataRanges: new[] { new DataRange(0x400, 64) },
        DataHash: Hash1,
        Searches: Array.Empty<DataSearch>(),
        Readers: new[]
        {
            new ReaderResult(Bbs757Module.Fma, Bbs757LayoutFileTests.SampleFma(), null),
            new ReaderResult(Bbs757Module.Efb, Bbs757LayoutFileTests.SampleEfb(), null),
            new ReaderResult(Bbs757Module.LowerEicas, lower, null),
            new ReaderResult(Bbs757Module.UpperEicas, Bbs757UpperEicasLayoutTests.Sample(), null),
            new ReaderResult(Bbs757Module.Ehsi, Bbs757EhsiLayoutTests.Sample(), null),
            new ReaderResult(Bbs757Module.Adi, Bbs757AdiLayoutTests.Sample(), null),
        });

    [Fact]
    public void The_lower_eicas_layout_round_trips_byte_identical()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(Sample()));
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757LowerEicasLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.LowerEicas).Layout!;
        Assert.Equal(Sample(), loaded);
        Assert.NotNull(loaded.Engine);
        Assert.NotNull(loaded.Status);
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
        foreach (string culture in new[] { "de-DE", "sv-SE" })
            Assert.Equal(text, TestCulture.Run(culture, () => LayoutFileFormat.Write(Module, FileWith(Sample()))));

        // Content equality: the anchor by content, every part by content, the reasons by value.
        Assert.Equal(Sample().GetHashCode(), Sample().GetHashCode());
        Assert.NotEqual(Sample(), Sample() with { Page = 577 });
        Assert.NotEqual(Sample(), Sample() with { Anchor = new byte[] { 9 } });
        Assert.NotEqual(Sample(), Sample() with { Engine = SampleEnginePage() with { Vibration = 1 } });
        Assert.NotEqual(Sample(), Sample() with { Status = SampleStatusPage() with { BrakeTemperatures = 1 } });

        string described = string.Join("\n", Bbs757LayoutDescriptions.DescribeLowerEicas(loaded));
        foreach (string part in new[] { "lowerVptr 0x134dc", "page 576", "enginePage 1", "statusPage 2", "engineOff 3", "nLatch 580",
                     "hpSpoolRr 24", "hpSpoolPw 16", "vibration 120", "holdDelay 7104", "listAddress 0xf0a74", "listCap 164",
                     "brakeTemperatures 584", "oxygenLevel 472" })
            Assert.Contains(part, described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_page_part_keeps_its_reason()
    {
        var layout = Sample() with
        {
            Engine = null, EngineMissing = "no engine page in this build",
            Status = null, StatusMissing = "no status list found",
        };
        string text = LayoutFileFormat.Write(Module, FileWith(layout));
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757LowerEicasLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.LowerEicas).Layout!;
        Assert.Null(loaded.Engine);
        Assert.Null(loaded.Status);
        Assert.Equal("no engine page in this build", loaded.EngineMissing);
        Assert.Equal("no status list found", loaded.StatusMissing);
        Assert.Equal(layout, loaded);
        Assert.NotEqual(layout, layout with { EngineMissing = "another reason" });
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));

        string described = string.Join("\n", Bbs757LayoutDescriptions.DescribeLowerEicas(loaded));
        Assert.Contains("engine page missing: no engine page in this build", described, StringComparison.Ordinal);
        Assert.Contains("status page missing: no status list found", described, StringComparison.Ordinal);

        // The required members and parts are still required: a file lacking the page field is refused.
        string damaged = text.Replace("\"page\": 576,", "");
        Assert.NotEqual(text, damaged);
        Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? refused));
        Assert.False(string.IsNullOrEmpty(refused));
    }

    [Fact]
    public void A_missing_page_part_is_reported_through_the_readers_part_failures()
    {
        var reader = Module.Reader(Bbs757Module.LowerEicas);
        Assert.Empty(reader.PartFailures(Sample()));

        var both = Sample() with
        {
            Engine = null, EngineMissing = "no engine page in this build",
            Status = null, StatusMissing = "no status list found",
        };
        Assert.Equal(new[] { ("engine page", "no engine page in this build"), ("STATUS page", "no status list found") },
            reader.PartFailures(both).ToArray());

        var onlyStatus = Sample() with { Status = null, StatusMissing = "no status list found" };
        Assert.Equal(new[] { ("STATUS page", "no status list found") }, reader.PartFailures(onlyStatus).ToArray());
    }

    [Fact]
    public void A_file_without_the_lower_eicas_reader_reads_as_missing()
    {
        // Today's generator-4 757 file: the FMA and EFB readers only (WASM-26).
        string text = LayoutFileFormat.Write(Module, FileWith(Sample()));
        var root = System.Text.Json.Nodes.JsonNode.Parse(text)!;
        Assert.True(root["readers"]!.AsObject().Remove(Bbs757Module.LowerEicas));
        string old = root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Assert.DoesNotContain("lowerEicas", old, StringComparison.Ordinal);
        Assert.False(LayoutFileFormat.TryRead(Module, old, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));
    }
}
