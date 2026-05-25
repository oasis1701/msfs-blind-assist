// FlyByWire A380X — MSFS Blind Assist accessibility bridge
// BRIDGE_JS_VERSION: 0.4.0-mcdu-grid
//
// Selectors and event names verified against the open-source FBW aircraft
// repo (master branch, fbw-a380x). See:
//
//   fbw-a380x/src/systems/instruments/src/MFD/MFD.tsx
//     - <a380x-mfd> custom element, mount at #MFD_CONTENT,
//       split into #MFD_LEFT_PARENT_DIV (CAPT) + #MFD_RIGHT_PARENT_DIV (FO).
//     - Per-MFD root: <div class="mfd-main">, page in <div class="mfd-navigator-container">.
//     - KCCU H:events fired by panel buttons: H:A32NX_KCCU_L_<KEY> / H:A32NX_KCCU_R_<KEY>.
//
//   fbw-a380x/src/base/.../model/behaviour/kccu.xml
//     - Complete key list (digits, A-Z, FPLN, PERF, INIT, DIR, NAVAID, DEST,
//       SECINDEX, SURV, ATCCOM, ND, MAILBOX, CLRINFO, UP, DOWN, LEFT, RIGHT,
//       DOT, SLASH, PLUSMINUS, SP, ENT, BACKSPACE, ESC, ESC2, FORWARD,
//       REWIND, KBD).
//
//   fbw-common/src/systems/instruments/src/EFB/Efb.tsx
//     - React app routes: /dashboard /dispatch /ground /performance
//       /navigation /atc /failures /checklists /presets /settings.
//     - Toolbar uses NavLink with activeClassName "bg-theme-accent".
//
// Wire protocol (see C# Forms/FBWA380/FBWA380MCDUForm.cs + FBWA380EFBForm.cs):
//   MCDU push:  type="fbwa380_mcdu_screen"
//               data={ mcdu:"1"|"2", rowCount, row0..row13 }
//   EFB push:   type="fbwa380_efb_elements"
//               data={ page, items.N.text/tag/role/value/type/clickable }
//   Commands consumed (GET /commands):
//     page_init / page_data / page_dir / page_fpln / page_perf
//     page_radnav / page_fuel / page_sec_fpln / page_atc / page_menu
//     page_airport / page_overfly
//     key_next_page / key_prev_page / key_exec
//     lsk_L1..lsk_L6 / lsk_R1..lsk_R6        (synthesised: scroll-to + click)
//     type_key { key in "A-Z","0-9","DOT","SLASH","PLUSMINUS","SP","CLR","DEL" }
//     select_mcdu { mcdu: "1" | "2" }
//     get_display_elements / click_display_element / set_element_value
//
// Runs in Coherent GT (older Chromium build). Top-level try/catch keeps
// errors here from breaking the avionics.

try {
if (window._fbwA380_bridge_loaded) {
    console.log('[A380 Bridge] Already loaded, skipping');
} else { window._fbwA380_bridge_loaded = true;

var _a380 = {
    JS_VERSION: '0.2.0-verified',
    SERVER_URL: 'http://localhost:19777',
    SCREEN_POLL_INTERVAL: 350,
    EFB_POLL_INTERVAL: 800,
    COMMAND_POLL_INTERVAL: 400,
    HEARTBEAT_INTERVAL: 5000,
    serverConnected: false,
    timers: { mcdu: null, mcduElements: null, efb: null, cmd: null, hb: null },
    lastMcduHash: null,
    lastMcduElementsHash: null,
    lastEfbHash: null,
    activeMcdu: 1,     // 1 = CAPT (left), 2 = FO (right)
    _mcduElements: [],
    role: detectRole() // 'mfd' or 'efb' — set once below
};

// The same JS is included from both mfd.html and efb.html. We pick what
// to do based on which DOM is hosting us: presence of #MFD_CONTENT means
// MFD, anything else falls back to EFB scraping. The custom element
// <a380x-mfd> appears under MFD too — checking either is sufficient.
function detectRole() {
    try {
        if (document.getElementById('MFD_CONTENT')
            || document.getElementsByTagName('a380x-mfd').length > 0) {
            return 'mfd';
        }
    } catch (e) { /* swallow */ }
    return 'efb';
}

// ----- HTTP -----------------------------------------------------------

_a380.post = function(type, data) {
    if (!_a380.serverConnected) return;
    try {
        fetch(_a380.SERVER_URL + '/state', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: type, data: data || {} })
        }).catch(function(){});
    } catch (e) { /* swallow */ }
};

