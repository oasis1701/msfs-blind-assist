using System.Globalization;
using System.Text.Json;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The catalog of BlueBird's tablet settings (<see cref="Bbs757EfbSettings"/>): every setting's key, file, tablet
/// label and words, the value BlueBird stores for each word (measured 2026-10-27), the
/// writes one change makes (the tablet's three rules: docs/b757.md, "The settings window"), how a number or text box shows and reads
/// what is typed and what its empty box means, and the random failure frequency's slider: its steps, what it reads and
/// where a stored value sits on it. Pure: no files, no simulator.
/// </summary>
public class Bbs757EfbSettingsTests
{
    private const string Egpws = "options.egpws_callouts.";
    private const string Cabin = "options.cabin_audio.";

    private static Bbs757Setting Setting(string path)
    {
        var setting = Bbs757EfbSettings.ByPath(path);
        Assert.True(setting is not null, $"The catalog has no setting '{path}'.");
        return setting!;
    }

    private static Bbs757Value Stored(string path, string word) =>
        Setting(path).Choices.Single(c => c.Word == word).Stored;

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static Bbs757Value Accepted(string path, string typed)
    {
        bool ok = Bbs757EfbSettings.TryParseEntry(Setting(path), typed, out var value, out var reason);
        Assert.True(ok, $"{path}: '{typed}' was refused ({reason})");
        Assert.Null(reason);
        return value;
    }

    private static string Refused(string path, string typed)
    {
        bool ok = Bbs757EfbSettings.TryParseEntry(Setting(path), typed, out var value, out var reason);
        Assert.False(ok, $"{path}: '{typed}' was accepted as {value}");
        Assert.NotNull(reason);
        return reason!;
    }

    // ---- the catalog as a whole ------------------------------------------------------------------

