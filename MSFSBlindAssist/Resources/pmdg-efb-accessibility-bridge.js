// PMDG EFB Accessibility Bridge
// BRIDGE_JS_VERSION: 35-prefclick
// Injected into PMDGTabletCA.html via mod package override to expose EFB
// functionality to the MSFS Blind Assist application via HTTP on localhost:19777.
//
// Runs in MSFS Coherent GT (older Chromium). No modern APIs like AbortSignal.timeout().
// Top-level try-catch ensures errors here never break the EFB.
try {

var _efb = {
    JS_VERSION: '35-prefclick',
    SERVER_URL: 'http://localhost:19777',
    COMMAND_POLL_INTERVAL: 500,
    HEARTBEAT_INTERVAL: 5000,
    BUS_WAIT_INTERVAL: 500,
    RECONNECT_INTERVAL: 5000,
    bus: null,
    publisher: null,
    serverConnected: false,
    commandPollTimer: null,
    heartbeatTimer: null,
    fmsTransferWxCode: null,
    fmsTransferFpCode: null,
    fmsTransferTimeout: null,
    pendingStates: [],
    MAX_PENDING_STATES: 20,
    MAX_STATE_RETRIES: 3,
    CRITICAL_STATE_TYPES: ['simbrief_loaded', 'navigraph_code', 'navigraph_auth_state', 'preferences', 'simbrief_fetch_result', 'fmc_upload_started', 'connected', 'error'],
    connecting: false,
    navigraphStateSent: false
};

// --- HTTP Communication ---

_efb.postState = async function(type, data) {
    if (!_efb.serverConnected) {
        _efb.queueState(type, data);
        return;
    }
    try {
        var response = await fetch(_efb.SERVER_URL + '/state', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: type, data: data || {} })
        });
        if (!response.ok) {
            _efb.queueState(type, data);
        }
    } catch (e) {
        _efb.queueState(type, data);
    }
};

_efb.queueState = function(type, data) {
    if (_efb.CRITICAL_STATE_TYPES.indexOf(type) === -1) return; // Drop non-critical
    if (_efb.pendingStates.length >= _efb.MAX_PENDING_STATES) {
        _efb.pendingStates.shift(); // Drop oldest
        console.warn('[EFB Bridge] Pending state queue full, dropped oldest entry');
    }
    _efb.pendingStates.push({ type: type, data: data || {}, retryCount: 0 });
};

_efb.flushPendingStates = async function() {
    var toRetry = _efb.pendingStates.slice();
    _efb.pendingStates = [];
    for (var i = 0; i < toRetry.length; i++) {
        var entry = toRetry[i];
        try {
            var response = await fetch(_efb.SERVER_URL + '/state', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ type: entry.type, data: entry.data })
            });
            if (!response.ok) throw new Error('not ok');
        } catch (e) {
            entry.retryCount++;
            if (entry.retryCount < _efb.MAX_STATE_RETRIES) {
                _efb.pendingStates.push(entry);
            } else {
                console.warn('[EFB Bridge] Dropping state after max retries:', entry.type);
            }
        }
    }
};

_efb.pollCommands = async function() {
    if (!_efb.serverConnected) return;
    try {
        var response = await fetch(_efb.SERVER_URL + '/commands');
        if (!response.ok) return;
        var commands = await response.json();
        for (var i = 0; i < commands.length; i++) {
            _efb.handleCommand(commands[i].command, commands[i].payload);
        }
    } catch (e) {
        // Server unreachable — will be detected by heartbeat
    }
};

_efb.fetchWithTimeout = function(url, options, timeoutMs) {
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

_efb._tryConnectInner = async function() {
    if (_efb.connecting) return _efb.serverConnected;
    _efb.connecting = true;
    try {
        var response = await _efb.fetchWithTimeout(_efb.SERVER_URL + '/ping', {}, 2000);
        if (response.ok) {
            if (!_efb.serverConnected) {
                _efb.serverConnected = true;
                _efb.navigraphStateSent = false;
                console.log('[EFB Bridge] Connected to accessibility server');
                _efb.startPolling();
                _efb.postState('connected');
                // Flush any states queued while disconnected
                await _efb.flushPendingStates();
                // Send current state now that connection is established
                _efb.sendCurrentNavigraphState();
            }
            _efb.connecting = false;
            return true;
        }
    } catch (e) {
        // Not available yet
    }

    if (_efb.serverConnected) {
        _efb.serverConnected = false;
        _efb.navigraphStateSent = false;
        console.log('[EFB Bridge] Lost connection to accessibility server');
        _efb.stopPolling();
    }
    _efb.connecting = false;
    return false;
};

_efb.tryConnect = async function() {
    try {
        return await _efb._tryConnectInner();
    } catch (e) {
        _efb.connecting = false;
        return false;
    }
};

_efb.startPolling = function() {
    _efb.stopPolling();
    _efb.commandPollTimer = setInterval(function() { _efb.pollCommands(); }, _efb.COMMAND_POLL_INTERVAL);
    _efb.heartbeatTimer = setInterval(async function() {
        var ok = await _efb.tryConnect();
        if (ok) {
            _efb.postState('heartbeat');
        }
    }, _efb.HEARTBEAT_INTERVAL);
};

_efb.stopPolling = function() {
    if (_efb.commandPollTimer) {
        clearInterval(_efb.commandPollTimer);
        _efb.commandPollTimer = null;
    }
    if (_efb.heartbeatTimer) {
        clearInterval(_efb.heartbeatTimer);
        _efb.heartbeatTimer = null;
    }
};

// --- Command Handlers ---

_efb.handleCommand = function(command, payload) {
    console.log('[EFB Bridge] Command:', command, payload);
    try {
        switch (command) {
            case 'fetch_simbrief':
                _efb.cmdFetchSimbrief();
                break;
            case 'replay_simbrief':
                _efb.cmdReplaySimbrief();
                break;
            case 'read_values':
                _efb.cmdReadValues(payload);
                break;
            case 'set_input_by_id':
                _efb.cmdSetInputById(payload);
                break;
            case 'set_select_by_id':
                _efb.cmdSetSelectById(payload);
                break;
            case 'get_select_options':
                _efb.cmdGetSelectOptions(payload);
                break;
            case 'dump_element':
                _efb.cmdDumpElement(payload);
                break;
            case 'ping':
                _efb.postState('pong', {
                    version: String(_efb.JS_VERSION || 'unknown'),
                    has_dump_element: String(typeof _efb.cmdDumpElement === 'function'),
                    has_set_select_by_id: String(typeof _efb.cmdSetSelectById === 'function'),
                    has_set_input_by_id: String(typeof _efb.cmdSetInputById === 'function'),
                    has_get_select_options: String(typeof _efb.cmdGetSelectOptions === 'function'),
                    has_read_values: String(typeof _efb.cmdReadValues === 'function')
                });
                break;
            case 'dump_preferences':
                _efb.cmdDumpPreferences();
                break;
            case 'send_to_fmc':
                _efb.cmdSendToFMC();
                break;
            case 'start_navigraph_auth':
                _efb.cmdStartNavigraphAuth();
                break;
            case 'sign_out_navigraph':
                _efb.cmdSignOutNavigraph();
                break;
            case 'check_navigraph_auth':
                _efb.cmdCheckNavigraphAuth();
                break;
            case 'get_preferences':
                _efb.cmdGetPreferences();
                break;
            case 'set_preference':
                if (payload) _efb.cmdSetPreference(payload.key, payload.value);
                break;
            case 'save_preferences':
                _efb.cmdSavePreferences();
                break;
            case 'explore_navigraph':
                _efb.cmdExploreNavigraph();
                break;
            case 'get_navdata_status':
                _efb.cmdGetNavdataStatus();
                break;
            case 'start_navdata_update':
                _efb.cmdStartNavdataUpdate();
                break;
            case 'get_page_text':
                var pageText = document.body ? document.body.innerText : '(no body)';
                _efb.postState('page_text', { text: pageText.substring(0, 2000) });
                break;
            case 'get_display_elements':
                _efb.cmdGetDisplayElements();
                break;
            case 'click_display_element':
                var clickIdx = parseInt((payload && payload.index) ? payload.index : '0');
                _efb.cmdClickDisplayElement(clickIdx);
                break;
            case 'get_page_html':
                _efb.cmdGetPageHtml();
                break;
            case 'click_by_path':
                _efb.cmdClickByPath((payload && payload.path) ? payload.path : '');
                break;
            case 'click_by_index':
                // Click an element in the real EFB page by its index
                var clickIdx2 = parseInt((payload && payload.idx) ? payload.idx : '-1');
                if (clickIdx2 >= 0) {
                    try {
                        var allClickEls = document.body.querySelectorAll('*');
                        if (clickIdx2 < allClickEls.length) {
                            allClickEls[clickIdx2].click();
                            // Auto-refresh display after click
                            setTimeout(function() { _efb.cmdGetPageHtml(); }, 500);
                        }
                    } catch(clickErr) {
                        _efb.postState('error', { message: 'Click failed: ' + clickErr.message });
                    }
                }
                break;
            case 'eval_js':
                // Hot-reload: execute arbitrary JS sent from C# — allows updating
                // bridge functions at runtime without restarting the simulator
                if (payload && payload.code) {
                    try {
                        var evalResult = eval(payload.code);
                        _efb.postState('eval_result', { result: String(evalResult) });
                    } catch(evalErr) {
                        _efb.postState('eval_result', { error: evalErr.message });
                    }
                }
                break;
            case 'click_by_id':
                // Targeted click on a DOM element by id. Uses the full
                // mousedown/mouseup/click sequence plus native .click() so
                // both plain DOM listeners and React's SyntheticEvent pipeline
                // see the interaction — bare .click() alone isn't enough for
                // PMDG's Import buttons on the Performance Tool pages.
                try {
                    var cbiId = payload && payload.id ? String(payload.id) : '';
                    var cbiEl = cbiId ? document.getElementById(cbiId) : null;
                    if (cbiEl) {
                        try {
                            cbiEl.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
                            cbiEl.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }));
                            cbiEl.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                        } catch (cbiMevt) { /* old Coherent GT — fall through to .click() */ }
                        try { cbiEl.click(); } catch (cbiClk) { /* event dispatch may already have fired */ }
                        _efb.postState('click_result', { id: cbiId, clicked: 'true' });
                    } else {
                        _efb.postState('click_result', { id: cbiId, clicked: 'false' });
                    }
                } catch (cbiErr) {
                    _efb.postState('error', { message: 'click_by_id: ' + cbiErr.message });
                }
                break;
            case 'wait_for_visible':
                // Poll until an element with the given id is visible (offsetParent
                // set), or timeoutMs elapses. Posts a ready state either way.
                try {
                    var wfvId = payload && payload.id ? String(payload.id) : '';
                    var wfvTimeout = parseInt((payload && payload.timeoutMs) ? payload.timeoutMs : '3000');
                    var wfvStart = Date.now();
                    var wfvCheck = function () {
                        var el = wfvId ? document.getElementById(wfvId) : null;
                        var visible = false;
                        if (el) {
                            try {
                                var cs = window.getComputedStyle(el);
                                visible = (el.offsetParent !== null) && cs.display !== 'none' && cs.visibility !== 'hidden';
                            } catch (e) { visible = el.offsetParent !== null; }
                        }
                        if (visible) {
                            _efb.postState('ready', { id: wfvId, found: 'true' });
                            return;
                        }
                        if (Date.now() - wfvStart >= wfvTimeout) {
                            _efb.postState('ready', { id: wfvId, found: 'false' });
                            return;
                        }
                        setTimeout(wfvCheck, 100);
                    };
                    wfvCheck();
                } catch (wfvErr) {
                    _efb.postState('error', { message: 'wait_for_visible: ' + wfvErr.message });
                }
                break;
            default:
                console.warn('[EFB Bridge] Unknown command:', command);
                _efb.postState('error', { message: 'Unknown bridge command: ' + command + ' (bridge may be stale; try Ctrl+Shift+R)' });
        }
    } catch (e) {
        console.error('[EFB Bridge] Command error:', e);
        _efb.postState('error', { message: 'Command failed: ' + e.message });
    }
};

