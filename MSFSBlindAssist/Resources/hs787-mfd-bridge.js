// HorizonSim 787-9 MFD Accessibility Bridge
// BRIDGE_JS_VERSION: 24-reconnect-guard
// Injected into HSB789_MFD.GE.html and HSB789_MFD.RR.html via mod package override.
// Reads the WT Boeing FMC screen from the DOM and exposes CDU controls to the
// MSFS Blind Assist application via HTTP on localhost:19778.
//
// Runs in MSFS Coherent GT (older Chromium). No modern APIs like AbortSignal.timeout().
// Top-level try-catch ensures errors here never break the FMC.
try {

// Guard against double-load: both import-script and <script src> tags may execute.
// Whichever fires first wins; the second is a no-op.
if (window._mfd_bridge_loaded) {
    console.log('[MFD Bridge] Already loaded, skipping (double-load guard)');
} else { window._mfd_bridge_loaded = true;

var _mfd = {
    JS_VERSION: '24-reconnect-guard',
    SERVER_URL: 'http://localhost:19778',
    SCREEN_POLL_INTERVAL: 300,
    COMMAND_POLL_INTERVAL: 400,
    HEARTBEAT_INTERVAL: 5000,
    RECONNECT_INTERVAL: 5000,
    serverConnected: false,
    connecting: false,         // guard so the heartbeat + reconnect loop can't run tryConnect concurrently
    commandPollTimer: null,
    heartbeatTimer: null,
    screenPollTimer: null,
    reconnectTimer: null,
    previousScreen: null,
    previousCduVisible: null,  // null = unknown, true/false after first poll
    stageReached: 0            // SimVar diagnostic: 1=loaded, 2=fetch failed, 3=connected
};

// Write L:MSFSBA_787_STAGE so the C# app can read bridge execution state without MSFS dev mode.
// Stages: 1=script loaded, 2=fetch blocked/failed, 3=connected.
// Never downgrades from 3 (connected) so a transient reconnect doesn't erase the good result.
_mfd.setStage = function(stage) {
    if (_mfd.stageReached >= 3 && stage < 3) return;
    _mfd.stageReached = stage;
    try {
        if (typeof SimVar !== 'undefined' && typeof SimVar.SetSimVarValue === 'function') {
            SimVar.SetSimVarValue('L:MSFSBA_787_STAGE', 'number', stage);
        }
    } catch (e) { }
};

// --- HTTP Communication ---

_mfd.fetchWithTimeout = function(url, options, timeoutMs) {
    return new Promise(function(resolve, reject) {
        var timer = setTimeout(function() {
            reject(new Error('Request timed out'));
        }, timeoutMs);
        fetch(url, options).then(function(response) {
            clearTimeout(timer);
            resolve(response);
        }).catch(function(err) {
            clearTimeout(timer);
            reject(err);
        });
    });
};

_mfd.postState = async function(type, data) {
    if (!_mfd.serverConnected) return;
    try {
        await fetch(_mfd.SERVER_URL + '/state', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: type, data: data || {} })
        });
    } catch (e) {
        // Server unreachable — will be detected by heartbeat
    }
};

_mfd.pollCommands = async function() {
    if (!_mfd.serverConnected) return;
    // Multiple MFD instances (MFD_1/2/3) all run this bridge and share one /commands/mfd
    // queue. Commands are dequeued on GET — the first instance to poll consumes them.
    // Only the CDU-visible instance should dequeue, otherwise commands are lost.
    if (_mfd.previousCduVisible !== true) return;
    try {
        // Poll /commands/mfd so EFB bridge commands don't cross-contaminate
        var response = await fetch(_mfd.SERVER_URL + '/commands/mfd');
        var commands = await response.json();
        for (var i = 0; i < commands.length; i++) {
            _mfd.handleCommand(commands[i].command, commands[i].payload);
        }
    } catch (e) {
        // Server unreachable — will be detected by heartbeat
    }
};

