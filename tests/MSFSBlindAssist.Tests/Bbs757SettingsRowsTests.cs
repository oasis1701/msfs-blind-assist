using System.Text.Json;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What each control of BlueBird's Settings tab shows (<see cref="Bbs757SettingsRows"/>): for a section, the rows built
/// from the parsed <c>efb_settings.json</c> and the registrations' files. A row shows the tablet's word for the stored
/// value, "Other (stored value N)" for a value the tablet has no word for, "Differs between registrations",
/// "Differs between BlueBird's files" (Units, against the settings file's <c>units</c>),
/// "Not readable in this BlueBird version" or "Automatic", and says whether the control may change. Pure: parsed JSON in,
/// rows out, no files and no simulator. The fixtures are the settings file with made-up IDs and codes and
/// G-BBS75-opt.json as BlueBird wrote it; a test changes one by replacing a stored value in its text.
/// </summary>
public class Bbs757SettingsRowsTests
{
    private const string Egpws = "options.egpws_callouts.";
    private const string Cabin = "options.cabin_audio.";

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static readonly string SettingsJson = File.ReadAllText(Fixture("bbs757-efb-settings.json"));
    private static readonly string LiveryJson = File.ReadAllText(Fixture("bbs757-livery-opt.json"));

    private static Bbs757Setting Setting(string path)
    {
        var setting = Bbs757EfbSettings.ByPath(path);
        Assert.True(setting is not null, $"The catalog has no setting '{path}'.");
        return setting!;
    }

    /// <summary>Where the tablet's word stands among a setting's choices.</summary>
    private static int IndexOf(string path, string word) =>
        Setting(path).Choices.Select((choice, index) => (choice.Word, index)).Single(c => c.Word == word).index;

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// <paramref name="json"/> with <paramref name="old"/> replaced by <paramref name="replacement"/>. The text must occur
    /// exactly once, so a typo cannot leave a test changing nothing.
    /// </summary>
    private static string Replaced(string json, string old, string replacement)
    {
        int first = json.IndexOf(old, StringComparison.Ordinal);
        Assert.True(first >= 0 && json.IndexOf(old, first + 1, StringComparison.Ordinal) < 0,
            $"'{old}' must occur exactly once in the fixture.");
        return json.Replace(old, replacement);
    }

    /// <summary>
    /// The rows of a section, built from the settings file's JSON (null: no settings file could be read) and from each
    /// registration's JSON.
    /// </summary>
    private static IReadOnlyList<Bbs757SettingRow> RowsOf(Bbs757SettingSection section, string? settings,
        params string[] registrations)
    {
        JsonElement? global = settings is null ? null : Json(settings);
        return Bbs757SettingsRows.For(section, global, registrations.Select(Json).ToArray());
    }

    private static Bbs757SettingRow RowFor(IReadOnlyList<Bbs757SettingRow> rows, string path) =>
        rows.Single(row => row.Setting.Path == path);

    // ---- a value the tablet has a word for --------------------------------------------------------

    [Fact]
    public void A_known_value_selects_its_choice()
    {
        var rows = RowsOf(Bbs757SettingSection.Aircraft, SettingsJson);   // loading_speed 2, automatic_loading false, baro_sync true

        var speed = RowFor(rows, "loading_speed");
        Assert.Equal(Bbs757RowState.Value, speed.State);
        Assert.Equal(IndexOf("loading_speed", "Quick"), speed.ChoiceIndex);
        Assert.Equal("Quick", speed.Text);
        Assert.False(speed.Inactive);
        Assert.True(speed.CanEdit(true));
        Assert.Same(Setting("loading_speed"), speed.Setting);

        // A switch selects Off, its first choice, or On, its second.
        var loading = RowFor(rows, "automatic_loading");
        Assert.Equal((Bbs757RowState.Value, 0, "Off"), (loading.State, loading.ChoiceIndex, loading.Text));
        var baro = RowFor(rows, "baro_sync");
        Assert.Equal((Bbs757RowState.Value, 1, "On"), (baro.State, baro.ChoiceIndex, baro.Text));

        // Two words for a true or false value: the one stored as true is the tablet's first.
        var lighting = RowFor(rows, "automatic_cockpit_lighting");   // true
        Assert.Equal((Bbs757RowState.Value, 0, "Automatic"), (lighting.State, lighting.ChoiceIndex, lighting.Text));
        var tiller = RowFor(rows, "tiller_realistic");               // false
        Assert.Equal((Bbs757RowState.Value, 1, "Rudder"), (tiller.State, tiller.ChoiceIndex, tiller.Text));
    }

