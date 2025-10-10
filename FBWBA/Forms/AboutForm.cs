using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace FBWBA.Forms
{
    public partial class AboutForm : Form
    {
        private TextBox aboutTextBox;
        private Button okButton;

        public AboutForm()
        {
            InitializeComponent();
            SetupAccessibility();
        }

        private void InitializeComponent()
        {
            Text = "About FBWBA";
            Size = new Size(450, 280);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Get version info from assembly
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionString = $"v{version.Major}.{version.Minor}.{version.Build}"; // Format as v0.2.3
            var copyright = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright © 2025";

            // About TextBox (read-only, multi-line, tabbable)
            aboutTextBox = new TextBox
            {
                Text = $"FlyByWire Blind Access\r\n\r\n" +
                       $"Version {versionString}\r\n\r\n" +
                       $"Accessible application to control the Fly By Wire A32nx aircraft"\r\n" +
                       $"for Microsoft Flight Simulator\r\n\r\n" +
                       $"{copyright}",
                Font = new Font("Segoe UI", 10),
                Location = new Point(20, 20),
                Size = new Size(400, 160),
                Multiline = true,
                ReadOnly = true,
                TabStop = true,
                BorderStyle = BorderStyle.Fixed3D,
                BackColor = System.Drawing.SystemColors.Control,
                TextAlign = HorizontalAlignment.Center,
                AccessibleName = "About Information",
                AccessibleDescription = "Application name, version, description, and copyright information"
            };

            // OK Button
            okButton = new Button
            {
                Text = "OK",
                Location = new Point(185, 210),
                Size = new Size(75, 30),
                DialogResult = DialogResult.OK,
                AccessibleName = "Close About Dialog",
                AccessibleDescription = "Close the About window"
            };
            okButton.Click += OkButton_Click;

            // Add controls to form
            Controls.AddRange(new Control[]
            {
                aboutTextBox, okButton
            });

            AcceptButton = okButton;
            CancelButton = okButton;
        }

        private void SetupAccessibility()
        {
            // Set tab order for logical navigation
            aboutTextBox.TabIndex = 0;
            okButton.TabIndex = 1;

            // Focus and bring window to front when opened
            Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false; // Flash to bring to front
                aboutTextBox.Focus();
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
