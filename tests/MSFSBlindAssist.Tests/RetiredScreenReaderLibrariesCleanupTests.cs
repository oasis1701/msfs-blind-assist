using System;
using System.IO;
using MSFSBlindAssist.Patching;
using Xunit;

/// <summary>
/// The updater only overlays (it deletes nothing the new zip leaves out), so an install updated past the move to Prism
/// keeps Tolk.dll and nvdaControllerClient.dll beside the exe. RetiredScreenReaderLibrariesCleanup removes those two
/// files at start-up, only in a Prism build's folder, and nothing else.
/// </summary>
public class RetiredScreenReaderLibrariesCleanupTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-retired-sr-" + Guid.NewGuid().ToString("N"));

    public RetiredScreenReaderLibrariesCleanupTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    string Make(string relative)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void An_updated_Prism_install_loses_Tolk_and_the_NVDA_client()
    {
        Make("prism.dll");
        string tolk = Make("Tolk.dll"), nvda = Make("nvdaControllerClient.dll");

        Assert.Equal(2, RetiredScreenReaderLibrariesCleanup.RemoveRetiredLibraries(_dir));

        Assert.False(File.Exists(tolk));
        Assert.False(File.Exists(nvda));
    }

    [Fact]
    public void Nothing_else_is_touched()
    {
        string prism = Make("prism.dll");
        string other = Make("Prismatoid.dll");
        string nested = Make(Path.Combine("navdatareader", "Tolk.dll"));
        Make("Tolk.dll");

        RetiredScreenReaderLibrariesCleanup.RemoveRetiredLibraries(_dir);

        Assert.True(File.Exists(prism));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void A_folder_without_prism_is_left_alone()
    {
        // Not a Prism build (an older version's folder): its own speech still needs these.
        string tolk = Make("Tolk.dll"), nvda = Make("nvdaControllerClient.dll");

        Assert.Equal(0, RetiredScreenReaderLibrariesCleanup.RemoveRetiredLibraries(_dir));

        Assert.True(File.Exists(tolk));
        Assert.True(File.Exists(nvda));
    }

    [Fact]
    public void Once_they_are_gone_it_does_nothing()
    {
        Make("prism.dll");

        Assert.Equal(0, RetiredScreenReaderLibrariesCleanup.RemoveRetiredLibraries(_dir));
    }

    [Fact]
    public void A_file_it_cannot_delete_is_skipped_without_throwing()
    {
        Make("prism.dll");
        string tolk = Make("Tolk.dll"), nvda = Make("nvdaControllerClient.dll");

        int removed;
        using (new FileStream(tolk, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            removed = RetiredScreenReaderLibrariesCleanup.RemoveRetiredLibraries(_dir);
        }

        Assert.Equal(1, removed);
        Assert.True(File.Exists(tolk));
        Assert.False(File.Exists(nvda));
    }

    [Fact]
    public void A_missing_folder_does_not_throw()
    {
        Assert.Equal(0, RetiredScreenReaderLibrariesCleanup.RemoveRetiredLibraries(Path.Combine(_dir, "absent")));
    }
}
