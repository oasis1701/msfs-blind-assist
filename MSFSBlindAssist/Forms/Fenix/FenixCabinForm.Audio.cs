using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

// The Cabin window's Audio page: boarding music with its channel and volume, the prerecorded
// announcements and the memo they are queued in, the announcement set, and the FAP loudspeaker volume.
// It plugs into the shell (FenixCabinForm.cs) through BuildAudioTab, UpdateAudioLists and UpdateAudioControls
// and sends only through the shell's RunMutationAsync (the FAP's audio mutations) and RunWriteAsync (the
// loudspeaker volume), which say "The Fenix is not answering." and "The Fenix did not accept that." themselves.
//
// Every handler checks in this order: the Fenix not answering, an unknown value, then
// the limits. Speech (CORE-7): a check box, a combo and a list say their own state, so those presses have
// no read-back; the volume, the announcement playing, the memo and the loudspeaker volume are what the
// reader cannot say, so a press reads that line back.
public sealed partial class FenixCabinForm
{
    // The lines a press waits on (FenixCabinLines): the PRAM volume, the announcement playing, the memo,
    // and the FAP loudspeaker volume.
    private const string VolumeKey = "audio.volume";
    private const string AnnouncingKey = "audio.announcing";
    private const string MemoKey = "audio.memo";
    private const string SpeakerVolumeKey = "audio.speakerVolume";

    private CheckBox _musicBox = null!;
    private ComboBox _channelCombo = null!;
    private DisplayListBox _announcementsList = null!;
    private DisplayListBox _memoList = null!;
    private FlowLayoutPanel _setPanel = null!;
    private ComboBox _setCombo = null!;

    // The announcements and the memo as the lists show them, so a press acts on the row the pilot selected.
    // They are set on every render (UpdateAudioLists), so they are never older than the last render.
    private IReadOnlyList<FenixCabinItem> _shownAnnouncements = Array.Empty<FenixCabinItem>();
    private IReadOnlyList<FenixCabinItem> _shownMemo = Array.Empty<FenixCabinItem>();

    // Whether the announcement set was last shown or hidden; null before the first render. Tracked here,
    // never read back from Visible, which says false for every control while its page is not showing.
    private bool? _setOffered;

    /// <summary>For the tests: the FAP's audio state as the window holds it.</summary>
    internal FenixCabinPramState? AudioState => _pram;

    /// <summary>For the tests: puts an audio state in the window, as a read of the FAP would, and asks for a render.</summary>
    internal void SetAudioState(FenixCabinPramState? pram)
    {
        _pram = pram;
        MarkDirty();
    }

    // ---- Building -------------------------------------------------------------------------------

    partial void BuildAudioTab(TabPage page)
    {
        // The page's value list fills what the rows below it leave. The rows dock to the bottom in the
        // order they are added to the page, the last one first: music (top), the lists, then the speaker row.
        AddLineList(page, "Audio status");

        // Boarding music, its channel and its volume. No AccessibleName on the check box and the buttons:
        // the reader reads their text. The labels carry the combos' mnemonics.
        _musicBox = new CheckBox { Text = "&Boarding music", AutoSize = true, TabIndex = 0 };
        _musicBox.CheckedChanged += OnMusicChanged;

        var channelLabel = new Label { Text = "Music &channel", AutoSize = true, Margin = new Padding(12, 7, 3, 3), TabIndex = 1 };
        _channelCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Music channel",
            Width = 200,
            TabIndex = 2
        };
        _channelCombo.SelectionChangeCommitted += OnChannelCommitted;
        // A value that changed while the list was open is shown once it closes.
        _channelCombo.DropDownClosed += (_, _) => MarkDirty();