_mfd.tryConnect = async function() {
    // The heartbeat timer and the bootstrap reconnect loop both call tryConnect every 5s. Without
    // this guard a second attempt can start while the first 2s ping is still outstanding, producing
    // overlapping pings and a connect/disconnect that can interleave the timer state. Bail if one is
    // already in flight (return the current state so callers' ok/not-ok branches stay correct).
    if (_mfd.connecting) return _mfd.serverConnected;
    _mfd.connecting = true;
    try {
        try {
            var response = await _mfd.fetchWithTimeout(_mfd.SERVER_URL + '/ping', {}, 2000);
            if (response.ok) {
                if (!_mfd.serverConnected) {
                    _mfd.serverConnected = true;
                    // Fresh (re)connection: force a full screen re-push so a C# side that just came
                    // back gets the current CDU contents instead of waiting for the next text change
                    // (pollScreen short-circuits while currentStr === previousScreen).
                    _mfd.previousScreen = null;
                    _mfd.previousCduVisible = null;
                    console.log('[MFD Bridge] Connected to accessibility server');
                    _mfd.setStage(3); // Stage 3: fetch succeeded, bridge connected
                    _mfd.startPolling();
                    _mfd.postState('mfd_connected');
                }
                return true;
            }
            console.warn('[MFD Bridge] /ping returned non-ok status:', response.status);
        } catch (e) {
            // Log the error type so we can diagnose what is blocking the connection
            // (PNA, CSP, network error, etc.) — check MSFS console log for these lines.
            console.error('[MFD Bridge] tryConnect failed:', e && e.name, e && e.message);
            _mfd.setStage(2); // Stage 2: fetch threw — likely CSP or network policy blocking localhost
        }

        if (_mfd.serverConnected) {
            _mfd.serverConnected = false;
            console.log('[MFD Bridge] Lost connection to accessibility server');
            _mfd.stopPolling();
        }
        return false;
    } finally {
        _mfd.connecting = false;
    }
};

_mfd.startPolling = function() {
    _mfd.stopPolling();
    _mfd.commandPollTimer = setInterval(function() { _mfd.pollCommands(); }, _mfd.COMMAND_POLL_INTERVAL);
    _mfd.screenPollTimer  = setInterval(function() { _mfd.pollScreen(); }, _mfd.SCREEN_POLL_INTERVAL);
    _mfd.heartbeatTimer   = setInterval(async function() {
        var ok = await _mfd.tryConnect();
        if (ok) _mfd.postState('heartbeat');
    }, _mfd.HEARTBEAT_INTERVAL);
};

_mfd.stopPolling = function() {
    if (_mfd.commandPollTimer) { clearInterval(_mfd.commandPollTimer); _mfd.commandPollTimer = null; }
    if (_mfd.screenPollTimer)  { clearInterval(_mfd.screenPollTimer);  _mfd.screenPollTimer = null; }
    if (_mfd.heartbeatTimer)   { clearInterval(_mfd.heartbeatTimer);   _mfd.heartbeatTimer = null; }
};

// --- FMC Screen Reading ---

_mfd.readLetters = function(container, allowHighlight) {
    var letters = container.querySelectorAll('.fmc-letter');
    if (!letters || letters.length === 0) {
        // Fallback: some FMC content (e.g. prompts, special rows) is not wrapped in
        // .fmc-letter spans. Read raw textContent so these cells don't appear blank.
        return (container.textContent || '').replace(/\s+$/, '');
    }
    var line = '';
    var prevHighlighted = false;
    var markedThisRun = false;
    for (var c = 0; c < letters.length; c++) {
        var el = letters[c];
        var cls = el.className || '';
        // green = active/confirmed, magenta = FMC-managed active state
        var isHighlighted = cls.indexOf('green') !== -1 || cls.indexOf('magenta') !== -1;
        var ch = el.textContent || ' ';
        if (isHighlighted !== prevHighlighted) {
            markedThisRun = false;
        }
        // Only insert 'X ' on rows that have LSK arrow prompts (< or >) — these are
        // the selectable option rows. Other rows show active state in colour but are
        // not toggles (e.g. active waypoint in magenta on the LEGS page).
        if (isHighlighted && !markedThisRun && ch.trim() !== '' && allowHighlight) {
            line += 'X ';
            markedThisRun = true;
        }
        prevHighlighted = isHighlighted;
        line += ch;
    }
    // Trim trailing whitespace only (leading spaces indicate alignment)
    return line.replace(/\s+$/, '');
};

