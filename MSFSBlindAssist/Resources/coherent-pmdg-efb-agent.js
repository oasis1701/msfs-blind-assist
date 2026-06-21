// PMDG EFB in-page agent — installed at runtime into the PMDG tablet's Coherent GT view
// (window.__MSFSBA_PMDG_EFB). ES5 only (Coherent GT = Chromium 49: var, no arrow funcs,
// no String.includes; use .indexOf). Generic: classifies controls by TYPE (never by element id)
// so new PMDG pages / installed EFB apps read with no code change. scrape() returns the element
// contract FbwEfbForm consumes (controlType/kind/clickable/level/value/options); clickElement(idx)
// and setValue(idx,text) drive the tablet by the stamped data-pmdg-efb-idx.
(function () {
  var A = {};
  A.INSTALLED = 'MSFSBA_PMDG_EFB_INSTALLED';

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

  // id -> label, stripping common PMDG prefixes and title-casing.
  A.idToLabel = function (id) {
    if (!id) return '';
    var s = id.replace(/^efb_preferences_/, '').replace(/^efb_dashboard_/, '')
             .replace(/^efb_general_/, '').replace(/^opt_/, '').replace(/^groundops_/, '')
             .replace(/^efb_/, '').replace(/^wb_/, '').replace(/^statusbar_/, '').replace(/_/g, ' ').trim();
    // The screen reader already announces the "button" role — a trailing "Button"/"Btn"
    // word in the id (e.g. "weather search button") is redundant clutter, so drop it.
    s = s.replace(/\s*\b(button|btn)\b\s*$/i, '').trim();
    if (!s) return '';
    return s.replace(/\b\w/g, function (c) { return c.toUpperCase(); });
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
        var hit = (c.matches && c.matches('.groundops_ui_label, .opt-label, .opt-output-label, .field-label')) ? c : null;
        if (hit) { var t = A.txt(hit); if (t && t.length < 60) return t.replace(/:\s*$/, ''); }
      }
    }
    return '';
  };

  // Active unit for a PMDG preference toggle — authoritative from the EFB Settings object,
  // keyed by the control id (efb_preferences_length_unit -> Settings.length_unit). Falls back ''.
  A.activeUnit = function (el) {
    try {
      var S = null;
      try { if (typeof window !== 'undefined' && window.Settings) S = window.Settings; } catch (e1) {}
      if (!S) { try { if (typeof Settings !== 'undefined') S = Settings; } catch (e2) {} }
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
  // buttons so status-bar buttons never grab the clock/sim-rate text.
  A._rowTextFor = function (el, textItems, requireLabel) {
    var cr = null; try { cr = el.getBoundingClientRect(); } catch (e) { return null; }
    if (!cr) return null;
    var cCy = cr.top + cr.height / 2, cLeft = Math.round(cr.left), best = null, bestDx = 1e9;
    for (var t = 0; t < textItems.length; t++) {
      var ti = textItems[t];
      if (ti._consumed || ti._cy == null || ti._left == null) continue;
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
      if (ai._consumed || ai._top == null || ai._left == null) continue;
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
      if ((typeof el.className === 'string' ? el.className : '').indexOf('leaflet-marker-icon') >= 0) { skipUnder = el; continue; }
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
          if (snm) { idx++; el.setAttribute('data-pmdg-efb-idx', String(idx)); out.push({ idx: idx, type: 'text-content', tag: el.tagName, label: snm, src: 'statusbar-indicator' }); lastText = null; continue; }
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
        if (el.closest && el.closest('button, a, select, .custom-select, label, [role=button], [role=tab], [role=link], [role=heading], h1, h2, h3, h4, h5, h6')) continue;
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
        var isLbl = false; try { isLbl = !!(el.closest && el.closest('.preflabel, .opt-label, .opt-output-label, .groundops_ui_label, .field-label, .input-label')); } catch (e2) {}
        var titem = { idx: idx, type: 'text-content', tag: el.tagName, label: own, src: 'text-content', _top: ttop, _left: tleft, _cy: tcy, _isLabel: isLbl };
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
      if (type === 'button') { var pl = A._pairLabel(el); if (pl && pl !== item.label) item.pair = pl; }
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
        var au = A.activeUnit(el); if (au) item.value = au;  // unit toggles: show the active unit
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
      q = q.replace(new RegExp('\\b' + bb.label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\b', 'i'), '').replace(/\s+/g, ' ').trim();
      if (q && norm(q) !== norm(bb.label)) bb.pair = q;
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
    return merged;
  };

  // Map an internal collect() item ({type,label,value,pair,captions,level,options,checked,disabled})
  // to the FbwEfbForm element contract (controlType/kind/clickable/level/value/options/...).
  A._toContract = function (it) {
    var o = { idx: it.idx, text: it.label || '', value: '', controlType: '', kind: '', clickable: false, level: 0, live: '', disabled: !!it.disabled, options: null };
    // fold the row-action pair ("Air Start Unit") into the visible text
    if (it.pair) o.text = it.pair + ': ' + (it.label || '');
    switch (it.type) {
      case 'button': o.kind = 'button'; o.clickable = true; break;
      case 'link': o.kind = 'link'; break;
      case 'heading': o.kind = 'heading'; o.level = it.level || 0; break;
      case 'status': o.kind = 'static'; o.live = 'polite'; break;
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

  A.scrape = function () {
    try {
      var raw = A.collect();
      var els = [];
      for (var i = 0; i < raw.length; i++) els.push(A._toContract(raw[i]));
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

  A.clickText = function (needle) {
    var els = A.collect(); needle = String(needle).toLowerCase();
    for (var i = 0; i < els.length; i++) {
      var t = (els[i].label || '').toLowerCase();
      if (t === needle || t.indexOf(needle) >= 0) return A.clickElement(els[i].idx) + ' :: ' + els[i].label;
    }
    return 'NO_MATCH:' + needle;
  };

  A.setValue = function (idx, val) {
    var el = A._byIdx(idx); if (!el) return 'NO_EL';
    val = String(val);
    try {
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
