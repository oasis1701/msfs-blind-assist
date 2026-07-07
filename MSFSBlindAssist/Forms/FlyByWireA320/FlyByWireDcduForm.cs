using System.Text.Json;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Utils.Logging;

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
/// Refresh: on open, every 1 s while open (change-only, caret-preserving), and
/// ~1.2 s after a soft key (the DCDU Button delays its action 1 s for its
/// visual confirm). Soft keys fire the REAL DCDU H-events via the calc path
/// ((>H:A32NX_DCDU_BTN_MPL_*) — each Button listens for both units), each
/// string sequence-uniquified so MobiFlight's consecutive-identical-string
/// coalescing can't drop a repeated key (the WILCO→SEND flow).
/// </summary>
public class FlyByWireDcduForm : Form
{
    // Match the MCDU window's positional width (FbwMcduFormat.PositionLine default
    // = 24 cols). The wider 30-col field right-aligned a lone right key (e.g.
    // "RECALL>") six columns further from its leading key number than the MCDU,
    // so the number and its label read as disconnected on a braille display. 24
    // keeps the whole line — number at column 0, label right-aligned with its
    // ">" side marker — within one 40-cell braille line, exactly like the MCDU.
    private const int LineWidth = 24;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnect.SimConnectManager _simConnect;
    private readonly ListBox _display;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _postActionTimer;
    private string _lastText = "";
    private string? _scrapeJs;
    private bool _refreshing;
    private string _btnL1 = "", _btnL2 = "", _btnR1 = "", _btnR2 = "";
    private bool _actL1, _actL2, _actR1, _actR2;
    private int _calcSeq;