_a380.pollCommands = function() {
    if (!_a380.serverConnected) return;
    try {
        fetch(_a380.SERVER_URL + '/commands').then(function(r){ return r.json(); }).then(function(cmds){
            if (!cmds || !cmds.length) return;
            for (var i = 0; i < cmds.length; i++) {
                _a380.handleCommand(cmds[i].command, cmds[i].payload);
            }
        }).catch(function(){});
    } catch (e) { /* swallow */ }
};

_a380.heartbeat = function() {
    try {
        fetch(_a380.SERVER_URL + '/ping').then(function(r){
            if (r.ok && !_a380.serverConnected) {
                _a380.serverConnected = true;
                console.log('[A380 Bridge] Connected to MSFSBA server, role=' + _a380.role);
                if (_a380.role === 'mfd') _a380.post('fbwa380_mcdu_connected');
                else                     _a380.post('fbwa380_efb_connected');
            }
        }).catch(function(){
            if (_a380.serverConnected) {
                _a380.serverConnected = false;
                console.log('[A380 Bridge] Lost MSFSBA server');
            }
        });
    } catch (e) { /* swallow */ }
};

// ----- KCCU key dispatch ---------------------------------------------
//
// All buttons on the A380X "MCDU"/KCCU panel fire H:A32NX_KCCU_{L|R}_<KEY>.
// The MFD listens for hEvents, slices off the "A32NX_KCCU_L_" prefix
// (13 characters), and dispatches the suffix to the active page.

_a380.fireKccu = function(key) {
    var side = _a380.activeMcdu === 1 ? 'L' : 'R';
    var eventName = 'A32NX_KCCU_' + side + '_' + key;
    try {
        // Preferred path: Coherent.trigger (the ASOBO_GT_Push_Button_Airliner
        // template fires H:events through Coherent).
        if (typeof Coherent !== 'undefined' && typeof Coherent.trigger === 'function') {
            Coherent.trigger('H:' + eventName);
        }
        // Backup path: SimVar.SetSimVarValue on the H:event name. Some
        // builds route H:events through this. No-op if the handler isn't
        // registered.
        if (typeof SimVar !== 'undefined' && typeof SimVar.SetSimVarValue === 'function') {
            SimVar.SetSimVarValue('H:' + eventName, 'number', 0);
        }
    } catch (e) {
        console.warn('[A380 Bridge] fireKccu failed for', key, e);
    }
};

// ----- MCDU screen scraper -------------------------------------------
//
// Each MFD instance is a <div class="mfd-main"> nested two levels under
// either #MFD_LEFT_PARENT_DIV (CAPT) or #MFD_RIGHT_PARENT_DIV (FO).
// The current page sits in .mfd-navigator-container; the page header
// (active tab labels) sits in .mfd-header-page-select-row.
//
// We don't try to map the FMS page to a 14-row classical MCDU layout —
// the A380 FMS pages aren't laid out that way. Instead we walk the
// visible text content of the navigator container in document order,
// up to a sensible row cap (24 rows), and return:
//   row0          = current page title (best-effort: active header tab)
//   row1..row22   = page lines
//   row23         = scratchpad / error message (best-effort)
// FBWA380MCDUForm.cs treats anything beyond row13 as "extra page rows".

_a380.findMfdRoots = function() {
    var roots = { 1: null, 2: null };
    var captParent = document.getElementById('MFD_LEFT_PARENT_DIV');
    var foParent   = document.getElementById('MFD_RIGHT_PARENT_DIV');
    if (captParent) roots[1] = captParent.querySelector('.mfd-main');
    if (foParent)   roots[2] = foParent.querySelector('.mfd-main');
    // Fallback for single-MFD layouts: take whatever .mfd-main is there.
    if (!roots[1] && !roots[2]) {
        var all = document.querySelectorAll('.mfd-main');
        if (all.length >= 1) roots[1] = all[0];
        if (all.length >= 2) roots[2] = all[1];
    }
    return roots;
};

_a380.getActivePageLabel = function(root) {
    // The A380X title bar puts the current page name in
    // .mfd-title-bar-text (verified in ActivePageTitleBar.tsx + style.scss).
    // We pick the first non-empty .mfd-title-bar-text inside the title bar
    // container — that's the page label. Subsequent ones are TMPY / EO /
    // PENALTY markers; we include them inline after the page name when
    // they're visible.
    var bar = root.querySelector('.mfd-title-bar-container');
    if (!bar) return '';
    var spans = bar.querySelectorAll('.mfd-title-bar-text');
    var parts = [];
    for (var i = 0; i < spans.length; i++) {
        var s = spans[i];
        var style = window.getComputedStyle(s);
        if (style.display === 'none' || style.visibility === 'hidden') continue;
        var t = (s.textContent || '').replace(/\s+/g, ' ').trim();
        if (t) parts.push(t);
    }
    return parts.join(' / ');
};

