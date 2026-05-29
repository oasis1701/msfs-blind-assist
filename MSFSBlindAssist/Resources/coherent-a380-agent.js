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
    ".mfd-dropdown-outer", ".mfd-dropdown-menu-element", ".mfd-page-selector-outer"
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
    if (c.contains("mfd-button")) return "button";
    if (c.contains("mfd-icon-button")) return "icon";
    if (c.contains("mfd-dropdown-outer")) return "dropdown";
    if (c.contains("mfd-dropdown-menu-element")) return "menu";
    if (c.contains("mfd-page-selector-outer")) return "tab";
    return "other";
  };

  A.readInputValue = function (node) {
    var inner = node.querySelector(".mfd-input-field-text-input");
    return inner ? clean(inner.textContent) : "";
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
      return text || "(empty)";
    }
    if (kind === "button" || kind === "icon" || kind === "menu") {
      var t = clean(node.textContent);
      if (!t && node.getAttribute) t = clean(node.getAttribute("aria-label") || node.getAttribute("title") || "");
      return t || "(unlabeled)";
    }
    if (kind === "dropdown") {
      var di = node.querySelector(".mfd-dropdown-inner");
      return clean(di ? di.textContent : node.textContent) || "(empty dropdown)";
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
  // grid). Inputs show their current value (or "(empty)"); the field's text
  // label, if any, is reported separately as its own static line above it.
  A.lineText = function (n, kind) {
    if (kind === "input") { var v = A.readInputValue(n); return v || "(empty)"; }
    if (kind === "dropdown") {
      var di = n.querySelector(".mfd-dropdown-inner");
      return clean(di ? di.textContent : n.textContent) || "(choice)";
    }
    if (kind === "tab") {
      var t = n.querySelector(".mfd-page-selector-label");
      return clean(t ? t.textContent : n.textContent);
    }
    var bt = clean(n.textContent);
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
        inp.text = clean(best.text) + ": " + (val || "(empty)");
        best.consumed = true;
      }
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
      // Drop empty non-input controls (noise). Empty inputs stay as "(empty)".
      if (kind !== "input" && label.length === 0) continue;
      n.setAttribute("data-fbwa380-mcdu-idx", String(idx));
      var r = n.getBoundingClientRect();
      items.push({
        top: r.top - pageRect.top, left: r.left - pageRect.left,
        right: r.right - pageRect.left, bot: r.bottom - pageRect.top,
        idx: idx, kind: kind, text: label,
        value: kind === "input" ? A.readInputValue(n) : "",
        disabled: n.classList.contains("disabled")
      });
      idx++;
    }

    // 2) static-text leaves not inside an interactive control above.
    var all = page.getElementsByTagName("*");
    for (var j = 0; j < all.length; j++) {
      var t = all[j];
      if (!A.isVisible(t)) continue;
      if (A.isInsideInteractive(t)) continue;
      var own = A.directText(t);
      if (!own) continue;
      var tr = t.getBoundingClientRect();
      var cls = (t.className && t.className.toString) ? t.className.toString() : "";
      items.push({
        top: tr.top - pageRect.top, left: tr.left - pageRect.left,
        right: tr.right - pageRect.left, bot: tr.bottom - pageRect.top,
        idx: 0, kind: "text", text: own, value: "", disabled: false,
        isLabel: cls.indexOf("mfd-label") >= 0
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
        if (el) { A.clickNode(el); return "ok"; }
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

  A.fireKey = function (key) {
    try {
      if (key === "CLR" || key === "DEL") key = "BACKSPACE";
      else if (key === "SPACE") key = "SP";
      var eventName = "A32NX_KCCU_" + (A.activeMcdu === 1 ? "L" : "R") + "_" + key;
      if (typeof Coherent !== "undefined" && typeof Coherent.trigger === "function") Coherent.trigger("H:" + eventName);
      if (typeof SimVar !== "undefined" && typeof SimVar.SetSimVarValue === "function") SimVar.SetSimVarValue("H:" + eventName, "number", 0);
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
      if (conts[i].className.toString().indexOf("disabled") >= 0) continue;
      var span = conts[i].querySelector(".mfd-input-field-text-input");
      if (!span) continue;
      var sd = conts[i].querySelector(".mfd-input-field-text-input-container");
      A.focusField(span, sd);
      A.typeIntoField(span, text, true);
      return "ok";
    }
    return "nofield";
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

    // Input field: focus via the real click->focus path, then type + ENTER.
    // Focusing clears the field's edit buffer, so no manual backspacing needed.
    var span = node.querySelector(".mfd-input-field-text-input");
    if (!span) { A.clickNode(node); return "noinput"; }
    var spanningDiv = node.querySelector(".mfd-input-field-text-input-container");
    A.focusField(span, spanningDiv);
    A.typeIntoField(span, newValue, true);
    return "ok";
  };

  A.setMcdu = function (n) { if (n === 1 || n === 2) A.activeMcdu = n; return ""; };

  // Maps a legacy page_/key_ command to its KCCU key and fires it.
  A.pageCommand = function (command) {
    if (Object.prototype.hasOwnProperty.call(A.PAGE_TO_KCCU, command)) {
      var key = A.PAGE_TO_KCCU[command];
      if (key) A.fireKey(key);
      return "ok";
    }
    return "unknown";
  };

  A.ping = function () { return "MSFSBA_A380_OK"; };

  window.__MSFSBA_A380 = A;
  return "MSFSBA_A380_INSTALLED";
})();
