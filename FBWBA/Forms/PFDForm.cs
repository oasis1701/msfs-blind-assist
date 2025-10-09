using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FBWBA.Accessibility;
using FBWBA.SimConnect;

namespace FBWBA.Forms
{
    public partial class PFDForm : Form
    {
        // Windows API declarations for focus management
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TextBox pfdTextBox;
        private Button refreshButton;
        private Button closeButton;
        private Label titleLabel;

        private readonly ScreenReaderAnnouncer _announcer;
        private readonly SimConnectManager _simConnectManager;
        private Dictionary<string, double> _variableValues = new Dictionary<string, double>();
        private readonly IntPtr previousWindow;

        public PFDForm(ScreenReaderAnnouncer announcer, SimConnectManager simConnectManager)
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

            RefreshPFDData(); // Load initial data
        }

        private void InitializeComponent()
        {
            Text = "PFD Window";
            Size = new Size(800, 600);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Title Label
            titleLabel = new Label
            {
                Text = "Primary Flight Display Information",
                Location = new Point(20, 20),
                Size = new Size(300, 20),
                Font = new Font("Microsoft Sans Serif", 10, FontStyle.Bold),
                AccessibleName = "PFD Title"
            };

            // PFD TextBox (read-only, multiline)
            pfdTextBox = new TextBox
            {
                Location = new Point(20, 50),
                Size = new Size(740, 450),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = "PFD Information",
                AccessibleDescription = "Primary Flight Display and FMA information from the aircraft",
                Font = new Font("Consolas", 10, FontStyle.Regular),
                Text = "Loading PFD data..."
            };

            // Refresh Button
            refreshButton = new Button
            {
                Text = "&Refresh",
                Location = new Point(600, 520),
                Size = new Size(75, 30),
                AccessibleName = "Refresh",
                AccessibleDescription = "Refresh PFD data from simulator"
            };
            refreshButton.Click += RefreshButton_Click;

            // Close Button
            closeButton = new Button
            {
                Text = "&Close",
                Location = new Point(685, 520),
                Size = new Size(75, 30),
                DialogResult = DialogResult.OK,
                AccessibleName = "Close",
                AccessibleDescription = "Close PFD window"
            };
            closeButton.Click += CloseButton_Click;

            // Add controls to form
            Controls.AddRange(new Control[]
            {
                titleLabel, pfdTextBox, refreshButton, closeButton
            });

            CancelButton = closeButton;
        }

        private void SetupAccessibility()
        {
            // Set tab order for logical navigation
            pfdTextBox.TabIndex = 0;
            refreshButton.TabIndex = 1;
            closeButton.TabIndex = 2;

            // Focus and bring window to front when opened
            Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false; // Flash to bring to front
                pfdTextBox.Focus();
            };
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            RefreshPFDData();
            _announcer?.Announce("PFD data refreshed");
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private async void RefreshPFDData()
        {
            try
            {
                pfdTextBox.Text = "Loading PFD data...";

                // First populate from cached values if available
                if (_simConnectManager != null && SimVarDefinitions.PanelControls.ContainsKey("PFD"))
                {
                    var pfdVariables = SimVarDefinitions.PanelControls["PFD"];
                    var cachedValues = _simConnectManager.GetCachedVariableSnapshot(pfdVariables);

                    foreach (var kvp in cachedValues)
                    {
                        _variableValues[kvp.Key] = kvp.Value;
                    }
                }

                // Then request fresh updates
                RequestAllPFDVariables();

                // Wait for any new updates
                await System.Threading.Tasks.Task.Delay(500);

                string pfdData = FormatPFDData();
                pfdTextBox.Text = pfdData;
            }
            catch (Exception ex)
            {
                pfdTextBox.Text = $"Error loading PFD data: {ex.Message}";
            }
        }

