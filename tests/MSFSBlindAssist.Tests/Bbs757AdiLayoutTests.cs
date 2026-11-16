using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The ADI layout record (plan 27, Task 2): it round-trips through the generic layout file format
/// byte-identical under any culture (WASM-21), a part that is missing keeps its reason, a file without the reader reads
/// as missing (WASM-26), and its equality covers every field but the source file. Offsets and constants are those
/// measured from the module's memory at a KSEA gate on 2026-11-16 (docs/b757.md, "The ADI (Alt+P)"), copied
/// here; the seventh part, the FMC's reference speeds, is not drawn on the ADI (it feeds the flap speed readouts).</summary>
public class Bbs757AdiLayoutTests
{
    private static readonly ModuleDescription Module = Bbs757Module.Description;

    public static Bbs757AdiAttitude SampleAttitude() => new(
        IrsAttitude: 3040, IrsMode: 3428, IrsModeNav: 5, IrsState: 3420, IrsStateValid: 4, IrsStateAligning: 3,
        AttWord: "ATT");

    public static Bbs757AdiSpeed SampleSpeed() => new(
        AdcAirspeed: 24, AdcAltitude: 32, MinAirspeed: 30,
        McpSpeed: 872, AfdsPitchMode: 11924, VnavModes: new[] { 6, 7 }, AfdsSpeedIntervene: 15584,
        FmsV1: 1664, FmsVr: 1668, FmsV1Inop: 38132,
        AfdsTimer: 15744, TimerStart: 8, TimerStopped: 16, Clock: 1197288, BugSeconds: 120, FlapSpeedAfterSeconds: 10,
        AfdsFlapSpeed: 16184, NoFlapSpeed: 100, FlapSpeedMaxAltitude: 20000, FlapSpeedNearVr: 4,
        TapeHalfRange: 41, MachShownFrom: 0.4, MachShownUntil: 0.38,
        SpdWord: "SPD", V1Word: "V1", InopWord: "INOP", V1BugWord: "1", VrBugWord: "R", FlapBugWord: "F",
        GroundSpeedWord: "GS", NoMachWord: "----");

    public static Bbs757AdiIls SampleIls() => new(
        LocPointer: 0, LocDots: 8, GsPointer: 16, LocScale: 32, GsScale: 33, LocValid: 34, GsValid: 35, Expanded: 36,
        LocFlag: 37, GsFlag: 38, Test: 39,
        AfdsRollMode: 11932, RunwayModes: new[] { 3, 4 }, RunwayBelow: 2500,
        LocWord: "LOC", GlideslopeWord: "G/S");

    public static Bbs757AdiRadioAltitude SampleRadioAltitude() => new(
        RaAltitude: 2920, RaOffset: 16.93, RaTest: 2868, RaTestState: 344, RaTestAltitude: 2880,
        RaTestAlone: new[] { 2, 4 }, RaTestAdded: 3, RaMaxShown: 2500, RaRoundFrom: 100,
        DhValue: 40, DhShown: 46,
        AdcVerticalSpeed: 56, AltDescent: -150, AltFrom: 500,
        DhWord: "DH", RaWord: "RA", AltWord: "ALT");

    public static Bbs757AdiFlightDirector SampleFlightDirector() => new(FdCaptain: 11957, FdPitch: 15848, FdRoll: 15856);

    public static Bbs757AdiTcas SampleTcas() => new(TcasAbove: 56, TcasBelow: 64, TcasAbovePitch: 72, TcasBelowPitch: 80);

    /// <summary>The FMC's VREF 30 and VREF 25 in the fms (the installed build's values): not drawn on the ADI, they
    /// feed the flap speed readouts.</summary>
    public static Bbs757AdiReferenceSpeeds SampleReferenceSpeeds() => new(FmsVref30: 2128, FmsVref25: 2120);

