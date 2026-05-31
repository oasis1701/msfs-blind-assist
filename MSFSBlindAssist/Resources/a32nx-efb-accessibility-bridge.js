// A32NX EFB Accessibility Bridge
// Injected into the FBW A32NX efb.html via community package override.
// Coherent GT compatible: no AbortSignal.timeout, no arrow functions at top level,
// var not let/const, top-level try-catch.
try {

if (window._a32nx_efb_bridge_loaded) {
    console.log('[A32NX EFB Bridge] Already loaded, skipping');
} else { window._a32nx_efb_bridge_loaded = true;

// ── DOM Selectors ─────────────────────────────────────────────────────────────
// CRITICAL: FBW source has ZERO data-test-id attributes. All DOM selectors below
// must be verified in MSFS DevTools against the minified deployed bundle.
// Strategy: use text-content matching helpers for buttons; fallback to aria-label.
// Fuel, payload boarding, and cargo are handled via SimVar API — NO DOM needed.
var _SEL = {
    APP_ROOT: '#MSFS_REACT_MOUNT',           // confirmed in fbw-common/src/systems/instruments/src/defaults.ts

    // Navigation — active tab indicator; class names are Tailwind/obfuscated, verify in DevTools
    NAV_ACTIVE_ITEM: '[class*="bg-theme-accent"]', // confirmed from ToolBar.tsx NavLink activeClassName

    // SimBrief — verify exact button text in English locale; FBW uses i18n keys
    // Expected English: "Import from SimBrief", "Send to FMS"
    SB_FETCH_BTN:  null, // use _efb.findBtnByText('Import') fallback
    SB_SEND_BTN:   null, // use _efb.findBtnByText('Send to FMS') fallback
    SB_STATUS:     null, // verify text element in DevTools

    // Ground services — English text per i18n keys in Ground/Pages/Services/A320_251N/A320Services.tsx
    // t('Ground.Services.JetBridge') → "Jetway", t('Ground.Services.ExternalPower') → "Ext. Power"
    // VERIFY: actual button element type (div/button) and exact text in deployed bundle
    GND_JETWAY:     null, // use _efb.findBtnByText('Jetway')
    GND_STAIRS_FWD: null, // use _efb.findBtnByText('Stairs') — VERIFY if split fwd/aft
    GND_STAIRS_AFT: null, // use _efb.findBtnByText('Stairs')
    GND_GPU:        null, // use _efb.findBtnByText('Ext. Power') or 'GPU'
    GND_FUEL_TRUCK: null, // use _efb.findBtnByText('Fuel Truck')
    GND_CATERING:   null, // use _efb.findBtnByText('Catering Truck') or 'Catering'
    GND_PUSHBACK:   null, // use _efb.findBtnByText('Pushback')

    // Settings — verify input selectors in DevTools
    SB_ID_INPUT:     null, // ThirdPartyOptionsPage SimBrief Pilot ID SimpleInput
    WEIGHT_UNIT_SEL: null, // weight unit selector (kg/lbs)
    SETTINGS_SAVE:   null, // save/apply button

    // Navigraph — Link/Unlink buttons in ThirdPartyOptionsPage
    NAV_SIGN_IN:  null, // "Link" button
    NAV_SIGN_OUT: null, // "Unlink" button
    NAV_STATUS:   null, // status text showing username when signed in
    AIRAC_LABEL:  null, // AIRAC cycle label
};

// NOTE: Fuel, payload cargo, and boarding rate/start/stop use SimVar API directly.
// The _SEL entries for FUEL_* and PAY_* have been REMOVED — they are not used.

// ── State object ──────────────────────────────────────────────────────────────
var _efb = {
    SERVER_URL: 'http://localhost:19777',
    COMMAND_POLL_INTERVAL: 500,
    HEARTBEAT_INTERVAL: 5000,
    APP_WAIT_INTERVAL: 200,
    RECONNECT_INTERVAL: 5000,
    serverConnected: false,
    commandPollTimer: null,
    heartbeatTimer: null,
    navObserver: null,
    pendingStates: [],
    MAX_PENDING_STATES: 20,
    MAX_STATE_RETRIES: 3,
    CRITICAL_STATE_TYPES: ['simbrief_loaded', 'navigraph_code', 'connected'],
    connecting: false
};

// ── HTTP helpers ──────────────────────────────────────────────────────────────
_efb.postState = async function(type, data) {
    if (!_efb.serverConnected) { _efb.queueState(type, data); return; }
    try {
        var r = await fetch(_efb.SERVER_URL + '/state', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: type, data: data || {} })
        });
        if (!r.ok) _efb.queueState(type, data);
    } catch(e) { _efb.queueState(type, data); }
};

_efb.queueState = function(type, data) {
    if (_efb.CRITICAL_STATE_TYPES.indexOf(type) === -1) return;
    if (_efb.pendingStates.length >= _efb.MAX_PENDING_STATES) _efb.pendingStates.shift();
    _efb.pendingStates.push({ type: type, data: data || {}, retryCount: 0 });
};

_efb.flushPendingStates = async function() {
    var toRetry = _efb.pendingStates.slice();
    _efb.pendingStates = [];
    for (var i = 0; i < toRetry.length; i++) {
        var entry = toRetry[i];
        try {
            var r = await fetch(_efb.SERVER_URL + '/state', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ type: entry.type, data: entry.data })
            });
            if (!r.ok) throw new Error('not ok');
        } catch(e) {
            entry.retryCount++;
            if (entry.retryCount < _efb.MAX_STATE_RETRIES) _efb.pendingStates.push(entry);
        }
    }
};

