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

  // Open the OANS control panel if it is hidden. It is toggled by the ND map
  // context-menu item "MAP DATA" (which flips controlPanelVisible). We click that
  // element directly — only when the panel is hidden, so we never toggle it shut.
  A.openPanel = function () {
    var p = A.findRoot();
    if (p && window.getComputedStyle(p).visibility !== "hidden") return "open";
    var items = document.querySelectorAll(".mfd-context-menu-element");
    for (var i = 0; i < items.length; i++) {
      if (clean(items[i].textContent) === "MAP DATA") { A.clickNode(items[i]); return "opened"; }
    }
    var all = document.querySelectorAll("span, div");
    for (var j = 0; j < all.length; j++) {
      if (clean(all[j].textContent) === "MAP DATA" && all[j].children.length <= 2) { A.clickNode(all[j]); return "opened"; }
    }
    return "notfound";
  };

  A.hasInteractive = function (els) {
    for (var i = 0; i < els.length; i++) if (els[i].idx > 0) return true;
    return false;
  };

  // ---- read ----
  A.scrape = function () {
    try {
      // NOTE: opening the panel is done via the explicit openPanel() command
      // (on form open / refresh), NOT here — otherwise a routine poll would
      // re-open the panel every time the user closed it on the ND.
      var root = A.findRoot();
      if (!root) return JSON.stringify({ ok: false, error: "OANS not present on this ND view" });
      var els = A.enumerate(root);
      if (!A.hasInteractive(els)) {
        // Panel is open but empty/collapsed: the OANS is not available yet (no GPS
        // position / ADIRS not aligned / not near an airport). Tell the user.
        els = [{
          top: 0, left: 0, idx: 0, kind: "text", tag: "div", role: "",
          text: "OANS not available yet. The airport map needs an aircraft position: align the ADIRS (NAV) and be at or near an airport. The runway/exit (BTV) controls appear here once it is available.",
          value: "", controlType: "", clickable: false, level: 0, live: "", disabled: false, options: []
        }];
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
