using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using FBWBA.Accessibility;
using FBWBA.SimConnect;

namespace FBWBA.Forms
{
    public partial class ECAMDisplayForm : Form
    {
        // Windows API declarations for focus management
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TextBox ecamTextBox;
        private Button refreshButton;
        private Button closeButton;
        private Label titleLabel;

        private readonly ScreenReaderAnnouncer _announcer;
        private readonly SimConnectManager _simConnectManager;
        private Dictionary<string, string> _messageLines = new Dictionary<string, string>();
        private bool _masterWarning = false;
        private bool _masterCaution = false;
        private bool _stallWarning = false;
        private bool _pack1On = false;
        private bool _pack2On = false;
        private double _togaThrustLimit = 0;

        // Engine parameters
        private double _engine1N1 = 0;
        private double _engine1N2 = 0;
        private double _engine1EGT = 0;
        private double _engine1FuelFlow = 0;
        private double _engine2N1 = 0;
        private double _engine2N2 = 0;
        private double _engine2EGT = 0;
        private double _engine2FuelFlow = 0;

        // Fuel quantity
        private double _fuelQuantity = 0;

        private readonly IntPtr previousWindow;

        public ECAMDisplayForm(ScreenReaderAnnouncer announcer, SimConnectManager simConnectManager)
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
                _simConnectManager.ECAMDataReceived += OnECAMDataReceived;
            }

