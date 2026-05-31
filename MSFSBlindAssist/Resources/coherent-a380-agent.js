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
  A.GRID_WIDTH = 36;
  A.MAX_BODY_ROWS = 28;
  A.ROW_Y_TOLERANCE_PX = 14;
  A.KEY_FIRE_DELAY_MS = 50;

  A.INTERACTIVE_SELECTOR = [
    ".mfd-input-field-container", ".mfd-button", ".mfd-icon-button",
    ".mfd-dropdown-outer", ".mfd-dropdown-menu-element", ".mfd-page-selector-outer",
    // Additional interactive MFD widgets (RadioButtonGroup, in-page sub-tabs via
    // TopTabNavigator, SURV/ADS-C buttons, context-menu items) — without these
    // whole controls (e.g. the PERF phase tabs, SURV switches) are unreachable.
    ".mfd-radio-button", ".mfd-top-tab-navigator-bar-element-outer",
    ".mfd-surv-button", ".mfd-surv-status-button", ".mfd-adsc-button",
    ".mfd-context-menu-element"
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
    if (c.contains("mfd-button")) return "button";
    if (c.contains("mfd-icon-button")) return "icon";
    if (c.contains("mfd-dropdown-outer")) return "dropdown";
    if (c.contains("mfd-dropdown-menu-element") || c.contains("mfd-context-menu-element")) return "menu";
    if (c.contains("mfd-page-selector-outer")) return "tab";
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

  A.elementLabel = function (node, kind) {
    var text = "";
    if (kind === "input") {
      var inner = node.querySelector(".mfd-input-field-text-input");
      if (inner) text = clean(inner.textContent);
      var label = node.previousElementSibling;
      if (label && label.classList && label.classList.contains("mfd-label")) {
        var lbl = clean(label.textContent);
        if (lbl) text = lbl + ": " + text;
      }
      return text || "blank";
    }
    if (kind === "button" || kind === "icon" || kind === "menu") {
      var t = clean(node.textContent);
      if (!t && node.getAttribute) t = clean(node.getAttribute("aria-label") || node.getAttribute("title") || "");
      return t || "(unlabeled)";
    }
    if (kind === "dropdown") {
      var di = node.querySelector(".mfd-dropdown-inner");
      return clean(di ? di.textContent : node.textContent) || "blank";
    }
    if (kind === "tab") {
      var t = node.querySelector(".mfd-page-selector-label");
      return clean(t ? t.textContent : node.textContent) || "(tab)";
    }
    return "";
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
      return clean(di ? di.textContent : n.textContent) || "(choice)";
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
    if (kind === "surv" || kind === "survstatus") {
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
        if (lb.right > bestRight) { bestRight = lb.right; best = lb; }
      }
      if (best) {
        var val = inp.value || inp.text || "";
        inp.text = clean(best.text) + ": " + (val || "blank") + (inp.isChoice ? " (combobox)" : "");
        best.consumed = true;
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

  // Decode an F-PLN altitude constraint token: "+N" = at or above N feet, "-N" =
  // at or below N feet, plain "N" = at N feet. (Speed constraints live in the
  // speed column, handled separately, not here.)
  A.fplnConstraint = function (c) {
    if (c.charAt(0) === "+") return "at or above " + c.substring(1) + " feet";
    if (c.charAt(0) === "-") return "at or below " + c.substring(1) + " feet";
    return "at " + c + " feet";
  };

  // Build ONE clean line per F-PLN waypoint. The MFD draws the flight plan as a
  // dense grid: each `.mfd-fms-fpln-line` groups a waypoint with its INBOUND leg
  // (airway/procedure name, track, distance) and any altitude constraint, but
  // those cells sit at different x/y, so a plain row-by-row read scattered them
  // into many lines full of empty "---"/"--:--" dashes. Here we read per waypoint
  // container and fold it into "IDENT, via AIRWAY, N NM, track T°, <constraint>,
  // ETA hh:mm", dropping empty/dash fields and the repeated airway name. Universal
  // to any A380 flight plan (class-driven, nothing hard-coded).
  A.buildFplnLines = function (page, pageRect, items) {
    var lines = page.querySelectorAll(".mfd-fms-fpln-line");
    function cellText(L, sel) { var n = L.querySelector(sel); return n ? clean(n.textContent) : ""; }
    function isDash(s) { return !s || /^[-:.\s]+$/.test(s); }
    for (var i = 0; i < lines.length; i++) {
      var L = lines[i];
      if (!A.isVisible(L)) continue;
      var ident = cellText(L, ".mfd-fms-fpln-line-ident");
      // Strip the "@" overfly marker the MFD glues to the ident (a visual symbol;
      // it reads as "at" otherwise). The waypoint name is what matters here.
      ident = ident.replace(/@/g, "").replace(/\s+/g, " ").replace(/^ | $/g, "");
      if (!ident) continue;                       // a spacer / non-waypoint row
      var anno = cellText(L, ".mfd-fms-fpln-line-annotation");
      // leg upper row carries the track ("217°") and the distance (bare integer)
      var track = "", dist = "";
      var ups = L.querySelectorAll(".mfd-fms-fpln-leg-upper-row");
      for (var u = 0; u < ups.length; u++) {
        var ut = clean(ups[u].textContent);
        if (ut.indexOf("°") >= 0) track = ut;
        else if (/^\d+$/.test(ut)) dist = ut;
      }
      // altitude constraint cell ("+500" / "-5000"); ignore dash placeholders
      var con = "";
      var cons = L.querySelectorAll('[class*="fpln-leg-constraint"]');
      for (var c = 0; c < cons.length; c++) { var ct = clean(cons[c].textContent); if (!isDash(ct)) con = ct; }
      var lr = L.getBoundingClientRect();
      // ETA in the lower row's leftmost cell — only meaningful once airborne
      var eta = cellText(L, ".mfd-fms-fpln-leg-lower-row");
      if (isDash(eta)) eta = "";

      // SPEED + ALTITUDE predictions: classless cells in the lower row, right of
      // the ETA. Blank (dashes) before takeoff, so nothing is added on the ground;
      // once the FMS computes them in flight they fold in as ", 250 knots" /
      // ", flight level 350". Captured by column position WITH a value-pattern
      // guard so a mis-placed cell can never inject garbage. (Verify in flight.)
      var spd = "", alt = "";
      var cells = L.getElementsByTagName("*");
      for (var p = 0; p < cells.length; p++) {
        var pc = cells[p];
        var pcls = (pc.className && pc.className.toString) ? pc.className.toString() : "";
        if (pcls.indexOf("mfd-fms-fpln") >= 0) continue;   // skip the classed cells
        var pt = clean(A.directText(pc));
        if (!pt || isDash(pt)) continue;
        var relX = pc.getBoundingClientRect().left - lr.left;
        if (!spd && relX >= 300 && relX < 405 && /^(M?\.\d{2}|\d{2,3})$/.test(pt)) spd = pt;
        else if (!alt && relX >= 405 && relX < 520 && /^(FL\d{2,3}|\d{3,5})$/.test(pt)) alt = pt;
      }

      var parts = [ident];
      // Keep the procedure/airway name on EVERY leg (NOT deduped) — for a blind
      // pilot it is situational awareness about which points belong to the SID /
      // STAR / airway, exactly as the MFD prints it next to each leg.
      if (anno) parts.push("via " + anno);
      if (dist) parts.push(dist + " NM");
      if (track) parts.push("track " + track);
      if (con) parts.push(A.fplnConstraint(con));
      if (spd) parts.push(/^M?\./.test(spd) ? "Mach " + spd.replace(/^M/, "") : spd + " knots");
      if (alt) parts.push(/^FL/.test(alt) ? "flight level " + alt.substring(2) : alt + " feet");
      if (eta) parts.push("ETA " + eta);

      var r = lr;
      items.push({
        top: r.top - pageRect.top, left: 0,
        right: r.right - pageRect.left, bot: r.bottom - pageRect.top,
        idx: 0, kind: "text", text: parts.join(", "), value: "", disabled: false, fpln: true
      });
    }
  };

  A.enumerateLines = function (root) {
    var stale = root.querySelectorAll("[data-fbwa380-mcdu-idx]");
    for (var s = 0; s < stale.length; s++) stale[s].removeAttribute("data-fbwa380-mcdu-idx");

    var page = root.querySelector(".mfd-navigator-container") || root;
    var pageRect = page.getBoundingClientRect();
    var items = [];
    var idx = 1;

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
      items.push({
        top: r.top - pageRect.top, left: r.left - pageRect.left,
        right: r.right - pageRect.left, bot: r.bottom - pageRect.top,
        idx: idx, kind: kind, text: label,
        value: kind === "input" ? A.readInputValue(n) : "",
        isChoice: kind === "input" && !!A.ancestorWithClass(n, "mfd-dropdown-outer"),
        disabled: n.classList.contains("disabled")
      });
      idx++;
    }

    // 1b) The FLIGHT PLAN, if shown, gets its own per-waypoint builder (one clean
    //     line per waypoint) — the generic row scraper below then SKIPS the plan
    //     grid so it isn't read twice.
    var isFpln = !!page.querySelector(".mfd-fms-fpln-line");
    if (isFpln) A.buildFplnLines(page, pageRect, items);

    // 2) static-text leaves not inside an interactive control above.
    var all = page.getElementsByTagName("*");
    for (var j = 0; j < all.length; j++) {
      var t = all[j];
      if (!A.isVisible(t)) continue;
      if (A.isInsideInteractive(t)) continue;
      if (isFpln && A.insideFpln(t)) continue;   // plan grid handled by buildFplnLines
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
      items.push({
        top: tr.top - pageRect.top, left: tr.left - pageRect.left,
        right: tr.right - pageRect.left, bot: tr.bottom - pageRect.top,
        idx: 0, kind: "text", text: own, value: "", disabled: false,
        isLabel: cls.indexOf("mfd-label") >= 0,
        // F-PLN leg/airway annotation (the SID/STAR/airway name printed to the
        // left of every leg). The same procedure name repeats on every leg it
        // covers, so flag it for consecutive-duplicate suppression below.
        isAnno: cls.indexOf("mfd-fms-fpln-line-annotation") >= 0
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
    var mergedLines = [];
    for (var mi = 0; mi < items.length; mi++) {
      var cur = items[mi];
      // Pre-built F-PLN waypoint lines (fpln:true) are already complete — never
      // merge them with each other or with neighbouring text.
      if (cur.idx === 0 && cur.kind === "text" && !cur.fpln && mergedLines.length > 0) {
        var prev = mergedLines[mergedLines.length - 1];
        if (prev.idx === 0 && prev.kind === "text" && !prev.fpln
            && Math.round(prev.top / A.ROW_Y_TOLERANCE_PX) === Math.round(cur.top / A.ROW_Y_TOLERANCE_PX)) {
          prev.text = prev.text + ", " + cur.text;
          if (cur.right > prev.right) prev.right = cur.right;
          if (cur.bot > prev.bot) prev.bot = cur.bot;
          continue;
        }
      }
      mergedLines.push(cur);
    }
    items = mergedLines;

    items = A.dedupeLines(items);

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
      if (!/^[A-Z0-9/.+\- ]$/.test(ch)) continue;
      A.dispatchKey(span, "keypress", ch.charCodeAt(0));
    }
    if (commit) A.dispatchKey(span, "keypress", 13);
  };

  // Legacy no-op kept so older call sites don't break; the DOM path needs no
  // KCCU keyboard-enable (that SimVar.Set doesn't even stick in-page anyway).
  A.ensureKccuKeyboardOn = function () {};

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
      if (typeof Coherent !== "undefined" && typeof Coherent.trigger === "function") Coherent.trigger("H:" + eventName);
      else if (typeof SimVar !== "undefined" && typeof SimVar.SetSimVarValue === "function") SimVar.SetSimVarValue("H:" + eventName, "number", 0);
    } catch (e) {}
    return "ok";
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
    var dd = A.ancestorWithClass(node, "mfd-dropdown-outer");
    if (dd) {
      if (!A.dropdownIsOpen(dd)) A.clickNode(dd);   // toggle open -> focuses inner field
      A.typeIntoField(span, newValue, true);        // type + ENTER (commits, closes)
      return "ok";
    }

    // Plain InputField: focus via the real click->focus path, then type + ENTER.
    // Focusing clears the field's edit buffer, so no manual backspacing needed.
    A.focusField(span, spanningDiv);
    A.typeIntoField(span, newValue, true);
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

  window.__MSFSBA_A380 = A;
  return "MSFSBA_A380_INSTALLED";
})();
