// GsxSettingsForm — accessible editor for GSX's own settings page.
//
// Renders MSFSBlindAssist.Services.Gsx.Remote.GsxSettingsSchema, the typed
// settings model GSX's Couatl Remote API publishes (replaces the old
// settings.html scrape). GSX owns persistence now (settings.set / settings.action
// over the Remote API) -- this form never writes GSX's own config file.
//
// Tab shapes: GSX publishes a tab's fields in TWO shapes. Some tabs (e.g.
// "simulation") carry nothing directly and split their fields across
// subtabs; others (timings, audio, network, diagnostic) have no subtabs at
// all and put every field straight on the tab. BuildPages renders both --
// see its remarks.
using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Forms;

public sealed class GsxSettingsForm : Form
{
    // Visual column width shared by every field row (labels + controls) --
    // FlowLayoutPanel doesn't stretch children to fill available width the
    // way Dock does, so each child needs an explicit size.
    private const int RowWidth = 660;
    private const int FieldSpacing = 12;

    private readonly GsxService _gsxService;
    private GsxSettingsSchema _schema;
    private readonly ListBox _tabSelector = new();
    private readonly Panel _settingsHost = new();

    // CheckBox/ComboBox/NumericUpDown commit live, the instant the control
    // changes. A TextBox only commits on Leave (or Enter) -- if the pilot is
    // still focused in one when the window closes (Escape, the Close
    // button, Alt+F4), Leave never fires. FormClosing flushes these so an
    // in-progress edit is never silently lost.
    private readonly List<(TextBox Box, string Key)> _pendingTextCommits = new();

    public GsxSettingsForm(
        GsxService gsxService,
        ScreenReaderAnnouncer announcer,
        GsxSettingsSchema schema)
    {
        _gsxService = gsxService ?? throw new ArgumentNullException(nameof(gsxService));
        // Validated but not used directly: every control here commits
        // silently (screen readers already announce the user's own
        // CheckedChanged/SelectedIndexChanged/ValueChanged interaction --
        // see BuildToggle/BuildChoice/BuildRange below), and the one
        // background announcement this feature makes ("GSX settings
        // loaded.") is owned by AccessGSXForm, which drives this form's
        // lifetime. Kept for constructor parity with the rest of the GSX UI
        // and in case a future background announcement needs it.
        _ = announcer ?? throw new ArgumentNullException(nameof(announcer));
        _schema = schema ?? GsxSettingsSchema.Empty;

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

    // Reuses BuildPages -- the exact traversal PopulateSettings renders
    // from -- rather than a separate _schema.AllFields() check, so "has
    // fields" can never drift from "would actually show something".
    public bool HasFields => BuildPages(_schema).Count > 0;

    /// <summary>
    /// Replace the displayed settings with a freshly published schema,
    /// rebuilding the UI only when the content actually changed. GSX can
    /// republish the whole settings tree more than once per session (a
    /// reconnect resends it as part of a full snapshot) -- rebuilding in
    /// place instead of recreating the window keeps screen-reader focus and
    /// avoids re-announcing the whole dialog. Returns true when the UI was
    /// rebuilt.
    /// </summary>
    public bool RefreshSchema(GsxSettingsSchema? schema)
    {
        schema ??= GsxSettingsSchema.Empty;
        if (BuildSchemaSignature(schema) == BuildSchemaSignature(_schema))
            return false;

        // A live republish while the pilot is on (say) the Diagnostic tab
        // shouldn't yank them back to the first one -- restore the same
        // index if it's still in range. PopulateSettings still selects 0 on
        // the very first build (restoreTabIndex's default), which is the
        // only time there's nothing sensible to restore.
        int previousTabIndex = _tabSelector.SelectedIndex;

        _schema = schema;
        PopulateSettings(previousTabIndex);
        SelectSectionList();
        return true;
    }

    // Record equality won't help here: every field type carries
    // IReadOnlyList properties (Choices, Buttons) that records compare by
    // reference. Built from the SAME BuildPages a real rebuild renders, so
    // "the signature changed" and "the rendered content changed" can never
    // drift apart. ASCII control characters separate every piece so free
    // text (Label, Tooltip, Placeholder, ...) can never collide across a
    // field boundary.
    private static string BuildSchemaSignature(GsxSettingsSchema schema) =>
        string.Join("\x1e", BuildPages(schema).SelectMany(page =>
            page.Fields.Select(f => page.Title + "\x1f" + FieldSignature(f))));

    private static string FieldSignature(GsxSettingsField f) =>
        string.Join("\x1f",
            f.Key, f.Type.ToString(), f.Label, f.Tooltip,
            f.NumericValue?.ToString(CultureInfo.InvariantCulture) ?? "",
            f.TextValue ?? "",
            f.Min?.ToString(CultureInfo.InvariantCulture) ?? "",
            f.Max?.ToString(CultureInfo.InvariantCulture) ?? "",
            f.Step?.ToString(CultureInfo.InvariantCulture) ?? "",
            f.Unit, f.IsFloat.ToString(), f.MaxLength.ToString(CultureInfo.InvariantCulture), f.Placeholder,
            string.Join("\x1f", f.Choices.Select(c => c.Value.ToString(CultureInfo.InvariantCulture) + "\x1f" + c.Label)),
            string.Join("\x1f", f.Buttons.Select(b => b.Key + "\x1f" + b.Label + "\x1f" + b.Disabled)));

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
        _tabSelector.AccessibleName = "Settings sections";
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

        FormClosing += (_, _) =>
        {
            foreach ((TextBox box, string key) in _pendingTextCommits)
                _gsxService.SetSettingText(key, box.Text);
        };
    }

