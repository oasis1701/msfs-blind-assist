using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

public partial class HandFlyOptionsForm : Form
{
    private Label titleLabel = null!;
    private GroupBox feedbackModeGroup = null!;
    private RadioButton tonesOnlyRadio = null!;
    private RadioButton announcementsOnlyRadio = null!;
    private RadioButton bothRadio = null!;

    private Label waveTypeLabel = null!;
    private ComboBox waveTypeCombo = null!;

    private Label volumeLabel = null!;
    private TrackBar volumeTrackBar = null!;
    private Label volumeValueLabel = null!;

    private Button testToneButton = null!;
    private Button okButton = null!;
    private Button cancelButton = null!;

    private AudioToneGenerator? testToneGenerator;

    public HandFlyFeedbackMode SelectedFeedbackMode { get; private set; }
    public HandFlyWaveType SelectedWaveType { get; private set; }
    public double SelectedVolume { get; private set; }

    public HandFlyOptionsForm(HandFlyFeedbackMode currentMode, HandFlyWaveType currentWaveType, double currentVolume)
    {
        SelectedFeedbackMode = currentMode;
        SelectedWaveType = currentWaveType;
        SelectedVolume = currentVolume;
        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        Text = "Hand Fly Options";
        Size = new Size(500, 450);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Title Label
        titleLabel = new Label
        {
            Text = "Configure hand fly mode audio and announcement settings:",
            Location = new Point(20, 20),
            Size = new Size(450, 20),
            AccessibleName = "Hand Fly Options Title"
        };

        // Feedback Mode Group
        feedbackModeGroup = new GroupBox
        {
            Text = "Feedback Mode",
            Location = new Point(20, 50),
            Size = new Size(450, 120),
            AccessibleName = "Feedback Mode Selection"
        };

        tonesOnlyRadio = new RadioButton
        {
            Text = "Audio Tones Only",
            Location = new Point(15, 25),
            Size = new Size(420, 25),
            AccessibleName = "Tones Only",
            AccessibleDescription = "Play audio tones without screen reader announcements",
            Checked = SelectedFeedbackMode == HandFlyFeedbackMode.TonesOnly
        };
        tonesOnlyRadio.CheckedChanged += FeedbackMode_CheckedChanged;

        announcementsOnlyRadio = new RadioButton
        {
            Text = "Screen Reader Announcements Only",
            Location = new Point(15, 55),
            Size = new Size(420, 25),
            AccessibleName = "Announcements Only",
            AccessibleDescription = "Use screen reader announcements without audio tones",
            Checked = SelectedFeedbackMode == HandFlyFeedbackMode.AnnouncementsOnly
        };
        announcementsOnlyRadio.CheckedChanged += FeedbackMode_CheckedChanged;

        bothRadio = new RadioButton
        {
            Text = "Both Tones and Announcements",
            Location = new Point(15, 85),
            Size = new Size(420, 25),
            AccessibleName = "Both",
            AccessibleDescription = "Use both audio tones and screen reader announcements",
            Checked = SelectedFeedbackMode == HandFlyFeedbackMode.Both
        };
        bothRadio.CheckedChanged += FeedbackMode_CheckedChanged;

        feedbackModeGroup.Controls.AddRange(new Control[] { tonesOnlyRadio, announcementsOnlyRadio, bothRadio });

        // Wave Type Label
        waveTypeLabel = new Label
        {
            Text = "Wave Type (affects tone character):",
            Location = new Point(20, 185),
            Size = new Size(250, 20),
            AccessibleName = "Wave Type Label"
        };

        // Wave Type ComboBox
        waveTypeCombo = new ComboBox
        {
            Location = new Point(280, 183),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Wave Type",
            AccessibleDescription = "Select the audio wave type for tone generation"
        };
        waveTypeCombo.Items.AddRange(new object[]
        {
            "Sine (Smoothest)",
            "Triangle (Smooth)",
            "Sawtooth (Bright)",
            "Square (Harsh)"
        });
        waveTypeCombo.SelectedIndex = (int)SelectedWaveType;
        waveTypeCombo.SelectedIndexChanged += WaveTypeCombo_SelectedIndexChanged;

        // Volume Label
        volumeLabel = new Label
        {
            Text = "Volume:",
            Location = new Point(20, 225),
            Size = new Size(100, 20),
            AccessibleName = "Volume Label"
        };

        // Volume TrackBar
        volumeTrackBar = new TrackBar
        {
            Location = new Point(120, 220),
            Size = new Size(300, 45),
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Value = (int)(SelectedVolume * 100),
            AccessibleName = "Volume Level",
            AccessibleDescription = "Adjust the audio tone volume from 0 to 100 percent"
        };
        volumeTrackBar.ValueChanged += VolumeTrackBar_ValueChanged;

        // Volume Value Label
        volumeValueLabel = new Label
        {
            Text = $"{volumeTrackBar.Value}%",
            Location = new Point(430, 225),
            Size = new Size(40, 20),
            AccessibleName = "Volume Value",
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Test Tone Button
        testToneButton = new Button
        {
            Text = "Test Tone",
            Location = new Point(20, 280),
            Size = new Size(120, 35),
            AccessibleName = "Test Tone",
            AccessibleDescription = "Play a sample tone with current settings"
        };
        testToneButton.Click += TestToneButton_Click;

        // OK Button
        okButton = new Button
        {
            Text = "OK",
            Location = new Point(310, 370),
            Size = new Size(75, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Apply Settings",
            AccessibleDescription = "Apply the hand fly mode settings"
        };
        okButton.Click += OkButton_Click;

        // Cancel Button
        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(395, 370),
            Size = new Size(75, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel without changing settings"
        };

        // Add controls to form
        Controls.AddRange(new Control[]
        {
            titleLabel, feedbackModeGroup, waveTypeLabel, waveTypeCombo,
            volumeLabel, volumeTrackBar, volumeValueLabel,
            testToneButton, okButton, cancelButton
        });

        AcceptButton = okButton;
        CancelButton = cancelButton;

        // Update control states based on feedback mode
        UpdateControlStates();
    }

    private void SetupAccessibility()
    {
        // Set tab order for logical navigation
        titleLabel.TabIndex = 0;
        feedbackModeGroup.TabIndex = 1;
        tonesOnlyRadio.TabIndex = 2;
        announcementsOnlyRadio.TabIndex = 3;
        bothRadio.TabIndex = 4;
        waveTypeLabel.TabIndex = 5;
        waveTypeCombo.TabIndex = 6;
        volumeLabel.TabIndex = 7;
        volumeTrackBar.TabIndex = 8;
        testToneButton.TabIndex = 9;
        okButton.TabIndex = 10;
        cancelButton.TabIndex = 11;

        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            tonesOnlyRadio.Focus();
        };

        // Stop test tone when form closes
        FormClosing += (sender, e) =>
        {
            StopTestTone();
        };
    }

    private void UpdateControlStates()
    {
        // Enable/disable audio controls based on feedback mode
        bool audioEnabled = SelectedFeedbackMode == HandFlyFeedbackMode.TonesOnly ||
                           SelectedFeedbackMode == HandFlyFeedbackMode.Both;

        waveTypeLabel.Enabled = audioEnabled;
        waveTypeCombo.Enabled = audioEnabled;
        volumeLabel.Enabled = audioEnabled;
        volumeTrackBar.Enabled = audioEnabled;
        volumeValueLabel.Enabled = audioEnabled;
        testToneButton.Enabled = audioEnabled;
    }

    private void FeedbackMode_CheckedChanged(object? sender, EventArgs e)
    {
        if (tonesOnlyRadio.Checked)
            SelectedFeedbackMode = HandFlyFeedbackMode.TonesOnly;
        else if (announcementsOnlyRadio.Checked)
            SelectedFeedbackMode = HandFlyFeedbackMode.AnnouncementsOnly;
        else if (bothRadio.Checked)
            SelectedFeedbackMode = HandFlyFeedbackMode.Both;

        UpdateControlStates();
    }

    private void WaveTypeCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        SelectedWaveType = (HandFlyWaveType)waveTypeCombo.SelectedIndex;
    }