_efb.fetchWithTimeout = function(url, options, ms) {
    return new Promise(function(resolve, reject) {
        var t = setTimeout(function() { reject(new Error('timeout')); }, ms);
        fetch(url, options).then(function(r) { clearTimeout(t); resolve(r); }).catch(function(e) { clearTimeout(t); reject(e); });
    });
};

// ── Connection lifecycle ──────────────────────────────────────────────────────
_efb.tryConnect = async function() {
    if (_efb.connecting) return _efb.serverConnected;
    _efb.connecting = true;
    try {
        var r = await _efb.fetchWithTimeout(_efb.SERVER_URL + '/ping', {}, 2000);
        if (r.ok) {
            if (!_efb.serverConnected) {
                _efb.serverConnected = true;
                _efb.startPolling();
                await _efb.postState('connected', {});
                await _efb.flushPendingStates();
            }
            _efb.connecting = false;
            return true;
        }
    } catch(e) {}
    if (_efb.serverConnected) { _efb.serverConnected = false; _efb.stopPolling(); }
    _efb.connecting = false;
    return false;
};

_efb.startPolling = function() {
    _efb.stopPolling();
    _efb.commandPollTimer = setInterval(function() { _efb.pollCommands(); }, _efb.COMMAND_POLL_INTERVAL);
    _efb.heartbeatTimer = setInterval(async function() {
        var ok = await _efb.tryConnect();
        if (ok) _efb.postState('heartbeat', {});
    }, _efb.HEARTBEAT_INTERVAL);
};

_efb.stopPolling = function() {
    if (_efb.commandPollTimer) { clearInterval(_efb.commandPollTimer); _efb.commandPollTimer = null; }
    if (_efb.heartbeatTimer) { clearInterval(_efb.heartbeatTimer); _efb.heartbeatTimer = null; }
};

_efb.pollCommands = async function() {
    if (!_efb.serverConnected) return;
    try {
        var r = await fetch(_efb.SERVER_URL + '/commands');
        if (!r.ok) return;
        var cmds = await r.json();
        for (var i = 0; i < cmds.length; i++) {
            _efb.handleCommand(cmds[i].command, cmds[i].payload || {});
        }
    } catch(e) {}
};

// ── App initialization ────────────────────────────────────────────────────────
_efb.waitForApp = function() {
    var root = document.querySelector(_SEL.APP_ROOT);
    if (root && root.children.length > 0) {
        _efb.initBridge();
    } else {
        setTimeout(_efb.waitForApp, _efb.APP_WAIT_INTERVAL);
    }
};

_efb.initBridge = function() {
    _efb.setupNavObserver();
    _efb.tryConnect();
    setInterval(function() { if (!_efb.serverConnected) _efb.tryConnect(); }, _efb.RECONNECT_INTERVAL);
};

_efb.setupNavObserver = function() {
    var observer = new MutationObserver(function() {
        var active = document.querySelector(_SEL.NAV_ACTIVE_ITEM);
        if (active) {
            var page = active.textContent.trim().toLowerCase().replace(/\s+/g, '_');
            _efb.postState('page_changed', { page: page });
        }
    });
    var target = document.querySelector(_SEL.APP_ROOT);
    if (target) observer.observe(target, { subtree: true, attributes: true, attributeFilter: ['class'] });
    _efb.navObserver = observer;
};

// ── React input helper ────────────────────────────────────────────────────────
_efb.setReactInput = function(el, value) {
    var nativeInput = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value');
    nativeInput.set.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
};

// ── Button text-search helpers ────────────────────────────────────────────────
// Partial match (substring). Used for most button lookups.
_efb.findBtnByText = function(text) {
    var candidates = document.querySelectorAll('button, div[role="button"], div[tabindex], span[tabindex]');
    var lower = text.toLowerCase();
    for (var i = 0; i < candidates.length; i++) {
        if (candidates[i].textContent.trim().toLowerCase().indexOf(lower) !== -1) return candidates[i];
    }
    return null;
};

// Exact match — needed to distinguish "Link" from "Unlink".
_efb.findBtnByExactText = function(text) {
    var candidates = document.querySelectorAll('button, div[role="button"], div[tabindex], span[tabindex]');
    var lower = text.toLowerCase();
    for (var i = 0; i < candidates.length; i++) {
        if (candidates[i].textContent.trim().toLowerCase() === lower) return candidates[i];
    }
    return null;
};

// ── Fuel unit helpers (L-vars store Gallons; bridge converts to/from kg) ──────
_efb.galToKg = function(gal) {
    var fwpg = SimVar.GetSimVarValue('FUEL WEIGHT PER GALLON', 'kilograms');
    return fwpg > 0 ? Math.round(gal * fwpg) : 0;
};
_efb.kgToGal = function(kg) {
    var fwpg = SimVar.GetSimVarValue('FUEL WEIGHT PER GALLON', 'kilograms');
    return fwpg > 0 ? kg / fwpg : 0;
};

// L:A32NX_EFB_REFUEL_RATE_SETTING: 0=Real, 1=Fast, 2=Instant
_efb.fuelModeStr = function(val) {
    if (val === 2) return 'instant';
    if (val === 1) return 'fast';
    return 'real';
};
_efb.fuelModeNum = function(str) {
    if (str === 'instant') return 2;
    if (str === 'fast') return 1;
    return 0;
};

