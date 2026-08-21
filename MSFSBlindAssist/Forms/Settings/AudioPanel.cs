using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>
/// Audio section of the unified Settings dialog: which output device MSFS BA's own tones
/// play on. One global setting covering taxi steering, takeoff assist centerline, hand fly,
/// visual landing guidance and the docking proximity beeps — so sim audio can stay on
/// speakers while the guidance tones go to a headset.
///
/// Screen-reader speech is NOT affected and is not offered here; NVDA and JAWS own their own
/// output device.
/// </summary>
public class AudioPanel : UserControl, ISettingsPanel
{
    // Audible enough to identify a device across a room without being startling in a
    // headset. The per-feature tones default to 0.05, but those are steering instruments
    // meant to sit under engine noise; this one has to answer "did that come out of the
    // right speakers?" in two seconds.
    private const double TestToneVolume = 0.1;
    private const double TestToneFrequencyHz = 440.0;

    // Two seconds of tone at TestTonePlayer's 100 ms tick — long enough to place a device in
    // the room, short enough not to become a nuisance if the pilot walks away from it.
    private const int TestToneTicks = 20;

    // One complete left-right-left cycle, built once. Panning is the ONLY thing this audition
    // demonstrates, so it has to reach both channels at this duration — which is exactly what
    // the hand-rolled loop this replaces did not do; see TestTonePan.FullCycle.
    private static readonly float[] PanSweep = TestTonePan.FullCycle(TestToneTicks);

    private ComboBox _deviceCombo = null!;
    private Button _testToneButton = null!;
    private TestTonePlayer _testTonePlayer = null!;
    private TextBox _statusTextBox = null!;

    private readonly List<AudioOutputDevice> _deviceRows = new();

    // The saved selection LoadFrom ran with, kept so the refresh triggers can rebuild the
    // same missing-device row; _loaded gates them off until LoadFrom has actually run.
    private string _savedId = string.Empty;
    private string _savedName = string.Empty;
    private bool _loaded;

    // Cached by LoadFrom and reused by UpdateStatusText, which fires on every
    // SelectedIndexChanged -- i.e. on every arrow-key press while a screen reader user
    // browses the dropdown. Re-resolving against live WASAPI state per keystroke would go back
    // to Core Audio twice each time: AudioOutputRouter.Enumerate() walks every active render
    // endpoint, and DefaultEndpointInfo() does a single GetDefaultAudioEndpoint lookup -- each
    // constructing its own MMDeviceEnumerator, on the UI thread. That is the same class of
    // defect as re-querying TaxiAssistForm's gate list per keystroke. Real endpoints only, same
    // contract as AudioOutputRouter.Enumerate(); UpdateStatusText resolves through the pure
    // AudioDeviceSelector.Resolve directly against these instead of re-enumerating.
    private IReadOnlyList<AudioOutputDevice> _realDevices = Array.Empty<AudioOutputDevice>();
    private (string Id, string Name) _defaultEndpoint = (string.Empty, string.Empty);

    public string TabTitle => "Audio";

    public AudioPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        int yPos = 20;
        const int labelHeight = 23;
        const int rowHeight = 23;

        var deviceLabel = new Label
        {
            Text = "Guidance tone output device:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(450, labelHeight),
            AccessibleName = "Guidance tone output device label"
        };
        Controls.Add(deviceLabel);

        yPos += labelHeight + 5;
        _deviceCombo = new ComboBox
        {
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(420, rowHeight),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Guidance tone output device",
            AccessibleDescription = "Choose which audio device plays taxi, takeoff, hand fly, landing guidance and docking tones"
        };
        _deviceCombo.SelectedIndexChanged += (_, _) => UpdateStatusText();
        // Refresh as the list is about to SHOW (DropDown fires before it opens), so a device
        // plugged in while this dialog is open is findable without closing and reopening the
        // dialog — see RefreshDeviceList for why there is no background-event refresh.
        _deviceCombo.DropDown += (_, _) => RefreshDeviceList();
        Controls.Add(_deviceCombo);

        yPos += rowHeight + 10;
        _testToneButton = new Button
        {
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(120, 30),
            AccessibleDescription = "Play a tone on the selected device to confirm where the guidance tones will be heard"
        };
        // The player owns this button's Text and AccessibleName from here on (it sets the idle
        // pair in its constructor), so they can never drift apart — see TestTonePlayer. The
        // failure sink is the status TextBox rather than a dialog: it is in the tab order, so
        // a screen-reader user can go back and re-read it.
        _testTonePlayer = new TestTonePlayer(_testToneButton, message => _statusTextBox.Text = message);
        _testToneButton.Click += TestToneButton_Click;
        Controls.Add(_testToneButton);

        yPos += 40;
        var statusLabel = new Label
        {
            Text = "Status:",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(450, labelHeight),
            AccessibleName = "Audio device status label"
        };
        Controls.Add(statusLabel);

