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

    private ComboBox _deviceCombo = null!;
    private Button _testToneButton = null!;
    private TextBox _statusTextBox = null!;

    private readonly List<AudioOutputDevice> _deviceRows = new();
    private AudioToneGenerator? _testTone;

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
        // Routes the initial label/name through the same helper every later state change
        // uses, so Text and AccessibleName can never drift apart — see SetTestToneButtonState.
        SetTestToneButtonState(playing: false);
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

        _deviceRows.Clear();
        _deviceRows.Add(new AudioOutputDevice(AudioDeviceSelector.FollowWindowsDefaultId, AudioDeviceSelector.DefaultDeviceLabel));
        _deviceRows.AddRange(AudioOutputDeviceService.Enumerate());

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
        StopTestTone();
        SetTestToneButtonState(playing: false);
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
        AudioOutputDevice row = SelectedRow();
        AudioDeviceResolution resolution = AudioOutputDeviceService.ResolveCurrent(row.Id, row.FriendlyName);
        _statusTextBox.Text = resolution.StatusText;
    }

    private void TestToneButton_Click(object? sender, EventArgs e)
    {
        if (_testTone?.IsPlaying == true)
        {
            StopTestTone();
            SetTestToneButtonState(playing: false);
        }
        else
        {
            // The button state is set from what PlayTestTone actually achieved, never assumed
            // — AudioToneGenerator.Start swallows its own exceptions by contract (audio is
            // optional feedback), so a real endpoint failure returns silently with no tone
            // playing. Assuming success here left the button reading "Stop Test" for a tone
            // that never started: the NEXT press then took this same start branch again
            // instead of stopping anything — the button was inverted, not merely
            // stale-labelled.
            bool started = PlayTestTone();
            SetTestToneButtonState(playing: started);
        }
    }

    /// <summary>Sets the button's label AND its accessible name together. WinForms'
    /// ControlAccessibleObject.Name returns an explicitly-set AccessibleName permanently once
    /// set — it does NOT fall back to Text — so every site that changes what this button will
    /// do next must go through this helper instead of assigning .Text directly, or a screen
    /// reader keeps announcing the stale action (e.g. "Test tone" while activating it would
    /// actually stop one).</summary>
    private void SetTestToneButtonState(bool playing)
    {
        _testToneButton.Text = playing ? "Stop Test" : "Test Tone";
        _testToneButton.AccessibleName = playing ? "Stop test tone" : "Test tone";
    }

    /// <summary>Starts the audition tone and reports whether it actually started, so the
    /// caller can set the Test Tone button's state from reality rather than an assumption —
    /// see the comment in TestToneButton_Click. A failure that AudioToneGenerator.Start
    /// swallowed (no exception, tone just never started) is written to the status line
    /// instead of relying solely on the MessageBox below, which only ever fires for a genuine
    /// thrown exception — a realistic endpoint failure throws nothing.</summary>
    private bool PlayTestTone()
    {
        try
        {
            // Auditions the COMBO's current selection, not the saved setting, so devices can
            // be compared before committing to one. Passed through UNCHANGED — deviceId is ""
            // for the "Windows default device" row (AudioDeviceSelector.FollowWindowsDefaultId),
            // and CreatePlayer's deviceIdOverride treats "" and null completely differently
            // (see the <param> doc on AudioOutputDeviceService.CreatePlayer / AudioToneGenerator.
            // Start): null means "use the SAVED setting". Collapsing "" to null here (via an
            // IsNullOrWhiteSpace check that used to sit on this line) made auditioning "Windows
            // default device" silently play on the saved device instead — the one control built
            // to prove which device is which was lying about it.
            string deviceId = SelectedRow().Id;

            // Captured into a LOCAL and used throughout the background loop below instead of
            // re-reading the _testTone field: a Stop (button press, OnLeaving, tab switch,
            // dialog close) followed by a fresh Start can land inside the loop's ~100ms
            // Task.Delay granularity, and a field re-read would pan a stray value into a NEW
            // session rather than the one this loop is actually driving.
            var tone = new AudioToneGenerator();
            tone.Start(HandFlyWaveType.Sine, TestToneVolume, TestToneFrequencyHz,
                deviceIdOverride: deviceId);

            if (!tone.IsPlaying)
            {
                // Start() never throws (audio is optional feedback and degrades by contract
                // — see AudioOutputDeviceService's class doc), so a real "could not open this
                // endpoint" failure lands here silently rather than in the catch block below.
                // Without this check the button still claimed "playing" and the pilot got no
                // feedback at all about why the audition was silent.
                tone.Dispose();
                _statusTextBox.Text = "Could not play the test tone on the selected device.";
                return false;
            }

            _testTone = tone;

            // Pan left to right so the pilot can confirm the device is the stereo pair they
            // expect, which is what the steering tones depend on.
            Task.Run(async () =>
            {
                for (int i = 0; i < 20 && tone.IsPlaying; i++)
                {
                    float pan = (float)Math.Sin(i * 0.15) * 0.8f;
                    tone.SetPan(pan);
                    await Task.Delay(100);
                }

                if (tone.IsPlaying && IsHandleCreated && !IsDisposed)
                {
                    try
                    {
                        Invoke(() =>
                        {
                            // Re-check on the UI thread — the same thread every write to
                            // _testTone happens on, so this needs no lock — that `tone` is
                            // STILL the current session before stopping/resetting anything. A
                            // newer Start/Stop (another Test Tone press, OnLeaving, tab switch,
                            // dialog close) may have already replaced or cleared _testTone
                            // while this delegate sat queued on the UI thread; stopping THAT
                            // session or relabelling the button out from under it would be
                            // wrong.
                            if (ReferenceEquals(_testTone, tone))
                            {
                                StopTestTone();
                                SetTestToneButtonState(playing: false);
                            }
                        });
                    }
                    catch (ObjectDisposedException)
                    {
                        // Handle actually torn down mid-flight — Invoke throws this once the
                        // control's handle has been destroyed rather than merely closing.
                        // OnLeaving/Dispose also call StopTestTone, so the tone still stops.
                    }
                    catch (InvalidOperationException)
                    {
                        // ObjectDisposedException derives from this, so it is caught above;
                        // this covers the handle-destroyed-mid-flight window more generally
                        // (tab switched/dialog closed) — OnLeaving/Dispose also call
                        // StopTestTone, so the tone still stops either way.
                    }
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            // A genuine thrown exception (as opposed to Start()'s silent degrade above) —
            // kept as a MessageBox since it signals something unexpected enough to be worth
            // an explicit acknowledgement, but the status line still gets the reason too so
            // it isn't the pilot's only record of what happened.
            _statusTextBox.Text = $"Could not play the test tone: {ex.Message}";
            MessageBox.Show($"Failed to play test tone: {ex.Message}", "Audio Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>Stops and disposes the audition tone. Idempotent and non-throwing —
    /// OnLeaving and Dispose callers must never fail.</summary>
    private void StopTestTone()
    {
        try
        {
            _testTone?.Stop();
            _testTone?.Dispose();
        }
        catch
        {
            // Non-throwing by contract.
        }
        finally
        {
            _testTone = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopTestTone();
        }

        base.Dispose(disposing);
    }
}
