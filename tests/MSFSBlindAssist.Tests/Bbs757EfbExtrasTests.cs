using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Plan 18, Task 6: what the app adds to the tablet's pages (<see cref="Bbs757EfbExtras"/>), as pure decisions: which pressed
/// control starts the load watch or the nav data watch, which parts of the load each starts, when the Navigraph sign-in is
/// offered, and what the nav data watch says when its 20 seconds are up. The words are the reader's (sentence case by
/// <see cref="Bbs757EfbVocabulary.Pretty"/>), as the 2026-10-29 captures give them. Everything spoken is checked under en-US,
/// de-DE and sv-SE (CI runs under en-US).
/// </summary>
public class Bbs757EfbExtrasTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private const string AcarsPage = "Navigation, ACARS dashboard";
    private const string FuelPage = "Aircraft, Fuel";
    private const string AiracPage = "Settings, AIRAC data";

    private static Bbs757EfbItem Control(string text, string key = "btn:|CONTROL@10,10") =>
        new(Bbs757EfbKind.Button, text, new Rectangle(100, 200, 180, 40), new Point(190, 220), true, key);

    // ---- which control starts what ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Fetch SIMBRIEF ofp")]      // as the brief spells the reader's words
    [InlineData("Refresh SIMBRIEF ofp")]
    [InlineData("Fetch SIMBRIEF OFP")]      // as Pretty leaves OFP, an acronym
    [InlineData("Refresh SIMBRIEF OFP")]
    [InlineData("FETCH SIMBRIEF OFP")]
    [InlineData("refresh simbrief ofp")]
    [InlineData("  Fetch   SIMBRIEF OFP ")]  // spaces do not matter
    public void Fetch_and_refresh_on_the_ACARS_dashboard_are_the_fetch_hook(string words)
    {
        Assert.Equal(Bbs757EfbHook.Fetch, Bbs757EfbExtras.HookFor(AcarsPage, Control(words)));
    }

    [Theory]
    [InlineData("Import from SIMBRIEF")]
    [InlineData("IMPORT FROM SIMBRIEF")]
    [InlineData("import from simbrief")]
    public void Import_from_SIMBRIEF_on_the_fuel_page_is_the_import_hook(string words)
    {
        Assert.Equal(Bbs757EfbHook.Import, Bbs757EfbExtras.HookFor(FuelPage, Control(words)));
    }

    [Theory]
    [InlineData(FuelPage)]
    [InlineData(AcarsPage)]
    [InlineData(AiracPage)]
    [InlineData("Home")]
    [InlineData("Unknown page")]
    public void Start_boarding_in_the_top_bar_is_the_boarding_hook_on_every_page(string page)
    {
        Assert.Equal(Bbs757EfbHook.StartBoarding,
            Bbs757EfbExtras.HookFor(page, Control("Start boarding", Bbs757EfbTopBar.BoardingKey)));
        Assert.Equal(Bbs757EfbHook.StartBoarding,
            Bbs757EfbExtras.HookFor(page, Control("START BOARDING", Bbs757EfbTopBar.BoardingKey)));
    }

    [Theory]
    [InlineData("Update", Bbs757EfbHook.NavDataUpdate)]
    [InlineData("UPDATE", Bbs757EfbHook.NavDataUpdate)]
    [InlineData("Remove", Bbs757EfbHook.NavDataRemove)]
    [InlineData("REMOVE", Bbs757EfbHook.NavDataRemove)]
    public void Update_and_remove_on_the_AIRAC_page_are_the_nav_data_hooks_as_the_tablet_labels_them(string words, Bbs757EfbHook expected)
    {
        // The spot is BlueBird's UPDATE while a newer cycle is out and its REMOVE otherwise: the hook follows the word.
        Assert.Equal(expected, Bbs757EfbExtras.HookFor(AiracPage, Control(words)));
    }

    [Theory]
    // The reader may name a box by a short label just above it ("Navigation data: Update") or, for a generic action, by its row
    // ("Update, Navigation data"); the key keeps the box's own words between | and @, which never take a caption.
    [InlineData(AiracPage, "Navigation data: Update", "btn:NAVIGATION DATA|UPDATE@64,16", Bbs757EfbHook.NavDataUpdate)]
    [InlineData(AiracPage, "Update, Navigation data", "btn:NAVIGATION DATA|UPDATE@64,16", Bbs757EfbHook.NavDataUpdate)]
    [InlineData(AiracPage, "Navigation data: Remove", "btn:NAVIGATION DATA|REMOVE@64,16", Bbs757EfbHook.NavDataRemove)]
    [InlineData(AcarsPage, "Flight plan: Fetch SIMBRIEF OFP", "btn:FLIGHT PLAN|FETCH SIMBRIEF OFP@42,40", Bbs757EfbHook.Fetch)]
    [InlineData(AcarsPage, "Flight plan: Refresh SIMBRIEF OFP", "btn:FLIGHT PLAN|REFRESH SIMBRIEF OFP@42,40", Bbs757EfbHook.Fetch)]
    [InlineData(FuelPage, "Total fuel: Import from SIMBRIEF", "btn:TOTAL FUEL|IMPORT FROM SIMBRIEF@18,46", Bbs757EfbHook.Import)]   // bright/21, dimmed
    // The key's words are judged as the text is: other words, or the right words on another page, hook nothing.
    [InlineData(AiracPage, "Navigation data: Updates", "btn:NAVIGATION DATA|UPDATES@64,16", Bbs757EfbHook.None)]
    [InlineData(AcarsPage, "Navigation data: Update", "btn:NAVIGATION DATA|UPDATE@64,16", Bbs757EfbHook.None)]
    [InlineData(AcarsPage, "Flight plan: Remove FP", "btn:FLIGHT PLAN|REMOVE FP@29,4", Bbs757EfbHook.None)]
    public void A_captioned_control_hooks_by_the_words_of_its_key(string page, string text, string key, Bbs757EfbHook expected)
    {
        Assert.Equal(expected, Bbs757EfbExtras.HookFor(page, Control(text, key)));
    }

    [Theory]
    [InlineData("Navigation, acars dashboard", "Fetch SIMBRIEF OFP", Bbs757EfbHook.Fetch)]   // the page name is matched with case ignored
    [InlineData("aircraft, fuel", "Import from SIMBRIEF", Bbs757EfbHook.Import)]
    [InlineData("SETTINGS, AIRAC DATA", "Update", Bbs757EfbHook.NavDataUpdate)]
    public void The_page_name_is_matched_with_case_ignored(string page, string words, Bbs757EfbHook expected)
    {
        Assert.Equal(expected, Bbs757EfbExtras.HookFor(page, Control(words)));
    }

    [Theory]
    // The same words on another page.
    [InlineData(FuelPage, "Fetch SIMBRIEF OFP")]
    [InlineData(FuelPage, "Refresh SIMBRIEF OFP")]
    [InlineData(AiracPage, "Fetch SIMBRIEF OFP")]
    [InlineData("Home", "Fetch SIMBRIEF OFP")]
    [InlineData(AcarsPage, "Import from SIMBRIEF")]
    [InlineData("Settings, General", "Import from SIMBRIEF")]
    [InlineData("Settings, Callouts", "Update")]
    [InlineData("Aircraft, Ground services", "Remove")]
    [InlineData("Aircraft, Ground services", "Update")]
    [InlineData("Home", "Remove")]
    [InlineData(AcarsPage, "Update")]
    // Other controls of those pages.
    [InlineData(AcarsPage, "Remove FP")]           // removes the plan at once: no hook
    [InlineData(AcarsPage, "Remove all")]
    [InlineData(AcarsPage, "Fetch SIMBRIEF OFP now")]
    [InlineData(AcarsPage, "")]
    [InlineData(FuelPage, "Import from SIMBRIEF flight plan")]
    [InlineData(FuelPage, "Start boarding")]       // without the top bar's key, the fuel page's own control is no hook
    [InlineData(AiracPage, "Logout")]
    [InlineData(AiracPage, "Update nav data")]
    [InlineData(AiracPage, "Remove FP")]
    [InlineData(AiracPage, "Sign in to Navigraph")]
    public void Other_words_or_another_page_hook_nothing(string page, string words)
    {
        Assert.Equal(Bbs757EfbHook.None, Bbs757EfbExtras.HookFor(page, Control(words)));
    }

    [Theory]
    [InlineData("Boarding… 10 min")]        // while boarding runs, the control is text
    [InlineData("Boarding")]
    [InlineData("Notifications: 3")]
    [InlineData("DEP -00:46")]
    public void The_top_bar_hooks_only_the_start_of_boarding(string words)
    {
        Assert.Equal(Bbs757EfbHook.None, Bbs757EfbExtras.HookFor(FuelPage, Control(words, Bbs757EfbTopBar.BoardingKey)));
        Assert.Equal(Bbs757EfbHook.None, Bbs757EfbExtras.HookFor(FuelPage, Control(words, Bbs757EfbTopBar.NotificationsKey)));
    }

    [Fact]
    public void The_hooks_follow_the_words_the_reader_makes_of_the_tablets_capitals()
    {
        // Pretty is what the reader does to an all-capitals read; the hooks must hold for its output, whatever it does to OFP.
        string P(string capitals) => Bbs757EfbVocabulary.Pretty(capitals);

        Assert.Equal(Bbs757EfbHook.Fetch, Bbs757EfbExtras.HookFor(AcarsPage, Control(P("FETCH SIMBRIEF OFP"))));
        Assert.Equal(Bbs757EfbHook.Fetch, Bbs757EfbExtras.HookFor(AcarsPage, Control(P("REFRESH SIMBRIEF OFP"))));
        Assert.Equal(Bbs757EfbHook.None, Bbs757EfbExtras.HookFor(AcarsPage, Control(P("REMOVE FP"))));
        Assert.Equal(Bbs757EfbHook.Import, Bbs757EfbExtras.HookFor(FuelPage, Control(P("IMPORT FROM SIMBRIEF"))));
        Assert.Equal(Bbs757EfbHook.StartBoarding, Bbs757EfbExtras.HookFor(FuelPage, Control(P("START BOARDING"), Bbs757EfbTopBar.BoardingKey)));
        Assert.Equal(Bbs757EfbHook.NavDataUpdate, Bbs757EfbExtras.HookFor(AiracPage, Control(P("UPDATE"))));
        Assert.Equal(Bbs757EfbHook.NavDataRemove, Bbs757EfbExtras.HookFor(AiracPage, Control(P("REMOVE"))));
    }

    // ---- against the captures ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_captured_fuel_page_hooks_import_and_start_boarding()
    {
        // bright/20: after FETCH SIMBRIEF OFP with automatic boarding off. START BOARDING sits in the top bar, IMPORT FROM
        // SIMBRIEF on the page; no other control of the page hooks anything.
        var m = Bbs757EfbScreenTests.Load("bright", "20-fuel-start-boarding");

        var import = m.Items.Single(i => i.Text == "Import from SIMBRIEF");
        var start = m.TopBar.Single(i => i.Key == Bbs757EfbTopBar.BoardingKey);
        Assert.Equal("Aircraft, Fuel", m.PageName);
        Assert.Equal(Bbs757EfbHook.Import, Bbs757EfbExtras.HookFor(m.PageName, import));
        Assert.Equal(Bbs757EfbHook.StartBoarding, Bbs757EfbExtras.HookFor(m.PageName, start));
        Assert.Equal(new[] { import.Key, start.Key },
            m.Menu.Concat(m.Items).Concat(m.TopBar).Where(i => Bbs757EfbExtras.HookFor(m.PageName, i) != Bbs757EfbHook.None).Select(i => i.Key));
    }

    [Fact]
    public void The_captured_boarding_in_progress_is_no_hook()
    {
        // bright/12: the top bar reads "Boarding… 10 min" as text where START BOARDING was.
        var m = Bbs757EfbScreenTests.Load("bright", "12-aircraft-fuel-boarding");

        Assert.Contains(m.TopBar, i => i.Key == Bbs757EfbTopBar.BoardingKey && i.Kind == Bbs757EfbKind.Text);
        Assert.All(m.Menu.Concat(m.Items).Concat(m.TopBar), i => Assert.Equal(Bbs757EfbHook.None, Bbs757EfbExtras.HookFor(m.PageName, i)));
    }

    [Fact]
    public void The_captured_airac_page_hooks_its_update_and_no_logout()
    {
        // bright/55: UPDATE and the two LOGOUTs are buttons named by their rows ("Update, Latest cycle"); the hook is judged by the
        // words of the key, the box's own, never the row's label.
        var m = Bbs757EfbScreenTests.Load("bright", "55-settings-airac-data");

        var update = m.Items.Single(i => i.Text == "Update, Latest cycle");
        Assert.Equal(Bbs757EfbHook.NavDataUpdate, Bbs757EfbExtras.HookFor(m.PageName, update));
        var logouts = m.Items.Where(i => i.Text.StartsWith("Logout, ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, logouts.Count);
        Assert.All(logouts, logout => Assert.Equal(Bbs757EfbHook.None, Bbs757EfbExtras.HookFor(m.PageName, logout)));
        Assert.Equal(new[] { update.Key },
            m.Menu.Concat(m.Items).Concat(m.TopBar).Where(i => Bbs757EfbExtras.HookFor(m.PageName, i) != Bbs757EfbHook.None).Select(i => i.Key));
    }

    [Fact]
    public void The_captured_airac_pages_remove_hooks_the_nav_data_removal()
    {
        // The same box reading REMOVE, as BlueBird shows it when no newer cycle waits.
        var m = Bbs757EfbScreenTests.LoadVariant("bright", "55-settings-airac-data", "remove");

        var remove = m.Items.Single(i => i.Text == "Remove, Latest cycle");
        Assert.Equal(Bbs757EfbHook.NavDataRemove, Bbs757EfbExtras.HookFor(m.PageName, remove));
        Assert.Equal(new[] { remove.Key },
            m.Menu.Concat(m.Items).Concat(m.TopBar).Where(i => Bbs757EfbExtras.HookFor(m.PageName, i) != Bbs757EfbHook.None).Select(i => i.Key));
    }

    [Theory]
    [InlineData("bright", "10-aircraft-presets")]
    [InlineData("bright", "11-aircraft-fuel")]
    [InlineData("bright", "13-aircraft-ground-services")]
    [InlineData("bright", "14-aircraft-cabin-doors")]
    [InlineData("bright", "16-aircraft-failures")]
    [InlineData("bright", "18-aircraft-ground-services-notification")]
    [InlineData("bright", "19-notification-list")]
    [InlineData("bright", "40-checklist")]
    [InlineData("bright", "51-settings-aircraft")]
    [InlineData("bright", "53-settings-callouts")]
    [InlineData("dim", "01-home")]
    [InlineData("dim", "13-aircraft-ground-services")]
    [InlineData("dim", "15-aircraft-maintenance")]
    [InlineData("dim", "50-settings-general")]
    public void No_control_of_the_other_captured_pages_hooks_anything(string set, string name)
    {
        var m = Bbs757EfbScreenTests.Load(set, name);

        Assert.All(m.Menu.Concat(m.Items).Concat(m.TopBar), i =>
            Assert.True(Bbs757EfbExtras.HookFor(m.PageName, i) == Bbs757EfbHook.None, $"{m.PageName}: '{i.Text}' ({i.Key}) hooks"));
    }

    // ---- which parts of the load a hook watches ---------------------------------------------------------------------------

    [Theory]
    [InlineData(Bbs757EfbHook.Fetch, true, Bbs757LoadParts.Both)]      // automatic boarding: the fetch alone fuels and boards
    [InlineData(Bbs757EfbHook.Fetch, false, Bbs757LoadParts.None)]
    [InlineData(Bbs757EfbHook.Fetch, null, Bbs757LoadParts.None)]      // an unread setting watches nothing
    [InlineData(Bbs757EfbHook.Import, true, Bbs757LoadParts.Fuel)]
    [InlineData(Bbs757EfbHook.Import, false, Bbs757LoadParts.Fuel)]
    [InlineData(Bbs757EfbHook.Import, null, Bbs757LoadParts.Fuel)]
    [InlineData(Bbs757EfbHook.StartBoarding, true, Bbs757LoadParts.Boarding)]
    [InlineData(Bbs757EfbHook.StartBoarding, false, Bbs757LoadParts.Boarding)]
    [InlineData(Bbs757EfbHook.StartBoarding, null, Bbs757LoadParts.Boarding)]
    [InlineData(Bbs757EfbHook.None, true, Bbs757LoadParts.None)]
    [InlineData(Bbs757EfbHook.NavDataUpdate, true, Bbs757LoadParts.None)]
    [InlineData(Bbs757EfbHook.NavDataRemove, true, Bbs757LoadParts.None)]
    [InlineData(Bbs757EfbHook.NavDataUpdate, null, Bbs757LoadParts.None)]
    public void Each_hook_starts_its_parts_of_the_load_watch(Bbs757EfbHook hook, bool? automaticLoading, Bbs757LoadParts expected)
    {
        Assert.Equal(expected, Bbs757EfbExtras.PartsFor(hook, automaticLoading));
    }

    // ---- the Navigraph sign-in ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(AiracPage, false, true)]      // that page, signed out
    [InlineData(AiracPage, true, false)]      // signed in
    [InlineData(AiracPage, null, false)]      // not read: the button is not offered on a guess
    [InlineData("settings, airac data", false, true)]
    [InlineData("Settings, General", false, false)]
    [InlineData("Settings, Aircraft", false, false)]
    [InlineData(AcarsPage, false, false)]
    [InlineData("Home", false, false)]
    [InlineData("Unknown page", false, false)]
    public void The_sign_in_is_offered_only_on_the_AIRAC_page_and_only_when_signed_out(string page, bool? signedIn, bool expected)
    {
        Assert.Equal(expected, Bbs757EfbExtras.OffersSignIn(page, signedIn));
    }

    // ---- what is said ---------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_two_texts_the_window_shows_are_verbatim_under_any_culture(string culture)
    {
        var texts = TestCulture.Run(culture, () => new[] { Bbs757EfbExtras.SignInText, Bbs757EfbExtras.PlanUnreadableText });

        Assert.Equal(new[]
        {
            "Sign in to Navigraph",
            "The SimBrief plan could not be read, so the app won't say when fueling and boarding are complete.",
        }, texts);
    }

    [Fact]
    public void An_unreadable_plan_says_why_first_when_the_reason_is_known()
    {
        const string plain = "The SimBrief plan could not be read, so the app won't say when fueling and boarding are complete.";
        string unreachable = MSFSBlindAssist.Services.SimBriefService.OfpFetchErrorText(
            MSFSBlindAssist.Services.SimBriefService.OfpFetchFailure.Unreachable, byId: true);

        Assert.Equal(plain, Bbs757EfbExtras.PlanUnreadable(null));
        Assert.Equal(plain, Bbs757EfbExtras.PlanUnreadable("  "));
        Assert.Equal("No SimBrief ID is set in the 757's EFB settings. " + plain,
            Bbs757EfbExtras.PlanUnreadable(Bbs757EfbExtras.NoSimBriefIdText));
        Assert.Equal("SimBrief could not be reached. Check your internet connection and try again. " + plain,
            Bbs757EfbExtras.PlanUnreadable(unreachable));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_nav_data_watch_says_each_of_its_five_texts_under_any_culture(string culture)
    {
        var said = TestCulture.Run(culture, () => new[]
        {
            // UPDATE pressed, a newer cycle appeared.
            Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataUpdate, 2609, 2610, true),
            // REMOVE pressed, the file went.
            Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataRemove, 2610, null, false),
            // UPDATE pressed, but the spot was BlueBird's REMOVE: the file went.
            Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataUpdate, 2610, null, false),
            // UPDATE pressed, nothing changed.
            Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataUpdate, 2609, 2609, true),
            // REMOVE pressed, the file stayed.
            Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataRemove, 2610, 2610, true),
        });

        Assert.Equal(new[]
        {
            "Nav data cycle 2610 installed. Reload the flight to use it.",
            "BlueBird removed the installed nav data.",
            "BlueBird removed the installed nav data instead of updating it. With an active Navigraph subscription, reload the flight and update again.",
            "The update did not finish. Try again after reloading the flight.",
            "BlueBird did not remove the nav data.",
        }, said);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_installed_cycle_is_said_as_a_plain_number_under_any_culture(string culture)
    {
        // 2610 is where a thousands separator would show (2.610, 2 610); the cycle is a name, not an amount.
        string said = TestCulture.Run(culture, () => Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataUpdate, null, 2610, true));

        Assert.Equal("Nav data cycle 2610 installed. Reload the flight to use it.", said);
    }

    [Theory]
    [InlineData(null, 2610)]       // nothing was installed
    [InlineData(2603, 2610)]
    [InlineData(2609, 2610)]
    public void A_newer_cycle_in_the_file_is_an_installed_update(int? before, int after)
    {
        Assert.Equal("Nav data cycle 2610 installed. Reload the flight to use it.",
            Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataUpdate, before, after, fileThere: true));
    }

    [Theory]
    [InlineData(2609, 2609, true)]       // unchanged
    [InlineData(2609, 2603, true)]       // an older cycle is not an update
    [InlineData(2609, null, true)]       // the file is there but unreadable
    [InlineData(null, null, true)]       // nothing installed, the file unreadable
    [InlineData(null, null, false)]      // nothing installed, nothing came
    public void An_update_that_changed_nothing_did_not_finish(int? before, int? after, bool fileThere)
    {
        Assert.Equal("The update did not finish. Try again after reloading the flight.",
            Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataUpdate, before, after, fileThere));
    }

    [Fact]
    public void A_removal_needs_something_that_was_installed_and_is_gone()
    {
        // Nothing installed before: nothing to remove, whichever button was pressed.
        Assert.Equal("BlueBird did not remove the nav data.",
            Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataRemove, null, null, fileThere: false));
        // The file there but unreadable after: it was not removed.
        Assert.Equal("BlueBird did not remove the nav data.",
            Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataRemove, 2610, null, fileThere: true));
        Assert.Equal("BlueBird removed the installed nav data.",
            Bbs757EfbExtras.NavDataOutcome(Bbs757EfbHook.NavDataRemove, 2610, null, fileThere: false));
    }

    [Theory]
    [InlineData(Bbs757EfbHook.None)]
    [InlineData(Bbs757EfbHook.Fetch)]
    [InlineData(Bbs757EfbHook.Import)]
    [InlineData(Bbs757EfbHook.StartBoarding)]
    public void A_hook_that_is_no_nav_data_press_has_nothing_to_say(Bbs757EfbHook hook)
    {
        Assert.Equal("", Bbs757EfbExtras.NavDataOutcome(hook, 2609, 2610, true));
        Assert.Equal("", Bbs757EfbExtras.NavDataOutcome(hook, 2610, null, false));
    }

    // ---- the nav data watch (plan 18, Task 8) -----------------------------------------------------------------------------------

    /// <summary>BlueBird's cycle file as the watch reads it, second by second on a clock the watch's waits move: what it holds and
    /// whether it is there in the first second, the second, and so on (the last reading from then on).</summary>
    private sealed class CycleFile
    {
        public (int? Cycle, bool There)[] Reads = { (2609, true) };
        public long Now;
        public readonly List<int> Waits = new();

        private (int? Cycle, bool There) Reading => Reads[Math.Clamp((int)(Now / 1000) - 1, 0, Reads.Length - 1)];

        public int? ReadCycle() => Reading.Cycle;

        public bool There() => Reading.There;

        public Task Delay(int ms, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Waits.Add(ms);
            Now += ms;
            return Task.CompletedTask;
        }

        public Task<string> Watch(Bbs757EfbHook hook, int? before) =>
            Bbs757EfbExtras.WatchNavDataAsync(hook, before, ReadCycle, There, Delay, () => Now, CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public async Task An_update_is_said_as_soon_as_the_file_holds_the_new_cycle(string culture)
    {
        var file = new CycleFile { Reads = new (int?, bool)[] { (2609, true), (2609, true), (2610, true) } };

        string said = await TestCulture.Run(culture, () => file.Watch(Bbs757EfbHook.NavDataUpdate, 2609));

        Assert.Equal("Nav data cycle 2610 installed. Reload the flight to use it.", said);
        Assert.Equal(new[] { 1000, 1000, 1000 }, file.Waits);   // read every second, ended at the third
    }

    [Fact]
    public async Task An_update_that_never_comes_is_judged_after_twenty_seconds()
    {
        // BlueBird may rewrite the file during an update, so a missing file is judged only once the whole window has passed.
        var file = new CycleFile { Reads = new (int?, bool)[] { (2609, true), (null, false), (2609, true) } };

        string said = await file.Watch(Bbs757EfbHook.NavDataUpdate, 2609);

        Assert.Equal("The update did not finish. Try again after reloading the flight.", said);
        Assert.Equal(Bbs757EfbExtras.NavDataWatchMs, file.Waits.Sum());
        Assert.All(file.Waits, w => Assert.Equal(Bbs757EfbExtras.NavDataPollMs, w));
    }

    [Fact]
    public async Task An_update_whose_file_is_gone_at_the_end_was_a_removal()
    {
        var file = new CycleFile { Reads = new (int?, bool)[] { (null, false) } };

        Assert.Equal("BlueBird removed the installed nav data instead of updating it. With an active Navigraph subscription, reload the flight and update again.",
            await file.Watch(Bbs757EfbHook.NavDataUpdate, 2609));
        Assert.Equal(20_000, file.Waits.Sum());
    }

    [Fact]
    public async Task A_removal_is_said_as_soon_as_the_file_is_gone()
    {
        var file = new CycleFile { Reads = new (int?, bool)[] { (2610, true), (null, false) } };

        Assert.Equal("BlueBird removed the installed nav data.", await file.Watch(Bbs757EfbHook.NavDataRemove, 2610));
        Assert.Equal(new[] { 1000, 1000 }, file.Waits);
    }

    [Fact]
    public async Task A_removal_whose_file_is_gone_at_the_first_poll_is_said()
    {
        // BlueBird deleted cycle.json at once: the cycle the press read before its tap (2610) says something was installed.
        var file = new CycleFile { Reads = new (int?, bool)[] { (null, false) } };

        Assert.Equal("BlueBird removed the installed nav data.", await file.Watch(Bbs757EfbHook.NavDataRemove, 2610));
        Assert.Equal(new[] { 1000 }, file.Waits);
    }

    [Fact]
    public async Task A_removal_that_never_comes_is_said_after_twenty_seconds()
    {
        var file = new CycleFile { Reads = new (int?, bool)[] { (2610, true) } };

        Assert.Equal("BlueBird did not remove the nav data.", await file.Watch(Bbs757EfbHook.NavDataRemove, 2610));
        Assert.Equal(20_000, file.Waits.Sum());
    }

    [Fact]
    public async Task A_hook_that_is_no_nav_data_press_watches_nothing()
    {
        var file = new CycleFile();

        Assert.Equal("", await file.Watch(Bbs757EfbHook.Fetch, 2609));
        Assert.Empty(file.Waits);
    }
}