    /// <summary>This build's values with every part derived: the same anchor, manager and pfd vptr as the FMA's sample
    /// (the ADI is drawn by the same object).</summary>
    public static Bbs757AdiLayout Sample() => new(
        SourceFile: "",
        AnchorAddress: 0x636930,
        Anchor: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        ManagerBase: 0xf1334,
        ManagerSpan: 0x100,
        PfdVptr: 82028,
        Powered: 4, Failed: 5, Blanked: 64,
        PfdToSource: 620, SourceToIrs: 712, SourceToAdc: 716, SourceToRa: 740,
        DevicePowered: 4, DeviceFailed: 5,
        PfdToMcp: 608, PfdToAfds: 612, PfdToFms: 628,
        IlsBlock: 136, AdcBlock: 368,
        Attitude: SampleAttitude(), AttitudeMissing: null,
        Speed: SampleSpeed(), SpeedMissing: null,
        Ils: SampleIls(), IlsMissing: null,
        RadioAltitude: SampleRadioAltitude(), RadioAltitudeMissing: null,
        FlightDirector: SampleFlightDirector(), FlightDirectorMissing: null,
        Tcas: SampleTcas(), TcasMissing: null,
        ReferenceSpeeds: SampleReferenceSpeeds(), ReferenceSpeedsMissing: null);

