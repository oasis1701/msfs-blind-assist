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
            Text = "Test Tone",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(120, 30),
            AccessibleName = "Test tone",
            AccessibleDescription = "Play a tone on the selected device to confirm where the guidance tones will be heard"
        };
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
        _testToneButton.Text = "Test Tone";
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
            _testToneButton.Text = "Test Tone";
        }
        else
        {
            PlayTestTone();
            _testToneButton.Text = "Stop Test";
        }
    }

    private void PlayTestTone()
    {
        try
        {
            // Auditions the COMBO's current selection, not the saved setting, so devices can
            // be compared before committing to one.
            string deviceId = SelectedRow().Id;

            _testTone = new AudioToneGenerator();
            _testTone.Start(HandFlyWaveType.Sine, TestToneVolume, TestToneFrequencyHz,
                deviceIdOverride: string.IsNullOrWhiteSpace(deviceId) ? null : deviceId);

            // Pan left to right so the pilot can confirm the device is the stereo pair they
            // expect, which is what the steering tones depend on.
            Task.Run(async () =>
            {
                for (int i = 0; i < 20 && _testTone?.IsPlaying == true; i++)
                {
                    float pan = (float)Math.Sin(i * 0.15) * 0.8f;
                    _testTone?.SetPan(pan);
                    await Task.Delay(100);
                }

                if (_testTone?.IsPlaying == true && IsHandleCreated && !IsDisposed)
                {
                    try
                    {
                        Invoke(() =>
                        {
                            StopTestTone();
                            _testToneButton.Text = "Test Tone";
                        });
                    }
                    catch (InvalidOperationException)
                    {
                        // Handle destroyed mid-flight (tab switched/dialog closed) —
                        // OnLeaving/Dispose also call StopTestTone, so the tone still stops.
                    }
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to play test tone: {ex.Message}", "Audio Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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
