using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>The startup data check (UPD-9): what a pilot is offered at launch, and how it reads.</summary>
public class StartupDataCheckTests
{
    private static StartupDataNeeds Find(bool enabled = true, bool navBuilt = true, bool locationAvailable = true,
        bool locationNeedsUpdate = false, bool readyAtNextStart = false) =>
        StartupDataCheck.Find(enabled, navBuilt, locationAvailable, locationNeedsUpdate, readyAtNextStart);

    [Fact]
    public void Nothing_missing_offers_nothing()
    {
        Assert.False(Find().Any);
    }

    [Fact]
    public void A_new_install_is_offered_both()
    {
        Assert.Equal(new StartupDataNeeds(true, true, false), Find(navBuilt: false, locationAvailable: false));
    }

    [Fact]
    public void Only_what_is_missing_is_offered()
    {
        Assert.Equal(new StartupDataNeeds(false, true, false), Find(locationAvailable: false));
        Assert.Equal(new StartupDataNeeds(true, false, false), Find(navBuilt: false));
    }

    [Fact]
    public void Location_data_built_for_an_older_version_is_offered_as_an_update()
    {
        Assert.Equal(new StartupDataNeeds(false, true, true), Find(locationAvailable: false, locationNeedsUpdate: true));
    }

    [Fact]
    public void A_build_waiting_for_the_next_start_is_not_offered()
    {
        // The startup sweep promotes it; a download now would fetch 400 MB only to delete it.
        Assert.False(Find(locationAvailable: false, readyAtNextStart: true).Any);
    }

    [Fact]
    public void With_the_setting_off_nothing_is_offered()
    {
        Assert.False(Find(enabled: false, navBuilt: false, locationAvailable: false).Any);
    }

    [Fact]
    public void The_text_lists_only_what_is_missing_and_where_to_turn_the_check_off()
    {
        string text = StartupDataCheck.Describe(new StartupDataNeeds(false, true, false));
        Assert.Equal(
            "Some data MSFS Blind Assist needs is not set up yet.\r\n" +
            "Location data: not downloaded. Output C and Location Info need it. About 442 megabytes to download, in the background.\r\n" +
            "To stop this check, turn off \"Check for missing data when the app starts\" in Settings, Updates tab.",
            text);
    }

    [Fact]
    public void The_navigation_database_line_says_how_it_is_built()
    {
        Assert.Contains(
            "\r\nNavigation database: not built. Taxi guidance, teleport, ILS guidance and flight plans need it. " +
            "Build it in Database Settings; it takes 2 to 5 minutes, and for MSFS 2024 the simulator must be running at the main menu.\r\n",
            StartupDataCheck.Describe(new StartupDataNeeds(true, false, false)));
    }

    [Fact]
    public void Location_data_for_an_older_version_says_it_needs_an_update()
    {
        Assert.Contains("\r\nLocation data: needs an update for this version of MSFS Blind Assist. Output C and Location Info need it. ",
            StartupDataCheck.Describe(new StartupDataNeeds(false, true, true)));
    }

    [Fact]
    public void A_download_started_from_the_dialog_reads_as_downloading()
    {
        Assert.Contains("\r\nLocation data: downloading. Its progress is spoken, and it keeps running when this window closes.\r\n",
            StartupDataCheck.Describe(new StartupDataNeeds(true, true, false), locationDownloading: true));
    }

    [Fact]
    public void The_dialog_has_a_button_only_for_what_is_missing()
    {
        static string[] Buttons(StartupDataNeeds needs)
        {
            using var dialog = new MSFSBlindAssist.Forms.StartupDataDialog(needs, () => { });
            return Descendants(dialog).OfType<System.Windows.Forms.Button>().Select(b => b.Text.Replace("&", "")).ToArray();
        }

        Assert.Equal(new[] { "Download location data", "Build navigation database", "Not now" }, Buttons(new(true, true, false)));
        Assert.Equal(new[] { "Update location data", "Not now" }, Buttons(new(false, true, true)));
        Assert.Equal(new[] { "Build navigation database", "Not now" }, Buttons(new(true, false, false)));
    }

    private static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control parent)
    {
        foreach (System.Windows.Forms.Control child in parent.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_download_size_keeps_its_invariant_digits(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Contains("About 442 megabytes to download", StartupDataCheck.Describe(new StartupDataNeeds(false, true, false)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