_efb.cmdFetchSimbrief = function() {
    if (!_efb.publisher) return;
    _efb.publisher.pub('current_app', 'efb');
    _efb.publisher.pub('current_page', 'dashboard');
    // Click the fetch button on the dashboard
    var fetchBtn = document.getElementById('efb_dashboard_requestsimbrief');
    if (fetchBtn) fetchBtn.click();
};

// Shared transformer: SimBrief OFP JSON -> flat payload matching the
// DashboardPanel field set. Used by both the live simbrief_data subscription
// and the replay command.

// Format a duration in seconds as "HHhMMm". Returns '' if not a positive number.
_efb.formatDurationSeconds = function(secs) {
    var n = parseInt(secs, 10);
    if (!n || n <= 0) return '';
    var h = Math.floor(n / 3600);
    var m = Math.floor((n % 3600) / 60);
    return h + 'h' + (m < 10 ? '0' : '') + m + 'm';
};

// Read the tablet-rendered STD/STA elements directly. The tablet renders them
// as "HH:MM UTC / HH:MM UTC" (scheduled / estimated). Returns a two-entry
// object {planned, estimated}; entries are empty strings when the DOM element
// is missing or not populated yet.
_efb.readTabletDashboardTime = function(elementId) {
    var result = { planned: '', estimated: '' };
    try {
        var el = document.getElementById(elementId);
        if (!el) return result;
        var txt = (el.textContent || '').replace(/\u00a0/g, ' ').trim();
        if (!txt) return result;
        var parts = txt.split('/');
        if (parts.length >= 1) result.planned = parts[0].trim();
        if (parts.length >= 2) result.estimated = parts[1].trim();
    } catch (e) {
        console.error('[EFB Bridge] readTabletDashboardTime failed:', e);
    }
    return result;
};

_efb.buildSimbriefPayload = function(data) {
    if (!data || !data.origin || !data.destination || !data.atc) return null;
    var aircraft = data.aircraft || {};
    var general = data.general || {};
    var times = data.times || {};
    var regValue = aircraft.reg || aircraft.registration || '';
    var typeValue = aircraft.icao_code || aircraft.icaocode || aircraft.icao || aircraft.type || '';
    var coRte = general.route_navigraph || general.coroute || general.co_route || general.route || '';
    var routeDist = general.route_distance || general.total_distance || general.air_distance || general.gc_distance || '';
    var ofpUnits = (data.params && data.params.units) || '';
    // STD/STA times come straight from the tablet's own DOM elements — single
    // source of truth, already rendered in the exact format we want to show.
    var stdTimes = _efb.readTabletDashboardTime('efb_dashboard_std');
    var staTimes = _efb.readTabletDashboardTime('efb_dashboard_sta');
    var estEnroute = _efb.formatDurationSeconds(times.est_time_enroute || times.sched_time_enroute);
    return {
        callsign: data.atc.callsign || '',
        origin_icao: data.origin.icao_code || '',
        dest_icao: data.destination.icao_code || '',
        alt_icao: (data.alternate && data.alternate.icao_code) || '',
        cruise_alt: String(general.initial_altitude || ''),
        cost_index: String(general.costindex || ''),
        zfw: String((data.weights && data.weights.est_zfw) || ''),
        fuel_total: String((data.fuel && data.fuel.plan_ramp) || ''),
        avg_wind: String(general.avg_wind_dir || '') + '/' + String(general.avg_wind_spd || ''),
        aircraft_reg: String(regValue),
        aircraft_type: String(typeValue),
        co_route: String(coRte),
        route_dist: String(routeDist),
        ofp_weight_unit: String(ofpUnits),
        planned_departure: stdTimes.planned,
        estimated_departure: stdTimes.estimated,
        planned_arrival: staTimes.planned,
        estimated_arrival: staTimes.estimated,
        est_time_enroute: estEnroute
    };
};

// Batch DOM read. Payload: {tag: "navdata", ids: ["el_a","el_b",...]}.
// Posts back: {type: "values", data: {_tag: tag, el_a: text, el_b: text, ...}}.
// _tag lets multiple panels share the 'values' state channel without collisions.
// Inputs return .value; elements with aria-valuenow return that attribute
// (useful for progress bars); everything else returns trimmed textContent.
_efb.cmdReadValues = function(payload) {
    try {
        // C# passes ids as a comma-separated string because EnqueueCommand's
        // payload dict is Dictionary<string,string>. Accept either form so the
        // command also works when invoked directly via eval_js with an array.
        var raw = payload && payload.ids;
        var ids = Array.isArray(raw) ? raw : (raw ? String(raw).split(',') : []);
        var tag = (payload && payload.tag) || '';
        var values = { _tag: String(tag) };
        for (var i = 0; i < ids.length; i++) {
            var id = String(ids[i]);
            var el = document.getElementById(id);
            if (!el) { values[id] = ''; continue; }
            var tagName = el.tagName;
            if (tagName === 'INPUT' || tagName === 'SELECT' || tagName === 'TEXTAREA') {
                values[id] = String(el.value || '');
            } else if (el.className && typeof el.className === 'string' && el.className.indexOf('custom-select') >= 0) {
                // PMDG custom-select: prefer the .selected-option child's text.
                // Falls back to data-selected attribute if the child isn't there.
                var selOpt = el.querySelector('.selected-option');
                if (selOpt) {
                    values[id] = (selOpt.textContent || '').replace(/\u00a0/g, ' ').trim();
                } else {
                    values[id] = String(el.getAttribute('data-selected') || '');
                }
            } else if (el.hasAttribute && el.hasAttribute('aria-valuenow')) {
                values[id] = String(el.getAttribute('aria-valuenow') || '');
            } else {
                var txt = (el.textContent || '').replace(/\u00a0/g, ' ').trim();
                values[id] = txt;
            }
        }
        _efb.postState('values', values);
    } catch (e) {
        _efb.postState('error', { message: 'read_values failed: ' + e.message });
    }
};

_efb.cmdReplaySimbrief = function() {
    // Prefer a fresh rebuild from the tablet's live OFP global — this path
    // runs on user action (form open / tab switch) so the Dashboard DOM is
    // definitely rendered by now, meaning the DOM-sourced time fields will be
    // populated (unlike the EventBus path which fires before React paints).
    try {
        if (typeof Dashboard !== 'undefined' && Dashboard.simbrief) {
            var payload = _efb.buildSimbriefPayload(Dashboard.simbrief);
            if (payload) {
                _efb.lastSimbriefPayload = payload;
                _efb.postState('simbrief_loaded', payload);
                return;
            }
        }
    } catch (e) {
        console.error('[EFB Bridge] replay_simbrief rebuild failed:', e);
    }
    // Fallback: the in-session cache from a prior EventBus post.
    if (_efb.lastSimbriefPayload) {
        _efb.postState('simbrief_loaded', _efb.lastSimbriefPayload);
        return;
    }
    _efb.postState('simbrief_not_cached', {});
};

