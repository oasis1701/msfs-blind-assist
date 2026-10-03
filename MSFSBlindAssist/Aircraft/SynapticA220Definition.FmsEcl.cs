using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// P3c/P4 wiring: the FMS window form (Shift+M, HotkeyAction.ShowFenixMCDU — the
/// shared "show this aircraft's FMS/CDU" action) and the ECL "first officer"
/// checklist form (Ctrl+Shift+C, HotkeyAction.ShowChecklistECL). Both scrape the
/// SAME DisplayUnits Coherent socket as the display pump via
/// <see cref="A220.A220DisplaysClient.CallAgentAsync"/> — one socket per page is a
/// hard Coherent GT rule, so nothing here may open its own client.
///
/// Input transports (live recon 2026-07-29, tools/a220-gen/reference/recon-2026-07-28.md):
/// - MKP keys are H: events fired via the calc path: <c>1 (&gt;H:A220_KBD_{ID}_{1|2})</c>
///   (IDs: A–Z, 0–9, DOT, SLASH, SPACE, PLUS_MINUS, CLEAR, ENTER, cursor keys, and
///   the format keys MAP/FMS/CNS/CHKL/SYN/DATA; from ModelBehaviorDefs MKP.xml).
///   CTP keys are <c>H:A220_CTP_{ID}_{1|2}</c>. Proven end-to-end: typed text
///   appears on the MKP scratchpad view.
/// - Field COMMIT is a synthetic pointer/mouse click on the field's value node in
///   the DisplayUnits view (the CCP cursor is a real mouse there) — done in-page by
///   the agent's clickFmsField/clickWinText. Proven live: EGLL typed + click on
///   ORIGIN committed the value and raised the MOD/EXEC prompt.
/// - EXEC/CNCL are the documented <c>L:A22X Flight Plan Execute/Cancel</c> flags
///   (write 1; the aircraft consumes it — matches the cockpit button behavior).
/// The aircraft's own hardware-keyboard "Entry Mode" is NEVER used or triggered
/// (it disables ALL sim input — plan gotcha #6); H: events don't involve it.
/// </summary>
public partial class SynapticA220Definition
{
    private Forms.A220.A220FmsForm? _fmsForm;
    private Forms.A220.A220ChecklistForm? _checklistForm;

    private void ShowFmsForm(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _sim = simConnect;
        _announcer = announcer;
        if (_fmsForm == null || _fmsForm.IsDisposed)
            _fmsForm = new Forms.A220.A220FmsForm(this, announcer);
        _fmsForm.ShowForm();
    }

    private void ShowChecklistEclForm(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _sim = simConnect;
        _announcer = announcer;
        if (_checklistForm == null || _checklistForm.IsDisposed)
            _checklistForm = new Forms.A220.A220ChecklistForm(this, announcer);
        _checklistForm.ShowForm();
    }

    /// <summary>Aircraft-swap teardown (called from StopAllMotion). Both forms hide
    /// on user close, so they must be Dispose()d directly here.</summary>
    private void DisposeFmsEclForms()
    {
        try { _fmsForm?.Dispose(); } catch { }
        _fmsForm = null;
        try { _checklistForm?.Dispose(); } catch { }
        _checklistForm = null;
    }

    // ---- shared DisplayUnits agent access (forms only) ----------------------

    /// <summary>Route an agent call through the display pump's ONE DisplayUnits
    /// socket. Null when the sim/view is unavailable.</summary>
    internal Task<string?> DisplaysAgentCallAsync(string call)
    {
        EnsureDisplayPump();
        var client = _displaysClient;
        return client == null ? Task.FromResult<string?>(null) : client.CallAgentAsync(call);
    }

    // ---- MKP / CTP key transport --------------------------------------------

    /// <summary>Fire one MKP key H: event (left MKP), seq-prefixed so MobiFlight's
    /// identical-command coalescing can never drop a double letter.</summary>
    internal void SendMkpKey(string keyId)
    {
        var sim = _sim;
        if (sim == null) return;
        sim.ExecuteCalculatorCode($"{SeqPrefix()}1 (>H:A220_KBD_{keyId}_1)", quiet: true);
    }

