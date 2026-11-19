using LayoutBundle;
using MSFSBlindAssist.DisplayLayouts;

namespace MSFSBlindAssist.Tests;

public sealed class LayoutBundlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "layoutbundler-" + Guid.NewGuid().ToString("N"));
    private string Store => Path.Combine(_root, "store");
    private string Out => Path.Combine(_root, "out");

    public LayoutBundlerTests() => Directory.CreateDirectory(Store);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static readonly ModuleDescription[] Modules = { TinyLayouts.TestModule };

    private BundleResult Run() => LayoutBundler.Bundle(Store, Out, Modules);

    private string Put(string folder, string name, string text)
    {
        string dir = Path.Combine(Store, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    private string PutValid(byte[] wasm)
    {
        var file = TinyLayouts.FileFor(wasm);
        return Put("tinytest", file.FileName, TinyLayouts.Text(file));
    }

    [Fact]
    public void Copies_every_layout_file_of_a_known_module()
    {
        string a = PutValid(TinyLayouts.Wasm);
        string b = PutValid(TinyLayouts.OtherCode);

        var result = Run();

        Assert.True(result.Ok, string.Join("\n", result.Lines));
        foreach (string source in new[] { a, b })
        {
            string copy = Path.Combine(Out, "tinytest", Path.GetFileName(source));
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(copy));
            // Both readers failed in these files: the release log shows it.
            Assert.Contains($"tinytest/{Path.GetFileName(source)}: generator version 1, 0 of 2 readers have a layout", result.Lines);
        }
    }

    [Fact]
    public void Each_file_line_counts_the_readers_with_a_layout()
    {
        var layout = new TinyLayouts.TinyLayout(TinyLayouts.DataAddress, TinyLayouts.Data[..8], "");
        var file = TinyLayouts.FileFor(TinyLayouts.Wasm, first: layout);
        Put("tinytest", file.FileName, TinyLayouts.Text(file));

        var result = Run();

        Assert.True(result.Ok, string.Join("\n", result.Lines));
        Assert.Contains($"tinytest/{file.FileName}: generator version 1, 1 of 2 readers have a layout", result.Lines);
    }

    [Fact]
    public void A_file_that_does_not_load_fails_and_nothing_is_copied()
    {
        PutValid(TinyLayouts.Wasm);
        string badName = TinyLayouts.FileFor(TinyLayouts.Variant).FileName;
        Put("tinytest", badName, "{");

        var result = Run();

        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains(badName));
        Assert.False(Directory.Exists(Path.Combine(Out, "tinytest")) && Directory.GetFiles(Path.Combine(Out, "tinytest")).Length > 0);
    }

    [Fact]
    public void A_file_whose_name_does_not_match_its_contents_fails()
    {
        string otherName = TinyLayouts.FileFor(TinyLayouts.OtherCode).FileName;
        Put("tinytest", otherName, TinyLayouts.Text(TinyLayouts.FileFor(TinyLayouts.Wasm)));

        var result = Run();

        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains(otherName) && l.Contains("name does not match"));
    }

    [Fact]
    public void A_known_module_with_no_file_fails()
    {
        var result = Run();

        Assert.False(result.Ok);
        Assert.Contains("tinytest: no layout file", result.Lines);
    }

    [Fact]
    public void An_unknown_module_folder_is_skipped()
    {
        PutValid(TinyLayouts.Wasm);
        Put("pmdg777", TinyLayouts.FileFor(TinyLayouts.Wasm).FileName, "{}");

        var result = Run();

        Assert.True(result.Ok, string.Join("\n", result.Lines));
        Assert.Contains("pmdg777: skipped, not a module this app knows", result.Lines);
        Assert.False(Directory.Exists(Path.Combine(Out, "pmdg777")));
    }

    [Fact]
    public void Dot_folders_and_non_layout_names_are_ignored()
    {
        string valid = PutValid(TinyLayouts.Wasm);
        Put(".git", "config", "x");
        File.WriteAllText(Path.Combine(Store, "README.md"), "x");
        File.WriteAllText(Path.Combine(Store, ".gitattributes"), "* -text\n"); // the store's root file
        Put("tinytest", "notes.txt", "x");
        Put("tinytest", TinyLayouts.FileFor(TinyLayouts.Wasm).FileName + ".tmp", "x");

        var result = Run();

        Assert.True(result.Ok, string.Join("\n", result.Lines));
        Assert.Equal(new[] { Path.GetFileName(valid) }, Directory.GetFiles(Path.Combine(Out, "tinytest")).Select(Path.GetFileName));
        Assert.DoesNotContain(result.Lines, l => l.Contains(".git"));
    }

    [Fact]
    public void A_missing_store_folder_fails()
    {
        string missing = Path.Combine(_root, "nowhere");

        var result = LayoutBundler.Bundle(missing, Out, Modules);

        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains(missing));
    }
}