_efb.cmdSendToFMC = function() {
    if (typeof Dashboard === 'undefined' || !Dashboard.simbrief) {
        _efb.postState('error', { message: 'No SimBrief data loaded' });
        return;
    }

    if (_efb.publisher) {
        _efb.publisher.pub('current_app', 'efb');
        _efb.publisher.pub('current_page', 'dashboard');
    }

    var simbrief_data = Dashboard.simbrief;
    var simbrief_processed_data = {
        message_tag: "simbrief_data",
        data: {
            "dept_icao": simbrief_data.origin.icao_code ? simbrief_data.origin.icao_code.toString() : "",
            "dest_icao": simbrief_data.destination.icao_code ? simbrief_data.destination.icao_code.toString() : "",
            "altn_icao": simbrief_data.alternate.icao_code ? simbrief_data.alternate.icao_code.toString() : "",
            "flight_id": (simbrief_data.general.icao_airline + simbrief_data.general.flight_number) ? (simbrief_data.general.icao_airline + simbrief_data.general.flight_number).toString() : "",
            "trans_alt": Number(simbrief_data.origin.trans_alt),
            "trans_lvl": Number(simbrief_data.destination.trans_level),
            "cost_index": Number(simbrief_data.general.costindex),
            "crz_alt": Number(simbrief_data.general.initial_altitude),
            "units_kg": simbrief_data.params.units == "kgs" ? 1 : 0,
            "zero_fuelweight": Number(simbrief_data.weights.est_zfw),
            "total_fuelload": Number(simbrief_data.fuel.plan_ramp),
            "fuel_reserves": Number(simbrief_data.fuel.alternate_burn) + Number(simbrief_data.fuel.reserve),
            "ave_crzwndspd": Number(simbrief_data.general.avg_wind_spd),
            "ave_crzwndhdg": Number(simbrief_data.general.avg_wind_dir),
            "ave_crzisadev": Number(simbrief_data.general.avg_temp_dev)
        }
    };
    MessageService.postPlaneMessage(simbrief_processed_data);

    var route_file = simbrief_data.fms_downloads.pmr.link;
    var wx_file = simbrief_data.fms_downloads.pmw.link;
    MessageService.postPlaneMessage({
        message_tag: "route_file",
        data: { route_fn: route_file ? route_file.toString() : "" }
    });
    setTimeout(function() {
        MessageService.postPlaneMessage({
            message_tag: "wx_file",
            data: { wx_fn: wx_file ? wx_file.toString() : "" }
        });
    }, 500);

    _efb.postState('fmc_upload_started');
};

_efb.cmdStartNavigraphAuth = function() {
    if (!_efb.publisher) return;
    _efb.publisher.pub('current_app', 'efb');
    _efb.publisher.pub('current_page', 'authenticate');
    _efb.startNavigraphCodeObserver();
    _efb.startNavigraphAuthPoller();
};

_efb.cmdSignOutNavigraph = function() {
    if (typeof Navigraph !== 'undefined' && Navigraph.auth) {
        Navigraph.auth.signOut();
        _efb.postState('navigraph_auth_state', { authenticated: 'false', username: '' });
    }
};

_efb.sendCurrentNavigraphState = function() {
    if (_efb.navigraphStateSent) return;
    var delays = [0, 3000, 10000];
    var attemptIndex = 0;

    function attempt() {
        if (_efb.navigraphStateSent || !_efb.serverConnected) return;
        if (typeof Navigraph !== 'undefined' && Navigraph.auth && Navigraph.auth.getUser) {
            Navigraph.auth.getUser(true).then(function(user) {
                if (_efb.navigraphStateSent) return;
                if (user) {
                    _efb.navigraphStateSent = true;
                    _efb.postState('navigraph_auth_state', {
                        authenticated: 'true',
                        username: user.preferred_username || user.name || 'Unknown'
                    });
                } else if (attemptIndex >= delays.length - 1) {
                    _efb.navigraphStateSent = true;
                    _efb.postState('navigraph_auth_state', {
                        authenticated: 'false',
                        username: ''
                    });
                } else {
                    attemptIndex++;
                    setTimeout(attempt, delays[attemptIndex]);
                }
            }).catch(function(e) {
                console.error('[EFB Bridge] Error getting Navigraph user:', e);
                if (attemptIndex < delays.length - 1) {
                    attemptIndex++;
                    setTimeout(attempt, delays[attemptIndex]);
                }
            });
        } else if (attemptIndex < delays.length - 1) {
            attemptIndex++;
            setTimeout(attempt, delays[attemptIndex]);
        }
    }

    attempt();
};

// Write a text input by simulating actual typing — focus the element, then
// for each character: keydown → native setValue with progressive substring →
// InputEvent → keyup. PMDG's pmdg_measurement fields seem to require this
// per-character keyboard event sequence; bulk setValue + dispatch input gets
// silently rejected by their validator.
_efb.cmdSetInputById = function(payload) {
    try {
        var id = payload && payload.id ? String(payload.id) : '';
        var val = payload && payload.value !== undefined ? String(payload.value) : '';
        if (!id) return;
        var el = document.getElementById(id);
        if (!el) {
            _efb.postState('error', { message: 'set_input_by_id: not found: ' + id });
            return;
        }

        var proto = (el.tagName === 'TEXTAREA')
            ? window.HTMLTextAreaElement.prototype
            : window.HTMLInputElement.prototype;
        var nativeSetter = null;
        try {
            nativeSetter = Object.getOwnPropertyDescriptor(proto, 'value').set;
        } catch (descErr) { nativeSetter = null; }

        // Set the .value property using the native setter, bypassing React's
        // own setter. Updates the DOM but doesn't fire any events on its own.
        var rawSet = function(v) {
            if (nativeSetter) {
                try { nativeSetter.call(el, v); return; } catch (e) {}
            }
            try { el.value = v; } catch (e2) {}
        };

        // Fire an InputEvent (React listens for this); fall back to Event.
        var fireInput = function(ch, type) {
            try {
                el.dispatchEvent(new InputEvent('input', {
                    bubbles: true, cancelable: true,
                    data: ch || null,
                    inputType: type || 'insertText'
                }));
                return;
            } catch (ieErr) { /* fall through */ }
            try { el.dispatchEvent(new Event('input', { bubbles: true })); } catch (e) {}
        };

        var fireKey = function(type, key) {
            try {
                el.dispatchEvent(new KeyboardEvent(type, {
                    bubbles: true, cancelable: true,
                    key: key, code: key.length === 1 ? 'Key' + key.toUpperCase() : key
                }));
            } catch (kErr) { /* old Coherent GT may not have KeyboardEvent */ }
        };

        try { el.focus(); } catch (fe) {}

        // 1. Clear via tracker invalidation + raw set + delete event so
        // anything previously in the field is cleared first.
        try {
            if (el._valueTracker && typeof el._valueTracker.setValue === 'function') {
                el._valueTracker.setValue('__force_diff__');
            }
        } catch (e) {}
        rawSet('');
        fireInput('', 'deleteContentBackward');

        // 2. Type each character with a full keyboard event cycle. The
        // tracker invalidation per-character lets React see the value change
        // every time.
        for (var i = 0; i < val.length; i++) {
            var ch = val.charAt(i);
            var partial = val.substring(0, i + 1);
            fireKey('keydown', ch);
            fireKey('keypress', ch);
            try {
                if (el._valueTracker && typeof el._valueTracker.setValue === 'function') {
                    el._valueTracker.setValue(val.substring(0, i));
                }
            } catch (e) {}
            rawSet(partial);
            fireInput(ch, 'insertText');
            fireKey('keyup', ch);
        }

        // 3. Commit events so any blur/change validators fire.
        try { el.dispatchEvent(new Event('change', { bubbles: true })); } catch (e) {}
        fireKey('keydown', 'Enter');
        fireKey('keypress', 'Enter');
        fireKey('keyup', 'Enter');
        try { el.blur(); } catch (be) {}
    } catch (e) {
        _efb.postState('error', { message: 'set_input_by_id failed: ' + e.message });
    }
};

// Dump a structural skeleton of a DOM element. Posts an 'element_dump' state
// with {id, tag, class, computed_display, outerHtml_preview, and up to
// 40 lines of "depth/tag/class/text" per descendant}. Used by Ctrl+Shift+E to
// discover runtime DOM structures we can't see in static captures.
// Dump the full outerHTML of a DOM element, chunked via the existing
// page_html_chunk mechanism so the C# side reassembles and saves to
// %TEMP%\efb_captured_HHMMSS.html. The caller gets a snapshot of exactly
// what PMDG has mounted at runtime — far more useful than a partial tree walk.
_efb.cmdDumpElement = function(payload) {
    var safeId = '?';
    try {
        var id = payload && payload.id ? String(payload.id) : '';
        safeId = id || '?';
        if (!id) {
            _efb.postState('element_dump', { id: '', error: 'no id in payload' });
            return;
        }
        var el = document.getElementById(id);
        if (!el) {
            _efb.postState('element_dump', { id: id, error: 'not found' });
            return;
        }

        // Prefer the actual outerHTML of the live element — this is the
        // ground truth of what PMDG's React/Coherent GT has rendered right
        // now, including any runtime-populated options.
        var html = '';
        try { html = el.outerHTML || ''; } catch (hErr) { html = '<!-- outerHTML failed: ' + hErr.message + ' -->'; }

        if (!html) {
            _efb.postState('element_dump', { id: id, error: 'outerHTML was empty' });
            return;
        }

        // Wrap in a minimal document so it opens standalone in the WebView if
        // the user wants to inspect it there. This matches cmdGetPageHtml's
        // structure so HandlePageHtmlChunk can save it exactly the same way.
        var wrapped = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Element dump: '
            + id + '</title></head><body>' + html + '</body></html>';

        console.log('[EFB Bridge] element_dump (' + id + ') size: ' + wrapped.length);

        // Reuse the page_html_chunk path so C# auto-saves to efb_captured_*.html.
        // 30KB chunks per HandleGetCommands size budget.
        var CHUNK_SIZE = 30000;
        var totalChunks = Math.ceil(wrapped.length / CHUNK_SIZE);
        var transferId = 'elem-' + id + '-' + Date.now().toString(36);
        for (var ci = 0; ci < totalChunks; ci++) {
            var ch = wrapped.substring(ci * CHUNK_SIZE, (ci + 1) * CHUNK_SIZE);
            _efb.postState('page_html_chunk', {
                id: transferId,
                index: String(ci),
                total: String(totalChunks),
                chunk: ch
            });
        }
        // Also post a small element_dump ack so the C# timeout doesn't fire.
        _efb.postState('element_dump', {
            id: id,
            info: 'saved as page_html_chunk, ' + totalChunks + ' chunks, ' + wrapped.length + ' chars'
        });
        return;
    } catch (e) {
        _efb.postState('element_dump', { id: safeId, error: 'exception: ' + (e && e.message ? e.message : String(e)) });
        return;
    }
};