// L:A32NX_BOARDING_RATE: 0=Instant, 1=Fast, 2=Real
_efb.boardingRateStr = function(val) {
    if (val === 2) return 'real';
    if (val === 1) return 'fast';
    return 'instant';
};
_efb.boardingRateNum = function(str) {
    if (str === 'real') return 2;
    if (str === 'fast') return 1;
    return 0;
};

// ── Command handlers ──────────────────────────────────────────────────────────
_efb.handleCommand = function(command, payload) {
    try {
        switch (command) {
            // Dashboard
            case 'get_simbrief_state': _efb.cmdGetSimbriefState(); break;
            case 'fetch_simbrief':     _efb.cmdFetchSimbrief(); break;
            case 'send_to_mcdu':       _efb.cmdSendToMcdu(); break;
            // Settings
            case 'get_settings':       _efb.cmdGetSettings(); break;
            case 'set_setting':        _efb.cmdSetSetting(payload); break;
            case 'save_settings':      _efb.cmdSaveSettings(); break;
            // Navigraph
            case 'start_navigraph_auth': _efb.cmdStartNavigraphAuth(); break;
            case 'sign_out_navigraph':   _efb.cmdSignOutNavigraph(); break;
            case 'check_navigraph_auth': _efb.cmdCheckNavigraphAuth(); break;
            case 'get_navdata_status':   _efb.cmdGetNavdataStatus(); break;
            // Ground
            case 'get_ground_state':         _efb.cmdGetGroundState(); break;
            case 'toggle_ground_service':    _efb.cmdToggleGroundService(payload); break;
            // Fuel
            case 'get_fuel_state':    _efb.cmdGetFuelState(); break;
            case 'set_fuel_target':   _efb.cmdSetFuelTarget(payload); break;
            case 'set_fuel_mode':     _efb.cmdSetFuelMode(payload); break;
            case 'start_fuel_loading': SimVar.SetSimVarValue('L:A32NX_REFUEL_STARTED_BY_USR', 'Bool', 1); break;
            case 'stop_fuel_loading':  SimVar.SetSimVarValue('L:A32NX_REFUEL_STARTED_BY_USR', 'Bool', 0); break;
            // Payload
            case 'get_payload_state':    _efb.cmdGetPayloadState(); break;
            case 'set_passenger_count':  _efb.cmdSetPassengerCount(payload); break;
            case 'set_cargo_weight':     _efb.cmdSetCargoWeight(payload); break;
            case 'set_boarding_rate':    _efb.cmdSetBoardingRate(payload); break;
            case 'start_boarding':       SimVar.SetSimVarValue('L:A32NX_BOARDING_STARTED_BY_USR', 'Bool', 1); break;
            case 'stop_boarding':        SimVar.SetSimVarValue('L:A32NX_BOARDING_STARTED_BY_USR', 'Bool', 0); break;
            // Diagnostics
            case 'ping':             _efb.postState('pong', { version: 1 }); break;
            case 'wake_efb':         _efb.cmdWakeEfb(); break;
            case 'get_page_text':    _efb.cmdGetPageText(payload); break;
            case 'run_diagnostics':  _efb.cmdRunDiagnostics(); break;
            default: _efb.postState('error', { message: 'Unknown command: ' + command }); break;
        }
    } catch(e) {
        _efb.postState('error', { message: command + ' failed: ' + e.message });
    }
};

// Generic helpers
_efb.cmdClickBtn = function(selector) {
    var btn = document.querySelector(selector);
    if (btn) btn.click();
    else _efb.postState('error', { message: 'Button not found: ' + selector });
};

_efb.cmdSetReactInput = function(selector, value) {
    var el = document.querySelector(selector);
    if (el) _efb.setReactInput(el, value);
    else _efb.postState('error', { message: 'Input not found: ' + selector });
};

_efb.getText = function(selector) {
    var el = document.querySelector(selector);
    return el ? el.textContent.trim() : '';
};

_efb.getValue = function(selector) {
    var el = document.querySelector(selector);
    return el ? (el.value || el.textContent.trim()) : '';
};

// SimBrief handlers
// NOTE: Redux store is NOT exposed on window — SimBrief flight data cannot be read
// back from the EFB DOM after import. C# fetches SimBrief data directly via the
// SimBrief OFP API (same pattern as PMDG EFBForm) using the pilot ID from settings.
// The bridge only needs to TRIGGER the EFB's own import (so FMS/MCDU gets the plan).
_efb.cmdGetSimbriefState = function() {
    // C# handles data fetch; bridge just confirms bridge is alive
    _efb.postState('simbrief_loaded', { bridge_only: 'true' });
};

_efb.cmdFetchSimbrief = function() {
    // Navigate to Dashboard first so the Import button is in the DOM.
    // C# fetches data directly from SimBrief API; this only triggers the EFB's own FMS import.
    var dashLink = document.querySelector('a[href="/dashboard"]');
    if (dashLink) dashLink.click();
    setTimeout(function() {
        var btn = _SEL.SB_FETCH_BTN ? document.querySelector(_SEL.SB_FETCH_BTN) : _efb.findBtnByText('Import');
        if (btn) {
            btn.click();
            setTimeout(function() {
                _efb.postState('simbrief_loaded', { triggered: 'true' });
            }, 2000);
        } else {
            // Dashboard loaded but button still not found — post so C# proceeds with direct API fetch
            _efb.postState('simbrief_loaded', { triggered: 'false', reason: 'button_not_found' });
        }
    }, 600);
};

