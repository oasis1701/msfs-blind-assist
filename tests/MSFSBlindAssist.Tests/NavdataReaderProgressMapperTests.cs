using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Tests;

public class NavdataReaderProgressMapperTests
{
    [Fact]
    public void Map_ReportsIndexCreationAt92NotAsAGenericWrite()
    {
        // The generic Creating|Writing arm used to be tested first and swallowed every
        // "Creating index ..." line, leaving the 92% branch unreachable.
        var mapper = new NavdataReaderProgressMapper();

        var update = mapper.Map("Creating index ix_airport_ident");

        Assert.NotNull(update);
        Assert.Equal(92, update!.Percent);
    }

    [Fact]
    public void Map_NeverWalksTheBarBackwards()
    {
        // navdatareader interleaves "Reading ..." lines throughout the run, so a naive
        // keyword ladder oscillates 25 -> 50 -> 75 -> 25 for the whole build.
        var mapper = new NavdataReaderProgressMapper();

        Assert.Equal(25, mapper.Map("Reading scenery configuration")!.Percent);
        Assert.Equal(50, mapper.Map("Processing airports")!.Percent);

        var backwards = mapper.Map("Reading BGL file EDDF.bgl");

        Assert.NotNull(backwards);
        Assert.Equal(-1, backwards!.Percent);
        Assert.NotNull(backwards.Details);
    }

    [Fact]
    public void Map_KeepsAdvancingAfterASuppressedBackwardsStep()
    {
        var mapper = new NavdataReaderProgressMapper();

        mapper.Map("Processing airports");
        mapper.Map("Reading BGL file EDDF.bgl");

        Assert.Equal(75, mapper.Map("Writing database")!.Percent);
    }

    [Fact]
    public void Map_ReturnsNullForAnUnrecognisedLine()
    {
        var mapper = new NavdataReaderProgressMapper();

        Assert.Null(mapper.Map("something entirely unrelated"));
    }

    [Fact]
    public void Map_ReturnsNullForBlankInput()
    {
        var mapper = new NavdataReaderProgressMapper();

        Assert.Null(mapper.Map("   "));
    }

    [Fact]
    public void Map_ReportsVacuumAndAnalyzeInOrder()
    {
        var mapper = new NavdataReaderProgressMapper();

        Assert.Equal(85, mapper.Map("Vacuum database")!.Percent);
        Assert.Equal(90, mapper.Map("Analyzing database")!.Percent);
    }
}