    [Fact]
    public void The_settings_file_reads_as_the_tablet_would_show_it()
    {
        var shown = new Dictionary<string, string>
        {
            // General
            ["force_keyboard_capture"] = "Off",
            ["efb_power_on_during_init"] = "On",
            ["auto_brightness"] = "Auto",
            ["display_brightness"] = "Automatic",
            ["background_num"] = "Default",
            ["autobrake_notifications"] = "On",
            ["notification_popback_seconds"] = "5 sec",
            ["ui_theme"] = "Dark blue",
            // Aircraft
            ["easy_flight_enabled"] = "Off",
            ["easy_flight_authority"] = "50",
            ["automatic_cockpit_lighting"] = "Automatic",
            ["tod_pause_distance"] = "0",
            ["automatic_step_climbs"] = "Disabled",
            ["tiller_realistic"] = "Rudder",
            ["throttle_lockout_enabled"] = "Off",
            ["displays_bootup_realistic"] = "Fast",
            ["baro_sync"] = "On",
            ["cockpit_door_initial_open"] = "Closed",
            ["parking_brake_realistic"] = "Easy",
            ["irs_align_time"] = "Instant",
            ["loading_speed"] = "Quick",
            ["automatic_loading"] = "Off",
            ["auto_connect_ground_services_on_init"] = "On",
            ["auto_attach_chocks_on_init"] = "On",
            ["dirty_panel_states_enabled"] = "Off",
            ["ground_services_mode"] = "BBS",
            // AIRAC data
            ["simbrief_id"] = "123456",
            // ACARS
            ["hoppie_user_enabled"] = "Off",
            ["cpdlc_network"] = "Hoppie",
            ["hoppie_logon_code"] = "NOT-A-REAL-CODE",
            ["sayintentions_acars_key"] = "NOT-A-REAL-KEY",
            // Failures: the slider reads as the tablet's "FREQUENCY: 1.0x".
            ["failures.random_multiplier"] = "1.0x",
            ["failures.random_enabled"] = "Off",
            ["failures.notifications_enabled"] = "On",
        };

        var rows = new[]
            {
                Bbs757SettingSection.General, Bbs757SettingSection.Aircraft, Bbs757SettingSection.AiracData,
                Bbs757SettingSection.Acars, Bbs757SettingSection.Failures,
            }
            .SelectMany(section => RowsOf(section, SettingsJson))
            .ToArray();

        // Every row of these sections is in the table, and each one reads as the table says. Only the brightness is
        // Automatic: the file's mode is automatic. Only Control response is inactive (Adaptive handling assist is off, and the
        // tablet then hides it), and every row but the brightness may change.
        Assert.Equal(shown.Keys.Order(), rows.Select(row => row.Setting.Path).Order());
        var mismatches = rows
            .Where(row => row.Text != shown[row.Setting.Path]
                || row.State != (row.Setting.Path == "display_brightness" ? Bbs757RowState.Automatic : Bbs757RowState.Value)
                || row.Inactive != (row.Setting.Path == "easy_flight_authority")
                || row.CanEdit(true) != (row.Setting.Path is not ("display_brightness" or "easy_flight_authority")))
            .Select(row => $"{row.Setting.Path}: {row.State} '{row.Text}' (inactive {row.Inactive})");
        Assert.Empty(mismatches);
    }

    [Fact]
    public void A_file_from_before_BlueBirds_update_reads_the_new_rows_as_unreadable_only()
    {
        // A pilot who has not installed BlueBird's 2026-10-31 update has none of its keys: those rows cannot be read or changed,
        // and every other row reads as before.
        string[] added = { "throttle_lockout_enabled", "easy_flight_enabled", "easy_flight_authority", "automatic_step_climbs" };
        string older = SettingsJson.Replace(
            ",\"ai_dispatch_enabled\":false,\"throttle_lockout_enabled\":false,\"easy_flight_enabled\":false,\"easy_flight_authority\":50,\"automatic_step_climbs\":false",
            "", StringComparison.Ordinal);
        Assert.NotEqual(SettingsJson, older);

        var now = RowsOf(Bbs757SettingSection.Aircraft, SettingsJson);
        var before = RowsOf(Bbs757SettingSection.Aircraft, older);

        foreach (var row in before)
        {
            if (added.Contains(row.Setting.Path))
            {
                Assert.Equal(Bbs757RowState.Unreadable, row.State);
                Assert.False(row.CanEdit(true), row.Setting.Path);
            }
            else
                Assert.Equal(RowFor(now, row.Setting.Path), row);
        }
    }

    [Fact]
    public void The_registration_file_reads_as_the_tablet_would_show_it()
    {
        var livery = RowsOf(Bbs757SettingSection.LiveryOptions, null, LiveryJson);
        Assert.Equal(new[] { "Imperial", "HDG", "Option 1" }, livery.Select(row => row.Text));
        Assert.All(livery, row => Assert.Equal(Bbs757RowState.Value, row.State));
        Assert.Equal(new[]
        {
            IndexOf("options.general.weight_unit", "Imperial"),
            IndexOf("options.ehsi.up_reference", "HDG"),
            IndexOf("options.general.vnav_takeoff_activation", "Option 1"),
        }, livery.Select(row => row.ChoiceIndex));

        // Each callout and cabin audio switch shows what the file itself holds for its key: On for true, Off for false.
        using var document = JsonDocument.Parse(LiveryJson);
        foreach (var section in new[] { Bbs757SettingSection.Callouts, Bbs757SettingSection.CabinAudio })
        {
            var rows = RowsOf(section, null, LiveryJson);
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                JsonElement held = document.RootElement;
                foreach (string key in row.Setting.Path.Split('.')) held = held.GetProperty(key);
                Assert.Equal(held.GetBoolean() ? "On" : "Off", row.Text);
                Assert.Equal(held.GetBoolean() ? 1 : 0, row.ChoiceIndex);
                Assert.Equal(Bbs757RowState.Value, row.State);
                Assert.False(row.Inactive);
            }
        }