    public FlyByWireDcduForm(ScreenReaderAnnouncer announcer, SimConnect.SimConnectManager simConnect)
    {
        _announcer = announcer;
        _simConnect = simConnect;

        Text = "A32NX DCDU";
        AccessibleName = "A32NX DCDU";
        Size = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        // ListBox (not a multiline TextBox) so each display line is its OWN
        // accessible row — a screen reader / braille display presents one line per
        // item cleanly, matching the MCDU window (FlyByWireMCDUForm), which uses a
        // ListBox and reads correctly on braille. The multiline TextBox presented
        // the rows so that a right-aligned key label (e.g. "RECALL>") read on a
        // separate braille line from its leading key number; one discrete row per
        // line keeps the whole line — number + right-aligned label — together.
        _display = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 12f),
            AccessibleName = "DCDU display",
            TabStop = true,
            IntegralHeight = false,
        };
        Controls.Add(_display);

        // 1 s poll: the DCDU itself delays every key action 1 s (its visual
        // press-confirm), so the perceived lag after a key is confirm + scrape;
        // the tight poll keeps that near the floor without a persistent socket.
        _pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _pollTimer.Tick += async (_, _) => { if (!IsDisposed) await RefreshDisplayAsync(); };
        _postActionTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _postActionTimer.Tick += async (_, _) => { _postActionTimer.Stop(); if (!IsDisposed) await RefreshDisplayAsync(); };

        Shown += async (_, _) =>
        {
            _display.Focus();
            await RefreshDisplayAsync();
            // The first eval can take seconds (view resolution + WS connect);
            // restarting a disposed WinForms timer silently re-creates its
            // native timer, leaving a zombie 1 Hz eval loop if the form was
            // closed during that first await.
            if (!IsDisposed) _pollTimer.Start();
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
                return ""; // transient read failure — leave null so the next poll retries
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
                Log.Debug("Forms", $"eval failed: {ex.Message}");
                // Keep the last good render; the next poll retries. But on the
                // FIRST render there is nothing to keep — a silent blank window
                // with no explanation is the worst outcome for a blind user.
                if (_lastText.Length == 0) SetText("DCDU unavailable. Retrying...");
                return;
            }
            if (IsDisposed) return;

            var lines = new List<string>();
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                {
                    // The DCDU is genuinely gone — clear the cached soft keys so
                    // a chord can't fire against a stale layout and falsely
                    // confirm an action the unit never saw.
                    _btnL1 = _btnL2 = _btnR1 = _btnR2 = "";
                    _actL1 = _actL2 = _actR1 = _actR2 = false;
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
                if (root.TryGetProperty("act", out var acts))
                {
                    _actL1 = acts.TryGetProperty("L1", out var a1) && a1.GetBoolean();
                    _actL2 = acts.TryGetProperty("L2", out var a2) && a2.GetBoolean();
                    _actR1 = acts.TryGetProperty("R1", out var a3) && a3.GetBoolean();
                    _actR2 = acts.TryGetProperty("R2", out var a4) && a4.GetBoolean();
                }
                else
                {
                    // Older scrape js without the act field (hot-dropped mix):
                    // assume label-present = active rather than refusing every key.
                    _actL1 = _btnL1.Length > 0; _actL2 = _btnL2.Length > 0;
                    _actR1 = _btnR1.Length > 0; _actR2 = _btnR2.Length > 0;
                }
            }
            catch
            {
                if (_lastText.Length == 0) SetText("DCDU unavailable. Retrying...");
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

    /// <summary>
    /// Change-only, selection-preserving update so the 1 s poll never yanks the
    /// braille reading position. Each line becomes its own ListBox item (one
    /// discrete accessible row), reconciled item-by-item so an unchanged poll is a
    /// no-op and a changed poll keeps the user's selected line where possible.
    /// </summary>
    private void SetText(string text)
    {
        if (text == _lastText) return;
        _lastText = text;
        string[] newItems = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        int saved = _display.SelectedIndex;
        Forms.DisplayList.UpdateInPlace(_display, newItems);
        // First populate (saved == -1): anchor on line 1 so a focused display
        // reads immediately; later updates keep the user's selected line.
        if (saved < 0)
        {
            if (_display.Items.Count > 0 && _display.SelectedIndex != 0)
                _display.SelectedIndex = 0;
        }
        else if (saved < _display.Items.Count && _display.SelectedIndex != saved)
        {
            _display.SelectedIndex = saved;
        }
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
                bool first = e.KeyCode == Keys.F1;
                FireButton(first ? "L1" : "L2", first ? _btnL1 : _btnL2, first ? _actL1 : _actL2);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            if (!e.Control && !e.Alt && e.KeyCode is Keys.F7 or Keys.F8)
            {
                bool first = e.KeyCode == Keys.F7;
                FireButton(first ? "R1" : "R2", first ? _btnR1 : _btnR2, first ? _actR1 : _actR2);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
        }
        else
        {
            if (e.Control && !e.Alt && e.KeyCode is Keys.D1 or Keys.D2)
            {
                bool first = e.KeyCode == Keys.D1;
                FireButton(first ? "L1" : "L2", first ? _btnL1 : _btnL2, first ? _actL1 : _actL2);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            if (e.Alt && !e.Control && e.KeyCode is Keys.D1 or Keys.D2)
            {
                bool first = e.KeyCode == Keys.D1;
                FireButton(first ? "R1" : "R2", first ? _btnR1 : _btnR2, first ? _actR1 : _actR2);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
        }
        // Message navigation: PageUp/Down steps between messages; with Ctrl it
        // scrolls within a long message (page-of-elements). Direction: DOWN is
        // FORWARD everywhere — messages sort oldest-first (index.tsx), so
        // MS0PLUS = newer message; POEPLUS = next page of a long message
        // (MessageVisualization.tsx: POEMINUS = pageIndex-1). The within-message
        // direction matters beyond reading order: the answer keys stay INACTIVE
        // until the pilot has paged to the END of a multi-page uplink.
        if (e.KeyCode is Keys.PageUp or Keys.PageDown)
        {
            string key = e.Control
                ? (e.KeyCode == Keys.PageUp ? "POEMINUS" : "POEPLUS")
                : (e.KeyCode == Keys.PageUp ? "MS0MINUS" : "MS0PLUS");
            FireDcduEvent($"BTN_MPL_{key}");
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

    private void FireButton(string slot, string label, bool active)
    {
        if (label.Length == 0)
        {
            _announcer.AnnounceImmediate("No action on that key.");
            return;
        }
        // An inactive Button ignores its H-event entirely (Button.tsx guards on
        // active) — most commonly because a long uplink hasn't been read to the
        // end yet, or a response is still transmitting. Saying the label here
        // would falsely confirm an action the unit refused.
        if (!active)
        {
            _announcer.AnnounceImmediate($"{label} not available yet. Read to the end of the message first.");
            return;
        }
        if (!_simConnect.IsMobiFlightConnected)
        {
            _announcer.AnnounceImmediate("Sim connection not ready. Key not sent.");
            return;
        }
        FireDcduEvent($"BTN_MPL_{slot}");
        // The DCDU confirms a press visually for 1 s before acting — speak the
        // label now (action confirmation, not a UI echo) and re-scrape after
        // the action lands.
        _announcer.AnnounceImmediate(label.Replace("*", "").Trim());
        _postActionTimer.Stop();
        _postActionTimer.Start();
    }

    /// <summary>
    /// Fires a DCDU H-event with a sequence-uniquified calc string. MobiFlight
    /// commands travel through a client-data area where two CONSECUTIVE
    /// IDENTICAL strings coalesce and the second never executes (the seat-motor
    /// lesson) — exactly the WILCO→SEND flow, which presses the same R2 slot
    /// twice in a row. The "{seq} 0 *" prefix evaluates to a discarded 0 but
    /// makes every string unique.
    /// </summary>
    private void FireDcduEvent(string key)
    {
        _simConnect.ExecuteCalculatorCode($"{++_calcSeq} 0 * (>H:A32NX_DCDU_{key})");
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