_efb.cmdSendToMcdu = function() {
    // Navigate to Dashboard first so the Send to FMS button is in the DOM.
    var dashLink = document.querySelector('a[href="/dashboard"]');
    if (dashLink) dashLink.click();
    setTimeout(function() {
        var btn = _SEL.SB_SEND_BTN ? document.querySelector(_SEL.SB_SEND_BTN) : _efb.findBtnByText('Send to FMS');
        if (btn) { btn.click(); _efb.postState('mcdu_upload_result', { success: 'true' }); }
        else _efb.postState('error', { message: 'Send to FMS button not found on Dashboard' });
    }, 600);
};

// ── NXDataStore helpers (FBW A32NX stores settings in localStorage with A32NX_ prefix) ──
_efb.nxGet = function(key) {
    try { var v = localStorage.getItem('A32NX_' + key); return (v !== null && v !== undefined) ? v : null; } catch(e) { return null; }
};
_efb.nxSet = function(key, value) {
    try { localStorage.setItem('A32NX_' + key, String(value)); } catch(e) {}
};

// All NX setting keys exposed in the C# form.
// Keys verified against FBW A32NX source (fbw-common/src/systems/instruments/src/EFB/Settings/Pages/).
_efb.NX_SETTINGS_KEYS = [
    // Sim Options (SimOptionsPage.tsx)
    'CONFIG_INIT_BARO_UNIT', 'FP_SYNC', 'CONFIG_AUTO_SIM_ROUTE_LOAD',
    'CONFIG_SIMBRIDGE_ENABLED', 'CONFIG_SIMBRIDGE_REMOTE',
    'DYNAMIC_REGISTRATION_DECAL', 'RADIO_RECEIVER_USAGE_ENABLED', 'FDR_ENABLED',
    // Realism (RealismPage.tsx)
    'CONFIG_ALIGN_TIME', 'CONFIG_SELF_TEST_TIME', 'CONFIG_BOARDING_RATE',
    'EFB_AUTOFILL_CHECKLISTS', 'REALISTIC_TILLER_ENABLED', 'MCDU_KB_INPUT',
    'FO_SYNC_EFIS_ENABLED', 'CONFIG_PILOT_AVATAR_VISIBLE', 'CONFIG_FIRST_OFFICER_AVATAR_VISIBLE', 'PAUSE_AT_TOD',
    // 3rd Party (ThirdPartyOptionsPage.tsx)
    'CONFIG_OVERRIDE_SIMBRIEF_USERID', 'CONFIG_AUTO_SIMBRIEF_IMPORT',
    'GSX_FUEL_SYNC', 'GSX_PAYLOAD_SYNC', 'GSX_POWER_SYNC',
    // ATSU / AOC (AtsuAocPage.tsx)
    'CONFIG_HOPPIE_USERID', 'CONFIG_ATIS_SRC', 'CONFIG_METAR_SRC', 'ACARS_PROVIDER',
    // Audio (AudioPage.tsx)
    'SOUND_EXTERIOR_MASTER', 'SOUND_INTERIOR_ENGINE', 'SOUND_INTERIOR_WIND',
    'SOUND_PTU_AUDIBLE_COCKPIT', 'SOUND_PASSENGER_AMBIENCE_ENABLED',
    'SOUND_ANNOUNCEMENTS_ENABLED', 'SOUND_BOARDING_MUSIC_ENABLED',
    // flyPad (FlyPadPage.tsx)
    'EFB_LANGUAGE', 'EFB_KEYBOARD_LAYOUT_IDENT', 'EFB_AUTO_OSK', 'EFB_USING_AUTOBRIGHTNESS',
    'EFB_BATTERY_LIFE_ENABLED', 'EFB_SHOW_STATUSBAR_FLIGHTPROGRESS', 'EFB_USING_COLOREDMETAR',
    'EFB_TIME_DISPLAYED', 'EFB_TIME_FORMAT'
];

// Settings handlers — read/write via NXDataStore (localStorage A32NX_ prefix)
_efb.cmdGetSettings = function() {
    var state = {};
    for (var i = 0; i < _efb.NX_SETTINGS_KEYS.length; i++) {
        var v = _efb.nxGet(_efb.NX_SETTINGS_KEYS[i]);
        if (v !== null) state[_efb.NX_SETTINGS_KEYS[i]] = v;
    }
    _efb.postState('settings', state);
};

_efb.cmdSetSetting = function(payload) {
    // C# sends ["key"], but accept ["id"] for backwards compat
    var key = payload.key || payload.id;
    if (!key || payload.value === undefined || payload.value === null) return;
    _efb.nxSet(key, payload.value);
};

_efb.cmdSaveSettings = function() {
    // localStorage saves are immediate; nothing to flush
};

