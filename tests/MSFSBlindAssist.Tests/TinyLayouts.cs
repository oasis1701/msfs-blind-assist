// A tiny module for the layout store's tests: two readers, hand-built .wasm files and the layout file the generator
// would write for one. No process.

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

internal static class TinyLayouts
{
    /// <summary>A layout as small as a reader's can be: an anchor and the source file.</summary>
    public sealed record TinyLayout(uint AnchorAddress, byte[] Anchor, string SourceFile);

    private static LayoutReader TinyReader(string id, string logName) =>
        new LayoutReader<TinyLayout>(id, logName, l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
            l => new[] { $"{logName} from {l.SourceFile}: anchor 0x{l.AnchorAddress:x}" });

    public static readonly ModuleDescription TestModule = new("tinytest", "Tiny.wasm", "^tiny$", new[] { "panel" },
        new[] { TinyReader("first", "first display"), TinyReader("second", "second display") });

    public const uint DataAddress = 0x1000;

    public static readonly byte[] Data = Enumerable.Range(0, 64).Select(i => (byte)(i + 1)).ToArray();

    /// <summary>What the layout reads: the whole segment, so any changed data byte changes the data hash.</summary>
    private static readonly DataRange[] ReadRanges = { new(DataAddress, 64) };

    public static readonly byte[] Wasm = TinyWasmFile.Build("draw", DataAddress, Data);

    /// <summary>The same code, one read byte different: the 737-800 and -900 case when the layout reads the byte.</summary>
    public static readonly byte[] Variant = TinyWasmFile.Build("draw", DataAddress, Data.Select((b, i) => i == 10 ? (byte)0xEE : b).ToArray());

    /// <summary>Another build: a different function name is different code.</summary>
    public static readonly byte[] OtherCode = TinyWasmFile.Build("drawOther", DataAddress, Data);

    public static WasmFileImage Image(byte[] wasm)
    {
        Assert.True(WasmFileImage.TryParse(wasm, out var image, out var failure), failure);
        return image;
    }

    /// <summary>The layout file the generator would write for <paramref name="wasm"/>: both readers failed unless the
    /// first is given a layout.</summary>
    public static LayoutFile FileFor(byte[] wasm, int generatorVersion = 1, TinyLayout? first = null)
    {
        var image = Image(wasm);
        var readers = new[]
        {
            first is null ? new ReaderResult("first", null, "no first table") : new ReaderResult("first", first, null),
            new ReaderResult("second", null, "no second table"),
        };
        return new LayoutFile(generatorVersion, image.CodeHash, ReadRanges, LayoutFit.DataHash(image, ReadRanges)!,
            Array.Empty<DataSearch>(), readers);
    }

    /// <summary>The file's text, as the generator writes it.</summary>
    public static string Text(LayoutFile file) => LayoutFileFormat.Write(TestModule, file);
}