// Width of the synthesised MCDU-style text grid in characters. The A380
// MFD is 768 px wide and uses a 30 px display font — about 25 glyphs fit
// across the page. We round up to 36 to leave a little space for the
// numbered "N:" row prefix and any padding between left/right halves.
_a380.GRID_WIDTH = 36;
_a380.MAX_BODY_ROWS = 28;
_a380.ROW_Y_TOLERANCE_PX = 14;

// Selectors that produce visible "text-bearing" leaves on the FMS page.
// Picking specific FBW widget classes rather than walking every element
// avoids the noise of layout containers re-emitting their entire subtree
// text and keeps the grid alignment honest. Verified in
// MsfsAvionicsCommon/UiWidgets and pages/common/style.scss.
_a380.MFD_LEAF_SELECTOR = [
    '.mfd-label',
    '.mfd-input-field-text-input',
    '.mfd-button',
    '.mfd-icon-button',
    '.mfd-dropdown-inner',
    '.mfd-page-selector-label',
    '.mfd-dropdown-menu-element',
    '.mfd-amber-error-message'
].join(',');

_a380.collectMfdGrid = function(root) {
    // Layout: row 0 = page title, rows 1..N = body lines, last row =
    // scratchpad / footer message. Each line is GRID_WIDTH chars wide so
    // the C# ListBox can render with a monospace font and preserve the
    // left/right spatial layout that's actually meaningful on an
    // Airbus FMS page.
    var rows = [];
    if (!root) return [_a380.padRight('', _a380.GRID_WIDTH)];

    // --- 1) Title row.
    rows.push(_a380.padRight('Title: ' + _a380.getActivePageLabel(root), _a380.GRID_WIDTH));

    // --- 2) Body rows from .mfd-navigator-container, positioned by
    // bounding-rect.
    var page = root.querySelector('.mfd-navigator-container');
    if (page) {
        var bodyRect = page.getBoundingClientRect();
        var leaves = page.querySelectorAll(_a380.MFD_LEAF_SELECTOR);
        var positioned = [];
        for (var i = 0; i < leaves.length; i++) {
            var n = leaves[i];
            var style = window.getComputedStyle(n);
            if (style.display === 'none' || style.visibility === 'hidden') continue;
            var text = (n.textContent || '').replace(/\s+/g, ' ').trim();
            if (!text) continue;
            var r = n.getBoundingClientRect();
            if (r.width === 0 || r.height === 0) continue;
            positioned.push({ text: text, left: r.left - bodyRect.left, top: r.top - bodyRect.top, width: r.width });
        }
        // Sort by Y first, then X — natural reading order.
        positioned.sort(function(a, b) { return a.top - b.top || a.left - b.left; });

        // Cluster into rows by Y tolerance.
        var clustered = [];
        var current = null;
        for (var j = 0; j < positioned.length; j++) {
            var p = positioned[j];
            if (!current || Math.abs(p.top - current.y) > _a380.ROW_Y_TOLERANCE_PX) {
                current = { y: p.top, items: [] };
                clustered.push(current);
            }
            current.items.push(p);
        }

        // Convert each cluster into a fixed-width string. Map element X
        // positions into grid columns by linear scaling against the body
        // width. Multiple items per row → laid out at their respective
        // column positions; collisions are resolved by truncation.
        var bodyW = Math.max(bodyRect.width, 1);
        var rowNum = 0;
        for (var c = 0; c < clustered.length && rowNum < _a380.MAX_BODY_ROWS; c++) {
            var cluster = clustered[c];
            // Sort items left-to-right within the row.
            cluster.items.sort(function(a, b) { return a.left - b.left; });
            var grid = _a380.makeBlankRow(_a380.GRID_WIDTH);
            for (var k = 0; k < cluster.items.length; k++) {
                var item = cluster.items[k];
                var col = Math.round((item.left / bodyW) * _a380.GRID_WIDTH);
                if (col < 0) col = 0;
                if (col >= _a380.GRID_WIDTH) col = _a380.GRID_WIDTH - 1;
                _a380.writeAt(grid, col, item.text);
            }
            // Drop pure-whitespace rows (the page often emits decorative
            // separators that contain only padding).
            var asString = grid.join('').replace(/\s+$/, '');
            if (asString.length === 0) continue;
            // Number the row in Fenix style: " N: " left-pads the line
            // so screen-reader users can navigate by row index.
            var prefix = _a380.padLeft(String(rowNum + 1), 2) + ': ';
            rows.push(prefix + asString);
            rowNum++;
        }
    }

    // --- 3) Scratchpad / footer message row.
    var footer = root.querySelector('.mfd-footer-message-area');
    var footerText = '';
    if (footer) footerText = (footer.textContent || '').replace(/\s+/g, ' ').trim();
    if (!footerText) {
        // Some pages render an inline amber error message rather than the
        // footer area — keep that too.
        var amber = root.querySelector('.mfd-amber-error-message');
        if (amber) footerText = (amber.textContent || '').replace(/\s+/g, ' ').trim();
    }
    rows.push(_a380.padRight('Scratchpad: ' + footerText, _a380.GRID_WIDTH));

    return rows;
};