    [Fact]
    public void Every_setting_has_a_unique_path_and_a_unique_label_in_its_section()
    {
        var all = Bbs757EfbSettings.All;
        Assert.Empty(all.GroupBy(s => s.Path).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Empty(all.GroupBy(s => (s.Section, s.Label)).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Section}: {g.Key.Label}"));
        Assert.All(all, s => Assert.Same(s, Bbs757EfbSettings.ByPath(s.Path)));
        Assert.Null(Bbs757EfbSettings.ByPath("no.such.key"));
    }

    private static readonly Dictionary<Bbs757SettingSection, (string Label, string Path)[]> TabletRows = new()
    {
        [Bbs757SettingSection.General] = new[]
        {
            ("Lock keyboard to EFB", "force_keyboard_capture"),
            ("EFB powered on during initialization", "efb_power_on_during_init"),
            ("EFB brightness mode", "auto_brightness"),
            ("EFB brightness", "display_brightness"),
            ("EFB background", "background_num"),
            ("Live autobrake info", "autobrake_notifications"),
            ("Notifications fade-out timer", "notification_popback_seconds"),
            ("EFB colors theme", "ui_theme"),
        },
        [Bbs757SettingSection.Aircraft] = new[]
        {
            // BlueBird's 2026-10-31 update added Adaptive handling assist (with Control response under it while it is on),
            // the automatic step climbs and Throttle lockout (measured 2026-11-01).
            ("Adaptive handling assist", "easy_flight_enabled"),
            ("Control response", "easy_flight_authority"),
            ("Adaptive cockpit lighting", "automatic_cockpit_lighting"),
            ("TOD pause distance (NM)", "tod_pause_distance"),
            ("Automatic step climbs master option (experimental)", "automatic_step_climbs"),
            ("Nosewheel steering", "tiller_realistic"),
            ("Throttle lockout", "throttle_lockout_enabled"),
            ("Displays bootup speed", "displays_bootup_realistic"),
            ("Sync barometers", "baro_sync"),
            ("Initial cockpit door", "cockpit_door_initial_open"),
            ("Parking brake mode", "parking_brake_realistic"),
            ("IRS alignment time", "irs_align_time"),
            ("Boarding & load rate", "loading_speed"),
            ("Automatic boarding", "automatic_loading"),
            ("Automatic GPU/jetway", "auto_connect_ground_services_on_init"),
            ("Initial chocks in place", "auto_attach_chocks_on_init"),
            ("Dirty panel states", "dirty_panel_states_enabled"),
            ("GND SRVC mode", "ground_services_mode"),
        },
        [Bbs757SettingSection.AiracData] = new[]
        {
            ("SimBrief ID number", "simbrief_id"),
        },
        // Not a tablet page: the ACARS rows of Settings > Aircraft (the switch and the network), then those of AIRAC data
        // (the two codes), each in the tablet's order.
        [Bbs757SettingSection.Acars] = new[]
        {
            ("Hoppie ACARS", "hoppie_user_enabled"),
            ("CPDLC network", "cpdlc_network"),
            ("Hoppie logon code", "hoppie_logon_code"),
            ("SayIntentions ACARS key", "sayintentions_acars_key"),
        },
        [Bbs757SettingSection.Failures] = new[]
        {
            ("Random failure frequency", "failures.random_multiplier"),
            ("Random failures", "failures.random_enabled"),
            ("Failure notifications", "failures.notifications_enabled"),
        },
        [Bbs757SettingSection.LiveryOptions] = new[]
        {
            ("Units", "options.general.weight_unit"),
            ("EHSI up reference", "options.ehsi.up_reference"),
            ("VNAV takeoff behavior", "options.general.vnav_takeoff_activation"),
        },
        [Bbs757SettingSection.Callouts] = new[]
        {
            ("80 KTS", "options.pilot_callouts.80_knots"),
            ("V1", "options.pilot_callouts.v1"),
            ("Rotate", "options.pilot_callouts.rotate"),
            ("Positive rate", "options.pilot_callouts.positive_rate"),
            ("2500", Egpws + "2500"),
            ("Radio altimeter (2500)", Egpws + "radio_altimeter"),
            ("1000", Egpws + "1000"),
            ("500", Egpws + "500"),
            ("400", Egpws + "400"),
            ("300", Egpws + "300"),
            ("200", Egpws + "200"),
            ("100", Egpws + "100"),
            ("80", Egpws + "80"),
            ("60", Egpws + "60"),
            ("50", Egpws + "50"),
            ("40", Egpws + "40"),
            ("35", Egpws + "35"),
            ("30", Egpws + "30"),
            ("20", Egpws + "20"),
            ("10", Egpws + "10"),
            ("5", Egpws + "5"),
            ("Plus hundred (DH + 100)", Egpws + "plus_hundred"),
            ("Approaching DH (DH + 100)", Egpws + "approaching_dh"),
            ("Approaching minimums (DH + 80)", Egpws + "approaching_minimums"),
            ("Fifty above (DH + 50)", Egpws + "50_above"),
            ("Minimums", Egpws + "minimums"),
            ("Minimums minimums", Egpws + "minimums_minimums"),
            ("Minimum", Egpws + "minimum"),
            ("Decide", Egpws + "decide"),
            ("Decision height", Egpws + "decision_height"),
        },
        [Bbs757SettingSection.CabinAudio] = new[]
        {
            ("Announcements", Cabin + "announcements"),
            ("Safety related announcements", Cabin + "safety_announcements"),
            ("Non-safety related announcements", Cabin + "non_safety_announcements"),
            ("Flight attendant calls", Cabin + "flight_attendant_calls"),
            ("Passenger sounds", Cabin + "passenger_sounds"),
        },
    };

    [Fact]
    public void Each_page_lists_the_tablet_s_rows_in_the_tablet_s_order()
    {
        var sections = Enum.GetValues<Bbs757SettingSection>();
        var expected = sections.SelectMany(sec => TabletRows[sec].Select(r => (sec, r.Label, r.Path))).ToArray();

        // All is the pages one after the other, in the order General ... Cabin audio.
        Assert.Equal(expected, Bbs757EfbSettings.All.Select(s => (s.Section, s.Label, s.Path)));
        foreach (var section in sections)
            Assert.Equal(expected.Where(r => r.sec == section).Select(r => r.Path),
                Bbs757EfbSettings.InSection(section).Select(s => s.Path));
    }

    [Fact]
    public void Sections_are_named_as_the_tablet_names_its_pages()
    {
        Assert.Equal(
            new[] { "General", "Aircraft", "AIRAC data", "ACARS", "Failures", "Livery options", "Callouts", "Cabin audio" },
            Enum.GetValues<Bbs757SettingSection>().Select(Bbs757EfbSettings.SectionName));
    }

    [Fact]
    public void Each_section_is_stored_in_the_file_its_page_belongs_to()
    {
        foreach (var s in Bbs757EfbSettings.All)
        {
            var expected = s.Section is Bbs757SettingSection.LiveryOptions or Bbs757SettingSection.Callouts
                or Bbs757SettingSection.CabinAudio
                ? Bbs757SettingFile.Registration
                : Bbs757SettingFile.Global;
            Assert.True(s.File == expected, $"{s.Path} is in the {s.File} file");
        }
    }

    [Fact]
    public void Keys_the_tablet_does_not_show_are_not_in_the_catalog()
    {
        string[] hidden =
        {
            "show_debug_cursor", "electrical_relay_lvar_export_enabled", "charts_night_mode", "charts_manual_icao",
            "chart_view_states", "navigation_map_zoom", "navigation_map_provider", "navigation_map_night_mode", "units",
            "options.general.allow_capture_gs_before_loc", "options.model_visibility.exit_version",
            "options.model_visibility.cabin_layout", "options.model_visibility.satcom",
        };
        string[] hiddenPrefixes = { "charts_", "chart_view_states", "navigation_map_", "options.model_visibility." };

        Assert.All(hidden, p => Assert.Null(Bbs757EfbSettings.ByPath(p)));
        Assert.DoesNotContain(Bbs757EfbSettings.All, s =>
            hidden.Contains(s.Path) || hiddenPrefixes.Any(p => s.Path.StartsWith(p, StringComparison.Ordinal)));
    }

    // ---- the words and the values BlueBird stores for them ----------------------------------------

    [Fact]
    public void Measured_values_are_the_ones_bluebird_wrote()
    {
        Assert.Equal(Bbs757Value.Of(2L), Stored("loading_speed", "Quick"));
        Assert.Equal(Bbs757Value.Of(0L), Stored("loading_speed", "Realistic"));
        Assert.Equal(Bbs757Value.Of(3L), Stored("loading_speed", "Instant"));
        Assert.Equal(Bbs757Value.Of(0L), Stored("irs_align_time", "Instant"));
        Assert.Equal(Bbs757Value.Of(2L), Stored("irs_align_time", "2 min"));
        Assert.Equal(Bbs757Value.Of(9L), Stored("irs_align_time", "Full (7 min+)"));
        Assert.Equal(Bbs757Value.Of(true), Stored("tiller_realistic", "Realistic"));
        Assert.Equal(Bbs757Value.Of(false), Stored("tiller_realistic", "Rudder"));
        Assert.Equal(Bbs757Value.Of("lbs"), Stored("options.general.weight_unit", "Imperial"));
        Assert.Equal(Bbs757Value.Of("kg"), Stored("options.general.weight_unit", "Metric"));
        Assert.Equal(Bbs757Value.Of(3L), Stored("notification_popback_seconds", "3 sec"));
        Assert.Equal(Bbs757Value.Of(0L), Stored("notification_popback_seconds", "Never"));
        Assert.Equal(Bbs757Value.Of(2L), Stored("ui_theme", "Dark grey"));
        Assert.Equal(Bbs757Value.Of(1L), Stored("ground_services_mode", "MSFS"));
        Assert.Equal(Bbs757Value.Of("clb_button"), Stored("options.general.vnav_takeoff_activation", "Option 2"));
    }

    private static (string Word, Bbs757Value Stored)[] TrueFalse(string whenTrue, string whenFalse) =>
        new[] { (whenTrue, Bbs757Value.Of(true)), (whenFalse, Bbs757Value.Of(false)) };

    private static (string Word, Bbs757Value Stored)[] Steps(params (string Word, long Stored)[] steps) =>
        steps.Select(s => (s.Word, Bbs757Value.Of(s.Stored))).ToArray();

    private static (string Word, Bbs757Value Stored)[] Words(params (string Word, string Stored)[] words) =>
        words.Select(w => (w.Word, Bbs757Value.Of(w.Stored))).ToArray();

    [Fact]
    public void Each_choice_lists_the_tablet_s_words_in_its_order_with_what_the_tablet_stores()
    {
        var expected = new (string Path, (string Word, Bbs757Value Stored)[] Choices)[]
        {
            ("auto_brightness", TrueFalse("Auto", "Manual")),
            // DEFAULT is 0, then the digit itself.
            ("background_num", Steps(Enumerable.Range(0, 10)
                .Select(n => (n == 0 ? "Default" : n.ToString(CultureInfo.InvariantCulture), (long)n)).ToArray())),
            // 3 SEC to 30 SEC along the slider, then NEVER (0) at its right end.
            ("notification_popback_seconds", Steps(Enumerable.Range(3, 28)
                .Select(n => (n.ToString(CultureInfo.InvariantCulture) + " sec", (long)n)).Append(("Never", 0L)).ToArray())),
            // The tablet's slider order, not the stored numbers' (Light grey is 3, Dark grey is 2).
            ("ui_theme", Steps(("Dark blue", 0), ("Light blue", 1), ("Light grey", 3), ("Dark grey", 2))),
            ("automatic_cockpit_lighting", TrueFalse("Automatic", "Manual")),
            ("automatic_step_climbs", TrueFalse("Enabled", "Disabled")),
            ("tiller_realistic", TrueFalse("Realistic", "Rudder")),
            ("displays_bootup_realistic", TrueFalse("Realistic", "Fast")),
            ("cockpit_door_initial_open", TrueFalse("Open", "Closed")),
            ("parking_brake_realistic", TrueFalse("Realistic", "Easy")),
            ("irs_align_time", Steps(("Instant", 0), ("1 min", 1), ("2 min", 2), ("5 min", 5), ("Full (7 min+)", 9))),
            ("loading_speed", Steps(("Realistic", 0), ("Standard", 1), ("Quick", 2), ("Instant", 3))),
            ("cpdlc_network", Steps(("Hoppie", 0), ("SayIntentions", 1))),
            ("ground_services_mode", Steps(("BBS", 0), ("MSFS", 1))),
            ("options.general.weight_unit", Words(("Imperial", "lbs"), ("Metric", "kg"))),
            ("options.ehsi.up_reference", Words(("HDG", "hdg"), ("TRK", "trk"))),
            ("options.general.vnav_takeoff_activation", Words(("Option 1", "vnav_button"), ("Option 2", "clb_button"))),
        };

        foreach (var (path, choices) in expected)
            Assert.Equal(choices, Setting(path).Choices.Select(c => (c.Word, c.Stored)));
        // Every Choice setting is in the table, and nothing else is one.
        Assert.Equal(expected.Select(e => e.Path).Order(),
            Bbs757EfbSettings.All.Where(s => s.Kind == Bbs757SettingKind.Choice).Select(s => s.Path).Order());
    }

    [Fact]
    public void Control_response_is_a_whole_number_to_100_while_adaptive_handling_assist_is_on()
    {
        // The tablet's slider stores 0 to 100 (GENTLE at 0, BALANCED at 50, RESPONSIVE at 100, "N/100" between; measured
        // 2026-11-01) and shows only while Adaptive handling assist is on.
        var response = Setting("easy_flight_authority");

        Assert.Equal(Bbs757SettingKind.Number, response.Kind);
        Assert.Equal(Bbs757ValueType.Integer, response.Type);
        Assert.Equal(new Bbs757NumberRange(Bbs757NumberEntry.Whole, 0, 100), response.Range);
        Assert.Equal("easy_flight_enabled", response.EnabledBy);
        Assert.Equal(Bbs757SettingKind.OnOff, Setting("easy_flight_enabled").Kind);
        Assert.Equal(Bbs757SettingKind.OnOff, Setting("throttle_lockout_enabled").Kind);
    }

    [Fact]
    public void The_two_long_choice_lists_are_pinned_by_hand()
    {
        // The table above builds these two lists with the same loops production does. Here they are written out, so a
        // slip in those loops (a word off by one, a missing end) cannot also move what the test expects.
        static (string Word, Bbs757Value Stored) At(IReadOnlyList<Bbs757SettingChoice> choices, int index) =>
            (choices[index].Word, choices[index].Stored);

        var fadeOut = Setting("notification_popback_seconds").Choices;
        Assert.Equal(29, fadeOut.Count);   // 3 sec to 30 sec is 28 words, then Never
        Assert.Equal(("3 sec", Bbs757Value.Of(3L)), At(fadeOut, 0));
        Assert.Equal(("4 sec", Bbs757Value.Of(4L)), At(fadeOut, 1));
        Assert.Equal(("15 sec", Bbs757Value.Of(15L)), At(fadeOut, 12));
        Assert.Equal(("29 sec", Bbs757Value.Of(29L)), At(fadeOut, 26));
        Assert.Equal(("30 sec", Bbs757Value.Of(30L)), At(fadeOut, 27));
        Assert.Equal(("Never", Bbs757Value.Of(0L)), At(fadeOut, 28));
        Assert.Equal(Bbs757Value.Of(30L), Stored("notification_popback_seconds", "30 sec"));

        var background = Setting("background_num").Choices;
        Assert.Equal(10, background.Count);   // Default, then 1 to 9
        Assert.Equal(("Default", Bbs757Value.Of(0L)), At(background, 0));
        Assert.Equal(("1", Bbs757Value.Of(1L)), At(background, 1));
        Assert.Equal(("5", Bbs757Value.Of(5L)), At(background, 5));
        Assert.Equal(("9", Bbs757Value.Of(9L)), At(background, 9));
        Assert.Equal(Bbs757Value.Of(9L), Stored("background_num", "9"));
    }

    [Fact]
    public void The_pilot_s_identifiers_are_never_logged()
    {
        string[] identifiers = { "simbrief_id", "hoppie_logon_code", "sayintentions_acars_key" };

        Assert.All(identifiers, p => Assert.False(Setting(p).MayLog, p));
        Assert.All(Bbs757EfbSettings.All.Where(s => !identifiers.Contains(s.Path)), s => Assert.True(s.MayLog, s.Path));
    }

    [Fact]
    public void Every_choice_holds_a_value_of_its_setting_s_type()
    {
        foreach (var s in Bbs757EfbSettings.All)
        {
            if (s.Kind is Bbs757SettingKind.OnOff or Bbs757SettingKind.Choice)
            {
                Assert.True(s.Choices.Count >= 2, $"{s.Path} has {s.Choices.Count} choices");
                Assert.All(s.Choices, c => Assert.True(c.Stored.Type == s.Type, $"{s.Path}: '{c.Word}' stores a {c.Stored.Type}"));
                Assert.True(s.Choices.Select(c => c.Word).Distinct().Count() == s.Choices.Count, $"{s.Path} repeats a word");
                Assert.True(s.Choices.Select(c => c.Stored).Distinct().Count() == s.Choices.Count, $"{s.Path} repeats a stored value");
            }
            else
            {
                Assert.True(s.Choices.Count == 0, $"{s.Path} is a box or a slider and has choices");
                if (s.Kind == Bbs757SettingKind.Number) Assert.NotNull(s.Range);
                if (s.Kind == Bbs757SettingKind.Text) Assert.Equal(Bbs757ValueType.Text, s.Type);
                // Only a slider has steps, and its steps are its values: it has no range of its own.
                Assert.True((s.Kind == Bbs757SettingKind.Slider) == (s.Steps is not null), $"{s.Path}: steps");
                if (s.Kind == Bbs757SettingKind.Slider) Assert.Null(s.Range);
                if (s.EmptyStored is { } empty) Assert.True(empty.Type == s.Type, $"{s.Path}: the empty value is a {empty.Type}");
            }
        }
    }

    [Fact]
    public void On_off_settings_have_off_then_on()
    {
        var switches = Bbs757EfbSettings.All.Where(s => s.Kind == Bbs757SettingKind.OnOff).ToArray();

        Assert.NotEmpty(switches);
        foreach (var s in switches)
        {
            Assert.Equal(Bbs757ValueType.Bool, s.Type);
            Assert.Equal(new[] { ("Off", Bbs757Value.Of(false)), ("On", Bbs757Value.Of(true)) },
                s.Choices.Select(c => (c.Word, c.Stored)));
        }
    }

    [Fact]
    public void The_number_and_text_boxes_hold_what_the_tablet_holds()
    {
        Assert.Equal(
            new[] { "display_brightness", "easy_flight_authority", "tod_pause_distance", "simbrief_id", "hoppie_logon_code",
                    "sayintentions_acars_key" }.Order(),
            Bbs757EfbSettings.All.Where(s => s.Kind is Bbs757SettingKind.Number or Bbs757SettingKind.Text)
                .Select(s => s.Path).Order());

        Assert.Equal(new Bbs757NumberRange(Bbs757NumberEntry.Percent, 0, 100), Setting("display_brightness").Range);
        Assert.Equal(new Bbs757NumberRange(Bbs757NumberEntry.Whole, 1, 999), Setting("tod_pause_distance").Range);
        Assert.Equal(Bbs757NumberEntry.Digits, Setting("simbrief_id").Range!.Entry);
        Assert.Null(Setting("hoppie_logon_code").Range);
        Assert.Null(Setting("sayintentions_acars_key").Range);

        Assert.Equal(Bbs757ValueType.Float, Setting("display_brightness").Type);
        Assert.Equal(Bbs757ValueType.Integer, Setting("tod_pause_distance").Type);
        Assert.Equal(Bbs757ValueType.Integer, Setting("simbrief_id").Type);

        // A cleared box stores what BlueBird stores for "not set"; the percent cannot be cleared.
        Assert.Equal(Bbs757Value.Of(-1L), Setting("simbrief_id").EmptyStored);
        Assert.Equal(Bbs757Value.Of(-1L), Setting("tod_pause_distance").EmptyStored);
        Assert.Equal(Bbs757Value.Of(""), Setting("hoppie_logon_code").EmptyStored);
        Assert.Equal(Bbs757Value.Of(""), Setting("sayintentions_acars_key").EmptyStored);
        Assert.Null(Setting("display_brightness").EmptyStored);
    }

    // ---- the random failure frequency's slider -----------------------------------------------------

    [Fact]
    public void The_failure_frequency_is_a_slider_along_the_tablet_s_scale()
    {
        var frequency = Setting("failures.random_multiplier");

        Assert.Equal(new[] { "failures.random_multiplier" },
            Bbs757EfbSettings.All.Where(s => s.Kind == Bbs757SettingKind.Slider).Select(s => s.Path));
        Assert.Equal(Bbs757ValueType.Float, frequency.Type);
        Assert.Null(frequency.EmptyStored);
        // Ten steps to each factor of ten, 1x to 1000x, as the tablet's 10^(3t) track runs, each at most one decimal
        // so the tablet's "N.Nx" shows it exactly.
        Assert.Equal(new double[]
        {
            1, 1.3, 1.6, 2, 2.5, 3.2, 4, 5, 6.3, 8,
            10, 13, 16, 20, 25, 32, 40, 50, 63, 80,
            100, 130, 160, 200, 250, 320, 400, 500, 630, 800,
            1000,
        }, frequency.Steps);
        var steps = frequency.Steps!;
        for (int i = 0; i < steps.Count; i++)
        {
            Assert.True(Math.Abs(Math.Log10(steps[i]) - i / 10.0) < 0.015, $"step {i} ({steps[i]}) is off the tablet's scale");
            Assert.Equal(Math.Round(steps[i], 1), steps[i]);
        }
        // Ten steps is a factor of ten: Page Up and Page Down.
        Assert.Equal(10.0, steps[10]);
        Assert.Equal(100.0, steps[20]);
    }

    [Fact]
    public void A_slider_reads_as_the_tablet_shows_it()
    {
        var frequency = Setting("failures.random_multiplier");

        // One decimal and an x, as the tablet's "FREQUENCY: 2.0x (1x-1000x)": 1.9952... is the tablet's own 2.0x.
        Assert.Equal("2.5x", Bbs757EfbSettings.SliderText(frequency, Bbs757Value.OfFloat(2.5)));
        Assert.Equal("2.0x", Bbs757EfbSettings.SliderText(frequency, Bbs757Value.OfFloat(1.9952623149688795)));
        Assert.Equal("1.0x", Bbs757EfbSettings.SliderText(frequency, Bbs757Value.OfFloat(1.0)));
        Assert.Equal("8.5x", Bbs757EfbSettings.SliderText(frequency, Bbs757Value.OfFloat(8.54)));
        Assert.Equal("1000.0x", Bbs757EfbSettings.SliderText(frequency, Bbs757Value.OfFloat(1000.0)));
    }

    [Fact]
    public void A_stored_value_sits_at_the_nearest_step_on_the_tablet_s_scale()
    {
        var frequency = Setting("failures.random_multiplier");
        int At(double value) => Bbs757EfbSettings.NearestStep(frequency, value);

        Assert.Equal(0, At(1.0));
        Assert.Equal(4, At(2.5));
        Assert.Equal(30, At(1000.0));
        // Between steps, the nearer on the tablet's scale: 8.54 is 8 (index 9), 36.57 is 40 (index 16).
        Assert.Equal(9, At(8.54));
        Assert.Equal(16, At(36.57));
        Assert.Equal(3, At(1.9952623149688795));
        // Off the scale, an end.
        Assert.Equal(0, At(0.5));
        Assert.Equal(0, At(-3));
        Assert.Equal(30, At(5000));
        Assert.Equal(0, At(double.NaN));
    }

    [Fact]
    public void A_slider_takes_only_its_own_steps()
    {
        var frequency = Setting("failures.random_multiplier");

        foreach (double step in frequency.Steps!)
            Assert.Equal(new[] { ("failures.random_multiplier", Bbs757SettingFile.Global, Bbs757Value.OfFloat(step)) },
                Bbs757EfbSettings.WritesFor(frequency, Bbs757Value.OfFloat(step)).Select(w => (w.Path, w.File, w.Value)));

        // The app writes only the slider's own steps: 2.4 is no step, the whole number 2 is not the float 2.0.
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(frequency, Bbs757Value.OfFloat(2.4)));
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(frequency, Bbs757Value.OfFloat(1001)));
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(frequency, Bbs757Value.Of(2L)));
    }

    [Fact]
    public void Slider_helpers_refuse_a_setting_of_another_kind()
    {
        foreach (var other in new[] { Setting("baro_sync"), Setting("loading_speed"), Setting("tod_pause_distance") })
        {
            Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.SliderText(other, Bbs757Value.OfFloat(1.0)));
            Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.NearestStep(other, 1.0));
        }
    }

    // ---- the tablet's three rules -----------------------------------------------------------------

    [Theory]
    [InlineData("kg", 0L)]
    [InlineData("lbs", 1L)]
    public void Units_writes_the_registration_unit_and_the_global_one(string unit, long global)
    {
        var units = Setting("options.general.weight_unit");

        var writes = Bbs757EfbSettings.WritesFor(units, Bbs757Value.Of(unit));

        Assert.Equal(
            new[]
            {
                ("options.general.weight_unit", Bbs757SettingFile.Registration, Bbs757Value.Of(unit)),
                ("units", Bbs757SettingFile.Global, Bbs757Value.Of(global)),
            },
            writes.Select(w => (w.Path, w.File, w.Value)));
        Assert.All(writes, w => Assert.Same(units, w.Setting));
        Assert.Equal(new Bbs757LinkedKey("units", Bbs757SettingFile.Global), units.Also);
        Assert.Equal(new[] { "options.general.weight_unit" },
            Bbs757EfbSettings.All.Where(s => s.Also is not null).Select(s => s.Path));
    }

    [Fact]
    public void A_setting_with_no_linked_key_writes_only_its_own_key()
    {
        var one = Bbs757EfbSettings.WritesFor(Setting("loading_speed"), Bbs757Value.Of(2L));
        Assert.Equal(new[] { ("loading_speed", Bbs757SettingFile.Global, Bbs757Value.Of(2L)) },
            one.Select(w => (w.Path, w.File, w.Value)));

        // A number or text box's value comes from TryParseEntry and is written as given.
        var distance = Bbs757EfbSettings.WritesFor(Setting("tod_pause_distance"), Bbs757Value.Of(120L));
        Assert.Equal(new[] { ("tod_pause_distance", Bbs757SettingFile.Global, Bbs757Value.Of(120L)) },
            distance.Select(w => (w.Path, w.File, w.Value)));
        var code = Bbs757EfbSettings.WritesFor(Setting("hoppie_logon_code"), Bbs757Value.Of("AB1"));
        Assert.Equal(new[] { ("hoppie_logon_code", Bbs757SettingFile.Global, Bbs757Value.Of("AB1")) },
            code.Select(w => (w.Path, w.File, w.Value)));
    }

    [Fact]
    public void A_switch_or_choice_takes_only_a_value_one_of_its_choices_stores()
    {
        var switchSetting = Setting("baro_sync");
        var exclusiveCallout = Setting(Egpws + "decide");
        var choice = Setting("loading_speed");
        var units = Setting("options.general.weight_unit");

        // The app writes only values BlueBird itself stores: a switch takes false or true and nothing else.
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(switchSetting, Bbs757Value.Of(1L)));
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(switchSetting, Bbs757Value.Of("true")));
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(exclusiveCallout, Bbs757Value.OfFloat(1.0)));
        // A choice takes only what one of its words stores: BlueBird has no fifth rate, and the float 2.0 is not 2.
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(choice, Bbs757Value.Of(4L)));
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(choice, Bbs757Value.OfFloat(2.0)));
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(choice, Bbs757Value.Of(true)));
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(units, Bbs757Value.Of("stone")));
        Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(units, Bbs757Value.Of("LBS")));
        Assert.Equal("value",
            Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.WritesFor(units, Bbs757Value.Of("stone"))).ParamName);

        // Nothing the catalog itself stores is refused: every choice's value is writable, as that setting's own write.
        foreach (var s in Bbs757EfbSettings.All.Where(s => s.Kind is Bbs757SettingKind.OnOff or Bbs757SettingKind.Choice))
        {
            foreach (var c in s.Choices)
                Assert.Contains(Bbs757EfbSettings.WritesFor(s, c.Stored), w => w.Path == s.Path && w.Value == c.Stored);
        }
    }

    [Fact]
    public void Switching_on_a_callout_switches_the_rest_of_its_group_off_first()
    {
        static void Check(string path, bool on, params (string Key, bool Value)[] expected)
        {
            var writes = Bbs757EfbSettings.WritesFor(Setting(path), Bbs757Value.Of(on));
            Assert.Equal(expected.Select(e => (e.Key, Bbs757Value.Of(e.Value))), writes.Select(w => (w.Path, w.Value)));
            // Each write's Setting is the one it changes, and callouts are in the registration files.
            Assert.All(writes, w => Assert.Same(Setting(w.Path), w.Setting));
            Assert.All(writes, w => Assert.Equal(Bbs757SettingFile.Registration, w.File));
        }

        Check(Egpws + "radio_altimeter", true, (Egpws + "2500", false), (Egpws + "radio_altimeter", true));
        Check(Egpws + "2500", true, (Egpws + "radio_altimeter", false), (Egpws + "2500", true));
        Check(Egpws + "plus_hundred", true, (Egpws + "approaching_dh", false), (Egpws + "plus_hundred", true));
        Check(Egpws + "approaching_dh", true, (Egpws + "plus_hundred", false), (Egpws + "approaching_dh", true));
        Check(Egpws + "decide", true,
            (Egpws + "minimums", false), (Egpws + "minimums_minimums", false), (Egpws + "minimum", false),
            (Egpws + "decision_height", false), (Egpws + "decide", true));
        Check(Egpws + "minimums", true,
            (Egpws + "minimums_minimums", false), (Egpws + "minimum", false), (Egpws + "decide", false),
            (Egpws + "decision_height", false), (Egpws + "minimums", true));

        // Switching one off leaves the whole group off: only its own write.
        Check(Egpws + "radio_altimeter", false, (Egpws + "radio_altimeter", false));
        Check(Egpws + "decide", false, (Egpws + "decide", false));
        // The other callouts change only their own key.
        Check("options.pilot_callouts.v1", true, ("options.pilot_callouts.v1", true));
        Check(Egpws + "1000", true, (Egpws + "1000", true));
        Check(Egpws + "approaching_minimums", true, (Egpws + "approaching_minimums", true));
        Check(Egpws + "50_above", true, (Egpws + "50_above", true));
    }

    [Fact]
    public void The_exclusive_groups_are_the_tablet_s()
    {
        var groups = Bbs757EfbSettings.All.Where(s => s.ExclusiveGroup is not null)
            .GroupBy(s => s.ExclusiveGroup!)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Path).ToArray());

        Assert.Equal(new[] { "2500", "dh100", "minimums" }.Order(), groups.Keys.Order());
        Assert.Equal(new[] { Egpws + "2500", Egpws + "radio_altimeter" }, groups["2500"]);
        Assert.Equal(new[] { Egpws + "plus_hundred", Egpws + "approaching_dh" }, groups["dh100"]);
        Assert.Equal(new[]
        {
            Egpws + "minimums", Egpws + "minimums_minimums", Egpws + "minimum", Egpws + "decide", Egpws + "decision_height",
        }, groups["minimums"]);
    }

    [Fact]
    public void The_announcement_categories_follow_announcements_and_control_response_its_assist()
    {
        var follow = Bbs757EfbSettings.All.Where(s => s.EnabledBy is not null).ToDictionary(s => s.Path, s => s.EnabledBy);

        Assert.Equal(new[] { "easy_flight_authority", Cabin + "non_safety_announcements", Cabin + "safety_announcements" },
            follow.Keys.Order());
        Assert.Equal(Cabin + "announcements", follow[Cabin + "non_safety_announcements"]);
        Assert.Equal(Cabin + "announcements", follow[Cabin + "safety_announcements"]);
        Assert.Equal("easy_flight_enabled", follow["easy_flight_authority"]);
        Assert.Null(Setting(Cabin + "announcements").EnabledBy);
        Assert.Null(Setting("easy_flight_enabled").EnabledBy);
    }

    // ---- what a number or text box shows, and what it takes ---------------------------------------

    [Fact]
    public void Numbers_show_as_the_tablet_does()
    {
        var brightness = Setting("display_brightness");
        var tod = Setting("tod_pause_distance");
        var simbrief = Setting("simbrief_id");

        // The tablet shows floor(v * 100) percent: 0.99901 reads 99%.
        Assert.Equal("99", Bbs757EfbSettings.NumberText(brightness, Bbs757Value.OfFloat(0.9990133047103882)));
        Assert.Equal("51", Bbs757EfbSettings.NumberText(brightness, Bbs757Value.OfFloat(0.5105262994766235)));
        Assert.Equal("100", Bbs757EfbSettings.NumberText(brightness, Bbs757Value.OfFloat(1.0)));
        Assert.Equal("0", Bbs757EfbSettings.NumberText(brightness, Bbs757Value.OfFloat(0.0)));
        // A value outside the range is clamped, not shown as 140 or -5.
        Assert.Equal("100", Bbs757EfbSettings.NumberText(brightness, Bbs757Value.OfFloat(1.4)));
        Assert.Equal("0", Bbs757EfbSettings.NumberText(brightness, Bbs757Value.OfFloat(-0.05)));

        // Not set shows an empty box.
        Assert.Equal("", Bbs757EfbSettings.NumberText(tod, Bbs757Value.Of(-1L)));
        Assert.Equal("120", Bbs757EfbSettings.NumberText(tod, Bbs757Value.Of(120L)));
        Assert.Equal("", Bbs757EfbSettings.NumberText(simbrief, Bbs757Value.Of(-1L)));
        Assert.Equal("1234567", Bbs757EfbSettings.NumberText(simbrief, Bbs757Value.Of(1234567L)));

        // A text box shows its text, the pilot's code in plain.
        Assert.Equal("AB1", Bbs757EfbSettings.NumberText(Setting("hoppie_logon_code"), Bbs757Value.Of("AB1")));
        Assert.Equal("", Bbs757EfbSettings.NumberText(Setting("sayintentions_acars_key"), Bbs757Value.Of("")));
    }

    [Fact]
    public void Typed_numbers_are_checked_and_stored()
    {
        // TOD pause distance: a whole number from 1 to 999, or empty for off.
        Assert.Equal(Bbs757Value.Of(120L), Accepted("tod_pause_distance", "120"));
        Assert.Equal(Bbs757Value.Of(120L), Accepted("tod_pause_distance", "  120 "));
        Assert.Equal(Bbs757Value.Of(1L), Accepted("tod_pause_distance", "1"));
        Assert.Equal(Bbs757Value.Of(999L), Accepted("tod_pause_distance", "999"));
        Assert.Equal(Bbs757Value.Of(-1L), Accepted("tod_pause_distance", ""));
        Assert.Equal(Bbs757Value.Of(-1L), Accepted("tod_pause_distance", "   "));
        // The refusal says how to switch it off too: the tablet's switch is the empty box.
        foreach (var bad in new[] { "0", "12.5", "1000", "-5", "abc", "12 3", "99999999999999999999" })
            Assert.Equal("type a whole number from 1 to 999, or leave the box empty to switch it off",
                Refused("tod_pause_distance", bad));

        // EFB brightness: a whole percent, stored a hair above so the tablet shows it whether it floors or rounds.
        var p29 = Accepted("display_brightness", "29");
        Assert.Equal(Bbs757ValueType.Float, p29.Type);
        Assert.Equal(0.294, p29.Float, 12);
        Assert.Equal("29", Bbs757EfbSettings.NumberText(Setting("display_brightness"), p29));
        Assert.Equal(Bbs757Value.OfFloat(1.0), Accepted("display_brightness", "100"));
        Assert.Equal("type a whole number from 0 to 100", Refused("display_brightness", "101"));
        Assert.Equal("type a whole number from 0 to 100", Refused("display_brightness", "50.5"));
        Assert.Equal("type a whole number from 0 to 100", Refused("display_brightness", "-1"));
        Assert.Equal("type a whole number from 0 to 100", Refused("display_brightness", ""));

        // SimBrief ID: digits only, not 0, or empty for not set. "0" is digits, so the refusal says "not 0" too.
        Assert.Equal(Bbs757Value.Of(1234567L), Accepted("simbrief_id", " 1234567"));
        Assert.Equal(Bbs757Value.Of(-1L), Accepted("simbrief_id", ""));
        foreach (var bad in new[] { "12a", "-12", "12 34", "0", "000" })
            Assert.Equal("type a SimBrief ID number (digits, not 0)", Refused("simbrief_id", bad));

        // The two codes: the text as typed, trimmed.
        Assert.Equal(Bbs757Value.Of("AB1"), Accepted("hoppie_logon_code", "  AB1 "));
        Assert.Equal(Bbs757Value.Of(""), Accepted("hoppie_logon_code", ""));
        Assert.Equal(Bbs757Value.Of("A B-1"), Accepted("sayintentions_acars_key", "A B-1"));
    }

    [Fact]
    public void A_typed_percent_shows_the_same_whether_the_tablet_floors_or_rounds()
    {
        var brightness = Setting("display_brightness");

        for (int p = 0; p <= 100; p++)
        {
            string typed = p.ToString(CultureInfo.InvariantCulture);
            var stored = Accepted("display_brightness", typed);
            Assert.Equal(typed, Bbs757EfbSettings.NumberText(brightness, stored));
            Assert.Equal((double)p, Math.Floor(stored.Float * 100));
            Assert.Equal((double)p, Math.Round(stored.Float * 100, MidpointRounding.AwayFromZero));
        }
    }

    [Fact]
    public void Number_and_text_helpers_refuse_a_setting_of_another_kind()
    {
        foreach (var other in new[] { Setting("baro_sync"), Setting("loading_speed"), Setting("failures.random_multiplier") })
        {
            Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.NumberText(other, Bbs757Value.Of(1L)));
            Assert.Throws<ArgumentException>(() => Bbs757EfbSettings.TryParseEntry(other, "1", out _, out _));
        }
    }

    // ---- what is written, in any Windows culture --------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_slider_reads_and_is_written_with_a_decimal_point(string culture)
    {
        var frequency = Setting("failures.random_multiplier");

        var (text, json, step) = TestCulture.Run(culture, () =>
            (Bbs757EfbSettings.SliderText(frequency, Bbs757Value.OfFloat(2.5)), Bbs757Value.OfFloat(2.5).ToJson(),
                Bbs757EfbSettings.NearestStep(frequency, 8.54)));

        Assert.Equal("2.5x", text);
        Assert.Equal("2.5", json);
        Assert.Equal(9, step);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Floats_are_written_with_a_decimal_point(string culture)
    {
        Assert.Equal("1.0", TestCulture.Run(culture, () => Bbs757Value.OfFloat(1.0).ToJson()));
        Assert.Equal("2.5", TestCulture.Run(culture, () => Bbs757Value.OfFloat(2.5).ToJson()));
        Assert.Equal("0.294", TestCulture.Run(culture, () => Bbs757Value.OfFloat(0.294).ToJson()));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_negative_integer_is_written_with_the_plain_minus(string culture)
    {
        // sv-SE's own minus sign is U+2212: BlueBird's file must get the ASCII one ("not set" is -1).
        var (json, text) = TestCulture.Run(culture, () =>
            (Bbs757Value.Of(-1L).ToJson(), Bbs757EfbSettings.NumberText(Setting("tod_pause_distance"), Bbs757Value.Of(-5L))));

        Assert.Equal("-1", json);
        Assert.Equal("-5", text);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Refusal_reasons_name_their_numbers_with_no_decimals(string culture)
    {
        var reasons = TestCulture.Run(culture, () => new[]
        {
            Refused("tod_pause_distance", "0"),
            Refused("display_brightness", "101"),
            Refused("simbrief_id", "0"),
        });

        Assert.Equal(new[]
        {
            "type a whole number from 1 to 999, or leave the box empty to switch it off",
            "type a whole number from 0 to 100", "type a SimBrief ID number (digits, not 0)",
        }, reasons);
    }

    [Fact]
    public void An_empty_box_says_what_it_means()
    {
        // TOD pause distance's empty box is the tablet's switch turned off; the pilot's identifiers' is "not set".
        Assert.Equal("Leave empty to switch it off.", Bbs757EfbSettings.EmptyHint(Setting("tod_pause_distance")));
        foreach (string path in new[] { "simbrief_id", "hoppie_logon_code", "sayintentions_acars_key" })
            Assert.Equal("Leave empty for not set.", Bbs757EfbSettings.EmptyHint(Setting(path)));

        // A box that cannot be cleared has none, and nor has a switch or a choice: exactly the boxes with an empty value.
        Assert.Null(Bbs757EfbSettings.EmptyHint(Setting("display_brightness")));
        Assert.Null(Bbs757EfbSettings.EmptyHint(Setting("failures.random_multiplier")));
        Assert.Null(Bbs757EfbSettings.EmptyHint(Setting("baro_sync")));
        Assert.Null(Bbs757EfbSettings.EmptyHint(Setting("loading_speed")));
        Assert.All(Bbs757EfbSettings.All, s => Assert.True(
            (s.Kind is Bbs757SettingKind.Number or Bbs757SettingKind.Text && s.EmptyStored is not null)
                == (Bbs757EfbSettings.EmptyHint(s) is not null),
            s.Path));
    }

    // ---- the values themselves --------------------------------------------------------------------

    [Fact]
    public void Values_are_written_as_json()
    {
        Assert.Equal("true", Bbs757Value.Of(true).ToJson());
        Assert.Equal("false", Bbs757Value.Of(false).ToJson());
        Assert.Equal("12", Bbs757Value.Of(12L).ToJson());
        Assert.Equal("-1", Bbs757Value.Of(-1L).ToJson());
        Assert.Equal("1.0", Bbs757Value.OfFloat(1.0).ToJson());
        Assert.Equal("1000.0", Bbs757Value.OfFloat(1000.0).ToJson());
        Assert.Equal("0.294", Bbs757Value.OfFloat(0.294).ToJson());
        Assert.Equal("1E+20", Bbs757Value.OfFloat(1e20).ToJson());   // an exponent is a decimal number already
        Assert.Equal("\"abc\"", Bbs757Value.Of("abc").ToJson());
        Assert.Equal("\"\"", Bbs757Value.Of("").ToJson());

        // A float's text reads back as the very same number.
        foreach (double v in new[] { 0.294, 0.9990133047103882, 1.9952623149688795, 156.58, 1000.0, 1.0, 0.004, 1e-7, 123456789.123 })
            Assert.Equal(v, Json(Bbs757Value.OfFloat(v).ToJson()).GetDouble());
    }

    [Fact]
    public void A_float_that_is_not_finite_is_never_written_as_json()
    {
        // JSON has no NaN or infinity: "NaN.0" in BlueBird's file would not be JSON at all.
        foreach (double notFinite in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Throws<ArgumentException>(() => Bbs757Value.OfFloat(notFinite).ToJson());
    }

    [Fact]
    public void Text_is_json_escaped_and_readable()
    {
        Assert.Equal("\"A\\\"B\\\\C é\"", Bbs757Value.Of("A\"B\\C é").ToJson());
        // What it writes reads back as the same text.
        Assert.Equal("A\"B\\C é\n<&>", Json(Bbs757Value.Of("A\"B\\C é\n<&>").ToJson()).GetString());
    }

    [Fact]
    public void A_json_value_reads_back_only_as_a_type_it_fits()
    {
        Assert.Equal(Bbs757Value.Of(true), Bbs757Value.From(Json("true"), Bbs757ValueType.Bool));
        Assert.Equal(Bbs757Value.Of(false), Bbs757Value.From(Json("false"), Bbs757ValueType.Bool));
        Assert.Equal(Bbs757Value.Of(12L), Bbs757Value.From(Json("12"), Bbs757ValueType.Integer));
        Assert.Equal(Bbs757Value.Of(-1L), Bbs757Value.From(Json("-1"), Bbs757ValueType.Integer));
        Assert.Equal(Bbs757Value.Of(long.MaxValue), Bbs757Value.From(Json("9223372036854775807"), Bbs757ValueType.Integer));
        Assert.Equal(Bbs757Value.OfFloat(0.294), Bbs757Value.From(Json("0.294"), Bbs757ValueType.Float));
        Assert.Equal(Bbs757Value.OfFloat(1.0), Bbs757Value.From(Json("1"), Bbs757ValueType.Float));   // a stored 1 is a fine Float
        Assert.Equal(Bbs757Value.Of("A\"B"), Bbs757Value.From(Json("\"A\\\"B\""), Bbs757ValueType.Text));
        Assert.Equal(Bbs757Value.Of(""), Bbs757Value.From(Json("\"\""), Bbs757ValueType.Text));

        // Anything that does not fit gives null.
        Assert.Null(Bbs757Value.From(Json("1"), Bbs757ValueType.Bool));
        Assert.Null(Bbs757Value.From(Json("\"true\""), Bbs757ValueType.Bool));
        Assert.Null(Bbs757Value.From(Json("true"), Bbs757ValueType.Integer));
        Assert.Null(Bbs757Value.From(Json("true"), Bbs757ValueType.Float));
        Assert.Null(Bbs757Value.From(Json("\"1\""), Bbs757ValueType.Integer));
        Assert.Null(Bbs757Value.From(Json("\"1.5\""), Bbs757ValueType.Float));
        Assert.Null(Bbs757Value.From(Json("1"), Bbs757ValueType.Text));
        Assert.Null(Bbs757Value.From(Json("2.5"), Bbs757ValueType.Integer));
        Assert.Null(Bbs757Value.From(Json("9223372036854775808"), Bbs757ValueType.Integer));   // too big for a long
        Assert.Null(Bbs757Value.From(Json("1e999"), Bbs757ValueType.Float));   // too big for a double: never an infinity
        foreach (var type in Enum.GetValues<Bbs757ValueType>())
        {
            Assert.Null(Bbs757Value.From(Json("null"), type));
            Assert.Null(Bbs757Value.From(Json("{}"), type));
            Assert.Null(Bbs757Value.From(Json("[]"), type));
            Assert.Null(Bbs757Value.From(default, type));
        }
    }

    [Fact]
    public void Values_compare_by_what_they_hold()
    {
        Assert.Equal(Bbs757Value.Of(2L), Bbs757Value.Of(2L));
        Assert.Equal(Bbs757Value.Of("kg"), Bbs757Value.Of("kg"));
        Assert.NotEqual(Bbs757Value.Of(2L), Bbs757Value.OfFloat(2.0));
        Assert.NotEqual(Bbs757Value.Of(1L), Bbs757Value.Of(true));
        Assert.NotEqual(Bbs757Value.Of(0L), Bbs757Value.Of(false));
        Assert.NotEqual(Bbs757Value.Of(""), Bbs757Value.Of(false));
    }
}
