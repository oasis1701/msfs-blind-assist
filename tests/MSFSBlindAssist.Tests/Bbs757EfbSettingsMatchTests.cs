using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Settings rows the tablet reader parses (plan 18, Task 4), matched to the catalog of BlueBird's settings
/// (<see cref="Bbs757EfbSettingsMatch"/>): which catalog entry a row is, from the page's section and the row's label with
/// case, spaces and punctuation ignored; the choices a slider offers, in the tablet's order; the value BlueBird stores for
/// a switch's state or a choice's word; and the callouts that switching one on switches off. The rows come from the
/// captured pages (tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb, read by <see cref="Bbs757EfbScreenTests.Load"/>), so
/// the reader's own spelling is what is matched ("Automatic gpu/jetway" against the catalog's "Automatic GPU/jetway").
/// Pure: no files, no simulator.
/// </summary>
public class Bbs757EfbSettingsMatchTests
{
    private const string Egpws = "options.egpws_callouts.";
    private const string Pilot = "options.pilot_callouts.";
    private const string Cabin = "options.cabin_audio.";

    private static Bbs757Setting Setting(string path)
    {
        var setting = Bbs757EfbSettings.ByPath(path);
        Assert.True(setting is not null, $"The catalog has no setting '{path}'.");
        return setting!;
    }

    // A row as the reader makes one, for the cases a capture does not hold.
    private static Bbs757EfbItem Row(Bbs757EfbKind kind, string text, bool? on = null, string value = "") =>
        new(kind, text, Rectangle.Empty, Point.Empty, true, "row:" + text, On: on, Value: value);

    private static bool IsRow(Bbs757EfbItem item) =>
        item.Kind is Bbs757EfbKind.Toggle or Bbs757EfbKind.Slider or Bbs757EfbKind.Checkbox or Bbs757EfbKind.Row;

    // The tablet pages that hold settings and the catalog sections each shows. The ACARS section is no page: its Hoppie
    // ACARS and CPDLC network are rows of Settings > Aircraft, its two codes rows of Settings > AIRAC data.
    private static readonly (string Page, Bbs757SettingSection[] Sections)[] Pages =
    {
        ("Settings, General", new[] { Bbs757SettingSection.General }),
        ("Settings, Aircraft", new[] { Bbs757SettingSection.Aircraft, Bbs757SettingSection.Acars }),
        ("Settings, Livery options", new[] { Bbs757SettingSection.LiveryOptions }),
        ("Settings, Callouts", new[] { Bbs757SettingSection.Callouts }),
        ("Settings, Cabin audio", new[] { Bbs757SettingSection.CabinAudio }),
        ("Settings, AIRAC data", new[] { Bbs757SettingSection.AiracData, Bbs757SettingSection.Acars }),
        ("Aircraft, Failures", new[] { Bbs757SettingSection.Failures }),
    };

    // ---- the page's section ----------------------------------------------------------------------

    [Theory]
    [InlineData("Settings, General", Bbs757SettingSection.General)]
    [InlineData("Settings, Aircraft", Bbs757SettingSection.Aircraft)]
    [InlineData("Settings, Livery options", Bbs757SettingSection.LiveryOptions)]
    [InlineData("Settings, Callouts", Bbs757SettingSection.Callouts)]
    [InlineData("Settings, Cabin audio", Bbs757SettingSection.CabinAudio)]
    [InlineData("Settings, AIRAC data", Bbs757SettingSection.AiracData)]
    [InlineData("Aircraft, Failures", Bbs757SettingSection.Failures)]
    public void SectionFor_names_the_section_of_each_settings_page(string page, Bbs757SettingSection expected) =>
        Assert.Equal(expected, Bbs757EfbSettingsMatch.SectionFor(page));

    [Theory]
    [InlineData("Aircraft, Fuel")]
    [InlineData("Aircraft, Ground services")]
    [InlineData("Aircraft, Cockpit presets")]
    [InlineData("Navigation, ACARS dashboard")]
    [InlineData("Checklist, Before start")]
    [InlineData("Settings")]
    [InlineData("Home")]
    [InlineData("Unknown page")]
    [InlineData("")]
    public void SectionFor_is_null_for_a_page_that_holds_no_settings(string page) =>
        Assert.Null(Bbs757EfbSettingsMatch.SectionFor(page));

