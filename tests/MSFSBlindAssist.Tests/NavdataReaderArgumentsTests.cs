using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Tests;

public class NavdataReaderArgumentsTests
{
    [Fact]
    public void Build_PutsFlagOutputConfigThenBasePath()
    {
        string args = NavdataReaderArguments.Build(
            "MSFS", @"C:\db\msfs.sqlite", @"C:\app\Resources\navdatareader.cfg", @"E:\msfs2024");

        Assert.Equal(
            "-f MSFS -o \"C:\\db\\msfs.sqlite\" -c \"C:\\app\\Resources\\navdatareader.cfg\" -b \"E:\\msfs2024\"",
            args);
    }

    [Fact]
    public void Build_DoublesTrailingBackslashSoItCannotEscapeTheClosingQuote()
    {
        // CommandLineToArgvW reads \" as a literal quote, so -b "E:\" would deliver E:"
        // and navdatareader would fail on a base path that does not exist.
        string args = NavdataReaderArguments.Build("MSFS", @"C:\db\msfs.sqlite", null, @"E:\");

        Assert.EndsWith(@"-b ""E:\\""", args);
    }

    [Fact]
    public void Build_OmitsConfigWhenNull()
    {
        string args = NavdataReaderArguments.Build("MSFS24", @"C:\db\fs2024.sqlite", null, null);

        Assert.Equal("-f MSFS24 -o \"C:\\db\\fs2024.sqlite\"", args);
        Assert.DoesNotContain("-c", args);
    }

    [Fact]
    public void Build_KeepsBasePathLastSoAMalformedOneCannotSwallowTheConfig()
    {
        string args = NavdataReaderArguments.Build("MSFS", "out.sqlite", "cfg.cfg", "base");

        Assert.True(args.IndexOf("-c ", StringComparison.Ordinal) < args.IndexOf("-b ", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_QuotesPathsContainingSpaces()
    {
        string args = NavdataReaderArguments.Build(
            "MSFS", @"C:\My Databases\msfs.sqlite", null, @"C:\Program Files\MSFS");

        Assert.Contains(@"-o ""C:\My Databases\msfs.sqlite""", args);
        Assert.Contains(@"-b ""C:\Program Files\MSFS""", args);
    }
}
