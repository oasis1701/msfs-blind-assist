// coherent-a380-agent.js
//
// Persistent in-page agent for the FlyByWire A380X MFD, installed via the
// Coherent GT debugger's Runtime.evaluate (NO injection, NO Community-folder
// patching). Runs inside the live MFD view's own JS context — so `SimVar`,
// `Coherent` and the MFD DOM are all directly reachable.
//
// CoherentDebuggerClient.cs sends this whole file once per connection to
// define window.__MSFSBA_A380. Thereafter it calls the small entry points:
//
//   __MSFSBA_A380.scrape(mcduIndex)            -> JSON string (see below)
//   __MSFSBA_A380.setMcdu(n)                   -> "" (1=Capt, 2=FO)
//   __MSFSBA_A380.clickElement(index)          -> "ok"|"missing"
//   __MSFSBA_A380.sendToField(index, text)     -> "ok"|"missing"
//   __MSFSBA_A380.sendScratchpad(text)         -> "ok"
//   __MSFSBA_A380.fireKey(key)                 -> "ok"     (KCCU key name)
//   __MSFSBA_A380.ping()                       -> "MSFSBA_A380_OK"
//
// If the page reloads, window.__MSFSBA_A380 disappears; the client detects
// the missing global (ping/scrape returns undefined) and re-installs.
//
// Target engine: Coherent GT (Chromium 49 era). ES5 ONLY — var, no arrow
// funcs, no String.includes (use indexOf), top-level try/catch.

