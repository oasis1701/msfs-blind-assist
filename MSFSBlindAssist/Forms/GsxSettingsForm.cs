using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms;

public sealed class GsxSettingsForm : Form
{
    private readonly GsxService _gsxService;
    private readonly ScreenReaderAnnouncer _announcer;
    private IReadOnlyList<GsxService.GsxSettingItem> _items;
    private readonly ListBox _tabSelector = new();
    private readonly Panel _settingsHost = new();
    private readonly Dictionary<string, FlowLayoutPanel> _tabPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action> _closeCommitters = new();
    private readonly Dictionary<string, string> _lastCommittedValues = new(StringComparer.OrdinalIgnoreCase);

    public GsxSettingsForm(
        GsxService gsxService,
        ScreenReaderAnnouncer announcer,
        IReadOnlyList<GsxService.GsxSettingItem> items)
    {
        _gsxService = gsxService ?? throw new ArgumentNullException(nameof(gsxService));
        _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));
        // Snapshot: GsxService.SettingsItems returns its internal list, which it
        // mutates in place (Clear + AddRange) before raising SettingsChanged.
        // Holding a copy here means BuildItemsSignature can detect what changed.
        _items = items?.ToArray() ?? Array.Empty<GsxService.GsxSettingItem>();

        BuildUi();
        PopulateSettings();
    }

    public void ShowForm()
    {
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        SelectSectionList();
    }

    public bool HasItems => _items.Count > 0;

    /// <summary>
    /// Replace the displayed settings with a fresh parse, rebuilding the UI
    /// only when the content actually changed. GSX writes settings.html
    /// asynchronously (the parse right after pressing C usually sees only
    /// the Python stub), so the service re-publishes ~1 s later — refreshing
    /// in place keeps screen-reader focus instead of recreating the window.
    /// Returns true when the UI was rebuilt.
    /// </summary>
    public bool RefreshItems(IReadOnlyList<GsxService.GsxSettingItem>? items)
    {
        items ??= Array.Empty<GsxService.GsxSettingItem>();
        if (BuildItemsSignature(items) == BuildItemsSignature(_items))
            return false;

        _items = items.ToArray();
        PopulateSettings();
        SelectSectionList();
        return true;
    }

    // Record equality won't work here: GsxSettingItem.Choices is an
    // IReadOnlyList property, which records compare by reference.
    // Use ASCII control characters as separators so free-text fields
    // (Label, Tip, Value, etc.) that may contain '|', ';', or ':' can
    // never collide across field boundaries.
    private static string BuildItemsSignature(IReadOnlyList<GsxService.GsxSettingItem> items) =>
        string.Join("\n", items.Select(i =>
            string.Join("",
                i.Key, i.Type, i.Label, i.Value, i.Category, i.Tip,
                i.InfoValue, i.ButtonText,
                i.Min?.ToString(CultureInfo.InvariantCulture) ?? "",
                i.Max?.ToString(CultureInfo.InvariantCulture) ?? "",
                i.Step?.ToString(CultureInfo.InvariantCulture) ?? "",
                i.Unit,
                string.Join("", i.Choices.Select(c =>
                    c.Value.ToString(CultureInfo.InvariantCulture) + "" + c.Label)))));

    private void BuildUi()
    {
        Text = "GSX Settings";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, 620);
        MinimumSize = new Size(560, 420);
        ShowInTaskbar = true;
        KeyPreview = true;

        _tabSelector.Dock = DockStyle.Top;
        _tabSelector.Height = 64;
        _tabSelector.IntegralHeight = false;
        _tabSelector.AccessibleRole = AccessibleRole.PageTab;
        _tabSelector.TabStop = true;
        _tabSelector.TabIndex = 0;
        _tabSelector.SelectedIndexChanged += (_, _) => ShowSelectedTab();
        _tabSelector.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Left)
            {
                MoveSelectedTab(-1);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Right)
            {
                MoveSelectedTab(1);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        _settingsHost.Dock = DockStyle.Fill;
        _settingsHost.TabIndex = 1;

        var closeButton = new Button
        {
            Text = "&Close",
            Dock = DockStyle.Right,
            Width = 100,
            TabIndex = 2,
            AccessibleName = "Close"
        };
        closeButton.Click += (_, _) => Close();

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8)
        };
        bottomPanel.Controls.Add(closeButton);

        Controls.Add(_settingsHost);
        Controls.Add(_tabSelector);
        Controls.Add(bottomPanel);

        FormClosing += (_, _) => CommitAllSettings();
    }

    private void PopulateSettings()
    {
        _tabSelector.Items.Clear();
        _settingsHost.Controls.Clear();
        foreach (var page in _tabPages.Values)
            page.Dispose();
        _tabPages.Clear();
        _closeCommitters.Clear();
        _lastCommittedValues.Clear();

        if (_items.Count == 0)
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(12)
            };

            panel.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "No GSX settings were available.",
                AccessibleName = "No GSX settings available"
            });
            _tabPages["Settings"] = panel;
            _tabSelector.Items.Add("Settings");
            _tabSelector.SelectedIndex = 0;
            return;
        }

        foreach (var group in _items.GroupBy(i => string.IsNullOrWhiteSpace(i.Category) ? "General" : i.Category))
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(12)
            };

            foreach (var item in group)
            {
                Control? control = CreateControlForItem(item);
                if (control == null)
                    continue;

                var row = new Panel
                {
                    Width = 700,
                    Height = Math.Max(58, control.Height + 22),
                    Margin = new Padding(0, 0, 0, 8)
                };

                var label = new Label
                {
                    Text = item.Label,
                    Dock = DockStyle.Top,
                    Height = 22,
                    AutoEllipsis = true
                };

                if (!string.IsNullOrWhiteSpace(item.Tip))
                    control.AccessibleDescription = item.Tip;

                control.Dock = DockStyle.Top;
                row.Controls.Add(control);
                row.Controls.Add(label);
                panel.Controls.Add(row);
            }

            _tabPages[group.Key] = panel;
            _tabSelector.Items.Add(group.Key);
        }

        if (_tabSelector.Items.Count > 0)
            _tabSelector.SelectedIndex = 0;
    }

    private void MoveSelectedTab(int delta)
    {
        if (_tabSelector.Items.Count == 0)
            return;

        int next = _tabSelector.SelectedIndex + delta;
        if (next < 0)
            next = _tabSelector.Items.Count - 1;
        else if (next >= _tabSelector.Items.Count)
            next = 0;

        _tabSelector.SelectedIndex = next;
    }

    private void ShowSelectedTab()
    {
        if (_tabSelector.SelectedItem is not string selected
            || !_tabPages.TryGetValue(selected, out FlowLayoutPanel? panel))
            return;

        _settingsHost.SuspendLayout();
        _settingsHost.Controls.Clear();
        _settingsHost.Controls.Add(panel);
        _settingsHost.ResumeLayout();
    }

    private Control? CreateControlForItem(GsxService.GsxSettingItem item)
    {
        string type = item.Type.ToLowerInvariant();
        return type switch
        {
            "toggle" => CreateToggle(item),
            "choice" => CreateChoice(item),
            "range" => CreateRange(item),
            "text" => CreateText(item),
            "action" => CreateAction(item),
            "info" => CreateInfo(item),
            _ => null
        };
    }

    private Control CreateToggle(GsxService.GsxSettingItem item)
    {
        var checkBox = new CheckBox
        {
            Text = string.Empty,
            Checked = ParseDouble(item.Value) != 0,
            Height = 28,
            AccessibleName = item.Label
        };
        SeedCommitted(item, ((double)(checkBox.Checked ? 1 : 0)).ToString(CultureInfo.InvariantCulture));
        checkBox.CheckedChanged += (_, _) =>
        {
            CommitNumber(item, checkBox.Checked ? 1 : 0);
        };
        _closeCommitters.Add(() => CommitNumber(item, checkBox.Checked ? 1 : 0));
        return checkBox;
    }

    private Control CreateChoice(GsxService.GsxSettingItem item)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Height = 28,
            AccessibleName = item.Label
        };

        foreach (var choice in item.Choices)
            combo.Items.Add(new ChoiceItem(choice));

        double current = ParseDouble(item.Value);
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ChoiceItem choiceItem && Math.Abs(choiceItem.Value - current) < 0.000001)
            {
                combo.SelectedIndex = i;
                break;
            }
        }

        if (combo.SelectedItem is ChoiceItem initialChoice)
            SeedCommitted(item, initialChoice.Value.ToString(CultureInfo.InvariantCulture));
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ChoiceItem choice) return;
            CommitNumber(item, choice.Value);
        };
        _closeCommitters.Add(() =>
        {
            if (combo.SelectedItem is ChoiceItem choice)
                CommitNumber(item, choice.Value);
        });

        return combo;
    }

    private Control CreateRange(GsxService.GsxSettingItem item)
    {
        if (IsSliderRange(item))
            return CreateSliderRange(item);

        var numeric = new NumericUpDown
        {
            Minimum = (decimal)(item.Min ?? 0),
            Maximum = (decimal)(item.Max ?? 100),
            Increment = (decimal)(item.Step ?? 1),
            Value = Clamp((decimal)ParseDouble(item.Value), (decimal)(item.Min ?? 0), (decimal)(item.Max ?? 100)),
            DecimalPlaces = DecimalPlaces(item.Step ?? 1),
            Height = 28,
            AccessibleName = item.Label
        };

        SeedCommitted(item, ((double)numeric.Value).ToString(CultureInfo.InvariantCulture));
        numeric.ValueChanged += (_, _) =>
        {
            CommitNumber(item, (double)numeric.Value);
        };
        _closeCommitters.Add(() => CommitNumber(item, (double)numeric.Value));

        return numeric;
    }

    private Control CreateSliderRange(GsxService.GsxSettingItem item)
    {
        double min = item.Min ?? 0;
        double max = item.Max ?? 100;
        double step = item.Step ?? 1;
        if (step <= 0)
            step = 1;

        int minimum = (int)Math.Round(min);
        int maximum = (int)Math.Round(max);
        int value = (int)Math.Round(ParseDouble(item.Value));
        int smallChange = Math.Max(1, (int)Math.Round(step));
        int largeChange = Math.Max(smallChange, Math.Min(10, Math.Max(1, maximum - minimum)));

        var trackBar = new SettingsSlider
        {
            Minimum = minimum,
            Maximum = Math.Max(minimum, maximum),
            TickFrequency = smallChange,
            SmallChange = smallChange,
            LargeChange = largeChange,
            Value = Math.Clamp(value, minimum, Math.Max(minimum, maximum)),
            Height = 44,
            AccessibleName = item.Label
        };

        SeedCommitted(item, ((double)trackBar.Value).ToString(CultureInfo.InvariantCulture));
        trackBar.ValueChanged += (_, _) => CommitNumber(item, trackBar.Value);
        _closeCommitters.Add(() => CommitNumber(item, trackBar.Value));

        return trackBar;
    }

    private static bool IsSliderRange(GsxService.GsxSettingItem item) =>
        string.Equals(item.Unit, "%", StringComparison.OrdinalIgnoreCase)
        || item.Key.Contains("volume", StringComparison.OrdinalIgnoreCase);

    private Control CreateText(GsxService.GsxSettingItem item)
    {
        var textBox = new TextBox
        {
            Text = item.Value,
            Height = 28,
            AccessibleName = item.Label
        };

        SeedCommitted(item, textBox.Text);
        textBox.TextChanged += (_, _) => CommitText(item, textBox.Text);
        textBox.Leave += (_, _) => CommitText(item, textBox.Text);
        textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            CommitText(item, textBox.Text);
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
        _closeCommitters.Add(() => CommitText(item, textBox.Text));

        return textBox;
    }

    private Control CreateAction(GsxService.GsxSettingItem item)
    {
        var button = new Button
        {
            Text = string.IsNullOrWhiteSpace(item.ButtonText) ? item.Label : item.ButtonText,
            Height = 32,
            AccessibleName = item.Label
        };
        button.Click += (_, _) =>
        {
            _gsxService.PulseSettingAction(item.Key);
        };
        return button;
    }

    private static Control CreateInfo(GsxService.GsxSettingItem item) =>
        new TextBox
        {
            Text = item.InfoValue,
            ReadOnly = true,
            Height = 28,
            AccessibleName = item.Label
        };

    private void CommitAllSettings()
    {
        foreach (var commit in _closeCommitters.ToList())
            commit();
    }

    private void CommitNumber(GsxService.GsxSettingItem item, double value)
    {
        string textValue = value.ToString(CultureInfo.InvariantCulture);
        if (!ShouldCommit(item, textValue))
            return;

        _gsxService.SetSettingNumber(item.Key, value);
        _gsxService.PersistSettingValue(item, textValue);
    }

    private void CommitText(GsxService.GsxSettingItem item, string value)
    {
        if (!ShouldCommit(item, value))
            return;

        _gsxService.SetSettingText(item.Key, value);
        _gsxService.PersistSettingValue(item, value);
    }

    private bool ShouldCommit(GsxService.GsxSettingItem item, string value)
    {
        if (string.IsNullOrWhiteSpace(item.Key))
            return false;

        if (_lastCommittedValues.TryGetValue(item.Key, out string? lastValue)
            && string.Equals(lastValue, value, StringComparison.Ordinal))
        {
            return false;
        }

        _lastCommittedValues[item.Key] = value;
        return true;
    }

    // Record a control's INITIAL value as already-committed so the
    // FormClosing CommitAllSettings pass only writes values the user
    // actually changed. Without this, ShouldCommit treats every never-
    // touched key as new and closing the window rewrites the whole GSX
    // config through lossy UI<->INI round-trips.
    private void SeedCommitted(GsxService.GsxSettingItem item, string value)
    {
        if (!string.IsNullOrWhiteSpace(item.Key))
            _lastCommittedValues[item.Key] = value;
    }

    private void SelectFirstInput()
    {
        if (_settingsHost.Controls.Count == 0)
            return;

        foreach (Control control in _settingsHost.Controls)
        {
            if (SelectFirstInput(control))
                return;
        }
    }

    private void SelectSectionList()
    {
        if (_tabSelector.Items.Count > 0)
            _tabSelector.Select();
    }

    private static bool SelectFirstInput(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child.CanSelect && child is not Label)
            {
                child.Select();
                return true;
            }

            if (SelectFirstInput(child))
                return true;
        }

        return false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) =>
        Math.Min(Math.Max(value, min), max);

    private static int DecimalPlaces(double step)
    {
        string text = step.ToString(CultureInfo.InvariantCulture);
        int dot = text.IndexOf('.');
        return dot < 0 ? 0 : Math.Min(3, text.Length - dot - 1);
    }

    private static double ParseDouble(string value) =>
        TryParseBooleanLike(value, out double booleanValue)
            ? booleanValue
            :
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0;

    private static bool TryParseBooleanLike(string value, out double parsed)
    {
        parsed = 0;
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
        {
            parsed = 1;
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private sealed class ChoiceItem
    {
        public double Value { get; }
        public string Text { get; }

        public ChoiceItem(GsxService.GsxSettingChoice choice)
        {
            Value = choice.Value;
            Text = choice.Label;
        }

        public override string ToString() => Text;
    }

    private sealed class SettingsSlider : TrackBar
    {
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (ApplyKey(keyData & Keys.KeyCode))
                return true;

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (ApplyKey(e.KeyCode))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
        }

        private bool ApplyKey(Keys keyCode)
        {
            int next = keyCode switch
            {
                Keys.Left or Keys.Down => Value - SmallChange,
                Keys.Right or Keys.Up => Value + SmallChange,
                Keys.PageDown => Value - LargeChange,
                Keys.PageUp => Value + LargeChange,
                Keys.Home => Minimum,
                Keys.End => Maximum,
                _ => int.MinValue
            };

            if (next == int.MinValue)
                return false;

            int clamped = Math.Clamp(next, Minimum, Maximum);
            if (Value != clamped)
            {
                Value = clamped;
            }

            return true;
        }
    }
}
