using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class SceneryPackageLocatorTests
{
    [Fact]
    public void Keeps_existing_addon_folders_and_drops_base_and_navigraph_entries()
    {
        string col = @"fs-base-genericairports, C:\P\Community\orbx-airport-ktiw-tacoma-narrows, C:\P\Community\navigraph-navdata, C:\P\Official\OneStore\asobo-airport-ksea-seattle-tacoma, C:\P\Community\missing";
        var dirs = SceneryPackageLocator.PackageDirs(col, d => !d.EndsWith("missing"));
        Assert.Equal(new[] { @"C:\P\Community\orbx-airport-ktiw-tacoma-narrows", @"C:\P\Official\OneStore\asobo-airport-ksea-seattle-tacoma" }, dirs);
    }

    [Fact]
    public void Duplicates_collapse_and_blank_input_yields_nothing()
    {
        string col = @"C:\P\Community\pyreegue-airport-egnx-east-midlands, C:\P\Community\pyreegue-airport-egnx-east-midlands";
        Assert.Single(SceneryPackageLocator.PackageDirs(col, _ => true));
        Assert.Empty(SceneryPackageLocator.PackageDirs("", _ => true));
    }
}
