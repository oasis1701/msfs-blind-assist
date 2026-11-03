// Characterization tests for the Fenix display channel's five Ctrl+M rows (Services/FenixChannelAnnouncementRows.cs): the
// FMA changes, the E/WD warnings, cautions and memos, and the DCDU messages. They are declared once, by plan 01, for the
// announcers of later plans to check before they speak; the dialog (FenixMonitorManagerForm) shows them after the cabin's
// four, an unticked row puts its key in FenixDisabledMonitorVariables, and a row's announcer asks IsMuted as it is about to
// speak, so a mute applies at once. The memos row starts muted (a one-time seed with a flag of its own, so a settings file
// whose older Fenix seed already ran still gets it), and ticking it again sticks.
//
// The contract: the keys are stable (a saved mute names them) and no variable's key; the labels answer to no other row's, as
// Ctrl+M is a flat list read by label; the rows come after the variable rows and the cabin rows, in the order FMA, warnings,
// cautions, memos, DCDU. The seed puts the memos key in once and leaves everything else as it was; a pilot who unticks it
// keeps it ticked at the next start, on a fresh install and on a file written before the seed existed alike.
//
// The first class is pure. The second reaches SettingsManager (the seed saves; IsMuted reads Current), so it is in the
// "SettingsManagerGlobalState" collection and every test of it runs with the settings paths redirected to a scratch folder
// (SettingsRedirect): no test touches the real settings file of the person running them.