// --- legacy cmdDumpElement tree walk path below is unused; kept for the
// one-off error/short paths above. Old tree logic removed so outerHTML is
// the only live return. ---
_efb._cmdDumpElement_unused = function(payload) {
    var safeId = '?';
    try {
        var id = payload && payload.id ? String(payload.id) : '';
        safeId = id || '?';
        if (!id) {
            _efb.postState('element_dump', { id: '', error: 'no id in payload' });
            return;
        }
        var el = document.getElementById(id);
        if (!el) {
            _efb.postState('element_dump', { id: id, error: 'not found' });
            return;
        }
        var info = { id: id };
        info.tag = String(el.tagName || '');
        try { info['class'] = String(el.className || ''); } catch (classErr) { info['class'] = '(err)'; }
        try { info.computed_display = String(window.getComputedStyle(el).display); } catch (csErr) { info.computed_display = '?'; }

        // Attrs (flatten to one key per attr)
        try {
            for (var ai = 0; ai < el.attributes.length; ai++) {
                info['attr_' + el.attributes[ai].name] = String(el.attributes[ai].value);
            }
        } catch (attrErr) { info.attr_err = attrErr.message; }

        // Walk descendants up to depth 4, one key per line
        var lineIndex = 0;
        var walk = function(node, depth) {
            if (lineIndex >= 60) return;
            if (!node || depth > 4) return;
            var prefix = '';
            for (var p = 0; p < depth; p++) prefix += '  ';
            var childTag = String(node.tagName || '#text');
            var childClass = '';
            try {
                if (node.className && typeof node.className === 'string') childClass = node.className;
            } catch (clsErr) { childClass = ''; }
            var childId = String(node.id || '');
            var dv = '';
            var ds = '';
            try {
                if (node.getAttribute) {
                    dv = String(node.getAttribute('data-value') || '');
                    ds = String(node.getAttribute('data-selected') || '');
                }
            } catch (attrErr2) { /* ignore */ }
            var ownText = '';
            try {
                if (node.childNodes) {
                    for (var c = 0; c < node.childNodes.length; c++) {
                        var cn = node.childNodes[c];
                        if (cn.nodeType === 3) ownText += (cn.textContent || '');
                    }
                }
            } catch (txtErr) { /* ignore */ }
            ownText = ownText.replace(/\s+/g, ' ').trim();
            if (ownText.length > 80) ownText = ownText.substring(0, 80);

            var line = prefix + childTag;
            if (childId) line += ' #' + childId;
            if (childClass) line += ' .' + childClass.replace(/\s+/g, '.');
            if (dv) line += ' [dv=' + dv + ']';
            if (ds) line += ' [ds=' + ds + ']';
            if (ownText) line += ' "' + ownText + '"';

            // One key per line — safer than packing newlines into a single value
            info['line_' + ('00' + lineIndex).slice(-2)] = line;
            lineIndex++;

            try {
                if (node.children) {
                    for (var i = 0; i < node.children.length && lineIndex < 60; i++) {
                        walk(node.children[i], depth + 1);
                    }
                }
            } catch (walkErr) { /* ignore */ }
        };
        walk(el, 0);
        info.line_count = String(lineIndex);
        _efb.postState('element_dump', info);
    } catch (e) {
        // Even on total failure, post something so the C# timeout doesn't fire
        _efb.postState('element_dump', { id: safeId, error: 'exception: ' + (e && e.message ? e.message : String(e)) });
    }
};

// Enumerate a PMDG custom-select's options. Posts a 'select_options' state
// with {_tag, _id, count, option_N_value, option_N_text, selected_value,
// selected_text}. Used by TakeoffPanel to populate the runway combo after
// PMDG fills its own dropdown in response to an ICAO entry.
_efb.cmdGetSelectOptions = function(payload) {
    try {
        var id = payload && payload.id ? String(payload.id) : '';
        var tag = payload && payload.tag ? String(payload.tag) : '';
        if (!id) return;
        var el = document.getElementById(id);
        if (!el) {
            _efb.postState('select_options', { _tag: tag, _id: id, count: '0', _error: 'not found' });
            return;
        }
        var opts = el.querySelectorAll('.option');
        var out = { _tag: tag, _id: id, count: String(opts.length) };
        for (var i = 0; i < opts.length; i++) {
            var dv = opts[i].getAttribute('data-value') || '';
            var tx = (opts[i].textContent || '').replace(/\u00a0/g, ' ').trim();
            out['option_' + i + '_value'] = String(dv);
            out['option_' + i + '_text'] = String(tx);
        }
        // Also include the current .selected-option text so the caller can
        // restore focus to whichever option was already active.
        var selOpt = el.querySelector('.selected-option');
        out['selected_text'] = selOpt ? (selOpt.textContent || '').replace(/\u00a0/g, ' ').trim() : '';
        out['selected_value'] = String(el.getAttribute('data-selected') || '');
        _efb.postState('select_options', out);
    } catch (e) {
        _efb.postState('error', { message: 'get_select_options failed: ' + e.message });
    }
};

// Set a PMDG custom-select. PMDG's select components can have options that
// aren't mounted or reactive until the select is opened, and their click
// handlers sometimes want a full mousedown/mouseup/click sequence rather
// than the bare .click() call. This routine:
//   1. Finds the target option by data-value, then by text, across all
//      descendants (not just direct .option children).
//   2. If not found, opens the select (clicks its root) and retries after
//      a short tick.
//   3. Fires mousedown + mouseup + click + native .click() on the option
//      so both DOM-event listeners and React's SyntheticEvent pipeline see
//      the interaction.
_efb.cmdSetSelectById = function(payload) {
    try {
        var id = payload && payload.id ? String(payload.id) : '';
        var val = payload && payload.value !== undefined ? String(payload.value) : '';
        if (!id) return;
        var el = document.getElementById(id);
        if (!el) {
            _efb.postState('error', { message: 'set_select_by_id: not found: ' + id });
            return;
        }

        var fireClick = function(target) {
            if (!target) return false;
            try {
                target.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
                target.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }));
                target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
            } catch (mevtErr) { /* old Coherent GT may not like MouseEvent */ }
            try { target.click(); } catch (clkErr) { /* already dispatched */ }
            return true;
        };

        // PMDG hides the options container with `style="visibility: hidden"`
        // when the dropdown is collapsed. Browsers won't dispatch click events
        // on elements with hidden visibility. Temporarily force-show the
        // container so the option click actually lands. PMDG's own click
        // handler will re-hide it after selection.
        var optionsContainer = el.querySelector('.options');
        if (optionsContainer && optionsContainer.style) {
            try { optionsContainer.style.visibility = 'visible'; } catch (visErr) {}
        }

        var find = function() {
            // 1. Exact data-value match on any descendant
            var byAttr = el.querySelectorAll('[data-value="' + val.replace(/"/g, '\\"') + '"]');
            if (byAttr.length > 0) return byAttr[0];
            // 2. .option children with matching text (case-insensitive)
            var lower = val.toLowerCase();
            var opts = el.querySelectorAll('.option');
            for (var i = 0; i < opts.length; i++) {
                if ((opts[i].textContent || '').trim().toLowerCase() === lower) return opts[i];
            }
            // 3. Any leaf descendant with matching text
            var all = el.querySelectorAll('div, span, li, button');
            for (var j = 0; j < all.length; j++) {
                var node = all[j];
                if (!node.children || node.children.length > 0) continue;
                if ((node.textContent || '').trim().toLowerCase() === lower) return node;
            }
            return null;
        };

        var hit = find();
        if (hit) {
            fireClick(hit);
            return;
        }

        // Not found — maybe the select needs to be opened first. Click the
        // root (selected-option child is the usual trigger) then retry.
        var opener = el.querySelector('.selected-option') || el;
        fireClick(opener);
        setTimeout(function() {
            var retry = find();
            if (retry) {
                fireClick(retry);
            } else {
                _efb.postState('error', {
                    message: 'set_select_by_id: no matching option "' + val + '" in #' + id
                });
            }
        }, 150);
    } catch (e) {
        _efb.postState('error', { message: 'set_select_by_id failed: ' + e.message });
    }
};