_a380.makeBlankRow = function(n) { var a = new Array(n); for (var i = 0; i < n; i++) a[i] = ' '; return a; };

_a380.writeAt = function(grid, col, text) {
    for (var i = 0; i < text.length && col + i < grid.length; i++) {
        grid[col + i] = text.charAt(i);
    }
};

_a380.padRight = function(s, n) {
    s = s == null ? '' : String(s);
    while (s.length < n) s += ' ';
    return s.length > n ? s.substring(0, n) : s;
};

_a380.padLeft = function(s, n) {
    s = s == null ? '' : String(s);
    while (s.length < n) s = ' ' + s;
    return s.length > n ? s.substring(s.length - n) : s;
};

_a380.pollMcdu = function() {
    if (_a380.role !== 'mfd') return;
    var roots = _a380.findMfdRoots();
    var root = roots[_a380.activeMcdu];
    var rows = _a380.collectMfdGrid(root);

    var hash = _a380.activeMcdu + '|' + rows.join('|');
    if (hash === _a380.lastMcduHash) return;
    _a380.lastMcduHash = hash;

    var data = {
        mcdu: String(_a380.activeMcdu),
        rowCount: String(rows.length),
        gridWidth: String(_a380.GRID_WIDTH)
    };
    for (var i = 0; i < rows.length; i++) data['row' + i] = rows[i];
    _a380.post('fbwa380_mcdu_screen', data);
};

// ----- MCDU interactive element enumerator ---------------------------
//
// Walks the active MFD's navigator container + header to enumerate every
// page-internal interactive element. Classes verified against
// fbw-a380x/src/systems/instruments/src/MsfsAvionicsCommon/UiWidgets/*
// (master):
//
//   .mfd-input-field-container       — data entry field (FLT NBR, FROM, …)
//     -> child .mfd-input-field-text-input holds the displayed value
//   .mfd-button                      — text button (INSERT*, RETURN, …)
//   .mfd-icon-button                 — icon-only button
//   .mfd-dropdown-outer              — closed dropdown (selector + arrow)
//     -> child .mfd-dropdown-inner holds the displayed value
//   .mfd-dropdown-menu-element       — open-dropdown menu item
//   .mfd-page-selector-outer         — header page-selector tab
//     -> child .mfd-page-selector-label is the label
//
// We also walk the header (.mfd-header-page-select-row) so the user can
// navigate ACTIVE / POSITION / SEC INDEX / DATA / etc. from the same list.

_a380.MCDU_INTERACTIVE_SELECTOR = [
    '.mfd-input-field-container',
    '.mfd-button',
    '.mfd-icon-button',
    '.mfd-dropdown-outer',
    '.mfd-dropdown-menu-element',
    '.mfd-page-selector-outer'
].join(',');

_a380.classifyMcduElement = function(node) {
    if (node.classList.contains('mfd-input-field-container')) return 'input';
    if (node.classList.contains('mfd-button'))                return 'button';
    if (node.classList.contains('mfd-icon-button'))           return 'icon';
    if (node.classList.contains('mfd-dropdown-outer'))        return 'dropdown';
    if (node.classList.contains('mfd-dropdown-menu-element')) return 'menu';
    if (node.classList.contains('mfd-page-selector-outer'))   return 'tab';
    return 'other';
};

_a380.mcduElementLabel = function(node, kind) {
    var text = '';
    switch (kind) {
        case 'input': {
            // The value lives in .mfd-input-field-text-input. The field
            // label is rendered as a sibling .mfd-label above the input.
            var inner = node.querySelector('.mfd-input-field-text-input');
            if (inner) text = (inner.textContent || '').replace(/\s+/g, ' ').trim();
            var label = node.previousElementSibling;
            if (label && label.classList && label.classList.contains('mfd-label')) {
                var lbl = (label.textContent || '').replace(/\s+/g, ' ').trim();
                if (lbl) text = lbl + ': ' + text;
            }
            return text || '(empty input field)';
        }
        case 'button':
        case 'icon':
        case 'menu':
            return (node.textContent || '').replace(/\s+/g, ' ').trim() || '(unlabeled)';
        case 'dropdown': {
            var di = node.querySelector('.mfd-dropdown-inner');
            return (di ? di.textContent : node.textContent || '').replace(/\s+/g, ' ').trim() || '(empty dropdown)';
        }
        case 'tab': {
            var t = node.querySelector('.mfd-page-selector-label');
            return (t ? t.textContent : node.textContent || '').replace(/\s+/g, ' ').trim() || '(tab)';
        }
    }
    return '';
};

