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
// all and put every field straight on the tab.
// GsxSettingsSchemaSignature.BuildPages (bottom of this file) renders both --
// see its remarks. The same helper owns the STRUCTURAL signature RefreshSchema
// uses to tell "GSX echoed a value I just set" (apply in place, keep focus)
// from "the settings tree itself changed" (rebuild the pages).
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

    // Cached alongside _schema, and ONLY ever assigned together with it (see
    // SetSchema): the flattened pages PopulateSettings renders from, and the
    // structural signature RefreshSchema compares an incoming schema against.
    // HasFields used to recompute BuildPages on every read and RefreshSchema
    // compared two freshly built signatures, so one republish walked the whole
    // schema up to five times (HasFields x2 in AccessGSXForm.OnSettingsChangedUi,
    // signature x2, PopulateSettings x1). Now an incoming schema is walked once.
    private IReadOnlyList<GsxSettingsPage> _pages;
    private string _structuralSignature;

    private readonly ListBox _tabSelector = new();
    private readonly Panel _settingsHost = new();

    // One slot per field of _pages, in BuildPages traversal order (null for a
    // field that renders no updatable control -- Separator, Unknown, a Choice
    // with no choices renders a read-only box which IS updatable). Built by
    // PopulateSettings; consumed by ApplyValuesInPlace when a republished
    // schema is structurally identical, so its new values can be pushed into
    // the EXISTING controls without disposing them or moving focus. Positional
    // rather than keyed on GsxSettingsField.Key because Info fields carry no
    // key at all (four in the live capture) and would all collide on "".
    // Same structural signature => same page count, same field count per
    // page, same field types in the same order, so lockstep is exact.
    private readonly List<Action<GsxSettingsField>?> _valueAppliers = new();

    // True while ApplyValuesInPlace is writing GSX's own values into the
    // controls. Every commit handler (CheckedChanged / SelectedIndexChanged /
    // ValueChanged) checks it, so a value that CAME from GSX is never echoed
    // back to GSX as a settings.set -- that would round-trip forever.
    private bool _applyingRemoteValues;

    // CheckBox/ComboBox/NumericUpDown commit live, the instant the control
    // changes. A TextBox only commits on Leave (or Enter) -- if the pilot is
    // still focused in one when its control is about to be disposed, Leave
    // never fires. Both places that dispose these controls -- FormClosing
    // AND a live RefreshSchema rebuild -- flush through FlushPendingTextEdits
    // so an in-progress edit is never silently lost either way.
    //
    // LastCommitted is what makes that flush safe. Seeded with the value GSX
    // itself published, it marks a box the pilot never touched as clean, so the
    // flush writes only genuine edits. Without it the flush wrote EVERY tracked
    // box -- and since it runs BEFORE _schema is reassigned, a value GSX had
    // just changed would be overwritten with the stale text still on screen,
    // silently undoing GSX's own change.
    private sealed class TextCommit(TextBox box, string key, string committed)
    {
        public TextBox Box { get; } = box;
        public string Key { get; } = key;
        public string LastCommitted { get; set; } = committed;
    }

    private readonly List<TextCommit> _pendingTextCommits = new();

    // Defensive re-entrancy guard for RefreshSchema -- see its remarks.
    private bool _isRefreshingSchema;

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
        SetSchema(schema ?? GsxSettingsSchema.Empty);

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

    // Reads the pages cached from the SAME BuildPages traversal PopulateSettings
    // renders from -- never a separate _schema.AllFields() check -- so "has
    // fields" can never drift from "would actually show something".
    public bool HasFields => _pages.Count > 0;

    /// <summary>
    /// The one place <see cref="_schema"/> is assigned, so its two derived
    /// caches can never be observed out of step with it. Walks the schema
    /// exactly once (BuildPages), and the signature is built from those same
    /// pages -- see <see cref="GsxSettingsSchemaSignature"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_schema), nameof(_pages), nameof(_structuralSignature))]
    private void SetSchema(GsxSettingsSchema schema)
    {
        IReadOnlyList<GsxSettingsPage> pages = GsxSettingsSchemaSignature.BuildPages(schema);
        SetSchema(schema, pages, GsxSettingsSchemaSignature.Structural(pages));
    }

    /// <summary>Overload for a caller (RefreshSchema) that has already walked the schema once and must not walk it again.</summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_schema), nameof(_pages), nameof(_structuralSignature))]
    private void SetSchema(GsxSettingsSchema schema, IReadOnlyList<GsxSettingsPage> pages, string structuralSignature)
    {
        _schema = schema;
        _pages = pages;
        _structuralSignature = structuralSignature;
    }

    /// <summary>
    /// Replace the displayed settings with a freshly published schema. Two
    /// outcomes, decided by the STRUCTURAL signature (everything but the live
    /// values -- <see cref="GsxSettingsSchemaSignature"/>):
    ///
    /// Structure unchanged: the new values are written into the EXISTING
    /// controls in place (<see cref="ApplyValuesInPlace"/>) -- nothing is
    /// disposed, focus does not move, and this returns false. This is by far
    /// the common case, because GSX ECHOES every settings.set back as a
    /// /settings patch: a checkbox tick, a combo pick, and EACH arrow-step of
    /// a NumericUpDown (ValueChanged sends per tick, no debounce) all come
    /// straight back as a republished schema. When the live values were part
    /// of the signature, every one of those rebuilt the whole window and
    /// SelectSectionList() yanked screen-reader focus off the very field the
    /// pilot was still adjusting -- range controls were effectively unusable.
    ///
    /// Structure changed (a field/tab/choice/button/bound appeared, vanished
    /// or was relabelled -- including the empty -> populated transition
    /// AccessGSXForm announces as "GSX settings loaded."): the pages are
    /// rebuilt, which genuinely disposes controls, so the previous tab is
    /// restored and focus is put on the sections list. Returns true ONLY in
    /// this case -- AccessGSXForm reads the return as "a rebuild happened".
    ///
    /// Re-entrancy: FlushPendingTextEdits below calls GsxService.SetSettingText,
    /// which is a fire-and-forget GsxRemoteConnection.Send -- it returns after
    /// writing the outgoing frame and never itself processes a reply.
    /// Any GSX response arrives later on the WebSocket receive loop
    /// (GsxRemoteConnection.ReceiveLoopAsync, a background Task), which
    /// reposts to the UI thread via Control.BeginInvoke (GsxService.
    /// EnsureUiThread) rather than calling back in-line -- so a settings
    /// patch that SetSettingText's write eventually provokes can only
    /// re-enter here on a LATER, separate message-loop turn, never on this
    /// call stack. _isRefreshingSchema guards it anyway: if that reasoning
    /// is ever invalidated by a future transport change, a same-stack
    /// re-entry must skip rebuilding rather than flush/dispose the very
    /// controls this call is still in the middle of tearing down.
    /// </summary>
    public bool RefreshSchema(GsxSettingsSchema? schema)
    {
        if (_isRefreshingSchema)
            return false;

        schema ??= GsxSettingsSchema.Empty;
        // One BuildPages for the incoming schema, zero for the current one
        // (its signature is cached by SetSchema).
        IReadOnlyList<GsxSettingsPage> incomingPages = GsxSettingsSchemaSignature.BuildPages(schema);
        string incomingSignature = GsxSettingsSchemaSignature.Structural(incomingPages);

        _isRefreshingSchema = true;
        try
        {
            if (string.Equals(incomingSignature, _structuralSignature, StringComparison.Ordinal)
                && ApplyValuesInPlace(incomingPages))
            {
                // Same structure: the caches are refreshed WITHOUT re-walking
                // (the pages were just built and the signature is identical),
                // so a later comparison is against the latest values.
                SetSchema(schema, incomingPages, incomingSignature);
                return false;
            }

            // A live republish is about to dispose every field control,
            // including any TextBox the pilot is still typing in -- flush
            // it first, the same as FormClosing, or those keystrokes vanish
            // with no error and nothing to show it happened.
            FlushPendingTextEdits();

            // A live republish while the pilot is on (say) the Diagnostic
            // tab shouldn't yank them back to the first one -- restore the
            // same index if it's still in range. PopulateSettings still
            // selects 0 on the very first build (restoreTabIndex's
            // default), which is the only time there's nothing sensible to
            // restore.
            int previousTabIndex = _tabSelector.SelectedIndex;

            SetSchema(schema, incomingPages, incomingSignature);
            PopulateSettings(previousTabIndex);
            SelectSectionList();
            return true;
        }
        finally
        {
            _isRefreshingSchema = false;
        }
    }

    /// <summary>
    /// Writes the values of a structurally identical schema into the controls
    /// already on screen, walking <see cref="_valueAppliers"/> in lockstep
    /// with the incoming pages' fields. Returns false -- meaning "fall back
    /// to a rebuild" -- only if the two somehow disagree in length, which an
    /// equal structural signature rules out; the check is a belt-and-braces
    /// guard, not an expected path.
    ///
    /// Rules, per control (each applier is built beside its control):
    /// a control the pilot is CURRENTLY IN (ContainsFocus -- NumericUpDown is
    /// a container whose inner edit box holds the focus, so plain Focused
    /// would miss it) is never written to. The pilot's own input is the truth
    /// there and GSX's echo only confirms it; writing the echo back would
    /// clobber a half-typed number or text, and -- because a fast run of
    /// arrow-presses outruns the echoes -- would set the control BACK to an
    /// already-passed value between two presses, so holding Up would fight
    /// the pilot's own hand. The next patch after focus has moved on (GSX
    /// echoes every settings.set of every field) reconciles it. A TextBox is
    /// additionally left alone while it holds an uncommitted edit (Text !=
    /// LastCommitted) even unfocused, and moving LastCommitted with the
    /// applied text keeps FlushPendingTextEdits from later re-sending GSX's
    /// own value back. Read-only value boxes (Info/Action, and a Choice with
    /// no choices) always take the new text -- there is no in-progress edit
    /// to protect. Choice: matching choice or leave the selection. Range:
    /// clamped to the control's current bounds (structurally the same bounds
    /// GSX published), then set.
    /// </summary>
    private bool ApplyValuesInPlace(IReadOnlyList<GsxSettingsPage> pages)
    {
        int slot = 0;
        foreach (GsxSettingsPage page in pages)
            slot += page.Fields.Count;
        if (slot != _valueAppliers.Count)
            return false;

        _applyingRemoteValues = true;
        try
        {
            slot = 0;
            foreach (GsxSettingsPage page in pages)
            {
                foreach (GsxSettingsField field in page.Fields)
                    _valueAppliers[slot++]?.Invoke(field);
            }
        }
        finally
        {
            _applyingRemoteValues = false;
        }
        return true;
    }

    // Shared by FormClosing and RefreshSchema -- both dispose the live
    // TextBox controls (FormClosing by tearing down the whole window,
    // RefreshSchema by rebuilding the page panels) and must flush the same
    // way, or one of the two paths silently drops an in-progress edit. Never
    // announces anything -- these are the same commits a Leave/Enter would
    // have made, just triggered a moment early by the control going away.
    private void FlushPendingTextEdits()
    {
        foreach (var commit in _pendingTextCommits)
            CommitText(commit);
    }

    /// <summary>
    /// Sends one text field to GSX, but only when it actually differs from what
    /// was last sent (or from what GSX published, for a box the pilot never
    /// edited). The single write path for all three triggers -- Leave, Enter,
    /// and the flush -- so "duplicate writes suppressed" is one rule, not three.
    /// Never announces: a text edit is a direct UI interaction.
    /// </summary>
    private void CommitText(TextCommit commit)
    {
        if (string.Equals(commit.Box.Text, commit.LastCommitted, StringComparison.Ordinal))
            return;

        commit.LastCommitted = commit.Box.Text;
        _gsxService.SetSettingText(commit.Key, commit.Box.Text);
    }

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

        FormClosing += (_, _) => FlushPendingTextEdits();
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
        _valueAppliers.Clear();

        IReadOnlyList<GsxSettingsPage> pages = _pages;

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

        foreach (GsxSettingsPage page in pages)
            _tabSelector.Items.Add(new TabPageItem(page.Title, BuildPagePanel(page.Fields)));

        _tabSelector.SelectedIndex = restoreTabIndex >= 0 && restoreTabIndex < _tabSelector.Items.Count
            ? restoreTabIndex
            : 0;
    }

    // A rendered field control plus the action that pushes a later
    // republished value of the SAME field into it in place (null when the
    // field renders nothing updatable). See ApplyValuesInPlace for the rules.
    private sealed record FieldControl(Control Control, Action<GsxSettingsField>? ApplyRemoteValue);

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

        // Exactly one _valueAppliers slot per field, renderable or not, so
        // the list stays in lockstep with BuildPages' field order (which is
        // what ApplyValuesInPlace walks).
        foreach (GsxSettingsField field in fields)
            _valueAppliers.Add(AppendField(panel, field));

        return panel;
    }

    /// <summary>Renders one field into <paramref name="panel"/> and returns its
    /// in-place value applier, or null when the field renders no updatable
    /// control (Separator, Unknown).</summary>
    private Action<GsxSettingsField>? AppendField(FlowLayoutPanel panel, GsxSettingsField field)
    {
        switch (field.Type)
        {
            case GsxFieldType.Separator:
                AppendSeparator(panel, field);
                return null;

            case GsxFieldType.Info:
            case GsxFieldType.Action:
                // Action's single button is synthesized into the same
                // Buttons list Info uses (GsxSettingsSchema.ParseFields) --
                // one render path covers both.
                return AppendInfoOrAction(panel, field);

            case GsxFieldType.Toggle:
                return AppendControlField(panel, field, BuildToggle(field));

            case GsxFieldType.Choice:
                return AppendControlField(panel, field, BuildChoice(field));

            case GsxFieldType.Range:
                return AppendControlField(panel, field, BuildRange(field));

            case GsxFieldType.Text:
                return AppendControlField(panel, field, BuildText(field));

            default:
                // Unknown -- GSX published a field type this build doesn't
                // recognize yet. Nothing sensible to render; skip it rather
                // than guess.
                return null;
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

    private static Action<GsxSettingsField>? AppendControlField(FlowLayoutPanel panel, GsxSettingsField field, FieldControl built)
    {
        panel.Controls.Add(new Label
        {
            Text = field.Label,
            Width = RowWidth,
            Height = 20,
            AutoEllipsis = true,
            Margin = new Padding(3, 6, 3, 0)
        });

        Control control = built.Control;
        control.Width = RowWidth;
        control.Margin = new Padding(3, 0, 3, FieldSpacing);
        panel.Controls.Add(control);
        return built.ApplyRemoteValue;
    }

    private Action<GsxSettingsField> AppendInfoOrAction(FlowLayoutPanel panel, GsxSettingsField field)
    {
        panel.Controls.Add(new Label
        {
            Text = field.Label,
            Width = RowWidth,
            Height = 20,
            AutoEllipsis = true,
            Margin = new Padding(3, 6, 3, 0)
        });

        FieldControl built = BuildReadOnlyValueBox(field);
        Control valueBox = built.Control;
        valueBox.Width = RowWidth;
        valueBox.Margin = new Padding(3, 0, 3, field.Buttons.Count > 0 ? 2 : FieldSpacing);
        panel.Controls.Add(valueBox);

        // Button.Enabled comes from Buttons[i].Disabled, which is part of the
        // STRUCTURAL signature -- a change there rebuilds the page, so the
        // applier only ever needs to refresh the value box.
        Action<GsxSettingsField> apply = built.ApplyRemoteValue!;

        if (field.Buttons.Count == 0)
            return apply;

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
        return apply;
    }

    private static string ReadOnlyValueText(GsxSettingsField field) =>
        // Action fields carry no "value" on the wire (there's nothing to
        // show but the label + button), so this reads back empty for them
        // -- expected, not a bug.
        field.TextValue ?? field.NumericValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static FieldControl BuildReadOnlyValueBox(GsxSettingsField field)
    {
        var box = new TextBox
        {
            Text = ReadOnlyValueText(field),
            ReadOnly = true,
            AccessibleName = field.Label,
            AccessibleDescription = field.Tooltip
        };
        // Read-only: no in-progress edit to protect, so the new text is
        // always taken -- e.g. toggling "diagnostic log" makes GSX republish
        // "Log file location" with a real path, which the pilot expects to
        // find updated when they arrow to it.
        return new FieldControl(box, f => box.Text = ReadOnlyValueText(f));
    }

    private FieldControl BuildToggle(GsxSettingsField field)
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
        // _applyingRemoteValues covers the OTHER programmatic write, an
        // in-place apply of GSX's own value (ApplyValuesInPlace).
        checkBox.CheckedChanged += (_, _) =>
        {
            if (_applyingRemoteValues) return;
            _gsxService.SetSettingNumber(key, checkBox.Checked ? 1 : 0);
        };
        return new FieldControl(checkBox, f =>
        {
            if (checkBox.ContainsFocus) return;
            checkBox.Checked = (f.NumericValue ?? 0) != 0;
        });
    }

    private FieldControl BuildChoice(GsxSettingsField field)
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

        int initial = FindChoiceIndex(field.Choices, field.NumericValue);
        if (initial >= 0)
            combo.SelectedIndex = initial;

        combo.SelectedIndexChanged += (_, _) =>
        {
            if (_applyingRemoteValues) return;
            if (combo.SelectedItem is GsxSettingsChoice choice)
                _gsxService.SetSettingNumber(key, choice.Value);
        };
        return new FieldControl(combo, f =>
        {
            if (combo.ContainsFocus) return;
            // Choices are structural, so f.Choices is the same list the
            // combo was built from; a value matching no choice leaves the
            // current selection alone rather than blanking it.
            int index = FindChoiceIndex(f.Choices, f.NumericValue);
            if (index >= 0 && index < combo.Items.Count)
                combo.SelectedIndex = index;
        });
    }

    private static int FindChoiceIndex(IReadOnlyList<GsxSettingsChoice> choices, double? value)
    {
        double current = value ?? double.NaN;
        for (int i = 0; i < choices.Count; i++)
        {
            if (Math.Abs(choices[i].Value - current) < 0.000001)
                return i;
        }
        return -1;
    }

    private FieldControl BuildRange(GsxSettingsField field)
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

        numeric.ValueChanged += (_, _) =>
        {
            if (_applyingRemoteValues) return;
            _gsxService.SetSettingNumber(key, (double)numeric.Value);
        };
        return new FieldControl(numeric, f =>
        {
            // ContainsFocus, not Focused: the pilot's caret sits in the
            // NumericUpDown's INNER edit box, so the container itself never
            // reads Focused while they are typing/arrowing in it.
            if (numeric.ContainsFocus) return;
            // Clamp to the CURRENT bounds before assigning -- Value throws
            // outside [Minimum, Maximum], and Min/Max are structural so they
            // are the same bounds GSX published for this field.
            numeric.Value = Math.Clamp(
                GsxRangeBoundsResolver.ToDecimal(f.NumericValue ?? 0), numeric.Minimum, numeric.Maximum);
        });
    }

    private FieldControl BuildText(GsxSettingsField field)
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

        var commit = new TextCommit(textBox, key, textBox.Text);

        textBox.Leave += (_, _) => CommitText(commit);
        textBox.KeyDown += (_, e) =>
        {
            // Commit on Enter too, without waiting for the field to lose
            // focus -- and suppress it so the control doesn't also emit the
            // default "invalid input" system beep.
            if (e.KeyCode != Keys.Enter) return;
            CommitText(commit);
            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        _pendingTextCommits.Add(commit);
        return new FieldControl(textBox, f =>
        {
            // Never clobber an in-progress edit: skip while the pilot is in
            // the box, and skip while it holds text that has not been
            // committed yet (Text != LastCommitted). Otherwise take GSX's
            // value AND move LastCommitted with it, so the box reads as clean
            // -- or FlushPendingTextEdits/Leave would later send GSX's own
            // value straight back to it as an "edit".
            if (textBox.ContainsFocus) return;
            if (!string.Equals(textBox.Text, commit.LastCommitted, StringComparison.Ordinal)) return;
            string text = f.TextValue ?? string.Empty;
            textBox.Text = text;
            commit.LastCommitted = text;
        });
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
        // Never publish an inverted or zero-width range -- and never overflow widening it.
        // `lo + 1m` throws when lo is already decimal.MaxValue, which an inverted published
        // range of extreme values reaches (min above max, both out of double range); widen
        // downwards instead. Same family as the ToDecimal defect above: the guard has to hold
        // at the boundary it exists for.
        if (lo >= hi)
        {
            if (lo < decimal.MaxValue) hi = lo + 1m;
            else { hi = lo; lo -= 1m; }
        }

        decimal increment = step.HasValue && step.Value > 0 ? ToDecimal(step.Value) : 1m;
        int decimalPlaces = isFloat ? 3 : 0;
        decimal value = Math.Clamp(current, lo, hi);

        return new NumericRangeBounds(lo, hi, increment, decimalPlaces, value);
    }

    /// <summary>
    /// The exact bounds of <see cref="decimal"/>, written as <see cref="double"/> literals.
    ///
    /// <para>
    /// <c>(double)decimal.MaxValue</c> CANNOT be used here: the conversion rounds UP to 2^96,
    /// which is one greater than <c>decimal.MaxValue</c>, so clamping to it produced a value the
    /// following cast still could not represent. These literals are the nearest doubles that
    /// round DOWN into range. Measured boundary: 7.922816251426433e28 converts, 7.922816251426434e28
    /// (= 2^96) throws.
    /// </para>
    /// </summary>
    private const double DecimalMaxAsDouble = 7.922816251426433e28;
    private const double DecimalMinAsDouble = -7.922816251426433e28;

    /// <summary>
    /// Double -> decimal without an OverflowException at the extremes — which the previous
    /// version did not deliver. It read
    /// <c>(decimal)Math.Clamp(value, (double)decimal.MinValue, (double)decimal.MaxValue)</c>, and
    /// was byte-for-byte equivalent to a bare <c>(decimal)value</c> in every case: it threw on
    /// 1e30, on NaN and on Infinity exactly as the unguarded cast does, because the clamp BOUND
    /// itself rounded out of range (see <see cref="DecimalMaxAsDouble"/>) and because
    /// <c>Math.Clamp(NaN, …)</c> returns NaN. A helper whose entire purpose is to prevent an
    /// exception, prevented none, while its own doc comment asserted the opposite.
    ///
    /// <para>
    /// Non-finite input now yields 0 rather than throwing. GSX should never publish one —
    /// <c>GsxSettingsSchema.NumOrNull</c> rejects it at ingest, which is the real fix — but this
    /// is the last line before a <c>NumericUpDown</c> on the UI thread, and the throw path had no
    /// <c>catch</c> anywhere between here and <c>AccessGSXForm.OnSettingsChangedUi</c>: it
    /// unwound into the message pump, so one malformed field cost the whole settings window AND
    /// the rest of that GSX frame's announcement processing.
    /// </para>
    /// </summary>
    internal static decimal ToDecimal(double value) =>
        double.IsNaN(value) ? 0m
        : (decimal)Math.Clamp(value, DecimalMinAsDouble, DecimalMaxAsDouble);
}