(function () {
  "use strict";
  var A = {};

  A.activeMcdu = 1;            // 1 = Captain (left), 2 = First Officer (right)
  A.ROW_Y_TOLERANCE_PX = 14;

  A.INTERACTIVE_SELECTOR = [
    ".mfd-input-field-container", ".mfd-button", ".mfd-icon-button",
    ".mfd-dropdown-outer", ".mfd-dropdown-menu-element", ".mfd-page-selector-outer",
    // Additional interactive MFD widgets (RadioButtonGroup, in-page sub-tabs via
    // TopTabNavigator, SURV/ADS-C buttons, context-menu items) — without these
    // whole controls (e.g. the PERF phase tabs, SURV switches) are unreachable.
    ".mfd-radio-button", ".mfd-top-tab-navigator-bar-element-outer",
    ".mfd-surv-button", ".mfd-surv-status-button", ".mfd-adsc-button",
    ".mfd-context-menu-element",
    // Duplicate-names dialog rows: each row is a clickable fix-picker entry rendered
    // via addEventListener (not a React prop) on elements with id="mfd-fms-dupl-{i}".
    ".mfd-fms-fpln-duplicate-table-row"
  ].join(",");

  A.LEAF_SELECTOR = [
    ".mfd-label", ".mfd-input-field-text-input", ".mfd-button", ".mfd-icon-button",
    ".mfd-dropdown-inner", ".mfd-page-selector-label", ".mfd-dropdown-menu-element",
    ".mfd-amber-error-message"
  ].join(",");

  // Maps the legacy A320-style page commands to A380 KCCU keys. null = the
  // A380 has no equivalent fixed key (those live as on-screen tabs instead).
  A.PAGE_TO_KCCU = {
    page_init: "INIT", page_dir: "DIR", page_fpln: "FPLN", page_perf: "PERF",
    page_radnav: "NAVAID", page_sec_fpln: "SECINDEX", page_atc: "ATCCOM",
    page_data: null, page_fuel: null, page_menu: null,
    key_next_page: "DOWN", key_prev_page: "UP", key_exec: "ENT"
  };

  A._mcduElements = [];

  function clean(s) { return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, ""); }

  A.isVisible = function (node) {
    try {
      var st = window.getComputedStyle(node);
      if (st.display === "none" || st.visibility === "hidden") return false;
      var r = node.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    } catch (e) { return true; }
  };

  A.findRoot = function (idx) {
    var roots = { 1: null, 2: null };
    var capt = document.getElementById("MFD_LEFT_PARENT_DIV");
    var fo = document.getElementById("MFD_RIGHT_PARENT_DIV");
    if (capt) roots[1] = capt.querySelector(".mfd-main");
    if (fo) roots[2] = fo.querySelector(".mfd-main");
    if (!roots[1] && !roots[2]) {
      var all = document.querySelectorAll(".mfd-main");
      if (all.length >= 1) roots[1] = all[0];
      if (all.length >= 2) roots[2] = all[1];
    }
    return roots[idx] || roots[1] || roots[2];
  };

  // Resolve the MFD-side container an element belongs to (LEFT/RIGHT parent div),
  // falling back to document. Used to scope label/header scans so a dropdown on one
  // pilot's MFD can't read the selected value off the OTHER pilot's MFD.
  A.mfdRootOf = function (n) {
    var cur = n;
    var hops = 0;
    while (cur && cur !== document.body && hops < 60) {
      if (cur.id === "MFD_LEFT_PARENT_DIV" || cur.id === "MFD_RIGHT_PARENT_DIV") return cur;
      cur = cur.parentElement;
      hops++;
    }
    return document;
  };

  // Status flags that share the .mfd-title-bar-text class but aren't a title.
  A.TITLE_FLAGS = { "PENALTY": 1, "EO": 1, "TMPY": 1, "SEC": 1 };

  A.activePageLabel = function (root) {
    var parts = [];
    // 1) The active top-level tab (ACTIVE / POSITION / SEC INDEX / DATA / ...).
    var tab = root.querySelector(".mfd-page-selector-label.active");
    if (tab && A.isVisible(tab)) { var tt = clean(tab.textContent); if (tt) parts.push(tt); }
    // 2) The first VISIBLE title bar's text — skipping hidden overlays
    //    (MESSAGE LIST / DUPLICATE NAMES are always in the DOM but hidden) and
    //    the PENALTY/EO/TMPY status flags.
    var bars = root.querySelectorAll(".mfd-title-bar-container");
    for (var b = 0; b < bars.length; b++) {
      if (!A.isVisible(bars[b])) continue;
      var spans = bars[b].querySelectorAll(".mfd-title-bar-text");
      for (var i = 0; i < spans.length; i++) {
        if (!A.isVisible(spans[i])) continue;
        var t = clean(spans[i].textContent);
        if (t && !A.TITLE_FLAGS[t.toUpperCase()]) parts.push(t);
      }
      break; // only the first visible title bar
    }
    return parts.join(" / ");
  };

  A.footerMessage = function (root) {
    var f = root.querySelectorAll(".mfd-footer-message-area");
    for (var i = 0; i < f.length; i++) { if (!A.isVisible(f[i])) continue; var t = clean(f[i].textContent); if (t) return t; }
    var a = root.querySelectorAll(".mfd-amber-error-message");
    for (var j = 0; j < a.length; j++) { if (!A.isVisible(a[j])) continue; var t2 = clean(a[j].textContent); if (t2) return t2; }
    return "";
  };

  A.classify = function (node) {
    var c = node.classList;
    if (c.contains("mfd-input-field-container")) return "input";
    if (c.contains("mfd-radio-button")) return "radio";
    if (c.contains("mfd-top-tab-navigator-bar-element-outer")) return "subtab";
    if (c.contains("mfd-surv-status-button")) return "survstatus";
    if (c.contains("mfd-surv-button")) return "surv";
    if (c.contains("mfd-adsc-button")) return "adsc";
    if (c.contains("mfd-button")) {
      // A button carrying a dropdown arrow is really a COMBO BOX / collapsible
      // list — activating it expands a list of options (RWY / SID / TRANS / APPR /
      // STAR / VIA on the departure & arrival pages, plus SPD ALT, F-PLN INFO).
      // Classify it as a dropdown so it reads as a combo box, not a plain button.
      try { if (node.querySelector('[class*="dropdown-arrow"]')) return "dropdown"; } catch (e) {}
      return "button";
    }
    if (c.contains("mfd-icon-button")) return "icon";
    if (c.contains("mfd-dropdown-outer")) return "dropdown";
    if (c.contains("mfd-dropdown-menu-element") || c.contains("mfd-context-menu-element")) return "menu";
    if (c.contains("mfd-page-selector-outer")) return "tab";
    if (c.contains("mfd-fms-fpln-duplicate-table-row")) return "button";
    return "other";
  };

  // For a label+value widget (SURV / ADS-C), join the label and its value span.
  A.labelValueText = function (n) {
    var lbl = n.querySelector(".mfd-label, .mfd-surv-status-button-label");
    var val = n.querySelector(".mfd-value, .mfd-surv-status-indicator");
    var l = clean(lbl ? lbl.textContent : "");
    var v = clean(val ? val.textContent : "");
    if (l && v) return l + ": " + v;
    return l || v || clean(n.textContent);
  };

  // The descriptive label for a control that sits in the PREVIOUS column/sibling
  // of the same row (e.g. the "ADS-C" / "ADS-C EMERGENCY" text beside an
  // AdscButton toggle, which carries no label of its own).
  A.contextLabel = function (n) {
    var col = n, guard = 0;
    while (col && guard < 5) {
      guard++;
      var prev = col.previousElementSibling;
      while (prev) {
        if (prev.classList && prev.classList.contains("mfd-label")) {
          var t0 = clean(prev.textContent); if (t0) return t0;
        }
        var lbl = prev.querySelector ? prev.querySelector(".mfd-label") : null;
        if (lbl) { var t = clean(lbl.textContent); if (t) return t; }
        prev = prev.previousElementSibling;
      }
      col = col.parentElement;
    }
    return "";
  };

  A.readInputValue = function (node) {
    var inner = node.querySelector(".mfd-input-field-text-input");
    return A.normEmptyBoxes(inner ? clean(inner.textContent) : "");
  };

  // An InputField may carry its OWN trailing unit span (.mfd-input-field-unit) —
  // e.g. the PERF T.O FLEX TEMP field's "°C". Used to label an otherwise anonymous
  // field that has no naming label in its cell. Returns "°C"/"KT"/… or "".
  A.inputOwnUnit = function (node) {
    var us = node.querySelectorAll(".mfd-input-field-unit.mfd-unit-trailing");
    for (var i = 0; i < us.length; i++) {
      var t = clean(us[i].textContent);
      if (t) return t;
    }
    return "";
  };

  // Some InputFields render their unit LEADING (e.g. a transition LEVEL field shows
  // "FL" before the value) rather than trailing. Captured separately so it can be
  // placed BEFORE the value ("FL 180", not "180 FL").
  A.inputLeadUnit = function (node) {
    var us = node.querySelectorAll(".mfd-input-field-unit.mfd-unit-leading");
    for (var i = 0; i < us.length; i++) {
      var t = clean(us[i].textContent);
      if (t) return t;
    }
    return "";
  };

  // The A380 MFD fills an empty entry field with U+25AF boxes (one per character
  // slot). A screen reader would read each as "white vertical rectangle". Render
  // a field that's ONLY boxes + template punctuation as "blank" (these are
  // editable fields, so "blank" reads better than "empty"); for a partially-typed
  // field, drop the leftover boxes. (The ▯ below is the literal U+25AF char; keep
  // this file UTF-8.)
  A.normEmptyBoxes = function (v) {
    if (!v || v.indexOf("▯") < 0) return v;
    if (/^[▯\s.\/:+\-]*$/.test(v)) return "blank";
    return clean(v.replace(/▯/g, ""));
  };

  // Direct (own) text of a node — only its immediate text children, so a big
  // container with text scattered through descendants returns "" (its leaves
  // get reported individually instead).
  A.directText = function (n) {
    var s = "";
    for (var c = 0; c < n.childNodes.length; c++) {
      if (n.childNodes[c].nodeType === 3) s += n.childNodes[c].nodeValue;
    }
    return clean(s);
  };

  // Like textContent, but joins text from SEPARATE descendant nodes with a space
  // so multi-span / stacked button captions don't mash together ("ACTIVATE" +
  // "APPR*" -> "ACTIVATE APPR*", not "ACTIVATEAPPR*"). The FBW MFD renders many
  // two-word/two-line button labels as adjacent spans with no whitespace.
  A.spacedText = function (n) {
    var parts = [];
    (function walk(node, depth) {
      if (depth > 40) return;   // defensive: bound recursion (button subtrees are shallow)
      for (var i = 0; i < node.childNodes.length; i++) {
        var c = node.childNodes[i];
        if (c.nodeType === 3) { var t = c.nodeValue; if (t && t.replace(/\s+/g, "")) parts.push(clean(t)); }
        else if (c.nodeType === 1) { walk(c, depth + 1); }
      }
    })(n, 0);
    return parts.join(" ").replace(/\s+/g, " ").trim();
  };

  // True when the node is (or sits inside) an already-captured interactive
  // control — used to avoid listing a button's inner text twice.
  A.isInsideInteractive = function (n) {
    var cur = n;
    while (cur) {
      if (cur.getAttribute && cur.getAttribute("data-fbwa380-mcdu-idx")) return true;
      cur = cur.parentElement;
    }
    return false;
  };

  // Short label for an interactive control, ONE thing per line (no positional
  // grid). Inputs show their current value (or "blank" when an editable field is
  // empty); the field's text label, if any, is reported separately above it.
  A.lineText = function (n, kind) {
    if (kind === "input") {
      var v = A.readInputValue(n);
      // An input wrapped in a DropdownMenu is really a CHOICE field (you pick a
      // value from a list — many, like ATC COM "NOTIFY TO ATC", don't accept
      // free text at all). Flag it so it doesn't read as a plain text box.
      var choice = A.ancestorWithClass(n, "mfd-dropdown-outer") ? " (combobox)" : "";
      return (v || "blank") + choice;
    }
    if (kind === "dropdown") {
      var di = n.querySelector(".mfd-dropdown-inner");
      // DropdownMenu widget carries its selected value in .mfd-dropdown-inner.
      // Use spacedText so multi-span values keep their word breaks (D-ATIS
      // "UPDATE OR PRINT", not "UPDATEOR PRINT").
      if (di) return A.spacedText(di) || "(choice)";
      // Button-style dropdown (ARRIVAL/DEPARTURE RWY/APPR/STAR/TRANS, etc.): the
      // element text is just the field NAME; the selected value sits in the summary
      // grid directly under the matching header. Fold it in so the combo announces
      // "RWY, 30R" instead of a bare "RWY" (nothing appended when none is set).
      // spacedText so multi-span button labels keep word breaks (D-ATIS function
      // selector "UPDATE OR PRINT", not "UPDATEOR PRINT").
      var dlbl = A.spacedText(n) || clean(n.textContent);
      var dval = A.comboSelectedValue(dlbl, n);
      return dval ? (dlbl + ", " + dval) : (dlbl || "(choice)");
    }
    if (kind === "tab") {
      var t = n.querySelector(".mfd-page-selector-label");
      return clean(t ? t.textContent : n.textContent);
    }
    if (kind === "radio") {
      // The <label class="mfd-radio-button"> text is the option name; the inner
      // <input type=radio> .checked marks the selection.
      var ri = n.querySelector("input[type=radio]");
      var sel = ri && ri.checked;
      var rt = clean(n.textContent);
      return (rt || "(option)") + (sel ? " (selected)" : "");
    }
    if (kind === "subtab") {
      // In-page TopTabNavigator tab (e.g. PERF T.O / CLB / CRZ / DES / APPR / GA).
      var lab = n.querySelector(".mfd-top-tab-navigator-bar-element-label");
      var active = n.classList.contains("active") || (lab && lab.classList.contains("active"));
      var st = clean(lab ? lab.textContent : n.textContent);
      // NB: the form appends the role word "tab" (RoleWord), so the active marker
      // must NOT also contain "tab" or the line reads "... active tab tab".
      return (st || "(tab)") + (active ? " (active)" : "");
    }
    if (kind === "adsc") {
      // AdscButton is a TOGGLE that renders BOTH labels stacked (e.g. ARMED over
      // OFF). The DIMMED (inactive) label carries .mfd-adsc-label-off; the ACTIVE
      // one is the other div. It has no own name, so borrow the row's label
      // (ADS-C / ADS-C EMERGENCY).
      var active = "", other = "";
      var kids = n.children;
      for (var k = 0; k < kids.length; k++) {
        var tx = clean(kids[k].textContent);
        if (!tx) continue;
        if (kids[k].classList && kids[k].classList.contains("mfd-adsc-label-off")) other = tx;
        else active = tx;
      }
      var ctx = A.contextLabel(n);
      var head = (ctx ? ctx + " " : "") + (active || "(state)");
      return other ? head + ", toggle, other option " + other : head + ", toggle";
    }
    if (kind === "surv") {
      // A SurvButton renders BOTH its labels (e.g. "OFF" and "ON"); the ACTIVE one
      // carries class mfd-value (green), the inactive one mfd-label. Reading both
      // ("OFF: ON") hides which is current — a blind pilot can't tell if ALT RPTG /
      // TERR SYS / GPWS is ON or OFF. Return just the active (mfd-value) label so it
      // reads its LIVE state ("ON" / "AUTO"). Fall back to both only when the button
      // is disabled (no green value present).
      var act = n.querySelector(".mfd-value");
      if (act) { var at = clean(act.textContent); if (at) return at; }
      return A.labelValueText(n) || "(button)";
    }
    if (kind === "survstatus") {
      return A.labelValueText(n) || "(button)";
    }
    var bt = A.spacedText(n);
    if (!bt && n.getAttribute) bt = clean(n.getAttribute("aria-label") || n.getAttribute("title") || "");
    return bt;
  };

  // Builds the flat, screen-reader-friendly line list for the current page:
  // every visible interactive control PLUS every visible static-text leaf,
  // one per line, sorted top-to-bottom / left-to-right. Interactive lines get
  // a stable 1-based idx (also stamped on the DOM node as data-fbwa380-mcdu-idx
  // so clicks/entries can find them); static lines get idx 0. Empty lines are
  // dropped. The scratchpad/footer message is appended as the last line.
  // Fold each input field's naming label into the field's own line. For every
  // input item, pick the label item that (a) vertically overlaps the field and
  // (b) sits to its left (label.right <= field.left + GAP), choosing the label
  // whose right edge is closest to the field. The chosen label is marked
  // `consumed` so the caller drops it as a separate line. Robust to multiple
  // label+field pairs sharing one row (FROM / TO / ALTN), since each field
  // takes its nearest left-hand label.
  A.associateInputLabels = function (items) {
    var GAP = 28; // px tolerance between a label's right edge and its field's left edge
    // A naming label sits in the SAME cell as its field, just to its left — never
    // hundreds of px away in a different column. Without this cap, a right-column
    // field grabs a left-column label that happens to share its row: e.g. the PERF
    // T.O FLEX TEMP input (a °C field, left ~629) was grabbing the V-speed column's
    // "S" slat-speed label (right ~130, ~500px away) and reading as a knots speed.
    var MAX_LEFT = 220; // px the label's right edge may sit left of the field's left edge
    for (var i = 0; i < items.length; i++) {
      var inp = items[i];
      if (inp.kind !== "input") continue;
      if (typeof inp.left !== "number") continue;
      var best = null, bestRight = -1;
      for (var j = 0; j < items.length; j++) {
        var lb = items[j];
        if (!lb.isLabel || lb.consumed) continue;
        if (typeof lb.right !== "number") continue;
        // vertical ranges must overlap (same visual row)
        if (!(lb.top < inp.bot && lb.bot > inp.top)) continue;
        // label must be to the left of the field, within tolerance
        if (lb.right > inp.left + GAP) continue;
        // ...but not so far left that it belongs to another column.
        if (inp.left - lb.right > MAX_LEFT) continue;
        if (lb.right > bestRight) { bestRight = lb.right; best = lb; }
      }
      // No label to the left? Try a column HEADER directly ABOVE the field (same
      // column, one row up) — e.g. the takeoff-config grid where FLAPS / THS FOR /
      // PACKS / ANTI ICE sit over their value cells. Pairs them as "FLAPS: -" etc.
      if (!best && typeof inp.right === "number") {
        var bestBot = -1;
        for (var k = 0; k < items.length; k++) {
          var hd = items[k];
          if (!hd.isLabel || hd.consumed) continue;
          if (typeof hd.left !== "number" || typeof hd.right !== "number") continue;
          if (hd.bot > inp.top) continue;                 // must be above the field
          if (inp.top - hd.bot > 40) continue;            // immediately above (one row)
          if (hd.right < inp.left || hd.left > inp.right) continue;  // same column
          // Don't grab a bare UNIT sitting above a field (T / % / KT / FL / NM /
          // KG / °, etc.) as if it were the field's name — only real word headers
          // (FLAPS, THS FOR, PACKS, ANTI ICE …). Require >= 3 chars and not a unit.
          var ht = clean(hd.text);
          if (ht.length < 3 || /^(T|KG|LB|KT|FL|NM|FT|M|MIN|HR|UTC|%|°|CG|GW)$/i.test(ht)) continue;
          if (hd.bot > bestBot) { bestBot = hd.bot; best = hd; }
        }
      }
      if (best) {
        var val = inp.value || inp.text || "";
        // Strip a trailing colon the MFD already prints on the label (e.g.
        // "NOTIFY TO ATC :") so we don't end up with a double colon.
        var lbl = clean(best.text).replace(/\s*:\s*$/, "");
        // Include the field's OWN unit — leading (e.g. "FL 180") or trailing (e.g.
        // "1510 FT", "----- FT"). The unit is a sibling span so it never appears in
        // the value; without this a LABELED input drops its unit (the FT-on-ALT and
        // THR RED / ACCEL / TRANS / DES CABIN RATE bug — see captures).
        var shown = (inp.leadUnit ? inp.leadUnit + " " : "") + (val || "blank") + (inp.ownUnit ? " " + inp.ownUnit : "");
        inp.text = lbl + ": " + shown + (inp.isChoice ? " (combobox)" : "");
        best.consumed = true;
      } else if (inp.ownUnit || inp.leadUnit) {
        // No naming label in this cell, but the field carries its own unit (the PERF
        // T.O FLEX TEMP °C field): show value + unit so it isn't an anonymous "---".
        var v2 = inp.value || inp.text || "";
        inp.text = (inp.leadUnit ? inp.leadUnit + " " : "") + (v2 || "blank") + (inp.ownUnit ? " " + inp.ownUnit : "") + (inp.isChoice ? " (combobox)" : "");
      }
    }
  };

  // True if the node sits anywhere inside the F-PLN grid (any element whose class
  // contains "mfd-fms-fpln" — line, header, leg, annotation, constraint, …). Used
  // to keep the generic row scraper OUT of the flight plan, which buildFplnLines
  // handles on its own.
  A.insideFpln = function (n) {
    var cur = n;
    while (cur) {
      var c = (cur.className && cur.className.toString) ? cur.className.toString() : "";
      if (c.indexOf("mfd-fms-fpln") >= 0) return true;
      cur = cur.parentElement;
    }
    return false;
  };

  // ---- PERF page: structure-aware emission (one clean line per field/row) ------
  // The PERF page is a dense multi-column grid; the generic Y-row merge is column-blind,
  // so it fused values into the wrong field (APPR F-speed into the wind line) and made
  // comma-soup. The DOM is well-structured (each field is a .mfd-label-value-container
  // with label/value/unit siblings; speed predictions are a grid of cells with a header
  // row), so we emit per field/row. Interactive controls stay with step-1 (they keep
  // their stamped idx + selectability); this only takes over STATIC text, like buildFplnLines.
  A.isPerfPage = function (page) {
    try { return !!page.querySelector('[class*="mfd-fms-perf"]'); } catch (e) { return false; }
  };
  A.insidePerf = function (n) {
    var cur = n;
    while (cur) { if (cur.getAttribute && cur.getAttribute("data-fbwa380-perf-owned")) return true; cur = cur.parentElement; }
    return false;
  };
  A.buildPerfLines = function (page, pageRect, items, startIdx) {
    var idx = startIdx;
    function own(el) { try { el.setAttribute("data-fbwa380-perf-owned", "1"); } catch (e) {} }
    function hasInteractive(el) { return !!el.querySelector("[data-fbwa380-mcdu-idx]"); }
    function isDash(s) { return !s || /^[-:.+\s]+$/.test(s); }
    function push(el, text) {
      if (!text) return;
      var r = el.getBoundingClientRect();
      items.push({ top: r.top - pageRect.top, left: r.left - pageRect.left, right: r.right - pageRect.left, bot: r.bottom - pageRect.top, idx: 0, kind: "text", text: text, value: "", disabled: false, perf: true });
    }

    // 1) Speed-prediction grids (CLB/CRZ/DES): rows delimited by a `br`-classed first
    //    cell; row 0 = column headers (MODE/SPD/MACH/PRED TO). Pair each data cell with
    //    its header by COLUMN INDEX so a right-column value can't leak into another field.
    var grids = page.querySelectorAll(".mfd-fms-perf-clb-grid, .mfd-fms-perf-crz-grid, .mfd-fms-perf-des-grid");
    for (var g = 0; g < grids.length; g++) {
      var grid = grids[g]; if (!A.isVisible(grid)) continue;
      var cells = grid.querySelectorAll(".mfd-fms-perf-speed-table-cell, .mfd-fms-perf-speed-presel-managed-table-cell");
      var rows = [], cur = null, ci;
      for (ci = 0; ci < cells.length; ci++) { var cl = cells[ci]; if (!A.isVisible(cl)) continue; if (cl.classList.contains("br") || cur === null) { cur = []; rows.push(cur); } cur.push(cl); }
      var headers = rows.length ? rows[0].map(function (c) { return clean(c.innerText); }) : [];
      for (var ri = 1; ri < rows.length; ri++) {
        var row = rows[ri]; var rlabel = clean(row[0].innerText); if (rlabel === "null") rlabel = ""; var parts = [];
        for (var k = 1; k < row.length; k++) {
          if (hasInteractive(row[k])) continue;   // editable cell (PRESEL) → step-1 owns it
          var v = clean(row[k].innerText); if (isDash(v) || v === "null") continue;
          var h = headers[k] || ""; parts.push(h ? (h + " " + v) : v);
        }
        if (rlabel || parts.length) push(row[0], (rlabel ? rlabel + ": " : "") + parts.join(", "));
      }
      own(grid);
    }

    // 2) Discrete fields: one clean line per visible .mfd-label-value-container that has
    //    a LABEL, no interactive child (those stay with step-1), and isn't in an owned grid.
    //    Label-less composite cells (e.g. CLB SPD LIM's value cells) are left to the generic
    //    pass. Green-dot speed has an <svg> circle for its label → synthesize "green dot".
    var lvcs = page.querySelectorAll(".mfd-label-value-container");
    for (var li = 0; li < lvcs.length; li++) {
      var c = lvcs[li]; if (!A.isVisible(c)) continue;
      if (c.getAttribute("data-fbwa380-perf-owned") || A.insidePerf(c.parentElement)) continue;
      if (hasInteractive(c)) continue;
      var lab = c.querySelector(".mfd-label"); var labT = lab ? clean(lab.textContent) : "";
      if (!labT && c.querySelector("svg circle")) labT = "green dot";
      if (!labT) continue;
      var valEl = c.querySelector(".mfd-value"); var valT = valEl ? clean(valEl.textContent) : "";
      var lu = c.querySelector(".mfd-unit-leading"), tu = c.querySelector(".mfd-unit-trailing");
      // A labeled container with NO value and NO unit is a header/composite (e.g. the APPR
      // approach summary "APPR ILS24R KJFK", where the ident lives in sibling cells) — don't
      // own/emit it as "LABEL:" with an empty value; leave it to the generic pass so the
      // ident isn't lost. Real value fields (even dashed, like "HD --- KT") have a unit.
      if (isDash(valT) && !lu && !tu) continue;
      var shown = clean((lu ? clean(lu.textContent) + " " : "") + valT + (tu ? " " + clean(tu.textContent) : ""));
      own(c); push(c, labT + ": " + shown);
    }
    return idx;
  };

  // General labeled-field emitter — runs on EVERY MFD page. One clean "LABEL: value unit"
  // line per visible .mfd-label-value-container that has an INNER .mfd-label, isn't already
  // owned (by buildFplnLines/buildPerfLines), has no interactive child, and isn't in the
  // F-PLN grid. Generalises the PERF labeled-field cleanup to FUEL&LOAD / POSITION / DATA /
  // ATC etc. (e.g. "GW: ---.- KLB", "EPU: 0.04 NM"). Containers whose naming label is a
  // SIBLING (not a child) — common on POSITION pages — are left for page-specific builders;
  // label-less composites are left to the generic pass. Safe: PERF LVCs are already owned, so
  // this no-ops there; F-PLN is skipped; nothing interactive is touched.
  A.buildLabeledFields = function (page, pageRect, items, startIdx) {
    var idx = startIdx;
    function isDash(s) { return !s || /^[-:.+\s]+$/.test(s); }
    var lvcs = page.querySelectorAll(".mfd-label-value-container");
    for (var i = 0; i < lvcs.length; i++) {
      var c = lvcs[i];
      if (!A.isVisible(c)) continue;
      if (c.getAttribute("data-fbwa380-perf-owned") || A.insidePerf(c.parentElement)) continue;
      if (A.insideFpln(c)) continue;
      if (c.querySelector("[data-fbwa380-mcdu-idx]")) continue;   // interactive child → step-1 owns it
      var lab = c.querySelector(".mfd-label"); var labT = lab ? clean(lab.textContent) : "";
      if (!labT) continue;   // label-less composite → leave to the generic pass / page builders
      var valEl = c.querySelector(".mfd-value"); var valT = valEl ? clean(valEl.textContent) : "";
      var lu = c.querySelector(".mfd-unit-leading"), tu = c.querySelector(".mfd-unit-trailing");
      if (isDash(valT) && !lu && !tu) continue;   // header/composite with no real value
      var shown = clean((lu ? clean(lu.textContent) + " " : "") + valT + (tu ? " " + clean(tu.textContent) : ""));
      try { c.setAttribute("data-fbwa380-perf-owned", "1"); } catch (e) {}
      var r = c.getBoundingClientRect();
      items.push({ top: r.top - pageRect.top, left: r.left - pageRect.left, right: r.right - pageRect.left, bot: r.bottom - pageRect.top, idx: 0, kind: "text", text: labT + ": " + shown, value: "", disabled: false, perf: true });
    }
    return idx;
  };

  // ---- SURV CONTROLS page: header-prefix the radios + toggle buttons ----------
  // The SURV CONTROLS page is a 2-D grid: section headers (XPDR / TCAS / TAWS as
  // .mfd-surv-heading; ALT RPTG / ELEVN/TILT as .mfd-label.bigger) and per-control
  // labels (WXR / PRED W/S / TURB / GAIN / MODE / WX ON VD / TERR SYS / GPWS / G/S
  // MODE / FLAP MODE as .mfd-surv-label) sit ABOVE the radios / SurvButtons they
  // name. A flat read gives bare "radio | TA/RA" / "surv | AUTO" with no idea which
  // system they belong to. Here we PREFIX each surv/radio control (already emitted +
  // stamped in step 1 — idx/selectability untouched) with the nearest header DIRECTLY
  // ABOVE it in the same column ("TCAS: TA/RA", "WXR: AUTO", "TERR SYS: OFF: ON"), and
  // mark the matched header owned so it isn't ALSO read as a bare line. The TCAS
  // ABV/BLW/NORM range column has no DOM header → those stay bare (self-evident).
  A.isSurvPage = function (page) {
    try { return !!page.querySelector(".mfd-surv-button, .mfd-surv-heading"); } catch (e) { return false; }
  };
  A.buildSurvControls = function (page, pageRect, items, startIdx) {
    function own(el) { try { el.setAttribute("data-fbwa380-perf-owned", "1"); } catch (e) {} }
    function dtext(el) { var s = ""; for (var c = 0; c < el.childNodes.length; c++) { if (el.childNodes[c].nodeType === 3) s += el.childNodes[c].nodeValue; } return clean(s); }
    // header candidates (with geometry)
    var hs = page.querySelectorAll(".mfd-surv-label, .mfd-surv-heading, .mfd-label.bigger");
    var headers = [];
    for (var h = 0; h < hs.length; h++) {
      if (!A.isVisible(hs[h])) continue;
      var t = dtext(hs[h]); if (!t) continue;
      var r = hs[h].getBoundingClientRect();
      headers.push({ t: t, top: r.top - pageRect.top, left: r.left - pageRect.left, right: r.right - pageRect.left, el: hs[h] });
    }
    function headerAbove(rect) {
      var best = null, bestGap = 1e9;
      for (var i = 0; i < headers.length; i++) {
        var hd = headers[i];
        if (hd.top >= rect.top) continue;                                 // must start above the control
        if (hd.right < rect.left + 3 || hd.left > rect.right - 3) continue; // same column (x-overlap)
        var gap = rect.top - hd.top;
        if (gap < bestGap) { bestGap = gap; best = hd; }
      }
      return best;
    }
    // enrich each surv-button / radio item by its stamped idx (text only — idx stays)
    var ctrls = page.querySelectorAll(".mfd-surv-button[data-fbwa380-mcdu-idx], .mfd-radio-button[data-fbwa380-mcdu-idx]");
    for (var c = 0; c < ctrls.length; c++) {
      var el = ctrls[c]; if (!A.isVisible(el)) continue;
      var cidx = parseInt(el.getAttribute("data-fbwa380-mcdu-idx"), 10);
      var item = null;
      for (var k = 0; k < items.length; k++) { if (items[k].idx === cidx) { item = items[k]; break; } }
      if (!item) continue;
      var rr = el.getBoundingClientRect();
      var hd = headerAbove({ top: rr.top - pageRect.top, left: rr.left - pageRect.left, right: rr.right - pageRect.left });
      if (hd && item.text.indexOf(hd.t + ":") !== 0) { item.text = hd.t + ": " + item.text; own(hd.el); }
    }

    // ---- structural grouping (SURV CONTROLS page only) ----------------------
    // The page lays XPDR/TCAS side-by-side and the WXR ELEVN/TILT column beside the
    // WXR grid, so a flat top→bottom, left→right read INTERLEAVES unrelated systems
    // (an XPDR option, a TCAS option, a bare display-range option, another XPDR
    // option, a button …). Tag every control with its logical group
    // (0 XPDR / 1 TCAS / 2 WXR / 3 TAWS / 4 SURV) + an in-group order; a final pass in
    // enumerateLines re-sorts the page into contiguous, self-labelled groups. We do
    // NOT touch geometry, so associateInputLabels (pairs SQWK with its label
    // geometrically, runs later) and the merge/dedupe passes are unaffected.
    if (!page.querySelector(".mfd-surv-controls-first-section")) return startIdx;  // STATUS & SWITCHING etc. → enrich only

    function itemByIdx(ix) { for (var q = 0; q < items.length; q++) if (items[q].idx === ix) return items[q]; return null; }
    function tagControls(container, grp, startOrder) {
      if (!container) return startOrder;
      var cs = container.querySelectorAll("[data-fbwa380-mcdu-idx]");
      var ord = startOrder;
      for (var ci = 0; ci < cs.length; ci++) {
        if (!A.isVisible(cs[ci])) continue;
        var it = itemByIdx(parseInt(cs[ci].getAttribute("data-fbwa380-mcdu-idx"), 10));
        if (it) { it.survGroup = grp; it.survOrder = ord++; }
      }
      return ord;
    }
    function emitHeading(text, grp, refEl) {
      var rt = refEl ? (refEl.getBoundingClientRect().top - pageRect.top) : 0;
      // left=right=0: a zero-width point at the far left so associateInputLabels can
      // never mistake the heading for a field's left-of-input label. perf:true so the
      // row-merge skips it — XPDR/TCAS (and TAWS/SURV) sit side-by-side on one visual
      // row, so without this the two headings merge into "XPDR, TCAS".
      items.push({ top: rt, left: 0, right: 0, bot: rt, idx: 0, kind: "text", text: text,
        value: "", disabled: false, survGroup: grp, survOrder: 0, perf: true });
    }
    // Own every FBW section heading; we emit our own group headings instead so each
    // group leads with its name regardless of whether the header was consumed above.
    var fbwHeads = page.querySelectorAll(".mfd-surv-heading");
    for (var fh = 0; fh < fbwHeads.length; fh++) own(fbwHeads[fh]);

    var xpdrSec = page.querySelector(".mfd-surv-controls-xpdr-section");
    var tcasSec = page.querySelector(".mfd-surv-controls-tcas-section");
    var wxrSec = page.querySelector(".mfd-surv-controls-second-section");
    var tawsSec = page.querySelector(".mfd-surv-controls-taws-section");
    var defCont = page.querySelector(".mfd-surv-controls-def-settings-container");

    emitHeading("XPDR", 0, xpdrSec); tagControls(xpdrSec, 0, 1);
    emitHeading("TCAS", 1, tcasSec); tagControls(tcasSec, 1, 1);
    emitHeading("WXR", 2, wxrSec); tagControls(wxrSec, 2, 1);
    emitHeading("TAWS", 3, tawsSec); tagControls(tawsSec, 3, 1);
    if (defCont) {
      var sl = defCont.querySelector(".mfd-surv-label"); if (sl) own(sl);
      emitHeading("SURV", 4, defCont); tagControls(defCont, 4, 1);
    }

    // TCAS right-hand column (NORM / ABV / BLW) is the traffic-display vertical range
    // and has NO on-screen header, so it read as bare "NORM"/"ABV"/"BLW" with no idea
    // what it controls. Qualify it with a "TCAS display" context (strip any stray
    // "TCAS:" the header pass may have prepended).
    var tcasRight = page.querySelector(".mfd-surv-controls-tcas-right");
    if (tcasRight) {
      var rr2 = tcasRight.querySelectorAll("[data-fbwa380-mcdu-idx]");
      for (var rj = 0; rj < rr2.length; rj++) {
        var rit = itemByIdx(parseInt(rr2[rj].getAttribute("data-fbwa380-mcdu-idx"), 10));
        if (rit) rit.text = "TCAS display: " + rit.text.replace(/^TCAS:\s*/, "");
      }
    }
    return startIdx;
  };

  // Build ONE FIXED-GRID line per F-PLN waypoint (Braille-display reading — THE
  // SPEC, user decision 2026-06). The MFD draws the flight plan as a dense grid:
  // each `.mfd-fms-fpln-line` groups a waypoint with its INBOUND leg
  // (airway/procedure name, track, distance) and any constraints, but those cells
  // sit at different x/y, so a plain row-by-row read scattered them into many
  // lines full of empty "---"/"--:--" dashes. Blind users read the window on
  // Braille displays DOWN COLUMNS across lines, so spoken connector words ("via",
  // "track", "Mach", "flight level", "ETA") are unwanted verbosity — instead each
  // waypoint becomes ONE fixed-width padded string mirroring the real display's
  // column order, with LITERAL screen values (".84", "FL380", "+5000", the '"'
  // ditto mark when a prediction is unchanged from the line above — shown, NOT
  // resolved, exactly like the real display):
  //   ident(8) airway(6) track(5, °) dist(4) spd(5) alt(7) time(5) [+" FPA x.x"]
  // — totals 40 so a line fits one 40-cell Braille display line.
  // A padded HEADER line ("TRK  NM  SPD  ALT    TIME" aligned to the columns)
  // is emitted before the first waypoint so a Braille user can calibrate the
  // columns. The destination footer keeps its spoken labels (a summary, not a
  // grid row). Universal to any A380 flight plan (class-driven, nothing
  // hard-coded).
  A.buildFplnLines = function (page, pageRect, items, startIdx) {
    var idx = startIdx;
    var lines = page.querySelectorAll(".mfd-fms-fpln-line");
    function cellText(L, sel) { var n = L.querySelector(sel); return n ? clean(n.textContent) : ""; }
    function isDash(s) { return !s || /^[-:.\s]+$/.test(s); }
    // The F-PLN page has a column-header toggle (MfdFmsFpln line 1033) that swaps
    // the two right-hand columns between SPD / ALT and EFOB / T.WIND. When it shows
    // "EFOB ... T.WIND", efobOrSpeed renders the speed cell as EFOB (tonnes, "NN.N")
    // and windOrAlt renders the altitude cell as the wind (direction + "/" +
    // magnitude). Detect the mode once so the cell parser below reads the right
    // columns; default to SPD/ALT if the header button isn't found (safe).
    var efobWindMode = false;
    var hdrBtn = page.querySelector(".mfd-fms-fpln-button-speed-alt");
    if (hdrBtn) efobWindMode = clean(hdrBtn.textContent).toUpperCase().indexOf("EFOB") >= 0;
    // Fixed-grid cell padding (Braille column reading — THE SPEC): pad each cell
    // with trailing spaces to its column width; a value that fills/exceeds its
    // width overflows with ONE trailing space (never truncate data).
    function padCell(s, w) {
      s = s || "";
      if (s.length >= w) return s + " ";
      var out = s;
      while (out.length < w) out += " ";
      return out;
    }
    // One padded HEADER line before the first waypoint (blank ident/airway
    // cells, then the column titles aligned to their columns) — this is what
    // lets a Braille user calibrate the columns. In the EFOB/T.WIND view the two
    // right-hand value columns are titled EFOB / WIND (mirroring the real header).
    // Widths total exactly 40 so a full line fits ONE 40-cell Braille display
    // line (user requirement): ident 8, airway 6, track 5 (with the ° glyph —
    // ONE cell in 8-dot computer braille, the user's reading mode), dist 4
    // (covers ≤999 NM; a rare 1000+ NM leg overflows that line by one), spd 5,
    // alt 7, time 5. The dist header is "NM" (the real display also keeps the
    // unit in the header) — "DIST" wouldn't fit the 4-wide column. The rare
    // descent-only FPA is an end-of-line suffix, not a permanently blank column.
    var headerDone = false;
    function emitHeader(topVal, rightVal) {
      var h = padCell("", 8) + padCell("", 6) + padCell("TRK", 5) +
              padCell("NM", 4) +
              padCell(efobWindMode ? "EFOB" : "SPD", 5) +
              padCell(efobWindMode ? "WIND" : "ALT", 7) + "TIME";
      items.push({
        top: topVal, left: 0, right: rightVal, bot: topVal,
        idx: 0, kind: "text", text: h.replace(/\s+$/, ""), value: "", disabled: false, fpln: true
      });
      headerDone = true;
    }
    // NOTE on dittos: wind direction/magnitude and the SPD/ALT predictions each
    // render a literal '"' ditto mark when unchanged from the line above
    // (formatWind / formatSpeed ~1887 / formatAltitude ~1808). The real display's
    // convention IS the spec for Braille users reading down a column — show the
    // mark, do NOT resolve it to the value.
    for (var i = 0; i < lines.length; i++) {
      var L = lines[i];
      if (!A.isVisible(L)) continue;
      var lcls = (L.className && L.className.toString) ? L.className.toString() : "";

      // SPECIAL lines (DISCONTINUITY / END OF F-PLN / NO ALTN F-PLN …) are drawn
      // as a delimiter band ("- - - DISCONTINUITY - - -") with NO
      // .mfd-fms-fpln-line-ident child, so the waypoint path below would skip them
      // and the pilot would never know a discontinuity exists. Surface the label.
      // The line div itself carries the lateral-revision click handler, so a
      // DISCONTINUITY is made actionable (Enter → menu → DELETE * clears it).
      // Other markers (END OF F-PLN …) open no menu, so they stay plain text.
      if (lcls.indexOf("mfd-fms-fpln-line-special") >= 0) {
        var slr = L.getBoundingClientRect();
        var sp = L.querySelector("span");
        var slabel = sp ? clean(sp.textContent) : clean(L.textContent);
        slabel = clean(slabel.replace(/[-–—]+/g, " "));   // drop the dash band
        if (!slabel) continue;
        var sIdx = 0, sKind = "text";
        if (/DISCON/i.test(slabel)) {
          L.setAttribute("data-fbwa380-mcdu-idx", String(idx));
          sIdx = idx; sKind = "fplndisc"; idx++;
        }
        items.push({
          top: slr.top - pageRect.top, left: 0,
          right: slr.right - pageRect.left, bot: slr.bottom - pageRect.top,
          idx: sIdx, kind: sKind, text: slabel, value: "", disabled: false, fpln: true
        });
        continue;
      }

      var ident = cellText(L, ".mfd-fms-fpln-line-ident");
      // Strip the "@" overfly marker the MFD glues to the ident (a visual symbol;
      // it reads as "at" otherwise). The waypoint name is what matters here.
      ident = ident.replace(/@/g, "").replace(/\s+/g, " ").replace(/^ | $/g, "");
      if (!ident) continue;                       // a spacer / non-waypoint row
      var anno = cellText(L, ".mfd-fms-fpln-line-annotation");
      // leg upper row carries the track ("217°"), the distance (bare integer) and —
      // during the descent — the flight-path angle ("3.0" / "-3.0", fpaRef innerText
      // = data.fpa.toFixed(1), MfdFmsFpln.tsx ~1658). Track has the ° glyph and the
      // distance is a pure integer, so the signed one-decimal pattern is the FPA.
      var track = "", dist = "", fpa = "";
      var ups = L.querySelectorAll(".mfd-fms-fpln-leg-upper-row");
      for (var u = 0; u < ups.length; u++) {
        var ut = clean(ups[u].textContent);
        if (ut.indexOf("°") >= 0) track = ut;
        else if (/^\d+$/.test(ut)) dist = ut;
        else if (/^-?\d+\.\d$/.test(ut)) fpa = ut;
      }
      var lr = L.getBoundingClientRect();
      // Constraint cells without a prediction (formatSpeed/formatAltitude render the
      // raw constraint into the SPD or ALT column when no prediction exists). The
      // old single-variable parse kept only the LAST match, so a SPEED constraint
      // was silently lost whenever an ALTITUDE constraint coexisted (and a lone
      // speed constraint mis-read as an altitude). Split by COLUMN BAND instead —
      // same relX bands the prediction parser below uses. Ignore dash placeholders
      // and the bare "*" met/missed markers (no digit); the altitude column can also
      // hold the literal "WINDOW" (a two-altitude window constraint renders as
      // displayedStr = altitudeConstraint.altitude2 ? 'WINDOW' : …, MfdFmsFpln.tsx
      // ~1861 — the two altitudes themselves are NOT in the DOM).
      var conSpd = "", conAlt = "";
      var cons = L.querySelectorAll('[class*="fpln-leg-constraint"]');
      for (var c = 0; c < cons.length; c++) {
        var ct = clean(cons[c].textContent);
        if (isDash(ct)) continue;
        var isWin = ct.toUpperCase() === "WINDOW";
        if (!/\d/.test(ct) && !isWin) continue;
        var cx = cons[c].getBoundingClientRect().left - lr.left;
        if (cx >= 300 && cx < 405) { if (!conSpd) conSpd = ct; }
        else if (cx >= 405 && cx < 520) { if (!conAlt) conAlt = ct; }
      }
      // ETA in the lower row's leftmost cell — only meaningful once airborne
      var eta = cellText(L, ".mfd-fms-fpln-leg-lower-row");
      if (isDash(eta)) eta = "";

      // HOLD rows (MfdFmsFpln.tsx ~1693): a hold leg re-uses the waypoint-line shell
      // with ident "HOLD L"/"HOLD R", empties the SPD/ALT cells, and puts the hold
      // SPEED into the TIME column with a small "SPD" annotation above it
      // (timeAnnotation.set('SPD'); timeText.set(data.holdSpeed)). Without this the
      // hold speed mis-read as an ETA ("HOLD L, 210"). Detect via the SPD annotation
      // in the time block (the block that also holds the leg-lower-row time cell).
      var isHoldRow = false, holdSpd = "";
      var hAnnos = L.querySelectorAll(".mfd-fms-fpln-line-annotation");
      for (var ha = 0; ha < hAnnos.length; ha++) {
        if (clean(hAnnos[ha].textContent).toUpperCase() !== "SPD") continue;
        var hPar = hAnnos[ha].parentElement;
        if (hPar && hPar.querySelector(".mfd-fms-fpln-leg-lower-row")) { isHoldRow = true; break; }
      }
      if (isHoldRow) { holdSpd = eta; eta = ""; }

      // SPEED + ALTITUDE predictions: classless cells in the lower row, right of
      // the ETA. Blank (dashes) before takeoff, so nothing is added on the ground;
      // once the FMS computes them in flight they fold in as ", 250 knots" /
      // ", flight level 350". Captured by column position WITH a value-pattern
      // guard so a mis-placed cell can never inject garbage. (Verify in flight.)
      var spd = "", alt = "", efob = "", windDir = "", windMag = "";
      var cells = L.getElementsByTagName("*");
      for (var p = 0; p < cells.length; p++) {
        var pc = cells[p];
        var pcls = (pc.className && pc.className.toString) ? pc.className.toString() : "";
        if (pcls.indexOf("mfd-fms-fpln") >= 0) continue;   // skip the classed cells
        var pt = clean(A.directText(pc));
        if (!pt || isDash(pt)) continue;
        var relX = pc.getBoundingClientRect().left - lr.left;
        if (efobWindMode) {
          // EFOB column ("NN.N" tonnes) sits in the old speed band; the wind splits
          // into direction (TAIL/HEAD/ddd° or ditto) + "/" + magnitude (knots or
          // ditto) across the old altitude band. The "--.-"/"---" blanks were
          // already dropped by isDash above; skip only the "/" separator glyph.
          if (pt === "/") continue;
          if (!efob && relX >= 300 && relX < 405 && /^\d{1,3}\.\d$/.test(pt)) efob = pt;
          else if (relX >= 405 && relX < 520) {
            if (relX < 472) { if (!windDir) windDir = pt; }   // direction slot (left)
            else { if (!windMag) windMag = pt; }              // magnitude slot (right)
          }
        } else {
          // A '"' is the SPD/ALT ditto mark (prediction unchanged from the line
          // above — formatSpeed ~1887 / formatAltitude ~1808); resolved below like
          // the wind dittos.
          if (!spd && relX >= 300 && relX < 405 && (pt === '"' || /^(M?\.\d{2}|\d{2,3})$/.test(pt))) spd = pt;
          else if (!alt && relX >= 405 && relX < 520 && (pt === '"' || /^(FL\d{2,3}|\d{3,5})$/.test(pt))) alt = pt;
        }
      }
      // Constraint markers: the "*" the MFD draws immediately to the LEFT of a SPD or
      // ALT prediction when that value has an entered constraint (SPDLIM, a STAR
      // altitude window, etc.). Two FBW states (MfdFmsFpln.tsx renderAltitude/renderSpeed):
      //   .mfd-fms-fpln-leg-constraint-respected  = magenta "*", prediction MEETS it
      //   .mfd-fms-fpln-leg-constraint-missed     = amber  "*", prediction will MISS it
      // The grid keeps the "*" exactly as displayed (prepended, the on-screen
      // left-of-value position). Column band (relX) says whether the marker
      // belongs to SPD or ALT.
      var spdCon = "", altCon = "";   // "" | "met" | "missed"
      var marks = efobWindMode ? [] : L.querySelectorAll('[class*="fpln-leg-constraint-respected"], [class*="fpln-leg-constraint-missed"]');
      for (var st = 0; st < marks.length; st++) {
        if (clean(marks[st].textContent) !== "*") continue;   // the marker glyph, not the value form (con)
        var mcls = (marks[st].className && marks[st].className.toString) ? marks[st].className.toString() : "";
        var mstate = mcls.indexOf("constraint-missed") >= 0 ? "missed" : "met";
        var stx = marks[st].getBoundingClientRect().left - lr.left;
        if (stx >= 300 && stx < 405) spdCon = mstate;
        else if (stx >= 405 && stx < 520) altCon = mstate;
      }

      // FIXED-GRID cell assembly (THE SPEC — Braille column reading, literal
      // screen values). Column order mirrors the real display (annotation row:
      // airway, track, dist; main row: spd, alt, time; descent FPA as a suffix):
      //   ident(8) airway(6) track(5) dist(4) spd(5) alt(7) time(5) — 40 cells
      // SPD/ALT carry the prediction (".84"/"FL380"), the literal '"' ditto when
      // unchanged from the line above, or — when no prediction exists — the
      // constraint LITERAL as displayed ("+250", "-FL100", "WINDOW"). A "*"
      // constraint marker stays prepended exactly as displayed. Hold rows put the
      // hold speed into the time column under its on-screen "SPD" annotation
      // ("SPD 210"). EFOB/T.WIND view: same grid, spd column = EFOB ("36.5"),
      // alt column = wind ("TAIL/7", dittos literal), no labels.
      var spdCell = "", altCell = "", timeCell = "";
      if (efobWindMode) {
        spdCell = efob;
        // Strip the FBW zero-padding on a numeric wind magnitude (007 -> 7);
        // a '"' ditto passes through literally.
        var wmag = windMag.replace(/^0+(?=\d)/, "");
        if (windDir || wmag) altCell = (windDir && wmag) ? windDir + "/" + wmag : (windDir || wmag);
        timeCell = eta;
      } else {
        if (spd) spdCell = (spdCon ? "*" : "") + spd;
        else if (conSpd) spdCell = conSpd;
        if (alt) altCell = (altCon ? "*" : "") + alt;
        else if (conAlt) altCell = conAlt;
        timeCell = isHoldRow ? (holdSpd ? "SPD " + holdSpd : "") : eta;
      }
      var gridLine = padCell(ident, 8) + padCell(anno, 6) +
                     padCell(track, 5) +
                     padCell(dist, 4) + padCell(spdCell, 5) +
                     padCell(altCell, 7) + padCell(timeCell, 5);
      gridLine = gridLine.replace(/\s+$/, "");
      // Descent-only FPA as a suffix (a dedicated column would sit blank on
      // nearly every line and blow the 40-cell budget).
      if (fpa) gridLine += " FPA " + fpa;
      // The column-header line goes immediately above the FIRST waypoint (one
      // row bucket up, so the geometric sort keeps it there).
      if (!headerDone) emitHeader(lr.top - pageRect.top - (A.ROW_Y_TOLERANCE_PX + 1), lr.right - pageRect.left);

      // Make the waypoint actionable: its ident cell carries the lateral-revision
      // click handler, so Enter opens the revisions menu (FROM P.POS DIR TO,
      // INSERT NEXT WPT, DELETE *, HOLD, AIRWAYS, OVERFLY *, DEPARTURE, ARRIVAL).
      // Stamp the ident as the click target and give the line a real idx so the
      // form treats it as a control (role word "waypoint"). Text is unchanged.
      var identEl = L.querySelector(".mfd-fms-fpln-line-ident");
      var wIdx = 0, wKind = "text";
      if (identEl) {
        identEl.setAttribute("data-fbwa380-mcdu-idx", String(idx));
        wIdx = idx; wKind = "fplnwpt"; idx++;
      }
      var r = lr;
      items.push({
        top: r.top - pageRect.top, left: 0,
        right: r.right - pageRect.left, bot: r.bottom - pageRect.top,
        idx: wIdx, kind: wKind, text: gridLine, value: "", disabled: false, fpln: true
      });

      // HOLD immediate-exit / resume-hold button. On an active or decel-sequenced
      // hold leg the MFD renders a Button into the leg's speed column
      // (MfdFmsFpln.renderHoldExitLine) reading "IMMEDIATE EXIT" or "RESUME HOLD".
      // buildFplnLines otherwise only emits the waypoint, so this control would be
      // invisible — surface it as its own clickable line so the pilot can exit (or
      // re-arm) the hold. Clicking it calls onImmediateExitHold (fms_set_hold_
      // immediate_exit). Only present while a hold is active, so it's a no-op
      // otherwise.
      var hbtns = L.querySelectorAll(".mfd-button");
      for (var hb = 0; hb < hbtns.length; hb++) {
        var btn = hbtns[hb];
        var bt = clean(btn.textContent).toUpperCase().replace(/\s/g, "");
        if (bt.indexOf("IMMEDIATEEXIT") >= 0 || bt.indexOf("RESUMEHOLD") >= 0) {
          var hlabel = bt.indexOf("RESUMEHOLD") >= 0 ? "Resume hold" : "Immediate exit";
          btn.setAttribute("data-fbwa380-mcdu-idx", String(idx));
          var hbr = btn.getBoundingClientRect();
          items.push({
            top: hbr.top - pageRect.top + 1, left: 0,
            right: hbr.right - pageRect.left, bot: hbr.bottom - pageRect.top,
            idx: idx, kind: "button", text: hlabel, value: "", disabled: false, fpln: true
          });
          idx++;
          break;
        }
      }
    }

    // DESTINATION footer (MfdFmsFpln.tsx ~1087-1148): the .mfd-fms-fpln-line-destination
    // row pins the DEST ident button + ETA (destTimeLabel, "HH:MM") + EFOB
    // (mfd-label-value-container with the weight unit T/KLB) + distance-to-dest
    // (mfd-label-value-container with "NM") to the bottom of the page. Its class
    // contains "mfd-fms-fpln", so the generic static pass skips it (insideFpln) and
    // the row was never spoken. Fold it into ONE line: "Destination KLAX24R,
    // ETA 19:57, 877 NM, EFOB 29.4 KLB". The ident/DEST buttons are stamped by the
    // interactive pass as before (still clickable); this only adds the read-out.
    var destRow = page.querySelector(".mfd-fms-fpln-line-destination");
    if (destRow && A.isVisible(destRow)) {
      var dIdent = "";
      var dBtns = destRow.querySelectorAll(".mfd-button");
      for (var db = 0; db < dBtns.length; db++) {
        if (!A.isVisible(dBtns[db])) continue;
        var dbt = clean(dBtns[db].textContent);
        // the ident button reads e.g. "KLAX24R"; skip the scroll-row "DEST" button
        // and the dashed not-loaded placeholder
        if (dbt && !isDash(dbt) && dbt.toUpperCase() !== "DEST") { dIdent = dbt; break; }
      }
      var dTime = "";
      var dLabs = destRow.querySelectorAll(".mfd-label");
      for (var dl = 0; dl < dLabs.length; dl++) {
        if (!A.isVisible(dLabs[dl])) continue;
        var dlt = clean(dLabs[dl].textContent);
        if (/^\d{1,2}:\d{2}$/.test(dlt)) { dTime = dlt; break; }
      }
      var dEfob = "", dDist = "";
      var dLvcs = destRow.querySelectorAll(".mfd-label-value-container");
      for (var dv = 0; dv < dLvcs.length; dv++) {
        var dValEl = dLvcs[dv].querySelector(".mfd-label");
        var dUnitEl = dLvcs[dv].querySelector(".mfd-unit-trailing");
        var dVal = dValEl ? clean(dValEl.textContent) : "";
        if (isDash(dVal)) continue;
        var dUnit = (dUnitEl && A.isVisible(dUnitEl)) ? clean(dUnitEl.textContent) : "";
        if (dUnit.toUpperCase() === "NM") { if (!dDist) dDist = dVal + " NM"; }
        else if (!dEfob) dEfob = dVal + (dUnit ? " " + dUnit : "");
      }
      // Emit only when something real is shown (everything is dashes pre-load).
      if (dIdent || dTime || dEfob || dDist) {
        var dParts = ["Destination" + (dIdent ? " " + dIdent : "")];
        if (dTime) dParts.push("ETA " + dTime);
        if (dDist) dParts.push(dDist);
        if (dEfob) dParts.push("EFOB " + dEfob);
        var drr = destRow.getBoundingClientRect();
        items.push({
          top: drr.top - pageRect.top, left: 0,
          right: drr.right - pageRect.left, bot: drr.bottom - pageRect.top,
          idx: 0, kind: "text", text: dParts.join(", "), value: "", disabled: false, fpln: true
        });
      }
    }
    return idx;
  };

  // True/false when `n` is an expandable combo box and whether its option list is
  // currently shown, else implicitly used only for combos. The FBW dropdown button
  // gains an "opened" class and its sibling .mfd-dropdown-menu flips to
  // display:block when expanded (MsfsAvionicsCommon DropdownMenu.tsx). Verified
  // live: collapsed button = "mfd-button", expanded = "mfd-button opened" + menu
  // display:block.
  A.comboExpanded = function (n) {
    var p = n, menu = null;
    for (var k = 0; k < 6 && p; k++) {
      var pc = (p.className && p.className.toString) ? p.className.toString() : "";
      if (/(^| )opened( |$)/.test(pc)) return true;
      if (!menu && pc.indexOf("mfd-dropdown-container") >= 0) menu = p.querySelector(".mfd-dropdown-menu");
      p = p.parentElement;
    }
    if (menu) {
      var d;
      try { d = getComputedStyle(menu).display; } catch (e) { d = menu.style ? menu.style.display : ""; }
      if (d && d !== "none") return true;
    }
    return false;
  };

  // Selected value for a BUTTON-style dropdown (no .mfd-dropdown-inner), e.g. the
  // ARRIVAL/DEPARTURE RWY/APPR/STAR/TRANS selectors. The MFD draws these as a name
  // header with the current selection in the cell directly beneath it (a summary
  // grid), separate from the selector button. Find the header text cell that equals
  // the button label, then read the value cell immediately below it (≤45px, x
  // overlapping). Returns "" when nothing is selected (dashes) so callers append
  // nothing. Verified live on ARRIVAL: RWY→"30R", APPR/VIA/STAR/TRANS→"" (unset).
  A.comboSelectedValue = function (label, node) {
    if (!label) return "";
    var scope = A.mfdRootOf(node);
    var all = scope.getElementsByTagName("*");
    var hdr = null, i;
    for (i = 0; i < all.length; i++) {
      if (all[i] === node || (node.contains && node.contains(all[i]))) continue;
      if (clean(A.directText(all[i])) === label) {
        var r = all[i].getBoundingClientRect();
        if (r.width > 0 && r.height > 0) { hdr = r; break; }
      }
    }
    if (!hdr) return "";
    var best = null, bg = 1e9;
    for (i = 0; i < all.length; i++) {
      var t = clean(A.directText(all[i]));
      if (!t || t === label) continue;
      // A dropdown SELECTION is short (a runway, ident, mode). Never treat a long
      // free-text block as a selected value — esp. the D-ATIS report sitting directly
      // below the per-station "UPDATE OR PRINT" button (.mfd-atccom-datis-block-msgarea).
      if (t.length > 40) continue;
      if (A.ancestorWithClass(all[i], "mfd-atccom-datis-block-msgarea")) continue;
      var rr = all[i].getBoundingClientRect();
      if (!(rr.width > 0 && rr.height > 0)) continue;
      var g = rr.top - hdr.bottom;
      if (g < 0 || g > 45) continue;                              // directly below
      if (rr.right < hdr.left + 3 || rr.left > hdr.right - 3) continue;  // same column
      if (g < bg) { bg = g; best = t; }
    }
    if (!best || /^[-:.\s]+$/.test(best)) return "";              // unset (dashes)
    return best;
  };

  A.enumerateLines = function (root) {
    // Sweep stale stamps DOCUMENT-wide, not just under the active root: both pilot
    // MFDs share one document, and clickElement/sendToField resolve stamps with a
    // document-wide querySelector — a root-scoped sweep left the OTHER side's old
    // stamps alive, so after a Captain->FO switch a click on idx N matched the
    // Captain's stale node first in document order.
    var stale = document.querySelectorAll("[data-fbwa380-mcdu-idx]");
    for (var s = 0; s < stale.length; s++) stale[s].removeAttribute("data-fbwa380-mcdu-idx");
    var staleP = document.querySelectorAll("[data-fbwa380-perf-owned]");
    for (var sp = 0; sp < staleP.length; sp++) staleP[sp].removeAttribute("data-fbwa380-perf-owned");

    var page = root.querySelector(".mfd-navigator-container") || root;

    // A visible modal dialog (MSG LIST / DUPLICATE NAMES) renders as a sibling of
    // .mfd-navigator-container inside the same .mfd-main root.  Both dialogs use
    // .mfd-fms-fpln-dialog-outer as their wrapper; visibility toggles via
    // style.display ("block" = open, "none" = hidden).  When one is open, scrape it
    // instead of the navigator page so the pilot hears — and can drive — the modal.
    // The duplicate-names dialog is a flow-blocker: typing an ambiguous ident hangs
    // silently waiting for a pick until the user selects one.
    var dlgCands = root.querySelectorAll(".mfd-fms-fpln-dialog-outer");
    for (var dc = 0; dc < dlgCands.length; dc++) {
      try {
        if (window.getComputedStyle(dlgCands[dc]).display !== "none") {
          page = dlgCands[dc];
          break;
        }
      } catch (e) {}
    }
    var pageRect = page.getBoundingClientRect();
    var items = [];
    var idx = 1;
    var menuConts = [];   // open menu containers seen this pass (index = group id)

    // TMPY / EO / PENALTY title flags (ActivePageTitleBar.tsx:66-78): the title bar
    // renders three visibility-toggled marker spans next to the page title
    // (.mfd-title-bar-text.label.{penalty|eo|tmpy}). activePageLabel intentionally
    // skips them (TITLE_FLAGS), and as bare leaves they'd read as cryptic "TMPY"
    // fragments — so decode the VISIBLE ones and append them to the title line
    // ("ACTIVE/F-PLN (engine out) (temporary)", screen order PENALTY-EO-TMPY). The
    // flag spans themselves are owned so the static pass below never reads them raw.
    var titleFlags = "";
    var titleBar = page.querySelector(".mfd-title-bar-container");
    if (titleBar) {
      var flagDefs = [["penalty", "(penalty)"], ["eo", "(engine out)"], ["tmpy", "(temporary)"]];
      var flagWords = [];
      for (var tf = 0; tf < flagDefs.length; tf++) {
        var flagEl = titleBar.querySelector(".mfd-title-bar-text.label." + flagDefs[tf][0]);
        if (!flagEl) continue;
        if (A.isVisible(flagEl)) flagWords.push(flagDefs[tf][1]);
        try { flagEl.setAttribute("data-fbwa380-perf-owned", "1"); } catch (e) {}
      }
      titleFlags = flagWords.join(" ");
    }

    // 1) interactive controls (innermost only — a wrapper that contains
    //    another control is skipped so two controls never merge onto a line).
    var nodes = page.querySelectorAll(A.INTERACTIVE_SELECTOR);
    for (var i = 0; i < nodes.length && idx <= 400; i++) {
      var n = nodes[i];
      if (!A.isVisible(n)) continue;
      if (n.querySelector(A.INTERACTIVE_SELECTOR)) continue;
      var kind = A.classify(n);
      var label = A.lineText(n, kind);
      // Drop empty non-input controls (noise). Empty inputs stay (read "blank").
      if (kind !== "input" && label.length === 0) continue;
      n.setAttribute("data-fbwa380-mcdu-idx", String(idx));
      var r = n.getBoundingClientRect();
      var isChoice = kind === "input" && !!A.ancestorWithClass(n, "mfd-dropdown-outer");
      // For combo boxes, report whether the option list is open so the screen
      // reader can say "combo box, collapsed" / "expanded".
      var isCombo = kind === "dropdown" || isChoice;
      // When a context/dropdown menu is OPEN, its options pop up over other content,
      // so the geometric sort scatters them between unrelated lines. Tag each option
      // with its menu container (group id) + its order within that menu, so the
      // regroup step below can pull them out into one contiguous block.
      var menuGroup = -1, menuOrder = -1;
      if (kind === "menu") {
        var cont = A.ancestorWithClass(n, "mfd-context-menu") || A.ancestorWithClass(n, "mfd-dropdown-menu");
        if (cont) {
          var gi = -1;
          for (var mc = 0; mc < menuConts.length; mc++) { if (menuConts[mc] === cont) { gi = mc; break; } }
          if (gi < 0) { gi = menuConts.length; menuConts.push(cont); }
          menuGroup = gi;
          var opts = cont.querySelectorAll(".mfd-context-menu-element, .mfd-dropdown-menu-element");
          for (var oi = 0; oi < opts.length; oi++) { if (opts[oi] === n) { menuOrder = oi; break; } }
        }
      }
      items.push({
        top: r.top - pageRect.top, left: r.left - pageRect.left,
        right: r.right - pageRect.left, bot: r.bottom - pageRect.top,
        idx: idx, kind: kind, text: label,
        value: kind === "input" ? A.readInputValue(n) : "",
        ownUnit: kind === "input" ? A.inputOwnUnit(n) : "",
        leadUnit: kind === "input" ? A.inputLeadUnit(n) : "",
        isChoice: isChoice,
        disabled: n.classList.contains("disabled") || n.classList.contains("inactive"),
        expanded: isCombo ? A.comboExpanded(n) : null,
        menuGroup: menuGroup, menuOrder: menuOrder
      });
      idx++;
    }

    // 1b) The FLIGHT PLAN, if shown, gets its own per-waypoint builder (one clean
    //     line per waypoint) — the generic row scraper below then SKIPS the plan
    //     grid so it isn't read twice.
    var isFpln = !!page.querySelector(".mfd-fms-fpln-line");
    if (isFpln) idx = A.buildFplnLines(page, pageRect, items, idx);

    // 1c) PERF page → structure-aware builder (one clean line per field/row); the generic
    //     static pass + Y-merge then SKIP the regions it consumed (data-fbwa380-perf-owned).
    var isPerf = A.isPerfPage(page);
    if (isPerf) idx = A.buildPerfLines(page, pageRect, items, idx);

    // 1d) General labeled-field cleanup on EVERY page (clean "LABEL: value unit" lines for
    //     any .mfd-label-value-container with an inner label). Runs after the PERF/F-PLN
    //     builders so it only takes the containers they didn't already own.
    idx = A.buildLabeledFields(page, pageRect, items, idx);

    // 1e) SURV CONTROLS page → prefix each radio / toggle button with its column
    //     header ("TCAS: TA/RA", "WXR: AUTO"); matched headers are owned so the
    //     static pass below skips them. Enriches existing items in place (idx kept).
    if (A.isSurvPage(page)) idx = A.buildSurvControls(page, pageRect, items, idx);

    // 2) static-text leaves not inside an interactive control above.
    var all = page.getElementsByTagName("*");
    for (var j = 0; j < all.length; j++) {
      var t = all[j];
      if (!A.isVisible(t)) continue;
      if (A.isInsideInteractive(t)) continue;
      if (isFpln && A.insideFpln(t)) continue;   // plan grid handled by buildFplnLines
      if (A.insidePerf(t)) continue;             // any region owned by a structure-aware builder
      var own = A.directText(t);
      // Skip empty leaves and FBW data-binding artifacts ("null"/"undefined"
      // rendered into empty MFD cells) — never meaningful to the pilot.
      if (!own || own === "null" || own === "undefined") continue;
      var tr = t.getBoundingClientRect();
      var cls = (t.className && t.className.toString) ? t.className.toString() : "";
      // F-PLN leg DISTANCE: a leg's upper row holds the track ("294°") and the
      // distance (a bare integer). Tag the bare-integer one with "NM" so it reads
      // as "80 NM" (nautical miles) instead of an ambiguous "80" (which sounds like
      // a Mach number). Class+pattern based — universal to ANY leg, nothing
      // hard-coded. Track carries "°", FPA carries a sign/decimal, so neither match.
      if (cls.indexOf("mfd-fms-fpln-leg-upper-row") >= 0 && /^\d+$/.test(own)) own = own + " NM";
      // Page-title leaf: append the decoded TMPY/EO/PENALTY flags (see the title-bar
      // pre-pass above; the raw flag spans are owned and skipped, only the title span
      // carries .mfd-title-bar-text here).
      if (titleFlags && cls.indexOf("mfd-title-bar-text") >= 0) own = own + " " + titleFlags;
      items.push({
        top: tr.top - pageRect.top, left: tr.left - pageRect.left,
        right: tr.right - pageRect.left, bot: tr.bottom - pageRect.top,
        idx: 0, kind: "text", text: own, value: "", disabled: false,
        // A naming label is `mfd-label`, NOT a `mfd-label-unit` UNIT span (KT/FL/°C/
        // …). Excluding units here stops associateInputLabels from grabbing a
        // neighbouring unit as a field's name — e.g. the PERF T.O FLEX TEMP input
        // (a °C field) was inheriting the V-speed column's "KT" and reading as "kt".
        // Unit spans still render via the row-merge as the value's trailing unit.
        isLabel: cls.indexOf("mfd-label") >= 0 && cls.indexOf("mfd-label-unit") < 0,
        // F-PLN leg/airway annotation (the SID/STAR/airway name printed to the
        // left of every leg). The same procedure name repeats on every leg it
        // covers, so flag it for consecutive-duplicate suppression below.
        isAnno: cls.indexOf("mfd-fms-fpln-line-annotation") >= 0,
        // SURV STATUS & SWITCHING status indicators (.mfd-surv-status-item) are
        // INDEPENDENT per-column cells (SYS1 status / a section heading / SYS2-or-OFF
        // status). They must never row-merge — comma-joining them, or binding a mid-row
        // heading (TAWS/XPDR/TCAS) to one as a "LABEL: value", reads as a false
        // relationship. Flag so the Y-merge leaves each on its own line.
        isStatusItem: cls.indexOf("mfd-surv-status-item") >= 0
      });
    }

    // Pair each input field with the on-screen label that names it, so the
    // screen reader announces "FLT NBR: <value>" instead of a bare, ambiguous
    // editable field. On the A380 MFD the label sits on the same visual row,
    // immediately to the LEFT of its field (label.right ~= field.left); labels
    // are absolutely positioned, NOT DOM siblings, so this is geometric.
    A.associateInputLabels(items);
    // Drop labels that were folded into an input line (avoid a dangling copy).
    items = items.filter(function (it) { return !it.consumed; });

    // VERT REV → STEP ALTs sub-page: the two editable fields are just "WPT" and "ALT",
    // which out of context don't say what they do. On THIS sub-page only (active STEP
    // ALTs subtab) clarify them to "Step waypoint" / "Step altitude" — the waypoint
    // where the step climb/descent occurs and its target altitude (both must be set to
    // create a step). Scoped so the generic WPT/ALT fields elsewhere are untouched.
    var stepAltsActive = false;
    for (var sa = 0; sa < items.length; sa++) {
      if (items[sa].kind === "subtab" && /STEP ALTs/i.test(items[sa].text) && /active/i.test(items[sa].text)) { stepAltsActive = true; break; }
    }
    if (stepAltsActive) {
      for (var si = 0; si < items.length; si++) {
        if (items[si].kind !== "input") continue;
        if (/^WPT\s*:/.test(items[si].text)) items[si].text = items[si].text.replace(/^WPT\s*:/, "Step waypoint:");
        else if (/^ALT\s*:/.test(items[si].text)) items[si].text = items[si].text.replace(/^ALT\s*:/, "Step altitude:");
      }
    }

    // reading order: rows top→bottom (rounded to a tolerance), then left→right.
    items.sort(function (a, b) {
      var dy = Math.round(a.top / A.ROW_Y_TOLERANCE_PX) - Math.round(b.top / A.ROW_Y_TOLERANCE_PX);
      return dy || (a.left - b.left);
    });

    // Suppress the REPEATED F-PLN procedure/airway annotation. The MFD prints the
    // SID/STAR/airway name (e.g. "BASU1D") next to every leg it covers, so a flat
    // read says "BASU1D" on six lines in a row. Keep it on the FIRST leg of each
    // run and drop it until the procedure/airway CHANGES (BASU1D … then P570),
    // exactly how a sighted pilot groups it. Tracked across waypoint rows (which
    // sit between legs) so the whole SID collapses to one mention.
    var lastAnno = null;
    items = items.filter(function (it) {
      if (!it.isAnno) return true;
      if (it.text === lastAnno) return false;   // same procedure as the leg above
      lastAnno = it.text;
      return true;
    });

    // Merge consecutive STATIC-TEXT cells that share one visual row into a single
    // line. Grid pages (esp. F-PLN, where each waypoint's name / airway / track /
    // distance / time / altitude are separate absolutely-positioned cells) would
    // otherwise read as a token-salad of one cell per line ("BASU1D" / "217°" /
    // "2" / …). Joined with ", " they read as one row with a screen-reader PAUSE
    // between each column ("BASU1D, 217°, 2 NM"; "FROM, TIME, TRK, DIST, FPA").
    // Interactive controls (idx>0) always keep their own line, so a clickable
    // waypoint/field is never swallowed into a text row.
    // A bare UNIT cell (°T, KT, NM, %, KLB, …) attaches to the value before it with
    // a space ("134.4 °T") instead of a comma; everything else is a column pause.
    var isUnitCell = function (s) { return /^(°[TWEC]?|KT|NM|FT|FL|%|KLB|KG|LB|M|MIN|HR|NM\/MIN|°\/[A-Z]+)$/i.test(s); };
    var mergedLines = [];
    for (var mi = 0; mi < items.length; mi++) {
      var cur = items[mi];
      // Pre-built F-PLN waypoint lines (fpln:true) are already complete — never
      // merge them with each other or with neighbouring text.
      if (cur.idx === 0 && cur.kind === "text" && !cur.fpln && !cur.perf && mergedLines.length > 0) {
        var prev = mergedLines[mergedLines.length - 1];
        if (prev.idx === 0 && prev.kind === "text" && !prev.fpln && !prev.perf
            && !cur.isStatusItem && !prev.isStatusItem
            && Math.round(prev.top / A.ROW_Y_TOLERANCE_PX) === Math.round(cur.top / A.ROW_Y_TOLERANCE_PX)) {
          // Compose label→value→unit groups so a row of sibling cells reads as
          // "T.TRK: 134.4 °T, T.HDG: 134.4 °T" instead of comma-soup:
          //  - a label cell ALREADY printing its own ":" ("ACTIVE ATC :") → strip + ": ";
          //  - a NEW label cell (.mfd-label) → ", " starts a fresh column group;
          //  - the first value AFTER a label → ": " binds it to that label;
          //  - a bare unit cell → " " attaches to its value;
          //  - anything else → ", " column pause.
          // A real naming label has letters and isn't a bare unit — so a number/time
          // cell that happens to carry .mfd-label (FUEL&LOAD DEST/ALTN prediction grid)
          // can't spuriously bind the next value with a ":".
          var curRealLabel = cur.isLabel && /[A-Za-z]/.test(cur.text) && !isUnitCell(cur.text);
          if (/:\s*$/.test(prev.text)) { prev.text = prev.text.replace(/\s*:\s*$/, ": ") + cur.text; prev._afterLabel = false; }
          else if (curRealLabel) { prev.text = prev.text + ", " + cur.text; prev._afterLabel = true; }
          else if (prev._afterLabel) { prev.text = prev.text + ": " + cur.text; prev._afterLabel = false; }
          else if (isUnitCell(cur.text)) { prev.text = prev.text + " " + cur.text; }
          // a LEADING "FL" unit binds its flight-level number ("FL 390", not "FL, 390").
          else if (/\bFL$/.test(prev.text) && /^[\d-]/.test(cur.text)) { prev.text = prev.text + " " + cur.text; }
          else { prev.text = prev.text + ", " + cur.text; }
          if (cur.right > prev.right) prev.right = cur.right;
          if (cur.bot > prev.bot) prev.bot = cur.bot;
          continue;
        }
      }
      // Seed the label-group state for a fresh row from whether its first cell is a real
      // (letters, non-unit) label.
      if (cur.idx === 0 && cur.kind === "text") cur._afterLabel = !!cur.isLabel && /[A-Za-z]/.test(cur.text) && !isUnitCell(cur.text);
      mergedLines.push(cur);
    }
    items = mergedLines;

    // Re-attach an orphaned bare-UNIT line (just "°" / "KT" / "FT" / …) to the value
    // line directly above it. The Y-row bucketing (ROW_Y_TOLERANCE_PX) can split a value
    // from its trailing unit when the two sit a pixel either side of a rounding boundary
    // (NAVAIDS SELECTED: "SLOPE: -3.0" at top 818 → bucket 58, its "°" at 820 → bucket 59),
    // leaving the unit stranded on its own line. Fold it back so it reads "SLOPE: -3.0 °".
    var attached = [];
    for (var ai = 0; ai < items.length; ai++) {
      var ln = items[ai];
      if (ln.idx === 0 && ln.kind === "text" && isUnitCell(ln.text) && attached.length > 0) {
        var pvl = attached[attached.length - 1];
        // only fold onto a static value line that ENDS in a number/percent (its value),
        // never onto a label-only line or an interactive control.
        if (pvl.idx === 0 && pvl.kind === "text" && /[\d%)]$/.test(pvl.text)) {
          pvl.text = pvl.text + " " + ln.text;
          if (ln.right > pvl.right) pvl.right = ln.right;
          continue;
        }
      }
      attached.push(ln);
    }
    items = attached;

    items = A.dedupeLines(items);

    // Regroup open-menu options: a popped-up combo/context menu overlays other
    // content, so the geometric sort scatters its options (the dropdown choices, or
    // DELETE */DIR TO/HOLD…) between unrelated lines. Pull each open menu's options
    // into one contiguous block (in menu order) and place that block immediately
    // AFTER the control that opened it, so the choices read right under it:
    //   - combo box: the stamped control in the same .mfd-dropdown-container as the
    //     open .mfd-dropdown-menu;
    //   - F-PLN waypoint/discontinuity revision (.mfd-context-menu): the F-PLN line
    //     the FWS marked '.selected' (set on the revised ident).
    // A menu whose opener can't be found falls back to the TOP (it's the priority).
    var hasMenu = false;
    for (var hm = 0; hm < items.length; hm++) { if (items[hm].menuGroup >= 0) { hasMenu = true; break; } }
    if (hasMenu) {
      var triggerIdxFor = function (cont) {
        var c = (cont.className && cont.className.toString) ? cont.className.toString() : "";
        if (c.indexOf("mfd-dropdown-menu") >= 0) {
          var box = cont;
          for (var k = 0; k < 6 && box; k++) {
            var bc = (box.className && box.className.toString) ? box.className.toString() : "";
            if (bc.indexOf("mfd-dropdown-container") >= 0) break;
            box = box.parentElement;
          }
          if (box) {
            var stamped = box.querySelectorAll("[data-fbwa380-mcdu-idx]");
            for (var s = 0; s < stamped.length; s++) {
              // the opener is the combo control, NOT one of the menu options
              if (!A.ancestorWithClass(stamped[s], "mfd-dropdown-menu"))
                return parseInt(stamped[s].getAttribute("data-fbwa380-mcdu-idx"), 10);
            }
          }
        }
        var sel = root.querySelector(".mfd-fms-fpln-line-ident.selected[data-fbwa380-mcdu-idx], .mfd-fms-fpln-line-special.selected[data-fbwa380-mcdu-idx], .mfd-button.opened[data-fbwa380-mcdu-idx]");
        if (sel) return parseInt(sel.getAttribute("data-fbwa380-mcdu-idx"), 10);
        return -1;
      };

      // collect each menu's options (menu order) and the opener idx of each group
      var groups = {}, nonMenu = [];
      for (var ri = 0; ri < items.length; ri++) {
        var it = items[ri];
        if (it.menuGroup != null && it.menuGroup >= 0) (groups[it.menuGroup] || (groups[it.menuGroup] = [])).push(it);
        else nonMenu.push(it);
      }
      var gk;
      for (gk in groups) groups[gk].sort(function (a, b) { return a.menuOrder - b.menuOrder; });
      var trig = {};
      for (var gc = 0; gc < menuConts.length; gc++) trig[gc] = triggerIdxFor(menuConts[gc]);

      var out = [], placed = {};
      // groups with no findable opener go to the top
      for (gk in groups) if (!(trig[gk] >= 0)) { for (var z = 0; z < groups[gk].length; z++) out.push(groups[gk][z]); placed[gk] = true; }
      // everything else: place each group right after its opener line
      for (var ni = 0; ni < nonMenu.length; ni++) {
        out.push(nonMenu[ni]);
        for (gk in groups) if (!placed[gk] && trig[gk] === nonMenu[ni].idx) { for (var y = 0; y < groups[gk].length; y++) out.push(groups[gk][y]); placed[gk] = true; }
      }
      // any opener we never matched in the list: append (safety net)
      for (gk in groups) if (!placed[gk]) { for (var w = 0; w < groups[gk].length; w++) out.push(groups[gk][w]); placed[gk] = true; }
      items = out;
    }

    // SURV CONTROLS final ordering: the geometric pass interleaves the page's
    // side-by-side columns, so re-sort the survGroup/survOrder-tagged items (set by
    // buildSurvControls) into contiguous logical groups (XPDR → TCAS → WXR → TAWS →
    // SURV). Untagged items (page title, footer) keep their slot; the whole grouped
    // block is spliced in where the FIRST grouped item sat (right after the title).
    // Runs last, AFTER merge/dedupe (which need geometric order), so it only reorders
    // the finished lines — it never re-merges anything.
    var anySurv = false;
    for (var qg = 0; qg < items.length; qg++) { if (items[qg].survGroup != null) { anySurv = true; break; } }
    if (anySurv) {
      var groupedSurv = [];
      for (var qa = 0; qa < items.length; qa++) if (items[qa].survGroup != null) groupedSurv.push(items[qa]);
      groupedSurv.sort(function (a, b) { return (a.survGroup - b.survGroup) || (a.survOrder - b.survOrder); });
      var reSurv = [], survDone = false;
      for (var qb = 0; qb < items.length; qb++) {
        if (items[qb].survGroup != null) {
          if (!survDone) { for (var qc = 0; qc < groupedSurv.length; qc++) reSurv.push(groupedSurv[qc]); survDone = true; }
        } else { reSurv.push(items[qb]); }
      }
      items = reSurv;
    }

    A._mcduElements = items;
    return items;
  };

  // Removes repeated text so the same thing isn't read twice. Two cases:
  //  (a) a stray STATIC label that duplicates an interactive control on the
  //      same (or an adjacent) row — e.g. a "FMS 1" text node sitting beside
  //      the "FMS 1" source dropdown, or the duplicate seen on ATC COM. The
  //      interactive control is the one worth keeping (it's actionable), so
  //      the static copy is dropped.
  //  (b) consecutive identical lines of any kind — collapsed to one, keeping
  //      the interactive line if one of the pair is interactive.
  // Operates on the already-sorted (reading-order) list.
  A.dedupeLines = function (items) {
    function rowBucket(it) { return Math.round(it.top / A.ROW_Y_TOLERANCE_PX); }
    function norm(s) { return clean(s).toUpperCase(); }

    // index interactive labels by row bucket
    var interactiveByRow = {};
    for (var a = 0; a < items.length; a++) {
      if (items[a].idx > 0) {
        var rb = rowBucket(items[a]);
        var key = norm(items[a].text);
        if (!key) continue;
        if (!interactiveByRow[rb]) interactiveByRow[rb] = {};
        interactiveByRow[rb][key] = true;
      }
    }

    var out = [];
    for (var b = 0; b < items.length; b++) {
      var it = items[b];

      // (a) drop a static line that an interactive control already covers
      //     on this or an immediately adjacent row.
      if (it.idx === 0) {
        var k = norm(it.text);
        var here = rowBucket(it);
        var isDup = false;
        for (var d = -1; d <= 1; d++) {
          var bucket = interactiveByRow[here + d];
          if (bucket && bucket[k]) { isDup = true; break; }
        }
        if (isDup) continue;
      }

      // (b) collapse a run of identical text; prefer keeping the interactive one.
      if (out.length > 0 && norm(out[out.length - 1].text) === norm(it.text)) {
        var prev = out[out.length - 1];
        if (prev.idx === 0 && it.idx > 0) out[out.length - 1] = it;
        continue;
      }

      out.push(it);
    }
    return out;
  };

  // Finds a visible interactive element whose label matches `label` (and any
  // of the `synonyms`) and clicks it. Used for page navigation by clicking the
  // MFD's own on-screen buttons rather than guessing KCCU H-event names. If no
  // match is found and `kccuKey` is given, falls back to firing that key.
  // Navigate by clicking a page-selector menu item by its STABLE element id.
  // The A380X MFD renders every dropdown menu item as
  //   <span id="{CAPT|FO}_MFD_pageSelector{Tab}_{idx}" ...>
  // with a click listener that calls uiService.navigateTo(uri) — and the click
  // fires even while the dropdown is collapsed (display:none). This is the
  // reliable navigation path (FlyByWire's own issue #9348 says the KCCU
  // H-events are "practically unusable for external tools"). `prefix` is the
  // tab (Active/Position/SecIndex/Data/…); the side (CAPT/FO) is taken from the
  // active MCDU. If the id isn't present (e.g. a different system's header is
  // mounted) and `kccuKey` is given, fall back to firing that KCCU key — which
  // navigates cross-system for the keyed pages (PERF/INIT/NAVAID/…).
  A.navigateById = function (prefix, index, kccuKey) {
    try {
      if (prefix && index >= 0) {
        var side = A.activeMcdu === 1 ? "CAPT" : "FO";
        var id = side + "_MFD_pageSelector" + prefix + "_" + index;
        var el = document.getElementById(id);
        if (el) {
          // A disabled menu item (FMS not ready for it — WIND/REPORT/GNSS/TIME
          // and the DATA sub-pages need a flight plan / data first) is a no-op
          // click. Report it so the caller can say "not available yet" instead
          // of silently staying on the current page.
          var ec = el.className ? el.className.toString() : "";
          if (ec.indexOf("disabled") >= 0) return "disabled";
          A.clickNode(el); return "ok";
        }
      }
    } catch (e) {}
    if (kccuKey) { A.fireKey(kccuKey); return "key"; }
    return "missing";
  };

  // Resolve the MFD's UIService instance. The <a380x-mfd> custom element's
  // fsInstrument does NOT always carry uiService directly; it lives on the
  // mounted side's display instance (mfdCaptRef / mfdFoRef .instance.uiService).
  // Verified live: navigateTo there changes the page from ANY current page,
  // including cross-system jumps (FMS -> ATCCOM), which the page-selector-id
  // click cannot do because the target system's header isn't mounted yet.
  //
  // Side ordering: when activeMcdu == 2 (First Officer), prefer mfdFoRef so
  // URI navigation drives the FO's MFD, not the Captain's. The per-MFD refs
  // always take precedence over fi.uiService (which is typically undefined);
  // fi.uiService is kept only as a last-resort fallback.
  A.mfdUiService = function () {
    try {
      var mfd = document.querySelector("a380x-mfd");
      if (!mfd || !mfd.fsInstrument) return null;
      var fi = mfd.fsInstrument;
      var refs = A.activeMcdu === 2
        ? ["mfdFoRef", "mfdCaptRef"]
        : ["mfdCaptRef", "mfdFoRef"];
      for (var i = 0; i < refs.length; i++) {
        var r = fi[refs[i]];
        if (r && r.instance && r.instance.uiService && r.instance.uiService.navigateTo)
          return r.instance.uiService;
      }
      // Last-resort: bare fi.uiService (rarely present, kept for future compat).
      if (fi.uiService && fi.uiService.navigateTo) return fi.uiService;
    } catch (e) {}
    return null;
  };

  // Navigate to an MFD page by its UIService URI (e.g. "atccom/msg-record",
  // "fms/active/f-pln"). The robust cross-system path: works from any current
  // page, unlike navigateById (whose page-selector ids only exist while that
  // system's header is mounted). Returns "ok"/"missing".
  A.navigateUri = function (uri) {
    try {
      var u = A.mfdUiService();
      if (u && uri) { u.navigateTo(uri); return "ok"; }
    } catch (e) {}
    return "missing";
  };

  A.navigate = function (label, kccuKey) {
    try {
      var root = A.findRoot(A.activeMcdu);
      if (root) {
        var want = String(label || "").toUpperCase().replace(/[\s.\-]/g, "");
        var nodes = root.querySelectorAll(A.INTERACTIVE_SELECTOR);
        for (var i = 0; i < nodes.length; i++) {
          var n = nodes[i];
          if (!A.isVisible(n)) continue;
          if (n.querySelector(A.INTERACTIVE_SELECTOR)) continue;
          var t = clean(n.textContent).toUpperCase().replace(/[\s.\-]/g, "");
          if (t && (t === want || t.indexOf(want) >= 0)) { A.clickNode(n); return "ok"; }
        }
      }
    } catch (e) {}
    if (kccuKey) { A.fireKey(kccuKey); return "key"; }
    return "missing";
  };

  // ---- public: read ------------------------------------------------------
  A.scrape = function (mcduIndex) {
    try {
      if (mcduIndex === 1 || mcduIndex === 2) A.activeMcdu = mcduIndex;
      var root = A.findRoot(A.activeMcdu);
      if (!root) return JSON.stringify({ ok: false, error: "MFD root not found (powered up?)" });
      var lines = A.enumerateLines(root);
      return JSON.stringify({
        ok: true, mcdu: A.activeMcdu,
        title: A.activePageLabel(root), scratchpad: A.footerMessage(root),
        elements: lines
      });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) });
    }
  };

  // ---- text entry (DOM keypress, NOT KCCU) -------------------------------
  //
  // The A380X MFD InputField (MsfsAvionicsCommon/UiWidgets/InputField.tsx) does
  // NOT consume the KCCU L:vars/H-events for external tools. Its real entry path,
  // read straight from the source, is:
  //   * DOM layout (per field):
  //       div.mfd-input-field-container          <- carries data-fbwa380-mcdu-idx
  //         div.mfd-input-field-text-input-container  (spanningDivRef) <- click = focus
  //           span.mfd-input-field-text-input         (textInputRef)   <- key listeners
  //   * focus: a `click` on the spanning div calls textInput.focus(); the resulting
  //     `focus` event flips the field's internal isFocused=true and clears the edit
  //     buffer (modifiedFieldValue=null), so we do NOT need to backspace first.
  //   * characters: a `keypress` event on the text-input span. The handler reads the
  //     DEPRECATED ev.keyCode (ev.key is undefined in Coherent) via
  //     String.fromCharCode(ev.keyCode).toUpperCase() and accepts /^[A-Z0-9/.+\- ]$/.
  //     => keyCode is simply the ASCII code of the UPPER-CASED character.
  //   * commit: a `keypress` with keyCode 13 (ENTER) -> handleEnter -> blur+validate.
  //   * delete: a `keydown` with keyCode 8 (BACK_SPACE) -> handleBackspace.
  // The `keypress`/`keydown` listeners are always attached; only the auto
  // click->focus listeners are skipped when a field is handleFocusBlurExternally,
  // so we both click the span AND fire a synthetic focus event to be robust.

  // Dispatch a keyboard event whose (read-only) keyCode/which/charCode all report
  // `code` — Coherent's Chromium 49 honours Object.defineProperty getters here.
  A.dispatchKey = function (target, type, code) {
    var ev;
    try { ev = new KeyboardEvent(type, { bubbles: true, cancelable: true }); }
    catch (e) { ev = document.createEvent("Event"); ev.initEvent(type, true, true); }
    try { Object.defineProperty(ev, "keyCode", { get: function () { return code; } }); } catch (e2) {}
    try { Object.defineProperty(ev, "which", { get: function () { return code; } }); } catch (e3) {}
    try { Object.defineProperty(ev, "charCode", { get: function () { return code; } }); } catch (e4) {}
    target.dispatchEvent(ev);
  };

  // Focus an InputField's text-input span the same way a real click does.
  A.focusField = function (span, spanningDiv) {
    try {
      var sd = spanningDiv || span;
      if (typeof sd.click === "function") sd.click();
      else sd.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
    } catch (e) {}
    try { if (typeof span.focus === "function") span.focus(); } catch (e2) {}
    // Fire a synthetic focus event too, in case .click()/.focus() didn't trigger
    // the field's own focus listener (covers re-focus + headless eval contexts).
    try {
      var fe;
      try { fe = new FocusEvent("focus", { bubbles: false, cancelable: false }); }
      catch (e3) { fe = document.createEvent("Event"); fe.initEvent("focus", false, false); }
      span.dispatchEvent(fe);
    } catch (e4) {}
  };

  // Type a string into a focused text-input span via keypress events, then ENTER.
  // Only chars the field accepts are sent; others (e.g. lowercase already upper-cased)
  // map to their ASCII code. Dispatched synchronously — the handlers are synchronous.
  A.typeIntoField = function (span, text, commit) {
    var v = String(text || "").toUpperCase();
    for (var i = 0; i < v.length; i++) {
      var ch = v.charAt(i);
      // Comma is the European decimal separator; map it to a period so users
      // on comma-decimal locales can type coordinates, altitudes, etc.
      if (ch === ",") ch = ".";
      if (!/^[A-Z0-9/.+\- ]$/.test(ch)) continue;
      A.dispatchKey(span, "keypress", ch.charCodeAt(0));
    }
    if (commit) A.dispatchKey(span, "keypress", 13);
  };

  // CLEAR a focused field. A bare ENTER on a just-focused field does NOT clear it —
  // InputField.onBlur sees modifiedFieldValue===null ("Enter after no modification") and
  // re-validates the CURRENT value, so nothing changes (this was the "Backspace doesn't
  // clear the field" bug). The real clear is a BACKSPACE keydown (keyCode 8): handleBackspace
  // sets the edit buffer to '' (via the canBeCleared branch, or the else slice→''), then ENTER
  // commits the empty value → the field clears (subject to the field allowing empty). One
  // backspace clears the WHOLE field — it's the "clear field" semantic, not char-by-char.
  A.clearFocusedField = function (span) {
    A.dispatchKey(span, "keydown", 8);    // BACKSPACE → modifiedFieldValue = ''
    A.dispatchKey(span, "keypress", 13);  // ENTER → blur+validate('') → field cleared
  };

  // The MFD instrument's own EventBus — the ONLY channel that delivers KCCU keys
  // to the MFD from an external tool. The instrument is the <a380x-mfd> custom
  // element; its `fsInstrument.bus` is the msfs-sdk EventBus the MFD subscribes to
  // ('hEvent'). (Coherent.trigger / SimVar "H:" writes do NOT reach it — verified.)
  A.mfdBus = function () {
    try { var el = document.querySelector("a380x-mfd"); return el && el.fsInstrument ? el.fsInstrument.bus : null; }
    catch (e) { return null; }
  };

  A.fireKey = function (key) {
    try {
      if (key === "CLR" || key === "DEL") key = "BACKSPACE";
      else if (key === "SPACE") key = "SP";
      var eventName = "A32NX_KCCU_" + (A.activeMcdu === 1 ? "L" : "R") + "_" + key;
      // Publish the KCCU H-event on the MFD's OWN bus — this is what actually
      // drives KCCU navigation AND the F-PLN line scroll. Fall back to the legacy
      // Coherent/SimVar paths only if the instrument bus can't be reached.
      var bus = A.mfdBus();
      if (bus && typeof bus.pub === "function") { bus.pub("hEvent", eventName, true); return "ok"; }
      if (typeof Coherent !== "undefined" && typeof Coherent.trigger === "function") { Coherent.trigger("H:" + eventName); return "ok"; }
      if (typeof SimVar !== "undefined" && typeof SimVar.SetSimVarValue === "function") { SimVar.SetSimVarValue("H:" + eventName, "number", 0); return "ok"; }
      return "no-dispatch-path";
    } catch (e) {
      return "ERR: " + ((e && e.message) ? e.message : String(e));
    }
  };

  A.clickNode = function (node) {
    try {
      if (typeof node.click === "function") node.click();
      else node.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
    } catch (e) {}
  };

  A.clickElement = function (index) {
    var node = document.querySelector('[data-fbwa380-mcdu-idx="' + index + '"]');
    if (!node) return "missing";
    // FBW RadioButtonGroup (e.g. PERF T.O TOGA/FLEX/DERATED) listens for the
    // 'change' event on its inner <input type=radio>, NOT a click on the <label>
    // (which is the stamped node). A programmatic label.click() in Coherent GT does
    // NOT fire that 'change', so the selection never moved (DERATED/FLEX looked
    // identical). Check the radio and dispatch 'change' directly. Verified live.
    if (node.classList && node.classList.contains("mfd-radio-button")) {
      var ri = node.querySelector('input[type="radio"]');
      if (ri) {
        ri.checked = true;
        var ev;
        try { ev = new Event("change", { bubbles: true }); }
        catch (e2) { ev = document.createEvent("HTMLEvents"); ev.initEvent("change", true, false); }
        ri.dispatchEvent(ev);
        return "ok";
      }
    }
    // A DropdownMenu-wrapped input opens its option list when the DROPDOWN (not
    // the inert inner field) is clicked — so activating "NOTIFY TO ATC" etc.
    // shows the choices instead of doing nothing.
    if (node.classList && node.classList.contains("mfd-input-field-container")) {
      var dd = A.ancestorWithClass(node, "mfd-dropdown-outer");
      if (dd) { A.clickNode(dd); return "ok"; }
    }
    A.clickNode(node);
    return "ok";
  };

  // The A380X MFD has NO shared scratchpad — every value is typed directly into
  // a focused field. So "send to scratchpad then ENTER on a field" collapses to:
  // focus the field, type the text, ENTER. sendScratchpad alone has no target, so
  // it focuses the first editable (non-disabled) input field on the page and types
  // there — but the real entry path the form uses is sendToField(index, text).
  A.sendScratchpad = function (text) {
    if (!text) return "ok";
    var root = A.findRoot(A.activeMcdu);
    if (!root) return "missing";
    var conts = root.querySelectorAll(".mfd-input-field-container");
    for (var i = 0; i < conts.length; i++) {
      if (!A.isVisible(conts[i])) continue;
      if (conts[i].classList && conts[i].classList.contains("disabled")) continue;
      var span = conts[i].querySelector(".mfd-input-field-text-input");
      if (!span) continue;
      var sd = conts[i].querySelector(".mfd-input-field-text-input-container");
      A.focusField(span, sd);
      A.typeIntoField(span, text, true);
      return "ok";
    }
    return "nofield";
  };

  // Nearest ancestor (or self) carrying a class, else null.
  A.ancestorWithClass = function (node, cls) {
    var cur = node;
    while (cur) { if (cur.classList && cur.classList.contains(cls)) return cur; cur = cur.parentElement; }
    return null;
  };

  // Is a DropdownMenu (its .mfd-dropdown-outer) currently open? The dropdown list
  // (.mfd-dropdown-menu, a sibling under .mfd-dropdown-container) is display:none
  // when closed.
  A.dropdownIsOpen = function (outer) {
    try {
      var cont = outer.parentElement;
      var menu = cont && cont.querySelector(".mfd-dropdown-menu");
      if (!menu) return false;
      return window.getComputedStyle(menu).display !== "none";
    } catch (e) { return false; }
  };

  A.sendToField = function (index, newValue) {
    var node = document.querySelector('[data-fbwa380-mcdu-idx="' + index + '"]');
    if (!node) return "missing";
    // Refuse to type into a field that is inactive or disabled — the MFD would
    // silently swallow the keystrokes.  Check the stamped node and up to 3 DOM
    // ancestors (the field wrapper may carry the class, not the leaf span).
    var check = node;
    for (var ai = 0; ai <= 3; ai++) {
      if (!check) break;
      if (check.classList && (check.classList.contains("inactive") || check.classList.contains("disabled")))
        return "inactive";
      check = check.parentElement;
    }
    var info = null;
    for (var i = 0; i < A._mcduElements.length; i++) {
      if (A._mcduElements[i].idx === index) { info = A._mcduElements[i]; break; }
    }
    // Non-input (button/dropdown/tab/menu) — just click.
    if (info && info.kind !== "input") { A.clickNode(node); return "ok"; }

    var span = node.querySelector(".mfd-input-field-text-input");
    if (!span) { A.clickNode(node); return "noinput"; }
    var spanningDiv = node.querySelector(".mfd-input-field-text-input-container");

    // DropdownMenu-wrapped input (e.g. ATC COM "NOTIFY TO ATC", any combo field):
    // its InputField is handleFocusBlurExternally, so a direct span click does NOT
    // focus it — the field only becomes editable when the DROPDOWN is opened
    // (DropdownMenu focuses the inner InputField on open). So open it first.
    // Empty newValue = a CLEAR request (the form's Backspace/Delete on a field). A clear
    // needs a real BACKSPACE+ENTER, not a bare ENTER (see clearFocusedField).
    var isClear = (newValue === null || String(newValue) === "");

    var dd = A.ancestorWithClass(node, "mfd-dropdown-outer");
    if (dd) {
      if (!A.dropdownIsOpen(dd)) A.clickNode(dd);   // toggle open -> focuses inner field
      if (isClear) A.clearFocusedField(span);       // backspace + ENTER (clears, closes)
      else A.typeIntoField(span, newValue, true);   // type + ENTER (commits, closes)
      return "ok";
    }

    // Plain InputField: focus via the real click->focus path, then type/clear + ENTER.
    A.focusField(span, spanningDiv);
    if (isClear) A.clearFocusedField(span);
    else A.typeIntoField(span, newValue, true);
    return "ok";
  };

  A.setMcdu = function (n) { if (n === 1 || n === 2) A.activeMcdu = n; return ""; };

  // Maps a legacy page_/key_ command to its KCCU key and fires it.
  A.pageCommand = function (command) {
    if (Object.prototype.hasOwnProperty.call(A.PAGE_TO_KCCU, command)) {
      var key = A.PAGE_TO_KCCU[command];
      if (key) {
        // F-PLN scrolling: KCCU UP/DOWN moves the window by ONE waypoint, so for a
        // "page" up/down fire it several times to move a screen-worth (a couple of
        // lines of overlap kept for continuity). Other keys fire once.
        var n = (command === "key_next_page" || command === "key_prev_page") ? 6 : 1;
        for (var i = 0; i < n; i++) A.fireKey(key);
      }
      return "ok";
    }
    return "unknown";
  };

  A.ping = function () { return "MSFSBA_A380_OK"; };

  // FMS-computed flight progress for the D / Shift+D hotkeys. None of this is a
  // stock SimVar — it comes from the FMS guidance controller (same numbers the MFD
  // shows). Returns JSON:
  //   distToDest : great-circle distance remaining along the active plan (NM)
  //   distToTD   : NM from the aircraft to Top of Descent (negative = passed); null
  //                until the FMS computes a descent (e.g. on the ground)
  //   distToTC   : NM to Top of Climb (null when none)
  // distToDest works on the ground (verified). The T/D and T/C pseudo-waypoints are
  // only computed once airborne, so they read null pre-flight — VERIFY IN FLIGHT.
  A.flightInfo = function () {
    try {
      var mfd = document.querySelector("a380x-mfd");
      if (!mfd || !mfd.fsInstrument || !mfd.fsInstrument.fmcService) return JSON.stringify({ ok: false, error: "FMS not ready" });
      var m = mfd.fsInstrument.fmcService.master;
      var gc = m && m.guidanceController;
      if (!gc) return JSON.stringify({ ok: false, error: "no guidance" });
      var info = { ok: true, distToDest: null, distToTD: null, distToTC: null, timeToTD: null, timeToTC: null, timeToDest: null, flightPhase: null };
      var map = gc.alongTrackDistancesToDestination;
      var dtd = (map && map.get) ? map.get(0) : null;        // dev build: Map keyed by plan index (0 = active)
      if (typeof dtd === "number" && isFinite(dtd)) info.distToDest = dtd;
      // RELEASE-COMPAT FALLBACK — REMOVE once an FBW A380 release ships the dev FMS API.
      // On the current A380 *release* (live-verified on flybywire-aircraft-a380-842 0.14.0)
      // the Map above does not exist; the guidance controller instead exposes a scalar
      // `alongTrackDistanceToDestination` (singular). The dev build keeps the Map, so this
      // branch only runs on release — dev behaviour is unchanged (no regression).
      if (info.distToDest == null && typeof gc.alongTrackDistanceToDestination === "number" && isFinite(gc.alongTrackDistanceToDestination))
        info.distToDest = gc.alongTrackDistanceToDestination;
      // Total active-plan length (last leg's cumulative distance from start), so a
      // pseudo-waypoint's distanceFromStart can be turned into distance-to-go:
      //   toGo = distToDest - (total - pwp.distanceFromStart)
      // Total plan length = the DESTINATION leg's cumulative distance. Anchor it to
      // the destination explicitly (NOT just the last leg with a number — that can be
      // a missed-approach/hold leg past the runway, giving a wrong datum and a wrong
      // T/D toGo). Fall back to the last finite cumulativeDistance.
      var planTotal = function (plan) {
        if (!plan || !plan.allLegs) return null;
        var legs = plan.allLegs;
        var di = (typeof plan.destinationLegIndex === "number") ? plan.destinationLegIndex : -1;
        if (di >= 0 && legs[di] && legs[di].calculated && isFinite(legs[di].calculated.cumulativeDistance))
          return legs[di].calculated.cumulativeDistance;
        for (var li = legs.length - 1; li >= 0; li--) {
          var c = legs[li] && legs[li].calculated;
          if (c && typeof c.cumulativeDistance === "number" && isFinite(c.cumulativeDistance)) return c.cumulativeDistance;
        }
        return null;
      };
      var total = null;
      // dev build: the active plan is reachable via m.flightPlanInterface.active
      try { total = planTotal(m.flightPlanInterface.active); } catch (e) {}
      // RELEASE-COMPAT FALLBACK — REMOVE once an FBW A380 release ships the dev FMS API.
      // The current release has no m.flightPlanInterface; the active plan lives at
      // gc.flightPlanService.active (or m.flightPlanService.active). Live-verified on
      // 0.14.0 release (dest leg cumulativeDistance). The dev build still has
      // flightPlanInterface, so these fallbacks never execute there — no regression.
      if (total == null) try { total = planTotal(gc.flightPlanService.active); } catch (e) {}
      if (total == null) try { total = planTotal(m.flightPlanService.active); } catch (e) {}
      // Time-to-destination (profile-aware): the same MCDU vertical-profile prediction the FMS
      // uses for DEST UTC. profileManager.mcduProfile.waypointPredictions is a Map keyed by
      // leg index; the destination leg's entry carries secondsFromPresent (live-verified on
      // 0.14.0 release). Wrapped defensively — a build without this structure just yields no
      // time, and the D readout falls back to distance-only (no regression).
      try {
        var actPlan = null;
        try { actPlan = m.flightPlanInterface.active; } catch (e) {}
        if (!actPlan) try { actPlan = gc.flightPlanService.active; } catch (e) {}
        if (!actPlan) try { actPlan = m.flightPlanService.active; } catch (e) {}
        var ddi = (actPlan && typeof actPlan.destinationLegIndex === "number") ? actPlan.destinationLegIndex : -1;
        var pmgr = gc.vnavDriver && gc.vnavDriver.profileManager;
        var preds = pmgr && pmgr.mcduProfile && pmgr.mcduProfile.waypointPredictions;
        if (preds && preds.get && ddi >= 0) {
          var dpr = preds.get(ddi);
          if (dpr && typeof dpr.secondsFromPresent === "number" && isFinite(dpr.secondsFromPresent)) info.timeToDest = dpr.secondsFromPresent;
        }
      } catch (e) {}
      var pw = gc.currentPseudoWaypoints || [];
      for (var p = 0; p < pw.length; p++) {
        if (!pw[p]) continue; // some pseudo-waypoint slots are null in flight; deref guard
        // mcduIdent FIRST: a cruise StepDescent pseudo-waypoint has ident '(T/D)' but
        // mcduIdent '(S/D)', and sits upstream of the real T/D — checking ident first
        // latched onto the step (mirrors coherent-a32nx-flightinfo.js's identical fix).
        var id = ((pw[p].mcduIdent || pw[p].ident || "") + "").toUpperCase();
        // Match ONLY the real Top of Descent / Top of Climb. NOT (DECEL): that is a
        // SEPARATE deceleration point that stays AHEAD during the descent, so matching it
        // made Shift+D keep reporting "N miles to top of descent" after the real T/D was
        // passed (the FMS drops the (T/D) pseudo-waypoint once behind you). Live-confirmed.
        var isTD = id.indexOf("T/D") >= 0, isTC = id.indexOf("T/C") >= 0;
        if (!isTD && !isTC) continue;
        // FMS's own time-to-go + distance-to-go from the VerticalWaypointPrediction —
        // accounts for the descent/decel/wind profile (live-verified), and the prediction
        // disappears once the point is passed. Fall back to the along-track derivation
        // only when the FMS doesn't supply a distance.
        var fpi = pw[p].flightPlanInfo;
        var secs = (fpi && typeof fpi.secondsFromPresent === "number" && isFinite(fpi.secondsFromPresent)) ? fpi.secondsFromPresent : null;
        var dGo = (fpi && typeof fpi.distanceFromAircraft === "number" && isFinite(fpi.distanceFromAircraft)) ? fpi.distanceFromAircraft : null;
        if (dGo == null) {
          var dfs = pw[p].distanceFromStart;
          if (typeof dfs === "number" && isFinite(dfs) && info.distToDest != null && total != null) {
            var toGo = info.distToDest - (total - dfs);
            if (isFinite(toGo)) dGo = toGo;
          }
        }
        if (dGo == null) continue;
        if (isTD) { if (info.distToTD == null) { info.distToTD = dGo; info.timeToTD = secs; } }
        else { if (info.distToTC == null) { info.distToTC = dGo; info.timeToTC = secs; } }
      }
      // FMS flight phase (A32NX/A380 shared L:var): 0 preflight, 1 takeoff, 2 climb,
      // 3 cruise, 4 descent, 5 approach, 6 go-around, 7 done. >= 4 means the TOD is
      // behind us ("past top of descent") — the robust signal PMDG gets from its
      // FMC_DistanceToTOD going negative (the A380's (T/D) pseudo-waypoint just vanishes).
      try { var ph = SimVar.GetSimVarValue("L:A32NX_FMGC_FLIGHT_PHASE", "number"); if (typeof ph === "number") info.flightPhase = ph; } catch (e) {}
      return JSON.stringify(info);
    } catch (e) { return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) }); }
  };

  window.__MSFSBA_A380 = A;
  return "MSFSBA_A380_INSTALLED";
})();
