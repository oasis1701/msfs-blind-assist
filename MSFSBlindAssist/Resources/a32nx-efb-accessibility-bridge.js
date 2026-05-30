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

// ── Button text-search helper ─────────────────────────────────────────────────
// Used when no stable selector exists. Walks all clickable elements for text match.
_efb.findBtnByText = function(text) {
    var candidates = document.querySelectorAll('button, div[role="button"], div[tabindex], span[tabindex]');
    var lower = text.toLowerCase();
    for (var i = 0; i < candidates.length; i++) {
        if (candidates[i].textContent.trim().toLowerCase().indexOf(lower) !== -1) return candidates[i];
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
            case 'get_page_text':    _efb.postState('page_text', { text: document.body.innerText.substring(0, 2000) }); break;
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
    // Trigger the EFB's own SimBrief import so MCDU/FMS gets the flight plan.
    // C# reads flight data directly from SimBrief API — not from this DOM.
    var btn = _SEL.SB_FETCH_BTN ? document.querySelector(_SEL.SB_FETCH_BTN) : _efb.findBtnByText('Import');
    if (btn) {
        btn.click();
        // Brief delay then confirm — C# is already fetching data directly
        setTimeout(function() {
            _efb.postState('simbrief_loaded', { triggered: 'true' });
        }, 2000);
    } else {
        // Button not found on current page — post anyway so C# proceeds with direct fetch
        _efb.postState('simbrief_loaded', { triggered: 'false', reason: 'button_not_found' });
    }
};

_efb.cmdSendToMcdu = function() {
    var btn = _SEL.SB_SEND_BTN ? document.querySelector(_SEL.SB_SEND_BTN) : _efb.findBtnByText('Send to FMS');
    if (btn) { btn.click(); _efb.postState('mcdu_upload_result', { success: 'true' }); }
    else _efb.postState('error', { message: 'Send to FMS button not found' });
};

// Settings handlers
_efb.cmdGetSettings = function() {
    _efb.postState('settings_loaded', {
        simbrief_id:  _efb.getValue(_SEL.SB_ID_INPUT),
        weight_unit:  _efb.getValue(_SEL.WEIGHT_UNIT_SEL)
    });
};

_efb.cmdSetSetting = function(payload) {
    var selMap = { simbrief_id: _SEL.SB_ID_INPUT, weight_unit: _SEL.WEIGHT_UNIT_SEL };
    var sel = selMap[payload.id];
    if (sel) _efb.cmdSetReactInput(sel, payload.value);
};

_efb.cmdSaveSettings = function() {
    var btn = document.querySelector(_SEL.SETTINGS_SAVE);
    if (btn) btn.click();
};

// Navigraph handlers
_efb.cmdStartNavigraphAuth = function() {
    var btn = document.querySelector(_SEL.NAV_SIGN_IN);
    if (btn) {
        btn.click();
        // Poll for device code appearing in DOM
        var attempts = 0;
        var poll = setInterval(function() {
            attempts++;
            var statusText = _efb.getText(_SEL.NAV_STATUS);
            // FBW shows the device code in the status area; extract it
            var codeMatch = statusText.match(/[A-Z0-9]{4,8}/);
            if (codeMatch || attempts > 30) {
                clearInterval(poll);
                if (codeMatch) {
                    _efb.postState('navigraph_code', { code: codeMatch[0], url: 'https://navigraph.com/activate' });
                }
            }
        }, 1000);
    }
};

_efb.cmdSignOutNavigraph = function() {
    var btn = document.querySelector(_SEL.NAV_SIGN_OUT);
    if (btn) {
        btn.click();
        setTimeout(function() {
            _efb.postState('navigraph_auth_state', { authenticated: 'false', username: 'signed_out' });
        }, 1000);
    }
};

_efb.cmdCheckNavigraphAuth = function() {
    var statusText = _efb.getText(_SEL.NAV_STATUS);
    // FBW shows username in status text when authenticated; check for sign-out button visibility
    var signOutBtn = document.querySelector(_SEL.NAV_SIGN_OUT);
    var authenticated = signOutBtn && !signOutBtn.disabled && signOutBtn.style.display !== 'none';
    var username = '';
    if (authenticated) {
        // Extract username from status text — FBW typically renders "Signed in as <username>"
        var match = statusText.match(/signed in as (.+)/i);
        username = match ? match[1].trim() : 'navigraph user';
    }
    _efb.postState('navigraph_auth_state', {
        authenticated: authenticated ? 'true' : 'false',
        username: username
    });
};

_efb.cmdGetNavdataStatus = function() {
    _efb.postState('navdata_status', { cycle: _efb.getText(_SEL.AIRAC_LABEL) });
};

// Ground service handlers — SimVar K-events (source-verified from A320Services.tsx).
// FBW fires standard MSFS K-events directly from their React handlers — no DOM click needed.
// State is read from INTERACTIVE POINT OPEN SimVars (door/service connection indicators).
// GPU uses FBW's internal event bus (not a K-event); best-effort via K:REQUEST_POWER_SUPPLY.
// INTERACTIVE POINT indices (confirmed from A320Services.tsx useSimVar calls):
//   0 = cabin left door (jetway/stairs), 1 = cabin right door, 2 = aft left door
//   3 = aft right door (catering), 5 = cargo door (baggage), 9 = fuel truck
var _GND_DOOR_IDX = {
    jetway:     0,
    stairs_fwd: 0,
    stairs_aft: 0,
    gpu:        -1,   // state via L:A32NX_EXT_PWR_AVAIL:1
    fuel_truck: 9,
    catering:   3,
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
        // GPU: FBW uses an internal event bus (not a K-event). K:REQUEST_POWER_SUPPLY is the
        // closest standard MSFS equivalent but FBW may not respond to it.
        case 'gpu':
            SimVar.SetSimVarValue('K:REQUEST_POWER_SUPPLY', 'bool', true);
            break;
        case 'fuel_truck':
            SimVar.SetSimVarValue('K:REQUEST_FUEL_KEY', 'bool', true);
            break;
        case 'catering':
            SimVar.SetSimVarValue('K:REQUEST_CATERING', 'bool', true);
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
    if (varName) SimVar.SetSimVarValue(varName, 'Number', _efb.kgToGal(parseFloat(payload.value) || 0));
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
var _CARGO_DESIRED_VARS = {
    fwd_baggage_container: 'L:A32NX_CARGO_FWD_BAGGAGE_CONTAINER_DESIRED',
    aft_container:         'L:A32NX_CARGO_AFT_CONTAINER_DESIRED',
    aft_baggage:           'L:A32NX_CARGO_AFT_BAGGAGE_DESIRED',
    aft_bulk_loose:        'L:A32NX_CARGO_AFT_BULK_LOOSE_DESIRED'
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
    var keys = Object.keys(_CARGO_DESIRED_VARS);
    for (var i = 0; i < keys.length; i++) {
        var k = keys[i];
        state[k + '_kg'] = String(Math.round(SimVar.GetSimVarValue(_CARGO_DESIRED_VARS[k], 'Number')));
    }
    _efb.postState('payload_state', state);
};

_efb.cmdSetCargoWeight = function(payload) {
    var varName = _CARGO_DESIRED_VARS[payload.zone];
    if (varName) SimVar.SetSimVarValue(varName, 'Number', parseFloat(payload.value) || 0);
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

// ── Bootstrap ────────────────────────────────────────────────────────────────
_efb.waitForApp();

} // end double-load guard

} catch(e) {
    console.error('[A32NX EFB Bridge] Fatal error:', e);
}
