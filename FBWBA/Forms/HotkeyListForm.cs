using System;
using System.Drawing;
using System.Windows.Forms;

namespace FBWBA.Forms
{
    public partial class HotkeyListForm : Form
    {
        private TextBox hotkeyTextBox;
        private Button okButton;

        public HotkeyListForm()
        {
            InitializeComponent();
            SetupAccessibility();
        }

        private void InitializeComponent()
        {
            Text = "Hotkey List";
            Size = new Size(600, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Hotkey TextBox (read-only, multi-line, tabbable)
            hotkeyTextBox = new TextBox
            {
                Text = GetHotkeyListText(),
                Font = new Font("Consolas", 9),
                Location = new Point(20, 20),
                Size = new Size(550, 390),
                Multiline = true,
                ReadOnly = true,
                TabStop = true,
                BorderStyle = BorderStyle.Fixed3D,
                BackColor = System.Drawing.SystemColors.Control,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = "Hotkey List",
                AccessibleDescription = "Complete list of all available hotkeys organized by output and input modes"
            };

            // OK Button
            okButton = new Button
            {
                Text = "OK",
                Location = new Point(250, 430),
                Size = new Size(90, 35),
                DialogResult = DialogResult.OK,
                AccessibleName = "Close Hotkey List Dialog",
                AccessibleDescription = "Close the Hotkey List window"
            };
            okButton.Click += OkButton_Click;

            // Add controls to form
            Controls.AddRange(new Control[]
            {
                hotkeyTextBox, okButton
            });

            AcceptButton = okButton;
            CancelButton = okButton;
        }

        private string GetHotkeyListText()
        {
            return @"FBWBA HOTKEY REFERENCE

Application Navigation:
  Ctrl+1     Focus Sections List
  Ctrl+2     Focus Panels List


OUTPUT MODE - Press ] to activate

FCU Controls:
  Shift+H    Read FCU Heading
  Shift+S    Read FCU Speed
  Shift+A    Read FCU Altitude
  Shift+V    Read FCU VS/FPA

Altitude:
  Q          Read Altitude AGL
  A          Read Altitude MSL

Airspeed:
  S          Read IAS (Indicated Airspeed)
  T          Read TAS (True Airspeed)
  G          Read Ground Speed

Heading:
  H          Read Magnetic Heading
  U          Read True Heading

Vertical:
  V          Read Vertical Speed

Fuel:
  F          Read Total Fuel Quantity

Navigation:
  Ctrl+I     Read ILS Guidance
  Ctrl+L     Read Location Info
  I          Read Wind Info
  Shift+I    Show METAR Report
  Ctrl+V     Toggle Visual Approach
  Ctrl+0     Read Approach Capability

Speed Tape:
  Shift+1    Read O Speed (GD)
  Shift+2    Read S-Speed
  Shift+3    Read F-Speed
  Shift+4    Read VLS (Minimum Selectable Speed)
  Shift+5    Read VS (Stall Speed)
  Shift+6    Read VFE Speed

Other:
  Ctrl+P     Show PFD Window
  Shift+D    SimBrief Briefing
  Shift+C    Show Flight Checklist


INPUT MODE - Press [ to activate

Teleport:
  Shift+R    Runway Teleport
  Shift+G    Gate Teleport
  Shift+D    Select Destination Runway

Autopilot:
  Shift+A    Toggle Autopilot 1
  Ctrl+O     Toggle Autopilot 2
  Shift+P    Toggle Approach Mode

FCU Push/Pull:
  Shift+1    Push Heading Knob
  Ctrl+1     Pull Heading Knob
  Shift+2    Push Altitude Knob
  Ctrl+2     Pull Altitude Knob
  Shift+3    Push Speed Knob
  Ctrl+3     Pull Speed Knob
  Shift+4    Push VS Knob
  Ctrl+4     Pull VS Knob

FCU Set Values:
  Ctrl+H     Set Heading Value
  Ctrl+S     Set Speed Value
  Ctrl+A     Set Altitude Value
  Ctrl+V     Set VS Value


General:
  Escape     Exit active hotkey mode";
        }

        private void SetupAccessibility()
        {
            // Set tab order for logical navigation
            hotkeyTextBox.TabIndex = 0;
            okButton.TabIndex = 1;

            // Focus and bring window to front when opened
            Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false; // Flash to bring to front
                hotkeyTextBox.Focus();
            };
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Handle Escape key
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }
    }
}