_mfd.readScreen = function() {
    // .wt787-cdu is the CDU component root. If absent, this MFD pane is not showing the CDU.
    var firstCdu = document.querySelector('.wt787-cdu');
    if (!firstCdu) return null;

    // CRITICAL: SimpleFmcRenderer.initializeContainer() replaces the original .wt787-cdu-screen
    // element with a new <div id="fmc-container"> that has NO wt787-cdu-screen class.
    // After FMC init, document.querySelector('.wt787-cdu-screen') returns null.
    // We must look for #fmc-container; fall back to .wt787-cdu-screen for pre-init timing.
    var cduScreen = firstCdu.querySelector('#fmc-container') || firstCdu.querySelector('.wt787-cdu-screen');
    if (!cduScreen) return null;

    var rows = cduScreen.querySelectorAll('.fmc-row');
    if (!rows || rows.length === 0) return null;

    // The 787 FMC renderer (screenCellHeight:13) creates rows 0-12 inside #fmc-container.
    // Layout: row0=title, rows1/3/5/7/9/11=labels 1-6, rows2/4/6/8/10=data 1-5, row12=LSK6/scratchpad.
    // Row 12 doubles as the LSK6 data row AND the scratchpad render row: the renderer writes
    // the scratchpad text there when the scratchpad is active, and page prompts/options otherwise.
    // We read all 13 DOM rows (0-12). The actual scratchpad text is read separately from
    // .wt787-cdu-scratchpad so C# has it as a distinct field for announcement logic.
    // Total rows sent = 14 (rows 0-12 from .fmc-row + scratchpad), matching the PMDG 777 layout.
    var screenRowCount = Math.min(rows.length, 13); // rows 0..12
    var lines = [];
    for (var r = 0; r < screenRowCount; r++) {
        // Allow X markers only on rows that contain LSK arrow prompts (< or >).
        // This matches option/toggle rows and avoids false X marks on active-state
        // displays like the magenta active leg on the LEGS page.
        var rowText = rows[r].textContent || '';
        var hasArrow = rowText.indexOf('<') !== -1 || rowText.indexOf('>') !== -1;
        lines.push(_mfd.readLetters(rows[r], hasArrow));
    }
    while (lines.length < 13) lines.push(''); // pad if DOM had fewer rows than expected

    // Read the scratchpad from .wt787-cdu-scratchpad (textContent set by the FMC component).
    // This element is NOT replaced by the renderer — it lives alongside #fmc-container.
    var scratchpad = '';
    var spEl = firstCdu.querySelector('.wt787-cdu-scratchpad');
    if (spEl) {
        scratchpad = (spEl.textContent || '').trim();
    }

    // Append scratchpad as the final row so C# receives it as rows[rowCount-1].
    lines.push(scratchpad);
    return lines;
};

// --- IRS alignment scrape ---
// The WT 787 exposes true (Realistic-respecting) IRS alignment only on its
// internal Coherent bus, surfaced in the ND "TIME TO ALIGN" element
// (div.time-to-align, 'hidden' class while not aligning, last child div = the
// minutes-remaining text: "--", "NN", or "7+"). The only IRS L-var WT sets is
// WT_IRS_POS_SET_N (position accepted, ~1 min — NOT alignment complete). So we
// scrape the element here and write synthetic L-vars MSFSBA reads normally:
//   L:MSFSBA_IRS_ALIGN_STATE   0=Off 1=Aligning 2=Aligned(Navigation)
//   L:MSFSBA_IRS_ALIGN_MINUTES minutes remaining, -1 = n/a
// Graceful degradation: if .time-to-align isn't in this MFD instance's
// document, fall back to the IRS-knob + WT_IRS_POS_SET L-vars (globally
// readable) so state still resolves Off/Aligned — only the live countdown is
// lost in that case.
// Guarded L-var write — mirrors setStage's `typeof SimVar` guard so an
// undefined SimVar can never throw out of the polled path. Returns true on
// a successful write attempt.
_mfd.setLVar = function(name, value) {
    try {
        if (typeof SimVar !== 'undefined' && typeof SimVar.SetSimVarValue === 'function') {
            SimVar.SetSimVarValue('L:' + name, 'number', value);
            return true;
        }
    } catch (e) { }
    return false;
};

