using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.Learjet35;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.Learjet35;

/// <summary>
/// Live read-out window for a Working Title GNS 530 or 430 in the Learjet 35A, with the bezel
/// on the keyboard.
///
/// READ AND WRITE OVER THE SAME COHERENT SOCKET. The GNS renders its pages as DOM text, so
/// the GNS agent (coherent-gns-agent.js) reads the layer on top — startup, self-test, a
/// dialog, or the active page — by structure, then the radios and the status footer; the
/// bezel keys are the H: events the vendor's own bezel fires (`H:AS530_ENT_Push` and
/// friends, the Working Title InteractionEventMap names), sent through the page's
/// SimVar.SetSimVarValue — measured 2026-09-08 to turn the pages, move the cursor and
/// clear the self-test.
///
/// AFTER A KEY THE WINDOW SAYS WHAT THE KEY DID, not what the key was: the agent's state
/// string carries the page or dialog on top, the highlighted row and the character under
/// the cursor of an ident entry, and Lj35GnsSpeech picks the one that matters for the key.
///
/// TWO THINGS ABOUT THE UNIT A PILOT HAS TO KNOW, both measured live: on the self-test page
/// only the LARGE right knob (moves the highlight to "OK?") and ENT do anything, so nothing
/// else on the bezel answers until that is done; and FPL, VNAV and PROC are DETACHED page
/// groups in Working Title's implementation — inside them the large knob turns no page, and
/// CLR (or the same button again) is the way back.
///
/// The key map avoids everything the list itself uses (arrows, Home, End, Page keys), so the
/// bezel sits on Ctrl and Alt: Ctrl+arrows are the right (FMS) knob, Alt+arrows the left
/// (radio) knob, Ctrl+letter the named buttons.
/// </summary>
public sealed class Lj35GnsDisplayForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly CoherentDisplayClient _client;
    private readonly DisplayListBox _text;
    private readonly IntPtr _previousWindow;
    private readonly string _prefix;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly System.Windows.Forms.Timer _connectWatchdog;
    private bool _gotRows;
    private bool _disposed;

    /// <summary>How long the instrument takes to redraw after a bezel event before the state is read back.</summary>
    private const int SettleMs = 350;

    /// <summary>Bezel keys → Working Title GNS interaction event suffix, what to say if the display cannot be read, and the kind of key.</summary>
    private static readonly Dictionary<Keys, (string Event, string Spoken, Lj35GnsKeyKind Kind)> BezelKeys = new()
    {
        [Keys.Control | Keys.Right] = ("RightLargeKnob_Right", "large knob right", Lj35GnsKeyKind.Knob),
        [Keys.Control | Keys.Left] = ("RightLargeKnob_Left", "large knob left", Lj35GnsKeyKind.Knob),
        [Keys.Control | Keys.Down] = ("RightSmallKnob_Right", "small knob right", Lj35GnsKeyKind.Knob),
        [Keys.Control | Keys.Up] = ("RightSmallKnob_Left", "small knob left", Lj35GnsKeyKind.Knob),
        [Keys.Shift | Keys.Enter] = ("RightSmallKnob_Push", "cursor", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.Enter] = ("ENT_Push", "enter", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.D] = ("DirectTo_Push", "direct to", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.F] = ("FPL_Push", "flight plan", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.P] = ("PROC_Push", "procedures", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.E] = ("MENU_Push", "menu", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.L] = ("CLR_Push", "clear", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.G] = ("MSG_Push", "message", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.O] = ("OBS_Push", "OBS", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.C] = ("CDI_Push", "CDI", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.V] = ("VNAV_Push", "VNAV", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.PageUp] = ("RNG_Dezoom", "range out", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.PageDown] = ("RNG_Zoom", "range in", Lj35GnsKeyKind.Button),
        [Keys.Alt | Keys.Up] = ("LeftLargeKnob_Right", "megahertz up", Lj35GnsKeyKind.Radio),
        [Keys.Alt | Keys.Down] = ("LeftLargeKnob_Left", "megahertz down", Lj35GnsKeyKind.Radio),
        [Keys.Alt | Keys.Right] = ("LeftSmallKnob_Right", "kilohertz up", Lj35GnsKeyKind.Radio),
        [Keys.Alt | Keys.Left] = ("LeftSmallKnob_Left", "kilohertz down", Lj35GnsKeyKind.Radio),
        [Keys.Alt | Keys.Enter] = ("LeftSmallKnob_Push", "COM NAV tuning toggle", Lj35GnsKeyKind.Radio),
        [Keys.Alt | Keys.Shift | Keys.Enter] = ("COMSWAP_Push", "COM swapped", Lj35GnsKeyKind.Button),
        [Keys.Control | Keys.Alt | Keys.Shift | Keys.Enter] = ("NAVSWAP_Push", "NAV swapped", Lj35GnsKeyKind.Button),
    };

    public Lj35GnsDisplayForm(string title, string coherentViewNeedle, string eventPrefix, ScreenReaderAnnouncer announcer)
    {
        _previousWindow = GetForegroundWindow();
        _prefix = eventPrefix;
        _announcer = announcer;

        Text = title;
        Size = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _text = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11, FontStyle.Regular),
            TabIndex = 0,
            AccessibleName = title,
            AccessibleDescription = title + ". Read with the arrow keys; the first line says which page or dialog is up. " +
                "Control with left and right turns the large right knob, control with up and down the small right knob. " +
                "On the self-test page turn the large knob to OK? and press control Enter; nothing else answers until then. " +
                "Shift with Enter pushes the cursor, control with Enter is ENT. " +
                "Control with D direct to, F flight plan, P procedures, E menu, L clear, G message, O OBS, C CDI, V VNAV. " +
                "Flight plan, procedures and VNAV are their own groups: control L leaves them. " +
                "Control with T types an ident into the field under the cursor, then control Enter confirms it. " +
                "On the flight plan page the large knob moves through the legs, the small knob on a leg inserts a waypoint, and control L on a leg asks to delete it. " +
                "Control with Page Up and Page Down is the map range. " +
                "Alt with up and down is the radio megahertz, Alt with left and right the kilohertz, Alt with Enter toggles COM and NAV tuning, " +
                "Alt with Shift and Enter swaps COM, Control Alt Shift Enter swaps NAV. F5 refreshes; Escape closes. Auto-updates."
        };
        _text.SetText("Connecting to the display...");

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var refreshButton = new Button { Text = "&Refresh (F5)", Location = new Point(560, 8), Size = new Size(90, 30), TabIndex = 1, AccessibleName = "Refresh" };
        refreshButton.Click += (s, e) => _ = _client?.ScrapeNowAsync();
        var closeButton = new Button { Text = "&Close", Location = new Point(655, 8), Size = new Size(85, 30), TabIndex = 2, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();
        bottom.Controls.AddRange(new Control[] { refreshButton, closeButton });
        Controls.Add(_text);
        Controls.Add(bottom);
        CancelButton = closeButton;

        _client = new CoherentDisplayClient(coherentViewNeedle, pollIntervalMs: 1200, agentFileName: "coherent-gns-agent.js");
        _client.RowsUpdated += OnRowsUpdated;
        _client.Error += OnClientError;

        _connectWatchdog = new System.Windows.Forms.Timer { Interval = 6000 };
        _connectWatchdog.Tick += (s, e) =>
        {
            _connectWatchdog!.Stop();
            if (_disposed || _gotRows) return;
            _text.SetLines(new List<string>
            {
                "Could not read the display.",
                "",
                "The GNS is dark until the aircraft has power and the avionics master is on,",
                "and Coherent allows only one debugger connection per screen.",
                "",
                "Check the sim is running with the aircraft loaded and powered, then press F5."
            });
        };

        Load += (s, e) =>
        {
            BringToFront();
            Activate();
            _text.Focus();
            _client.Start();
            _client.SetActive(true);
            _connectWatchdog.Start();
        };

        FormClosed += (s, e) =>
        {
            _connectWatchdog.Stop();
            _connectWatchdog.Dispose();
            _client.RowsUpdated -= OnRowsUpdated;
            _client.Error -= OnClientError;
            _client.Stop();
            _client.Dispose();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    private void OnClientError(string message)
    {
        if (_disposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                _text.SetLines(new List<string> { "Display error: " + message, "", "Press F5 to retry." });
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void OnRowsUpdated(List<string> rows)
    {
        if (_disposed || !IsHandleCreated) return;
        _gotRows = true;
        IReadOnlyList<string> lines = rows.Count > 0 ? rows : new[] { "No data from the display." };
        try
        {
            BeginInvoke(new Action(() => { if (!_disposed) _text.SetLines(lines); }));
        }
        catch (InvalidOperationException) { }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            _ = _client.ScrapeNowAsync();
            return true;
        }
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        if (keyData == (Keys.Control | Keys.T))
        {
            TypeIdent();
            return true;
        }
        if (BezelKeys.TryGetValue(keyData, out var key))
        {
            _ = PressAsync(key.Event, key.Spoken, key.Kind);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Ctrl+T: type an ident into the field under the cursor instead of spelling it with the
    /// knobs. The agent hands the text to the instrument's OWN keyboard path
    /// (AlphaNumInput.setValueFromOS, the same one the sim's on-screen keyboard uses), so the
    /// unit's search and facility lookup run exactly as for a sighted pilot; after they have
    /// had a moment the window speaks what the unit resolved ("EGKK, LONDON GATWICK, U KINGDOM")
    /// and the pilot confirms with Ctrl+Enter as usual. With no ident field on screen the
    /// dialog is refused up front rather than typing into nothing.
    /// </summary>
    private void TypeIdent()
    {
        var dialog = new ValueInputForm("Type an ident", "ident", "letters and digits, up to six",
            _announcer, input => { var r = Lj35GnsIdent.Validate(input); return (r.ok, r.message); });
        dialog.ShowCancelButton = true;
        if (dialog.ShowDialog(this) != DialogResult.OK) { _text.Focus(); return; }
        string ident = Lj35GnsIdent.Validate(dialog.InputValue).ident;
        _text.Focus();
        _ = TypeIdentAsync(ident);
    }

    private async Task TypeIdentAsync(string ident)
    {
        string result = await _client.InvokeAsync($"window.__MSFSBA_GNS ? __MSFSBA_GNS.typeIdent('{ident}') : ''");
        if (_disposed) return;
        if (!result.StartsWith("ok", StringComparison.Ordinal))
        {
            _announcer.AnnounceImmediate(result.Contains("no field", StringComparison.Ordinal)
                ? "No ident field on screen. Open Direct To with control D, or put the cursor on a waypoint field first."
                : "Could not type into the display.");
            return;
        }
        // The unit's search is debounced; give it a beat before reading what it resolved.
        await Task.Delay(900);
        if (_disposed) return;
        string typed = await _client.InvokeAsync("window.__MSFSBA_GNS ? __MSFSBA_GNS.typed() : ''");
        _announcer.AnnounceImmediate(string.IsNullOrWhiteSpace(typed) ? ident + " typed." : typed);
        _ = _client.ScrapeNowAsync();
    }

    /// <summary>
    /// Fires one bezel event inside the page, lets the instrument redraw, then reads the
    /// agent's state back and says what changed — the page or dialog now on top, the row the
    /// cursor landed on, the character an ident entry now shows — before re-reading the
    /// screen into the list.
    /// </summary>
    private async Task PressAsync(string eventSuffix, string spoken, Lj35GnsKeyKind kind)
    {
        string js = $"SimVar.SetSimVarValue('H:{_prefix}_{eventSuffix}','number',1); 'sent'";
        string result = await _client.InvokeAsync(js);
        if (!result.Contains("sent", StringComparison.Ordinal))
        {
            _announcer.AnnounceImmediate("Key did not reach the display");
            return;
        }
        await Task.Delay(SettleMs);
        if (_disposed) return;
        string raw = await _client.InvokeAsync("window.__MSFSBA_GNS ? __MSFSBA_GNS.state() : ''");
        if (_disposed) return;
        _announcer.AnnounceImmediate(Lj35GnsSpeech.Compose(kind, Lj35GnsSpeech.Parse(raw), spoken));
        _ = _client.ScrapeNowAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _client.RowsUpdated -= OnRowsUpdated;
            _client.Dispose();
        }
        base.Dispose(disposing);
    }
}
