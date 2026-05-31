using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Forms.A32NX;

/// <summary>
/// Accessible, generic live view of the FlyByWire A320 flyPad (EFB). Renders
/// whatever page is open as a flat list of native accessible controls, scraped
/// from the live DOM via the generic CDP agent. No per-feature knowledge.
/// </summary>
public sealed class A32NXEFBForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly CoherentEFBClient _client;

    private Label _statusLabel = null!;
    private Button _selfTestBtn = null!;
    private FlowLayoutPanel _list = null!;
    private System.Windows.Forms.Timer _pollTimer = null!;

    private IntPtr _previousWindow = IntPtr.Zero;
    private string _renderSignature = "";
    private string _lastPage = "";
    private bool _polling;
    private DateTime _lastScrapeUtc = DateTime.MinValue;
    private bool _lastScrapeOk;
    private string _lastScrapeError = "";

    public A32NXEFBForm(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;

        string agentPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "coherent-a32nx-flypad-agent.js");
        _client = new CoherentEFBClient(agentPath);
        _client.Connected    += (_, _) => { if (IsHandleCreated) BeginInvoke(UpdateStatus); };
        _client.Disconnected += (_, _) => { if (IsHandleCreated) BeginInvoke(UpdateStatus); };

        Text = "FlyByWire A320 flyPad";
        AccessibleName = "FlyByWire A320 flyPad";
        Size = new Size(700, 640);
        MinimumSize = new Size(560, 480);
        KeyPreview = true;

        BuildLayout();

        _pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _pollTimer.Tick += async (_, _) => await PollOnce();

        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F5) { e.Handled = true; await PollOnce(force: true); }
        };

        FormClosing += (_, e) =>
        {
            // Hide instead of dispose so reopening is instant; release the single
            // CDP connection so the verification tool / other consumers can use it.
            e.Cancel = true;
            _pollTimer.Stop();
            _client.Stop();
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;

        _client.Start();          // connect + install agent + powerOn
        _pollTimer.Start();
        UpdateStatus();
        _selfTestBtn.Focus();
    }

    private void BuildLayout()
    {
        SuspendLayout();

        _statusLabel = new Label
        {
            Dock = DockStyle.Top, Height = 24, Text = "Connecting…",
            AccessibleName = "Connection status", TabIndex = 0
        };

        _selfTestBtn = new Button
        {
            Dock = DockStyle.Top, Height = 28, Text = "Status / Self-test",
            AccessibleName = "Status and self-test", TabIndex = 1
        };
        _selfTestBtn.Click += async (_, _) => await SelfTest();

        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(8), TabIndex = 2,
            AccessibleName = "flyPad controls"
        };

        Controls.Add(_list);
        Controls.Add(_selfTestBtn);
        Controls.Add(_statusLabel);
        ResumeLayout();
    }

    // ── polling ────────────────────────────────────────────────────────────────

    private async Task PollOnce(bool force = false)
    {
        if (_polling) return;
        _polling = true;
        try
        {
            if (!_client.IsReady) { UpdateStatus(); return; }
            var scrape = await _client.ScrapeAsync();
            _lastScrapeUtc = DateTime.UtcNow;
            _lastScrapeOk = scrape.Ok;
            _lastScrapeError = scrape.Error ?? "";
            if (!scrape.Ok)
            {
                // flyPad probably powered off — nudge it and keep last good render.
                await _client.PowerOnAsync();
                UpdateStatus();
                return;
            }
            string sig = BuildSignature(scrape);
            if (force || sig != _renderSignature)
            {
                Render(scrape);
                _renderSignature = sig;
            }
            if (scrape.Page != _lastPage)
            {
                _lastPage = scrape.Page;
                _announcer?.Announce(scrape.Page);
            }
            UpdateStatus();
        }
        finally { _polling = false; }
    }

    private static string BuildSignature(FlypadScrape s)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(s.Page).Append('|');
        foreach (var e in s.Elements)
            sb.Append(e.Idx).Append(':').Append(e.Kind).Append(':')
              .Append(e.Text).Append('=').Append(e.Value)
              .Append(e.Disabled ? "#d" : "").Append(';');
        return sb.ToString();
    }

    // ── rendering ────────────────────────────────────────────────────────────

    private void Render(FlypadScrape scrape)
    {
        // Remember which scraped element had focus so we can restore it post-rebuild.
        int focusedIdx = (_list.GetContainerControl() as ContainerControl)?.ActiveControl is Control c
                         && c.Tag is int ti ? ti : -1;

        _list.SuspendLayout();
        _list.Controls.Clear();
        Control? toFocus = null;

        foreach (var e in scrape.Elements)
        {
            Control? ctrl = BuildControl(e);
            if (ctrl == null) continue;
            ctrl.Tag = e.Idx;
            ctrl.Width = _list.ClientSize.Width - 28;
            _list.Controls.Add(ctrl);
            if (e.Idx == focusedIdx) toFocus = ctrl;
        }

        _list.ResumeLayout();
        toFocus?.Focus();
    }

    private Control? BuildControl(FlypadElement e)
    {
        // Headings → label with Heading role (NVDA H-key nav).
        if (e.Kind == "heading")
            return new Label { Text = e.Text, AutoSize = true, AccessibleRole = AccessibleRole.StaticText,
                               Font = new Font(Font, FontStyle.Bold), AccessibleName = e.Text };

        // Toggles / checkboxes / checklist items → CheckBox.
        if (e.ControlType == "checkbox")
        {
            var cb = new CheckBox
            {
                Text = string.IsNullOrEmpty(e.Text) ? "(toggle)" : e.Text,
                Checked = e.Value == "true", AutoSize = true,
                Enabled = !e.Disabled, AccessibleName = e.Text
            };
            cb.Click += async (_, _) => { if (cb.Tag is int idx) await _client.SetValueAsync(idx, cb.Checked ? "true" : "false"); ScheduleQuickRescrape(); };
            return cb;
        }

        // Selects → ComboBox.
        if (e.ControlType == "select")
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Enabled = !e.Disabled,
                                       AccessibleName = e.Text };
            combo.Items.AddRange(e.Options.Cast<object>().ToArray());
            combo.SelectedItem = e.Value;
            combo.SelectionChangeCommitted += async (_, _) =>
            { if (combo.Tag is int idx && combo.SelectedItem != null) await _client.SetValueAsync(idx, combo.SelectedItem.ToString()!); ScheduleQuickRescrape(); };
            return combo;
        }

        // Text/numeric inputs → labeled TextBox (commit on Enter / leave).
        if (e.ControlType == "text")
        {
            var panel = new Panel { Height = 26, AccessibleName = e.Text };
            var box = new TextBox { Text = e.Value, Width = 160, Dock = DockStyle.Right,
                                    Enabled = !e.Disabled, AccessibleName = e.Text };
            async void Commit() { if (box.Tag is int idx) await _client.SetValueAsync(idx, box.Text); ScheduleQuickRescrape(); }
            box.Leave += (_, _) => Commit();
            box.KeyDown += (_, ke) => { if (ke.KeyCode == Keys.Enter) { ke.Handled = true; Commit(); } };
            panel.Controls.Add(box);
            panel.Controls.Add(new Label { Text = e.Text, Dock = DockStyle.Fill, AccessibleName = e.Text });
            // Tag carries idx for both panel (focus restore) and box (commit).
            box.Tag = e.Idx;
            return panel;
        }

        // Clickable links/buttons/tabs → Button.
        if (e.Clickable)
        {
            var btn = new Button { Text = string.IsNullOrEmpty(e.Text) ? "(button)" : e.Text,
                                   AutoSize = true, Enabled = !e.Disabled, AccessibleName = e.Text };
            btn.Click += async (_, _) => { if (btn.Tag is int idx) await _client.ClickAsync(idx); ScheduleQuickRescrape(); };
            return btn;
        }

        // Plain descriptive text.
        if (!string.IsNullOrEmpty(e.Text))
            return new Label { Text = e.Text, AutoSize = true, AccessibleName = e.Text };

        return null;
    }

    private void ScheduleQuickRescrape()
    {
        var t = new System.Windows.Forms.Timer { Interval = 300 };
        t.Tick += async (_, _) => { t.Stop(); t.Dispose(); await PollOnce(force: true); };
        t.Start();
    }

    // ── status / diagnostics ───────────────────────────────────────────────────

    private void UpdateStatus()
    {
        if (!_client.IsReady) { _statusLabel.Text = "flyPad not detected — connecting…"; return; }
        if (!_lastScrapeOk && _lastScrapeUtc != DateTime.MinValue)
        { _statusLabel.Text = $"flyPad off — {_lastScrapeError}"; return; }
        int n = _list.Controls.Count;
        string age = _lastScrapeUtc == DateTime.MinValue ? "—" : $"{(int)(DateTime.UtcNow - _lastScrapeUtc).TotalSeconds}s ago";
        _statusLabel.Text = $"Live: \"{_lastPage}\", {n} controls (updated {age})";
    }

    private async Task SelfTest()
    {
        string pong = await _client.PingAsync();
        var scrape = await _client.ScrapeAsync();
        string msg = _client.IsReady
            ? (pong == "MSFSBA_FLYPAD_OK"
                ? (scrape.Ok
                    ? $"Connected. Agent installed. Page \"{scrape.Page}\", {scrape.Elements.Count} controls."
                    : $"Connected, agent installed, but scrape failed: {scrape.Error}")
                : "Connected, but agent not responding — reinstalling.")
            : "Not connected to the flyPad. Is the FBW A320 loaded and the tablet powered on?";
        _announcer?.Announce(msg);
        UpdateStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _pollTimer?.Dispose(); _client?.Dispose(); }
        base.Dispose(disposing);
    }
}
