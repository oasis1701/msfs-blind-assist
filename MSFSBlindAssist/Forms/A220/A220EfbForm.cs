using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Forms.A220;

/// <summary>
/// Accessible Synaptic A220 EFB tablet window (Shift+T).
///
/// Presents the in-page agent's collected items (see
/// <c>Resources/coherent-a220-efb-agent.js</c>) as a flat ListBox the screen
/// reader can arrow through — the same list-index dispatch idiom as the PMDG EFB.
/// Enter activates buttons/toggles; Enter or F2 on a field opens a value prompt;
/// F5 re-collects; Escape hides the window (state survives a reopen — the form is
/// disposed for real by the A220 definition's StopAllMotion on aircraft swap).
///
/// Screen-reader rules (CLAUDE.md): list navigation and activations are never
/// announced (the screen reader already reads focus/selection changes); the form
/// only speaks errors, EFB confirmation dialogs (async pop-ups the list refresh
/// discovers), a page change after an action, and a changed toggle value —
/// all async results, not UI echoes.
/// </summary>
public sealed class A220EfbForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int PostActionRefreshDelayMs = 600;

    private readonly A220EfbClient _client;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Label _breadcrumb;
    private readonly ListBox _list;
    private List<A220EfbClient.Item> _items = new();
    private string _page = "";
    private string? _lastAnnouncedAlert;
    private bool _busy;
    private IntPtr _previousWindow = IntPtr.Zero;

    public A220EfbForm(A220EfbClient client, ScreenReaderAnnouncer announcer)
    {
        _client = client;
        _announcer = announcer;

        Text = "A220 EFB";
        Size = new Size(760, 640);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _breadcrumb = new Label
        {
            Location = new Point(12, 10),
            Size = new Size(720, 24),
            Text = "Loading…",
            AccessibleName = "EFB page"
        };
        Controls.Add(_breadcrumb);

        _list = new ListBox
        {
            Location = new Point(12, 40),
            Size = new Size(720, 500),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            IntegralHeight = false,
            AccessibleName = "EFB items"
        };
        // Claim Enter so the ListBox delivers it to KeyDown instead of swallowing it.
        _list.PreviewKeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.F2) e.IsInputKey = true; };
        _list.KeyDown += OnListKeyDown;
        _list.DoubleClick += (_, _) => _ = ActivateSelectedAsync();
        Controls.Add(_list);

        var hint = new Label
        {
            Location = new Point(12, 548),
            Size = new Size(720, 40),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Text = "Enter: activate or edit.  F2: edit field.  F5: refresh.  Escape: close.",
            AccessibleName = "Keyboard help"
        };
        Controls.Add(hint);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5) { _ = RefreshAsync(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        };

        // Hide on user close so reopening keeps the tablet where it was; the A220
        // definition disposes the form for real on aircraft swap (Form.Dispose()
        // bypasses this handler).
        FormClosing += (_, e) =>
        {
            if (e.CloseReason is CloseReason.ApplicationExitCall
                or CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing)
            {
                return;
            }
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

    // ---- keyboard --------------------------------------------------------

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            _ = ActivateSelectedAsync();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F2)
        {
            var item = SelectedItem();
            if (item != null && item.kind == "field") _ = EditFieldAsync(item);
            e.Handled = true;
        }
    }

    private A220EfbClient.Item? SelectedItem()
    {
        int idx = _list.SelectedIndex;
        return idx >= 0 && idx < _items.Count ? _items[idx] : null;
    }

    private async Task ActivateSelectedAsync()
    {
        var item = SelectedItem();
        if (item == null || _busy) return;
        switch (item.kind)
        {
            case "button":
            case "toggle":
            {
                _busy = true;
                try
                {
                    string result = await _client.ClickAsync(item.i);
                    if (result.StartsWith("ERR", StringComparison.Ordinal) || result.Length == 0)
                    {
                        _announcer.AnnounceImmediate("The EFB did not accept the action. " +
                            (result.Length == 0 ? "The tablet is not reachable." : result));
                        return;
                    }
                    await Task.Delay(PostActionRefreshDelayMs);
                    await RefreshAsync(afterAction: true, actionIndex: _list.SelectedIndex);
                }
                finally { _busy = false; }
                break;
            }
            case "field":
                await EditFieldAsync(item);
                break;
        }
    }

    private async Task EditFieldAsync(A220EfbClient.Item item)
    {
        if (_busy) return;
        string? input = PromptValue(item.label, item.value);
        if (input == null) return;
        _busy = true;
        try
        {
            string committed = await _client.SetFieldAsync(item.i, input);
            if (committed.StartsWith("ERR", StringComparison.Ordinal) || committed.Length == 0)
            {
                _announcer.AnnounceImmediate("The value could not be set. " +
                    (committed.Length == 0 ? "The tablet is not reachable." : committed));
                return;
            }
            // Numeric input confirmation — the one interaction that IS announced.
            _announcer.AnnounceImmediate($"{item.label} set to {(committed.Length == 0 ? "empty" : committed)}");
            await Task.Delay(PostActionRefreshDelayMs);
            await RefreshAsync();
        }
        finally { _busy = false; }
    }

    private string? PromptValue(string label, string current)
    {
        using var dialog = new Form
        {
            Text = label,
            Size = new Size(430, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false
        };
        var prompt = new Label { Text = label, Location = new Point(12, 10), Size = new Size(390, 20) };
        var box = new TextBox { Text = current, Location = new Point(12, 34), Size = new Size(390, 26), AccessibleName = label };
        var ok = new Button { Text = "&OK", DialogResult = DialogResult.OK, Location = new Point(238, 70), Size = new Size(80, 30) };
        var cancel = new Button { Text = "&Cancel", DialogResult = DialogResult.Cancel, Location = new Point(324, 70), Size = new Size(80, 30) };
        dialog.Controls.AddRange(new Control[] { prompt, box, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        box.SelectAll();
        return dialog.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }

    // ---- refresh ---------------------------------------------------------

    private async Task RefreshAsync(bool afterAction = false, int actionIndex = -1)
    {
        var snap = await _client.CollectAsync();
        if (IsDisposed) return;

        if (!snap.ok)
        {
            string message = snap.error ?? "The A220 EFB is not available.";
            _breadcrumb.Text = message;
            _announcer.AnnounceImmediate(message);
            return;
        }

        string previousPage = _page;
        string? previousLabel = afterAction && actionIndex >= 0 && actionIndex < _items.Count
            ? Render(_items[actionIndex]) : null;

        _page = snap.page ?? "";
        _items = snap.items!;
        _breadcrumb.Text = $"Page: {_page}";
        Text = $"A220 EFB — {_page}";

        int keep = _list.SelectedIndex;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in _items) _list.Items.Add(Render(item));
        if (_list.Items.Count > 0)
            _list.SelectedIndex = Math.Min(Math.Max(keep, 0), _list.Items.Count - 1);
        _list.EndUpdate();

        // EFB confirmation/alert cards are async pop-ups — speak once per appearance.
        var alert = _items.FirstOrDefault(x => x.kind == "alert");
        if (alert != null)
        {
            if (_lastAnnouncedAlert != alert.label)
            {
                _lastAnnouncedAlert = alert.label;
                _announcer.AnnounceImmediate(alert.label);
            }
        }
        else
        {
            _lastAnnouncedAlert = null;
        }

        if (afterAction)
        {
            if (_page != previousPage)
            {
                _announcer.AnnounceImmediate(_page);
            }
            else if (previousLabel != null && actionIndex < _items.Count)
            {
                string now = Render(_items[actionIndex]);
                if (now != previousLabel && alert == null) _announcer.AnnounceImmediate(now);
            }
        }
    }

    private static string Render(A220EfbClient.Item item) => item.kind switch
    {
        "button" => $"{item.label} (button)",
        "toggle" => $"{item.label}: {item.value} (toggle)",
        "field" => $"{item.label}: {(item.value.Length == 0 ? "empty" : item.value)} (edit)",
        "alert" => $"Alert: {item.label}",
        _ => item.label
    };
}
