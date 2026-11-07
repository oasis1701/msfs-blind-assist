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
/// for this simulator session only, as the Fenix EFB's own toggle does; Shift+F2 makes a change permanent and
/// Ctrl+F2 resets an option the pilot (or the session) changed to the Fenix default, as the EFB's per-option
/// menu does, each only after a Yes/No that defaults to No.
/// </summary>
/// <remarks>
/// Every request is awaited from a UI-thread handler, so continuations resume on the UI thread; nothing runs on
/// a pool thread and nothing subscribes, so nothing marshals. One read or change at a time
/// (<see cref="_loading"/>, <see cref="_changing"/>).
/// A read that keeps every group and its options in the same order only rewrites the Text of the nodes, so
/// NVDA's place in the tree holds; any other read rebuilds the tree and reselects the same option or group.
/// A change is sent only for an option <see cref="FenixOptionEdits.RefusalText"/> lets through (F2 and
/// Shift+F2 an on/off or choice option, Ctrl+F2 any option the pilot or the session changed), with persist
/// false unless it is the pilot's confirmed permanent change, and what is spoken afterwards is what the Fenix
/// reads back, never what was asked for. All three keys share one flow, so the read-back exists once. It waits
/// out the Fenix's lag after a write (FNX-13) with silent reads that leave the tree alone, then writes the last
/// read to the tree once. That rewrite changes the focused row's text in place, which the screen reader reads,
/// so an applied change's row is spoken only when the reader will not have said it
/// (<see cref="FenixOptionEdits.ScreenReaderReadsRow"/>, CORE-7).
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

    // What F5 and the change keys say while a read or a change is running.
    private const string StillLoadingText = "Still loading.";

    // What a change says when the Fenix refuses it, and when it was sent but no read could confirm it.
    private const string NotAcceptedText = "The Fenix did not accept the change.";
    private const string NotReadBackText = "Change sent, but the Fenix did not read it back. Press F5.";

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

    // One read of the options, parsed and grouped, with the tree, the status and the screen reader untouched:
    // what a read-back repeats while the Fenix catches up. Null when the Fenix did not answer. Everything an
    // unexpected answer could make throw runs here, before the tree is touched, so a throw leaves the window
    // as it was.
    private async Task<OptionsRead?> FetchAsync(CancellationToken ct)
    {
        JObject? data = await _data.QueryAsync(FenixDataQueries.SettingsQuery, null, ct);
        if (data == null) return null;

        string? version = FenixDataQueries.ParseSoftwareVersion(data);
        IReadOnlyList<FenixSetting> settings = FenixDataQueries.ParseSettings(data);
        IReadOnlyList<FenixOptionGroup> groups = FenixOptionGrouping.Group(settings);
        return new OptionsRead(settings, groups, FenixOptionGrouping.StatusText(version, settings, changesAllowed: ChangesAllowed));
    }

    private sealed record OptionsRead(IReadOnlyList<FenixSetting> Settings, IReadOnlyList<FenixOptionGroup> Groups, string Status);

    // The one read of the options and the one place the tree is written, for F5, the first show and the
    // read-back after a change. Returns the options it applied, or null when the Fenix did not answer, the
    // read failed or the window went away. Never throws.
    private Task<IReadOnlyList<FenixSetting>?> ReadAndApplyAsync(bool speak) => ReadAndApplyAsync(speak, FetchAsync);

    // `fetch` gives the read to apply: a single read, or the wait after a change (ReadBackAsync), which hands
    // over only its last read, so the tree is written once whatever it took to get there.
    private async Task<IReadOnlyList<FenixSetting>?> ReadAndApplyAsync(bool speak, Func<CancellationToken, Task<OptionsRead?>> fetch)
    {
        if (IsDisposed || _tornDown || _loading) return null;

        _loading = true;
        try
        {
            OptionsRead? read = await fetch(_cts.Token);
            if (IsDisposed || _tornDown) return null;

            if (read == null)
            {
                Log.Debug(LogCategory, "The Fenix did not answer the settings read");
                ReportNoAnswer(speak);
                return null;
            }

            IReadOnlyList<FenixSetting> settings = read.Settings;
            // No earlier read with options to count changes against (the Fenix did not answer on open, or
            // answered with none): F5 says the status.
            string? result = !speak ? null
                : _settings is not { Count: > 0 } ? read.Status
                : FenixOptionGrouping.RefreshText(FenixOptionGrouping.CountChanged(_settings, settings));

            bool rebuilt = Apply(read.Groups);
            _settings = settings;
            SetStatus(read.Status);
            Log.Debug(LogCategory, $"Read {settings.Count} options in {read.Groups.Count} groups ({(rebuilt ? "rebuilt" : "updated in place")})");
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

    // What the pilot asked for is a FenixChangeKind: F2 a change for this simulator session, Shift+F2 a
    // permanent one, Ctrl+F2 a reset to the Fenix default. All three are the EFB's own calls (its toggle and
    // its per-option menu).

    /// <summary>
    /// F2, Shift+F2, Ctrl+F2: change the selected on/off or choice option for this simulator session, make
    /// a change permanent, or (any option the pilot or the session changed) reset it to the Fenix default.
    /// F2 and Shift+F2 pick the new value in a dialog; Shift+F2 (after that dialog) and Ctrl+F2 always ask
    /// Yes or No, and F2 does for an option that reshapes the cockpit (No by default for all). Nothing is
    /// sent without a Yes.
    /// The request is sent as the EFB sends it, then the options are read again and what the Fenix reads back
    /// is spoken, never what was asked for (CORE-7). Awaited on the UI thread, never throws.
    /// </summary>
    private async Task ChangeAsync(FenixChangeKind kind)
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

        string key = option.Key;
        string label = FenixOptionCatalog.Label(key);
        // F2 and Shift+F2 offer an on/off or choice option a value; Ctrl+F2 resets any option the EFB lets you
        // write, but only a value that comes from the user or the session (the EFB's own rule).
        if (FenixOptionEdits.RefusalText(kind, label, option) is { } refusal)
        {
            _announcer.AnnounceImmediate(refusal);
            return;
        }

        string what = Describe(kind);
        _changing = true;
        try
        {
            // The value to send, and how the pilot hears it; a reset sends no value.
            string? chosen = null;
            string choiceLabel = "";
            if (kind != FenixChangeKind.Reset)
            {
                IReadOnlyList<FenixOptionChoice> choices = FenixOptionEdits.Choices(option);
                using (var dialog = new FenixOptionsChangeDialog(label, choices, option.Value, permanent: kind == FenixChangeKind.Permanent))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    chosen = dialog.SelectedValue;
                }
                if (IsDisposed || _tornDown) return;
                // The same value again changes nothing for the session: nothing is sent and nothing is spoken.
                // A permanent change may be the current value (making it permanent), so it goes on to the question.
                if (chosen == null || (kind == FenixChangeKind.Session && FenixOptionCatalog.SameValue(chosen, option.Value))) return;
                choiceLabel = choices.FirstOrDefault(c => string.Equals(c.Value, chosen, StringComparison.Ordinal))?.Label ?? chosen;
            }

            // The one question box: Yes sends, anything else sends nothing and says nothing; No is the default.
            string? question = FenixOptionEdits.Question(kind, option, label, choiceLabel);
            if (question != null)
            {
                DialogResult answer = MessageBox.Show(this, question, ConfirmTitle(kind),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (IsDisposed || _tornDown || answer != DialogResult.Yes) return;
            }

            // What each kind sends and how it knows the Fenix accepted it; the read-back is the same for all.
            string document;
            object variables;
            Func<JObject?, bool> accepted;
            if (kind == FenixChangeKind.Reset)
            {
                document = FenixOptionEdits.SetDefaultDocument;
                variables = FenixOptionEdits.DefaultVariables(key);
                accepted = FenixOptionEdits.DefaultSucceeded;
            }
            else
            {
                document = FenixOptionEdits.SetSettingDocument;
                variables = FenixOptionEdits.Variables(key, chosen!, persist: FenixOptionEdits.Persists(kind));
                accepted = FenixOptionEdits.Succeeded;
            }
            await SendAndReadBackAsync(key, kind, document, variables, accepted,
                now => FenixOptionEdits.Applied(kind, option, chosen, now));
        }
        catch (OperationCanceledException)
        {
            // The window closed while the change was being asked for.
        }
        catch (Exception ex)
        {
            // Only before anything was sent: the send and the read-back report their own failures.
            ReportFailure(key, what, ex, NotAcceptedText);
        }
        finally
        {
            _changing = false;
        }
    }

    // Sends the request and, when the Fenix accepts it, reads the option back and speaks what the Fenix now
    // has, never what was asked for. `applied` says whether a read shows the request applied (the change's
    // value read, the value now the pilot's own, the option no longer as it was: FenixOptionEdits.Applied).
    // The Fenix's settings query lags a write by up to about 2 s (FNX-13), so a read that does not show it
    // applied is read again (ReadBackAsync), and only one that still does not after the whole wait is spoken
    // as kept, not made permanent or not reset. A request with no answer at all (a time-out, the gateway gone,
    // or GraphQL errors) may still have landed, so the same silent wait says which. Never throws.
    private async Task SendAndReadBackAsync(string key, FenixChangeKind kind, string document, object variables,
        Func<JObject?, bool> accepted, Func<FenixSetting, bool> applied)
    {
        string what = Describe(kind);
        try
        {
            // Just before the write: the row the reader is on, to tell afterwards whether the re-read changed it.
            RowWatch watch = WatchRow(key);
            Log.Debug(LogCategory, $"Setting {what} sent for {key}");
            JObject? reply = await _data.QueryAsync(document, variables, _cts.Token);
            if (IsDisposed || _tornDown) return;
            if (reply == null)
            {
                Log.Debug(LogCategory, $"Setting {what} for {key} got no answer; reading it back");
                IReadOnlyList<FenixSetting>? read = await ReadBackAsync(key, what, applied);
                if (IsDisposed || _tornDown) return;
                FenixSetting? landed = Find(read, key);
                if (read == null)
                {
                    Log.Debug(LogCategory, $"Setting {what} for {key}: no answer to the read either");
                    _announcer.AnnounceImmediate(FenixOptionGrouping.NoAnswerText);
                }
                else if (landed == null)
                {
                    Log.Debug(LogCategory, $"Setting {what} for {key} was not read back");
                    _announcer.AnnounceImmediate(NotReadBackText);
                }
                else if (applied(landed))
                {
                    Log.Debug(LogCategory, $"Setting {key} reads back as {(kind == FenixChangeKind.Reset ? "reset" : "sent")} after no answer");
                    AnnounceAppliedRow(key, landed, watch);
                }
                else
                {
                    Log.Debug(LogCategory, $"Setting {what} for {key} got no answer and reads back as not changed");
                    _announcer.AnnounceImmediate(NotAcceptedText);
                }
                return;
            }
            if (!accepted(reply))
            {
                Log.Debug(LogCategory, $"Setting {what} not accepted for {key}");
                _announcer.AnnounceImmediate(NotAcceptedText);
                return;
            }

            FenixSetting? now = Find(await ReadBackAsync(key, what, applied), key);
            if (IsDisposed || _tornDown) return;

            if (now == null)
            {
                Log.Debug(LogCategory, $"Setting {what} for {key} was not read back");
                _announcer.AnnounceImmediate(NotReadBackText);
            }
            else if (applied(now))
            {
                Log.Debug(LogCategory, $"Setting {key} reads back as {(kind == FenixChangeKind.Reset ? "reset" : "sent")}");
                AnnounceAppliedRow(key, now, watch);
            }
            else
            {
                Log.Debug(LogCategory, $"Setting {key} reads back as not changed");
                _announcer.AnnounceImmediate(FenixOptionEdits.NotAppliedText(kind, FenixOptionCatalog.RowText(now)));
            }
        }
        catch (OperationCanceledException)
        {
            // The window closed while the request was in flight.
        }
        catch (Exception ex)
        {
            // The request was on its way: the Fenix may well have applied it.
            ReportFailure(key, what, ex, NotReadBackText);
        }
    }

    // The read after a request: reads the options every 250 ms, up to 16 reads (FenixSettingReadBack, the wait the
    // documents window makes too), until `applied` shows the change or an answered read lacks the option
    // altogether (FenixOptionEdits.ReadBackSettled: "not read back" is spoken at once, not after 16 reads), and
    // applies only the last read, once.
    // The reads in between touch neither the tree, the status nor the screen reader, so no row is rewritten
    // for the reader to say twice (CORE-7) and nothing flickers; the one rewrite comes after the wait, so
    // AnnounceAppliedRow still sees this write's row text before and after. A read with no answer ends the wait:
    // each can take the data service's 10 s time-out. Returns the options it applied, or null when the last
    // read had no answer. Never throws.
    private Task<IReadOnlyList<FenixSetting>?> ReadBackAsync(string key, string what, Func<FenixSetting, bool> applied)
        => ReadAndApplyAsync(speak: false, async ct =>
        {
            int reads = 0;
            FenixReadBackResult<OptionsRead> wait = await FenixSettingReadBack.AwaitAsync<OptionsRead>(
                async t => { reads++; return await FetchAsync(t); },
                read => FenixOptionEdits.ReadBackSettled(read.Settings, key, applied),
                FenixSettingReadBack.Interval, FenixSettingReadBack.Attempts, Task.Delay, ct, stopOnNoAnswer: true);
            string ended = !wait.Landed ? "does not read back as applied"
                : Find(wait.Last?.Settings, key) == null ? "reads back without the option"
                : "reads back as applied";
            Log.Debug(LogCategory, $"Setting {what} for {key}: {ended} after {reads} reads");
            return wait.Last;
        });

    // The row the screen reader is on when a write is about to be sent: whether the selected node is the
    // option's own, and that node's text. The re-read rewrites that text in place, which NVDA reads.
    private readonly record struct RowWatch(bool OnThisOption, string? TextBefore);

    private RowWatch WatchRow(string key)
        => OptionKey(_tree.SelectedNode) == key
            ? new RowWatch(true, _tree.SelectedNode!.Text)
            : new RowWatch(false, null);

    // Speaks the option's row after an applied change, unless the screen reader has already said it (CORE-7):
    // the tree has focus, its selected node is still this option's, and the re-read changed that node's text.
    // Judged now, after the final re-read, not before it. A row that did not change (a current value made
    // permanent, a reset where only the source changed) is spoken, as the reader says nothing for it. Logs the
    // key only.
    private void AnnounceAppliedRow(string key, FenixSetting now, RowWatch watch)
    {
        TreeNode? node = _tree.SelectedNode;
        bool selectedIsThisOption = watch.OnThisOption && OptionKey(node) == key;
        if (FenixOptionEdits.ScreenReaderReadsRow(_tree.Focused, selectedIsThisOption, watch.TextBefore, node?.Text ?? ""))
        {
            Log.Debug(LogCategory, $"Setting {key} read-back left to the screen reader");
            return;
        }
        string row = FenixOptionCatalog.RowText(now);
        _announcer.AnnounceImmediate(row);
    }

    // Never silent: a change that throws is reported in the nearest ruled words. Logs the key and the
    // exception, never a value.
    private void ReportFailure(string key, string what, Exception ex, string spoken)
    {
        Log.Debug(LogCategory, $"Setting {what} for {key} failed: {ex.GetType().Name}: {ex.Message}");
        if (IsDisposed || _tornDown) return;
        try { _announcer.AnnounceImmediate(spoken); }
        catch (Exception inner) { Log.Debug(LogCategory, $"Setting change report failed: {inner.GetType().Name}: {inner.Message}"); }
    }

    // How a kind of change reads in the log (keys and kinds only).
    private static string Describe(FenixChangeKind kind) => kind switch
    {
        FenixChangeKind.Permanent => "permanent change",
        FenixChangeKind.Reset => "reset",
        _ => "change",
    };

    // The title of the question box for a kind of change; the permanent dialog carries the same title.
    private static string ConfirmTitle(FenixChangeKind kind) => kind switch
    {
        FenixChangeKind.Permanent => "Make a Fenix option permanent",
        FenixChangeKind.Reset => "Reset Fenix option",
        _ => "Change Fenix option",
    };

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
        // The first options shown are the first that fill an empty tree: an answer with no options leaves the
        // tree empty, so the next read is still the first.
        bool first = _tree.Nodes.Count == 0;
        bool rebuild = first || _groups == null || !SameShape(_groups, groups) || _tree.Nodes.Count != groups.Count;
        if (rebuild) Rebuild(groups, first);
        else UpdateTexts(groups);
        _groups = groups;
        ShowDetails();

        // The first options shown: the tree takes focus with the first option selected, which the reader says.
        if (first && _tree.Nodes.Count > 0)
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
            // F2 changes for this session, Shift+F2 makes a change permanent, Ctrl+F2 resets to the default.
            FenixChangeKind? change = keyData == Keys.F2 ? FenixChangeKind.Session
                : keyData == (Keys.Shift | Keys.F2) ? FenixChangeKind.Permanent
                : keyData == (Keys.Control | Keys.F2) ? FenixChangeKind.Reset
                : null;
            if (change is { } kind)
            {
                // Bit 30 again: holding the key must not ask twice.
                if ((msg.LParam.ToInt64() & 0x40000000) == 0) _ = ChangeAsync(kind);
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
