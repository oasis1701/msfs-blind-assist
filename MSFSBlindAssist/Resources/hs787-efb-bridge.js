// HorizonSim 787-9 EFB Accessibility Bridge
// Injected into HSB789_EFB.GE.html and HSB789_EFB.RR.html.
// Reads Boeing EFB buttons and page text, exposes clickable controls to the
// MSFS Blind Assist app via HTTP on localhost:19778.
//
// Runs in MSFS Coherent GT (older Chromium). No modern APIs.
// Top-level try-catch ensures errors here never break the EFB.
try {

var _efb = {
    SERVER_URL: 'http://localhost:19778',
    SCREEN_POLL_INTERVAL: 500,
    COMMAND_POLL_INTERVAL: 400,
    HEARTBEAT_INTERVAL: 5000,
    RECONNECT_INTERVAL: 5000,
    serverConnected: false,
    commandPollTimer: null,
    heartbeatTimer: null,
    screenPollTimer: null,
    previousScreen: null
};

// --- HTTP Communication ---

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

_efb.postState = function(type, data) {
    if (!_efb.serverConnected) return;
    try {
        fetch(_efb.SERVER_URL + '/state', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: type, data: data || {} })
        });
    } catch (e) { }
};

_efb.pollCommands = function() {
    if (!_efb.serverConnected) return;
    fetch(_efb.SERVER_URL + '/commands/efb').then(function(response) {
        return response.json();
    }).then(function(commands) {
        for (var i = 0; i < commands.length; i++) {
            _efb.handleCommand(commands[i].command, commands[i].payload);
        }
    }).catch(function() { });
};

_efb.tryConnect = function() {
    return _efb.fetchWithTimeout(_efb.SERVER_URL + '/ping', {}, 2000).then(function(response) {
        if (response.ok) {
            if (!_efb.serverConnected) {
                _efb.serverConnected = true;
                console.log('[EFB Bridge] Connected to accessibility server');
                _efb.startPolling();
                _efb.postState('efb_connected');
            }
            return true;
        }
        throw new Error('not ok');
    }).catch(function() {
        if (_efb.serverConnected) {
            _efb.serverConnected = false;
            console.log('[EFB Bridge] Lost connection to accessibility server');
            _efb.stopPolling();
        }
        return false;
    });
};

_efb.startPolling = function() {
    _efb.stopPolling();
    _efb.commandPollTimer = setInterval(function() { _efb.pollCommands(); }, _efb.COMMAND_POLL_INTERVAL);
    _efb.screenPollTimer  = setInterval(function() { _efb.pollScreen(); },  _efb.SCREEN_POLL_INTERVAL);
    _efb.heartbeatTimer   = setInterval(function() {
        _efb.tryConnect().then(function(ok) {
            if (ok) _efb.postState('heartbeat');
        });
    }, _efb.HEARTBEAT_INTERVAL);
};

_efb.stopPolling = function() {
    if (_efb.commandPollTimer) { clearInterval(_efb.commandPollTimer); _efb.commandPollTimer = null; }
    if (_efb.screenPollTimer)  { clearInterval(_efb.screenPollTimer);  _efb.screenPollTimer = null; }
    if (_efb.heartbeatTimer)   { clearInterval(_efb.heartbeatTimer);   _efb.heartbeatTimer = null; }
};

// --- EFB Screen Reading ---

_efb.cleanText = function(s) {
    return (s || '').replace(/\s+/g, ' ').trim();
};

// Read the current page title from the Boeing EFB title element.
_efb.getPageTitle = function() {
    var titleEl = document.querySelector('[class*="efb-title-page"]');
    if (titleEl) return _efb.cleanText(titleEl.textContent);
    return '';
};

