// coherent-flypad-agent.js
//
// Persistent in-page agent for the FlyByWire flyPad EFB (A32NX + A380X — one shared agent) (flyPadOS 3),
// installed via the Coherent GT debugger's Runtime.evaluate (NO injection, NO
// Community-folder patching). Runs inside the live EFB view's own JS context,
// so the React flyPad DOM is directly reachable.
//
// CoherentEFBClient.cs sends this whole file once per connection to define
// window.__MSFSBA_FLYPAD. Thereafter it calls the small entry points:
//
//   __MSFSBA_FLYPAD.scrape()              -> JSON string (see below)
//   __MSFSBA_FLYPAD.clickElement(index)   -> "ok"|"missing"
//   __MSFSBA_FLYPAD.setValue(index, text) -> "ok"|"missing"
//   __MSFSBA_FLYPAD.ping()                -> "MSFSBA_FLYPAD_OK"
//
// If the page reloads, window.__MSFSBA_FLYPAD disappears; the client detects
// the missing global (ping/scrape returns undefined) and re-installs.
//
// The flyPad is a React app (react-router MemoryRouter): its controls are
// mostly styled <div>s/<a>s, not native <input>s, so we classify by tag /
// role / class. One thing per line, de-duplicated, in reading order.
//
// Target engine: Coherent GT (Chromium 49 era). ES5 ONLY — var, no arrow
// funcs, no String.includes (use indexOf), top-level try/catch.

