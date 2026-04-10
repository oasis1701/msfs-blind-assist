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