        // The fixture has both words among the callouts, so the check above looked at both.
        var callouts = RowsOf(Bbs757SettingSection.Callouts, null, LiveryJson);
        Assert.Equal("Off", RowFor(callouts, Egpws + "radio_altimeter").Text);
        Assert.Equal("On", RowFor(callouts, Egpws + "2500").Text);
    }

    [Fact]
    public void A_section_gives_its_settings_in_the_tablets_order()
    {
        foreach (var section in Enum.GetValues<Bbs757SettingSection>())
        {
            var expected = Bbs757EfbSettings.InSection(section);
            var rows = RowsOf(section, SettingsJson, LiveryJson);

            Assert.NotEmpty(expected);
            Assert.Equal(expected.Count, rows.Count);
            for (int i = 0; i < expected.Count; i++) Assert.Same(expected[i], rows[i].Setting);
        }
    }

    // ---- a value the tablet has no word for ---------------------------------------------------------

    [Fact]
    public void An_unknown_value_is_other()
    {
        string json = Replaced(SettingsJson, "\"irs_align_time\":0", "\"irs_align_time\":77");

        var row = RowFor(RowsOf(Bbs757SettingSection.Aircraft, json), "irs_align_time");

        Assert.Equal(Bbs757RowState.Other, row.State);
        Assert.Equal(-1, row.ChoiceIndex);
        Assert.Equal("Other (stored value 77)", row.Text);
        Assert.False(row.Inactive);
        Assert.True(row.CanEdit(true));      // the pilot may pick a value BlueBird stores
        Assert.False(row.CanEdit(false));

        // Text is quoted as the file holds it, from a registration's file too.
        string stone = Replaced(LiveryJson, "\"weight_unit\":\"lbs\"", "\"weight_unit\":\"stone\"");
        var units = RowFor(RowsOf(Bbs757SettingSection.LiveryOptions, null, stone), "options.general.weight_unit");
        Assert.Equal((Bbs757RowState.Other, -1, "Other (stored value \"stone\")"), (units.State, units.ChoiceIndex, units.Text));
        Assert.True(units.CanEdit(true));

        // Registrations that all hold the same unknown value show it too.
        units = RowFor(RowsOf(Bbs757SettingSection.LiveryOptions, null, stone, stone), "options.general.weight_unit");
        Assert.Equal((Bbs757RowState.Other, "Other (stored value \"stone\")"), (units.State, units.Text));
    }

    [Fact]
    public void A_number_or_text_box_shows_whatever_is_stored_and_is_never_other()
    {
        // Past the 1 to 999 the app takes for a typed distance, an unset -1 and an empty code: each is shown as stored.
        string json = Replaced(Replaced(Replaced(SettingsJson,
            "\"tod_pause_distance\":0", "\"tod_pause_distance\":5000"),
            "\"simbrief_id\":123456", "\"simbrief_id\":-1"),
            "\"hoppie_logon_code\":\"NOT-A-REAL-CODE\"", "\"hoppie_logon_code\":\"\"");

        var aircraft = RowsOf(Bbs757SettingSection.Aircraft, json);
        var boxes = RowsOf(Bbs757SettingSection.AiracData, json)
            .Concat(RowsOf(Bbs757SettingSection.Acars, json).Where(row => row.Setting.Kind == Bbs757SettingKind.Text))
            .ToArray();

        var distance = RowFor(aircraft, "tod_pause_distance");
        Assert.Equal((Bbs757RowState.Value, -1, "5000"), (distance.State, distance.ChoiceIndex, distance.Text));
        // "Not set" is the tablet's empty box.
        Assert.Equal("", RowFor(boxes, "simbrief_id").Text);
        Assert.Equal("", RowFor(boxes, "hoppie_logon_code").Text);
        Assert.Equal(3, boxes.Length);
        Assert.All(boxes, row =>
        {
            Assert.Equal(Bbs757RowState.Value, row.State);
            Assert.Equal(-1, row.ChoiceIndex);
            Assert.True(row.CanEdit(true));
        });
    }

    [Fact]
    public void The_slider_reads_the_stored_value_at_its_nearest_step()
    {
        static Bbs757SettingRow Frequency(string stored) => RowFor(RowsOf(Bbs757SettingSection.Failures,
            Replaced(SettingsJson, "\"random_multiplier\":1.0", $"\"random_multiplier\":{stored}")), "failures.random_multiplier");

        // A step reads as the tablet shows it, at its own place.
        var step = Frequency("2.5");
        Assert.Equal((Bbs757RowState.Value, 4, "2.5x"), (step.State, step.ChoiceIndex, step.Text));
        Assert.True(step.CanEdit(true));
        // A value set on the tablet between steps reads as it is, at the nearest step: 8.54 is 8x (step 9).
        var between = Frequency("8.54");
        Assert.Equal((Bbs757RowState.Value, 9, "8.5x"), (between.State, between.ChoiceIndex, between.Text));
        // A whole number in the file is still a number BlueBird can hold.
        Assert.Equal("1000.0x", Frequency("1000").Text);
        // Text where the number belongs cannot be read.
        Assert.Equal(Bbs757RowState.Unreadable, Frequency("\"2.5\"").State);
    }

    // ---- what cannot be read ------------------------------------------------------------------------

    [Fact]
    public void A_missing_or_wrong_type_key_is_unreadable()
    {
        // In the settings file: a whole number kept as text, one with a fraction, a switch kept as a number, a null and a
        // list, and the key the tablet writes missing. Every other key reads as before.
        string json = Replaced(Replaced(Replaced(Replaced(Replaced(SettingsJson,
            "\"loading_speed\":2", "\"loading_speed\":\"2\""),
            "\"irs_align_time\":0", "\"irs_align_time\":2.5"),
            "\"baro_sync\":true", "\"baro_sync\":1"),
            "\"cpdlc_network\":0", "\"cpdlc_network\":null"),
            "\"ground_services_mode\":0", "\"ground_services_mode\":[0]");
        json = Replaced(json, "\"automatic_loading\":false,", "");

        var rows = RowsOf(Bbs757SettingSection.Aircraft, json).Concat(RowsOf(Bbs757SettingSection.Acars, json)).ToArray();

        string[] unreadable =
        [
            "loading_speed", "irs_align_time", "baro_sync", "cpdlc_network", "ground_services_mode", "automatic_loading",
        ];
        foreach (string path in unreadable)
        {
            var row = RowFor(rows, path);
            Assert.Equal((Bbs757RowState.Unreadable, -1, "Not readable in this BlueBird version"), (row.State, row.ChoiceIndex, row.Text));
            Assert.False(row.Inactive);
            Assert.False(row.CanEdit(true), path);
        }
        Assert.Equal(Bbs757RowState.Value, RowFor(rows, "tiller_realistic").State);
        Assert.Equal("Rudder", RowFor(rows, "tiller_realistic").Text);

        // A file with none of the keys: every row.
        Assert.All(RowsOf(Bbs757SettingSection.Aircraft, "{}"), row =>
        {
            Assert.Equal(Bbs757RowState.Unreadable, row.State);
            Assert.Equal(Bbs757SettingsRows.UnreadableText, row.Text);
            Assert.False(row.CanEdit(true));
        });
    }

    [Fact]
    public void A_registration_key_of_the_wrong_type_is_unreadable()
    {
        string wrong = Replaced(Replaced(LiveryJson,
            "\"weight_unit\":\"lbs\"", "\"weight_unit\":5"),
            "\"v1\":true", "\"v1\":\"yes\"");

        var livery = RowsOf(Bbs757SettingSection.LiveryOptions, null, wrong);
        var callouts = RowsOf(Bbs757SettingSection.Callouts, null, wrong);

        Assert.Equal(Bbs757RowState.Unreadable, RowFor(livery, "options.general.weight_unit").State);
        Assert.Equal(Bbs757RowState.Unreadable, RowFor(callouts, "options.pilot_callouts.v1").State);
        Assert.Equal(Bbs757RowState.Value, RowFor(callouts, "options.pilot_callouts.rotate").State);
    }

    [Fact]
    public void Without_a_readable_settings_file_every_global_row_is_unreadable()
    {
        foreach (var section in new[]
                 {
                     Bbs757SettingSection.General, Bbs757SettingSection.Aircraft, Bbs757SettingSection.AiracData,
                     Bbs757SettingSection.Failures,
                 })
        {
            // Registrations do not make up for it, and nothing is Automatic: with no file the mode is not known to be anything.
            var rows = RowsOf(section, null, LiveryJson);

            Assert.NotEmpty(rows);
            Assert.All(rows, row =>
            {
                Assert.Equal((Bbs757RowState.Unreadable, -1, Bbs757SettingsRows.UnreadableText), (row.State, row.ChoiceIndex, row.Text));
                // Control response waits for Adaptive handling assist, which does not read On either.
                Assert.Equal(row.Setting.Path == "easy_flight_authority", row.Inactive);
                Assert.False(row.CanEdit(true), row.Setting.Path);
            });
        }

        // The registrations' rows do not need that file.
        Assert.Equal("Imperial", RowFor(RowsOf(Bbs757SettingSection.LiveryOptions, null, LiveryJson), "options.general.weight_unit").Text);
    }

    // ---- brightness ---------------------------------------------------------------------------------

    [Fact]
    public void Brightness_reads_automatic_while_the_mode_is_auto()
    {
        // The fixture's mode is automatic: the stored 0.8 is not the tablet's brightness then.
        var auto = RowFor(RowsOf(Bbs757SettingSection.General, SettingsJson), "display_brightness");

        Assert.Equal((Bbs757RowState.Automatic, -1, "Automatic"), (auto.State, auto.ChoiceIndex, auto.Text));
        Assert.False(auto.Inactive);
        Assert.False(auto.CanEdit(true));
        Assert.False(auto.CanEdit(false));

        // The mode's own row says "Auto" ...
        var autoMode = RowFor(RowsOf(Bbs757SettingSection.General, SettingsJson), "auto_brightness");
        Assert.Equal((Bbs757RowState.Value, IndexOf("auto_brightness", "Auto"), "Auto"), (autoMode.State, autoMode.ChoiceIndex, autoMode.Text));

        // ... and once it is manual the brightness is the stored percent (floored: 0.51 is 51) and may change.
        string manualFile = Replaced(Replaced(SettingsJson,
            "\"auto_brightness\":true", "\"auto_brightness\":false"),
            "\"display_brightness\":0.8", "\"display_brightness\":0.51");
        var general = RowsOf(Bbs757SettingSection.General, manualFile);

        var brightness = RowFor(general, "display_brightness");
        Assert.Equal((Bbs757RowState.Value, -1, "51"), (brightness.State, brightness.ChoiceIndex, brightness.Text));
        Assert.True(brightness.CanEdit(true));
        Assert.False(brightness.CanEdit(false));
        var mode = RowFor(general, "auto_brightness");
        Assert.Equal((Bbs757RowState.Value, IndexOf("auto_brightness", "Manual"), "Manual"), (mode.State, mode.ChoiceIndex, mode.Text));
    }

    [Fact]
    public void Brightness_may_be_written_only_while_the_mode_is_known_to_be_manual()
    {
        // The mode missing, of another type or null: not known to be manual, so the row is Automatic and cannot change.
        foreach (string mode in new[] { "", "\"auto_brightness\":\"false\",", "\"auto_brightness\":0,", "\"auto_brightness\":null," })
        {
            string json = "{" + mode + "\"display_brightness\":0.51}";

            var row = RowFor(RowsOf(Bbs757SettingSection.General, json), "display_brightness");

            Assert.Equal((Bbs757RowState.Automatic, "Automatic"), (row.State, row.Text));
            Assert.False(row.CanEdit(true), json);
        }

        // Manual, but the brightness itself missing or of another type: nothing to show, nothing to change.
        foreach (string brightness in new[] { "", ",\"display_brightness\":\"0.5\"", ",\"display_brightness\":null" })
        {
            string json = "{\"auto_brightness\":false" + brightness + "}";

            var row = RowFor(RowsOf(Bbs757SettingSection.General, json), "display_brightness");

            Assert.Equal((Bbs757RowState.Unreadable, Bbs757SettingsRows.UnreadableText), (row.State, row.Text));
            Assert.False(row.CanEdit(true), json);
        }
    }

    // ---- registrations ------------------------------------------------------------------------------

    [Fact]
    public void Registrations_that_agree_show_the_value_and_ones_that_differ_say_so()
    {
        string quiet = Replaced(LiveryJson, "\"80\":true", "\"80\":false");   // the 80 callout off in this one only

        // Two registrations that agree read as one.
        foreach (var section in new[] { Bbs757SettingSection.LiveryOptions, Bbs757SettingSection.Callouts, Bbs757SettingSection.CabinAudio })
        {
            var one = RowsOf(section, null, LiveryJson);
            var two = RowsOf(section, null, LiveryJson, LiveryJson);
            Assert.Equal(one.Select(r => (r.State, r.ChoiceIndex, r.Text, r.Inactive)), two.Select(r => (r.State, r.ChoiceIndex, r.Text, r.Inactive)));
        }

        // One that differs: the row says so, and the others, which agree, keep their value.
        foreach (var registrations in new[]
                 {
                     new[] { LiveryJson, quiet },
                     new[] { quiet, LiveryJson },
                     new[] { LiveryJson, quiet, LiveryJson },
                 })
        {
            var rows = RowsOf(Bbs757SettingSection.Callouts, null, registrations);

            var differs = RowFor(rows, Egpws + "80");
            Assert.Equal((Bbs757RowState.Differs, -1, "Differs between registrations"), (differs.State, differs.ChoiceIndex, differs.Text));
            Assert.False(differs.Inactive);
            Assert.True(differs.CanEdit(true));     // setting one value for all registrations
            Assert.False(differs.CanEdit(false));

            var agrees = RowFor(rows, Egpws + "60");
            Assert.Equal((Bbs757RowState.Value, 1, "On"), (agrees.State, agrees.ChoiceIndex, agrees.Text));
            Assert.Single(rows, row => row.State == Bbs757RowState.Differs);
        }

        // A known value in one registration and an unknown one in another differ too.
        string stone = Replaced(LiveryJson, "\"weight_unit\":\"lbs\"", "\"weight_unit\":\"stone\"");
        Assert.Equal(Bbs757RowState.Differs,
            RowFor(RowsOf(Bbs757SettingSection.LiveryOptions, null, LiveryJson, stone), "options.general.weight_unit").State);
    }

    [Fact]
    public void No_registrations_make_the_rows_unreadable()
    {
        foreach (var section in new[] { Bbs757SettingSection.LiveryOptions, Bbs757SettingSection.Callouts, Bbs757SettingSection.CabinAudio })
        {
            // None at all (with a settings file read), and some that hold none of the section's keys.
            foreach (var rows in new[]
                     {
                         RowsOf(section, SettingsJson),
                         RowsOf(section, SettingsJson, "{}", "{\"options\":{}}"),
                     })
            {
                Assert.NotEmpty(rows);
                Assert.All(rows, row =>
                {
                    Assert.Equal((Bbs757RowState.Unreadable, -1, "Not readable in this BlueBird version"), (row.State, row.ChoiceIndex, row.Text));
                    Assert.False(row.CanEdit(true), row.Setting.Path);
                });
            }
        }

        // The settings file's own rows are not affected by there being no registration.
        Assert.DoesNotContain(RowsOf(Bbs757SettingSection.Aircraft, SettingsJson), row => row.State == Bbs757RowState.Unreadable);
    }

    [Fact]
    public void A_registration_without_the_key_does_not_change_what_the_others_show()
    {
        string noUnit = Replaced(LiveryJson, "\"weight_unit\":\"lbs\",", "");                      // the key missing
        string numberUnit = Replaced(LiveryJson, "\"weight_unit\":\"lbs\"", "\"weight_unit\":5"); // the wrong type
        const string WeightUnit = "options.general.weight_unit";

        foreach (string lacking in new[] { noUnit, numberUnit, "{}", "[]", "5", "null" })
        {
            // Read in either order, the registration with the key decides; with it alone nothing is.
            Assert.Equal((Bbs757RowState.Value, "Imperial"),
                RowSummary(RowsOf(Bbs757SettingSection.LiveryOptions, null, LiveryJson, lacking), WeightUnit));
            Assert.Equal((Bbs757RowState.Value, "Imperial"),
                RowSummary(RowsOf(Bbs757SettingSection.LiveryOptions, null, lacking, LiveryJson), WeightUnit));
            Assert.Equal((Bbs757RowState.Unreadable, Bbs757SettingsRows.UnreadableText),
                RowSummary(RowsOf(Bbs757SettingSection.LiveryOptions, null, lacking), WeightUnit));
        }
    }

    [Fact]
    public void Units_read_from_the_registrations()
    {
        const string WeightUnit = "options.general.weight_unit";

        var imperial = RowFor(RowsOf(Bbs757SettingSection.LiveryOptions, null, LiveryJson), WeightUnit);
        Assert.Equal((Bbs757RowState.Value, IndexOf(WeightUnit, "Imperial"), "Imperial"), (imperial.State, imperial.ChoiceIndex, imperial.Text));
        Assert.True(imperial.CanEdit(true));

        string kilos = Replaced(LiveryJson, "\"weight_unit\":\"lbs\"", "\"weight_unit\":\"kg\"");
        var metric = RowFor(RowsOf(Bbs757SettingSection.LiveryOptions, null, kilos), WeightUnit);
        Assert.Equal((Bbs757RowState.Value, IndexOf(WeightUnit, "Metric"), "Metric"), (metric.State, metric.ChoiceIndex, metric.Text));

        // A second registration with "kg": the registrations differ.
        var differs = RowFor(RowsOf(Bbs757SettingSection.LiveryOptions, null, LiveryJson, kilos), WeightUnit);
        Assert.Equal((Bbs757RowState.Differs, -1, Bbs757SettingsRows.DiffersText), (differs.State, differs.ChoiceIndex, differs.Text));
        Assert.True(differs.CanEdit(true));

        // The value shown is the registrations' own; the settings file's `units` only says whether the two files agree.
        string metricGlobal = Replaced(SettingsJson, "\"units\":1", "\"units\":0");
        Assert.Equal("Imperial", RowFor(RowsOf(Bbs757SettingSection.LiveryOptions, SettingsJson, LiveryJson), WeightUnit).Text);
        Assert.Equal("Metric", RowFor(RowsOf(Bbs757SettingSection.LiveryOptions, metricGlobal, kilos), WeightUnit).Text);
    }

    [Fact]
    public void Units_differ_when_the_settings_file_holds_the_other_unit_s_units()
    {
        const string WeightUnit = "options.general.weight_unit";
        string kilos = Replaced(LiveryJson, "\"weight_unit\":\"lbs\"", "\"weight_unit\":\"kg\"");
        string metricGlobal = Replaced(SettingsJson, "\"units\":1", "\"units\":0");

        // The registrations agree on pounds while the settings file's `units` is 0, metric's (and the other way round):
        // BlueBird's two files disagree, which the tablet's one UNITS switch never leaves them in.
        foreach (var (global, registrations) in new[]
                 {
                     (metricGlobal, new[] { LiveryJson }),
                     (metricGlobal, new[] { LiveryJson, LiveryJson }),
                     (SettingsJson, new[] { kilos }),
                     // A units value the tablet never stores is another value too.
                     (Replaced(SettingsJson, "\"units\":1", "\"units\":7"), new[] { LiveryJson }),
                 })
        {
            var row = RowFor(RowsOf(Bbs757SettingSection.LiveryOptions, global, registrations), WeightUnit);

            Assert.Equal((Bbs757RowState.Differs, -1, "Differs between BlueBird's files"), (row.State, row.ChoiceIndex, row.Text));
            Assert.Equal(Bbs757SettingsRows.LinkedDiffersText, row.Text);
            Assert.False(row.Inactive);
            Assert.True(row.CanEdit(true));     // setting Units writes both files again
            Assert.False(row.CanEdit(false));
        }

        // Registrations that differ among themselves keep their own text, whatever the settings file holds.
        Assert.Equal((Bbs757RowState.Differs, Bbs757SettingsRows.DiffersText),
            RowSummary(RowsOf(Bbs757SettingSection.LiveryOptions, metricGlobal, LiveryJson, kilos), WeightUnit));
        Assert.Equal((Bbs757RowState.Differs, Bbs757SettingsRows.DiffersText),
            RowSummary(RowsOf(Bbs757SettingSection.LiveryOptions, SettingsJson, LiveryJson, kilos), WeightUnit));

        // A unit no word stores has no `units` of its own to compare: it stays Other.
        string stone = Replaced(LiveryJson, "\"weight_unit\":\"lbs\"", "\"weight_unit\":\"stone\"");
        Assert.Equal((Bbs757RowState.Other, "Other (stored value \"stone\")"),
            RowSummary(RowsOf(Bbs757SettingSection.LiveryOptions, metricGlobal, stone), WeightUnit));

        // Only Units has a linked key: the other Livery options rows read the registrations alone.
        var livery = RowsOf(Bbs757SettingSection.LiveryOptions, metricGlobal, LiveryJson);
        Assert.Equal((Bbs757RowState.Value, "HDG"), RowSummary(livery, "options.ehsi.up_reference"));
        Assert.Equal((Bbs757RowState.Value, "Option 1"), RowSummary(livery, "options.general.vnav_takeoff_activation"));
    }

    [Fact]
    public void Units_show_the_registrations_when_the_settings_file_s_units_does_not_read()
    {
        const string WeightUnit = "options.general.weight_unit";

        // No settings file read, `units` missing, and `units` of another type: nothing to compare with, so the row
        // shows what the registrations agree on.
        foreach (string? global in new[]
                 {
                     null,
                     Replaced(SettingsJson, ",\"units\":1", ""),
                     Replaced(SettingsJson, "\"units\":1", "\"units\":\"0\""),
                     Replaced(SettingsJson, "\"units\":1", "\"units\":0.5"),
                     Replaced(SettingsJson, "\"units\":1", "\"units\":null"),
                 })
        {
            Assert.Equal((Bbs757RowState.Value, "Imperial"),
                RowSummary(RowsOf(Bbs757SettingSection.LiveryOptions, global, LiveryJson), WeightUnit));
        }
    }

    private static (Bbs757RowState, string) RowSummary(IReadOnlyList<Bbs757SettingRow> rows, string path)
    {
        var row = RowFor(rows, path);
        return (row.State, row.Text);
    }

    // ---- the announcement categories ----------------------------------------------------------------

    [Fact]
    public void An_announcement_category_is_inactive_unless_announcements_is_on_everywhere()
    {
        string announcementsOff = Replaced(LiveryJson, "\"announcements\":true", "\"announcements\":false");
        string[] categories = [Cabin + "safety_announcements", Cabin + "non_safety_announcements"];
        int on = IndexOf(categories[0], "On");

        // Announcements off: the categories keep their own stored value and show it, but cannot change.
        var off = RowsOf(Bbs757SettingSection.CabinAudio, null, announcementsOff);
        foreach (string path in categories)
        {
            var row = RowFor(off, path);
            Assert.True(row.Inactive, path);
            Assert.Equal((Bbs757RowState.Value, on, "On"), (row.State, row.ChoiceIndex, row.Text));
            Assert.False(row.CanEdit(true), path);
            Assert.False(row.CanEdit(false), path);
        }
        // Announcements itself, and the rows that do not follow it, are not inactive.
        Assert.All(off.Where(row => !categories.Contains(row.Setting.Path)), row =>
        {
            Assert.False(row.Inactive, row.Setting.Path);
            Assert.True(row.CanEdit(true), row.Setting.Path);
        });
        var announcements = RowFor(off, Cabin + "announcements");
        Assert.Equal((Bbs757RowState.Value, IndexOf(Cabin + "announcements", "Off")), (announcements.State, announcements.ChoiceIndex));

        // Announcements on: the categories change like any other row.
        var all = RowsOf(Bbs757SettingSection.CabinAudio, null, LiveryJson);
        foreach (string path in categories)
        {
            var row = RowFor(all, path);
            Assert.False(row.Inactive, path);
            Assert.True(row.CanEdit(true), path);
        }

        // Two registrations, announcements off in one: the tablet's lock holds in that one, so the categories wait.
        foreach (var registrations in new[] { new[] { LiveryJson, announcementsOff }, new[] { announcementsOff, LiveryJson } })
        {
            var mixed = RowsOf(Bbs757SettingSection.CabinAudio, null, registrations);
            Assert.Equal(Bbs757RowState.Differs, RowFor(mixed, Cabin + "announcements").State);
            foreach (string path in categories)
            {
                Assert.True(RowFor(mixed, path).Inactive, path);
                Assert.False(RowFor(mixed, path).CanEdit(true), path);
            }
        }

        // Announcements not readable anywhere: nothing says it is on.
        foreach (var rows in new[] { RowsOf(Bbs757SettingSection.CabinAudio, null), RowsOf(Bbs757SettingSection.CabinAudio, null, "{}") })
            foreach (string path in categories)
                Assert.True(RowFor(rows, path).Inactive, path);

        // What the pilot is told.
        Assert.Equal("Turn on Announcements to change this.",
            Bbs757SettingsRows.InactiveText(Bbs757EfbSettings.ByPath("options.cabin_audio.announcements")!));
    }

    [Fact]
    public void A_registration_without_announcements_does_not_switch_the_categories_off()
    {
        // The others say On, and a file that lacks the key says nothing (a bad file is reported when a save reaches it).
        string without = Replaced(LiveryJson, "\"announcements\":true,", "");

        var rows = RowsOf(Bbs757SettingSection.CabinAudio, null, LiveryJson, without);

        Assert.Equal(Bbs757RowState.Value, RowFor(rows, Cabin + "announcements").State);
        Assert.False(RowFor(rows, Cabin + "safety_announcements").Inactive);
        Assert.False(RowFor(rows, Cabin + "non_safety_announcements").Inactive);
        // With that file alone, announcements are not readable.
        Assert.True(RowFor(RowsOf(Bbs757SettingSection.CabinAudio, null, without), Cabin + "safety_announcements").Inactive);
    }

    [Fact]
    public void Only_the_announcement_categories_and_control_response_follow_another_setting()
    {
        // The fixture's Adaptive handling assist is off: Control response waits for it as the categories wait for Announcements.
        var rows = new[]
            {
                Bbs757SettingSection.General, Bbs757SettingSection.Aircraft, Bbs757SettingSection.AiracData,
                Bbs757SettingSection.Failures, Bbs757SettingSection.LiveryOptions, Bbs757SettingSection.Callouts,
                Bbs757SettingSection.CabinAudio,
            }
            .SelectMany(section => RowsOf(section, SettingsJson, Replaced(LiveryJson, "\"announcements\":true", "\"announcements\":false")));

        Assert.Equal(new[] { "easy_flight_authority", Cabin + "non_safety_announcements", Cabin + "safety_announcements" },
            rows.Where(row => row.Inactive).Select(row => row.Setting.Path).Order());
    }

    [Fact]
    public void Control_response_may_change_once_adaptive_handling_assist_is_on()
    {
        var on = RowsOf(Bbs757SettingSection.Aircraft,
            Replaced(SettingsJson, "\"easy_flight_enabled\":false", "\"easy_flight_enabled\":true"));

        var response = RowFor(on, "easy_flight_authority");
        Assert.Equal((Bbs757RowState.Value, "50"), (response.State, response.Text));
        Assert.False(response.Inactive);
        Assert.True(response.CanEdit(true));
    }

    // ---- what may be edited -------------------------------------------------------------------------

    [Theory]
    [InlineData(Bbs757RowState.Value, false, true, true)]
    [InlineData(Bbs757RowState.Other, false, true, true)]
    [InlineData(Bbs757RowState.Differs, false, true, true)]
    [InlineData(Bbs757RowState.Unreadable, false, true, false)]
    [InlineData(Bbs757RowState.Automatic, false, true, false)]
    [InlineData(Bbs757RowState.Value, true, true, false)]        // inactive
    [InlineData(Bbs757RowState.Differs, true, true, false)]
    [InlineData(Bbs757RowState.Other, true, true, false)]
    [InlineData(Bbs757RowState.Value, false, false, false)]      // the settings are read-only
    [InlineData(Bbs757RowState.Other, false, false, false)]
    [InlineData(Bbs757RowState.Differs, false, false, false)]
    public void A_row_can_be_edited_only_when_writable_active_and_holding_a_value(
        Bbs757RowState state, bool inactive, bool writable, bool expected)
    {
        var row = new Bbs757SettingRow(Setting("loading_speed"), state, -1, "text", inactive);

        Assert.Equal(expected, row.CanEdit(writable));
    }

    [Fact]
    public void Nothing_is_editable_while_not_writable()
    {
        // Rows in every state: a value, an unknown value, registrations that differ, an unreadable key and the automatic brightness.
        string settings = Replaced(SettingsJson, "\"irs_align_time\":0", "\"irs_align_time\":77");
        settings = Replaced(settings, "\"cpdlc_network\":0,", "");
        string quiet = Replaced(LiveryJson, "\"80\":true", "\"80\":false");
        string announcementsOff = Replaced(LiveryJson, "\"announcements\":true", "\"announcements\":false");

        var rows = Enum.GetValues<Bbs757SettingSection>()
            .SelectMany(section => RowsOf(section, settings, quiet, announcementsOff))
            .ToArray();

        Assert.Equal(
            new[] { Bbs757RowState.Value, Bbs757RowState.Other, Bbs757RowState.Unreadable, Bbs757RowState.Differs, Bbs757RowState.Automatic },
            rows.Select(row => row.State).Distinct().Order());
        Assert.Contains(rows, row => row.Inactive);
        Assert.All(rows, row => Assert.False(row.CanEdit(false), row.Setting.Path));
        // The same rows while writable: those that hold a value can be changed, and only those.
        Assert.Contains(rows, row => row.CanEdit(true));
        Assert.All(rows, row => Assert.Equal(
            !row.Inactive && (row.State is Bbs757RowState.Value or Bbs757RowState.Other or Bbs757RowState.Differs),
            row.CanEdit(true)));
    }

    // ---- texts --------------------------------------------------------------------------------------

    [Fact]
    public void The_texts_are_the_specs_words()
    {
        Assert.Equal("Not readable in this BlueBird version", Bbs757SettingsRows.UnreadableText);
        Assert.Equal("Differs between registrations", Bbs757SettingsRows.DiffersText);
        Assert.Equal("Differs between BlueBird's files", Bbs757SettingsRows.LinkedDiffersText);
        Assert.Equal("Automatic", Bbs757SettingsRows.AutomaticText);
        Assert.Equal("Other (stored value 7)", Bbs757SettingsRows.OtherText("7"));
        Assert.Equal("Other (stored value \"stone\")", Bbs757SettingsRows.OtherText("\"stone\""));
        Assert.Equal("Other (stored value -1)", Bbs757SettingsRows.OtherText("-1"));
    }

    [Fact]
    public void The_registrations_line_counts_and_names_them()
    {
        Assert.Equal("BlueBird has no registration options yet. Fly a livery once to create them.",
            Bbs757SettingsRows.RegistrationsLine(Array.Empty<string>()));
        Assert.Equal("Applies to 1 registration: G-BBS75", Bbs757SettingsRows.RegistrationsLine(["G-BBS75"]));
        Assert.Equal("Applies to 2 registrations: G-BBS75, N757BB", Bbs757SettingsRows.RegistrationsLine(["G-BBS75", "N757BB"]));
        Assert.Equal("Applies to 3 registrations: A, B, C", Bbs757SettingsRows.RegistrationsLine(["A", "B", "C"]));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_texts_do_not_depend_on_the_pcs_culture(string culture)
    {
        // A fraction, a negative whole number and a count: each is text that a comma-decimal culture (de-DE) or one with
        // its own minus sign (sv-SE) would write differently.
        string settings = SettingsJson;
        settings = Replaced(settings, "\"auto_brightness\":true", "\"auto_brightness\":false");
        settings = Replaced(settings, "\"display_brightness\":0.8", "\"display_brightness\":0.51");
        settings = Replaced(settings, "\"irs_align_time\":0", "\"irs_align_time\":-4");
        settings = Replaced(settings, "\"random_multiplier\":1.0", "\"random_multiplier\":1.9952623149688795");

        string[] Texts() =>
        [
            .. RowsOf(Bbs757SettingSection.General, settings).Select(row => row.Text),
            .. RowsOf(Bbs757SettingSection.Aircraft, settings).Select(row => row.Text),
            .. RowsOf(Bbs757SettingSection.Failures, settings).Select(row => row.Text),
            Bbs757SettingsRows.RegistrationsLine(["A", "B", "C"]),
        ];

        string[] expected = TestCulture.Run("en-US", Texts);
        string[] underCulture = TestCulture.Run(culture, Texts);

        Assert.Equal(expected, underCulture);
        Assert.Contains("51", underCulture);                                   // the brightness, floored from 0.51
        Assert.Contains("Other (stored value -4)", underCulture);              // an ASCII minus
        Assert.Contains("2.0x", underCulture);                                 // the slider's reading, a decimal point
        Assert.Contains("Applies to 3 registrations: A, B, C", underCulture);
    }

    // ---- the EFB colors theme's note (B757-21) ---------------------------------------------------------

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_colors_theme_shows_its_stored_choice_with_the_note_or_the_note_alone(string culture)
    {
        // The settings window offers the theme as no control (the owner's ruling of 2026-10-30): its read-only box, named by the
        // row's label, says the stored choice when the file reads one, and that non-default color schemes are not supported.
        string Note(string? settings) =>
            TestCulture.Run(culture, () => Bbs757SettingsRows.ThemeNoteText(RowFor(RowsOf(Bbs757SettingSection.General, settings), "ui_theme")));

        Assert.Equal("Dark blue. Non-default color schemes are not supported.", Note(SettingsJson));                       // 0
        Assert.Equal("Light grey. Non-default color schemes are not supported.",
            Note(Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme\":3")));                                              // the slider's third
        // A value no word stores, a missing key and no settings file name no choice.
        Assert.Equal("Non-default color schemes are not supported.", Note(Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme\":7")));
        Assert.Equal("Non-default color schemes are not supported.", Note(Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme_x\":0")));
        Assert.Equal("Non-default color schemes are not supported.", Note(null));
    }

    // ---- the way back to Dark blue (B757-21, the owner's ruling of 2026-10-30) ----------------------------------

    private static Bbs757SettingRow ThemeRow(string? settings) =>
        RowFor(RowsOf(Bbs757SettingSection.General, settings), Bbs757EfbSettings.ThemePath);

    [Theory]
    [InlineData("\"ui_theme\":1", "Light blue")]
    [InlineData("\"ui_theme\":3", "Light grey")]
    [InlineData("\"ui_theme\":2", "Dark grey")]
    public void The_way_back_to_dark_blue_is_offered_while_the_file_holds_another_theme(string stored, string word)
    {
        // A pilot whose file already holds another theme (one set on the tablet by a sighted helper, say) has no other way back: the
        // reader reads only Dark blue, and neither window offers the theme as a choice.
        var row = ThemeRow(Replaced(SettingsJson, "\"ui_theme\":0", stored));

        Assert.Equal((Bbs757RowState.Value, word), (row.State, row.Text));
        Assert.True(Bbs757SettingsRows.OffersDarkBlue(row));
    }

    [Fact]
    public void The_way_back_is_offered_for_a_stored_theme_no_word_names_too()
    {
        // Not Dark blue either (a theme this BlueBird version does not know): Dark blue is a value BlueBird stores.
        var row = ThemeRow(Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme\":7"));

        Assert.Equal(Bbs757RowState.Other, row.State);
        Assert.True(Bbs757SettingsRows.OffersDarkBlue(row));
    }

    [Fact]
    public void The_way_back_is_not_offered_for_dark_blue_or_a_theme_that_cannot_be_read()
    {
        Assert.False(Bbs757SettingsRows.OffersDarkBlue(ThemeRow(SettingsJson)));                                                // 0
        Assert.False(Bbs757SettingsRows.OffersDarkBlue(ThemeRow(Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme_x\":0"))));    // no key
        Assert.False(Bbs757SettingsRows.OffersDarkBlue(ThemeRow(Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme\":\"1\""))));  // text
        Assert.False(Bbs757SettingsRows.OffersDarkBlue(ThemeRow(Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme\":1.5"))));    // no whole number
        Assert.False(Bbs757SettingsRows.OffersDarkBlue(ThemeRow(null)));                                                        // no settings file
        Assert.Equal(Bbs757RowState.Unreadable, ThemeRow(Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme\":1.5")).State);
    }

    [Fact]
    public void No_other_row_offers_the_way_back()
    {
        // Every other setting, from files whose theme is Light blue: the button belongs to the theme's row alone.
        string lightBlue = Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme\":1");
        foreach (Bbs757SettingSection section in Enum.GetValues<Bbs757SettingSection>())
        {
            foreach (var row in RowsOf(section, lightBlue, LiveryJson).Where(r => r.Setting.Path != Bbs757EfbSettings.ThemePath))
                Assert.False(Bbs757SettingsRows.OffersDarkBlue(row), row.Setting.Path);
        }
    }

    [Fact]
    public void The_way_back_writes_the_catalogs_dark_blue_and_says_so()
    {
        // The button writes the theme the catalog lists first, Dark blue (stored 0), and names it with that word.
        var theme = Setting(Bbs757EfbSettings.ThemePath);

        Assert.Equal(("Dark blue", Bbs757Value.Of(0L)), (theme.Choices[0].Word, theme.Choices[0].Stored));
        Assert.Equal(theme.Choices[0].Stored, Bbs757EfbSettings.DarkBlueTheme);
        Assert.Equal("Set to Dark blue", Bbs757SettingsRows.SetDarkBlueText);
        Assert.EndsWith(theme.Choices[0].Word, Bbs757SettingsRows.SetDarkBlueText, StringComparison.Ordinal);
    }
}
