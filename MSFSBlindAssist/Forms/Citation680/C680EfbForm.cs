using System.Runtime.InteropServices;
using System.Text.Json;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.Citation680;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.Citation680;

/// <summary>
/// The vendor EFB (Shift+T) as a Page combo, a Tab combo, a Section combo (Checklists: the
/// sections of the chosen checklist category) and a list of the active page's rows from
/// coherent-c680-efb-agent.js: service cards and settings as "Label: on/off" or "Label: A* / B",
/// Access doors as "Door: open/closed", buttons as "[Label]", the rest as text in column order.
/// Enter on a card, setting or button row acts on it (toggles the checkbox, steps the selector,
/// presses the button) and speaks the result. F5 re-reads, Escape closes. One inspector socket:
/// the window owns it.
/// </summary>
public sealed class C680EfbForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SettleMs = 400;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private sealed class PageItem { public string id { get; set; } = ""; public string label { get; set; } = ""; public bool active { get; set; } }
    private sealed class TabItem { public string label { get; set; } = ""; public bool active { get; set; } }
    private sealed class SectionItem { public string id { get; set; } = ""; public string label { get; set; } = ""; public bool active { get; set; } }

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IntPtr _previousWindow;
    private readonly ComboBox _page;
    private readonly ComboBox _tab;
    private readonly ComboBox _section;
    private readonly DisplayListBox _list;
    private readonly CoherentDisplayClient _client;
    private List<PageItem> _pages = new();
    private List<SectionItem> _sections = new();
    private bool _syncing;
    private bool _busy;
    private bool _everConnected;

    public C680EfbForm(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;
        _previousWindow = GetForegroundWindow();
        Text = "Citation Sovereign+ EFB";
        Size = new Size(640, 620);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var pl = new Label { Text = "Page", AutoSize = true, Location = new Point(12, 14) };
        _page = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(60, 10), Width = 160, AccessibleName = "Page", TabIndex = 1 };
        var tl = new Label { Text = "Tab", AutoSize = true, Location = new Point(240, 14) };
        _tab = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(280, 10), Width = 220, AccessibleName = "Tab", TabIndex = 2 };
        var sl = new Label { Text = "Section", AutoSize = true, Location = new Point(12, 48) };
        _section = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(80, 44), Width = 532, AccessibleName = "Checklist section", TabIndex = 3, Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _list = new DisplayListBox
        {
            Location = new Point(12, 78), Size = new Size(600, 486), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = "EFB page", TabIndex = 0
        };
        _list.SetText("Connecting to the EFB…");
        Controls.AddRange(new Control[] { pl, _page, tl, _tab, sl, _section, _list });

        _page.SelectedIndexChanged += async (_, _) => { if (!_syncing && _page.SelectedIndex >= 0 && _page.SelectedIndex < _pages.Count) await Act($"__MSFSBA_C680_EFB.goPage({Js(_pages[_page.SelectedIndex].id)})", _pages[_page.SelectedIndex].label, readTabs: true); };
        _tab.SelectedIndexChanged += async (_, _) => { if (!_syncing && _tab.SelectedItem is string t) await Act($"__MSFSBA_C680_EFB.goTab({Js(t)})", t, readTabs: true); };
        _section.SelectedIndexChanged += async (_, _) =>
        {
            if (_syncing || _section.SelectedIndex < 0 || _section.SelectedIndex >= _sections.Count) return;
            var s = _sections[_section.SelectedIndex];
            await Act($"__MSFSBA_C680_EFB.goSection({Js(s.id)})", s.label);
        };

        _client = new CoherentDisplayClient(" - EFB", 1500, "coherent-c680-efb-agent.js");
        _client.RowsUpdated += rows => { if (_busy) return; Apply(rows); if (!_everConnected) { _everConnected = true; _ = SyncNavAsync(); } };
        _client.Error += msg => { if (!_everConnected) _list.SetText("The EFB is not answering: " + msg); };

        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
            if (e.KeyCode == Keys.F5) { e.Handled = true; Apply(await _client.ScrapeNowAsync()); await SyncNavAsync(); return; }
            // A keypad or keyboard popup ("Set SimBrief User ID"): typed digits and letters press its keys,
            // Backspace its Clear. The popup's own Cancel and Set/OK rows take Enter like any button.
            bool popup = _list.Items.Count > 0 && (_list.Items[0]?.ToString() ?? "").StartsWith("Popup:", StringComparison.Ordinal);
            if (popup && _list.Focused && e.Modifiers == Keys.None)
            {
                string? key = null;
                if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) key = ((char)('0' + (e.KeyCode - Keys.D0))).ToString();
                else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) key = ((char)('0' + (e.KeyCode - Keys.NumPad0))).ToString();
                else if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z) key = ((char)('A' + (e.KeyCode - Keys.A))).ToString();
                else if (e.KeyCode == Keys.Back) key = "Clear";
                if (key != null)
                {
                    e.Handled = true; e.SuppressKeyPress = true;
                    await Act($"__MSFSBA_C680_EFB.press({Js(key)})", key, speakRow: 1);
                    return;
                }
            }
            if (e.KeyCode == Keys.Enter && _list.Focused && _list.SelectedIndex > 0)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                int i = _list.SelectedIndex;
                string row = _list.Items[i]?.ToString() ?? "";
                if (row.StartsWith("[", StringComparison.Ordinal) || row.Contains(": ", StringComparison.Ordinal))
                {
                    await Act($"__MSFSBA_C680_EFB.act({i})", row.StartsWith("[", StringComparison.Ordinal) ? row.Trim('[', ']') : row, pressedRow: row);
                    return;
                }
                _announcer.AnnounceImmediate("Nothing to do on this row");
            }
        };
        Shown += (_, _) => { _list.Focus(); _client.Start(); };
        FormClosing += (_, _) => { _client.Dispose(); if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow); };
    }

    private void Apply(IReadOnlyList<string> rows)
    {
        if (rows.Count == 0) return;
        int keep = _list.SelectedIndex;
        _list.SetLines(rows);
        if (keep >= 0 && keep < _list.Items.Count && _list.SelectedIndex < 0) _list.SelectedIndex = keep;
        if (_list.SelectedIndex < 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private async Task SyncNavAsync()
    {
        _syncing = true;
        try
        {
            string pj = await _client.InvokeAsync("__MSFSBA_C680_EFB.pages()");
            var pages = Parse<List<PageItem>>(pj) ?? new List<PageItem>();
            if (pages.Count > 0)
            {
                _pages = pages;
                _page.Items.Clear();
                foreach (var p in pages) _page.Items.Add(p.label);
                int ai = pages.FindIndex(p => p.active); if (ai >= 0) _page.SelectedIndex = ai;
            }
            string tj = await _client.InvokeAsync("__MSFSBA_C680_EFB.tabs()");
            var tabs = Parse<List<TabItem>>(tj) ?? new List<TabItem>();
            _tab.Items.Clear();
            foreach (var t in tabs) _tab.Items.Add(t.label);
            int ti = tabs.FindIndex(t => t.active); if (ti >= 0) _tab.SelectedIndex = ti;
            _tab.Enabled = tabs.Count > 0;

            // Checklists only: the sections of the chosen category (Abnormal has 243, labelled by CAS message).
            string sj = await _client.InvokeAsync("__MSFSBA_C680_EFB.sections()");
            _sections = Parse<List<SectionItem>>(sj) ?? new List<SectionItem>();
            _section.Items.Clear();
            foreach (var s in _sections) _section.Items.Add(s.label);
            int si = _sections.FindIndex(s => s.active); if (si >= 0) _section.SelectedIndex = si;
            _section.Enabled = _sections.Count > 0;
        }
        catch { }
        finally { _syncing = false; }
    }

    /// <summary>
    /// Drive the EFB, wait, re-read. <paramref name="speakRow"/> speaks that row of the new read (a
    /// popup keypad's entry line); <paramref name="pressedRow"/> speaks the result of acting on a row
    /// (<see cref="C680EfbRows.SpokenAfterAct"/>). Page, tab and section changes speak nothing: the
    /// combo already announced the choice.
    /// </summary>
    private async Task Act(string expr, string spoken, bool readTabs = false, int speakRow = -1, string? pressedRow = null)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            string r = await _client.InvokeAsync(expr);
            if (r == "noaction") { _announcer.AnnounceImmediate("Nothing to do on this row"); return; }
            if (r == "none") { _announcer.AnnounceImmediate(spoken + " is not on this page"); return; }
            if (r.Length == 0) { _announcer.AnnounceImmediate("The EFB did not answer"); return; }
            await Task.Delay(SettleMs);
            var rows = await _client.ScrapeNowAsync();
            Apply(rows);
            if (readTabs) await SyncNavAsync();
            if (pressedRow != null) _announcer.AnnounceImmediate(C680EfbRows.SpokenAfterAct(pressedRow, rows));
            else if (speakRow >= 0 && speakRow < rows.Count) _announcer.AnnounceImmediate(rows[speakRow]);
        }
        finally { _busy = false; }
    }

    private static T? Parse<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch { return null; }
    }

    private static string Js(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
}
