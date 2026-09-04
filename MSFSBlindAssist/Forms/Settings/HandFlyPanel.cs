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
    private TestTonePlayer testTonePlayer = null!;

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
    // Waypoint Flight Director (en-route hand-fly) tone options + slip cue volume.
    private Label fdSectionLabel = null!;
    private Label fdToneLabel = null!; private ComboBox fdToneCombo = null!;
    private Label fdVolumeLabel = null!; private TrackBar fdVolumeTrackBar = null!; private Label fdVolumeValueLabel = null!;
    private Label fdCurrentToneLabel = null!; private ComboBox fdCurrentToneCombo = null!;
    private Label fdCurrentVolumeLabel = null!; private TrackBar fdCurrentVolumeTrackBar = null!; private Label fdCurrentVolumeValueLabel = null!;
    private CheckBox fdHardPanCheckBox = null!;
    private CheckBox fdApMuteCheckBox = null!;
    private CheckBox vgCenteredCheckBox = null!;
    private Label vgCenteredWaveLabel = null!; private ComboBox vgCenteredWaveCombo = null!;
    private CheckBox fdCenteredCheckBox = null!;
    private Label fdCenteredWaveLabel = null!; private ComboBox fdCenteredWaveCombo = null!;
    private Button fdTestToneButton = null!;
    private Label slipVolumeLabel = null!; private TrackBar slipVolumeTrackBar = null!; private Label slipVolumeValueLabel = null!;
    private CheckBox handFlyAutoActivateOnTakeoffCheckBox = null!;

    // Six seconds of tone at TestTonePlayer's 100 ms tick — the longest of the three
    // auditions, because this one demonstrates pitch as well as pan and the pitch sweep is
    // deliberately slow (see the rate limiter in TestTonePitch).
    private const int TestToneTicks = 60;

    // One complete left-right-left cycle, built once. Bank drives pan in hand-fly mode, so the
    // preview has to reach both channels; see TestTonePan.FullCycle.
    private static readonly float[] PanSweep = TestTonePan.FullCycle(TestToneTicks);
    // The FD preview plays BOTH tones at once (that is the whole point — the pilot flies by
    // matching them), so it needs its own pair, separate from the single-tone Hand Fly preview.
    private AudioToneGenerator? fdTestDesiredTone;
    private AudioToneGenerator? fdTestCurrentTone;

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
            Location = new Point(20, 280),
            Size = new Size(120, 35),
            AccessibleDescription = "Play a sample tone with current settings"
        };
        // The player owns this button's Text and AccessibleName from here on (it sets the idle
        // pair in its constructor). Assigning Text alone — which this panel used to do — leaves
        // a screen reader announcing "Test Tone" on a button that would actually stop one,
        // because ControlAccessibleObject.Name never falls back to Text once AccessibleName has
        // been set. This panel has no status readout, so a failure surfaces as the same dialog
        // it always used.
        testTonePlayer = new TestTonePlayer(testToneButton, ShowAudioError);
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

        // Visual-guidance "centered tone change" — the SAME option the Flight Director has, and
        // VisualGuidanceManager already implements it (centeredToneEnabled / centeredToneWaveType,
        // 1.5° deadband). The settings existed and MainForm passed them to Initialize, but nothing
        // ever set them: with no control here the feature could not be switched on at all.
        vgCenteredCheckBox = new CheckBox
        {
            Text = "Play a centered tone change when on target",
            Location = new Point(20, 593),
            Size = new Size(460, 25),
            AccessibleName = "Play a centered visual guidance tone change on target",
            AccessibleDescription = "When enabled, the visual-guidance command tone changes to a different waveform while you are laterally centred, giving a timbre cue for centred versus off-track on top of the pan. Default off."
        };
        vgCenteredWaveLabel = new Label
        {
            Text = "Centered tone type:",
            Location = new Point(20, 621),
            Size = new Size(250, 20),
            AccessibleName = "Visual guidance centered tone type Label"
        };
        vgCenteredWaveCombo = new ComboBox
        {
            Location = new Point(280, 619),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Visual guidance centered tone type",
            AccessibleDescription = "Waveform the visual-guidance command tone switches to while centred."
        };
        vgCenteredWaveCombo.Items.AddRange(new object[]{ "Sine (Smoothest)", "Triangle (Smooth)", "Sawtooth (Bright)", "Square (Sharp)" });

        // Takeoff Assist - Tone Waveform Label
        takeoffToneLabel = new Label
        {
            Text = "Takeoff Assist Tone:",
            Location = new Point(20, 651),
            Size = new Size(250, 20),
            AccessibleName = "Takeoff Assist Tone Label"
        };

        // Takeoff Assist - Tone Waveform ComboBox
        takeoffToneCombo = new ComboBox
        {
            Location = new Point(280, 649),
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
            Location = new Point(20, 686),
            Size = new Size(100, 20),
            AccessibleName = "Takeoff Assist Volume Label"
        };

        // Takeoff Assist Volume TrackBar
        takeoffVolumeTrackBar = new TrackBar
        {
            Location = new Point(120, 681),
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
            Location = new Point(430, 686),
            Size = new Size(40, 20),
            AccessibleName = "Takeoff Assist Volume Value",
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Mute Centerline Deviation Announcements Checkbox
        muteCenterlineCheckBox = new CheckBox
        {
            Text = "Mute centerline deviation announcements",
            Location = new Point(20, 721),
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
            Location = new Point(20, 756),
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
            Location = new Point(20, 786),
            Size = new Size(450, 25),
            AccessibleName = "Hard-pan centerline tone",
            AccessibleDescription = "When enabled, the takeoff-assist centerline tone plays at full pan to one side or the other instead of a proportional curve. Useful for stereo-speaker users who can't easily distinguish partial pan from centred. Default off."
        };

        // Heading Tone Threshold Label
        headingToneThresholdLabel = new Label
        {
            Text = "Play steering tone:",
            Location = new Point(20, 821),
            Size = new Size(250, 20),
            AccessibleName = "Steering Tone Threshold Label"
        };

        // Heading Tone Threshold ComboBox
        headingToneThresholdCombo = new ComboBox
        {
            Location = new Point(280, 819),
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
            Location = new Point(20, 856),
            Size = new Size(450, 25),
            AccessibleName = "Legacy takeoff assist mode",
            AccessibleDescription = "When enabled, takeoff assist announces heading deviation in degrees without audio tone. When disabled, uses centerline tracking with audio tone."
        };

        // Enable Takeoff Callouts Checkbox
        enableCalloutsCheckBox = new CheckBox
        {
            Text = "Enable takeoff assistant call outs",
            Location = new Point(20, 886),
            Size = new Size(450, 25),
            AccessibleName = "Enable takeoff assistant call outs",
            AccessibleDescription = "When enabled, announces speed callouts during takeoff roll: 80 knots, 100 knots, V1, and rotate."
        };

        // Auto-Activate on Lineup Checkbox
        autoActivateOnLineupCheckBox = new CheckBox
        {
            Text = "Auto-activate Takeoff Assist on lineup",
            Location = new Point(20, 916),
            Size = new Size(450, 25),
            AccessibleName = "Auto-activate Takeoff Assist on lineup",
            AccessibleDescription = "When enabled, Takeoff Assist activates automatically when taxi guidance reaches a stable runway lineup, so you don't have to press control T. One-shot per route: if you disable Takeoff Assist after it auto-activates, it won't re-engage until the next taxi route."
        };

        // ── Waypoint Flight Director (en-route hand-fly) tone options ──────────
        fdSectionLabel = new Label { Text = "Waypoint Flight Director (en-route to tracked fixes):", Location = new Point(20, 956), Size = new Size(500, 20), AccessibleName = "Waypoint Flight Director section" };
        fdToneLabel = new Label { Text = "FD target tone type:", Location = new Point(20, 984), Size = new Size(250, 20), AccessibleName = "FD target tone type Label" };
        fdToneCombo = new ComboBox { Location = new Point(280, 982), Size = new Size(190, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "FD target tone type" };
        fdToneCombo.Items.AddRange(new object[]{ "Sine (Smoothest)", "Triangle (Smooth)", "Sawtooth (Bright)", "Square (Sharp)" });
        fdVolumeLabel = new Label { Text = "FD target volume:", Location = new Point(20, 1014), Size = new Size(150, 20), AccessibleName = "FD target volume Label" };
        fdVolumeTrackBar = new TrackBar { Location = new Point(180, 1009), Size = new Size(250, 45), Minimum = 0, Maximum = 100, TickFrequency = 10, AccessibleName = "FD target volume", AccessibleDescription = "Volume of the Flight Director target tone, 0 to 100 percent" };
        fdVolumeValueLabel = new Label { Text = "5%", Location = new Point(435, 1014), Size = new Size(45, 20), AccessibleName = "FD target volume value" };
        fdVolumeTrackBar.ValueChanged += (_, _) => fdVolumeValueLabel.Text = fdVolumeTrackBar.Value + "%";
        fdCurrentToneLabel = new Label { Text = "FD current-attitude tone type:", Location = new Point(20, 1056), Size = new Size(250, 20), AccessibleName = "FD current tone type Label" };
        fdCurrentToneCombo = new ComboBox { Location = new Point(280, 1054), Size = new Size(190, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "FD current-attitude tone type" };
        fdCurrentToneCombo.Items.AddRange(new object[]{ "Sine (Smoothest)", "Triangle (Smooth)", "Sawtooth (Bright)", "Square (Sharp)" });
        fdCurrentVolumeLabel = new Label { Text = "FD current volume:", Location = new Point(20, 1086), Size = new Size(150, 20), AccessibleName = "FD current volume Label" };
        fdCurrentVolumeTrackBar = new TrackBar { Location = new Point(180, 1081), Size = new Size(250, 45), Minimum = 0, Maximum = 100, TickFrequency = 10, AccessibleName = "FD current volume", AccessibleDescription = "Volume of the Flight Director current-attitude tone, 0 to 100 percent" };
        fdCurrentVolumeValueLabel = new Label { Text = "5%", Location = new Point(435, 1086), Size = new Size(45, 20), AccessibleName = "FD current volume value" };
        fdCurrentVolumeTrackBar.ValueChanged += (_, _) => fdCurrentVolumeValueLabel.Text = fdCurrentVolumeTrackBar.Value + "%";
        fdHardPanCheckBox = new CheckBox { Text = "Hard-pan the FD tone (snap fully left/right instead of proportional)", Location = new Point(20, 1128), Size = new Size(460, 25), AccessibleName = "Hard-pan the Flight Director tone" };
        fdApMuteCheckBox = new CheckBox { Text = "Auto-mute FD tones while the autopilot is engaged", Location = new Point(20, 1156), Size = new Size(460, 25), AccessibleName = "Auto-mute Flight Director tones while autopilot engaged" };
        fdCenteredCheckBox = new CheckBox { Text = "Play a centered tone change when on target", Location = new Point(20, 1184), Size = new Size(460, 25), AccessibleName = "Play a centered tone change on target" };
        fdCenteredWaveLabel = new Label { Text = "Centered tone type:", Location = new Point(20, 1212), Size = new Size(250, 20), AccessibleName = "FD centered tone type Label" };
        fdCenteredWaveCombo = new ComboBox { Location = new Point(280, 1210), Size = new Size(190, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "FD centered tone type" };
        fdCenteredWaveCombo.Items.AddRange(new object[]{ "Sine (Smoothest)", "Triangle (Smooth)", "Sawtooth (Bright)", "Square (Sharp)" });
        fdTestToneButton = new Button
        {
            Text = "Test Flight Director Tones",
            Location = new Point(20, 1240),
            Size = new Size(220, 35),
            AccessibleName = "Test Flight Director Tones",
            AccessibleDescription = "Play both Flight Director tones together with a left to right bank sweep, so you can hear the command tone move against the steady current-attitude tone. Applies your waveform, volume, hard-pan and centered-tone selections. Stops on its own after a few seconds."
        };
        fdTestToneButton.Click += FdTestToneButton_Click;
        slipVolumeLabel = new Label { Text = "Slip cue volume (Ctrl+K):", Location = new Point(20, 1289), Size = new Size(160, 20), AccessibleName = "Slip cue volume Label" };
        slipVolumeTrackBar = new TrackBar { Location = new Point(190, 1284), Size = new Size(240, 45), Minimum = 0, Maximum = 100, TickFrequency = 10, AccessibleName = "Slip cue volume", AccessibleDescription = "Volume of the rudder-coordination slip cue, 0 to 100 percent" };
        slipVolumeValueLabel = new Label { Text = "20%", Location = new Point(435, 1289), Size = new Size(45, 20), AccessibleName = "Slip cue volume value" };
        slipVolumeTrackBar.ValueChanged += (_, _) => slipVolumeValueLabel.Text = slipVolumeTrackBar.Value + "%";
        // Auto-Activate Hand Fly on Takeoff Checkbox — completes the taxi → Takeoff Assist →
        // Hand Fly hands-free chain. Placed BELOW the Waypoint FD section (y 900–1273) to
        // avoid overlapping it (the panel AutoScrolls).
        handFlyAutoActivateOnTakeoffCheckBox = new CheckBox
        {
            Text = "Auto-activate Hand Fly on takeoff (deactivates Takeoff Assist)",
            Location = new Point(20, 1341),
            Size = new Size(460, 25),
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
            vgCenteredCheckBox, vgCenteredWaveLabel, vgCenteredWaveCombo,
            takeoffToneLabel, takeoffToneCombo,
            takeoffVolumeLabel, takeoffVolumeTrackBar, takeoffVolumeValueLabel,
            muteCenterlineCheckBox, steerTowardToneCheckBox, hardPanCheckBox,
            headingToneThresholdLabel, headingToneThresholdCombo,
            legacyTakeoffCheckBox, enableCalloutsCheckBox, autoActivateOnLineupCheckBox,
            fdSectionLabel, fdToneLabel, fdToneCombo, fdVolumeLabel, fdVolumeTrackBar, fdVolumeValueLabel,
            fdCurrentToneLabel, fdCurrentToneCombo, fdCurrentVolumeLabel, fdCurrentVolumeTrackBar, fdCurrentVolumeValueLabel,
            fdHardPanCheckBox, fdApMuteCheckBox, fdCenteredCheckBox, fdCenteredWaveLabel, fdCenteredWaveCombo,
            fdTestToneButton,
            slipVolumeLabel, slipVolumeTrackBar, slipVolumeValueLabel,
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
        vgCenteredCheckBox.TabIndex = 21;
        vgCenteredWaveLabel.TabIndex = 22;
        vgCenteredWaveCombo.TabIndex = 23;
        takeoffToneLabel.TabIndex = 24;
        takeoffToneCombo.TabIndex = 25;
        takeoffVolumeLabel.TabIndex = 26;
        takeoffVolumeTrackBar.TabIndex = 27;
        muteCenterlineCheckBox.TabIndex = 28;
        steerTowardToneCheckBox.TabIndex = 29;
        hardPanCheckBox.TabIndex = 30;
        headingToneThresholdLabel.TabIndex = 31;
        headingToneThresholdCombo.TabIndex = 32;
        legacyTakeoffCheckBox.TabIndex = 33;
        enableCalloutsCheckBox.TabIndex = 34;
        autoActivateOnLineupCheckBox.TabIndex = 35;
        handFlyAutoActivateOnTakeoffCheckBox.TabIndex = 36;
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

        // Disabling the button also ends a sounding audition: TestTonePlayer stops itself on
        // its button's EnabledChanged, so no disable site has to remember to Stop() first.
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

    // Pitch demo, rate-limited so the oscillator's frequency never steps far enough between
    // ticks to crackle. 3 deg/s over TestTonePlayer's 100 ms tick.
    private const double MaxPitchDeltaPerTick = 3.0 * 0.1;

    // Slow enough that the rate limiter above can still track it to this demo's own ±10° sweep.
    // NOTE that ±10° is NOT the full span of the tone's mapping — see StepPitch. The travel
    // budget below is stated against this demo's sweep, not against the mapping's −10°/+20°.
    //
    // This deliberately covers only HALF a cycle over the demo's 60 ticks (0..1.5 rad), so the
    // pitch demo sweeps nose-up only. Do NOT "harmonise" it onto TestTonePan.FullCycle the way
    // the pan sweep was: a full pitch cycle needs 40° of target travel, and the rate limiter
    // above only affords 60 × 0.3 = 18°, so the demo would simply fail to track its own curve.
    // Correcting it means re-tuning MaxPitchDeltaPerTick, which is a crackle-avoidance number
    // that can only be judged by ear against real hardware.
    private const double PitchSweepRadPerTick = 0.025;

    private void TestToneButton_Click(object? sender, EventArgs e)
    {
        // Fresh per press: the pitch demo is rate-limited, so it carries state from one tick to
        // the next. The closure is per-Toggle, and only one demo loop runs at a time, so this
        // local is confined to the loop driving it.
        double currentPitch = 0.0;

        // TestTonePlayer owns the lifecycle: whether the tone actually sounded (Start degrades
        // silently by contract), the button's Text/AccessibleName pairing, the stale-session
        // guard and the auto-stop. This panel supplies only the tone and what each tick does.
        testTonePlayer.Toggle(
            StartTestTone,
            (tone, i) =>
            {
                currentPitch = StepPitch(currentPitch, i);
                tone.UpdatePitch(currentPitch);

                // Pan directly rather than through UpdateBank's degrees-to-pan mapping (the two
                // write the same panner), and with no rate limiter: the shared sweep is already
                // in pan units and already smoother than the limiter's own threshold, so the
                // limiter could never bind. Measured — at 60 ticks the sweep's largest step is
                // 0.084 pan/tick, against a cap of 1.5°/tick which is 0.150 pan/tick once
                // UpdateBank's ±10° range is applied. Limiting a curve that never exceeds the
                // limit is dead code, not safety.
                //
                // What DOES change is the shape of the extremes: the old cosine-plus-limiter
                // demo saturated, sitting pegged at full ±1.0 for ticks 6-8, 22-32 and 46-51
                // (bank trough −17.63° at tick 27, well past the ±10° clamp). It reached both
                // channels — do not repeat the earlier claim that it never reached full left,
                // which was a simulation error — it just spent a third of the demo hard against
                // the stops. The sweep now peaks smoothly at ±0.8 with no plateaus.
                tone.SetPan(PanSweep[i]);
            },
            TestToneTicks);
    }

    // ---- Waypoint Flight Director tone preview -------------------------------------------
    // History worth knowing: this button was originally written against the standalone
    // Forms/HandFlyOptionsForm.cs. main later retired that dialog into this panel, and when the
    // feature branch merged main the file was deleted — taking the button with it, silently and
    // without a conflict. The FD's other settings controls had been ported here; only the preview
    // was lost, which is why the docs described a button that did not exist. If you move this
    // panel again, move the preview with it.
    //
    // Mirrors the Hand Fly Test Tone button above, but plays BOTH FD tones together: the
    // "desired" (command) tone sweeps left↔right while the "current" (actual attitude) tone
    // holds steady at centre. That is the idiom the pilot flies — you hear the command move
    // against the reference and match them — so a preview of one tone alone would be useless.
    // Honours the panel's LIVE selections (waveform, volume, hard-pan, centered tone change),
    // not the saved settings, so the pilot can audition a change before pressing OK.

    /// <summary>Deadband within which the command tone counts as centred, for the centered-tone
    /// waveform swap. Mirrors <c>WaypointFlightDirectorManager.CenteredDeadbandDeg</c> — keep the
    /// two in step or the preview lies about where the timbre changes.</summary>
    private const double FdPreviewCenteredDeadbandDeg = 1.5;

    /// <summary>Six seconds at the preview loop's 100 ms tick, matching the Hand Fly audition's
    /// TestToneTicks. Also the divisor for the bank sweep, so the pan covers exactly one cycle.</summary>
    private const int FdPreviewTicks = 60;

    /// <summary>Sets the FD preview button's label AND accessible name together. WinForms returns
    /// an explicitly-set AccessibleName permanently — it does NOT fall back to Text — so assigning
    /// only Text left a screen reader announcing "Test Flight Director Tones" on a button that
    /// would stop one. Same rule TestTonePlayer.SetButtonState enforces for the other auditions.</summary>
    private void SetFdButtonState(bool playing)
    {
        if (fdTestToneButton.IsDisposed) return;
        fdTestToneButton.Text = playing ? "Stop FD Test" : "Test Flight Director Tones";
        fdTestToneButton.AccessibleName = playing ? "Stop Flight Director test tones" : "Test Flight Director Tones";
    }

    private void FdTestToneButton_Click(object? sender, EventArgs e)
    {
        if (fdTestDesiredTone?.IsPlaying == true || fdTestCurrentTone?.IsPlaying == true)
        {
            StopFdTestTones();
            SetFdButtonState(playing: false);
        }
        else
        {
            if (PlayFdTestTones())
                SetFdButtonState(playing: true);
        }
    }

    /// <summary>Starts the two-tone FD audition. Returns whether it is actually SOUNDING —
    /// <see cref="AudioToneGenerator.Start"/> swallows its own failures by contract (audio is
    /// optional feedback and degrades rather than throwing), so a dead endpoint returns silently
    /// with nothing playing. Without this the button latched to "Stop FD Test" — and announced it
    /// to the screen reader — over silence, and never reset, because both the demo loop and the
    /// auto-stop are gated on IsPlaying. Same check TestTonePlayer.TryStart makes.</summary>
    private bool PlayFdTestTones()
    {
        try
        {
            var desiredWave = (HandFlyWaveType)fdToneCombo.SelectedIndex;
            var currentWave = (HandFlyWaveType)fdCurrentToneCombo.SelectedIndex;
            var centeredWave = (HandFlyWaveType)fdCenteredWaveCombo.SelectedIndex;
            double desiredVol = fdVolumeTrackBar.Value / 100.0;
            double currentVol = fdCurrentVolumeTrackBar.Value / 100.0;
            bool hardPan = fdHardPanCheckBox.Checked;
            bool centeredOn = fdCenteredCheckBox.Checked;

            // Map Hz/pan the way the FD actually does. Configure must precede Start (the mapping is
            // captured there). The settings panel has no aircraft context, so this uses the baseline
            // profile defaults — which is what most airframes fly with anyway; a widebody's only
            // difference here is a wider TonePitchRangeDeg.
            var toneProfile = new MSFSBlindAssist.Aircraft.WaypointFlightDirectorProfile();

            fdTestDesiredTone = new AudioToneGenerator();
            fdTestDesiredTone.Configure(toneProfile.ToneMinFrequencyHz, toneProfile.ToneMaxFrequencyHz,
                                        toneProfile.TonePitchRangeDeg, toneProfile.ToneBankRangeDeg);
            fdTestDesiredTone.Start(desiredWave, desiredVol);
            fdTestCurrentTone = new AudioToneGenerator();
            fdTestCurrentTone.Configure(toneProfile.ToneMinFrequencyHz, toneProfile.ToneMaxFrequencyHz,
                                        toneProfile.TonePitchRangeDeg, toneProfile.ToneBankRangeDeg);
            fdTestCurrentTone.Start(currentWave, currentVol);

            if (fdTestDesiredTone?.IsPlaying != true || fdTestCurrentTone?.IsPlaying != true)
            {
                StopFdTestTones();
                ShowAudioError("Could not play the Flight Director test tones on the selected device.");
                return false;
            }

            // The current tone is the steady reference: level, wings level, for the whole preview.
            fdTestCurrentTone.UpdatePitch(0.0);
            ApplyPreviewBank(fdTestCurrentTone, 0.0, hardPan);

            var appliedDesiredWave = desiredWave;

            Task.Run(async () =>
            {
                for (int i = 0; i < FdPreviewTicks && fdTestDesiredTone?.IsPlaying == true; i++)
                {
                    // One slow left↔right cycle plus a gentler pitch swing, so both the pan
                    // (bank command) and the frequency (pitch command) are audible against the
                    // steady current tone. The step is DERIVED from the tick count so the sweep
                    // is exactly one full cycle and always reaches the left channel — the same
                    // rule TestTonePan.FullCycle exists to enforce (a hardcoded step is what let
                    // the old 20-tick audition stay in [0, pi] and never pan left at all).
                    double phase = i * 2.0 * Math.PI / FdPreviewTicks;
                    double bank = Math.Sin(phase) * 12.0;
                    double pitch = Math.Sin(phase * 0.5) * 4.0;

                    fdTestDesiredTone?.UpdatePitch(pitch);
                    var tone = fdTestDesiredTone;
                    if (tone != null) ApplyPreviewBank(tone, bank, hardPan);

                    if (centeredOn && tone != null)
                    {
                        var want = Math.Abs(bank) <= FdPreviewCenteredDeadbandDeg ? centeredWave : desiredWave;
                        if (want != appliedDesiredWave)
                        {
                            tone.UpdateWaveType(want);
                            appliedDesiredWave = want;
                        }
                    }

                    await Task.Delay(100);
                }

                // Auto-stop after ~6 seconds.
                if (fdTestDesiredTone?.IsPlaying == true || fdTestCurrentTone?.IsPlaying == true)
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        try
                        {
                            Invoke(() =>
                            {
                                StopFdTestTones();
                                SetFdButtonState(playing: false);
                            });
                        }
                        catch (InvalidOperationException)
                        {
                            // Handle destroyed mid-flight (tab switched/dialog closed) —
                            // StopFdTestTones is also called from OnLeaving/Dispose.
                        }
                    }
                    else
                    {
                        StopFdTestTones();
                    }
                }
            });
            return true;
        }
        catch
        {
            // Audio is optional feedback — never let a preview take the settings dialog down.
            StopFdTestTones();
            return false;
        }
    }

    /// <summary>Applies a bank to a preview tone the same way
    /// <c>WaypointFlightDirectorManager.ApplyBank</c> does, so hard-pan sounds in the preview
    /// exactly as it will in flight (snap to full left/right outside a 1° deadband).</summary>
    private static void ApplyPreviewBank(AudioToneGenerator tone, double bankDeg, bool hardPan)
    {
        if (hardPan)
            tone.SetPan(Math.Abs(bankDeg) < 1.0 ? 0f : (bankDeg > 0 ? 1f : -1f));
        else
            tone.UpdateBank(bankDeg);
    }

    /// <summary>Stops and disposes both FD preview tones. Idempotent and non-throwing.</summary>
    private void StopFdTestTones()
    {
        try
        {
            fdTestDesiredTone?.Stop();
            fdTestDesiredTone?.Dispose();
        }
        catch
        {
            // Ignore — teardown is best-effort.
        }
        finally
        {
            fdTestDesiredTone = null;
        }

        try
        {
            fdTestCurrentTone?.Stop();
            fdTestCurrentTone?.Dispose();
        }
        catch
        {
            // Ignore — teardown is best-effort.
        }
        finally
        {
            fdTestCurrentTone = null;
        }
    }

    /// <summary>Constructs and starts the hand-fly preview with the settings currently shown.
    /// Whether it actually sounded is TestTonePlayer's check.</summary>
    private AudioToneGenerator? StartTestTone()
    {
        var waveType = (HandFlyWaveType)waveTypeCombo.SelectedIndex;
        double volume = volumeTrackBar.Value / 100.0;

        var tone = new AudioToneGenerator();
        tone.Start(waveType, volume);
        return tone;
    }

    /// <summary>One rate-limited step of the pitch demo: pitch sweeps to +10°, which under the
    /// tone's default mapping is 650 Hz — NOT the top of the range. The mapping runs to +20°
    /// (800 Hz) because hand fly is auto-activated at liftoff, where an airliner is already
    /// 12–18° nose up, so this preview deliberately demonstrates only the lower part of what
    /// the tone actually plays in flight. Extending it would need MaxPitchDeltaPerTick
    /// re-tuned (60 ticks × 0.3° = 18° of travel, short of the 20° needed), and that is a
    /// crackle-avoidance number judgeable only by ear against real hardware.</summary>
    private static double StepPitch(double currentPitch, int tick)
    {
        double targetPitch = Math.Sin(tick * PitchSweepRadPerTick) * 10.0;

        double delta = targetPitch - currentPitch;
        if (Math.Abs(delta) > MaxPitchDeltaPerTick)
        {
            delta = Math.Sign(delta) * MaxPitchDeltaPerTick;
        }

        return currentPitch + delta;
    }

    private void ShowAudioError(string message)
    {
        MessageBox.Show(message, "Audio Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        vgCenteredCheckBox.Checked = settings.VisualGuidanceCenteredToneEnabled;
        vgCenteredWaveCombo.SelectedIndex = (int)settings.VisualGuidanceCenteredToneWaveform;

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

        fdToneCombo.SelectedIndex = (int)settings.WaypointFdToneWaveform;
        fdVolumeTrackBar.Value = (int)(settings.WaypointFdToneVolume * 100);
        fdVolumeValueLabel.Text = $"{fdVolumeTrackBar.Value}%";
        fdCurrentToneCombo.SelectedIndex = (int)settings.WaypointFdCurrentToneWaveform;
        fdCurrentVolumeTrackBar.Value = (int)(settings.WaypointFdCurrentToneVolume * 100);
        fdCurrentVolumeValueLabel.Text = $"{fdCurrentVolumeTrackBar.Value}%";
        fdHardPanCheckBox.Checked = settings.WaypointFdHardPanTone;
        fdApMuteCheckBox.Checked = settings.WaypointFdApAutoMute;
        fdCenteredCheckBox.Checked = settings.WaypointFdCenteredToneEnabled;
        fdCenteredWaveCombo.SelectedIndex = (int)settings.WaypointFdCenteredToneWaveform;
        slipVolumeTrackBar.Value = (int)(settings.SlipCueVolume * 100);
        slipVolumeValueLabel.Text = $"{slipVolumeTrackBar.Value}%";

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
        settings.VisualGuidanceCenteredToneEnabled = vgCenteredCheckBox.Checked;
        settings.VisualGuidanceCenteredToneWaveform = (HandFlyWaveType)vgCenteredWaveCombo.SelectedIndex;

        settings.TakeoffAssistToneWaveform = (HandFlyWaveType)takeoffToneCombo.SelectedIndex;
        settings.TakeoffAssistToneVolume = takeoffVolumeTrackBar.Value / 100.0;
        settings.TakeoffAssistMuteCenterlineAnnouncements = muteCenterlineCheckBox.Checked;
        settings.TakeoffAssistSteerTowardTone = steerTowardToneCheckBox.Checked;
        settings.TakeoffAssistHardPanTone = hardPanCheckBox.Checked;
        settings.TakeoffAssistHeadingToneThreshold = headingToneThresholdCombo.SelectedIndex;
        settings.TakeoffAssistLegacyMode = legacyTakeoffCheckBox.Checked;
        settings.TakeoffAssistEnableCallouts = enableCalloutsCheckBox.Checked;
        settings.TakeoffAssistAutoActivateOnLineup = autoActivateOnLineupCheckBox.Checked;
        settings.WaypointFdToneWaveform = (HandFlyWaveType)fdToneCombo.SelectedIndex;
        settings.WaypointFdToneVolume = fdVolumeTrackBar.Value / 100.0;
        settings.WaypointFdCurrentToneWaveform = (HandFlyWaveType)fdCurrentToneCombo.SelectedIndex;
        settings.WaypointFdCurrentToneVolume = fdCurrentVolumeTrackBar.Value / 100.0;
        settings.WaypointFdHardPanTone = fdHardPanCheckBox.Checked;
        settings.WaypointFdApAutoMute = fdApMuteCheckBox.Checked;
        settings.WaypointFdCenteredToneEnabled = fdCenteredCheckBox.Checked;
        settings.WaypointFdCenteredToneWaveform = (HandFlyWaveType)fdCenteredWaveCombo.SelectedIndex;
        settings.SlipCueVolume = slipVolumeTrackBar.Value / 100.0;
        settings.HandFlyAutoActivateOnTakeoff = handFlyAutoActivateOnTakeoffCheckBox.Checked;
    }

    /// <summary>Stops the test tone whenever this tab is left (tab switch or dialog close on
    /// any path — OK, Cancel, or the [X] button), and resets the Test Tone button's caption AND
    /// accessible name back to idle so re-entering the tab never shows — or announces — a stale
    /// "Stop Test". Idempotent and non-throwing.</summary>
    public void OnLeaving()
    {
        testTonePlayer?.Stop();
        StopFdTestTones();
        SetFdButtonState(playing: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            testTonePlayer?.Dispose();
            StopFdTestTones();
        }
        base.Dispose(disposing);
    }
}