(function () {
  "use strict";
  var A = {};

  A.ROW_Y_TOLERANCE_PX = 14;
  A._elements = [];

  function clean(s) { return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, ""); }
  function lower(s) { return (s || "").toString().toLowerCase(); }

  A.cls = function (n) {
    return (n.className && n.className.toString) ? n.className.toString() : "";
  };

  // True when `cls` is a STANDALONE class token on n (not a substring — so
  // "bg-theme-highlight" does NOT match "hover:bg-theme-highlight").
  A.hasClassToken = function (n, cls) {
    var toks = A.cls(n).split(/\s+/);
    for (var i = 0; i < toks.length; i++) if (toks[i] === cls) return true;
    return false;
  };

  A.isVisible = function (n) {
    try {
      var st = window.getComputedStyle(n);
      if (st.display === "none" || st.visibility === "hidden") return false;
      var r = n.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    } catch (e) { return false; }
  };

  // The React flyPad mounts under MSFS_REACT_MOUNT (#EFB is an empty shell).
  // Fall back to body if that node is empty.
  A.findRoot = function () {
    var root = document.getElementById("MSFS_REACT_MOUNT");
    if (!root || root.querySelectorAll("*").length === 0) root = document.body;
    return root;
  };

  // True when n carries a React onClick handler. React routes events through its
  // own delegated system, so the handler lives in the fiber props (own key
  // "__reactProps$…" on React 17, "__reactEventHandlers$…" on 16) — NOT as an
  // onclick attribute or property. Lets us detect clickable <div>s that have no
  // role / cursor-pointer / onclick (e.g. the Fuel page refuel start/stop button).
  A.hasReactClick = function (n) {
    try {
      var ks = Object.keys(n);
      for (var i = 0; i < ks.length; i++) {
        if (ks[i].indexOf("__reactProps") === 0 || ks[i].indexOf("__reactEventHandlers") === 0) {
          var p = n[ks[i]];
          if (p && typeof p.onClick === "function") return true;
        }
      }
    } catch (e) {}
    return false;
  };

  // True when n is inside the flyPad top STATUS BAR (bg-theme-statusbar: a fixed
  // h-10 bar at top 0 carrying date / zulu+local time / route + sched times /
  // SimBridge connection / battery, plus the Quick Controls gear). Used to (a)
  // surface the gear as a button and (b) collapse the bar's ~9 text fragments
  // into ONE status line (the per-leaf text is suppressed in the pass-2 loop).
  A.insideStatusBar = function (n) {
    var cur = n, g = 0;
    while (cur && g < 8) {
      if (A.cls(cur).indexOf("statusbar") >= 0) return true;
      cur = cur.parentElement; g++;
    }
    return false;
  };

  // The Quick Controls GEAR: the only clickable, TEXT-LESS, svg-only control in
  // the top status bar (a bare <div onClick> wrapping a <Gear/> icon — no role,
  // no class token, no heading child), so every other classify branch misses it
  // and it was invisible to the screen reader. Gate tightly to the status bar (a
  // top-<45px icon with NO text anywhere inside) so we never start surfacing
  // random icon divs on content pages. It uses onClick (not onMouseDown), so the
  // existing clickNode (which dispatches a real click) actuates it and opens the
  // Quick Controls pane (Align ADIRS / Finish Boarding / SimBridge / OSK / Pause
  // at TOD / sim rate / brightness / power), whose buttons+sliders then classify
  // normally on the next scrape.
  A.isStatusBarIcon = function (n) {
    try {
      if (typeof n.onclick !== "function" && !A.hasReactClick(n)) return false;
      if (clean(n.textContent)) return false;                 // pure icon — no text inside
      if (!n.querySelector || !n.querySelector("svg")) return false;
      if (n.querySelector("h1,h2,h3,h4,h5,h6")) return false;
      var r = n.getBoundingClientRect();
      return r.width > 0 && r.top >= 0 && r.top < 45;          // in the top status bar
    } catch (e) { return false; }
  };

  // True when n is inside the OPEN Quick Controls pane (FBW QuickControls.tsx renders
  // it as an `absolute z-40 bg-theme-accent` panel; the dimmer backdrop is z-30, so
  // z-40 uniquely identifies the pane). Used to (a) relabel the sim-rate +/- chevron
  // buttons and (b) group all the pane controls together right after the gear.
  A.insideQuickControlsPane = function (n) {
    var cur = n, g = 0;
    while (cur && g < 10) {
      if (A.hasClassToken(cur, "z-40")) return true;
      cur = cur.parentElement; g++;
    }
    return false;
  };

  // The current simulation rate shown in the Quick Controls sim-rate incrementer —
  // the `<span>{simRate}x</span>` infoBox sitting between the down/up chevron
  // buttons. Walk up from a button and find the nearest "Nx" leaf (1x, 2x, 0.5x).
  A.simRateValue = function (n) {
    var cur = n, g = 0;
    while (cur && g < 4) {
      try {
        var sp = cur.querySelectorAll("span, div");
        for (var i = 0; i < sp.length; i++) {
          var t = clean(sp[i].textContent);
          if (/^\d+(\.\d+)?x$/i.test(t)) return t;
        }
      } catch (e) {}
      cur = cur.parentElement; g++;
    }
    return "";
  };

  // The Fuel page refuel button stacks two state icons in a FIXED order — PlayFill
  // (start, index 0) shown when stopped, StopCircleFill (stop, index 1) shown when
  // refuelling — and hides the inactive one with the `hidden` class. The first
  // VISIBLE icon reports the state, so we never have to parse icon path data.
  A.refuelIconStarted = function (n) {
    try {
      var svgs = n.querySelectorAll("svg");
      for (var i = 0; i < svgs.length; i++) { if (A.isVisible(svgs[i])) return i > 0; }
    } catch (e) {}
    return false;
  };

  // Returns one of: link, input, slider, checkbox, select, heading, toggle,
  // tab, button, or null (not an element we surface as its own line).
  A.classify = function (n) {
    var c = lower(A.cls(n));
    var role = lower(n.getAttribute && n.getAttribute("role") || "");
    var tag = n.tagName.toLowerCase();
    if (tag === "a") return "link";
    if (tag === "input") {
      var ty = lower(n.getAttribute("type") || "text");
      if (ty === "range") return "slider";
      if (ty === "checkbox" || ty === "radio") return "checkbox";
      return "input";
    }
    if (tag === "select") return "select";
    if (tag === "textarea") return "input";
    if (/^h[1-6]$/.test(tag)) return "heading";
    if (role === "slider" || c.indexOf("slider") >= 0) return "slider";
    if (role === "checkbox" || role === "switch" ||
        c.indexOf("toggle") >= 0 || c.indexOf("switch") >= 0 || c.indexOf("checkbox") >= 0) return "toggle";
    // flyPad Toggle switch (UtilComponents/Form/Toggle): a cursor-pointer pill
    // (rounded-full, w-14) with a knob child and NO text. State lives in the
    // knob's class (translate-x-5 = on). It has no role/aria, so match the shape.
    if (c.indexOf("rounded-full") >= 0 && c.indexOf("cursor-pointer") >= 0 && c.indexOf("w-14") >= 0) return "toggle";
    if (role === "tab" || c.indexOf("tab-") >= 0 || c.indexOf("-tab") >= 0) return "tab";
    if (role === "button" || c.indexOf("button") >= 0 || c.indexOf("btn") >= 0 || tag === "button") return "button";
    if (n.getAttribute && n.getAttribute("contenteditable") === "true") return "input";
    // Electronic-checklist item row: a "flex-row ... space-x-4" row that wraps a
    // small "checkbox box" (.border-4). Clicking the row toggles the item complete;
    // a check icon (svg) appears in the box when done. Match the box in ANY border
    // state — pending/current/completed/sensed items can use different border colors
    // (e.g. border-current vs a muted/completed token), and gating on
    // ".border-4.border-current" silently DROPPED every item that wasn't the current
    // colour, which could leave a multi-item checklist showing only one row. Gate on
    // the cheap class signature so the querySelector only runs on candidate rows.
    if (c.indexOf("space-x-4") >= 0 && c.indexOf("flex-row") >= 0) {
      try { if (n.querySelector(".border-4")) return "checkitem"; } catch (e) {}
    }
    // flyPad SelectInput dropdown (UtilComponents/Form/SelectInput — the
    // Performance takeoff/landing calculators' 13+11 dropdowns): the clickable
    // ROOT is a border-theme-accent div with cursor-pointer (or
    // cursor-not-allowed when disabled) whose VALUE text sits in an inner
    // child next to a ChevronDown svg — so its directText is EMPTY and the
    // generic rules below all miss it (the dropdowns were unreachable).
    // Class-shape detection (no React internals → also works in jsdom tests).
    if (A.isSelectInputRoot(n)) return "button";
    // flyPad action buttons are frequently styled <div>s with NO button/btn
    // token and no role — e.g. the modal "Cancel"/"Confirm" buttons and the
    // segmented setting controls ("Instant / Fast / Real"). They are
    // distinguishable from static text by carrying Tailwind interactivity
    // classes: a hover: rule OR the cursor-pointer class. (The SELECTED segment
    // of a group drops its hover: rule but keeps cursor-pointer, so checking
    // both is what captures the currently-selected option too.) Treat such a
    // leaf with its own short text as a clickable button.
    if (c.indexOf("hover:") >= 0 || c.indexOf("cursor-pointer") >= 0) {
      var dt = A.directText(n);
      if (dt && dt.length <= 30) return "button";
    }
    // Clickable "tile" whose visible label sits in a CHILD heading (e.g. the
    // flyPad Ground service tiles: a cursor-pointer/onclick <div> wrapping an
    // <h_> name + an icon). directText is empty, so the branch above misses it;
    // match the onclick handler plus a child heading instead. enumerate() still
    // skips it when it also wraps a more specific control (containsInteractive).
    if (typeof n.onclick === "function") {
      try { if (n.querySelector && n.querySelector("h1,h2,h3,h4,h5,h6")) return "button"; } catch (e) {}
    }
    // Refuel / boarding / deboarding action button: a `bg-current` <div> whose only
    // content is state icons (Play/Stop on Fuel, ArrowLeftRight/Stop on Payload) with
    // a React onClick — no text, role, cursor-pointer, or onclick property, so every
    // rule above misses it. Surface it ONLY when we can NAME it: a TooltipWrapper
    // caption sibling ("Begin Boarding"/"Begin Deboarding") or a fuel/refuel section
    // heading (the refuel button). That keeps random captionless icon toggles we
    // can't label from flooding the list. labelFor names it from the caption/context.
    if (A.hasClassToken(n, "bg-current")) {
      try {
        if (n.querySelector && n.querySelector("svg") && A.hasReactClick(n) &&
            (A.tooltipSibling(n) || /fuel|refuel/i.test(A.nearestHeading(n)))) return "button";
      } catch (e) {}
    }
    // Quick Controls gear (status-bar icon button). See A.isStatusBarIcon — placed
    // last so the specific branches above win first.
    if (A.isStatusBarIcon(n)) return "button";
    return null;
  };

  // The C# side maps "kind" to a native control. We collapse the rich classify
  // set onto the four families the EFB form renders.
  A.controlType = function (kind, n) {
    if (kind === "input" || kind === "select" || kind === "slider") {
      if (kind === "slider") return "text"; // editable numeric value
      if (kind === "select") return "select";
      return "text";
    }
    if (kind === "checkbox" || kind === "toggle" || kind === "checkitem") return "checkbox";
    return ""; // button/link/heading/tab handled via tag/clickable
  };

  A.directText = function (n) {
    var s = "";
    for (var c = 0; c < n.childNodes.length; c++) {
      if (n.childNodes[c].nodeType === 3) s += n.childNodes[c].nodeValue;
    }
    return clean(s);
  };

  // Prettify a router href into a label: "/pinned-charts" -> "Pinned Charts".
  A.prettifyHref = function (href) {
    href = clean(href || "").replace(/^#/, "").replace(/^\//, "");
    if (!href) return "";
    var seg = href.split("/").pop().replace(/[-_]/g, " ");
    return seg.replace(/\b\w/g, function (c) { return c.toUpperCase(); });
  };

  // Nearest preceding heading text (section context) for an element. The flyPad
  // groups controls under <h1>-<h6> section headers; an unlabeled / generic
  // control borrows that heading so the user knows what it belongs to.
  A.nearestHeading = function (n) {
    var p = n, guard = 0;
    while (p && guard < 80) {
      guard++;
      var prev = p.previousElementSibling;
      while (prev) {
        if (/^h[1-6]$/i.test(prev.tagName)) { var t = clean(prev.textContent); if (t) return t; }
        if (prev.querySelector) { var h = prev.querySelector("h1,h2,h3,h4,h5,h6"); if (h) { var t2 = clean(h.textContent); if (t2) return t2; } }
        prev = prev.previousElementSibling;
      }
      p = p.parentElement;
    }
    return "";
  };

  // The short unit/label text wrapping a value input (e.g. "PAX", "KGS") — the
  // input itself has no aria/own text, but its parent (or grandparent) holds the
  // visible label. Only accept SHORT text so we never grab a whole container.
  A.fieldUnitLabel = function (n) {
    var hops = [n.parentElement, n.parentElement && n.parentElement.parentElement,
                n.parentElement && n.parentElement.parentElement && n.parentElement.parentElement.parentElement];
    for (var h = 0; h < hops.length; h++) {
      var p = hops[h];
      if (!p) continue;
      var t = clean(p.textContent);
      if (t && t.length <= 42) return t;
    }
    return "";
  };

  // A bare unit token ("KGS", "feet", "degrees", "%", "NM"...) — never a field
  // name, so it must not be used as a label on its own.
  A.unitToken = function (s) {
    s = (s || "").trim();
    if (!s) return false;
    if (/^(kgs?|lbs?|feet|ft|ft amsl|m|degrees?|deg|°|%|nm|kt|kts|knots|kts or °\/kts|°\/kts|hpa|inhg|psi|c|°c|x ?1000 ?kgs?)$/i.test(s)) return true;
    // A bare symbol-only token (e.g. "°") is a unit, never a field name.
    if (s.length <= 6 && !/[a-z]/i.test(s)) return true;
    return false;
  };
  // Text that's just a number / punctuation (a value, not a name).
  A.numericish = function (s) {
    return /^[\d.,%/+\-\s]+$/.test((s || "").trim()) && /\d/.test(s || "");
  };

  // Caption for an icon control with no own text: flyPad's TooltipWrapper puts
  // the label in an adjacent NON-interactive sibling span ("Reduce Font Size").
  // Prefer the NEXT sibling (each button's own tooltip follows it). Guarded so a
  // neighbouring button's text is never borrowed.
  A.tooltipSibling = function (n) {
    function ok(s){ s = (s || "").replace(/\s+/g, " ").trim();
      return (s && s.length <= 30 && /[a-zA-Z]/.test(s) && !A.unitToken(s) && !A.numericish(s) && !/[a-z][A-Z]/.test(s)) ? s : ""; }
    function passive(el){ var c = (el && el.className && el.className.toString) ? el.className.toString() : "";
      return el && c.indexOf("hover:") < 0 && c.indexOf("cursor-pointer") < 0; }
    var ns = n.nextElementSibling;
    if (passive(ns)) { var t = ok(ns.textContent); if (t) return t; }
    var ps = n.previousElementSibling;
    if (passive(ps)) { var t2 = ok(ps.textContent); if (t2) return t2; }
    return "";
  };

  // The field's NAME (not its unit/value). flyPad value inputs put the caption
  // either as a PRECEDING sibling somewhere up the ancestor chain (flex rows:
  // "Current Altitude", "Angle", "Per Passenger Weight") or as the FIRST cell of
  // an enclosing table row (W&B: "Cargo", "ZFW", "Passengers"). Walk up looking
  // for the first short, alphabetic, non-unit label.
  A.fieldName = function (n) {
    function clean2(t){ return (t || "").replace(/\s+/g, " ").trim(); }
    // Reject bare units, pure numbers, and CONCATENATED multi-element text — a
    // lowercase->Uppercase run with no space ("MainUpper", "SYNCSynchronize")
    // means textContent merged several sibling controls, not one clean label.
    function good(t){ return t && t.length <= 30 && /[a-zA-Z]/.test(t)
      && !A.unitToken(t) && !A.numericish(t) && !/[a-z][A-Z]/.test(t); }
    var a = n, guard = 0;
    while (a && guard < 9) {
      var ps = a.previousElementSibling;
      while (ps) {
        var st = clean2(ps.textContent);
        if (good(st)) return st;
        ps = ps.previousElementSibling;
      }
      // first cell of an enclosing table row
      if (a.tagName && lower(a.tagName) === "tr" && a.firstElementChild) {
        var ft = clean2(a.firstElementChild.textContent);
        if (good(ft)) return ft;
      }
      a = a.parentElement;
      guard++;
    }
    // Fallback: some layouts put the caption AFTER the field (label-after-input,
    // e.g. the throttle "Deadband +/-"). Only reached when NO preceding label was
    // found, so it never overrides the table/altitude (preceding) convention.
    var b = n, g2 = 0;
    while (b && g2 < 3) {
      var nx = b.nextElementSibling;
      while (nx) {
        var nt = clean2(nx.textContent);
        if (good(nt)) return nt;
        nx = nx.nextElementSibling;
      }
      b = b.parentElement;
      g2++;
    }
    return "";
  };

  // A label good enough for a screen reader. The flyPad behaves like an app:
  // many controls have no text (icon nav-rail links) or a generic label
  // ("Go to Page"). Derive something meaningful from aria/title, then the
  // element's own text, then the router href, then the section heading.
  A.labelFor = function (n) {
    // SelectInput dropdown: compose "FieldLabel: Value" — the value sits in the
    // inner cell, the field name in the enclosing Label row's <p> (TakeoffWidget
    // `<Label text=...><SelectInput/></Label>` = a justify-between row whose
    // first child is the <p>). Label-less SelectInputs (e.g. the OFP/METAR
    // source picker) read their bare value.
    if (A.isSelectInputRoot(n)) {
      var vcell = A.selectInputValueCell(n);
      var val = vcell ? A.directText(vcell) : "";
      var row = n, rh = 0;
      while (row && rh < 4) {
        // NOTE: labelFor declares a local `var lower` (a string) further down,
        // which hoists over the module-level lower() helper for this whole
        // function scope — lowercase inline here.
        var rc = " " + (A.cls(row) || "").toLowerCase() + " ";
        if (rc.indexOf(" justify-between ") >= 0) {
          var p = row.querySelector && row.querySelector("p");
          var fname = p ? clean(p.textContent) : "";
          if (fname) return fname + ": " + (val || "(empty)");
          break;
        }
        row = row.parentElement; rh++;
      }
      return val || "(empty)";
    }

    var base = clean((n.getAttribute && (n.getAttribute("aria-label") || n.getAttribute("title"))) || "");
    if (!base) base = A.directText(n);
    if (!base) {
      // A clickable tile (e.g. a Ground service) keeps its label in a child
      // heading; prefer that clean name over the concatenated textContent.
      var ch = n.querySelector && n.querySelector("h1,h2,h3,h4,h5,h6");
      base = ch ? clean(ch.textContent) : clean(n.textContent);
    }

    // ATC page tune buttons: every FrequencyCard repeats bare "Set Active" /
    // "Set Standby" — prefix the card's callsign + frequency so multiple
    // controllers stay distinguishable: "UNICOM 122.800: Set Active". Live DOM
    // (2026-06-11): the buttons sit in their OWN h2s inside the opacity-0 hover
    // overlay; the card proper carries the bg-theme-secondary token and its
    // first two non-button h2s are callsign + frequency (ATC.tsx FrequencyCard).
    if (base === "Set Active" || base === "Set Standby") {
      var card = n, hops = 0;
      while (card && hops < 6) {
        var cc = " " + ((card.className && card.className.baseVal !== undefined) ? card.className.baseVal : (card.className || "")) + " ";
        if (cc.indexOf(" bg-theme-secondary ") >= 0 && card.querySelectorAll) {
          var h2s = card.querySelectorAll("h2"), names = [];
          for (var hi = 0; hi < h2s.length && names.length < 2; hi++) {
            var ht = clean(h2s[hi].textContent);
            if (ht && ht !== "Set Active" && ht !== "Set Standby") names.push(ht);
          }
          if (names.length > 0) return names.join(" ") + ": " + base;
          break;
        }
        card = card.parentElement; hops++;
      }
    }

    var lower = base.toLowerCase();
    var generic = (base === "" || lower === "go to page" || lower === "go to" || lower === "open");
    if (!generic) return base;

    var href = (n.getAttribute && n.getAttribute("href")) || "";
    var heading = A.nearestHeading(n);
    if (base === "") {
      // Quick Controls gear: its label is the TooltipWrapper caption "Click to open
      // Quick Controls" (the gear's nextElementSibling — verified live), normalised
      // to "Quick Controls".
      if (A.isStatusBarIcon(n)) {
        var qtip = A.tooltipSibling(n);
        if (/quick control/i.test(qtip)) return "Quick Controls";
        return qtip || "Quick Controls";
      }
      // Quick Controls sim-rate incrementer: two icon-only chevron buttons flanking
      // the "Simrate" label + "Nx" value (FBW LargeQuickSettingsIncrementer). The
      // down button carries the source class `mr-5`, the up button `ml-5`. Without
      // this they inherit the adjacent "Simrate"/"1x" text and read meaninglessly.
      // Append the current rate so the selected value is spoken (the "Nx" value text
      // itself is suppressed as it sits inside the status-bar subtree).
      if (n.tagName.toLowerCase() === "button" && A.insideQuickControlsPane(n)
          && (A.hasClassToken(n, "mr-5") || A.hasClassToken(n, "ml-5"))) {
        var rate = A.simRateValue(n);
        var verb = A.hasClassToken(n, "mr-5") ? "Decrease" : "Increase";
        return verb + " simulation rate" + (rate ? ", currently " + rate : "");
      }
      // Refuel / boarding / deboarding action button (bg-current icon button): no
      // text/aria/title. Name it from its TooltipWrapper caption ("Begin Boarding" /
      // "Begin Deboarding") when present, else the fuel context — prefixing the live
      // verb from the visible state icon (first icon visible = not started => Start;
      // the Stop icon visible => Stop).
      if (A.hasClassToken(n, "bg-current")) {
        var procCap = A.tooltipSibling(n);
        if (procCap) {
          var procNoun = procCap.replace(/^(begin|start|stop)\s+/i, "");
          procNoun = procNoun.charAt(0).toLowerCase() + procNoun.slice(1);
          return (A.refuelIconStarted(n) ? "Stop " : "Start ") + procNoun;
        }
        // Captionless bg-current button: ONLY the Fuel page refuel button qualifies
        // for the "refueling" label (its section heading reads Refuel/Fuel). Any other
        // captionless icon button (e.g. a small Payload toggle) is NOT a refuel
        // control — return "" so enumerate drops it instead of mislabelling it
        // "Start refueling".
        if (/fuel|refuel/i.test(A.nearestHeading(n))) {
          return A.refuelIconStarted(n) ? "Stop refueling" : "Start refueling";
        }
        return "";
      }
      // Numeric/value inputs on the flyPad carry NO aria/own text; their visible
      // label/unit (e.g. "PAX", "KGS") sits as the parent's own text next to the
      // field. Prefer that over the section heading, which mislabels every field
      // on the page with the same name (e.g. "Ground").
      var tag = n.tagName.toLowerCase();
      var clz = A.cls(n).toLowerCase();
      var isToggle = (clz.indexOf("rounded-full") >= 0 && clz.indexOf("cursor-pointer") >= 0)
                  || clz.indexOf("toggle") >= 0 || clz.indexOf("switch") >= 0;
      if (tag === "input" || tag === "textarea" ||
          (n.getAttribute && n.getAttribute("contenteditable") === "true")) {
        var ph = clean((n.getAttribute && n.getAttribute("placeholder")) || "");
        // A MEANINGFUL placeholder (an ICAO like "LIMC", a hint like "Search") is
        // the field's best label. But a UNIT placeholder ("feet"/"KGS"/"degrees")
        // or a default-value placeholder ("84"/"20") is NOT the field's name —
        // fall through to derive the real name from a nearby label.
        if (ph && !A.unitToken(ph) && !A.numericish(ph)) return ph;
        // The field NAME sits as a preceding label / the first cell of the
        // enclosing table row (e.g. "Cargo", "ZFW", "Current Altitude", "Angle",
        // "Per Passenger Weight"). Append the unit when we can find one so the
        // readout is "Cargo (KGS)", "Current Altitude (feet)", etc.
        var name = A.fieldName(n);
        if (name) {
          var unit = A.unitToken(ph) ? ph
                   : (A.unitToken(A.fieldUnitLabel(n)) ? clean(A.fieldUnitLabel(n)) : "");
          return unit ? (name + " (" + unit + ")") : name;
        }
        // Last resort: the adjacent text (may be a bare unit, but better than
        // the page heading which mislabels every field identically).
        var fl = A.fieldUnitLabel(n);
        if (fl) return fl;
      }
      // Toggles/switches carry no own text; take the row's setting NAME (its
      // first real text leaf) — never the page heading, which would mislabel
      // every toggle on the page identically.
      if (isToggle) return A.toggleLabel(n);
      if (href) return A.prettifyHref(href);   // icon nav-rail link -> "Dashboard" etc.
      // Icon button with no own text: a flyPad TooltipWrapper renders the caption
      // as an adjacent sibling span ("Reduce Font Size"). Prefer it over the page
      // heading, which would label every icon button with the page name.
      var tip = A.tooltipSibling(n);
      if (tip) return tip;
      if (heading) return heading;
      return "";
    }
    // Generic ("Go to Page"): qualify with the section, else the href target.
    if (heading) return heading + ": " + base;
    if (href) return base + " (" + A.prettifyHref(href) + ")";
    return base;
  };

  A.valueOf = function (kind, n) {
    if (kind === "checkitem") {
      // Completed = the row carries text-utility-green (true in BOTH manual and
      // auto-fill modes). The old "any svg in the box" test misread every
      // auto-sensed condition item as checked (they always contain a Link45deg svg
      // regardless of completion state — ChecklistItemComponent.tsx:113-121).
      var cls = " " + ((n.className && n.className.baseVal !== undefined) ? n.className.baseVal : (n.className || "")) + " ";
      return cls.indexOf("text-utility-green") >= 0 ? "true" : "false";
    }
    if (kind === "slider") return clean(n.getAttribute("aria-valuenow") || n.value || "");
    if (kind === "checkbox" || kind === "toggle") {
      var ck = n.getAttribute && n.getAttribute("aria-checked");
      if (ck !== null && ck !== undefined && ck !== "") return ck === "true" ? "true" : "false";
      if (typeof n.checked === "boolean") return n.checked ? "true" : "false";
      // flyPad Toggle div: the knob child carries translate-x-5 when ON.
      var knob = n.firstElementChild;
      var kc = (knob && knob.className && knob.className.toString) ? knob.className.toString() : "";
      return kc.indexOf("translate-x") >= 0 ? "true" : "false";
    }
    if (kind === "input") {
      if (typeof n.value === "string") return clean(n.value);
      if (n.getAttribute && n.getAttribute("contenteditable") === "true") return clean(n.textContent);
      return "";
    }
    if (kind === "select") { return typeof n.value === "string" ? clean(n.value) : ""; }
    return "";
  };

  A.isClickable = function (kind) {
    return kind === "link" || kind === "button" || kind === "tab";
  };

  // Heading level 1-6 for an h1..h6 node, else 0. Lets the WebView2 renderer
  // emit real <h1>..<h6> so a screen reader's heading quick-nav (H key) works.
  A.headingLevel = function (n) {
    var tag = n.tagName.toLowerCase();
    var m = /^h([1-6])$/.exec(tag);
    return m ? parseInt(m[1], 10) : 0;
  };

  // aria-live politeness for this node or its nearest ancestor with one, so the
  // renderer can mirror live regions ("polite"/"assertive"); "" when none.
  A.liveFor = function (n) {
    var cur = n, guard = 0;
    while (cur && guard < 40) {
      guard++;
      if (cur.getAttribute) {
        var v = lower(cur.getAttribute("aria-live") || "");
        if (v === "polite" || v === "assertive") return v;
        if (lower(cur.getAttribute("role") || "") === "alert") return "assertive";
      }
      cur = cur.parentElement;
    }
    return "";
  };

  // SelectInput root detector (class-shape only — works live AND in jsdom):
  // SelectInput.tsx renders `relative cursor-pointer rounded-md border-2
  // border-theme-accent` (disabled: cursor-not-allowed opacity-50) with the
  // value in an inner `relative flex px-3` child beside a ChevronDown svg.
  A.isSelectInputRoot = function (n) {
    try {
      var c = " " + lower(A.cls(n)) + " ";
      if (c.indexOf(" border-theme-accent ") < 0) return false;
      if (c.indexOf(" cursor-pointer ") < 0 && c.indexOf(" cursor-not-allowed ") < 0) return false;
      if (!n.querySelector || !n.querySelector("svg")) return false;
      return !!A.selectInputValueCell(n);
    } catch (e) { return false; }
  };

  // The inner value cell of a SelectInput root (the `relative flex px-3` div
  // whose direct text is the current selection).
  A.selectInputValueCell = function (n) {
    try {
      for (var i = 0; i < n.children.length; i++) {
        var ch = n.children[i];
        var cc = " " + lower(A.cls(ch)) + " ";
        if (cc.indexOf(" flex ") >= 0 && cc.indexOf(" absolute ") < 0 && A.directText(ch)) return ch;
      }
    } catch (e) {}
    return null;
  };

  // True when the control is disabled: native disabled / aria-disabled, or FBW's
  // Tailwind idioms — pointer-events-none / opacity-20 / grayscale on the node or
  // a near ancestor (FBW disables via wrapper divs; synthetic click() would bypass
  // pointer-events-none, so we must both REPORT and avoid actuating these).
  A.disabledFor = function (n) {
    try {
      if (n.disabled === true) return true;
      if (n.getAttribute && lower(n.getAttribute("aria-disabled") || "") === "true") return true;
      var cur = n, hops = 0;
      while (cur && cur.nodeType === 1 && hops < 4) {
        var c = " " + ((cur.className && cur.className.baseVal !== undefined) ? cur.className.baseVal : (cur.className || "")) + " ";
        if (c.indexOf(" pointer-events-none ") >= 0) return true;
        // cursor-not-allowed: the SelectInput component's disabled idiom
        // (SelectInput.tsx swaps cursor-pointer for it; opacity-50 alone is
        // ambiguous — enabled-but-faded buttons use it too).
        if (hops === 0 && (c.indexOf(" opacity-20 ") >= 0 || c.indexOf(" grayscale ") >= 0 ||
                           c.indexOf(" cursor-not-allowed ") >= 0)) return true;
        cur = cur.parentElement; hops++;
      }
    } catch (e) {}
    return false;
  };

  // Option labels for a real <select>, so the renderer can build a true
  // <select> with <option>s instead of a free-text field. [] when not a select.
  A.optionsFor = function (n) {
    var out = [];
    try {
      if (n.tagName.toLowerCase() === "select" && n.options) {
        for (var i = 0; i < n.options.length; i++) out.push(clean(n.options[i].text || n.options[i].value || ""));
      }
    } catch (e) {}
    return out;
  };

  // True when this node sits inside an already-stamped interactive control —
  // avoids reporting a button's inner text as its own separate line.
  A.isInsideStamped = function (n) {
    var cur = n;
    while (cur) {
      if (cur.getAttribute && cur.getAttribute("data-fbw-efb-idx")) return true;
      cur = cur.parentElement;
    }
    return false;
  };

  // True when n is the ACTIVE option of a segmented / choice control (e.g. the
  // chosen throttle axis "4", the detent being calibrated "TO/GA", a "Fast /
  // Real" sync setting). The bare bg-theme-highlight token is AMBIGUOUS: FBW's
  // primary Button component also uses it as its base background, so every
  // action button (Back, Apply, Reset, Set from Throttle, ...) would otherwise
  // read "(selected)". A genuine segmented control has UNSELECTED sibling options;
  // a standalone button does not. Two unselected-peer signatures exist:
  //   (A) accent-styled peer — bg-theme-accent (the throttle axis selector, and
  //       generic FBW segmented controls).
  //   (B) cursor-pointer option that drops its background to bg-opacity-0 and only
  //       paints it on hover — the throttle-calibration DETENT selector
  //       (TO/GA…Reverse Full) and the independent-axis-count selector (1/2/4).
  //       Here the selected option keeps bg-theme-highlight + bg-opacity-100 while
  //       its peers are bg-opacity-0. Standalone action buttons (Apply / Back /
  //       Set from Throttle / the bottom button row) have no such peer, so they
  //       stay unmarked.
  // Require at least one such peer to disambiguate.
  A.isSelectedSegment = function (n) {
    if (!A.hasClassToken(n, "bg-theme-highlight")) return false;
    // A genuine segmented OPTION is an FBW SelectItem — a cursor-pointer leaf.
    // ACTION buttons (the bottom Reset/Load/Apply/Save row, modal Confirm/Cancel)
    // are <button>s / styled divs WITHOUT cursor-pointer; when a disabled peer is
    // styled bg-theme-accent (invalid-config Apply/Save, or a modal's secondary
    // button) they would otherwise satisfy the peer test below and read a bogus
    // "(selected)". Gate on the option itself being cursor-pointer to exclude them.
    if (!A.hasClassToken(n, "cursor-pointer")) return false;
    var p = n.parentElement;
    if (!p) return false;
    var sibs = p.children;
    for (var i = 0; i < sibs.length; i++) {
      if (sibs[i] === n) continue;
      if (A.hasClassToken(sibs[i], "bg-theme-accent")) return true;                 // (A)
      if (A.hasClassToken(sibs[i], "cursor-pointer") &&
          A.hasClassToken(sibs[i], "bg-opacity-0")) return true;                    // (B)
    }
    // (C) FBW SelectGroup option (boarding/refuel rate Instant/Fast/Real, etc.):
    // EACH option may be wrapped in its own TooltipWrapper <div> (so it can be
    // disabled-with-tooltip), so the SELECTED option's only sibling is its tooltip,
    // not the other options — the immediate-sibling checks above then miss it. The
    // options live together in the SelectGroup container (class "divide-x"). If an
    // ancestor is such a group AND holds ANOTHER cursor-pointer option, n is a
    // selected segment. (A standalone action button has no divide-x group, so this
    // stays specific.)
    var g = p, up = 0;
    while (g && up < 4) {
      if (A.hasClassToken(g, "divide-x")) {
        var opts = g.getElementsByTagName("*");
        for (var o = 0; o < opts.length; o++) {
          if (opts[o] !== n && !opts[o].contains(n) && !n.contains(opts[o]) &&
              A.hasClassToken(opts[o], "cursor-pointer")) return true;
        }
      }
      g = g.parentElement; up++;
    }
    return false;
  };

  // Ground-services tile state. The FBW GroundServiceButton (A320 + A380 share the
  // ServiceButtonState styles) encodes its state in the Tailwind background color:
  //   bg-green-*  = ACTIVE   (service connected / operating)
  //   bg-amber-*  = CALLED   (requested / in transit / released)
  // (DISABLED is opacity-20, already surfaced by disabledFor.) These tiles are plain
  // "button" kinds with no value, so the state was invisible to the reader. Returns
  // "active"/"called" (or "" for an ordinary button). Checks the tile element itself
  // (where buttonsStyles is applied) plus an inner element as a fallback.
  A.serviceClassString = function (e) {
    var c = e && e.className;
    if (c && typeof c !== "string" && c.baseVal != null) c = c.baseVal; // SVGAnimatedString
    return typeof c === "string" ? c : "";
  };
  A.serviceState = function (n) {
    var c = A.serviceClassString(n);
    if (c.indexOf("bg-green") < 0 && c.indexOf("bg-amber") < 0 && n.querySelector) {
      try {
        if (n.querySelector('[class*="bg-green"]')) c = "bg-green";
        else if (n.querySelector('[class*="bg-amber"]')) c = "bg-amber";
      } catch (e) {}
    }
    if (c.indexOf("bg-green") >= 0) return "active";
    if (c.indexOf("bg-amber") >= 0) return "called";
    return "";
  };

  // Open/closed state of a DOOR tile (user request: doors read open/closed, not the
  // generic active/[nothing]). The state colour sits on the cursor-pointer tile
  // WRAPPER (green = open, amber = in transit), not on the inner <h_>, so climb to
  // the nearest cursor-pointer ancestor before reading. No colour = closed. Works in
  // both states: on the ground the tile is the cursor-pointer button itself; in
  // flight the door is disabled and only its inner heading surfaces, so we climb to
  // the (disabled, colourless => "closed") wrapper.
  A.doorOpenState = function (n) {
    var tile = n, g = 0;
    while (tile && g < 5) { if (A.hasClassToken(tile, "cursor-pointer")) break; tile = tile.parentElement; g++; }
    var svc = A.serviceState(tile || n);
    if (svc === "active") return "open";
    if (svc === "called") return "in transit";
    return "closed";
  };

  // True when a nav-rail link or a sub-tab is the ACTIVE one. The flyPad marks
  // the selected nav-rail page link AND the selected Ground sub-tab
  // (Services/Fuel/Payload/Pushback) with the STANDALONE class token
  // "bg-theme-accent" (inactive ones only carry "hover:bg-theme-accent", which
  // hasClassToken correctly rejects). Same marker pageLabel() uses for the page
  // title, so the two always agree.
  A.isActiveTab = function (n) {
    return A.hasClassToken(n, "bg-theme-accent");
  };

  // Precise door identity from the tile's click handler, MATCHING MSFSBA's own door
  // names (its `_doorDefs` table — which is also what the read-only door-state
  // announcements speak). FBW labels several distinct doors with the SAME tile text
  // ("Door Fwd"/"Door Aft"), and its flyPad enum DIGIT is unreliable — e.g. the A380
  // `Main4Right` controls INTERACTIVE POINT OPEN:9, which the FBW MODEL itself names
  // "M5R" and the EWD calls "MAIN 5R", i.e. MSFSBA's "Main Door 5 Right" — NOT "Main 4".
  // So we don't trust FBW's digit: we parse the enum NAME from the handler comment
  // (`() => handleButtonClick(N /* Main4Right */)`) and look it up in a fixed map to the
  // app's authoritative name, keeping the flyPad label consistent with the announcement.
  // GUARDED: an unknown/renamed enum, a minified build (no comment), or a Cargo handler
  // all return "" so the caller falls back to the column-based Left/Right — never a
  // wrong label. Keep this map in sync with FlyByWire{A320,A380}Definition._doorDefs.
  A.DOOR_NAMES = {
    // A320 (FBW enum -> INTERACTIVE POINT -> MSFSBA name): pt 0/1/2/3
    cabinleftdoor: "Forward Left Door", cabinrightdoor: "Forward Right Door",
    aftleftdoor: "Aft Left Door", aftrightdoor: "Aft Right Door",
    // A380: Main1Left=pt0, Main2Left=pt2, Main4Right=pt9 (the model's M5R), Upper1Left=pt10
    main1left: "Main Door 1 Left", main2left: "Main Door 2 Left",
    main4right: "Main Door 5 Right", upper1left: "Upper Door 1 Left"
  };
  A.doorIdentity = function (n) {
    function srcsOf(el) {
      var out = [];
      try { if (typeof el.onclick === "function") out.push("" + el.onclick); } catch (e) {}
      try {
        var ks = Object.keys(el);
        for (var k = 0; k < ks.length; k++) {
          if (ks[k].indexOf("__reactProps") === 0) {
            var p = el[ks[k]];
            if (p && typeof p.onClick === "function") out.push("" + p.onClick);
          }
        }
      } catch (e2) {}
      return out;
    }
    var srcs = [], cur = n, g = 0;
    while (cur && g < 4) { srcs = srcs.concat(srcsOf(cur)); cur = cur.parentElement; g++; }
    // The React handler (which carries the enum-name comment) may sit on the stamped
    // node, an ancestor, OR an inner descendant — the agent doesn't always stamp the
    // exact clickable div (e.g. the A380's first forward door stamps an outer wrapper
    // whose DOM onclick has no comment, while a child holds the React onClick). Scan
    // the tile subtree too. Each door tile is its own small subtree, so this can't pick
    // up a neighbouring door's handler.
    try { var kids = n.getElementsByTagName("*"); for (var d = 0; d < kids.length && d < 50; d++) srcs = srcs.concat(srcsOf(kids[d])); } catch (e3) {}
    // IN FLIGHT the door tile is DISABLED, so FBW wires the DOM onClick to undefined —
    // the loops above find nothing. But the parent still PASSES the onClick closure
    // (with the const-enum `/* Main1Left */` comment) as a React prop, so it survives
    // in the FIBER's memoizedProps. Walk the fiber return chain (the closure sits ~1
    // hop up, on the GroundServiceButton fiber) so the precise identity works disabled
    // too. Only this door's onClick is in its own return chain (siblings aren't
    // ancestors), so this can't borrow a neighbour's identity.
    try {
      function fiberOf(el) { var ks = Object.keys(el); for (var k = 0; k < ks.length; k++) if (ks[k].indexOf("__reactFiber") === 0) return el[ks[k]]; return null; }
      var fb = null, fcur = n, fg = 0;
      while (fcur && fg < 4 && !fb) { fb = fiberOf(fcur); if (!fb) { fcur = fcur.parentElement; fg++; } }
      var hops = 0;
      while (fb && hops < 6) {
        if (fb.memoizedProps && typeof fb.memoizedProps.onClick === "function") srcs.push("" + fb.memoizedProps.onClick);
        fb = fb.return; hops++;
      }
    } catch (ef) {}
    for (var i = 0; i < srcs.length; i++) {
      var m = /\/\*\s*([A-Za-z0-9]+)\s*\*\//.exec(srcs[i]);
      if (m) {
        var nm = A.DOOR_NAMES[m[1].toLowerCase()];
        if (nm) { try { n.setAttribute("data-fbw-door", nm); } catch (e4) {} return nm; }
      }
    }
    // The handler (and its enum comment) is REMOVED when the door button is disabled
    // — FBW sets onClick=undefined while an attached service (jet bridge, stairs)
    // controls that door, so the identity is state-dependent. We stamped the resolved
    // name on the node the first time it WAS parseable (door inactive); reuse that
    // cache so the label stays stable across the door's state changes. The cache is
    // NOT cleared by the per-scrape stale sweep, and the door tile's DOM node persists
    // across React re-renders, so it survives.
    try { var cached = n.getAttribute("data-fbw-door"); if (cached) return cached; } catch (e5) {}
    return "";
  };

  // Read-only ground-equipment status indicator (Wheel Chocks / Safety Cones).
  // FBW renders these as a cursor-pointer <div> with NO click handler whose TEXT
  // color encodes state: text-green-500 = placed/present, text-gray-500 = not
  // placed (source A380Services.tsx: "Wheel Chocks and Security Cones are only
  // visual information"). They are NOT controls — there is no onClick/setter — so
  // they surface as a status line, never a clickable button. Detect the wrapper
  // STRUCTURALLY (so it is language-independent) starting from the tile's heading
  // and walking up a few levels. Returns "placed"/"not placed", else "". The
  // no-handler gate means if a future FBW build makes them real buttons, this
  // bows out and the normal button + serviceState path takes over.
  A.groundStatusTile = function (n) {
    var p = n, guard = 0;
    while (p && guard < 3) {
      guard++;
      var c = lower(A.cls(p));
      var hasState = c.indexOf("text-green-500") >= 0 || c.indexOf("text-gray-500") >= 0;
      if (c.indexOf("cursor-pointer") >= 0 && hasState &&
          typeof p.onclick !== "function" && !A.hasReactClick(p)) {
        return c.indexOf("text-green-500") >= 0 ? "placed" : "not placed";
      }
      p = p.parentElement;
    }
    return "";
  };

  // ---- Ground form builders (Payload / Fuel) -----------------------------
  // These re-represent the structured Payload/Fuel content as clean, ordered
  // screen-reader lines. They re-surface the REAL <input> nodes (stamping idx)
  // so set/click are unchanged, and mark suppressed/replaced subtrees with
  // data-fbw-ground-region so enumerate's generic pass skips them. The nav rail
  // and Ground sub-tab strip live OUTSIDE these subtrees and flow through normally.

  // True when the named Ground sub-tab ("payload"/"fuel") is the ACTIVE one (its
  // /ground/<name> link carries the standalone bg-theme-accent token). Language-
  // independent page detector.
  A.activeGroundSub = function (root, name) {
    try {
      var links = root.querySelectorAll("a[href]");
      for (var i = 0; i < links.length; i++) {
        var h = links[i].getAttribute("href") || "";
        if (h.indexOf("/ground/" + name) >= 0 && A.hasClassToken(links[i], "bg-theme-accent")) return true;
      }
    } catch (e) {}
    return false;
  };

  // True when n is inside a subtree a Ground builder has taken ownership of
  // (marked data-fbw-ground-region). enumerate skips these in both passes.
  A.insideGroundRegion = function (n) {
    var cur = n;
    while (cur) {
      if (cur.getAttribute && cur.getAttribute("data-fbw-ground-region")) return true;
      cur = cur.parentElement;
    }
    return false;
  };

  A.markOwned = function (n) { if (n && n.setAttribute) n.setAttribute("data-fbw-ground-region", "1"); };

  // A380 Quick Controls "Cabin Lighting" row — suppressed. Its rc-slider has no native
  // input and the injected agent CANNOT write SimVars (Coherent restriction — see the
  // CLAUDE.md flyPad note), so it can't be set from here; it surfaced as a misleading
  // "0%Set Cabin Lighting" button. Cabin brightness/auto are now app-side panel controls
  // (A380 Interior Lighting). Own the whole row so the generic passes skip it.
  A.suppressCabinLighting = function (root) {
    try {
      var divs = root.getElementsByTagName("div");
      for (var i = 0; i < divs.length; i++) {
        var d = divs[i];
        if (A.cls(d).indexOf("flex-row") < 0) continue;
        var t = clean(d.textContent);
        if (!t || t.length > 60 || t.toLowerCase().indexOf("cabin lighting") < 0) continue;
        if (d.querySelector && d.querySelector(".rc-slider")) { A.markOwned(d); return; }
      }
    } catch (e) {}
  };

  // Suppress the per-page "Fill … from SimBrief" controls on Payload/Fuel (the
  // user imports via the Dashboard). With no plan loaded only a hover-tooltip
  // caption exists; the real CloudArrowDown icon button renders only when planned
  // values differ from current. Own the innermost caption element + its clickable
  // icon-button sibling in the same TooltipWrapper — but NEVER a sibling holding an
  // <input> (the Fuel value field lives next to it).
  A.suppressSimbrief = function (section) {
    var all = section.getElementsByTagName("*");
    for (var i = 0; i < all.length; i++) {
      var n = all[i];
      var tc = n.textContent || "";
      if (tc.length > 60 || !/simbrief/i.test(tc)) continue;
      var hasInnerRef = false;
      for (var c = 0; c < n.children.length; c++) { if (/simbrief/i.test(n.children[c].textContent || "")) { hasInnerRef = true; break; } }
      if (hasInnerRef) continue;                       // not the innermost — keep descending
      A.markOwned(n);                                   // the caption
      var par = n.parentElement;
      if (!par) continue;
      for (var s = 0; s < par.children.length; s++) {
        var ch = par.children[s];
        if (ch === n) continue;
        try { if (ch.getElementsByTagName("input").length) continue; } catch (e) { continue; }   // never the value field
        var clickable = (typeof ch.onclick === "function") || A.hasReactClick(ch) ||
                        ch.tagName.toLowerCase() === "button" || A.cls(ch).indexOf("cursor-pointer") >= 0 ||
                        (ch.getElementsByTagName && ch.getElementsByTagName("svg").length > 0);
        if (clickable) A.markOwned(ch);                 // the CloudArrowDown icon button
      }
    }
  };

  // Join a value cell FBW renders as separate spans (dim leading-zero pad + number
  // + unit) into one clean string, taking the FIRST numeric token so a trailing
  // tooltip concatenated into the same cell is ignored: "0154 PAX" -> "154 PAX",
  // "34.64%Maximum ZFWCG 40%" -> "34.64 %".
  A.joinValueCell = function (cell) {
    if (!cell) return "";
    var t = clean(cell.textContent);
    var m = /0*(\d[\d,]*\.?\d*)\s*(%|[A-Za-z]{1,4})?/.exec(t);
    if (m) { return m[2] ? (m[1] + " " + m[2]) : m[1]; }
    return t;
  };

  // Find a "Maximum <Row> <value>" readout for a row label; returns the trailing
  // value(+unit), else "". Matched by the row label being a substring of the line
  // (both from the same t() translation -> language-independent).
  A.findMaxFor = function (root, rowLabel) {
    if (!rowLabel) return "";
    var rl = lower(rowLabel);
    var all = root.getElementsByTagName("*");
    for (var i = 0; i < all.length; i++) {
      var own = A.directText(all[i]);
      if (!own) continue;
      if (lower(own).indexOf(rl) >= 0 && /max/i.test(own)) {
        var mm = /([\d][\d,]*\.?\d*\s*[%A-Za-z]*)\s*$/.exec(own);
        if (mm) return clean(mm[1]);
      }
    }
    return "";
  };

  // Throttle-calibration page relabel. The two axis panels (Axis 1 = Throttle
  // 1+2, Axis 2 = Throttle 3+4) are shown SIDE BY SIDE around a shared centre
  // detent-selector column, so several controls/values are DUPLICATED with
  // identical labels — "Current Value: …", the low/high range numbers (rendered
  // as two bare numbers like 0.95 / 1.00 with NO Low/High label in the DOM),
  // "Deadband +/-", and "Set from Throttle". A blind pilot then can't tell which
  // axis a "Set from Throttle" / value belongs to, and the range numbers are
  // meaningless. This pass qualifies each ambiguous control/value with the axis
  // whose heading it sits nearest to (by horizontal position — the DOM nests the
  // shared detent column inside one axis's subtree, so ancestry is unreliable),
  // and labels the two range numbers Low/High by their x order within the axis.
  // Collapse the top STATUS BAR into ONE readable line — date, zulu/local time,
  // route + scheduled out/in times, SimBridge connection, battery — instead of
  // the ~9 scattered text fragments it otherwise scatters at the top of EVERY
  // page. The Quick Controls gear is surfaced separately as a button (its "Click
  // to open Quick Controls" tooltip is dropped here); the per-leaf text is
  // suppressed in the pass-2 loop via A.insideStatusBar. SimBridge status and
  // battery are pulled to the end (they sit on the right of the real bar, but
  // their tooltips render at a collapsed left:16, so a raw left-sort would mis-
  // lead them). Returns a single item (top:-1 so it reads first), or null.
  A.buildStatusBarLine = function (root) {
    var bar = root.querySelector('[class*="statusbar"]');
    if (!bar) return null;
    // When the Quick Controls pane is OPEN it renders INSIDE the bar subtree (an
    // absolute z-40 panel + a full-screen z-30 h-screen backdrop, per FBW
    // QuickControls.tsx), so its buttons' tooltips would pollute the status line.
    // Its controls are surfaced as their own buttons, so skip the line while open.
    if (bar.querySelector('[class~="z-40"]') || bar.querySelector('[class~="h-screen"]')) return null;
    var all = bar.getElementsByTagName("*"), seen = {}, arr = [];
    for (var i = 0; i < all.length; i++) {
      var el = all[i];
      if (!A.isVisible(el)) continue;
      var t = A.directText(el);
      if (!t || t.length > 40) continue;
      if (/quick control/i.test(t)) continue;             // the gear (its own button)
      if (/^(true|false|undefined|null)$/i.test(t)) continue;
      if (seen[t]) continue; seen[t] = 1;
      var r = el.getBoundingClientRect();
      arr.push({ left: r.left, top: r.top, t: t });
    }
    if (!arr.length) return null;
    arr.sort(function (a, b) { return (a.left - b.left) || (a.top - b.top); });
    var main = [], conn = "", batt = "";
    for (var j = 0; j < arr.length; j++) {
      var s = arr[j].t;
      if (/simbridge|connected|disconnected/i.test(s)) { conn = s; continue; }
      if (/%$/.test(s)) { batt = "battery " + s; continue; }
      main.push(s);
    }
    if (conn) main.push(conn);
    if (batt) main.push(batt);
    if (!main.length) return null;
    return {
      top: -1, left: 0, idx: 0, kind: "text", tag: "div", role: "",
      text: "Status bar: " + main.join(", "), value: "", controlType: "",
      clickable: false, level: 0, live: "", disabled: false, options: [],
      _axis: 0, navRail: false
    };
  };

  // The shared detent selector (TO/GA…Reverse Full) is intentionally left alone:
  // it is shared and already reads with "(selected)". Gated on an "Axis N for
  // Throttle" heading so it only fires on this page (A320 + A380 both use it).
  A.relabelThrottleCalib = function (items) {
    var any = false;
    for (var z = 0; z < items.length; z++) if (items[z]._axis) { any = true; break; }
    if (!any) return items;   // not the throttle calibration page

    // Tag the per-axis range numbers low/high (within an axis, left number = low
    // bound). Grouping by the DOM-derived axis tag — never by x-proximity — is
    // what keeps a column's high value from leaking into the adjacent axis.
    var byAxis = {};
    for (var r = 0; r < items.length; r++) {
      var ri = items[r];
      if (ri._axis && ri.kind === "text" && /^-?\d\.\d{2}$/.test(ri.text || ""))
        (byAxis[ri._axis] = byAxis[ri._axis] || []).push(ri);
    }
    for (var key in byAxis) {
      if (!byAxis.hasOwnProperty(key)) continue;
      var grp = byAxis[key].sort(function (x, y) { return x.left - y.left; });
      for (var g = 0; g < grp.length; g++) grp[g]._rangeRole = (g === 0 ? "low" : "high");
    }

    for (var j = 0; j < items.length; j++) {
      var it = items[j], t = it.text || "";
      if (!it._axis) continue;   // shared detent selector / header / footer — left alone
      if (it._rangeRole) { it.text = "Axis " + it._axis + " detent " + it._rangeRole + " " + t; continue; }
      if (t === "Set from Throttle" || t === "Deadband +/-" || t.indexOf("Current Value") === 0)
        it.text = "Axis " + it._axis + " " + t;
    }
    return items;
  };

  // Throttle-calibration page READ ORDER. The page is genuinely THREE columns —
  // Axis 1 panel │ shared detent selector │ Axis 2 panel (BaseThrottleConfig ×2
  // around the VerticalSelectGroup, per FBW ThrottleConfig.tsx) — plus a
  // full-width header bar (status bar + TOGA / Reverser / Independent-Axes
  // toggles + the axis-count selector) above and a full-width action bar (Back /
  // Reset / Load / Apply / Save) below. The generic two-column splitter can't
  // model three columns, so it zippers the two axes' values together ("Axis 1
  // current, Axis 2 current, Axis 1 low, Axis 2 low, …"). This pass assigns each
  // item a band and sorts (navRail-last, band, top, left) so each block reads
  // intact in logical order: header → Axis 1 → detents → Axis 2 → warnings →
  // action bar → nav rail. Returns true when it handled (and re-sorted) the page.
  // Generalises to the 1/2/4-axis variants: axis columns are ranked left→right,
  // and the shared detent selector reads right after the axis column to its left.
  A.orderThrottleCalib = function (items) {
    // Axis headings carry the DOM-derived _axis tag + their left position. Order
    // the axis columns left→right and map each axis number to a band.
    var headings = [];
    for (var i = 0; i < items.length; i++) {
      if (items[i].kind === "heading" && items[i]._axis &&
          /^Axis\s+\d+\s+for\s+Throttle/i.test(items[i].text || "")) headings.push(items[i]);
    }
    if (!headings.length) return false;             // not the throttle calibration page
    headings.sort(function (a, b) { return a.left - b.left; });   // left → right
    var bandOfAxis = {}, minHeadTop = 1e9;
    for (var h = 0; h < headings.length; h++) {
      bandOfAxis[headings[h]._axis] = (h + 1) * 100;
      if (headings[h].top < minHeadTop) minHeadTop = headings[h].top;
    }

    var detentNames = { "TO/GA": 1, "FLX": 1, "CLB": 1, "Idle": 1, "Reverse Idle": 1, "Reverse Full": 1 };
    var footer = { "Back": 1, "Reset to Defaults": 1, "Load from File": 1, "Apply": 1, "Save and Apply": 1 };
    function bare(t) { return (t || "").replace(/\s+\(selected\)$/, ""); }
    function detentBand(left) {                      // shared selector reads after the axis column left of it
      var b = 50;
      for (var k = 0; k < headings.length; k++) if (headings[k].left < left) b = (k + 1) * 100 + 50;
      return b;
    }

    for (var j = 0; j < items.length; j++) {
      var it = items[j], t = it.text || "";
      if (it.navRail) { it._band = 1e7; continue; }
      if (it.top < minHeadTop - 6) { it._band = 0; continue; }        // status bar + header globals
      if (footer[bare(t)]) { it._band = 1e6; continue; }              // bottom action bar
      if (detentNames[bare(t)]) { it._band = detentBand(it.left); continue; }  // shared detent selector
      // Group BOTH "Set from Throttle" buttons together (after the axis value
      // blocks, before the warning/action bar) so they read consecutively — they
      // are otherwise split across the two axis columns. The relabel pass has
      // already prefixed them "Axis N Set from Throttle", so match on the suffix.
      if (bare(t).indexOf("Set from Throttle") >= 0) { it._band = 850; continue; }
      if (it._axis && bandOfAxis[it._axis]) { it._band = bandOfAxis[it._axis]; continue; }  // axis column
      it._band = 900;                                                 // validation/config note (full-width)
    }

    // Tie-break within a band by the contiguous ROW index the enumerate pass
    // already assigned (tolerance-based, so items a few px apart in top — the
    // low/high range numbers, and the header toggle row vs the axis-count
    // selector 8 px above it — share a row) then left, so each visual row reads
    // left-to-right rather than splitting on a raw-top boundary.
    items.sort(function (a, b) {
      return ((a.navRail ? 1 : 0) - (b.navRail ? 1 : 0)) ||
             ((a._band || 0) - (b._band || 0)) ||
             ((a._row || 0) - (b._row || 0)) || (a.left - b.left);
    });
    return true;
  };

  // Ground page READ ORDER. The Ground/Services page is a two-column service-tile
  // grid with the Services/Fuel/Payload/Pushback sub-tab strip in the upper-right.
  // The generic two-column sort reads the left column (some service tiles) fully,
  // then the right column — which starts with the status bar + the sub-tab strip,
  // THEN the rest of the service tiles — so the tab strip lands in the MIDDLE of
  // the service list. This pass bands the page so it reads: top status bar →
  // sub-tab strip (grouped) → all service tiles (grouped: left column then right) →
  // nav rail. Gated on the presence of "/ground/" sub-tab links, so every other
  // page is untouched. Returns true when it handled (and re-sorted) the page.
  A.orderGroundServices = function (items) {
    var hasSub = false;
    for (var i = 0; i < items.length; i++) { if (items[i]._groundSub) { hasSub = true; break; } }
    if (!hasSub) return false;                       // not the Ground page

    for (var j = 0; j < items.length; j++) {
      var it = items[j];
      if (it.navRail) { it._gband = 1e7; continue; }
      if (it._groundSub) { it._gband = 100; continue; }   // sub-tab strip, grouped
      if (it.top < 50) { it._gband = 0; continue; }        // global EFB status bar (top)
      // Group the door tiles together (user request) right after the sub-tab strip,
      // ahead of the other services. Include heading-doors: IN FLIGHT the door tiles
      // are disabled and surface as headings (not buttons), so a button-only gate left
      // them ungrouped, scattered among the other service rows.
      if ((it.kind === "button" || it.kind === "heading") && /\bdoor\b/i.test(it.text || "")) { it._gband = 150; continue; }
      it._gband = 200;                                     // all other service tiles + chocks/cones
    }
    // Within the service band keep the existing column→row order so the left
    // column's tiles read fully, then the right column's — contiguous, no strip.
    items.sort(function (a, b) {
      return ((a.navRail ? 1 : 0) - (b.navRail ? 1 : 0)) ||
             ((a._gband || 0) - (b._gband || 0)) ||
             ((a._col || 0) - (b._col || 0)) ||
             ((a._row || 0) - (b._row || 0)) || (a.left - b.left);
    });
    return true;
  };

  // Pin the top status bar to the front on EVERY page: the status line first, then
  // the Quick Controls gear — so the gear always reads right under the status line
  // instead of landing mid-page on a two-column layout (Dashboard). When the pane is
  // OPEN, also pull its controls (an absolute overlay the geometric sort otherwise
  // scatters through the page — the user's "weird spot") into one block right after
  // the gear. Uses a splice-to-front (NOT a re-sort) so the existing order of all
  // other content — set by orderThrottleCalib / orderGroundServices / the column
  // sort, and the nav-rail-last rule — is preserved exactly. Runs LAST.
  A.orderQuickControls = function (items) {
    var paneOpen = false;
    for (var i = 0; i < items.length; i++) { if (items[i]._qc) { paneOpen = true; break; } }
    var statusLine = [], gear = [], pane = [], rest = [];
    for (var j = 0; j < items.length; j++) {
      var it = items[j];
      if (/^Status bar:/.test(it.text || "")) statusLine.push(it);
      else if (it.text === "Quick Controls") gear.push(it);
      else if (paneOpen && it._qc) pane.push(it);
      else rest.push(it);
    }
    if (!gear.length && !statusLine.length) return false;
    // Read the pane controls in visual order (row then left); leave `rest` untouched.
    pane.sort(function (a, b) { return ((a._row || 0) - (b._row || 0)) || (a.left - b.left); });
    var out = statusLine.concat(gear).concat(pane).concat(rest);
    for (var k = 0; k < out.length; k++) items[k] = out[k];
    return true;
  };

  // Builds the flat, screen-reader-friendly element list for the current
  // flyPad page: every visible interactive control PLUS visible headings/text,
  // one per line, in reading order, de-duplicated. Interactive/clickable items
  // get a stable index stamped on the node (data-fbw-efb-idx) so clicks and
  // value sets can find them again.
  // True on the EFB Dashboard ("Your Flight" + "Important Information" two-column
  // layout). Detected by the FlightWidget's heading so it survives the user
  // reordering the right-column reminder sections. Used to switch enumerate() to a
  // column-first read order (left widget fully, then right widget).
  A.isDashboard = function (root) {
    try {
      var hs = root.getElementsByTagName("h1");
      for (var i = 0; i < hs.length; i++) {
        var t = clean(hs[i].textContent);
        if (t && t.indexOf("Your Flight") >= 0) return true;
      }
    } catch (e) {}
    return false;
  };

  // General two-column detector. Many flyPad pages are a LEFT control column beside
  // a RIGHT control column (the Checklist phase-menu beside its item list; a
  // two-column input form like Performance). A plain row-by-row, left-to-right read
  // ZIPPERS them ("menu1, item1, menu2, item2, …") — the reported "checklist items
  // interspersed between checklists". If the interactive items (excluding the nav
  // rail) split across ONE clear horizontal gap into a left group and a right group,
  // each substantial, return the x boundary so enumerate() reads each column fully
  // top-to-bottom. A single-column page (or a label+control settings page, whose
  // left side is non-interactive TEXT and so isn't counted here) has no such gap and
  // keeps the row-wise order. NOTE: position-based, NOT a fixed fraction of width —
  // both checklist columns sit in the left half, so a midline split misses them.
  A.splitColumns = function (items) {
    var pts = [];
    for (var i = 0; i < items.length; i++)
      if (items[i].idx > 0 && !items[i].navRail) pts.push(items[i]);
    if (pts.length < 6) return -1;
    var xs = pts.map(function (p) { return p.left; }).sort(function (a, b) { return a - b; });
    var gi = -1, gv = 0;
    for (var k = 1; k < xs.length; k++) { var g = xs[k] - xs[k - 1]; if (g > gv) { gv = g; gi = k; } }
    if (gv < 150) return -1;                       // no clear column gutter
    var boundary = (xs[gi - 1] + xs[gi]) / 2;
    // Both columns must be substantial AND vertically OVERLAP. The overlap test is
    // what separates a true side-by-side layout (checklist menu beside its items —
    // each menu row sits at the same height as an item row, so a row-wise read
    // zippers them) from a STACKED layout (Ground's sub-tab strip ABOVE its payload
    // form — row-wise already reads the tabs row, then the form rows, correctly).
    // Only the side-by-side case needs column-first; reordering a stacked page would
    // wrongly bury its top toolbar at the end.
    var lMin = 1e9, lMax = -1e9, rMin = 1e9, rMax = -1e9, nl = 0, nr = 0;
    for (var j = 0; j < pts.length; j++) {
      if (pts[j].left < boundary) { nl++; if (pts[j].top < lMin) lMin = pts[j].top; if (pts[j].top > lMax) lMax = pts[j].top; }
      else { nr++; if (pts[j].top < rMin) rMin = pts[j].top; if (pts[j].top > rMax) rMax = pts[j].top; }
    }
    if (nl < 3 || nr < 3) return -1;
    var overlap = Math.min(lMax, rMax) - Math.max(lMin, rMin);
    var minRange = Math.min(lMax - lMin, rMax - rMin);
    if (overlap < 0.3 * minRange) return -1;       // stacked, not side-by-side → row-wise
    return boundary;
  };

  // Locate the page content section ("h-content-section..."), the common root of
  // the Payload/Fuel form area.
  A.contentSection = function (root) {
    try {
      var s = root.querySelectorAll('[class*="h-content-section"]');
      if (s && s.length) return s[0];
    } catch (e) {}
    return null;
  };

  // Build clean Payload-page content lines, or null when not the Payload page.
  // Re-surfaces the real <input> nodes (stamps idx); folds each row's current +
  // max into the field label; emits ZFWCG as a read-only line; suppresses the
  // seat map / cargo bars / CG chart and the table's split-span cells. Builder
  // items carry EMIT-ORDER synthetic coordinates (top 200+, left 0) so the unified
  // sort keeps them in order, after the sub-tab strip and before the nav rail.
  A.buildPayloadLines = function (root, idxRef) {
    if (!A.activeGroundSub(root, "payload")) return null;
    var section = A.contentSection(root);
    if (!section) return null;
    var table = section.querySelector("table");
    if (!table) return null;

    // Ownership: suppress every non-actionable top-level child (seat map, cargo
    // bars), the table itself (re-emitted below), and — inside the actionable
    // column that holds the table — the CG chart card (the direct child of that
    // column that contains a <canvas>). The boarding panel (no canvas) stays
    // unowned so the generic pass surfaces its buttons + rate selector.
    var actionable = null, kids = section.children;
    for (var c = 0; c < kids.length; c++) {
      if (kids[c].contains(table)) actionable = kids[c];
      else A.markOwned(kids[c]);
    }
    A.markOwned(table);
    if (actionable) {
      var canv = actionable.getElementsByTagName("canvas");
      for (var v = 0; v < canv.length; v++) {
        var p = canv[v];
        while (p && p.parentElement !== actionable) p = p.parentElement;
        if (p && !p.contains(table)) A.markOwned(p);
      }
    }
    // Every visible label/limit/caption on this page is a pointer-events-none
    // ABSOLUTE z-50 hover tooltip ("Per Passenger Weight", "Maximum ZFW …", "Begin
    // Boarding"). We read the maxes from them (findMaxFor) and fold labels into
    // fields, so they must not leak as their own lines — suppress them. Gate on
    // "absolute": FBW's DISABLED buttons are ALSO pointer-events-none (opacity-20
    // pointer-events-none) but are in-flow, NOT absolute — owning those would drop
    // a disabled Start-deboarding / SimBrief button.
    var tips = section.querySelectorAll('[class*="pointer-events-none"]');
    var weightTips = [];
    for (var tp = 0; tp < tips.length; tp++) {
      if (A.cls(tips[tp]).indexOf("absolute") < 0) continue;   // not a tooltip overlay
      A.markOwned(tips[tp]);
      var tx = clean(tips[tp].textContent);
      if (/weight/i.test(tx) && !/max/i.test(tx)) weightTips.push(tx);
    }
    A.suppressSimbrief(section);

    var out = [];
    function emit(o) { o.top = 200 + out.length * 30; o.left = 0; o._axis = 0; o.navRail = false; out.push(o); }
    function field(node, label) {
      var ix = idxRef.v++; node.setAttribute("data-fbw-efb-idx", String(ix));
      emit({ idx: ix, kind: "input", tag: "input", role: "", text: label,
             value: A.valueOf("input", node), controlType: "text", clickable: false,
             level: 0, live: "", disabled: A.disabledFor(node), options: [] });
    }
    function textline(s) {
      emit({ idx: 0, kind: "text", tag: "div", role: "", text: s, value: "",
             controlType: "", clickable: false, level: 0, live: "", disabled: false, options: [] });
    }

    var rows = table.querySelectorAll("tr");
    for (var i = 0; i < rows.length; i++) {
      var cells = rows[i].children;
      if (cells.length < 3) continue;
      var label = clean(cells[0].textContent);
      if (!label) continue;                              // header row (blank label cell)
      var input = cells[1].querySelector("input");
      var current = A.joinValueCell(cells[2]);
      var max = A.findMaxFor(root, label);
      if (input) {
        var ctx = [];
        if (current) ctx.push("current " + current);
        if (max) ctx.push("max " + max);
        field(input, label + (ctx.length ? ", " + ctx.join(", ") : ""));
      } else {
        var planned = A.joinValueCell(cells[1]);
        var s = label + ": planned " + (planned || "n/a") + ", current " + (current || "n/a");
        if (max) s += " (max " + max + ")";
        textline(s);
      }
    }

    // Per-passenger / per-bag weight inputs (outside the table, in the boarding
    // panel). Their labels are weight tooltips; match each input to its nearest
    // one, own the bare unit sibling ("LBS"), and emit with NO default-value hint.
    // The only non-table inputs on Payload are the weight fields; pair them to the
    // weight tooltips by DOM order (both render in source order: pax then bag).
    var inputs = root.getElementsByTagName("input"), wi = 0;
    for (var k = 0; k < inputs.length; k++) {
      var n = inputs[k];
      if (n.getAttribute("data-fbw-efb-idx")) continue;   // table inputs, already taken
      if (A.insideGroundRegion(n)) continue;
      if (!A.isVisible(n)) continue;
      var nm = weightTips[wi] || A.fieldName(n); wi++;
      if (!nm) continue;
      var unit = "", sibs = n.parentElement ? n.parentElement.children : [];
      for (var sx = 0; sx < sibs.length; sx++) {
        if (sibs[sx] === n) continue;
        var st = clean(sibs[sx].textContent);
        if (A.unitToken(st)) { if (!unit) unit = st; A.markOwned(sibs[sx]); }
      }
      field(n, unit ? (nm + " (" + unit + ")") : nm);
    }

    // Boarding time: FBW renders the duration as a parenthetical CHILD of the label
    // (<div>Boarding Time<span>(11:00 minutes)</span></div>), so the generic pass
    // captures only "Boarding Time" (directText) and the parenthetical-skip rule
    // drops the time. Emit the FULL text (space before the paren) and own it.
    var btAll = root.getElementsByTagName("*");
    for (var bt = 0; bt < btAll.length; bt++) {
      var bn = btAll[bt];
      if (!/^boarding time/i.test(A.directText(bn))) continue;
      if (A.insideGroundRegion(bn)) continue;
      textline(clean(bn.textContent).replace(/\s*\(/, " ("));
      A.markOwned(bn);
      break;
    }
    return out;
  };

  // Build clean Fuel-page content lines, or null when not the Fuel page. Emits one
  // read-only line per tank ("Name: current / capacity unit"), then the fuel-target
  // pair (an accessible range slider + the real numeric input, BOTH carrying the
  // input's idx so either writes the sim), and leaves the Refuel status / Start /
  // rate selector to the generic pass. Owns each tank widget + the rc-slider.
  A.buildFuelLines = function (root, idxRef) {
    if (!A.activeGroundSub(root, "fuel")) return null;
    // Scope to ROOT, not the tank content-section: on the A380 the Total/Trim cards
    // AND the refuel input + rc-slider are absolutely-positioned OUTSIDE the tank
    // section, so a section-scoped scan misses them. Tank/value/input detection is
    // fuel-specific, so scanning root captures every layout without false matches.
    A.suppressSimbrief(root);

    var out = [];
    function emit(o) { o.top = 200 + out.length * 30; o.left = 0; o._axis = 0; o.navRail = false; out.push(o); }
    function textline(s) {
      emit({ idx: 0, kind: "text", tag: "div", role: "", text: s, value: "",
             controlType: "", clickable: false, level: 0, live: "", disabled: false, options: [] });
    }
    // A tank value display, in either layout: A320 "cur / cap UNIT" (single text
    // node) or A380 "0…0value UNIT" (ValueUnitDisplay with leading-zero pad spans,
    // no capacity). Returns { s: clean readout, cap } or null.
    function fuelVal(txt) {
      var a = /^([\d][\d,]*)\s*\/\s*([\d][\d,]*)\s*(LBS|KGS)$/i.exec(txt);
      if (a) return { s: a[1] + " / " + a[2] + " " + a[3].toUpperCase(), cap: parseInt(a[2].replace(/,/g, ""), 10) };
      var b = /^0*(\d[\d,]*)\s*(LBS|KGS)$/i.exec(txt);
      if (b) return { s: b[1] + " " + b[2].toUpperCase(), cap: 0 };
      return null;
    }
    function descHasFuel(el) {
      for (var c = 0; c < el.children.length; c++) {
        if (fuelVal(clean(el.children[c].textContent))) return true;
        if (descHasFuel(el.children[c])) return true;
      }
      return false;
    }
    // Tank NAME for a value display + own the name source so it doesn't leak: the
    // enclosing table row's first cell (A380 tables), else the nearest heading
    // (A320 TankReadoutWidget), else the nearest preceding alphabetic label.
    function fuelTankName(el) {
      var p = el, g = 0;
      while (p && p !== root && g < 8) {
        if (p.tagName && p.tagName.toLowerCase() === "tr") { A.markOwned(p); var fc = p.firstElementChild; return fc ? clean(fc.textContent) : "Tank"; }
        p = p.parentElement; g++;
      }
      var q = el, gg = 0;
      while (q && q !== root && gg < 8) {
        var prev = q.previousElementSibling;
        while (prev) {
          if (/^h[1-6]$/i.test(prev.tagName)) { var ht = clean(prev.textContent); if (ht) { A.markOwned(prev); return ht; } }
          var pt = clean(prev.textContent);
          if (pt && /[a-z]/i.test(pt) && !/(LBS|KGS|%)/i.test(pt) && !/^\d/.test(pt) && pt.length <= 20) { A.markOwned(prev); return pt; }
          prev = prev.previousElementSibling;
        }
        q = q.parentElement; gg++;
      }
      return "Tank";
    }

    // Tank readouts. Match on textContent (A380 splits the value into spans, so
    // directText is empty), take only the INNERMOST match (the value wrapper), and
    // dedup by text+POSITION (distinct tanks can share a value, e.g. Left & Right
    // Outer both "0/1528"; the outline+fill double-render repeats text at one spot).
    var all = root.getElementsByTagName("*"), capacity = 0, seen = {}, unit = "LBS";
    for (var j = 0; j < all.length; j++) {
      var txt = clean(all[j].textContent);
      var fv = fuelVal(txt);
      if (!fv) continue;
      if (descHasFuel(all[j])) continue;           // not the innermost value display
      var tr = all[j].getBoundingClientRect();
      var key = txt + "@" + Math.round(tr.top) + "," + Math.round(tr.left);
      if (seen[key]) continue; seen[key] = 1;
      if (/KGS/i.test(fv.s)) unit = "KGS";
      var name = fuelTankName(all[j]);
      textline(name + ": " + fv.s);
      if (/total/i.test(name) && fv.cap) capacity = fv.cap;
      A.markOwned(all[j]);                          // the value wrapper (pad/value/unit)
    }

    // Fuel-target numeric input: the SimpleInput under the "Refuel" heading (on the
    // A380 it's in root, not the tank section). Emit an accessible range slider + the
    // number field, both writing the input's idx.
    var inputs = root.getElementsByTagName("input"), fuelInput = null;
    for (var k = 0; k < inputs.length; k++) {
      if (A.isVisible(inputs[k]) && /refuel/i.test(A.nearestHeading(inputs[k]))) { fuelInput = inputs[k]; break; }
    }
    if (fuelInput) {
      // Slider max = total fuel capacity. The A320 Total tank carries it ("cur/cap");
      // the A380 tanks show current only, so fall back to the stock capacity SimVar
      // (gallons x weight-per-gallon = lbs), converted to the displayed unit.
      var cap = capacity;
      if (!cap) {
        try {
          var lbs = SimVar.GetSimVarValue("FUEL TOTAL CAPACITY", "gallons") * SimVar.GetSimVarValue("FUEL WEIGHT PER GALLON", "pounds");
          cap = Math.round(unit === "KGS" ? lbs * 0.45359237 : lbs);
        } catch (e) { cap = 0; }
      }
      if (!cap) cap = parseInt(clean(fuelInput.getAttribute("placeholder") || "0").replace(/,/g, ""), 10) || 0;
      var step = cap > 100000 ? 1000 : 100;
      var ix = idxRef.v++; fuelInput.setAttribute("data-fbw-efb-idx", String(ix));
      var val = A.valueOf("input", fuelInput);
      var dis = A.disabledFor(fuelInput);
      emit({ idx: ix, kind: "slider", tag: "input", role: "slider", text: "Fuel target (slider)",
             value: val, controlType: "range", clickable: false, level: 0, live: "", disabled: dis,
             options: [], min: 0, max: cap, step: step });
      emit({ idx: ix, kind: "input", tag: "input", role: "", text: "Fuel target (" + unit + ")",
             value: val, controlType: "text", clickable: false, level: 0, live: "", disabled: dis, options: [] });
      // Suppress the rc-slider widget (re-rendered as our accessible range above)
      // and the input's lone unit sibling ("LBS").
      var slider = root.querySelector('[class*="rc-slider"]');
      if (slider) A.markOwned(slider);
      var fsibs = fuelInput.parentElement ? fuelInput.parentElement.children : [];
      for (var fx = 0; fx < fsibs.length; fx++) {
        if (fsibs[fx] !== fuelInput && A.unitToken(clean(fsibs[fx].textContent))) A.markOwned(fsibs[fx]);
      }
    }

    // Suppress the redundant "Refuel Time" heading: it labels the Instant/Fast/Real
    // rate selector but reads as a second, oddly-placed time line (after "Start
    // refueling") when the actual estimated duration already shows above. The rate
    // options are self-evident on the Fuel page — matches the Payload boarding flow,
    // which has no separate rate heading.
    var rtAll = root.getElementsByTagName("*");
    for (var rt = 0; rt < rtAll.length; rt++) {
      if (/^h[1-6]$/i.test(rtAll[rt].tagName) && /^refuel time$/i.test(clean(rtAll[rt].textContent))) {
        A.markOwned(rtAll[rt]); break;
      }
    }
    return out;
  };

  // Navigraph / LIDO / FAA CHART LIST rows are clickable container <div>s (they carry a
  // React AND a DOM onClick) whose visible text lives in child spans (chart name + a small
  // index-code chip). The generic leaf-only pass therefore emitted the name/code as plain
  // TEXT and never marked the row clickable — a blind pilot could READ the chart names but
  // not SELECT one (the accessibility bug). This builder finds each chart row (a clickable
  // div containing a ".bg-theme-secondary" index-code chip), owns its subtree, and emits ONE
  // clickable line "Name, code" stamped for clickElement. Shared with the A32NX. (The chart
  // IMAGE itself stays inaccessible — that's the separate AI-reader idea.)
  A.buildChartLines = function (root, idxRef) {
    var onCharts = false, hs = root.querySelectorAll("h1,h2");
    for (var h = 0; h < hs.length; h++) {
      if (/navigation\s*&\s*charts/i.test(clean(hs[h].textContent))) { onCharts = true; break; }
    }
    if (!onCharts) return null;
    var divs = root.getElementsByTagName("div"), rows = [];
    for (var i = 0; i < divs.length; i++) {
      var d = divs[i];
      if (!A.isVisible(d)) continue;
      if (!(typeof d.onclick === "function" || A.hasReactClick(d))) continue;
      if (!d.querySelector(".bg-theme-secondary")) continue;   // the chart index-code chip
      rows.push(d);
    }
    if (!rows.length) return null;
    var out = [];
    for (var r = 0; r < rows.length; r++) {
      var row = rows[r], nested = false;
      for (var q = 0; q < rows.length; q++) { if (q !== r && rows[q].contains(row)) { nested = true; break; } }
      if (nested) continue;
      // Join the row's leaf text pieces (chart name + index-code chip) with ", " so the
      // name and code don't run together ("AIRPORT BRIEFING (GEN), 10-1P").
      var pieces = [], leaves = row.getElementsByTagName("*");
      for (var z = 0; z < leaves.length; z++) {
        if (leaves[z].children.length) continue;     // leaf only
        var tt = clean(leaves[z].textContent);
        if (tt && pieces.indexOf(tt) < 0) pieces.push(tt);
      }
      var label = pieces.length ? pieces.join(", ") : clean(row.textContent);
      if (!label) continue;
      A.markOwned(row);
      var ix = idxRef.v++; row.setAttribute("data-fbw-efb-idx", String(ix));
      out.push({ idx: ix, kind: "link", tag: "div", role: "", text: label, value: "",
                 top: 200 + out.length * 22, left: 0, _axis: 0, navRail: false,
                 controlType: "", clickable: true, level: 0, live: "", disabled: false, options: [] });
    }
    return out.length ? out : null;
  };

  // ---- Settings builder ---------------------------------------------------
  // The flyPad Settings detail pages (/settings/<category>) flow through the
  // generic pass, producing cluttered output (loose option buttons, mislabeled
  // inputs that borrow a neighbour's label, value folded into the label, and the
  // Audio page's rc-slider internals as phantom "slider" lines). buildSettingsLines
  // owns the settings content region and re-emits one clean labeled line per
  // control (name line + tight options), mirroring buildPayloadLines/buildFuelLines.

  // The settings rows wrapper (div.divide-y-2) of a /settings/<category> DETAIL
  // page, or null. Bails on the throttle Calibrate sub-page (it has "Axis N for
  // Throttle" headings and is owned by orderThrottleCalib).
  A.settingsContentRoot = function (root) {
    var hs = root.getElementsByTagName("h1");
    for (var h = 0; h < hs.length; h++) if (/Axis\s+\d+\s+for\s+Throttle/i.test(clean(hs[h].textContent))) return null;
    var divs = root.getElementsByTagName("div");
    for (var i = 0; i < divs.length; i++) {
      if (A.hasClassToken(divs[i], "divide-y-2") && A.isVisible(divs[i])) {
        var r = divs[i].getBoundingClientRect();
        if (r.left > 100) return divs[i];     // right of the category column (not the index list)
      }
    }
    return null;
  };

  // The content-area back affordance: an <a href="/settings"> that is NOT the
  // far-left nav-rail link (left > 100).
  A.settingsBackLink = function (root) {
    var links = root.querySelectorAll('a[href="/settings"]');
    for (var i = 0; i < links.length; i++) {
      if (!A.isVisible(links[i])) continue;
      var r = links[i].getBoundingClientRect();
      if (r.left > 100) return links[i];
    }
    return null;
  };

  A.buildSettingsLines = function (root, idxRef) {
    var rowsWrap = A.settingsContentRoot(root);
    if (!rowsWrap) return null;
    // Only OWN the region when it actually contains setting CONTROLS. An
    // informational sub-page (e.g. About) uses the same divide-y-2 wrapper but holds
    // only headings / value text; likewise a future or A320 layout whose rows we
    // don't recognise would, if owned, be re-emitted BLANK. In both cases return null
    // so the generic pass still surfaces the content (graceful degrade, never worse).
    var units = A.settingUnits(rowsWrap);
    var hasControl = false;
    for (var u = 0; u < units.length; u++) { if (A.settingUnitHasControl(units[u].ctrl)) { hasControl = true; break; } }
    if (!hasControl) return null;

    var out = [];
    function emit(o) { o.top = 200 + out.length * 30; o.left = 0; o._axis = 0; o.navRail = false; out.push(o); }

    var back = A.settingsBackLink(root);
    if (back) {
      var bx = idxRef.v++; back.setAttribute("data-fbw-efb-idx", String(bx)); A.markOwned(back);
      emit({ idx: bx, kind: "link", tag: "a", role: "", text: "Back to Settings", value: "",
             controlType: "", clickable: true, level: 0, live: "", disabled: false, options: [] });
    }

    A.emitSettingsRows(rowsWrap, idxRef, emit, units);
    A.markOwned(rowsWrap);
    return out;
  };

  // True when a SettingItem control cell holds an actionable control (segmented
  // group, toggle, input, or an action/sub-page link). Read-only — stamps nothing —
  // so the OWN/skip decision can be made before any emission.
  A.settingUnitHasControl = function (ctrl) {
    if (!ctrl) return false;
    var opts = A.selectGroupOptions(ctrl);
    if (opts && opts.length >= 2) return true;
    if (A.toggleNode(ctrl)) return true;
    if (ctrl.querySelector("input, a, button")) return true;
    var divs = ctrl.getElementsByTagName("div");
    for (var i = 0; i < divs.length; i++) if (A.hasClassToken(divs[i], "cursor-pointer") && clean(divs[i].textContent)) return true;
    return false;
  };

  // Emit one accessible numeric/text input line labeled `label`, stamping the click
  // idx. Shared by the SimpleInput and Audio-volume paths.
  A.emitSettingInput = function (input, label, idxRef, emit) {
    var ix = idxRef.v++; input.setAttribute("data-fbw-efb-idx", String(ix));
    emit({ idx: ix, kind: "input", tag: "input", role: "", text: label,
           value: A.valueOf("input", input), controlType: "text", clickable: false,
           level: 0, live: "", disabled: A.disabledFor(input), options: [] });
  };

  A.textItem = function (s) {
    return { idx: 0, kind: "text", tag: "div", role: "", text: s, value: "",
             controlType: "", clickable: false, level: 0, live: "", disabled: false, options: [] };
  };

  // Discover the SettingItem units under the rows wrapper. Each setting renders as
  // a flex-row whose FIRST child is a plain text label cell and whose SECOND child
  // is the control wrapper. Setting rows can be wrapped in div.py-4 containers
  // (sometimes COMPOUND — two settings in one py-4, e.g. Auto Cabin Lighting toggle
  // + Cabin Lighting Brightness slider), so we scan ALL flex-rows rather than the
  // wrapper's direct children. A selectgroup's own inner flex-row (option spans) is
  // excluded because its first child is a cursor-pointer option, not a text label;
  // a slider/value row is excluded because its first child has no text.
  A.settingUnits = function (rowsWrap) {
    var out = [], all = rowsWrap.getElementsByTagName("*");
    for (var i = 0; i < all.length; i++) {
      var el = all[i];
      if (!A.isVisible(el)) continue;
      if (!A.hasClassToken(el, "flex-row")) continue;   // standalone token, not a substring of e.g. flex-row-reverse
      var label = el.firstElementChild;
      if (!label) continue;
      if (A.hasClassToken(label, "cursor-pointer")) continue;   // option row, not a setting row
      var lt = clean(label.textContent);
      if (!lt || lt.length > 120) continue;
      var ctrl = label.nextElementSibling;
      if (!ctrl) continue;
      out.push({ name: lt, ctrl: ctrl, row: el });
    }
    return out;
  };

  // Emit one clean line (or name + tight options) per SettingItem unit. The control
  // cell holds a SelectGroup (span.cursor-pointer options), a Toggle div, an input,
  // an <a> link, or an rc-slider+input (Audio); inputs/links/Audio go through
  // emitSettingsControl.
  A.emitSettingsRows = function (rowsWrap, idxRef, emit, units) {
    units = units || A.settingUnits(rowsWrap);
    for (var i = 0; i < units.length; i++) {
      var row = units[i].row, name = units[i].name, ctrl = units[i].ctrl;

      // SelectGroup: control cell holds >=2 span.cursor-pointer option leaves.
      var opts = A.selectGroupOptions(ctrl);
      if (opts && opts.length >= 2) {
        if (name) emit(A.textItem(name));
        for (var o = 0; o < opts.length; o++) {
          var op = opts[o];
          var ix = idxRef.v++; op.setAttribute("data-fbw-efb-idx", String(ix));
          var sel = A.isSelectedSegment(op);   // selected = bg-theme-highlight + unselected peers
          emit({ idx: ix, kind: "button", tag: op.tagName.toLowerCase(), role: "",
                 text: clean(op.textContent) + (sel ? " (selected)" : ""), value: "",
                 controlType: "", clickable: true, level: 0, live: "", disabled: A.disabledFor(op), options: [] });
        }
        continue;
      }

      // Toggle: a div with the rounded-full / h-8 knob shape.
      var tog = A.toggleNode(ctrl);
      if (tog) {
        var tx = idxRef.v++; tog.setAttribute("data-fbw-efb-idx", String(tx));
        emit({ idx: tx, kind: "toggle", tag: "div", role: "switch", text: name || A.toggleLabel(tog),
               value: A.valueOf("toggle", tog), controlType: "checkbox", clickable: false,
               level: 0, live: "", disabled: A.disabledFor(tog), options: [] });
        continue;
      }

      // Input / link / Audio — emitSettingsControl owns the name line for these.
      A.emitSettingsControl(row, name, ctrl, idxRef, emit);
    }
  };

  // SelectGroup option leaves: cursor-pointer spans with their own short text in
  // the control cell. Returns [] / null when not a segmented control.
  A.selectGroupOptions = function (ctrl) {
    if (!ctrl) return null;
    var spans = ctrl.getElementsByTagName("span"), out = [];
    for (var i = 0; i < spans.length; i++) {
      if (!A.isVisible(spans[i])) continue;
      if (!A.hasClassToken(spans[i], "cursor-pointer")) continue;
      if (!clean(spans[i].textContent)) continue;
      out.push(spans[i]);
    }
    return out;
  };

  // The Toggle node inside a control cell — the FBW Toggle pill (its firstElementChild
  // is the knob, so A.valueOf("toggle") reads translate-x*). Reuse the canonical
  // classify() toggle detection rather than re-deriving the Tailwind shape, so the
  // two never drift.
  A.toggleNode = function (ctrl) {
    if (!ctrl) return null;
    var all = ctrl.getElementsByTagName("*");
    for (var i = 0; i < all.length; i++) {
      if (A.classify(all[i]) === "toggle") return all[i];
    }
    return null;
  };

  // Emit the non-toggle, non-segmented control of a SettingItem row, labeled from
  // the row's OWN name (fixes the borrowed-label + value-in-label bugs). Handles
  // SimpleInput; Audio (Task 7) and sub-page/action links (Task 6) are tried first.
  A.emitSettingsControl = function (row, name, ctrl, idxRef, emit) {
    if (!ctrl) { if (name) emit(A.textItem(name)); return false; }

    if (A.emitAudioRow(row, name, ctrl, idxRef, emit)) return true;   // Task 7

    var input = ctrl.querySelector("input");
    if (input && A.isVisible(input)) {
      A.emitSettingInput(input, name || A.fieldName(input) || "Value", idxRef, emit);
      return true;
    }

    if (A.emitSettingsLink(row, name, ctrl, idxRef, emit)) return true;   // Task 6

    if (name) emit(A.textItem(name));
    return false;
  };

  // Audio volume SettingItem: the control area holds an rc-slider (phantom for a
  // screen reader) plus the real number <input>. Own the rc-slider explicitly and
  // emit one labeled numeric control from the row name. Returns true when handled.
  // (buildSettingsLines also owns the whole rows wrapper, so the phantom slider
  // lines are doubly suppressed — this keeps the Audio intent explicit and scans the
  // entire ROW for the input in case the slider and input sit in separate cells.)
  A.emitAudioRow = function (row, name, ctrl, idxRef, emit) {
    var slider = row.querySelector('[class*="rc-slider"]');
    if (!slider) return false;                       // not an audio/slider row
    A.markOwned(slider);
    var input = row.querySelector("input");
    if (input && A.isVisible(input)) {
      A.emitSettingInput(input, name || A.fieldName(input) || "Volume", idxRef, emit);
    } else if (name) {
      emit(A.textItem(name));
    }
    return true;
  };

  // A SettingItem whose control opens a sub-page or performs an action ("Select",
  // "Calibrate", "Unlink Account"). Label it "<Row name>: <action>" so the bare verb
  // gains context. Returns true when it emitted.
  A.emitSettingsLink = function (row, name, ctrl, idxRef, emit) {
    if (!ctrl) return false;
    var link = ctrl.querySelector("a, button");
    if (!link) {
      // Fallback: a div styled as a button. Require CLEAN own text (short, no
      // lowercase->Uppercase merge) so a wrapper that concatenates sibling text
      // ("SelectEnglish (US)") isn't picked as the verb / click target.
      var divs = ctrl.getElementsByTagName("div");
      for (var i = 0; i < divs.length; i++) {
        if (!A.hasClassToken(divs[i], "cursor-pointer")) continue;
        var dt = clean(divs[i].textContent);
        if (dt && dt.length <= 30 && !/[a-z][A-Z]/.test(dt)) { link = divs[i]; break; }
      }
    }
    if (!link || !A.isVisible(link)) return false;
    var verb = clean(link.textContent);
    if (!verb) return false;
    var label = name ? (name + ": " + verb) : verb;
    var ix = idxRef.v++; link.setAttribute("data-fbw-efb-idx", String(ix));
    emit({ idx: ix, kind: "link", tag: link.tagName.toLowerCase(), role: "", text: label, value: "",
           controlType: "", clickable: true, level: 0, live: "", disabled: A.disabledFor(link), options: [] });
    return true;
  };

  A.enumerate = function (root) {
    var stale = root.querySelectorAll("[data-fbw-efb-idx]");
    for (var s = 0; s < stale.length; s++) stale[s].removeAttribute("data-fbw-efb-idx");
    var staleR = root.querySelectorAll("[data-fbw-ground-region]");
    for (var sr = 0; sr < staleR.length; sr++) staleR[sr].removeAttribute("data-fbw-ground-region");

    var rootRect = root.getBoundingClientRect();
    var all = root.getElementsByTagName("*");
    var items = [];
    var idx = 1;

    // Throttle-calibration: precompute each axis PANEL (the BaseThrottleConfig
    // root = the "Axis N for Throttle …" heading's parent element). Every value /
    // control is then tied to its axis by DOM containment, which is robust where
    // x-position fails — the 4-axis layout packs columns so tightly that an
    // axis's HIGH-bound value sits closer to the NEXT axis's heading and would be
    // mis-assigned. The shared detent selector is a SIBLING of the panels (not
    // inside any), so it correctly tags as axis 0. Empty on non-throttle pages.
    var axisPanels = [];
    for (var ap = 0; ap < all.length; ap++) {
      var an = all[ap];
      if (!/^h[1-6]$/i.test(an.tagName)) continue;
      var am = /^Axis\s+(\d+)\s+for\s+Throttle/i.exec(clean(an.textContent));
      if (am && an.parentElement) axisPanels.push({ num: parseInt(am[1], 10), panel: an.parentElement });
    }
    function axisOfNode(n) {
      for (var k = 0; k < axisPanels.length; k++) {
        try { if (axisPanels[k].panel.contains(n)) return axisPanels[k].num; } catch (e) {}
      }
      return 0;
    }

    // Dedicated Ground-form builders (Payload/Fuel) own + replace their content
    // region with clean lines. They stamp idx on the real inputs and mark the
    // suppressed/replaced subtrees data-fbw-ground-region; the generic passes
    // below skip those subtrees but still surface the nav rail, sub-tabs, and the
    // boarding/refuel buttons (which sit OUTSIDE the owned subtrees).
    var idxRef = { v: idx };
    var groundItems = A.buildPayloadLines(root, idxRef) || A.buildFuelLines(root, idxRef) || A.buildChartLines(root, idxRef) || A.buildSettingsLines(root, idxRef);
    if (groundItems) { for (var gi = 0; gi < groundItems.length; gi++) items.push(groundItems[gi]); idx = idxRef.v; }

    // Collapse the top status bar into one line (the gear is surfaced separately
    // as a button in pass 1; the bar's per-leaf text is suppressed in pass 2).
    var statusLine = A.buildStatusBarLine(root);
    if (statusLine) items.push(statusLine);

    // Drop the A380 cabin-lighting widget (can't be set via the agent; now an app-side
    // Interior Lighting panel control). No-op on the A320 / when the pane is closed.
    A.suppressCabinLighting(root);

    for (var i = 0; i < all.length && idx <= 400; i++) {
      var n = all[i];
      if (!A.isVisible(n)) continue;
      if (A.insideGroundRegion(n)) continue;
      var kind = A.classify(n);
      if (!kind) continue;

      // User-requested: hide the Fuel page refuel target SLIDER (rc-slider bar, which
      // reads as an empty field for a screen reader) and the numeric edit box (it is
      // redundant with the Instant/Fast/Real + Start refueling controls). Scoped to
      // the refuel context via the section heading so Payload inputs (Passengers /
      // Cargo / ZFW) and any sliders on other pages are untouched.
      if (kind === "slider" && /refuel/i.test(A.nearestHeading(n))) continue;
      // Quick Controls brightness rc-slider(s): screen brightness has no value for a
      // blind pilot and the rc-slider's internal inputs read as empty "slider" lines.
      // The Auto-Brightness toggle remains. Scoped to the status-bar/pane subtree.
      if (kind === "slider" && A.insideStatusBar(n)) continue;

      var interactive = kind !== "heading";

      // For interactive controls, skip wrappers that contain a more specific
      // control (we want the innermost actionable node, one per line).
      if (interactive && A.containsInteractive(n)) continue;
      if (A.isInsideStamped(n)) continue;

      var text = A.labelFor(n);
      var ctype = A.controlType(kind, n);
      var clickable = A.isClickable(kind);

      var r = n.getBoundingClientRect();
      var topRel = r.top - rootRect.top, leftRel = r.left - rootRect.left;
      // The left nav rail (page links in the far-left column) is grouped at the
      // END so it stops interleaving with the page content row-by-row.
      var navRail = (kind === "link" && leftRel < 100 && topRel > 40);
      // Ground page sub-tab (Services/Fuel/Payload/Pushback): a link whose href is
      // "/ground/<x>". Tagged so orderGroundServices can group the tab strip apart
      // from the service tiles (the two-column read order otherwise drops the strip
      // between the left- and right-column service items).
      var hrefAttr = (n.getAttribute && n.getAttribute("href")) || "";
      var groundSub = (kind === "link" && /^\/ground\//.test(hrefAttr));

      // Mark the selected option of a segmented setting control (e.g. the chosen
      // "Instant / Fast / Real", throttle axis, or calibration detent). Only a
      // genuine choice control gets the marker — a primary action button shares
      // the same bg-theme-highlight background, so isSelectedSegment additionally
      // requires an unselected (bg-theme-accent) sibling to disambiguate.
      if (text && A.isSelectedSegment(n)) text = text + " (selected)";

      // Distinguish the duplicated passenger door tiles: FBW labels several distinct
      // doors "Door Fwd"/"Door Aft". Prefer the PRECISE identity parsed from the tile's
      // click handler (Door Forward Left / Door Main 2 Left / Door Upper 1 Left / Door
      // Aft Right) — guarded so it can only return a known door or nothing. If the
      // handler isn't parseable (minified build), fall back to the robust column-based
      // side: the screen LEFT column is the aircraft's LEFT side (the xr-anchored
      // wrapper holds CabinLeftDoor / Main1Left = interactive point 0). Cargo Door is
      // unique — left alone. Done BEFORE the (active)/(called) suffix so the focus-
      // stable key strip (which trims the trailing state marker) still works.
      // Precise/disambiguated door NAME. doorIdentity reads the door enum from the
      // tile's click handler — now including the React fiber, so it resolves even in
      // flight when the tile is a disabled heading. Fall back to the left/right column
      // guess ONLY for an operable button (columns are meaningful there); for a
      // disabled heading with no resolvable identity keep FBW's generic label rather
      // than invent a possibly-wrong side.
      if (text && /\bdoor\b/i.test(text) && !/cargo/i.test(text) && (kind === "button" || kind === "heading")) {
        var did = A.doorIdentity(n);
        if (did) text = did;
        else if (kind === "button") text = text + (leftRel < rootRect.width * 0.5 ? " Left" : " Right");
      }

      // Door tiles read OPEN / CLOSED (from the tile colour) instead of the generic
      // active / [silent-when-closed] — whether the tile is an operable button (on the
      // ground) or a disabled heading (in flight). "in transit" covers the amber
      // mid-move. Other ground-service tiles keep the connected/called state.
      if (text && /\bdoor\b/i.test(text) && (kind === "button" || kind === "heading")) {
        text = text + " (" + A.doorOpenState(n) + ")";
      } else if (text && kind === "button") {
        var svc = A.serviceState(n);
        if (svc) text = text + " (" + svc + ")";
      }

      // Read-only ground status indicator (Wheel Chocks / Safety Cones): the name
      // is a heading whose wrapper colour encodes placed/not-placed. Append it so
      // the reader hears "Wheel Chocks: placed" instead of a bare name.
      if (text && kind === "heading") {
        var gstat = A.groundStatusTile(n);
        if (gstat) text = text + ": " + gstat;
      }

      // Drop empty, non-actionable noise. Inputs/checkboxes keep their slot
      // (their value carries meaning even with no label).
      if (!text && ctype !== "text" && ctype !== "select" && ctype !== "checkbox") continue;

      var thisIdx = 0;
      if (interactive) { thisIdx = idx; n.setAttribute("data-fbw-efb-idx", String(idx)); idx++; }

      // Active tab/nav indicator: mark the selected nav-rail page and the selected
      // Ground sub-tab so the reader knows where it is. Nav-rail links read
      // "current page"; in-content sub-tabs read "selected".
      // Settings-index category links all carry the standalone bg-theme-accent token
      // as their BASE card background (not an active-tab marker), so isActiveTab
      // over-reports every category as "(selected)". The index does not highlight an
      // active category — don't suffix them; the page label names the active one.
      var isSettingsCat = (kind === "link" && /^\/settings\//.test(hrefAttr));
      if (text && (kind === "link" || kind === "tab") && !isSettingsCat && A.isActiveTab(n)) {
        text = text + (navRail ? " (current page)" : " (selected)");
      }

      items.push({
        top: topRel, left: leftRel,
        idx: thisIdx, kind: kind, tag: n.tagName.toLowerCase(),
        role: lower(n.getAttribute && n.getAttribute("role") || ""),
        text: text, value: A.valueOf(kind, n),
        controlType: ctype, clickable: clickable,
        level: A.headingLevel(n), live: A.liveFor(n),
        disabled: A.disabledFor(n), options: A.optionsFor(n),
        // Throttle-calibration axis tag (0 = not on this page / shared detent column).
        _axis: axisPanels.length ? axisOfNode(n) : 0,
        navRail: navRail, _groundSub: groundSub,
        // Quick Controls pane membership — grouped right after the gear by
        // orderQuickControls (only set when the pane is open).
        _qc: A.insideQuickControlsPane(n)
      });
    }

    // Pass 2: visible DESCRIPTIVE TEXT leaves (setting names like "ADIRS Align
    // Time", values, list/table cells, descriptions). The flyPad renders these
    // as plain <div>/<span>/<p>, which pass 1 (interactive + headings) skips, so
    // controls would otherwise have no context ("Instant / Fast / Real — of
    // what?"). Mirror the MCDU agent's static-text leaf pass.
    for (var ti = 0; ti < all.length && items.length < 600; ti++) {
      var tn = all[ti];
      if (!A.isVisible(tn)) continue;
      if (A.insideGroundRegion(tn)) continue; // owned by a Ground builder
      if (A.insideStatusBar(tn)) continue;    // collapsed into the one status line
      if (A.classify(tn)) continue;        // already captured (interactive/heading)
      if (A.isInsideStamped(tn)) continue; // text inside a captured control
      var own = A.directText(tn);          // immediate text only -> leaf labels/values
      if (!own || own.length > 80) continue;
      // Stringified booleans/nullish leak from FBW template literals like
      // `${!cond && t('…')}` (e.g. the Fuel rate-selector tooltips render "false");
      // they are never real content.
      if (/^(true|false|undefined|null)$/i.test(own)) continue;
      // A pure "(...)" sub-label belongs to an adjacent control (it is folded into
      // that control's label), so don't surface it as its own line.
      if (own.charAt(0) === "(" && own.charAt(own.length - 1) === ")") continue;
      var tr = tn.getBoundingClientRect();
      var tTop = tr.top - rootRect.top, tLeft = tr.left - rootRect.left;
      // Skip text in the far-left nav-rail column (below the top status bar): these
      // are the rail's full-name tooltips (e.g. "Navigation & Charts", "Air Traffic
      // Control") that duplicate the nav LINKS and aren't clickable.
      if (tLeft < 100 && tTop > 12) continue;
      items.push({
        top: tTop, left: tLeft,
        idx: 0, kind: "text", tag: tn.tagName.toLowerCase(), role: "",
        text: own, value: "", controlType: "", clickable: false,
        level: 0, live: A.liveFor(tn), disabled: false, options: [],
        _axis: axisPanels.length ? axisOfNode(tn) : 0, navRail: false
      });
    }

    // Cluster items into visual ROWS by top-proximity (NOT a fixed grid) so a
    // label and its control on the same row never split across a bucket boundary
    // (e.g. a "Theme" label at top 727 and its options at 719 must read together,
    // label first). Then read order = nav-rail-last, row, left-to-right.
    var TOL = 22;
    var byTop = items.slice().sort(function (a, b) {
      return ((a.navRail ? 1 : 0) - (b.navRail ? 1 : 0)) || (a.top - b.top);
    });
    var row = 0, lastTop = null, lastNav = null;
    for (var ri = 0; ri < byTop.length; ri++) {
      var it = byTop[ri];
      if (lastTop === null || it.navRail !== lastNav || (it.top - lastTop) > TOL) row++;
      it._row = row;
      lastTop = it.top; lastNav = it.navRail;
    }

    // The Dashboard is a TWO-COLUMN layout: left = "Your Flight" (FlightWidget),
    // right = "Important Information" (RemindersWidget: Weather / Pinned Charts /
    // Maintenance / Checklists). A plain row-by-row, left-to-right read interleaves
    // the two columns into a jumble ("Your Flight ... Weather ... ZFW ... DUBAI ..."),
    // so for the Dashboard ONLY we read the entire left column top-to-bottom, then
    // the entire right column — each widget then reads as one coherent block under
    // its own heading. Other pages (Settings rows, etc.) keep the row-wise order so
    // a setting name and its control stay together on one line.
    // Dashboard keeps its dedicated 48%-width split (its right column is reminder
    // widgets, detected by heading); every other page uses the general gap-based
    // column detector so two-column pages (Checklists menu+items, etc.) read
    // column-first instead of zippering. Single-column pages return -1 → row-wise.
    var dashMid = A.isDashboard(root) ? (rootRect.width * 0.48) : A.splitColumns(items);
    if (dashMid > 0) {
      for (var ci = 0; ci < items.length; ci++)
        items[ci]._col = (!items[ci].navRail && items[ci].left > dashMid) ? 1 : 0;
      items.sort(function (a, b) {
        return ((a.navRail ? 1 : 0) - (b.navRail ? 1 : 0)) || (a._col - b._col) || (a._row - b._row) || (a.left - b.left);
      });
    } else {
      items.sort(function (a, b) {
        return ((a.navRail ? 1 : 0) - (b.navRail ? 1 : 0)) || (a._row - b._row) || (a.left - b.left);
      });
    }

    // Qualify the duplicated/unlabeled throttle-calibration controls by axis +
    // Low/High BEFORE dedupe, so the now-distinct labels are no longer collapsed.
    items = A.relabelThrottleCalib(items);
    // Re-order the throttle-calibration page into logical column blocks (the
    // generic two-column sort zippers its three columns). No-op on other pages.
    A.orderThrottleCalib(items);
    // Group the Ground page's sub-tab strip apart from its service tiles. No-op
    // on other pages.
    A.orderGroundServices(items);
    // Group the open Quick Controls pane right after the gear (no-op when closed).
    A.orderQuickControls(items);

    items = A.dedupe(items);
    A._elements = items;
    return items;
  };

  // The setting NAME for a toggle/switch: the first meaningful text leaf in the
  // control's row (the flyPad lays a setting out as name [+ "(...)" sublabel] on
  // the left and the Toggle on the right). Skips parenthetical sublabels like
  // "(Unrealistic)". Returning the exact name also lets the de-dup drop the
  // separate text line so the setting is not read twice.
  A.firstTextLeaf = function (root) {
    var all = root.getElementsByTagName("*");
    for (var i = 0; i < all.length; i++) {
      if (!A.isVisible(all[i])) continue;
      var t = A.directText(all[i]);
      if (t && t.length >= 2 && t.charAt(0) !== "(") return t;
    }
    return "";
  };

  // First parenthetical sub-label in a row, e.g. "(Unrealistic)".
  A.firstParenthetical = function (root) {
    var all = root.getElementsByTagName("*");
    for (var i = 0; i < all.length; i++) {
      if (!A.isVisible(all[i])) continue;
      var t = A.directText(all[i]);
      if (t && t.charAt(0) === "(" && t.charAt(t.length - 1) === ")") return t;
    }
    return "";
  };

  A.toggleLabel = function (n) {
    // Walk up to the row container (a parent that holds text besides the toggle),
    // then take its first real text leaf as the setting name, plus any "(...)"
    // sub-label (so "(Unrealistic)" reads as part of the setting, not its own line).
    var row = n.parentElement, guard = 0;
    while (row && guard < 4) {
      guard++;
      if (clean(row.textContent).length >= 2) {
        var name = A.firstTextLeaf(row);
        if (name) {
          var sub = A.firstParenthetical(row);
          return sub ? name + " " + sub : name;
        }
      }
      row = row.parentElement;
    }
    return "";
  };

  // A node "contains an interactive control" if any descendant classifies as
  // something other than a heading. Used to keep only innermost controls.
  A.containsInteractive = function (n) {
    var kids = n.getElementsByTagName("*");
    for (var i = 0; i < kids.length; i++) {
      if (!A.isVisible(kids[i])) continue;
      var k = A.classify(kids[i]);
      if (k && k !== "heading") return true;
    }
    return false;
  };

  // Collapse repeats so nothing is read twice:
  //  (a) a non-interactive heading whose text duplicates a clickable item on
  //      the same/adjacent row (flyPad cards render the label as both a link
  //      and an inner <h2> with identical text), and
  //  (b) consecutive identical lines (keep the interactive/clickable one).
  A.dedupe = function (items) {
    function rowBucket(it) { return Math.round(it.top / A.ROW_Y_TOLERANCE_PX); }
    function norm(s) { return clean(s).toUpperCase(); }

    var interactiveByRow = {};
    var interactiveAll = {};
    var interactivePrefix = {};
    for (var a = 0; a < items.length; a++) {
      if (items[a].idx > 0) {
        var rb = rowBucket(items[a]);
        var key = norm(items[a].text);
        if (!key) continue;
        if (!interactiveByRow[rb]) interactiveByRow[rb] = {};
        interactiveByRow[rb][key] = true;
        interactiveAll[key] = true;
        // A control labelled "NAME (SUBLABEL)" makes the separate "NAME" text line
        // redundant — record the NAME prefix so it is dropped below.
        var pi = key.indexOf(" (");
        if (pi > 0) interactivePrefix[key.substring(0, pi)] = true;
      }
    }

    var out = [];
    for (var b = 0; b < items.length; b++) {
      var it = items[b];
      if (it.idx === 0) {
        var k = norm(it.text);
        // A descriptive-text line whose exact text is ALSO an interactive control
        // anywhere on the page is redundant (e.g. nav-rail icon labels duplicated
        // by their links, or a card title repeated as its link). Also drop a NAME
        // line already folded into a "NAME (SUBLABEL)" control. Drop it.
        if (k && (interactiveAll[k] || interactivePrefix[k])) continue;
        var here = rowBucket(it);
        var dup = false;
        for (var d = -1; d <= 1; d++) {
          var bucket = interactiveByRow[here + d];
          if (bucket && bucket[k]) { dup = true; break; }
        }
        if (dup) continue;
      }
      if (out.length > 0 && norm(out[out.length - 1].text) === norm(it.text)) {
        var prev = out[out.length - 1];
        // Replace a non-interactive text line with the interactive control that
        // echoes it; otherwise drop a non-interactive echo of the previous line.
        // BUT two DISTINCT interactive controls that happen to share a label (e.g.
        // the per-axis "Set from Throttle" button on each throttle calibration
        // axis, or a "Set" button repeated per row) are SEPARATE controls — keep
        // both. The shell disambiguates identical labels with an occurrence index.
        if (prev.idx === 0 && it.idx > 0) { out[out.length - 1] = it; continue; }
        if (it.idx === 0) continue;
        // both interactive with identical label → fall through, keep both
      }
      out.push(it);
    }
    return out;
  };

  // Page label = the ACTIVE nav-rail route, not the first <h1>. The flyPad marks
  // the selected nav link with the bare class token "bg-theme-accent" (inactive
  // links only have "hover:bg-theme-accent"); its href (e.g. "/settings") gives
  // the true current page. Using the first <h1> was wrong — it caught modal/toast
  // headings (e.g. "Checklist Reset Warning") and reported the wrong page.
  A.pageLabel = function (root) {
    var base = "";
    try {
      var links = root.querySelectorAll("a[href]");
      for (var i = 0; i < links.length; i++) {
        var href = (links[i].getAttribute("href") || "");
        if (href.charAt(0) !== "/") continue;
        var toks = A.cls(links[i]).split(/\s+/);
        for (var t = 0; t < toks.length; t++) {
          if (toks[t] === "bg-theme-accent") { base = A.prettifyHref(href); break; }
        }
        if (base) break;
      }
    } catch (e) {}
    // Append the content's own heading when it adds detail — this is how a
    // settings SUB-page (e.g. "3rd Party Options") or an open modal (e.g.
    // "Checklist Reset Warning") becomes visible, so navigating into a sub-page
    // announces a changed label instead of staying on the nav-rail section name.
    var head = "";
    try {
      var h = root.querySelector("h1, h2");
      if (h) head = clean(h.textContent);
    } catch (e2) {}
    // Don't fold a transient notice/warning heading into the page name (e.g. a
    // persistent "Checklist Reset Warning" toast) — it isn't the page title and
    // made every page read as "X: Checklist Reset Warning". Real sub-page titles
    // (e.g. "3rd Party Options") still get appended.
    var isNotice = /WARNING|RESET|CAUTION|NOTICE|CONFIRM|ARE YOU SURE/i.test(head);
    // The settings detail content heading already starts with the active route name
    // ("Settings - <Category>"), so concatenating base ("Settings") doubles it.
    if (base && head && !isNotice && /^Settings\s*-\s*/i.test(head)) return head;
    if (base && head && !isNotice && head.toUpperCase() !== base.toUpperCase()) return base + ": " + head;
    if (base) return base;
    if (head) return head;
    return clean(document.title || "");
  };

  // ---- public: read ------------------------------------------------------
  A.scrape = function () {
    try {
      var root = A.findRoot();
      if (!root) return JSON.stringify({ ok: false, error: "flyPad root not found (powered up?)" });
      var els = A.enumerate(root);
      return JSON.stringify({ ok: true, page: A.pageLabel(root), elements: els });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) });
    }
  };

  // ---- public: drive -----------------------------------------------------
  A.findByIdx = function (index) {
    return document.querySelector('[data-fbw-efb-idx="' + index + '"]');
  };

  A.clickNode = function (node) {
    try {
      if (typeof node.click === "function") node.click();
      else node.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
    } catch (e) {}
  };

  A.clickElement = function (index) {
    var node = A.findByIdx(index);
    if (!node) return "missing";
    if (A.disabledFor(node)) return "disabled";
    A.clickNode(node);
    return "ok";
  };

  // Sets a value the React-friendly way: use the native value setter so React's
  // internal value tracker notices the change, then dispatch input + change.
  A.setNativeValue = function (el, value) {
    try {
      var proto = el.tagName === "TEXTAREA"
        ? window.HTMLTextAreaElement.prototype
        : window.HTMLInputElement.prototype;
      var desc = Object.getOwnPropertyDescriptor(proto, "value");
      if (desc && desc.set) desc.set.call(el, value);
      else el.value = value;
    } catch (e) { try { el.value = value; } catch (e2) {} }
    try { el.dispatchEvent(new Event("input", { bubbles: true })); } catch (e) {}
    try { el.dispatchEvent(new Event("change", { bubbles: true })); } catch (e) {}
  };

  A.setValue = function (index, value) {
    var node = A.findByIdx(index);
    if (!node) return "missing";
    var kind = A.classify(node);
    value = (value === null || value === undefined) ? "" : String(value);

    if (kind === "checkbox" || kind === "toggle" || kind === "checkitem") {
      var want = value === "true";
      var cur = A.valueOf(kind, node) === "true";
      if (want !== cur) A.clickNode(node);   // checklist item: clicking the row toggles complete
      return "ok";
    }

    var tag = node.tagName.toLowerCase();
    if (tag === "input" || tag === "textarea") {
      node.focus();
      A.setNativeValue(node, value);
      // FBW's SimpleInput commits (clamp + write to the sim) in its onFocusOut
      // handler — the blur() below is the LOAD-BEARING commit step. The Enter
      // keydown/keyup dispatches don't match FBW's keypress listener and have no
      // observable effect on SimpleInput, but are kept as harmless belt-and-braces
      // for any other input that does listen to keydown/keyup (Passengers, Cargo,
      // ZFW, weights, fuel target).
      try { node.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", keyCode: 13, which: 13, bubbles: true })); } catch (e) {}
      try { node.dispatchEvent(new KeyboardEvent("keyup", { key: "Enter", keyCode: 13, which: 13, bubbles: true })); } catch (e2) {}
      try { node.blur(); } catch (e3) {}
      return "ok";
    }
    if (node.getAttribute && node.getAttribute("contenteditable") === "true") {
      node.focus();
      node.textContent = value;
      try { node.dispatchEvent(new Event("input", { bubbles: true })); } catch (e) {}
      return "ok";
    }
    if (tag === "select") {
      node.value = value;
      try { node.dispatchEvent(new Event("change", { bubbles: true })); } catch (e) {}
      return "ok";
    }
    // Not directly settable (e.g. a slider div) — click as a fallback.
    A.clickNode(node);
    return "ok";
  };

  A.ping = function () { return "MSFSBA_FLYPAD_OK"; };

  // Power the flyPad/EFB screen on. Two parts:
  //   1. Set L:A32NX_EFB_TURNED_ON = 1. NOTE: this write is legacy / no-op in
  //      current FBW builds — there are no upstream references to this L:var in
  //      the live flyPad source. Kept as belt-and-braces in case a future build
  //      re-introduces it, but the real wake is step 2.
  //   2. The EFB gauge's render loop stays DORMANT until it receives an
  //      interaction (this is what "tapping the tablet" actually does — the
  //      synthetic click on the shutoff overlay is the REAL wake path). If the
  //      screen has no content yet, we dispatch a benign synthetic pointer/mouse
  //      interaction on the root container to resume rendering. While dormant
  //      the container is empty, so this can't activate any control; once
  //      content exists we skip it.
  // Idempotent — safe to call on every (re)connect.
  A.powerOn = function () {
    try {
      if (typeof SimVar !== "undefined" && SimVar.SetSimVarValue) {
        SimVar.SetSimVarValue("L:A32NX_EFB_TURNED_ON", "number", 1);
      }
    } catch (e) {}
    try {
      var mount = document.getElementById("MSFS_REACT_MOUNT") || document.body;
      var empty = !mount || (mount.innerText || "").replace(/\s+/g, "").length === 0;
      if (empty) {
        var root = (mount && mount.firstChild) || document.body;
        var o = { bubbles: true, cancelable: true, clientX: 5, clientY: 5 };
        var types = ["pointerdown", "mousedown", "pointerup", "mouseup", "click"];
        for (var i = 0; i < types.length; i++) {
          try { root.dispatchEvent(new MouseEvent(types[i], o)); } catch (e) {}
        }
      }
    } catch (e) {}
    return "ok";
  };

  window.__MSFSBA_FLYPAD = A;
  return "MSFSBA_FLYPAD_INSTALLED";
})();
