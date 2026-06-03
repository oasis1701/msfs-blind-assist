// coherent-oans-agent.js
//
// In-page agent for the FlyByWire A380X ND OANS (Onboard Airport Navigation
// System) CONTROL PANEL — the airport-map controls used for BTV (Brake-To-
// Vacate) exit selection, airport/runway/exit search, LDG SHIFT, etc. Installed
// via the Coherent GT debugger into the ND view (title "A380X_ND_1"/"_2"); NO
// injection. The OANS control panel is built from the same MFD UI widgets as the
// MCDU (.mfd-input-field, .mfd-dropdown, .mfd-button, .mfd-top-tab-navigator),
// so this agent classifies those and exposes them in the SAME element shape the
// flyPad agent uses — so CoherentNDClient + the WebView2 EFB form render it with
// no extra mapping.
//
//   __MSFSBA_OANS.scrape()              -> JSON {ok,page,elements:[...]}
//   __MSFSBA_OANS.clickElement(index)   -> "ok"|"missing"
//   __MSFSBA_OANS.setValue(index,text)  -> "ok"|"missing"
//   __MSFSBA_OANS.ping()                -> "MSFSBA_OANS_OK"
//
// Target engine: Coherent GT (Chromium 49). ES5 ONLY.

(function () {
  "use strict";
  var A = {};
  A.STAMP = "data-fbwa380-oans-idx";

  function clean(s) { return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, ""); }
  function lower(s) { return (s || "").toString().toLowerCase(); }
  A.cls = function (n) { return (n.className && n.className.toString) ? n.className.toString() : ""; };

  A.isVisible = function (n) {
    try {
      var st = window.getComputedStyle(n);
      if (st.display === "none" || st.visibility === "hidden") return false;
      var r = n.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    } catch (e) { return true; }
  };

  // The OANS control panel container on the ND.
  A.findRoot = function () {
    return document.querySelector(".oans-control-panel") ||
           document.querySelector(".mfd-oans") || null;
  };

  A.INTERACTIVE = [
    ".mfd-input-field-container", ".mfd-button", ".mfd-icon-button",
    ".mfd-dropdown-outer", ".mfd-dropdown-menu-element", ".mfd-context-menu-element",
    ".mfd-top-tab-navigator-bar-element-outer", ".mfd-radio-button"
  ].join(",");

  A.classify = function (n) {
    var c = n.classList;
    if (c.contains("mfd-input-field-container")) return "input";
    if (c.contains("mfd-radio-button")) return "radio";
    if (c.contains("mfd-top-tab-navigator-bar-element-outer")) return "tab";
    if (c.contains("mfd-icon-button")) return "button";
    if (c.contains("mfd-button")) return "button";
    if (c.contains("mfd-dropdown-outer")) return "dropdown";
    if (c.contains("mfd-dropdown-menu-element") || c.contains("mfd-context-menu-element")) return "menu";
    return "other";
  };

  // flyPad-style controlType for the WebView2 renderer.
  A.controlType = function (kind) {
    if (kind === "input") return "text";
    if (kind === "radio") return "checkbox";
    return ""; // button/dropdown/menu/tab -> rendered as button/link by tag+clickable
  };

  A.isClickable = function (kind) {
    return kind === "button" || kind === "dropdown" || kind === "menu" || kind === "tab";
  };

  A.readInputValue = function (n) {
    var s = n.querySelector(".mfd-input-field-text-input");
    return s ? clean(s.textContent) : "";
  };

  A.directText = function (n) {
    var s = "";
    for (var c = 0; c < n.childNodes.length; c++) {
      if (n.childNodes[c].nodeType === 3) s += n.childNodes[c].nodeValue;
    }
    return clean(s);
  };

  A.containsInteractive = function (n) {
    var kids = n.querySelectorAll(A.INTERACTIVE);
    return kids.length > 0;
  };

  A.insideStamped = function (n) {
    var cur = n;
    while (cur) { if (cur.getAttribute && cur.getAttribute(A.STAMP)) return true; cur = cur.parentElement; }
    return false;
  };

  // Short label/value for one control line.
  A.lineText = function (n, kind) {
    if (kind === "input") { return A.readInputValue(n) || "(empty)"; }
    if (kind === "dropdown") {
      var di = n.querySelector(".mfd-dropdown-inner");
      return clean(di ? di.textContent : n.textContent) || "(choice)";
    }
    if (kind === "tab") {
      var lab = n.querySelector(".mfd-top-tab-navigator-bar-element-label");
      var active = n.classList.contains("active") || (lab && lab.classList.contains("active"));
      var t = clean(lab ? lab.textContent : n.textContent);
      return (t || "(tab)") + (active ? " (active tab)" : "");
    }
    if (kind === "radio") {
      var ri = n.querySelector("input[type=radio]");
      var rt = clean(n.textContent);
      return (rt || "(option)") + (ri && ri.checked ? " (selected)" : "");
    }
    var bt = clean(n.textContent);
    if (!bt && n.getAttribute) bt = clean(n.getAttribute("aria-label") || n.getAttribute("title") || "");
    return bt;
  };

  // Fold the nearest left/preceding .mfd-label into an input's line so it reads
  // "RWY: 09L" etc. Returns the input's combined label, or "" if none.
  A.inputLabel = function (n) {
    var prev = n.previousElementSibling, guard = 0;
    while (prev && guard < 4) {
      guard++;
      if (prev.classList && prev.classList.contains("mfd-label")) {
        var t = clean(prev.textContent); if (t) return t;
      }
      var l = prev.querySelector ? prev.querySelector(".mfd-label") : null;
      if (l) { var t2 = clean(l.textContent); if (t2) return t2; }
      prev = prev.previousElementSibling;
    }
    // try the parent's preceding sibling (column layout)
    var par = n.parentElement;
    if (par && par.previousElementSibling && par.previousElementSibling.querySelector) {
      var l2 = par.previousElementSibling.querySelector(".mfd-label");
      if (l2) { var t3 = clean(l2.textContent); if (t3) return t3; }
    }
    return "";
  };

  A.enumerate = function (root) {
    var stale = root.querySelectorAll("[" + A.STAMP + "]");
    for (var s = 0; s < stale.length; s++) stale[s].removeAttribute(A.STAMP);

    var rootRect = root.getBoundingClientRect();
    var items = [];
    var idx = 1;

    // 1) interactive controls (innermost only).
    var nodes = root.querySelectorAll(A.INTERACTIVE);
    for (var i = 0; i < nodes.length && idx <= 300; i++) {
      var n = nodes[i];
      if (!A.isVisible(n)) continue;
      if (A.containsInteractive(n)) continue;     // keep innermost
      if (A.insideStamped(n)) continue;
      var kind = A.classify(n);
      var text = A.lineText(n, kind);
      if (kind === "input") {
        var lbl = A.inputLabel(n);
        if (lbl) text = lbl + ": " + (A.readInputValue(n) || "(empty)");
      }
      if (!text && kind !== "input") continue;
      n.setAttribute(A.STAMP, String(idx));
      var r = n.getBoundingClientRect();
      items.push({
        top: r.top - rootRect.top, left: r.left - rootRect.left,
        idx: idx, kind: kind, tag: n.tagName.toLowerCase(), role: "",
        text: text, value: kind === "input" ? A.readInputValue(n) : "",
        controlType: A.controlType(kind), clickable: A.isClickable(kind),
        level: 0, live: "", disabled: n.classList.contains("disabled"), options: []
      });
      idx++;
    }

    // 2) static-text leaves (labels/readouts: RWY, LDA, BTV STOP DISTANCE, ...).
    var all = root.getElementsByTagName("*");
    for (var j = 0; j < all.length && items.length < 400; j++) {
      var t = all[j];
      if (!A.isVisible(t)) continue;
      if (A.insideStamped(t)) continue;
      if (t.querySelector && t.querySelector(A.INTERACTIVE)) continue;
      var own = A.directText(t);
      if (!own || own.length > 60) continue;
      var tr = t.getBoundingClientRect();
      items.push({
        top: tr.top - rootRect.top, left: tr.left - rootRect.left,
        idx: 0, kind: "text", tag: t.tagName.toLowerCase(), role: "",
        text: own, value: "", controlType: "", clickable: false,
        level: 0, live: "", disabled: false, options: []
      });
    }

    items.sort(function (a, b) {
      var dy = Math.round(a.top / 14) - Math.round(b.top / 14);
      return dy || (a.left - b.left);
    });

    // de-dup consecutive identical text
    var out = [];
    for (var k = 0; k < items.length; k++) {
      var it = items[k];
      if (out.length && lower(out[out.length - 1].text) === lower(it.text)) {
        if (out[out.length - 1].idx === 0 && it.idx > 0) out[out.length - 1] = it;
        continue;
      }
      out.push(it);
    }
    return out;
  };

  // KEY: the OANS airport map + its control panel only RENDER at the lowest "airport
  // zoom" ND range. At any normal nav range the whole .oans-control-panel is 0x0 / hidden
  // and nothing can be read or clicked (live-verified: range 1 -> 0x0; range 0 -> 768x256
  // with the MAP DATA / ARPT SEL / STATUS tabs visible). A blind pilot can't turn the EFIS
  // range knob and watch the map appear, so we force the captain ND to the airport zoom
  // whenever the panel has no geometry. (Harmless when not near an airport — it just zooms
  // the captain ND, which a screen-reader user doesn't see anyway.)
  A.AIRPORT_ZOOM = 0;            // lowest ND range = airport map
  A._emptyZoomedTicks = 0;       // consecutive empty polls while already at the airport zoom
  // Returns true if the panel HAS geometry (is rendered), false otherwise. Forces the zoom
  // when needed; the map then takes ~0.5-1 s to draw, so the first poll after a fresh open
  // is normally still empty (handled by the "loading" message in scrape()).
  A.ensureAirportZoom = function () {
    try {
      var p = A.findRoot();
      if (p) {
        // The control panel lays out at the airport zoom but stays visibility:hidden until
        // it's "opened" on the ND map — and its controls then INHERIT visibility:hidden, so
        // they have geometry but fail our isVisible() test (the panel reads but the agent
        // finds "nothing"). A screen-reader user can't open it on the map, so force it (and
        // its background) visible. React may re-hide on a re-render, so re-apply every poll;
        // scrape() forces this then enumerates synchronously, so the controls are visible the
        // instant we read them.
        try { p.style.visibility = "visible"; } catch (e1) {}
        var bg = document.querySelector(".oans-control-panel-background");
        if (bg) { try { bg.style.visibility = "visible"; } catch (e2) {} }
        if (p.getBoundingClientRect().width > 1) return true;
      }
      if (typeof SimVar !== "undefined") SimVar.SetSimVarValue("L:A32NX_EFIS_L_ND_RANGE", "number", A.AIRPORT_ZOOM);
    } catch (e) {}
    return false;
  };

  // Open / reveal the OANS control panel. With the map rendered (see ensureAirportZoom),
  // the MAP DATA / ARPT SEL / STATUS tab bar is already visible — selecting a tab shows its
  // content. We just make sure the map is zoomed in; the user drives the tabs via the list.
  A.openPanel = function () {
    A._emptyZoomedTicks = 0;     // fresh "loading" window on every open / refresh
    var rendered = A.ensureAirportZoom();
    return rendered ? "open" : "zooming";
  };

  A.hasInteractive = function (els) {
    for (var i = 0; i < els.length; i++) if (els[i].idx > 0) return true;
    return false;
  };

  // A position-aware reason the OANS panel is empty/hidden, so we don't tell a
  // pilot who has ALREADY aligned the ADIRS to "align the ADIRS". The OANS
  // (airport map + BTV) only renders on the ground or in the terminal area near
  // an airport — it's not active in the cruise. Uses SimVar (available in the ND
  // view); falls back to the generic reason if SimVar is unreadable.
  A.unavailableReason = function () {
    var aligned = true, onGround = false;
    try {
      var s1 = SimVar.GetSimVarValue("L:A32NX_ADIRS_ADIRU_1_STATE", "number");
      aligned = (s1 >= 2);  // 0=off, 1=aligning, 2=aligned/NAV
    } catch (e) {}
    try { onGround = SimVar.GetSimVarValue("SIM ON GROUND", "bool") > 0; } catch (e2) {}
    if (!aligned) {
      return "OANS airport map not available. Align the ADIRS first: set the ADIRS mode selectors to NAV and wait for alignment, then this page shows the airport map and the runway/exit (BTV) controls.";
    }
    // ADIRS aligned: it's a position/phase issue, not an ADIRS one.
    return "OANS airport map not active right now. It's an on-ground and terminal-area tool: the airport map and its runway/exit (BTV) controls appear when you're on the ground or within range of an airport (for example on approach)" +
      (onGround ? ", or once an airport is selected on the Navigation Display." : ", not in the cruise. Bring up an airport on the Navigation Display first.");
  };

  // ---- read ----
  A.scrape = function () {
    try {
      // Self-heal: keep the OANS map zoomed in so it stays rendered (it only draws at the
      // airport zoom). Returns true once the panel actually has geometry.
      var rendered = A.ensureAirportZoom();
      var root = A.findRoot();
      if (!root) return JSON.stringify({ ok: false, error: "OANS not present on this ND view" });
      var els = A.enumerate(root);
      if (!A.hasInteractive(els)) {
        // No controls yet. The map takes ~0.5-1 s to draw after we force the zoom, so the
        // first poll(s) after opening are normally still empty — show a LOADING message for
        // the first few seconds and only fall back to the "not available" reason once it's
        // stayed empty long enough that we're genuinely not near a usable airport.
        A._emptyZoomedTicks++;
        var text = (A._emptyZoomedTicks <= 6)
          ? "Loading the airport map - give it a moment, then press F5 to refresh."
          : A.unavailableReason();
        els = [{
          top: 0, left: 0, idx: 0, kind: "text", tag: "div", role: "",
          text: text,
          value: "", controlType: "", clickable: false, level: 0, live: "", disabled: false, options: []
        }];
      } else {
        A._emptyZoomedTicks = 0;
      }
      return JSON.stringify({ ok: true, page: "OANS Airport Map / BTV", elements: els });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) });
    }
  };

  // ---- drive ----
  A.findByIdx = function (index) { return document.querySelector('[' + A.STAMP + '="' + index + '"]'); };

  A.clickNode = function (node) {
    try {
      if (typeof node.click === "function") node.click();
      else node.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
    } catch (e) {}
  };

  A.dropdownIsOpen = function (outer) {
    try {
      var cont = outer.parentElement;
      var menu = cont && cont.querySelector(".mfd-dropdown-menu");
      return menu && window.getComputedStyle(menu).display !== "none";
    } catch (e) { return false; }
  };

  A.ancestorWithClass = function (node, c) {
    var cur = node;
    while (cur) { if (cur.classList && cur.classList.contains(c)) return cur; cur = cur.parentElement; }
    return null;
  };

  A.clickElement = function (index) {
    var node = A.findByIdx(index);
    if (!node) return "missing";
    // A dropdown-wrapped input opens its list when the dropdown is clicked.
    if (node.classList && node.classList.contains("mfd-input-field-container")) {
      var dd = A.ancestorWithClass(node, "mfd-dropdown-outer");
      if (dd) { A.clickNode(dd); return "ok"; }
    }
    A.clickNode(node);
    return "ok";
  };

  // Real FBW InputField entry: focus the text-input span (click its container),
  // then dispatch keypress events (keyCode = ASCII of upper-cased char), ENTER=13.
  A.dispatchKey = function (target, type, code) {
    var ev;
    try { ev = new KeyboardEvent(type, { bubbles: true, cancelable: true }); }
    catch (e) { ev = document.createEvent("Event"); ev.initEvent(type, true, true); }
    try { Object.defineProperty(ev, "keyCode", { get: function () { return code; } }); } catch (e2) {}
    try { Object.defineProperty(ev, "which", { get: function () { return code; } }); } catch (e3) {}
    try { Object.defineProperty(ev, "charCode", { get: function () { return code; } }); } catch (e4) {}
    target.dispatchEvent(ev);
  };

  A.focusField = function (span, spanningDiv) {
    try { var sd = spanningDiv || span; if (sd.click) sd.click(); else sd.dispatchEvent(new MouseEvent("click", { bubbles: true })); } catch (e) {}
    try { if (span.focus) span.focus(); } catch (e2) {}
    try {
      var fe; try { fe = new FocusEvent("focus", { bubbles: false }); } catch (e3) { fe = document.createEvent("Event"); fe.initEvent("focus", false, false); }
      span.dispatchEvent(fe);
    } catch (e4) {}
  };

  A.typeInto = function (span, text) {
    var v = String(text == null ? "" : text).toUpperCase();
    for (var i = 0; i < v.length; i++) {
      var ch = v.charAt(i);
      if (!/^[A-Z0-9/.+\- ]$/.test(ch)) continue;
      A.dispatchKey(span, "keypress", ch.charCodeAt(0));
    }
    A.dispatchKey(span, "keypress", 13);
  };

  A.setValue = function (index, text) {
    var node = A.findByIdx(index);
    if (!node) return "missing";
    var kind = A.classify(node);
    if (kind !== "input") { A.clickNode(node); return "ok"; }
    var span = node.querySelector(".mfd-input-field-text-input");
    if (!span) { A.clickNode(node); return "noinput"; }
    var spanningDiv = node.querySelector(".mfd-input-field-text-input-container");
    // DropdownMenu-wrapped input: open the dropdown first (focuses the field).
    var dd = A.ancestorWithClass(node, "mfd-dropdown-outer");
    if (dd) { if (!A.dropdownIsOpen(dd)) A.clickNode(dd); A.typeInto(span, text); return "ok"; }
    A.focusField(span, spanningDiv);
    A.typeInto(span, text);
    return "ok";
  };

  A.ping = function () { return "MSFSBA_OANS_OK"; };

  window.__MSFSBA_OANS = A;
  return "MSFSBA_OANS_INSTALLED";
})();