// Diagnostic: dump every known preference key with its raw value and JS type,
// plus the current DOM state of each toggle checkbox (checked/value). Used by
// the C# Ctrl+Shift+D hotkey to debug mismatches between PMDG's stored value
// and what our combo boxes expect.
_efb.cmdDumpPreferences = function() {
    var out = {};
    var keys = [
        'simbrief_id','hoppie_id','sayintentions_id',
        'weather_source','atc_network',
        'weight_unit','distance_unit','altitude_unit','temperature_unit',
        'length_unit','speed_unit','airspeed_unit','pressure_unit',
        'on_screen_keyboard','theme_setting','start_screen_on'
    ];
    for (var i = 0; i < keys.length; i++) {
        var k = keys[i];
        var v;
        try {
            v = (typeof Settings !== 'undefined') ? Settings[k] : undefined;
        } catch (e) {
            v = '<getter threw ' + e.message + '>';
        }
        out[k] = (typeof v) + ':' + String(v);
        // If the corresponding DOM element is a checkbox, append its .checked
        var el = document.getElementById('efb_preferences_' + k);
        if (el && el.tagName === 'INPUT' && el.type === 'checkbox') {
            out[k] += ' [checkbox.checked=' + el.checked + ']';
            // Read the two CSS pseudo-element labels the toggle renders on
            // each side. PMDG uses ::before and ::after to paint the unit
            // text via content: "...". Strip outer quotes for readability.
            try {
                var before = window.getComputedStyle(el, '::before').content || '';
                var after = window.getComputedStyle(el, '::after').content || '';
                var strip = function(s) { return s.replace(/^"|"$/g, '').replace(/^'|'$/g, ''); };
                out[k] += ' [::before=' + strip(before) + '] [::after=' + strip(after) + ']';
            } catch (cssErr) {
                out[k] += ' [css: ' + cssErr.message + ']';
            }
        } else if (el && el.hasAttribute && el.hasAttribute('data-selected')) {
            out[k] += ' [data-selected=' + el.getAttribute('data-selected') + ']';
        } else if (el && el.value !== undefined) {
            out[k] += ' [input.value=' + el.value + ']';
        }
    }
    // Also list every key on the Settings object so we can find fields PMDG
    // stores under names we didn't expect (e.g. ground speed appears to live
    // somewhere other than speed_unit).
    try {
        if (typeof Settings !== 'undefined' && Settings) {
            var all = [];
            for (var prop in Settings) {
                if (typeof Settings[prop] === 'function') continue;
                all.push(prop + '=' + (typeof Settings[prop]) + ':' + String(Settings[prop]).substring(0, 60));
            }
            out['__ALL_SETTINGS_KEYS__'] = all.join(' | ');
        }
    } catch (e) {
        out['__ALL_SETTINGS_KEYS__'] = 'enum threw: ' + e.message;
    }

    // Deep-dump the unit_set and default_units objects — the individual
    // Settings.weight_unit etc. seem to sometimes disagree with what the
    // tablet renders, so the real state may live inside these.
    var dumpObj = function(label, obj) {
        try {
            if (!obj || typeof obj !== 'object') { out[label] = '(not an object)'; return; }
            var parts = [];
            for (var p in obj) {
                var v = obj[p];
                if (typeof v === 'function') continue;
                if (v && typeof v === 'object') {
                    var sub = [];
                    for (var q in v) {
                        if (typeof v[q] === 'function') continue;
                        sub.push(q + '=' + String(v[q]).substring(0, 40));
                    }
                    parts.push(p + ':{' + sub.join(', ') + '}');
                } else {
                    parts.push(p + '=' + (typeof v) + ':' + String(v).substring(0, 40));
                }
            }
            out[label] = parts.join(' | ');
        } catch (dumpErr) {
            out[label] = 'dump threw: ' + dumpErr.message;
        }
    };
    try {
        if (typeof Settings !== 'undefined' && Settings) {
            dumpObj('__UNIT_SET__', Settings.unit_set);
            dumpObj('__DEFAULT_UNITS__', Settings.default_units);
        }
    } catch (e) { /* ignore */ }

    _efb.postState('preferences_debug', out);
};

_efb.cmdGetPreferences = function() {
    var prefs = {};
    var keys = [
        'simbrief_id', 'hoppie_id', 'sayintentions_id',
        'weather_source', 'atc_network',
        'weight_unit', 'distance_unit', 'altitude_unit', 'temperature_unit',
        'length_unit', 'speed_unit', 'airspeed_unit', 'pressure_unit',
        'on_screen_keyboard', 'theme_setting', 'start_screen_on'
    ];
    for (var i = 0; i < keys.length; i++) {
        if (typeof Settings !== 'undefined' && Settings[keys[i]] !== undefined) {
            prefs[keys[i]] = String(Settings[keys[i]]);
        }
    }
    _efb.postState('preferences', prefs);
};

// Normalize a unit string for robust comparison — lowercase, trim, strip
// trailing 's' so "kts"/"kt" and "lbs"/"lb" both match.
_efb._normUnit = function(s) {
    s = String(s || '').trim().toLowerCase();
    if (s.length > 1 && s.charAt(s.length - 1) === 's') s = s.substring(0, s.length - 1);
    return s;
};

_efb.cmdSetPreference = function(key, value) {
    if (typeof Settings === 'undefined' || !Settings.updateSetting) {
        _efb.postState('error', { message: 'EFB Settings not available — preferences cannot be saved' });
        return;
    }

    var el = document.getElementById('efb_preferences_' + key);

    // Checkbox toggle path: click the element if Settings[key] doesn't
    // already match the target. Clicking triggers PMDG's own handler which
    // updates Settings AND re-renders dependent unit spans across all pages
    // (Takeoff, Dashboard, etc.) — the exact re-render path we need.
    if (el && el.tagName === 'INPUT' && el.type === 'checkbox') {
        var currentNorm = _efb._normUnit(Settings[key]);
        var targetNorm = _efb._normUnit(value);
        if (currentNorm === targetNorm) {
            // Already in target state — no click, no write.
            return;
        }
        // Flip via a realistic click sequence so both plain DOM listeners and
        // React's SyntheticEvent pipeline see the interaction.
        try {
            el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
            el.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }));
            el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
        } catch (mEvtErr) { /* Coherent GT MouseEvent ctor missing */ }
        try { el.click(); } catch (clkErr) { /* fallback */ }
        // Belt-and-braces: direct Settings writes in case the click handler
        // didn't fire (e.g. element hidden / React not mounted on this page).
        try { Settings.updateSetting(key, value); } catch (setErr) {}
        try {
            if (Settings.unit_set) {
                var shortKey = key.substring(0, key.length - 5);
                Settings.unit_set[shortKey] = value;
            }
        } catch (usErr) {}
        return;
    }

    // Custom-select path (atc_network, theme, weather_source, etc.) —
    // route through the same robust set_select_by_id flow that actually
    // clicks the matching option element, force-shows the .options
    // container if needed, and dispatches a real mousedown/mouseup/click
    // sequence so PMDG's React handler fires and updates internal state.
    // Just setting data-selected and rewriting .selected-option's text is
    // cosmetic — PMDG's React state stays stale and import flows that read
    // that state (e.g. weather source) silently use the previous value.
    if (el && el.className && typeof el.className === 'string'
        && el.className.indexOf('custom-select') >= 0) {
        _efb.cmdSetSelectById({ id: 'efb_preferences_' + key, value: value });
        // Settings.updateSetting as belt-and-braces in case the click
        // didn't propagate (hidden DOM, React not mounted, etc.).
        try { Settings.updateSetting(key, value); } catch (setErr) {}
        return;
    }

    // Text input path (simbrief_id, hoppie_id, etc.) or unknown element:
    // fall back to updateSetting which persists to DataStore.
    Settings.updateSetting(key, value);
};

_efb.cmdSavePreferences = function() {
    // All preferences were already persisted via Settings.updateSetting() in set_preference.
    // Auto-dismiss any alert the EFB might show after settings changes.
    _efb.dismissAlertAfterDelay();
    // Send back the current state so the C# form can confirm.
    _efb.cmdGetPreferences();
};

_efb.dismissAlertAfterDelay = function() {
    // The EFB may show a PMDGAlert after certain operations. Poll briefly to dismiss it.
    var attempts = 0;
    var timer = setInterval(function() {
        var alertDiv = document.getElementById('PMDGAlert');
        if (alertDiv && alertDiv.style.display === 'block') {
            var okBtn = document.getElementById('alert_card_button');
            if (okBtn) okBtn.click();
            clearInterval(timer);
            return;
        }
        attempts++;
        if (attempts >= 6) clearInterval(timer); // Stop after 3 seconds
    }, 500);
};

// --- Navigraph Auth Code Observer ---

_efb.navigraphCodeTimer = null;

_efb.startNavigraphCodeObserver = function() {
    _efb.stopNavigraphCodeObserver();

    _efb.navigraphCodeTimer = setInterval(function() {
        var codeEl = document.getElementById('navigraph_code');
        if (codeEl) {
            var code = codeEl.textContent.trim();
            if (code && code !== '\u00A0' && code !== '&nbsp') {
                clearInterval(_efb.navigraphCodeTimer);
                _efb.navigraphCodeTimer = null;
                _efb.postState('navigraph_code', {
                    code: code,
                    url: 'https://navigraph.com/code'
                });
            }
        }
    }, 500);

    setTimeout(function() {
        if (_efb.navigraphCodeTimer) {
            clearInterval(_efb.navigraphCodeTimer);
            _efb.navigraphCodeTimer = null;
        }
    }, 60000);
};

_efb.stopNavigraphCodeObserver = function() {
    if (_efb.navigraphCodeTimer) {
        clearInterval(_efb.navigraphCodeTimer);
        _efb.navigraphCodeTimer = null;
    }
};

// Active check for current Navigraph auth state. Called on bridge-connect
// and on Prefs tab activation so the UI reflects reality even when the user
// was already signed in when MSFSBA launched. Without this, our UI only sees
// auth state after explicit sign-in/out actions (onAuthStateChanged is
// unreliable for the "already authenticated at load" case).
_efb.cmdCheckNavigraphAuth = function() {
    try {
        if (typeof Navigraph === 'undefined' || !Navigraph.auth || !Navigraph.auth.getUser) {
            _efb.postState('navigraph_auth_state', { authenticated: 'false', username: '' });
            return;
        }
        Navigraph.auth.getUser(true).then(function(user) {
            if (user) {
                _efb.postState('navigraph_auth_state', {
                    authenticated: 'true',
                    username: user.preferred_username || user.name || 'Unknown'
                });
            } else {
                _efb.postState('navigraph_auth_state', { authenticated: 'false', username: '' });
            }
        }).catch(function(e) {
            _efb.postState('navigraph_auth_state', { authenticated: 'false', username: '' });
        });
    } catch (e) {
        _efb.postState('error', { message: 'check_navigraph_auth failed: ' + e.message });
    }
};

// Poll for auth completion after starting the device flow.
// This catches the auth state change even if onAuthStateChanged doesn't fire reliably.
// Also auto-dismisses the EFB's success alert popup.
_efb.navigraphAuthPoller = null;