// Navigraph handlers — navigate EFB to Settings > 3rd Party, click Link, extract device-flow code.
_efb.cmdStartNavigraphAuth = function() {
    // Step 1: Navigate to /settings via ToolBar NavLink (href="/settings")
    var settingsLink = document.querySelector('a[href="/settings"]');
    if (settingsLink) settingsLink.click();

    setTimeout(function() {
        // Step 2: Navigate to 3rd Party Options (Link renders as <a href*="3rd-party-options">)
        var thirdPartyLink = document.querySelector('a[href*="3rd-party-options"]');
        if (thirdPartyLink) thirdPartyLink.click();

        setTimeout(function() {
            // Step 3: Click the Link button (exact text to avoid hitting Unlink)
            var btn = _efb.findBtnByExactText('Link') || _efb.findBtnByText('Link');
            if (!btn) {
                _efb.postState('error', { message: 'Navigraph Link button not found. EFB may not be on Settings > 3rd Party.' });
                return;
            }
            btn.click();

            // Step 4: Poll for device-flow code rendered by NavigraphAuthUI.
            // The code appears in an <h1 class*="tracking-wider"> element.
            var attempts = 0;
            var authPoll = setInterval(function() {
                attempts++;
                var code = '';
                var url = '';

                // NavigraphAuthUI: <h1 class="... text-4xl font-bold tracking-wider ...">USER_CODE</h1>
                var h1s = document.querySelectorAll('h1');
                for (var i = 0; i < h1s.length; i++) {
                    var txt = h1s[i].textContent.trim();
                    // Device-flow codes: uppercase alphanumeric, 4–12 chars, may include hyphen
                    if (/^[A-Z0-9][A-Z0-9-]{3,11}$/.test(txt)) { code = txt; break; }
                }

                // Verification URL is in a span with class*="text-theme-highlight"
                if (!url) {
                    var spans = document.querySelectorAll('span');
                    for (var j = 0; j < spans.length; j++) {
                        var spanTxt = spans[j].textContent.trim();
                        if (spanTxt.indexOf('navigraph') !== -1 || spanTxt.indexOf('navigate.chart') !== -1) {
                            url = spanTxt; break;
                        }
                    }
                }

                if (code || attempts > 30) {
                    clearInterval(authPoll);
                    if (code) {
                        _efb.postState('navigraph_code', {
                            code: code,
                            url: url || 'https://navigraph.com/activate'
                        });
                    } else {
                        _efb.postState('error', { message: 'Navigraph auth code not found in DOM after ' + attempts + ' attempts' });
                    }
                }
            }, 1000);
        }, 700);
    }, 700);
};

_efb.cmdSignOutNavigraph = function() {
    var btn = _SEL.NAV_SIGN_OUT ? document.querySelector(_SEL.NAV_SIGN_OUT) : null;
    if (!btn) btn = _efb.findBtnByText('Unlink');
    if (btn) {
        btn.click();
        setTimeout(function() {
            _efb.postState('navigraph_status', { signed_in: 'false', username: '' });
        }, 1000);
    } else {
        // Clear local auth state even if DOM click fails
        try { localStorage.removeItem('A32NX_NAVIGRAPH_USERNAME'); localStorage.removeItem('A32NX_navigraph_username'); } catch(e) {}
        _efb.postState('navigraph_status', { signed_in: 'false', username: '' });
    }
};

_efb.cmdCheckNavigraphAuth = function() {
    // Try NXDataStore variants FBW might use
    var username = _efb.nxGet('NAVIGRAPH_USERNAME') || _efb.nxGet('navigraph_username') || '';

    // Try raw localStorage keys (navigraph/auth SDK doesn't use A32NX_ prefix)
    if (!username) {
        try {
            var rawKeys = ['navigraph_username', 'navigraph_user', 'ng_username'];
            for (var i = 0; i < rawKeys.length; i++) {
                var v = localStorage.getItem(rawKeys[i]);
                if (v && v.length > 0) { username = v; break; }
            }
        } catch(e) {}
    }

    // DOM fallback: if Unlink button is visible, user is signed in — try to read username from nearby text
    if (!username) {
        var unlinkBtn = _efb.findBtnByExactText('Unlink');
        if (unlinkBtn) {
            // ThirdPartyOptionsPage shows username text before the Unlink button
            var parent = unlinkBtn.parentElement;
            if (parent) {
                var spans = parent.querySelectorAll('span');
                for (var k = 0; k < spans.length; k++) {
                    var t = spans[k].textContent.trim();
                    // Username is short, doesn't contain "navigraph" or "unlink"
                    if (t && t.length > 0 && t.length < 60 &&
                        t.toLowerCase().indexOf('unlink') === -1 &&
                        t.toLowerCase().indexOf('navigraph') === -1) {
                        username = t; break;
                    }
                }
            }
            if (!username) username = 'Signed in';
        }
    }

    _efb.postState('navigraph_status', {
        signed_in: username.length > 0 ? 'true' : 'false',
        username: username
    });
};

_efb.cmdGetNavdataStatus = function() {
    var cycle = _efb.nxGet('AIRAC_CYCLE') || _efb.nxGet('NAVIGRAPH_AIRAC_CYCLE') ||
                (_SEL.AIRAC_LABEL ? _efb.getText(_SEL.AIRAC_LABEL) : '') || '—';
    _efb.postState('navdata_status', { cycle: cycle });
};

// Ground service handlers — SimVar K-events (source-verified from A320Services.tsx).
// FBW fires standard MSFS K-events directly from their React handlers — no DOM click needed.
// State is read from INTERACTIVE POINT OPEN SimVars (door/service connection indicators).
// GPU uses FBW's internal event bus (not a K-event); best-effort via K:REQUEST_POWER_SUPPLY.
// INTERACTIVE POINT indices (confirmed from A320Services.tsx useSimVar calls):
//   0 = cabin left door (jetway/stairs), 1 = cabin right door, 2 = aft left door
//   3 = aft right door (catering), 5 = cargo door (baggage), 8 = GPU, 9 = fuel truck
var _GND_DOOR_IDX = {
    jetway:     0,
    stairs_fwd: 0,
    stairs_aft: 0,
    gpu:        -1,   // state via L:A32NX_EXT_PWR_AVAIL:1 (toggled separately in cmdToggleGroundService)
    fuel_truck: 9,
    catering:   3,
    baggage:    5,    // cargo door; K:REQUEST_LUGGAGE fires the truck
    pushback:   -1,   // state via PUSHBACK ATTACHED
};

