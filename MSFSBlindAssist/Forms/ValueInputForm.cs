using System.ComponentModel;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Definition for a toggle button in a value input dialog.
/// </summary>
public record ToggleButtonDef(
    string Label,
    Func<string> GetCurrentState,
    Action OnPressed
)
{
    /// <summary>
    /// Optional enable gate, polled by windows that refresh their buttons from live
    /// state. Null means always enabled. The PMDG autopilot window uses it to disable
    /// stateful toggles while the CDA snapshot is missing — a toggle press computes
    /// its flip target from the current state, which is unreadable until then.
    /// </summary>
    public Func<bool>? IsEnabled { get; init; }

    /// <summary>
    /// Optional, asked just before the post-press state announce: true SUPPRESSES it. For a press
    /// the aircraft REFUSED — the dialog's announce is an interrupting <c>AnnounceImmediate</c>
    /// fired 1.2 s later, so it truncated the refusal the action itself had queued, and the label
    /// is the redundant half (the screen reader already read the control when it was pressed).
    /// Null, the default, announces exactly as before.
    /// </summary>
    public Func<bool>? SuppressStateAnnounce { get; init; }
}

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
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowCancelButton { get; set; } = true;

        private readonly ScreenReaderAnnouncer announcer;
        private readonly string parameterType;
        private readonly Func<string, (bool isValid, string message)> validator;
        private readonly Action<string>? onValueSet;
        private readonly IntPtr previousWindow;
        /// <summary>
        /// An ADDITIONAL labelled entry beside the main one.
        ///
        /// ⚠️ ADDITIVE BY CONSTRUCTION. With no extra fields the offset is zero and every
        /// control keeps the exact position, tab index and accessible name it had, so a
        /// dialog that does not ask for extras is unchanged for every other aircraft. That
        /// is the whole reason this lives here rather than in a DA40-local fork of the form.
        ///
        /// It exists because a dialog can genuinely be about TWO numbers: the DA40 has two
        /// altimeters on two independent transports, and Ctrl+B setting them to one shared
        /// value could never express "they disagree", which is the state a standby exists
        /// to reveal.
        /// </summary>
        public sealed class ExtraFieldDef
        {
            /// <summary>Spoken and shown. Becomes the field's AccessibleName.</summary>
            public string Label { get; init; } = "";
            /// <summary>Pre-filled and selected, so overtyping replaces it.</summary>
            public string InitialValue { get; init; } = "";
            /// <summary>Falls back to the main validator when null.</summary>
            public Func<string, (bool isValid, string message)>? Validator { get; init; }
        }

        /// <summary>
        /// Pre-fills the MAIN box, applied on Load and selected so overtyping replaces it.
        /// Set after construction; empty leaves the box blank exactly as before.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string InitialValue { get; set; } = "";

        /// <summary>What the extra fields held when Set was pressed, in declaration order.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<string> ExtraValues => _extraValues;

        private readonly List<ExtraFieldDef> _extraDefs;
        private readonly List<Button> _extraButtons = new();
        private Button? mainSetButton;

        /// <summary>
        /// Which field the pilot asked to write: -1 for all of them (the "Set all" button
        /// or Enter), 0 for the main box, 1..n for an extra. The caller applies only that
        /// one, which is what makes the two altimeters settable independently.
        /// </summary>
        public int SetFieldIndex { get; private set; } = -1;
        private readonly List<TextBox> _extraBoxes = new();
        private readonly List<Label> _extraLabels = new();
        private readonly List<string> _extraValues = new();
        private readonly List<ToggleButtonDef> _toggleDefs;
        private readonly List<Button> _toggleButtons = new();
        private System.Windows.Forms.Timer? _toggleRefreshTimer;
        private readonly Func<bool>? _inputEnabledCheck;
        private bool? _lastInputEnabledState;

        public ValueInputForm(string title, string parameterType, string rangeText,
            ScreenReaderAnnouncer announcer, Func<string, (bool, string)> validator)
            : this(title, parameterType, rangeText, announcer, validator, new List<ToggleButtonDef>(), null)
        {
        }

        /// <summary>The two-field shape: a main value plus one or more labelled extras.</summary>
        public ValueInputForm(string title, string parameterType, string rangeText,
            ScreenReaderAnnouncer announcer, Func<string, (bool, string)> validator,
            List<ExtraFieldDef> extraFields)
            : this(title, parameterType, rangeText, announcer, validator,
                   new List<ToggleButtonDef>(), null, null, extraFields)
        {
        }

        public ValueInputForm(string title, string parameterType, string rangeText,
            ScreenReaderAnnouncer announcer, Func<string, (bool, string)> validator,
            List<ToggleButtonDef> toggles, Action<string>? onValueSet = null,
            Func<bool>? inputEnabledCheck = null,
            List<ExtraFieldDef>? extraFields = null)
        {
            _extraDefs = extraFields ?? new List<ExtraFieldDef>();
            previousWindow = GetForegroundWindow();
            this.announcer = announcer;
            this.parameterType = parameterType;
            this.validator = validator;
            this.onValueSet = onValueSet;
            _toggleDefs = toggles;
            _inputEnabledCheck = inputEnabledCheck;

            InitializeComponent(title, rangeText);
            SetupAccessibility();
            if (_toggleDefs.Count > 0)
            {
                _toggleRefreshTimer = new System.Windows.Forms.Timer { Interval = 400 };
                _toggleRefreshTimer.Tick += (_, _) => RefreshToggleLabels();
                _toggleRefreshTimer.Start();
                FormClosed += (_, _) =>
                {
                    _toggleRefreshTimer?.Stop();
                    _toggleRefreshTimer?.Dispose();
                    _toggleRefreshTimer = null;
                };
            }
        }

        private void InitializeComponent(string title, string rangeText)
        {
            int toggleOffset = _toggleDefs.Count * 35;

            Text = title;
            Size = new Size(350, 200 + toggleOffset + _extraDefs.Count * 48);
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
                string label = string.IsNullOrEmpty(state) ? def.Label : $"{def.Label}: {state}";
                string accessLabel = label.Replace("&", "");
                var btn = new Button
                {
                    Text = label,
                    Location = new Point(20, toggleY),
                    Size = new Size(295, 28),
                    AccessibleName = accessLabel,
                    TabIndex = tabIdx++,
                    FlatStyle = FlatStyle.Standard
                };

                var capturedDef = def;
                var capturedBtn = btn;
                btn.Click += (_, _) =>
                {
                    capturedDef.OnPressed();
                    // Wait for sim to process, then update ALL toggle buttons
                    Task.Delay(1200).ContinueWith(_ =>
                    {
                        // try/catch closes the TOCTOU window: the form can be disposed between the
                        // IsHandleCreated check and Invoke, which would throw on this threadpool
                        // continuation as an unobserved task exception.
                        try
                        {
                            if (capturedBtn.IsDisposed || !capturedBtn.IsHandleCreated) return;
                            capturedBtn.Invoke(() =>
                            {
                                // Update all buttons — pressing one toggle can affect others
                                for (int j = 0; j < _toggleButtons.Count; j++)
                                {
                                    var b = _toggleButtons[j];
                                    var d = _toggleDefs[j];
                                    string s = d.GetCurrentState();
                                    string lbl = string.IsNullOrEmpty(s) ? d.Label : $"{d.Label}: {s}";
                                    b.Text = lbl;
                                    b.AccessibleName = lbl.Replace("&", "");
                                }
                                UpdateInputEnabled();
                                // Announce the pressed button's new state — unless the action
                                // refused. This is an INTERRUPTING announce 1.2 s after the press,
                                // so on a refused press it cut off the refusal the action itself
                                // had queued; the label is the redundant half, since the screen
                                // reader already read the control when it was pressed.
                                if (capturedDef.SuppressStateAnnounce?.Invoke() == true) return;
                                string newState = capturedDef.GetCurrentState();
                                string announceLabel = capturedDef.Label.Replace("&", "");
                                if (string.IsNullOrEmpty(newState))
                                    announcer.AnnounceImmediate(announceLabel);
                                else
                                    announcer.AnnounceImmediate($"{announceLabel} {newState}");
                            });
                        }
                        catch (ObjectDisposedException) { }
                        catch (InvalidOperationException) { }
                    });
                };

                _toggleButtons.Add(btn);
                toggleY += 35;
            }

            // Each extra field is a label above its box, stacked under the main one. The
            // MAIN box keeps its position, so an extras-free dialog is pixel-identical.
            int extraY = 105 + toggleOffset;
            foreach (var ef in _extraDefs)
            {
                var lbl = new Label
                {
                    Text = ef.Label,
                    Location = new Point(20, extraY),
                    Size = new Size(300, 18),
                    AccessibleName = ef.Label
                };
                var box = new TextBox
                {
                    Text = ef.InitialValue,
                    Location = new Point(20, extraY + 20),
                    Size = new Size(150, 25),
                    AccessibleName = $"{ef.Label} value",
                    AccessibleDescription = $"Enter {ef.Label} and press Enter to set",
                    TabIndex = tabIdx++
                };
                box.KeyDown += ValueTextBox_KeyDown;
                box.GotFocus += (_, _) => box.SelectAll();

                // ⚠️ ONE BUTTON FOR TWO FIELDS READ AS A BUTTON FOR ONE OF THEM. The shared
                // "Set" is named after the MAIN parameter, so a two-field dialog announced
                // "Set Main altimeter setting" - a button that in fact wrote BOTH. Reported
                // from the cockpit as "there is only a set main button, there's no set
                // standby button, and you can't set them individually".
                //
                // Each extra field now carries its own button, named for the field it
                // writes, and the shared one is renamed below to say it does all of them.
                // Additive: a dialog with no extras gets neither change and stays
                // pixel-identical.
                int mine = _extraBoxes.Count;
                var setBtn = new Button
                {
                    Text = "Set",
                    Location = new Point(185, extraY + 20),
                    Size = new Size(60, 25),
                    AccessibleName = $"Set {ef.Label}",
                    TabIndex = tabIdx++
                };
                setBtn.Click += (_, _) => SetOneField(mine + 1);

                _extraLabels.Add(lbl);
                _extraBoxes.Add(box);
                _extraButtons.Add(setBtn);
                extraY += 48;
            }
            int extraOffset = _extraDefs.Count * 48;

            if (_extraDefs.Count > 0)
            {
                mainSetButton = new Button
                {
                    Text = "Set",
                    Location = new Point(185, 105 + toggleOffset - 20),
                    Size = new Size(60, 25),
                    AccessibleName = $"Set {parameterType}",
                    TabIndex = tabIdx++
                };
                mainSetButton.Click += (_, _) => SetOneField(0);
            }

            okButton = new Button
            {
                Text = _extraDefs.Count > 0 ? "Set all" : "Set",
                Location = new Point(185, 105 + toggleOffset + extraOffset),
                Size = new Size(_extraDefs.Count > 0 ? 80 : 60, 30),
                // Naming it after the MAIN parameter was the defect: it writes every field.
                AccessibleName = _extraDefs.Count > 0
                    ? "Set all fields"
                    : $"Set {parameterType}",
                TabIndex = tabIdx++
            };
            okButton.Click += OkButton_Click;

            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(255, 105 + toggleOffset + extraOffset),
                Size = new Size(60, 30),
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Cancel",
                TabIndex = tabIdx++
            };

            Controls.Add(titleLabel);
            Controls.Add(rangeLabel);
            foreach (var btn in _toggleButtons)
                Controls.Add(btn);
            Controls.Add(valueTextBox);
            if (mainSetButton != null) Controls.Add(mainSetButton);
            foreach (var l in _extraLabels) Controls.Add(l);
            foreach (var b in _extraBoxes) Controls.Add(b);
            foreach (var b in _extraButtons) Controls.Add(b);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        // Idempotent: only mutates control state when the gate flips. Safe to
        // call every timer tick. The 1200 ms post-click refresh missed the
        // state change when the sim took longer than that to commit (the
        // PMDG NG3 broadcast plus AP-side animation can exceed 1200 ms for
        // some MCP modes — e.g. 737 V/S), leaving the textbox stuck read-only
        // even after the engage button's label correctly flipped to "Engaged"
        // via the 400 ms label-refresh timer. Running this check on the same
        // timer keeps both in sync.
        private void UpdateInputEnabled()
        {
            if (_inputEnabledCheck == null) return;
            bool enabled = _inputEnabledCheck();
            if (_lastInputEnabledState == enabled) return;
            _lastInputEnabledState = enabled;
            valueTextBox.ReadOnly = !enabled;
            okButton.Enabled = enabled;
            valueTextBox.AccessibleDescription = enabled
                ? $"Enter {parameterType} value and press Enter to set"
                : $"Engage mode first to enter a value";
        }

        // Keeps every toggle button's label AND the input-enabled gate in sync
        // with live mode state while the dialog is open. Pressing one mode can
        // change others (e.g. LNAV drops Heading Select), the AP can change
        // modes on its own, and on the 737 V/S engagement can take longer than
        // the click handler's 1200 ms one-shot refresh window. Updates only on
        // change to avoid screen-reader churn. Never announces — that stays in
        // the click handler for the button the user actually pressed.
        private void RefreshToggleLabels()
        {
            for (int j = 0; j < _toggleButtons.Count; j++)
            {
                var b = _toggleButtons[j];
                var d = _toggleDefs[j];
                string s = d.GetCurrentState();
                string lbl = string.IsNullOrEmpty(s) ? d.Label : $"{d.Label}: {s}";
                if (b.Text != lbl)
                {
                    b.Text = lbl;
                    b.AccessibleName = lbl.Replace("&", "");
                }
            }
            UpdateInputEnabled();
        }

        private void SetupAccessibility()
        {
            Load += (sender, e) =>
            {
                if (!ShowCancelButton)
                {
                    cancelButton.Visible = false;
                    CancelButton = null;
                }
                BringToFront();
                Activate();
                TopMost = true;
                TopMost = false;
                if (!string.IsNullOrEmpty(InitialValue))
                {
                    valueTextBox.Text = InitialValue;
                    valueTextBox.SelectAll();
                }
                valueTextBox.Focus();
                UpdateInputEnabled();
            };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                if (previousWindow != IntPtr.Zero)
                    SetForegroundWindow(previousWindow);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ValueTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                if (_inputEnabledCheck == null || _inputEnabledCheck())
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
            SetFieldIndex = -1;      // every field, which is what "Set all" says it does
            SetValue();
        }

        /// <summary>
        /// Write ONE field and nothing else. Validates only that field - the all-fields
        /// path deliberately validates every box before sending anything, because a partial
        /// write there would leave two altimeters differently set for a reason the pilot
        /// never asked for; here the pilot has asked for exactly one, so the others are not
        /// their business and must not block them.
        /// </summary>
        private void SetOneField(int index)
        {
            var box = index == 0 ? valueTextBox : _extraBoxes[index - 1];
            var check = index == 0 ? validator : (_extraDefs[index - 1].Validator ?? validator);
            string label = index == 0 ? parameterType : _extraDefs[index - 1].Label;

            string input = box.Text.Trim();
            if (string.IsNullOrEmpty(input))
            {
                announcer.AnnounceImmediate($"Please enter {label}");
                box.Focus();
                return;
            }

            var result = check(input);
            if (!result.isValid)
            {
                announcer.AnnounceImmediate(result.message);
                box.Focus();
                box.SelectAll();
                return;
            }

            SetFieldIndex = index;
            IsValidInput = true;
            InputValue = index == 0 ? input : InputValue;
            _extraValues.Clear();
            for (int i = 0; i < _extraBoxes.Count; i++) _extraValues.Add(_extraBoxes[i].Text.Trim());
            DialogResult = DialogResult.OK;
            Close();
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

            // ⚠️ EVERY EXTRA FIELD IS VALIDATED BEFORE ANYTHING IS SENT, and focus is put on
            // the first one that fails. A dialog that writes one altimeter and then rejects
            // the other would leave the two set differently for a reason the pilot never
            // asked for - which is precisely the fault state this dialog exists to make
            // visible, so producing it by accident would be worse than useless.
            for (int i = 0; i < _extraBoxes.Count; i++)
            {
                string ev = _extraBoxes[i].Text.Trim();
                if (string.IsNullOrEmpty(ev))
                {
                    announcer.AnnounceImmediate($"Please enter {_extraDefs[i].Label}");
                    _extraBoxes[i].Focus();
                    return;
                }
                var r = (_extraDefs[i].Validator ?? validator)(ev);
                if (!r.isValid)
                {
                    announcer.AnnounceImmediate(r.message);
                    _extraBoxes[i].Focus();
                    _extraBoxes[i].SelectAll();
                    return;
                }
            }

            if (validationResult.isValid)
            {
                _extraValues.Clear();
                foreach (var b in _extraBoxes) _extraValues.Add(b.Text.Trim());
                InputValue = input;
                IsValidInput = true;

                if (onValueSet != null)
                {
                    // Callback mode: send value immediately, stay open for more input
                    onValueSet(input);
                    valueTextBox.SelectAll();
                    valueTextBox.Focus();
                }
                else
                {
                    // Legacy mode: close dialog, caller reads InputValue
                    DialogResult = DialogResult.OK;
                    Close();

                    if (previousWindow != IntPtr.Zero)
                    {
                        SetForegroundWindow(previousWindow);
                    }
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
