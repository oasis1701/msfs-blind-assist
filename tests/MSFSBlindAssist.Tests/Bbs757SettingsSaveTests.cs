using System.Text.Json;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// One save of the 757 EFB window's Settings tab (<see cref="Bbs757SettingsSave"/>): the check of every key before any
/// write, the writes in order and the stop at the first one not done everywhere, the rule that lets a group's other
/// callout be switched off past a file without it, the callouts said turned off, and every sentence said. Pure: the files
/// are parsed JSON (the settings file with made-up IDs and codes, and G-BBS75-opt.json as BlueBird wrote it), and the
/// writes are a fake file layer that records what it was asked, in order, and answers as the real one would.
/// </summary>
public class Bbs757SettingsSaveTests
{
    private const string Egpws = "options.egpws_callouts.";
    private const string V1 = "options.pilot_callouts.v1";
    private const string NotInFile = Bbs757SettingsFile.NotInFileReason;
    private const string InUse = Bbs757SettingsFile.InUseReason;

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static readonly string SettingsJson = File.ReadAllText(Fixture("bbs757-efb-settings.json"));
    private static readonly string LiveryJson = File.ReadAllText(Fixture("bbs757-livery-opt.json"));

    private static readonly Bbs757SaveResult Saved = new(Bbs757SaveStatus.Saved);
    private static readonly Bbs757SaveResult Unchanged = new(Bbs757SaveStatus.Unchanged);

    private static Bbs757SaveResult Refused(string reason) => new(Bbs757SaveStatus.Refused, reason);

    private static Bbs757Setting Setting(string path)
    {
        var setting = Bbs757EfbSettings.ByPath(path);
        Assert.True(setting is not null, $"The catalog has no setting '{path}'.");
        return setting!;
    }

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

    /// <summary>The files as read: the settings file's JSON (null: no settings file) and each registration's.</summary>
    private static Bbs757SettingsSnapshot Files(string? settings, params string[] registrations) =>
        new(settings is null ? null : Json(settings), settings is not null, registrations.Select(Json).ToArray(),
            registrations.Length);

    // The rows the tab showed before a save.
    private static Bbs757SettingRow On(string path) =>
        new(Setting(path), Bbs757RowState.Value, Bbs757SettingsRows.OnChoiceIndex, "On");

    private static Bbs757SettingRow Off(string path) => new(Setting(path), Bbs757RowState.Value, 0, "Off");

    private static Bbs757SettingRow Differs(string path) =>
        new(Setting(path), Bbs757RowState.Differs, -1, Bbs757SettingsRows.DiffersText);

    private static Bbs757SettingRow Unreadable(string path) =>
        new(Setting(path), Bbs757RowState.Unreadable, -1, Bbs757SettingsRows.UnreadableText);

    /// <summary>
    /// The file layer, faked: every write it is asked for, as "global key = value" or "registrations key = value", in
    /// order, and every log line. A key it has no answer for is saved (in two registrations, for a registration key).
    /// </summary>
    private sealed class FakeFiles
    {
        public List<string> Writes { get; } = new();
        public List<string> Log { get; } = new();
        public Dictionary<string, Bbs757SaveResult> Global { get; } = new();
        public Dictionary<string, (string Registration, Bbs757SaveResult Result)[]> Registrations { get; } = new();

        public Bbs757SettingSaveOutcome Save(Bbs757Setting setting, Bbs757Value value, Bbs757SettingsSnapshot files,
            params Bbs757SettingRow[] shown) =>
            Bbs757SettingsSave.Run(setting, value, files, shown,
                (path, written) =>
                {
                    Writes.Add($"global {path} = {written.ToJson()}");
                    return Global.GetValueOrDefault(path, Saved);
                },
                (path, written) =>
                {
                    Writes.Add($"registrations {path} = {written.ToJson()}");
                    return Registrations.TryGetValue(path, out var results)
                        ? results
                        : new[] { ("G-AAAA", Saved), ("G-BBBB", Saved) };
                },
                line => Log.Add(line));
    }

    // ---- a group's other callouts: never blocked by a file without them --------------------------------

