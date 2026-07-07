using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>Taxi Guidance section of the unified Settings dialog. Extracted from the retired
/// standalone Taxi Guidance Options dialog — same controls, same AccessibleNames/TabIndex, but
/// the old OK/Cancel buttons are gone (the dialog owns OK/Cancel) and both the steering test tone
/// and the docking-beep test are tied to <see cref="OnLeaving"/>/<see cref="Dispose(bool)"/>
/// instead of FormClosing/OK-click. The panel is taller than the tab viewport, so it scrolls
/// (<c>AutoScroll = true</c>).</summary>
public class TaxiGuidancePanel : UserControl, ISettingsPanel
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
    private Label takeoffGsAnnounceLabel = null!;
    private ComboBox takeoffGsAnnounceCombo = null!;
    private CheckBox useFeetForDistancesCheckBox = null!;
    private CheckBox useMetresCheckBox = null!;
    private CheckBox gsxAutoSelectGateCheckBox = null!;

    private GroupBox dockingGroup = null!;
    private CheckBox dockingEnabledCheckBox = null!;
    private Label dockingBeepTypeLabel = null!;
    private ComboBox dockingBeepTypeCombo = null!;
    private Label dockingBeepVolumeLabel = null!;
    private TrackBar dockingBeepVolumeTrackBar = null!;
    private Label dockingBeepVolumeValueLabel = null!;
    private Button dockingBeepTestButton = null!;

    private Button refreshTaxiwayNamesButton = null!;
    private CheckBox taxiAugmentEnabledCheckBox = null!;
    private Label taxiAugmentAttributionLabel = null!;

    // Optional callback for the manual taxiway-names refresh. Null when the caller doesn't
    // supply augmenting-provider support — the button disables itself in that case.
    private readonly Func<Task>? _onRefreshTaxiwayNames;

    private AudioToneGenerator? testToneGenerator;

    private ProximityBeeper? _dockingBeepTester;
    private CancellationTokenSource? _dockingBeepTestCts;

    public string TabTitle => "Taxi Guidance";

    public TaxiGuidancePanel(Func<Task>? refreshTaxiwayNames)
    {
        _onRefreshTaxiwayNames = refreshTaxiwayNames;
        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        AutoScroll = true;

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
            AccessibleName = "Volume Level",
            AccessibleDescription = "Adjust the taxi steering tone volume from 0 to 100 percent"
        };
        volumeTrackBar.ValueChanged += (s, e) => volumeValueLabel.Text = $"{volumeTrackBar.Value}%";

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
            AccessibleName = "Invert steering tone direction",
            AccessibleDescription = "When enabled, the stereo pan is reversed. A tone playing in the right ear means steer left to centre it, and vice versa. Default off."
        };

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
            AccessibleName = "Hard-pan steering tone",
            AccessibleDescription = "When enabled, the steering tone plays at full pan to one side or the other instead of the proportional sqrt curve. Useful for stereo-speaker users who can't easily distinguish partial pan from centred. Default off."
        };

        // Announce-crossings checkbox
        announceCrossingsCheckBox = new CheckBox
        {
            Text = "Announce crossing taxiways (e.g., \"Crossing taxiway Link 53\")",
            Location = new Point(20, 250),
            Size = new Size(450, 25),
            AccessibleName = "Announce crossing taxiways",
            AccessibleDescription = "When enabled, taxi guidance announces named taxiways being crossed at intersections. Disable for quieter taxiing."
        };

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

        // Separate ground-speed cadence used WHILE TAKEOFF ASSIST IS ACTIVE. The global
        // GS announcer already covers the takeoff roll using the taxi interval; this lets
        // the pilot pick a COARSER cadence (or silence) on the roll so the callouts don't
        // crowd out the centerline-deviation announcements. "Same as taxi" (default) keeps
        // today's behaviour; "Off" silences the roll; 5 / 10 override the taxi interval.
        takeoffGsAnnounceLabel = new Label
        {
            Text = "During takeoff assist, announce ground speed every:",
            Location = new Point(20, 320),
            Size = new Size(300, 20),
            AccessibleName = "Takeoff assist ground speed announcement interval Label"
        };
        takeoffGsAnnounceCombo = new ComboBox
        {
            Location = new Point(320, 318),
            Size = new Size(150, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Takeoff assist ground speed announcement interval",
            AccessibleDescription = "Choose how often ground speed is announced during the takeoff roll. Same as taxi uses the taxi interval; Off disables ground speed callouts while takeoff assist is active; or pick a 5 or 10 knot interval."
        };
        takeoffGsAnnounceCombo.Items.AddRange(new object[]
        {
            "Same as taxi (default)",
            "Off",
            "Every 5 knots",
            "Every 10 knots"
        });

        // App-wide distance unit toggle: feet or metres
        useFeetForDistancesCheckBox = new CheckBox
        {
            Text = "Use feet for distances (default: metres)",
            Location = new Point(20, 355),
            Size = new Size(450, 25),
            AccessibleName = "Use feet for distances",
            AccessibleDescription = "When enabled, all ground distances (taxi guidance, runway exits, parking) are announced in feet. Default is metres."
        };

        // Ground traffic distance unit: metres or feet (INDEPENDENT of the app-wide Distance units setting)
        useMetresCheckBox = new CheckBox
        {
            Text = "Show ground traffic distances in metres (default: feet)",
            Location = new Point(20, 390),
            Size = new Size(450, 25),
            AccessibleName = "Show ground traffic distances in metres",
            AccessibleDescription = "When enabled, proximity alert distances (e.g. 'Traffic ahead, 100 metres') are in metres. Default is feet, which matches aviation conventions. Independent of the app-wide distance units setting."
        };

        // GSX auto-select gate on route calculation. When checked, calculating
        // a taxi route to a gate also drives the GSX menu to select that gate
        // so the marshaller/VDGS arms there — only when GSX is running.
        gsxAutoSelectGateCheckBox = new CheckBox
        {
            Text = "Auto-select arrival gate in GSX on route calculation",
            Location = new Point(20, 420),
            Size = new Size(450, 25),
            AccessibleName = "Auto-select arrival gate in GSX on route calculation",
            AccessibleDescription = "When checked, calculating a taxi route to a gate also drives the GSX menu to select that gate so the marshaller and VDGS arm there automatically. Only fires when GSX is running."
        };

        // --- Docking guidance section ---
        dockingGroup = new GroupBox
        {
            Text = "Docking guidance",
            Location = new Point(20, 455),
            Size = new Size(450, 210),
            AccessibleName = "Docking guidance"
        };

        // Docking enabled checkbox
        dockingEnabledCheckBox = new CheckBox
        {
            Text = "Docking guidance (auto-engage at the gate)",
            Location = new Point(15, 25),
            Size = new Size(420, 25),
            AccessibleName = "Docking guidance",
            AccessibleDescription = "When enabled, audio docking guidance (steering tone, proximity beep, and spoken distance) auto-engages as you taxi onto your selected gate, guiding you to the stop position."
        };

        // Docking beep type label
        dockingBeepTypeLabel = new Label
        {
            Text = "Docking beep sound:",
            Location = new Point(15, 60),
            Size = new Size(230, 20),
            AccessibleName = "Docking beep sound Label"
        };

        // Docking beep type combo
        dockingBeepTypeCombo = new ComboBox
        {
            Location = new Point(255, 58),
            Size = new Size(175, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Docking beep sound",
            AccessibleDescription = "Select the audio wave type for the docking proximity beep"
        };
        dockingBeepTypeCombo.Items.AddRange(new object[]
        {
            "Sine (Smoothest)",
            "Triangle (Smooth)",
            "Sawtooth (Bright)",
            "Sine (Rich)"
        });

        // Docking beep volume label
        dockingBeepVolumeLabel = new Label
        {
            Text = "Docking beep volume:",
            Location = new Point(15, 100),
            Size = new Size(100, 20),
            AccessibleName = "Docking beep volume Label"
        };

        // Docking beep volume trackbar
        dockingBeepVolumeTrackBar = new TrackBar
        {
            Location = new Point(115, 95),
            Size = new Size(280, 45),
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            AccessibleName = "Docking Beep Volume Level",
            AccessibleDescription = "Adjust the docking proximity beep volume from 0 to 100 percent"
        };
        dockingBeepVolumeTrackBar.ValueChanged += (s, e) =>
            dockingBeepVolumeValueLabel.Text = $"{dockingBeepVolumeTrackBar.Value}%";

        // Docking beep volume value label
        dockingBeepVolumeValueLabel = new Label
        {
            Text = $"{dockingBeepVolumeTrackBar.Value}%",
            Location = new Point(405, 100),
            Size = new Size(40, 20),
            AccessibleName = "Docking Beep Volume Value",
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Docking beep test button
        dockingBeepTestButton = new Button
        {
            Text = "Test",
            Location = new Point(15, 150),
            Size = new Size(120, 35),
            AccessibleName = "Test docking beep",
            AccessibleDescription = "Play a preview of the docking proximity beep at the selected sound and volume"
        };
        dockingBeepTestButton.Click += DockingBeepTestButton_Click;

        dockingGroup.Controls.AddRange(new Control[]
        {
            dockingEnabledCheckBox,
            dockingBeepTypeLabel, dockingBeepTypeCombo,
            dockingBeepVolumeLabel, dockingBeepVolumeTrackBar, dockingBeepVolumeValueLabel,
            dockingBeepTestButton
        });

        // Refresh Taxiway Names Button — manual refresh, only wired when callback is provided
        refreshTaxiwayNamesButton = new Button
        {
            Text = "Refresh Taxiway Names",
            Location = new Point(20, 680),
            Size = new Size(200, 35),
            Enabled = _onRefreshTaxiwayNames != null,
            AccessibleName = "Refresh Taxiway Names",
            AccessibleDescription = "Download fresh taxiway-name data for the nearest airport and announce when complete"
        };
        refreshTaxiwayNamesButton.Click += RefreshTaxiwayNamesButton_Click;

        // Online taxiway/gate-name augmentation enable toggle.
        taxiAugmentEnabledCheckBox = new CheckBox
        {
            Text = "Online taxiway and gate names (OpenStreetMap + X-Plane)",
            Location = new Point(20, 720),
            Size = new Size(450, 25),
            AccessibleName = "Online taxiway and gate names",
            AccessibleDescription = "When enabled, fetches real-world taxiway and gate names from OpenStreetMap and the X-Plane Scenery Gateway to enrich your navdata, on demand for departure and destination. Disable to use navdata names only with no online requests. Applies immediately."
        };

        // ODbL / source attribution (required for OSM-derived data).
        taxiAugmentAttributionLabel = new Label
        {
            Text = "Online names: © OpenStreetMap contributors (ODbL) + X-Plane Scenery Gateway.",
            Location = new Point(20, 747),
            Size = new Size(450, 30),
            AccessibleName = "Online taxiway name data attribution"
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
            takeoffGsAnnounceLabel, takeoffGsAnnounceCombo,
            useFeetForDistancesCheckBox,
            useMetresCheckBox,
            gsxAutoSelectGateCheckBox,
            dockingGroup,
            refreshTaxiwayNamesButton,
            taxiAugmentEnabledCheckBox, taxiAugmentAttributionLabel
        });
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
        takeoffGsAnnounceCombo.TabIndex = tabIdx++;
        useFeetForDistancesCheckBox.TabIndex = tabIdx++;
        useMetresCheckBox.TabIndex = tabIdx++;
        gsxAutoSelectGateCheckBox.TabIndex = tabIdx++;
        dockingGroup.TabIndex = tabIdx++;
        dockingEnabledCheckBox.TabIndex = 0;
        dockingBeepTypeCombo.TabIndex = 1;
        dockingBeepVolumeTrackBar.TabIndex = 2;
        dockingBeepTestButton.TabIndex = 3;
        refreshTaxiwayNamesButton.TabIndex = tabIdx++;
        taxiAugmentEnabledCheckBox.TabIndex = tabIdx++;
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
            var waveType = (HandFlyWaveType)toneTypeCombo.SelectedIndex;
            double volume = volumeTrackBar.Value / 100.0;

            testToneGenerator = new AudioToneGenerator();
            testToneGenerator.Start(waveType, volume, 440);

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

    /// <summary>Stops and disposes the steering test-tone generator. Idempotent and
    /// non-throwing — safe to call whether or not a tone is currently playing.</summary>
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

    private async void DockingBeepTestButton_Click(object? sender, EventArgs e)
    {
        StopDockingBeepTest();

        dockingBeepTestButton.Enabled = false;
        var cts = new CancellationTokenSource();
        _dockingBeepTestCts = cts;
        var beeper = new ProximityBeeper();
        _dockingBeepTester = beeper;
        try
        {
            var wf = (HandFlyWaveType)dockingBeepTypeCombo.SelectedIndex;
            double vol = dockingBeepVolumeTrackBar.Value / 100.0;
            beeper.Start(wf, vol);
            // ramp 25 m -> 1 m over ~2.5 s so the acceleration + solid are audible
            for (int i = 0; i <= 24; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                double d = 25.0 - i; // 25 .. 1
                beeper.Update(d, active: true);
                await Task.Delay(100, cts.Token);
            }
            beeper.Update(0.3, active: true); // solid
            await Task.Delay(500, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Stopped early via OnLeaving/Dispose — not an error.
        }
        catch { }
        finally
        {
            try { beeper.Stop(); beeper.Dispose(); } catch { }
            if (ReferenceEquals(_dockingBeepTester, beeper))
                _dockingBeepTester = null;
            if (ReferenceEquals(_dockingBeepTestCts, cts))
                _dockingBeepTestCts = null;
            cts.Dispose();
            if (!IsDisposed)
                dockingBeepTestButton.Enabled = true;
        }
    }

    /// <summary>Stops and disposes the docking-beep test (cancels the running ramp loop if
    /// one is in flight). Idempotent and non-throwing.</summary>
    private void StopDockingBeepTest()
    {
        try
        {
            _dockingBeepTestCts?.Cancel();
        }
        catch
        {
            // Non-throwing by contract.
        }
        try
        {
            _dockingBeepTester?.Stop();
            _dockingBeepTester?.Dispose();
        }
        catch
        {
            // Non-throwing by contract.
        }
        finally
        {
            _dockingBeepTester = null;
        }
    }

    private async void RefreshTaxiwayNamesButton_Click(object? sender, EventArgs e)
    {
        if (_onRefreshTaxiwayNames == null)
            return;

        refreshTaxiwayNamesButton.Enabled = false;
        try
        {
            await _onRefreshTaxiwayNames();
        }
        finally
        {
            if (!IsDisposed)
                refreshTaxiwayNamesButton.Enabled = true;
        }
    }

    public void LoadFrom(UserSettings settings)
    {
        toneTypeCombo.SelectedIndex = (int)settings.TaxiGuidanceToneWaveform;
        volumeTrackBar.Value = (int)(settings.TaxiGuidanceToneVolume * 100);
        volumeValueLabel.Text = $"{volumeTrackBar.Value}%";

        invertToneCheckBox.Checked = settings.TaxiGuidanceInvertSteeringTone;
        hardPanToneCheckBox.Checked = settings.TaxiGuidanceHardPanTone;
        announceCrossingsCheckBox.Checked = settings.TaxiGuidanceAnnounceCrossings;

        // Map the stored interval back to the combo index. Anything other than 5 or 10 → Off.
        gsAnnounceCombo.SelectedIndex = settings.TaxiGuidanceGroundSpeedAnnounceInterval switch
        {
            5 => 1,
            10 => 2,
            _ => 0,
        };

        // Map the stored interval back to the combo index. -1 = same as taxi (default),
        // 0 = off, 5 / 10 = explicit cadence; anything unexpected loads as same-as-taxi.
        takeoffGsAnnounceCombo.SelectedIndex = settings.TakeoffAssistGroundSpeedAnnounceInterval switch
        {
            0 => 1,
            5 => 2,
            10 => 3,
            _ => 0,
        };

        useFeetForDistancesCheckBox.Checked = settings.GroundDistanceUnit == DistanceUnit.Feet;
        useMetresCheckBox.Checked = settings.GroundTrafficUseMetres;
        gsxAutoSelectGateCheckBox.Checked = settings.GsxAutoSelectGateOnRoute;

        dockingEnabledCheckBox.Checked = settings.DockingGuidanceEnabled;
        dockingBeepTypeCombo.SelectedIndex = (int)settings.DockingBeepWaveform;
        dockingBeepVolumeTrackBar.Value = (int)(settings.DockingBeepVolume * 100);
        dockingBeepVolumeValueLabel.Text = $"{dockingBeepVolumeTrackBar.Value}%";

        taxiAugmentEnabledCheckBox.Checked = settings.TaxiAugmentEnabled;
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.TaxiGuidanceToneWaveform = (HandFlyWaveType)toneTypeCombo.SelectedIndex;
        settings.TaxiGuidanceToneVolume = volumeTrackBar.Value / 100.0;
        settings.TaxiGuidanceInvertSteeringTone = invertToneCheckBox.Checked;
        settings.TaxiGuidanceHardPanTone = hardPanToneCheckBox.Checked;
        settings.TaxiGuidanceAnnounceCrossings = announceCrossingsCheckBox.Checked;

        settings.TaxiGuidanceGroundSpeedAnnounceInterval = gsAnnounceCombo.SelectedIndex switch
        {
            1 => 5,
            2 => 10,
            _ => 0,
        };
        settings.TakeoffAssistGroundSpeedAnnounceInterval = takeoffGsAnnounceCombo.SelectedIndex switch
        {
            1 => 0,
            2 => 5,
            3 => 10,
            _ => -1,
        };

        settings.GroundDistanceUnit = useFeetForDistancesCheckBox.Checked ? DistanceUnit.Feet : DistanceUnit.Metres;
        settings.GroundTrafficUseMetres = useMetresCheckBox.Checked;
        settings.GsxAutoSelectGateOnRoute = gsxAutoSelectGateCheckBox.Checked;

        settings.DockingGuidanceEnabled = dockingEnabledCheckBox.Checked;
        settings.DockingBeepWaveform = (HandFlyWaveType)dockingBeepTypeCombo.SelectedIndex;
        settings.DockingBeepVolume = dockingBeepVolumeTrackBar.Value / 100.0;

        settings.TaxiAugmentEnabled = taxiAugmentEnabledCheckBox.Checked;
    }

    /// <summary>Stops both the steering test tone and the docking-beep test whenever this tab
    /// is left (tab switch or dialog close on any path — OK, Cancel, or the [X] button), and
    /// resets the steering Test Tone button's caption back to idle so re-entering the tab
    /// never shows a stale "Stop Test". Idempotent and non-throwing.</summary>
    public void OnLeaving()
    {
        StopTestTone();
        testToneButton.Text = "Test Tone";
        StopDockingBeepTest();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopTestTone();
            StopDockingBeepTest();
        }
        base.Dispose(disposing);
    }
}
