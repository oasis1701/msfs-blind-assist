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
/// coalescing can't drop a repeated key (the WILCO→SEND flow). A key the unit
/// is refusing is recovered rather than dead-ended — see
/// <see cref="ReassertEndOfMessageAndFireAsync"/>.
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
    private int _pageIndex, _pageCount;
    private int _calcSeq;

    /// <summary>
    /// How long to let the DCDU re-render after the page-forward key before
    /// re-reading the soft keys in <see cref="ReassertEndOfMessageAndFireAsync"/>.
    /// The unit's page handler is plain synchronous React state — the 1 s
    /// press-confirm delay applies only to the answer Buttons — so this just
    /// has to cover the H-event round trip.
    /// </summary>
    private const int ReassertSettleMs = 350;

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
                    _pageIndex = _pageCount = 0;
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
                // 0/0 when the message fits one page (the unit renders no page
                // counter then) and for an older scrape js without the field.
                _pageIndex = _pageCount = 0;
                if (root.TryGetProperty("page", out var page))
                {
                    if (page.TryGetProperty("idx", out var pi)) pi.TryGetInt32(out _pageIndex);
                    if (page.TryGetProperty("cnt", out var pc)) pc.TryGetInt32(out _pageCount);
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
        bool useAltKeys = MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys;
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
        // active), so pressing it now would do nothing and saying the label
        // would falsely confirm it. Try to clear the cause before giving up.
        if (!active)
        {
            _ = ReassertEndOfMessageAndFireAsync(slot, label);
            return;
        }
        if (!_simConnect.IsMobiFlightConnected)
        {
            _announcer.AnnounceImmediate("Sim connection not ready. Key not sent.");
            return;
        }
        SendSoftKey(slot, label);
    }

    private void SendSoftKey(string slot, string label)
    {
        FireDcduEvent($"BTN_MPL_{slot}");
        // The DCDU confirms a press visually for 1 s before acting — speak the
        // label now (action confirmation, not a UI echo) and re-scrape after
        // the action lands.
        _announcer.AnnounceImmediate(label.Replace("*", "").Trim());
        _postActionTimer.Stop();
        _postActionTimer.Start();
    }

    /// <summary>
    /// Handles a soft key the DCDU is currently refusing, recovering it where
    /// the refusal is the unit's own stale state rather than a real "you have
    /// not read this yet".
    ///
    /// Every answer key is gated on the displayed message's
    /// <c>reachedEndOfMessage</c> flag (WilcoUnableButtons / AffirmNegative-
    /// Buttons / OutputButtons / SemanticResponseButtons all fold it into
    /// <c>buttonsBlocked</c>). That flag is raised in exactly TWO places, both
    /// in MessageVisualization: the render-time page-count transition
    /// (<c>if (messageView.pageCount !== pageCount) reachedEndOfMessage(uid,
    /// messageView.pageCount === 1)</c>) and the POEMINUS/POEPLUS page-key
    /// handlers. Because <c>pageCount</c> is component STATE that survives a
    /// message swap, a new message whose page count MATCHES the one the
    /// visualization last rendered never trips that transition — so its block
    /// keeps the initial <c>reachedEndOfMessage = false</c> and every answer
    /// key stays dead, with nothing on screen to say why. Measured live
    /// (2026-07-28) on a single-page "CONTACT ... ON 123.225" uplink: pageIndex
    /// 0, pageCount 1, flag false, UNABLE/STBY/WILCO all active=false. This is
    /// FBW-side and hits sighted pilots identically — the cockpit buttons are
    /// H-event-only, so a mouse click is refused the same way.
    ///
    /// The unit's own way out is a page key. POEPLUS sets the flag from
    /// <c>pageCount &lt;= pageIndex + 2</c> and does NOT advance the page once
    /// the last page is displayed, so when the pilot really is at the end it is
    /// a pure re-assert that can only unblock. We therefore send it and retry
    /// the key — but ONLY when the scrape shows nothing left to read. With
    /// pages still unread we refuse as before, now naming the page the pilot is
    /// on rather than dead-ending on "read to the end first"; paging past
    /// unread text on their behalf is never right.
    ///
    /// Both decisions are taken on a FRESH scrape: the caller's active/page
    /// snapshot can be up to a poll old, so a pilot who pages to the last page
    /// and answers straight away would otherwise be refused against the state
    /// before their own page key.
    /// </summary>
    private async Task ReassertEndOfMessageAndFireAsync(string slot, string label)
    {
        string spoken = label.Replace("*", "").Trim();
        if (!_simConnect.IsMobiFlightConnected)
        {
            _announcer.AnnounceImmediate("Sim connection not ready. Key not sent.");
            return;
        }
        await ForceRefreshAsync();
        if (IsDisposed) return;
        if (TryPressSlot(slot, label)) return;
        if (_pageCount > 1 && _pageIndex < _pageCount)
        {
            _announcer.AnnounceImmediate(
                $"{spoken} not available yet. Page {_pageIndex} of {_pageCount}. Press control page down to read on.");
            return;
        }
        FireDcduEvent("BTN_MPL_POEPLUS");
        await Task.Delay(ReassertSettleMs);
        if (IsDisposed) return;
        await ForceRefreshAsync();
        if (IsDisposed) return;
        if (TryPressSlot(slot, label)) return;
        // The remaining known blocker is a response still going out
        // (ComStatus == Sending), which no key press can shorten.
        _announcer.AnnounceImmediate($"{spoken} not available. The message may still be transmitting.");
    }

    /// <summary>
    /// Presses the slot only if it is live AND still carries the key the pilot
    /// asked for — a message arriving in between re-labels the slot, and
    /// pressing it then would answer something they never chose.
    /// </summary>
    private bool TryPressSlot(string slot, string label)
    {
        var (nowLabel, nowActive) = SlotState(slot);
        if (!nowActive || !string.Equals(nowLabel, label, StringComparison.Ordinal)) return false;
        SendSoftKey(slot, label);
        return true;
    }

    private (string Label, bool Active) SlotState(string slot) => slot switch
    {
        "L1" => (_btnL1, _actL1),
        "L2" => (_btnL2, _actL2),
        "R1" => (_btnR1, _actR1),
        _ => (_btnR2, _actR2),
    };

    /// <summary>
    /// A fresh scrape, even if the 1 s poll is mid-flight —
    /// <see cref="RefreshDisplayAsync"/> no-ops while one is, so a bare call can
    /// return having re-read nothing. The retry decision must never be made on
    /// the pre-page-key snapshot. (An in-flight poll that started AFTER the page
    /// key is itself fresh, so falling through the wait is safe.)
    /// </summary>
    private async Task ForceRefreshAsync()
    {
        for (int i = 0; i < 12 && _refreshing && !IsDisposed; i++) await Task.Delay(80);
        if (!IsDisposed) await RefreshDisplayAsync();
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
