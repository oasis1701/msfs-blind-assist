using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

public class TaxiGuidanceOptionsForm : Form
{
    private Label titleLabel = null!;

    private Label toneTypeLabel = null!;
    private ComboBox toneTypeCombo = null!;

    private Label volumeLabel = null!;
    private TrackBar volumeTrackBar = null!;
    private Label volumeValueLabel = null!;

    private Button testToneButton = null!;
    private CheckBox invertToneCheckBox = null!;
    private CheckBox hardPanToneCheckBox = null!;
    private CheckBox announceCrossingsCheckBox = null!;
    private Label gsAnnounceLabel = null!;
    private ComboBox gsAnnounceCombo = null!;
    private CheckBox useMetresCheckBox = null!;
    private CheckBox parkingRadiusMetresCheckBox = null!;

    private Button okButton = null!;
    private Button cancelButton = null!;

    private AudioToneGenerator? testToneGenerator;

    public HandFlyWaveType SelectedToneWaveform { get; private set; }
    public double SelectedVolume { get; private set; }
    public bool InvertSteeringTone { get; private set; }
    public bool HardPanSteeringTone { get; private set; }
    public bool AnnounceCrossings { get; private set; }
    public int GroundSpeedAnnounceInterval { get; private set; }
    public bool GroundTrafficUseMetres { get; private set; }
    public bool ParkingRadiusUseMetres { get; private set; }

