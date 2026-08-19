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

    // Cached by LoadFrom and reused by UpdateStatusText, which fires on every
    // SelectedIndexChanged -- i.e. on every arrow-key press while a screen reader user
    // browses the dropdown. Re-resolving against live WASAPI state per keystroke would
    // perform TWO full endpoint enumerations each time (Enumerate() and
    // DefaultEndpointInfo(), each constructing its own MMDeviceEnumerator) on the UI thread --
    // the same class of defect as re-querying TaxiAssistForm's gate list per keystroke. Real
    // endpoints only, same contract as AudioOutputRouter.Enumerate(); UpdateStatusText
    // resolves through the pure AudioDeviceSelector.Resolve directly against these instead of
    // re-enumerating.
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
        string savedId = settings.GuidanceToneDeviceId ?? string.Empty;
        string savedName = settings.GuidanceToneDeviceName ?? string.Empty;

        // Enumerated ONCE per load and cached (see the field comments) rather than once here
        // AND again inside every later UpdateStatusText call.
        _realDevices = AudioOutputRouter.Shared.Enumerate();
        _defaultEndpoint = AudioOutputRouter.Shared.DefaultEndpointInfo();

        _deviceRows.Clear();
        _deviceRows.Add(new AudioOutputDevice(AudioDeviceSelector.FollowWindowsDefaultId, AudioDeviceSelector.DefaultDeviceLabel));
        _deviceRows.AddRange(_realDevices);

        bool savedIsPresent = string.IsNullOrWhiteSpace(savedId)
            || _deviceRows.Any(d => string.Equals(d.Id, savedId, StringComparison.OrdinalIgnoreCase));

        // A saved device that is not connected right now is still listed, so the pilot's
        // choice stays visible and is never silently reset to default behind their back.
        if (!savedIsPresent)
        {
            _deviceRows.Add(new AudioOutputDevice(savedId, string.IsNullOrWhiteSpace(savedName) ? "Saved device" : savedName));
        }

        _deviceCombo.Items.Clear();
        foreach (AudioOutputDevice row in _deviceRows)
        {
            bool connected = string.IsNullOrWhiteSpace(row.Id)
                || !string.Equals(row.Id, savedId, StringComparison.OrdinalIgnoreCase)
                || savedIsPresent;
            _deviceCombo.Items.Add(connected ? row.FriendlyName : $"{row.FriendlyName} (not connected)");
        }

        int index = _deviceRows.FindIndex(d => string.Equals(d.Id, savedId, StringComparison.OrdinalIgnoreCase));
        _deviceCombo.SelectedIndex = index >= 0 ? index : 0;

        UpdateStatusText();
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
        // AudioOutputRouter to re-resolve, which would re-enumerate WASAPI from
        // scratch on every call -- this method is wired to SelectedIndexChanged, so a screen
        // reader user arrowing the dropdown would otherwise fire two full endpoint
        // enumerations per keystroke on the UI thread.
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
        return tone;
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
