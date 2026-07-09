// PMDG EFB in-page agent — installed at runtime into the PMDG tablet's Coherent GT view
// (window.__MSFSBA_PMDG_EFB). ES5 only (Coherent GT = Chromium 49: var, no arrow funcs,
// no String.includes; use .indexOf). Generic: classifies controls by TYPE (never by element id)
// so new PMDG pages / installed EFB apps read with no code change. scrape() returns the element
// contract FbwEfbForm consumes (controlType/kind/clickable/level/value/options); clickElement(idx)
// and setValue(idx,text) drive the tablet by the stamped data-pmdg-efb-idx.
(function () {
  var A = {};
  A.INSTALLED = 'MSFSBA_PMDG_EFB_INSTALLED';
  // Synthesized Preferences controls for config-only settings the tablet STORES but renders no UI
  // for. Reserved collision-free idx range (sequential scrape idx never reaches 990000+), so each
  // stays stable across scrapes; setValue dispatches by idx. opts = [[displayText, settingValue]].
  // weather_source/time_format set via Settings.updateSetting; selected_map (Map Provider) also
  // drives the live Dashboard.dualMap.switchMap (auth-guarded). See _applySynthetic.
  A.SYN_PREFS = [
    { idx: 990001, key: 'weather_source', label: 'Weather Source', opts: [['Real World', 'REAL-WORLD'], ['Sim', 'SIM']] },
    { idx: 990002, key: 'time_format', label: 'Time Format', opts: [['UTC', 'utc'], ['Local', 'local']] },
    { idx: 990003, key: 'selected_map', label: 'Map Provider', opts: [['Navigraph', 'navigraph'], ['PMDG', 'pmdg']] }
  ];

  // FontAwesome icon class -> human name (icon-only buttons / status bar).
  A.FA_MAP = {
    'fa-home': 'Home', 'fa-house': 'Dashboard', 'fa-sliders': 'Preferences',
    'fa-database': 'Navdata', 'fa-map-location-dot': 'Navigation', 'fa-map': 'Navigation',
    'fa-file-lines': 'Charts', 'fa-file': 'Charts', 'fa-info': 'Info', 'fa-circle-info': 'Info',
    'fa-arrows-rotate': 'Refresh', 'fa-rotate': 'Refresh', 'fa-sync': 'Refresh',
    'fa-gear': 'Settings', 'fa-cog': 'Settings', 'fa-search': 'Search', 'fa-magnifying-glass': 'Search',
    'fa-plus': 'Zoom In', 'fa-minus': 'Zoom Out', 'fa-expand': 'Full Screen', 'fa-lock': 'Lock',
    'fa-xmark': 'Close', 'fa-times': 'Close', 'fa-check': 'OK', 'fa-bars': 'Menu',
    'fa-chevron-left': 'Back', 'fa-chevron-right': 'Forward', 'fa-arrow-left': 'Back',
    'fa-print': 'Print', 'fa-trash': 'Delete', 'fa-download': 'Download', 'fa-upload': 'Upload',
    'fa-plane': 'Aircraft', 'fa-cloud': 'Weather', 'fa-clock': 'Clock'
  };

  // The EFB Settings singleton (config + updateSetting/getSetting). window first, then bare global.
  A.settingsObj = function () {
    try { if (typeof window !== 'undefined' && window.Settings) return window.Settings; } catch (e) {}
    try { if (typeof Settings !== 'undefined') return Settings; } catch (e2) {}
    return null;
  };

  A.side = function () { try { return (typeof getTabletSide === 'function') ? getTabletSide() : ''; } catch (e) { return ''; } };
  A.variant = function () { try { return (typeof pmdg_tablet_path !== 'undefined') ? String(pmdg_tablet_path) : ''; } catch (e) { return ''; } };

  A.isVisible = function (el) {
    if (!el) return false;
    try {
      if (el.offsetParent === null && el.tagName !== 'BODY') {
        var cs0 = window.getComputedStyle(el);
        if (cs0.position !== 'fixed') return false;
      }
      var cs = window.getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden') return false;
      var r = el.getBoundingClientRect();
      if (r.width <= 0 || r.height <= 0) return false;
      return true;
    // Fail-CLOSED: the PMDG EFB tablet is an HTML page; its only SVG content is
    // font-awesome icon glyphs, which are presence-checked elsewhere (never
    // passed through isVisible) — treat a style/rect-read exception as hidden.
    } catch (e) { return false; }
  };

  A.txt = function (el) {
    try { return (el.textContent || '').replace(/ /g, ' ').replace(/\s+/g, ' ').trim(); } catch (e) { return ''; }
  };

  A.ownText = function (el) {
    var s = '';
    try { for (var n = 0; n < el.childNodes.length; n++) { if (el.childNodes[n].nodeType === 3) s += el.childNodes[n].nodeValue; } } catch (e) {}
    return s.replace(/\s+/g, ' ').trim();
  };

  A.faName = function (el) {
    try {
      var i = el.querySelector ? el.querySelector('i[class*="fa-"], svg[class*="fa-"]') : null;
      var src = i || el;
      var cls = (typeof src.className === 'string') ? src.className : (src.className && src.className.baseVal) || '';
      var toks = cls.split(/\s+/);
      for (var k = 0; k < toks.length; k++) { if (A.FA_MAP[toks[k]]) return A.FA_MAP[toks[k]]; }
    } catch (e) {}
    return '';
  };

  // Current VALUE behind a status-bar icon indicator, so the readout says "Battery: Full,
  // charging" / "Signal: Connected" instead of a bare "Battery"/"Signal". Battery level comes
  // from the fa-battery-* class + charging from fa-bolt/fa-plug; Signal connectivity from colour
  // (PMDG green = connected) or a slash icon (= no signal). Returns '' when nothing determinable.
  A.statusbarValue = function (el, name) {
    try {
      var n = String(name || '').toLowerCase();
      if (n.indexOf('battery') >= 0) {
        var bi = el.querySelector ? el.querySelector('i[class*="fa-battery"]') : null;
        var bc = bi ? ((typeof bi.className === 'string') ? bi.className : (bi.className && bi.className.baseVal) || '') : '';
        var lvl = '';
        if (/fa-battery-full/.test(bc)) lvl = 'Full';
        else if (/fa-battery-three-quarters/.test(bc)) lvl = 'Three quarters';
        else if (/fa-battery-half/.test(bc)) lvl = 'Half';
        else if (/fa-battery-quarter/.test(bc)) lvl = 'Quarter';
        else if (/fa-battery-empty/.test(bc)) lvl = 'Empty';
        var charging = !!(el.querySelector && el.querySelector('i[class*="fa-bolt"], i[class*="fa-plug"]'));
        var parts = [];
        if (lvl) parts.push(lvl);
        if (charging) parts.push('charging');
        return parts.join(', ');
      }
      if (n.indexOf('signal') >= 0) {
        if (el.querySelector && el.querySelector('i[class*="fa-signal-slash"], i[class*="fa-ban"]')) return 'No signal';
        var col = ''; try { col = window.getComputedStyle(el).color || ''; } catch (e) {}
        var m = col.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
        if (m) { var r = +m[1], g = +m[2], b = +m[3]; if (g > r && g >= b) return 'Connected'; if (r > g && r > b) return 'No signal'; }
        return 'Connected';  // icon present, no slash → connected
      }
    } catch (e) {}
    return '';
  };

  // id -> label, stripping common PMDG prefixes and title-casing.
  A.idToLabel = function (id) {
    if (!id) return '';
    var s = id.replace(/^efb_preferences_/, '').replace(/^efb_dashboard_/, '')
             .replace(/^efb_general_/, '').replace(/^opt_/, '').replace(/^groundops_/, '')
             .replace(/^efb_/, '').replace(/^wb_/, '').replace(/^statusbar_/, '').replace(/_/g, ' ')
             // "leaflet" is the internal JS map-library name; never a user-facing word.
             .replace(/\bleaflet\b/gi, ' ').replace(/\s+/g, ' ').trim();
    // The screen reader already announces the "button" role — a trailing "Button"/"Btn"
    // word in the id (e.g. "weather search button") is redundant clutter, so drop it.
    s = s.replace(/\s*\b(button|btn)\b\s*$/i, '').trim();
    if (!s) return '';
    return s.replace(/\b\w/g, function (c) { return c.toUpperCase(); });
  };

  // Expand a unit abbreviation to a full word so a unit toggle reads clearly
  // ("Weight Unit: kilograms" not "...: kg"). Unmapped abbreviations pass through unchanged.
  A.UNIT_WORDS = {
    kg: 'kilograms', kgs: 'kilograms', lb: 'pounds', lbs: 'pounds',
    nm: 'nautical miles', km: 'kilometers', m: 'meters', ft: 'feet',
    mps: 'meters per second', 'm/s': 'meters per second', 'm/sec': 'meters per second',
    kt: 'knots', kts: 'knots', kn: 'knots', mph: 'miles per hour',
    c: 'Celsius', f: 'Fahrenheit', hpa: 'hectopascals', inhg: 'inches of mercury', 'in': 'inches'
  };
  A.unitWord = function (u) {
    var k = String(u || '').trim();
    return A.UNIT_WORDS[k.toLowerCase()] || k;
  };

  // Preference UNIT toggles are TEXTLESS checkboxes: ::before/::after content is empty, and
  // window.Settings (the saved value) only commits on "Save Preferences", so it LAGS the live
  // toggle. The ONLY live signal is el.checked. Verified live (PMDG 737/777 EFB, 2026-06) by
  // reading each toggle's checked + its saved Settings value. Map = id -> [uncheckedUnit, checkedUnit].
  // We render these as 2-option SELECTS (clearer for a screen reader than a bare "checked" checkbox)
  // and toggle the checkbox on selection (see setValue). speed_unit / airspeed_unit are omitted —
  // their second value wasn't confirmed — so they fall back to the saved-Settings read.
  A.UNIT_PAIRS = {
    efb_preferences_length_unit:      ['m',   'ft'],
    efb_preferences_altitude_unit:    ['m',   'ft'],
    efb_preferences_distance_unit:    ['km',  'nm'],
    efb_preferences_weight_unit:      ['kg',  'lbs'],
    efb_preferences_temperature_unit: ['F',   'C'],
    efb_preferences_pressure_unit:    ['hpa', 'inhg']
  };

  // Active-tab / current-page marker (flypad convention). PMDG marks the selected tab/nav with
  // the class `active_button`; the EFB sub-nav pages additionally carry `efb_main_menu_button`.
  // Returns " (current page)" for an active nav page, " (selected)" for an active in-page tab,
  // else "". (Detection is gated by isVisible at the call site so hidden pages aren't marked.)
  A.activeMarker = function (el) {
    try {
      var cls = (typeof el.className === 'string') ? el.className : '';
      // A dropdown's chosen .selected-option is a COMBO VALUE (already surfaced as the select's
      // value), not a tab — never mark it. Likewise the map-layer "_inactive" toggles aren't tabs.
      if (/(^|\s)selected-option(\s|$)/.test(cls)) return '';
      // The EFB uses several active-state conventions across its pages: `active_button` (nav pages +
      // Performance / Charts / Navdata tabs) and a `*_highlighted` suffix (Ground Operations
      // sub-tabs: groundops_menu_button_highlighted). Cover them all so the "(selected)" marker
      // works EVERYWHERE in the EFB, not just Performance.
      var active = /(^|\s)(active_button|active|is-active|selected)(\s|$)/.test(cls)
        || /(^|\s|_)highlighted(\s|$)/.test(cls)
        || el.getAttribute('aria-selected') === 'true'
        || !!el.getAttribute('aria-current');
      if (!active) return '';
      return /efb_main_menu_button|efb_nav_button|main_menu_button/.test(cls) ? ' (current page)' : ' (selected)';
    } catch (e) { return ''; }
  };

  // pseudo-element captions on a toggle (PMDG renders unit text via ::before / ::after content).
  A.toggleCaptions = function (el) {
    try {
      var strip = function (s) { return String(s || '').replace(/^["']|["']$/g, ''); };
      var b = strip(window.getComputedStyle(el, '::before').content);
      var a = strip(window.getComputedStyle(el, '::after').content);
      var out = [];
      if (b && b !== 'none' && b !== 'normal') out.push(b);
      if (a && a !== 'none' && a !== 'normal') out.push(a);
      return out;
    } catch (e) { return []; }
  };

  // Tight row pairing: a label cell that is a SIBLING (same row), not a distant ancestor.
  // Used to prefix row-action buttons / value cells, e.g. "Air Start Unit: REQUEST".
  A._pairLabel = function (el) {
    // immediate parent (the row) only — a grandparent scan spuriously pairs standalone
    // buttons (e.g. section-nav tabs) with an unrelated label in the section.
    var p = el.parentElement;
    if (p) {
      var kids = p.children;
      for (var i = 0; i < kids.length; i++) {
        var c = kids[i]; if (c === el || c.contains(el)) continue;
        // direct-sibling label cell ONLY — a descendant scan reaches into adjacent rows
        // (a top-level button grabbing a distant .opt-label).
        var hit = (c.matches && c.matches('.groundops_ui_label, .opt-label, .opt-output-label, .field-label, .popup-label')) ? c : null;
        if (hit) { var t = A.txt(hit); if (t && t.length < 60) return t.replace(/:\s*$/, ''); }
      }
    }
    return '';
  };

  // Nearest enclosing SECTION heading for an element — used to disambiguate duplicate buttons
  // by the section a sighted user sees them under (e.g. the two "Import From OFP" buttons read
  // "Airport: Import From OFP" / "Aircraft: Import From OFP" instead of an ugly id qualifier).
  A._sectionHeading = function (el) {
    try {
      var p = el.parentElement, hops = 0;
      while (p && hops < 5) {
        var h = p.querySelector ? p.querySelector('h1,h2,h3,h4,h5,h6,[role=heading]') : null;
        if (h && !h.contains(el)) { var t = A.txt(h); if (t && t.length > 0 && t.length < 40) return t; }
        p = p.parentElement; hops++;
      }
    } catch (e) {}
    return '';
  };

  // Active unit for a PMDG preference toggle — authoritative from the EFB Settings object,
  // keyed by the control id (efb_preferences_length_unit -> Settings.length_unit). Falls back ''.
  A.activeUnit = function (el) {
    try {
      var S = A.settingsObj();
      if (el.id && S) {
        var key = el.id.replace(/^efb_preferences_/, '');
        if (key !== el.id) { var v = S[key]; if (v !== undefined && v !== null && String(v) !== '') return String(v); }
      }
    } catch (e) {}
    return '';
  };

  // The unit-adornment span (.input-unit "ft" / .output-unit "kt") for THIS field — a DIRECT
  // sibling only. Climbing parents grabbed a neighbouring field's unit (Airport Name -> "ft").
  A.fieldUnit = function (el) {
    try {
      var p = el.parentElement; if (!p) return '';
      for (var i = 0; i < p.children.length; i++) {
        var c = p.children[i]; if (c === el || c.contains(el)) continue;
        if (c.matches && c.matches('.input-unit, .output-unit')) { var t = A.txt(c); if (t && t.length <= 8) return t; }
        if (c.children.length <= 1 && c.querySelector) { var inn = c.querySelector('.input-unit, .output-unit'); if (inn) { var t2 = A.txt(inn); if (t2 && t2.length <= 8) return t2; } }
      }
    } catch (e) {}
    return '';
  };

  A._rowLabel = function (el) {
    var p = el.parentElement, hops = 0;
    while (p && hops < 4) {
      var cands = p.querySelectorAll ? p.querySelectorAll('.opt-label, .opt-output-label, .groundops_ui_label, .field-label, .input-label, label') : [];
      for (var i = 0; i < cands.length; i++) {
        var lblEl = cands[i];
        if (lblEl === el || lblEl.contains(el)) continue;
        // Skip value-DISPLAY labels: the weather widget renders Temp/Wind/Dewpoint/QNH as
        // <label class="pmdg_measurement ...">28</label> (a value, not a field label). Without
        // this the "Toggle Weather" checkbox + "Weather Icao" field borrowed the temperature.
        var lc = (typeof lblEl.className === 'string') ? lblEl.className : '';
        if (lc.indexOf('pmdg_measurement') >= 0 || lc.indexOf('output-unit') >= 0) continue;
        var lt = A.txt(lblEl);
        // A real field label contains letters; a bare number ("28" / "1021" / "020/4") is a value.
        if (lt && lt.length < 60 && /[A-Za-z]/.test(lt)) return lt;
      }
      p = p.parentElement; hops++;
    }
    return '';
  };

  // Geometry pairing: nearest text item to the LEFT on the same row (by vertical CENTRE, so a
  // control vertically centred inside a taller label row still matches), else directly ABOVE
  // (label-on-top layouts). requireLabel restricts to label-class cells (_isLabel) — used for
  // buttons so status-bar buttons never grab the clock/sim-rate text. VALUE-display items
  // (_isValue, e.g. a pmdg_measurement reading) are never eligible as a field label — this is
  // what stops a measurement (which carries a unit, so it has letters) from bleeding onto a
  // control regardless of layout; geometry alone is not relied upon.
  A._rowTextFor = function (el, textItems, requireLabel) {
    var cr = null; try { cr = el.getBoundingClientRect(); } catch (e) { return null; }
    if (!cr) return null;
    var cCy = cr.top + cr.height / 2, cLeft = Math.round(cr.left), best = null, bestDx = 1e9;
    for (var t = 0; t < textItems.length; t++) {
      var ti = textItems[t];
      if (ti._consumed || ti._isValue || ti._cy == null || ti._left == null) continue;
      if (requireLabel && !ti._isLabel) continue;
      if (!/[A-Za-z]/.test(ti.label || '')) continue;
      if (Math.abs(ti._cy - cCy) > 24) continue;
      if (ti._left > cLeft + 4) continue;
      var dx = cLeft - ti._left; if (dx < bestDx) { bestDx = dx; best = ti; }
    }
    if (best) return best;
    var bestDy = 1e9;
    for (var u = 0; u < textItems.length; u++) {
      var ai = textItems[u];
      if (ai._consumed || ai._isValue || ai._top == null || ai._left == null) continue;
      if (requireLabel && !ai._isLabel) continue;
      if (!/[A-Za-z]/.test(ai.label || '')) continue;
      var dy = cr.top - ai._top; if (dy <= 0 || dy > 40) continue;
      if (Math.abs(ai._left - cLeft) > 30) continue;
      if (dy < bestDy) { bestDy = dy; best = ai; }
    }
    return best;
  };

  // Returns {text, src}. Priority depends on control type:
  //  - form controls (text/select/checkbox/radio/range): aria -> label-for -> row-label -> id
  //  - buttons/status/links/headings: aria -> own-text -> fa-icon -> title -> id
  // NEVER let a button borrow a neighbour's row-label (that bled "Pause at ToD" onto the status bar).
  A.label = function (el, type) {
    try {
      var al = el.getAttribute && el.getAttribute('aria-label'); if (al) return { text: al.trim(), src: 'aria' };
      var lb = el.getAttribute && el.getAttribute('aria-labelledby');
      if (lb) { var t = document.getElementById(lb); if (t) return { text: A.txt(t), src: 'labelledby' }; }
      var isFormControl = (type === 'text' || type === 'select' || type === 'checkbox' || type === 'radio' || type === 'range' || type === 'progressbar');
      if (isFormControl) {
        if (el.id) { var fl = document.querySelector('label[for="' + el.id + '"]'); if (fl) return { text: A.txt(fl), src: 'label-for' }; }
        var rl = A._rowLabel(el); if (rl) return { text: rl, src: 'row-label' };
        var idl = A.idToLabel(el.id); if (idl) return { text: idl, src: 'id' };
      } else {
        // visible text (own or from a child, e.g. the home tiles' inner span) beats id.
        // Buttons stay short; headings/status/links may be a sentence (Manuals H2).
        var cap = (type === 'button') ? 48 : 140;
        var full = A.txt(el); if (full && full.length > 0 && full.length <= cap) return { text: full, src: 'text' };
        // Persistent chrome (nav rail / status bar) icon-only buttons: the element id is the
        // authoritative app/control name; the shared FA glyph is an unreliable guess (file-lines
        // for Paperwork, arrows-rotate for Restart). Prefer id when there is no own text.
        if (type === 'button' && !full) {
          var cls0 = (typeof el.className === 'string') ? el.className : '';
          if (cls0.indexOf('efb_main_menu_button') >= 0 || /^statusbar_/.test(el.id || '')) {
            var idc = A.idToLabel(el.id); if (idc) return { text: idc, src: 'id' };
          }
        }
        var fa = A.faName(el); if (fa) return { text: fa, src: 'fa-icon' };
        var title = el.getAttribute && (el.getAttribute('title') || el.getAttribute('alt')); if (title) return { text: title, src: 'title' };
        var idl2 = A.idToLabel(el.id); if (idl2) return { text: idl2, src: 'id' };
      }
      // enclosing heading (LAST resort — this is the bleed to minimize)
      var p2 = el.parentElement, h2 = 0;
      while (p2 && h2 < 4) {
        var hd = p2.querySelector ? p2.querySelector('h1,h2,h3,h4') : null;
        if (hd && !hd.contains(el)) { var ht = A.txt(hd); if (ht) return { text: ht, src: 'heading-bleed' }; }
        p2 = p2.parentElement; h2++;
      }
      return { text: '', src: 'none' };
    } catch (e) { return { text: '', src: 'err' }; }
  };

  A.classify = function (el) {
    var tag = el.tagName;
    var role = (el.getAttribute && el.getAttribute('role')) || '';
    var cls = (typeof el.className === 'string') ? el.className : '';
    if (tag === 'INPUT') {
      var ty = (el.getAttribute('type') || 'text').toLowerCase();
      if (ty === 'checkbox' || ty === 'radio') return ty;
      if (ty === 'range') return 'range';
      if (ty === 'hidden') return '';
      return 'text';
    }
    if (tag === 'TEXTAREA') return 'text';
    if (tag === 'SELECT' || cls.indexOf('custom-select') >= 0 || role === 'combobox' || role === 'listbox') return 'select';
    if (role === 'progressbar') return 'progressbar';
    if (/^H[1-6]$/.test(tag) || role === 'heading') return 'heading';
    if (tag === 'A' || role === 'link') return 'link';
    if (role === 'status' || (el.getAttribute && el.getAttribute('aria-live'))) return 'status';
    if (tag === 'BUTTON' || cls.indexOf('button') >= 0 || cls.indexOf('btn') >= 0 || role === 'button' || role === 'tab' ||
        (el.getAttribute && el.getAttribute('onclick')) || el.onclick) return 'button';
    try {
      var cs2 = window.getComputedStyle(el);
      if (cs2.cursor === 'pointer') {
        var own = A.ownText(el);
        if ((own && own.length < 40) || (el.children.length <= 2 && A.txt(el).length < 40)) return 'button';
      }
    } catch (e) {}
    return '';
  };

  A.collect = function () {
    // Clear stale stamps from prior scrapes — a now-hidden element (e.g. a Preferences input)
    // keeping an old data-pmdg-efb-idx makes _byIdx resolve the WRONG element (DOM-order first).
    var old = document.querySelectorAll('[data-pmdg-efb-idx]');
    for (var o = 0; o < old.length; o++) old[o].removeAttribute('data-pmdg-efb-idx');
    var nodes = document.querySelectorAll('*');
    var out = [], idx = 0, emittedBtns = [];
    var lastText = null, lastTextTop = null;  // for same-row text-fragment merging (NOTAM grids etc.)
    var textItems = [];  // emitted text items w/ rect, for two-column label↔control pairing
    var skipUnder = null;  // when set, skip the subtree of this element (handled as a pre block)
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      if (skipUnder) { if (skipUnder.contains(el)) continue; skipUnder = null; }
      if (!A.isVisible(el)) continue;
      // Leaflet map markers (aircraft/waypoint icons) are visual-only and flood the list as empty
      // clickable buttons after a route loads (31+). Skip the marker SUBTREE (inner icon divs also
      // classify as buttons). Waypoint name labels live in a separate pane, so they're unaffected.
      // Leaflet map is a graphic: tiles, markers, the route line and waypoint TOOLTIPS carry no
      // readable content for a blind pilot (the route is read via the FMC/CDU + Flight Details).
      // Skip the whole .leaflet-container subtree — its tooltip labels otherwise flood the list.
      // Map control buttons + the source select are SIBLINGS of .leaflet-container, so kept.
      var clsLeaf = (typeof el.className === 'string' ? el.className : '');
      if (clsLeaf.indexOf('leaflet-container') >= 0 || clsLeaf.indexOf('leaflet-marker-icon') >= 0) { skipUnder = el; continue; }
      // The EFB alert/confirmation card (#alert_card: heading + message + OK button — e.g.
      // "Success / Tablet preferences were updated.") is emitted as ONE announce-flagged 'alert'
      // item by a dedicated pass below, so the screen reader speaks it the moment it pops up
      // (it otherwise rendered as silent text and was never read). Skip the card's heading/message
      // here to avoid a duplicate; KEEP the OK button (#alert_card_button) so it stays clickable.
      if (el.id !== 'alert_card_button') { try { if (el.closest && el.closest('#alert_card')) continue; } catch (eac) {} }
      // Preformatted block (the SimBrief OFP is ONE <pre> of 800+ fragments). Emit its OWN
      // line breaks verbatim — that's the briefing exactly as a sighted pilot reads it
      // (aligned columns, fuel table, NOTAMs) — and skip its children so they don't fragment.
      var ws = ''; try { ws = window.getComputedStyle(el).whiteSpace; } catch (e) {}
      if ((el.tagName === 'PRE' || ws.indexOf('pre') === 0) && el.children.length > 0) {
        var block = el.innerText || el.textContent || '';
        if (block.indexOf('\n') >= 0 && block.length > 40) {
          var lns = block.split('\n');
          for (var li = 0; li < lns.length; li++) {
            var s = lns[li].replace(/\s+$/, '');
            if (!s.trim()) continue;
            if (s.length > 400) s = s.substring(0, 400);
            idx++;
            out.push({ idx: idx, type: 'pre', tag: 'PRE', label: s, src: 'pre-line' });
          }
          lastText = null;
          skipUnder = el;
          continue;
        }
      }
      var type = A.classify(el);
      if (!type) {
        // Status-bar icon indicators (Signal/Battery): no text, not clickable, FA unmapped — the
        // generic passes drop them. Surface as read-only statics, in DOM order, so the bar matches
        // the sighted view. (Actionable status-bar items — restart/home/autocruise — classify above.)
        if (el.id && /^statusbar_/.test(el.id) && el.closest && el.closest('#StatusBar') &&
            el.querySelector && el.querySelector('i[class*="fa-"]') && !A.txt(el)) {
          var snm = A.idToLabel(el.id);
          if (snm) {
            var sval = A.statusbarValue(el, snm);
            idx++; el.setAttribute('data-pmdg-efb-idx', String(idx));
            out.push({ idx: idx, type: 'text-content', tag: el.tagName, label: (sval ? (snm + ': ' + sval) : snm), src: 'statusbar-indicator' });
            lastText = null; continue;
          }
        }
        // READABLE TEXT capture — leaf own-text not part of any control/heading/label cell.
        // Surfaces document text (OFP briefing, Navdata, Manuals, Dashboard values) that the
        // control-only scrape misses. ownText = direct text nodes only, so a wrapper div with
        // text in children never double-counts; the leaf bearing the text emits once.
        var own = A.ownText(el);
        if (!own || own.length === 0 || own.length > 400) continue;
        // Single-character standalone text is a decorative graphic glyph, never real EFB content
        // (e.g. the Take-Off page runway-designator diagram renders "35R"/"17L" as per-character
        // nodes "3","5","R","1","7","L"). Skip them BEFORE the same-row merge so "R" + "1" can't
        // merge into "R 1". Real values/units live in labeled fields or .input-unit spans.
        if (own.replace(/\s+/g, '').length <= 1) continue;
        // A pmdg_measurement <label> is a VALUE display (ZFW / Route Dist / weather temp), not a
        // form label — capture it (with its sibling unit) instead of skipping it as a <label>.
        // It is tagged _isValue below so it can never be paired AS a control's label (the unit
        // makes it letter-bearing, so the letter-guard alone would not exclude it).
        // EXCEPT a label OWNED by a dedicated output pass (.groundops_ui_outputlabel / .opt-output):
        // those also carry pmdg_measurement, and the dedicated pass emits them as a NAMED line
        // ("Target Fuel: 5400 kg"). Capturing them here too produced an orphan bare "5400 kg"
        // duplicate (issue #113 — masked while the value was the single-char "0", which the
        // glyph-skip above drops). Treat them as non-measurement so the label-skip below owns them.
        var isMeasure = (el.matches && el.matches('label.pmdg_measurement') &&
                         !el.matches('.groundops_ui_outputlabel, .opt-output'));
        if (!isMeasure && el.closest && el.closest('button, a, select, .custom-select, label, [role=button], [role=tab], [role=link], [role=heading], h1, h2, h3, h4, h5, h6')) continue;
        if (isMeasure) {
          var mu = el.parentElement && el.parentElement.querySelector ? el.parentElement.querySelector('.output-unit') : null;
          var mut = mu ? A.txt(mu) : '';
          if (mut) own = own + ' ' + mut;
        }
        // unit adornment spans (.input-unit "ft"/"kg", .output-unit "kt") belong WITH their
        // field, not as standalone text — skip them here (append-to-field is a polish item).
        if (el.closest && el.closest('.input-unit, .output-unit')) continue;
        if (/leaflet-/.test(typeof el.className === 'string' ? el.className : '')) continue;
        // merge with the previous text fragment if on the same visual row (NOTAM/table grids)
        var ttop = null; try { ttop = Math.round(el.getBoundingClientRect().top); } catch (e) {}
        if (lastText && ttop !== null && lastTextTop !== null && Math.abs(ttop - lastTextTop) <= 6) {
          lastText.label += ' ' + own;
          continue;
        }
        idx++;
        el.setAttribute('data-pmdg-efb-idx', String(idx));
        var tleft = null, tcy = null;
        try { var trc = el.getBoundingClientRect(); tleft = Math.round(trc.left); tcy = trc.top + trc.height / 2; } catch (e) {}
        var isLbl = false; try { isLbl = !!(el.closest && el.closest('.preflabel, .opt-label, .opt-output-label, .groundops_ui_label, .field-label, .input-label, .popup-label')); } catch (e2) {}
        var titem = { idx: idx, type: 'text-content', tag: el.tagName, label: own, src: 'text-content', _top: ttop, _left: tleft, _cy: tcy, _isLabel: isLbl, _isValue: !!isMeasure };
        out.push(titem);
        textItems.push(titem);
        lastText = titem; lastTextTop = ttop;
        continue;
      }
      lastText = null;  // a non-text control breaks the same-row merge run
      // Skip empty heading/status wrappers (layout-only; the OFP renders dozens of them).
      if ((type === 'heading' || type === 'status') && !A.txt(el)) continue;
      if (type === 'button') {
        var nested = false;
        for (var b = 0; b < emittedBtns.length; b++) { if (emittedBtns[b].contains(el)) { nested = true; break; } }
        if (nested) continue;
        if (el.querySelector && el.querySelector('input,select,textarea,.custom-select')) continue;
        // a custom-select's own trigger / option is part of the select, not a separate button
        if (el.closest && el.closest('.custom-select, select')) continue;
        // GROUP container: if it holds real clickable descendants (e.g. a btn-group of tabs),
        // emit those, not the wrapper. Tiles (single non-clickable child span) have none → emitted.
        if (el.querySelector && el.querySelector('button, a[href], [role=button], [role=tab]')) continue;
        emittedBtns.push(el);
      }
      idx++;
      el.setAttribute('data-pmdg-efb-idx', String(idx));
      var lab = A.label(el, type);
      var item = { idx: idx, type: type, tag: el.tagName, role: (el.getAttribute('role') || ''), label: lab.text, src: lab.src };
      if (lab.src === 'heading-bleed' || lab.src === 'none') {
        item.hint = { id: el.id || '', fa: A.faName(el), cls: (typeof el.className === 'string' ? el.className : '').substring(0, 40) };
      }
      // Two-column form pairing: prefer a same-row text label to the LEFT over id-derivation
      // (Preferences etc. render labels in a separate column from the controls).
      if (type === 'text' || type === 'select' || type === 'checkbox' || type === 'radio' || type === 'range') {
        if (item.src === 'id' || item.src === 'none' || item.src === 'heading-bleed') {
          var best = A._rowTextFor(el, textItems, false);
          if (best) { item.label = best.label; item.src = 'row-text'; best._consumed = true; }
        }
      }
      if (type === 'button') { var pl = A._pairLabel(el); if (pl && pl !== item.label) item.pair = pl; item.active = A.activeMarker(el); item._el = el; }
      // Buttons in a two-column layout (Preferences Sign Out / Factory Reset) keep their own
      // text but gain the left label cell as context ("Navigraph Authentication: Sign Out").
      // requireLabel=true so chrome buttons (Home etc.) never claim clock/sim-rate text.
      if (type === 'button' && !item.pair) {
        var bbest = A._rowTextFor(el, textItems, true);
        if (bbest) { item.pair = bbest.label; bbest._consumed = true; }
      }
      if (el.id) item._id = el.id;
      if (type === 'heading') { var lv = el.getAttribute('aria-level'); item.level = lv ? Number(lv) : Number((el.tagName.match(/H([1-6])/) || [0, 0])[1]); }
      if (type === 'text' || type === 'range') {
        item.value = String(el.value || '');
        var fu = A.fieldUnit(el);
        if (fu) { if (item.value) item.value += ' ' + fu; else item.label += ' (' + fu + ')'; }
      }
      if (type === 'checkbox' || type === 'radio') {
        item.checked = !!el.checked;
        var caps = A.toggleCaptions(el); if (caps.length) item.captions = caps;
        // A known UNIT toggle is a TEXTLESS checkbox whose active unit can only be read from
        // el.checked (see UNIT_PAIRS). Render it as a 2-option SELECT — "Length unit: feet / meters"
        // — which is far clearer for a screen reader than a "checked/unchecked" checkbox, and reads
        // the LIVE unit (the old ::after / saved-Settings read showed a stuck value because the
        // captions are empty and Settings lags until Save). Selecting an option toggles the checkbox
        // (see setValue). type is overridden to 'select' so the form renders a combo box.
        if (el.id && A.UNIT_PAIRS[el.id]) {
          var pr = A.UNIT_PAIRS[el.id];
          item.type = 'select';
          item.options = [A.unitWord(pr[0]), A.unitWord(pr[1])];
          item.value = A.unitWord(pr[el.checked ? 1 : 0]);
        } else if (el.id && /unit/i.test(el.id)) {
          // Unit toggle with no confirmed pair (speed/airspeed): best-effort saved value.
          var active = caps.length >= 2 ? caps[1] : (caps.length === 1 ? caps[0] : '');
          if (active) item.value = A.unitWord(active);
          else { var au = A.activeUnit(el); if (au) item.value = A.unitWord(au); }
        } else { var au2 = A.activeUnit(el); if (au2) item.value = A.unitWord(au2); }
      }
      if (type === 'range') { item.min = el.getAttribute('min'); item.max = el.getAttribute('max'); item.step = el.getAttribute('step'); }
      if (type === 'select') {
        var selOpt = el.querySelector('.selected-option');
        item.value = selOpt ? A.txt(selOpt) : String(el.value || el.getAttribute('data-selected') || '');
        var opts = el.querySelectorAll('.option, option');
        var ov = []; for (var k = 0; k < opts.length && k < 40; k++) ov.push(A.txt(opts[k]) || opts[k].getAttribute('data-value') || '');
        item.options = ov;
      }
      if (type === 'progressbar') item.value = el.getAttribute('aria-valuenow') || A.txt(el);
      if (el.disabled || (el.getAttribute && el.getAttribute('aria-disabled') === 'true')) item.disabled = true;
      // Drop a button with NO discernible name (no text, no fa-icon, no title/id/aria, no row-pair).
      // After a route/weather load the Dashboard map sprouts unlabeled overlay/control buttons that
      // the leaflet-marker-icon skip misses; an unnamed button reads as "(button)" and is useless to
      // a screen reader. A genuinely useful control resolves a label via one of the above sources.
      if (type === 'button' && !item.label && !item.pair) continue;
      out.push(item);
    }
    // Performance OUTPUT panel (Take Off / Landing): the computed results (V1/VR/V2/VRef, Flaps,
    // %N1, RTG, Trim, Sel Temp, Accel Height, Weight) render as <label class="opt-output">value
    // cells — skipped by the generic <label> text-skip, so the whole results panel was invisible
    // after Calculate. Emit each as a readable "Name: value unit" line (the name comes from the
    // row's .opt-output-label, the unit from the sibling .output-unit). Only the visible perf
    // sub-page is captured (the hidden Take Off / Landing panels are display:none → isVisible false).
    var outs = document.querySelectorAll('.opt-output');
    for (var oi = 0; oi < outs.length; oi++) {
      var ov = outs[oi];
      if (!A.isVisible(ov)) continue;
      var oval = A.txt(ov);
      var rowc = ov.closest ? ov.closest('.row') : null;
      var nameEl = rowc && rowc.querySelector ? rowc.querySelector('.opt-output-label') : null;
      var onm = nameEl ? A.txt(nameEl).replace(/:\s*$/, '') : (ov.id ? A.idToLabel(ov.id) : '');
      if (!onm) continue;
      var unitEl = ov.parentElement && ov.parentElement.querySelector ? ov.parentElement.querySelector('.output-unit') : null;
      var ounit = unitEl ? A.txt(unitEl) : '';
      idx++;
      out.push({ idx: idx, type: 'text-content', tag: 'LABEL', label: oval ? (onm + ': ' + oval + (ounit ? ' ' + ounit : '')) : onm, src: 'output-row' });
    }
    // GROUND OPS output values (Automated Ground Ops + every ground-ops sub-page): the live
    // readouts — Turn Time, Turn Time Remaining, Fuel Uplift Remaining, Target Fuel, Uplift, ... —
    // render as <label class="groundops_ui_outputlabel">value, preceded by a sibling
    // <label class="groundops_ui_label">name (and a <br>), with an optional <span class="output-unit">.
    // Like .opt-output above, the generic <label> text-skip dropped ALL of these, so the page showed
    // only the settable Plan-Fuel input + the section headings — issue #113 ("turnaround time … not
    // there"). Emit each visible one as a readable "Name: value unit" line. Skip empty values so a
    // not-yet-running turn doesn't read a wall of blanks.
    var gouts = document.querySelectorAll('.groundops_ui_outputlabel');
    for (var gi = 0; gi < gouts.length; gi++) {
      var gv = gouts[gi];
      if (!A.isVisible(gv)) continue;
      var gval = A.txt(gv);
      if (!gval) continue;
      // Name = the nearest PRECEDING .groundops_ui_label sibling (skip <br>/text between them).
      var gnm = '';
      var ps = gv.previousElementSibling;
      while (ps) { if (ps.className && String(ps.className).indexOf('groundops_ui_label') >= 0) { gnm = A.txt(ps).replace(/:\s*$/, ''); break; } if (ps.tagName !== 'BR') { var pn = A.txt(ps); if (pn) break; } ps = ps.previousElementSibling; }
      if (!gnm) gnm = gv.id ? A.idToLabel(gv.id) : '';
      if (!gnm) continue;
      // Unit = a following .output-unit sibling, else one inside the parent.
      var gu = '';
      var ns = gv.nextElementSibling;
      if (ns && ns.className && String(ns.className).indexOf('output-unit') >= 0) gu = A.txt(ns);
      if (!gu && gv.parentElement && gv.parentElement.querySelector) { var gue = gv.parentElement.querySelector('.output-unit'); if (gue) gu = A.txt(gue); }
      idx++;
      out.push({ idx: idx, type: 'text-content', tag: 'LABEL', label: gnm + ': ' + gval + (gu ? ' ' + gu : ''), src: 'groundops-output' });
    }
    // GROUND OPS progress bars (Turnaround / per-service): <div class="progress_bar"> holds a
    // <label class="progress_label">Name<span>- NN%</span>. Emit "Name: NN%" so the screen reader
    // gets the turnaround/service completion percentage (the visual bar alone is meaningless).
    var pbars = document.querySelectorAll('.progress_bar');
    for (var pbi = 0; pbi < pbars.length; pbi++) {
      var pb = pbars[pbi];
      if (!A.isVisible(pb)) continue;
      var plab = pb.querySelector ? pb.querySelector('.progress_label') : null;
      if (!plab) continue;
      var pfull = A.txt(plab);                 // e.g. "Turnaround - 0%"
      var pspan = plab.querySelector ? plab.querySelector('span') : null;
      var ppct = pspan ? A.txt(pspan) : '';    // e.g. "- 0%"
      var pname = pfull;
      if (ppct) { pname = pfull.replace(ppct, '').trim(); }      // strip the % off the name
      var pclean = ppct.replace(/^[\s\-]+/, '').trim();          // "- 0%" -> "0%"
      if (!pname) pname = (pb.id ? A.idToLabel(pb.id) : 'Progress');
      idx++;
      out.push({ idx: idx, type: 'text-content', tag: 'LABEL', label: pname + (pclean ? ': ' + pclean : ''), src: 'groundops-progress' });
    }
    // ALERT / confirmation card (#alert_card: heading + message + OK button). These pop up on
    // Save Preferences, errors, etc. ("Success — Tablet preferences were updated.") and previously
    // rendered as silent text that was never announced — issue: the screen reader didn't read them.
    // Emit ONE item flagged for ASSERTIVE announcement (type 'alert'); the host form speaks it the
    // moment it appears (see FbwEfbForm). The card's heading/message are skipped in the main loop
    // (above) so this is the single representation; the OK button is emitted separately + clickable.
    var alertCard = document.getElementById('alert_card');
    if (alertCard && A.isVisible(alertCard)) {
      var ah = document.getElementById('alert_card_heading');
      var am = document.getElementById('alert_card_message');
      var ahTxt = ah ? A.txt(ah) : '';
      // The message packs multiple sentences with no separating space ("updated.Hoppie ID …")
      // because the source <p> joins separate text nodes; re-insert a space after .!? before a
      // capital LETTER (a new sentence) so the screen reader reads "updated. Hoppie ID …" instead
      // of one run-on token. Only [A-Z] — a digit after a dot is a decimal ("12.5"), never a new
      // sentence, so it must not be split into "12. 5".
      var amTxt = am ? A.txt(am).replace(/([.!?])([A-Z])/g, '$1 $2') : '';
      var alertLabel = ahTxt && amTxt ? (ahTxt + ': ' + amTxt) : (ahTxt || amTxt);
      if (alertLabel) { idx++; out.push({ idx: idx, type: 'alert', tag: 'DIV', label: alertLabel, src: 'alert-card' }); }
    }
    // Drop standalone text that duplicates a control's label, and UPGRADE a weak id-derived
    // control label to a fuller standalone text that contains it ("Brightness" -> "Tablet
    // Brightness"). Both remove redundant text lines next to their control.
    var norm = function (s) { return String(s || '').trim().toLowerCase(); };
    // Readable standalone text (NOT input fields, which are type 'text', and NOT 'pre' document lines).
    var isReadable = function (t) { return t === 'text-content' || t === 'label-value'; };
    var ctrls = [];
    for (var c1 = 0; c1 < out.length; c1++) if (!isReadable(out[c1].type) && out[c1].type !== 'pre' && out[c1].label) ctrls.push(out[c1]);
    for (var c2 = 0; c2 < out.length; c2++) {
      if (!isReadable(out[c2].type)) continue;
      var tn = norm(out[c2].label);
      for (var c3 = 0; c3 < ctrls.length; c3++) {
        var c = ctrls[c3], cn = norm(c.label);
        if (cn === tn) { out[c2]._consumed = true; break; }
        if (c.src === 'id' && cn.length >= 5 && tn.length <= cn.length + 14) {
          var mi = tn.indexOf(cn);
          // whole-word containment: match at start or after a space, and ending the string
          // (prevents "Speed Unit" matching inside "AirSpeed Unit").
          var boundary = (mi === 0 || (mi > 0 && tn.charAt(mi - 1) === ' '));
          if (mi >= 0 && boundary && (mi + cn.length === tn.length || mi === 0)) {
            c.label = out[c2].label; c.src = 'text-upgrade'; out[c2]._consumed = true; break;
          }
        }
      }
    }
    // Disambiguate duplicate-text buttons via their id qualifier (W&B: 3 "Randomize" / 3 "Uplift").
    var btnCount = {};
    for (var d1 = 0; d1 < out.length; d1++) if (out[d1].type === 'button' && !out[d1]._consumed) btnCount[norm(out[d1].label)] = (btnCount[norm(out[d1].label)] || 0) + 1;
    for (var d2 = 0; d2 < out.length; d2++) {
      var bb = out[d2];
      if (bb.type !== 'button' || bb._consumed || !bb._id || bb.pair) continue;
      if ((btnCount[norm(bb.label)] || 0) < 2) continue;
      var q = A.idToLabel(bb._id);
      if (!q) continue;
      // Two icon-only buttons collide on a generic glyph word (e.g. after a weather load BOTH the
      // status-bar refresh and the weather-search button show fa "Refresh"). The id-derived name is
      // self-descriptive, so USE IT as the label ("Weather Search" / "Statusbar Restart") rather than
      // the clunky "Weather Search Button: Refresh". A button with real visible TEXT keeps the
      // qualifier prefix instead (W&B "Pax Level: Randomize" stays distinct + meaningful).
      if (bb.src === 'fa-icon') { if (norm(q) !== norm(bb.label)) { bb.label = q; bb.src = 'id'; } continue; }
      // Strip every word of the button label out of the id qualifier (e.g. "Import Ofp To Wt"
      // for two "Import From OFP" buttons -> "To Wt"); "Pax Level Randomize" -> "Pax Level".
      var lw = norm(bb.label).split(/\s+/);
      for (var w = 0; w < lw.length; w++) { if (lw[w].length > 1) q = q.replace(new RegExp('\\b' + lw[w].replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\b', 'ig'), ''); }
      q = q.replace(/\s+/g, ' ').trim();
      // If what's left is junk (empty / only tiny noise words), pair by the SECTION heading the
      // sighted user reads them under; otherwise keep the meaningful id qualifier (W&B "Pax Level").
      var NOISE = { to: 1, of: 1, the: 1, wt: 1, a: 1, 'in': 1, 'for': 1, on: 1 };
      var meaningful = q && q.split(/\s+/).some(function (x) { return x.length > 2 && !NOISE[x.toLowerCase()]; });
      if (!meaningful) {
        var sect = bb._el ? A._sectionHeading(bb._el) : '';
        if (sect && norm(sect) !== norm(bb.label)) bb.pair = sect;
        continue;
      }
      if (norm(q) !== norm(bb.label)) bb.pair = q;
    }
    var kept = out.filter(function (x) { return !x._consumed; });
    // Merge a colon-ending label heading with its immediately-following value
    // (Dashboard Flight Details: "CALLSIGN:" + "BVI214" -> "CALLSIGN: BVI214").
    var merged = [];
    for (var m = 0; m < kept.length; m++) {
      var it = kept[m];
      if (it.type === 'heading' && /:\s*$/.test(it.label || '') && m + 1 < kept.length) {
        var nx = kept[m + 1];
        if ((nx.type === 'heading' || nx.type === 'text-content' || nx.type === 'label-value') && nx.label && nx.label.length < 60 && !/:\s*$/.test(nx.label)) {
          merged.push({ idx: it.idx, type: 'label-value', tag: it.tag, label: it.label.replace(/\s+$/, '') + ' ' + nx.label, src: 'label-value' });
          m++; continue;
        }
      }
      merged.push(it);
    }
    // Dashboard route header: the origin/destination/time h3s render as bare codes. Label them
    // faithfully (sighted layout: IATA ICAO  ✈  ICAO IATA, with STD/STA below).
    var byId = {};
    for (var r0 = 0; r0 < merged.length; r0++) if (merged[r0]._id) byId[merged[r0]._id] = merged[r0];
    var combine = function (primaryId, otherId, prefix, primaryFirst) {
      var p = byId['efb_dashboard_' + primaryId], o2 = byId['efb_dashboard_' + otherId];
      if (!p) return;
      var a = primaryFirst ? (p.label || '') : (o2 ? o2.label : '');
      var b = primaryFirst ? (o2 ? o2.label : '') : (p.label || '');
      if (!a && !b) return;  // both codes empty — leave as-is (no dangling "Origin: ")
      p.label = prefix + ': ' + (a && b ? (a + ' / ' + b) : (a || b));
      if (o2) o2._consumed = true;
    };
    var prefixOne = function (id2, prefix) { var it2 = byId['efb_dashboard_' + id2]; if (it2 && !/^\s*$/.test(it2.label || '') && it2.label.indexOf(prefix + ':') !== 0) it2.label = prefix + ': ' + it2.label; };
    combine('originiata', 'originicao', 'Origin', true);                 // "Origin: LAX / KLAX"
    combine('destinationicao', 'destinationiata', 'Destination', true);  // "Destination: KDFW / DFW"
    prefixOne('std', 'STD');
    prefixOne('sta', 'STA');
    merged = merged.filter(function (x) { return !x._consumed; });

    // Synthesize selects on the Preferences page for config-only settings the tablet renders no UI
    // for (Weather Source / Time Format / Map Provider). Gated on the Preferences page being shown
    // + Settings available; each reads its active value (driven back in setValue's synthetic branch).
    try {
      var prefsEl = document.querySelector('#efb_preferences');
      var Sx = A.settingsObj();
      if (prefsEl && A.isVisible(prefsEl) && Sx && typeof Sx.updateSetting === 'function') {
        var synItems = [];
        for (var sp = 0; sp < A.SYN_PREFS.length; sp++) {
          var def = A.SYN_PREFS[sp];
          if (Sx[def.key] == null) continue;
          var cur = String(Sx[def.key]).toLowerCase(), disp = def.opts[0][0], optTexts = [];
          for (var oo = 0; oo < def.opts.length; oo++) { optTexts.push(def.opts[oo][0]); if (String(def.opts[oo][1]).toLowerCase() === cur) disp = def.opts[oo][0]; }
          synItems.push({ idx: def.idx, type: 'select', tag: 'SELECT', label: def.label, value: disp, options: optTexts, src: 'synthetic' });
        }
        if (synItems.length) {
          // Place them right AFTER the last Preferences FORM CONTROL (e.g. Map Zoom Step), so they
          // join the settings list before the action buttons. Anchor on a non-button control, NOT
          // "first efb_preferences_ button" — the nav rail's own #efb_preferences_button matches
          // that id too and would jam the synthetics into the middle of the nav rail.
          var insAfter = -1;
          for (var pw = 0; pw < merged.length; pw++) {
            var mw = merged[pw];
            if (mw.type !== 'button' && mw._id && mw._id.indexOf('efb_preferences_') === 0) insAfter = pw;
          }
          for (var si = 0; si < synItems.length; si++) {
            if (insAfter >= 0) merged.splice(insAfter + 1 + si, 0, synItems[si]); else merged.push(synItems[si]);
          }
        }
      }
    } catch (eWs) {}
    return merged;
  };

  // Map an internal collect() item ({type,label,value,pair,captions,level,options,checked,disabled})
  // to the FbwEfbForm element contract (controlType/kind/clickable/level/value/options/...).
  A._toContract = function (it) {
    var o = { idx: it.idx, text: it.label || '', value: '', controlType: '', kind: '', clickable: false, level: 0, live: '', disabled: !!it.disabled, options: null };
    // fold the row-action pair ("Air Start Unit") into the visible text
    if (it.pair) o.text = it.pair + ': ' + (it.label || '');
    switch (it.type) {
      case 'button': o.kind = 'button'; o.clickable = true; if (it.active) o.text = (o.text || '') + it.active; break;
      case 'link': o.kind = 'link'; break;
      case 'heading': o.kind = 'heading'; o.level = it.level || 0; break;
      case 'status': o.kind = 'static'; o.live = 'polite'; break;
      case 'alert': o.kind = 'alert'; o.live = 'assertive'; break;
      case 'text': case 'number': o.controlType = 'text'; o.value = String(it.value || ''); break;
      case 'range':
        o.controlType = 'range'; o.value = String(it.value || '');
        if (it.min != null) o.min = Number(it.min); if (it.max != null) o.max = Number(it.max); if (it.step != null) o.step = Number(it.step);
        break;
      case 'checkbox': case 'radio':
        o.controlType = 'checkbox';
        // value MUST stay boolean — the form renders the checked state from value==='true'.
        o.value = it.checked ? 'true' : 'false';
        // a unit toggle exposes its ACTIVE unit (e.g. "kg") in it.value; surface it in the LABEL
        // (not the checkbox value), so "Weight Unit" reads "Weight Unit: kg" and stays checkable.
        if (it.value && it.value !== 'true' && it.value !== 'false') o.text = (o.text || '') + ': ' + it.value;
        break;
      case 'select': o.controlType = 'select'; o.value = String(it.value || ''); o.options = it.options || []; break;
      case 'pre': o.kind = 'static'; o.controlType = 'pre'; break;
      case 'text-content': case 'label-value': o.kind = 'static'; break;
      default: o.kind = 'static'; break;
    }
    return o;
  };

  // ---- Dirty gate (MutationObserver): skip collect()'s two full-tree traversals + per-element
  // layout reads on a 600ms poll where the page hasn't actually changed. Chromium 49 (Coherent GT)
  // supports MutationObserver. One observer on document.body with {subtree,childList,characterData,
  // attributes} covers DOM structure/text-node changes and attribute-driven state (the class-based
  // active/highlighted tab markers in A.activeMarker, the disabled attribute, etc).
  //
  // SELF-TRIGGER TRAP (why the observer is disconnected around collect(), not left running): collect()
  // itself mutates the DOM — it stamps/clears the `data-pmdg-efb-idx` attribute on every element it
  // enumerates (see the "Clear stale stamps" pass below + the two setAttribute call sites). Those are
  // real attribute mutations on document.body's subtree, so a permanently-attached attributes:true
  // observer would queue them as MutationRecords; its callback fires as a MICROTASK once the current
  // synchronous script (this whole scrape() call) finishes — i.e. right after we've already returned
  // the JSON, but BEFORE the next poll — which would re-arm `_dirty` from collect()'s OWN bookkeeping
  // on every single scrape and make the gate never engage. Since JS is single-threaded and collect()
  // never yields, NOTHING else can mutate the DOM while it runs, so it's always safe to disconnect the
  // observer for the exact duration of the collect() call and reconnect immediately after — this drops
  // only the self-caused idx-stamp mutations while still catching every genuine page-driven mutation
  // that happens between polls (those occur on the page's own async tasks, fully outside this call).
  //
  // CHECKED-PROPERTY CAVEAT (verified against the WHATWG DOM spec): the Preference UNIT toggles
  // (A.UNIT_PAIRS) are real <input type="checkbox"|"radio"> read via the LIVE `el.checked` IDL
  // property. `.checked` is DECOUPLED from the `checked` CONTENT ATTRIBUTE once it is flipped by
  // either a user tap or script — neither path touches the attribute, so MutationObserver's
  // attributes:true NEVER fires for a toggle flip; this is a real gap in the DOM-mutation signal.
  // Mitigated two ways: (1) a native click DOES fire a bubbling 'change'/'input' event as part of its
  // default action — exactly how our own clickElement()'s el.click() and setValue()'s unit-toggle
  // click() drive the real control — so a capture-phase 'change'/'input' listener on document closes
  // the gap for every realistic input path (real tablet tap or our own driven click); (2) as a
  // last-resort safety net against a hypothetical path that flips .checked with NEITHER a DOM mutation
  // NOR a change/input event (e.g. a future controlled-component re-render setting the property
  // directly), force a full scrape every FORCE_FULL_EVERY polls regardless of the dirty flag, bounding
  // worst-case staleness to a few seconds instead of indefinitely.
  A.OBSERVER_OPTS = { subtree: true, childList: true, characterData: true, attributes: true };
  A.FORCE_FULL_EVERY = 10; // ~6s at the C# client's 600ms poll cadence
  A._dirty = true;
  A._everScraped = false;
  A._pollCount = 0;
  A._observer = null;
  A._markDirty = function () { A._dirty = true; };

  // Defensive install: EnsureConnected re-injects this whole script onto a STILL-OPEN Coherent
  // socket whenever the agent goes missing (eval timeout, page re-evaluated) without necessarily a
  // full page reload — that re-runs this IIFE and overwrites `A` with a brand new object, but any
  // MutationObserver / event listener from a PRIOR injection keeps firing against the orphaned old
  // closure unless torn down first. The live handles are stashed on `window` (which survives across
  // injections into the same page, unlike `A`) so each (re)install disconnects its predecessor —
  // guards double-install without depending on a page reload to clean up.
  A._installObserver = function () {
    try {
      if (window.__MSFSBA_PMDG_EFB_OBS && typeof window.__MSFSBA_PMDG_EFB_OBS.disconnect === 'function') {
        window.__MSFSBA_PMDG_EFB_OBS.disconnect();
      }
    } catch (e) {}
    try {
      var oldH = window.__MSFSBA_PMDG_EFB_HANDLERS;
      if (oldH) {
        try { document.removeEventListener('change', oldH.change, true); } catch (e2) {}
        try { document.removeEventListener('input', oldH.input, true); } catch (e3) {}
      }
    } catch (e) {}
    try {
      var mo = new MutationObserver(A._markDirty);
      mo.observe(document.body, A.OBSERVER_OPTS);
      A._observer = mo;
      window.__MSFSBA_PMDG_EFB_OBS = mo;
    } catch (e) {}
    try {
      document.addEventListener('change', A._markDirty, true);
      document.addEventListener('input', A._markDirty, true);
      window.__MSFSBA_PMDG_EFB_HANDLERS = { change: A._markDirty, input: A._markDirty };
    } catch (e) {}
  };
  A._installObserver();

  A.scrape = function () {
    try {
      A._pollCount++;
      var forceFull = !A._everScraped || (A._pollCount % A.FORCE_FULL_EVERY === 0);
      if (!A._dirty && !forceFull) return JSON.stringify({ ok: true, unchanged: true });
      // Clear BEFORE traversing: any mutation that lands mid-scrape must dirty the NEXT poll, never
      // be silently lost. (In practice nothing CAN mutate the DOM mid-collect(), since JS is
      // single-threaded and collect() never yields — this ordering is the safe-by-construction form
      // regardless.) Disconnect around collect() only — see the SELF-TRIGGER TRAP note above.
      A._dirty = false;
      if (A._observer) { try { A._observer.disconnect(); } catch (e) {} }
      var raw;
      try { raw = A.collect(); }
      finally { if (A._observer) { try { A._observer.observe(document.body, A.OBSERVER_OPTS); } catch (e2) {} } }
      var els = [];
      for (var i = 0; i < raw.length; i++) els.push(A._toContract(raw[i]));
      A._everScraped = true;
      return JSON.stringify({ ok: true, side: A.side(), variant: A.variant(), elements: els });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e && e.message || e) }); }
  };

  A._byIdx = function (idx) { return document.querySelector('[data-pmdg-efb-idx="' + idx + '"]'); };

  // Native .click() is the UNIVERSAL mechanism — verified across home tiles (onclick property),
  // Performance tabs, Preferences checkboxes (toggles), and Ground Ops section buttons
  // (groundops_menu_button — dispatched MouseEvents silently failed on these). It fires the
  // onclick property, addEventListener listeners, React onClick, AND default actions (checkbox
  // toggle). NEVER combine native + dispatched (double-fires: checkbox no-toggle, tile in/out).
  // Dispatched MouseEvents are only needed for custom-select option picking (see _setSelect).
  A.clickElement = function (idx) {
    var el = A._byIdx(idx); if (!el) return 'NO_EL';
    try { el.click(); return 'OK'; } catch (e) { return 'ERR:' + e.message; }
  };

  // Apply a synthesized Preferences control (no DOM element). Resolves the chosen display text to
  // the setting value, then drives the native setter(s). selected_map additionally switches the
  // live map and skips persistence when an unauthenticated Navigraph switch would be blocked
  // (so the shown value never desyncs from the actual map).
  A._applySynthetic = function (idx, val) {
    var S = A.settingsObj();
    if (!S || typeof S.updateSetting !== 'function') return 'NO_SETTINGS';
    var def = null;
    for (var i = 0; i < A.SYN_PREFS.length; i++) if (A.SYN_PREFS[i].idx === idx) def = A.SYN_PREFS[i];
    if (!def) return 'UNKNOWN_SYN';
    var target = def.opts[0][1];
    for (var o = 0; o < def.opts.length; o++) if (String(def.opts[o][0]).toLowerCase() === String(val).toLowerCase()) target = def.opts[o][1];
    try {
      if (def.key === 'weather_source') {
        // use the live enum if present (guards against a future literal change; updateSetting validates).
        var W = (typeof window !== 'undefined' && window.WeatherSource) ? window.WeatherSource : null;
        if (W) target = /sim/i.test(String(val)) ? (W.SIM || target) : (W.AWC || target);
      } else if (def.key === 'selected_map') {
        var dm = (typeof window !== 'undefined' && window.DualMap) ? window.DualMap : (typeof DualMap !== 'undefined' ? DualMap : null);
        var dash = (typeof window !== 'undefined' && window.Dashboard) ? window.Dashboard : (typeof Dashboard !== 'undefined' ? Dashboard : null);
        var dmap = (dash && dash.dualMap) ? dash.dualMap : null;
        if (target === 'navigraph' && dm && dm.isNGAuthed === false) {
          if (dmap && typeof dmap.switchMap === 'function') dmap.switchMap('navigraph');  // shows the auth alert; no switch
          return 'NOAUTH';
        }
        if (dmap && typeof dmap.switchMap === 'function') dmap.switchMap(target);
        S.updateSetting('selected_map', target);
        return 'OK:' + target;
      }
      S.updateSetting(def.key, target);
      return 'OK:' + target;
    } catch (e) { return 'ERR:' + e.message; }
  };

  A.setValue = function (idx, val) {
    var ni = Number(idx);
    if (ni >= 990000 && ni < 991000) return A._applySynthetic(ni, val);
    var el = A._byIdx(idx); if (!el) return 'NO_EL';
    val = String(val);
    try {
      // A unit toggle is reported as a 2-option SELECT but is really a checkbox (or, defensively, a
      // radio — collect() rewrites EITHER into the select): selecting a unit means "set the input to
      // the state that yields that unit" — click only if it must flip. Must accept 'radio' too, or a
      // radio-typed toggle reads as a settable select the user can never actually change.
      if ((el.type === 'checkbox' || el.type === 'radio') && A.UNIT_PAIRS[el.id]) {
        var upr = A.UNIT_PAIRS[el.id];
        var lv = val.toLowerCase();
        var wantChecked = (lv === A.unitWord(upr[1]).toLowerCase() || lv === upr[1].toLowerCase());
        if (!!el.checked !== wantChecked) { try { el.click(); } catch (e) {} }
        return 'OK_UNIT:' + (el.checked ? upr[1] : upr[0]);
      }
      if (el.tagName === 'SELECT' || (typeof el.className === 'string' && el.className.indexOf('custom-select') >= 0)) return A._setSelect(el, val);
      var proto = (el.tagName === 'TEXTAREA') ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;
      var setter = null; try { setter = Object.getOwnPropertyDescriptor(proto, 'value').set; } catch (d) {}
      var rawSet = function (v) { if (setter) { try { setter.call(el, v); return; } catch (e) {} } try { el.value = v; } catch (e2) {} };
      var fireInput = function (ch, ty) { try { el.dispatchEvent(new InputEvent('input', { bubbles: true, cancelable: true, data: ch || null, inputType: ty || 'insertText' })); return; } catch (e) {} try { el.dispatchEvent(new Event('input', { bubbles: true })); } catch (e2) {} };
      var fireKey = function (ty, key) { try { el.dispatchEvent(new KeyboardEvent(ty, { bubbles: true, cancelable: true, key: key, code: key.length === 1 ? 'Key' + key.toUpperCase() : key })); } catch (e) {} };
      try { el.focus(); } catch (f) {}
      try { if (el._valueTracker && el._valueTracker.setValue) el._valueTracker.setValue('__force__'); } catch (e) {}
      rawSet(''); fireInput('', 'deleteContentBackward');
      for (var i = 0; i < val.length; i++) {
        var ch = val.charAt(i);
        fireKey('keydown', ch); fireKey('keypress', ch);
        try { if (el._valueTracker && el._valueTracker.setValue) el._valueTracker.setValue(val.substring(0, i)); } catch (e) {}
        rawSet(val.substring(0, i + 1)); fireInput(ch, 'insertText'); fireKey('keyup', ch);
      }
      try { el.dispatchEvent(new Event('change', { bubbles: true })); } catch (e) {}
      fireKey('keydown', 'Enter'); fireKey('keyup', 'Enter');
      try { el.blur(); } catch (b) {}
      return 'OK:' + el.value;
    } catch (e) { return 'ERR:' + e.message; }
  };

  A._setSelect = function (el, val) {
    try {
      var oc = el.querySelector('.options'); if (oc && oc.style) { try { oc.style.visibility = 'visible'; } catch (e) {} }
      var lower = val.toLowerCase();
      var find = function () {
        var byAttr = el.querySelectorAll('[data-value="' + val.replace(/"/g, '\\"') + '"]'); if (byAttr.length) return byAttr[0];
        var opts = el.querySelectorAll('.option, option');
        for (var i = 0; i < opts.length; i++) if ((opts[i].textContent || '').trim().toLowerCase() === lower) return opts[i];
        return null;
      };
      var hit = find();
      var clk = function (t) { try { t.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })); t.dispatchEvent(new MouseEvent('mouseup', { bubbles: true })); t.dispatchEvent(new MouseEvent('click', { bubbles: true })); } catch (e) {} };
      if (hit) { clk(hit); if (oc && oc.style) setTimeout(function () { try { oc.style.visibility = 'hidden'; } catch (e) {} }, 100); return 'OK_SELECT'; }
      var opener = el.querySelector('.selected-option') || el; clk(opener);
      return 'OPENED_RETRY';
    } catch (e) { return 'ERR:' + e.message; }
  };

  window.__MSFSBA_PMDG_EFB = A;
  return A.INSTALLED;
})();