_a380.collectMcduElements = function(root) {
    var out = [];
    if (!root) return out;

    // Scrape the header (tabs/dropdowns) AND the navigator container.
    // The header sits outside .mfd-navigator-container so a single
    // querySelectorAll on the whole .mfd-main covers both.
    var nodes = root.querySelectorAll(_a380.MCDU_INTERACTIVE_SELECTOR);
    var idx = 0;
    for (var i = 0; i < nodes.length && idx < 400; i++) {
        var n = nodes[i];

        // Skip non-visible nodes (closed dropdowns render their menu but
        // hide it with display:none).
        var style = (n.ownerDocument && n.ownerDocument.defaultView)
            ? n.ownerDocument.defaultView.getComputedStyle(n) : null;
        if (style && (style.display === 'none' || style.visibility === 'hidden')) continue;

        var kind = _a380.classifyMcduElement(n);
        var text = _a380.mcduElementLabel(n, kind);
        var disabled = n.classList.contains('disabled');

        n.setAttribute('data-fbwa380-mcdu-idx', String(idx));
        out.push({
            index: idx, kind: kind, text: text,
            value: kind === 'input' ? _a380.readInputValue(n) : '',
            disabled: disabled
        });
        idx++;
    }
    return out;
};

_a380.readInputValue = function(inputContainer) {
    var inner = inputContainer.querySelector('.mfd-input-field-text-input');
    return inner ? (inner.textContent || '').replace(/\s+/g, ' ').trim() : '';
};

_a380.flattenMcduElementsForPost = function(elements) {
    var data = { count: String(elements.length), mcdu: String(_a380.activeMcdu) };
    for (var i = 0; i < elements.length; i++) {
        var e = elements[i];
        data['items.' + i + '.text']     = e.text || '';
        data['items.' + i + '.kind']     = e.kind || '';
        data['items.' + i + '.value']    = e.value || '';
        data['items.' + i + '.disabled'] = e.disabled ? 'true' : 'false';
    }
    return data;
};

_a380.pollMcduElements = function() {
    if (_a380.role !== 'mfd') return;
    var roots = _a380.findMfdRoots();
    var root = roots[_a380.activeMcdu];
    var elements = _a380.collectMcduElements(root);

    var hashBuf = _a380.activeMcdu + '|' + elements.length + '|';
    for (var i = 0; i < elements.length; i++) hashBuf += elements[i].text + '/' + elements[i].value + '/';
    if (hashBuf === _a380.lastMcduElementsHash) return;
    _a380.lastMcduElementsHash = hashBuf;

    _a380._mcduElements = elements;
    _a380.post('fbwa380_mcdu_elements', _a380.flattenMcduElementsForPost(elements));
};

_a380.clickMcduElement = function(index) {
    var node = document.querySelector('[data-fbwa380-mcdu-idx="' + index + '"]');
    if (!node) return;
    try {
        if (typeof node.click === 'function') node.click();
        else node.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    } catch (e) { console.warn('[A380 Bridge] mcdu click failed', e); }
    setTimeout(function(){ _a380.lastMcduElementsHash = null; _a380.pollMcduElements(); }, 200);
};

