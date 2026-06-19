using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.HS787;

/// <summary>
/// Accessible EFB tablet display for the HorizonSim 787-9.
/// Content is pushed by the JS bridge (hs787-efb-bridge.js) via EFBBridgeServer on port 19778.
/// The page title is displayed in a dedicated bold label and announced on navigation.
/// Page content is shown in a ListBox so screen readers can navigate line-by-line.
/// Buttons are dynamically generated and numbered 1-9 for Alt+N keyboard shortcuts.
/// </summary>
public partial class HS787EFBForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly CoherentHS787EfbClient _efb;
    private readonly ScreenReaderAnnouncer _announcer;

    private string _previousTitle = "";
    private string[] _currentButtons = Array.Empty<string>();
    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _disposed;

    public HS787EFBForm(ScreenReaderAnnouncer announcer)
    {
        // The EFB is now read + driven over the Coherent debugger (HSB789_EFB) — no HTTP
        // bridge, no injected hs787-efb-bridge.js, no HTML patching. The form owns the client.
        _efb = new CoherentHS787EfbClient();
        _efb.Start();
        _announcer = announcer;

        InitializeComponent();
        SetupEventHandlers();

        _efb.StateUpdated += BridgeServer_StateUpdated;
    }

    // Only poll while the window is visible (socket + agent stay warm while hidden).
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!_disposed) _efb.SetActive(Visible);
    }

    // ------------------------------------------------------------------
    // Event wiring
    // ------------------------------------------------------------------

    private void SetupEventHandlers()
    {
        Load += (s, e) => contentList.Focus();

        FormClosing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero)
                SetForegroundWindow(_previousWindow);
        };

        this.KeyDown += Form_KeyDown;
        refreshButton.Click += (_, _) => _efb.EnqueueCommand("read_screen");
        groundServicesButton.Click += (_, _) => NavigateGroundServices();
    }

    private void NavigateGroundServices()
    {
        _efb.EnqueueCommand("navigate_ground_services");
        // Force a fresh screen read after navigation so the form updates
        Task.Delay(900).ContinueWith(_ =>
        {
            // try/catch closes the TOCTOU window between the IsHandleCreated check and BeginInvoke
            // (the form can be disposed in between, throwing on this threadpool continuation).
            try
            {
                if (IsHandleCreated)
                    BeginInvoke(() => _efb.EnqueueCommand("read_screen"));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });
    }

    // ------------------------------------------------------------------
    // Bridge state handler
    // ------------------------------------------------------------------

    private void BridgeServer_StateUpdated(object? sender, EFBStateUpdateEventArgs e)
    {
        if (e.Type != "efb_screen") return;

        string title = e.Data.TryGetValue("pageTitle", out string? pt) ? pt ?? "" : "";
        string text  = e.Data.TryGetValue("text",      out string? t)  ? t  ?? "" : "";
        int btnCount = e.Data.TryGetValue("buttonCount", out string? bcStr) && int.TryParse(bcStr, out int bc) ? bc : 0;

        var buttons = new string[btnCount];
        for (int i = 0; i < btnCount; i++)
            buttons[i] = e.Data.TryGetValue($"btn{i}", out string? b) ? b ?? $"Button {i + 1}" : $"Button {i + 1}";

        if (IsHandleCreated)
            BeginInvoke(() => UpdateDisplay(title, text, buttons));
    }

    // ------------------------------------------------------------------
    // Display update
    // ------------------------------------------------------------------

    private void UpdateDisplay(string title, string text, string[] buttons)
    {
        bool connected = _efb.IsBridgeConnected;

        statusLabel.Text = connected
            ? "EFB Connected"
            : "EFB Bridge Not Connected — install mod package and restart MSFS";

        if (!connected) return;

        // Page title — update label and announce navigation when the page changes
        bool pageChanged = title != _previousTitle;
        pageTitleLabel.Text = $"Page: {(string.IsNullOrWhiteSpace(title) ? "—" : title)}";
        if (pageChanged && !string.IsNullOrWhiteSpace(title))
        {
            _announcer.Announce(title);
            _previousTitle = title;
        }

        // Content list — split text into lines; each is a separately navigable item
        // The title is excluded from the text by the bridge (sent as pageTitle only)
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();

        int savedIndex = contentList.SelectedIndex;
        contentList.BeginUpdate();
        while (contentList.Items.Count > lines.Count) contentList.Items.RemoveAt(contentList.Items.Count - 1);
        while (contentList.Items.Count < lines.Count) contentList.Items.Add("");
        for (int i = 0; i < lines.Count; i++)
        {
            if (contentList.Items[i]?.ToString() != lines[i])
                contentList.Items[i] = lines[i];
        }
        contentList.EndUpdate();

        // On page navigation reset to top; otherwise preserve position
        if (pageChanged)
            contentList.SelectedIndex = contentList.Items.Count > 0 ? 0 : -1;
        else if (savedIndex >= 0 && savedIndex < contentList.Items.Count)
            contentList.SelectedIndex = savedIndex;

        // Buttons
        if (!ButtonLabelsMatch(buttons, _currentButtons))
            RebuildButtonPanel(buttons);
    }

    private static bool ButtonLabelsMatch(string[] a, string[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    // Clears the button panel and builds one Button per EFB button.
    // Buttons 1-9 get a numeric prefix ("1. Back") so Alt+1-9 shortcuts are self-documenting.
    // Disabled buttons become disabled controls — screen readers announce them as "unavailable".
    private void RebuildButtonPanel(string[] buttons)
    {
        bool hadFocusInPanel = buttonsPanel.ContainsFocus;

        buttonsPanel.SuspendLayout();
        // Dispose the old buttons before clearing. Controls.Clear() only removes them from the
        // collection — it does NOT dispose them, so each rebuild (one per EFB page change) would
        // otherwise leak the Win32 handle and the captured Click delegate. Over a long session with
        // frequent page changes that accumulates.
        var old = new List<Control>();
        foreach (Control c in buttonsPanel.Controls) old.Add(c);
        buttonsPanel.Controls.Clear();
        foreach (Control c in old) c.Dispose();

        int btnWidth = buttonsPanel.Width - SystemInformation.VerticalScrollBarWidth - 6;

        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            string rawLabel = buttons[i];
            bool disabled = rawLabel.EndsWith(" (disabled)", StringComparison.Ordinal);
            string label = disabled ? rawLabel[..^" (disabled)".Length].TrimEnd() : rawLabel;

            // Prefix buttons 1-9 with a number so the Alt+N shortcuts are visible
            string displayLabel = i < 9 ? $"{i + 1}. {label}" : label;

            var btn = new Button
            {
                Text = displayLabel,
                AccessibleName = displayLabel,
                Enabled = !disabled,
                Location = new Point(2, 2 + i * 32),
                Size = new Size(btnWidth, 28),
                TabIndex = i
            };
            btn.Click += (_, _) => ClickButton(idx);
            buttonsPanel.Controls.Add(btn);
        }

        buttonsPanel.ResumeLayout();
        _currentButtons = buttons;

        if (hadFocusInPanel && buttonsPanel.Controls.Count > 0)
            buttonsPanel.Controls[0].Focus();
    }

    // ------------------------------------------------------------------
    // Button activation
    // ------------------------------------------------------------------

    private void ClickButton(int index)
    {
        if (index < 0 || index >= _currentButtons.Length) return;
        _efb.EnqueueCommand($"click_btn_{index}");
    }

    // ------------------------------------------------------------------
    // Keyboard shortcuts
    // ------------------------------------------------------------------

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        // Alt+1-9: press button N (N is 1-indexed)
        if (e.Alt && !e.Control && !e.Shift && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
        {
            int idx = e.KeyCode - Keys.D1;
            if (idx < _currentButtons.Length)
                ClickButton(idx);
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+P: focus page content list
        if (e.Alt && !e.Control && !e.Shift && e.KeyCode == Keys.P)
        {
            contentList.Focus();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+B: focus first button
        if (e.Alt && !e.Control && !e.Shift && e.KeyCode == Keys.B)
        {
            if (buttonsPanel.Controls.Count > 0)
                buttonsPanel.Controls[0].Focus();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+R: refresh
        if (e.Alt && !e.Control && !e.Shift && e.KeyCode == Keys.R)
        {
            _efb.EnqueueCommand("read_screen");
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+G: navigate to ground services
        if (e.Alt && !e.Control && !e.Shift && e.KeyCode == Keys.G)
        {
            NavigateGroundServices();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        contentList.Focus();
        NavigateGroundServices();
    }

    // ------------------------------------------------------------------
    // Dispose
    // ------------------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _efb.StateUpdated -= BridgeServer_StateUpdated;
            _efb.Dispose();
        }
        base.Dispose(disposing);
    }
}
