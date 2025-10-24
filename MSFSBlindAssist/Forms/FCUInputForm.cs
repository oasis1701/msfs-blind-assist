using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

public partial class FCUInputForm : Form
    {
        // Windows API declarations for focus management
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TextBox valueTextBox = null!;
        private Label titleLabel = null!;
        private Label rangeLabel = null!;
        private Button okButton = null!;
        private Button cancelButton = null!;

        public string InputValue { get; private set; } = null!;
        public bool IsValidInput { get; private set; }

        private readonly ScreenReaderAnnouncer announcer;
        private readonly string parameterType;
        private readonly Func<string, (bool isValid, string message)> validator;
        private readonly IntPtr previousWindow;

        public FCUInputForm(string title, string parameterType, string rangeText, ScreenReaderAnnouncer announcer, Func<string, (bool, string)> validator)
        {
            // Capture the current foreground window (likely the simulator)
            previousWindow = GetForegroundWindow();

            this.announcer = announcer;
            this.parameterType = parameterType;
            this.validator = validator;

            InitializeComponent(title, rangeText);
            SetupAccessibility();
        }

        private void InitializeComponent(string title, string rangeText)
        {
            // Form properties
            Text = title;
            Size = new Size(350, 200);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Title Label
            titleLabel = new Label
            {
                Text = title,
                Location = new Point(20, 20),
                Size = new Size(300, 20),
                Font = new Font(Font, FontStyle.Bold),
                AccessibleName = title
            };

            // Range Label
            rangeLabel = new Label
            {
                Text = $"Range: {rangeText}",
                Location = new Point(20, 45),
                Size = new Size(300, 20),
                AccessibleName = $"Valid range: {rangeText}"
            };

            // Value TextBox
            valueTextBox = new TextBox
            {
                Location = new Point(20, 75),
                Size = new Size(150, 25),
                AccessibleName = $"{parameterType} value",
                AccessibleDescription = $"Enter {parameterType} value and press Enter to set"
            };
            valueTextBox.KeyDown += ValueTextBox_KeyDown;

            // OK Button
            okButton = new Button
            {
                Text = "Set",
                Location = new Point(185, 105),
                Size = new Size(60, 30),
                AccessibleName = $"Set {parameterType}",
                AccessibleDescription = $"Set the {parameterType} value"
            };
            okButton.Click += OkButton_Click;

            // Cancel Button
            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(255, 105),
                Size = new Size(60, 30),
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Cancel",
                AccessibleDescription = "Cancel input"
            };

            // Add controls to form
            Controls.AddRange(new Control[]
            {
                titleLabel, rangeLabel, valueTextBox, okButton, cancelButton
            });

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void SetupAccessibility()
        {
            // Set tab order
            valueTextBox.TabIndex = 0;
            okButton.TabIndex = 1;
            cancelButton.TabIndex = 2;

            // Focus and bring window to front when opened
            Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false; // Flash to bring to front
                valueTextBox.Focus();
            };
        }

        private void ValueTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                SetValue();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                DialogResult = DialogResult.Cancel;
                Close();

                // Restore focus to the previous window (likely the simulator)
                if (previousWindow != IntPtr.Zero)
                {
                    SetForegroundWindow(previousWindow);
                }
            }
        }

        private void OkButton_Click(object? sender, EventArgs e)
        {
            SetValue();
        }

        private void SetValue()
        {
            string input = valueTextBox.Text.Trim();

            if (string.IsNullOrEmpty(input))
            {
                announcer.AnnounceImmediate("Please enter a value");
                valueTextBox.Focus();
                return;
            }

            var validationResult = validator(input);

            if (validationResult.isValid)
            {
                InputValue = input;
                IsValidInput = true;
                DialogResult = DialogResult.OK;
                Close();

                // Restore focus to the previous window (likely the simulator)
                if (previousWindow != IntPtr.Zero)
                {
                    SetForegroundWindow(previousWindow);
                }
            }
            else
            {
                announcer.AnnounceImmediate(validationResult.message);
                valueTextBox.Focus();
                valueTextBox.SelectAll();
            }
        }
}