    /// <summary>Fire one CTP button H: event (captain's CTP) — window format keys
    /// (FMS/CHKL/MAP/...), line-select keys, INDB L/R.</summary>
    internal void SendCtpKey(string keyId)
    {
        var sim = _sim;
        if (sim == null) return;
        sim.ExecuteCalculatorCode($"{SeqPrefix()}1 (>H:A220_CTP_{keyId}_1)", quiet: true);
    }

    /// <summary>
    /// Type text onto the MKP scratchpad, one key event per character,
    /// CLOSED-LOOP: after each key, poll the scratchpad tap until the buffer
    /// shows the character landed, and resend once if it did not. Open-loop
    /// pacing (110 ms flat) demonstrably DROPS keystrokes — live 2026-07-30,
    /// "109270" arrived as "10970"/"09270"/"1020"/"19270", a different character
    /// lost each attempt — so a fixed delay can never be trusted here. When the
    /// tap is unavailable (state UNKNOWN) this degrades to the old paced send.
    /// Returns the characters actually sent (unsupported characters skipped).
    /// </summary>
    internal async Task<string> SendScratchpadTextAsync(string text, CancellationToken ct = default)
    {
        var sent = new System.Text.StringBuilder();
        bool confirmable = (await ScratchpadStateAsync())?.Kind == "TEXT";
        foreach (char raw in text.ToUpperInvariant())
        {
            string? id = raw switch
            {
                >= 'A' and <= 'Z' => raw.ToString(),
                >= '0' and <= '9' => raw.ToString(),
                '.' => "DOT",
                '/' => "SLASH",
                ' ' => "SPACE",
                '+' or '-' => "PLUS_MINUS",
                _ => null
            };
            if (id == null) continue;
            sent.Append(raw);
            SendMkpKey(id);
            if (!confirmable)
            {
                await Task.Delay(110, ct);
                continue;
            }
            string expected = PredictScratchpadBuffer(sent.ToString());
            if (!await WaitForScratchpadAsync(expected, 700, ct))
            {
                Utils.Logging.Log.Debug("a220_fms",
                    $"scratchpad key '{id}' not confirmed, resending (expected '{expected}')");
                SendMkpKey(id);
                if (!await WaitForScratchpadAsync(expected, 700, ct))
                {
                    // Two sends unseen — stop typing; the caller's whole-buffer
                    // verify decides whether to retry the entry from scratch.
                    Utils.Logging.Log.Debug("a220_fms",
                        $"scratchpad key '{id}' lost twice (expected '{expected}')");
                    break;
                }
            }
        }
        return sent.ToString();
    }

    /// <summary>Poll the scratchpad tap until the buffer equals
    /// <paramref name="expected"/>. False on timeout or tap loss.</summary>
    private async Task<bool> WaitForScratchpadAsync(string expected, int timeoutMs, CancellationToken ct)
    {
        for (int waited = 0; waited < timeoutMs; waited += 70)
        {
            await Task.Delay(70, ct);
            var st = await ScratchpadStateAsync();
            if (st == null || st.Value.Kind == "UNKNOWN") return false;
            if (st.Value.Kind == "TEXT" && st.Value.Text == expected) return true;
        }
        return false;
    }

    /// <summary>Predicted MKP scratchpad buffer after typing <paramref name="sent"/>
    /// (SendScratchpadTextAsync's return value) into an EMPTY scratchpad — mirrors
    /// the MKP bundle's pushChar exactly: characters append with a 24-char cap, and
    /// the PLUS_MINUS key appends '-' first, then TOGGLES a trailing sign. Used to
    /// verify a typed entry actually landed before the commit click.</summary>
    internal static string PredictScratchpadBuffer(string sent)
    {
        var b = new System.Text.StringBuilder();
        foreach (char c in sent.ToUpperInvariant())
        {
            if (c is '+' or '-')
            {
                char last = b.Length > 0 ? b[^1] : '\0';
                if (last == '+') { b[^1] = '-'; continue; }
                if (last == '-') { b[^1] = '+'; continue; }
                if (b.Length < 24) b.Append('-');
                continue;
            }
            if (b.Length < 24) b.Append(c);
        }
        return b.ToString();
    }