    [Fact]
    public void A_registration_without_another_callout_of_the_group_does_not_block_switching_one_on()
    {
        // G-BBBB's file has no 2500 (an older livery file): its refusal of 2500's switch-off counts as done.
        string older = Replaced(LiveryJson, "\"2500\":true,", "");
        var files = new FakeFiles();
        files.Registrations[Egpws + "2500"] = new[] { ("G-AAAA", Saved), ("G-BBBB", Refused(NotInFile)) };

        var outcome = files.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, LiveryJson, older),
            On(Egpws + "2500"), Off(Egpws + "radio_altimeter"));

        Assert.Equal(new[] { $"registrations {Egpws}2500 = false", $"registrations {Egpws}radio_altimeter = true" },
            files.Writes);
        Assert.Equal("2500 turned off", outcome.Spoken);
        Assert.False(outcome.Immediate);
        Assert.Equal(new[] { "2500" }, outcome.TurnedOff);
    }

    [Fact]
    public void A_callout_of_the_group_that_no_registration_has_does_not_block_either()
    {
        // No file has 2500: the check before the writes leaves its switch-off alone, and each file's refusal is done.
        string without = Replaced(LiveryJson, "\"2500\":true,", "");
        var files = new FakeFiles();
        files.Registrations[Egpws + "2500"] = new[] { ("G-AAAA", Refused(NotInFile)), ("G-BBBB", Refused(NotInFile)) };

        var outcome = files.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, without, without),
            Unreadable(Egpws + "2500"), Off(Egpws + "radio_altimeter"));

        Assert.Equal(new[] { $"registrations {Egpws}2500 = false", $"registrations {Egpws}radio_altimeter = true" },
            files.Writes);
        // 2500 showed nothing on, and a check box that saved adds nothing to the screen reader's "checked".
        Assert.Null(outcome.Spoken);
        Assert.False(outcome.Immediate);
        Assert.Empty(outcome.TurnedOff);
    }

    [Fact]
    public void The_setting_s_own_key_missing_everywhere_still_refuses_before_any_write()
    {
        string without = Replaced(LiveryJson, "\"radio_altimeter\":false,", "");
        var files = new FakeFiles();

        var outcome = files.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, without),
            On(Egpws + "2500"));

        Assert.Empty(files.Writes);
        Assert.Equal("Couldn't save Radio altimeter (2500): this BlueBird version doesn't have it", outcome.Spoken);
        Assert.True(outcome.Immediate);
        Assert.Empty(outcome.TurnedOff);
    }

    [Fact]
    public void A_callout_of_the_group_refused_for_another_reason_stops_the_rest()
    {
        // One registration: said under the callout the pilot pressed, which was not saved, naming the one not turned off.
        var one = new FakeFiles();
        one.Registrations[Egpws + "2500"] = new[] { ("G-BBS75", Refused(InUse)) };

        var outcome = one.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, LiveryJson),
            On(Egpws + "2500"));

        // Radio altimeter is never written: both on would be a state the tablet cannot make.
        Assert.Equal(new[] { $"registrations {Egpws}2500 = false" }, one.Writes);
        Assert.Equal("Couldn't save Radio altimeter (2500): 2500 could not be turned off: the file is in use", outcome.Spoken);
        Assert.True(outcome.Immediate);
        Assert.Empty(outcome.TurnedOff);

        // Several registrations, none turned off: each one named with its reason.
        var two = new FakeFiles();
        two.Registrations[Egpws + "2500"] = new[] { ("G-AAAA", Refused(InUse)), ("G-BBBB", Refused(Bbs757SettingsFile.UnreadableReason)) };

        var both = two.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, LiveryJson),
            On(Egpws + "2500"));

        Assert.Equal(new[] { $"registrations {Egpws}2500 = false" }, two.Writes);
        Assert.Equal("Couldn't save Radio altimeter (2500): 2500 could not be turned off: G-AAAA: the file is in use; "
            + "G-BBBB: the file could not be read.", both.Spoken);
        Assert.True(both.Immediate);
        Assert.Empty(both.TurnedOff);
    }

    [Fact]
    public void A_callout_of_the_group_switched_off_in_some_files_only_is_named_and_stops_the_rest()
    {
        var files = new FakeFiles();
        files.Registrations[Egpws + "2500"] = new[] { ("G-AAAA", Saved), ("G-BBBB", Refused(InUse)) };

        var outcome = files.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, LiveryJson),
            On(Egpws + "2500"));

        Assert.Equal(new[] { $"registrations {Egpws}2500 = false" }, files.Writes);
        // The callout pressed comes first: it was saved nowhere, as the chain stopped before its own write.
        Assert.Equal("Couldn't save Radio altimeter (2500): 2500 turned off for 1 of 2 registrations; G-BBBB: the file is in use.",
            outcome.Spoken);
        Assert.True(outcome.Immediate);
        Assert.Empty(outcome.TurnedOff);
    }

    [Fact]
    public void A_file_without_the_callout_is_not_counted_as_one_that_turned_it_off()
    {
        // G-BBBB has no 2500: its refusal counts as done (the callout cannot be on there), but nothing was saved in it.
        var files = new FakeFiles();
        files.Registrations[Egpws + "2500"] = new[]
        {
            ("G-AAAA", Saved), ("G-BBBB", Refused(NotInFile)), ("G-CCCC", Refused(InUse)),
        };

        var some = files.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, LiveryJson),
            On(Egpws + "2500"));

        Assert.Equal("Couldn't save Radio altimeter (2500): 2500 turned off for 1 of 3 registrations; G-CCCC: the file is in use.",
            some.Spoken);

        // Already off counts, as a file that holds the value already is saved.
        files.Registrations[Egpws + "2500"] = new[]
        {
            ("G-AAAA", Unchanged), ("G-BBBB", Refused(NotInFile)), ("G-CCCC", Refused(InUse)),
        };

        Assert.Equal("Couldn't save Radio altimeter (2500): 2500 turned off for 1 of 3 registrations; G-CCCC: the file is in use.",
            files.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, LiveryJson),
                On(Egpws + "2500")).Spoken);

        // With nothing turned off anywhere, it is no partial save: the one file that failed is named.
        files.Registrations[Egpws + "2500"] = new[] { ("G-AAAA", Refused(NotInFile)), ("G-BBBB", Refused(InUse)) };

        var none = files.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, LiveryJson),
            On(Egpws + "2500"));

        Assert.Equal("Couldn't save Radio altimeter (2500): 2500 could not be turned off: G-BBBB: the file is in use.",
            none.Spoken);
        Assert.True(none.Immediate);
        Assert.Empty(none.TurnedOff);
    }

    // ---- "turned off" ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("on", true)]
    [InlineData("differs", true)]
    [InlineData("off", false)]
    [InlineData("unreadable", false)]
    public void Only_a_callout_that_showed_on_or_different_values_is_said_turned_off(string shown, bool said)
    {
        string path = Egpws + "2500";
        Bbs757SettingRow row = shown switch
        {
            "on" => On(path),
            "differs" => Differs(path),
            "off" => Off(path),
            _ => Unreadable(path),
        };

        var outcome = new FakeFiles().Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true),
            Files(SettingsJson, LiveryJson), row);

        Assert.Equal(said ? "2500 turned off" : null, outcome.Spoken);
        Assert.Equal(said ? new[] { "2500" } : Array.Empty<string>(), outcome.TurnedOff);
        Assert.False(outcome.Immediate);
    }

    [Fact]
    public void Several_callouts_switched_off_are_said_in_the_tablet_s_order()
    {
        var files = new FakeFiles();

        var outcome = files.Save(Setting(Egpws + "decide"), Bbs757Value.Of(true), Files(SettingsJson, LiveryJson),
            On(Egpws + "minimums"), Off(Egpws + "minimums_minimums"), Differs(Egpws + "minimum"),
            Off(Egpws + "decide"), Off(Egpws + "decision_height"));

        Assert.Equal(new[]
        {
            $"registrations {Egpws}minimums = false",
            $"registrations {Egpws}minimums_minimums = false",
            $"registrations {Egpws}minimum = false",
            $"registrations {Egpws}decision_height = false",
            $"registrations {Egpws}decide = true",
        }, files.Writes);
        Assert.Equal("Minimums turned off. Minimum turned off", outcome.Spoken);
        Assert.Equal(new[] { "Minimums", "Minimum" }, outcome.TurnedOff);
    }

    [Fact]
    public void A_failure_after_a_callout_was_switched_off_says_both_at_once()
    {
        var files = new FakeFiles();
        files.Registrations[Egpws + "radio_altimeter"] = new[] { ("G-AAAA", Refused(InUse)), ("G-BBBB", Refused(InUse)) };

        var outcome = files.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, LiveryJson),
            On(Egpws + "2500"));

        Assert.Equal("Couldn't save Radio altimeter (2500): G-AAAA: the file is in use; G-BBBB: the file is in use. 2500 turned off",
            outcome.Spoken);
        Assert.True(outcome.Immediate);
        Assert.Equal(new[] { "2500" }, outcome.TurnedOff);
    }

    // ---- the check before any write -----------------------------------------------------------------------

    [Fact]
    public void A_key_missing_before_any_write_is_said_with_its_write_s_label_and_nothing_is_written()
    {
        // Units writes its linked units too, which this settings file lacks.
        string noUnits = Replaced(SettingsJson, ",\"units\":1", "");
        var files = new FakeFiles();

        var outcome = files.Save(Setting("options.general.weight_unit"), Bbs757Value.Of("kg"), Files(noUnits, LiveryJson));

        Assert.Empty(files.Writes);
        Assert.Equal("Couldn't save Units: this BlueBird version doesn't have it", outcome.Spoken);
        Assert.True(outcome.Immediate);
        // Logged as the file layer logs a refusal: the key missing, with its value, as Units may be logged.
        Assert.Equal(new[] { "settings: units = 0 not saved: this BlueBird version doesn't have it" }, files.Log);
    }

    [Fact]
    public void The_pilot_s_identifiers_are_logged_without_their_value()
    {
        string noId = Replaced(SettingsJson, "\"simbrief_id\":123456,", "");
        var files = new FakeFiles();

        var outcome = files.Save(Setting("simbrief_id"), Bbs757Value.Of(654321L), Files(noId));

        Assert.Equal("Couldn't save SimBrief ID number: this BlueBird version doesn't have it", outcome.Spoken);
        Assert.Equal(new[] { "settings: simbrief_id not saved: this BlueBird version doesn't have it" }, files.Log);
        Assert.DoesNotContain(files.Log, line => line.Contains("654321", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_that_is_missing_or_unreadable_is_said_so()
    {
        var files = new FakeFiles();
        var noSettings = new Bbs757SettingsSnapshot(null, false, new[] { Json(LiveryJson) }, 1);
        var unreadableSettings = new Bbs757SettingsSnapshot(null, true, new[] { Json(LiveryJson) }, 1);
        var noRegistration = Files(SettingsJson);
        var noRegistrationParses = new Bbs757SettingsSnapshot(Json(SettingsJson), true, Array.Empty<JsonElement>(), 2);

        Assert.Equal("Couldn't save Boarding & load rate: BlueBird's settings file is missing",
            files.Save(Setting("loading_speed"), Bbs757Value.Of(1L), noSettings).Spoken);
        Assert.Equal("Couldn't save Boarding & load rate: the file could not be read",
            files.Save(Setting("loading_speed"), Bbs757Value.Of(1L), unreadableSettings).Spoken);
        Assert.Equal("Couldn't save V1: BlueBird's settings file is missing",
            files.Save(Setting(V1), Bbs757Value.Of(false), noRegistration).Spoken);
        Assert.Equal("Couldn't save V1: the file could not be read",
            files.Save(Setting(V1), Bbs757Value.Of(false), noRegistrationParses).Spoken);
        Assert.Empty(files.Writes);
    }

    // ---- the writes and what is said ------------------------------------------------------------------------

    [Fact]
    public void Some_registrations_saved_names_each_one_not_saved_with_its_own_reason()
    {
        var files = new FakeFiles();
        files.Registrations[V1] = new[] { ("G-AAAA", Saved), ("G-BBBB", Refused(InUse)) };

        var one = files.Save(Setting(V1), Bbs757Value.Of(false), Files(SettingsJson, LiveryJson));

        Assert.Equal("Saved for 1 of 2 registrations; G-BBBB: the file is in use.", one.Spoken);
        Assert.True(one.Immediate);

        // The setting's own key keeps the strict rule: a file without it is not saved.
        files.Registrations[V1] = new[] { ("G-AAAA", Unchanged), ("G-BBBB", Refused(InUse)), ("G-CCCC", Refused(NotInFile)) };

        var two = files.Save(Setting(V1), Bbs757Value.Of(false), Files(SettingsJson, LiveryJson));

        Assert.Equal("Saved for 1 of 3 registrations; G-BBBB: the file is in use; G-CCCC: this BlueBird version doesn't have it.",
            two.Spoken);
        Assert.True(two.Immediate);
    }

    [Fact]
    public void No_registration_saved_names_each_one_with_its_own_reason()
    {
        var files = new FakeFiles();
        files.Registrations[V1] = new[]
        {
            ("G-AAAA", Refused(InUse)),
            ("G-BBBB", Refused(Bbs757SettingsFile.UnreadableReason)),
        };

        var outcome = files.Save(Setting(V1), Bbs757Value.Of(false), Files(SettingsJson, LiveryJson));

        Assert.Equal("Couldn't save V1: G-AAAA: the file is in use; G-BBBB: the file could not be read.", outcome.Spoken);
        Assert.True(outcome.Immediate);

        // With one registration there is nothing to tell apart: its reason alone.
        files.Registrations[V1] = new[] { ("G-BBS75", Refused(InUse)) };

        var one = files.Save(Setting(V1), Bbs757Value.Of(false), Files(SettingsJson, LiveryJson));

        Assert.Equal("Couldn't save V1: the file is in use", one.Spoken);
        Assert.True(one.Immediate);
    }

    [Fact]
    public void Units_writes_every_registration_s_weight_unit_then_the_units_in_the_settings_file()
    {
        var files = new FakeFiles();

        var outcome = files.Save(Setting("options.general.weight_unit"), Bbs757Value.Of("kg"), Files(SettingsJson, LiveryJson));

        Assert.Equal(new[] { "registrations options.general.weight_unit = \"kg\"", "global units = 0" }, files.Writes);
        // A choice that saved: the screen reader has said it.
        Assert.Null(outcome.Spoken);
        Assert.False(outcome.Immediate);

        // The linked key keeps the strict rule, and its failure is said with Units' label.
        files.Global["units"] = Refused(InUse);

        var refused = files.Save(Setting("options.general.weight_unit"), Bbs757Value.Of("lbs"), Files(SettingsJson, LiveryJson));

        Assert.Equal("Couldn't save Units: the file is in use", refused.Spoken);
        Assert.True(refused.Immediate);
    }

    [Fact]
    public void A_number_or_text_box_that_saved_says_so_and_a_switch_says_nothing()
    {
        var files = new FakeFiles();

        var box = files.Save(Setting("tod_pause_distance"), Bbs757Value.Of(40L), Files(SettingsJson));

        Assert.Equal(new[] { "global tod_pause_distance = 40" }, files.Writes);
        Assert.Equal("TOD pause distance (NM) saved", box.Spoken);
        Assert.False(box.Immediate);

        var check = files.Save(Setting("baro_sync"), Bbs757Value.Of(false), Files(SettingsJson));

        Assert.Null(check.Spoken);
        Assert.False(check.Immediate);
    }

    [Fact]
    public void A_slider_that_saved_says_nothing_and_one_refused_says_why_at_once()
    {
        var files = new FakeFiles();

        var moved = files.Save(Setting("failures.random_multiplier"), Bbs757Value.OfFloat(2.5), Files(SettingsJson));

        Assert.Equal(new[] { "global failures.random_multiplier = 2.5" }, files.Writes);
        // A slider that saved: the screen reader has said its new value.
        Assert.Null(moved.Spoken);
        Assert.False(moved.Immediate);

        files.Global["failures.random_multiplier"] = Refused(InUse);

        var refused = files.Save(Setting("failures.random_multiplier"), Bbs757Value.OfFloat(4.0), Files(SettingsJson));

        Assert.Equal("Couldn't save Random failure frequency: the file is in use", refused.Spoken);
        Assert.True(refused.Immediate);
    }

    [Fact]
    public void The_words_and_a_logged_value_are_the_same_on_a_comma_decimal_pc()
    {
        string noBrightness = Replaced(SettingsJson, "\"display_brightness\":0.8,", "");
        IReadOnlyList<string> logged = TestCulture.Run("de-DE", () =>
        {
            var files = new FakeFiles();
            files.Save(Setting("display_brightness"), Bbs757Value.OfFloat(0.554), Files(noBrightness));
            return files.Log;
        });
        Assert.Equal(new[] { "settings: display_brightness = 0.554 not saved: this BlueBird version doesn't have it" }, logged);

        string? spoken = TestCulture.Run("sv-SE", () =>
        {
            var files = new FakeFiles();
            files.Registrations[V1] = new[] { ("G-AAAA", Saved), ("G-BBBB", Refused(InUse)) };
            return files.Save(Setting(V1), Bbs757Value.Of(false), Files(SettingsJson, LiveryJson)).Spoken;
        });
        Assert.Equal("Saved for 1 of 2 registrations; G-BBBB: the file is in use.", spoken);

        string? turnedOff = TestCulture.Run("de-DE", () =>
        {
            var files = new FakeFiles();
            files.Registrations[Egpws + "2500"] = new[] { ("G-AAAA", Saved), ("G-BBBB", Refused(InUse)) };
            return files.Save(Setting(Egpws + "radio_altimeter"), Bbs757Value.Of(true), Files(SettingsJson, LiveryJson),
                On(Egpws + "2500")).Spoken;
        });
        Assert.Equal("Couldn't save Radio altimeter (2500): 2500 turned off for 1 of 2 registrations; G-BBBB: the file is in use.",
            turnedOff);
    }

    // ---- why a row cannot change for its own state --------------------------------------------------------

    private static string? ReasonOf(Bbs757SettingSection section, string path, Bbs757SettingsSnapshot files) =>
        Bbs757SettingsSave.CannotChangeReason(
            Bbs757SettingsRows.For(section, files.Global, files.Registrations).Single(row => row.Setting.Path == path), files);

    [Fact]
    public void The_brightness_reading_automatic_sends_the_pilot_to_the_mode_s_manual_word()
    {
        var files = Files(SettingsJson);   // auto_brightness true
        Assert.Equal(Bbs757RowState.Automatic,
            Bbs757SettingsRows.For(Bbs757SettingSection.General, files.Global, files.Registrations)
                .Single(row => row.Setting.Path == "display_brightness").State);

        Assert.Equal("set EFB brightness mode to Manual first",
            ReasonOf(Bbs757SettingSection.General, "display_brightness", files));
    }

    [Fact]
    public void The_brightness_with_a_mode_that_cannot_be_read_names_the_mode()
    {
        // The mode missing, or of another type: the brightness itself is in the file, so its reason names the mode.
        foreach (string json in new[]
                 {
                     Replaced(SettingsJson, "\"auto_brightness\":true,", ""),
                     Replaced(SettingsJson, "\"auto_brightness\":true", "\"auto_brightness\":\"false\""),
                 })
        {
            var files = Files(json);
            Assert.Equal(Bbs757RowState.Automatic,
                Bbs757SettingsRows.For(Bbs757SettingSection.General, files.Global, files.Registrations)
                    .Single(row => row.Setting.Path == "display_brightness").State);

            string? reason = ReasonOf(Bbs757SettingSection.General, "display_brightness", files);

            Assert.Equal("EFB brightness mode is not readable in this BlueBird version", reason);
            Assert.Equal("Couldn't save EFB brightness: EFB brightness mode is not readable in this BlueBird version",
                Bbs757SettingsSave.CouldNotSave("EFB brightness", reason!));
        }
    }

    [Fact]
    public void An_unreadable_row_says_why_its_key_does_not_read()
    {
        var noSpeed = Files(Replaced(SettingsJson, "\"loading_speed\":2,", ""));
        var noSettings = new Bbs757SettingsSnapshot(null, false, Array.Empty<JsonElement>(), 0);
        var unreadableSettings = new Bbs757SettingsSnapshot(null, true, Array.Empty<JsonElement>(), 0);
        var noRegistrationParses = new Bbs757SettingsSnapshot(Json(SettingsJson), true, Array.Empty<JsonElement>(), 2);

        Assert.Equal(NotInFile, ReasonOf(Bbs757SettingSection.Aircraft, "loading_speed", noSpeed));
        Assert.Equal(Bbs757SettingsFile.MissingReason, ReasonOf(Bbs757SettingSection.Aircraft, "loading_speed", noSettings));
        Assert.Equal(Bbs757SettingsFile.UnreadableReason,
            ReasonOf(Bbs757SettingSection.Aircraft, "loading_speed", unreadableSettings));
        Assert.Equal(Bbs757SettingsFile.MissingReason, ReasonOf(Bbs757SettingSection.Callouts, Egpws + "2500", Files(SettingsJson)));
        Assert.Equal(Bbs757SettingsFile.UnreadableReason,
            ReasonOf(Bbs757SettingSection.Callouts, Egpws + "2500", noRegistrationParses));
        // A row that can change has no reason.
        Assert.Null(ReasonOf(Bbs757SettingSection.Aircraft, "baro_sync", Files(SettingsJson)));
    }

    // ---- the EFB colors theme's way back to Dark blue (B757-21) ------------------------------------------------------

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Set_to_dark_blue_plans_one_write_of_the_theme_and_saves_nothing_else(string culture)
    {
        // The settings window's Set to Dark blue saves Bbs757EfbSettings.DarkBlueTheme for the theme, as any save goes: one write,
        // ui_theme 0 in efb_settings.json, and nothing said after it, as a choice list's save says nothing more (CORE-7).
        var theme = Setting(Bbs757EfbSettings.ThemePath);
        Bbs757SettingWrite planned = Assert.Single(Bbs757EfbSettings.WritesFor(theme, Bbs757EfbSettings.DarkBlueTheme));
        Assert.Equal((Bbs757EfbSettings.ThemePath, Bbs757SettingFile.Global, Bbs757Value.Of(0L)), (planned.Path, planned.File, planned.Value));
        Assert.Same(theme, planned.Setting);

        var files = new FakeFiles();
        var outcome = TestCulture.Run(culture, () => files.Save(theme, Bbs757EfbSettings.DarkBlueTheme,
            Files(Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme\":1"), LiveryJson),
            new Bbs757SettingRow(theme, Bbs757RowState.Value, 1, "Light blue")));

        Assert.Equal(new[] { "global ui_theme = 0" }, files.Writes);
        Assert.Empty(files.Log);
        Assert.Null(outcome.Spoken);
        Assert.False(outcome.Immediate);
        Assert.Empty(outcome.TurnedOff);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Set_to_dark_blue_not_saved_says_why_at_once_in_the_windows_words(string culture)
    {
        // The simulator started between the window's last check and the press: the file layer refuses, and the failure is said at once.
        var theme = Setting(Bbs757EfbSettings.ThemePath);
        var files = new FakeFiles();
        files.Global[Bbs757EfbSettings.ThemePath] = Refused(Bbs757SettingsFile.SimRunningReason);

        var outcome = TestCulture.Run(culture, () => files.Save(theme, Bbs757EfbSettings.DarkBlueTheme,
            Files(Replaced(SettingsJson, "\"ui_theme\":0", "\"ui_theme\":2"), LiveryJson)));

        Assert.Equal(new[] { "global ui_theme = 0" }, files.Writes);
        Assert.Equal("Couldn't save EFB colors theme: the simulator is running", outcome.Spoken);
        Assert.True(outcome.Immediate);
    }
}
