using FBWBA.Accessibility;
using FBWBA.SimConnect;

namespace FBWBA.Forms;

public partial class NavigationDisplayForm : Form
{
        // Windows API declarations for focus management
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TextBox navigationTextBox = null!;
        private Button refreshButton = null!;
        private Button closeButton = null!;
        private Label titleLabel = null!;
        private GroupBox controlsGroupBox = null!;
        private RadioButton roseIlsButton = null!;
        private RadioButton roseVorButton = null!;
        private RadioButton roseNavButton = null!;
        private RadioButton arcButton = null!;
        private RadioButton planButton = null!;
        private ComboBox rangeComboBox = null!;
        private Label rangeLabel = null!;
        private Button cstrButton = null!;
        private Button wptButton = null!;
        private Button vordButton = null!;
        private Button ndbButton = null!;
        private Button arptButton = null!;

        private readonly ScreenReaderAnnouncer _announcer;
        private readonly SimConnectManager _simConnectManager = null!;
        private Dictionary<string, double> _variableValues = new Dictionary<string, double>();
        private readonly IntPtr previousWindow;

        public NavigationDisplayForm(ScreenReaderAnnouncer announcer, SimConnectManager simConnectManager)
        {
            // Capture the current foreground window (likely the simulator)
            previousWindow = GetForegroundWindow();

            _announcer = announcer;
            _simConnectManager = simConnectManager;
            InitializeComponent();
            SetupAccessibility();

            // Subscribe to SimVar updates
            if (_simConnectManager != null)
            {
                _simConnectManager.SimVarUpdated += OnSimVarUpdated;
            }

            RefreshNavigationData(); // Load initial data
        }

