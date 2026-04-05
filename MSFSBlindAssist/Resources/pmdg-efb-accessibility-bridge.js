// PMDG EFB Accessibility Bridge
// Injected into PMDGTabletCA.html via mod package override to expose EFB
// functionality to the MSFS Blind Assist application via HTTP on localhost:19777.
//
// Runs in MSFS Coherent GT (older Chromium). No modern APIs like AbortSignal.timeout().
// Top-level try-catch ensures errors here never break the EFB.
try {

var _efb = {
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
    fmsTransferTimeout: null
};

// --- HTTP Communication ---

_efb.postState = async function(type, data) {
    if (!_efb.serverConnected) return;
    try {
        await fetch(_efb.SERVER_URL + '/state', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: type, data: data || {} })
        });
    } catch (e) {
        // Server unreachable — will be detected by heartbeat
    }
};

_efb.pollCommands = async function() {
    if (!_efb.serverConnected) return;
    try {
        var response = await fetch(_efb.SERVER_URL + '/commands');
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

_efb.tryConnect = async function() {
    try {
        var response = await _efb.fetchWithTimeout(_efb.SERVER_URL + '/ping', {}, 2000);
        if (response.ok) {
            if (!_efb.serverConnected) {
                _efb.serverConnected = true;
                console.log('[EFB Bridge] Connected to accessibility server');
                _efb.startPolling();
                _efb.postState('connected');
                // Send current state now that connection is established
                _efb.sendCurrentNavigraphState();
            }
            return true;
        }
    } catch (e) {
        // Not available yet
    }

    if (_efb.serverConnected) {
        _efb.serverConnected = false;
        console.log('[EFB Bridge] Lost connection to accessibility server');
        _efb.stopPolling();
    }
    return false;
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
            case 'send_to_fmc':
                _efb.cmdSendToFMC();
                break;
            case 'start_navigraph_auth':
                _efb.cmdStartNavigraphAuth();
                break;
            case 'sign_out_navigraph':
                _efb.cmdSignOutNavigraph();
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
            default:
                console.warn('[EFB Bridge] Unknown command:', command);
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
    // Navigraph SDK may not be fully initialized yet when the bridge first connects.
    // Retry a few times with increasing delays to catch it.
    var attempts = [0, 2000, 5000, 10000, 20000];
    for (var i = 0; i < attempts.length; i++) {
        (function(delay) {
            setTimeout(function() {
                if (typeof Navigraph !== 'undefined' && Navigraph.auth && Navigraph.auth.getUser) {
                    Navigraph.auth.getUser(true).then(function(user) {
                        if (user) {
                            _efb.postState('navigraph_auth_state', {
                                authenticated: 'true',
                                username: user.preferred_username || user.name || 'Unknown'
                            });
                        }
                        // Only send "not authenticated" on the last attempt to avoid false negatives
                        else if (delay === 20000) {
                            _efb.postState('navigraph_auth_state', {
                                authenticated: 'false',
                                username: ''
                            });
                        }
                    }).catch(function(e) {
                        console.error('[EFB Bridge] Error getting Navigraph user:', e);
                    });
                }
            }, delay);
        })(attempts[i]);
    }
};

_efb.cmdGetPreferences = function() {
    var prefs = {};
    var keys = [
        'simbrief_id', 'weather_source', 'weight_unit', 'distance_unit',
        'altitude_unit', 'temperature_unit', 'length_unit', 'speed_unit',
        'airspeed_unit', 'pressure_unit', 'on_screen_keyboard', 'theme_setting'
    ];
    for (var i = 0; i < keys.length; i++) {
        if (typeof Settings !== 'undefined' && Settings[keys[i]] !== undefined) {
            prefs[keys[i]] = String(Settings[keys[i]]);
        }
    }
    _efb.postState('preferences', prefs);
};

_efb.cmdSetPreference = function(key, value) {
    // Use Settings.updateSetting() directly — this persists to DataStore
    // and is the same mechanism the EFB's own save uses.
    if (typeof Settings !== 'undefined' && Settings.updateSetting) {
        Settings.updateSetting(key, value);
    } else {
        _efb.postState('error', { message: 'EFB Settings not available — preferences cannot be saved' });
    }
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
        try {
            _efb.postState('simbrief_loaded', {
                callsign: data.atc.callsign || '',
                origin_icao: data.origin.icao_code || '',
                dest_icao: data.destination.icao_code || '',
                alt_icao: data.alternate.icao_code || '',
                cruise_alt: String(data.general.initial_altitude || ''),
                cost_index: String(data.general.costindex || ''),
                zfw: String(data.weights.est_zfw || ''),
                fuel_total: String(data.fuel.plan_ramp || ''),
                avg_wind: data.general.avg_wind_dir + '/' + data.general.avg_wind_spd
            });
        } catch (e) {
            console.error('[EFB Bridge] Error processing simbrief_data:', e);
        }
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

// Entry point
console.log('[EFB Bridge] Accessibility bridge script loaded, waiting for EventBus...');
_efb.waitForBus();

} catch (e) {
    console.error('[EFB Bridge] Fatal error during initialization:', e);
}
