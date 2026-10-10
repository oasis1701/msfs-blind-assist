// coherent-gtc-agent.js — MSFSBA in-page agent for a Working Title G3000 GTC (touchscreen
// controller) view on the Skyward Citation Sovereign+. Installed by CoherentDisplayClient
// through Runtime.evaluate (NO injection, no Community package) and polled through the shared
// __MSFSBA_DISP.scrape() contract.
//
// Measured 2026-09-09/10 on the live aircraft: buttons are .touch-button / .bg-img-touch-button,
// disabled ones carry touch-button-disabled, hidden ones touch-button-hidden or a zero-size
// rect; the page title is the visible .gtc-view-title-inner-text; a click is a
// mousedown / mouseup / click triple at the button's centre (navigation, the keyboard, the
// keypad and the transponder all answer); knobs are the WT H: events
// AS3000_TSC_Vertical_<n>_<Name> (the four Sovereign GTCs are all VERTICAL units).
// ES5 ONLY (Chromium 49). Returns "MSFSBA_DISP_INSTALLED".
(function () {
  "use strict";
  var A = {}; A.VERSION = 1; A._buttons = [];
  // U+0338 is the Garmin font's slashed zero ("EME0̸1", "L60̸3"): a screen reader speaks the
  // combining overlay, so it is dropped (measured 2026-09-15 on the Active Flight Plan page).
  // U+00D0 "Ð" is Garmin's direct-to glyph ("VNAV Ð" is Vertical Direct To). An empty entry field
  // is a run of underscores ("_____ LB", "_.__°", "__:__ UTC") that a screen reader reads as
  // "underline underline …", so it reads "blank". The Weight and Fuel worksheet puts its
  // operators in cells of their own ("@ | = | 0 | LB", "– | _____ | LB"), so a lone operator reads
  // as a word (measured 2026-09-15).
  var OPERATORS = { "+": "plus", "-": "minus", "–": "minus", "−": "minus", "=": "equals", "@": "at" };
  function clean(s) {
    var t = String(s || "").replace(/̸/g, "").replace(/Ð/g, "Direct To")
      .replace(/_+(?:[.:]_+)*/g, " blank ").replace(/[–—-]{3,}/g, " blank ")   // "–––" is a field with no data (Airport Information Time / Fuel)
      .replace(/ @ /g, " at ").replace(/\s+/g, " ").trim();
    return OPERATORS.hasOwnProperty(t) ? OPERATORS[t] : t;
  }
  function txt(e) { return clean(e.innerText || e.textContent || ""); }
  // A zero-size rect OR visibility:hidden (the frequency buttons' digit-entry slots keep their
  // size while hidden) means the pilot cannot see it.
  function visible(e) {
    var r = e.getBoundingClientRect(); if (!(r.width > 0 && r.height > 0)) return false;
    try { return window.getComputedStyle(e).visibility !== "hidden"; } catch (x) { return true; }
  }
  function hasClass(e, c) { return (" " + String(e.className) + " ").indexOf(" " + c + " ") >= 0; }
  // A .gtc-view the pilot cannot see or touch, though it is still display:block with a real rect
  // (measured 2026-09-15): the page a press slides away keeps gtc-page-close-*-animation and sits
  // off-screen at x=-480 for ~1 s before it gains "hidden"; a page under a full popup (the Timer
  // page under its Enter Time keypad) gains "occlude-hidden" and fades to opacity 0. Reading either
  // is what left the previous page's buttons in the list after a press.
  function deadView(v) {
    var c = " " + String(v.className) + " ";
    return c.indexOf(" hidden ") >= 0 || c.indexOf(" occlude-hidden ") >= 0 || /-close-[a-z-]*animation/.test(c);
  }
  // The topmost open popup, or null. Only it can be touched: a popup does not always mark the page
  // under it (Audio & Radios slides down over PFD Home in the OVERLAY stack with
  // gtc-popup-no-background-occlusion, and Home keeps no occlude-hidden — measured 2026-09-15), and
  // a press outside an open popup closes it rather than reaching the page. Overlay-stack popups
  // sit above main-stack ones; within a stack the one opened last (last in the DOM) is on top.
  // Computed once per scrape (A._top), since every visibility test consults it.
  function findTopPopup() {
    var ps = document.querySelectorAll(".gtc-popup-wrapper"), top = null, topOverlay = false;
    for (var i = 0; i < ps.length; i++) {
      if (deadView(ps[i]) || !visible(ps[i])) continue;
      var overlay = !!ancestorWith(ps[i], "gtc-overlay-view-stack");
      if (!top || overlay || !topOverlay) { top = ps[i]; topOverlay = overlay; }
    }
    return top;
  }
  // The nearest enclosing .gtc-view decides; an element outside every view (the radio bar, the
  // bottom bar, the title bar) is live.
  function inDeadView(e) {
    var p = e;
    while (p && p !== document) {
      if (hasClass(p, "gtc-view")) return deadView(p) || (A._top !== null && A._top !== undefined && p !== A._top);
      p = p.parentNode;
    }
    return false;
  }
  function inPopup(e) {
    var p = e;
    while (p && p !== document) { if (hasClass(p, "gtc-popup-wrapper")) return true; p = p.parentNode; }
    return false;
  }
  // Tabs (Audio & Radios Pilot / Copilot / Pass, the PERF and setup pages) are not touch buttons:
  // .gtc-tab with .gtc-tab-label, gtc-tab-selected, gtc-tab-disabled (measured 2026-09-15).
  var BUTTONS = ".touch-button, .bg-img-touch-button, .gtc-tab";
  function isButton(e) { return hasClass(e, "touch-button") || hasClass(e, "bg-img-touch-button") || hasClass(e, "gtc-tab"); }
  function isDisabled(e) { return hasClass(e, "touch-button-disabled") || hasClass(e, "gtc-tab-disabled"); }
  function isHidden(e) { return hasClass(e, "touch-button-hidden") || !visible(e) || inDeadView(e); }
  function insideButton(e) {
    var p = e.parentNode;
    while (p && p !== document) { if (isButton(p)) return true; p = p.parentNode; }
    return false;
  }
  function inChrome(e) {
    var p = e.parentNode;
    while (p && p !== document) {
      if (hasClass(p, "gtc-view-title") || hasClass(p, "label-bar") || hasClass(p, "gtc-nav-com-top-bar") || hasClass(p, "button-bar")) return true;
      p = p.parentNode;
    }
    return false;
  }
  function gtcIndex() { var m = /WTG3000_GTC_(\d)/.exec(document.title || ""); return m ? m[1] : "1"; }
  function main() { return document.querySelector(".gtc-main-content") || document.body; }

  // The title bar keeps one slot per open view (title-1, title-2 …) and its container names
  // the slot in front with show-title-N; only that slot is the current page.
  // A slide-out popup (the VNAV Constraint menu) leaves the title bar on the page underneath and
  // carries its own .gtc-panel-title (measured 2026-09-15), so the topmost live popup's panel title
  // wins; a full popup such as the Enter Time keypad does move the title bar.
  function livePopupTitle() {
    var top = findTopPopup(); if (!top) return "";
    var h = top.querySelector(".gtc-panel-title");
    return h ? txt(h) : "";
  }
  A.title = function () {
    var pt = livePopupTitle(); if (pt) return pt;
    var bar = document.querySelector(".gtc-view-title");
    if (bar) {
      var m = /show-title-(\d+)/.exec(String(bar.className));
      if (m) { var slot = bar.querySelector(".gtc-view-title-inner-text.title-" + m[1]); if (slot && txt(slot)) return txt(slot); }
    }
    var ts = document.querySelectorAll(".gtc-view-title-inner-text"); var last = "";
    for (var i = 0; i < ts.length; i++) if (visible(ts[i]) && txt(ts[i])) last = txt(ts[i]);
    return last;
  };
  // A button's label with a space between its parts ("COM1 124.850", not "COM1124.850").
  // Walks TEXT NODES, not leaf elements: the VNAV Constraint altitude button renders "4000" as a
  // bare text node beside its <span>FT</span>, and a leaf-element walk kept only "FT".
  // Directly adjacent text nodes of ONE element display run together ("EGNX" "-" "EKCH" is how the
  // ACARS request list renders EGNX-EKCH), so they join with no separator; text split by an
  // element (a <br>, a <span>) or in different elements joins with `sep`.
  function collect(root, keep, sep) {
    var out = [], lastParent = null; var w = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null, false); var n;
    while ((n = w.nextNode())) {
      var raw = String(n.nodeValue || ""); if (!clean(raw)) continue;
      var p = n.parentNode; if (!keep(p)) continue;
      if (p === lastParent && out.length && n.previousSibling && n.previousSibling.nodeType === 3) out[out.length - 1] += raw;   // "Wind REQ<br>ALT" keeps its space
      else out.push(raw);
      lastParent = p;
    }
    var parts = []; for (var i = 0; i < out.length; i++) { var t = clean(out[i]); if (t) parts.push(t); }
    return parts.join(sep);
  }
  function labelOf(e) {
    var t = collect(e, function (p) { return !(p && p !== e && !visible(p)); }, " ");   // hidden digit placeholders inside the frequency buttons are skipped
    return t || txt(e);
  }

  // What the label alone does not say (measured 2026-09-15 on Audio & Radios):
  // - a toggle button (touch-button-toggle: NAV1, COM1, MIC, Marker, High Sense, OBS …) shows its
  //   state ONLY as the toggle-status-bar-on class on its status bar → ", on" / ", off";
  // - a radio row's frequency button holds .active-freq and .stby-freq → "110.50, standby 113.90";
  // - a radio row's volume is a slider whose .vol-label sits outside every button → spoken on the
  //   row's first button ("NAV1, on, volume 100%") and dropped from the text rows.
  function decorate(e, label) {
    var base = label;
    var act = e.querySelector(".active-freq"), stby = e.querySelector(".stby-freq");
    if (act && stby) {
      // Whatever else the button says (the ADF button's "Mode ADF", a navaid ident) follows.
      var rest = " " + label + " ";
      rest = rest.replace(" " + txt(act) + " ", " ").replace(" " + txt(stby) + " ", " ");
      label = txt(act) + ", standby " + txt(stby) + (clean(rest) ? ", " + clean(rest) : "");
    }
    // Utilities → Initialization: a task's tick is an image that carries `hidden` until done.
    var tick = e.querySelector(".init-page-task-item-check-icon");
    if (tick) label += hasClass(tick, "hidden") ? ", not completed" : ", completed";
    if (hasClass(e, "gtc-tab")) {
      var tl = e.querySelector(".gtc-tab-label");
      label = (tl ? txt(tl) : label) + " tab" + (hasClass(e, "gtc-tab-selected") ? ", selected" : "");
    }
    // State (measured 2026-09-15, in flight): a toggle's status bar carries toggle-status-bar-on;
    // a CHOICE button (touch-button-set-value: PFD map Off / HSI Map / Inset Map, Traffic Auto /
    // TA Only, Relative / Absolute) carries the same class on the chosen one. A toggle whose text IS
    // a state word is also a choice in practice (Exterior Lights "Navigation: On | Off", "Beacon:
    // Normal | On | Off"; Traffic "On" / "Standby"), so it reads "selected", never "On, on".
    var sbar = e.querySelector(".toggle-status-bar");
    var lit = !!sbar && hasClass(sbar, "toggle-status-bar-on");
    if (hasClass(e, "touch-button-set-value") || (hasClass(e, "touch-button-toggle") && STATE_WORD.test(base))) {
      if (lit) label += ", selected";
    } else if (hasClass(e, "touch-button-toggle")) {
      if (sbar) label += lit ? ", on" : ", off";
    }
    var item = ancestorWith(e, "list-item");
    if (item && item.querySelector(".touch-button") === e) {
      var vol = item.querySelector(".vol-label");
      if (vol && visible(vol) && txt(vol)) label += ", volume " + txt(vol);
    }
    // A list row's own text names what its button acts on: ACARS Flight Plan Request rows are
    // "BVI4MF / EGNX-EKCH / Ready for Import" beside an [Import] or [Request] button, and read as
    // four bare "Request" buttons without it (measured 2026-09-15).
    var rt = item ? rowText(item) : "";
    if (rt) return rt + ", " + label;
    // A group title beside the buttons ("XPDR/TCAS Mode", "Altitude Display", "ADS-B" over their
    // choices; the Exterior Lights <label> "Navigation" before On / Off) names them.
    var gl = groupLabel(e);
    if (gl) return gl + ": " + label;
    // A row of buttons with no text of its own: the first names the rest (Speed Bugs "Vapp" then
    // "117 KT"; map settings "Traffic" then "Settings", "Connext Radar" then "1000 NM"). ONLY for a
    // button that says nothing by itself — a bare value or a generic word: MFD Home lays its
    // directory buttons out four to a row, and "Map Settings: TAWS" is wrong (measured 2026-09-15).
    var row = item || e.parentNode;
    if (row && row.nodeType === 1 && BARE_VALUE.test(base)) {
      var peers = row.querySelectorAll(".touch-button, .bg-img-touch-button");
      if (peers.length >= 2 && peers.length <= 4 && peers[0] !== e && sameLine(peers[0], e)) {
        var firstBase = labelOf(peers[0]);
        if (firstBase && firstBase !== base) return firstBase + ": " + label;
      }
    }
    return label;
  }
  var STATE_WORD = /^(On|Off|Normal|Auto|Standby|Enabled?|Disabled?|Manual|Dim|Bright|Norm)$/i;
  var BARE_VALUE = /^(blank|[-−+]?[\d.,:]+\s*(KT|KTS|NM|FT|M|MIN|SEC|S|LB|LBS|KG|GAL|%|°|FPM|MHZ|KHZ|HPA|IN)?|Settings|Options|Setup|Edit)$/i;
  function sameLine(a, b) { var ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect(); return Math.abs((ra.top + ra.bottom) / 2 - (rb.top + rb.bottom) / 2) < 12; }
  // The title of the button's own group: a <label>, or an element whose class ENDS in "title"
  // (xpdr-tcas-group-title, tfc-settings-group-title), that comes BEFORE the group's first button.
  // Narrow on purpose (measured 2026-09-15): a "-label" class is usually a value caption — Landing
  // Data's "Landing Weight" sits AFTER its buttons and captions a number, and Weight and Fuel's
  // "wf-label-value-row" is a label + value line — and either would have named unrelated buttons.
  // Marked so the text rows do not repeat it.
  function groupLabel(e) {
    var p = e.parentNode; if (!p || p.nodeType !== 1 || hasClass(p, "list-item")) return "";
    var buttons = p.querySelectorAll(".touch-button, .bg-img-touch-button"); if (buttons.length > 8) return "";
    for (var c = p.firstElementChild; c; c = c.nextElementSibling) {
      if (isButton(c) || c.querySelector(".touch-button, .bg-img-touch-button")) return "";   // reached a button first: no title precedes the group
      var cls = String(c.className);
      if (c.tagName !== "LABEL" && !/(^|\s)[\w-]*title(\s|$)/i.test(cls)) continue;
      var t = txt(c); if (!t || !visible(c)) continue;
      c.__msfsbaGroupTitle = A._stamp;
      return t;
    }
    return "";
  }
  // The visible text of a list row that sits outside every button (and is not a volume label).
  // A unit in an element of its own stays with its number: Nearest Airport rows are "100 | ° | 33.7 |
  // NM | VFR | 4950 | FT | EGFE Haverfordwest", read "100°, 33.7 NM, VFR, 4950 FT, EGFE Haverfordwest".
  function rowText(item) {
    return collect(item, function (p) {
      return !!p && p.nodeType === 1 && visible(p) && !isButton(p) && !insideButton(p) && !hasClass(p, "vol-label");
    }, ", ").replace(/, (°)/g, "$1").replace(/, (NM|FT|KT|LB|LBS|GAL|MHZ|MHz|KHZ|kHz|MIN|SEC|%|FPM|M)(?=,|$)/g, " $1");
  }
  // A list row's text is spoken on its buttons (see decorate), so the text rows drop it.
  function ownedByRowButton(e) {
    var item = ancestorWith(e, "list-item");
    return !!(item && item.querySelector(".touch-button"));
  }

  // Every page carries two persistent bars — the radio bar on top (.gtc-nav-com-top-bar: Audio &
  // Radios, COM1/2 and their standbys, MIC, MON) and the button bar at the bottom (.button-bar:
  // XPDR, squawk, Back, Home, MSG, Full/Half). They are listed AFTER the page's own buttons, each
  // under its own marker row, so the page content comes first (measured 2026-09-10).
  function barOf(e) {
    var p = e.parentNode;
    while (p && p !== document) {
      if (hasClass(p, "gtc-nav-com-top-bar")) return "top";
      if (hasClass(p, "button-bar")) return "bottom";
      p = p.parentNode;
    }
    return "";
  }
  function ancestorWith(e, c) {
    var p = e;
    while (p && p !== document) { if (hasClass(p, c)) return p; p = p.parentNode; }
    return null;
  }
  function one(root, sel) { var e = root.querySelector(sel); return e ? txt(e) : ""; }
  // "3000FT" / "FL120FT" / "_____FT" → "3000 feet" / "flight level 120" / "".
  function altText(line) {
    if (!line) return "";
    var inner = line.querySelector(".line-inner"); var t = inner ? txt(inner) : txt(line);
    t = t.replace(/FT$/, "").trim();
    if (!t || /^(FL ?)?blank$/.test(t)) return "";   // "_____" / "FL___" after clean()
    var fl = /^FL(\d+)$/.exec(t); if (fl) return "flight level " + fl[1];
    return t + " feet";
  }
  // One leg row of the Active Flight Plan page as a sentence. Measured 2026-09-15 against the
  // WT G3000 v2 source (FlightPlanLegData / AltitudeConstraintDisplay / SpeedConstraintDisplay):
  // the altitude display's altitude-constraint-display-{at,atorabove,atorbelow,between} is a
  // constraint, -unused with a number is VNAV's predicted altitude (CH645 9821 between the 12000
  // and 5000 constraints), -cyan on a constraint means DESIGNATED (VNAV flies it) and its absence
  // means published but not designated; `edited` differs from the published value and `invalid`
  // is the crossed-out constraint VNAV cannot meet. The FPA box shows the phase word (CLIMB)
  // instead of an angle when it carries show-phase; the speed display's at/above/below and
  // ias/mach classes give the speed constraint. A between constraint's line 2 is the lower bound.
  function legSummary(item) {
    var parts = [];
    var name = one(item, ".leg-name"), sub = one(item, ".leg-sub-label"), sup = one(item, ".leg-super-label");
    parts.push(sub && sub !== name ? name + " " + sub : name);
    if (sup) parts.push(sup);
    if (hasClass(item, "active-leg")) parts.push("active leg");
    var icon = item.querySelector(".leg-icon");
    if (icon && /fly_over/.test(String(icon.getAttribute("src")))) parts.push("fly-over");
    // An airway-exit row writes "MADUX exit Airway Q70" (name included), so it replaces the name.
    var exit = one(item, ".airway-exit-text");
    if (exit) { if (exit.indexOf(name) === 0) parts[0] = exit; else parts.push(/\bexit\b/i.test(exit) ? exit : "exit " + exit); }

    var box = item.querySelector(".flight-plan-altitude-box");
    var disp = box && !hasClass(box, "hidden") ? box.querySelector(".altitude-constraint-display") : null;
    if (disp) {
      var l1 = altText(disp.querySelector(".line-1")), l2 = altText(disp.querySelector(".line-2"));
      var a = "";
      if (hasClass(disp, "altitude-constraint-display-at") && l1) a = "at " + l1;
      else if (hasClass(disp, "altitude-constraint-display-atorabove") && l1) a = "at or above " + l1;
      else if (hasClass(disp, "altitude-constraint-display-atorbelow") && l1) a = "at or below " + l1;
      else if (hasClass(disp, "altitude-constraint-display-between") && l1) a = "between " + (l2 || "?") + " and " + l1;
      else if (l1) a = "predicted " + l1;
      if (a) {
        var constraint = a.indexOf("predicted ") !== 0;
        if (constraint && !hasClass(disp, "altitude-constraint-display-cyan")) a += ", not designated";
        if (hasClass(disp, "edited")) a += ", edited";
        if (hasClass(disp, "invalid")) a += ", invalid";
        parts.push(a);
      }
    }

    var sbox = item.querySelector(".flight-plan-fpa-speed-box");
    if (sbox && !hasClass(sbox, "touch-button-hidden")) {
      var fpa = sbox.querySelector(".flight-path-angle-display");
      if (fpa) {
        if (hasClass(fpa, "show-phase")) { var ph = one(fpa, ".phase"); if (ph) parts.push(ph.toLowerCase()); }
        else {
          var num = one(fpa, ".numberunit-num").replace(/−/g, "-");
          if (num && num !== "blank") parts.push("angle " + num + " degrees" + (hasClass(fpa, "edited") ? ", edited" : ""));
        }
      }
      var spd = sbox.querySelector(".speed-constraint-display");
      if (spd) {
        var v = hasClass(spd, "mach") ? one(spd, ".mach").replace(/^M\s*/, "Mach ") : one(spd, ".speed-number-unit .numberunit-num");
        if (v && !/blank/.test(v) && !/NaN/.test(v)) {
          if (!hasClass(spd, "mach")) v += " knots";
          var kind = hasClass(spd, "above") ? "at or above " : hasClass(spd, "below") ? "at or below " : "at ";
          parts.push(kind + v + (hasClass(spd, "edited") ? ", edited" : "") + (hasClass(spd, "invalid") ? ", invalid" : ""));
        }
      }
    }
    return parts.join(", ");
  }

  // Order: an open popup's buttons (what the pilot just opened), the page's own, the radio bar, the
  // bottom bar. A flight-plan leg is ONE row (its waypoint button, labelled with the whole leg);
  // its altitude and FPA/speed boxes are appended after every listed button, unlisted, and the
  // leg row names their indices so the window can press them (A / S).
  A.buttons = function () {
    // _stamp marks group titles on page elements; those marks outlive a re-installed agent, so a new
    // install starts at a random value rather than 1 (the EFB agent hid a whole table that way).
    A._top = findTopPopup(); A._stamp = (A._stamp || Math.floor(Math.random() * 1e9) * 1000) + 1;
    var popup = [], main = [], top = [], bottom = [], sub = []; var bs = document.querySelectorAll(BUTTONS);
    for (var i = 0; i < bs.length; i++) {
      var e = bs[i]; if (isHidden(e)) continue;
      if (e.querySelector(BUTTONS)) continue;   // keep the innermost button
      var leg = ancestorWith(e, "flight-plan-leg-list-item");
      if (leg) {
        if (hasClass(e, "flight-plan-leg-button")) {
          var lb = { el: e, label: legSummary(leg), enabled: !isDisabled(e), bar: "", leg: leg };
          (inPopup(e) ? popup : main).push(lb);
        } else if (ancestorWith(e, "flight-plan-altitude-box") || hasClass(e, "flight-plan-fpa-speed-box")) {
          sub.push({ el: e, label: "", enabled: !isDisabled(e), bar: "sub", leg: leg, kind: hasClass(e, "flight-plan-fpa-speed-box") ? "spd" : "alt" });
        }
        continue;
      }
      var base = labelOf(e), t = decorate(e, base); if (!t) continue;
      var b = { el: e, label: t, base: base, enabled: !isDisabled(e), bar: barOf(e) };
      (b.bar === "top" ? top : b.bar === "bottom" ? bottom : inPopup(e) ? popup : main).push(b);
    }
    var out = popup.concat(main, top, bottom, sub);
    for (var k = 0; k < out.length; k++) out[k].i = k;
    for (var s = 0; s < sub.length; s++) {
      for (var m = 0; m < out.length; m++) if (out[m].leg === sub[s].leg && out[m].bar === "") { out[m][sub[s].kind] = sub[s].i; break; }
    }
    A._buttons = out; return out;
  };

  // Text outside buttons, one line per screen row, an open popup's text first. Built from text
  // nodes (see labelOf); a popup's own .gtc-panel-title is the page title and is not repeated.
  A.rows = function () {
    A._top = findTopPopup();
    var items = [];
    // A caption over its value — VNAV Profile's .box of .label "VS REQ" and .data "____ FPM" (measured
    // 2026-09-15) — read "VS REQ: blank FPM" as one item, or the labels line up in one row and the
    // values in the next with nothing tying them together.
    var labels = main().querySelectorAll(".label");
    var pairStamp = (A._stamp || 0) + 0.5;
    for (var li = 0; li < labels.length; li++) {
      var lab = labels[li], val = lab.nextElementSibling;
      if (!val || !/(^|\s)(data|value)(\s|$)|-value(\s|$)/.test(String(val.className))) continue;
      if (isButton(lab) || insideButton(lab) || inDeadView(lab) || !visible(lab) || inChrome(lab)) continue;
      var lt = txt(lab), vt = collect(val, function (p) { return !(p && p !== val && !visible(p)); }, " ");
      if (!lt) continue;
      var lr = lab.getBoundingClientRect();
      lab.__msfsbaPair = pairStamp; val.__msfsbaPair = pairStamp;
      items.push({ t: lt + ": " + (vt || "blank"), x: Math.round(lr.left), y: Math.round(lr.top + lr.height / 2), pop: inPopup(lab) ? 0 : 1, p: lab, o: items.length });
    }
    var w = document.createTreeWalker(main(), NodeFilter.SHOW_TEXT, null, false); var n;
    while ((n = w.nextNode())) {
      var t = clean(n.nodeValue); if (!t) continue;
      var e = n.parentNode; if (!e || e.nodeType !== 1) continue;
      var prev = items.length ? items[items.length - 1] : null;
      if (prev && prev.p === e && n.previousSibling && n.previousSibling.nodeType === 3) { prev.t = clean(prev.t + n.nodeValue); continue; }   // one element's adjacent text nodes run together
      var r = e.getBoundingClientRect(); if (r.width === 0 || r.height === 0) continue;
      if (insideButton(e) || isButton(e)) continue;   // buttons are listed separately
      if (inChrome(e)) continue;       // the title slots, the knob label bar and the two button bars are rendered elsewhere
      if (inDeadView(e) || !visible(e)) continue;   // a page sliding away, or covered by a popup
      if (ancestorWith(e, "flight-plan-leg-list-item")) continue;   // the leg row's own button carries the whole leg
      if (ancestorWith(e, "gtc-panel-title")) continue;
      if (ownedByRowButton(e)) continue;   // a list row's text and volume are spoken on its buttons
      if (ancestorWithProp(e, "__msfsbaGroupTitle", A._stamp)) continue;   // a group title is spoken on its buttons
      if (ancestorWithProp(e, "__msfsbaPair", pairStamp)) continue;   // already read as "caption: value"
      items.push({ t: t, x: Math.round(r.left), y: Math.round(r.top + r.height / 2), pop: inPopup(e) ? 0 : 1, p: e, o: items.length });
    }
    // Same parent keeps document order (x ties inside one element); otherwise row, then x.
    items.sort(function (a, b) { return (a.pop - b.pop) || (Math.round(a.y / 14) - Math.round(b.y / 14)) || (a.p === b.p ? a.o - b.o : a.x - b.x); });
    var lines = []; var cur = null; var cy = -999; var cp = -1; var lastP = null;
    for (var j = 0; j < items.length; j++) {
      if (items[j].pop !== cp || Math.abs(items[j].y - cy) > 14) { if (cur !== null) lines.push(cur); cur = items[j].t; cy = items[j].y; cp = items[j].pop; }
      else cur += (items[j].p === lastP ? " " : " | ") + items[j].t;   // "3000" + <span>FT</span> reads "3000 FT", not "3000 | FT"
      lastP = items[j].p;
    }
    if (cur !== null) lines.push(cur);
    return lines;
  };

  function ancestorWithProp(e, prop, value) {
    var p = e; while (p && p !== document) { if (p[prop] === value) return p; p = p.parentNode; }
    return null;
  }
  // The label bar draws the knob push/hold hints with arrow glyphs ("Push:1–2 Hold:↕").
  A.knobLabel = function () {
    var d = document.querySelector(".label-bar-label.dual-knob");
    var c = document.querySelector(".label-bar-label.center-knob");
    function k(e) { return e ? txt(e).replace(/[←-⇿]/g, "").replace(/:\s*(?=\s|$)/g, "").replace(/\s+/g, " ").trim() : ""; }
    return k(d) + " / " + k(c);
  };

  A.scrape = function () {
    try {
      var rows = ["Page: " + A.title()];
      var bs = A.buttons(); var lastBar = "";   // buttons first: labelling them marks the group titles the text rows then skip
      var text = A.rows(); for (var i = 0; i < text.length; i++) rows.push(text[i]);
      for (var k = 0; k < bs.length; k++) {
        if (bs[k].bar === "sub") break;   // a leg's altitude / FPA-speed boxes: pressed through the leg row, never listed
        if (bs[k].bar !== lastBar) { rows.push(bs[k].bar === "top" ? "Radio bar:" : "Bottom bar:"); lastBar = bs[k].bar; }
        var extra = (bs[k].alt !== undefined || bs[k].spd !== undefined) ? " {alt=" + (bs[k].alt !== undefined ? bs[k].alt : -1) + ";spd=" + (bs[k].spd !== undefined ? bs[k].spd : -1) + "}" : "";
        rows.push("[" + bs[k].label + "]" + (bs[k].enabled ? "" : " (disabled)") + extra);
      }
      rows.push("Knobs: " + A.knobLabel());
      return JSON.stringify({ ok: true, rows: rows });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e), rows: [] }); }
  };

  function clickEl(e) {
    var r = e.getBoundingClientRect(); var x = r.left + r.width / 2, y = r.top + r.height / 2;
    function ev(type) { var m = document.createEvent("MouseEvents"); m.initMouseEvent(type, true, true, window, 1, x, y, x, y, false, false, false, false, 0, null); return m; }
    e.dispatchEvent(ev("mousedown")); e.dispatchEvent(ev("mouseup")); e.dispatchEvent(ev("click"));
  }
  A.click = function (i) { var b = A._buttons[i]; if (!b || !b.el || isHidden(b.el)) return "stale"; clickEl(b.el); return "ok"; };
  // By the label the pilot hears OR the button's own text: callers name buttons by their text
  // ("Home", "Aircraft Systems", a synoptic, a keyboard key), and the spoken label may carry ", on",
  // " tab" or a list row's text in front. The exact spoken label wins over a text-only match.
  A.press = function (label) {
    var bs = A.buttons(), byText = null;
    for (var i = 0; i < bs.length; i++) {
      if (!bs[i].enabled) continue;
      if (bs[i].label === label) { clickEl(bs[i].el); return "ok"; }
      if (!byText && bs[i].base === label) byText = bs[i];
    }
    if (byText) { clickEl(byText.el); return "ok"; }
    return "none";
  };
  A.knob = function (name) { var ev = "H:AS3000_TSC_Vertical_" + gtcIndex() + "_" + name; SimVar.SetSimVarValue(ev, "number", 1); return ev; };
  A.state = function () { return JSON.stringify({ title: A.title(), knobs: A.knobLabel(), gtc: gtcIndex() }); };

  window.__MSFSBA_GTC = A; window.__MSFSBA_DISP = A;
  return "MSFSBA_DISP_INSTALLED";
})();
