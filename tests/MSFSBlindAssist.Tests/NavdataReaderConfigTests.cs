using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Tests;

public class NavdataReaderConfigTests
{
    private const string Good = """
        # comment
        [Options]
        DatabaseReport=true

        [Filter]
        ExcludeFilenames=brx*.bgl,obx*.bgl,AIRACCycle.bgl,*_jetways.bgl
        ExcludeBglObjectFilter=APRON2
        """;

    [Fact]
    public void IsUsable_AcceptsAConfigCarryingTheJetwaysExclusion()
        => Assert.True(NavdataReaderConfig.IsUsable(Good));

    [Fact]
    public void IsUsable_RejectsEmptyOrWhitespace()
    {
        Assert.False(NavdataReaderConfig.IsUsable(null));
        Assert.False(NavdataReaderConfig.IsUsable(""));
        Assert.False(NavdataReaderConfig.IsUsable("   \r\n  "));
    }

    [Fact]
    public void IsUsable_RejectsATruncatedCopyThatNeverReachesExcludeFilenames()
    {
        // A partial extract or interrupted copy: header comments only. This is the case
        // File.Exists cannot catch, and navdatareader accepts it silently while running
        // with every filter empty.
        string truncated = "# =====\n# Navdatareader configuration used by MSFS Blind Assist\n# =====\n";
        Assert.False(NavdataReaderConfig.IsUsable(truncated));
    }

    [Fact]
    public void IsUsable_RejectsAConfigWhoseExclusionWasRemoved()
    {
        string withoutExclusion = Good.Replace(",*_jetways.bgl", "");
        Assert.False(NavdataReaderConfig.IsUsable(withoutExclusion));
    }

    [Fact]
    public void IsUsable_IgnoresSurroundingWhitespaceAndCase()
    {
        string spaced = "[Filter]\r\nExcludeFilenames = brx*.bgl , *_JETWAYS.BGL \r\n";
        Assert.True(NavdataReaderConfig.IsUsable(spaced));
    }

    [Fact]
    public void IsUsable_IgnoresACommentedOutExcludeFilenamesLine()
    {
        string commented = "[Filter]\r\n#ExcludeFilenames=*_jetways.bgl\r\n";
        Assert.False(NavdataReaderConfig.IsUsable(commented));
    }

    [Fact]
    public void IsUsableFile_ReturnsFalseForAMissingFile()
        => Assert.False(NavdataReaderConfig.IsUsableFile(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".cfg")));

    [Fact]
    public void IsUsableFile_ReturnsTrueForTheShippedConfigShape()
    {
        string path = Path.Combine(Path.GetTempPath(), "ndr-" + Guid.NewGuid().ToString("N") + ".cfg");
        File.WriteAllText(path, Good);
        try
        {
            Assert.True(NavdataReaderConfig.IsUsableFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