_efb.startNavigraphAuthPoller = function() {
    _efb.stopNavigraphAuthPoller();
    _efb.navigraphAuthPoller = setInterval(function() {
        if (typeof Navigraph === 'undefined' || !Navigraph.auth || !Navigraph.auth.getUser) return;
        Navigraph.auth.getUser(true).then(function(user) {
            if (user) {
                _efb.postState('navigraph_auth_state', {
                    authenticated: 'true',
                    username: user.preferred_username || user.name || 'Unknown'
                });
                _efb.stopNavigraphAuthPoller();
                // Auto-dismiss the EFB's success alert if visible
                _efb.dismissAlertAfterDelay();
            }
        }).catch(function() {});
    }, 2000);

    // Stop polling after 5 minutes
    setTimeout(function() { _efb.stopNavigraphAuthPoller(); }, 300000);
};

_efb.stopNavigraphAuthPoller = function() {
    if (_efb.navigraphAuthPoller) {
        clearInterval(_efb.navigraphAuthPoller);
        _efb.navigraphAuthPoller = null;
    }
};

// --- Navigraph Navdata Management ---

// Explore what Navigraph APIs are available (discovery command)
_efb.cmdExploreNavigraph = function() {
    var info = {
        hasNavigraph: typeof Navigraph !== 'undefined',
        hasAuth: false,
        hasPackages: false,
        hasCharts: false,
        topLevelKeys: [],
        packageMethods: [],
        authMethods: []
    };

    if (typeof Navigraph !== 'undefined') {
        info.topLevelKeys = Object.getOwnPropertyNames(Navigraph);
        info.hasAuth = typeof Navigraph.auth !== 'undefined';
        info.hasPackages = typeof Navigraph.packages !== 'undefined';
        info.hasCharts = typeof Navigraph.charts !== 'undefined';

        if (Navigraph.auth) {
            try { info.authMethods = Object.getOwnPropertyNames(Navigraph.auth); } catch(e) {}
        }
        if (Navigraph.packages) {
            try { info.packageMethods = Object.getOwnPropertyNames(Navigraph.packages); } catch(e) {}
        }

        // Also check for NavigraphNavdata or similar top-level objects
        var globals = ['NavigraphNavdata', 'NavigraphPackages', 'navdata', 'NavdataManager'];
        for (var i = 0; i < globals.length; i++) {
            if (typeof window[globals[i]] !== 'undefined') {
                info['found_' + globals[i]] = Object.getOwnPropertyNames(window[globals[i]]);
            }
        }
    }

    // Check for navdata-related DOM elements in the EFB
    var navdataElements = document.querySelectorAll('[class*="navdata"], [class*="airac"], [id*="navdata"], [id*="airac"], [class*="update"], [data-page*="nav"]');
    info.navdataElementCount = navdataElements.length;
    if (navdataElements.length > 0) {
        info.navdataElementSample = [];
        for (var j = 0; j < Math.min(5, navdataElements.length); j++) {
            info.navdataElementSample.push({
                tag: navdataElements[j].tagName,
                id: navdataElements[j].id || '',
                className: navdataElements[j].className || '',
                text: (navdataElements[j].textContent || '').substring(0, 100)
            });
        }
    }

    console.log('[EFB Bridge] Navigraph exploration:', JSON.stringify(info, null, 2));
    _efb.postState('navigraph_exploration', info);
};

// Get current navdata/AIRAC cycle status
_efb.cmdGetNavdataStatus = function() {
    if (typeof Navigraph === 'undefined') {
        _efb.postState('navdata_status', { error: 'Navigraph SDK not available' });
        return;
    }

    if (!Navigraph.packages) {
        _efb.postState('navdata_status', { error: 'Navigraph packages API not available' });
        return;
    }

    try {
        var pkgApi = Navigraph.packages;
        var result = {
            availableMethods: Object.getOwnPropertyNames(pkgApi).join(', ')
        };

        // Get available packages and try getPackage for detailed info
        pkgApi.listPackages().then(function(pkgs) {
            result.available_packages = JSON.stringify(pkgs);
            if (pkgs && pkgs.length > 0) {
                result.available_cycle = pkgs[0].cycle;
                result.package_count = '' + pkgs.length;

                // Count unique formats
                var formats = {};
                for (var i = 0; i < pkgs.length; i++) {
                    formats[pkgs[i].format] = true;
                }
                result.formats = Object.keys(formats).join(', ');

                // Check installation state per format using getPackage
                // "No packages found" = not installed, data returned = installed
                if (typeof pkgApi.getPackage === 'function') {
                    var seenFormats = {};
                    var installed = [];
                    var notInstalled = [];
                    var detailPromises = [];

                    for (var j = 0; j < pkgs.length; j++) {
                        var fmt = pkgs[j].format;
                        if (!seenFormats[fmt]) {
                            seenFormats[fmt] = true;
                            (function(pkg) {
                                detailPromises.push(
                                    pkgApi.getPackage(pkg.id).then(function(detail) {
                                        installed.push(pkg.format);
                                    }).catch(function(e) {
                                        notInstalled.push(pkg.format);
                                    })
                                );
                            })(pkgs[j]);
                        }
                    }
                    return Promise.all(detailPromises).then(function() {
                        result.installed_formats = installed.join(', ') || 'none';
                        result.not_installed_formats = notInstalled.join(', ') || 'none';
                        result.all_installed = installed.length > 0 && notInstalled.length === 0 ? 'true' : 'false';
                        result.needs_update = notInstalled.length > 0 ? 'true' : 'false';
                    });
                }
            }
        }).then(function() {
            _efb.postState('navdata_status', result);
        }).catch(function(e) {
            result.error = e.message;
            _efb.postState('navdata_status', result);
        });

    } catch(e) {
        _efb.postState('navdata_status', { error: e.message });
    }
};

// Scrape navdata/AIRAC information from the EFB's DOM
_efb.scrapeNavdataFromDOM = function() {
    var info = { source: 'dom_scrape' };

    // Look for AIRAC cycle text anywhere in the page
    var allText = document.body ? document.body.innerText : '';
    var airacMatch = allText.match(/AIRAC\s*(\d{4})/i);
    if (airacMatch) {
        info.airac_cycle = airacMatch[1];
    }

    // Look for "update" buttons related to navdata
    var buttons = document.querySelectorAll('button, [role="button"]');
    for (var i = 0; i < buttons.length; i++) {
        var txt = (buttons[i].textContent || '').toLowerCase();
        if (txt.indexOf('update') >= 0 && (txt.indexOf('nav') >= 0 || txt.indexOf('airac') >= 0 || txt.indexOf('data') >= 0)) {
            info.updateButton = { text: buttons[i].textContent.trim(), id: buttons[i].id || '', className: buttons[i].className || '' };
        }
    }

    return info;
};

// Start navdata update — navigate to the EFB's Navigraph page and trigger update
_efb.cmdStartNavdataUpdate = function() {
    if (typeof Navigraph === 'undefined' || !Navigraph.packages) {
        _efb.postState('navdata_error', { message: 'Navigraph packages API not available' });
        return;
    }

    _efb.postState('navdata_progress', { status: 'starting', percent: 0 });

    try {
        var pkgApi = Navigraph.packages;

        // First get the package list to find download URLs
        pkgApi.listPackages().then(function(pkgs) {
            if (!pkgs || pkgs.length === 0) {
                _efb.postState('navdata_error', { message: 'No packages available' });
                return;
            }

            // Filter to packages relevant to installed variants (workfolder + hub formats)
            var toInstall = [];
            for (var i = 0; i < pkgs.length; i++) {
                var fmt = pkgs[i].format;
                // Include hub packages for installed variants and workfolder packages
                if (fmt.indexOf('hub') >= 0 || fmt === 'pmdg_v3') {
                    toInstall.push(pkgs[i]);
                }
            }

            // Also include one workfolder package (they all have the same hash/file)
            var addedWorkfolder = false;
            for (var j = 0; j < pkgs.length; j++) {
                if (pkgs[j].format === 'pmdg_workfolder_v1' && !addedWorkfolder) {
                    toInstall.push(pkgs[j]);
                    addedWorkfolder = true;
                }
            }

            if (toInstall.length === 0) toInstall = pkgs;

            _efb.postState('navdata_progress', { status: 'installing', percent: 10 });

            // Try to use getPackage to trigger installation for each package
            // The getPackage method might download/install when called on an uninstalled package
            var installPromises = [];
            var completed = 0;
            var total = toInstall.length;

            for (var k = 0; k < toInstall.length; k++) {
                (function(pkg, idx) {
                    installPromises.push(
                        pkgApi.getPackage(pkg.id).then(function(result) {
                            completed++;
                            var pct = Math.round(10 + (completed / total) * 80);
                            _efb.postState('navdata_progress', { status: 'installing ' + pkg.format, percent: pct });
                            return { format: pkg.format, status: 'ok', result: JSON.stringify(result) };
                        }).catch(function(e) {
                            completed++;
                            return { format: pkg.format, status: 'error', message: e.message };
                        })
                    );
                })(toInstall[k], k);
            }

            Promise.all(installPromises).then(function(results) {
                var succeeded = results.filter(function(r) { return r.status === 'ok'; });
                var failed = results.filter(function(r) { return r.status === 'error'; });

                if (succeeded.length > 0) {
                    _efb.postState('navdata_complete', {
                        success: true,
                        message: succeeded.length + ' packages installed, ' + failed.length + ' failed'
                    });
                } else {
                    // getPackage doesn't trigger installation — try DOM approach
                    _efb.postState('navdata_progress', { status: 'trying EFB UI approach', percent: 50 });
                    _efb.tryNavigraphPageUpdate();
                }
            });
        }).catch(function(e) {
            _efb.postState('navdata_error', { message: 'Failed to list packages: ' + e.message });
        });
    } catch(e) {
        _efb.postState('navdata_error', { message: 'Update error: ' + e.message });
    }
};

