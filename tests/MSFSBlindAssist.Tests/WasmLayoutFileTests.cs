// The app loads its layouts from layout files (LayoutStore), and the reading tests from the golden ones (GoldenLayouts).
// Every golden must load, and save back to the very text it was loaded from on any PC and culture (WASM-21); a file with
// an unknown format version or damaged text is refused, never read as a partial layout (WASM-26).

using System.Globalization;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class WasmLayoutFileTests
{
    private static readonly ModuleDescription Module = Pmdg737Module.Description;

    private static readonly string[] Goldens =
    {
        "fma", "engine-displays", "nav-display", "nav-display-vdev-up", "nav-display-trend-left", "pfd",
    };

    public static TheoryData<string> Names => new(Goldens);

    private static string Refused(string text)
    {
        Assert.False(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure));
        Assert.Null(file);
        Assert.False(string.IsNullOrEmpty(failure));
        return failure!;
    }

    private static string SourceFileOf(object layout) => layout switch
    {
        Pmdg737FmaLayout l => l.SourceFile,
        Pmdg737EngineDisplayLayout l => l.SourceFile,
        Pmdg737NavDisplayLayout l => l.SourceFile,
        Pmdg737PfdLayout l => l.SourceFile,
        _ => throw new ArgumentException($"no 737 layout: {layout.GetType().Name}", nameof(layout)),
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_golden_loads_and_saves_back_to_the_same_text(string name)
    {
        string text = GoldenLayouts.Text(name);
        Assert.True(text == LayoutFileFormat.Write(Module, GoldenLayouts.Load(name)), $"{name} saves back to other text");
    }

    [Fact]
    public void Saving_a_golden_under_sv_SE_and_de_DE_gives_the_same_text()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in Goldens)
            {
                string text = GoldenLayouts.Text(name);
                foreach (string culture in new[] { "sv-SE", "de-DE" })
                {
                    CultureInfo.CurrentCulture = new CultureInfo(culture);
                    var file = GoldenLayouts.Load(name);
                    Assert.True(text == LayoutFileFormat.Write(Module, file), $"{name} saves to other text under {culture}");
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void The_golden_list_is_every_golden_file()
    {
        // A golden this class does not list would be skipped by every test here.
        string folder = Path.GetDirectoryName(GoldenLayouts.PathOf("fma"))!;
        var files = Directory.GetFiles(folder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(Goldens.OrderBy(n => n, StringComparer.Ordinal), files);
    }

    [Fact]
    public void An_unknown_format_version_is_refused()
    {
        string text = GoldenLayouts.Text("fma");
        Assert.Contains("\"formatVersion\": 1,", text);
        Assert.Contains("format version 2", Refused(text.Replace("\"formatVersion\": 1,", "\"formatVersion\": 2,")));
    }

    [Fact]
    public void Damaged_text_is_refused()
    {
        foreach (string name in Goldens)
        {
            string text = GoldenLayouts.Text(name);
            Refused(text[..(text.Length / 2)]);
        }
    }

    [Fact]
    public void A_golden_loads_with_an_empty_source_file()
    {
        // The file never holds an installation path; the store fills in the installed file, the tests "fixture".
        foreach (string name in Goldens)
        {
            var layouts = GoldenLayouts.Load(name).Readers.Where(r => r.Layout is not null).ToList();
            Assert.NotEmpty(layouts);
            Assert.All(layouts, r => Assert.Equal("", SourceFileOf(r.Layout!)));
        }
        Assert.Equal("fixture", GoldenLayouts.NavDisplay.SourceFile);
    }

    [Fact]
    public void Loading_a_golden_twice_gives_equal_layouts()
    {
        foreach (string name in Goldens)
        {
            var first = GoldenLayouts.Load(name);
            var second = GoldenLayouts.Load(name);
            Assert.Equal(first.Readers.Select(r => (r.Reader, r.Failure)), second.Readers.Select(r => (r.Reader, r.Failure)));
            for (int i = 0; i < first.Readers.Count; i++)
            {
                var layout = first.Readers[i].Layout;
                Assert.True(layout is null ? second.Readers[i].Layout is null : layout.Equals(second.Readers[i].Layout),
                    $"{name}: {first.Readers[i].Reader} loads differently the second time");
            }
        }
    }
}