_efb.cmdGetGroundState = function() {
    var state = {};
    var ids = Object.keys(_GND_DOOR_IDX);
    for (var i = 0; i < ids.length; i++) {
        var id = ids[i];
        var idx = _GND_DOOR_IDX[id];
        var connected;
        if (id === 'gpu') {
            connected = SimVar.GetSimVarValue('L:A32NX_EXT_PWR_AVAIL:1', 'bool') ? true : false;
        } else if (id === 'pushback') {
            connected = SimVar.GetSimVarValue('PUSHBACK ATTACHED', 'bool') ? true : false;
        } else {
            connected = SimVar.GetSimVarValue('A:INTERACTIVE POINT OPEN:' + idx, 'Percent over 100') > 0.5;
        }
        state[id] = connected ? 'connected' : 'available';
    }
    _efb.postState('ground_state', state);
};

_efb.cmdToggleGroundService = function(payload) {
    var id = payload.service_id;
    switch (id) {
        // FS2020: jetway button toggles both jetway AND ramp truck simultaneously
        case 'jetway':
            SimVar.SetSimVarValue('K:TOGGLE_JETWAY', 'bool', false);
            SimVar.SetSimVarValue('K:TOGGLE_RAMPTRUCK', 'bool', false);
            break;
        case 'stairs_fwd':
        case 'stairs_aft':
            SimVar.SetSimVarValue('K:TOGGLE_RAMPTRUCK', 'bool', false);
            break;
        // GPU: replicates GPUManagement.toggleGPU() from fbw-common/src/systems/shared/src/GPUManagement.ts.
        // INTERACTIVE POINT OPEN:8 = GPU door (physically connected); L:A32NX_EXT_PWR_AVAIL:1 = FBW power flag.
        // Powered-stand path sets the L-var directly; cart path fires K:REQUEST_POWER_SUPPLY.
        case 'gpu':
            var gpuAvailNow = SimVar.GetSimVarValue('L:A32NX_EXT_PWR_AVAIL:1', 'bool') ? true : false;
            var gpuDoorOpen = SimVar.GetSimVarValue('A:INTERACTIVE POINT OPEN:8', 'Percent over 100') >= 1.0;
            if (!gpuAvailNow) {
                if (gpuDoorOpen) {
                    // Powered stand: directly set FBW's ext-power L-var
                    SimVar.SetSimVarValue('L:A32NX_EXT_PWR_AVAIL:1', 'Bool', 1);
                } else {
                    // No GPU present: call the GPU cart
                    SimVar.SetSimVarValue('K:REQUEST_POWER_SUPPLY', 'Bool', true);
                }
            } else {
                if (gpuDoorOpen) {
                    // Cart was called and connected: disconnect it
                    SimVar.SetSimVarValue('K:REQUEST_POWER_SUPPLY', 'Bool', true);
                } else {
                    // Powered stand: clear ext-power and overhead PB
                    SimVar.SetSimVarValue('L:A32NX_EXT_PWR_AVAIL:1', 'Bool', 0);
                    SimVar.SetSimVarValue('L:A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON', 'Bool', 0);
                }
            }
            break;
        case 'fuel_truck':
            SimVar.SetSimVarValue('K:REQUEST_FUEL_KEY', 'bool', true);
            break;
        case 'catering':
            SimVar.SetSimVarValue('K:REQUEST_CATERING', 'bool', true);
            break;
        // baggage: K:REQUEST_LUGGAGE confirmed from A320Services.tsx line 157
        case 'baggage':
            SimVar.SetSimVarValue('K:REQUEST_LUGGAGE', 'bool', true);
            break;
        case 'pushback':
            SimVar.SetSimVarValue('K:TOGGLE_PUSHBACK', 'bool', true);
            break;
        default:
            _efb.postState('error', { message: 'Unknown ground service: ' + id });
            return;
    }
    _efb.postState('ground_toggle_sent', { service_id: id });
};

// Fuel handlers — SimVar-based (no DOM interaction needed)
// L:A32NX_FUEL_*_DESIRED and FUEL TANK * QUANTITY are in Gallons.
// Bridge converts to/from kg using FUEL WEIGHT PER GALLON.
_efb.cmdGetFuelState = function() {
    var loading = SimVar.GetSimVarValue('L:A32NX_REFUEL_STARTED_BY_USR', 'Bool');
    _efb.postState('fuel_state', {
        status:               loading ? 'Loading' : 'Idle',
        mode:                 _efb.fuelModeStr(SimVar.GetSimVarValue('L:A32NX_EFB_REFUEL_RATE_SETTING', 'Number')),
        centre_actual_kg:     String(_efb.galToKg(SimVar.GetSimVarValue('FUEL TANK CENTER QUANTITY', 'Gallons'))),
        left_main_actual_kg:  String(_efb.galToKg(SimVar.GetSimVarValue('FUEL TANK LEFT MAIN QUANTITY', 'Gallons'))),
        left_aux_actual_kg:   String(_efb.galToKg(SimVar.GetSimVarValue('FUEL TANK LEFT AUX QUANTITY', 'Gallons'))),
        right_main_actual_kg: String(_efb.galToKg(SimVar.GetSimVarValue('FUEL TANK RIGHT MAIN QUANTITY', 'Gallons'))),
        right_aux_actual_kg:  String(_efb.galToKg(SimVar.GetSimVarValue('FUEL TANK RIGHT AUX QUANTITY', 'Gallons'))),
        centre_target_kg:     String(_efb.galToKg(SimVar.GetSimVarValue('L:A32NX_FUEL_CENTER_DESIRED', 'Number'))),
        left_main_target_kg:  String(_efb.galToKg(SimVar.GetSimVarValue('L:A32NX_FUEL_LEFT_MAIN_DESIRED', 'Number'))),
        left_aux_target_kg:   String(_efb.galToKg(SimVar.GetSimVarValue('L:A32NX_FUEL_LEFT_AUX_DESIRED', 'Number'))),
        right_main_target_kg: String(_efb.galToKg(SimVar.GetSimVarValue('L:A32NX_FUEL_RIGHT_MAIN_DESIRED', 'Number'))),
        right_aux_target_kg:  String(_efb.galToKg(SimVar.GetSimVarValue('L:A32NX_FUEL_RIGHT_AUX_DESIRED', 'Number'))),
        total_target_kg:      String(_efb.galToKg(SimVar.GetSimVarValue('L:A32NX_FUEL_TOTAL_DESIRED', 'Number')))
    });
};