        private void InitializeComponent()
        {
            Text = "Navigation Display";
            Size = new Size(900, 700);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Title Label
            titleLabel = new Label
            {
                Text = "Navigation Display Information",
                Location = new Point(20, 20),
                Size = new Size(400, 20),
                Font = new Font("Microsoft Sans Serif", 10, FontStyle.Bold),
                AccessibleName = "Navigation Display Title"
            };

            // Navigation TextBox (read-only, multiline)
            navigationTextBox = new TextBox
            {
                Location = new Point(20, 50),
                Size = new Size(840, 400),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = "Navigation Display Information",
                AccessibleDescription = "Navigation display data including waypoint, deviation, and settings",
                Font = new Font("Consolas", 10, FontStyle.Regular),
                Text = "Loading navigation data..."
            };

            // Controls GroupBox
            controlsGroupBox = new GroupBox
            {
                Text = "ND Controls",
                Location = new Point(20, 460),
                Size = new Size(840, 140),
                AccessibleName = "Navigation Display Controls"
            };

            // Mode Radio Buttons
            int modeButtonY = 25;
            roseIlsButton = new RadioButton
            {
                Text = "ROSE &ILS",
                Location = new Point(15, modeButtonY),
                Size = new Size(150, 25),
                AccessibleName = "ROSE ILS Mode",
                AccessibleDescription = "Set ND mode to ROSE ILS"
            };
            roseIlsButton.CheckedChanged += ModeButton_CheckedChanged;

            roseVorButton = new RadioButton
            {
                Text = "ROSE &VOR",
                Location = new Point(180, modeButtonY),
                Size = new Size(150, 25),
                AccessibleName = "ROSE VOR Mode",
                AccessibleDescription = "Set ND mode to ROSE VOR"
            };
            roseVorButton.CheckedChanged += ModeButton_CheckedChanged;

            roseNavButton = new RadioButton
            {
                Text = "ROSE &NAV",
                Location = new Point(345, modeButtonY),
                Size = new Size(150, 25),
                AccessibleName = "ROSE NAV Mode",
                AccessibleDescription = "Set ND mode to ROSE NAV"
            };
            roseNavButton.CheckedChanged += ModeButton_CheckedChanged;

            arcButton = new RadioButton
            {
                Text = "&ARC",
                Location = new Point(510, modeButtonY),
                Size = new Size(150, 25),
                AccessibleName = "ARC Mode",
                AccessibleDescription = "Set ND mode to ARC"
            };
            arcButton.CheckedChanged += ModeButton_CheckedChanged;

            planButton = new RadioButton
            {
                Text = "&PLAN",
                Location = new Point(675, modeButtonY),
                Size = new Size(150, 25),
                AccessibleName = "PLAN Mode",
                AccessibleDescription = "Set ND mode to PLAN"
            };
            planButton.CheckedChanged += ModeButton_CheckedChanged;

            // Range Label and ComboBox
            rangeLabel = new Label
            {
                Text = "ND &Range:",
                Location = new Point(15, modeButtonY + 40),
                Size = new Size(100, 20),
                AccessibleName = "Range Label"
            };

            rangeComboBox = new ComboBox
            {
                Location = new Point(120, modeButtonY + 37),
                Size = new Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = "ND Range",
                AccessibleDescription = "Select navigation display range"
            };
            rangeComboBox.Items.AddRange(new object[] { "10 NM", "20 NM", "40 NM", "80 NM", "160 NM", "320 NM" });
            rangeComboBox.SelectedIndexChanged += RangeComboBox_SelectedIndexChanged;

            // Add controls to group box
            controlsGroupBox.Controls.AddRange(new Control[]
            {
                roseIlsButton, roseVorButton, roseNavButton, arcButton, planButton,
                rangeLabel, rangeComboBox
            });

            // Refresh Button
            refreshButton = new Button
            {
                Text = "&Refresh (F5)",
                Location = new Point(700, 620),
                Size = new Size(75, 30),
                AccessibleName = "Refresh",
                AccessibleDescription = "Refresh navigation data from simulator"
            };
            refreshButton.Click += RefreshButton_Click;

            // Close Button
            closeButton = new Button
            {
                Text = "&Close",
                Location = new Point(785, 620),
                Size = new Size(75, 30),
                DialogResult = DialogResult.OK,
                AccessibleName = "Close",
                AccessibleDescription = "Close navigation display window"
            };
            closeButton.Click += CloseButton_Click;

            // EFIS Control Buttons (at bottom, after Close in tab order)
            cstrButton = new Button
            {
                Text = "C&STR",
                Location = new Point(20, 660),
                Size = new Size(60, 30),
                AccessibleName = "CSTR",
                AccessibleDescription = "Toggle constraints display on ND"
            };
            cstrButton.Click += CstrButton_Click;

            wptButton = new Button
            {
                Text = "&WPT",
                Location = new Point(90, 660),
                Size = new Size(60, 30),
                AccessibleName = "WPT",
                AccessibleDescription = "Toggle waypoints display on ND"
            };
            wptButton.Click += WptButton_Click;

            vordButton = new Button
            {
                Text = "VOR &D",
                Location = new Point(160, 660),
                Size = new Size(70, 30),
                AccessibleName = "VOR D",
                AccessibleDescription = "Toggle VOR/DME display on ND"
            };
            vordButton.Click += VordButton_Click;

            ndbButton = new Button
            {
                Text = "&NDB",
                Location = new Point(240, 660),
                Size = new Size(60, 30),
                AccessibleName = "NDB",
                AccessibleDescription = "Toggle NDB display on ND"
            };
            ndbButton.Click += NdbButton_Click;

            arptButton = new Button
            {
                Text = "&ARPT",
                Location = new Point(310, 660),
                Size = new Size(60, 30),
                AccessibleName = "ARPT",
                AccessibleDescription = "Toggle airports display on ND"
            };
            arptButton.Click += ArptButton_Click;

            // Add controls to form
            Controls.AddRange(new Control[]
            {
                titleLabel, navigationTextBox, controlsGroupBox, refreshButton, closeButton,
                cstrButton, wptButton, vordButton, ndbButton, arptButton
            });

            CancelButton = closeButton;
            KeyPreview = true;
        }

