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
    var base = clean((n.getAttribute && (n.getAttribute("aria-label") || n.getAttribute("title"))) || "");
    if (!base) base = A.directText(n);
    if (!base) {
      // A clickable tile (e.g. a Ground service) keeps its label in a child
      // heading; prefer that clean name over the concatenated textContent.
      var ch = n.querySelector && n.querySelector("h1,h2,h3,h4,h5,h6");
      base = ch ? clean(ch.textContent) : clean(n.textContent);
    }

    var lower = base.toLowerCase();
    var generic = (base === "" || lower === "go to page" || lower === "go to" || lower === "open");
    if (!generic) return base;

    var href = (n.getAttribute && n.getAttribute("href")) || "";
    var heading = A.nearestHeading(n);
    if (base === "") {
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
      // Completed = a check icon (svg) inside the item's checkbox box.
      var box = n.querySelector(".border-4.border-current");
      return (box && box.querySelector("svg")) ? "true" : "false";
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

  // True when the control is disabled (native disabled or aria-disabled), so the
  // renderer can mark it and a screen reader announces it as unavailable.
  A.disabledFor = function (n) {
    try {
      if (n.disabled === true) return true;
      if (n.getAttribute && lower(n.getAttribute("aria-disabled") || "") === "true") return true;
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
    return false;
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

  A.enumerate = function (root) {
    var stale = root.querySelectorAll("[data-fbw-efb-idx]");
    for (var s = 0; s < stale.length; s++) stale[s].removeAttribute("data-fbw-efb-idx");

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

    for (var i = 0; i < all.length && idx <= 400; i++) {
      var n = all[i];
      if (!A.isVisible(n)) continue;
      var kind = A.classify(n);
      if (!kind) continue;

      // User-requested: hide the Fuel page refuel target SLIDER (rc-slider bar, which
      // reads as an empty field for a screen reader) and the numeric edit box (it is
      // redundant with the Instant/Fast/Real + Start refueling controls). Scoped to
      // the refuel context via the section heading so Payload inputs (Passengers /
      // Cargo / ZFW) and any sliders on other pages are untouched.
      if ((kind === "slider" || kind === "input") && /refuel/i.test(A.nearestHeading(n))) continue;

      var interactive = kind !== "heading";

      // For interactive controls, skip wrappers that contain a more specific
      // control (we want the innermost actionable node, one per line).
      if (interactive && A.containsInteractive(n)) continue;
      if (A.isInsideStamped(n)) continue;

      var text = A.labelFor(n);
      var ctype = A.controlType(kind, n);
      var clickable = A.isClickable(kind);

      // Mark the selected option of a segmented setting control (e.g. the chosen
      // "Instant / Fast / Real", throttle axis, or calibration detent). Only a
      // genuine choice control gets the marker — a primary action button shares
      // the same bg-theme-highlight background, so isSelectedSegment additionally
      // requires an unselected (bg-theme-accent) sibling to disambiguate.
      if (text && A.isSelectedSegment(n)) text = text + " (selected)";

      // Drop empty, non-actionable noise. Inputs/checkboxes keep their slot
      // (their value carries meaning even with no label).
      if (!text && ctype !== "text" && ctype !== "select" && ctype !== "checkbox") continue;

      var thisIdx = 0;
      if (interactive) { thisIdx = idx; n.setAttribute("data-fbw-efb-idx", String(idx)); idx++; }

      var r = n.getBoundingClientRect();
      var topRel = r.top - rootRect.top, leftRel = r.left - rootRect.left;
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
        // The left nav rail (page links in the far-left column) is grouped at the
        // END so it stops interleaving with the page content row-by-row.
        navRail: (kind === "link" && leftRel < 100 && topRel > 40)
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
      if (A.classify(tn)) continue;        // already captured (interactive/heading)
      if (A.isInsideStamped(tn)) continue; // text inside a captured control
      var own = A.directText(tn);          // immediate text only -> leaf labels/values
      if (!own || own.length > 80) continue;
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
  //   1. Set L:A32NX_EFB_TURNED_ON = 1 (the power state a cockpit tap sets).
  //   2. The EFB gauge's render loop stays DORMANT until it receives an
  //      interaction (this is what "tapping the tablet" actually does — the
  //      L-var alone doesn't wake it). If the screen has no content yet, we
  //      dispatch a benign synthetic pointer/mouse interaction on the root
  //      container to resume rendering. While dormant the container is empty,
  //      so this can't activate any control; once content exists we skip it.
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
