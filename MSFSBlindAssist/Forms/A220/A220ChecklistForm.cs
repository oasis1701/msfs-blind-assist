using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Forms.A220;

/// <summary>
/// A220 ECL "first officer" (Ctrl+Shift+C). Reads the live CHKL window from the
/// shared DisplayUnits scrape (agent ecl(): item texts + the 28 px checkbox rects
/// whose fill is the sensed/checked state) and lists items with their states.
/// Tiles (SUMMARY/NORMAL/NON-NORMAL/PROC/FCTN), checklist names and YES/NO options
/// activate via in-window clicks.
///
/// "Do this item for me" maps the item's sensed ECL variable (checklists.json
/// `sensed` name → <see cref="A220EclActionMap"/>) to the def control write —
/// through HandleUIVariableSet, so the APU hold-to-start and the flap detent walk
/// are shared with the panel — then polls the scraped checkbox for ~3 s and
/// announces "sensed complete" or not. MSFSBA NEVER ticks a checklist box itself;
/// item-at-a-time only, no run-whole-checklist, and excluded items (fire handles,
/// thrust levers, sidestick) say WHY honestly.
/// </summary>
public sealed class A220ChecklistForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly SynapticA220Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Label _breadcrumb;
    private readonly ListBox _list;
    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _busy;

    private A220FmsScreenParsing.EclModel? _model;
    private A220ChecklistPack? _pack;
    private bool _packLoadAttempted;

    public A220ChecklistForm(SynapticA220Definition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;

        Text = "A220 Electronic Checklist";
        Size = new Size(760, 660);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _breadcrumb = new Label
        {
            Location = new Point(12, 10),
            Size = new Size(720, 24),
            Text = "Loading…",
            AccessibleName = "Checklist context"
        };
        Controls.Add(_breadcrumb);

        _list = new ListBox
        {
            Location = new Point(12, 40),
            Size = new Size(720, 440),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            IntegralHeight = false,
            AccessibleName = "Checklist items"
        };
        _list.PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) e.IsInputKey = true; };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { _ = ActivateSelectedAsync(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        _list.DoubleClick += (_, _) => _ = ActivateSelectedAsync();
        Controls.Add(_list);

        int bx = 12, by = 492;
        void Add(string text, EventHandler onClick, int width = 120)
        {
            var b = new Button
            {
                Location = new Point(bx, by),
                Size = new Size(width, 30),
                Text = text,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            b.Click += onClick;
            Controls.Add(b);
            bx += width + 8;
            if (bx > 620) { bx = 12; by += 36; }
        }

        Add("&Do this item for me", (_, _) => _ = DoSelectedItemAsync(), 160);
        Add("Read &current item", (_, _) => ReadCurrentItem(), 140);
        Add("&Refresh", (_, _) => _ = RefreshAsync(), 100);
        Add("&Open checklist window", (_, _) => _ = OpenChklWindowAsync(), 170);
        bx = 12; by += 36;
        Add("&Summary", (_, _) => _ = ClickTextAsync("SUMMARY"), 100);
        Add("&Normal", (_, _) => _ = ClickTextAsync("NORMAL"), 95);
        Add("Non-nor&mal", (_, _) => _ = ClickTextAsync("NON-NORMAL"), 110);
        Add("&Proc", (_, _) => _ = ClickTextAsync("PROC"), 80);
        Add("&Fctn", (_, _) => _ = ClickTextAsync("FCTN"), 80);

        var hint = new Label
        {
            Location = new Point(12, by + 40),
            Size = new Size(720, 40),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Text = "Enter on a name/option: open it.  Enter on an item: do it for me.  F5: refresh.  Escape: close.",
            AccessibleName = "Keyboard help"
        };
        Controls.Add(hint);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5) { _ = RefreshAsync(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        };

        FormClosing += (_, e) =>
        {
            if (e.CloseReason is CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing)
                return;
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        Activate();
        _list.Focus();
        _ = RefreshAsync();
    }

    // ---- scrape -------------------------------------------------------------

    private async Task<A220FmsScreenParsing.EclModel?> ScrapeAsync()
    {
        string? raw = await _def.DisplaysAgentCallAsync("ecl()");
        if (string.IsNullOrEmpty(raw)) return null;
        var win = A220FmsScreenParsing.ParseAgentTokens(raw);
        if (win == null || !win.Ok) return null;
        return A220FmsScreenParsing.ParseEcl(win.Tokens, win.Boxes);
    }

    private async Task RefreshAsync()
    {
        var model = await ScrapeAsync();
        if (IsDisposed) return;
        _model = model;
        _list.BeginUpdate();
        int keep = _list.SelectedIndex;
        _list.Items.Clear();
        if (model == null)
        {
            _breadcrumb.Text = "No checklist window is open — use Open checklist window";
            _list.EndUpdate();
            return;
        }
        _breadcrumb.Text = (model.Title ?? "Checklist summary")
            + (model.PackName != null ? $"  (pack {model.PackName})" : "");
        foreach (var item in model.Items)
            _list.Items.Add(item.Checked switch
            {
                true => "[done] " + item.Text,
                false => "[open] " + item.Text,
                null => item.Text
            });
        _list.EndUpdate();
        if (keep >= 0 && keep < _list.Items.Count) _list.SelectedIndex = keep;
        else if (_list.Items.Count > 0) _list.SelectedIndex = 0;

        EnsurePackLoaded(model.PackName);
    }

    private void EnsurePackLoaded(string? partNumber)
    {
        if (_packLoadAttempted && _pack != null) return;
        if (_packLoadAttempted && partNumber == null) return;
        _packLoadAttempted = true;
        _ = Task.Run(() =>
        {
            var pack = A220ChecklistPack.LoadFor(partNumber);
            if (pack != null) _pack = pack;
        });
    }

    // ---- actions ------------------------------------------------------------

    private A220FmsScreenParsing.EclItem? SelectedItem()
        => _model != null && _list.SelectedIndex >= 0 && _list.SelectedIndex < _model.Items.Count
            ? _model.Items[_list.SelectedIndex]
            : null;

    private async Task ActivateSelectedAsync()
    {
        var item = SelectedItem();
        if (item == null || _busy) return;
        if (item.Checked == null)
        {
            // Checklist name / tile / YES-NO option / free text: click it in-window.
            // Click the first token of the row (names are single tokens).
            string needle = item.Text.Split("  ", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(item.Text);
            await ClickTextAsync(needle);
            return;
        }
        await DoSelectedItemAsync();
    }

    private async Task ClickTextAsync(string label)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            string? result = await _def.DisplaysAgentCallAsync(
                $"clickWinText(\"ecl\",{A220DisplaysClient.JsString(label)},0)");
            if (result == null || !result.StartsWith("CLICKED", StringComparison.Ordinal))
            {
                _announcer.AnnounceImmediate($"Could not press {label} — {(result == "NO_WINDOW" ? "no checklist window open" : "not shown")}.");
                return;
            }
            await Task.Delay(700);
            await RefreshAsync();
        }
        finally { _busy = false; }
    }

    private async Task OpenChklWindowAsync()
    {
        _def.SendCtpKey("CHKL");
        await Task.Delay(1200);
        await RefreshAsync();
        if (_model == null)
            _announcer.AnnounceImmediate("Checklist window did not appear — is the aircraft powered?");
    }

    private void ReadCurrentItem()
    {
        var model = _model;
        if (model == null) { _announcer.AnnounceImmediate("No checklist window open."); return; }
        var current = model.Items.FirstOrDefault(i => i.Checked == false);
        _announcer.AnnounceImmediate(current != null
            ? $"Current item: {current.Text}"
            : "No open items visible on this page.");
    }

    /// <summary>
    /// "Do this item for me": resolve the scraped line to the checklists.json item,
    /// its sensed ECL variable to the action map, actuate the def control (the
    /// aircraft's own sensing must confirm — we never tick the box), then poll the
    /// re-scraped checkbox state and speak the honest result.
    /// </summary>
    private async Task DoSelectedItemAsync()
    {
        if (_busy) return;
        var item = SelectedItem();
        if (item == null) return;
        if (item.Checked == null)
        {
            _announcer.AnnounceImmediate("That row is not a checklist item.");
            return;
        }
        if (item.Checked == true)
        {
            _announcer.AnnounceImmediate("Already complete.");
            return;
        }

        var model = _model;
        if (model?.Title == null)
        {
            _announcer.AnnounceImmediate("Open a checklist first — this looks like a listing page.");
            return;
        }
        if (_pack == null)
        {
            _announcer.AnnounceImmediate("Checklist content file not found on disk — cannot map this item to a control.");
            return;
        }

        string normalized = A220FmsScreenParsing.NormalizeChallenge(item.Text);
        var packItem = _pack.FindItem(model.Title, normalized);
        if (packItem == null)
        {
            _announcer.AnnounceImmediate("Could not match this line to the checklist content — do it manually.");
            return;
        }
        if (packItem.Sensed == null)
        {
            _announcer.AnnounceImmediate($"{packItem.Challenge}: not a sensed item — complete it manually, response {packItem.Response}.");
            return;
        }
        if (!A220EclActionMap.TryGet(packItem.Sensed, out var action))
        {
            _announcer.AnnounceImmediate("This item's sensed state is unknown to the action map — do it manually.");
            return;
        }
        if (!action.IsMapped)
        {
            _announcer.AnnounceImmediate(action.Exclusion switch
            {
                A220EclExclusion.HardwareAxis =>
                    "This item is a hardware lever or stick input — move your own controls.",
                A220EclExclusion.GuardedDestructive =>
                    "This is a guarded destructive control — operate it deliberately from the cockpit panel.",
                A220EclExclusion.Judgment =>
                    "This is a judgment item — decide and set it yourself.",
                _ => "No control mapping exists for this item yet — do it manually."
            });
            return;
        }

        _busy = true;
        try
        {
            if (!_def.ActuateControl(action.ControlKey!, action.Value, out string error))
            {
                _announcer.AnnounceImmediate($"Could not actuate: {error}");
                return;
            }
            _announcer.AnnounceImmediate($"Setting {packItem.Challenge.TrimStart('*', ' ')} — waiting for the aircraft to sense it.");

            // Poll the scraped checkbox for up to ~6 s (APU start alone holds 3.3 s).
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(750);
                var fresh = await ScrapeAsync();
                var freshItem = fresh?.Items.FirstOrDefault(x =>
                    x.Checked != null && A220FmsScreenParsing.NormalizeChallenge(x.Text) == normalized);
                if (freshItem?.Checked == true)
                {
                    _announcer.AnnounceImmediate("Sensed complete.");
                    await RefreshAsync();
                    return;
                }
            }
            _announcer.AnnounceImmediate("Not sensed complete — check the switch and the checklist manually.");
            await RefreshAsync();
        }
        finally { _busy = false; }
    }
}
