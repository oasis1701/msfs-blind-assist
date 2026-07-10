using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>Hand Fly section of the unified Settings dialog. Extracted from the retired
/// standalone Hand Fly Options dialog — same controls, same AccessibleNames/TabIndex, but the
/// old OK/Cancel buttons are gone (the dialog owns OK/Cancel) and the tone lifecycle is tied to
/// <see cref="OnLeaving"/>/<see cref="Dispose(bool)"/> instead of FormClosing/OK-click.
/// The panel is taller than the tab viewport, so it scrolls (<c>AutoScroll = true</c>).</summary>
public class HandFlyPanel : UserControl, ISettingsPanel
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

    private CheckBox monitorHeadingCheckBox = null!;
    private CheckBox monitorVSCheckBox = null!;

    private Label guidanceToneLabel = null!;
    private ComboBox guidanceToneCombo = null!;

    private Label guidanceVolumeLabel = null!;
    private TrackBar guidanceVolumeTrackBar = null!;
    private Label guidanceVolumeValueLabel = null!;

    // Visual Guidance — second "current attitude" follower tone (always on; pilot matches it by ear).
    private Label currentToneLabel = null!;
    private ComboBox currentToneCombo = null!;
    private Label currentToneVolumeLabel = null!;
    private TrackBar currentToneVolumeTrackBar = null!;
    private Label currentToneVolumeValueLabel = null!;
    private CheckBox visualGuidanceHardPanCheckBox = null!;

    private Label takeoffToneLabel = null!;
    private ComboBox takeoffToneCombo = null!;

    private Label takeoffVolumeLabel = null!;
    private TrackBar takeoffVolumeTrackBar = null!;
    private Label takeoffVolumeValueLabel = null!;

    private CheckBox muteCenterlineCheckBox = null!;
    private CheckBox steerTowardToneCheckBox = null!;
    private CheckBox hardPanCheckBox = null!;
    private Label headingToneThresholdLabel = null!;
    private ComboBox headingToneThresholdCombo = null!;
    private CheckBox legacyTakeoffCheckBox = null!;
    private CheckBox enableCalloutsCheckBox = null!;
    private CheckBox autoActivateOnLineupCheckBox = null!;
    private CheckBox handFlyAutoActivateOnTakeoffCheckBox = null!;

    private AudioToneGenerator? testToneGenerator;

    public string TabTitle => "Hand Fly";

    public HandFlyPanel()
    {
        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        AutoScroll = true;

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
            Text = "Hand flying feedback type",
            Location = new Point(20, 50),
            Size = new Size(450, 120),
            AccessibleName = "Hand flying feedback type"
        };

        tonesOnlyRadio = new RadioButton
        {
            Text = "Audio Tones Only",
            Location = new Point(15, 25),
            Size = new Size(420, 25),
            AccessibleName = "Tones Only",
            AccessibleDescription = "Play audio tones without screen reader announcements"
        };
        tonesOnlyRadio.CheckedChanged += FeedbackMode_CheckedChanged;

        announcementsOnlyRadio = new RadioButton
        {
            Text = "Screen Reader Announcements Only",
            Location = new Point(15, 55),
            Size = new Size(420, 25),
            AccessibleName = "Announcements Only",
            AccessibleDescription = "Use screen reader announcements without audio tones"
        };
        announcementsOnlyRadio.CheckedChanged += FeedbackMode_CheckedChanged;

        bothRadio = new RadioButton
        {
            Text = "Both Tones and Announcements",
            Location = new Point(15, 85),
            Size = new Size(420, 25),
            AccessibleName = "Both",
            AccessibleDescription = "Use both audio tones and screen reader announcements"
        };
        bothRadio.CheckedChanged += FeedbackMode_CheckedChanged;

        feedbackModeGroup.Controls.AddRange(new Control[] { tonesOnlyRadio, announcementsOnlyRadio, bothRadio });

        // Wave Type Label
        waveTypeLabel = new Label
        {
            Text = "Hand fly tone type:",
            Location = new Point(20, 185),
            Size = new Size(250, 20),
            AccessibleName = "Hand fly tone type Label"
        };

        // Wave Type ComboBox
        waveTypeCombo = new ComboBox
        {
            Location = new Point(280, 183),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Hand fly tone type",
            AccessibleDescription = "Select the audio wave type for hand fly tone generation"
        };
        waveTypeCombo.Items.AddRange(new object[]
        {
            "Sine (Smoothest)",
            "Triangle (Smooth)",
            "Sawtooth (Bright)",
            "Sine (Rich)"
        });

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

        // Monitor Heading Checkbox
        monitorHeadingCheckBox = new CheckBox
        {
            Text = "Monitor Heading (announce heading changes at 1-second intervals)",
            Location = new Point(20, 330),
            Size = new Size(450, 25),
            AccessibleName = "Monitor Heading",
            AccessibleDescription = "Enable heading announcements during hand fly mode"
        };

        // Monitor Vertical Speed Checkbox
        monitorVSCheckBox = new CheckBox
        {
            Text = "Monitor Vertical Speed (announce VS changes at 1-second intervals)",
            Location = new Point(20, 365),
            Size = new Size(450, 25),
            AccessibleName = "Monitor Vertical Speed",
            AccessibleDescription = "Enable vertical speed announcements during hand fly mode"
        };

        // Visual Guidance - Tone Waveform Label
        guidanceToneLabel = new Label
        {
            Text = "Visual Guidance Tone:",
            Location = new Point(20, 405),
            Size = new Size(250, 20),
            AccessibleName = "Guidance Tone Label"
        };

        // Visual Guidance - Tone Waveform ComboBox
        guidanceToneCombo = new ComboBox
        {
            Location = new Point(280, 403),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Visual Guidance Tone",
            AccessibleDescription = "Select the audio wave type for visual guidance tone"
        };
        guidanceToneCombo.Items.AddRange(new object[]
        {
            "Sine (Smoothest)",
            "Triangle (Smooth)",
            "Sawtooth (Bright)",
            "Sine (Rich)"
        });

        // Visual Guidance Volume Label
        guidanceVolumeLabel = new Label
        {
            Text = "Visual Guidance Volume:",
            Location = new Point(20, 440),
            Size = new Size(100, 20),
            AccessibleName = "Visual Guidance Volume Label"
        };

        // Visual Guidance Volume TrackBar
        guidanceVolumeTrackBar = new TrackBar
        {
            Location = new Point(120, 435),
            Size = new Size(300, 45),
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            AccessibleName = "Visual Guidance Volume Level",
            AccessibleDescription = "Adjust the visual guidance tone volume from 0 to 100 percent"
        };
        guidanceVolumeTrackBar.ValueChanged += GuidanceVolumeTrackBar_ValueChanged;

        // Visual Guidance Volume Value Label
        guidanceVolumeValueLabel = new Label
        {
            Text = $"{guidanceVolumeTrackBar.Value}%",
            Location = new Point(430, 440),
            Size = new Size(40, 20),
            AccessibleName = "Visual Guidance Volume Value",
            TextAlign = ContentAlignment.MiddleLeft
        };

        // ── Visual Guidance — Current-Attitude (follower) tone ──
        // A second tone always plays alongside the desired tone with the SAME 200–800 Hz / ±1.0 pan
        // mapping, tracking the aircraft's actual pitch/bank. The pilot zero-beats the two
        // frequencies (vertical) and matches the two pans (lateral) by ear.

        currentToneLabel = new Label
        {
            Text = "Current-attitude tone:",
            Location = new Point(20, 485),
            Size = new Size(250, 20),
            AccessibleName = "Current attitude tone Label"
        };

        currentToneCombo = new ComboBox
        {
            Location = new Point(280, 483),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Current attitude tone",
            AccessibleDescription = "Wave type for the second visual-guidance tone. Pick a different waveform from the main guidance tone so the two stay distinguishable when their pitches match."
        };
        currentToneCombo.Items.AddRange(new object[]
        {
            "Sine (Smoothest)",
            "Triangle (Smooth)",
            "Sawtooth (Bright)",
            "Sine (Rich)"
        });

        currentToneVolumeLabel = new Label
        {
            Text = "Current-attitude Volume:",
            Location = new Point(20, 520),
            Size = new Size(100, 20),
            AccessibleName = "Current attitude tone volume Label"
        };

        currentToneVolumeTrackBar = new TrackBar
        {
            Location = new Point(120, 515),
            Size = new Size(300, 45),
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            AccessibleName = "Current attitude tone volume level",
            AccessibleDescription = "Adjust the current-attitude (follower) tone volume from 0 to 100 percent"
        };
        currentToneVolumeTrackBar.ValueChanged += CurrentToneVolumeTrackBar_ValueChanged;

        currentToneVolumeValueLabel = new Label
        {
            Text = $"{currentToneVolumeTrackBar.Value}%",
            Location = new Point(430, 520),
            Size = new Size(40, 20),
            AccessibleName = "Current attitude tone volume value",
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Hard-pan checkbox for the dual-tone system. ON snaps both tones' pan to full left /
        // full right once bank exceeds ~1°, instead of proportional pan. Useful on stereo
        // speakers where partial pan blends with centred. Headphones generally don't need this.
        visualGuidanceHardPanCheckBox = new CheckBox
        {
            Text = "Hard-pan visual-guidance tones (speaker-friendly)",
            Location = new Point(20, 565),
            Size = new Size(450, 25),
            AccessibleName = "Hard-pan visual guidance tones",
            AccessibleDescription = "When enabled, both visual-guidance tones snap to full left or full right once bank exceeds about one degree, instead of a proportional pan. Useful on stereo speakers where partial pan is hard to distinguish from centred. Headphone users normally leave this off. Default off."
        };

        // Takeoff Assist - Tone Waveform Label
        takeoffToneLabel = new Label
        {
            Text = "Takeoff Assist Tone:",
            Location = new Point(20, 595),
            Size = new Size(250, 20),
            AccessibleName = "Takeoff Assist Tone Label"
        };

        // Takeoff Assist - Tone Waveform ComboBox
        takeoffToneCombo = new ComboBox
        {
            Location = new Point(280, 593),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Takeoff Assist Tone",
            AccessibleDescription = "Select the audio wave type for takeoff assist Heading alignment tone"
        };
        takeoffToneCombo.Items.AddRange(new object[]
        {
            "Sine (Smoothest)",
            "Triangle (Smooth)",
            "Sawtooth (Bright)",
            "Sine (Rich)"
        });

        // Takeoff Assist Volume Label
        takeoffVolumeLabel = new Label
        {
            Text = "Takeoff Assist Volume:",
            Location = new Point(20, 630),
            Size = new Size(100, 20),
            AccessibleName = "Takeoff Assist Volume Label"
        };

        // Takeoff Assist Volume TrackBar
        takeoffVolumeTrackBar = new TrackBar
        {
            Location = new Point(120, 625),
            Size = new Size(300, 45),
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            AccessibleName = "Takeoff Assist Volume Level",
            AccessibleDescription = "Adjust the takeoff assist centerline tone volume from 0 to 100 percent"
        };
        takeoffVolumeTrackBar.ValueChanged += TakeoffVolumeTrackBar_ValueChanged;

        // Takeoff Assist Volume Value Label
        takeoffVolumeValueLabel = new Label
        {
            Text = $"{takeoffVolumeTrackBar.Value}%",
            Location = new Point(430, 630),
            Size = new Size(40, 20),
            AccessibleName = "Takeoff Assist Volume Value",
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Mute Centerline Deviation Announcements Checkbox
        muteCenterlineCheckBox = new CheckBox
        {
            Text = "Mute centerline deviation announcements",
            Location = new Point(20, 665),
            Size = new Size(450, 25),
            AccessibleName = "Mute centerline deviation announcements",
            AccessibleDescription = "When enabled, mutes centerline deviation announcements in modern takeoff assist mode. Audio tone and pitch announcements continue."
        };

        // Steer-toward-the-tone checkbox. CHECKED = the tone plays on the side you
        // should steer toward (steer INTO it to centre) — binds 1:1 to
        // UserSettings.TakeoffAssistSteerTowardTone, no negation anywhere.
        steerTowardToneCheckBox = new CheckBox
        {
            Text = "Steer toward the tone to stay on the centerline",
            Location = new Point(20, 700),
            Size = new Size(550, 25),
            AccessibleName = "Steer toward the tone to stay on the centerline",
            AccessibleDescription = "Checked (default for new installs): the tone plays on the side you should steer toward, so you steer into the tone to return to the centerline. With a tone threshold of 1 degree or higher it goes silent when you are tracking straight; at Always it plays continuously, centred when on track. Uncheck to reverse the panning, so you steer away from the tone instead."
        };

        // Hard-pan tone checkbox. Forces the centerline tone to full ±1
        // instead of the proportional headingDiff/5° curve. For users on
        // stereo speakers where partial pan blends with the centred case
        // and the side becomes hard to tell. The tone always exits one
        // speaker only — direction is unambiguous, no magnitude conveyed.
        hardPanCheckBox = new CheckBox
        {
            Text = "Hard-pan centerline tone (full left or full right; speaker-friendly)",
            Location = new Point(20, 730),
            Size = new Size(450, 25),
            AccessibleName = "Hard-pan centerline tone",
            AccessibleDescription = "When enabled, the takeoff-assist centerline tone plays at full pan to one side or the other instead of a proportional curve. Useful for stereo-speaker users who can't easily distinguish partial pan from centred. Default off."
        };

        // Heading Tone Threshold Label
        headingToneThresholdLabel = new Label
        {
            Text = "Play steering tone:",
            Location = new Point(20, 765),
            Size = new Size(250, 20),
            AccessibleName = "Steering Tone Threshold Label"
        };

        // Heading Tone Threshold ComboBox
        headingToneThresholdCombo = new ComboBox
        {
            Location = new Point(280, 763),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Play steering tone",
            AccessibleDescription = "Select when the steering tone plays. Always plays continuously, or only when the required steering correction (heading error plus centerline correction) exceeds the selected threshold."
        };
        headingToneThresholdCombo.Items.AddRange(new object[]
        {
            "Always",
            "At 1 degree error",
            "At 2 degrees error",
            "At 3 degrees error",
            "At 4 degrees error",
            "At 5 degrees error"
        });

        // Legacy Takeoff Assist Mode Checkbox
        legacyTakeoffCheckBox = new CheckBox
        {
            Text = "Legacy takeoff assist mode (heading-based, no tone)",
            Location = new Point(20, 800),
            Size = new Size(450, 25),
            AccessibleName = "Legacy takeoff assist mode",
            AccessibleDescription = "When enabled, takeoff assist announces heading deviation in degrees without audio tone. When disabled, uses centerline tracking with audio tone."
        };

        // Enable Takeoff Callouts Checkbox
        enableCalloutsCheckBox = new CheckBox
        {
            Text = "Enable takeoff assistant call outs",
            Location = new Point(20, 830),
            Size = new Size(450, 25),
            AccessibleName = "Enable takeoff assistant call outs",
            AccessibleDescription = "When enabled, announces speed callouts during takeoff roll: 80 knots, 100 knots, V1, and rotate."
        };

        // Auto-Activate on Lineup Checkbox
        autoActivateOnLineupCheckBox = new CheckBox
        {
            Text = "Auto-activate Takeoff Assist on lineup",
            Location = new Point(20, 860),
            Size = new Size(450, 25),
            AccessibleName = "Auto-activate Takeoff Assist on lineup",
            AccessibleDescription = "When enabled, Takeoff Assist activates automatically when taxi guidance reaches a stable runway lineup, so you don't have to press control T. One-shot per route: if you disable Takeoff Assist after it auto-activates, it won't re-engage until the next taxi route."
        };

        // Auto-Activate Hand Fly on Takeoff Checkbox — completes the
        // taxi → Takeoff Assist → Hand Fly hands-free chain.
        handFlyAutoActivateOnTakeoffCheckBox = new CheckBox
        {
            Text = "Auto-activate Hand Fly on takeoff (deactivates Takeoff Assist)",
            Location = new Point(20, 890),
            Size = new Size(450, 25),
            AccessibleName = "Auto-activate Hand Fly on takeoff",
            AccessibleDescription = "When enabled, shortly after the aircraft lifts off, if Takeoff Assist is active it is turned off and Hand Fly mode turns on automatically, so you don't have to switch manually at rotation. If you already activated Hand Fly yourself, only Takeoff Assist is turned off. Liftoffs without Takeoff Assist are unaffected."
        };

        // Add controls to panel
        Controls.AddRange(new Control[]
        {
            titleLabel, feedbackModeGroup, waveTypeLabel, waveTypeCombo,
            volumeLabel, volumeTrackBar, volumeValueLabel,
            testToneButton, monitorHeadingCheckBox, monitorVSCheckBox,
            guidanceToneLabel, guidanceToneCombo,
            guidanceVolumeLabel, guidanceVolumeTrackBar, guidanceVolumeValueLabel,
            currentToneLabel, currentToneCombo,
            currentToneVolumeLabel, currentToneVolumeTrackBar, currentToneVolumeValueLabel,
            visualGuidanceHardPanCheckBox,
            takeoffToneLabel, takeoffToneCombo,
            takeoffVolumeLabel, takeoffVolumeTrackBar, takeoffVolumeValueLabel,
            muteCenterlineCheckBox, steerTowardToneCheckBox, hardPanCheckBox,
            headingToneThresholdLabel, headingToneThresholdCombo,
            legacyTakeoffCheckBox, enableCalloutsCheckBox, autoActivateOnLineupCheckBox,
            handFlyAutoActivateOnTakeoffCheckBox
        });

        // Update control states based on feedback mode (no radio is checked yet at
        // construction, so audio controls start disabled until LoadFrom sets the
        // real selection).
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
        monitorHeadingCheckBox.TabIndex = 10;
        monitorVSCheckBox.TabIndex = 11;
        guidanceToneLabel.TabIndex = 12;
        guidanceToneCombo.TabIndex = 13;
        guidanceVolumeLabel.TabIndex = 14;
        guidanceVolumeTrackBar.TabIndex = 15;
        currentToneLabel.TabIndex = 16;
        currentToneCombo.TabIndex = 17;
        currentToneVolumeLabel.TabIndex = 18;
        currentToneVolumeTrackBar.TabIndex = 19;
        visualGuidanceHardPanCheckBox.TabIndex = 20;
        takeoffToneLabel.TabIndex = 21;
        takeoffToneCombo.TabIndex = 22;
        takeoffVolumeLabel.TabIndex = 23;
        takeoffVolumeTrackBar.TabIndex = 24;
        muteCenterlineCheckBox.TabIndex = 25;
        steerTowardToneCheckBox.TabIndex = 26;
        hardPanCheckBox.TabIndex = 27;
        headingToneThresholdLabel.TabIndex = 28;
        headingToneThresholdCombo.TabIndex = 29;
        legacyTakeoffCheckBox.TabIndex = 30;
        enableCalloutsCheckBox.TabIndex = 31;
        autoActivateOnLineupCheckBox.TabIndex = 32;
        handFlyAutoActivateOnTakeoffCheckBox.TabIndex = 33;
    }

    private void UpdateControlStates()
    {
        // Enable/disable audio controls based on feedback mode
        bool audioEnabled = tonesOnlyRadio.Checked || bothRadio.Checked;

        waveTypeLabel.Enabled = audioEnabled;
        waveTypeCombo.Enabled = audioEnabled;
        volumeLabel.Enabled = audioEnabled;
        volumeTrackBar.Enabled = audioEnabled;
        volumeValueLabel.Enabled = audioEnabled;
        testToneButton.Enabled = audioEnabled;
    }

    private void FeedbackMode_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateControlStates();
    }

    private void VolumeTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        volumeValueLabel.Text = $"{volumeTrackBar.Value}%";
    }

    private void GuidanceVolumeTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        guidanceVolumeValueLabel.Text = $"{guidanceVolumeTrackBar.Value}%";
    }

    private void CurrentToneVolumeTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        currentToneVolumeValueLabel.Text = $"{currentToneVolumeTrackBar.Value}%";
    }

    private void TakeoffVolumeTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        takeoffVolumeValueLabel.Text = $"{takeoffVolumeTrackBar.Value}%";
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
            var waveType = (HandFlyWaveType)waveTypeCombo.SelectedIndex;
            double volume = volumeTrackBar.Value / 100.0;

            testToneGenerator = new AudioToneGenerator();
            testToneGenerator.Start(waveType, volume);

            // Simulate varying pitch and bank for demonstration with smooth transitions
            Task.Run(async () =>
            {
                // Rate limiting for smooth transitions (different rates for pitch vs panning)
                const double MAX_PITCH_RATE_DEG_PER_SEC = 3.0;  // Moderate rate to reach full range
                const double MAX_BANK_RATE_DEG_PER_SEC = 15.0;  // Fast but smooth panning
                const double UPDATE_INTERVAL_SEC = 0.1; // 100ms

                double maxPitchDelta = MAX_PITCH_RATE_DEG_PER_SEC * UPDATE_INTERVAL_SEC;
                double maxBankDelta = MAX_BANK_RATE_DEG_PER_SEC * UPDATE_INTERVAL_SEC;

                // Track current values for rate limiting
                double currentPitch = 0.0;
                double currentBank = 0.0;

                for (int i = 0; i < 60 && testToneGenerator?.IsPlaying == true; i++)
                {
                    // Calculate target pitch from -10 to +10 degrees (full 200-800 Hz range)
                    // Slower sine wave (0.025) allows rate limiting to reach full range
                    double targetPitch = Math.Sin(i * 0.025) * 10.0;

                    // Calculate target bank from -30 to +30 degrees for full stereo width
                    // (AudioToneGenerator clamps at ±20° which maps to full left/right panning)
                    double targetBank = Math.Cos(i * 0.15) * 30.0;

                    // Apply rate limiting to pitch (prevents crackling)
                    double pitchDelta = targetPitch - currentPitch;
                    if (Math.Abs(pitchDelta) > maxPitchDelta)
                    {
                        pitchDelta = Math.Sign(pitchDelta) * maxPitchDelta;
                    }
                    currentPitch += pitchDelta;

                    // Apply gentle rate limiting to bank (prevents jumpy panning)
                    double bankDelta = targetBank - currentBank;
                    if (Math.Abs(bankDelta) > maxBankDelta)
                    {
                        bankDelta = Math.Sign(bankDelta) * maxBankDelta;
                    }
                    currentBank += bankDelta;

                    // Update tone with smoothed values
                    testToneGenerator?.UpdatePitch(currentPitch);
                    testToneGenerator?.UpdateBank(currentBank);

                    await Task.Delay(100);
                }

                // Auto-stop after 6 seconds
                if (testToneGenerator?.IsPlaying == true)
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        try
                        {
                            Invoke(() =>
                            {
                                StopTestTone();
                                testToneButton.Text = "Test Tone";
                            });
                        }
                        catch (InvalidOperationException)
                        {
                            // Handle destroyed mid-flight (tab switched/dialog closed) — StopTestTone
                            // is also called from OnLeaving/Dispose, so the tone still stops.
                        }
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

    /// <summary>Stops and disposes the test-tone generator. Idempotent and non-throwing —
    /// safe to call whether or not a tone is currently playing.</summary>
    private void StopTestTone()
    {
        try
        {
            testToneGenerator?.Stop();
            testToneGenerator?.Dispose();
        }
        catch
        {
            // Non-throwing by contract (OnLeaving/Dispose callers must never fail).
        }
        finally
        {
            testToneGenerator = null;
        }
    }

    public void LoadFrom(UserSettings settings)
    {
        tonesOnlyRadio.Checked = settings.HandFlyFeedbackMode == HandFlyFeedbackMode.TonesOnly;
        announcementsOnlyRadio.Checked = settings.HandFlyFeedbackMode == HandFlyFeedbackMode.AnnouncementsOnly;
        bothRadio.Checked = settings.HandFlyFeedbackMode == HandFlyFeedbackMode.Both;

        waveTypeCombo.SelectedIndex = (int)settings.HandFlyWaveType;
        volumeTrackBar.Value = (int)(settings.HandFlyToneVolume * 100);
        volumeValueLabel.Text = $"{volumeTrackBar.Value}%";

        monitorHeadingCheckBox.Checked = settings.HandFlyMonitorHeading;
        monitorVSCheckBox.Checked = settings.HandFlyMonitorVerticalSpeed;

        guidanceToneCombo.SelectedIndex = (int)settings.VisualGuidanceToneWaveform;
        guidanceVolumeTrackBar.Value = (int)(settings.VisualGuidanceToneVolume * 100);
        guidanceVolumeValueLabel.Text = $"{guidanceVolumeTrackBar.Value}%";

        currentToneCombo.SelectedIndex = (int)settings.VisualGuidanceCurrentToneWaveform;
        currentToneVolumeTrackBar.Value = (int)(settings.VisualGuidanceCurrentToneVolume * 100);
        currentToneVolumeValueLabel.Text = $"{currentToneVolumeTrackBar.Value}%";
        visualGuidanceHardPanCheckBox.Checked = settings.VisualGuidanceHardPanTone;

        takeoffToneCombo.SelectedIndex = (int)settings.TakeoffAssistToneWaveform;
        takeoffVolumeTrackBar.Value = (int)(settings.TakeoffAssistToneVolume * 100);
        takeoffVolumeValueLabel.Text = $"{takeoffVolumeTrackBar.Value}%";

        muteCenterlineCheckBox.Checked = settings.TakeoffAssistMuteCenterlineAnnouncements;
        steerTowardToneCheckBox.Checked = settings.TakeoffAssistSteerTowardTone;
        hardPanCheckBox.Checked = settings.TakeoffAssistHardPanTone;
        headingToneThresholdCombo.SelectedIndex = settings.TakeoffAssistHeadingToneThreshold;
        legacyTakeoffCheckBox.Checked = settings.TakeoffAssistLegacyMode;
        enableCalloutsCheckBox.Checked = settings.TakeoffAssistEnableCallouts;
        autoActivateOnLineupCheckBox.Checked = settings.TakeoffAssistAutoActivateOnLineup;
        handFlyAutoActivateOnTakeoffCheckBox.Checked = settings.HandFlyAutoActivateOnTakeoff;

        UpdateControlStates();
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.HandFlyFeedbackMode = tonesOnlyRadio.Checked ? HandFlyFeedbackMode.TonesOnly
            : announcementsOnlyRadio.Checked ? HandFlyFeedbackMode.AnnouncementsOnly
            : HandFlyFeedbackMode.Both;
        settings.HandFlyWaveType = (HandFlyWaveType)waveTypeCombo.SelectedIndex;
        settings.HandFlyToneVolume = volumeTrackBar.Value / 100.0;
        settings.HandFlyMonitorHeading = monitorHeadingCheckBox.Checked;
        settings.HandFlyMonitorVerticalSpeed = monitorVSCheckBox.Checked;

        settings.VisualGuidanceToneWaveform = (HandFlyWaveType)guidanceToneCombo.SelectedIndex;
        settings.VisualGuidanceToneVolume = guidanceVolumeTrackBar.Value / 100.0;
        settings.VisualGuidanceCurrentToneWaveform = (HandFlyWaveType)currentToneCombo.SelectedIndex;
        settings.VisualGuidanceCurrentToneVolume = currentToneVolumeTrackBar.Value / 100.0;
        settings.VisualGuidanceHardPanTone = visualGuidanceHardPanCheckBox.Checked;

        settings.TakeoffAssistToneWaveform = (HandFlyWaveType)takeoffToneCombo.SelectedIndex;
        settings.TakeoffAssistToneVolume = takeoffVolumeTrackBar.Value / 100.0;
        settings.TakeoffAssistMuteCenterlineAnnouncements = muteCenterlineCheckBox.Checked;
        settings.TakeoffAssistSteerTowardTone = steerTowardToneCheckBox.Checked;
        settings.TakeoffAssistHardPanTone = hardPanCheckBox.Checked;
        settings.TakeoffAssistHeadingToneThreshold = headingToneThresholdCombo.SelectedIndex;
        settings.TakeoffAssistLegacyMode = legacyTakeoffCheckBox.Checked;
        settings.TakeoffAssistEnableCallouts = enableCalloutsCheckBox.Checked;
        settings.TakeoffAssistAutoActivateOnLineup = autoActivateOnLineupCheckBox.Checked;
        settings.HandFlyAutoActivateOnTakeoff = handFlyAutoActivateOnTakeoffCheckBox.Checked;
    }

    /// <summary>Stops the test tone whenever this tab is left (tab switch or dialog close on
    /// any path — OK, Cancel, or the [X] button), and resets the Test Tone button's caption
    /// back to idle so re-entering the tab never shows a stale "Stop Test". Idempotent and
    /// non-throwing.</summary>
    public void OnLeaving()
    {
        StopTestTone();
        testToneButton.Text = "Test Tone";
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