// Collect all EFB buttons from the DOM.
//
// Boeing EFB uses two button types:
//   1. BoeingEfbSideButton  → <span class="boeing-efb-button [boeing-efb-button-disabled]">TEXT</span>
//   2. BoeingEfbDropdownButton → <div>
//                                  <div class="button-name">LABEL</div>
//                                  <span class="boeing-efb-dropdown-button">...</span>
//                                </div>
//
// Strategy A: Find every .button-name element (dropdown labels).
//   The sibling .boeing-efb-dropdown-button span is the clickable target.
// Strategy B: Find every .boeing-efb-button* span (side buttons).
//   The label is the element's own textContent.
//
// Disabled side buttons are still included in the list (marked "(disabled)") so the
// user can understand the page layout. Clicking a disabled button is a no-op in the sim.
//
_efb.collectButtons = function() {
    var buttons = [];
    var seenEls = [];

    function notSeen(el) {
        for (var i = 0; i < seenEls.length; i++) {
            if (seenEls[i] === el) return false;
        }
        seenEls.push(el);
        return true;
    }

    // Strategy A: .button-name → dropdown buttons
    var nameEls = document.querySelectorAll('.button-name');
    for (var i = 0; i < nameEls.length; i++) {
        var nameEl = nameEls[i];
        var label = _efb.cleanText(nameEl.textContent);
        if (!label) continue;

        // Clickable is a sibling .boeing-efb-dropdown-button span inside the same parent div.
        // Fall back to the parent div itself if no dropdown span found.
        var parent = nameEl.parentElement;
        var clickEl = null;
        if (parent) {
            clickEl = parent.querySelector('.boeing-efb-dropdown-button');
            if (!clickEl) clickEl = parent;
        }
        if (!clickEl || !notSeen(clickEl)) continue;

        var cls = (clickEl.className || '').toString();
        var disabled = cls.indexOf('boeing-efb-button-disabled') !== -1;
        buttons.push({ index: buttons.length, label: disabled ? label + ' (disabled)' : label, el: clickEl, disabled: disabled });
    }

    // Strategy B: direct boeing-efb-button* side button spans
    var directSels = '.boeing-efb-button,.boeing-efb-button1,.boeing-efb-button2,.boeing-efb-button3,.boeing-efb-textfield-button';
    var sideEls = document.querySelectorAll(directSels);
    for (var i = 0; i < sideEls.length; i++) {
        var el = sideEls[i];
        if (!notSeen(el)) continue;

        var label = _efb.cleanText(el.textContent);
        if (!label) continue;

        var cls = (el.className || '').toString();
        var disabled = cls.indexOf('boeing-efb-button-disabled') !== -1;
        buttons.push({ index: buttons.length, label: disabled ? label + ' (disabled)' : label, el: el, disabled: disabled });
    }

    return buttons;
};

// Dispatch pointer + click events to a Boeing EFB element.
// Boeing WT components use addEventListener('click') on their root element.
_efb.clickElement = function(el) {
    var opts = { bubbles: true, cancelable: true };
    try { el.dispatchEvent(new PointerEvent('pointerdown', opts)); } catch(e) {}
    try { el.dispatchEvent(new PointerEvent('pointerup', opts)); } catch(e) {}
    try { el.dispatchEvent(new MouseEvent('click', opts)); } catch(e) {}
};

// Collect visible text from the EFB page, skipping button and title regions.
_efb.walkVisibleText = function(node, out) {
    if (!node) return;
    if (node.nodeType === 1) {
        var cls = (node.className || '').toString();
        // Skip button containers — they're captured separately in the button list
        if (cls.indexOf('boeing-efb-button') !== -1 ||
            cls.indexOf('boeing-efb-dropdown-button') !== -1 ||
            cls.indexOf('boeing-mfd-button') !== -1) return;
        // Skip title elements — already the first line
        if (cls.indexOf('efb-title') !== -1) return;
    }
    if (node.nodeType === 3) {
        var text = _efb.cleanText(node.nodeValue);
        if (text.length > 1) out.push(text);
        return;
    }
    if (node.nodeType !== 1) return;
    for (var i = 0; i < node.childNodes.length; i++) {
        _efb.walkVisibleText(node.childNodes[i], out);
    }
};

_efb.buildPageText = function(title) {
    // title is sent separately as pageTitle — do not include in text to avoid duplication
    var lines = [];

    var textParts = [];
    _efb.walkVisibleText(document.body, textParts);

    var seen = {};
    for (var i = 0; i < textParts.length; i++) {
        if (!seen[textParts[i]]) {
            seen[textParts[i]] = true;
            lines.push(textParts[i]);
        }
    }
    return lines.join('\n').replace(/\n{3,}/g, '\n\n').trim();
};

_efb.pollScreen = function() {
    var title = _efb.getPageTitle();
    var buttons = _efb.collectButtons();
    var text = _efb.buildPageText(title);

    var btnList = '';
    for (var i = 0; i < buttons.length; i++) {
        btnList += (i > 0 ? '|' : '') + buttons[i].index + ':' + buttons[i].label;
    }
    var combined = text + '\x00' + btnList;

    if (combined === _efb.previousScreen) return;
    _efb.previousScreen = combined;

    var data = {
        text: text,
        pageTitle: title,
        buttonCount: String(buttons.length)
    };
    for (var i = 0; i < buttons.length; i++) {
        data['btn' + i] = buttons[i].label;
    }
    _efb.postState('efb_screen', data);
};