    public TaxiGuidanceOptionsForm(
        HandFlyWaveType currentWaveform,
        double currentVolume,
        bool invertSteeringTone,
        bool hardPanSteeringTone,
        bool announceCrossings,
        int groundSpeedAnnounceInterval,
        bool groundTrafficUseMetres,
        bool parkingRadiusUseMetres)
    {
        SelectedToneWaveform = currentWaveform;
        SelectedVolume = currentVolume;
        InvertSteeringTone = invertSteeringTone;
        HardPanSteeringTone = hardPanSteeringTone;
        AnnounceCrossings = announceCrossings;
        GroundSpeedAnnounceInterval = groundSpeedAnnounceInterval;
        GroundTrafficUseMetres = groundTrafficUseMetres;
        ParkingRadiusUseMetres = parkingRadiusUseMetres;
        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        Text = "Taxi Guidance Options";
        Size = new Size(500, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Title Label
        titleLabel = new Label
        {
            Text = "Configure taxi guidance steering tone settings:",
            Location = new Point(20, 20),
            Size = new Size(450, 20),
            AccessibleName = "Taxi Guidance Options Title"
        };

        // Tone Type Label
        toneTypeLabel = new Label
        {
            Text = "Steering tone type:",
            Location = new Point(20, 55),
            Size = new Size(250, 20),
            AccessibleName = "Steering tone type Label"
        };

        // Tone Type ComboBox
        toneTypeCombo = new ComboBox
        {
            Location = new Point(280, 53),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Steering tone type",
            AccessibleDescription = "Select the audio wave type for the taxi steering tone"
        };
        toneTypeCombo.Items.AddRange(new object[]
        {
            "Sine (Smoothest)",
            "Triangle (Smooth)",
            "Sawtooth (Bright)",
            "Sine (Rich)"
        });
        toneTypeCombo.SelectedIndex = (int)SelectedToneWaveform;
        toneTypeCombo.SelectedIndexChanged += (s, e) =>
            SelectedToneWaveform = (HandFlyWaveType)toneTypeCombo.SelectedIndex;

        // Volume Label
        volumeLabel = new Label
        {
            Text = "Volume:",
            Location = new Point(20, 95),
            Size = new Size(100, 20),
            AccessibleName = "Volume Label"
        };

        // Volume TrackBar
        volumeTrackBar = new TrackBar
        {
            Location = new Point(120, 90),
            Size = new Size(300, 45),
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Value = (int)(SelectedVolume * 100),
            AccessibleName = "Volume Level",
            AccessibleDescription = "Adjust the taxi steering tone volume from 0 to 100 percent"
        };
        volumeTrackBar.ValueChanged += (s, e) =>
        {
            SelectedVolume = volumeTrackBar.Value / 100.0;
            volumeValueLabel.Text = $"{volumeTrackBar.Value}%";
        };

        // Volume Value Label
        volumeValueLabel = new Label
        {
            Text = $"{volumeTrackBar.Value}%",
            Location = new Point(430, 95),
            Size = new Size(40, 20),
            AccessibleName = "Volume Value",
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Test Tone Button
        testToneButton = new Button
        {
            Text = "Test Tone",
            Location = new Point(20, 145),
            Size = new Size(120, 35),
            AccessibleName = "Test Tone",
            AccessibleDescription = "Play a sample steering tone with current settings to preview the sound"
        };
        testToneButton.Click += TestToneButton_Click;

        // Invert steering tone direction. Default off (current behaviour:
        // pan in the direction the pilot must turn). When ticked: tone in
        // the right ear means steer LEFT to centre it. Mirrors the
        // Takeoff Assist option for users who prefer the inverted mapping.
        invertToneCheckBox = new CheckBox
        {
            Text = "Invert steering tone direction (steer AWAY from the tone)",
            Location = new Point(20, 185),
            Size = new Size(450, 25),
            Checked = InvertSteeringTone,
            AccessibleName = "Invert steering tone direction",
            AccessibleDescription = "When enabled, the stereo pan is reversed. A tone playing in the right ear means steer left to centre it, and vice versa. Default off."
        };
        invertToneCheckBox.CheckedChanged += (s, e) =>
            InvertSteeringTone = invertToneCheckBox.Checked;

        // Hard-pan checkbox. Forces the pan to full ±1 instead of the
        // proportional sqrt curve. Intended for users on stereo speakers
        // where partial pan is hard to distinguish from "centred". The
        // tone always exits via exactly one speaker — which side, plus
        // the silent-band behaviour, communicates direction unambiguously
        // (no magnitude information conveyed).
        hardPanToneCheckBox = new CheckBox
        {
            Text = "Hard-pan steering tone (full left or full right; speaker-friendly)",
            Location = new Point(20, 215),
            Size = new Size(450, 25),
            Checked = HardPanSteeringTone,
            AccessibleName = "Hard-pan steering tone",
            AccessibleDescription = "When enabled, the steering tone plays at full pan to one side or the other instead of the proportional sqrt curve. Useful for stereo-speaker users who can't easily distinguish partial pan from centred. Default off."
        };
        hardPanToneCheckBox.CheckedChanged += (s, e) =>
            HardPanSteeringTone = hardPanToneCheckBox.Checked;

        // Announce-crossings checkbox
        announceCrossingsCheckBox = new CheckBox
        {
            Text = "Announce crossing taxiways (e.g., \"Crossing taxiway Link 53\")",
            Location = new Point(20, 250),
            Size = new Size(450, 25),
            Checked = AnnounceCrossings,
            AccessibleName = "Announce crossing taxiways",
            AccessibleDescription = "When enabled, taxi guidance announces named taxiways being crossed at intersections. Disable for quieter taxiing."
        };
        announceCrossingsCheckBox.CheckedChanged += (s, e) =>
            AnnounceCrossings = announceCrossingsCheckBox.Checked;

        // Ground-speed announcement interval. Useful for monitoring taxi-speed
        // SOPs (10 kt turns / 30 kt straight) without needing to press the GS
        // hotkey. Also fires during takeoff roll, complementing the takeoff-
        // assist 80/100/V1/rotate callouts. Off by default to keep behaviour
        // for existing users; user picks 5 / 10 kt to enable.
        gsAnnounceLabel = new Label
        {
            Text = "Announce ground speed every:",
            Location = new Point(20, 285),
            Size = new Size(220, 20),
            AccessibleName = "Ground speed announcement interval Label"
        };
        gsAnnounceCombo = new ComboBox
        {
            Location = new Point(250, 283),
            Size = new Size(220, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Ground speed announcement interval",
            AccessibleDescription = "Pick a knot interval to hear the current ground speed at each multiple while taxiing. Off disables the feature."
        };
        gsAnnounceCombo.Items.AddRange(new object[]
        {
            "Off",
            "Every 5 knots",
            "Every 10 knots"
        });
        // Map the stored interval back to the combo index. Anything other than 5 or 10 → Off.
        gsAnnounceCombo.SelectedIndex = GroundSpeedAnnounceInterval switch
        {
            5 => 1,
            10 => 2,
            _ => 0,
        };
        gsAnnounceCombo.SelectedIndexChanged += (s, e) =>
        {
            GroundSpeedAnnounceInterval = gsAnnounceCombo.SelectedIndex switch
            {
                1 => 5,
                2 => 10,
                _ => 0,
            };
        };

        // Ground traffic distance unit: metres or feet
        useMetresCheckBox = new CheckBox
        {
            Text = "Show ground traffic distances in metres (default: feet)",
            Location = new Point(20, 320),
            Size = new Size(450, 25),
            Checked = GroundTrafficUseMetres,
            AccessibleName = "Show ground traffic distances in metres",
            AccessibleDescription = "When enabled, proximity alert distances (e.g. 'Traffic ahead, 100 metres') are in metres. Default is feet, which matches aviation conventions."
        };
        useMetresCheckBox.CheckedChanged += (s, e) =>
            GroundTrafficUseMetres = useMetresCheckBox.Checked;

        // Parking radius unit interpretation for "show only fitting" gate filters.
        parkingRadiusMetresCheckBox = new CheckBox
        {
            Text = "Treat parking radius as metres (untick for feet)",
            Location = new Point(20, 355),
            Size = new Size(450, 25),
            Checked = ParkingRadiusUseMetres,
            AccessibleName = "Treat parking radius as metres",
            AccessibleDescription = "When enabled, parking radius values from the airport database are interpreted as metres for the show only fitting stands filter. Untick to interpret them as feet."
        };
        parkingRadiusMetresCheckBox.CheckedChanged += (s, e) =>
            ParkingRadiusUseMetres = parkingRadiusMetresCheckBox.Checked;

        // OK Button
        okButton = new Button
        {
            Text = "OK",
            Location = new Point(310, 430),
            Size = new Size(75, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Apply Settings",
            AccessibleDescription = "Apply the taxi guidance settings"
        };
        okButton.Click += (s, e) =>
        {
            StopTestTone();
            DialogResult = DialogResult.OK;
            Close();
        };

        // Cancel Button
        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(395, 430),
            Size = new Size(75, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel without changing settings"
        };

        Controls.AddRange(new Control[]
        {
            titleLabel, toneTypeLabel, toneTypeCombo,
            volumeLabel, volumeTrackBar, volumeValueLabel,
            testToneButton,
            invertToneCheckBox,
            hardPanToneCheckBox,
            announceCrossingsCheckBox,
            gsAnnounceLabel, gsAnnounceCombo,
            useMetresCheckBox,
            parkingRadiusMetresCheckBox,
            okButton, cancelButton
        });

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void SetupAccessibility()
    {
        int tabIdx = 0;
        toneTypeCombo.TabIndex = tabIdx++;
        volumeTrackBar.TabIndex = tabIdx++;
        testToneButton.TabIndex = tabIdx++;
        invertToneCheckBox.TabIndex = tabIdx++;
        hardPanToneCheckBox.TabIndex = tabIdx++;
        announceCrossingsCheckBox.TabIndex = tabIdx++;
        gsAnnounceCombo.TabIndex = tabIdx++;
        useMetresCheckBox.TabIndex = tabIdx++;
        parkingRadiusMetresCheckBox.TabIndex = tabIdx++;
        okButton.TabIndex = tabIdx++;
        cancelButton.TabIndex = tabIdx++;

        Load += (s, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false;
            toneTypeCombo.Focus();
        };

        FormClosing += (s, e) => StopTestTone();
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
            testToneGenerator.Start(SelectedToneWaveform, SelectedVolume, 440);

            // Simulate panning left and right to demonstrate steering tone
            Task.Run(async () =>
            {
                for (int i = 0; i < 40 && testToneGenerator?.IsPlaying == true; i++)
                {
                    float pan = (float)Math.Sin(i * 0.15) * 0.8f;
                    testToneGenerator?.SetPan(pan);
                    await Task.Delay(100);
                }

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

    protected override bool ProcessDialogKey(Keys keyData)
    {
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