_mfd.pollIrsAlign = function() {
    // The .time-to-align element lives only on the ND. The bridge runs in
    // EVERY MFD instance, so this function early-returns in MFDs that don't
    // have it — otherwise the CDU/EFB MFDs race the ND's L-var writes with
    // -1/Aligning values 3x/sec and the announcer flickers.
    //
    // DIAGNOSTIC L:MSFSBA_IRS_DEBUG (sum of):
    //   1   = function entered
    //   2   = .time-to-align element found (i.e. this IS the ND MFD)
    //   4   = element visible (not 'hidden') — alignment panel showing
    //   8   = knobOn (B787_IRS_Knob_State:1|2 > 0)
    //   16  = posSet (WT_IRS_POS_SET_1 > 0)
    //   32  = irsSawVisible latch (saw alignment panel visible since knob-on)
    //   64  = irsPosSetSeenZero latch (saw posSet=0 with knobs on since knob-on)
    //   +100*state at the end (0=Off, 1=Aligning, 2=Aligned).
    //   +1000 if exception thrown mid-run.
    var dbg = 1;
    _mfd.setLVar('MSFSBA_IRS_DEBUG', dbg);
    try {
        var el = document.querySelector('.time-to-align');
        if (!el) return;   // not the ND MFD — stay silent
        dbg += 2;

        var knobOn = false, posSet = false;
        try {
            knobOn = SimVar.GetSimVarValue('L:B787_IRS_Knob_State:1', 'number') > 0 ||
                     SimVar.GetSimVarValue('L:B787_IRS_Knob_State:2', 'number') > 0;
            posSet = SimVar.GetSimVarValue('L:WT_IRS_POS_SET_1', 'number') > 0;
        } catch (e) { }
        if (knobOn) dbg += 8;
        if (posSet) dbg += 16;

        var visible = !el.classList.contains('hidden');
        if (visible) dbg += 4;

        var state, minutes = -1;
        if (visible) {
            // Alignment panel showing — actively aligning, parse the countdown.
            state = 1;
            var kids = el.querySelectorAll('div');
            var txt = kids.length ? (kids[kids.length - 1].textContent || '').trim() : '';
            if (txt === '' || txt === '--') {
                minutes = -1;
            } else if (txt.indexOf('+') >= 0) {
                var p = parseInt(txt, 10); minutes = isNaN(p) ? 7 : p;
            } else {
                var n = parseInt(txt, 10); minutes = isNaN(n) ? -1 : n;
            }
            _mfd.irsSawVisible = true;   // primary latch
        } else if (!knobOn) {
            // Knobs off — IRS is off. Reset BOTH arming latches so a fresh
            // power cycle starts from a known state.
            state = 0;
            _mfd.irsSawVisible = false;
            _mfd.irsPosSetSeenZero = false;
        } else if (_mfd.irsSawVisible || (_mfd.irsPosSetSeenZero && posSet)) {
            // Aligned. Two independent signals can fire this:
            //   (a) irsSawVisible — we caught the alignment panel rendered at
            //       least once during this knob-on cycle. The transition
            //       visible→hidden is WT's "alignment complete" event.
            //   (b) irsPosSetSeenZero + posSet=1 — for INSTANT-mode alignment
            //       where WT may not render the panel long enough for a 300ms
            //       poll to catch it. If we saw posSet=0 earlier this session
            //       (proving WT cleared it on knob-on) AND posSet is now 1,
            //       that's a verified POS INIT transition WITHIN this session,
            //       so combined with !visible the IRS is in Navigation mode.
            //       The "saw zero" requirement prevents a false-positive on
            //       knob-cycle-with-stale-posSet (WT may keep posSet=1 across
            //       a quick power cycle).
            state = 2;
        } else {
            // Knobs on but neither arming signal has fired — just turned on,
            // panel hasn't rendered yet, or POS INIT not done. Report Aligning.
            state = 1;
        }
        if (_mfd.irsSawVisible) dbg += 32;
        if (_mfd.irsPosSetSeenZero) dbg += 64;

        dbg += 100 * state;
        _mfd.setLVar('MSFSBA_IRS_DEBUG', dbg);

        // Arm the second latch AFTER state computation, so it reflects "posSet
        // was zero in a PRIOR frame this session" — never the current frame.
        // This way (irsPosSetSeenZero && posSet) means a 0→1 transition we
        // actually witnessed, not just a stale posSet=1 from a previous session.
        if (knobOn && !posSet) {
            _mfd.irsPosSetSeenZero = true;
        }

        if (state !== _mfd.prevIrsState) {
            _mfd.setLVar('MSFSBA_IRS_ALIGN_STATE', state);
            _mfd.prevIrsState = state;
        }
        if (minutes !== _mfd.prevIrsMinutes) {
            _mfd.setLVar('MSFSBA_IRS_ALIGN_MINUTES', minutes);
            _mfd.prevIrsMinutes = minutes;
        }
    } catch (e) {
        _mfd.setLVar('MSFSBA_IRS_DEBUG', dbg + 1000);
    }
};