        private void SetupAccessibility()
        {
            // Set tab order for logical navigation
            navigationTextBox.TabIndex = 0;
            controlsGroupBox.TabIndex = 1;
            roseIlsButton.TabIndex = 0;
            roseVorButton.TabIndex = 1;
            roseNavButton.TabIndex = 2;
            arcButton.TabIndex = 3;
            planButton.TabIndex = 4;
            rangeLabel.TabIndex = 5;
            rangeComboBox.TabIndex = 6;
            refreshButton.TabIndex = 2;
            closeButton.TabIndex = 3;
            // EFIS buttons come after Close in tab order
            cstrButton.TabIndex = 4;
            wptButton.TabIndex = 5;
            vordButton.TabIndex = 6;
            ndbButton.TabIndex = 7;
            arptButton.TabIndex = 8;

            // Focus and bring window to front when opened
            Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false; // Flash to bring to front
                navigationTextBox.Focus();
            };
        }

        private void ModeButton_CheckedChanged(object? sender, EventArgs e)
        {
            RadioButton? button = sender as RadioButton;
            if (button == null || !button.Checked) return;

            // Determine mode value
            int modeValue = -1;
            if (button == roseIlsButton) modeValue = 0;
            else if (button == roseVorButton) modeValue = 1;
            else if (button == roseNavButton) modeValue = 2;
            else if (button == arcButton) modeValue = 3;
            else if (button == planButton) modeValue = 4;

            if (modeValue >= 0)
            {
                // Write to L:var
                _simConnectManager?.SetLVar("A32NX_FCU_EFIS_L_EFIS_MODE", modeValue);
                _announcer?.Announce($"ND mode set to {button.Text.Replace("&", "")}");
            }
        }

        private void RangeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (rangeComboBox.SelectedIndex >= 0)
            {
                // Write to L:var
                _simConnectManager?.SetLVar("A32NX_FCU_EFIS_L_EFIS_RANGE", rangeComboBox.SelectedIndex);
                _announcer?.Announce($"ND range set to {rangeComboBox.SelectedItem}");
            }
        }

        private void RefreshButton_Click(object? sender, EventArgs e)
        {
            RefreshNavigationData();
            _announcer?.Announce("Navigation data refreshed");
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            Close();
        }

        private void CstrButton_Click(object? sender, EventArgs e)
        {
            _simConnectManager?.SendEvent("A32NX.FCU_EFIS_L_CSTR_PUSH");
            _announcer?.Announce("CSTR toggled");
        }

        private void WptButton_Click(object? sender, EventArgs e)
        {
            _simConnectManager?.SendEvent("A32NX.FCU_EFIS_L_WPT_PUSH");
            _announcer?.Announce("WPT toggled");
        }

        private void VordButton_Click(object? sender, EventArgs e)
        {
            _simConnectManager?.SendEvent("A32NX.FCU_EFIS_L_VORD_PUSH");
            _announcer?.Announce("VOR D toggled");
        }

        private void NdbButton_Click(object? sender, EventArgs e)
        {
            _simConnectManager?.SendEvent("A32NX.FCU_EFIS_L_NDB_PUSH");
            _announcer?.Announce("NDB toggled");
        }

        private void ArptButton_Click(object? sender, EventArgs e)
        {
            _simConnectManager?.SendEvent("A32NX.FCU_EFIS_L_ARPT_PUSH");
            _announcer?.Announce("ARPT toggled");
        }

        private async void RefreshNavigationData()
        {
            try
            {
                navigationTextBox.Text = "Loading navigation data...";

                // Request all navigation variables
                RequestAllNavigationVariables();

                // Wait for updates
                await System.Threading.Tasks.Task.Delay(500);

                string navData = FormatNavigationData();
                navigationTextBox.Text = navData;

                // Update control states to match current mode/range
                UpdateControlStates();
            }
            catch (Exception ex)
            {
                navigationTextBox.Text = $"Error loading navigation data: {ex.Message}";
            }
        }

        private string FormatNavigationData()
        {
            var data = new System.Text.StringBuilder();

            data.AppendLine("NAVIGATION DISPLAY");

            // Active Waypoint Section
            data.AppendLine("Active Waypoint");
            string waypointName = UnpackWaypointName(
                GetVariableRawValue("A32NX_EFIS_L_TO_WPT_IDENT_0"),
                GetVariableRawValue("A32NX_EFIS_L_TO_WPT_IDENT_1")
            );

            if (string.IsNullOrWhiteSpace(waypointName))
            {
                data.AppendLine("No active waypoint (PPOS - Present Position)");
            }
            else
            {
                data.AppendLine($"Name: {waypointName}");
                double distance = GetVariableRawValue("A32NX_EFIS_L_TO_WPT_DISTANCE");
                data.AppendLine($"Distance: {distance:0.0} NM");

                // Note: Values are in radians, need to convert to degrees
                double bearingRadians = GetVariableRawValue("A32NX_EFIS_L_TO_WPT_BEARING");
                double trueBearingRadians = GetVariableRawValue("A32NX_EFIS_L_TO_WPT_TRUE_BEARING");

                // Convert from radians to degrees
                double bearing = bearingRadians * (180.0 / Math.PI);
                double trueBearing = trueBearingRadians * (180.0 / Math.PI);

                data.AppendLine($"Bearing: {bearing:0}° (Magnetic)");
                data.AppendLine($"True Bearing: {trueBearing:0}° (True)");
                double eta = GetVariableRawValue("A32NX_EFIS_L_TO_WPT_ETA");
                string etaFormatted = FormatEtaDuration(eta);
                data.AppendLine($"ETA (UTC): {etaFormatted}");
            }

            // Navigation Status Section
            data.AppendLine("Navigation Status");

            // Get current mode to determine which deviation to show
            double currentMode = GetVariableRawValue("A32NX_EFIS_L_ND_MODE");

            if (currentMode == 0 || currentMode == 1) // ROSE ILS or ROSE VOR
            {
                // Show ILS deviation
                double locIsValid = GetVariableRawValue("A32NX_RADIO_RECEIVER_LOC_IS_VALID");
                double locDev = GetVariableRawValue("A32NX_RADIO_RECEIVER_LOC_DEVIATION");
                data.AppendLine($"Localizer: {FormatLocalizerDeviation(locDev, locIsValid > 0.5)}");

                if (currentMode == 0) // ROSE ILS has glideslope
                {
                    double gsIsValid = GetVariableRawValue("A32NX_RADIO_RECEIVER_GS_IS_VALID");
                    double gsDev = GetVariableRawValue("A32NX_RADIO_RECEIVER_GS_DEVIATION");
                    data.AppendLine($"Glideslope: {FormatGlideslopeDeviation(gsDev, gsIsValid > 0.5)}");
                }
            }
            else // ROSE NAV, ARC, PLAN
            {
                // Show cross track error
                double xte = GetVariableRawValue("A32NX_FG_CROSS_TRACK_ERROR");
                data.AppendLine($"Cross Track: {FormatCrossTrackError(xte)}");
            }

            double rnp = GetVariableRawValue("A32NX_FMGC_L_RNP");
            if (rnp > 0)
            {
                data.AppendLine($"RNP: {rnp:0.00}");
            }
            else
            {
                data.AppendLine("RNP: N/A");
            }

            // FM Messages Section
            data.AppendLine("FM Messages");
            double fmFlags = GetVariableRawValue("A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS");
            var fmMessages = DecodeFmMessageFlags((int)fmFlags);
            if (fmMessages.Count > 0)
            {
                foreach (var message in fmMessages)
                {
                    data.AppendLine(message);
                }
            }
            else
            {
                data.AppendLine("No active FM messages");
            }

            // Approach Section
            data.AppendLine("Approach");
            string apprMsg = UnpackApproachMessage(
                GetVariableRawValue("A32NX_EFIS_L_APPR_MSG_0"),
                GetVariableRawValue("A32NX_EFIS_L_APPR_MSG_1")
            );

            if (string.IsNullOrWhiteSpace(apprMsg))
            {
                data.AppendLine("Status: No approach active");
            }
            else
            {
                data.AppendLine($"Status: {apprMsg}");
            }

            // Vertical Navigation Section
            data.AppendLine("Vertical Navigation");
            double fcuAlt = GetVariableRawValue("A32NX_FCU_AFS_DISPLAY_ALT_VALUE");
            if (fcuAlt > 0)
            {
                data.AppendLine($"FCU Selected Altitude: {fcuAlt:0} ft");
            }
            else
            {
                data.AppendLine("FCU Selected Altitude: N/A");
            }

            double profileLatched = GetVariableRawValue("A32NX_PFD_VERTICAL_PROFILE_LATCHED");
            data.AppendLine($"Profile: {(profileLatched > 0.5 ? "Latched" : "Not Latched")}");

            double linearDev = GetVariableRawValue("A32NX_PFD_LINEAR_DEVIATION_ACTIVE");
            data.AppendLine($"Linear Deviation: {(linearDev > 0.5 ? "Active" : "Not Active")}");

            // Display ND mode and range at the bottom
            string modeText = GetNdModeText((int)currentMode);
            data.AppendLine($"ND Mode: {modeText}");

            double currentRange = GetVariableRawValue("A32NX_EFIS_L_ND_RANGE");
            string rangeText = GetNdRangeText((int)currentRange);
            data.AppendLine($"ND Range: {rangeText}");

            data.AppendLine("Press F5 to refresh, Press ESC to close");

            return data.ToString();
        }

        private void UpdateControlStates()
        {
            // Temporarily unsubscribe from events to prevent triggering writes to L:vars
            roseIlsButton.CheckedChanged -= ModeButton_CheckedChanged;
            roseVorButton.CheckedChanged -= ModeButton_CheckedChanged;
            roseNavButton.CheckedChanged -= ModeButton_CheckedChanged;
            arcButton.CheckedChanged -= ModeButton_CheckedChanged;
            planButton.CheckedChanged -= ModeButton_CheckedChanged;
            rangeComboBox.SelectedIndexChanged -= RangeComboBox_SelectedIndexChanged;

            // Update mode radio buttons
            double currentMode = GetVariableRawValue("A32NX_EFIS_L_ND_MODE");
            int modeInt = (int)currentMode;

            roseIlsButton.Checked = (modeInt == 0);
            roseVorButton.Checked = (modeInt == 1);
            roseNavButton.Checked = (modeInt == 2);
            arcButton.Checked = (modeInt == 3);
            planButton.Checked = (modeInt == 4);

            // Update range combobox
            double currentRange = GetVariableRawValue("A32NX_EFIS_L_ND_RANGE");
            int rangeInt = (int)currentRange;
            if (rangeInt >= 0 && rangeInt < rangeComboBox.Items.Count)
            {
                rangeComboBox.SelectedIndex = rangeInt;
            }

            // Re-subscribe to events
            roseIlsButton.CheckedChanged += ModeButton_CheckedChanged;
            roseVorButton.CheckedChanged += ModeButton_CheckedChanged;
            roseNavButton.CheckedChanged += ModeButton_CheckedChanged;
            arcButton.CheckedChanged += ModeButton_CheckedChanged;
            planButton.CheckedChanged += ModeButton_CheckedChanged;
            rangeComboBox.SelectedIndexChanged += RangeComboBox_SelectedIndexChanged;
        }

        // String Decoding Utilities
        private string UnpackWaypointName(double ident0, double ident1)
        {
            double[] values = { ident0, ident1 };
            string result = "";

            for (int i = 0; i < values.Length * 8; i++)
            {
                int word = i / 8;
                int charPos = i % 8;
                int code = (int)(values[word] / Math.Pow(2, charPos * 6)) & 0x3F;

                if (code > 0)
                {
                    result += (char)(code + 31);
                }
            }

            return result.Trim();
        }

        private string UnpackApproachMessage(double msg0, double msg1)
        {
            double[] values = { msg0, msg1 };
            string result = "";

            for (int i = 0; i < values.Length * 8; i++)
            {
                int word = i / 8;
                int charPos = i % 8;
                int code = (int)(values[word] / Math.Pow(2, charPos * 6)) & 0x3F;

                if (code > 0)
                {
                    result += (char)(code + 31);
                }
            }

            return result.Trim();
        }

        private string FormatEtaDuration(double etaSeconds)
        {
            if (etaSeconds <= 0)
            {
                return "N/A";
            }

            int hours = (int)(etaSeconds / 3600);
            int minutes = (int)((etaSeconds % 3600) / 60);
            int seconds = (int)(etaSeconds % 60);

            return $"{hours}:{minutes:D2}:{seconds:D2}";
        }

        private string GetNdModeText(int mode)
        {
            string[] modes = { "ROSE ILS", "ROSE VOR", "ROSE NAV", "ARC", "PLAN" };
            if (mode >= 0 && mode < modes.Length)
                return modes[mode];
            return "UNKNOWN";
        }

        private string GetNdRangeText(int range)
        {
            int[] ranges = { 10, 20, 40, 80, 160, 320 };
            if (range >= 0 && range < ranges.Length)
                return $"{ranges[range]} NM";
            return "UNKNOWN";
        }

        private string FormatCrossTrackError(double xteMeters)
        {
            // Convert meters to nautical miles (1 NM = 1852 meters)
            double xteNM = xteMeters / 1852.0;

            if (Math.Abs(xteNM) < 0.01)
            {
                return "On track";
            }
            string direction = xteMeters > 0 ? "right" : "left";
            return $"{Math.Abs(xteNM):0.00} NM {direction} of track";
        }

        private string FormatLocalizerDeviation(double locDev, bool isValid)
        {
            if (!isValid)
            {
                return "No localizer signal";
            }
            if (Math.Abs(locDev) < 0.05)
            {
                return "On localizer centerline";
            }
            string direction = locDev > 0 ? "right" : "left";
            return $"{Math.Abs(locDev):0.00}° {direction} of centerline";
        }

        private string FormatGlideslopeDeviation(double gsDev, bool isValid)
        {
            if (!isValid)
            {
                return "No glideslope signal";
            }
            if (Math.Abs(gsDev) < 0.05)
            {
                return "On glideslope";
            }
            string direction = gsDev > 0 ? "above" : "below";
            return $"{Math.Abs(gsDev):0.00}° {direction} glideslope";
        }

        private List<string> DecodeFmMessageFlags(int flags)
        {
            var messages = new List<string>();

            // Decode each bit according to FlyByWire documentation
            if ((flags & (1 << 0)) != 0) messages.Add("SELECT TRUE REF");
            if ((flags & (1 << 1)) != 0) messages.Add("CHECK NORTH REF");
            if ((flags & (1 << 2)) != 0) messages.Add("NAV ACCURACY DOWNGRADE");
            if ((flags & (1 << 3)) != 0) messages.Add("NAV ACCURACY UPGRADE NO GPS");
            if ((flags & (1 << 4)) != 0) messages.Add("SPECIFIED VOR DME UNAVAILABLE");
            if ((flags & (1 << 5)) != 0) messages.Add("NAV ACCURACY UPGRADE GPS");
            if ((flags & (1 << 6)) != 0) messages.Add("GPS PRIMARY");
            if ((flags & (1 << 7)) != 0) messages.Add("MAP PARTLY DISPLAYED");
            if ((flags & (1 << 8)) != 0) messages.Add("SET OFFSIDE RANGE MODE");
            if ((flags & (1 << 9)) != 0) messages.Add("OFFSIDE FM CONTROL");
            if ((flags & (1 << 10)) != 0) messages.Add("OFFSIDE FM WXR CONTROL");
            if ((flags & (1 << 11)) != 0) messages.Add("OFFSIDE WXR CONTROL");
            if ((flags & (1 << 12)) != 0) messages.Add("GPS PRIMARY LOST");
            if ((flags & (1 << 13)) != 0) messages.Add("RTA MISSED");
            if ((flags & (1 << 14)) != 0) messages.Add("BACKUP NAV");

            return messages;
        }

        private double GetVariableRawValue(string variableName)
        {
            try
            {
                if (_variableValues.ContainsKey(variableName))
                {
                    return _variableValues[variableName];
                }
                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private (bool isValid, double value) DecodeArinc429(double arincValue)
        {
            // ARINC 429 format: 32-bit word with SSM (Sign/Status Matrix) in bits 30-31
            // SSM values: 00=Failure, 01=NCD (No Computed Data), 10=Test, 11=Normal
            // Data is in bits 11-29 (19 bits)

            // Convert to 32-bit unsigned integer for bit manipulation
            uint arincWord = (uint)arincValue;

            // Extract SSM (bits 30-31) - shift right 29 positions and mask with 0x3 (binary 11)
            uint ssm = (arincWord >> 29) & 0x3;

            // Check if data is valid (SSM = 3 = 0b11 = Normal Operation)
            if (ssm != 3)
            {
                return (false, 0); // NCD, Failure, or Test mode - no valid constraint
            }

            // Extract data bits (bits 11-29, which is 19 bits)
            // Shift right 10 positions to skip label/SDI, mask with 0x7FFFF (19 bits of 1s)
            uint dataBits = (arincWord >> 10) & 0x7FFFF;

            // Convert to altitude (FlyByWire stores altitude directly in feet)
            double altitude = dataBits;

            return (true, altitude);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Handle F5 key for refresh
            if (keyData == Keys.F5)
            {
                RefreshNavigationData();
                _announcer?.Announce("Navigation data refreshed");
                return true;
            }

            // Handle Escape key
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
        {
            // Update our local variable values dictionary
            if (!string.IsNullOrEmpty(e.VarName))
            {
                _variableValues[e.VarName] = e.Value;
            }
        }

        private void RequestAllNavigationVariables()
        {
            if (_simConnectManager != null)
            {
                // Waypoint variables
                _simConnectManager.RequestVariable("A32NX_EFIS_L_TO_WPT_IDENT_0");
                _simConnectManager.RequestVariable("A32NX_EFIS_L_TO_WPT_IDENT_1");
                _simConnectManager.RequestVariable("A32NX_EFIS_L_TO_WPT_DISTANCE");
                _simConnectManager.RequestVariable("A32NX_EFIS_L_TO_WPT_BEARING");
                _simConnectManager.RequestVariable("A32NX_EFIS_L_TO_WPT_TRUE_BEARING");
                _simConnectManager.RequestVariable("A32NX_EFIS_L_TO_WPT_ETA");

                // Cross track error
                _simConnectManager.RequestVariable("A32NX_FG_CROSS_TRACK_ERROR");

                // ILS deviation
                _simConnectManager.RequestVariable("A32NX_RADIO_RECEIVER_LOC_IS_VALID");
                _simConnectManager.RequestVariable("A32NX_RADIO_RECEIVER_LOC_DEVIATION");
                _simConnectManager.RequestVariable("A32NX_RADIO_RECEIVER_GS_IS_VALID");
                _simConnectManager.RequestVariable("A32NX_RADIO_RECEIVER_GS_DEVIATION");

                // ND settings
                _simConnectManager.RequestVariable("A32NX_EFIS_L_ND_MODE");
                _simConnectManager.RequestVariable("A32NX_EFIS_L_ND_RANGE");

                // Navigation performance
                _simConnectManager.RequestVariable("A32NX_FMGC_L_RNP");

                // Approach messages
                _simConnectManager.RequestVariable("A32NX_EFIS_L_APPR_MSG_0");
                _simConnectManager.RequestVariable("A32NX_EFIS_L_APPR_MSG_1");

                // FM message flags
                _simConnectManager.RequestVariable("A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS");

                // Vertical navigation
                _simConnectManager.RequestVariable("A32NX_FCU_AFS_DISPLAY_ALT_VALUE");
                _simConnectManager.RequestVariable("A32NX_PFD_VERTICAL_PROFILE_LATCHED");
                _simConnectManager.RequestVariable("A32NX_PFD_LINEAR_DEVIATION_ACTIVE");
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Unsubscribe from events
            if (_simConnectManager != null)
            {
                _simConnectManager.SimVarUpdated -= OnSimVarUpdated;
            }
            base.OnFormClosed(e);

            // Restore focus to the previous window (likely the simulator)
            if (previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
        }
}