_efb.cmdSetFuelTarget = function(payload) {
    var varMap = {
        centre:     'L:A32NX_FUEL_CENTER_DESIRED',
        left_main:  'L:A32NX_FUEL_LEFT_MAIN_DESIRED',
        left_aux:   'L:A32NX_FUEL_LEFT_AUX_DESIRED',
        right_main: 'L:A32NX_FUEL_RIGHT_MAIN_DESIRED',
        right_aux:  'L:A32NX_FUEL_RIGHT_AUX_DESIRED'
    };
    var varName = varMap[payload.tank];
    if (varName) SimVar.SetSimVarValue(varName, 'Number', _efb.kgToGal(parseFloat(payload.kg) || 0));
    // Recompute total desired after changing any tank
    var total = SimVar.GetSimVarValue('L:A32NX_FUEL_CENTER_DESIRED', 'Number') +
                SimVar.GetSimVarValue('L:A32NX_FUEL_LEFT_MAIN_DESIRED', 'Number') +
                SimVar.GetSimVarValue('L:A32NX_FUEL_LEFT_AUX_DESIRED', 'Number') +
                SimVar.GetSimVarValue('L:A32NX_FUEL_RIGHT_MAIN_DESIRED', 'Number') +
                SimVar.GetSimVarValue('L:A32NX_FUEL_RIGHT_AUX_DESIRED', 'Number');
    SimVar.SetSimVarValue('L:A32NX_FUEL_TOTAL_DESIRED', 'Number', total);
};

_efb.cmdSetFuelMode = function(payload) {
    SimVar.SetSimVarValue('L:A32NX_EFB_REFUEL_RATE_SETTING', 'Number', _efb.fuelModeNum(payload.mode));
};

// Payload handlers — SimVar-based for boarding rate/start/stop and cargo zones.
// Cargo L-var names confirmed from fbw-a32nx/.../A32NX_PayloadManager.ts.
// Cargo units: kg (NXUnits.kgToUser wrapping exists in EFB but L-vars store kg).
var _CARGO_VARS = {
    fwd_baggage_container: 'L:A32NX_CARGO_FWD_BAGGAGE_CONTAINER',
    aft_container:         'L:A32NX_CARGO_AFT_CONTAINER',
    aft_baggage:           'L:A32NX_CARGO_AFT_BAGGAGE',
    aft_bulk_loose:        'L:A32NX_CARGO_AFT_BULK_LOOSE'
};
// Keys match zone IDs sent by C# WireCargoTarget
var _CARGO_DESIRED_VARS = {
    FWD_BAGGAGE:   'L:A32NX_CARGO_FWD_BAGGAGE_CONTAINER_DESIRED',
    AFT_CONTAINER: 'L:A32NX_CARGO_AFT_CONTAINER_DESIRED',
    AFT_BAGGAGE:   'L:A32NX_CARGO_AFT_BAGGAGE_DESIRED',
    AFT_BULK:      'L:A32NX_CARGO_AFT_BULK_LOOSE_DESIRED'
};

// Pax distribution: fill stations front-to-back.
// Stations: A=rows1-6(36), B=rows7-13(42), C=rows14-21(48), D=rows22-29(48). Total=174.
var _PAX_STATIONS = [
    { desiredVar: 'L:A32NX_PAX_A_DESIRED', capacity: 36 },
    { desiredVar: 'L:A32NX_PAX_B_DESIRED', capacity: 42 },
    { desiredVar: 'L:A32NX_PAX_C_DESIRED', capacity: 48 },
    { desiredVar: 'L:A32NX_PAX_D_DESIRED', capacity: 48 }
];

