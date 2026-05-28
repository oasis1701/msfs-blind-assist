// FlyByWire A380X — MSFS Blind Assist accessibility bridge
// BRIDGE_JS_VERSION: 0.5.0-flat
//
// Selectors and event names verified against the open-source FBW aircraft
// repo (master branch, fbw-a380x):
//   - <a380x-mfd> custom element, mount at #MFD_CONTENT, split into
//     #MFD_LEFT_PARENT_DIV (CAPT) + #MFD_RIGHT_PARENT_DIV (FO).
//   - Per-MFD root: <div class="mfd-main">.
//   - Title bar:   .mfd-title-bar-text (in .mfd-title-bar-container).
//   - Footer msg:  .mfd-footer-message-area.
//   - Interactive widgets (from MsfsAvionicsCommon/UiWidgets/):
//       .mfd-input-field-container / .mfd-button / .mfd-icon-button /
//       .mfd-dropdown-outer / .mfd-dropdown-menu-element /
//       .mfd-page-selector-outer
//   - KCCU H:events: H:A32NX_KCCU_{L|R}_<KEY>, where KEY is from kccu.xml
//     (FPLN, PERF, INIT, DIR, NAVAID, DEST, SECINDEX, SURV, ATCCOM, ND,
//      MAILBOX, CLRINFO, UP, DOWN, LEFT, RIGHT, DOT, SLASH, PLUSMINUS,
//      SP, ENT, BACKSPACE, ESC, ESC2, A-Z, 0-9, KBD).
//   - KCCU keyboard enable L:var: L:A32NX_KCCU_L_KBD_ON_OFF (and R).
//   - EFB mount point: #MSFS_REACT_MOUNT (verified in installed efb.html).
//
// Display semantics (mirrors Forms/FenixA320/FenixMCDUForm.OnDisplayUpdated):
//   row 0:  "Title: <page name>"
//   body:   label rows are "   <text>" (3-space indent, unnumbered);
//           value rows are "N: <text>" where N is the interactive
//           element's sequential index on this page. Rows that contain
//           multiple interactive fields get inline "N: <text>" markers
//           at each field's spatial position.
//   last:   "Scratchpad: <text>"
//
// Wire protocol (consumed by Forms/FBWA380/FBWA380MCDUForm + FBWA380EFBForm):
//   POST /state:
//     fbwa380_mcdu_connected  (heartbeat marker)
//     fbwa380_efb_connected   (heartbeat marker)
//     fbwa380_mcdu_screen     { mcdu, rowCount, gridWidth, row0..rowN }
//     fbwa380_mcdu_elements   { mcdu, count, items.N.text/kind/value/disabled }
//     fbwa380_efb_elements    { page, items.N.text/tag/role/value/type/clickable }
//   GET /commands  (dispatched here):
//     page_init / page_data / page_dir / page_fpln / page_perf /
//     page_radnav / page_fuel / page_sec_fpln / page_atc / page_menu /
//     page_airport / page_overfly
//     key_next_page / key_prev_page / key_exec
//     type_key  { key }   — fire one KCCU key
//     select_mcdu { mcdu }
//     get_mcdu_elements / click_mcdu_element / set_mcdu_element_value
//     send_scratchpad     { text } — type each char + ENT into the
//                                   currently-focused MFD field
//     send_to_field       { index, text } — click field [index], clear it,
//                                   type each char, fire ENT
//     get_display_elements / click_display_element / set_element_value
//                                   (EFB analogues)
//
// Runs in Coherent GT (older Chromium). Top-level try/catch keeps a bug
// here from breaking the avionics. fetch() to localhost works in CGT —
// verified by the existing PMDG bridge in this same repo.