// Helper: find a clickable element containing specific text
_efb.findClickableByText = function(searchText) {
    searchText = searchText.toLowerCase();
    // Search buttons, links, divs, spans — anything clickable
    var selectors = 'button, [role="button"], a, [onclick], .clickable, div, span, li';
    var elements = document.querySelectorAll(selectors);
    for (var i = 0; i < elements.length; i++) {
        var el = elements[i];
        var txt = (el.textContent || '').trim().toLowerCase();
        // Match if element text IS the search text (not just contains, to avoid parent matches)
        if (txt === searchText || (txt.indexOf(searchText) >= 0 && txt.length < searchText.length + 20)) {
            return el;
        }
    }
    return null;
};

// Step-by-step navigation: Home → EFB → Navigraph → Update
_efb.tryNavigraphPageUpdate = function() {
    try {
        _efb.postState('page_text', { text: 'Step 1: Looking for Electronic Flight Bag...\n' + (document.body ? document.body.innerText.substring(0, 1500) : '') });

        // Step 1: Click "Electronic Flight Bag" from home page
        var efbLink = _efb.findClickableByText('Electronic Flight Bag');
        if (!efbLink) {
            // Maybe we're already in the EFB — skip to step 2
            _efb.postState('navdata_progress', { status: 'EFB link not found, may already be in EFB', percent: 55 });
            _efb.navigraphStep2();
            return;
        }

        efbLink.click();
        _efb.postState('navdata_progress', { status: 'Clicked Electronic Flight Bag', percent: 50 });

        // Wait for EFB to load, then proceed
        setTimeout(function() {
            _efb.postState('page_text', { text: 'Step 2: Inside EFB, looking for Navigraph...\n' + (document.body ? document.body.innerText.substring(0, 1500) : '') });
            _efb.navigraphStep2();
        }, 2000);

    } catch(e) {
        _efb.postState('navdata_error', { message: 'Navigation error: ' + e.message });
    }
};

// Step 2: Find and click the Navigraph section within the EFB
_efb.navigraphStep2 = function() {
    try {
        // Look for Navigraph link/tab/button
        var navLink = _efb.findClickableByText('Navigraph')
            || _efb.findClickableByText('Nav Data')
            || _efb.findClickableByText('Navigation Data');

        if (navLink) {
            navLink.click();
            _efb.postState('navdata_progress', { status: 'Clicked Navigraph section', percent: 60 });
        } else {
            _efb.postState('navdata_progress', { status: 'Navigraph section not found, searching for update button directly', percent: 60 });
        }

        // Wait then look for the update button
        setTimeout(function() {
            var pageText = document.body ? document.body.innerText.substring(0, 2000) : '';
            _efb.postState('page_text', { text: 'Step 3: Looking for update button...\n' + pageText });

            var updateBtn = _efb.findNavdataUpdateButton();
            if (updateBtn) {
                updateBtn.click();
                _efb.postState('navdata_progress', { status: 'Clicked update button', percent: 80 });

                setTimeout(function() {
                    _efb.postState('page_text', { text: 'After update click:\n' + (document.body ? document.body.innerText.substring(0, 2000) : '') });
                }, 3000);

                _efb.startNavdataProgressPoller();
            } else {
                // No specific update button — list what we can see
                var allClickables = document.querySelectorAll('button, [role="button"], a');
                var texts = [];
                for (var j = 0; j < Math.min(30, allClickables.length); j++) {
                    var txt = (allClickables[j].textContent || '').trim();
                    if (txt.length > 0 && txt.length < 60) texts.push(txt);
                }
                _efb.postState('navdata_progress', { status: 'No update button found', percent: 65 });
                _efb.postState('page_text', {
                    text: 'No update button found.\nClickable elements: ' + texts.join(' | ') + '\n\nPage text:\n' + pageText
                });
            }
        }, 2000);
    } catch(e) {
        _efb.postState('navdata_error', { message: 'Step 2 error: ' + e.message });
    }
};

// Find a navdata update button in the DOM
_efb.findNavdataUpdateButton = function() {
    var buttons = document.querySelectorAll('button, [role="button"]');
    for (var i = 0; i < buttons.length; i++) {
        var txt = (buttons[i].textContent || '').toLowerCase();
        if (txt.indexOf('update') >= 0 || txt.indexOf('download') >= 0 || txt.indexOf('install') >= 0) {
            if (txt.indexOf('nav') >= 0 || txt.indexOf('airac') >= 0 || txt.indexOf('data') >= 0) {
                return buttons[i];
            }
        }
    }
    return null;
};

// ================================================================
// INTERACTIVE DISPLAY — scan visible elements for accessible navigation
// ================================================================

// Store element references for click-by-index
_efb._displayElements = [];

