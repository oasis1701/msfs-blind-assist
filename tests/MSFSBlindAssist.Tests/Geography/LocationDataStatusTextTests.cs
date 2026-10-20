using System.Globalization;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

public class LocationDataStatusTextTests
{
    private static readonly LocationDataInfo Installed = new(LocationDataStore.SchemaVersion, true, "2026-10-10", "v5.1.2", 8_361_204, DateTime.UtcNow);

    [Fact]
    public void Installed_data_says_ready_with_its_date_release_and_size()
    {
        Assert.Equal("Ready. GeoNames data from 10 October 2026, Natural Earth v5.1.2, 8.4 million places.",
            LocationDataStatusText.Idle(Installed, LocationDataStore.SchemaVersion, 0));
    }

    [Fact]
    public void Data_from_another_schema_asks_for_an_update()
    {
        Assert.StartsWith("This version of MSFS Blind Assist needs newer location data.",
            LocationDataStatusText.Idle(Installed with { SchemaVersion = 0 }, LocationDataStore.SchemaVersion, 0));
        Assert.StartsWith("This version of MSFS Blind Assist needs newer location data.",
            LocationDataStatusText.Idle(Installed with { Complete = false }, LocationDataStore.SchemaVersion, 0));
    }

    [Fact]
    public void A_kept_partial_download_is_offered_for_resume()
    {
        Assert.Equal("Not downloaded yet. Partial download kept: 150 megabytes. Press Download location data to resume.",
            LocationDataStatusText.Idle(null, LocationDataStore.SchemaVersion, 150L * 1024 * 1024));
    }

    [Fact]
    public void A_whole_download_waiting_to_be_built_says_so()
    {
        Assert.Equal("Downloaded but not built yet. Press Download location data to build it; nothing is downloaded again.",
            LocationDataStatusText.Idle(null, LocationDataStore.SchemaVersion, 0, downloadComplete: true));
        // With current data installed, the leftover download only matters to the next update.
        Assert.StartsWith("Ready.", LocationDataStatusText.Idle(Installed, LocationDataStore.SchemaVersion, 0, downloadComplete: true));
    }

    [Fact]
    public void A_build_waiting_for_the_next_start_says_so()
    {
        Assert.Equal("Location data is ready and will be used from the next start of MSFS Blind Assist.",
            LocationDataStatusText.Idle(null, LocationDataStore.SchemaVersion, 0, readyAtNextStart: true));
        // An update waiting to replace current data too: the next start installs the new build.
        Assert.Equal(LocationDataStatusText.ReadyAtNextStart,
            LocationDataStatusText.Idle(Installed, LocationDataStore.SchemaVersion, 0, readyAtNextStart: true));
    }

    [Fact]
    public void Nothing_yet_explains_the_download_and_the_disk_it_needs()
    {
        string text = LocationDataStatusText.Idle(null, LocationDataStore.SchemaVersion, 0);
        Assert.StartsWith("Not downloaded. About ", text);
        Assert.Contains("free disk space", text);
        Assert.EndsWith("Output C and Location Info need it.", text);
    }

    [Theory]
    [InlineData(8_361_204, "8.4 million")]
    [InlineData(1_000_000, "1.0 million")]
    [InlineData(834_512, "834512")]
    public void Place_counts_read_as_millions(long count, string expected)
    {
        Assert.Equal(expected, LocationDataStatusText.Millions(count));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Dates_and_numbers_are_invariant_under_any_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("Ready. GeoNames data from 10 October 2026, Natural Earth v5.1.2, 8.4 million places.",
                LocationDataStatusText.Idle(Installed, LocationDataStore.SchemaVersion, 0));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
