// coherent-flypad-agent.js
//
// Persistent in-page agent for the FlyByWire A380X flyPad EFB (flyPadOS 3),
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
    // small "checkbox box" (.border-4.border-current). Clicking the row toggles
    // the item complete; a check icon (svg) appears in the box when done. Gate the
    // querySelector on the cheap class signature so it only runs on candidate rows.
    if (c.indexOf("space-x-4") >= 0 && c.indexOf("flex-row") >= 0) {
      try { if (n.querySelector(".border-4.border-current")) return "checkitem"; } catch (e) {}
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

  // A label good enough for a screen reader. The flyPad behaves like an app:
  // many controls have no text (icon nav-rail links) or a generic label
  // ("Go to Page"). Derive something meaningful from aria/title, then the
  // element's own text, then the router href, then the section heading.
  A.labelFor = function (n) {
    var base = clean((n.getAttribute && (n.getAttribute("aria-label") || n.getAttribute("title"))) || "");
    if (!base) base = A.directText(n);
    if (!base) base = clean(n.textContent);

    var lower = base.toLowerCase();
    var generic = (base === "" || lower === "go to page" || lower === "go to" || lower === "open");
    if (!generic) return base;

    var href = (n.getAttribute && n.getAttribute("href")) || "";
    var heading = A.nearestHeading(n);
    if (base === "") {
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
        // The field's own placeholder is usually its best hint (e.g. a METAR box
        // placeholder is the airport ICAO "LIMC"/"EDDF"). Prefer it over the
        // parent text, which can be a neighbouring control's caption.
        var ph = clean((n.getAttribute && n.getAttribute("placeholder")) || "");
        if (ph) return ph;
        // Otherwise borrow the parent unit label (PAX/KGS).
        var fl = A.fieldUnitLabel(n);
        if (fl) return fl;
      }
      // Toggles/switches carry no own text; take the row's setting NAME (its
      // first real text leaf) — never the page heading, which would mislabel
      // every toggle on the page identically.
      if (isToggle) return A.toggleLabel(n);
      if (href) return A.prettifyHref(href);   // icon nav-rail link -> "Dashboard" etc.
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
      if (cur.getAttribute && cur.getAttribute("data-fbwa380-efb-idx")) return true;
      cur = cur.parentElement;
    }
    return false;
  };

  // Builds the flat, screen-reader-friendly element list for the current
  // flyPad page: every visible interactive control PLUS visible headings/text,
  // one per line, in reading order, de-duplicated. Interactive/clickable items
  // get a stable index stamped on the node (data-fbwa380-efb-idx) so clicks and
  // value sets can find them again.
  A.enumerate = function (root) {
    var stale = root.querySelectorAll("[data-fbwa380-efb-idx]");
    for (var s = 0; s < stale.length; s++) stale[s].removeAttribute("data-fbwa380-efb-idx");

    var rootRect = root.getBoundingClientRect();
    var all = root.getElementsByTagName("*");
    var items = [];
    var idx = 1;

    for (var i = 0; i < all.length && idx <= 400; i++) {
      var n = all[i];
      if (!A.isVisible(n)) continue;
      var kind = A.classify(n);
      if (!kind) continue;

      var interactive = kind !== "heading";

      // For interactive controls, skip wrappers that contain a more specific
      // control (we want the innermost actionable node, one per line).
      if (interactive && A.containsInteractive(n)) continue;
      if (A.isInsideStamped(n)) continue;

      var text = A.labelFor(n);
      var ctype = A.controlType(kind, n);
      var clickable = A.isClickable(kind);

      // Mark the selected option of a segmented setting control (e.g. the chosen
      // "Instant / Fast / Real"): the active segment carries bg-theme-highlight as
      // a STANDALONE token. Must be a token check, not a substring — otherwise the
      // common hover:bg-theme-highlight style falsely marks every button selected.
      if (text && A.hasClassToken(n, "bg-theme-highlight")) text = text + " (selected)";

      // Drop empty, non-actionable noise. Inputs/checkboxes keep their slot
      // (their value carries meaning even with no label).
      if (!text && ctype !== "text" && ctype !== "select" && ctype !== "checkbox") continue;

      var thisIdx = 0;
      if (interactive) { thisIdx = idx; n.setAttribute("data-fbwa380-efb-idx", String(idx)); idx++; }

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
        level: 0, live: A.liveFor(tn), disabled: false, options: [], navRail: false
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
    items.sort(function (a, b) {
      return ((a.navRail ? 1 : 0) - (b.navRail ? 1 : 0)) || (a._row - b._row) || (a.left - b.left);
    });

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
        if (prev.idx === 0 && it.idx > 0) out[out.length - 1] = it;
        continue;
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
    return document.querySelector('[data-fbwa380-efb-idx="' + index + '"]');
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

  // Power the flyPad/EFB screen on. The tablet boots off (L:A32NX_EFB_TURNED_ON
  // = 0) and is otherwise woken by tapping it in the 3D cockpit; setting the
  // L-var here replaces that so the form has content instead of a blank screen.
  // Idempotent — safe to call on every (re)connect.
  A.powerOn = function () {
    try {
      if (typeof SimVar !== "undefined" && SimVar.SetSimVarValue) {
        SimVar.SetSimVarValue("L:A32NX_EFB_TURNED_ON", "number", 1);
      }
    } catch (e) {}
    return "ok";
  };

  window.__MSFSBA_FLYPAD = A;
  return "MSFSBA_FLYPAD_INSTALLED";
})();
