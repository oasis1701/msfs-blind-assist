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
    private CheckBox invertPanningCheckBox = null!;
    private CheckBox hardPanCheckBox = null!;
    private Label headingToneThresholdLabel = null!;
    private ComboBox headingToneThresholdCombo = null!;
    private CheckBox legacyTakeoffCheckBox = null!;
    private CheckBox enableCalloutsCheckBox = null!;
    private CheckBox autoActivateOnLineupCheckBox = null!;

    // Visual Guidance — optional "centered tone change".
    private CheckBox vgCenteredCheckBox = null!;
    private Label vgCenteredWaveLabel = null!;
    private ComboBox vgCenteredWaveCombo = null!;

    // Waypoint Flight Director (en-route) tone options.
    private Label fdSectionLabel = null!;
    private Label fdToneLabel = null!;
    private ComboBox fdToneCombo = null!;
    private Label fdVolumeLabel = null!;
    private TrackBar fdVolumeTrackBar = null!;
    private Label fdVolumeValueLabel = null!;
    private Label fdCurrentToneLabel = null!;
    private ComboBox fdCurrentToneCombo = null!;
    private Label fdCurrentVolumeLabel = null!;
    private TrackBar fdCurrentVolumeTrackBar = null!;
    private Label fdCurrentVolumeValueLabel = null!;
    private CheckBox fdHardPanCheckBox = null!;
    private CheckBox fdApAutoMuteCheckBox = null!;
    private CheckBox fdCenteredCheckBox = null!;
    private Label fdCenteredWaveLabel = null!;
    private ComboBox fdCenteredWaveCombo = null!;

    private Button okButton = null!;
    private Button cancelButton = null!;

    private AudioToneGenerator? testToneGenerator;

    // New-option staging values (read from SettingsManager.Current; committed only on OK so Cancel
    // is respected). These ride SettingsManager.Current directly rather than the constructor, to
    // avoid widening the already-large constructor + its MainForm caller.
    private bool _vgCenteredEnabled;
    private HandFlyWaveType _vgCenteredWave;
    private HandFlyWaveType _fdToneWave;
    private double _fdVolume;
    private HandFlyWaveType _fdCurrentWave;
    private double _fdCurrentVolume;
    private bool _fdHardPan;
    private bool _fdApAutoMute;
    private bool _fdCenteredEnabled;
    private HandFlyWaveType _fdCenteredWave;

    public HandFlyFeedbackMode SelectedFeedbackMode { get; private set; }
    public HandFlyWaveType SelectedWaveType { get; private set; }
    public double SelectedVolume { get; private set; }
    public bool MonitorHeading { get; private set; }
    public bool MonitorVerticalSpeed { get; private set; }
    public HandFlyWaveType GuidanceToneWaveform { get; private set; }
    public double SelectedGuidanceVolume { get; private set; }
    public HandFlyWaveType VisualGuidanceCurrentToneWaveform { get; private set; }
    public double VisualGuidanceCurrentToneVolume { get; private set; }
    public bool VisualGuidanceHardPanTone { get; private set; }
    public HandFlyWaveType TakeoffToneWaveform { get; private set; }
    public double TakeoffToneVolume { get; private set; }
    public bool TakeoffAssistMuteCenterlineAnnouncements { get; private set; }
    public bool TakeoffAssistInvertPanning { get; private set; }
    public bool TakeoffAssistHardPanTone { get; private set; }
    public int TakeoffAssistHeadingToneThreshold { get; private set; }
    public bool TakeoffAssistLegacyMode { get; private set; }
    public bool TakeoffAssistEnableCallouts { get; private set; }
    public bool TakeoffAssistAutoActivateOnLineup { get; private set; }

    public HandFlyOptionsForm(HandFlyFeedbackMode currentMode, HandFlyWaveType currentWaveType, double currentVolume,
        bool monitorHeading, bool monitorVerticalSpeed, HandFlyWaveType guidanceToneWaveform,
        double currentGuidanceVolume,
        HandFlyWaveType visualGuidanceCurrentToneWaveform,
        double visualGuidanceCurrentToneVolume,
        bool visualGuidanceHardPanTone,
        HandFlyWaveType takeoffToneWaveform, double takeoffToneVolume,
        bool takeoffAssistMuteCenterlineAnnouncements, bool takeoffAssistInvertPanning,
        bool takeoffAssistHardPanTone,
        int takeoffAssistHeadingToneThreshold, bool takeoffAssistLegacyMode,
        bool takeoffAssistEnableCallouts,
        bool takeoffAssistAutoActivateOnLineup)
    {
        SelectedFeedbackMode = currentMode;
        SelectedWaveType = currentWaveType;
        SelectedVolume = currentVolume;
        MonitorHeading = monitorHeading;
        MonitorVerticalSpeed = monitorVerticalSpeed;
        GuidanceToneWaveform = guidanceToneWaveform;
        SelectedGuidanceVolume = currentGuidanceVolume;
        VisualGuidanceCurrentToneWaveform = visualGuidanceCurrentToneWaveform;
        VisualGuidanceCurrentToneVolume = visualGuidanceCurrentToneVolume;
        VisualGuidanceHardPanTone = visualGuidanceHardPanTone;
        TakeoffToneWaveform = takeoffToneWaveform;
        TakeoffToneVolume = takeoffToneVolume;
        TakeoffAssistMuteCenterlineAnnouncements = takeoffAssistMuteCenterlineAnnouncements;
        TakeoffAssistInvertPanning = takeoffAssistInvertPanning;
        TakeoffAssistHardPanTone = takeoffAssistHardPanTone;
        TakeoffAssistHeadingToneThreshold = takeoffAssistHeadingToneThreshold;
        TakeoffAssistLegacyMode = takeoffAssistLegacyMode;
        TakeoffAssistEnableCallouts = takeoffAssistEnableCallouts;
        TakeoffAssistAutoActivateOnLineup = takeoffAssistAutoActivateOnLineup;
        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        Text = "Hand Fly Options";
        Size = new Size(500, 1000);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        // The dialog has grown past a typical screen height; scroll rather than clip on OK/Cancel.
        AutoScroll = true;

        // Stage the new (Visual Guidance centered + Flight Director) options from settings. They are
        // committed back to SettingsManager.Current in OkButton_Click (so Cancel discards them).
        var s = SettingsManager.Current;
        _vgCenteredEnabled = s.VisualGuidanceCenteredToneEnabled;
        _vgCenteredWave = s.VisualGuidanceCenteredToneWaveform;
        _fdToneWave = s.WaypointFdToneWaveform;
        _fdVolume = s.WaypointFdToneVolume;
        _fdCurrentWave = s.WaypointFdCurrentToneWaveform;
        _fdCurrentVolume = s.WaypointFdCurrentToneVolume;
        _fdHardPan = s.WaypointFdHardPanTone;
        _fdApAutoMute = s.WaypointFdApAutoMute;
        _fdCenteredEnabled = s.WaypointFdCenteredToneEnabled;
        _fdCenteredWave = s.WaypointFdCenteredToneWaveform;

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

        // Monitor Heading Checkbox
        monitorHeadingCheckBox = new CheckBox
        {
            Text = "Monitor Heading (announce heading changes at 1-second intervals)",
            Location = new Point(20, 330),
            Size = new Size(450, 25),
            Checked = MonitorHeading,
            AccessibleName = "Monitor Heading",
            AccessibleDescription = "Enable heading announcements during hand fly mode"
        };
        monitorHeadingCheckBox.CheckedChanged += MonitorHeadingCheckBox_CheckedChanged;

        // Monitor Vertical Speed Checkbox
        monitorVSCheckBox = new CheckBox
        {
            Text = "Monitor Vertical Speed (announce VS changes at 1-second intervals)",
            Location = new Point(20, 365),
            Size = new Size(450, 25),
            Checked = MonitorVerticalSpeed,
            AccessibleName = "Monitor Vertical Speed",
            AccessibleDescription = "Enable vertical speed announcements during hand fly mode"
        };
        monitorVSCheckBox.CheckedChanged += MonitorVSCheckBox_CheckedChanged;

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
        guidanceToneCombo.SelectedIndex = (int)GuidanceToneWaveform;
        guidanceToneCombo.SelectedIndexChanged += GuidanceToneCombo_SelectedIndexChanged;

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
            Value = (int)(SelectedGuidanceVolume * 100),
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
        currentToneCombo.SelectedIndex = (int)VisualGuidanceCurrentToneWaveform;
        currentToneCombo.SelectedIndexChanged += CurrentToneCombo_SelectedIndexChanged;

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
            Value = (int)(VisualGuidanceCurrentToneVolume * 100),
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
            Checked = VisualGuidanceHardPanTone,
            AccessibleName = "Hard-pan visual guidance tones",
            AccessibleDescription = "When enabled, both visual-guidance tones snap to full left or full right once bank exceeds about one degree, instead of a proportional pan. Useful on stereo speakers where partial pan is hard to distinguish from centred. Headphone users normally leave this off. Default off."
        };
        visualGuidanceHardPanCheckBox.CheckedChanged += VisualGuidanceHardPanCheckBox_CheckedChanged;

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
        takeoffToneCombo.SelectedIndex = (int)TakeoffToneWaveform;
        takeoffToneCombo.SelectedIndexChanged += TakeoffToneCombo_SelectedIndexChanged;

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
            Value = (int)(TakeoffToneVolume * 100),
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
            Checked = TakeoffAssistMuteCenterlineAnnouncements,
            AccessibleName = "Mute centerline deviation announcements",
            AccessibleDescription = "When enabled, mutes centerline deviation announcements in modern takeoff assist mode. Audio tone and pitch announcements continue."
        };
        muteCenterlineCheckBox.CheckedChanged += MuteCenterlineCheckBox_CheckedChanged;

        // Invert Heading Track Panning Checkbox
        invertPanningCheckBox = new CheckBox
        {
            Text = "Invert heading track panning",
            Location = new Point(20, 700),
            Size = new Size(450, 25),
            Checked = TakeoffAssistInvertPanning,
            AccessibleName = "Invert heading track panning",
            AccessibleDescription = "When enabled, reverses the audio panning direction. Heading right of runway pans tone to left ear instead of right."
        };
        invertPanningCheckBox.CheckedChanged += InvertPanningCheckBox_CheckedChanged;

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
            Checked = TakeoffAssistHardPanTone,
            AccessibleName = "Hard-pan centerline tone",
            AccessibleDescription = "When enabled, the takeoff-assist centerline tone plays at full pan to one side or the other instead of a proportional curve. Useful for stereo-speaker users who can't easily distinguish partial pan from centred. Default off."
        };
        hardPanCheckBox.CheckedChanged += (s, e) =>
            TakeoffAssistHardPanTone = hardPanCheckBox.Checked;

        // Heading Tone Threshold Label
        headingToneThresholdLabel = new Label
        {
            Text = "Play heading deviation tone:",
            Location = new Point(20, 765),
            Size = new Size(250, 20),
            AccessibleName = "Heading Tone Threshold Label"
        };

        // Heading Tone Threshold ComboBox
        headingToneThresholdCombo = new ComboBox
        {
            Location = new Point(280, 763),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Play heading deviation tone",
            AccessibleDescription = "Select when the heading deviation tone plays. Always plays continuously, or only when deviation exceeds selected threshold."
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
        headingToneThresholdCombo.SelectedIndex = TakeoffAssistHeadingToneThreshold;
        headingToneThresholdCombo.SelectedIndexChanged += HeadingToneThresholdCombo_SelectedIndexChanged;

        // Legacy Takeoff Assist Mode Checkbox
        legacyTakeoffCheckBox = new CheckBox
        {
            Text = "Legacy takeoff assist mode (heading-based, no tone)",
            Location = new Point(20, 800),
            Size = new Size(450, 25),
            Checked = TakeoffAssistLegacyMode,
            AccessibleName = "Legacy takeoff assist mode",
            AccessibleDescription = "When enabled, takeoff assist announces heading deviation in degrees without audio tone. When disabled, uses centerline tracking with audio tone."
        };
        legacyTakeoffCheckBox.CheckedChanged += LegacyTakeoffCheckBox_CheckedChanged;

        // Enable Takeoff Callouts Checkbox
        enableCalloutsCheckBox = new CheckBox
        {
            Text = "Enable takeoff assistant call outs",
            Location = new Point(20, 830),
            Size = new Size(450, 25),
            Checked = TakeoffAssistEnableCallouts,
            AccessibleName = "Enable takeoff assistant call outs",
            AccessibleDescription = "When enabled, announces speed callouts during takeoff roll: 80 knots, 100 knots, V1, and rotate."
        };
        enableCalloutsCheckBox.CheckedChanged += EnableCalloutsCheckBox_CheckedChanged;

        // Auto-Activate on Lineup Checkbox
        autoActivateOnLineupCheckBox = new CheckBox
        {
            Text = "Auto-activate Takeoff Assist on lineup",
            Location = new Point(20, 860),
            Size = new Size(450, 25),
            Checked = TakeoffAssistAutoActivateOnLineup,
            AccessibleName = "Auto-activate Takeoff Assist on lineup",
            AccessibleDescription = "When enabled, Takeoff Assist activates automatically when taxi guidance reaches a stable runway lineup, so you don't have to press control T. One-shot per route: if you disable Takeoff Assist after it auto-activates, it won't re-engage until the next taxi route."
        };
        autoActivateOnLineupCheckBox.CheckedChanged += AutoActivateOnLineupCheckBox_CheckedChanged;

        // ── Visual Guidance — centered tone change (optional) ──
        vgCenteredCheckBox = new CheckBox
        {
            Text = "Centered tone change (Visual Guidance)",
            Location = new Point(20, 900),
            Size = new Size(450, 25),
            Checked = _vgCenteredEnabled,
            AccessibleName = "Centered tone change for Visual Guidance",
            AccessibleDescription = "Off by default. When on, the Visual Guidance command tone changes to a waveform you choose below while you are laterally centered on the localizer, and changes back to its normal waveform when you drift off centerline. This gives you an extra timbre cue for centered versus not centered, on top of the left or right stereo pan. When off, the tone keeps its normal waveform at all times."
        };
        vgCenteredCheckBox.CheckedChanged += (s2, e2) => _vgCenteredEnabled = vgCenteredCheckBox.Checked;

        vgCenteredWaveLabel = new Label
        {
            Text = "Centered tone type:",
            Location = new Point(20, 933),
            Size = new Size(250, 20),
            AccessibleName = "Visual Guidance centered tone type Label"
        };
        vgCenteredWaveCombo = new ComboBox
        {
            Location = new Point(280, 931),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Visual Guidance centered tone type",
            AccessibleDescription = "The waveform the Visual Guidance command tone switches to while centered, when the centered tone change option above is enabled. Pick one clearly different from the normal guidance tone so the change is obvious."
        };
        vgCenteredWaveCombo.Items.AddRange(new object[] { "Sine (Smoothest)", "Triangle (Smooth)", "Sawtooth (Bright)", "Sine (Rich)" });
        vgCenteredWaveCombo.SelectedIndex = (int)_vgCenteredWave;
        vgCenteredWaveCombo.SelectedIndexChanged += (s2, e2) => _vgCenteredWave = (HandFlyWaveType)vgCenteredWaveCombo.SelectedIndex;

        // ── Waypoint Flight Director (en-route) tones ──
        fdSectionLabel = new Label
        {
            Text = "Flight Director (en-route) tones:",
            Location = new Point(20, 968),
            Size = new Size(450, 20),
            AccessibleName = "Flight Director tones section"
        };

        fdToneLabel = new Label
        {
            Text = "Flight Director tone:",
            Location = new Point(20, 998),
            Size = new Size(250, 20),
            AccessibleName = "Flight Director tone type Label"
        };
        fdToneCombo = new ComboBox
        {
            Location = new Point(280, 996),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Flight Director tone type",
            AccessibleDescription = "Waveform of the Waypoint Flight Director's command tone (the one whose pan is the bank command and whose pitch is the climb or descend command)."
        };
        fdToneCombo.Items.AddRange(new object[] { "Sine (Smoothest)", "Triangle (Smooth)", "Sawtooth (Bright)", "Sine (Rich)" });
        fdToneCombo.SelectedIndex = (int)_fdToneWave;
        fdToneCombo.SelectedIndexChanged += (s2, e2) => _fdToneWave = (HandFlyWaveType)fdToneCombo.SelectedIndex;

        fdVolumeLabel = new Label
        {
            Text = "Flight Director Volume:",
            Location = new Point(20, 1033),
            Size = new Size(100, 20),
            AccessibleName = "Flight Director volume Label"
        };
        fdVolumeTrackBar = new TrackBar
        {
            Location = new Point(120, 1028),
            Size = new Size(300, 45),
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Value = (int)(_fdVolume * 100),
            AccessibleName = "Flight Director volume level",
            AccessibleDescription = "Adjust the Waypoint Flight Director command-tone volume from 0 to 100 percent."
        };
        fdVolumeValueLabel = new Label
        {
            Text = $"{fdVolumeTrackBar.Value}%",
            Location = new Point(430, 1033),
            Size = new Size(40, 20),
            AccessibleName = "Flight Director volume value",
            TextAlign = ContentAlignment.MiddleLeft
        };
        fdVolumeTrackBar.ValueChanged += (s2, e2) =>
        {
            _fdVolume = fdVolumeTrackBar.Value / 100.0;
            fdVolumeValueLabel.Text = $"{fdVolumeTrackBar.Value}%";
        };

        fdCurrentToneLabel = new Label
        {
            Text = "Flight Director current-attitude tone:",
            Location = new Point(20, 1078),
            Size = new Size(250, 20),
            AccessibleName = "Flight Director current attitude tone type Label"
        };
        fdCurrentToneCombo = new ComboBox
        {
            Location = new Point(280, 1076),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Flight Director current attitude tone type",
            AccessibleDescription = "Waveform of the Flight Director's second tone, which tracks your actual attitude. Pick a waveform different from the command tone above so the two stay distinguishable when their pitches match."
        };
        fdCurrentToneCombo.Items.AddRange(new object[] { "Sine (Smoothest)", "Triangle (Smooth)", "Sawtooth (Bright)", "Sine (Rich)" });
        fdCurrentToneCombo.SelectedIndex = (int)_fdCurrentWave;
        fdCurrentToneCombo.SelectedIndexChanged += (s2, e2) => _fdCurrentWave = (HandFlyWaveType)fdCurrentToneCombo.SelectedIndex;

        fdCurrentVolumeLabel = new Label
        {
            Text = "FD current-attitude Volume:",
            Location = new Point(20, 1113),
            Size = new Size(100, 20),
            AccessibleName = "Flight Director current attitude tone volume Label"
        };
        fdCurrentVolumeTrackBar = new TrackBar
        {
            Location = new Point(120, 1108),
            Size = new Size(300, 45),
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Value = (int)(_fdCurrentVolume * 100),
            AccessibleName = "Flight Director current attitude tone volume level",
            AccessibleDescription = "Adjust the Flight Director's current-attitude (follower) tone volume from 0 to 100 percent."
        };
        fdCurrentVolumeValueLabel = new Label
        {
            Text = $"{fdCurrentVolumeTrackBar.Value}%",
            Location = new Point(430, 1113),
            Size = new Size(40, 20),
            AccessibleName = "Flight Director current attitude tone volume value",
            TextAlign = ContentAlignment.MiddleLeft
        };
        fdCurrentVolumeTrackBar.ValueChanged += (s2, e2) =>
        {
            _fdCurrentVolume = fdCurrentVolumeTrackBar.Value / 100.0;
            fdCurrentVolumeValueLabel.Text = $"{fdCurrentVolumeTrackBar.Value}%";
        };

        fdHardPanCheckBox = new CheckBox
        {
            Text = "Hard-pan Flight Director tones (speaker-friendly)",
            Location = new Point(20, 1158),
            Size = new Size(450, 25),
            Checked = _fdHardPan,
            AccessibleName = "Hard-pan Flight Director tones",
            AccessibleDescription = "When enabled, both Flight Director tones snap to full left or full right once the bank command exceeds about one degree, instead of a proportional pan. Useful on stereo speakers where partial pan is hard to distinguish from centred. Headphone users normally leave this off. Default off."
        };
        fdHardPanCheckBox.CheckedChanged += (s2, e2) => _fdHardPan = fdHardPanCheckBox.Checked;

        fdApAutoMuteCheckBox = new CheckBox
        {
            Text = "Flight Director auto-mute with autopilot",
            Location = new Point(20, 1188),
            Size = new Size(450, 25),
            Checked = _fdApAutoMute,
            AccessibleName = "Flight Director auto-mute with autopilot",
            AccessibleDescription = "When enabled, the Flight Director tones go silent while the autopilot master is engaged and resume when you disengage it, so you can hand-fly with the Flight Director, engage the autopilot for cruise, and have the tone step aside on its own. Default on."
        };
        fdApAutoMuteCheckBox.CheckedChanged += (s2, e2) => _fdApAutoMute = fdApAutoMuteCheckBox.Checked;

        fdCenteredCheckBox = new CheckBox
        {
            Text = "Centered tone change (Flight Director)",
            Location = new Point(20, 1218),
            Size = new Size(450, 25),
            Checked = _fdCenteredEnabled,
            AccessibleName = "Centered tone change for the Flight Director",
            AccessibleDescription = "Off by default. When on, the Flight Director command tone changes to a waveform you choose below while you are on track (the bank command is near zero), and changes back when you drift off track. An extra timbre cue for on-track versus not, on top of the left or right pan. When off, the tone keeps its normal waveform at all times."
        };
        fdCenteredCheckBox.CheckedChanged += (s2, e2) => _fdCenteredEnabled = fdCenteredCheckBox.Checked;

        fdCenteredWaveLabel = new Label
        {
            Text = "Centered tone type:",
            Location = new Point(20, 1251),
            Size = new Size(250, 20),
            AccessibleName = "Flight Director centered tone type Label"
        };
        fdCenteredWaveCombo = new ComboBox
        {
            Location = new Point(280, 1249),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Flight Director centered tone type",
            AccessibleDescription = "The waveform the Flight Director command tone switches to while on track, when the centered tone change option above is enabled. Pick one clearly different from the normal Flight Director tone so the change is obvious."
        };
        fdCenteredWaveCombo.Items.AddRange(new object[] { "Sine (Smoothest)", "Triangle (Smooth)", "Sawtooth (Bright)", "Sine (Rich)" });
        fdCenteredWaveCombo.SelectedIndex = (int)_fdCenteredWave;
        fdCenteredWaveCombo.SelectedIndexChanged += (s2, e2) => _fdCenteredWave = (HandFlyWaveType)fdCenteredWaveCombo.SelectedIndex;

        // OK Button
        okButton = new Button
        {
            Text = "OK",
            Location = new Point(310, 1310),
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
            Location = new Point(395, 1310),
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
            testToneButton, monitorHeadingCheckBox, monitorVSCheckBox,
            guidanceToneLabel, guidanceToneCombo,
            guidanceVolumeLabel, guidanceVolumeTrackBar, guidanceVolumeValueLabel,
            currentToneLabel, currentToneCombo,
            currentToneVolumeLabel, currentToneVolumeTrackBar, currentToneVolumeValueLabel,
            visualGuidanceHardPanCheckBox,
            takeoffToneLabel, takeoffToneCombo,
            takeoffVolumeLabel, takeoffVolumeTrackBar, takeoffVolumeValueLabel,
            muteCenterlineCheckBox, invertPanningCheckBox, hardPanCheckBox,
            headingToneThresholdLabel, headingToneThresholdCombo,
            legacyTakeoffCheckBox, enableCalloutsCheckBox, autoActivateOnLineupCheckBox,
            vgCenteredCheckBox, vgCenteredWaveLabel, vgCenteredWaveCombo,
            fdSectionLabel,
            fdToneLabel, fdToneCombo,
            fdVolumeLabel, fdVolumeTrackBar, fdVolumeValueLabel,
            fdCurrentToneLabel, fdCurrentToneCombo,
            fdCurrentVolumeLabel, fdCurrentVolumeTrackBar, fdCurrentVolumeValueLabel,
            fdHardPanCheckBox, fdApAutoMuteCheckBox,
            fdCenteredCheckBox, fdCenteredWaveLabel, fdCenteredWaveCombo,
            okButton, cancelButton
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
        invertPanningCheckBox.TabIndex = 26;
        hardPanCheckBox.TabIndex = 27;
        headingToneThresholdLabel.TabIndex = 28;
        headingToneThresholdCombo.TabIndex = 29;
        legacyTakeoffCheckBox.TabIndex = 30;
        enableCalloutsCheckBox.TabIndex = 31;
        autoActivateOnLineupCheckBox.TabIndex = 32;
        vgCenteredCheckBox.TabIndex = 33;
        vgCenteredWaveLabel.TabIndex = 34;
        vgCenteredWaveCombo.TabIndex = 35;
        fdSectionLabel.TabIndex = 36;
        fdToneLabel.TabIndex = 37;
        fdToneCombo.TabIndex = 38;
        fdVolumeLabel.TabIndex = 39;
        fdVolumeTrackBar.TabIndex = 40;
        fdCurrentToneLabel.TabIndex = 41;
        fdCurrentToneCombo.TabIndex = 42;
        fdCurrentVolumeLabel.TabIndex = 43;
        fdCurrentVolumeTrackBar.TabIndex = 44;
        fdHardPanCheckBox.TabIndex = 45;
        fdApAutoMuteCheckBox.TabIndex = 46;
        fdCenteredCheckBox.TabIndex = 47;
        fdCenteredWaveLabel.TabIndex = 48;
        fdCenteredWaveCombo.TabIndex = 49;
        okButton.TabIndex = 50;
        cancelButton.TabIndex = 51;

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

    private void MonitorHeadingCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        MonitorHeading = monitorHeadingCheckBox.Checked;
    }

    private void MonitorVSCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        MonitorVerticalSpeed = monitorVSCheckBox.Checked;
    }

    private void GuidanceToneCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        GuidanceToneWaveform = (HandFlyWaveType)guidanceToneCombo.SelectedIndex;
    }

    private void GuidanceVolumeTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        SelectedGuidanceVolume = guidanceVolumeTrackBar.Value / 100.0;
        guidanceVolumeValueLabel.Text = $"{guidanceVolumeTrackBar.Value}%";
    }

    private void CurrentToneCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        VisualGuidanceCurrentToneWaveform = (HandFlyWaveType)currentToneCombo.SelectedIndex;
    }

    private void CurrentToneVolumeTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        VisualGuidanceCurrentToneVolume = currentToneVolumeTrackBar.Value / 100.0;
        currentToneVolumeValueLabel.Text = $"{currentToneVolumeTrackBar.Value}%";
    }

    private void VisualGuidanceHardPanCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        VisualGuidanceHardPanTone = visualGuidanceHardPanCheckBox.Checked;
    }

    private void TakeoffToneCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        TakeoffToneWaveform = (HandFlyWaveType)takeoffToneCombo.SelectedIndex;
    }

    private void TakeoffVolumeTrackBar_ValueChanged(object? sender, EventArgs e)
    {
        TakeoffToneVolume = takeoffVolumeTrackBar.Value / 100.0;
        takeoffVolumeValueLabel.Text = $"{takeoffVolumeTrackBar.Value}%";
    }

    private void MuteCenterlineCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        TakeoffAssistMuteCenterlineAnnouncements = muteCenterlineCheckBox.Checked;
    }

    private void InvertPanningCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        TakeoffAssistInvertPanning = invertPanningCheckBox.Checked;
    }

    private void HeadingToneThresholdCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        TakeoffAssistHeadingToneThreshold = headingToneThresholdCombo.SelectedIndex;
    }

    private void LegacyTakeoffCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        TakeoffAssistLegacyMode = legacyTakeoffCheckBox.Checked;
    }

    private void EnableCalloutsCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        TakeoffAssistEnableCallouts = enableCalloutsCheckBox.Checked;
    }

    private void AutoActivateOnLineupCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        TakeoffAssistAutoActivateOnLineup = autoActivateOnLineupCheckBox.Checked;
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

        // Commit the new Visual Guidance centered + Flight Director options directly to settings
        // (these ride SettingsManager.Current rather than the constructor/property round-trip).
        // MainForm calls SettingsManager.Save() after this dialog returns OK.
        var s = SettingsManager.Current;
        s.VisualGuidanceCenteredToneEnabled = _vgCenteredEnabled;
        s.VisualGuidanceCenteredToneWaveform = _vgCenteredWave;
        s.WaypointFdToneWaveform = _fdToneWave;
        s.WaypointFdToneVolume = _fdVolume;
        s.WaypointFdCurrentToneWaveform = _fdCurrentWave;
        s.WaypointFdCurrentToneVolume = _fdCurrentVolume;
        s.WaypointFdHardPanTone = _fdHardPan;
        s.WaypointFdApAutoMute = _fdApAutoMute;
        s.WaypointFdCenteredToneEnabled = _fdCenteredEnabled;
        s.WaypointFdCenteredToneWaveform = _fdCenteredWave;

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