    private void PopulateSettings(int restoreTabIndex = 0)
    {
        // Controls.Clear()/Items.Clear() below only drop the parent-child
        // reference -- they don't Dispose. A page can be rebuilt many times
        // over a long session (every live GSX settings republish), so
        // explicitly dispose the outgoing pages (which cascades to every
        // field control they hold) or their window/GDI handles accumulate.
        foreach (TabPageItem entry in _tabSelector.Items.OfType<TabPageItem>())
            entry.Panel.Dispose();

        _tabSelector.Items.Clear();
        _settingsHost.Controls.Clear();
        _pendingTextCommits.Clear();

        List<SettingsPage> pages = BuildPages(_schema);

        if (pages.Count == 0)
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(12)
            };

            // Read-only TextBox, not a Label -- a Label has no tab stop, so
            // a screen-reader user could never reach this message at all.
            panel.Controls.Add(new TextBox
            {
                Text = "No GSX settings were available.",
                ReadOnly = true,
                Width = RowWidth,
                AccessibleName = "GSX settings status"
            });

            _tabSelector.Items.Add(new TabPageItem("Settings", panel));
            _tabSelector.SelectedIndex = 0;
            return;
        }

        foreach (SettingsPage page in pages)
            _tabSelector.Items.Add(new TabPageItem(page.Title, BuildPagePanel(page.Fields)));

        _tabSelector.SelectedIndex = restoreTabIndex >= 0 && restoreTabIndex < _tabSelector.Items.Count
            ? restoreTabIndex
            : 0;
    }

    private sealed record SettingsPage(string Title, IReadOnlyList<GsxSettingsField> Fields);

    /// <summary>
    /// Flattens the schema's tab/subtab tree into one page per navigable
    /// section, rendering BOTH shapes GSX publishes. A live capture has 5
    /// top-level tabs: "simulation" carries no fields of its own and splits
    /// 42 across 4 subtabs (Services/Pushback/Parking/UI); "timings",
    /// "audio", "network" and "diagnostic" have no subtabs and carry their
    /// 39 fields directly on the tab. An earlier version of this reader
    /// walked subtabs only and silently dropped those 39 fields -- four
    /// whole tabs worth of settings never shown.
    ///
    /// A subtab/tab contributes a page only when it has at least one field
    /// AppendField will actually render a control for -- Separator and
    /// Unknown fields render nothing (AppendField skips both), so a
    /// section holding only those would otherwise become a tab the pilot
    /// can select but that shows nothing once they arrow into it. Not
    /// reachable with a live capture today (every observed section mixes
    /// separators in among real controls), but cheap to rule out.
    /// </summary>
    private static List<SettingsPage> BuildPages(GsxSettingsSchema schema)
    {
        var pages = new List<SettingsPage>();
        foreach (GsxSettingsTab tab in schema.Tabs)
        {
            foreach (GsxSettingsSubtab subtab in tab.Subtabs)
            {
                if (!subtab.Fields.Any(IsRenderable)) continue;
                pages.Add(new SettingsPage($"{tab.Label} - {subtab.Label}", subtab.Fields));
            }

            // Not observed in a live capture (every subtabbed tab today
            // carries no fields of its own), but the schema doesn't forbid
            // it -- give a tab's own fields a page instead of silently
            // dropping them if GSX ever publishes both at once.
            if (tab.Fields.Any(IsRenderable))
                pages.Add(new SettingsPage(tab.Label, tab.Fields));
        }
        return pages;
    }

    private static bool IsRenderable(GsxSettingsField f) =>
        f.Type is not (GsxFieldType.Separator or GsxFieldType.Unknown);

    private FlowLayoutPanel BuildPagePanel(IReadOnlyList<GsxSettingsField> fields)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12)
        };

        foreach (GsxSettingsField field in fields)
            AppendField(panel, field);

        return panel;
    }

    private void AppendField(FlowLayoutPanel panel, GsxSettingsField field)
    {
        switch (field.Type)
        {
            case GsxFieldType.Separator:
                AppendSeparator(panel, field);
                break;

            case GsxFieldType.Info:
            case GsxFieldType.Action:
                // Action's single button is synthesized into the same
                // Buttons list Info uses (GsxSettingsSchema.ParseFields) --
                // one render path covers both.
                AppendInfoOrAction(panel, field);
                break;

            case GsxFieldType.Toggle:
                AppendControlField(panel, field, BuildToggle(field));
                break;

            case GsxFieldType.Choice:
                AppendControlField(panel, field, BuildChoice(field));
                break;

            case GsxFieldType.Range:
                AppendControlField(panel, field, BuildRange(field));
                break;

            case GsxFieldType.Text:
                AppendControlField(panel, field, BuildText(field));
                break;

            default:
                // Unknown -- GSX published a field type this build doesn't
                // recognize yet. Nothing sensible to render; skip it rather
                // than guess.
                break;
        }
    }

    private void AppendSeparator(FlowLayoutPanel panel, GsxSettingsField field)
    {
        if (string.IsNullOrWhiteSpace(field.Label))
            return; // Pure spacing -- the surrounding fields' own margins already separate groups.

        panel.Controls.Add(new Label
        {
            Text = field.Label,
            Width = RowWidth,
            Height = 22,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(3, 14, 3, 4)
        });
    }

    private static void AppendControlField(FlowLayoutPanel panel, GsxSettingsField field, Control control)
    {
        panel.Controls.Add(new Label
        {
            Text = field.Label,
            Width = RowWidth,
            Height = 20,
            AutoEllipsis = true,
            Margin = new Padding(3, 6, 3, 0)
        });

        control.Width = RowWidth;
        control.Margin = new Padding(3, 0, 3, FieldSpacing);
        panel.Controls.Add(control);
    }

    private void AppendInfoOrAction(FlowLayoutPanel panel, GsxSettingsField field)
    {
        panel.Controls.Add(new Label
        {
            Text = field.Label,
            Width = RowWidth,
            Height = 20,
            AutoEllipsis = true,
            Margin = new Padding(3, 6, 3, 0)
        });

        TextBox valueBox = BuildReadOnlyValueBox(field);
        valueBox.Width = RowWidth;
        valueBox.Margin = new Padding(3, 0, 3, field.Buttons.Count > 0 ? 2 : FieldSpacing);
        panel.Controls.Add(valueBox);

        if (field.Buttons.Count == 0)
            return;

        var buttonsRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = RowWidth,
            Margin = new Padding(3, 0, 3, FieldSpacing)
        };

        foreach (GsxSettingsButton button in field.Buttons)
        {
            string buttonKey = button.Key;
            var btn = new Button
            {
                Text = button.Label,
                Enabled = !button.Disabled,
                AutoSize = true,
                // The button's own label (e.g. "Open Log") is what makes it
                // distinguishable from any sibling buttons on the same
                // field -- field.Label ("Diagnostic log") names the group,
                // not the individual action.
                AccessibleName = button.Label,
                AccessibleDescription = field.Tooltip,
                Margin = new Padding(0, 0, 8, 4)
            };
            btn.Click += (_, _) => _gsxService.PulseSettingAction(buttonKey);
            buttonsRow.Controls.Add(btn);
        }

        panel.Controls.Add(buttonsRow);
    }

    private static TextBox BuildReadOnlyValueBox(GsxSettingsField field) => new()
    {
        // Action fields carry no "value" on the wire (there's nothing to
        // show but the label + button), so this reads back empty for them
        // -- expected, not a bug.
        Text = field.TextValue ?? field.NumericValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        ReadOnly = true,
        AccessibleName = field.Label,
        AccessibleDescription = field.Tooltip
    };

    private Control BuildToggle(GsxSettingsField field)
    {
        string key = field.Key;
        var checkBox = new CheckBox
        {
            Text = string.Empty,
            // NumericValue is nullable; a missing value defaults to
            // unchecked rather than (NumericValue != 0 with null on the
            // left) reading a genuinely-absent value as checked.
            Checked = (field.NumericValue ?? 0) != 0,
            AccessibleName = field.Label,
            AccessibleDescription = field.Tooltip
        };
        // Wiring the handler AFTER the initializer means the initial
        // Checked assignment above can never fire it -- no seed/dedup
        // bookkeeping needed to stop an "echo" write back to GSX on open.
        checkBox.CheckedChanged += (_, _) => _gsxService.SetSettingNumber(key, checkBox.Checked ? 1 : 0);
        return checkBox;
    }

    private Control BuildChoice(GsxSettingsField field)
    {
        if (field.Choices.Count == 0)
            return BuildReadOnlyValueBox(field); // Nothing to pick from -- never render an empty combo.

        string key = field.Key;
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(GsxSettingsChoice.Label),
            AccessibleName = field.Label,
            AccessibleDescription = field.Tooltip
        };

        foreach (GsxSettingsChoice choice in field.Choices)
            combo.Items.Add(choice);

        double current = field.NumericValue ?? double.NaN;
        for (int i = 0; i < field.Choices.Count; i++)
        {
            if (Math.Abs(field.Choices[i].Value - current) < 0.000001)
            {
                combo.SelectedIndex = i;
                break;
            }
        }

        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedItem is GsxSettingsChoice choice)
                _gsxService.SetSettingNumber(key, choice.Value);
        };
        return combo;
    }

    private Control BuildRange(GsxSettingsField field)
    {
        string key = field.Key;
        NumericRangeBounds bounds = GsxRangeBoundsResolver.Resolve(
            field.Min, field.Max, field.Step, field.NumericValue ?? 0, field.IsFloat);

        var numeric = new NumericUpDown
        {
            // Minimum/Maximum before Value: NumericUpDown.Value throws if
            // assigned outside the CURRENT [Minimum, Maximum] at the time
            // of assignment (Minimum/Maximum themselves self-adjust to stay
            // consistent with each other regardless of order).
            Minimum = bounds.Minimum,
            Maximum = bounds.Maximum,
            Increment = bounds.Increment,
            DecimalPlaces = bounds.DecimalPlaces,
            Value = bounds.Value,
            AccessibleName = string.IsNullOrWhiteSpace(field.Unit) ? field.Label : $"{field.Label} ({field.Unit})",
            AccessibleDescription = field.Tooltip
        };

        numeric.ValueChanged += (_, _) => _gsxService.SetSettingNumber(key, (double)numeric.Value);
        return numeric;
    }

    private Control BuildText(GsxSettingsField field)
    {
        string key = field.Key;
        var textBox = new TextBox
        {
            Text = field.TextValue ?? string.Empty,
            MaxLength = field.MaxLength, // 0 when GSX doesn't publish one -- WinForms treats 0 as "no limit".
            PlaceholderText = field.Placeholder,
            AccessibleName = field.Label,
            AccessibleDescription = field.Tooltip
        };

        textBox.Leave += (_, _) => _gsxService.SetSettingText(key, textBox.Text);
        textBox.KeyDown += (_, e) =>
        {
            // Commit on Enter too, without waiting for the field to lose
            // focus -- and suppress it so the control doesn't also emit the
            // default "invalid input" system beep.
            if (e.KeyCode != Keys.Enter) return;
            _gsxService.SetSettingText(key, textBox.Text);
            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        _pendingTextCommits.Add((textBox, key));
        return textBox;
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
        if (_tabSelector.SelectedItem is not TabPageItem entry)
            return;

        _settingsHost.SuspendLayout();
        _settingsHost.Controls.Clear();
        _settingsHost.Controls.Add(entry.Panel);
        _settingsHost.ResumeLayout();
    }

    private void SelectSectionList()
    {
        if (_tabSelector.Items.Count > 0)
            _tabSelector.Select();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            // An open choice dropdown owns Escape (closes the dropdown);
            // only close the window when nothing is dropped down.
            if (ActiveControl is ComboBox { DroppedDown: true })
                return base.ProcessCmdKey(ref msg, keyData);
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private sealed class TabPageItem
    {
        public string Title { get; }
        public FlowLayoutPanel Panel { get; }

        public TabPageItem(string title, FlowLayoutPanel panel)
        {
            Title = title;
            Panel = panel;
        }

        // ListBox displays each item via ToString() by default.
        public override string ToString() => Title;
    }
}

/// <summary>
/// Resolves a NumericUpDown's Minimum/Maximum/Increment/starting Value from
/// GSX's Min/Max/Step, which are nullable -- GSX can genuinely omit a bound
/// (distinct from publishing 0). A naive `?? 0` fallback would collapse a
/// missing bound into an unusable 0..0 range; this widens to a generous
/// fallback span instead, and widens that further if needed so the field's
/// own current value is never clamped out of range on first show. Every
/// real field in a live GSX capture publishes all three, so this path is
/// defensive rather than commonly exercised -- kept internal + covered
/// directly by GsxRangeBoundsResolverTests via InternalsVisibleTo.
/// </summary>
internal static class GsxRangeBoundsResolver
{
    // Wide enough to be practically unlimited for any real GSX setting
    // while staying comfortably inside decimal's range.
    private const decimal FallbackFloor = -1_000_000m;
    private const decimal FallbackCeiling = 1_000_000m;

    public static NumericRangeBounds Resolve(double? min, double? max, double? step, double currentValue, bool isFloat)
    {
        decimal current = ToDecimal(currentValue);
        decimal lo = min.HasValue ? ToDecimal(min.Value) : Math.Min(FallbackFloor, current);
        decimal hi = max.HasValue ? ToDecimal(max.Value) : Math.Max(FallbackCeiling, current);
        if (lo >= hi)
            hi = lo + 1m; // Never publish an inverted or zero-width range.

        decimal increment = step.HasValue && step.Value > 0 ? ToDecimal(step.Value) : 1m;
        int decimalPlaces = isFloat ? 3 : 0;
        decimal value = Math.Clamp(current, lo, hi);

        return new NumericRangeBounds(lo, hi, increment, decimalPlaces, value);
    }

    private static decimal ToDecimal(double value) =>
        (decimal)Math.Clamp(value, (double)decimal.MinValue, (double)decimal.MaxValue);
}

internal readonly record struct NumericRangeBounds(
    decimal Minimum, decimal Maximum, decimal Increment, int DecimalPlaces, decimal Value);