_mfd.pollScreen = function() {
    _mfd.pollIrsAlign();   // runs regardless of CDU visibility (IRS aligns with CDU hidden)
    var lines = _mfd.readScreen();
    if (!lines) {
        // Only send cdu_not_visible if WE previously reported cdu_visible.
        // Multiple MFD instances (MFD_1/2/3) all run this bridge; MFD_1 and MFD_2
        // never show the CDU by default (they default to NavigationMap) while MFD_3
        // defaults to CDU view. Without this guard, MFD_1/MFD_2 would send
        // cdu_not_visible at startup and override MFD_3's cdu_visible event.
        if (_mfd.previousCduVisible === true) {
            _mfd.previousCduVisible = false;
            _mfd.postState('cdu_not_visible');
        }
        return;
    }

    if (_mfd.previousCduVisible !== true) {
        _mfd.previousCduVisible = true;
        _mfd.previousScreen = null; // force full screen push on CDU becoming visible
        _mfd.postState('cdu_visible');
    }

    var currentStr = lines.join('\n');
    if (currentStr === _mfd.previousScreen) return;
    _mfd.previousScreen = currentStr;

    var screenData = {};
    for (var i = 0; i < lines.length; i++) {
        screenData['row' + i] = lines[i];
    }
    screenData['rowCount'] = String(lines.length);
    _mfd.postState('fmc_screen', screenData);
};

// --- Button Clicking ---

// Dispatch pointer + click events for reliable activation of WT Boeing buttons
_mfd.clickElement = function(el) {
    var opts = { bubbles: true, cancelable: true };
    try { el.dispatchEvent(new PointerEvent('pointerdown', opts)); } catch(e) {}
    try { el.dispatchEvent(new PointerEvent('pointerup', opts)); } catch(e) {}
    try { el.dispatchEvent(new MouseEvent('click', opts)); } catch(e) {}
};

// Click the Nth left LSK (1-6) in the FIRST CDU
_mfd.clickLskLeft = function(n) {
    var firstCdu = document.querySelector('.wt787-cdu');
    if (!firstCdu) return;
    var lsks = firstCdu.querySelectorAll('.wt787-cdu-lsk-column-left .wt787-cdu-lsk');
    if (lsks.length >= n) {
        var lsk = lsks[n - 1];
        var btn = lsk.querySelector('.wt787-cdu-button');
        _mfd.clickElement(btn || lsk);
    }
};

// Click the Nth right LSK (1-6) in the FIRST CDU
_mfd.clickLskRight = function(n) {
    var firstCdu = document.querySelector('.wt787-cdu');
    if (!firstCdu) return;
    var lsks = firstCdu.querySelectorAll('.wt787-cdu-lsk-column-right .wt787-cdu-lsk');
    if (lsks.length >= n) {
        var lsk = lsks[n - 1];
        var btn = lsk.querySelector('.wt787-cdu-button');
        _mfd.clickElement(btn || lsk);
    }
};

// Click a page button in the FIRST CDU by logical name.
// DOM order of wt787-cdu-button elements outside LSK columns (confirmed from render source):
//   0  INIT/REF   1  RTE      2  DEP/ARR  3  ALTN    4  VNAV
//   5  EXEC       6  FIX      7  LEGS     8  HOLD     9  FMC/COMM
//   10 PROG       11 NAV/RAD  12 OFST*    13 RTA*     14 PREV PAGE  15 NEXT PAGE
// (* = inop buttons still present in DOM)
_mfd.pageButtonIndex = {
    'INIT_REF':  0, 'RTE':      1, 'DEP_ARR': 2, 'ALTN': 3, 'VNAV': 4,
    'EXEC':      5, 'FIX':      6, 'LEGS':    7, 'HOLD': 8, 'FMC_COMM': 9,
    'PROG':      10, 'NAV_RAD': 11,
    'PREV_PAGE': 14, 'NEXT_PAGE': 15
};

_mfd.clickPageButton = function(pageKey) {
    var firstCdu = document.querySelector('.wt787-cdu');
    if (!firstCdu) return;

    var allBtns = firstCdu.querySelectorAll('.wt787-cdu-button');
    var pageBtns = [];
    for (var i = 0; i < allBtns.length; i++) {
        var btn = allBtns[i];
        var inLsk = btn.closest('.wt787-cdu-lsk-column-left') || btn.closest('.wt787-cdu-lsk-column-right');
        if (!inLsk) pageBtns.push(btn);
    }

    var idx = _mfd.pageButtonIndex[pageKey];
    if (idx !== undefined && pageBtns.length > idx) {
        _mfd.clickElement(pageBtns[idx]);
    }
};

// --- Command Handlers ---