    private void VolumeTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        SelectedVolume = volumeTrackBar.Value / 100.0;
        volumeValueLabel.Text = $"{volumeTrackBar.Value}%";
    }

    private void TestToneButton_Click(object? sender, EventArgs e)
    {
        if (testToneGenerator?.IsPlaying == true)
        {
            StopTestTone();
            testToneButton.Text = "Test Tone";
        }
        else
        {
            PlayTestTone();
            testToneButton.Text = "Stop Test";
        }
    }

    private void PlayTestTone()
    {
        try
        {
            testToneGenerator = new AudioToneGenerator();
            testToneGenerator.Start(SelectedWaveType, SelectedVolume);

            // Simulate varying pitch and bank for demonstration
            Task.Run(async () =>
            {
                for (int i = 0; i < 60 && testToneGenerator?.IsPlaying == true; i++)
                {
                    // Sweep pitch from -5 to +5 degrees
                    double pitch = Math.Sin(i * 0.1) * 5.0;
                    testToneGenerator?.UpdatePitch(pitch);

                    // Sweep bank from -30 to +30 degrees
                    double bank = Math.Cos(i * 0.15) * 30.0;
                    testToneGenerator?.UpdateBank(bank);

                    await Task.Delay(100);
                }

                // Auto-stop after 6 seconds
                if (testToneGenerator?.IsPlaying == true)
                {
                    Invoke(() =>
                    {
                        StopTestTone();
                        testToneButton.Text = "Test Tone";
                    });
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to play test tone: {ex.Message}", "Audio Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopTestTone()
    {
        testToneGenerator?.Stop();
        testToneGenerator?.Dispose();
        testToneGenerator = null;
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        StopTestTone();
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Handle Escape key
        if (keyData == Keys.Escape)
        {
            StopTestTone();
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }
}