        FlowLayoutPanel music = AudioRow(tabIndex: 1);
        music.Controls.Add(_musicBox);
        music.Controls.Add(channelLabel);
        music.Controls.Add(_channelCombo);
        music.Controls.Add(AudioButton("Volume &up", 3, () => StepVolume(up: true)));
        music.Controls.Add(AudioButton("Volume &down", 4, () => StepVolume(up: false)));
        page.Controls.Add(music);

        // The announcements and the memo: a list each, with its buttons under it.
        _announcementsList = AudioList("Prerecorded announcements", tabIndex: 0);
        _memoList = AudioList("Announcement memo", tabIndex: 2);

        var lists = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 190,
            ColumnCount = 2,
            RowCount = 2,
            TabIndex = 2
        };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lists.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        FlowLayoutPanel announcementButtons = ButtonFlow(tabIndex: 1);
        announcementButtons.Controls.Add(AudioButton("&Play now", 0, PlayNow));
        announcementButtons.Controls.Add(AudioButton("&Add to memo", 1, AddToMemo));
        FlowLayoutPanel memoButtons = ButtonFlow(tabIndex: 3);
        memoButtons.Controls.Add(AudioButton("&Remove from memo", 0, RemoveFromMemo));
        memoButtons.Controls.Add(AudioButton("Clear &memo", 1, ClearMemo));
        memoButtons.Controls.Add(AudioButton("Play &next", 2, PlayNext));
        memoButtons.Controls.Add(AudioButton("Play a&ll", 3, PlayAll));
        memoButtons.Controls.Add(AudioButton("&Stop", 4, StopAnnouncements));

        lists.Controls.Add(_announcementsList, 0, 0);
        lists.Controls.Add(announcementButtons, 0, 1);
        lists.Controls.Add(_memoList, 1, 0);
        lists.Controls.Add(memoButtons, 1, 1);
        page.Controls.Add(lists);

        // The announcement set (only when the Fenix has more than one) and the FAP loudspeaker volume.
        var setLabel = new Label { Text = "Announcement s&et", AutoSize = true, Margin = new Padding(12, 7, 3, 3), TabIndex = 0 };
        _setCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Announcement set",
            Width = 70,
            TabIndex = 1
        };
        _setCombo.SelectionChangeCommitted += OnSetCommitted;
        _setCombo.DropDownClosed += (_, _) => MarkDirty();
        _setPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            TabIndex = 0
        };
        _setPanel.Controls.Add(setLabel);
        _setPanel.Controls.Add(_setCombo);

        FlowLayoutPanel speaker = AudioRow(tabIndex: 3);
        speaker.Controls.Add(_setPanel);
        speaker.Controls.Add(AudioButton("FAP loudspeaker volume up", 1, () => StepSpeakerVolume(up: true)));
        speaker.Controls.Add(AudioButton("FAP loudspeaker volume down", 2, () => StepSpeakerVolume(up: false)));
        page.Controls.Add(speaker);

        // Until the first audio state says the Fenix has more than one set.
        _setPanel.Visible = false;
        _setOffered = false;
    }

    private static FlowLayoutPanel AudioRow(int tabIndex) => new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
        TabIndex = tabIndex
    };

    private static FlowLayoutPanel ButtonFlow(int tabIndex) => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
        TabIndex = tabIndex
    };

    private static DisplayListBox AudioList(string accessibleName, int tabIndex)
        => new() { Dock = DockStyle.Fill, AccessibleName = accessibleName, TabIndex = tabIndex };

    private static Button AudioButton(string text, int tabIndex, Action onClick)
    {
        var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, TabIndex = tabIndex };
        button.Click += (_, _) => onClick();
        return button;
    }

    // ---- Setting the controls from the audio state ----------------------------------------------

    // On every render, also while the controls are held (the shell calls it before the hold): a list is the
    // data a press acts on, so it follows the Fenix at once. Only a check box or a combo flips back and
    // needs the hold. Rewritten in place, so the pilot's selection stays.
    partial void UpdateAudioLists()
    {
        FenixCabinPramState? pram = _pram;

        _shownAnnouncements = pram?.Announcements ?? Array.Empty<FenixCabinItem>();
        _announcementsList.SetLines(_shownAnnouncements.Select(item => item.Name).ToArray());
        _shownMemo = pram?.Memo ?? Array.Empty<FenixCabinItem>();
        _memoList.SetLines(_shownMemo.Select(item => item.Name).ToArray());
    }

    partial void UpdateAudioControls()
    {
        FenixCabinPramState? pram = _pram;

        bool music = pram is { MusicOn: true };
        if (_musicBox.Checked != music) _musicBox.Checked = music;

        UpdateChannelCombo(pram);
        UpdateSetCombo(pram);
    }

    // The items are the channel names, rebuilt only when the names change (never by comparing the state
    // records, which compare their lists by reference). Left alone while the pilot has the list open;
    // DropDownClosed asks for another render.
    private void UpdateChannelCombo(FenixCabinPramState? pram)
    {
        if (_channelCombo.DroppedDown) return;

        string[] names = pram?.Channels.Select(channel => channel.Name).ToArray() ?? Array.Empty<string>();
        if (!ItemsAre(_channelCombo, names))
        {
            _channelCombo.BeginUpdate();
            try
            {
                _channelCombo.Items.Clear();
                _channelCombo.Items.AddRange(names);
            }
            finally
            {
                _channelCombo.EndUpdate();
            }
        }

        // An index outside the channels selects none.
        int selected = pram is { SelectedChannel: >= 0 } && pram.SelectedChannel < names.Length ? pram.SelectedChannel : -1;
        if (_channelCombo.SelectedIndex != selected) _channelCombo.SelectedIndex = selected;
    }

    // The set 0 … MaxVariant, shown only when the Fenix has more than one (MaxVariant above 0).
    private void UpdateSetCombo(FenixCabinPramState? pram)
    {
        int max = pram?.MaxVariant ?? 0;
        bool offered = max > 0;
        if (_setOffered != offered)
        {
            _setOffered = offered;
            _setPanel.Visible = offered;
        }
        if (!offered || _setCombo.DroppedDown) return;

        string[] values = Enumerable.Range(0, max + 1).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (!ItemsAre(_setCombo, values))
        {
            _setCombo.BeginUpdate();
            try
            {
                _setCombo.Items.Clear();
                _setCombo.Items.AddRange(values);
            }
            finally
            {
                _setCombo.EndUpdate();
            }
        }

        // The index is the value; one outside 0 … MaxVariant selects none.
        int selected = pram!.PreferredVariant >= 0 && pram.PreferredVariant <= max ? pram.PreferredVariant : -1;
        if (_setCombo.SelectedIndex != selected) _setCombo.SelectedIndex = selected;
    }

    private static bool ItemsAre(ComboBox combo, string[] texts)
    {
        if (combo.Items.Count != texts.Length) return false;
        for (int i = 0; i < texts.Length; i++)
        {
            if (!string.Equals(combo.Items[i] as string, texts[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    // ---- Refusals -------------------------------------------------------------------------------

    /// <summary>
    /// True, after saying so, when no audio press can work: the Fenix is not answering (the
    /// shell's not-answering message), or it answers without an audio state ("No audio data from the
    /// Fenix.", as the page's line reads "Audio: no data from the Fenix"). A press is never sent on a state
    /// the window does not have.
    /// </summary>
    private bool RefuseWithoutAudioState()
    {
        if (!_cache.AnyKnown)
        {
            Refuse(NotAnswering);
            return true;
        }
        if (_pram == null)
        {
            Refuse("No audio data from the Fenix.");
            return true;
        }
        return false;
    }

    // Runs the setter with the change handlers off: it shows the Fenix's value again after a refusal.
    private void WhileUpdating(Action set)
    {
        bool before = _updatingControls;
        _updatingControls = true;
        try
        {
            set();
        }
        finally
        {
            _updatingControls = before;
        }
    }

    // ---- Music: check box, channel and volume ---------------------------------------------------

    private void OnMusicChanged(object? sender, EventArgs e)
    {
        if (_updatingControls) return;
        bool isChecked = _musicBox.Checked;
        if (RefuseWithoutAudioState())
        {
            WhileUpdating(() => _musicBox.Checked = _pram is { MusicOn: true });
            return;
        }
        HoldControls();
        // The explicit target, as the FAP's own toggle sends it. No read-back: the reader says the new state.
        _ = RunMutationAsync(FenixCabinPram.SetMusicOn(isChecked), null, speakIfUnchanged: false);
    }

    private void OnChannelCommitted(object? sender, EventArgs e)
    {
        if (_updatingControls) return;
        int index = _channelCombo.SelectedIndex;
        if (RefuseWithoutAudioState())
        {
            WhileUpdating(() => _channelCombo.SelectedIndex = ShownChannel(_pram));
            return;
        }
        if (index < 0 || index >= _channelCombo.Items.Count) return;
        HoldControls();
        // No read-back: the reader says the new channel.
        _ = RunMutationAsync(FenixCabinPram.SetChannel(index), null, speakIfUnchanged: false);
    }

    private static int ShownChannel(FenixCabinPramState? pram)
        => pram is { SelectedChannel: >= 0 } && pram.SelectedChannel < pram.Channels.Count ? pram.SelectedChannel : -1;

    // The FAP's own steps: the volume plus or minus 1. Its range is not measured, so the only limit is 0.
    private void StepVolume(bool up)
    {
        if (RefuseWithoutAudioState()) return;
        int volume = _pram!.Volume;
        if (!up && volume <= 0)
        {
            Refuse("Volume is already 0.");
            return;
        }
        _ = RunMutationAsync(FenixCabinPram.SetVolume(up ? volume + 1 : volume - 1), VolumeKey, speakIfUnchanged: true);
    }

    // ---- Announcements and memo -----------------------------------------------------------------

    // The id behind the announcement list's selected row, or null: no selection, a stale row, or a
    // malformed entry without an id, which is never sent.
    private string? SelectedAnnouncementId()
    {
        int index = _announcementsList.SelectedIndex;
        if (index < 0 || index >= _shownAnnouncements.Count) return null;
        string id = _shownAnnouncements[index].Id;
        return id.Length > 0 ? id : null;
    }

    private void PlayNow()
    {
        if (RefuseWithoutAudioState()) return;
        if (SelectedAnnouncementId() is not string id)
        {
            Refuse("Select an announcement first.");
            return;
        }
        _ = RunMutationAsync(FenixCabinPram.DirectPlay(id), AnnouncingKey, speakIfUnchanged: false);
    }

    private void AddToMemo()
    {
        if (RefuseWithoutAudioState()) return;
        if (SelectedAnnouncementId() is not string id)
        {
            Refuse("Select an announcement first.");
            return;
        }
        _ = RunMutationAsync(FenixCabinPram.AddToMemo(id), MemoKey, speakIfUnchanged: false);
    }

    private void RemoveFromMemo()
    {
        if (RefuseWithoutAudioState()) return;
        int index = _memoList.SelectedIndex;
        if (index < 0 || index >= _shownMemo.Count)
        {
            Refuse("Select a memo entry first.");
            return;
        }
        // The Fenix applies the index to its memo as it is now. If that is no longer the memo the pilot
        // sees (it changed after the last render, a poll lag away), the index may name another entry:
        // show the new memo and ask again.
        if (!SameEntries(_shownMemo, _pram!.Memo))
        {
            MarkDirty();
            Refuse("The memo has changed. Select the entry again.");
            return;
        }
        _ = RunMutationAsync(FenixCabinPram.RemoveFromMemo(index), MemoKey, speakIfUnchanged: false);
    }

    // The same entries in the same order, by id and name (never the state records, which compare their
    // lists by reference).
    private static bool SameEntries(IReadOnlyList<FenixCabinItem> a, IReadOnlyList<FenixCabinItem> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Id, b[i].Id, StringComparison.Ordinal)
                || !string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    // A press that would change nothing says so up front and sends nothing (CORE-7's validation messages).
    private void ClearMemo()
    {
        if (RefuseWithoutAudioState()) return;
        if (_pram!.Memo.Count == 0)
        {
            Refuse("The memo is already empty.");
            return;
        }
        _ = RunMutationAsync(FenixCabinPram.ClearMemo(), MemoKey, speakIfUnchanged: false);
    }

    private void PlayNext()
    {
        if (RefuseWithoutAudioState() || RefuseEmptyMemo()) return;
        _ = RunMutationAsync(FenixCabinPram.PlayNext(), AnnouncingKey, speakIfUnchanged: false);
    }

    private void PlayAll()
    {
        if (RefuseWithoutAudioState() || RefuseEmptyMemo()) return;
        _ = RunMutationAsync(FenixCabinPram.PlayAll(), AnnouncingKey, speakIfUnchanged: false);
    }

    // True, after saying so, when the memo has nothing to play. Call after RefuseWithoutAudioState.
    private bool RefuseEmptyMemo()
    {
        if (_pram!.Memo.Count > 0) return false;
        Refuse("The memo is empty.");
        return true;
    }

    private void StopAnnouncements()
    {
        if (RefuseWithoutAudioState()) return;
        // Nothing to stop: no announcement playing and the memo not being played.
        if (string.IsNullOrEmpty(_pram!.NowAnnouncing) && !_pram.PlayingAllInMemo)
        {
            Refuse("Nothing is playing.");
            return;
        }
        _ = RunMutationAsync(FenixCabinPram.Stop(), AnnouncingKey, speakIfUnchanged: false);
    }

    private void OnSetCommitted(object? sender, EventArgs e)
    {
        if (_updatingControls) return;
        int value = _setCombo.SelectedIndex;
        if (RefuseWithoutAudioState())
        {
            WhileUpdating(() => _setCombo.SelectedIndex = ShownSet(_pram));
            return;
        }
        // The items are 0 … MaxVariant, so the index is the value.
        if (value < 0 || value > _pram!.MaxVariant) return;
        HoldControls();
        // No read-back: the reader says the new set.
        _ = RunMutationAsync(FenixCabinPram.SetAnnouncementsVariant(value), null, speakIfUnchanged: false);
    }

    private static int ShownSet(FenixCabinPramState? pram)
        => pram is { PreferredVariant: >= 0 } && pram.PreferredVariant <= pram.MaxVariant ? pram.PreferredVariant : -1;

    // ---- FAP loudspeaker volume -----------------------------------------------------------------

    // One 5 percent step, from the volume the FAP Set-Up page shows. It does not need the audio state:
    // the volume is its own data name. An unknown volume is not stepped from the FAP's assumed 100.
    private void StepSpeakerVolume(bool up)
    {
        if (!_cache.AnyKnown)
        {
            Refuse(NotAnswering);
            return;
        }
        double? current = _cache.Number(FenixCabinNames.FapSpeakerVolume);
        if (current == null)
        {
            Refuse("FAP loudspeaker volume is not known.");
            return;
        }
        FenixCabinWrite? write = FenixCabinWrites.SpeakerVolumeStep(current, up);
        if (write == null)
        {
            Refuse(up ? "FAP loudspeaker volume is already at maximum." : "FAP loudspeaker volume is already at minimum.");
            return;
        }
        // The read-back speaks the line: "FAP loudspeaker volume: 95 percent".
        _ = RunWriteAsync(write, SpeakerVolumeKey, ReadbackWindow, speakIfUnchanged: true);
    }
}