// Scan the DOM for all visible text-bearing elements
_efb.cmdGetDisplayElements = function() {
    try {
        var items = [];
        _efb._displayElements = [];

        // Walk the DOM tree to find leaf-level text elements
        var walker = function(el, depth) {
            if (!el || depth > 15) return;
            // Skip hidden, script, style
            if (el.nodeType !== 1) return;
            var tag = (el.tagName || '').toLowerCase();
            if (tag === 'script' || tag === 'style' || tag === 'link' || tag === 'meta') return;

            var style = null;
            try { style = window.getComputedStyle(el); } catch(e) {}
            if (style && (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0')) return;

            // Get direct text (not from children)
            var directText = '';
            for (var i = 0; i < el.childNodes.length; i++) {
                if (el.childNodes[i].nodeType === 3) { // Text node
                    directText += el.childNodes[i].textContent;
                }
            }
            directText = directText.trim();

            // Check for icon/image alt text
            var altText = el.getAttribute('aria-label') || el.getAttribute('title') || el.getAttribute('alt') || '';

            // Determine text to show
            var displayText = directText || altText;

            // For SVG/img without text, try parent's aria-label
            if (!displayText && (tag === 'svg' || tag === 'img' || tag === 'path')) {
                var parent = el.parentElement;
                if (parent) {
                    displayText = parent.getAttribute('aria-label') || parent.getAttribute('title') || '';
                    if (displayText) el = parent; // Click the parent instead
                }
            }

            // If this element has meaningful text, add it
            if (displayText && displayText.length > 0 && displayText.length < 200) {
                // On an EFB tablet, almost everything is tappable.
                // Mark all items as clickable — pressing Enter on non-interactive text is harmless.
                var isClickable = true;

                // Don't add if a child will add the same text (prefer leaf nodes)
                var childHasText = false;
                var children = el.children;
                if (children) {
                    for (var c = 0; c < children.length; c++) {
                        var childText = (children[c].textContent || '').trim();
                        if (childText === displayText) { childHasText = true; break; }
                    }
                }

                if (!childHasText) {
                    var idx = _efb._displayElements.length;
                    _efb._displayElements.push(el);
                    items.push({
                        index: idx,
                        text: displayText.replace(/\n/g, ' ').substring(0, 100),
                        clickable: isClickable,
                        tag: tag,
                        role: el.getAttribute('role') || ''
                    });
                }
            }

            // Recurse into children
            var kids = el.children;
            if (kids) {
                for (var k = 0; k < kids.length; k++) {
                    walker(kids[k], depth + 1);
                }
            }
        };

        walker(document.body, 0);

        _efb.postState('display_elements', {
            count: items.length,
            items: JSON.stringify(items)
        });

    } catch(e) {
        _efb.postState('navdata_error', { message: 'Display scan error: ' + e.message });
    }
};

// Click a display element by stored index
_efb.cmdClickDisplayElement = function(index) {
    try {
        if (index < 0 || index >= _efb._displayElements.length) {
            _efb.postState('navdata_error', { message: 'Invalid element index: ' + index });
            return;
        }
        var el = _efb._displayElements[index];
        var txt = (el.textContent || '').trim().substring(0, 50);
        el.click();
        _efb.postState('navdata_progress', { status: 'Clicked: ' + txt, percent: 0 });

        // Auto-refresh display after click (give page time to update)
        setTimeout(function() {
            _efb.cmdGetDisplayElements();
        }, 1000);
    } catch(e) {
        _efb.postState('navdata_error', { message: 'Click error: ' + e.message });
    }
};

// Poll for navdata update progress from DOM changes
_efb.navdataProgressPoller = null;
_efb.startNavdataProgressPoller = function() {
    if (_efb.navdataProgressPoller) clearInterval(_efb.navdataProgressPoller);
    _efb.navdataProgressPoller = setInterval(function() {
        var allText = document.body ? document.body.innerText : '';
        // Always send the page text for diagnostics
        _efb.postState('page_text', { text: allText.substring(0, 2000) });
        var progressMatch = allText.match(/(\d+)\s*%/);
        if (progressMatch) {
            var pct = parseInt(progressMatch[1]);
            _efb.postState('navdata_progress', { status: 'updating', percent: pct });
            if (pct >= 100) {
                clearInterval(_efb.navdataProgressPoller);
                _efb.navdataProgressPoller = null;
                _efb.postState('navdata_complete', { success: true });
            }
        }
    }, 2000);

    // Stop after 10 minutes
    setTimeout(function() {
        if (_efb.navdataProgressPoller) {
            clearInterval(_efb.navdataProgressPoller);
            _efb.navdataProgressPoller = null;
        }
    }, 600000);
};

// --- FMS Transfer Result Reporting ---

_efb.reportFmsTransferResult = function() {
    if (_efb.fmsTransferTimeout !== null) {
        clearTimeout(_efb.fmsTransferTimeout);
        _efb.fmsTransferTimeout = null;
    }
    // Nothing to report if neither result arrived
    if (_efb.fmsTransferWxCode === null && _efb.fmsTransferFpCode === null) return;

    var wxOk = _efb.fmsTransferWxCode === '200';
    var fpOk = _efb.fmsTransferFpCode === '200';
    var message = '';
    if (_efb.fmsTransferWxCode !== null && _efb.fmsTransferFpCode !== null) {
        if (wxOk && fpOk) {
            message = 'Route and weather files transferred successfully';
        } else if (wxOk) {
            message = 'Weather file transferred, but route file failed (status ' + _efb.fmsTransferFpCode + ')';
        } else if (fpOk) {
            message = 'Route file transferred, but weather file failed (status ' + _efb.fmsTransferWxCode + ')';
        } else {
            message = 'Both transfers failed (WX: ' + _efb.fmsTransferWxCode + ', Route: ' + _efb.fmsTransferFpCode + ')';
        }
    } else if (_efb.fmsTransferWxCode !== null) {
        message = wxOk ? 'Weather file transferred, but route file result was not received' : 'Weather file failed (status ' + _efb.fmsTransferWxCode + '), route file result was not received';
    } else {
        message = fpOk ? 'Route file transferred, but weather file result was not received' : 'Route file failed (status ' + _efb.fmsTransferFpCode + '), weather file result was not received';
    }
    _efb.postState('simbrief_fetch_result', {
        success: String(wxOk && fpOk && _efb.fmsTransferWxCode !== null && _efb.fmsTransferFpCode !== null),
        message: message
    });
    // Reset for next transfer
    _efb.fmsTransferWxCode = null;
    _efb.fmsTransferFpCode = null;
};

// --- EventBus Subscriptions ---

_efb.subscribeToBusEvents = function() {
    var subscriber = _efb.bus.getSubscriber();

    subscriber.on('current_app').whenChanged().handle(function(app) {
        _efb.postState('page_changed', { app: app, page: '' });
    });
    subscriber.on('current_page').whenChanged().handle(function(page) {
        _efb.postState('page_changed', { app: '', page: page });
    });

    subscriber.on('simbrief_data').whenChanged().handle(function(data) {
        // Defer briefly so the tablet's React layer finishes rendering the
        // Dashboard DOM. buildSimbriefPayload reads efb_dashboard_std/sta
        // directly from the DOM for STD/STA times, and those elements still
        // hold placeholder content at the exact moment this event fires.
        setTimeout(function() {
            try {
                var payload = _efb.buildSimbriefPayload(data);
                if (payload) {
                    _efb.lastSimbriefPayload = payload;
                    _efb.postState('simbrief_loaded', payload);
                }
            } catch (e) {
                console.error('[EFB Bridge] Error processing simbrief_data:', e);
            }
        }, 750);
    });

    // The WASM sends two simbrief_fetch_result messages: one for WX, one for Flightplans.
    // Each has data.directory ("WX" or "Flightplans") and data.result (HTTP status code).
    // We collect both before reporting success/failure. A 15s timeout flushes partial results
    // in case one message never arrives.
    subscriber.on('simbrief_fetch_result').handle(function(result) {
        try {
            var parsed = typeof result === 'string' ? JSON.parse(result) : result;
            if (parsed.data && parsed.data.directory === 'WX') {
                _efb.fmsTransferWxCode = parsed.data.result;
            } else if (parsed.data && parsed.data.directory === 'Flightplans') {
                _efb.fmsTransferFpCode = parsed.data.result;
            }

            // Start a timeout on first result so we don't get stuck if the second never arrives
            if (_efb.fmsTransferTimeout === null && (_efb.fmsTransferWxCode !== null || _efb.fmsTransferFpCode !== null)) {
                _efb.fmsTransferTimeout = setTimeout(function() {
                    _efb.reportFmsTransferResult();
                }, 15000);
            }

            // Report immediately once we have both results
            if (_efb.fmsTransferWxCode !== null && _efb.fmsTransferFpCode !== null) {
                _efb.reportFmsTransferResult();
            }
        } catch (e) {
            console.error('[EFB Bridge] Error processing simbrief_fetch_result:', e);
        }
    });

    if (typeof Navigraph !== 'undefined' && Navigraph.auth) {
        Navigraph.auth.onAuthStateChanged(function(user) {
            if (user) {
                _efb.postState('navigraph_auth_state', {
                    authenticated: 'true',
                    username: user.preferred_username || user.name || 'Unknown'
                });
            } else {
                _efb.postState('navigraph_auth_state', {
                    authenticated: 'false',
                    username: ''
                });
            }
        }, true);
    }
};

// --- Initialization ---

_efb.initBridge = function(eventBus) {
    _efb.bus = eventBus;
    _efb.publisher = eventBus.getPublisher();
    console.log('[EFB Bridge] EventBus acquired, subscribing to events');
    _efb.subscribeToBusEvents();
    _efb.startConnectionLoop();
};

_efb.startConnectionLoop = function() {
    _efb.tryConnect();
    setInterval(async function() {
        if (!_efb.serverConnected) {
            await _efb.tryConnect();
        }
    }, _efb.RECONNECT_INTERVAL);
};

_efb.waitForBus = function() {
    var check = setInterval(function() {
        if (typeof MessageService !== 'undefined' && MessageService.messaging_bus) {
            clearInterval(check);
            console.log('[EFB Bridge] MessageService.messaging_bus found');
            _efb.initBridge(MessageService.messaging_bus);
        }
    }, _efb.BUS_WAIT_INTERVAL);
};

// ================================================================
// PAGE HTML CAPTURE — for WebView2 accessible display
// ================================================================

// Helper: build a CSS selector path to uniquely identify an element
_efb.getElementPath = function(el) {
    var parts = [];
    while (el && el !== document.body && el !== document.documentElement) {
        var tag = (el.tagName || '').toLowerCase();
        if (!tag) break;
        var idx = 0;
        var sibling = el;
        while (sibling.previousElementSibling) {
            sibling = sibling.previousElementSibling;
            if ((sibling.tagName || '').toLowerCase() === tag) idx++;
        }
        parts.unshift(tag + ':nth-of-type(' + (idx + 1) + ')');
        el = el.parentElement;
    }
    return 'body > ' + parts.join(' > ');
};

// Capture the page HTML and send in chunks to avoid HTTP body size limits
_efb.cmdGetPageHtml = function() {
    try {
        var bodyHtml = '';
        try {
            bodyHtml = document.body ? document.body.innerHTML : '';
        } catch(e1) {
            _efb.postState('page_html', { html: '<p>innerHTML failed: ' + e1.message + '</p>' });
            return;
        }

        if (!bodyHtml || bodyHtml.length < 10) {
            _efb.postState('page_html', { html: '<p>No page content</p>' });
            return;
        }

        // Truncate very large pages
        if (bodyHtml.length > 400000) {
            bodyHtml = bodyHtml.substring(0, 400000) + '<!-- truncated -->';
        }

        // Capture all style tags from head so layout/visibility is preserved
        var styles = '';
        try {
            var styleEls = document.querySelectorAll('head style, head link[rel="stylesheet"]');
            for (var s = 0; s < styleEls.length; s++) {
                if (styleEls[s].tagName.toLowerCase() === 'style') {
                    styles += styleEls[s].outerHTML + '\n';
                }
                // Skip <link> tags — external CSS won't resolve in WebView2
            }
        } catch(e2) { /* styles optional */ }

        // Add data-efb-index to all clickable elements for routing clicks back
        try {
            var allEls = document.body.querySelectorAll('*');
            for (var idx = 0; idx < allEls.length; idx++) {
                allEls[idx].setAttribute('data-efb-idx', String(idx));
            }
            // Re-grab innerHTML now that we've added indices
            bodyHtml = document.body.innerHTML;
            // Remove the attributes from the real page so they don't accumulate
            for (var idx2 = 0; idx2 < allEls.length; idx2++) {
                allEls[idx2].removeAttribute('data-efb-idx');
            }
        } catch(e3) { /* indices optional */ }

        // Wrap with captured styles + click interceptor that sends element index
        var html = '<!DOCTYPE html><html><head><meta charset="utf-8">'
            + styles
            + '<style>:focus{outline:3px solid #0078D4 !important;}body{font-family:sans-serif;}</style>'
            + '</head><body>'
            + bodyHtml
            + '<script>'
            + 'document.body.addEventListener("click",function(e){'
            + 'var el=e.target;'
            + 'while(el&&!el.getAttribute("data-efb-idx")){el=el.parentElement;}'
            + 'if(el&&window.chrome&&window.chrome.webview){'
            + 'window.chrome.webview.postMessage(JSON.stringify({type:"click",idx:el.getAttribute("data-efb-idx"),text:(el.textContent||"").trim().substring(0,100)}));'
            + 'e.preventDefault();}'
            + '},true);'
            + '</script></body></html>';

        console.log('[EFB Bridge] page_html size: ' + html.length + ' chars');

        // Send in 30KB chunks to stay under HTTP POST limit (64KB with JSON overhead)
        var CHUNK_SIZE = 30000;
        var totalChunks = Math.ceil(html.length / CHUNK_SIZE);
        var transferId = Date.now().toString(36);

        for (var i = 0; i < totalChunks; i++) {
            var chunk = html.substring(i * CHUNK_SIZE, (i + 1) * CHUNK_SIZE);
            _efb.postState('page_html_chunk', {
                id: transferId,
                index: String(i),
                total: String(totalChunks),
                chunk: chunk
            });
        }
    } catch(e) {
        _efb.postState('page_html', { html: '<p>Error: ' + e.message + '</p>' });
    }
};

// Click an element by its CSS path (routed from WebView2)
_efb.cmdClickByPath = function(path) {
    try {
        if (!path) return;
        var el = document.querySelector(path);
        if (el) {
            el.click();
            var txt = (el.textContent || '').trim().substring(0, 50);
            _efb.postState('navdata_progress', { status: 'Clicked: ' + txt, percent: 0 });
            // Auto-refresh HTML after click
            setTimeout(function() { _efb.cmdGetPageHtml(); }, 1000);
        } else {
            _efb.postState('navdata_error', { message: 'Element not found for path: ' + path });
        }
    } catch(e) {
        _efb.postState('navdata_error', { message: 'Click by path error: ' + e.message });
    }
};

// Entry point
console.log('[EFB Bridge] Accessibility bridge script loaded, waiting for EventBus...');
_efb.waitForBus();

} catch (e) {
    console.error('[EFB Bridge] Fatal error during initialization:', e);
}
