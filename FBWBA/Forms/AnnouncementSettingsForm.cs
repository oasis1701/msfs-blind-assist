using System;
using System.Drawing;
using System.Windows.Forms;
using FBWBA.Accessibility;

namespace FBWBA.Forms
{
    public partial class AnnouncementSettingsForm : Form
    {
        private RadioButton screenReaderRadio;
        private RadioButton sapiRadio;
        private Button okButton;
        private Button cancelButton;
        private Label statusLabel;
        private Label titleLabel;

        public AnnouncementMode SelectedMode { get; private set; }

        public AnnouncementSettingsForm(AnnouncementMode currentMode)
        {
            SelectedMode = currentMode;
            InitializeComponent();
            SetupAccessibility();
            UpdateStatus();
        }

        private void InitializeComponent()
        {
            Text = "Announcement Settings";
            Size = new Size(450, 250);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Title Label
            titleLabel = new Label
            {
                Text = "Choose how aircraft state announcements are delivered:",
                Location = new Point(20, 20),
                Size = new Size(400, 20),
                AccessibleName = "Announcement Settings Title"
            };

            // Screen Reader Radio Button
            screenReaderRadio = new RadioButton
            {
                Text = "Screen Reader (NVDA, JAWS, etc.) - Recommended",
                Location = new Point(20, 55),
                Size = new Size(400, 25),
                AccessibleName = "Screen Reader Mode",
                AccessibleDescription = "Send announcements through your screen reader for natural speech integration",
                Checked = SelectedMode == AnnouncementMode.ScreenReader
            };
            screenReaderRadio.CheckedChanged += RadioButton_CheckedChanged;

            // SAPI Radio Button
            sapiRadio = new RadioButton
            {
                Text = "SAPI (Windows Speech) - Fallback",
                Location = new Point(20, 85),
                Size = new Size(400, 25),
                AccessibleName = "SAPI Mode",
                AccessibleDescription = "Use Windows built-in speech synthesis for announcements",
                Checked = SelectedMode == AnnouncementMode.SAPI
            };
            sapiRadio.CheckedChanged += RadioButton_CheckedChanged;

            // Status Label
            statusLabel = new Label
            {
                Location = new Point(20, 120),
                Size = new Size(400, 40),
                AccessibleName = "Screen Reader Status",
                Text = "Checking screen reader status..."
            };

            // OK Button
            okButton = new Button
            {
                Text = "OK",
                Location = new Point(270, 180),
                Size = new Size(75, 30),
                DialogResult = DialogResult.OK,
                AccessibleName = "Apply Settings",
                AccessibleDescription = "Apply the selected announcement mode"
            };
            okButton.Click += OkButton_Click;

            // Cancel Button
            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(355, 180),
                Size = new Size(75, 30),
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Cancel",
                AccessibleDescription = "Cancel without changing settings"
            };

            // Add controls to form
            Controls.AddRange(new Control[]
            {
                titleLabel, screenReaderRadio, sapiRadio, statusLabel, okButton, cancelButton
            });

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void SetupAccessibility()
        {
            // Set tab order for logical navigation
            titleLabel.TabIndex = 0;
            screenReaderRadio.TabIndex = 1;
            sapiRadio.TabIndex = 2;
            statusLabel.TabIndex = 3;
            okButton.TabIndex = 4;
            cancelButton.TabIndex = 5;

            // Focus and bring window to front when opened
            Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false; // Flash to bring to front
                screenReaderRadio.Focus();
            };
        }

        private void UpdateStatus()
        {
            try
            {
                using (var tolkTest = new TolkWrapper())
                {
                    if (tolkTest.Initialize())
                    {
                        if (tolkTest.IsScreenReaderRunning())
                        {
                            string detected = tolkTest.DetectedScreenReader;
                            statusLabel.Text = $"Screen reader detected: {detected}\nChoose 'Screen Reader' for best experience.";
                            statusLabel.ForeColor = Color.DarkGreen;
                        }
                        else
                        {
                            statusLabel.Text = "No screen reader detected.\nSAPI mode recommended for speech feedback.";
                            statusLabel.ForeColor = Color.DarkOrange;
                        }
                    }
                    else
                    {
                        statusLabel.Text = "Unable to initialize screen reader detection.\nSAPI mode will be used as fallback.";
                        statusLabel.ForeColor = Color.Red;
                    }
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Error checking screen reader: {ex.Message}\nSAPI mode recommended.";
                statusLabel.ForeColor = Color.Red;
            }
        }

        private void RadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (screenReaderRadio.Checked)
            {
                SelectedMode = AnnouncementMode.ScreenReader;
            }
            else if (sapiRadio.Checked)
            {
                SelectedMode = AnnouncementMode.SAPI;
            }
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
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }
    }
}