    private static LayoutFile FileWith(ReaderResult adi) => new(
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
            new ReaderResult(Bbs757Module.Ehsi, Bbs757EhsiLayoutTests.Sample(), null),
            adi,
        });

    private static LayoutFile FileWith(Bbs757AdiLayout adi) => FileWith(new ReaderResult(Bbs757Module.Adi, adi, null));

    private static Bbs757AdiLayout ReadAdi(string text)
    {
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        return (Bbs757AdiLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Adi).Layout!;
    }

    private static JsonObject AdiNode(JsonNode root) => root["readers"]![Bbs757Module.Adi]!["layout"]!.AsObject();

    [Fact]
    public void The_adi_layout_round_trips_byte_identical()
    {
        var expected = Sample();
        string text = LayoutFileFormat.Write(Module, FileWith(Sample() with { SourceFile = "C:\\somewhere\\BBS752.json" }));
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757AdiLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Adi).Layout!;
        Assert.Equal(expected, loaded);
        Assert.Equal(expected.GetHashCode(), loaded.GetHashCode());
        Assert.Equal("", loaded.SourceFile);   // saved empty: the store fills it in
        Assert.Equal(SampleAttitude(), loaded.Attitude);
        Assert.Equal(SampleSpeed(), loaded.Speed);
        Assert.Equal(SampleIls(), loaded.Ils);
        Assert.Equal(SampleRadioAltitude(), loaded.RadioAltitude);
        Assert.Equal(SampleFlightDirector(), loaded.FlightDirector);
        Assert.Equal(SampleTcas(), loaded.Tcas);
        Assert.Equal(SampleReferenceSpeeds(), loaded.ReferenceSpeeds);
        Assert.Null(loaded.AttitudeMissing);
        Assert.Null(loaded.SpeedMissing);
        Assert.Null(loaded.IlsMissing);
        Assert.Null(loaded.RadioAltitudeMissing);
        Assert.Null(loaded.FlightDirectorMissing);
        Assert.Null(loaded.TcasMissing);
        Assert.Null(loaded.ReferenceSpeedsMissing);

        // Byte-identical: written again from what was read, and under a comma-decimal culture (WASM-21).
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            Assert.Equal(text, TestCulture.Run(culture, () => LayoutFileFormat.Write(Module, FileWith(Sample()))));
            Assert.Equal(expected, TestCulture.Run(culture, () => ReadAdi(text)));
        }

        // The draw's constants keep their fractions and signs, in the text and read back.
        foreach (string member in new[] { "\"raOffset\": 16.93", "\"machShownFrom\": 0.4", "\"machShownUntil\": 0.38",
                     "\"altDescent\": -150", "\"minAirspeed\": 30", "\"clock\": 1197288", "\"pfdVptr\": 82028",
                     "\"fmsVref30\": 2128", "\"fmsVref25\": 2120" })
            Assert.Contains(member, text, StringComparison.Ordinal);
        Assert.Equal((2128, 2120), (loaded.ReferenceSpeeds!.FmsVref30, loaded.ReferenceSpeeds.FmsVref25));
        // The parts are saved in the record's order, the reference speeds last (the EHSI has a tcasMissing of its own,
        // earlier in the file: the ADI's is the last).
        Assert.True(text.LastIndexOf("\"tcasMissing\"", StringComparison.Ordinal) < text.IndexOf("\"referenceSpeeds\"", StringComparison.Ordinal));
        Assert.Equal(16.93, loaded.RadioAltitude!.RaOffset);
        Assert.Equal(-150.0, loaded.RadioAltitude.AltDescent);
        Assert.Equal(0.4, loaded.Speed!.MachShownFrom);
        Assert.Equal(0.38, loaded.Speed.MachShownUntil);
        Assert.Equal(1197288u, loaded.Speed.Clock);
        Assert.Equal(new[] { 6, 7 }, loaded.Speed.VnavModes);
        Assert.Equal(new[] { 3, 4 }, loaded.Ils!.RunwayModes);
        Assert.Equal(new[] { 2, 4 }, loaded.RadioAltitude.RaTestAlone);
        Assert.Equal(("SPD", "V1", "INOP", "1", "R", "F", "GS", "----"),
            (loaded.Speed.SpdWord, loaded.Speed.V1Word, loaded.Speed.InopWord, loaded.Speed.V1BugWord, loaded.Speed.VrBugWord,
                loaded.Speed.FlapBugWord, loaded.Speed.GroundSpeedWord, loaded.Speed.NoMachWord));
        Assert.Equal(("ATT", "LOC", "G/S", "DH", "RA", "ALT"),
            (loaded.Attitude!.AttWord, loaded.Ils.LocWord, loaded.Ils.GlideslopeWord, loaded.RadioAltitude.DhWord,
                loaded.RadioAltitude.RaWord, loaded.RadioAltitude.AltWord));

        // Equality is content equality: the source file ignored, the anchor and the lists by content.
        Assert.Equal(Sample(), Sample() with
        {
            SourceFile = "C:\\elsewhere.json",
            Anchor = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            Speed = SampleSpeed() with { VnavModes = new List<int> { 6, 7 } },
            Ils = SampleIls() with { RunwayModes = new List<int> { 3, 4 } },
            RadioAltitude = SampleRadioAltitude() with { RaTestAlone = new List<int> { 2, 4 } },
        });
        Assert.NotEqual(Sample(), Sample() with { IlsBlock = 137 });
        Assert.NotEqual(Sample(), Sample() with { Anchor = new byte[] { 1, 2, 3, 4, 5, 6, 7, 9 } });
        Assert.NotEqual(Sample(), Sample() with { Attitude = SampleAttitude() with { AttWord = "ATT " } });
        Assert.NotEqual(Sample(), Sample() with { Speed = SampleSpeed() with { VnavModes = new[] { 6, 8 } } });
        Assert.NotEqual(Sample(), Sample() with { Ils = SampleIls() with { RunwayModes = new[] { 3 } } });
        Assert.NotEqual(Sample(), Sample() with { RadioAltitude = SampleRadioAltitude() with { RaOffset = 16.94 } });
        Assert.NotEqual(Sample(), Sample() with { FlightDirector = SampleFlightDirector() with { FdRoll = 15857 } });
        Assert.NotEqual(Sample(), Sample() with { Tcas = SampleTcas() with { TcasBelowPitch = 81 } });
        Assert.NotEqual(Sample(), Sample() with { ReferenceSpeeds = SampleReferenceSpeeds() with { FmsVref25 = 2121 } });

        // The dump names the core and every part, the same under any culture.
        var lines = Bbs757LayoutDescriptions.DescribeAdi(loaded with { SourceFile = "BBS752.json" });
        Assert.Equal(8, lines.Count);
        Assert.All(lines, l => Assert.StartsWith("file analysis: BBS752.json: ADI", l, StringComparison.Ordinal));
        string described = string.Join("\n", lines);
        foreach (string part in new[]
                 {
                     "ADI: manager 0xf1334 span 256, pfd vptr 0x1406c", "powered 4, failed 5, blanked 64",
                     "pfdToSource 620, sourceToIrs 712, sourceToAdc 716, sourceToRa 740", "devicePowered 4, deviceFailed 5",
                     "pfdToMcp 608, pfdToAfds 612, pfdToFms 628, ilsBlock 136, adcBlock 368",
                     "ADI attitude: irsAttitude 3040, irsMode 3428, irsModeNav 5, irsState 3420, irsStateValid 4, irsStateAligning 3, attWord \"ATT\"",
                     "ADI speed tape: adcAirspeed 24, adcAltitude 32, minAirspeed 30.0, mcpSpeed 872, afdsPitchMode 11924, vnavModes [6, 7], afdsSpeedIntervene 15584",
                     "fmsV1 1664, fmsVr 1668, fmsV1Inop 38132, afdsTimer 15744, timerStart 8, timerStopped 16, clock 0x1244e8",
                     "bugSeconds 120.0, flapSpeedAfterSeconds 10.0, afdsFlapSpeed 16184, noFlapSpeed 100.0, flapSpeedMaxAltitude 20000.0, flapSpeedNearVr 4.0",
                     "tapeHalfRange 41.0, machShownFrom 0.4, machShownUntil 0.38",
                     "spdWord \"SPD\", v1Word \"V1\", inopWord \"INOP\", v1BugWord \"1\", vrBugWord \"R\", flapBugWord \"F\", groundSpeedWord \"GS\", noMachWord \"----\"",
                     "ADI localizer and glideslope: locPointer 0, locDots 8, gsPointer 16, locScale 32, gsScale 33, locValid 34, gsValid 35, expanded 36, locFlag 37, gsFlag 38, test 39",
                     "afdsRollMode 11932, runwayModes [3, 4], runwayBelow 2500.0, locWord \"LOC\", glideslopeWord \"G/S\"",
                     "ADI radio altitude and decision height: raAltitude 2920, raOffset 16.93, raTest 2868, raTestState 344, raTestAltitude 2880, raTestAlone [2, 4], raTestAdded 3",
                     "raMaxShown 2500.0, raRoundFrom 100.0, dhValue 40, dhShown 46, adcVerticalSpeed 56, altDescent -150.0, altFrom 500.0",
                     "dhWord \"DH\", raWord \"RA\", altWord \"ALT\"",
                     "ADI flight director: fdCaptain 11957, fdPitch 15848, fdRoll 15856",
                     "ADI TCAS: tcasAbove 56, tcasBelow 64, tcasAbovePitch 72, tcasBelowPitch 80",
                     "ADI reference speeds: fmsVref30 2128, fmsVref25 2120",
                 })
            Assert.Contains(part, described, StringComparison.Ordinal);
        foreach (string culture in new[] { "de-DE", "sv-SE" })
            Assert.Equal(lines, TestCulture.Run(culture, () => Bbs757LayoutDescriptions.DescribeAdi(loaded with { SourceFile = "BBS752.json" })));
    }

    [Fact]
    public void A_missing_adi_part_keeps_its_reason()
    {
        var layout = Sample() with
        {
            Speed = null, SpeedMissing = "speed: no stopwatch in the afds",
            Tcas = null, TcasMissing = "tcas: nothing writes the PFD's block",
        };
        string text = LayoutFileFormat.Write(Module, FileWith(layout));
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757AdiLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Adi).Layout!;
        Assert.Null(loaded.Speed);
        Assert.Null(loaded.Tcas);
        Assert.Equal("speed: no stopwatch in the afds", loaded.SpeedMissing);
        Assert.Equal("tcas: nothing writes the PFD's block", loaded.TcasMissing);
        Assert.Equal(SampleAttitude(), loaded.Attitude);
        Assert.Equal(SampleIls(), loaded.Ils);
        Assert.Equal(SampleRadioAltitude(), loaded.RadioAltitude);
        Assert.Equal(SampleFlightDirector(), loaded.FlightDirector);
        Assert.Equal(SampleReferenceSpeeds(), loaded.ReferenceSpeeds);
        Assert.Null(loaded.AttitudeMissing);
        Assert.Null(loaded.IlsMissing);
        Assert.Null(loaded.RadioAltitudeMissing);
        Assert.Null(loaded.FlightDirectorMissing);
        Assert.Null(loaded.ReferenceSpeedsMissing);
        Assert.Equal(layout, loaded);
        Assert.Equal(layout.GetHashCode(), loaded.GetHashCode());
        Assert.NotEqual(layout, Sample());
        Assert.NotEqual(layout, layout with { SpeedMissing = "another reason" });
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));

        var lines = Bbs757LayoutDescriptions.DescribeAdi(loaded);
        Assert.Equal(8, lines.Count);
        Assert.Contains(lines, l => l.EndsWith("ADI: speed tape missing: speed: no stopwatch in the afds", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.EndsWith("ADI: TCAS missing: tcas: nothing writes the PFD's block", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("ADI attitude: irsAttitude 3040", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.EndsWith("ADI reference speeds: fmsVref30 2128, fmsVref25 2120", StringComparison.Ordinal));

        // The reader reports each missing part with its reason, in the layout's order, for the generator and the store.
        var reader = Module.Reader(Bbs757Module.Adi);
        Assert.Empty(reader.PartFailures(Sample()));
        Assert.Equal(new[] { ("speed tape", "speed: no stopwatch in the afds"), ("TCAS", "tcas: nothing writes the PFD's block") },
            reader.PartFailures(loaded).ToArray());

        // Every part missing: the core alone still loads, each reason kept.
        var none = Sample() with
        {
            Attitude = null, AttitudeMissing = "no attitude",
            Speed = null, SpeedMissing = "no speed",
            Ils = null, IlsMissing = "no ils",
            RadioAltitude = null, RadioAltitudeMissing = "no radio altitude",
            FlightDirector = null, FlightDirectorMissing = "no flight director",
            Tcas = null, TcasMissing = "no tcas",
            ReferenceSpeeds = null, ReferenceSpeedsMissing = "no reference speeds",
        };
        var noneLoaded = ReadAdi(LayoutFileFormat.Write(Module, FileWith(none)));
        Assert.Equal(none, noneLoaded);
        Assert.Null(noneLoaded.ReferenceSpeeds);
        Assert.Equal("no reference speeds", noneLoaded.ReferenceSpeedsMissing);
        Assert.Equal(new[]
            {
                ("attitude", "no attitude"), ("speed tape", "no speed"), ("localizer and glideslope", "no ils"),
                ("radio altitude and decision height", "no radio altitude"), ("flight director", "no flight director"),
                ("TCAS", "no tcas"), ("reference speeds", "no reference speeds"),
            },
            reader.PartFailures(noneLoaded).ToArray());
        Assert.Equal(new[] { ("reference speeds", "no reference speeds") },
            reader.PartFailures(Sample() with { ReferenceSpeeds = null, ReferenceSpeedsMissing = "no reference speeds" }).ToArray());
        var noneLines = Bbs757LayoutDescriptions.DescribeAdi(noneLoaded);
        Assert.Equal(8, noneLines.Count);
        foreach (string missing in new[]
                 {
                     "ADI: attitude missing: no attitude", "ADI: speed tape missing: no speed",
                     "ADI: localizer and glideslope missing: no ils",
                     "ADI: radio altitude and decision height missing: no radio altitude",
                     "ADI: flight director missing: no flight director", "ADI: TCAS missing: no tcas",
                     "ADI: reference speeds missing: no reference speeds",
                 })
            Assert.Contains(noneLines, l => l.EndsWith(missing, StringComparison.Ordinal));

        // The core is still required, a missing part's own member too (saved as null, never left out), and a part that is
        // there needs every one of its members: such a file is refused, never read with zeros (WASM-26).
        foreach (var damage in new Action<JsonObject>[]
                 {
                     adi => Assert.True(adi.Remove("pfdToSource")),
                     adi => Assert.True(adi.Remove("adcBlock")),
                     adi => Assert.True(adi.Remove("speed")),
                     adi => Assert.True(adi.Remove("tcasMissing")),
                     adi => Assert.True(adi.Remove("referenceSpeeds")),
                     adi => Assert.True(adi.Remove("referenceSpeedsMissing")),
                     adi => Assert.True(adi["attitude"]!.AsObject().Remove("irsMode")),
                     adi => Assert.True(adi["radioAltitude"]!.AsObject().Remove("raTestAlone")),
                     adi => Assert.True(adi["referenceSpeeds"]!.AsObject().Remove("fmsVref25")),
                 })
        {
            var root = JsonNode.Parse(text)!;
            damage(AdiNode(root));
            string damaged = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            using (JsonDocument.Parse(damaged)) { }   // still JSON: refused for the missing member, not its syntax
            Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? refused));
            Assert.Contains("reader adi: the layout does not load", refused, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_file_without_the_adi_reader_reads_as_missing()
    {
        // A file written before this reader has no entry for it: refused whole, never read with zeros (WASM-26).
        string text = LayoutFileFormat.Write(Module, FileWith(Sample()));
        var root = JsonNode.Parse(text)!;
        Assert.True(root["readers"]!.AsObject().Remove(Bbs757Module.Adi));
        string old = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        using (JsonDocument.Parse(old)) { }   // still JSON: refused for the missing reader, not its syntax
        Assert.DoesNotContain("\"adi\"", old, StringComparison.Ordinal);
        Assert.Contains("\"ehsi\"", old, StringComparison.Ordinal);
        Assert.False(LayoutFileFormat.TryRead(Module, old, out _, out string? failure));
        Assert.Equal("reader adi is missing", failure);

        // Nor is such a file ever written.
        var without = FileWith(Sample());
        without = without with { Readers = without.Readers.Where(r => r.Reader != Bbs757Module.Adi).ToArray() };
        Assert.Throws<ArgumentException>(() => LayoutFileFormat.Write(Module, without));

        // A file whose generator could not derive the ADI's core says so in the reader's place, and loads.
        string failed = LayoutFileFormat.Write(Module, FileWith(new ReaderResult(Bbs757Module.Adi, null, "adi: no pfd in the manager")));
        Assert.True(LayoutFileFormat.TryRead(Module, failed, out var file, out string? unexpected), unexpected);
        var result = file!.Readers.Single(r => r.Reader == Bbs757Module.Adi);
        Assert.Null(result.Layout);
        Assert.Equal("adi: no pfd in the manager", result.Failure);
        Assert.Equal(5, file.Readers.Count(r => r.Layout is not null));
        Assert.Equal(failed, LayoutFileFormat.Write(Module, file));
    }

    [Fact]
    public void Bbs757AdiLayout_equality_covers_every_field_but_the_source_file()
    {
        var sample = Sample();
        var sourceOnly = sample with { SourceFile = "C:\\elsewhere.json" };
        Assert.Equal(sample, sourceOnly);
        Assert.Equal(sample.GetHashCode(), sourceOnly.GetHashCode());

        Assert.Equal(typeof(Bbs757AdiLayout).GetConstructors().Single().GetParameters().Length - 1,
            AssertEveryFieldCounts(sample, skip: nameof(Bbs757AdiLayout.SourceFile), hashesDiffer: true));
        AssertEveryFieldCounts(SampleAttitude());
        AssertEveryFieldCounts(SampleSpeed());
        AssertEveryFieldCounts(SampleIls());
        AssertEveryFieldCounts(SampleRadioAltitude());
        AssertEveryFieldCounts(SampleFlightDirector());
        AssertEveryFieldCounts(SampleTcas());
        AssertEveryFieldCounts(SampleReferenceSpeeds());

        var derivation = new Bbs757AdiDerivation(sample, null);
        Assert.Equal(derivation, new Bbs757AdiDerivation(Sample(), null));
        Assert.NotEqual(derivation, new Bbs757AdiDerivation(null, "adi: no pfd in the manager"));
    }

    [Fact]
    public void The_adi_reader_is_the_modules_last()
    {
        Assert.Equal("adi", Bbs757Module.Adi);
        Assert.Equal(Bbs757Module.Adi, Module.Readers.Last().Id);
        Assert.Equal(new[] { "fma", "efb", "lowerEicas", "upperEicas", "ehsi", "adi" }, Module.Readers.Select(r => r.Id).ToArray());
        var reader = Module.Reader(Bbs757Module.Adi);
        Assert.Equal("ADI", reader.LogName);
        Assert.Equal(typeof(Bbs757AdiLayout), reader.LayoutType);
        Assert.Equal(new ModuleAnchor(0x636930, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), reader.AnchorOf(Sample()));
        Assert.Equal("C:\\layouts\\BBS752.json", ((Bbs757AdiLayout)reader.WithSourceFile(Sample(), "C:\\layouts\\BBS752.json")).SourceFile);
        Assert.Equal(Bbs757LayoutDescriptions.DescribeAdi(Sample()), reader.Describe(Sample()));
    }

    /// <summary>Changes each constructor argument of <paramref name="sample"/> in turn and checks the result is no
    /// longer equal, then that a copy with every list and array rebuilt still is and hashes alike. Returns how many
    /// arguments it changed.</summary>
    private static int AssertEveryFieldCounts<T>(T sample, string? skip = null, bool hashesDiffer = false) where T : class
    {
        var ctor = typeof(T).GetConstructors().Single();
        var parameters = ctor.GetParameters();
        object?[] Values() => parameters.Select(p => typeof(T).GetProperty(p.Name!)!.GetValue(sample)).ToArray();

        int changedFields = 0;
        for (int i = 0; i < parameters.Length; i++)
        {
            string name = parameters[i].Name!;
            if (name == skip) continue;
            object?[] args = Values();
            args[i] = Changed(parameters[i].ParameterType, args[i], $"{typeof(T).Name}.{name}");
            var changed = (T)ctor.Invoke(args);
            Assert.False(sample.Equals(changed), $"{typeof(T).Name}.Equals ignores {name}");
            Assert.False(changed.Equals(sample), $"{typeof(T).Name}.Equals ignores {name} (reversed)");
            // Unequal values may collide; the layout's own hash is checked as its neighbors' are.
            if (hashesDiffer) Assert.NotEqual(sample.GetHashCode(), changed.GetHashCode());
            changedFields++;
        }

        object?[] rebuilt = Values().Select(v => v switch
        {
            byte[] b => b.ToArray(),
            IReadOnlyList<int> a => a.ToList(),
            _ => v,
        }).ToArray();
        var copy = (T)ctor.Invoke(rebuilt);
        Assert.Equal(sample, copy);
        Assert.Equal(sample.GetHashCode(), copy.GetHashCode());
        return changedFields;
    }

    private static object Changed(Type type, object? value, string what) => value switch
    {
        uint u => u + 1,
        int n => n + 1,
        double d => d + 1.5,
        string s => s + "x",
        null when type == typeof(string) => "a reason",
        byte[] b => b.Select((x, i) => i == b.Length - 1 ? (byte)(x ^ 0x55) : x).ToArray(),
        IReadOnlyList<int> a => a.Select(x => x + 1).ToArray(),
        Bbs757AdiAttitude p => p with { IrsStateAligning = p.IrsStateAligning + 1 },
        Bbs757AdiSpeed p => p with { VnavModes = new[] { 6 } },
        Bbs757AdiIls p => p with { RunwayModes = new[] { 4, 3 } },
        Bbs757AdiRadioAltitude p => p with { RaTestAlone = new[] { 2, 4, 5 } },
        Bbs757AdiFlightDirector p => p with { FdPitch = p.FdPitch + 1 },
        Bbs757AdiTcas p => p with { TcasAbove = p.TcasAbove + 1 },
        Bbs757AdiReferenceSpeeds p => p with { FmsVref30 = p.FmsVref30 + 1 },
        _ => throw new InvalidOperationException($"{what}: no change for {value?.GetType().Name ?? type.Name}"),
    };
}
