using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The Fenix "Failures" window (Fenix windows list, Ctrl+Shift+F in input mode): the Fenix's manual failures
/// as a tree of ATA chapters, groups and failures, with search, a show filter, and Fail now, Arm and Clear.
/// Every write asks Yes/No with No as the default and is confirmed once, from the gateway's reply. While
/// the window is open it re-reads the catalog every 10 s and speaks changes it did not make.
/// </summary>
/// <remarks>
/// Every request is awaited from a UI-thread handler, so continuations resume on the UI thread; nothing runs on
/// a pool thread and nothing subscribes, so nothing marshals. One request at a time (<see cref="_busy"/>).
/// A state change only rewrites the Text of the nodes it changed, so NVDA's place in the tree holds; the tree is
/// rebuilt only when the filtered list of failures changes (search, Show, another variant).
/// </remarks>
public sealed class FenixFailuresForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int WM_KEYDOWN = 0x0100;
    private const string LogCategory = "FenixFailures";

    // Marks the one empty child a chapter carries until it is first expanded.
    private static readonly object Placeholder = new();

    private sealed record GroupTag(int Ata, string Name);

    private enum LoadReason { Open, Refresh, Tick, Variant, AfterSave }

    private readonly FenixA320Definition _definition;
    private readonly FenixDataService _data;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _searchDebounce;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, TreeNode> _failureNodes = new();

    private TextBox _search = null!;
    private ComboBox _show = null!;
    private NativeAccessibleTreeView _tree = null!;
    private TextBox _status = null!;

    private FenixFailureCatalog? _catalog;
    private IReadOnlyList<FenixFailureChapter> _view = Array.Empty<FenixFailureChapter>();
    private List<string> _viewIds = new();
    private string _automaticFailures = "";
    private string _lastResult = "";
    private bool _busy;
    private bool _outageSpoken;
    private bool _started;
    private bool _tornDown;
    private IntPtr _previousWindow;

    public FenixFailuresForm(FenixA320Definition definition, FenixDataService data, ScreenReaderAnnouncer announcer)
    {
        _definition = definition;
        _data = data;
        _announcer = announcer;
        _timer = new System.Windows.Forms.Timer { Interval = 10000 };
        _timer.Tick += (_, _) => { if (!_busy) _ = LoadAsync(LoadReason.Tick, null); };
        // The EFB's own search debounce.
        _searchDebounce = new System.Windows.Forms.Timer { Interval = 300 };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            Rebuild(expandForFilter: true);
            _lastResult = SearchText.Length > 0 ? FenixFailureText.Matches(_viewIds.Count, SearchText) : "";
            UpdateStatus();
        };
        Build();
        _definition.VariantChanged += OnVariantChanged;
    }

    private void Build()
    {
        Text = "Fenix failures";
        Size = new Size(640, 560);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(6) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 4 * Font.Height + 12));
        int tab = 0;

        var searchLabel = new Label { Text = "&Search", AutoSize = true, Anchor = AnchorStyles.Left, TabIndex = tab++ };
        _search = new TextBox { AccessibleName = "Search failures", Dock = DockStyle.Fill, TabIndex = tab++ };
        _search.TextChanged += (_, _) => { _searchDebounce.Stop(); _searchDebounce.Start(); };
        _search.KeyDown += OnSearchKeyDown;
        layout.Controls.Add(searchLabel, 0, 0);
        layout.Controls.Add(_search, 1, 0);

        var showLabel = new Label { Text = "S&how", AutoSize = true, Anchor = AnchorStyles.Left, TabIndex = tab++ };
        _show = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Show", Width = 200, TabIndex = tab++ };
        _show.Items.AddRange(new object[] { "All failures", "Armed", "Failed", "Armed or failed" });
        _show.SelectedIndex = 0;
        _show.SelectedIndexChanged += (_, _) =>
        {
            Rebuild(expandForFilter: true);
            string shown = FenixFailureText.Shown(_viewIds.Count);
            _lastResult = shown;
            UpdateStatus();
            _announcer.Announce(shown);
        };
        layout.Controls.Add(showLabel, 0, 1);
        layout.Controls.Add(_show, 1, 1);

        // NativeAccessibleTreeView, never a plain tree (CORE-12); no check boxes: a failure's state is in
        // its text, and writes come only from the buttons.
        _tree = new NativeAccessibleTreeView
        {
            Dock = DockStyle.Fill,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            HideSelection = false,
            AccessibleName = "Failures",
            AccessibleRole = AccessibleRole.Outline,
            TabIndex = tab++,
        };
        _tree.BeforeExpand += OnBeforeExpand;
        _tree.KeyDown += OnTreeKeyDown;
        layout.Controls.Add(_tree, 0, 2);
        layout.SetColumnSpan(_tree, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, TabIndex = tab++ };
        var failNow = new Button { Text = "Fail &now", AutoSize = true, TabIndex = 0 };
        var arm = new Button { Text = "&Arm…", AutoSize = true, TabIndex = 1 };
        var clear = new Button { Text = "&Clear", AutoSize = true, TabIndex = 2 };
        var refresh = new Button { Text = "&Refresh", AutoSize = true, TabIndex = 3 };
        failNow.Click += (_, _) => _ = ChangeAsync(FenixFailureAction.FailNow);
        arm.Click += (_, _) => _ = ChangeAsync(FenixFailureAction.Arm);
        clear.Click += (_, _) => _ = ChangeAsync(FenixFailureAction.Clear);
        refresh.Click += (_, _) => RequestRefresh();
        buttons.Controls.AddRange(new Control[] { failNow, arm, clear, refresh });
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        // Read-only TextBox, never a Label, so it is in the tab order ([VAT-13]).
        _status = new TextBox
        {
            ReadOnly = true,
            Multiline = true,
            TabStop = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            AccessibleName = "Failures status",
            Text = "Loading Fenix failures.",
            TabIndex = tab++,
        };
        layout.Controls.Add(_status, 0, 4);
        layout.SetColumnSpan(_status, 2);

        Controls.Add(layout);

        FormClosing += (_, _) =>
        {
            TearDown();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    /// <summary>Shows and focuses the window; the first show loads the catalog and starts the 10 s re-read.</summary>
    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        _tree.Focus();
        if (!_started)
        {
            _started = true;
            _timer.Start();
            _ = LoadAsync(LoadReason.Open, null);
        }
    }

    private string SearchText => _search.Text.Trim();

    private FenixFailureFilter ShowFilter => _show.SelectedIndex switch
    {
        1 => FenixFailureFilter.Armed,
        2 => FenixFailureFilter.Failed,
        3 => FenixFailureFilter.ArmedOrFailed,
        _ => FenixFailureFilter.All,
    };

    // ---- Reads ------------------------------------------------------------------------------------

    // GETs the catalog (and, on open and F5, the settings) and applies it. Awaited on the UI thread.
    private async Task LoadAsync(LoadReason reason, string? exceptId)
    {
        if (_busy && reason != LoadReason.AfterSave) return;
        _busy = true;
        try
        {
            if (reason is LoadReason.Open or LoadReason.Refresh)
            {
                var settings = await _data.GetSettingsAsync(_cts.Token);
                if (IsDisposed || _tornDown) return;
                _automaticFailures = FenixFailureText.AutomaticFailures(settings);
            }

            string? json = await _data.GetRestAsync(FenixFailureRequests.ManualPath, _cts.Token);
            if (IsDisposed || _tornDown) return;

            FenixFailureCatalog? loaded = FenixFailureCatalog.Parse(json);
            if (loaded == null)
            {
                ReportUnavailable(reason);
                return;
            }

            _outageSpoken = false;
            FenixFailureCatalog? before = _catalog;
            ApplyCatalog(loaded);

            if (reason == LoadReason.Refresh)
            {
                _lastResult = FenixFailureText.Refreshed(loaded);
                _announcer.Announce(_lastResult);
            }
            else if ((reason is LoadReason.Tick or LoadReason.AfterSave) && before != null)
            {
                // A background change the pilot did not make here (CORE-7); this window's own save is excluded.
                string? changes = FenixFailureText.Changes(before, loaded, exceptId, TimeZoneInfo.Local);
                if (changes != null)
                {
                    _lastResult = changes;
                    _announcer.Announce(changes);
                }
            }
            UpdateStatus();
        }
        catch (OperationCanceledException)
        {
            // The window closed while a read was in flight.
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"Failures read failed: {ex.GetType().Name}: {ex.Message}");
            // Never silent: a read that throws is heard as an outage, like a read that failed.
            if (!IsDisposed && !_tornDown)
            {
                try { ReportUnavailable(reason); }
                catch (Exception inner) { Log.Debug(LogCategory, $"Failures outage report failed: {inner.GetType().Name}: {inner.Message}"); }
            }
        }
        finally
        {
            if (reason != LoadReason.AfterSave) _busy = false;
        }
    }

    // A read that gave no catalog: the status says so, and it is spoken once per outage on the re-read and
    // always when the pilot asked (open, F5).
    private void ReportUnavailable(LoadReason reason)
    {
        bool speak = reason is LoadReason.Open or LoadReason.Refresh || !_outageSpoken;
        _outageSpoken = true;
        _lastResult = "";
        UpdateStatus(unavailable: true);
        if (speak) _announcer.AnnounceImmediate(FenixFailureText.Unavailable);
    }

    private void RequestRefresh()
    {
        if (_busy)
        {
            _announcer.Announce("Still loading.");
            return;
        }
        _ = LoadAsync(LoadReason.Refresh, null);
    }

    private void OnVariantChanged(object? sender, FenixVariant variant)
    {
        // Raised on the UI thread. The next re-read picks the new catalog up if one is already in flight.
        if (IsDisposed || _tornDown) return;
        UpdateStatus(unavailable: _catalog == null && _outageSpoken);
        if (!_busy) _ = LoadAsync(LoadReason.Variant, null);
    }

    // ---- Writes -----------------------------------------------------------------------------------

    private async Task ChangeAsync(FenixFailureAction action)
    {
        if (_catalog == null || _tree.SelectedNode?.Tag is not string id || _catalog.Find(id) is not { } failure)
        {
            _announcer.AnnounceImmediate(FenixFailureText.SelectFailureFirst);
            return;
        }
        if (_busy)
        {
            _announcer.Announce("Still loading.");
            return;
        }

        FenixFailureCondition? condition = null;
        DialogResult answer;
        // No re-read while the pilot is in the arm dialog or the Yes/No box: the catalog stays the one the
        // question was asked about.
        _timer.Stop();
        try
        {
            if (action == FenixFailureAction.Arm)
            {
                using var dialog = new FenixFailuresArmDialog(failure, _announcer);
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Condition == null) return;
                condition = dialog.Condition;
                if (IsDisposed || _tornDown) return;
            }

            // Every write asks, with No as the default button.
            answer = MessageBox.Show(this,
                FenixFailureText.Confirm(failure, action, condition, TimeZoneInfo.Local),
                "Confirm failure change",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
        }
        finally
        {
            if (!IsDisposed && !_tornDown) _timer.Start();
        }
        if (answer != DialogResult.Yes || IsDisposed || _tornDown) return;
        if (_busy)
        {
            _announcer.Announce("Still loading. Nothing was changed.");
            return;
        }

        // A variant change may still have replaced the catalog: the body is built from the current object.
        if (_catalog?.Find(failure.Id) is not { } current)
        {
            _announcer.AnnounceImmediate(FenixFailureText.NotConfirmed(failure.Title));
            return;
        }

        _busy = true;
        bool resultSpoken = false;
        try
        {
            string body = FenixFailureRequests.BuildSaveBody(current, action, condition);
            Log.Debug(LogCategory, $"Save {failure.Id} ({action}): {body}");
            string? reply = await _data.PostRestAsync(FenixFailureRequests.SavePath, body, _cts.Token);
            if (IsDisposed || _tornDown) return;
            Log.Debug(LogCategory, $"Save {failure.Id} reply: {reply ?? "(none)"}");

            FenixFailure? saved = FenixFailureRequests.ParseSaved(reply);
            if (saved != null && saved.Id == failure.Id && _catalog != null)
            {
                ApplyCatalog(_catalog.With(saved));
                _lastResult = FenixFailureText.SavedLine(saved, TimeZoneInfo.Local);
                UpdateStatus();
                _announcer.Announce(_lastResult);
                resultSpoken = true;
            }
            else
            {
                _lastResult = FenixFailureText.NotConfirmed(failure.Title);
                UpdateStatus();
                _announcer.AnnounceImmediate(_lastResult);
                resultSpoken = true;
            }

            await LoadAsync(LoadReason.AfterSave, failure.Id);
        }
        catch (OperationCanceledException)
        {
            // The window closed while the save was in flight.
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"Save {failure.Id} failed: {ex.GetType().Name}: {ex.Message}");
            if (!IsDisposed && !_tornDown)
            {
                // Never silent: success was not confirmed, so say so, then read back what the Fenix has.
                if (!resultSpoken) _announcer.AnnounceImmediate(FenixFailureText.NotConfirmed(failure.Title));
                await LoadAsync(LoadReason.AfterSave, failure.Id);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- Tree -------------------------------------------------------------------------------------

    // A new catalog: rewrite the text that changed, or rebuild when the filtered failures differ.
    private void ApplyCatalog(FenixFailureCatalog catalog)
    {
        bool first = _catalog == null;
        _catalog = catalog;
        var view = catalog.Filter(SearchText, ShowFilter);
        if (!first && IdsOf(view).SequenceEqual(_viewIds))
        {
            _view = view;
            UpdateTexts();
            return;
        }
        Rebuild(expandForFilter: false, view);
    }

    private static List<string> IdsOf(IReadOnlyList<FenixFailureChapter> view)
        => view.SelectMany(c => c.AllFailures).Select(f => f.Id).ToList();

    // Builds the tree from the filtered view, keeping expanded chapters and groups and the selected node.
    private void Rebuild(bool expandForFilter, IReadOnlyList<FenixFailureChapter>? view = null)
    {
        if (_catalog == null) return;
        _view = view ?? _catalog.Filter(SearchText, ShowFilter);
        _viewIds = IdsOf(_view);
        bool expandAll = expandForFilter && (SearchText.Length > 0 || ShowFilter != FenixFailureFilter.All);

        var expandedChapters = new HashSet<int>();
        var expandedGroups = new HashSet<GroupTag>();
        foreach (TreeNode chapterNode in _tree.Nodes)
        {
            if (!chapterNode.IsExpanded || chapterNode.Tag is not int ata) continue;
            expandedChapters.Add(ata);
            foreach (TreeNode child in chapterNode.Nodes)
                if (child.IsExpanded && child.Tag is GroupTag g) expandedGroups.Add(g);
        }
        object? selectedTag = _tree.SelectedNode?.Tag;
        int? selectedChapter = _tree.SelectedNode is { } sel ? ChapterOf(sel) : null;

        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            _failureNodes.Clear();
            foreach (FenixFailureChapter chapter in _view)
            {
                var node = new TreeNode(FenixFailureText.ChapterNode(chapter)) { Tag = chapter.Ata };
                node.Nodes.Add(new TreeNode("") { Tag = Placeholder });
                _tree.Nodes.Add(node);
                if (expandAll || expandedChapters.Contains(chapter.Ata))
                {
                    node.Expand();
                    foreach (TreeNode child in node.Nodes)
                        if (child.Tag is GroupTag g && (expandAll || expandedGroups.Contains(g))) child.Expand();
                }
            }

            TreeNode? select = selectedTag switch
            {
                string id when _failureNodes.TryGetValue(id, out var f) => f,
                _ => null,
            };
            select ??= FindChapterNode(selectedTag as int? ?? selectedChapter);
            select ??= _tree.Nodes.Count > 0 ? _tree.Nodes[0] : null;
            if (select != null) _tree.SelectedNode = select;
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private static int? ChapterOf(TreeNode node)
    {
        while (node.Parent != null) node = node.Parent;
        return node.Tag as int?;
    }

    private TreeNode? FindChapterNode(int? ata)
    {
        if (ata == null) return null;
        foreach (TreeNode n in _tree.Nodes)
            if (n.Tag is int a && a == ata) return n;
        return null;
    }

    // The children of a chapter, built on its first expand from the current view.
    private void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        TreeNode? node = e.Node;
        if (node?.Tag is not int ata || node.Nodes.Count != 1 || node.Nodes[0].Tag != Placeholder) return;
        FenixFailureChapter? chapter = _view.FirstOrDefault(c => c.Ata == ata);

        _tree.BeginUpdate();
        try
        {
            node.Nodes.Clear();
            if (chapter == null) return;
            foreach (FenixFailureGroup group in chapter.Groups)
            {
                TreeNodeCollection parent = node.Nodes;
                if (group.ShowsHeader)
                {
                    var groupNode = new TreeNode(FenixFailureText.GroupNode(group)) { Tag = new GroupTag(ata, group.Name) };
                    node.Nodes.Add(groupNode);
                    parent = groupNode.Nodes;
                }
                foreach (FenixFailure failure in group.Failures)
                {
                    var failureNode = new TreeNode(FenixFailureText.FailureNode(failure, TimeZoneInfo.Local)) { Tag = failure.Id };
                    parent.Add(failureNode);
                    _failureNodes[failure.Id] = failureNode;
                }
            }
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    // A state change: only the Text that differs is written, so the reader's place in the tree holds.
    private void UpdateTexts()
    {
        var failures = new Dictionary<string, FenixFailure>();
        foreach (FenixFailure f in _view.SelectMany(c => c.AllFailures)) failures.TryAdd(f.Id, f);

        foreach (TreeNode chapterNode in _tree.Nodes)
        {
            if (chapterNode.Tag is not int ata || _view.FirstOrDefault(c => c.Ata == ata) is not { } chapter) continue;
            SetText(chapterNode, FenixFailureText.ChapterNode(chapter));
            foreach (TreeNode child in chapterNode.Nodes)
            {
                if (child.Tag is GroupTag g && chapter.Groups.FirstOrDefault(x => x.Name == g.Name) is { } group)
                    SetText(child, FenixFailureText.GroupNode(group));
            }
        }
        foreach (var (id, node) in _failureNodes)
        {
            if (failures.TryGetValue(id, out FenixFailure? failure))
                SetText(node, FenixFailureText.FailureNode(failure, TimeZoneInfo.Local));
        }
    }

    private static void SetText(TreeNode node, string text)
    {
        if (node.Text != text) node.Text = text;
    }

    private TreeNode? FirstFailureNode()
    {
        foreach (TreeNode chapterNode in _tree.Nodes)
        {
            chapterNode.Expand();
            foreach (TreeNode child in chapterNode.Nodes)
            {
                if (child.Tag is string) return child;
                if (child.Tag is GroupTag)
                {
                    child.Expand();
                    if (child.Nodes.Count > 0) return child.Nodes[0];
                }
            }
        }
        return null;
    }

    // ---- Status and keys --------------------------------------------------------------------------

    private void UpdateStatus(bool unavailable = false)
    {
        var lines = new List<string>();
        FenixVariant variant = _definition.Variant;
        if (variant.IsKnown) lines.Add(variant.Describe());
        if (unavailable || _catalog == null)
            lines.Add(unavailable ? FenixFailureText.Unavailable : "Loading Fenix failures.");
        else
            lines.Add(FenixFailureText.Summary(_catalog));
        if (_automaticFailures.Length > 0) lines.Add(_automaticFailures);
        if (_lastResult.Length > 0) lines.Add(_lastResult);

        string text = string.Join(Environment.NewLine, lines);
        if (_status.Text != text) _status.Text = text;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (_catalog == null)
        {
            _announcer.AnnounceImmediate(FenixFailureText.Unavailable);
            return;
        }
        _searchDebounce.Stop();
        Rebuild(expandForFilter: true);
        string matches = SearchText.Length > 0
            ? FenixFailureText.Matches(_viewIds.Count, SearchText)
            : FenixFailureText.Shown(_viewIds.Count);
        _lastResult = matches;
        UpdateStatus();
        if (FirstFailureNode() is { } first)
        {
            _tree.SelectedNode = first;
            _tree.Focus();
        }
        _announcer.Announce(matches);
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter on a failure arms it; on a chapter or group it is left to the tree.
        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None && _tree.SelectedNode?.Tag is string)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = ChangeAsync(FenixFailureAction.Arm);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (msg.Msg == WM_KEYDOWN)
        {
            if (keyData == Keys.Escape)
            {
                // An open Show list closes first; the combo handles this Escape.
                if (_show.DroppedDown) return base.ProcessCmdKey(ref msg, keyData);
                Close();
                return true;
            }
            if (keyData == Keys.F5)
            {
                // Bit 30: the key was already down (auto-repeat).
                if ((msg.LParam.ToInt64() & 0x40000000) == 0) RequestRefresh();
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Stops the timers, cancels a request in flight and leaves VariantChanged; safe to call twice.
    private void TearDown()
    {
        if (_tornDown) return;
        _tornDown = true;
        _timer.Stop();
        _searchDebounce.Stop();
        _definition.VariantChanged -= OnVariantChanged;
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TearDown();
            _timer.Dispose();
            _searchDebounce.Dispose();
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }
}