// Composite "set the value of this input field". The FBW MFD input
// fields are edited by clicking to enter edit mode, typing characters
// via the KCCU keyboard, and pressing ENT to commit. We do the whole
// sequence here so the form caller just sends one set_mcdu_element_value
// command per field.
_a380.setMcduElementValue = function(index, newValue) {
    var node = document.querySelector('[data-fbwa380-mcdu-idx="' + index + '"]');
    if (!node) return;
    var elementInfo = _a380._mcduElements[index];
    if (!elementInfo) return;

    // Only input fields accept a value. Dropdowns / buttons / menu items
    // are activated with click — caller should use click_mcdu_element.
    if (elementInfo.kind !== 'input') {
        _a380.clickMcduElement(index);
        return;
    }

    try {
        // Click the field to focus / enter edit mode.
        if (typeof node.click === 'function') node.click();

        // Walk the displayed value backwards and BACKSPACE the existing
        // contents. The MFD's edit mode swallows BACKSPACE without
        // committing, so this clears in place.
        var existing = _a380.readInputValue(node);
        var existingLen = existing ? existing.length : 0;
        var delay = 25; // ms between key presses; the MFD eats them at
                       // about this rate without dropping any.

        var step = 0;
        var queue = [];
        for (var i = 0; i < existingLen; i++) queue.push('BACKSPACE');

        // Convert the new value into KCCU key names.
        var v = String(newValue || '').toUpperCase();
        for (var j = 0; j < v.length; j++) {
            var c = v.charCodeAt(j);
            var kc = v.charAt(j);
            if (kc >= 'A' && kc <= 'Z') queue.push(kc);
            else if (kc >= '0' && kc <= '9') queue.push(kc);
            else if (kc === '.') queue.push('DOT');
            else if (kc === '/') queue.push('SLASH');
            else if (kc === '+' || kc === '-') queue.push('PLUSMINUS');
            else if (kc === ' ') queue.push('SP');
            // Unsupported characters are silently dropped (the KCCU
            // doesn't have them anyway).
        }
        queue.push('ENT');

        function fireNext() {
            if (step >= queue.length) {
                setTimeout(function(){
                    _a380.lastMcduElementsHash = null;
                    _a380.pollMcduElements();
                }, 200);
                return;
            }
            _a380.fireKccu(queue[step]);
            step++;
            setTimeout(fireNext, delay);
        }
        setTimeout(fireNext, 80); // small initial delay so the click registers
    } catch (e) {
        console.warn('[A380 Bridge] setMcduElementValue failed', e);
    }
};

// ----- Command dispatch ----------------------------------------------

// Page button → KCCU key. The A380X exposes a smaller set than the A320
// MCDU: missing pages route to the closest equivalent or are no-ops.
_a380.PAGE_TO_KCCU = {
    'page_init':     'INIT',
    'page_data':     null,        // No direct DATA key — DATA is a header dropdown
    'page_dir':      'DIR',
    'page_fpln':     'FPLN',
    'page_perf':     'PERF',
    'page_radnav':   'NAVAID',
    'page_fuel':     null,        // Reached via header ACTIVE → FUEL&LOAD
    'page_sec_fpln': 'SECINDEX',
    'page_atc':      'ATCCOM',
    'page_menu':     null,        // No MENU key on A380 KCCU
    'page_airport':  null,        // No standalone AIRPORT key
    'page_overfly':  null,        // No standalone OVFY key
    'key_next_page': 'DOWN',      // A380 paginates with up/down rather than NEXT/PREV
    'key_prev_page': 'UP',
    'key_exec':      'ENT'        // ENT confirms edits on A380 KCCU
};

_a380.handleCommand = function(command, payload) {
    try {
        if (Object.prototype.hasOwnProperty.call(_a380.PAGE_TO_KCCU, command)) {
            var key = _a380.PAGE_TO_KCCU[command];
            if (key) _a380.fireKccu(key);
            return;
        }
        if (command === 'select_mcdu') {
            if (payload && payload.mcdu) {
                var n = parseInt(payload.mcdu, 10);
                if (n === 1 || n === 2) _a380.activeMcdu = n;
                _a380.lastMcduHash = null; // force resend
            }
            return;
        }
        if (command === 'type_key') {
            if (payload && payload.key) {
                var k = payload.key;
                if (k === 'CLR') k = 'BACKSPACE';
                else if (k === 'DEL') k = 'BACKSPACE';
                else if (k === 'SPACE') k = 'SP';
                _a380.fireKccu(k);
            }
            return;
        }
        // LSK chord — A380 has no LSKs. Best we can do: synthesize an UP/DOWN
        // scroll + ENT click on the currently-highlighted input field. For
        // now we just log and ignore; the user navigates the readout list
        // and uses type_key for entries.
        var lsk = /^lsk_([LR])(\d)$/.exec(command);
        if (lsk) {
            console.log('[A380 Bridge] lsk_' + lsk[1] + lsk[2] + ' received — A380 has no LSKs; use UP/DOWN + ENT.');
            return;
        }
        if (command === 'get_display_elements') {
            _a380.pollEfb(true);
            return;
        }
        if (command === 'get_mcdu_elements') {
            _a380.lastMcduElementsHash = null;
            _a380.pollMcduElements();
            return;
        }
        if (command === 'click_mcdu_element') {
            if (payload && payload.index) _a380.clickMcduElement(parseInt(payload.index, 10));
            return;
        }
        if (command === 'set_mcdu_element_value') {
            if (payload && payload.index !== undefined) {
                _a380.setMcduElementValue(parseInt(payload.index, 10), payload.value || '');
            }
            return;
        }
        if (command === 'click_display_element') {
            if (payload && payload.index) _a380.clickEfbElement(parseInt(payload.index, 10));
            return;
        }
        if (command === 'set_element_value') {
            if (payload && payload.index) {
                _a380.setEfbElementValue(parseInt(payload.index, 10),
                                         payload.value || '',
                                         payload.controlType || '');
            }
            return;
        }
    } catch (e) {
        console.warn('[A380 Bridge] handleCommand failed:', e);
    }
};

