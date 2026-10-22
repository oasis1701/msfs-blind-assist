using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The "Fenix options" window (Fenix windows list, Ctrl+Shift+F in input mode): every Fenix EFB option with
/// its value, as a tree of groups, the options that change how the app works first. Enter on an option speaks
/// its details; F5 reads the options again and says how many changed; F2 changes an on/off or choice option
/// for this simulator session only, as the Fenix EFB's own toggle does.
/// </summary>
/// <remarks>
/// Every request is awaited from a UI-thread handler, so continuations resume on the UI thread; nothing runs on
/// a pool thread and nothing subscribes, so nothing marshals. One read or change at a time
/// (<see cref="_loading"/>, <see cref="_changing"/>).
/// A read that keeps every group and its options in the same order only rewrites the Text of the nodes, so
/// NVDA's place in the tree holds; any other read rebuilds the tree and reselects the same option or group.
/// A change is sent only for an option <see cref="FenixOptionEdits.CanChange"/> allows, with persist false,
/// and what is spoken afterwards is what the Fenix reads back, never what was asked for.
/// Nothing here logs an option's value: keys and counts only.
/// </remarks>
public sealed class FenixOptionsForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int WM_KEYDOWN = 0x0100;
    private const string LogCategory = "FenixOptions";

    // Marks the one child a group carries until it is first expanded.
    private const string PlaceholderTag = "placeholder";

    // Whether options can be changed from this window; the status box says which.
    private const bool ChangesAllowed = true;

    // How long the Fenix gets to apply a change before it is read back a second time.
    private const int ReadBackRetryMilliseconds = 500;

    // What F5 and F2 say while a read or a change is running.
    private const string StillLoadingText = "Still loading.";

    private readonly FenixDataService _data;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly CancellationTokenSource _cts = new();

    private NativeAccessibleTreeView _tree = null!;
    private TextBox _details = null!;
    private TextBox _status = null!;

    // The last read the Fenix answered (for F5's change count) and the groups the tree shows.
    private IReadOnlyList<FenixSetting>? _settings;
    private IReadOnlyList<FenixOptionGroup>? _groups;
    private bool _loading;
    private bool _changing;   // a change is being asked for, sent and read back
    private bool _tornDown;
    private IntPtr _previousWindow;

    public FenixOptionsForm(FenixDataService data, ScreenReaderAnnouncer announcer)
    {
        _data = data;
        _announcer = announcer;
        Build();
    }

    private void Build()
    {
        Text = "Fenix options";
        Size = new Size(640, 560);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(6), TabIndex = 0 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 6 * Font.Height + 12));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int tab = 0;

        // NativeAccessibleTreeView, never a plain tree (CORE-12), children filled on first expand; no check
        // boxes (CORE-18): an option's value is in its text.
        _tree = new NativeAccessibleTreeView
        {
            Dock = DockStyle.Fill,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            HideSelection = false,
            AccessibleName = "Fenix options",
            AccessibleRole = AccessibleRole.Outline,
            TabIndex = tab++,
        };
        _tree.BeforeExpand += OnBeforeExpand;
        _tree.AfterSelect += (_, _) => ShowDetails();
        _tree.KeyDown += OnTreeKeyDown;
        layout.Controls.Add(_tree, 0, 0);

        // Read-only TextBoxes, never Labels, so both are in the tab order ([VAT-13]).
        _details = new TextBox
        {
            ReadOnly = true,
            Multiline = true,
            TabStop = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            AccessibleName = "Details",
            TabIndex = tab++,
        };
        layout.Controls.Add(_details, 0, 1);

        // One line, so the reader says all of it on Tab.
        _status = new TextBox
        {
            ReadOnly = true,
            TabStop = true,
            Dock = DockStyle.Fill,
            AccessibleName = "Status",
            Text = FenixOptionGrouping.LoadingText,
            TabIndex = tab++,
        };
        layout.Controls.Add(_status, 0, 2);

        var close = new Button { Text = "&Close", AutoSize = true, Anchor = AnchorStyles.Right, TabIndex = tab++ };
        close.Click += (_, _) => Close();
        layout.Controls.Add(close, 0, 3);

        Controls.Add(layout);

        FormClosing += (_, _) =>
        {
            TearDown();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    /// <summary>
    /// Shows and focuses the window and reads the options, silently (CORE-7): on the first show, and again when
    /// the window is already open and the pilot opens it from the list.
    /// </summary>
    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        _tree.Focus();
        _ = LoadAsync(speak: false);
    }

    // ---- Reads ------------------------------------------------------------------------------------

    // Reads every option and applies it; F5 (speak) says the result. Awaited on the UI thread, never throws.
    // A read or a change already running answers "Still loading." to F5 and is left to finish.
    private async Task LoadAsync(bool speak)
    {
        if (IsDisposed || _tornDown) return;
        if (_loading || _changing)
        {
            if (speak) _announcer.AnnounceImmediate(StillLoadingText);
            return;
        }

        await ReadAndApplyAsync(speak);
    }

    // The one read of the options and the one place the tree is written, for F5, the first show and the
    // read-back after a change. Returns the options it applied, or null when the Fenix did not answer, the
    // read failed or the window went away. Never throws.
    private async Task<IReadOnlyList<FenixSetting>?> ReadAndApplyAsync(bool speak)
    {
        if (IsDisposed || _tornDown || _loading) return null;

        _loading = true;
        try
        {
            JObject? data = await _data.QueryAsync(FenixDataQueries.SettingsQuery, null, _cts.Token);
            if (IsDisposed || _tornDown) return null;

            if (data == null)
            {
                Log.Debug(LogCategory, "The Fenix did not answer the settings read");
                ReportNoAnswer(speak);
                return null;
            }

            // Everything an unexpected answer could make throw runs before the tree is touched, so a throw
            // leaves the window as it was.
            string? version = FenixDataQueries.ParseSoftwareVersion(data);
            IReadOnlyList<FenixSetting> settings = FenixDataQueries.ParseSettings(data);
            IReadOnlyList<FenixOptionGroup> groups = FenixOptionGrouping.Group(settings);
            string status = FenixOptionGrouping.StatusText(version, settings, changesAllowed: ChangesAllowed);
            // No earlier read to count changes against (the Fenix did not answer on open): F5 says the status.
            string? result = !speak ? null
                : _settings == null ? status
                : FenixOptionGrouping.RefreshText(FenixOptionGrouping.CountChanged(_settings, settings));

            bool rebuilt = Apply(groups);
            _settings = settings;
            SetStatus(status);
            Log.Debug(LogCategory, $"Read {settings.Count} options in {groups.Count} groups ({(rebuilt ? "rebuilt" : "updated in place")})");
            if (result != null) _announcer.AnnounceImmediate(result);
            return settings;
        }
        catch (OperationCanceledException)
        {
            // The window closed while a read was in flight.
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"Options read failed: {ex.GetType().Name}: {ex.Message}");
            // Never silent: a read that throws is reported like one the Fenix did not answer.
            if (!IsDisposed && !_tornDown)
            {
                try { ReportNoAnswer(speak); }
                catch (Exception inner) { Log.Debug(LogCategory, $"Options outage report failed: {inner.GetType().Name}: {inner.Message}"); }
            }
            return null;
        }
        finally
        {
            _loading = false;
        }
    }

    // ---- Changes ----------------------------------------------------------------------------------

    /// <summary>
    /// F2: change the selected on/off or choice option for this simulator session. The pilot picks the new
    /// value in a dialog; an option that reshapes the cockpit asks Yes or No first (No by default). The change
    /// is sent as the EFB sends it, then the options are read again and what the Fenix reads back is spoken,
    /// never what was asked for (CORE-7). Awaited on the UI thread, never throws.
    /// </summary>
    private async Task ChangeAsync()
    {
        if (IsDisposed || _tornDown) return;
        if (_loading || _changing)
        {
            _announcer.AnnounceImmediate(StillLoadingText);
            return;
        }

        FenixSetting? option = SettingOf(_tree.SelectedNode);
        if (option == null)
        {
            _announcer.AnnounceImmediate("Select an option first.");
            return;
        }
        if (!FenixOptionEdits.CanChange(option))
        {
            _announcer.AnnounceImmediate("MSFS Blind Assist cannot change this option.");
            return;
        }

        string key = option.Key;
        bool sent = false;
        _changing = true;
        try
        {
            string label = FenixOptionCatalog.Label(key);
            IReadOnlyList<FenixOptionChoice> choices = FenixOptionEdits.Choices(option);

            string? chosen;
            using (var dialog = new FenixOptionsChangeDialog(label, choices, option.Value))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                chosen = dialog.SelectedValue;
            }
            if (IsDisposed || _tornDown) return;
            // The same value again changes nothing: nothing is sent and nothing is spoken.
            if (chosen == null || FenixOptionEdits.SameValue(chosen, option.Value)) return;

            if (FenixOptionEdits.NeedsConfirm(option))
            {
                string choiceLabel = choices.FirstOrDefault(c => string.Equals(c.Value, chosen, StringComparison.Ordinal))?.Label ?? chosen;
                DialogResult answer = MessageBox.Show(this, FenixOptionEdits.ConfirmText(label, choiceLabel), "Change Fenix option",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (IsDisposed || _tornDown || answer != DialogResult.Yes) return;
            }

            Log.Debug(LogCategory, $"Setting change sent for {key}");
            sent = true;
            JObject? reply = await _data.QueryAsync(FenixOptionEdits.SetSettingDocument, FenixOptionEdits.Variables(key, chosen), _cts.Token);
            if (IsDisposed || _tornDown) return;
            if (!FenixOptionEdits.Succeeded(reply))
            {
                Log.Debug(LogCategory, $"Setting change not accepted for {key}");
                _announcer.AnnounceImmediate("The Fenix did not accept the change.");
                return;
            }

            // What the Fenix reads back is the answer. It may take a moment to apply a change, so a value that
            // is not the one sent gets one more look before it is called kept.
            FenixSetting? now = Find(await ReadAndApplyAsync(speak: false), key);
            if (IsDisposed || _tornDown) return;
            if (now != null && !FenixOptionEdits.SameValue(now.Value, chosen))
            {
                await Task.Delay(ReadBackRetryMilliseconds, _cts.Token);
                if (IsDisposed || _tornDown) return;
                now = Find(await ReadAndApplyAsync(speak: false), key);
                if (IsDisposed || _tornDown) return;
            }

            if (now == null)
            {
                Log.Debug(LogCategory, $"Setting change for {key} was not read back");
                _announcer.AnnounceImmediate("Change sent, but the Fenix did not read it back. Press F5.");
            }
            else if (FenixOptionEdits.SameValue(now.Value, chosen))
            {
                Log.Debug(LogCategory, $"Setting {key} reads back as sent");
                _announcer.AnnounceImmediate(FenixOptionCatalog.RowText(now));
            }
            else
            {
                Log.Debug(LogCategory, $"Setting {key} reads back as not changed");
                _announcer.AnnounceImmediate($"Fenix kept {FenixOptionCatalog.RowText(now)}.");
            }
        }
        catch (OperationCanceledException)
        {
            // The window closed while the change was in flight.
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"Setting change for {key} failed: {ex.GetType().Name}: {ex.Message}");
            // Never silent: a change that throws is reported like one that could not be completed.
            if (!IsDisposed && !_tornDown)
            {
                try
                {
                    _announcer.AnnounceImmediate(sent
                        ? "Change sent, but the Fenix did not read it back. Press F5."
                        : "The Fenix did not accept the change.");
                }
                catch (Exception inner) { Log.Debug(LogCategory, $"Setting change report failed: {inner.GetType().Name}: {inner.Message}"); }
            }
        }
        finally
        {
            _changing = false;
        }
    }

    // The option with this key in a read; null when there was no read or it lacks the key.
    private static FenixSetting? Find(IReadOnlyList<FenixSetting>? settings, string key)
        => settings?.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));

    // A read with no answer: the status says so and the tree keeps the last values; F5 speaks it.
    private void ReportNoAnswer(bool speak)
    {
        SetStatus(FenixOptionGrouping.NoAnswerText);
        if (speak) _announcer.AnnounceImmediate(FenixOptionGrouping.NoAnswerText);
    }

    private void SetStatus(string text)
    {
        if (_status.Text != text) _status.Text = text;
    }

    // ---- Tree -------------------------------------------------------------------------------------

    // New groups: rewrite the texts when every group and its options are where they were, else rebuild.
    // Returns true when the tree was rebuilt.
    private bool Apply(IReadOnlyList<FenixOptionGroup> groups)
    {
        bool first = _groups == null;
        bool rebuild = first || !SameShape(_groups!, groups) || _tree.Nodes.Count != groups.Count;
        if (rebuild) Rebuild(groups, first);
        else UpdateTexts(groups);
        _groups = groups;
        ShowDetails();

        // The first options shown: the tree takes focus with the first option selected, which the reader says.
        if (first)
        {
            if (ContainsFocus) _tree.Focus();
            else ActiveControl = _tree;
        }
        return rebuild;
    }

    private static bool SameShape(IReadOnlyList<FenixOptionGroup> before, IReadOnlyList<FenixOptionGroup> after)
    {
        if (before.Count != after.Count) return false;
        for (int i = 0; i < before.Count; i++)
        {
            if (!string.Equals(before[i].Name, after[i].Name, StringComparison.Ordinal)) return false;
            if (!before[i].Options.Select(o => o.Key).SequenceEqual(after[i].Options.Select(o => o.Key), StringComparer.Ordinal))
                return false;
        }
        return true;
    }

    // Builds one node per group, keeping the expanded groups and the selected option (its first occurrence)
    // or group. The first build expands the first group and selects its first option.
    private void Rebuild(IReadOnlyList<FenixOptionGroup> groups, bool first)
    {
        string? selectedKey = OptionKey(_tree.SelectedNode);
        string? selectedGroup = (_tree.SelectedNode?.Tag as FenixOptionGroup)?.Name;
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        foreach (TreeNode node in _tree.Nodes)
            if (node.IsExpanded && node.Tag is FenixOptionGroup g) expanded.Add(g.Name);

        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            foreach (FenixOptionGroup group in groups)
            {
                var node = new TreeNode(group.NodeText) { Tag = group };
                node.Nodes.Add(new TreeNode("Loading...") { Tag = PlaceholderTag });
                _tree.Nodes.Add(node);
                if (expanded.Contains(group.Name)) node.Expand();
            }

            TreeNode? select = selectedKey != null ? ExpandToOption(selectedKey) : null;
            select ??= selectedGroup != null ? FindGroupNode(selectedGroup) : null;
            if (select == null && first && _tree.Nodes.Count > 0)
            {
                TreeNode firstGroup = _tree.Nodes[0];
                firstGroup.Expand();
                select = firstGroup.Nodes.Count > 0 ? firstGroup.Nodes[0] : firstGroup;
            }
            select ??= _tree.Nodes.Count > 0 ? _tree.Nodes[0] : null;
            if (select != null) _tree.SelectedNode = select;
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    // The same groups with the same options in the same order: only the texts that differ are written.
    private void UpdateTexts(IReadOnlyList<FenixOptionGroup> groups)
    {
        for (int i = 0; i < groups.Count && i < _tree.Nodes.Count; i++)
        {
            TreeNode groupNode = _tree.Nodes[i];
            FenixOptionGroup group = groups[i];
            groupNode.Tag = group;   // an unexpanded group fills from the new values
            SetText(groupNode, group.NodeText);
            if (IsUnfilled(groupNode)) continue;
            for (int j = 0; j < groupNode.Nodes.Count && j < group.Options.Count; j++)
            {
                TreeNode optionNode = groupNode.Nodes[j];
                FenixSetting option = group.Options[j];
                if (optionNode.Tag is string key && string.Equals(key, option.Key, StringComparison.Ordinal))
                    SetText(optionNode, FenixOptionCatalog.RowText(option));
            }
        }
    }

    private static void SetText(TreeNode node, string text)
    {
        if (node.Text != text) node.Text = text;
    }

    // The options of a group, built on its first expand from the group's latest read.
    private void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        TreeNode? node = e.Node;
        if (node?.Tag is not FenixOptionGroup group || !IsUnfilled(node)) return;

        _tree.BeginUpdate();
        try
        {
            node.Nodes.Clear();
            foreach (FenixSetting option in group.Options)
                node.Nodes.Add(new TreeNode(FenixOptionCatalog.RowText(option)) { Tag = option.Key });
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private static bool IsUnfilled(TreeNode groupNode)
        => groupNode.Nodes.Count == 1 && groupNode.Nodes[0].Tag is PlaceholderTag;

    // An affecting option is listed twice; this finds its first occurrence, searching the groups in order.
    private TreeNode? ExpandToOption(string key)
    {
        foreach (TreeNode groupNode in _tree.Nodes)
        {
            if (groupNode.Tag is not FenixOptionGroup group
                || !group.Options.Any(o => string.Equals(o.Key, key, StringComparison.Ordinal))) continue;
            groupNode.Expand();
            foreach (TreeNode child in groupNode.Nodes)
                if (child.Tag is string k && string.Equals(k, key, StringComparison.Ordinal)) return child;
        }
        return null;
    }

    private TreeNode? FindGroupNode(string name)
    {
        foreach (TreeNode node in _tree.Nodes)
            if (node.Tag is FenixOptionGroup g && string.Equals(g.Name, name, StringComparison.Ordinal)) return node;
        return null;
    }

    // The key of an option node; null for a group or the placeholder.
    private static string? OptionKey(TreeNode? node)
        => node?.Parent != null && node.Tag is string key && key != PlaceholderTag ? key : null;

    // The option an option node shows, from its group's latest read.
    private static FenixSetting? SettingOf(TreeNode? node)
        => OptionKey(node) is { } key && node!.Parent?.Tag is FenixOptionGroup group
            ? group.Options.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.Ordinal))
            : null;

    // ---- Details and keys -------------------------------------------------------------------------

    // The details box follows the selection; empty for a group. Never spoken: the reader says it on Tab.
    private void ShowDetails()
    {
        string text = SettingOf(_tree.SelectedNode) is { } option
            ? string.Join(Environment.NewLine, FenixOptionCatalog.DetailLines(option))
            : "";
        if (_details.Text != text) _details.Text = text;
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter on an option speaks its details, which the reader cannot say from the tree; on a group it is
        // left to the tree.
        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None && SettingOf(_tree.SelectedNode) is { } option)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _announcer.AnnounceImmediate(string.Join(" ", FenixOptionCatalog.DetailLines(option)));
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (msg.Msg == WM_KEYDOWN)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if (keyData == Keys.F5)
            {
                // Bit 30: the key was already down (auto-repeat).
                if ((msg.LParam.ToInt64() & 0x40000000) == 0) _ = LoadAsync(speak: true);
                return true;
            }
            if (keyData == Keys.F2)
            {
                // Bit 30 again: holding F2 must not ask twice. Shift and Ctrl with F2 are not this key.
                if ((msg.LParam.ToInt64() & 0x40000000) == 0) _ = ChangeAsync();
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Cancels a read in flight; safe to call twice.
    private void TearDown()
    {
        if (_tornDown) return;
        _tornDown = true;
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TearDown();
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }
}