            RefreshECAMData(); // Load initial data
        }

        private void InitializeComponent()
        {
            Text = "Upper ECAM Display";
            Size = new Size(800, 600);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Title Label
            titleLabel = new Label
            {
                Text = "Upper ECAM - FlyByWire A32NX",
                Location = new Point(20, 20),
                Size = new Size(400, 20),
                Font = new Font("Microsoft Sans Serif", 10, FontStyle.Bold),
                AccessibleName = "ECAM Display Title"
            };

            // ECAM TextBox (read-only, multiline)
            ecamTextBox = new TextBox
            {
                Location = new Point(20, 50),
                Size = new Size(740, 450),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = "ECAM Messages",
                AccessibleDescription = "Upper ECAM messages including warnings, cautions, and memos",
                Font = new Font("Consolas", 10, FontStyle.Regular),
                Text = "Loading ECAM data..."
            };

            // Refresh Button
            refreshButton = new Button
            {
                Text = "&Refresh (F5)",
                Location = new Point(600, 520),
                Size = new Size(75, 30),
                AccessibleName = "Refresh",
                AccessibleDescription = "Refresh ECAM data from simulator"
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
                AccessibleDescription = "Close ECAM window"
            };
            closeButton.Click += CloseButton_Click;

            // Add controls to form
            Controls.AddRange(new Control[]
            {
                titleLabel, ecamTextBox, refreshButton, closeButton
            });

            CancelButton = closeButton;
            KeyPreview = true;
        }

        private void SetupAccessibility()
        {
            // Set tab order for logical navigation
            ecamTextBox.TabIndex = 0;
            refreshButton.TabIndex = 1;
            closeButton.TabIndex = 2;

            // Focus and bring window to front when opened
            Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false; // Flash to bring to front
                ecamTextBox.Focus();
            };
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            RefreshECAMData();
            _announcer?.Announce("ECAM data refreshed");
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void RefreshECAMData()
        {
            try
            {
                // Show loading message
                ecamTextBox.Text = "Loading ECAM data...";

                // Request ECAM messages from SimConnect via MobiFlight
                // The OnECAMDataReceived event handler will update the UI when data arrives
                _simConnectManager?.RequestECAMMessages();

                // Request PACK and TOGA variables directly (like Warnings panel does)
                _simConnectManager?.RequestVariable("A32NX_OVHD_COND_PACK_1_PB_IS_ON");
                _simConnectManager?.RequestVariable("A32NX_OVHD_COND_PACK_2_PB_IS_ON");
                _simConnectManager?.RequestVariable("A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA");

                // Request engine parameters
                _simConnectManager?.RequestVariable("A32NX_ENGINE_N1:1");
                _simConnectManager?.RequestVariable("A32NX_ENGINE_N2:1");
                _simConnectManager?.RequestVariable("A32NX_ENGINE_EGT:1");
                _simConnectManager?.RequestVariable("A32NX_ENGINE_FF:1");
                _simConnectManager?.RequestVariable("A32NX_ENGINE_N1:2");
                _simConnectManager?.RequestVariable("A32NX_ENGINE_N2:2");
                _simConnectManager?.RequestVariable("A32NX_ENGINE_EGT:2");
                _simConnectManager?.RequestVariable("A32NX_ENGINE_FF:2");

                // Request fuel quantity
                _simConnectManager?.RequestFuelQuantity();

                System.Diagnostics.Debug.WriteLine("[ECAMDisplayForm] ECAM data requested, waiting for response");
            }
            catch (Exception ex)
            {
                ecamTextBox.Text = $"Error loading ECAM data: {ex.Message}";
            }
        }

        private string FormatECAMData()
        {
            var data = new System.Text.StringBuilder();

            // Show stall warning if active (critical safety information)
            if (_stallWarning)
            {
                data.AppendLine("[STALL WARNING: ACTIVE]");
            }

            // Show PACK status
            if (_pack1On)
            {
                data.AppendLine("PACK 1: ON");
            }
            if (_pack2On)
            {
                data.AppendLine("PACK 2: ON");
            }

            // Show TOGA thrust limit if available
            if (_togaThrustLimit > 0)
            {
                data.AppendLine($"TOGA THRUST LIMIT: {_togaThrustLimit:F1}% N1");
            }

            // Show engine parameters
            data.AppendLine();
            data.AppendLine("Engine Parameters");
            data.AppendLine($"Left Engine: N1: {_engine1N1:F1}%, N2: {_engine1N2:F1}%, EGT: {_engine1EGT:F0}°C, Fuel Flow: {_engine1FuelFlow:F0} Kg/h");
            data.AppendLine($"Right Engine: N1: {_engine2N1:F1}%, N2: {_engine2N2:F1}%, EGT: {_engine2EGT:F0}°C, Fuel Flow: {_engine2FuelFlow:F0} Kg/h");
            data.AppendLine($"FOB: {_fuelQuantity:F0} KG");

            // Add separator before ECAM messages
            data.AppendLine();

            // Collect and display left side messages
            bool hasMessages = false;

            for (int i = 1; i <= 7; i++)
            {
                string leftKey = $"LEFT_LINE_{i}";
                if (_messageLines.ContainsKey(leftKey) && !string.IsNullOrWhiteSpace(_messageLines[leftKey]))
                {
                    string cleanMessage = CleanECAMMessage(_messageLines[leftKey]);
                    if (!string.IsNullOrWhiteSpace(cleanMessage))
                    {
                        // Show color information at the end
                        string color = GetMessagePriority(_messageLines[leftKey]);
                        if (color != "")
                        {
                            data.AppendLine($"{cleanMessage} ({color})");
                        }
                        else
                        {
                            data.AppendLine(cleanMessage);
                        }
                        hasMessages = true;
                    }
                }
            }

            // Display right side messages for continuation/details
            for (int i = 1; i <= 7; i++)
            {
                string rightKey = $"RIGHT_LINE_{i}";
                if (_messageLines.ContainsKey(rightKey) && !string.IsNullOrWhiteSpace(_messageLines[rightKey]))
                {
                    string cleanMessage = CleanECAMMessage(_messageLines[rightKey]);
                    if (!string.IsNullOrWhiteSpace(cleanMessage))
                    {
                        // Show color information at the end for right side messages too
                        string color = GetMessagePriority(_messageLines[rightKey]);
                        if (color != "")
                        {
                            data.AppendLine($"{cleanMessage} ({color})");
                        }
                        else
                        {
                            data.AppendLine(cleanMessage);
                        }
                        hasMessages = true;
                    }
                }
            }

            if (!hasMessages)
            {
                data.AppendLine("NORMAL - No active messages");
            }

            data.AppendLine("Press F5 to refresh | Press ESC to close");

            return data.ToString();
        }

        /// <summary>
        /// Removes ANSI color codes and formatting from ECAM message strings
        /// </summary>
        private string CleanECAMMessage(string rawMessage)
        {
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return "";
            }

            // Remove all ANSI escape sequences - use two passes for safety
            // Pass 1: Remove ANSI codes with escape character
            // Pattern matches: ESC followed by optional (<digits> OR digits), ending with m or )
            // Examples: \x1b<4m (color), \x1b4m (underline), \x1bm (reset), \x1b)m (reset variant)
            string cleaned = Regex.Replace(rawMessage, @"[\x1b\u001b](<\d+>|\d+)?[m)]", "");

            // Pass 2: Remove any leftover ANSI-like patterns (in case ESC was already corrupted)
            // This catches <3m, <4m, etc. that might be left behind
            cleaned = Regex.Replace(cleaned, @"<\d+>m?", "");

            // Pass 3: Remove standalone formatting codes like "4m" or "5m" at word boundaries
            cleaned = Regex.Replace(cleaned, @"\b\d[m]\b", "");

            return cleaned.Trim();
        }

        /// <summary>
        /// Extracts color information from ANSI codes for display
        /// </summary>
        private string GetMessagePriority(string rawMessage)
        {
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return "";
            }

            if (rawMessage.Contains("\x1b<2m")) return "Red";      // Warning
            if (rawMessage.Contains("\x1b<4m")) return "Amber";    // Caution
            if (rawMessage.Contains("\x1b<3m")) return "Green";    // Memo
            if (rawMessage.Contains("\x1b<5m")) return "White";    // Action items
            if (rawMessage.Contains("\x1b<6m")) return "Cyan";     // Info
            if (rawMessage.Contains("\x1b<7m")) return "Gray";     // Conditions

            return "";
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Handle F5 key for refresh
            if (keyData == Keys.F5)
            {
                RefreshECAMData();
                _announcer?.Announce("ECAM data refreshed");
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

        private void OnSimVarUpdated(object sender, SimVarUpdateEventArgs e)
        {
            // Handle PACK and TOGA variable updates
            if (e.VarName == "A32NX_OVHD_COND_PACK_1_PB_IS_ON")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _pack1On = e.Value > 0.5;
                UpdateDisplay();
            }
            else if (e.VarName == "A32NX_OVHD_COND_PACK_2_PB_IS_ON")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _pack2On = e.Value > 0.5;
                UpdateDisplay();
            }
            else if (e.VarName == "A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _togaThrustLimit = e.Value;
                UpdateDisplay();
            }
            // Handle Engine 1 parameters
            else if (e.VarName == "A32NX_ENGINE_N1:1")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _engine1N1 = e.Value;
                UpdateDisplay();
            }
            else if (e.VarName == "A32NX_ENGINE_N2:1")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _engine1N2 = e.Value;
                UpdateDisplay();
            }
            else if (e.VarName == "A32NX_ENGINE_EGT:1")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _engine1EGT = e.Value;
                UpdateDisplay();
            }
            else if (e.VarName == "A32NX_ENGINE_FF:1")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _engine1FuelFlow = e.Value;
                UpdateDisplay();
            }
            // Handle Engine 2 parameters
            else if (e.VarName == "A32NX_ENGINE_N1:2")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _engine2N1 = e.Value;
                UpdateDisplay();
            }
            else if (e.VarName == "A32NX_ENGINE_N2:2")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _engine2N2 = e.Value;
                UpdateDisplay();
            }
            else if (e.VarName == "A32NX_ENGINE_EGT:2")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _engine2EGT = e.Value;
                UpdateDisplay();
            }
            else if (e.VarName == "A32NX_ENGINE_FF:2")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _engine2FuelFlow = e.Value;
                UpdateDisplay();
            }
            // Handle Fuel Quantity
            else if (e.VarName == "FUEL_QUANTITY")
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnSimVarUpdated(sender, e)));
                    return;
                }
                _fuelQuantity = e.Value;
                UpdateDisplay();
            }
        }

        private void UpdateDisplay()
        {
            // Only update if we have ECAM message data
            if (_messageLines.Count > 0)
            {
                ecamTextBox.Text = FormatECAMData();
            }
        }

        private void OnECAMDataReceived(object sender, ECAMDataEventArgs e)
        {
            // This event fires on a background thread, so we need to invoke on UI thread
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnECAMDataReceived(sender, e)));
                return;
            }

            // Update message lines dictionary
            _messageLines["LEFT_LINE_1"] = e.LeftLine1 ?? "";
            _messageLines["LEFT_LINE_2"] = e.LeftLine2 ?? "";
            _messageLines["LEFT_LINE_3"] = e.LeftLine3 ?? "";
            _messageLines["LEFT_LINE_4"] = e.LeftLine4 ?? "";
            _messageLines["LEFT_LINE_5"] = e.LeftLine5 ?? "";
            _messageLines["LEFT_LINE_6"] = e.LeftLine6 ?? "";
            _messageLines["LEFT_LINE_7"] = e.LeftLine7 ?? "";

            _messageLines["RIGHT_LINE_1"] = e.RightLine1 ?? "";
            _messageLines["RIGHT_LINE_2"] = e.RightLine2 ?? "";
            _messageLines["RIGHT_LINE_3"] = e.RightLine3 ?? "";
            _messageLines["RIGHT_LINE_4"] = e.RightLine4 ?? "";
            _messageLines["RIGHT_LINE_5"] = e.RightLine5 ?? "";
            _messageLines["RIGHT_LINE_6"] = e.RightLine6 ?? "";
            _messageLines["RIGHT_LINE_7"] = e.RightLine7 ?? "";

            _masterWarning = e.MasterWarning;
            _masterCaution = e.MasterCaution;
            _stallWarning = e.StallWarning;

            // Update the UI with the new ECAM data
            string ecamData = FormatECAMData();
            ecamTextBox.Text = ecamData;

            System.Diagnostics.Debug.WriteLine("[ECAMDisplayForm] ECAM data received and UI updated");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Unsubscribe from events
            if (_simConnectManager != null)
            {
                _simConnectManager.SimVarUpdated -= OnSimVarUpdated;
                _simConnectManager.ECAMDataReceived -= OnECAMDataReceived;
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