internal readonly record struct NumericRangeBounds(
    decimal Minimum, decimal Maximum, decimal Increment, int DecimalPlaces, decimal Value);

/// <summary>One navigable section of the GSX settings window: the tab (or
/// tab - subtab) title and the fields rendered on it, in wire order.</summary>
internal sealed record GsxSettingsPage(string Title, IReadOnlyList<GsxSettingsField> Fields);

/// <summary>
/// The page traversal GsxSettingsForm renders from, and the STRUCTURAL
/// signature RefreshSchema uses to decide between "apply the new values into
/// the existing controls" and "dispose everything and rebuild". Both live
/// here, on the same page list, so "the signature changed" and "the rendered
/// content changed" can never drift apart. Record equality would not do:
/// every field type carries IReadOnlyList properties (Choices, Buttons) that
/// records compare by reference.
///
/// STRUCTURAL means everything that shapes a control at build time -- key,
/// type, label, tooltip, bounds, step, unit, float-ness, max length,
/// placeholder, the choice list, the button list including each button's
/// Disabled (rendered as Button.Enabled) -- and page title. It deliberately
/// EXCLUDES the live NumericValue/TextValue: GSX echoes every settings.set
/// back as a /settings patch, so with the values folded in every checkbox
/// tick, combo pick and NumericUpDown arrow-step read as a changed schema,
/// rebuilt the window and moved screen-reader focus off the field the pilot
/// was adjusting. Value changes are applied in place instead
/// (GsxSettingsForm.ApplyValuesInPlace).
///
/// ASCII control characters separate every piece so free text (Label,
/// Tooltip, Placeholder, ...) can never collide across a field boundary.
/// Internal, reached by GsxSettingsSchemaSignatureTests via
/// InternalsVisibleTo (Properties/InternalsVisibleTo.cs) -- same pattern as
/// GsxRangeBoundsResolver above.
/// </summary>
internal static class GsxSettingsSchemaSignature
{
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
    public static IReadOnlyList<GsxSettingsPage> BuildPages(GsxSettingsSchema schema)
    {
        var pages = new List<GsxSettingsPage>();
        foreach (GsxSettingsTab tab in schema.Tabs)
        {
            foreach (GsxSettingsSubtab subtab in tab.Subtabs)
            {
                if (!subtab.Fields.Any(IsRenderable)) continue;
                pages.Add(new GsxSettingsPage($"{tab.Label} - {subtab.Label}", subtab.Fields));
            }

            // Not observed in a live capture (every subtabbed tab today
            // carries no fields of its own), but the schema doesn't forbid
            // it -- give a tab's own fields a page instead of silently
            // dropping them if GSX ever publishes both at once.
            if (tab.Fields.Any(IsRenderable))
                pages.Add(new GsxSettingsPage(tab.Label, tab.Fields));
        }
        return pages;
    }