    [Fact]
    public void SectionFor_takes_the_page_names_the_reader_gives()
    {
        Assert.Equal(Bbs757SettingSection.Aircraft,
            Bbs757EfbSettingsMatch.SectionFor(Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft").PageName));
        Assert.Equal(Bbs757SettingSection.Callouts,
            Bbs757EfbSettingsMatch.SectionFor(Bbs757EfbScreenTests.Load("bright", "53-settings-callouts").PageName));
        Assert.Equal(Bbs757SettingSection.General,
            Bbs757EfbSettingsMatch.SectionFor(Bbs757EfbScreenTests.Load("dim", "50-settings-general").PageName));
        Assert.Equal(Bbs757SettingSection.Failures,
            Bbs757EfbSettingsMatch.SectionFor(Bbs757EfbScreenTests.Load("bright", "16-aircraft-failures").PageName));

        var fuel = Bbs757EfbScreenTests.Load("bright", "22-fuel-adjustment-complete");
        Assert.Equal("Aircraft, Fuel", fuel.PageName);
        Assert.Null(Bbs757EfbSettingsMatch.SectionFor(fuel.PageName));
        Assert.Null(Bbs757EfbSettingsMatch.SectionFor(Bbs757EfbScreenTests.Load("dim", "01-home").PageName));
    }

    // ---- the rows of the captured pages ------------------------------------------------------------

    // Every row the page shows that the catalog holds, matched, with its key in BlueBird's file; the page's headings and
    // notes are never taken for a setting.
    private static void AssertPage(string set, string name, string page, params (string Row, string Path)[] expected)
    {
        var model = Bbs757EfbScreenTests.Load(set, name);
        Assert.Equal(page, model.PageName);

        foreach (var (text, path) in expected)
        {
            var row = Assert.Single(model.Items, i => i.Text == text);
            Assert.True(IsRow(row), $"{page}: '{text}' is a {row.Kind}, not a row.");
            var setting = Bbs757EfbSettingsMatch.SettingFor(model.PageName, row);
            Assert.True(setting is not null, $"{page}: '{text}' matched no setting.");
            Assert.Equal(path, setting!.Path);
        }

        Assert.All(model.Items.Where(i => !IsRow(i)),
            i => Assert.Null(Bbs757EfbSettingsMatch.SettingFor(model.PageName, i)));
    }

    [Fact]
    public void The_Aircraft_page_rows_are_matched_with_their_keys() =>
        AssertPage("bright", "51-settings-aircraft", "Settings, Aircraft",
            ("Adaptive cockpit lighting", "automatic_cockpit_lighting"),
            ("TOD pause distance (NM)", "tod_pause_distance"),
            ("Nosewheel steering", "tiller_realistic"),
            ("Displays bootup speed", "displays_bootup_realistic"),
            ("Sync barometers", "baro_sync"),
            ("Initial cockpit door", "cockpit_door_initial_open"),
            ("Parking brake mode", "parking_brake_realistic"),
            ("IRS alignment time", "irs_align_time"),
            ("Boarding & load rate", "loading_speed"),
            ("Automatic boarding", "automatic_loading"),
            ("Automatic gpu/jetway", "auto_connect_ground_services_on_init"),
            ("Hoppie ACARS", "hoppie_user_enabled"),
            ("CPDLC network", "cpdlc_network"),
            ("Initial chocks in place", "auto_attach_chocks_on_init"),
            ("Dirty panel states", "dirty_panel_states_enabled"));

    [Fact]
    public void The_updated_Aircraft_page_rows_are_matched_with_their_keys() =>
        // BlueBird's 2026-10-31 update (captured 2026-11-01): three new rows, and the page scrolls; its lower rows are below.
        AssertPage("bright", "56-settings-aircraft-step-climbs", "Settings, Aircraft",
            ("Adaptive handling assist", "easy_flight_enabled"),
            ("Adaptive cockpit lighting", "automatic_cockpit_lighting"),
            ("TOD pause distance (NM)", "tod_pause_distance"),
            ("Automatic step climbs master option: (experimental)", "automatic_step_climbs"),
            ("Nosewheel steering", "tiller_realistic"),
            ("Throttle lockout", "throttle_lockout_enabled"),
            ("Displays bootup speed", "displays_bootup_realistic"),
            ("Sync barometers", "baro_sync"),
            ("Initial cockpit door", "cockpit_door_initial_open"),
            ("Parking brake mode", "parking_brake_realistic"),
            ("IRS alignment time", "irs_align_time"),
            ("Boarding & load rate", "loading_speed"),
            ("Automatic boarding", "automatic_loading"),
            ("Automatic gpu/jetway", "auto_connect_ground_services_on_init"));

    [Fact]
    public void Control_response_shows_under_adaptive_handling_assist_switched_on()
    {
        AssertPage("bright", "57-settings-aircraft-control-response", "Settings, Aircraft",
            ("Adaptive handling assist", "easy_flight_enabled"),
            ("Control response", "easy_flight_authority"));

        // A number along its track, as the window offers it: no choice list of words.
        var model = Bbs757EfbScreenTests.Load("bright", "57-settings-aircraft-control-response");
        var response = Assert.Single(model.Items, i => i.Text == "Control response");
        Assert.Equal(Bbs757EfbKind.Slider, response.Kind);
        Assert.Null(Bbs757EfbSettingsMatch.ChoicesFor(Bbs757EfbSettingsMatch.SettingFor(model.PageName, response)!));
    }

    [Fact]
    public void The_Callouts_page_rows_are_matched_with_their_keys() =>
        AssertPage("bright", "53-settings-callouts", "Settings, Callouts",
            ("80 KTS", Pilot + "80_knots"),
            ("V1", Pilot + "v1"),
            ("Rotate", Pilot + "rotate"),
            ("Positive rate", Pilot + "positive_rate"),
            ("2500", Egpws + "2500"),
            ("Radio altimeter (2500)", Egpws + "radio_altimeter"),
            ("1000", Egpws + "1000"),
            ("500", Egpws + "500"),
            ("400", Egpws + "400"),
            ("300", Egpws + "300"),
            ("200", Egpws + "200"),
            ("100", Egpws + "100"),
            ("80", Egpws + "80"),
            ("60", Egpws + "60"));

    [Fact]
    public void The_General_page_rows_are_matched_with_their_keys() =>
        AssertPage("dim", "50-settings-general", "Settings, General",
            ("Lock keyboard to EFB", "force_keyboard_capture"),
            ("EFB powered on during initialization", "efb_power_on_during_init"),
            ("EFB brightness mode", "auto_brightness"),
            ("EFB background", "background_num"),
            ("Live autobrake info", "autobrake_notifications"),
            ("Notifications fade-out timer", "notification_popback_seconds"),
            ("EFB colors theme", "ui_theme"));

    [Fact]
    public void Hoppie_ACARS_and_CPDLC_network_on_the_Aircraft_page_are_the_ACARS_settings()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft");
        foreach (var text in new[] { "Hoppie ACARS", "CPDLC network" })
        {
            var setting = Bbs757EfbSettingsMatch.SettingFor(page.PageName, Assert.Single(page.Items, i => i.Text == text));
            Assert.Equal(Bbs757SettingSection.Acars, setting?.Section);
        }
    }

    [Fact]
    public void The_codes_on_the_AIRAC_data_page_are_the_SimBrief_ID_and_the_ACARS_settings()
    {
        // The reader makes these rows (Row, "set" or "not set": a credential is never read) on Settings > AIRAC data.
        foreach (var (label, path, section) in new[]
                 {
                     ("SimBrief ID number", "simbrief_id", Bbs757SettingSection.AiracData),
                     ("Hoppie logon code", "hoppie_logon_code", Bbs757SettingSection.Acars),
                     ("SayIntentions ACARS key", "sayintentions_acars_key", Bbs757SettingSection.Acars),
                 })
        {
            var setting = Bbs757EfbSettingsMatch.SettingFor("Settings, AIRAC data", Row(Bbs757EfbKind.Row, label, value: "set"));
            Assert.Equal(path, setting?.Path);
            Assert.Equal(section, setting?.Section);
        }
    }

    [Fact]
    public void A_manual_brightness_slider_is_the_EFB_brightness_setting_not_its_mode()
    {
        // In manual mode the reader gives the mode's switch and, beside it, the brightness slider.
        Assert.Equal("auto_brightness",
            Bbs757EfbSettingsMatch.SettingFor("Settings, General", Row(Bbs757EfbKind.Toggle, "EFB brightness mode", on: false, value: "Manual"))?.Path);
        Assert.Equal("display_brightness",
            Bbs757EfbSettingsMatch.SettingFor("Settings, General", Row(Bbs757EfbKind.Slider, "EFB brightness", value: "100%"))?.Path);
    }

    [Fact]
    public void The_80_knots_callout_and_the_80_foot_callout_are_two_settings()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "53-settings-callouts");
        var knots = Bbs757EfbSettingsMatch.SettingFor(page.PageName, Assert.Single(page.Items, i => i.Text == "80 KTS"));
        var feet = Bbs757EfbSettingsMatch.SettingFor(page.PageName, Assert.Single(page.Items, i => i.Text == "80"));

