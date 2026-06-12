using System.Text.Json;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms.FlyByWireA320;

/// <summary>
/// Accessible DCDU (CPDLC display) for the FlyByWire A32NX — opened with
/// Ctrl+Shift+D (input mode). Live CPDLC uplinks display on the DCDU and can
/// ONLY be answered there (WILCO / UNABLE / STANDBY / CLOSE / RECALL); the
/// MCDU's ATC MSG RECORD page only reads history. Relevant with a datalink
/// connection (Hoppie / SayIntentions / BeyondATC as the FBW ACARS provider).
///
/// Display + keys MIRROR THE MCDU WINDOW MODEL (FlyByWireMCDUForm): the screen
/// renders as positioned lines via <see cref="Services.FbwMcduFormat.PositionLine"/>
/// — a soft-key label sits at its real place in its row (left key at the line
/// start, right key right-aligned), with the unit's own star convention
/// marking the adjacent key (e.g. "RECALL*" bottom-right = right key 2;
/// "*STBY" at a line start = the left key on that row). No separate key-map
/// listing is rendered. Soft keys use the SAME chords as the MCDU LSKs,
/// honouring the shared MCDUUseAlternateLSKKeys setting:
///   standard:  Ctrl+1 / Ctrl+2 = left keys, Alt+1 / Alt+2 = right keys
///   alternate: F1 / F2 = left keys, F7 / F8 = right keys
/// Row 1 is the upper soft-key row, row 2 the lower (where RECALL lives).
/// PageUp / PageDown step between messages; Ctrl+PageUp / Ctrl+PageDown
/// scroll within a long message; F5 refreshes.
///
/// Transport: ONE-SHOT <see cref="SimConnect.CoherentEvalClient"/> evals of
/// Resources/coherent-a32nx-dcdu.js against the "DCDU" Coherent view — NO
/// persistent Coherent socket on the A32NX by policy (the A320 EWD scrape was
/// removed over socket crash risk; one-shots are the flightInfo-proven path).
/// Refresh: on open, every 2 s while open (change-only, caret-preserving), and
/// ~1.5 s after a soft key (the DCDU Button delays its action 1 s for its
/// visual confirm). Soft keys fire the REAL DCDU H-events via the calc path
/// ((>H:A32NX_DCDU_BTN_MPL_*) — each Button listens for both units).
/// </summary>
public class FlyByWireDcduForm : Form
{
    private const int LineWidth = 30;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnect.SimConnectManager _simConnect;
    private readonly TextBox _display;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _postActionTimer;
    private string _lastText = "";
    private string? _scrapeJs;
    private bool _refreshing;
    private string _btnL1 = "", _btnL2 = "", _btnR1 = "", _btnR2 = "";