_efb.cmdGetPayloadState = function() {
    var boarding = SimVar.GetSimVarValue('L:A32NX_BOARDING_STARTED_BY_USR', 'Bool');

    // Count current pax by summing set bits across PAX station desired vars.
    // Each bit represents one occupied seat (front-to-back fill order).
    var totalPax = 0;
    for (var s = 0; s < _PAX_STATIONS.length; s++) {
        var bits = SimVar.GetSimVarValue(_PAX_STATIONS[s].desiredVar, 'Number');
        // Brian Kernighan bit-count (safe for values up to 2^53)
        var n = bits; var count = 0;
        while (n > 0) { n = n - (n & -n); count++; }
        totalPax += count;
    }

    var state = {
        status:        boarding ? 'Boarding' : 'Not Started',
        boarding_rate: _efb.boardingRateStr(SimVar.GetSimVarValue('L:A32NX_BOARDING_RATE', 'Number')),
        pax_count:     String(totalPax)
    };
    // State keys match what C# ApplyPayloadState expects
    state['fwd_baggage']  = String(Math.round(SimVar.GetSimVarValue('L:A32NX_CARGO_FWD_BAGGAGE_CONTAINER_DESIRED', 'Number')));
    state['aft_container'] = String(Math.round(SimVar.GetSimVarValue('L:A32NX_CARGO_AFT_CONTAINER_DESIRED', 'Number')));
    state['aft_baggage']  = String(Math.round(SimVar.GetSimVarValue('L:A32NX_CARGO_AFT_BAGGAGE_DESIRED', 'Number')));
    state['aft_bulk']     = String(Math.round(SimVar.GetSimVarValue('L:A32NX_CARGO_AFT_BULK_LOOSE_DESIRED', 'Number')));
    _efb.postState('payload_state', state);
};

_efb.cmdSetCargoWeight = function(payload) {
    var varName = _CARGO_DESIRED_VARS[payload.zone];
    if (varName) SimVar.SetSimVarValue(varName, 'Number', parseFloat(payload.kg) || 0);
};

_efb.cmdSetBoardingRate = function(payload) {
    SimVar.SetSimVarValue('L:A32NX_BOARDING_RATE', 'Number', _efb.boardingRateNum(payload.rate));
};

// Passenger count: distribute total evenly across stations front-to-back.
// Pax stations use bit flags (1 bit per seat). Math.pow(2, n) - 1 fills n seats.
// VERIFY: MSFS L-var precision supports up to 48-bit values (within JS 53-bit safe int).
_efb.cmdSetPassengerCount = function(payload) {
    var total = Math.min(parseInt(payload.value) || 0, 174);
    var remaining = total;
    for (var i = 0; i < _PAX_STATIONS.length; i++) {
        var n = Math.min(remaining, _PAX_STATIONS[i].capacity);
        var bits = n >= 32 ? (Math.pow(2, n) - 1) : ((1 << n) - 1);
        SimVar.SetSimVarValue(_PAX_STATIONS[i].desiredVar, 'Number', bits);
        remaining -= n;
    }
};

// Navigate to a specific EFB page route and return body text once there.
// Without navigation the text reflects whatever page is currently visible,
// which would be wrong if the user is on a different tab.
_efb.cmdGetPageText = function(payload) {
    var page = payload.page || 'unknown';
    // NavLink hrefs from ToolBar.tsx — these are the actual React Router routes
    var routeMap = {
        'failures':   '/failures',
        'checklists': '/checklists',
        'presets':    '/presets',
        'dashboard':  '/dashboard',
        'dispatch':   '/dispatch',
        'navigation': '/navigation',
        'atc':        '/atc'
    };
    var route = routeMap[page];
    if (route) {
        var navLink = document.querySelector('a[href="' + route + '"]');
        if (navLink) navLink.click();
        // Wait for React to re-render the new page before scraping
        setTimeout(function() {
            _efb.postState('page_text', { page: page, text: document.body.innerText.substring(0, 2000) });
        }, 700);
    } else {
        // Unknown page — scrape whatever is currently visible
        _efb.postState('page_text', { page: page, text: document.body.innerText.substring(0, 2000) });
    }
};

// ── Diagnostics ──────────────────────────────────────────────────────────────
_efb.cmdRunDiagnostics = function() {
    var appRoot = document.querySelector(_SEL.APP_ROOT);
    var allScripts = document.querySelectorAll('script');
    var ourScriptFound = false;
    for (var si = 0; si < allScripts.length; si++) {
        var src = allScripts[si].src || allScripts[si].getAttribute('src') || '';
        if (src.indexOf('a32nx-efb-accessibility-bridge') !== -1) { ourScriptFound = true; break; }
    }
    var navItem = document.querySelector(_SEL.NAV_ACTIVE_ITEM);
    var activePage = navItem ? navItem.textContent.trim() : 'none';
    var hasFetchBtn = !!_efb.findBtnByText('Import');
    var hasJetwayBtn = !!_efb.findBtnByText('Jetway');
    _efb.postState('diagnostics', {
        bridge_js_version: '2',
        app_root_found: String(!!appRoot),
        app_root_child_count: String(appRoot ? appRoot.children.length : 0),
        simvar_available: String(typeof SimVar !== 'undefined'),
        connected: String(_efb.serverConnected),
        pending_state_count: String(_efb.pendingStates.length),
        our_script_in_dom: String(ourScriptFound),
        total_script_tags: String(allScripts.length),
        active_page: activePage,
        import_btn_found: String(hasFetchBtn),
        jetway_btn_found: String(hasJetwayBtn),
        page_title: document.title || 'none'
    });
};

_efb.cmdWakeEfb = function() {
    // H:A32NX_EFB_POWER fires the flypad power-toggle interaction event.
    // Toggles STANDBY→LOADED (or LOADED→STANDBY). Only call when EFB is in standby.
    SimVar.SetSimVarValue('H:A32NX_EFB_POWER', 'number', 1);
    _efb.postState('wake_efb_sent', {});
};

// ── Bootstrap ────────────────────────────────────────────────────────────────
_efb.waitForApp();

} // end double-load guard

} catch(e) {
    console.error('[A32NX EFB Bridge] Fatal error:', e);
}
