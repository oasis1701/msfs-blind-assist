// coherent-c680-efb-agent.js — MSFSBA in-page agent for the Skyward Citation Sovereign+ vendor
// EFB (the "VCockpit - EFB" view). Installed by CoherentDisplayClient through Runtime.evaluate
// (no injection) and read through the shared __MSFSBA_DISP.scrape() contract.
//
// Measured 2026-09-09/10: pages are #menu-bar .menu-item[data-page] (home, ground, flight,
// checklists, settings) and #page-container .page.active; each page's tabs are .sub-nav-item
// (the active one carries "active"); the Services cards are .ground-service-card with an
// input[type=checkbox]; Settings rows are .settings-option-row with a .settings-option-title
// and either a checkbox or .settings-selector-btn choices (the chosen one carries "active").
// A page or tab is reached by a mousedown/mouseup/click triple; a checkbox or selector button
// by .click() (proven: chocks and Meter Overlay flipped their L:vars).
//
// Measured 2026-09-15 (reading order and the page-specific layouts):
// - Pages lay blocks SIDE BY SIDE (Payload: FUEL QTY | WEIGHT & BALANCE; Hydraulics: RESERVOIR |
//   PRESSURE), so reading strictly by screen row interleaved the columns ("Payload | 0 kg | 1964 |
//   1964"). Items are ordered by COLUMN first: a container whose visible children include two
//   tall (>= 60 px) blocks that sit beside each other is a column container, and each item's key
//   is its path of column indices; absolutely positioned children (the Access panels over the
//   aircraft picture) are placed by position instead.
// - Text is gathered from TEXT NODES: a leaf-element walk lost "relevant key is shown." after an
//   inline element and could not see a card's own prose.
// - Home: .hw widgets (.hw-head title; .hw-stat = value ABOVE label) read "FLIGHT: GS kts 0, …";
//   .cover-alert rows ("Wheel Chocks installed") carry icon-only Remove cover / Dismiss buttons
//   named only by data-tip, which were invisible to a text walk — a pilot could not remove the
//   pitot covers from the Home page.
// - Services > Access: .access-panel = title + input[type=checkbox] (the door's L:var, checked =
//   open) + a button to the linked Services tab.
// - Services > O2 / N2: .gas-gauge-card = label + an SVG dial whose reading is the text element
//   with an id ending "-value-svg"; the dial's tick labels are noise.
// - Flight: the OFP is <pre id="ofp-text"> of ~2,100 lines: one row per line.
// - Checklists: every category is pre-rendered (Abnormal ~9,800 elements, Emergency ~9,300), so a
//   whole-page walk every poll is expensive. That page has its own reader that touches ONE
//   section: sections() lists #cl-nav-sections .cl-nav-section-item (data-cl-scroll-target =
//   the .cl-checklist-group id), goSection() picks one.
// ES5 ONLY (Chromium 49). Returns "MSFSBA_DISP_INSTALLED".
(function () {
  "use strict";
  var A = {}; A.VERSION = 2; A._acts = []; A._clTarget = "";
  // Ownership marks are expando properties left on page elements, and they outlive this agent: a
  // re-install (reconnect, a second window) must not start counting where an earlier install's marks
  // already sit, or read N of the new agent matches read N of the old one and silently hides content
  // (the whole Payload weight table vanished that way, measured 2026-09-15). Start at random.
  var scrapeId = Math.floor(Math.random() * 1e9) * 1000;
  function clean(s) { return String(s || "").replace(/\s+/g, " ").trim(); }
  function txt(e) { return clean(e.innerText || e.textContent || ""); }
  function visible(e) {
    var r = e.getBoundingClientRect(); if (!(r.width > 0 && r.height > 0)) return false;
    try { return window.getComputedStyle(e).visibility !== "hidden"; } catch (x) { return true; }
  }
  function hasClass(e, c) { return !!e && e.nodeType === 1 && (" " + String(e.className) + " ").indexOf(" " + c + " ") >= 0; }
  function clickEl(e) {
    var r = e.getBoundingClientRect(); var x = r.left + r.width / 2, y = r.top + r.height / 2;
    function ev(type) { var m = document.createEvent("MouseEvents"); m.initMouseEvent(type, true, true, window, 1, x, y, x, y, false, false, false, false, 0, null); return m; }
    e.dispatchEvent(ev("mousedown")); e.dispatchEvent(ev("mouseup")); e.dispatchEvent(ev("click"));
  }
  function page() { return document.querySelector("#page-container .page.active") || document.querySelector(".page.active") || document.body; }
  function one(root, sel) { var e = root.querySelector(sel); return e ? txt(e) : ""; }
  // A keypad / keyboard popup ("Set SimBrief User ID": digits, Clear, Cancel, Set ID) sits OUTSIDE
  // the page container; while one is up it is what the pilot must see and press.
  function overlay() {
    var ovs = document.querySelectorAll(".payload-keyboard-overlay, .keyboard-overlay, [class*='keyboard-popup'], [class*='-overlay'], [class*='modal']");
    for (var i = 0; i < ovs.length; i++) if (visible(ovs[i]) && txt(ovs[i])) return ovs[i];
    return null;
  }
  // Buttons here are mostly styled DIVs: anything whose class names a button/btn, plus real <button>s.
  var BTN_CLASS = /(^|\s)([a-z0-9]+-)*(btn|button)(-[a-z0-9-]+)?(\s|$)/i;
  function isButton(e) {
    if (!e || e.nodeType !== 1) return false;
    if (e.tagName === "BUTTON") return true;
    if (e.getAttribute && e.getAttribute("role") === "button") return true;
    var c = String(e.className);
    if (!BTN_CLASS.test(c)) return false;
    if (/selector-btn|menu-item|sub-nav/.test(c)) return false;   // selector choices and nav are handled elsewhere
    return true;
  }
  function insideButton(e) { var p = e; while (p && p !== document) { if (isButton(p)) return true; p = p.parentNode; } return false; }
  function inside(e, sel) { var p = e; while (p && p !== document) { if (p.matches && p.matches(sel)) return p; p = p.parentNode; } return null; }
  // A button's name: its text, else the tooltip / title / aria-label an icon-only button carries.
  function buttonLabel(e) {
    var t = txt(e);
    if (!t) t = clean(e.getAttribute("data-tip") || e.getAttribute("title") || e.getAttribute("aria-label") || "");
    return t;
  }
  function selectedSuffix(e) { return hasClass(e, "active") || hasClass(e, "selected") ? ", selected" : ""; }
  // Text of a subtree from its text nodes; directly adjacent text nodes join with no separator.
  function textOf(root, sep) {
    var out = [], last = null; var w = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null, false); var n;
    while ((n = w.nextNode())) {
      var raw = String(n.nodeValue || ""); if (!clean(raw)) continue;
      var p = n.parentNode; if (p && p.nodeType === 1 && p !== root && !visible(p)) continue;
      if (p === last && out.length && n.previousSibling && n.previousSibling.nodeType === 3) out[out.length - 1] += raw;
      else out.push(raw);
      last = p;
    }
    var parts = []; for (var i = 0; i < out.length; i++) { var t = clean(out[i]); if (t) parts.push(t); }
    return parts.join(sep === undefined ? " " : sep);
  }

  // ---- column-aware ordering -------------------------------------------------------------------
  // Payload's FUEL QTY panel (380x210) and WEIGHT & BALANCE table (600x599) are ABSOLUTELY placed
  // inside a full-screen container, so an absolute child counts when it is a real panel (>= 180 px
  // tall, >= 150 wide); the Access door panels (125 px, scattered over the aircraft picture) do not.
  function isColumnContainer(p) {
    if (p.__msfsbaColId === scrapeId) return p.__msfsbaCol;
    var tall = [], kids = p.children;
    for (var i = 0; i < kids.length; i++) {
      var k = kids[i]; var r = k.getBoundingClientRect(); if (!(r.width >= 40 && r.height >= 60)) continue;
      var pos = ""; try { pos = window.getComputedStyle(k).position; } catch (x) { }
      if ((pos === "absolute" || pos === "fixed") && !(r.height >= 180 && r.width >= 150)) continue;
      tall.push(r);
    }
    var col = false;
    for (var a = 0; a < tall.length && !col; a++)
      for (var b = 0; b < tall.length && !col; b++)
        if (a !== b && tall[a].right <= tall[b].left + 4 && tall[a].top < tall[b].bottom && tall[b].top < tall[a].bottom) col = true;
    p.__msfsbaColId = scrapeId; p.__msfsbaCol = col;
    return col;
  }
  // The key is the path of LEFT EDGES of the column blocks an element sits in, outermost first, so
  // columns read left to right whatever order the markup lists them in.
  function colKey(e, root) {
    var path = [], q = e;
    while (q && q !== root && q.parentNode && q.parentNode !== document) {
      var p = q.parentNode;
      if (p.nodeType === 1 && isColumnContainer(p)) path.unshift(Math.round(q.getBoundingClientRect().left));
      if (p === root) break;
      q = p;
    }
    return path;
  }
  function cmpKey(a, b) {
    var n = Math.min(a.length, b.length);
    for (var i = 0; i < n; i++) if (a[i] !== b[i]) return a[i] - b[i];
    return 0;
  }
  function sameKey(a, b) { if (a.length !== b.length) return false; for (var i = 0; i < a.length; i++) if (a[i] !== b[i]) return false; return true; }

  A.pages = function () {
    var out = []; var mi = document.querySelectorAll("#menu-bar .menu-item");
    for (var i = 0; i < mi.length; i++) out.push({ id: mi[i].getAttribute("data-page") || "", label: txt(mi[i]), active: hasClass(mi[i], "active") });
    return JSON.stringify(out);
  };
  A.goPage = function (id) {
    var mi = document.querySelectorAll("#menu-bar .menu-item");
    for (var i = 0; i < mi.length; i++) if (mi[i].getAttribute("data-page") === id) { clickEl(mi[i]); return "ok"; }
    return "none";
  };
  A.tabs = function () {
    var out = []; var sn = page().querySelectorAll(".sub-nav-item");
    for (var i = 0; i < sn.length; i++) if (visible(sn[i])) out.push({ label: txt(sn[i]), active: hasClass(sn[i], "active") });
    return JSON.stringify(out);
  };
  A.goTab = function (label) {
    var sn = page().querySelectorAll(".sub-nav-item");
    for (var i = 0; i < sn.length; i++) if (visible(sn[i]) && txt(sn[i]) === label) { clickEl(sn[i]); return "ok"; }
    return "none";
  };

  // ---- checklists --------------------------------------------------------------------------------
  function isChecklists(p) { return p && p.id === "checklists-page"; }
  function navSections() {
    var out = [], ns = document.querySelectorAll("#cl-nav-sections .cl-nav-section-item");
    for (var i = 0; i < ns.length; i++) {
      var id = ns[i].getAttribute("data-cl-scroll-target") || ""; if (!id || !document.getElementById(id)) continue;
      out.push({ id: id, label: txt(ns[i]), active: hasClass(ns[i], "active"), el: ns[i] });
    }
    return out;
  }
  function currentSection(secs) {
    for (var i = 0; i < secs.length; i++) if (secs[i].id === A._clTarget) return i;
    for (var j = 0; j < secs.length; j++) if (secs[j].active) return j;
    return secs.length ? 0 : -1;
  }
  A.sections = function () {
    if (!isChecklists(page())) return "[]";
    var secs = navSections(), cur = currentSection(secs), out = [];
    for (var i = 0; i < secs.length; i++) out.push({ id: secs[i].id, label: secs[i].label, active: i === cur });
    return JSON.stringify(out);
  };
  A.goSection = function (id) {
    var secs = navSections();
    for (var i = 0; i < secs.length; i++) if (secs[i].id === id) { A._clTarget = id; clickEl(secs[i].el); return "ok"; }
    return "none";
  };
  // Checklist text is read from textContent: the vendor draws some content only when its category is
  // showing, and a visibility test would drop a header built of SVG message boxes. The vendor's
  // renderer also leaves the literal text "undefined" where it lost an item (Abnormal "This CAS
  // message indicates:" followed by three "undefined" lines, measured 2026-09-15) — said as missing.
  var MISSING = "(item missing from the aircraft's EFB)";
  function clText(e) { var t = clean(e ? e.textContent : ""); return t === "undefined" ? MISSING : t; }
  function checklistRows(node, rows) {
    var kids = node.children;
    for (var i = 0; i < kids.length; i++) {
      var c = kids[i];
      if (hasClass(c, "checklist-item")) {
        var num = clText(c.querySelector(".checklist-number")), action = clText(c.querySelector(".checklist-action")), state = clText(c.querySelector(".checklist-state"));
        if (hasClass(c, "checklist-bullet")) { rows.push("• " + action); continue; }
        var row = (num ? num + " " : "") + action;
        if (state && !hasClass(c, "checklist-no-state")) row += ": " + state;
        if (hasClass(c, "checklist-memory")) row += ", memory item";
        rows.push(row);
      } else if (hasClass(c, "checklist-division")) {
        var lv = /checklist-division-(\d)/.exec(String(c.className)); var level = lv ? parseInt(lv[1], 10) : 1;
        rows.push((level > 1 ? "Condition, level " + level + ": " : "Condition: ") + clText(c.querySelector(".division-text")));
      } else if (hasClass(c, "checklist-tag-row")) {
        rows.push(clText(c) + ":");   // CAUTION / WARNING
      } else if (hasClass(c, "checklist-divider")) {
        // "NOTE" introduces the rows after it; anything else is a heading, often dash-decorated
        // ("-WHEN PASSING 10'000'-", "- - WHEN CLEARED TO LAND - -", "MAXIMUM GLIDE AIRSPEED (KIAS)").
        var dt = clText(c).replace(/^[-–—\s]+|[-–—\s]+$/g, "");
        if (/^(NOTES?|CAUTION|WARNING)$/i.test(dt)) rows.push(dt + ":"); else if (dt) rows.push("Heading: " + dt);
      } else if (hasClass(c, "checklist-subtitle")) {
        rows.push("Heading: " + clText(c));
      } else if (hasClass(c, "checklist-plain")) {
        var pt = clText(c); if (pt) rows.push(pt);
      } else if (hasClass(c, "checklist-table-container")) {
        var trs = c.querySelectorAll("tr");
        for (var t = 0; t < trs.length; t++) {
          var cells = [], cs = trs[t].children;
          for (var k = 0; k < cs.length; k++) { var ct = clText(cs[k]); if (ct) cells.push(ct); }
          if (cells.length) rows.push(cells.join(" | "));
        }
      } else if (c.children.length) {
        checklistRows(c, rows);   // memory groups, division content, anything nesting the above
      } else {
        var lt = clText(c); if (lt) rows.push(lt);
      }
    }
  }
  function scrapeChecklists(p, tab) {
    var rows = ["Page: Checklists" + (tab ? ", " + tab : "")], secs = navSections(), cur = currentSection(secs);
    if (cur < 0) { rows.push("No checklist sections on this tab"); A._acts = [null, null]; return JSON.stringify({ ok: true, rows: rows }); }
    var g = groupRows(document.getElementById(secs[cur].id), secs[cur].label);
    rows.push("Section: " + g.title + " (" + (cur + 1) + " of " + secs.length + ")");
    for (var r = 0; r < g.rows.length; r++) rows.push(g.rows[r]);
    var acts = []; for (var i = 0; i < rows.length; i++) acts.push(null);
    A._acts = acts;
    return JSON.stringify({ ok: true, rows: rows });
  }
  // One checklist: its header ("title, severity, code") and body rows. Header severity is
  // cl-cas-{warning,caution,advisory} with two variants (measured over all 354 checklists,
  // 2026-09-15): "-pfd" is drawn INVERSE (a filled block) — the PFD's own annunciation (AP, ALT,
  // PULL UP, RWY TOO SHORT), not a CAS list message — and "-clr" is a title built from several
  // message boxes, some drawn as SVG text. Plain cl-cas-header (the Emergency procedures) has none.
  function groupRows(group, fallbackTitle) {
    var head = group.querySelector(".cl-group-header");
    var titleEl = head ? head.querySelector(".cl-group-header-title") || head : null;
    var title = titleEl ? clText(titleEl) : (fallbackTitle || "");
    var sev = head ? /cl-cas-(warning|caution|advisory)(-pfd|-clr)?(\s|$)/.exec(String(head.className)) : null;
    var kind = sev ? (sev[2] === "-pfd" ? "PFD " + sev[1] : sev[1]) : "";
    var code = head ? one(head, ".cl-group-header-tab") : "";
    var rows = [];
    var body = group.querySelector(".cl-group-body"); if (body) checklistRows(body, rows);
    return { title: title + (kind ? ", " + kind : "") + (code ? ", " + code : ""), rows: rows };
  }
  // Diagnostic: any checklist by its group id, whichever category is showing (audits every section
  // without scrolling the EFB).
  A.readChecklist = function (id) {
    var g = document.getElementById(id); if (!g) return "none";
    var r = groupRows(g, ""); return JSON.stringify({ title: r.title, rows: r.rows });
  };

  // ---- popups ----------------------------------------------------------------------------------
  function scrapeOverlay(ov) {
    var rows = []; var acts = [null];
    var head = ov.querySelector("[class*='header'], [class*='title']"); rows.push("Popup: " + (head ? txt(head) : txt(ov).slice(0, 40)));
    var disp = ov.querySelector("[class*='display']"); if (disp) { rows.push("Entry: " + (txt(disp) || "(empty)")); acts.push(null); }
    var items = []; var els = ov.querySelectorAll("*");
    for (var i = 0; i < els.length; i++) {
      var e = els[i]; if (!visible(e) || !isButton(e)) continue;
      if (e.querySelector && Array.prototype.some.call(e.querySelectorAll("*"), isButton)) continue;
      var t = buttonLabel(e); if (!t) continue;
      var r = e.getBoundingClientRect(); items.push({ t: "[" + t + "]", x: Math.round(r.left), y: Math.round(r.top + r.height / 2), act: { el: e, kind: "button" } });
    }
    items.sort(function (a, b) { return (Math.round(a.y / 16) - Math.round(b.y / 16)) || (a.x - b.x); });
    for (var j = 0; j < items.length; j++) { rows.push(items[j].t); acts.push(items[j].act); }
    A._acts = acts;
    return JSON.stringify({ ok: true, rows: rows });
  }

  // ---- owned blocks: page-specific layouts read as whole units ----------------------------------
  // Each builder returns { el, rows: [ {t, act} ] }; the block is placed in reading order by its
  // element, and nothing inside it is read again by the generic walk.
  function btnRow(el, label) { return { t: "[" + label + "]", act: { el: el, kind: "button" } }; }
  var STAT_NAMES = { GS: "ground speed", ALT: "altitude", HDG: "heading", FUEL: "fuel" };
  function blocksOf(p) {
    var blocks = [];
    function own(el, rows) { el.__msfsbaOwned = scrapeId; if (rows.length) blocks.push({ el: el, rows: rows }); }
    var i, q;
    // Services cards: a checkbox card reads "Title: on/off"; a card of selector choices (Settings >
    // ATC network) reads its prose then one row per choice; any other card its title then buttons.
    var cards = p.querySelectorAll(".ground-service-card");
    for (i = 0; i < cards.length; i++) {
      var card = cards[i]; if (!visible(card)) continue;
      var rows = []; var cb = card.querySelector("input[type=checkbox]");
      var titleEl = card.querySelector(".ground-service-title, .card-title, h3, h4");
      var sel = card.querySelectorAll(".settings-selector-btn");
      if (cb && !sel.length) rows.push({ t: (titleEl ? txt(titleEl) : textOf(card).slice(0, 60)) + ": " + (cb.checked ? "on" : "off"), act: { el: cb, kind: "checkbox" } });
      else {
        var heading = titleEl ? txt(titleEl) : "";
        if (heading) rows.push({ t: heading, act: null });
        var desc = card.querySelectorAll(".ground-service-description");
        for (q = 0; q < desc.length; q++) if (visible(desc[q])) { var dt = textOf(desc[q]); if (dt) rows.push({ t: dt, act: null }); }
        for (q = 0; q < sel.length; q++) if (visible(sel[q])) rows.push(btnRow(sel[q], txt(sel[q]) + selectedSuffix(sel[q])));
        var cbs = card.querySelectorAll("*");
        for (q = 0; q < cbs.length; q++) {
          var be = cbs[q]; if (!visible(be) || !isButton(be) || hasClass(be, "settings-selector-btn")) continue;   // selector choices were listed above (they are real <button>s)
          if (Array.prototype.some.call(be.querySelectorAll("*"), isButton)) continue;
          var bl = buttonLabel(be); if (bl) rows.push(btnRow(be, bl + selectedSuffix(be)));
        }
        if (!rows.length) { var ct = textOf(card); if (ct) rows.push({ t: ct, act: null }); }
      }
      own(card, rows);
    }
    // Settings rows.
    var srows = p.querySelectorAll(".settings-option-row");
    for (i = 0; i < srows.length; i++) {
      var row = srows[i]; if (!visible(row) || row.__msfsbaOwned === scrapeId || inside(row, ".ground-service-card")) continue;
      var t = row.querySelector(".settings-option-title"); var rn = t ? txt(t) : textOf(row).slice(0, 40);
      var rcb = row.querySelector("input[type=checkbox]"); var btns = row.querySelectorAll(".settings-selector-btn");
      var rr = [];
      if (rcb) rr.push({ t: rn + ": " + (rcb.checked ? "on" : "off"), act: { el: rcb, kind: "checkbox" } });
      else if (btns.length) {
        var opts = [], activeIdx = -1;
        for (var b = 0; b < btns.length; b++) { opts.push(txt(btns[b]) + (hasClass(btns[b], "active") ? "*" : "")); if (hasClass(btns[b], "active")) activeIdx = b; }
        rr.push({ t: rn + ": " + opts.join(" / "), act: { el: btns[(activeIdx + 1) % btns.length], kind: "selector" } });
      }
      else rr.push({ t: rn + ": " + textOf(row).replace(rn, "").trim(), act: null });
      own(row, rr);
    }
    // Home widgets. The map widget is a canvas with icon zoom buttons: nothing to read.
    var hws = p.querySelectorAll(".hw");
    for (i = 0; i < hws.length; i++) {
      var hw = hws[i]; if (!visible(hw)) continue;
      if (hasClass(hw, "hw-map")) { own(hw, []); continue; }
      var head = one(hw, ".hw-head"), parts = [], stats = hw.querySelectorAll(".hw-stat");
      // The stat labels are "GS kts" / "ALT ft" / "HDG" / "FUEL kg", uppercased by CSS: spoken as words.
      if (stats.length) for (q = 0; q < stats.length; q++) {
        var lblEl = stats[q].querySelector(".hw-stat-lbl"), words = clean(lblEl ? lblEl.textContent : "").split(" ");
        var name = STAT_NAMES[String(words[0]).toUpperCase()] || words[0];
        parts.push(name + " " + one(stats[q], ".hw-stat-val") + (words.length > 1 ? " " + words.slice(1).join(" ").toLowerCase() : ""));
      }
      else {
        var kids = hw.children;
        for (q = 0; q < kids.length; q++) if (!hasClass(kids[q], "hw-head") && visible(kids[q])) { var kt = textOf(kids[q]); if (kt) parts.push(kt); }
      }
      if (hw.id === "hw-route") {
        var dep = one(hw, "#departure"), arr = one(hw, "#arrival"), rem = one(hw, "#home-tl-remaining");
        parts = [dep + " to " + arr]; if (rem) parts.push(rem);
      }
      own(hw, [{ t: (head ? head.charAt(0) + head.slice(1).toLowerCase() + ": " : "") + parts.join(", "), act: null }]);
    }
    // Home cover alerts: the alert, then its icon-only Remove cover / Dismiss buttons.
    var alerts = p.querySelectorAll(".cover-alert");
    for (i = 0; i < alerts.length; i++) {
      var al = alerts[i]; if (!visible(al)) continue;
      var lab = one(al, ".cover-alert-label"), what = lab.replace(/\s+installed$/i, "");
      var ar = [{ t: lab, act: null }];
      var rm = al.querySelector(".cover-alert-btn-remove"), ds = al.querySelector(".cover-alert-btn-dismiss");
      if (rm && visible(rm)) ar.push(btnRow(rm, "Remove " + what));
      if (ds && visible(ds)) ar.push(btnRow(ds, "Dismiss " + what + " alert"));
      own(al, ar);
    }
    // Services > Access panels (placed over the aircraft picture).
    var aps = p.querySelectorAll(".access-panel");
    for (i = 0; i < aps.length; i++) {
      var ap = aps[i]; if (!visible(ap)) continue;
      var at = one(ap, ".access-panel-title"), acb = ap.querySelector("input[type=checkbox]"), abtn = ap.querySelector(".access-panel-button");
      var apr = [];
      if (acb) apr.push({ t: at + ": " + (acb.checked ? "open" : "closed"), act: { el: acb, kind: "checkbox" } });
      if (abtn && visible(abtn)) apr.push(btnRow(abtn, at + ": go to " + txt(abtn)));
      own(ap, apr);
    }
    // Services > O2 / N2 gauges.
    var gs = p.querySelectorAll(".gas-gauge-card");
    for (i = 0; i < gs.length; i++) {
      var g = gs[i]; if (!visible(g)) continue;
      var gl = one(g, ".gas-gauge-label"), val = g.querySelector("[id$='-value-svg']");
      var gr = [{ t: gl.charAt(0) + gl.slice(1).toLowerCase() + ": " + (val ? clean(val.textContent) + " PSI" : "no reading"), act: null }];
      var fb = g.querySelectorAll("button");
      for (q = 0; q < fb.length; q++) if (visible(fb[q])) gr.push(btnRow(fb[q], buttonLabel(fb[q])));
      own(g, gr);
    }
    // Services > Payload (measured 2026-09-15): the FUEL QTY panel's two tank figures carry no text
    // label, only ids; a payload station (Cargo Bay) is label + unit + a value button; the SimBrief
    // import chips sit either side of a bare "+".
    var fq = p.querySelector(".fuel-qty-panel");
    if (fq && visible(fq)) {
      var unit = one(fq, "#fuel-qty-unit-label");
      own(fq, [{ t: "Fuel quantity: total " + one(fq, "#fuel-qty-total") + " " + unit + ", left " + one(fq, "#fuel-qty-left") + ", right " + one(fq, "#fuel-qty-right"), act: null }]);
    }
    var stations = p.querySelectorAll("[class*='payload-station-']");
    for (i = 0; i < stations.length; i++) {
      var st = stations[i]; if (!visible(st) || !/(^|\s)payload-station-\d+(\s|$)/.test(String(st.className))) continue;
      var sb = st.querySelector("button");
      var sl = one(st, ".payload-station-label"), su = one(st, ".payload-unit-display");
      own(st, sb ? [btnRow(sb, sl + ": " + txt(sb) + (su ? " " + su : ""))] : [{ t: sl + ": " + textOf(st), act: null }]);
    }
    var imp = p.querySelector(".simbrief-import-compound");
    if (imp && visible(imp)) {
      var ir = [], chips = imp.querySelectorAll("button");
      for (q = 0; q < chips.length; q++) if (visible(chips[q])) ir.push(btnRow(chips[q], "SimBrief Import: " + txt(chips[q])));
      own(imp, ir);
    }
    // Any table (the Payload WEIGHT & BALANCE table): one row per table row, cells joined, so a
    // cell drawn taller than its neighbours ("Gross Weight" over "31582 lbs") stays with its row.
    var tables = p.querySelectorAll("table");
    for (i = 0; i < tables.length; i++) {
      var tb = tables[i]; if (!visible(tb) || tb.__msfsbaOwned === scrapeId) continue;
      var tr = [], trs = tb.querySelectorAll("tr");
      for (q = 0; q < trs.length; q++) {
        if (!visible(trs[q])) continue;
        if (Array.prototype.some.call(trs[q].querySelectorAll("*"), function (x) { return x.__msfsbaOwned === scrapeId; })) continue;   // a row holding a block another reader owns (the SimBrief import chips)
        var cells = [], tds = trs[q].children;
        for (var z = 0; z < tds.length; z++) { var cellText = textOf(tds[z]); if (cellText) cells.push(cellText); }
        if (cells.length) tr.push({ t: cells.join(" | "), act: null });
      }
      own(tb, tr);
    }
    // Services > Electrical: each battery is a title, a "24V / LEFT BATTERY" drawing, a "+" terminal
    // (data-lvar L:BAT_DISC_*; pointer-events:none while it cannot be used), the status and voltage.
    var bats = p.querySelectorAll(".battery-system");
    for (i = 0; i < bats.length; i++) {
      var bt = bats[i]; if (!visible(bt)) continue;
      var bn = one(bt, ".battery-title"), status = one(bt, ".connection-status"), volts = one(bt, ".voltage-display"), rating = one(bt, ".battery-labels");
      var br = [{ t: bn.charAt(0) + bn.slice(1).toLowerCase() + ": " + [rating, status.toLowerCase(), volts].filter(function (x) { return !!x; }).join(", "), act: null }];
      var term = bt.querySelector(".terminal");
      var usable = term && (window.getComputedStyle(term).pointerEvents !== "none");
      if (term && usable) br.push(btnRow(term, bn.charAt(0) + bn.slice(1).toLowerCase() + ": " + (/disconnect/i.test(status) ? "connect" : "disconnect") + " the terminal"));
      own(bt, br);
    }
    // Preformatted text (the Flight page OFP): one row per non-blank line; rules of dashes dropped.
    var pres = p.querySelectorAll("pre");
    for (i = 0; i < pres.length; i++) {
      var pre = pres[i]; if (!visible(pre)) continue;
      var lines = String(pre.textContent || "").split(/\r?\n/), pr = [];
      for (q = 0; q < lines.length; q++) { var ln = clean(lines[q]); if (ln && !/^[-=_\s]+$/.test(ln)) pr.push({ t: ln, act: null }); }
      own(pre, pr);
    }
    return blocks;
  }
  function hasOwnText(e) {
    for (var c = e.firstChild; c; c = c.nextSibling) if (c.nodeType === 3 && clean(c.nodeValue)) return true;
    return false;
  }
  function isInline(e) { try { return window.getComputedStyle(e).display === "inline"; } catch (x) { return false; } }
  // The element to read whole when `pe` is part of running prose: a block that has text of its own
  // AND inline element children. Null for ordinary single-element text.
  function paragraphOf(pe) {
    var block = pe;
    if (isInline(pe) && pe.parentNode && pe.parentNode.nodeType === 1 && hasOwnText(pe.parentNode)) block = pe.parentNode;
    if (!hasOwnText(block) || isButton(block)) return null;
    for (var c = block.firstElementChild; c; c = c.nextElementSibling) if (isInline(c) && clean(c.textContent)) return block;
    return null;
  }
  function owned(e) { var p = e; while (p && p !== document) { if (p.__msfsbaOwned === scrapeId) return true; p = p.parentNode; } return false; }

  A.scrape = function () {
    try {
      scrapeId++;
      var ov = overlay(); if (ov) return scrapeOverlay(ov);
      var p = page();
      var pageName = ""; var mi = document.querySelectorAll("#menu-bar .menu-item.active"); if (mi.length) pageName = txt(mi[0]);
      var tab = ""; var sn = p.querySelectorAll(".sub-nav-item"); for (var s = 0; s < sn.length; s++) if (hasClass(sn[s], "active")) tab = txt(sn[s]);
      if (isChecklists(p)) return scrapeChecklists(p, tab);

      var items = [];   // { key, x, y, t, act, p(parent for same-line joins) } or { key, x, y, block }
      var blocks = blocksOf(p);
      // Home's cover alerts live in their own absolutely placed column; listed together right after
      // the clock and date, since each is something to clear before flight.
      var hh = document.getElementById("home-header"), alertY = hh ? Math.round(hh.getBoundingClientRect().bottom) + 1 : 0;
      for (var b = 0; b < blocks.length; b++) {
        var br = blocks[b].el.getBoundingClientRect();
        var isAlert = hasClass(blocks[b].el, "cover-alert");
        items.push({ key: isAlert ? [] : colKey(blocks[b].el, p), x: isAlert ? 0 : Math.round(br.left), y: isAlert ? alertY : Math.round(br.top), block: blocks[b] });
      }
      // Everything else: buttons as [Label], text in reading order, checkboxes as on/off.
      var els = p.querySelectorAll("*");
      for (var i = 0; i < els.length; i++) {
        var e = els[i];
        if (!isButton(e) && !(e.tagName === "INPUT")) continue;
        if (!visible(e) || owned(e)) continue;
        if (inside(e, ".sub-nav-item") || inside(e, "#menu-bar") || inside(e, ".sub-nav")) continue;
        var r = e.getBoundingClientRect();
        if (e.tagName === "INPUT") {
          if (insideButton(e)) continue;
          var lt = e.type === "checkbox" ? (e.checked ? "on" : "off") : "input " + (e.value || "");
          items.push({ key: colKey(e, p), x: Math.round(r.left), y: Math.round(r.top + r.height / 2), t: lt, act: e.type === "checkbox" ? { el: e, kind: "checkbox" } : null });
          continue;
        }
        if (Array.prototype.some.call(e.querySelectorAll("*"), isButton)) continue;   // keep the innermost button
        var bt = buttonLabel(e); if (!bt) continue;
        items.push({ key: colKey(e, p), x: Math.round(r.left), y: Math.round(r.top + r.height / 2), t: "[" + bt + selectedSuffix(e) + "]", act: { el: e, kind: "button" } });
      }
      var w = document.createTreeWalker(p, NodeFilter.SHOW_TEXT, null, false), n;
      while ((n = w.nextNode())) {
        var raw = clean(n.nodeValue); if (!raw) continue;
        var pe = n.parentNode; if (!pe || pe.nodeType !== 1) continue;
        if (pe.tagName === "SCRIPT" || pe.tagName === "STYLE") continue;
        var prev = items.length ? items[items.length - 1] : null;
        if (prev && prev.p === pe && n.previousSibling && n.previousSibling.nodeType === 3) { prev.t = clean(prev.t + n.nodeValue); continue; }
        if (!visible(pe) || owned(pe) || insideButton(pe)) continue;
        if (inside(pe, ".sub-nav-item") || inside(pe, "#menu-bar") || inside(pe, ".sub-nav")) continue;
        // A paragraph that wraps over several lines with an inline highlight inside ("consists of a
        // <span>2 GAL potable water tank</span>, pump …" on Vanity Service) is read WHOLE, in markup
        // order: split into text nodes, the highlight sorted onto its own line and left a hole.
        var para = paragraphOf(pe);
        if (para) {
          para.__msfsbaOwned = scrapeId;
          var pr = para.getBoundingClientRect();
          items.push({ key: colKey(para, p), x: Math.round(pr.left), y: Math.round(pr.top + 8), t: textOf(para).replace(/ ([,.;:])/g, "$1"), act: null });
          continue;
        }
        var tr = pe.getBoundingClientRect();
        items.push({ key: colKey(pe, p), x: Math.round(tr.left), y: Math.round(tr.top + tr.height / 2), t: raw, act: null, p: pe });
      }
      for (var o = 0; o < items.length; o++) items[o].o = o;   // Chromium 49's sort is not stable: document order breaks ties
      items.sort(function (a, c) { return cmpKey(a.key, c.key) || (Math.round(a.y / 16) - Math.round(c.y / 16)) || (a.x - c.x) || (a.o - c.o); });

      var rows = ["Page: " + pageName + (tab ? ", " + tab : "")], acts = [null];
      var cur = null, cy = -999, curKey = null, curAct = null, curP = null;
      function flush() { if (cur !== null) { rows.push(cur); acts.push(curAct); cur = null; } }
      for (var j = 0; j < items.length; j++) {
        var it = items[j];
        if (it.block) { flush(); for (var k = 0; k < it.block.rows.length; k++) { rows.push(it.block.rows[k].t); acts.push(it.block.rows[k].act); } continue; }
        if (cur === null || it.act || curAct || !sameKey(it.key, curKey) || Math.abs(it.y - cy) > 16) { flush(); cur = it.t; cy = it.y; curKey = it.key; curAct = it.act; curP = it.p; }
        else { cur += (it.p && it.p === curP ? " " : " | ") + it.t; curP = it.p; }
      }
      flush();
      A._acts = acts;
      return JSON.stringify({ ok: true, rows: rows });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e), rows: [] }); }
  };
  // "noaction" for a row that is only text (a checklist step reads "1. APU: START"), "none" when the
  // row's control has gone.
  A.act = function (rowIndex) {
    if (rowIndex < 0 || rowIndex >= A._acts.length || !A._acts[rowIndex]) return "noaction";
    var a = A._acts[rowIndex]; if (!a.el || !document.body.contains(a.el)) return "none";
    if (a.kind === "checkbox" || a.kind === "selector") { try { a.el.click(); } catch (x) { clickEl(a.el); } return "ok"; }
    clickEl(a.el); return "ok";
  };
  // Press a button by its label — inside the popup while one is up (so typed digits reach the keypad), else on the page.
  A.press = function (label) {
    var root = overlay() || page(); var bs = root.querySelectorAll("*");
    for (var i = 0; i < bs.length; i++) if (visible(bs[i]) && isButton(bs[i]) && buttonLabel(bs[i]) === label) { clickEl(bs[i]); return "ok"; }
    return "none";
  };
  A.hasPopup = function () { return overlay() ? "yes" : "no"; };
  window.__MSFSBA_C680_EFB = A; window.__MSFSBA_DISP = A;
  return "MSFSBA_DISP_INSTALLED";
})();