_mfd.handleCommand = function(command, payload) {
    console.log('[MFD Bridge] Command:', command, payload);
    try {
        var lskLeftMatch = command.match(/^lsk_L(\d)$/);
        if (lskLeftMatch) { _mfd.clickLskLeft(parseInt(lskLeftMatch[1])); return; }

        var lskRightMatch = command.match(/^lsk_R(\d)$/);
        if (lskRightMatch) { _mfd.clickLskRight(parseInt(lskRightMatch[1])); return; }

        // type_key:{KEY} — fire an FMC CDU button H-event (AS01B_FMC_1_BTN_ prefix).
        var typeKeyMatch = command.match(/^type_key:(.+)$/);
        if (typeKeyMatch) {
            try {
                SimVar.SetSimVarValue('H:AS01B_FMC_1_BTN_' + typeKeyMatch[1], 'number', 1);
            } catch (e) {
                console.error('[MFD Bridge] type_key error:', e);
            }
            return;
        }

        // fcu_key:{KEY} — fire an FCU/MCP H-event (AS01B_FMC_1_ prefix, no BTN_).
        // Used for MCP buttons like ALTITUDE_INTERVENTION that route through onFcuEvent.
        var fcuKeyMatch = command.match(/^fcu_key:(.+)$/);
        if (fcuKeyMatch) {
            try {
                SimVar.SetSimVarValue('H:AS01B_FMC_1_' + fcuKeyMatch[1], 'number', 1);
            } catch (e) {
                console.error('[MFD Bridge] fcu_key error:', e);
            }
            return;
        }

        switch (command) {
            case 'page_init_ref': _mfd.clickPageButton('INIT_REF'); break;
            case 'page_rte':      _mfd.clickPageButton('RTE'); break;
            case 'page_dep_arr':  _mfd.clickPageButton('DEP_ARR'); break;
            case 'page_altn':     _mfd.clickPageButton('ALTN'); break;
            case 'page_vnav':     _mfd.clickPageButton('VNAV'); break;
            case 'key_exec':      _mfd.clickPageButton('EXEC'); break;
            case 'page_fix':      _mfd.clickPageButton('FIX'); break;
            case 'page_legs':     _mfd.clickPageButton('LEGS'); break;
            case 'page_hold':     _mfd.clickPageButton('HOLD'); break;
            case 'page_fmc_comm': _mfd.clickPageButton('FMC_COMM'); break;
            case 'page_prog':     _mfd.clickPageButton('PROG'); break;
            case 'page_nav_rad':  _mfd.clickPageButton('NAV_RAD'); break;
            case 'key_prev_page': _mfd.clickPageButton('PREV_PAGE'); break;
            case 'key_next_page': _mfd.clickPageButton('NEXT_PAGE'); break;
            case 'read_screen':
                _mfd.previousScreen = null; // force re-send
                _mfd.pollScreen();
                break;
            case 'eval_js':
                // Trust boundary: this eval is only reachable via the C# app's
                // localhost-only HTTP bridge (EFBBridgeServer on 127.0.0.1:19778).
                // Commands are enqueued server-side; this bridge polls for them.
                // No external network exposure — only the local MSFSBA process can
                // enqueue commands. Used for hot-reload of bridge functions and
                // live DOM inspection without restarting the simulator. Same trust
                // boundary the PMDG EFB bridge already ships (eval_js, port 19777).
                if (payload && payload.code) {
                    try {
                        var evalResult = eval(payload.code);
                        _mfd.postState('eval_result', { result: String(evalResult) });
                    } catch (evalErr) {
                        _mfd.postState('eval_result', { error: evalErr.message });
                    }
                }
                break;
            default:
                console.warn('[MFD Bridge] Unknown command:', command);
        }
    } catch (e) {
        console.error('[MFD Bridge] Command error:', e);
    }
};

// --- Initialization ---

_mfd.startConnectionLoop = function() {
    _mfd.tryConnect();
    // Stored so hot-reload teardown can clear it (otherwise the old instance's
    // reconnect loop keeps running alongside the freshly-eval'd one).
    _mfd.reconnectTimer = setInterval(async function() {
        if (!_mfd.serverConnected) {
            await _mfd.tryConnect();
        }
    }, _mfd.RECONNECT_INTERVAL);
};

console.log('[MFD Bridge] Accessibility bridge loaded, connecting...');
_mfd.setStage(1); // Stage 1: script executed in this VCockpit context
setTimeout(function() {
    _mfd.startConnectionLoop();
}, 3000);

} // end double-init guard

} catch (e) {
    console.error('[MFD Bridge] Fatal error during initialization:', e);
}