// ----- flyPad EFB scraper --------------------------------------------
//
// flyPadOS is a React app with a MemoryRouter (so location.hash is
// empty). Active page is found by reading the toolbar's NavLink whose
// activeClassName is "bg-theme-accent" — that's the FBW EFB convention.
// The page content sits next to the toolbar in the main flex row.

_a380._efbElements = [];

_a380.getEfbPage = function() {
    // The toolbar lives at the top of the document. Active NavLink has
    // the "bg-theme-accent" class applied via react-router-dom NavLink
    // activeClassName.
    var active = document.querySelector('a.bg-theme-accent[href], a[class*="bg-theme-accent"][href]');
    if (active) {
        var href = active.getAttribute('href') || '';
        if (href.startsWith('/')) return href.substring(1);
        return href;
    }
    return '';
};

_a380.findEfbContentRoot = function() {
    // The A380X EFB instrument mounts React under #MSFS_REACT_MOUNT (see
    // the runtime mfd.html / efb.html in the installed FBW A380X package).
    // Inside that root the EfbInstrument component renders <ToolBar/>
    // alongside the page content. The toolbar has className containing
    // "w-32"; everything else in the flex row is the page itself.
    var mount = document.getElementById('MSFS_REACT_MOUNT') || document.body;
    var nav = mount.querySelector('nav.flex.flex-col.w-32, nav[class*="w-32"][class*="flex-col"]');
    if (nav && nav.parentNode) {
        var siblings = nav.parentNode.children;
        for (var i = 0; i < siblings.length; i++) {
            if (siblings[i] !== nav) return siblings[i];
        }
    }
    return mount;
};

_a380.collectEfbElements = function(root) {
    var out = [];
    if (!root) return out;
    var idx = 0;
    var seenText = Object.create(null);
    var walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT, null, false);
    while (walker.nextNode()) {
        var n = walker.currentNode;
        // Skip our own bridge nodes and offscreen/hidden trees.
        if (n.hasAttribute('data-fbwa380-skip')) continue;
        var style = (n.ownerDocument && n.ownerDocument.defaultView)
            ? n.ownerDocument.defaultView.getComputedStyle(n) : null;
        if (style && (style.display === 'none' || style.visibility === 'hidden')) continue;

        var tag = (n.tagName || '').toLowerCase();
        var role = (n.getAttribute && n.getAttribute('role')) || '';
        var className = (n.className && typeof n.className === 'string') ? n.className : '';

        // Skip pure layout containers — divs without text-only children
        // and no role/click handlers.
        var clickable = tag === 'button' || tag === 'a' || role === 'button'
                       || (n.onclick !== null && n.onclick !== undefined)
                       || className.indexOf('cursor-pointer') >= 0;

        var isHeading = tag === 'h1' || tag === 'h2' || tag === 'h3' || tag === 'h4';
        var controlType = '';
        var value = '';
        if (tag === 'input') {
            var type = (n.getAttribute('type') || 'text').toLowerCase();
            controlType = (type === 'checkbox' || type === 'radio') ? type : 'text';
            value = controlType === 'checkbox' ? (n.checked ? 'true' : 'false') : (n.value || '');
        } else if (tag === 'select') {
            controlType = 'select';
            value = n.value || '';
        }

        var text = '';
        if (controlType) {
            var labelEl = (n.id ? document.querySelector('label[for="' + n.id + '"]') : null);
            text = (labelEl && labelEl.textContent) || n.getAttribute('aria-label') || n.placeholder || n.title || '';
        } else if (clickable || isHeading) {
            text = (n.textContent || '').replace(/\s+/g, ' ').trim();
        } else if (tag === 'p' || tag === 'span' || tag === 'label' || tag === 'div') {
            var ownText = '';
            for (var c = 0; c < n.childNodes.length; c++) {
                if (n.childNodes[c].nodeType === 3) ownText += n.childNodes[c].nodeValue;
            }
            text = ownText.replace(/\s+/g, ' ').trim();
        }

        if (!text && !controlType) continue;
        // Truncate huge concatenated strings (e.g. wrapping <div> that
        // happened to be a "leaf" because all its text was directly
        // inside it) — anything beyond 240 chars is almost certainly
        // a layout artefact.
        if (text.length > 240) text = text.substring(0, 240) + '…';

        // Dedup adjacent identical labels (toolbar tooltip wrappers
        // double-emit otherwise).
        var dedupKey = tag + '|' + text + '|' + value;
        if (seenText[dedupKey] && !controlType) continue;
        seenText[dedupKey] = true;

        n.setAttribute('data-fbwa380-idx', String(idx));
        out.push({
            index: idx, tag: tag, role: role, text: text, value: value,
            controlType: controlType, clickable: clickable
        });
        idx++;
        // Hard cap so a huge React tree doesn't blow the bridge body limit.
        if (idx >= 400) break;
    }
    return out;
};

