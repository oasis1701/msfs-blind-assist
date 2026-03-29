using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Definition for a toggle button in a value input dialog.
/// </summary>
public record ToggleButtonDef(
    string Label,
    Func<string> GetCurrentState,
    Action OnPressed
);

public partial class ValueInputForm : Form
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
        private readonly List<ToggleButtonDef> _toggleDefs;
        private readonly List<Button> _toggleButtons = new();

        public ValueInputForm(string title, string parameterType, string rangeText,
            ScreenReaderAnnouncer announcer, Func<string, (bool, string)> validator)
            : this(title, parameterType, rangeText, announcer, validator, new List<ToggleButtonDef>())
        {
        }

        public ValueInputForm(string title, string parameterType, string rangeText,
            ScreenReaderAnnouncer announcer, Func<string, (bool, string)> validator,
            List<ToggleButtonDef> toggles)
        {
            previousWindow = GetForegroundWindow();
            this.announcer = announcer;
            this.parameterType = parameterType;
            this.validator = validator;
            _toggleDefs = toggles;

            InitializeComponent(title, rangeText);
            SetupAccessibility();
        }

        private void InitializeComponent(string title, string rangeText)
        {
            int toggleOffset = _toggleDefs.Count * 35;

            Text = title;
            Size = new Size(350, 200 + toggleOffset);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            titleLabel = new Label
            {
                Text = title,
                Location = new Point(20, 20),
                Size = new Size(300, 20),
                Font = new Font(Font, FontStyle.Bold),
                AccessibleName = title
            };

            rangeLabel = new Label
            {
                Text = $"Range: {rangeText}",
                Location = new Point(20, 45),
                Size = new Size(300, 20),
                AccessibleName = $"Valid range: {rangeText}"
            };

            // Text box gets TabIndex 0 (first focus target)
            int tabIdx = 0;

            valueTextBox = new TextBox
            {
                Location = new Point(20, 75 + toggleOffset),
                Size = new Size(150, 25),
                AccessibleName = $"{parameterType} value",
                AccessibleDescription = $"Enter {parameterType} value and press Enter to set",
                TabIndex = tabIdx++
            };
            valueTextBox.KeyDown += ValueTextBox_KeyDown;

            // Toggle buttons
            int toggleY = 70;
            foreach (var def in _toggleDefs)
            {
                string state = def.GetCurrentState();
                var btn = new Button
                {
                    Text = $"{def.Label}: {state}",
                    Location = new Point(20, toggleY),
                    Size = new Size(295, 28),
                    AccessibleName = $"{def.Label}: {state}",
                    AccessibleDescription = $"Press to toggle {def.Label}",
                    TabIndex = tabIdx++,
                    FlatStyle = FlatStyle.Standard
                };

                var capturedDef = def;
                var capturedBtn = btn;
                btn.Click += (_, _) =>
                {
                    capturedDef.OnPressed();
                    Task.Delay(150).ContinueWith(_ =>
                    {
                        if (!capturedBtn.IsDisposed && capturedBtn.IsHandleCreated)
                        {
                            capturedBtn.Invoke(() =>
                            {
                                string newState = capturedDef.GetCurrentState();
                                capturedBtn.Text = $"{capturedDef.Label}: {newState}";
                                capturedBtn.AccessibleName = $"{capturedDef.Label}: {newState}";
                                announcer.AnnounceImmediate($"{capturedDef.Label} {newState}");
                            });
                        }
                    });
                };

                _toggleButtons.Add(btn);
                toggleY += 35;
            }

            okButton = new Button
            {
                Text = "Set",
                Location = new Point(185, 105 + toggleOffset),
                Size = new Size(60, 30),
                AccessibleName = $"Set {parameterType}",
                AccessibleDescription = $"Set the {parameterType} value",
                TabIndex = tabIdx++
            };
            okButton.Click += OkButton_Click;

            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(255, 105 + toggleOffset),
                Size = new Size(60, 30),
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Cancel",
                AccessibleDescription = "Cancel input",
                TabIndex = tabIdx++
            };

            Controls.Add(titleLabel);
            Controls.Add(rangeLabel);
            foreach (var btn in _toggleButtons)
                Controls.Add(btn);
            Controls.Add(valueTextBox);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void SetupAccessibility()
        {
            Load += (sender, e) =>
            {
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false;
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