using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class FenixChannelMonitorRowsTests
{
    private static readonly MonitorRow[] ChannelRows =
    {
        new("FENIX_CHANNEL_FMA", "FMA changes"),
        new("FENIX_CHANNEL_WARNINGS", "E/WD warnings"),
        new("FENIX_CHANNEL_CAUTIONS", "E/WD cautions"),
        new("FENIX_CHANNEL_MEMOS", "E/WD memos"),
        new("FENIX_CHANNEL_DCDU", "DCDU messages")
    };

    [Fact]
    public void The_five_kinds_have_stable_keys_and_their_own_rows()
    {
        Assert.Equal("FENIX_CHANNEL_FMA", FenixChannelAnnouncementRows.FmaKey);
        Assert.Equal("FENIX_CHANNEL_WARNINGS", FenixChannelAnnouncementRows.WarningsKey);
        Assert.Equal("FENIX_CHANNEL_CAUTIONS", FenixChannelAnnouncementRows.CautionsKey);
        Assert.Equal("FENIX_CHANNEL_MEMOS", FenixChannelAnnouncementRows.MemosKey);
        Assert.Equal("FENIX_CHANNEL_DCDU", FenixChannelAnnouncementRows.DcduKey);

        Assert.Equal(ChannelRows, FenixChannelAnnouncementRows.Rows);
    }

    [Fact]
    public void The_five_channel_rows_follow_the_cabin_rows()
    {
        var variables = new FenixA320Definition().GetVariables();

        List<MonitorRow> rows = FenixMonitorManagerForm.BuildRows(variables);

        // The variable rows first, exactly as before, then the four cabin rows in their order, then the five channel rows
        // in theirs, appended after the sort as the cabin's are, and nothing after them.
        int variableCount = MonitorRowBuilder.Build(variables).Count;
        int cabinCount = FenixCabinAnnouncements.Rows.Count;
        Assert.Equal(variableCount + cabinCount + ChannelRows.Length, rows.Count);
        Assert.Equal(MonitorRowBuilder.Build(variables), rows.Take(variableCount));
        Assert.Equal(FenixCabinAnnouncements.Rows, rows.Skip(variableCount).Take(cabinCount));
        Assert.Equal(ChannelRows, rows.Skip(variableCount + cabinCount));
    }

    [Fact]
    public void Each_row_key_is_unique_and_no_variable_owns_it()
    {
        var variables = new FenixA320Definition().GetVariables();
        string[] keys = FenixChannelAnnouncementRows.Rows.Select(row => row.Key).ToArray();

        // A saved mute names a key and Ctrl+M is one flat list: a key two rows share, or a variable's key, would put
        // the mute on both, and the variable's own announcement would go quiet with the row.
        Assert.Equal(ChannelRows.Length, keys.Length);   // five keys to check, not an empty list that checks nothing
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        foreach (string key in keys)
        {
            Assert.DoesNotContain(FenixCabinAnnouncements.Rows, cabin => string.Equals(cabin.Key, key, StringComparison.Ordinal));
            Assert.False(variables.ContainsKey(key), key);
        }
    }

    [Fact]
    public void Each_row_label_is_told_apart_from_every_other_fenix_row()
    {
        var variables = new FenixA320Definition().GetVariables();
        List<MonitorRow> others = MonitorRowBuilder.Build(variables);
        others.AddRange(FenixCabinAnnouncements.Rows);
        string[] labels = FenixChannelAnnouncementRows.Rows.Select(row => row.Label).ToArray();

        // The pilot reads the list by label: no channel label may answer to another row's, the cabin's included.
        Assert.Equal(ChannelRows.Length, labels.Length);   // five labels to check, not an empty list that checks nothing
        foreach (string label in labels)
        {
            Assert.DoesNotContain(others, other => string.Equals(other.Label, label, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(labels.Length, labels.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

[Collection("SettingsManagerGlobalState")]
public class FenixChannelMonitorRowsSettingsTests : IDisposable
{
    private const string MemosKey = FenixChannelAnnouncementRows.MemosKey;

    // A pilot's own mute of an older Fenix row, which the seed must leave as it is.
    private const string OlderMute = "S_SEAT_HEIGHT_CAPT";

    private readonly SettingsRedirect _settings = new();

    public void Dispose() => _settings.Dispose();

    [Fact]
    public void Memos_start_muted_once_and_a_re_enable_sticks()
    {
        var settings = new UserSettings();
        settings.FenixDisabledMonitorVariables.Add(OlderMute);
        Assert.False(settings.FenixChannelMonitorDefaultsSeeded);

        SettingsManager.SeedFenixChannelMonitorDefaults(settings);

        // The memos row starts muted and no other channel row does; what the pilot muted before stays as it was.
        Assert.True(settings.FenixChannelMonitorDefaultsSeeded);
        Assert.Equal(new[] { OlderMute, MemosKey }, settings.FenixDisabledMonitorVariables);

        // The pilot ticks the memos row (Ctrl+M takes the key out of the list and saves), and the next start seeds
        // nothing again: the row stays ticked.
        settings.FenixDisabledMonitorVariables.Remove(MemosKey);
        SettingsManager.Save(settings);
        SettingsManager.SeedFenixChannelMonitorDefaults(settings);

        Assert.Equal(new[] { OlderMute }, settings.FenixDisabledMonitorVariables);
        Assert.False(FenixChannelAnnouncementRows.IsMuted(MemosKey));
    }

    [Fact]
    public void The_seed_does_not_add_a_memos_mute_the_pilot_already_has()
    {
        var settings = new UserSettings();
        settings.FenixDisabledMonitorVariables.Add(MemosKey);

        SettingsManager.SeedFenixChannelMonitorDefaults(settings);

        Assert.True(settings.FenixChannelMonitorDefaultsSeeded);
        Assert.Equal(new[] { MemosKey }, settings.FenixDisabledMonitorVariables);
    }

    [Fact]
    public void A_file_that_was_seeded_is_left_alone()
    {
        // The list is deliberately empty: the flag alone says the seed has run, so it adds nothing.
        var settings = new UserSettings { FenixChannelMonitorDefaultsSeeded = true };

        SettingsManager.SeedFenixChannelMonitorDefaults(settings);

        Assert.True(settings.FenixChannelMonitorDefaultsSeeded);
        Assert.Empty(settings.FenixDisabledMonitorVariables);
    }

    [Fact]
    public void Both_load_paths_seed_the_memos_row()
    {
        // A fresh install: no settings file at all.
        UserSettings fresh = SettingsManager.Load();
        Assert.True(fresh.FenixChannelMonitorDefaultsSeeded);
        Assert.Contains(MemosKey, fresh.FenixDisabledMonitorVariables);

        // An install that has run before: its file carries the older Fenix seed's flag and a mute of the pilot's own, and
        // no flag of this seed, since it was written before the seed existed. The older flag is why the older seed
        // does not run again; it must not stand in the way of this one.
        Directory.CreateDirectory(_settings.Folder);
        File.WriteAllText(_settings.SettingsFile,
            $$"""{ "FenixMonitorDefaultsSeeded": true, "FenixDisabledMonitorVariables": [ "{{OlderMute}}" ] }""");

        UserSettings upgraded = SettingsManager.Load();

        Assert.True(upgraded.FenixChannelMonitorDefaultsSeeded);
        Assert.Equal(new[] { OlderMute, MemosKey }, upgraded.FenixDisabledMonitorVariables);
        // And it was saved: the next start finds the flag in the file, whatever the pilot does in between.
        UserSettings onDisk = JsonSerializer.Deserialize<UserSettings>(
            File.ReadAllText(_settings.SettingsFile), SettingsManager.JsonOptions)!;
        Assert.True(onDisk.FenixChannelMonitorDefaultsSeeded);
        Assert.Equal(new[] { OlderMute, MemosKey }, onDisk.FenixDisabledMonitorVariables);

        // The pilot ticks the memos row, and it stays ticked at the next start.
        upgraded.FenixDisabledMonitorVariables.Remove(MemosKey);
        SettingsManager.Save(upgraded);

        UserSettings restarted = SettingsManager.Load();

        Assert.True(restarted.FenixChannelMonitorDefaultsSeeded);
        Assert.Equal(new[] { OlderMute }, restarted.FenixDisabledMonitorVariables);
    }

    [Fact]
    public void A_mute_applies_at_once()
    {
        // The first use of Current loads from the scratch folder, as a first start does: a fresh install.
        UserSettings settings = SettingsManager.Current;
        Assert.False(FenixChannelAnnouncementRows.IsMuted(FenixChannelAnnouncementRows.FmaKey));
        Assert.True(FenixChannelAnnouncementRows.IsMuted(MemosKey));   // the seeded default

        // The Ctrl+M dialog unticks a row: the key goes into the live list and the settings are saved, which rebuilds the
        // set an announcer reads. The very next ask sees it: no reload, no restart.
        settings.FenixDisabledMonitorVariables.Add(FenixChannelAnnouncementRows.FmaKey);
        SettingsManager.Save();
        Assert.True(FenixChannelAnnouncementRows.IsMuted(FenixChannelAnnouncementRows.FmaKey));
        Assert.False(FenixChannelAnnouncementRows.IsMuted(FenixChannelAnnouncementRows.WarningsKey));   // that row only

        // Ticking it again applies at once as well.
        settings.FenixDisabledMonitorVariables.Remove(FenixChannelAnnouncementRows.FmaKey);
        SettingsManager.Save();
        Assert.False(FenixChannelAnnouncementRows.IsMuted(FenixChannelAnnouncementRows.FmaKey));

        // Nothing is copied at the first ask: settings replaced as a whole are seen by the next one.
        SettingsManager.Save(new UserSettings { FenixDisabledMonitorVariables = { FenixChannelAnnouncementRows.WarningsKey } });
        Assert.True(FenixChannelAnnouncementRows.IsMuted(FenixChannelAnnouncementRows.WarningsKey));
        Assert.False(FenixChannelAnnouncementRows.IsMuted(MemosKey));
    }
}

/// <summary>
/// Points SettingsManager at a scratch folder for as long as it lives, so that a test which reaches SettingsManager.Save
/// or SettingsManager.Current never touches the settings file of the person running the tests, and starts it with no
/// current settings, so the first Current loads from the scratch folder as a first start does. Dispose puts everything
/// back and deletes the folder. The class that holds one is in the "SettingsManagerGlobalState" collection: the redirect
/// is process-wide, so no other test that reaches SettingsManager may run beside it.
/// </summary>
/// <remarks>
/// The redirect SettingsSeedTests makes, by the same means: the paths are <c>static readonly</c>, which reflection will
/// not set once the type has run its initializer, so a tiny dynamic method stores them (<c>stsfld</c>); that class's own
/// helper is private to it, so the pattern is repeated here. One path more is redirected: the legacy FBWBA file. A Load with
/// no settings file would otherwise copy the legacy file of the machine into the scratch folder and read the old settings of
/// whoever runs the tests back as a fresh install.
/// </remarks>
internal sealed class SettingsRedirect : IDisposable
{
    private static readonly FieldInfo DirectoryField = SettingsManagerField("SettingsDirectory");
    private static readonly FieldInfo FilePathField = SettingsManagerField("SettingsFilePath");
    private static readonly FieldInfo LegacyFilePathField = SettingsManagerField("LegacySettingsFilePath");
    private static readonly FieldInfo CurrentSettingsField = SettingsManagerField("_currentSettings");

    private readonly object? _originalDirectory = DirectoryField.GetValue(null);
    private readonly object? _originalFilePath = FilePathField.GetValue(null);
    private readonly object? _originalLegacyFilePath = LegacyFilePathField.GetValue(null);
    private readonly object? _originalCurrentSettings = CurrentSettingsField.GetValue(null);

    /// <summary>The scratch folder. It is created by the first save into it.</summary>
    public string Folder { get; }

    /// <summary>The settings file inside it, the one SettingsManager now reads and writes.</summary>
    public string SettingsFile { get; }

    public SettingsRedirect()
    {
        Folder = Path.Combine(Path.GetTempPath(), "MSFSBlindAssistTests_" + Guid.NewGuid().ToString("N"));
        SettingsFile = Path.Combine(Folder, "settings.json");

        SetStaticField(DirectoryField, Folder);
        SetStaticField(FilePathField, SettingsFile);
        SetStaticField(LegacyFilePathField, Path.Combine(Folder, "legacy", "settings.json"));
        CurrentSettingsField.SetValue(null, null);   // not initonly, plain SetValue is fine
    }

    public void Dispose()
    {
        SetStaticField(DirectoryField, _originalDirectory);
        SetStaticField(FilePathField, _originalFilePath);
        SetStaticField(LegacyFilePathField, _originalLegacyFilePath);
        CurrentSettingsField.SetValue(null, _originalCurrentSettings);

        try
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a leftover scratch folder is harmless.
        }
    }

    private static FieldInfo SettingsManagerField(string name)
        => typeof(SettingsManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    // FieldInfo.SetValue refuses a static readonly field once the declaring type has run its initializer; a hand-emitted
    // stsfld against the same field is accepted by the runtime, which is how SettingsSeedTests redirects these too.
    private static void SetStaticField(FieldInfo field, object? value)
    {
        var method = new DynamicMethod(
            "ForceSetStatic_" + field.Name,
            typeof(void),
            new[] { typeof(object) },
            typeof(SettingsRedirect).Module,
            skipVisibility: true);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, field.FieldType);
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);

        var setter = (Action<object?>)method.CreateDelegate(typeof(Action<object?>));
        setter(value);
    }
}