// --- Navigation ---

// Navigate the EFB to the Ground Services page.
// The WT Boeing EFB uses a left sidebar with nav buttons (SVG icons + text labels).
// We try several strategies because the exact class names can vary between EFB versions.
_efb.navigateGroundServices = function() {
    // Strategy 1: sidebar/tab nav elements containing "ground" or "gnd" text
    var navCandidates = document.querySelectorAll(
        'button, [role="button"], [role="tab"], ' +
        '[class*="efb-nav"], [class*="EfbNav"], [class*="efb-tab"], [class*="EfbTab"], ' +
        '[class*="sidebar"], [class*="Sidebar"], [class*="nav-item"], [class*="NavItem"]'
    );
    for (var i = 0; i < navCandidates.length; i++) {
        var el = navCandidates[i];
        var text = _efb.cleanText(el.textContent).toLowerCase();
        var label = (el.getAttribute('aria-label') || '').toLowerCase();
        var title = (el.getAttribute('title') || '').toLowerCase();
        if (text.indexOf('ground') !== -1 || text === 'gnd' || text === 'gnd svc' || text === 'gnd srv' ||
            label.indexOf('ground') !== -1 || title.indexOf('ground') !== -1) {
            _efb.clickElement(el);
            _efb.scheduleScreenRefresh(900);
            return;
        }
    }

    // Strategy 2: any leaf-level element whose entire text is "Ground" (case-insensitive)
    var walker = document.createTreeWalker(document.body, 1 /* NodeFilter.SHOW_ELEMENT */);
    var node;
    while ((node = walker.nextNode())) {
        if (node.childElementCount > 0) continue;
        var nodeText = _efb.cleanText(node.textContent).toLowerCase();
        if (nodeText === 'ground' || nodeText === 'gnd' || nodeText === 'gnd svc' || nodeText === 'gnd srv') {
            var clickTarget = node.closest('[role="button"]') || node.parentElement || node;
            _efb.clickElement(clickTarget);
            _efb.scheduleScreenRefresh(900);
            return;
        }
    }

    // Strategy 3: data attributes
    var dataEls = document.querySelectorAll('[data-page], [data-tab], [data-route]');
    for (var i = 0; i < dataEls.length; i++) {
        var val = ((dataEls[i].getAttribute('data-page') || '') +
                   (dataEls[i].getAttribute('data-tab') || '') +
                   (dataEls[i].getAttribute('data-route') || '')).toLowerCase();
        if (val.indexOf('ground') !== -1 || val.indexOf('gnd') !== -1) {
            _efb.clickElement(dataEls[i]);
            _efb.scheduleScreenRefresh(900);
            return;
        }
    }

    // Navigation element not found — read current screen so the user sees what's available
    console.warn('[EFB Bridge] Ground services nav element not found; reading current page');
    _efb.previousScreen = null;
    _efb.pollScreen();
};

_efb.scheduleScreenRefresh = function(delayMs) {
    setTimeout(function() {
        _efb.previousScreen = null;
        _efb.pollScreen();
    }, delayMs);
};

// --- Command Handlers ---

_efb.handleCommand = function(command, payload) {
    console.log('[EFB Bridge] Command:', command, payload);
    try {
        var btnMatch = command.match(/^click_btn_(\d+)$/);
        if (btnMatch) {
            var idx = parseInt(btnMatch[1]);
            var buttons = _efb.collectButtons();
            if (idx < buttons.length) {
                _efb.clickElement(buttons[idx].el);
                // Force fresh screen push after click so C# sees the updated state
                _efb.scheduleScreenRefresh(600);
            }
            return;
        }

        switch (command) {
            case 'read_screen':
                _efb.previousScreen = null;
                _efb.pollScreen();
                break;
            case 'navigate_ground_services':
                _efb.navigateGroundServices();
                break;
            default:
                console.warn('[EFB Bridge] Unknown command:', command);
        }
    } catch (e) {
        console.error('[EFB Bridge] Command error:', e);
    }
};

// --- Initialization ---

_efb.startConnectionLoop = function() {
    _efb.tryConnect();
    setInterval(function() {
        if (!_efb.serverConnected) {
            _efb.tryConnect();
        }
    }, _efb.RECONNECT_INTERVAL);
};

console.log('[EFB Bridge] Accessibility bridge loaded, connecting...');
setTimeout(function() {
    _efb.startConnectionLoop();
}, 3000);

} catch (e) {
    console.error('[EFB Bridge] Fatal error during initialization:', e);
}
