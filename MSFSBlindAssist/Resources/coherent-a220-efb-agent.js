// MSFS Blind Assist — Synaptic A220-300 EFB in-page agent (Coherent GT debugger).
//
// Injected into the "efbA220_1"/"efbA220_2" Coherent views via Runtime.evaluate
// (no Community mod, no HTML patching). The iniBuilds-style EFB is REAL HTML
// (Bootstrap-grid markup, plain JS — no React: verified live 2026-07-28, zero
// fiber keys on every probed node), so a DOM walk + native value writes work.
//
// Page architecture (live-verified): #iniEFB > #renderer holds ONE wrapper div
// per app page, exactly one carries class "visiblePage" (the rest "hiddenPage").
// Pages are identified by their inner root id — lockscreen / dashboard / equip /
// flight / loadsheet / takeOffPerfViewer / ldgperf / settings-bg / termcharts.
// Modal chrome lives OUTSIDE #renderer: #powered-off, #control-box (power/lock/
// brightness/display mode + the "AIRCRAFT START STATES" panel-state buttons:
// Cold and Dark / GPU:APU / Taxi / Ready for take off — each writes
// L:A22X Flight Stage 0-3 via its onclick), #confirm-box ("Confirm New Panel
// State" after door clicks) and #header-bar's #menu-home-button (back to the
// dashboard). GOTCHA (live-proven 2026-07-29): the gear toggle handler is bound
// to the #control-box-button-icon IMG, NOT its wrapper DIV — clicking the DIV
// is a silent no-op, so every gear open/close click must target the img.
//
// Contract (mirrors the repo's PMDG-EFB agent idiom, list-index dispatch):
//   window.__a220Efb.collect()          -> JSON {ok,page,items:[{i,kind,label,value}]}
//                                          kind: 'text'|'button'|'field'|'toggle'|'alert'
//   window.__a220Efb.click(i)           -> 'ok' | 'ERR ...'
//   window.__a220Efb.setField(i, value) -> committed value | 'ERR ...'
//
// Field commit: the EFB's inputs are plain <input> with inline sanitizer
// handlers; committing = set .value natively then dispatch bubbling
// 'input' + 'change' + 'keyup' and blur() (no React value-setter dance needed).
// The 'keyup' is LOAD-BEARING (bundle-decoded 2026-07-29): every Settings
// text-field save handler — including the SimBrief ID, which both the FMS
// SIMBRIEF uplink and the loadsheet import depend on — is bound to el.onkeyup
// (it saves to DataStore + Coherent.call('COMM_BUS_WASM_CALLBACK',
// 'SetSimbriefUsername', v)), and the Takeoff-perf ICAO/OAT/wind sanitizers
// hang off onkeyup too. input+change alone sets the text but never commits.
(function () {
  // Version-gated install: a live Coherent view outlives app restarts, so a
  // plain "already installed" guard would pin the OLD agent until the aircraft
  // reloads. Bump AGENT_V whenever the agent changes.
  var AGENT_V = 3;
  if (window.__a220Efb && window.__a220Efb.v === AGENT_V) return 'MSFSBA_A220_EFB_INSTALLED';

  var A = {};
  A.v = AGENT_V;
  A._els = [];      // collect-index -> element to act on (click target / input)

  var PAGE_NAMES = {
    lockscreen: 'Lock Screen',
    dashboard: 'Main Menu',
    equip: 'Ground Equipment',
    flight: 'My Flight',
    loadsheet: 'Loadsheet',
    takeOffPerfViewer: 'Takeoff Performance',
    ldgperf: 'Landing Performance',
    'settings-bg': 'Settings'
  };
  var PAGE_NAMES_BY_CLASS = [
    ['ofp-navigation', 'Flight Plan'],
    ['termcharts', 'Charts'],
    ['delay-card-page', 'Map']
  ];

  function txt(el) { return el ? String(el.textContent || '').replace(/\s+/g, ' ').trim() : ''; }
  function cls(el) { return el && typeof el.className === 'string' ? el.className : ''; }
  function hasCls(el, c) { return (' ' + cls(el) + ' ').indexOf(' ' + c + ' ') >= 0; }

  // Visible = not display:none / visibility:hidden anywhere up to the stop node,
  // AND a non-zero rect. (Page WRAPPERS report zero-height while their children
  // render — rect is only trusted on the leaf/control itself, never ancestors.)
  function isShown(el, stop) {
    var n = el;
    while (n && n !== stop && n.nodeType === 1) {
      var cs;
      try { cs = window.getComputedStyle(n); } catch (e) { cs = null; }
      if (cs && (cs.display === 'none' || cs.visibility === 'hidden')) return false;
      n = n.parentElement;
    }
    var r;
    try { r = el.getBoundingClientRect(); } catch (e) { return true; }
    return r.width > 0 || r.height > 0;
  }
  function overlayShown(el) {
    if (!el) return false;
    var cs;
    try { cs = window.getComputedStyle(el); } catch (e) { return false; }
    return cs.display !== 'none' && cs.visibility !== 'hidden';
  }

  // ---- collection -------------------------------------------------------

  var seq = 0; // consumed-marker generation (labels/units claimed by a control)

  function labelFor(el) {
    if (!el.id) return null;
    try { return document.querySelector('label[for="' + el.id + '"]'); } catch (e) { return null; }
  }

  // Bootstrap button-group radios/checkboxes (.btn-check — the Settings
  // "NavData Source" SIM DEFAULT/NAVIGRAPH pair) are clipped to a zero box;
  // only their <label class="btn"> renders. A radio/checkbox whose input fails
  // isShown still counts as shown when its label is visible.
  function controlShown(el, root) {
    if (isShown(el, root)) return true;
    if (el.tagName === 'INPUT') {
      var t = String(el.type || '').toLowerCase();
      if (t === 'radio' || t === 'checkbox') {
        var lab = labelFor(el);
        return !!(lab && isShown(lab, root));
      }
    }
    return false;
  }

  function rowOf(el) {
    var n = el.parentElement;
    while (n && n.id !== 'renderer') {
      if (hasCls(n, 'row') || hasCls(n, 'loadsheet-value-row') || hasCls(n, 'input-group')) {
        if (!hasCls(n, 'input-group')) return n;
      }
      n = n.parentElement;
    }
    return null;
  }

  function isLabelCell(el) {
    if (el.children.length !== 0) return false;
    if (!/[A-Za-z°%\/]/.test(txt(el))) return false;
    return hasCls(el, 'flight-value-title') || hasCls(el, 'text-end') ||
           el.tagName === 'LABEL' || /^H[1-6]$/.test(el.tagName);
  }

  // Nearest label cells in the control's row that PRECEDE it in document order.
  // ignoreUsed=true lets a value-only control (a disabled "actual" cell) reuse a
  // label another control already claimed.
  function rowLabel(el, ignoreUsed) {
    var row = rowOf(el);
    if (!row) return '';
    var cells = row.querySelectorAll('*');
    // Only label cells AFTER the last other control that precedes el count —
    // otherwise a section-wide .row (Settings) donates every earlier heading.
    var parts = [];
    for (var i = 0; i < cells.length; i++) {
      var c = cells[i];
      if (c === el) break;
      if (!(c.compareDocumentPosition(el) & 4 /* el follows c */)) continue;
      if ((c.tagName === 'INPUT' || c.tagName === 'BUTTON') && isShown(c, row)) { parts = []; continue; }
      if (!ignoreUsed && c.__mbaUsed === seq) continue;
      if (isLabelCell(c) && isShown(c, row)) parts.push(c);
    }
    var texts = [];
    for (var j = 0; j < parts.length; j++) {
      if (!ignoreUsed) parts[j].__mbaUsed = seq;
      texts.push(txt(parts[j]));
    }
    return texts.join(' ');
  }

  // Unit caption: span.input-group-text following the input in its input-group.
  function unitOf(el) {
    var g = el.parentElement;
    while (g && !hasCls(g, 'input-group')) g = g.parentElement;
    if (!g) return '';
    var spans = g.querySelectorAll('span.input-group-text');
    for (var i = 0; i < spans.length; i++) {
      var s = spans[i];
      if ((el.compareDocumentPosition(s) & 4) && s.__mbaUsed !== seq) {
        s.__mbaUsed = seq;
        return txt(s);
      }
    }
    return '';
  }

  // Friendly names for icon-only / id-only controls.
  var BTN_NAMES = {
    pb_left: 'Pushback left', pb_stop: 'Pushback stop',
    pb_right: 'Pushback right', pb_aft: 'Pushback aft',
    refreshMetar: 'Refresh METAR'
  };

  // Sliders sit beside plain SPAN captions ("CABIN") that the strict label-cell
  // test rejects — accept any nearest preceding text leaf in the row for them.
  function rangeLabel(el) {
    var row = rowOf(el);
    if (row) {
      var cells = row.querySelectorAll('span,div,label,h1,h2,h3');
      var best = null;
      for (var i = 0; i < cells.length; i++) {
        var c = cells[i];
        if (c === el || !(c.compareDocumentPosition(el) & 4)) continue;
        if (c.children.length === 0 && /[A-Za-z]/.test(txt(c)) && isShown(c, row)) best = c;
      }
      if (best) { best.__mbaUsed = seq; return txt(best); }
    }
    return '';
  }

  function nearestHeading(el, root) {
    // Last H1/H2 before el (radio groups + sliders sit under a section heading).
    var hs = root.querySelectorAll('h1,h2');
    var best = null;
    for (var i = 0; i < hs.length; i++) {
      if (hs[i].compareDocumentPosition(el) & 4) best = hs[i];
    }
    return best ? txt(best) : '';
  }

  function Collector() {
    this.items = [];
    this.els = [];
    this.pendingRow = null;      // text-merge accumulator: { row, parts }
    this.anonSeq = 0;            // numbering for unnamed swatch buttons
  }
  Collector.prototype.flushText = function () {
    var p = this.pendingRow;
    this.pendingRow = null;
    if (!p || p.parts.length === 0) return;
    var label = p.parts.length > 1 && /[A-Za-z]/.test(p.parts[0])
      ? p.parts[0] + ': ' + p.parts.slice(1).join(' ')
      : p.parts.join(' ');
    this.items.push({ i: this.els.length, kind: 'text', label: label, value: '' });
    this.els.push(null);
  };
  Collector.prototype.add = function (kind, label, value, actEl) {
    this.flushText();
    this.items.push({ i: this.els.length, kind: kind, label: label || '(unnamed)', value: value == null ? '' : String(value) });
    this.els.push(actEl || null);
  };
  Collector.prototype.text = function (el, root) {
    var s = txt(el);
    if (!s) return;
    var row = rowOf(el);
    if (this.pendingRow && this.pendingRow.row === row && row) {
      this.pendingRow.parts.push(s);
      return;
    }
    this.flushText();
    if (row) this.pendingRow = { row: row, parts: [s] };
    else this.add('text', s, '', null);
  };

  // Resolve every control's label/unit BEFORE the text walk (labels usually
  // precede their control in the DOM, so claiming must happen up front) and
  // STORE the result on the element — the walk then emits from the stored data.
  function resolveControls(root) {
    var ctrls = root.querySelectorAll('button,input');
    for (var i = 0; i < ctrls.length; i++) {
      var el = ctrls[i];
      if (!controlShown(el, root)) continue;
      el.__mbaSeq = seq;
      var tag = el.tagName;
      if (tag === 'BUTTON') { el.__mbaName = rowLabel(el); continue; }
      var type = String(el.type || 'text').toLowerCase();
      if (type === 'button' || type === 'submit') { el.__mbaName = rowLabel(el); continue; }
      if (type === 'radio' || type === 'checkbox') {
        var lab = labelFor(el);
        if (lab) lab.__mbaUsed = seq;
        el.__mbaLab = lab || null;
        el.__mbaHead = nearestHeading(el, root);
        continue;
      }
      if (type === 'range') { el.__mbaName = rangeLabel(el) || nearestHeading(el, root); continue; }
      // text / number
      var lf = labelFor(el);
      if (lf) lf.__mbaUsed = seq;
      var name = lf ? txt(lf) : rowLabel(el);
      var unit = unitOf(el);
      if (!name && (el.disabled || el.readOnly)) {
        // A value-only cell beside a labeled input (the loadsheet "ACTUAL"
        // column) — reuse the row's already-claimed label.
        var reused = rowLabel(el, true);
        if (reused) name = unit ? reused : reused + ' (actual)';
      }
      if (unit && name && unit.toLowerCase() === name.toLowerCase()) unit = '';
      el.__mbaUnit = unit;
      el.__mbaName = name;
    }
  }

  function collectControl(co, el) {
    var tag = el.tagName;
    var stored = el.__mbaSeq === seq;
    if (tag === 'BUTTON') {
      var bl = txt(el);
      if (!bl && el.id) bl = BTN_NAMES[el.id] || el.id.replace(/[_-]+/g, ' ');
      var rl0 = stored ? (el.__mbaName || '') : '';
      if (!bl) {
        if (hasCls(el, 'cabin-color-button')) { bl = 'Cabin color option ' + (++co.anonSeq); rl0 = ''; }
        else if (rl0) { bl = rl0; rl0 = ''; }
        else return;   // truly anonymous button — drop (PMDG-EFB rule)
      }
      co.add('button', rl0 && rl0 !== bl ? rl0 + ': ' + bl : bl, '', el);
      return;
    }
    if (tag !== 'INPUT') return;
    var type = String(el.type || 'text').toLowerCase();
    if (type === 'button' || type === 'submit') {
      var rl = stored ? (el.__mbaName || '') : '';
      if (rl) co.add('toggle', rl, el.value, el);
      else co.add('button', String(el.value || el.id || 'button'), '', el);
      return;
    }
    if (type === 'radio' || type === 'checkbox') {
      var lab = stored ? el.__mbaLab : null;
      var opt = lab ? txt(lab) : (el.id || 'option');
      var head = stored ? (el.__mbaHead || '') : '';
      var on = el.checked || (lab && hasCls(lab, 'active'));
      co.add('toggle', (head ? head + ': ' : '') + opt, on ? 'selected' : 'not selected', lab || el);
      return;
    }
    if (type === 'range') {
      var rname = (stored ? el.__mbaName : '') || el.id || 'slider';
      co.add('field', rname + ' (' + (el.min || '0') + '-' + (el.max || '100') + ')', el.value, el);
      return;
    }
    // text / number
    var name = stored ? (el.__mbaName || '') : '';
    var unit = stored ? (el.__mbaUnit || '') : '';
    if (unit) name = name ? name + ' (' + unit + ')' : unit;
    if (!name) name = el.placeholder || el.id || 'field';
    var val = String(el.value);
    if (el.disabled || el.readOnly) co.add('text', name + ': ' + (val === '' ? '—' : val), '', null);
    else co.add('field', name, val, el);
  }

  function walk(co, el, root) {
    for (var i = 0; i < el.children.length; i++) {
      var c = el.children[i];
      var tag = c.tagName;
      if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'IMG') continue;
      if (!isShown(c, root)) {
        // Clipped .btn-check radios still collect, via their visible label.
        if (tag === 'INPUT' && controlShown(c, root)) collectControl(co, c);
        continue;
      }

      // Dashboard/menu tiles: clickable DIVs, not <button>.
      if (hasCls(c, 'menu-row-item') && hasCls(c, 'is-button')) {
        // Tile captions use <br> ("My<br>Flight") which textContent joins with
        // no space — re-split on the lower→upper camel boundary.
        var t = (txt(c.querySelector('.home-button-text')) || txt(c) || 'menu item')
          .replace(/([a-z])([A-Z])/g, '$1 $2');
        co.add('button', t, '', c);
        continue;
      }
      if (tag === 'BUTTON' || tag === 'INPUT') { collectControl(co, c); continue; }
      if (c.children.length === 0) {
        if (c.__mbaUsed === seq) continue;
        var s = txt(c);
        if (!s) continue;
        if (s.length <= 1 && !/[0-9]/.test(s)) continue;  // decorative glyph
        c.__mbaUsed = seq;
        co.text(c, root);
        continue;
      }
      walk(co, c, root);
    }
  }

  A.collect = function () {
    try {
      seq++;
      var renderer = document.getElementById('renderer');
      if (!renderer) return JSON.stringify({ ok: false, error: 'A220 EFB not found in this view.' });
      var co = new Collector();

      var powered = document.getElementById('powered-off');
      if (overlayShown(powered)) {
        co.add('text', 'The EFB is powered off.', '', null);
        co.add('button', 'Power On (tap screen)', '', powered);
        A._els = co.els;
        return JSON.stringify({ ok: true, page: 'Powered Off', items: co.items });
      }

      // EFB CONTROL modal (gear button): power off / lock / brightness /
      // display mode / aircraft start states (the panel-state loader).
      var ctrlBox = document.getElementById('control-box');
      if (overlayShown(ctrlBox)) {
        resolveControls(ctrlBox);
        walk(co, ctrlBox, ctrlBox);
        co.flushText();
        // The gear icon IMG toggles the modal closed (the sim UI's "click
        // anywhere to exit" is the same handler) — give the user a way out.
        var gearClose = document.getElementById('control-box-button-icon') ||
                        document.getElementById('control-box-button');
        if (gearClose) co.add('button', 'Close menu', '', gearClose);
        A._els = co.els;
        return JSON.stringify({ ok: true, page: 'EFB Control', items: co.items });
      }

      // Confirmation dialog (e.g. after door/equipment clicks) — announce + keep page below.
      var confirmBox = document.getElementById('confirm-box');
      if (overlayShown(confirmBox)) {
        var btns = confirmBox.querySelectorAll('button');
        var msg = txt(confirmBox);
        for (var b = 0; b < btns.length; b++) msg = msg.replace(txt(btns[b]), ' ');
        co.add('alert', msg.replace(/\s+/g, ' ').trim(), '', null);
        for (var b2 = 0; b2 < btns.length; b2++) co.add('button', txt(btns[b2]), '', btns[b2]);
      }

      var page = null, pageName = 'Unknown';
      for (var i = 0; i < renderer.children.length; i++) {
        if (hasCls(renderer.children[i], 'visiblePage')) { page = renderer.children[i]; break; }
      }
      if (page) {
        var rootEl = page.children[0];
        if (rootEl && rootEl.id && PAGE_NAMES[rootEl.id]) pageName = PAGE_NAMES[rootEl.id];
        else {
          for (var p = 0; p < PAGE_NAMES_BY_CLASS.length; p++) {
            if ((rootEl && hasCls(rootEl, PAGE_NAMES_BY_CLASS[p][0])) || hasCls(page, PAGE_NAMES_BY_CLASS[p][0])) {
              pageName = PAGE_NAMES_BY_CLASS[p][1];
              break;
            }
          }
        }
      }

      if (pageName === 'Lock Screen') {
        var unlock = document.querySelector('#lockscreen .unlock-button img') ||
                     document.querySelector('#lockscreen .unlock-button');
        co.add('text', 'The EFB is locked.', '', null);
        if (unlock) co.add('button', 'Unlock', '', unlock);
      } else if (page) {
        if (pageName !== 'Main Menu') {
          var home = document.getElementById('menu-home-button');
          if (home) co.add('button', 'Main Menu', '', home);
        }
        resolveControls(page);
        walk(co, page, page);
        co.flushText();
      } else {
        co.add('text', 'No EFB page is currently visible.', '', null);
      }

      // Act on the icon IMG, not the wrapper DIV — the EFB's toggle handler
      // lives on the img; a DIV click silently does nothing (live-proven).
      var gear = document.getElementById('control-box-button');
      var gearIcon = document.getElementById('control-box-button-icon') || gear;
      if (gear && isShown(gear, document.body)) co.add('button', 'EFB control panel', '', gearIcon);

      A._els = co.els;
      if (co.items.length > 400) co.items = co.items.slice(0, 400);
      return JSON.stringify({ ok: true, page: pageName, items: co.items });
    } catch (e) {
      return JSON.stringify({ ok: false, error: 'collect failed: ' + e.message });
    }
  };

  // ---- actions ----------------------------------------------------------

  function fireClick(el) {
    var r = el.getBoundingClientRect();
    var x = r.left + r.width / 2, y = r.top + r.height / 2;
    var types = ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'];
    for (var i = 0; i < types.length; i++) {
      var ev;
      try {
        ev = new MouseEvent(types[i], { bubbles: true, cancelable: true, view: window, clientX: x, clientY: y, button: 0 });
      } catch (e) {
        ev = document.createEvent('MouseEvents');
        ev.initMouseEvent(types[i], true, true, window, 1, x, y, x, y, false, false, false, false, 0, null);
      }
      el.dispatchEvent(ev);
    }
  }

  A.click = function (i) {
    try {
      var el = A._els[i];
      if (!el) return 'ERR no clickable element at index ' + i;
      fireClick(el);
      return 'ok';
    } catch (e) { return 'ERR ' + e.message; }
  };

  A.setField = function (i, value) {
    try {
      var el = A._els[i];
      if (!el || el.tagName !== 'INPUT') return 'ERR no input element at index ' + i;
      el.value = String(value);
      // 'keyup' included: the EFB binds its Settings save handlers (SimBrief ID,
      // Hoppie…) and the perf-page sanitizers to el.onkeyup, not input/change —
      // all are zero-arg handlers reading .value, so a generic Event suffices.
      var types = ['input', 'change', 'keyup'];
      for (var t = 0; t < types.length; t++) {
        var ev;
        try { ev = new Event(types[t], { bubbles: true, cancelable: true }); } catch (e) { ev = document.createEvent('Event'); ev.initEvent(types[t], true, true); }
        el.dispatchEvent(ev);
      }
      try { el.blur(); } catch (e2) { }
      return String(el.value);
    } catch (e) { return 'ERR ' + e.message; }
  };

  window.__a220Efb = A;
  return 'MSFSBA_A220_EFB_INSTALLED';
})()