        yPos += labelHeight + 5;
        _statusTextBox = new TextBox
        {
            // Read-only TextBox, never a Label: a Label is not in the tab order, so a screen
            // reader user would have to hunt for this with the review cursor.
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(420, rowHeight),
            ReadOnly = true,
            AccessibleName = "Audio device status"
        };
        Controls.Add(_statusTextBox);

        yPos += rowHeight + 15;
        var noteLabel = new Label
        {
            Text = "Screen reader speech is not affected by this setting.",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(450, labelHeight * 2),
            ForeColor = System.Drawing.Color.Gray,
            AccessibleName = "Audio device note"
        };
        Controls.Add(noteLabel);
    }

    public void LoadFrom(UserSettings settings)
    {
        _savedId = settings.GuidanceToneDeviceId ?? string.Empty;
        _savedName = settings.GuidanceToneDeviceName ?? string.Empty;
        _loaded = true;

        PopulateDeviceList(selectId: _savedId);
    }

    /// <summary>
    /// Enumerates the endpoints (once — cached for every later UpdateStatusText call, see the
    /// field comments) and rebuilds the combo, selecting <paramref name="selectId"/> when it
    /// is still listed, else the saved device, else the default row. LoadFrom calls this with
    /// the saved selection; the refresh triggers (tab entry, dropdown open) call it with the
    /// pilot's CURRENT selection so a refresh never silently discards an uncommitted choice.
    /// </summary>
    private void PopulateDeviceList(string selectId)
    {
        _realDevices = AudioOutputRouter.Shared.Enumerate();
        _defaultEndpoint = AudioOutputRouter.Shared.DefaultEndpointInfo();

        _deviceRows.Clear();
        _deviceRows.Add(new AudioOutputDevice(AudioDeviceSelector.FollowWindowsDefaultId, AudioDeviceSelector.DefaultDeviceLabel));
        _deviceRows.AddRange(_realDevices);

        // The resolver already answers "is the saved device actually there?" — it is the same
        // question, asked the same way, that produces the status line and drives every routing
        // fallback. Re-deriving it here with a second presence rule (which had to special-case
        // the synthetic default row twice over) was one more place for the two answers to
        // disagree.
        AudioDeviceResolution saved = AudioDeviceSelector.Resolve(
            _savedId, _savedName, _realDevices, _defaultEndpoint.Id, _defaultEndpoint.Name);
        bool savedIsMissing = saved.FellBack;

        // A saved device that is not connected right now is still listed, so the pilot's
        // choice stays visible and is never silently reset to default behind their back.
        // The ROW keeps the CLEAN stored name — a blank stays blank, so ApplyTo can never
        // persist a display placeholder ("Saved device") as though it were the hardware's
        // name and have the fallback announcement speak it; the placeholder belongs to the
        // combo's DISPLAY text below, alongside "(not connected)".
        int missingRowIndex = -1;
        if (savedIsMissing)
        {
            missingRowIndex = _deviceRows.Count;
            _deviceRows.Add(new AudioOutputDevice(_savedId, _savedName));
        }

        _deviceCombo.Items.Clear();
        for (int i = 0; i < _deviceRows.Count; i++)
        {
            AudioOutputDevice row = _deviceRows[i];
            _deviceCombo.Items.Add(i == missingRowIndex
                ? $"{(string.IsNullOrWhiteSpace(row.FriendlyName) ? "Saved device" : row.FriendlyName)} (not connected)"
                : row.FriendlyName);
        }

        int index = _deviceRows.FindIndex(d => string.Equals(d.Id, selectId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = _deviceRows.FindIndex(d => string.Equals(d.Id, _savedId, StringComparison.OrdinalIgnoreCase));
        }

        _deviceCombo.SelectedIndex = index >= 0 ? index : 0;

        UpdateStatusText();
    }

    /// <summary>
    /// Re-enumerates and rebuilds the device list, keeping the pilot's current selection.
    /// Wired to the tab becoming visible and to the combo's dropdown OPENING — the two
    /// moments a screen-reader user goes looking for a device — so a headset plugged in
    /// while the Settings dialog is already open actually appears (the list used to be
    /// snapshotted once per dialog open, with no refresh and no staleness hint). Never
    /// wired to a background device event: rebuilding the combo underneath a user who is
    /// arrowing it would move their caret, the same rebuild-under-the-caret failure the
    /// Monitor Manager invariants exist to prevent.
    /// </summary>
    private void RefreshDeviceList()
    {
        if (!_loaded || _deviceCombo == null || _deviceCombo.IsDisposed)
        {
            return;
        }

        PopulateDeviceList(selectId: SelectedRow().Id);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            RefreshDeviceList();
        }
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = string.Empty;
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        AudioOutputDevice row = SelectedRow();
        settings.GuidanceToneDeviceId = row.Id;
        // Store the CLEAN friendly name, never the combo's "(not connected)" display text.
        settings.GuidanceToneDeviceName = string.IsNullOrWhiteSpace(row.Id) ? string.Empty : row.FriendlyName;
    }

    public void OnLeaving()
    {
        // Stop() also returns the button to its idle label, so re-entering the tab never shows
        // a stale "Stop Test".
        _testTonePlayer?.Stop();
    }

    private AudioOutputDevice SelectedRow()
    {
        int index = _deviceCombo.SelectedIndex;
        if (index < 0 || index >= _deviceRows.Count)
        {
            return new AudioOutputDevice(AudioDeviceSelector.FollowWindowsDefaultId, AudioDeviceSelector.DefaultDeviceLabel);
        }

        return _deviceRows[index];
    }

    private void UpdateStatusText()
    {
        // Writing a TextBox is not an announcement, so this does not violate the
        // never-announce-a-combo-change rule; the screen reader speaks the combo itself.
        //
        // Resolves against the CACHED _realDevices/_defaultEndpoint (see their field
        // comments) via the pure AudioDeviceSelector.Resolve directly, rather than asking
        // AudioOutputRouter to re-resolve, which would go back to Core Audio from scratch on
        // every call -- this method is wired to SelectedIndexChanged, so a screen reader user
        // arrowing the dropdown would otherwise fire an endpoint enumeration plus a default-
        // endpoint lookup per keystroke on the UI thread.
        AudioOutputDevice row = SelectedRow();
        AudioDeviceResolution resolution = AudioDeviceSelector.Resolve(
            row.Id, row.FriendlyName, _realDevices, _defaultEndpoint.Id, _defaultEndpoint.Name);
        _statusTextBox.Text = resolution.StatusText;
    }

    private void TestToneButton_Click(object? sender, EventArgs e)
    {
        // Everything the audition needs to get right — whether Start actually produced a
        // sounding tone, the button's Text/AccessibleName pairing, the stale-session guard and
        // the auto-stop — belongs to TestTonePlayer. All this panel supplies is which device
        // to open and what to do on each tick.
        _testTonePlayer.Toggle(StartAuditionTone, (tone, i) => tone.SetPan(PanSweep[i]), TestToneTicks);
    }

    /// <summary>Constructs and starts the audition tone on the device the combo currently
    /// shows. Returns it unconditionally — whether it actually sounded is TestTonePlayer's
    /// check, because AudioToneGenerator.Start degrades silently by contract.</summary>
    private AudioToneGenerator? StartAuditionTone()
    {
        // Auditions the COMBO's current selection, not the saved setting, so devices can
        // be compared before committing to one. Passed through UNCHANGED — deviceId is ""
        // for the "Windows default device" row (AudioDeviceSelector.FollowWindowsDefaultId),
        // and OpenFor's deviceIdOverride treats "" and null completely differently
        // (see the <param> doc on AudioOutputRouter.OpenFor / AudioToneGenerator.Start):
        // null means "use the SAVED setting". Collapsing "" to null here (via an
        // IsNullOrWhiteSpace check that used to sit on this line) made auditioning "Windows
        // default device" silently play on the saved device instead — the one control built
        // to prove which device is which was lying about it.
        string deviceId = SelectedRow().Id;

        var tone = new AudioToneGenerator();
        tone.Start(HandFlyWaveType.Sine, TestToneVolume, TestToneFrequencyHz,
            deviceIdOverride: deviceId);

        if (tone.IsPlaying)
        {
            ReportAuditionDevice(deviceId, tone.CurrentDeviceId);
        }

        return tone;
    }

    /// <summary>Says which device the audition ACTUALLY reached, which is not always the one
    /// the pilot picked.
    ///
    /// OpenFor falls back to the default endpoint when the chosen one will not open, and hands
    /// back a perfectly playing session — so IsPlaying alone cannot tell the pilot whether they
    /// just heard the device they selected, and the status line went on naming the selection
    /// while the sound came out of something else. That is the exact failure this whole panel
    /// exists to make impossible. AudioToneGenerator.CurrentDeviceId carries the answer and
    /// had no reader.
    ///
    /// A match refreshes the status line from the resolver rather than leaving whatever a
    /// previous failed audition wrote there, so the warning below can never outlive the
    /// condition that produced it.</summary>
    private void ReportAuditionDevice(string requestedId, string actualId)
    {
        // The "Windows default device" row asks for whatever Windows currently calls the
        // default, so there is no specific endpoint for the session to contradict.
        if (string.IsNullOrWhiteSpace(requestedId)
            || string.Equals(actualId, requestedId, StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatusText();
            return;
        }

        string actualName = _realDevices.FirstOrDefault(d =>
            string.Equals(d.Id, actualId, StringComparison.OrdinalIgnoreCase)).FriendlyName;
        _statusTextBox.Text = string.IsNullOrWhiteSpace(actualName)
            ? "Selected device could not be opened - playing on another device."
            : $"Selected device could not be opened - playing on {actualName}.";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _testTonePlayer?.Dispose();
        }

        base.Dispose(disposing);
    }
}
