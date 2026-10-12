using System.Globalization;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests;

/// <summary>The startup data check (UPD-9): what a pilot is offered at launch, and how it reads.</summary>
public sealed class StartupDataCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-startup-" + Guid.NewGuid().ToString("N"));

    public StartupDataCheckTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static StartupDataNeeds Find(bool enabled = true, string? simulator = "FS2020", string[]? navBuilt = null,
        bool locationAvailable = true, bool readyAtNextStart = false) =>
        StartupDataCheck.Find(enabled, simulator, sim => (navBuilt ?? new[] { "FS2020", "FS2024" }).Contains(sim),
            () => readyAtNextStart, () => locationAvailable);

    [Fact]
    public void Nothing_missing_offers_nothing()
    {
        Assert.False(Find().Any);
    }

    [Fact]
    public void A_new_install_is_offered_both()
    {
        Assert.Equal(new StartupDataNeeds(true, true), Find(navBuilt: [], locationAvailable: false));
    }

    [Fact]
    public void Only_what_is_missing_is_offered()
    {
        Assert.Equal(new StartupDataNeeds(false, true), Find(locationAvailable: false));
        Assert.Equal(new StartupDataNeeds(true, false), Find(navBuilt: []));
    }

    [Fact]
    public void A_database_for_the_other_simulator_does_not_count()
    {
        // Connecting switches the setting to the running simulator whether or not its database
        // exists, and every feature opens only that one.
        Assert.True(Find(simulator: "FS2024", navBuilt: ["FS2020"]).NavigationDatabase);
        Assert.True(Find(simulator: "FS2020", navBuilt: ["FS2024"]).NavigationDatabase);
        Assert.False(Find(simulator: "FS2024", navBuilt: ["FS2024"]).NavigationDatabase);
    }

    [Fact]
    public void With_no_simulator_saved_the_FS2020_database_counts()
    {
        // DatabaseSelector's own default.
        Assert.False(Find(simulator: null, navBuilt: ["FS2020"]).NavigationDatabase);
        Assert.True(Find(simulator: null, navBuilt: ["FS2024"]).NavigationDatabase);
    }

    [Fact]
    public void A_build_waiting_for_the_next_start_is_not_offered()
    {
        // The startup sweep promotes it; a download now would fetch 400 MB only to delete it.
        Assert.False(Find(locationAvailable: false, readyAtNextStart: true).Any);
    }

    [Fact]
    public void A_build_promoted_between_the_two_reads_is_not_offered()
    {
        // The sweep moves .ready onto the live file right after the first read: that read sees the
        // files before the move, the second after it.
        bool promoted = false;
        bool Ready() { bool before = !promoted; promoted = true; return before; }
        bool Available() { bool after = promoted; promoted = true; return after; }

        Assert.False(StartupDataCheck.Find(true, "FS2020", _ => true, Ready, Available).LocationData);
    }

    [Fact]
    public void With_the_setting_off_nothing_is_offered_or_probed()
    {
        bool probed = false;
        var needs = StartupDataCheck.Find(false, "FS2020",
            _ => probed = true, () => probed = true, () => probed = true);

        Assert.False(needs.Any);
        Assert.False(probed);
    }

    [Fact]
    public void The_text_lists_only_what_is_missing_and_where_to_turn_the_check_off()
    {
        string text = StartupDataCheck.Describe(new StartupDataNeeds(false, true), "Not downloaded.");
        Assert.Equal(
            "Some data MSFS Blind Assist needs is not set up yet.\r\n" +
            "Location data: Not downloaded.\r\n" +
            "To stop this check, turn off \"Check for missing data when the app starts\" in Settings, Updates tab.",
            text);
    }

    [Fact]
    public void The_navigation_database_line_names_the_menu_item_that_builds_it()
    {
        // MainForm's File menu item and the window it opens are both "Nav Database".
        Assert.Contains(
            "\r\nNavigation database: not built. Taxi guidance, teleport, ILS guidance and flight plans need it. " +
            "Build it in the File menu, Nav Database; it takes 2 to 5 minutes, and for MSFS 2024 the simulator must be running at the main menu.\r\n",
            StartupDataCheck.Describe(new StartupDataNeeds(true, false), ""));
    }

    [Fact]
    public void A_running_job_says_it_outlives_the_window()
    {
        Assert.Contains(
            "\r\nLocation data: Building location data: 40 percent\r\nIt keeps running when this window closes, and its progress is spoken.\r\n",
            StartupDataCheck.Describe(new StartupDataNeeds(true, true), "Building location data: 40 percent", locationRunning: true));
    }

    [Theory]
    [InlineData("Idle", false, "&Download location data", true)]
    [InlineData("Idle", true, "&Update location data", true)]
    [InlineData("Downloading", false, "Downloading location data", false)]
    [InlineData("Building", true, "Building location data", false)]
    [InlineData("Ready", true, "Location data ready", false)]
    [InlineData("Failed", false, "&Download location data", true)]
    [InlineData("Cancelled", true, "&Update location data", true)]
    public void The_download_button_says_what_the_job_does_and_starts_nothing_while_it_runs_or_once_ready(
        string phase, bool hasData, string text, bool pressStarts)
    {
        // Never dimmed, so it keeps focus; named for what the job does, so a press on a slow
        // connection visibly did something. After a failure or cancel a press retries.
        Assert.Equal((text, pressStarts), StartupDataCheck.DownloadButton(Enum.Parse<DownloadJobPhase>(phase), hasData));
    }

    private LocationDataStore Store() => new(Path.Combine(_dir, "location-data.sqlite"));

    private static string[] Buttons(StartupDataNeeds needs, LocationDataDownloader downloader)
    {
        using var dialog = new StartupDataDialog(needs, downloader);
        return Descendants(dialog).OfType<System.Windows.Forms.Button>().Select(b => b.Text.Replace("&", "")).ToArray();
    }

    private static string Description(StartupDataNeeds needs, LocationDataDownloader downloader)
    {
        using var dialog = new StartupDataDialog(needs, downloader);
        return Descendants(dialog).OfType<System.Windows.Forms.TextBox>().Single().Text;
    }

    [Fact]
    public void The_dialog_has_a_button_only_for_what_is_missing()
    {
        using var downloader = new LocationDataDownloader(Store());

        Assert.Equal(new[] { "Download location data", "Build navigation database", "Not now" }, Buttons(new(true, true), downloader));
        Assert.Equal(new[] { "Download location data", "Not now" }, Buttons(new(false, true), downloader));
        Assert.Equal(new[] { "Build navigation database", "Not now" }, Buttons(new(true, false), downloader));
    }

    [Fact]
    public void Data_built_for_an_older_version_is_offered_as_an_update_as_the_GeoNames_tab_names_it()
    {
        var store = Store();
        using (var c = new SqliteConnection(LocationDataStore.ConnectionString(store.DatabasePath, readOnly: false)))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT); "
                + "INSERT INTO meta VALUES ('schema_version', '0'), ('complete', '1');";
            cmd.ExecuteNonQuery();
        }
        using var downloader = new LocationDataDownloader(store);

        Assert.Equal(new[] { "Update location data", "Not now" }, Buttons(new(false, true), downloader));
        Assert.Contains("\r\nLocation data: This version of MSFS Blind Assist needs newer location data. Press Update location data.\r\n",
            Description(new(false, true), downloader));
    }

    [Fact]
    public void The_location_line_is_the_job_s_own_status_with_its_disk_space_need()
    {
        using var downloader = new LocationDataDownloader(Store());

        Assert.Contains("\r\nLocation data: " + downloader.IdleStatus().Text + "\r\n", Description(new(true, true), downloader));
        Assert.Contains("of free disk space", downloader.IdleStatus().Text);
    }

    [Fact]
    public void A_kept_partial_download_is_offered_as_a_resume()
    {
        var store = Store();
        Directory.CreateDirectory(store.DownloadFolder);
        File.WriteAllBytes(Path.Combine(store.DownloadFolder, "allCountries.zip.part"), new byte[1024 * 1024]);
        using var downloader = new LocationDataDownloader(store);

        Assert.Contains("\r\nLocation data: Not downloaded yet. Partial download kept: ", Description(new(false, true), downloader));
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
    public void The_dialog_text_keeps_its_invariant_digits(string culture)
    {
        using var downloader = new LocationDataDownloader(Store());
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string invariant = Description(new(true, true), downloader);
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.Equal(invariant, Description(new(true, true), downloader));
            Assert.Contains("About 442 megabytes to download", invariant);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
