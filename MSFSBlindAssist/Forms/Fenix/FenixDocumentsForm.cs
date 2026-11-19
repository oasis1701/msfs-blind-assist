using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The "Fenix documents" window (Fenix windows list, Ctrl+Shift+F in input mode): the Fenix EFB's documents (ECAM
/// procedures, QRH, the Fenix user guide, the pilot's own) in the EFB's folders and order, the gateway's search, and
/// a reader that shows one document as an inert page (scripts off, every navigation but its own cancelled), with a
/// plain-text fallback when WebView2 cannot run. Ctrl+P opens the documents the Fenix pinned, one after another.
/// Keys: Enter opens, Ctrl+F search box, F6 document, tree, search box (Shift+F6 the reverse), F5 reload, Ctrl+D pins or unpins, Escape hides.
/// Before a document opens the reader shows a placeholder page, never a blank one.
/// </summary>
/// <remarks>
/// Every request is a GET awaited from a UI-thread handler, so its continuation resumes on the UI thread, and each
/// one checks IsDisposed before it touches a control: the window is disposed on an aircraft switch
/// (DisposeTrackedWindows) while a request may still be in flight. A newer open or search wins; an older answer is
/// dropped. Escape and the close button only hide the window, which keeps the open document; the pin watcher polls
/// only while the window is shown. Speech is kept to what the screen reader cannot say (CORE-7); everything
/// else is what NVDA reads from the focused control.
/// </remarks>
public sealed class FenixDocumentsForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int WM_KEYDOWN = 0x0100;
    private const string LogCategory = "FenixDocuments";
    private const string SearchingText = "Searching...";

    // Marks the one child a folder carries until it is first expanded (FirstOfficerForm's pattern).
    private const string Placeholder = "placeholder";

    // The two header nodes' tags. A document node (in a folder, Pinned or the search results) carries a
    // DocumentTag, a folder its FenixDocumentNode; the "This folder is empty" node carries nothing.
    private static readonly object PinnedHeader = new();
    private static readonly object ResultsHeader = new();

    private sealed record DocumentTag(string Path);

    private readonly FenixDataService _data;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly FenixDocumentPinWatcher _watcher;
    private readonly CancellationTokenSource _cts = new();

    private TextBox _search = null!;
    private NativeAccessibleTreeView _tree = null!;
    private WebView2 _reader = null!;
    private TextBox _fallback = null!;
    private TextBox _status = null!;

    private IReadOnlyList<FenixDocumentItem>? _items;
    private IReadOnlyDictionary<string, FenixDocumentItem>? _byPath;
    // The pins that are in the list, worked out once per change (Listed logs every call).
    private IReadOnlyList<FenixPinnedDocument> _listed = Array.Empty<FenixPinnedDocument>();
    private int _pinCycle = -1;
    private int _openSequence;
    private int _searchSequence;
    private bool _loading;
    private bool _pinning;
    private bool _started;
    private bool _statusIsReady;
    private bool _tornDown;
    private IntPtr _previousWindow;

    // The reader: ready once WebView2 has started; the fallback replaces it for good after a failure.
    private bool _readerReady;
    private bool _fallbackShown;
    private bool _focusReaderOnLoad;
    // The open document, kept so the fallback can show it after a reader failure, and so a document that arrives
    // before the reader is ready is shown once it is.
    private string? _openPath;
    private string? _openHtml;
    private bool _pendingShow;

    public FenixDocumentsForm(FenixDataService data, ScreenReaderAnnouncer announcer)
    {
        _data = data;
        _announcer = announcer;
        _watcher = new FenixDocumentPinWatcher(ct => _data.GetSettingsAsync(ct));
        _watcher.PinsChanged += OnPinsChanged;
        Build();
    }

    private void Build()
    {
        Text = "Fenix documents";
        Size = new Size(1100, 760);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(6) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 3 * Font.Height + 12));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // The EFB's own label for its search box. Search runs on Enter, never as the pilot types.
        _search = new TextBox { AccessibleName = "Search documents", Dock = DockStyle.Fill, TabIndex = 0 };
        _search.KeyDown += OnSearchKeyDown;
        layout.Controls.Add(_search, 0, 0);

        // NativeAccessibleTreeView, never a plain tree (CORE-12), and no check boxes (CORE-18).
        _tree = new NativeAccessibleTreeView
        {
            Dock = DockStyle.Fill,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            HideSelection = false,
            AccessibleName = "Documents",
            AccessibleRole = AccessibleRole.Outline,
            TabIndex = 1,
        };
        _tree.BeforeExpand += OnBeforeExpand;
        _tree.KeyDown += OnTreeKeyDown;
        _tree.NodeMouseDoubleClick += (_, e) => { if (e.Node?.Tag is DocumentTag d) _ = OpenAsync(d.Path); };
        layout.Controls.Add(_tree, 0, 1);

        // The reader and its fallback share one place; only one is ever visible.
        var documentPane = new Panel { Dock = DockStyle.Fill, TabIndex = 2 };
        _reader = new WebView2 { Dock = DockStyle.Fill, AccessibleName = "Document", TabIndex = 0 };
        // Keys pressed inside the document never reach ProcessCmdKey: the wrapper raises only the accelerator keys
        // (Escape, F-keys, Ctrl combinations) as this control's KeyDown (UpdateAvailableForm's pattern).
        _reader.KeyDown += OnReaderKeyDown;
        _fallback = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Visible = false,
            AccessibleName = "Document",
            // Until a document opens, the placeholder's title and hint, as the reader's placeholder page shows them.
            Text = FenixDocumentTexts.NoDocumentTitle + "\r\n" + FenixDocumentTexts.NoDocumentHint,
            TabIndex = 1,
        };
        documentPane.Controls.Add(_fallback);
        documentPane.Controls.Add(_reader);
        layout.Controls.Add(documentPane, 1, 0);
        layout.SetRowSpan(documentPane, 2);

        // Read-only TextBox, never a Label, so it is in the tab order ([VAT-13]).
        _status = new TextBox
        {
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            AccessibleName = "Status",
            Text = FenixDocumentTexts.Loading,
            TabIndex = 3,
        };
        layout.Controls.Add(_status, 0, 2);
        layout.SetColumnSpan(_status, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, TabIndex = 4 };
        var close = new Button { Text = "&Close", AutoSize = true, TabIndex = 0 };
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        FormClosing += OnFormClosing;
    }

    /// <summary>
    /// Shows and focuses the window. The first show loads the list (silently: the tree or the status box takes the
    /// focus and NVDA reads it); a later show only polls the pins and returns to the control the pilot left.
    /// </summary>
    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        _watcher.Start();
        if (!_started)
        {
            _started = true;
            _tree.Focus();
            _ = LoadAsync(speak: false);
        }
        else
        {
            _ = _watcher.PollOnceAsync(_cts.Token);
        }
    }

    // Hide, never close (FenixEFBForm's pattern): reopening returns to the open document. Only an app or Windows
    // shutdown closes the window; an aircraft switch disposes it (DisposeTrackedWindows: Close, which hides, then
    // Dispose). The foreground window is given back only when this window was the one showing.
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason is CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
            return;
        e.Cancel = true;
        bool wasVisible = Visible;
        _watcher.Stop();
        Hide();
        if (wasVisible && _previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    // ---- The list ---------------------------------------------------------------------------------

    // GETs the list, then the pins, and builds the tree. One at a time: F5 while a load runs only says so, as in
    // the Fenix options window.
    private async Task LoadAsync(bool speak)
    {
        if (_loading)
        {
            if (speak) _announcer.AnnounceImmediate(FenixDocumentTexts.StillLoading);
            return;
        }
        _loading = true;
        CancellationToken ct = _cts.Token;
        try
        {
            SetStatus(FenixDocumentTexts.Loading);
            string? json = await _data.GetRestAsync(FenixDocumentEndpoints.Index, ct);
            if (IsDisposed) return;

            IReadOnlyList<FenixDocumentItem>? items = FenixDocumentIndex.Parse(json);
            if (items == null)
            {
                Log.Debug(LogCategory, "list: no answer");
                ReportNoAnswer(speak);
                return;
            }

            // A poll the watcher's timer already runs makes this return at once; its PinsChanged fills Pinned later.
            await _watcher.PollOnceAsync(ct);
            if (IsDisposed) return;

            ApplyList(items);
            ShowReadyStatus();
            Log.Debug(LogCategory, $"list: {items.Count} documents, {_listed.Count} pinned");
            if (speak) _announcer.AnnounceImmediate(FenixDocumentTexts.Refreshed(items.Count, _listed.Count));
        }
        catch (Exception ex)
        {
            // Never silent: a load that throws is reported like one that got no answer.
            Log.Debug(LogCategory, $"list failed: {ex.GetType().Name}: {ex.Message}");
            if (!IsDisposed) ReportNoAnswer(speak);
        }
        finally
        {
            _loading = false;
        }
    }

    // No list: the status says so and any previous tree stays. With nothing in the tree the status box takes the
    // focus, and NVDA reads it, so the text is spoken only when the focus did not move there (F5).
    private void ReportNoAnswer(bool speak)
    {
        SetStatus(FenixDocumentTexts.NoAnswer);
        bool moved = false;
        if (_tree.Nodes.Count == 0 && Visible && !_status.Focused)
        {
            _status.Focus();
            moved = true;
        }
        if (speak && !moved) _announcer.AnnounceImmediate(FenixDocumentTexts.NoAnswer);
    }

    // A new list: the tree is rebuilt only when the list changed, keeping the pilot's place where it still exists.
    // The first list selects the first pinned document (else the first node) and focuses the tree.
    private void ApplyList(IReadOnlyList<FenixDocumentItem> items)
    {
        bool first = _items == null;
        bool same = !first && _items!.SequenceEqual(items);
        _items = items;
        _byPath = FenixDocumentIndex.ByPath(items);
        if (same)
        {
            UpdatePinned();
            return;
        }

        List<string>? keys = first ? null : KeyChain(_tree.SelectedNode);
        TreeNode? results = FindHeader(ResultsHeader);
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            UpdatePinned();
            if (results != null) _tree.Nodes.Add(results);
            foreach (FenixDocumentNode node in FenixDocumentIndex.BuildTree(items)) _tree.Nodes.Add(MakeNode(node));

            TreeNode? select = keys != null ? FindByKeys(keys) : null;
            if (select == null && first && _listed.Count > 0 && FindHeader(PinnedHeader) is { Nodes.Count: > 0 } pinned) select = pinned.Nodes[0];
            // With no pins the first node after the Pinned header (QRH) is selected, as before the header stayed.
            if (select == null && first && _listed.Count == 0 && FindHeader(PinnedHeader) != null && _tree.Nodes.Count > 1) select = _tree.Nodes[1];
            select ??= _tree.Nodes.Count > 0 ? _tree.Nodes[0] : null;
            if (select != null) _tree.SelectedNode = select;
        }
        finally
        {
            _tree.EndUpdate();
        }
        if (first && Visible) _tree.Focus();
    }

    private static TreeNode MakeNode(FenixDocumentNode node)
    {
        if (!node.IsFolder)
            return new TreeNode(FenixDocumentTexts.FileNode(node.Document!)) { Tag = new DocumentTag(node.Document!.Path) };
        var folder = new TreeNode(node.Name) { Tag = node };
        folder.Nodes.Add(new TreeNode("Loading...") { Tag = Placeholder });
        return folder;
    }

    // A folder's children, built on its first expand.
    private void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        TreeNode? node = e.Node;
        if (node?.Tag is not FenixDocumentNode folder || node.Nodes.Count != 1 || node.Nodes[0].Tag is not Placeholder) return;

        _tree.BeginUpdate();
        try
        {
            node.Nodes.Clear();
            if (folder.Children.Count == 0)
                node.Nodes.Add(new TreeNode(FenixDocumentTexts.EmptyFolder));
            else
                foreach (FenixDocumentNode child in folder.Children) node.Nodes.Add(MakeNode(child));
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private TreeNode? FindHeader(object tag)
    {
        foreach (TreeNode node in _tree.Nodes)
            if (ReferenceEquals(node.Tag, tag)) return node;
        return null;
    }

    private static string? KeyOf(TreeNode node) => node.Tag switch
    {
        DocumentTag d => "document:" + d.Path,
        FenixDocumentNode f => "folder:" + f.Path,
        _ when ReferenceEquals(node.Tag, PinnedHeader) => "pinned",
        _ when ReferenceEquals(node.Tag, ResultsHeader) => "results",
        _ => null,
    };

    // The keys from the top of the tree down to the node (or to its nearest ancestor that has one).
    private static List<string>? KeyChain(TreeNode? node)
    {
        while (node != null && KeyOf(node) == null) node = node.Parent;
        if (node == null) return null;
        var keys = new List<string>();
        for (TreeNode? n = node; n != null; n = n.Parent) keys.Insert(0, KeyOf(n) ?? "");
        return keys;
    }

    // The deepest node along the keys; folders on the way are expanded, which fills them.
    private TreeNode? FindByKeys(List<string> keys)
    {
        TreeNodeCollection level = _tree.Nodes;
        TreeNode? found = null;
        for (int i = 0; i < keys.Count; i++)
        {
            TreeNode? next = null;
            foreach (TreeNode node in level)
            {
                if (KeyOf(node) == keys[i])
                {
                    next = node;
                    break;
                }
            }
            if (next == null) break;
            found = next;
            if (i < keys.Count - 1)
            {
                next.Expand();
                level = next.Nodes;
            }
        }
        return found;
    }

    // ---- Pins -------------------------------------------------------------------------------------

    // Raised on the UI thread (the watcher's timer, or a poll awaited here). Never throws: the timer's tick is async
    // void, so an exception here would reach the app's ThreadException handler. Nothing is spoken for a new pin.
    private void OnPinsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        try
        {
            _pinCycle = -1;
            UpdatePinned();
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"pins update failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The Pinned node, updated in place so the reader's place in the tree holds: shown at the top while a pinned
    // document is in the list, its children in the setting's order, the selection kept by path when still there.
    // Safe to run while a load is in flight: it keys on the header's tag, and the load applies its own list after.
    private void UpdatePinned()
    {
        _listed = _byPath == null || _watcher.Current == null
            ? Array.Empty<FenixPinnedDocument>()
            : FenixDocumentPins.Listed(_watcher.Current, _byPath);

        TreeNode? header = FindHeader(PinnedHeader);
        TreeNode? selected = _tree.SelectedNode;
        bool selectionInside = header != null && selected != null && (selected == header || selected.Parent == header);
        string? selectedPath = selectionInside && selected!.Tag is DocumentTag d ? d.Path : null;

        _tree.BeginUpdate();
        try
        {
            if (_byPath == null || _watcher.Current == null)
            {
                // No list or no answer from the Fenix yet: no Pinned node.
                if (header != null)
                {
                    header.Remove();
                    if (selectionInside && _tree.Nodes.Count > 0) _tree.SelectedNode = _tree.Nodes[0];
                }
            }
            else
            {
                string headerText = FenixDocumentTexts.PinnedNode(_listed.Count);
                bool created = header == null;
                if (header == null)
                {
                    header = new TreeNode(headerText) { Tag = PinnedHeader };
                    _tree.Nodes.Insert(0, header);
                }
                SetText(header, headerText);
                if (_listed.Count == 0)
                {
                    // One child that is not a document (no tag), like the empty-folder child.
                    while (header.Nodes.Count > 0 && header.Nodes[0].Tag != null) header.Nodes.RemoveAt(0);
                    if (header.Nodes.Count == 0) header.Nodes.Add(new TreeNode(FenixDocumentTexts.NoPins));
                    while (header.Nodes.Count > 1) header.Nodes.RemoveAt(header.Nodes.Count - 1);
                }
                else
                {
                    // Drop the "no pins" child (it has no tag) before the document children are matched by position.
                    if (header.Nodes.Count > 0 && header.Nodes[0].Tag == null) header.Nodes.Clear();
                    for (int i = 0; i < _listed.Count; i++)
                    {
                        FenixPinnedDocument pin = _listed[i];
                        string text = FenixDocumentTexts.PinnedItem(pin.Path, pin.Label);
                        var tag = new DocumentTag(pin.Path);
                        if (i < header.Nodes.Count)
                        {
                            SetText(header.Nodes[i], text);
                            if (!Equals(header.Nodes[i].Tag, tag)) header.Nodes[i].Tag = tag;
                        }
                        else
                        {
                            header.Nodes.Add(new TreeNode(text) { Tag = tag });
                        }
                    }
                    while (header.Nodes.Count > _listed.Count) header.Nodes.RemoveAt(header.Nodes.Count - 1);
                }
                // Opened once, when it first appears (a node with no children cannot be); later the pilot's choice holds.
                if (created) header.Expand();
                if (selectedPath != null) _tree.SelectedNode = FindChild(header, selectedPath) ?? header;
            }
        }
        finally
        {
            _tree.EndUpdate();
        }
        if (_statusIsReady) ShowReadyStatus();
    }

    private static TreeNode? FindChild(TreeNode parent, string path)
    {
        foreach (TreeNode node in parent.Nodes)
            if (node.Tag is DocumentTag d && string.Equals(d.Path, path, StringComparison.Ordinal)) return node;
        return null;
    }

    private static void SetText(TreeNode node, string text)
    {
        if (node.Text != text) node.Text = text;
    }

    // Ctrl+P: the pinned documents in turn, the first one again after the pins change.
    private void OpenNextPinned()
    {
        if (IsDisposed) return;
        if (_listed.Count == 0)
        {
            // Pins unknown (the Fenix did not answer) is not the same as no pins.
            _announcer.AnnounceImmediate(_watcher.Current == null ? FenixDocumentTexts.NoAnswer : FenixDocumentTexts.NoPins);
            return;
        }
        _pinCycle = (_pinCycle + 1) % _listed.Count;
        _ = OpenAsync(_listed[_pinCycle].Path);
    }

    // ---- Search -----------------------------------------------------------------------------------

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        _ = SearchAsync(_search.Text.Trim());
    }

    // The gateway's search (10 hits at most). A newer search wins: an older answer is dropped. Results: the tree
    // takes the focus on the results node and NVDA reads it with its count, so nothing is spoken here.
    private async Task SearchAsync(string term)
    {
        if (term.Length < FenixDocumentEndpoints.MinimumSearchLength)
        {
            _announcer.AnnounceImmediate(FenixDocumentTexts.TooShort);
            return;
        }

        int sequence = ++_searchSequence;
        CancellationToken ct = _cts.Token;
        try
        {
            SetStatus(SearchingText);
            string? json = await _data.GetRestAsync(FenixDocumentEndpoints.Search(term), ct);
            if (IsDisposed || sequence != _searchSequence) return;

            IReadOnlyList<FenixDocumentSearchResult>? results = FenixDocumentSearch.Parse(json);
            if (results == null)
            {
                Log.Debug(LogCategory, "search: no answer");
                SetStatus(FenixDocumentTexts.NoAnswer);
                _announcer.AnnounceImmediate(FenixDocumentTexts.NoAnswer);
                return;
            }
            Log.Debug(LogCategory, $"search: {results.Count} results");
            if (results.Count == 0)
            {
                // The focus stays in the search box.
                FindHeader(ResultsHeader)?.Remove();
                SetStatus(FenixDocumentTexts.NoResults);
                _announcer.AnnounceImmediate(FenixDocumentTexts.NoResults);
                return;
            }
            ShowResults(term, results);
            SetStatus(FenixDocumentTexts.ResultsNode(term, results.Count));
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"search failed: {ex.GetType().Name}: {ex.Message}");
            if (!IsDisposed && sequence == _searchSequence)
            {
                SetStatus(FenixDocumentTexts.NoAnswer);
                _announcer.AnnounceImmediate(FenixDocumentTexts.NoAnswer);
            }
        }
    }

    // Replaces the results node (after Pinned), expands and selects it, and focuses the tree.
    private void ShowResults(string term, IReadOnlyList<FenixDocumentSearchResult> results)
    {
        _tree.BeginUpdate();
        try
        {
            FindHeader(ResultsHeader)?.Remove();
            var header = new TreeNode(FenixDocumentTexts.ResultsNode(term, results.Count)) { Tag = ResultsHeader };
            foreach (FenixDocumentSearchResult result in results)
                header.Nodes.Add(new TreeNode(FenixDocumentSearch.ResultText(result)) { Tag = new DocumentTag(result.Path) });
            _tree.Nodes.Insert(FindHeader(PinnedHeader) != null ? 1 : 0, header);
            header.Expand();
            _tree.SelectedNode = header;
        }
        finally
        {
            _tree.EndUpdate();
        }
        if (Visible) _tree.Focus();
    }

    // ---- Opening a document -----------------------------------------------------------------------

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter opens a document, a pinned document or a search result; on a folder it is left to the tree.
        if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None || _tree.SelectedNode?.Tag is not DocumentTag d) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        _ = OpenAsync(d.Path);
    }

    // GETs one document and shows it. Only a path in the list is fetched, by the path as the gateway lists it.
    // A newer open wins: an older answer is dropped. Nothing is spoken on success: the reader takes the focus
    // once the page has loaded, and NVDA reads its title and the document.
    private async Task OpenAsync(string path)
    {
        if (_byPath == null || !_byPath.TryGetValue(path.Replace('\\', '/'), out FenixDocumentItem? item))
        {
            _announcer.AnnounceImmediate(FenixDocumentTexts.NotListed);
            return;
        }
        if (string.Equals(item.Kind, "Pdf", StringComparison.Ordinal))
        {
            _announcer.AnnounceImmediate(FenixDocumentTexts.PdfNotReadable);
            return;
        }

        int sequence = ++_openSequence;
        CancellationToken ct = _cts.Token;
        try
        {
            SetStatus("Loading " + FenixDocumentNames.DisplayName(item.Path) + "...");
            string? json = await _data.GetRestAsync(FenixDocumentEndpoints.Content(item.Path), ct);
            if (IsDisposed || sequence != _openSequence) return;

            string? html = FenixDocumentPage.ParseHtml(json);
            if (html == null)
            {
                Log.Debug(LogCategory, $"open {item.Path}: not sent");
                SetStatus(FenixDocumentTexts.NotSent);
                _announcer.AnnounceImmediate(FenixDocumentTexts.NotSent);
                return;
            }
            Log.Debug(LogCategory, $"open {item.Path}: {html.Length} characters");
            _openPath = item.Path;
            _openHtml = html;
            ShowDocument(focus: true);
            SetStatus(FenixDocumentTexts.Opened(item.Path));
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"open {item.Path} failed: {ex.GetType().Name}: {ex.Message}");
            if (!IsDisposed && sequence == _openSequence)
            {
                SetStatus(FenixDocumentTexts.NotSent);
                _announcer.AnnounceImmediate(FenixDocumentTexts.NotSent);
            }
        }
    }

    // Shows the open document in the reader, or as plain text in the fallback. Before the reader is ready the
    // document waits for it.
    private void ShowDocument(bool focus)
    {
        if (_openPath == null || _openHtml == null) return;
        if (_fallbackShown)
        {
            _fallback.Text = FenixDocumentPage.ToPlainText(_openPath, _openHtml);
            _fallback.Select(0, 0);
            if (focus && Visible) _fallback.Focus();
            return;
        }
        if (!_readerReady)
        {
            _pendingShow = true;
            return;
        }
        _pendingShow = false;
        try
        {
            _focusReaderOnLoad = focus;
            _reader.CoreWebView2.NavigateToString(FenixDocumentPage.Build(_openPath, _openHtml));
        }
        catch (Exception ex)
        {
            _focusReaderOnLoad = false;
            Log.Debug(LogCategory, $"reader could not show {_openPath}: {ex.GetType().Name}; plain text instead");
            _pendingShow = focus;
            ShowFallback();
        }
    }

    // The page the reader shows while no document is open (the owner heard "document about:blank" there). It never
    // takes the focus: only a document the pilot opens does.
    private void ShowPlaceholder()
    {
        try
        {
            _focusReaderOnLoad = false;
            _reader.CoreWebView2.NavigateToString(FenixDocumentPage.Placeholder(FenixDocumentTexts.NoDocumentTitle, FenixDocumentTexts.NoDocumentHint));
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"reader could not show the placeholder: {ex.GetType().Name}; plain text instead");
            ShowFallback();
        }
    }

    // ---- The reader -------------------------------------------------------------------------------

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Fire-and-forget is safe: the method catches everything and falls back to plain text.
        _ = InitReaderAsync();
    }

    private async Task InitReaderAsync()
    {
        try
        {
            await _reader.EnsureCoreWebView2Async();
            if (IsDisposed) return;
            CoreWebView2 core = _reader.CoreWebView2;
            CoreWebView2Settings s = core.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = false;
            s.IsZoomControlEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            // The page carries no script of its own; a pilot's own document may, and it must stay inert.
            s.IsScriptEnabled = false;
            s.IsWebMessageEnabled = false;
            s.AreHostObjectsAllowed = false;
            s.IsStatusBarEnabled = false;

            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationCompleted += OnNavigationCompleted;
            // A browser-process death after a successful start raises only this event, never an exception here.
            core.ProcessFailed += OnProcessFailed;
            _readerReady = true;
            // A document already waiting wins; otherwise the placeholder, so the reader never reads "about:blank".
            if (_pendingShow) ShowDocument(focus: true);
            else if (_openPath == null) ShowPlaceholder();
        }
        catch (Exception ex)
        {
            // No WebView2 runtime, or the window was disposed while it started.
            Log.Debug(LogCategory, $"reader could not start ({ex.GetType().Name}); plain text instead");
            ShowFallback();
        }
    }

    // The reader shows only the page NavigateToString gives it: that arrives here as a data: navigation, and the
    // blank start page as about: (UpdateAvailableForm's rule). Everything else is cancelled; a link to an http or
    // https page opens in the default browser.
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        e.Cancel = true;
        OpenExternally(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternally(e.Uri);
    }

    // Opens http and https links in the default browser; drops anything else.
    private static void OpenExternally(string uri)
    {
        if (!uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"link not opened: {ex.GetType().Name}");
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            // A cancelled link, or a page a newer open replaced before it finished.
            Log.Debug(LogCategory, $"reader navigation not completed: {e.WebErrorStatus}");
            return;
        }
        if (!_focusReaderOnLoad) return;
        _focusReaderOnLoad = false;
        // NVDA reads the title and the document; nothing is spoken here (CORE-7).
        if (Visible) _reader.Focus();
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Debug(LogCategory, $"reader process failed ({e.ProcessFailedKind}); plain text instead");
        ShowFallback();
    }

    // Replaces the reader with the plain-text fallback for good and shows the open document there.
    private void ShowFallback()
    {
        if (IsDisposed || _fallbackShown) return;
        _fallbackShown = true;
        _readerReady = false;
        // Order matters (UpdateAvailableForm): show the fallback BEFORE hiding the reader, so focus that was in the
        // reader lands on the fallback, not on the next control.
        _fallback.Visible = true;
        _reader.Visible = false;
        bool focus = _pendingShow;
        _pendingShow = false;
        ShowDocument(focus);
    }

    // ---- Status and keys --------------------------------------------------------------------------

    private void SetStatus(string text)
    {
        _statusIsReady = false;
        if (_status.Text != text) _status.Text = text;
    }

    private void ShowReadyStatus()
    {
        if (_items == null) return;
        SetStatus(FenixDocumentTexts.Ready(_items.Count, _listed.Count));
        _statusIsReady = true;
    }

    // The window's keys, wherever the focus is. inDocument: the key came from inside the reader.
    private Action? ActionFor(Keys keyData, bool inDocument) => keyData switch
    {
        Keys.Escape => () => { if (!IsDisposed && Visible) Close(); },
        Keys.F5 => () => { if (!IsDisposed) _ = LoadAsync(speak: true); },
        Keys.F6 => () => MovePane(1, inDocument),
        Keys.Shift | Keys.F6 => () => MovePane(-1, inDocument),
        Keys.Control | Keys.F => FocusSearch,
        Keys.Control | Keys.P => OpenNextPinned,
        Keys.Control | Keys.D => () => TogglePin(inDocument),
        _ => null,
    };

    // Ctrl+D: pins or unpins the selected document, or the open one when the key comes from inside the document.
    // Any other node, or no open document, does nothing. This is the one write of the window: the Fenix's own
    // pinned_documents setting, written as the EFB writes it.
    private void TogglePin(bool inDocument)
    {
        if (IsDisposed || _pinning) return;
        Control document = _fallbackShown ? _fallback : _reader;
        string? path = inDocument || document.Focused
            ? _openPath
            : (_tree.SelectedNode?.Tag as DocumentTag)?.Path;
        if (path == null) return;
        // The list's own spelling of the path, which the pins use: a search result can name the same document
        // with '\' where the list has '/', and pins match exactly.
        if (_byPath != null && _byPath.TryGetValue(path.Replace('\\', '/'), out FenixDocumentItem? item))
            path = item.Path;
        _ = TogglePinAsync(path);
    }

    private async Task TogglePinAsync(string path)
    {
        if (_pinning) return;
        _pinning = true;
        CancellationToken ct = _cts.Token;
        try
        {
            IReadOnlyList<FenixSetting> settings = await _data.GetSettingsAsync(ct);
            if (_tornDown || IsDisposed) return;
            IReadOnlyList<FenixPinnedDocument>? pins = FenixDocumentPins.FromSettings(settings);
            if (pins == null)
            {
                Log.Debug(LogCategory, "pin: no answer");
                _announcer.AnnounceImmediate(FenixDocumentTexts.NoAnswer);
                return;
            }

            bool wantPinned = !pins.Any(p => string.Equals(p.Path, path, StringComparison.Ordinal));
            IReadOnlyList<FenixPinnedDocument> updated = FenixDocumentPins.Toggle(pins, path);
            var data = await _data.QueryAsync(FenixDocumentPins.SetSettingDocument, FenixDocumentPins.Variables(FenixDocumentPins.Serialize(updated)), ct);
            if (_tornDown || IsDisposed) return;
            if (!FenixDocumentPins.Succeeded(data))
            {
                Log.Debug(LogCategory, $"pin {path}: not accepted");
                _announcer.AnnounceImmediate(FenixDocumentPins.NotAccepted);
                return;
            }

            // Read back what the Fenix holds: its settings query lags the write by up to about 2 s, so re-read
            // until it shows the change (the watcher's own poll may be in flight and skip an explicit one).
            int reads = 0;
            FenixPinReadBack outcome = await FenixDocumentPins.AwaitStateAsync(
                async t => { reads++; return await _data.GetSettingsAsync(t); },
                path, wantPinned, FenixSettingReadBack.Interval, FenixSettingReadBack.Attempts, Task.Delay, ct);
            if (_tornDown || IsDisposed) return;
            Log.Debug(LogCategory, $"pin {path}: wanted {wantPinned}, {outcome} after {reads} reads, {pins.Count} pins before");
            _announcer.AnnounceImmediate(outcome switch
            {
                FenixPinReadBack.Confirmed => FenixDocumentPins.ReadBack(path, wantPinned),
                FenixPinReadBack.Opposite => FenixDocumentPins.NotAccepted,
                _ => FenixDocumentTexts.NoAnswer,
            });
            // Already announced: nothing below may speak.
            try
            {
                await _watcher.PollOnceAsync(ct);
                if (_tornDown || IsDisposed) return;
            }
            catch (Exception ex)
            {
                Log.Debug(LogCategory, $"pin refresh failed: {ex.GetType().Name}");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The window is closing (Dispose cancels the token): nothing is spoken.
            return;
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"pin {path} failed: {ex.GetType().Name}: {ex.Message}");
            if (!_tornDown && !IsDisposed) _announcer.AnnounceImmediate(FenixDocumentPins.NotAccepted);
        }
        finally
        {
            _pinning = false;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (msg.Msg == WM_KEYDOWN)
        {
            Action? action = ActionFor(keyData, inDocument: false);
            if (action != null)
            {
                // Bit 30: the key was already down (auto-repeat); a held F5 reloads once.
                if (keyData != Keys.F5 || (msg.LParam.ToInt64() & 0x40000000) == 0) action();
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Keys inside the document. The browser waits while this handler runs (the WebView2 wrapper's own warning), so
    // the key is claimed here and its action runs once the handler has returned.
    private void OnReaderKeyDown(object? sender, KeyEventArgs e)
    {
        Action? action = ActionFor(e.KeyData, inDocument: true);
        if (action == null) return;
        e.Handled = true;
        SafeBeginInvoke(action);
    }

    // F6 runs document, tree, search box and round again; Shift+F6 runs the reverse (owner, 2026-10-22). From
    // Status or Close, F6 goes to the tree and Shift+F6 to the search box.
    private void MovePane(int step, bool inDocument)
    {
        if (IsDisposed || !Visible) return;
        Control document = _fallbackShown ? _fallback : _reader;
        Control[] panes = { document, _tree, _search };
        int current = inDocument || document.Focused ? 0 : _tree.Focused ? 1 : _search.Focused ? 2 : -1;
        Control target = current < 0
            ? (step > 0 ? _tree : _search)
            : panes[(current + step + panes.Length) % panes.Length];
        target.Focus();
    }

    private void FocusSearch()
    {
        if (IsDisposed || !Visible) return;
        _search.Focus();
        _search.SelectAll();
    }

    // [A380C-6]: an IsHandleCreated check alone races handle destruction, so BeginInvoke is wrapped.
    private void SafeBeginInvoke(Action action)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window is going away; the key no longer matters.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_tornDown)
        {
            _tornDown = true;
            // Stops the poll; a request still in flight is cancelled, and its answer finds IsDisposed set.
            _watcher.PinsChanged -= OnPinsChanged;
            _watcher.Dispose();
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { }
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }
}