try {
if (window._fbwA380_bridge_loaded) {
    console.log('[A380 Bridge] Already loaded, skipping');
} else { window._fbwA380_bridge_loaded = true;

var _a380 = {
    JS_VERSION: '0.5.0-flat',
    SERVER_URL: 'http://localhost:19777',
    SCREEN_POLL_INTERVAL: 350,
    EFB_POLL_INTERVAL: 800,
    COMMAND_POLL_INTERVAL: 400,
    HEARTBEAT_INTERVAL: 5000,
    KEY_FIRE_DELAY_MS: 50,   // inter-key delay when typing into a field
    serverConnected: false,
    timers: { mcdu: null, mcduElements: null, efb: null, cmd: null, hb: null },
    lastMcduHash: null,
    lastMcduElementsHash: null,
    lastEfbHash: null,
    activeMcdu: 1,           // 1 = CAPT, 2 = FO. The A380X has no third MCDU.
    _mcduElements: [],
    role: 'efb'
};

// Same JS file is included from both mfd.html and efb.html. Pick the role
// from the host DOM so the unused pollers don't waste CPU.
try {
    if (document.getElementById('MFD_CONTENT')
        || document.getElementsByTagName('a380x-mfd').length > 0) {
        _a380.role = 'mfd';
    } else if (document.getElementById('MSFS_REACT_MOUNT')) {
        _a380.role = 'efb';
    }
} catch (e) { /* default to efb */ }

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

_a380.ensureKccuKeyboardOn = function() {
    // Without the KCCU keyboard enabled, the MFD ignores typed
    // characters. Flip the L:var on if it isn't already. The cockpit
    // panel has a KBD ON / OFF switch; this is the same toggle.
    try {
        if (typeof SimVar !== 'undefined' && typeof SimVar.SetSimVarValue === 'function') {
            var name = 'L:A32NX_KCCU_' + (_a380.activeMcdu === 1 ? 'L' : 'R') + '_KBD_ON_OFF';
            SimVar.SetSimVarValue(name, 'bool', 1);
        }
    } catch (e) { /* swallow */ }
};

_a380.fireKccu = function(key) {
    var side = _a380.activeMcdu === 1 ? 'L' : 'R';
    var eventName = 'A32NX_KCCU_' + side + '_' + key;
    try {
        // Coherent.trigger is the canonical path the WT/FBW SDK wires
        // H:events through. SimVar.SetSimVarValue on the same name is a
        // belt-and-braces fallback some builds also recognise.
        if (typeof Coherent !== 'undefined' && typeof Coherent.trigger === 'function') {
            Coherent.trigger('H:' + eventName);
        }
        if (typeof SimVar !== 'undefined' && typeof SimVar.SetSimVarValue === 'function') {
            SimVar.SetSimVarValue('H:' + eventName, 'number', 0);
        }
    } catch (e) {
        console.warn('[A380 Bridge] fireKccu failed for', key, e);
    }
};

_a380.charToKccuKey = function(c) {
    if (c >= 'A' && c <= 'Z') return c;
    if (c >= 'a' && c <= 'z') return c.toUpperCase();
    if (c >= '0' && c <= '9') return c;
    if (c === '.') return 'DOT';
    if (c === '/') return 'SLASH';
    if (c === '+' || c === '-') return 'PLUSMINUS';
    if (c === ' ') return 'SP';
    return null;
};

// ----- Stale-attribute hygiene ---------------------------------------
//
// Each poll re-assigns sequential indices starting at 1 to whatever
// interactive elements are visible RIGHT NOW. Stale [data-fbwa380-*]
// attributes from a previous poll on now-removed elements would mean
// that a "click element 5" command issued by the form lands on the wrong
// node — or worse, a hidden node that's still in the React tree.
// Clearing before each poll keeps the index space invariant.

_a380.clearStaleAttrs = function(root, attrName) {
    if (!root) return;
    var stale = root.querySelectorAll('[' + attrName + ']');
    for (var i = 0; i < stale.length; i++) {
        stale[i].removeAttribute(attrName);
    }
};

// ----- MFD layout helpers --------------------------------------------

_a380.findMfdRoots = function() {
    // Two MFDs only — CAPT (left) and FO (right). The A380X has no
    // standby MCDU. The MFD instrument splits its single iframe into
    // MFD_LEFT_PARENT_DIV / MFD_RIGHT_PARENT_DIV (see MFD/instrument.tsx).
    var roots = { 1: null, 2: null };
    var captParent = document.getElementById('MFD_LEFT_PARENT_DIV');
    var foParent   = document.getElementById('MFD_RIGHT_PARENT_DIV');
    if (captParent) roots[1] = captParent.querySelector('.mfd-main');
    if (foParent)   roots[2] = foParent.querySelector('.mfd-main');
    if (!roots[1] && !roots[2]) {
        var all = document.querySelectorAll('.mfd-main');
        if (all.length >= 1) roots[1] = all[0];
        if (all.length >= 2) roots[2] = all[1];
    }
    return roots;
};

_a380.isVisible = function(node) {
    try {
        var s = window.getComputedStyle(node);
        if (s.display === 'none' || s.visibility === 'hidden') return false;
        var r = node.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
    } catch (e) { return true; }
};

_a380.getActivePageLabel = function(root) {
    var bar = root.querySelector('.mfd-title-bar-container');
    if (!bar) return '';
    var spans = bar.querySelectorAll('.mfd-title-bar-text');
    var parts = [];
    for (var i = 0; i < spans.length; i++) {
        var s = spans[i];
        if (!_a380.isVisible(s)) continue;
        var t = (s.textContent || '').replace(/\s+/g, ' ').trim();
        if (t) parts.push(t);
    }
    return parts.join(' / ');
};

_a380.getFooterMessage = function(root) {
    var footer = root.querySelector('.mfd-footer-message-area');
    if (footer) {
        var t = (footer.textContent || '').replace(/\s+/g, ' ').trim();
        if (t) return t;
    }
    var amber = root.querySelector('.mfd-amber-error-message');
    if (amber) return (amber.textContent || '').replace(/\s+/g, ' ').trim();
    return '';
};

// ----- MCDU interactive element classification -----------------------

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

_a380.readInputValue = function(inputContainer) {
    var inner = inputContainer.querySelector('.mfd-input-field-text-input');
    return inner ? (inner.textContent || '').replace(/\s+/g, ' ').trim() : '';
};

_a380.mcduElementLabel = function(node, kind) {
    var text = '';
    switch (kind) {
        case 'input': {
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

// Returns the field index attached to the nearest ancestor that's an
// interactive widget, or null if this leaf isn't inside one.
_a380.findAncestorFieldIdx = function(node, root) {
    var cur = node;
    while (cur && cur !== root.parentNode) {
        if (cur.getAttribute) {
            var idx = cur.getAttribute('data-fbwa380-mcdu-idx');
            if (idx) return parseInt(idx, 10);
        }
        cur = cur.parentElement;
    }
    return null;
};

// ----- MCDU element enumeration (interactive list) -------------------

_a380.enumerateInteractiveElements = function(root) {
    // Tag each currently-visible interactive widget with a fresh
    // sequential index starting at 1, returns the metadata list.
    _a380.clearStaleAttrs(root, 'data-fbwa380-mcdu-idx');
    var out = [];
    if (!root) return out;
    var nodes = root.querySelectorAll(_a380.MCDU_INTERACTIVE_SELECTOR);
    var idx = 1;
    for (var i = 0; i < nodes.length && idx <= 400; i++) {
        var n = nodes[i];
        if (!_a380.isVisible(n)) continue;
        var kind = _a380.classifyMcduElement(n);
        var text = _a380.mcduElementLabel(n, kind);
        n.setAttribute('data-fbwa380-mcdu-idx', String(idx));
        out.push({
            index: idx, kind: kind, text: text,
            value: kind === 'input' ? _a380.readInputValue(n) : '',
            disabled: n.classList.contains('disabled')
        });
        idx++;
    }
    return out;
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

// ----- MCDU display grid (Fenix-style) -------------------------------
//
// Renders the page as a fixed-width text grid:
//   row 0:  "Title: <name>"
//   body:   pure label rows are "   <text>" (3-space indent, no number).
//           Interactive value rows are "N: <text>" where N is the field
//           index assigned by enumerateInteractiveElements. Multiple
//           fields on one row get inline "N: <text>" markers at their
//           horizontal positions, mirroring the cockpit layout.
//   last:   "Scratchpad: <text>"

_a380.GRID_WIDTH = 36;
_a380.MAX_BODY_ROWS = 28;
_a380.ROW_Y_TOLERANCE_PX = 14;

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
    var rows = [];
    if (!root) return rows;

    rows.push('Title: ' + _a380.getActivePageLabel(root));

    var page = root.querySelector('.mfd-navigator-container');
    if (page) {
        var bodyRect = page.getBoundingClientRect();
        var leaves = page.querySelectorAll(_a380.MFD_LEAF_SELECTOR);
        var positioned = [];
        for (var i = 0; i < leaves.length; i++) {
            var n = leaves[i];
            if (!_a380.isVisible(n)) continue;
            var text = (n.textContent || '').replace(/\s+/g, ' ').trim();
            if (!text) continue;
            var r = n.getBoundingClientRect();
            positioned.push({
                text: text,
                top: r.top - bodyRect.top,
                left: r.left - bodyRect.left,
                width: r.width,
                fieldIdx: _a380.findAncestorFieldIdx(n, page)
            });
        }
        positioned.sort(function(a, b) { return a.top - b.top || a.left - b.left; });

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

        var bodyW = Math.max(bodyRect.width, 1);
        var bodyRowCount = 0;
        for (var c = 0; c < clustered.length && bodyRowCount < _a380.MAX_BODY_ROWS; c++) {
            var cluster = clustered[c];
            cluster.items.sort(function(a, b) { return a.left - b.left; });

            // Decide: is this row a pure label row (no interactive
            // children) or does it contain at least one numbered field?
            var anyField = false;
            for (var k = 0; k < cluster.items.length; k++) {
                if (cluster.items[k].fieldIdx !== null) { anyField = true; break; }
            }

            if (!anyField) {
                // Pure label row — 3-space indent, no number prefix.
                var labelGrid = _a380.makeBlankRow(_a380.GRID_WIDTH);
                for (var m = 0; m < cluster.items.length; m++) {
                    var item = cluster.items[m];
                    var col = Math.round((item.left / bodyW) * _a380.GRID_WIDTH);
                    if (col < 3) col = 3; // honour the 3-space indent
                    _a380.writeAt(labelGrid, col, item.text);
                }
                var labelText = labelGrid.join('').replace(/\s+$/, '');
                if (labelText.trim().length === 0) continue;
                rows.push('   ' + labelText.substring(3));
                bodyRowCount++;
                continue;
            }

            // Interactive row — number each field, label-only items kept
            // inline at their position.
            var valueGrid = _a380.makeBlankRow(_a380.GRID_WIDTH);
            for (var n2 = 0; n2 < cluster.items.length; n2++) {
                var it = cluster.items[n2];
                var col2 = Math.round((it.left / bodyW) * _a380.GRID_WIDTH);
                if (col2 < 0) col2 = 0;
                if (col2 >= _a380.GRID_WIDTH) col2 = _a380.GRID_WIDTH - 1;
                var prefix = it.fieldIdx !== null ? (it.fieldIdx + ': ') : '';
                _a380.writeAt(valueGrid, col2, prefix + it.text);
            }
            var line = valueGrid.join('').replace(/\s+$/, '');
            if (line.length === 0) continue;
            rows.push(line);
            bodyRowCount++;
        }
    }

    rows.push('Scratchpad: ' + _a380.getFooterMessage(root));
    return rows;
};

_a380.makeBlankRow = function(n) { var a = new Array(n); for (var i = 0; i < n; i++) a[i] = ' '; return a; };

_a380.writeAt = function(grid, col, text) {
    for (var i = 0; i < text.length && col + i < grid.length; i++) {
        grid[col + i] = text.charAt(i);
    }
};

// ----- MCDU polling --------------------------------------------------

_a380.pollMcdu = function() {
    if (_a380.role !== 'mfd') return;
    var roots = _a380.findMfdRoots();
    var root = roots[_a380.activeMcdu];

    // Element enumeration must run BEFORE the grid scrape so each
    // interactive widget has its data-fbwa380-mcdu-idx attribute set —
    // the grid scrape's "find ancestor field idx" relies on those attrs.
    var elements = _a380.enumerateInteractiveElements(root);
    var rows = _a380.collectMfdGrid(root);

    // Push the grid display if it changed.
    var hash = _a380.activeMcdu + '|' + rows.join('|');
    if (hash !== _a380.lastMcduHash) {
        _a380.lastMcduHash = hash;
        var data = {
            mcdu: String(_a380.activeMcdu),
            rowCount: String(rows.length),
            gridWidth: String(_a380.GRID_WIDTH)
        };
        for (var i = 0; i < rows.length; i++) data['row' + i] = rows[i];
        _a380.post('fbwa380_mcdu_screen', data);
    }

    // Push the element list if it changed (form's Page-fields view).
    var elHash = _a380.activeMcdu + '|' + elements.length + '|';
    for (var k = 0; k < elements.length; k++) elHash += elements[k].text + '/' + elements[k].value + '/';
    if (elHash !== _a380.lastMcduElementsHash) {
        _a380.lastMcduElementsHash = elHash;
        _a380._mcduElements = elements;
        _a380.post('fbwa380_mcdu_elements', _a380.flattenMcduElementsForPost(elements));
    }
};

// ----- MCDU command dispatch -----------------------------------------

_a380.PAGE_TO_KCCU = {
    'page_init':     'INIT',
    'page_data':     null,
    'page_dir':      'DIR',
    'page_fpln':     'FPLN',
    'page_perf':     'PERF',
    'page_radnav':   'NAVAID',
    'page_fuel':     null,
    'page_sec_fpln': 'SECINDEX',
    'page_atc':      'ATCCOM',
    'page_menu':     null,
    'page_airport':  null,
    'page_overfly':  null,
    'key_next_page': 'DOWN',
    'key_prev_page': 'UP',
    'key_exec':      'ENT'
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
                _a380.lastMcduHash = null;
                _a380.lastMcduElementsHash = null;
            }
            return;
        }
        if (command === 'type_key') {
            if (payload && payload.key) {
                var k = payload.key;
                if (k === 'CLR' || k === 'DEL') k = 'BACKSPACE';
                else if (k === 'SPACE') k = 'SP';
                _a380.fireKccu(k);
            }
            return;
        }
        if (command === 'send_scratchpad') {
            _a380.sendScratchpadComposite(payload && payload.text);
            return;
        }
        if (command === 'send_to_field') {
            if (payload && payload.index !== undefined) {
                _a380.sendToFieldComposite(
                    parseInt(payload.index, 10),
                    payload.text || ''
                );
            }
            return;
        }
        if (command === 'get_mcdu_elements') {
            _a380.lastMcduElementsHash = null;
            _a380.pollMcdu();
            return;
        }
        if (command === 'click_mcdu_element') {
            if (payload && payload.index) _a380.clickMcduElement(parseInt(payload.index, 10));
            return;
        }
        if (command === 'set_mcdu_element_value') {
            if (payload && payload.index !== undefined) {
                _a380.sendToFieldComposite(parseInt(payload.index, 10), payload.value || '');
            }
            return;
        }
        if (command === 'get_display_elements') {
            _a380.lastEfbHash = null;
            _a380.pollEfb();
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

_a380.clickMcduElement = function(index) {
    var node = document.querySelector('[data-fbwa380-mcdu-idx="' + index + '"]');
    if (!node) return;
    try {
        if (typeof node.click === 'function') node.click();
        else node.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    } catch (e) { console.warn('[A380 Bridge] mcdu click failed', e); }
    setTimeout(function(){ _a380.lastMcduHash = null; _a380.lastMcduElementsHash = null; _a380.pollMcdu(); }, 200);
};

// Type a sequence of chars + ENT into whatever field the cockpit cursor
// is currently focused on.
_a380.sendScratchpadComposite = function(text) {
    if (!text) return;
    _a380.ensureKccuKeyboardOn();
    var v = String(text).toUpperCase();
    var step = 0;
    var queue = [];
    for (var j = 0; j < v.length; j++) {
        var k = _a380.charToKccuKey(v.charAt(j));
        if (k) queue.push(k);
    }
    queue.push('ENT');
    function fireNext() {
        if (step >= queue.length) {
            setTimeout(function(){ _a380.lastMcduHash = null; _a380.pollMcdu(); }, 200);
            return;
        }
        _a380.fireKccu(queue[step]);
        step++;
        setTimeout(fireNext, _a380.KEY_FIRE_DELAY_MS);
    }
    fireNext();
};

// Composite: click field [index] → clear existing → type new → ENT.
_a380.sendToFieldComposite = function(index, newValue) {
    var node = document.querySelector('[data-fbwa380-mcdu-idx="' + index + '"]');
    if (!node) return;
    var elementInfo = null;
    for (var i = 0; i < _a380._mcduElements.length; i++) {
        if (_a380._mcduElements[i].index === index) { elementInfo = _a380._mcduElements[i]; break; }
    }

    // Non-input element (button, dropdown, menu item, tab) — just click.
    if (elementInfo && elementInfo.kind !== 'input') {
        _a380.clickMcduElement(index);
        return;
    }

    _a380.ensureKccuKeyboardOn();
    try {
        if (typeof node.click === 'function') node.click();
        else node.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    } catch (e) {}

    var existingLen = elementInfo
        ? (elementInfo.value ? elementInfo.value.length : 0)
        : _a380.readInputValue(node).length;
    var queue = [];
    for (var ii = 0; ii < existingLen; ii++) queue.push('BACKSPACE');
    var v = String(newValue || '').toUpperCase();
    for (var jj = 0; jj < v.length; jj++) {
        var kk = _a380.charToKccuKey(v.charAt(jj));
        if (kk) queue.push(kk);
    }
    queue.push('ENT');

    var step = 0;
    function fireNext() {
        if (step >= queue.length) {
            setTimeout(function(){ _a380.lastMcduHash = null; _a380.lastMcduElementsHash = null; _a380.pollMcdu(); }, 200);
            return;
        }
        _a380.fireKccu(queue[step]);
        step++;
        setTimeout(fireNext, _a380.KEY_FIRE_DELAY_MS);
    }
    // Small initial delay so the click registers before we start typing.
    setTimeout(fireNext, 100);
};

// ----- flyPad EFB scraper --------------------------------------------

_a380._efbElements = [];

_a380.getEfbPage = function() {
    var active = document.querySelector('a.bg-theme-accent[href], a[class*="bg-theme-accent"][href]');
    if (active) {
        var href = active.getAttribute('href') || '';
        if (href.startsWith('/')) return href.substring(1);
        return href;
    }
    return '';
};

_a380.findEfbContentRoot = function() {
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
    _a380.clearStaleAttrs(root, 'data-fbwa380-idx');
    var out = [];
    if (!root) return out;
    var idx = 0;
    var seenText = Object.create(null);
    var walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT, null, false);
    while (walker.nextNode()) {
        var n = walker.currentNode;
        if (!_a380.isVisible(n)) continue;
        var tag = (n.tagName || '').toLowerCase();
        var role = (n.getAttribute && n.getAttribute('role')) || '';
        var className = (n.className && typeof n.className === 'string') ? n.className : '';
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
        if (text.length > 240) text = text.substring(0, 240) + '…';

        var dedupKey = tag + '|' + text + '|' + value;
        if (seenText[dedupKey] && !controlType) continue;
        seenText[dedupKey] = true;

        n.setAttribute('data-fbwa380-idx', String(idx));
        out.push({
            index: idx, tag: tag, role: role, text: text, value: value,
            controlType: controlType, clickable: clickable
        });
        idx++;
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

_a380.pollEfb = function() {
    if (_a380.role !== 'efb') return;
    var page = _a380.getEfbPage();
    var root = _a380.findEfbContentRoot();
    var elements = _a380.collectEfbElements(root);
    var hashBuf = page + '|' + elements.length + '|';
    for (var i = 0; i < elements.length; i++) hashBuf += elements[i].text + elements[i].value;
    if (hashBuf === _a380.lastEfbHash) return;
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
    } catch (e) { console.warn('[A380 Bridge] efb click failed', e); }
    setTimeout(function(){ _a380.lastEfbHash = null; _a380.pollEfb(); }, 250);
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
            var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
            setter.call(node, value);
            node.dispatchEvent(new Event('input', { bubbles: true }));
            node.dispatchEvent(new Event('change', { bubbles: true }));
        }
    } catch (e) { console.warn('[A380 Bridge] efb set value failed', e); }
    setTimeout(function(){ _a380.lastEfbHash = null; _a380.pollEfb(); }, 250);
};

// ----- Lifecycle ------------------------------------------------------

_a380.start = function() {
    _a380.timers.hb  = setInterval(_a380.heartbeat,    _a380.HEARTBEAT_INTERVAL);
    _a380.timers.cmd = setInterval(_a380.pollCommands, _a380.COMMAND_POLL_INTERVAL);
    if (_a380.role === 'mfd') {
        _a380.timers.mcdu = setInterval(_a380.pollMcdu, _a380.SCREEN_POLL_INTERVAL);
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