_a380.flattenElementsForPost = function(elements, page) {
    var data = { page: page || '' };
    for (var i = 0; i < elements.length; i++) {
        var e = elements[i];
        data['items.' + i + '.text']      = e.text || '';
        data['items.' + i + '.tag']       = e.tag || '';
        data['items.' + i + '.role']      = e.role || '';
        data['items.' + i + '.value']     = e.value || '';
        data['items.' + i + '.type']      = e.controlType || '';
        data['items.' + i + '.clickable'] = e.clickable ? 'true' : 'false';
    }
    return data;
};

_a380.pollEfb = function(force) {
    if (_a380.role !== 'efb') return;
    var page = _a380.getEfbPage();
    var root = _a380.findEfbContentRoot();
    var elements = _a380.collectEfbElements(root);

    // Hash based on page + element count + concatenated text. Cheap
    // enough to compute at 800 ms cadence.
    var hashBuf = page + '|' + elements.length + '|';
    for (var i = 0; i < elements.length; i++) hashBuf += elements[i].text + elements[i].value;
    if (!force && hashBuf === _a380.lastEfbHash) return;
    _a380.lastEfbHash = hashBuf;

    _a380._efbElements = elements;
    _a380.post('fbwa380_efb_elements', _a380.flattenElementsForPost(elements, page));
};

_a380.clickEfbElement = function(index) {
    var node = document.querySelector('[data-fbwa380-idx="' + index + '"]');
    if (!node) return;
    try {
        if (typeof node.click === 'function') node.click();
        else node.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    } catch (e) { console.warn('[A380 Bridge] click failed', e); }
    setTimeout(function(){ _a380.pollEfb(true); }, 250);
};

_a380.setEfbElementValue = function(index, value, controlType) {
    var node = document.querySelector('[data-fbwa380-idx="' + index + '"]');
    if (!node) return;
    try {
        if (controlType === 'checkbox') {
            node.checked = (value === 'true');
            node.dispatchEvent(new Event('change', { bubbles: true }));
        } else if (controlType === 'select') {
            for (var i = 0; i < node.options.length; i++) {
                if (node.options[i].value === value || node.options[i].text === value) {
                    node.selectedIndex = i; break;
                }
            }
            node.dispatchEvent(new Event('change', { bubbles: true }));
        } else {
            // React's onChange listens to the native input event. Use the
            // prototype setter so React picks up the new value.
            var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
            setter.call(node, value);
            node.dispatchEvent(new Event('input', { bubbles: true }));
            node.dispatchEvent(new Event('change', { bubbles: true }));
        }
    } catch (e) { console.warn('[A380 Bridge] set value failed', e); }
    setTimeout(function(){ _a380.pollEfb(true); }, 250);
};

// ----- Lifecycle ------------------------------------------------------

_a380.start = function() {
    _a380.timers.hb  = setInterval(_a380.heartbeat,    _a380.HEARTBEAT_INTERVAL);
    _a380.timers.cmd = setInterval(_a380.pollCommands, _a380.COMMAND_POLL_INTERVAL);
    if (_a380.role === 'mfd') {
        _a380.timers.mcdu = setInterval(_a380.pollMcdu, _a380.SCREEN_POLL_INTERVAL);
        // Element enumeration on a slightly slower cadence — the
        // structural list changes less often than the text content does.
        _a380.timers.mcduElements = setInterval(_a380.pollMcduElements, _a380.SCREEN_POLL_INTERVAL * 2);
    } else {
        _a380.timers.efb = setInterval(_a380.pollEfb, _a380.EFB_POLL_INTERVAL);
    }
    _a380.heartbeat();
    console.log('[A380 Bridge] Started, version', _a380.JS_VERSION, 'role', _a380.role);
};

_a380.start();
window._fbwA380_bridge = _a380;

} // end double-load guard
} catch (e) {
    console.error('[A380 Bridge] Fatal init error:', e);
}