    /// <summary>The aircraft scratchpad's last-known state, from the displays
    /// agent's passive store tap. Kind: UNKNOWN (no store event seen since agent
    /// install), NULL (--DELETE-- armed), TEXT (buffer holds <see cref="Text"/>).
    /// <see cref="Error"/> carries the FMS's broadcast refusal message ("INVALID
    /// ENTRY"…) when one is current. Null when the agent is unreachable.</summary>
    internal readonly record struct ScratchpadState(string Kind, string Text, string Error);

    internal async Task<ScratchpadState?> ScratchpadStateAsync()
    {
        string? raw = await DisplaysAgentCallAsync("spState()");
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            return new ScratchpadState(
                root.TryGetProperty("t", out var t) ? t.GetString() ?? "UNKNOWN" : "UNKNOWN",
                root.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "",
                root.TryGetProperty("err", out var e) ? e.GetString() ?? "" : "");
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>
    /// Empty the aircraft's shared scratchpad RELIABLY, via the same store event
    /// the aircraft's own success path fires (agent resetScratchpad), confirmed
    /// through the passive tap. The MKP CLEAR key is NOT a clear: its popChar
    /// removes ONE character, and on an already-empty scratchpad it ARMS
    /// --DELETE-- — so the old "press CLEAR once" left a refused entry's text in
    /// place and every following entry concatenated into a fresh "INVALID ENTRY"
    /// (live 2026-07-30, FUEL page). Returns false when the empty state could not
    /// be confirmed; callers may still proceed (typing appends), but know.
    /// </summary>
    internal async Task<bool> ClearScratchpadAsync(CancellationToken ct = default)
    {
        string? r = await DisplaysAgentCallAsync("resetScratchpad()");
        if (r != "RESET") return false;
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(60, ct);
            var st = await ScratchpadStateAsync();
            if (st is { Kind: "TEXT", Text: "" }) return true;
        }
        return false;
    }

    /// <summary>EXEC a pending flight-plan modification (documented write flag).</summary>
    internal void PressFplnExec()
    {
        var sim = _sim;
        if (sim != null) WriteLVar(sim, "A22X Flight Plan Execute", 1);
    }

    /// <summary>CNCL a pending flight-plan modification (proven live: reverts the MOD).</summary>
    internal void PressFplnCancel()
    {
        var sim = _sim;
        if (sim != null) WriteLVar(sim, "A22X Flight Plan Cancel", 1);
    }

    /// <summary>Pending-modification state (drives the EXEC prompt).</summary>
    internal bool FlightPlanModified()
        => _sim != null && Cached(_sim, "A22X_FPLN_MODIFIED") > 0.5;

    // ---- ECL actuation ------------------------------------------------------

    /// <summary>
    /// Actuate a mapped ECL item through the SAME write path as the panel combo
    /// (HandleUIVariableSet — APU hold-to-start, flap detent walk and friends
    /// included). Returns false with a reason when the aircraft/def can't do it.
    /// </summary>
    internal bool ActuateControl(string controlKey, double value, out string error)
    {
        error = "";
        var sim = _sim;
        var announcer = _announcer;
        if (sim == null || !sim.IsConnected) { error = "Not connected to the simulator."; return false; }
        if (announcer == null) { error = "Announcer unavailable."; return false; }
        if (!GetVariables().TryGetValue(controlKey, out var varDef))
        {
            error = $"Unknown control {controlKey}.";
            return false;
        }
        return HandleUIVariableSet(controlKey, value, varDef, sim, announcer);
    }
}