    public FlyByWireDcduForm(ScreenReaderAnnouncer announcer, SimConnect.SimConnectManager simConnect)
    {
        _announcer = announcer;
        _simConnect = simConnect;

        Text = "A32NX DCDU";
        AccessibleName = "A32NX DCDU";
        Size = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _display = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 12f),
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "DCDU display",
            TabStop = true
        };
        Controls.Add(_display);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _pollTimer.Tick += async (_, _) => await RefreshDisplayAsync();
        _postActionTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _postActionTimer.Tick += async (_, _) => { _postActionTimer.Stop(); await RefreshDisplayAsync(); };

        Shown += async (_, _) =>
        {
            _display.Focus();
            await RefreshDisplayAsync();
            _pollTimer.Start();
        };
        FormClosed += (_, _) => { _pollTimer.Stop(); _postActionTimer.Stop(); };
        KeyDown += OnFormKeyDown;
    }

    private string LoadScrapeJs()
    {
        if (_scrapeJs == null)
        {
            try
            {
                _scrapeJs = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-a32nx-dcdu.js"));
            }
            catch
            {
                _scrapeJs = "";
            }
        }
        return _scrapeJs;
    }

    private async Task RefreshDisplayAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            string js = LoadScrapeJs();
            if (js.Length == 0) { SetText("DCDU scrape script missing."); return; }
            string raw;
            try { raw = await SimConnect.CoherentEvalClient.EvalAsync("DCDU", js); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DCDU] eval failed: {ex.Message}");
                return; // keep the last good render; the next poll retries
            }
            if (IsDisposed) return;

            var lines = new List<string>();
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                {
                    SetText("DCDU unavailable.");
                    return;
                }
                if (root.TryGetProperty("rows", out var rows))
                {
                    foreach (var r in rows.EnumerateArray())
                    {
                        string kind = r.TryGetProperty("t", out var t) ? t.GetString() ?? "" : "";
                        if (kind == "keys")
                        {
                            string l = r.TryGetProperty("l", out var le) ? le.GetString() ?? "" : "";
                            string c = r.TryGetProperty("c", out var ce) ? ce.GetString() ?? "" : "";
                            string rr = r.TryGetProperty("r", out var re) ? re.GetString() ?? "" : "";
                            lines.Add(Services.FbwMcduFormat.PositionLine(l, c, rr, LineWidth));
                        }
                        else
                        {
                            lines.Add(r.TryGetProperty("txt", out var tx) ? tx.GetString() ?? "" : "");
                        }
                    }
                }
                if (root.TryGetProperty("btns", out var btns))
                {
                    _btnL1 = btns.TryGetProperty("L1", out var l1) ? l1.GetString() ?? "" : "";
                    _btnL2 = btns.TryGetProperty("L2", out var l2) ? l2.GetString() ?? "" : "";
                    _btnR1 = btns.TryGetProperty("R1", out var r1) ? r1.GetString() ?? "" : "";
                    _btnR2 = btns.TryGetProperty("R2", out var r2) ? r2.GetString() ?? "" : "";
                }
            }
            catch
            {
                return; // malformed payload — keep the last render
            }

            if (lines.Count == 0) lines.Add("(no CPDLC message displayed)");
            SetText(string.Join(Environment.NewLine, lines));
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Change-only, caret-preserving write so the 2 s poll never yanks the reading cursor.</summary>
    private void SetText(string text)
    {
        if (text == _lastText) return;
        _lastText = text;
        int caret = _display.SelectionStart;
        _display.Text = text;
        _display.SelectionStart = Math.Min(caret, _display.TextLength);
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        // Soft keys — same scheme as the MCDU LSKs, honouring the shared
        // alternate-keys setting (FlyByWireMCDUForm precedent): standard =
        // Ctrl+1/2 left + Alt+1/2 right; alternate = F1/F2 left + F7/F8 right.
        bool useAltKeys = Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys;
        if (useAltKeys)
        {
            if (!e.Control && !e.Alt && e.KeyCode is Keys.F1 or Keys.F2)
            {
                FireButton(e.KeyCode == Keys.F1 ? "L1" : "L2", e.KeyCode == Keys.F1 ? _btnL1 : _btnL2);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            if (!e.Control && !e.Alt && e.KeyCode is Keys.F7 or Keys.F8)
            {
                FireButton(e.KeyCode == Keys.F7 ? "R1" : "R2", e.KeyCode == Keys.F7 ? _btnR1 : _btnR2);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
        }
        else
        {
            if (e.Control && !e.Alt && e.KeyCode is Keys.D1 or Keys.D2)
            {
                FireButton(e.KeyCode == Keys.D1 ? "L1" : "L2", e.KeyCode == Keys.D1 ? _btnL1 : _btnL2);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            if (e.Alt && !e.Control && e.KeyCode is Keys.D1 or Keys.D2)
            {
                FireButton(e.KeyCode == Keys.D1 ? "R1" : "R2", e.KeyCode == Keys.D1 ? _btnR1 : _btnR2);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
        }
        // Message navigation: PageUp/Down steps between messages; with Ctrl it
        // scrolls within a long message (page-of-elements).
        if (e.KeyCode is Keys.PageUp or Keys.PageDown)
        {
            string key = e.Control
                ? (e.KeyCode == Keys.PageUp ? "POEPLUS" : "POEMINUS")
                : (e.KeyCode == Keys.PageUp ? "MS0PLUS" : "MS0MINUS");
            _simConnect.ExecuteCalculatorCode($"(>H:A32NX_DCDU_BTN_MPL_{key})");
            _postActionTimer.Stop();
            _postActionTimer.Start();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.F5)
        {
            _ = RefreshDisplayAsync();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    private void FireButton(string slot, string label)
    {
        if (label.Length == 0)
        {
            _announcer.AnnounceImmediate("No action on that key.");
            return;
        }
        _simConnect.ExecuteCalculatorCode($"(>H:A32NX_DCDU_BTN_MPL_{slot})");
        // The DCDU confirms a press visually for 1 s before acting — speak the
        // label now (action confirmation, not a UI echo) and re-scrape after
        // the action lands.
        _announcer.AnnounceImmediate(label.Replace("*", "").Trim());
        _postActionTimer.Stop();
        _postActionTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Dispose();
            _postActionTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