    private static bool IsRenderable(GsxSettingsField f) =>
        f.Type is not (GsxFieldType.Separator or GsxFieldType.Unknown);

    public static string Structural(GsxSettingsSchema schema) => Structural(BuildPages(schema));

    public static string Structural(IReadOnlyList<GsxSettingsPage> pages) =>
        string.Join("\x1e", pages.SelectMany(page =>
            page.Fields.Select(f => page.Title + "\x1f" + StructuralField(f))));

    private static string StructuralField(GsxSettingsField f) =>
        string.Join("\x1f",
            f.Key, f.Type.ToString(), f.Label, f.Tooltip,
            f.Min?.ToString(CultureInfo.InvariantCulture) ?? "",
            f.Max?.ToString(CultureInfo.InvariantCulture) ?? "",
            f.Step?.ToString(CultureInfo.InvariantCulture) ?? "",
            f.Unit, f.IsFloat.ToString(), f.MaxLength.ToString(CultureInfo.InvariantCulture), f.Placeholder,
            string.Join("\x1f", f.Choices.Select(c => c.Value.ToString(CultureInfo.InvariantCulture) + "\x1f" + c.Label)),
            string.Join("\x1f", f.Buttons.Select(b => b.Key + "\x1f" + b.Label + "\x1f" + b.Disabled)));
}