        Assert.NotNull(knots);
        Assert.NotNull(feet);
        Assert.NotSame(knots, feet);
    }

    // ---- how a label is matched ------------------------------------------------------------------

    [Theory]
    [InlineData("Settings, Aircraft", "Sync barometers", "baro_sync")]
    [InlineData("Settings, Aircraft", "SYNC BAROMETERS", "baro_sync")]
    [InlineData("Settings, Aircraft", "sync   barometers", "baro_sync")]
    [InlineData("Settings, Aircraft", "Sync barometers.", "baro_sync")]
    [InlineData("Settings, Aircraft", "Automatic gpu/jetway", "auto_connect_ground_services_on_init")]
    [InlineData("Settings, Aircraft", "AUTOMATIC GPU/JETWAY", "auto_connect_ground_services_on_init")]
    [InlineData("Settings, Aircraft", "TOD pause distance NM", "tod_pause_distance")]
    [InlineData("Settings, General", "Notifications fade out timer", "notification_popback_seconds")]
    [InlineData("Settings, General", "NOTIFICATIONS FADE-OUT TIMER", "notification_popback_seconds")]
    [InlineData("Settings, Callouts", "Radio altimeter 2500", "options.egpws_callouts.radio_altimeter")]
    [InlineData("Settings, Callouts", "approaching dh (dh + 100)", "options.egpws_callouts.approaching_dh")]
    [InlineData("Settings, Cabin audio", "Non safety related announcements", "options.cabin_audio.non_safety_announcements")]
    [InlineData("Settings, Livery options", "Vnav takeoff behavior", "options.general.vnav_takeoff_activation")]
    [InlineData("Aircraft, Failures", "Random failure frequency", "failures.random_multiplier")]
    public void A_label_is_matched_with_case_spaces_and_punctuation_ignored(string page, string label, string path) =>
        Assert.Equal(path, Bbs757EfbSettingsMatch.SettingFor(page, Row(Bbs757EfbKind.Toggle, label))?.Path);

    [Fact]
    public void A_label_matching_no_catalog_entry_is_not_judged_by_the_file()
    {
        // A label no catalog entry matches: the press is judged by the page alone, never by a key that may be another setting's.
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor("Settings, Aircraft", Row(Bbs757EfbKind.Toggle, "Sync baromoters")));
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor("Settings, Aircraft", Row(Bbs757EfbKind.Toggle, "Nosewheel stearing")));
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor("Settings, Callouts", Row(Bbs757EfbKind.Toggle, "I000")));
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor("Settings, Callouts", Row(Bbs757EfbKind.Toggle, "5OO")));

        // The misread of a real row from a capture.
        var page = Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft");
        var sync = Assert.Single(page.Items, i => i.Text == "Sync barometers");
        Assert.NotNull(Bbs757EfbSettingsMatch.SettingFor(page.PageName, sync));
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor(page.PageName, sync with { Text = "Sync barometer s0" }));
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor(page.PageName, sync with { Text = "" }));
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor(page.PageName, sync with { Text = "?!" }));
    }

    [Fact]
    public void A_label_of_another_page_is_not_a_setting_of_this_one()
    {
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor("Settings, Aircraft", Row(Bbs757EfbKind.Toggle, "Lock keyboard to EFB")));
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor("Settings, General", Row(Bbs757EfbKind.Toggle, "Sync barometers")));
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor("Settings, Callouts", Row(Bbs757EfbKind.Toggle, "Announcements")));
        Assert.Equal(Bbs757SettingSection.CabinAudio,
            Bbs757EfbSettingsMatch.SettingFor("Settings, Cabin audio", Row(Bbs757EfbKind.Toggle, "Announcements"))?.Section);
    }

    [Theory]
    [InlineData("Aircraft, Fuel")]
    [InlineData("Aircraft, Ground services")]
    [InlineData("Navigation, ACARS dashboard")]
    [InlineData("Home")]
    [InlineData("")]
    public void A_row_on_a_page_that_holds_no_settings_is_not_a_setting(string page) =>
        Assert.Null(Bbs757EfbSettingsMatch.SettingFor(page, Row(Bbs757EfbKind.Toggle, "Sync barometers")));

    [Theory]
    [InlineData(Bbs757EfbKind.Toggle, true)]
    [InlineData(Bbs757EfbKind.Slider, true)]
    [InlineData(Bbs757EfbKind.Checkbox, true)]
    [InlineData(Bbs757EfbKind.Row, true)]
    [InlineData(Bbs757EfbKind.Heading, false)]
    [InlineData(Bbs757EfbKind.Text, false)]
    [InlineData(Bbs757EfbKind.Button, false)]
    [InlineData(Bbs757EfbKind.Menu, false)]
    public void Only_a_switch_slider_check_box_or_row_can_be_a_setting(Bbs757EfbKind kind, bool matches)
    {
        var setting = Bbs757EfbSettingsMatch.SettingFor("Settings, Aircraft", Row(kind, "Sync barometers"));

        Assert.Equal(matches, setting is not null);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("tr-TR")]
    public void A_label_is_matched_in_any_Windows_culture(string culture)
    {
        // tr-TR lowers an "I" to a dotless one with a culture-sensitive ToLower: INITIAL must still read "Initial".
        var path = TestCulture.Run(culture, () =>
            Bbs757EfbSettingsMatch.SettingFor("Settings, Aircraft", Row(Bbs757EfbKind.Toggle, "INITIAL COCKPIT DOOR"))?.Path);

        Assert.Equal("cockpit_door_initial_open", path);
    }

    [Fact]
    public void Every_setting_is_found_by_its_own_label_on_each_page_that_shows_it()
    {
        // A label two settings of one page share (once case, spaces and punctuation are ignored) would hide one of them.
        foreach (var (page, sections) in Pages)
        {
            foreach (var setting in Bbs757EfbSettings.All.Where(s => sections.Contains(s.Section)))
            {
                var found = Bbs757EfbSettingsMatch.SettingFor(page, Row(Bbs757EfbKind.Toggle, setting.Label));
                Assert.True(ReferenceEquals(setting, found), $"{page}: '{setting.Label}' found {found?.Path ?? "nothing"}, not {setting.Path}.");
            }
        }
    }

    [Fact]
    public void Every_section_of_the_catalog_is_shown_on_a_settings_page()
    {
        // A section added to the catalog needs its page here and in SectionFor.
        foreach (var section in Enum.GetValues<Bbs757SettingSection>())
            Assert.Contains(Pages, page => page.Sections.Contains(section));
    }

    // ---- the choices a slider offers ----------------------------------------------------------------

    [Fact]
    public void EFB_colors_theme_choices_are_the_tablets_order()
    {
        var theme = Setting("ui_theme");

        // The slider's order is not the stored numbers': Light grey is 3 and Dark grey is 2.
        Assert.Equal(new[] { "Dark blue", "Light blue", "Light grey", "Dark grey" }, Bbs757EfbSettingsMatch.ChoicesFor(theme));
        Assert.Equal(Bbs757Value.Of(0L), Bbs757EfbSettingsMatch.StoredFor(theme, null, "Dark blue"));
        Assert.Equal(Bbs757Value.Of(1L), Bbs757EfbSettingsMatch.StoredFor(theme, null, "Light blue"));
        Assert.Equal(Bbs757Value.Of(3L), Bbs757EfbSettingsMatch.StoredFor(theme, null, "Light grey"));
        Assert.Equal(Bbs757Value.Of(2L), Bbs757EfbSettingsMatch.StoredFor(theme, null, "Dark grey"));
    }

    [Fact]
    public void The_other_sliders_offer_their_words_in_the_tablets_order()
    {
        Assert.Equal(new[] { "Instant", "1 min", "2 min", "5 min", "Full (7 min+)" },
            Bbs757EfbSettingsMatch.ChoicesFor(Setting("irs_align_time")));
        Assert.Equal(new[] { "Realistic", "Standard", "Quick", "Instant" },
            Bbs757EfbSettingsMatch.ChoicesFor(Setting("loading_speed")));
        Assert.Equal(new[] { "Default", "1", "2", "3", "4", "5", "6", "7", "8", "9" },
            Bbs757EfbSettingsMatch.ChoicesFor(Setting("background_num")));

        // 3 sec to 30 sec, one whole second at a time, then Never at the slider's right end.
        var fade = Bbs757EfbSettingsMatch.ChoicesFor(Setting("notification_popback_seconds"));
        Assert.NotNull(fade);
        Assert.Equal(29, fade!.Count);
        Assert.Equal("3 sec", fade[0]);
        Assert.Equal("10 sec", fade[7]);
        Assert.Equal("30 sec", fade[27]);
        Assert.Equal("Never", fade[28]);
    }

    [Fact]
    public void A_switch_and_a_two_word_choice_offer_their_words()
    {
        Assert.Equal(new[] { "Off", "On" }, Bbs757EfbSettingsMatch.ChoicesFor(Setting("baro_sync")));
        Assert.Equal(new[] { "Realistic", "Rudder" }, Bbs757EfbSettingsMatch.ChoicesFor(Setting("tiller_realistic")));
        Assert.Equal(new[] { "Hoppie", "SayIntentions" }, Bbs757EfbSettingsMatch.ChoicesFor(Setting("cpdlc_network")));
    }

    [Fact]
    public void A_number_a_text_box_and_a_continuous_slider_offer_no_choices()
    {
        Assert.Null(Bbs757EfbSettingsMatch.ChoicesFor(Setting("tod_pause_distance")));
        Assert.Null(Bbs757EfbSettingsMatch.ChoicesFor(Setting("display_brightness")));
        Assert.Null(Bbs757EfbSettingsMatch.ChoicesFor(Setting("simbrief_id")));
        Assert.Null(Bbs757EfbSettingsMatch.ChoicesFor(Setting("hoppie_logon_code")));
        Assert.Null(Bbs757EfbSettingsMatch.ChoicesFor(Setting("failures.random_multiplier")));
    }

    [Fact]
    public void Every_choice_word_finds_its_own_choice()
    {
        // Two words of one setting that read the same would send a press to the wrong choice.
        foreach (var setting in Bbs757EfbSettings.All.Where(s => s.Choices.Count > 0))
        {
            var words = Bbs757EfbSettingsMatch.ChoicesFor(setting);
            Assert.NotNull(words);
            Assert.Equal(setting.Choices.Count, words!.Count);
            for (int i = 0; i < words.Count; i++)
            {
                Assert.Equal(i, Bbs757EfbSettingsMatch.ChoiceIndex(setting, words[i]));
                Assert.Equal(setting.Choices[i].Stored, Bbs757EfbSettingsMatch.StoredFor(setting, null, words[i]));
            }
        }
    }

    // ---- which choice the reader's word is --------------------------------------------------------

    [Theory]
    [InlineData("irs_align_time", "FULL(7 MIN+)", 4)]
    [InlineData("irs_align_time", "FULL (7 MIN+)", 4)]
    [InlineData("irs_align_time", "Full (7 min+)", 4)]
    [InlineData("irs_align_time", "INSTANT", 0)]
    [InlineData("irs_align_time", "1 MIN", 1)]
    [InlineData("irs_align_time", "5 min", 3)]
    [InlineData("irs_align_time", "  5   MIN ", 3)]
    [InlineData("irs_align_time", "FULL (8 MIN+)", -1)]
    [InlineData("loading_speed", "REALISTIC", 0)]
    [InlineData("loading_speed", "INSTANT", 3)]
    [InlineData("notification_popback_seconds", "10 SEC", 7)]
    [InlineData("notification_popback_seconds", "3 SEC", 0)]
    [InlineData("notification_popback_seconds", "NEVER", 28)]
    [InlineData("notification_popback_seconds", "2 SEC", -1)]
    [InlineData("ui_theme", "DARK BLUE", 0)]
    [InlineData("ui_theme", "LIGHT GREY", 2)]
    [InlineData("ui_theme", "DARK GREY", 3)]
    [InlineData("ui_theme", "DARK", -1)]
    [InlineData("background_num", "DEFAULT", 0)]
    [InlineData("background_num", "7", 7)]
    [InlineData("baro_sync", "OFF", 0)]
    [InlineData("baro_sync", "On", 1)]
    [InlineData("tiller_realistic", "RUDDER", 1)]
    [InlineData("cpdlc_network", "SAYINTENTIONS", 1)]
    [InlineData("cpdlc_network", "Hoppie", 0)]
    [InlineData("options.general.vnav_takeoff_activation", "OPTION 2", 1)]
    [InlineData("options.general.weight_unit", "METRIC", 1)]
    public void ChoiceIndex_is_the_place_of_the_read_value_among_the_choices(string path, string read, int expected) =>
        Assert.Equal(expected, Bbs757EfbSettingsMatch.ChoiceIndex(Setting(path), read));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void ChoiceIndex_is_minus_one_for_nothing_read(string? read) =>
        Assert.Equal(-1, Bbs757EfbSettingsMatch.ChoiceIndex(Setting("irs_align_time"), read));

    [Fact]
    public void ChoiceIndex_is_minus_one_for_a_setting_without_choices()
    {
        Assert.Equal(-1, Bbs757EfbSettingsMatch.ChoiceIndex(Setting("tod_pause_distance"), "40"));
        Assert.Equal(-1, Bbs757EfbSettingsMatch.ChoiceIndex(Setting("failures.random_multiplier"), "2.0x"));
        Assert.Equal(-1, Bbs757EfbSettingsMatch.ChoiceIndex(Setting("simbrief_id"), "set"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("tr-TR")]
    public void ChoiceIndex_reads_the_same_in_any_Windows_culture(string culture)
    {
        // The tablet's FULL(7 MIN+) and the catalog's "Full (7 min+)". tr-TR would lower the I of INSTANT to a dotless i.
        var irs = Setting("irs_align_time");
        var fade = Setting("notification_popback_seconds");

        var places = TestCulture.Run(culture, () => new[]
        {
            Bbs757EfbSettingsMatch.ChoiceIndex(irs, "FULL(7 MIN+)"),
            Bbs757EfbSettingsMatch.ChoiceIndex(irs, "INSTANT"),
            Bbs757EfbSettingsMatch.ChoiceIndex(irs, "5 MIN"),
            Bbs757EfbSettingsMatch.ChoiceIndex(fade, "10 SEC"),
            Bbs757EfbSettingsMatch.ChoiceIndex(fade, "NEVER"),
        });

        Assert.Equal(new[] { 4, 0, 3, 7, 28 }, places);
    }

    [Fact]
    public void A_matched_row_reads_one_of_its_settings_choices()
    {
        // The words the reader gives a matched row are the catalog's words: the window's choice list holds the current one.
        int checkedRows = 0;
        foreach (var (set, name) in new[] { ("bright", "51-settings-aircraft"), ("bright", "53-settings-callouts"), ("dim", "50-settings-general") })
        {
            var page = Bbs757EfbScreenTests.Load(set, name);
            foreach (var row in page.Items.Where(i => IsRow(i) && i.Value.Length > 0))
            {
                // Every row of these pages that reads a word is one the catalog holds.
                var setting = Bbs757EfbSettingsMatch.SettingFor(page.PageName, row);
                Assert.True(setting is not null, $"{page.PageName}: '{row.Text}' matched no setting.");
                if (setting!.Choices.Count == 0) continue;

                Assert.True(Bbs757EfbSettingsMatch.ChoiceIndex(setting, row.Value) >= 0,
                    $"{page.PageName}: '{row.Text}' reads '{row.Value}', none of {string.Join(", ", setting.Choices.Select(c => c.Word))}.");
                checkedRows++;
            }
        }

        // 14 rows of the Aircraft page, 14 switches of the Callouts page and the 7 of the General page.
        Assert.Equal(35, checkedRows);
    }

    [Fact]
    public void The_sliders_of_the_captured_pages_read_their_place_among_the_choices()
    {
        var aircraft = Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft");
        var general = Bbs757EfbScreenTests.Load("dim", "50-settings-general");

        void AssertReads(Bbs757EfbScreenModel page, string row, string word)
        {
            var item = Assert.Single(page.Items, i => i.Text == row);
            Assert.Equal(Bbs757EfbKind.Slider, item.Kind);
            var setting = Bbs757EfbSettingsMatch.SettingFor(page.PageName, item);
            Assert.NotNull(setting);
            var choices = Bbs757EfbSettingsMatch.ChoicesFor(setting!);
            Assert.NotNull(choices);
            Assert.Equal(word, choices![Bbs757EfbSettingsMatch.ChoiceIndex(setting!, item.Value)]);
        }

        AssertReads(aircraft, "IRS alignment time", "Full (7 min+)");
        AssertReads(aircraft, "Boarding & load rate", "Realistic");
        AssertReads(general, "EFB background", "Default");
        AssertReads(general, "Notifications fade-out timer", "10 sec");
        AssertReads(general, "EFB colors theme", "Dark blue");
    }

    // ---- the value BlueBird stores for the state asked for ---------------------------------------------

    [Fact]
    public void StoredFor_a_switch_is_its_on_and_off()
    {
        var sync = Setting("baro_sync");

        Assert.Equal(Bbs757Value.Of(true), Bbs757EfbSettingsMatch.StoredFor(sync, true, null));
        Assert.Equal(Bbs757Value.Of(false), Bbs757EfbSettingsMatch.StoredFor(sync, false, null));

        // A callout is a switch too, and the file keeps it as a bool.
        var rotate = Setting(Pilot + "rotate");
        Assert.Equal(Bbs757Value.Of(true), Bbs757EfbSettingsMatch.StoredFor(rotate, true, null));
        Assert.Equal(Bbs757Value.Of(false), Bbs757EfbSettingsMatch.StoredFor(rotate, false, null));
    }

    [Fact]
    public void StoredFor_a_switch_takes_the_tablets_word_too()
    {
        var sync = Setting("baro_sync");

        Assert.Equal(Bbs757Value.Of(true), Bbs757EfbSettingsMatch.StoredFor(sync, null, "On"));
        Assert.Equal(Bbs757Value.Of(true), Bbs757EfbSettingsMatch.StoredFor(sync, null, "ON"));
        Assert.Equal(Bbs757Value.Of(false), Bbs757EfbSettingsMatch.StoredFor(sync, null, "Off"));
        Assert.Equal(Bbs757Value.Of(true), Bbs757EfbSettingsMatch.StoredFor(sync, true, "On"));
    }

    [Fact]
    public void StoredFor_a_two_word_switch_is_the_value_of_the_word_the_switch_shows()
    {
        // The tablet shows one word at each end of its switch: Realistic is stored true, Rudder false.
        var steering = Setting("tiller_realistic");

        Assert.Equal(Bbs757Value.Of(true), Bbs757EfbSettingsMatch.StoredFor(steering, true, null));
        Assert.Equal(Bbs757Value.Of(false), Bbs757EfbSettingsMatch.StoredFor(steering, false, null));
        Assert.Equal(Bbs757Value.Of(true), Bbs757EfbSettingsMatch.StoredFor(steering, null, "REALISTIC"));
        Assert.Equal(Bbs757Value.Of(false), Bbs757EfbSettingsMatch.StoredFor(steering, null, "Rudder"));
    }

    [Fact]
    public void StoredFor_a_choice_is_the_value_of_its_word()
    {
        Assert.Equal(Bbs757Value.Of(9L), Bbs757EfbSettingsMatch.StoredFor(Setting("irs_align_time"), null, "FULL(7 MIN+)"));
        Assert.Equal(Bbs757Value.Of(0L), Bbs757EfbSettingsMatch.StoredFor(Setting("irs_align_time"), null, "Instant"));
        Assert.Equal(Bbs757Value.Of(3L), Bbs757EfbSettingsMatch.StoredFor(Setting("loading_speed"), null, "INSTANT"));
        Assert.Equal(Bbs757Value.Of(0L), Bbs757EfbSettingsMatch.StoredFor(Setting("notification_popback_seconds"), null, "Never"));
        Assert.Equal(Bbs757Value.Of(10L), Bbs757EfbSettingsMatch.StoredFor(Setting("notification_popback_seconds"), null, "10 SEC"));
        Assert.Equal(Bbs757Value.Of(1L), Bbs757EfbSettingsMatch.StoredFor(Setting("cpdlc_network"), null, "SayIntentions"));

        // A text key: the value is the text BlueBird writes, not the word.
        Assert.Equal(Bbs757Value.Of("kg"), Bbs757EfbSettingsMatch.StoredFor(Setting("options.general.weight_unit"), null, "Metric"));
        Assert.Equal(Bbs757Value.Of("clb_button"),
            Bbs757EfbSettingsMatch.StoredFor(Setting("options.general.vnav_takeoff_activation"), null, "Option 2"));
    }

    [Fact]
    public void StoredFor_names_nothing_a_switch_or_a_word_cannot_name()
    {
        // No state asked for.
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("baro_sync"), null, null));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("baro_sync"), null, ""));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("baro_sync"), null, "  "));

        // A word the setting does not have.
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("irs_align_time"), null, "FULL (8 MIN+)"));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("baro_sync"), null, "Maybe"));

        // A switch's on does not name a choice among several, nor one that is not stored as a bool.
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("irs_align_time"), true, null));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("ui_theme"), false, null));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("cpdlc_network"), true, null));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("options.general.weight_unit"), false, null));

        // A box or a continuous slider: what the pilot typed or set is not a choice.
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("tod_pause_distance"), true, null));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("tod_pause_distance"), false, "Off"));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("hoppie_logon_code"), null, "set"));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("failures.random_multiplier"), null, "2.0x"));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(Setting("failures.random_enabled"), null, null));
    }

    [Fact]
    public void StoredFor_does_not_judge_a_state_and_a_word_that_disagree()
    {
        var steering = Setting("tiller_realistic");

        Assert.Equal(Bbs757Value.Of(true), Bbs757EfbSettingsMatch.StoredFor(steering, true, "Realistic"));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(steering, true, "Rudder"));
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(steering, false, "Realistic"));

        // A word that names nothing is not rescued by the switch beside it.
        Assert.Null(Bbs757EfbSettingsMatch.StoredFor(steering, true, "Realisitc"));

        // A state that names nothing here leaves the word to name the value.
        Assert.Equal(Bbs757Value.Of(0L), Bbs757EfbSettingsMatch.StoredFor(Setting("cpdlc_network"), false, "Hoppie"));
    }

    [Fact]
    public void StoredFor_gives_only_values_the_catalogs_choices_store()
    {
        // BlueBird's files get only values it stores itself (B757-17): every answer is one of a choice's.
        foreach (var setting in Bbs757EfbSettings.All)
        {
            foreach (bool? on in new bool?[] { true, false, null })
            {
                foreach (string? word in new[] { null, "On", "Off", "Auto", "Manual", "Automatic", "Metric", "Never", "Dark grey" })
                {
                    var stored = Bbs757EfbSettingsMatch.StoredFor(setting, on, word);
                    if (stored is { } value)
                        Assert.Contains(setting.Choices, c => c.Stored == value);
                }
            }
        }
    }

    // ---- the callouts a press switches off ---------------------------------------------------------

    [Fact]
    public void Radio_altimeter_2500_switches_2500_off()
    {
        var radio = Setting(Egpws + "radio_altimeter");
        var height = Setting(Egpws + "2500");

        Assert.Equal(new[] { Egpws + "2500" }, Bbs757EfbSettingsMatch.SwitchedOffWith(radio).Select(s => s.Path));
        Assert.Equal(new[] { Egpws + "radio_altimeter" }, Bbs757EfbSettingsMatch.SwitchedOffWith(height).Select(s => s.Path));
        Assert.Equal("2500 turned off", Bbs757EfbSettingsMatch.TurnedOffText(height));
        Assert.Equal("Radio altimeter (2500) turned off", Bbs757EfbSettingsMatch.TurnedOffText(radio));
    }

    [Fact]
    public void The_minimums_callouts_switch_the_other_four_off()
    {
        var minimums = Setting(Egpws + "minimums");

        // In the catalog's order, the one the tablet writes false for first.
        Assert.Equal(new[] { Egpws + "minimums_minimums", Egpws + "minimum", Egpws + "decide", Egpws + "decision_height" },
            Bbs757EfbSettingsMatch.SwitchedOffWith(minimums).Select(s => s.Path));
        Assert.Equal(new[] { Egpws + "minimums", Egpws + "minimums_minimums", Egpws + "minimum", Egpws + "decide" },
            Bbs757EfbSettingsMatch.SwitchedOffWith(Setting(Egpws + "decision_height")).Select(s => s.Path));
    }

    [Fact]
    public void The_DH_plus_100_callouts_switch_each_other_off()
    {
        Assert.Equal(new[] { Egpws + "approaching_dh" },
            Bbs757EfbSettingsMatch.SwitchedOffWith(Setting(Egpws + "plus_hundred")).Select(s => s.Path));
        Assert.Equal(new[] { Egpws + "plus_hundred" },
            Bbs757EfbSettingsMatch.SwitchedOffWith(Setting(Egpws + "approaching_dh")).Select(s => s.Path));
    }

    [Fact]
    public void A_setting_outside_a_group_switches_nothing_off()
    {
        foreach (string path in new[] { "baro_sync", Pilot + "v1", Pilot + "80_knots", Egpws + "1000", Egpws + "approaching_minimums",
                     Egpws + "50_above", Cabin + "announcements", "ui_theme", "tod_pause_distance" })
        {
            var others = Bbs757EfbSettingsMatch.SwitchedOffWith(Setting(path));

            Assert.NotNull(others);
            Assert.Empty(others);
        }
    }

    [Fact]
    public void The_callouts_a_press_switches_off_are_the_ones_the_catalog_writes_false()
    {
        // The tablet's rule is the catalog's (WritesFor): the window names exactly the keys the file gets false for.
        foreach (var setting in Bbs757EfbSettings.All.Where(s => s.ExclusiveGroup != null))
        {
            var writes = Bbs757EfbSettings.WritesFor(setting, Bbs757Value.Of(true));
            var falseWrites = writes.Take(writes.Count - 1).Select(w => w.Path);
            var others = Bbs757EfbSettingsMatch.SwitchedOffWith(setting);

            Assert.Equal(falseWrites, others.Select(s => s.Path));
            Assert.DoesNotContain(setting, others);
            Assert.All(others, other => Assert.Equal(setting.ExclusiveGroup, other.ExclusiveGroup));
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void TurnedOffText_is_the_label_and_turned_off_in_any_Windows_culture(string culture)
    {
        var texts = TestCulture.Run(culture, () => Bbs757EfbSettings.All
            .Where(s => s.ExclusiveGroup != null)
            .Select(Bbs757EfbSettingsMatch.TurnedOffText)
            .ToArray());

        // The 2500 pair, the DH + 100 pair and the five minimums callouts.
        Assert.Equal(9, texts.Length);
        Assert.Contains("2500 turned off", texts);
        Assert.Contains("Radio altimeter (2500) turned off", texts);
        Assert.Contains("Approaching DH (DH + 100) turned off", texts);
        Assert.Contains("Decision height turned off", texts);
        Assert.All(texts, text => Assert.EndsWith(" turned off", text, StringComparison.Ordinal));
    }
}
