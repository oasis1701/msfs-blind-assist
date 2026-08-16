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
        Assert.NotNull(backwards.Status);
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

    [Fact]
    public void Map_KeepsTheHighWaterMarkAcrossASuppressedReading()
    {
        // A mutant that assigns _highestReported inside the suppressed branch would
        // still pass the basic backwards tests, but this pins the high-water mark behavior.
        // 85 (Vacuum) → suppressed Reading (25, but suppressed) → 50 (Processing) must
        // ALSO be suppressed because 50 < 85.
        var mapper = new NavdataReaderProgressMapper();

        mapper.Map("Vacuum database");
        mapper.Map("Reading BGL file");

        var result = mapper.Map("Processing airports");

        Assert.NotNull(result);
        Assert.Equal(-1, result!.Percent);
    }

    [Fact]
    public void Map_ReportsAirportCountAt90WithFormattedNumbers()
    {
        var mapper = new NavdataReaderProgressMapper();

        var result = mapper.Map("Found 12345 airports in the database");

        Assert.NotNull(result);
        Assert.Equal(90, result!.Percent);
        Assert.Equal("Processed 12,345 airports", result.Status);
        Assert.Equal("Found 12,345 airports in scenery library", result.Details);
    }

    [Fact]
    public void Map_ReportsLoadingLineWithoutMovingTheBar()
    {
        var mapper = new NavdataReaderProgressMapper();

        // First establish a baseline
        mapper.Map("Processing airports");

        var result = mapper.Map("Loading scenery file");

        Assert.NotNull(result);
        Assert.Equal(-1, result!.Percent);
        Assert.Null(result.Status);
        Assert.Equal("Loading scenery file", result.Details);
    }

    [Fact]
    public void Map_ReturnsNullForLongLoadingLine()
    {
        var mapper = new NavdataReaderProgressMapper();

        var longLine = "Loading " + new string('x', 100);
        var result = mapper.Map(longLine);

        Assert.Null(result);
    }
}
