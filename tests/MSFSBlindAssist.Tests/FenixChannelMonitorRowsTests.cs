// Characterization tests for the Fenix display channel's six Ctrl+M rows (Services/FenixChannelAnnouncementRows.cs): the
// FMA changes, the E/WD warnings, cautions and memos, the DCDU messages and the ND messages. The dialog
// (FenixMonitorManagerForm) shows them after the cabin's four, an unticked row puts its key in
// FenixDisabledMonitorVariables, and a row's announcer asks IsMuted as it is about to speak, so a mute applies at once.
// Every row starts on, the memos' included (spec row 63, Robin, 2026-11-04).
//
// The contract: the keys are stable (a saved mute names them) and no variable's key; the labels answer to no other row's, as
// Ctrl+M is a flat list read by label; the rows come after the variable rows and the cabin rows, in the order FMA, warnings,
// cautions, memos, DCDU, ND. Nothing mutes a row but the pilot, on a fresh install and on a file that has run before alike, and
// a mute a file already holds stays.
//
// The first class is pure. The second reaches SettingsManager (Load seeds and saves; IsMuted reads Current), so it is in the
// "SettingsManagerGlobalState" collection and every test of it takes a SettingsRedirect (FenixChannelTestSettings.cs,
// which also keeps the whole test process away from a real settings file): a scratch folder of its own and no current
// settings, so each test starts as a first start does.

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
        new("FENIX_CHANNEL_DCDU", "DCDU messages"),
        new("FENIX_CHANNEL_ND", "ND messages")
    };

    [Fact]
    public void The_six_kinds_have_stable_keys_and_their_own_rows()
    {
        Assert.Equal("FENIX_CHANNEL_FMA", FenixChannelAnnouncementRows.FmaKey);
        Assert.Equal("FENIX_CHANNEL_WARNINGS", FenixChannelAnnouncementRows.WarningsKey);
        Assert.Equal("FENIX_CHANNEL_CAUTIONS", FenixChannelAnnouncementRows.CautionsKey);
        Assert.Equal("FENIX_CHANNEL_MEMOS", FenixChannelAnnouncementRows.MemosKey);
        Assert.Equal("FENIX_CHANNEL_DCDU", FenixChannelAnnouncementRows.DcduKey);
        Assert.Equal("FENIX_CHANNEL_ND", FenixChannelAnnouncementRows.NdKey);
        Assert.Equal(6, FenixChannelAnnouncementRows.Rows.Count);
        Assert.Equal(FenixChannelAnnouncementRows.NdKey, FenixChannelAnnouncementRows.Rows[^1].Key);

        Assert.Equal(ChannelRows, FenixChannelAnnouncementRows.Rows);
    }

    [Fact]
    public void The_six_channel_rows_follow_the_cabin_rows()
    {
        var variables = new FenixA320Definition().GetVariables();

        List<MonitorRow> rows = FenixMonitorManagerForm.BuildRows(variables);

        // The variable rows first, exactly as before, then the four cabin rows in their order, then the six channel rows
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
        Assert.Equal(ChannelRows.Length, keys.Length);   // six keys to check, not an empty list that checks nothing
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
        Assert.Equal(ChannelRows.Length, labels.Length);   // six labels to check, not an empty list that checks nothing
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

    // A pilot's own mute of an older Fenix row, which a load must leave as it is.
    private const string OlderMute = "S_SEAT_HEIGHT_CAPT";

    private readonly SettingsRedirect _settings = new();

    public void Dispose() => _settings.Dispose();

    [Fact]
    public void No_row_starts_muted_on_either_load_path()
    {
        // A fresh install: no settings file at all.
        UserSettings fresh = SettingsManager.Load();
        foreach (MonitorRow row in FenixChannelAnnouncementRows.Rows)
        {
            Assert.DoesNotContain(row.Key, fresh.FenixDisabledMonitorVariables);
        }

        // An install that has run before: its file carries the older Fenix seed's flag and a mute of the pilot's own.
        Directory.CreateDirectory(_settings.Folder);
        File.WriteAllText(_settings.SettingsFile,
            $$"""{ "FenixMonitorDefaultsSeeded": true, "FenixDisabledMonitorVariables": [ "{{OlderMute}}" ] }""");

        UserSettings upgraded = SettingsManager.Load();

        Assert.Equal(new[] { OlderMute }, upgraded.FenixDisabledMonitorVariables);
    }

    [Fact]
    public void A_memos_mute_the_file_already_holds_stays_until_the_row_is_ticked()
    {
        // A file from an earlier build of this branch, which seeded this mute and wrote that seed's flag. The flag is no
        // setting any more and the file still loads; the row stays as the list says, and a tick sticks at the next start.
        Directory.CreateDirectory(_settings.Folder);
        File.WriteAllText(_settings.SettingsFile,
            $$"""{ "FenixMonitorDefaultsSeeded": true, "FenixChannelMonitorDefaultsSeeded": true, "FenixDisabledMonitorVariables": [ "{{MemosKey}}" ] }""");

        UserSettings earlier = SettingsManager.Load();
        Assert.Equal(new[] { MemosKey }, earlier.FenixDisabledMonitorVariables);

        earlier.FenixDisabledMonitorVariables.Remove(MemosKey);
        SettingsManager.Save(earlier);

        UserSettings restarted = SettingsManager.Load();
        Assert.Empty(restarted.FenixDisabledMonitorVariables);
    }

    [Fact]
    public void A_mute_applies_at_once()
    {
        // The first use of Current loads from the scratch folder, as a first start does: a fresh install.
        UserSettings settings = SettingsManager.Current;
        Assert.False(FenixChannelAnnouncementRows.IsMuted(FenixChannelAnnouncementRows.FmaKey));
        Assert.False(FenixChannelAnnouncementRows.IsMuted(MemosKey));

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
        settings.FenixDisabledMonitorVariables.Add(MemosKey);
        SettingsManager.Save();
        Assert.True(FenixChannelAnnouncementRows.IsMuted(MemosKey));
        SettingsManager.Save(new UserSettings { FenixDisabledMonitorVariables = { FenixChannelAnnouncementRows.WarningsKey } });
        Assert.True(FenixChannelAnnouncementRows.IsMuted(FenixChannelAnnouncementRows.WarningsKey));
        Assert.False(FenixChannelAnnouncementRows.IsMuted(MemosKey));
    }
}