        private string FormatPFDData()
        {
            var data = new System.Text.StringBuilder();

            data.AppendLine("PRIMARY FLIGHT DISPLAY INFORMATION");
            data.AppendLine("=".PadRight(50, '='));
            data.AppendLine();

            data.AppendLine("THRUST & BRAKE SYSTEMS");
            data.AppendLine($"Autothrust Mode: {GetVariableValue("A32NX_AUTOTHRUST_MODE")}");
            data.AppendLine($"Autobrake Mode: {GetVariableValue("A32NX_AUTOBRAKES_ARMED_MODE")}");
            data.AppendLine($"A/THR Status: {GetVariableValue("A32NX_AUTOTHRUST_STATUS")}");
            data.AppendLine();

            data.AppendLine("VERTICAL FLIGHT MODE");
            data.AppendLine($"Active Vertical Mode: {GetVariableValue("A32NX_FMA_VERTICAL_MODE")}");
            string verticalArmed = GetArmedVerticalModes(GetVariableRawValue("A32NX_FMA_VERTICAL_ARMED"));
            data.AppendLine($"Armed Vertical Modes: {verticalArmed}");
            data.AppendLine($"Cruise Altitude Mode: {GetVariableValue("A32NX_FMA_CRUISE_ALT_MODE")}");
            data.AppendLine();

            data.AppendLine("LATERAL FLIGHT MODE");
            data.AppendLine($"Active Lateral Mode: {GetVariableValue("A32NX_FMA_LATERAL_MODE")}");
            string lateralArmed = GetArmedLateralModes(GetVariableRawValue("A32NX_FMA_LATERAL_ARMED"));
            data.AppendLine($"Armed Lateral Modes: {lateralArmed}");
            data.AppendLine();

            data.AppendLine("APPROACH & NAVIGATION");
            data.AppendLine($"Approach Capability: {GetVariableValue("A32NX_APPROACH_CAPABILITY")}");
            data.AppendLine($"Linear Deviation: {GetVariableValue("A32NX_PFD_LINEAR_DEVIATION_ACTIVE")}");
            data.AppendLine($"FMGC L/DEV Request: {GetVariableValue("A32NX_FMGC_1_LDEV_REQUEST")}");
            data.AppendLine();

            data.AppendLine("APPROACH SETTINGS");
            double mda = GetVariableRawValue("A32NX_FM1_MINIMUM_DESCENT_ALTITUDE");
            data.AppendLine($"Minimum Descent Altitude: {(mda > 0 ? mda + " ft" : "Not set")}");
            double qnh = GetVariableRawValue("A32NX_DESTINATION_QNH");
            data.AppendLine($"Destination QNH: {(qnh > 0 ? qnh + " mb" : "Not set")}");
            data.AppendLine();

            data.AppendLine("AUTOPILOT STATUS");
            data.AppendLine($"Autopilot: {GetAutopilotStatus()}");
            data.AppendLine();

            data.AppendLine("PFD MESSAGES");
            data.AppendLine($"SET HOLD SPEED: {GetVariableValue("A32NX_PFD_MSG_SET_HOLD_SPEED")}");
            data.AppendLine($"T/D REACHED: {GetVariableValue("A32NX_PFD_MSG_TD_REACHED")}");
            data.AppendLine($"CHECK SPEED MODE: {GetVariableValue("A32NX_PFD_MSG_CHECK_SPEED_MODE")}");
            data.AppendLine();

            data.AppendLine("FLIGHT DIRECTOR");
            data.AppendLine("FD indicators are not implemented, use OCR for this");
            data.AppendLine();

            data.AppendLine("ADDITIONAL NOTES");
            data.AppendLine("Special message annunciators not fully supported, use OCR in control+1 view for complete PFD messages");

            return data.ToString();
        }

        private string GetVariableValue(string variableName)
        {
            try
            {
                if (!SimVarDefinitions.Variables.ContainsKey(variableName))
                {
                    return "Variable not found";
                }

                var variable = SimVarDefinitions.Variables[variableName];
                double rawValue = GetVariableRawValue(variableName);

                if (variable.ValueDescriptions != null && variable.ValueDescriptions.ContainsKey(rawValue))
                {
                    return variable.ValueDescriptions[rawValue];
                }

                return rawValue.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
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

        private string GetAutopilotStatus()
        {
            try
            {
                double ap1 = GetVariableRawValue("A32NX_FCU_AP_1_LIGHT_ON");
                double ap2 = GetVariableRawValue("A32NX_FCU_AP_2_LIGHT_ON");

                if (ap1 > 0 && ap2 > 0)
                    return "AP1+2: Both Autopilots are active";
                else if (ap1 > 0)
                    return "AP1: Only Autopilot One is active";
                else if (ap2 > 0)
                    return "AP2: Only Autopilot Two is active";
                else
                    return "No Autopilot active";
            }
            catch
            {
                return "Error reading autopilot status";
            }
        }

        private string GetArmedLateralModes(double bitmask)
        {
            try
            {
                var modes = new List<string>();
                int intMask = (int)bitmask;

                // Check bit 0 for NAV
                if ((intMask & 1) != 0)
                    modes.Add("NAV");

                // Check bit 1 for LOC
                if ((intMask & 2) != 0)
                    modes.Add("LOC");

                return modes.Count > 0 ? string.Join(", ", modes) : "None";
            }
            catch
            {
                return "Error reading armed modes";
            }
        }

        private string GetArmedVerticalModes(double bitmask)
        {
            try
            {
                var modes = new List<string>();
                int intMask = (int)bitmask;

                // Check bits 0-6 for vertical modes
                if ((intMask & 1) != 0)     // Bit 0: ALT
                    modes.Add("ALT");
                if ((intMask & 2) != 0)     // Bit 1: ALT_CST
                    modes.Add("ALT_CST");
                if ((intMask & 4) != 0)     // Bit 2: CLB
                    modes.Add("CLB");
                if ((intMask & 8) != 0)     // Bit 3: DES
                    modes.Add("DES");
                if ((intMask & 16) != 0)    // Bit 4: GS
                    modes.Add("GS");
                if ((intMask & 32) != 0)    // Bit 5: FINAL
                    modes.Add("FINAL");
                if ((intMask & 64) != 0)    // Bit 6: TCAS
                    modes.Add("TCAS");

                return modes.Count > 0 ? string.Join(", ", modes) : "None";
            }
            catch
            {
                return "Error reading armed vertical modes";
            }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Handle Escape key
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        private void OnSimVarUpdated(object sender, SimVarUpdateEventArgs e)
        {
            // Update our local variable values dictionary
            if (!string.IsNullOrEmpty(e.VarName))
            {
                _variableValues[e.VarName] = e.Value;
            }
        }

        private void RequestAllPFDVariables()
        {
            if (_simConnectManager != null)
            {
                // Request only the specific PFD panel variables
                _simConnectManager.RequestPanelVariables("PFD", "PFD Refresh");
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
}