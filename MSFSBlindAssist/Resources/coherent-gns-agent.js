// coherent-gns-agent.js
//
// MSFSBA in-page agent for the Working Title GNS 530W / 430W (the stock MSFS units the
// Flysimware Learjet 35A carries). Installed once per connection by CoherentDisplayClient
// through the Coherent GT debugger's Runtime.evaluate (NO injection), and polled through
// the shared __MSFSBA_DISP.scrape() contract for a JSON { ok, rows }.
//
// WHY A GNS-SPECIFIC AGENT. The generic row-clustering agent reads every visible text node
// on the screen and groups it by Y. On the GNS that is wrong three ways at once: the
// self-test page is DRAWN OVER the NAV page (both are rendered, one on top), so the two
// merged into one soup; the left radio pane (COM / VLOC / VOR-RAD-DIS / ENR) sits beside
// every page and was stitched into its rows; and the unit symbols are private glyphs in
// Garmin's font that read back as "�". This agent renders ONLY the layer on top - startup,
// self-test, a dialog, or the active page - by DOM structure, translates the glyphs,
// and marks the cursor. The radios and the status footer come after the page, as fixed
// lines, so the page the pilot is working on is read first.
//
// THE UNIT GLYPHS come from GNSNumberUnitDisplay.getUnitChar in WT530B.js (measured against
// the live view 2026-09-08): a Latin-1 letter drawn by the instrument font as a unit
// ligature. They are translated only where the instrument puts them - after a digit, an
// underscore or inside a unit span - so a facility name with a real accented letter is
// left alone.
//
// THE CURSOR is a class, never a colour: `selected` on the focused control (a list row, a
// menu entry, a flight-plan leg, an ident character slot), `selected-white` on the
// self-test buttons and Page Menu items, `selected-cyan` / `selected-yellow` variants,
// and `highlight-active` on a field open for editing. The alpha-num ident entry is five
// character slots with `selected` on the one the small knob will change.
//
// ES5 ONLY (Coherent GT = Chromium 49): var, no arrow funcs, no String.includes,
// no Array.find, no template strings, top-level try/catch.
//
// Installed under window.__MSFSBA_GNS (this object) and window.__MSFSBA_DISP (the shared
// scrape contract). Returns "MSFSBA_DISP_INSTALLED" - the token CoherentDisplayClient
// tests for; anything else leaves the window stuck on "Connecting...".

(function () {
  "use strict";
  var A = {};
  A.VERSION = 1;

  // ------------------------------------------------------------------ helpers

  function classList(el) {
    var cn = el && el.className;
    if (cn && cn.baseVal !== undefined) cn = cn.baseVal;
    return typeof cn === "string" ? cn.split(/\s+/) : [];
  }

  function hasClass(el, name) {
    var parts = classList(el);
    for (var i = 0; i < parts.length; i++) if (parts[i] === name) return true;
    return false;
  }

  function hasClassContaining(el, fragment) {
    var parts = classList(el);
    for (var i = 0; i < parts.length; i++) if (parts[i].indexOf(fragment) >= 0) return true;
    return false;
  }

  // Working Title hides a page, a dialog or a startup screen by adding `hide-element`
  // (display:none !important), and a few things carry `hidden-element` / `hidden`. A
  // rendered-but-covered element (the NAV page under the self-test) is NOT hidden by any
  // of these - which is exactly why this agent picks ONE layer instead of reading all.
  function hidden(el) {
    if (!el || el.nodeType !== 1) return true;
    if (hasClass(el, "hide-element") || hasClass(el, "hidden-element") || hasClass(el, "hidden")) return true;
    try {
      var st = window.getComputedStyle(el);
      if (st.display === "none" || st.visibility === "hidden") return true;
    } catch (e) { /* SVG nodes may throw */ }
    return false;
  }

  function rect(el) {
    try { return el.getBoundingClientRect(); } catch (e) { return { left: 0, top: 0, width: 0, height: 0, right: 0, bottom: 0 }; }
  }

  function visible(el) {
    if (hidden(el)) return false;
    var r = rect(el);
    return r.width > 0 && r.height > 0;
  }

  // Unit ligatures (GNSNumberUnitDisplay.getUnitChar). 'ï' is a MAGNETIC bearing, 'ð' a
  // TRUE one; the time box ends in 'Ä' (a zone marker the instrument draws as a small
  // suffix). Anything not listed passes through untouched.
  var UNITS = {
    "ï": "°",      // degrees, magnetic
    "ð": "°T",     // degrees, true
    "à": " ft",
    "á": " nm",
    "â": " kph",
    "ã": " km",
    "ä": " mph",
    "å": " mi",
    "æ": " m",
    "ç": " mpm",
    "è": " fpm",
    "é": " gal",
    "ì": " kg",
    "í": " L",
    "î": " lb",
    "À": " inHg",
    "Å": " hPa",
    "Á": "°C",
    "Â": "°F",
    "È": " kt",
    "Ä": ""
  };
  var UNIT_CHARS = "ïðàáâãäåæçèéìíîÀÅÁÂÈÄ";
  var UNIT_RE = new RegExp("([0-9_.'°]|^)([" + UNIT_CHARS + "])", "g");

  // A unit glyph is translated where the instrument puts one: after a digit, an
  // underscore placeholder, a decimal point, or standing alone in its own span.
  function units(s) {
    return s.replace(UNIT_RE, function (m, before, g) { return before + (UNITS[g] !== undefined ? UNITS[g] : g); });
  }
  A.units = units;

  // A field the pilot has not filled in is a row of underscores; "blank" is what that
  // means and it is one word. "__:__" and "__._" fold as one blank each (the separators
  // are swallowed), so an unset time does not read as two blanks with a colon between.
  // The run never crosses whitespace: "_____ _____" is TWO blank fields (the from and to
  // legs of the NAV page), and folding across the space read them as one.
  function blanks(s) {
    return s.replace(/_(?:[:.\/-]*_)+/g, "blank").replace(/(^|[^A-Za-z0-9])_(?=[^A-Za-z0-9_]|$)/g, "$1blank");
  }
  A.blanks = blanks;

  function tidy(s) {
    return (s || "").replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
  }

  // Text of an element read node by node and joined with a space between WORDS but not
  // inside a value: the instrument renders "127." and "850" as two spans, "09" ":37" ":44"
  // as three, and a blanket space turns those into "127. 850" and "09 :37 :44".
  function spaced(el) {
    if (!el) return "";
    var parts = [];
    var walker = document.createTreeWalker(el, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, {
      acceptNode: function (n) {
        if (n.nodeType === 1) return hidden(n) ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_SKIP;
        return NodeFilter.FILTER_ACCEPT;
      }
    }, false);
    var n;
    while ((n = walker.nextNode())) {
      var t = tidy(n.nodeValue);
      if (t) parts.push(t);
    }
    var out = "";
    for (var i = 0; i < parts.length; i++) {
      if (!out) { out = parts[i]; continue; }
      var a = out.charAt(out.length - 1);
      var b = parts[i].charAt(0);
      var glue = (/[0-9.:_]/.test(a) && /[0-9.:_]/.test(b)) ||
                 (/[0-9_.']/.test(a) && UNIT_CHARS.indexOf(b) >= 0) ||
                 /[°]/.test(a) || /[°]/.test(b) || /^[)\],]/.test(b) || /[(\[]$/.test(a);
      out += (glue ? "" : " ") + parts[i];
    }
    return out;
  }

  function finish(s) {
    return tidy(blanks(units(s)));
  }

  function textOf(el) {
    return finish(spaced(el));
  }

  function firstVisible(sel, root) {
    var all = (root || document).querySelectorAll(sel);
    for (var i = 0; i < all.length; i++) if (visible(all[i])) return all[i];
    return null;
  }

  function isHeading(el) {
    var t = el.tagName;
    return t === "H1" || t === "H2" || t === "H3" || t === "LABEL";
  }

  // A real title, as opposed to a <label> - a column of labels beside a column of values
  // is a table to zip, a column that opens with an <h3> is a block of its own.
  function isTitle(el) {
    var t = el.tagName;
    return t === "H1" || t === "H2" || t === "H3";
  }

  // The element's OWN text nodes - a block that carries its label as bare text and its
  // value in a child div ("FACILITY, CITY & REGION" + <div>TRENT U KINGDOM</div>).
  function ownText(el) {
    var s = "";
    for (var i = 0; i < el.childNodes.length; i++) if (el.childNodes[i].nodeType === 3) s += el.childNodes[i].nodeValue + " ";
    return finish(s);
  }

  // The leaf fields of a row, one token each, joined with a space - so two adjacent
  // number fields ("EG32" and "075°") never glue into one. A unit span glues to the
  // number before it.
  // A value leaf: no element children, or only the number/unit spans the instrument
  // splits a value into ("028" + unit) - a field, not a container.
  function isValueLeaf(n) {
    for (var i = 0; i < n.children.length; i++) {
      var c = n.children[i];
      if (isDecoration(c)) continue;
      if (c.tagName !== "SPAN") return false;
      if (!(hasClass(c, "numberunit-unit") || hasClass(c, "numberunit-num") || hasClass(c, "smaller") || c.children.length === 0)) return false;
    }
    return true;
  }

  function fields(el) {
    var out = [];
    // A value leaf is one token, its unit span included - so the walk stops there
    // rather than descending into the unit and reading it a second time.
    function visit(n) {
      if (hidden(n) || isDecoration(n)) return;
      if (isValueLeaf(n)) {
        var t = textOf(n);
        if (!t) return;
        if ((hasClass(n, "numberunit-unit") || /^[°]/.test(t)) && out.length) out[out.length - 1] += (/^[°]/.test(t) ? "" : " ") + t;
        else out.push(t);
        return;
      }
      for (var i = 0; i < n.children.length; i++) visit(n.children[i]);
    }
    for (var k = 0; k < el.children.length; k++) visit(el.children[k]);
    if (!out.length) return textOf(el);
    return out.join(" ").replace(/ +°/g, "°");
  }

  var CURSOR_CLASSES = ["selected", "selected-white", "selected-cyan", "selected-yellow", "highlight-active", "highlight-select"];

  function isCursor(el) {
    for (var i = 0; i < CURSOR_CLASSES.length; i++) if (hasClass(el, CURSOR_CLASSES[i])) return true;
    return false;
  }

  // ⚠️ Rendered SIZE, not computed style: a descendant of a display:none page keeps its
  // own computed display, so hidden() alone found a cursor on a page that was not up.
  function containsCursor(el) {
    if (isCursor(el)) return true;
    var all = el.querySelectorAll ? el.querySelectorAll("[class*=selected],[class*=highlight]") : [];
    for (var i = 0; i < all.length; i++) if (isCursor(all[i]) && visible(all[i])) return true;
    return false;
  }

  var CURSOR = "Cursor: ";

  // ---------------------------------------------------------------- the root

  A.root = function () {
    return document.querySelector("wt-gns530") || document.querySelector("wt-gns430");
  };

  A.type = function () {
    return document.querySelector("wt-gns430") ? "430" : "530";
  };

  // ---------------------------------------------------------- atom renderers
  //
  // An ATOM is a block this agent renders as ONE line by its own structure, instead of
  // recursing into it. Everything here was read off the live 530 (tools scratch
  // catalogue, 2026-09-08); the class names are Working Title's.

  // "TRK 178°" - a label above a value.
  function mapDataField(el) {
    var label = el.querySelector(".map-data-field-label");
    var field = el.querySelector(".map-data-field-field");
    return tidy(textOf(label) + " " + textOf(field));
  }

  // The five-slot ident entry. Slots are rendered as the instrument shows them; the one
  // under the cursor is named separately so the pilot knows which character the small
  // knob will change.
  function alphaNum(el) {
    var slots = el.querySelectorAll(".alpha-num-slot");
    var chars = "";
    var cur = -1, curChar = "";
    for (var i = 0; i < slots.length; i++) {
      if (hidden(slots[i])) continue;
      var c = tidy(slots[i].textContent) || "_";
      if (isCursor(slots[i])) { cur = chars.length + 1; curChar = c; }
      chars += c;
    }
    var out = chars;
    if (cur > 0) out += ", cursor on " + (curChar === "_" ? "blank" : curChar) + " at " + cur;
    return out;
  }
  A.alphaNum = alphaNum;

  // "N 52°49.62'"
  function latlon(el) {
    var p = el.querySelector(".latlon-prefix");
    var n = el.querySelector(".latlon-num");
    return tidy(textOf(p) + " " + textOf(n));
  }

  // "09:37:44"
  function timebox(el) {
    return textOf(el);
  }

  // A flight-plan leg: "EGNX 178° 12.4 nm 12.4 nm", the cursor on the name.
  function fplEntry(el) {
    var name = el.querySelector(".waypoint-leg-name");
    var type = el.querySelector(".waypoint-leg-type");
    var cols = el.querySelectorAll(".fpl-entry-col1, .fpl-entry-col2, .fpl-entry-col3");
    var s = textOf(name);
    var ty = textOf(type);
    if (ty) s += " " + ty;
    for (var i = 0; i < cols.length; i++) {
      var c = textOf(cols[i]);
      if (c) s += " " + c;
    }
    return (name && isCursor(name) ? CURSOR : "") + tidy(s);
  }

  // A nearest-list row: airport rows carry two lines (ident brg dis apr / freq rwy).
  function nearestItem(el) {
    return (containsCursor(el) ? CURSOR : "") + fields(el);
  }

  // A menu entry / list item, with the cursor and the disabled state.
  function menuEntry(el) {
    var s = textOf(el);
    var disabled = hasClassContaining(el, "disabled") || el.querySelector(".page-menu-item-disabled") !== null;
    return (containsCursor(el) ? CURSOR : "") + s + (disabled ? " (unavailable)" : "");
  }

  // "RUNWAY: 09-27" - a selector box on the WPT and procedure pages. The small knob OPENS a
  // popup list under the box (a `dialog-box` inside the selector, hidden until then) and
  // scrolls it; ENT picks; the box's own text changes only on ENT. So while the list is
  // open the ITEMS are what the pilot is moving through, and the highlighted one is the
  // cursor line - reading the closed box alone made every knob turn sound like nothing.
  function selector(el) {
    var label = el.querySelector(".waypoint-page-selector-label");
    var sel = el.querySelector(".waypoint-page-selector-selected");
    var head = tidy(textOf(label) + ": " + textOf(sel || el));
    var popout = el.querySelector(".dialog-box");
    if (popout && visible(popout)) {
      var lines = [head + ", choose:"];
      var items = popout.querySelectorAll(".waypoint-page-selector-item");
      for (var i = 0; i < items.length; i++) {
        if (!visible(items[i])) continue;
        lines.push((isCursor(items[i]) || containsCursor(items[i]) ? CURSOR : "") + textOf(items[i]));
      }
      return lines.join("\n");
    }
    return (containsCursor(el) ? CURSOR : "") + head;
  }

  // The GPS status satellite bar graph: one line, not twenty-eight.
  function bargraph(el) {
    var items = el.querySelectorAll(".gps-status-bargraph-item");
    var inUse = [], collecting = [], other = [];
    for (var i = 0; i < items.length; i++) {
      if (hidden(items[i])) continue;
      var prn = tidy((items[i].querySelector(".gps-status-bargraph-prn") || {}).textContent);
      if (!prn) continue;
      if (items[i].querySelector(".gps-status-bargraph-bar.in-use")) inUse.push(prn);
      else if (items[i].querySelector(".gps-status-bargraph-bar.data-collected")) collecting.push(prn);
      else other.push(prn);
    }
    var parts = [];
    if (inUse.length) parts.push(inUse.length + " in use (" + inUse.join(" ") + ")");
    if (collecting.length) parts.push(collecting.length + " collecting (" + collecting.join(" ") + ")");
    if (other.length) parts.push(other.length + " searching (" + other.join(" ") + ")");
    return "Satellites: " + (parts.length ? parts.join(", ") : "none");
  }

  // Two or three columns of equal length (the self-test table, the GPS status
  // HFOM/VFOM/EPU block): zip the rows, so "HFOM 16" reads together instead of the
  // labels column then the values column.
  function columnRows(el) {
    var cols = [];
    var prevRight = -1e9, firstTop = null;
    for (var i = 0; i < el.children.length; i++) {
      var c = el.children[i];
      if (hidden(c)) continue;
      // Only SIDE-BY-SIDE columns zip. A header stacked above a list has two children
      // of equal row count too, and zipping those welded "SETUP 2" onto its first entry.
      // An atom (a lat/lon pair) is a row of its own, never a column of two, and a
      // column that opens with its own heading (POSITION / TIME / ALT on the GPS status
      // page) is a block to read on its own, not a column of a table.
      if (isAtom(c)) return null;
      if (c.children.length && isTitle(c.children[0])) return null;
      var cr = rect(c);
      if (firstTop === null) firstTop = cr.top;
      if (cr.left < prevRight - 4 || Math.abs(cr.top - firstTop) > 12) return null;
      prevRight = cr.right;
      var rows = [];
      for (var k = 0; k < c.children.length; k++) {
        if (hidden(c.children[k])) continue;
        var t = textOf(c.children[k]);
        if (t !== "") rows.push({ t: t, cursor: containsCursor(c.children[k]) });
      }
      cols.push(rows);
    }
    if (cols.length < 2) return null;
    var n = cols[0].length;
    for (var j = 1; j < cols.length; j++) if (cols[j].length !== n) return null;
    if (n === 0) return null;
    var out = [];
    for (var r = 0; r < n; r++) {
      var line = "", cur = false;
      for (var q = 0; q < cols.length; q++) { line += (line ? " " : "") + cols[q][r].t; cur = cur || cols[q][r].cursor; }
      out.push((cur ? CURSOR : "") + line);
    }
    return out;
  }

  function isAtom(el) {
    return hasClass(el, "map-data-field") || hasClass(el, "alpha-num-input") || hasClass(el, "latlon-coord") ||
      hasClass(el, "waypoint-runway-page-databox") ||
      hasClass(el, "fpl-entry") || hasClass(el, "aux-entry") || hasClass(el, "page-menu-entry") ||
      hasClass(el, "menu-list-item") || hasClass(el, "option-dialog-item") || hasClass(el, "waypoint-page-selector") ||
      hasClass(el, "gps-status-bargraph") || hasClass(el, "gps-status-timebox") || hasClass(el, "radio-frequency") ||
      hasClass(el, "waypoint-leg") || hasClass(el, "self-test-control") || hasClass(el, "number-input") ||
      hasClass(el, "tfc-map-toggle") || hasClass(el, "facility-frequency") || hasClass(el, "map-range-legend");
  }

  function atom(el) {
    if (hasClass(el, "map-data-field")) return mapDataField(el);
    if (hasClass(el, "alpha-num-input")) return alphaNum(el);
    if (hasClass(el, "latlon-coord")) return latlon(el);
    if (hasClass(el, "fpl-entry")) return fplEntry(el);
    if (hasClass(el, "aux-entry")) return nearestItem(el);
    if (hasClass(el, "page-menu-entry") || hasClass(el, "menu-list-item") || hasClass(el, "option-dialog-item")) return menuEntry(el);
    if (hasClass(el, "waypoint-page-selector")) return selector(el);
    if (hasClass(el, "gps-status-bargraph")) return bargraph(el);
    if (hasClass(el, "gps-status-timebox")) return timebox(el);
    if (hasClass(el, "map-range-legend")) { var t = textOf(el); return t ? "Map range " + t : ""; }
    var s = textOf(el);
    return (containsCursor(el) ? CURSOR : "") + s;
  }

  // ----------------------------------------------------- the block renderer
  //
  // Walks a block in document order. An atom is one line. A block that starts with a
  // heading renders as "HEADING: the rest" when the rest is short, or as the heading and
  // then its lines. Columns of equal length are zipped. A short, one-row block (a header
  // row of column labels, a label beside a value) is one line. Everything else recurses.

  var ONE_ROW_PX = 26;

  // Things that draw but say nothing: pictures, rules, scroll bars, icons.
  function isDecoration(el) {
    var tag = el.tagName;
    return tag === "CANVAS" || tag === "IMG" || tag === "SVG" || tag === "svg" || tag === "BR" || tag === "HR" ||
      hasClass(el, "scroll-bar") || hasClass(el, "waypoint-page-icon") || hasClass(el, "leg-icon") ||
      hasClass(el, "map-compass-north") || hasClassContaining(el, "-icon");
  }

  // Inline text pieces the instrument splits a value into: "294" + "à", a name with <br>s.
  function isInline(el) {
    var tag = el.tagName;
    return tag === "SPAN" || tag === "B" || tag === "I" || tag === "BR" || tag === "LABEL" && !el.children.length;
  }

  function emit(el, lines) {
    if (hidden(el) || isDecoration(el)) return;
    var r = rect(el);
    if (r.width === 0 && r.height === 0) return;

    if (isAtom(el)) { push(lines, atom(el)); return; }

    var kids = [], allInline = true;
    for (var i = 0; i < el.children.length; i++) {
      var ch = el.children[i];
      if (hidden(ch) || isDecoration(ch)) continue;
      kids.push(ch);
      if (!isInline(ch) || isAtom(ch) || ch.children.length > 0) allInline = false;
    }

    // No block children: the text is the line. This is what keeps "294" and its unit
    // together, and a facility name split by <br>s in one piece.
    if (kids.length === 0 || allInline) { push(lines, (containsCursor(el) ? CURSOR : "") + textOf(el)); return; }

    // The block's own bare text is its label for the children below it.
    var own = ownText(el);
    if (own) {
      var body = [];
      for (var b = 0; b < kids.length; b++) emit(kids[b], body);
      var bodyJoined = body.join(", ");
      if (body.length === 0) push(lines, own);
      else if (body.length <= 3 && bodyJoined.length <= 60 && bodyJoined.indexOf(CURSOR) < 0) push(lines, own + ": " + bodyJoined);
      else { push(lines, own); for (var q = 0; q < body.length; q++) push(lines, body[q]); }
      return;
    }

    // A leading heading owns what follows it, up to the next heading: the Direct-To
    // dialog puts "FPL" and "NRST" side by side, each with its own list.
    if (isHeading(kids[0]) && kids.length > 1) {
      var groups = [];
      for (var k = 0; k < kids.length; k++) {
        if (isHeading(kids[k])) { groups.push({ head: textOf(kids[k]), sub: [] }); continue; }
        emit(kids[k], groups[groups.length - 1].sub);
      }
      for (var g = 0; g < groups.length; g++) {
        var head = groups[g].head, sub = groups[g].sub;
        var joined = sub.join(", ");
        if (sub.length === 0) push(lines, head);
        else if (sub.length <= 3 && joined.length <= 60 && joined.indexOf(CURSOR) < 0) push(lines, head ? head + ": " + joined : joined);
        else { push(lines, head); for (var m = 0; m < sub.length; m++) push(lines, sub[m]); }
      }
      return;
    }

    // The NAV page's from/to legs read as a sentence, not two blanks.
    if (hasClass(el, "arc-map-waypoints")) {
      var from = el.querySelector(".arc-map-waypoints-from"), to = el.querySelector(".arc-map-waypoints-to");
      push(lines, "From " + (from ? textOf(from) : "blank") + " to " + (to ? textOf(to) : "blank"));
      return;
    }

    // Columns of equal length zip into rows.
    if (kids.length >= 2 && kids.length <= 3 && r.height > ONE_ROW_PX * 1.5) {
      var cols = columnRows(el);
      if (cols) { for (var c = 0; c < cols.length; c++) push(lines, cols[c]); return; }
    }

    // A grid of cells (the PROC page's loaded-procedures table: label, value, dash per
    // row) reads row by row: cells sharing a top edge make one line.
    if (kids.length >= 4 && r.height > ONE_ROW_PX) {
      var simple = true;
      for (var s = 0; s < kids.length && simple; s++) if (isAtom(kids[s]) || !isValueLeaf(kids[s])) simple = false;
      if (simple) {
        var rowsByTop = [];
        for (var c2 = 0; c2 < kids.length; c2++) {
          var kr = rect(kids[c2]);
          var t2 = textOf(kids[c2]);
          if (!t2) continue;
          var placed = false;
          for (var rr = 0; rr < rowsByTop.length && !placed; rr++) {
            if (Math.abs(rowsByTop[rr].top - kr.top) <= 8) { rowsByTop[rr].cells.push({ x: kr.left, t: t2, cur: containsCursor(kids[c2]) }); placed = true; }
          }
          if (!placed) rowsByTop.push({ top: kr.top, cells: [{ x: kr.left, t: t2, cur: containsCursor(kids[c2]) }] });
        }
        rowsByTop.sort(function (a, b) { return a.top - b.top; });
        for (var q2 = 0; q2 < rowsByTop.length; q2++) {
          var cells = rowsByTop[q2].cells;
          cells.sort(function (a, b) { return a.x - b.x; });
          var line = "", cur = false;
          for (var w = 0; w < cells.length; w++) { line += (line ? " " : "") + cells[w].t; cur = cur || cells[w].cur; }
          push(lines, (cur ? CURSOR : "") + line);
        }
        return;
      }
    }

    // A block one text row tall is one line (a header row, a label beside its value) -
    // built from its children so an ident entry inside it keeps its cursor.
    if (r.height <= ONE_ROW_PX) {
      var pieces = [];
      for (var p = 0; p < kids.length; p++) emit(kids[p], pieces);
      var t = pieces.join(" ");
      if (t) push(lines, (containsCursor(el) && t.indexOf(CURSOR) < 0 ? CURSOR : "") + t);
      return;
    }

    for (var j = 0; j < kids.length; j++) emit(kids[j], lines);
  }

  // An atom may hand back several lines (an open selector list). A cursor mark that a
  // one-row join left mid-line is hoisted to the front, where cursorLine() looks for it.
  function push(lines, s) {
    var parts = String(s || "").split("\n");
    for (var i = 0; i < parts.length; i++) {
      var t = tidy(parts[i]);
      if (!t) continue;
      if (t === CURSOR.replace(/\s+$/, "")) continue;
      var at = t.indexOf(CURSOR);
      if (at > 0) t = CURSOR + tidy(t.substring(0, at) + " " + t.substring(at + CURSOR.length));
      lines.push(t);
    }
  }

  // ------------------------------------------------------------- the layers

  // The Working Title boot sequence: logo, copyright, versions, databases, the map
  // disclaimer, then a confirm screen that waits for ENT.
  A.startup = function () {
    var root = A.root();
    var s = root && root.querySelector(".startup-screen");
    if (!s || hidden(s)) return null;
    var lines = [];
    var kids = s.children;
    var anyText = false;
    for (var i = 0; i < kids.length; i++) {
      if (hidden(kids[i])) continue;
      if (kids[i].tagName === "IMG") { lines.push("Garmin"); continue; }
      var t = textOf(kids[i]);
      if (t) { lines.push(t); anyText = true; }
    }
    var confirm = s.querySelector(".startup-screen-confirm");
    var waiting = confirm && !hidden(confirm);
    return { context: waiting ? "Starting up. Press Enter to continue." : "Starting up, please wait.", lines: lines, waiting: !!waiting, anyText: anyText };
  };

  // The self-test page: the pilot turns the LARGE knob to OK? and presses ENT. Nothing
  // else on the unit answers until then.
  A.selfTest = function () {
    var root = A.root();
    var s = root && root.querySelector(".self-test");
    if (!s || hidden(s)) return null;
    var lines = [];
    var left = s.querySelector(".self-test-left");
    if (left) {
      // The labels column and the values column are the children of the left half.
      var cols = columnRows(left);
      if (cols) { for (var c = 0; c < cols.length; c++) push(lines, cols[c]); }
      else emit(left, lines);
    }
    var right = s.querySelector(".self-test-right");
    if (right) {
      for (var k = 0; k < right.children.length; k++) {
        var el = right.children[k];
        if (hidden(el)) continue;
        if (isHeading(el)) {
          var val = el.nextElementSibling;
          push(lines, textOf(el) + ": " + (val ? textOf(val) : ""));
          k++;
          continue;
        }
        if (hasClass(el, "self-test-field")) continue;
        emit(el, lines);
      }
    }
    // A fresh boot parks the cursor on OK? already; a cursor moved elsewhere (or left
    // there by an earlier session) needs the LARGE knob, the only one this page answers.
    var onOk = false;
    var ok = s.querySelector(".ok-button .selected-white, .ok-button.selected-white");
    if (ok && visible(ok)) onOk = true;
    return {
      context: onOk ? "Self-test. Cursor on OK?: press Enter." : "Self-test. Turn the large knob to OK? and press Enter.",
      lines: lines
    };
  };

  // A dialog drawn over the page: Page Menu, MESSAGES, an option list, the Direct-To
  // waypoint dialog, a confirm prompt. The first visible one, top-most last.
  A.dialog = function () {
    var root = A.root();
    if (!root) return null;
    var all = root.querySelectorAll(".dialog, .dto-dialog");
    var d = null;
    for (var i = 0; i < all.length; i++) if (visible(all[i])) d = all[i];
    if (!d) return null;
    var lines = [];
    var box = d.querySelector(".dialog-box-inner") || d;
    emit(box, lines);
    var title = "";
    var h = firstVisible("h1, h2, h3, .message-dialog-title, .obs-dialog-title", box);
    if (h) title = textOf(h);
    if (!title && lines.length) title = lines[0];
    // The title is the context line; the same words as line one would be read twice.
    if (lines.length && lines[0] === title) lines.shift();
    if (/^SELECT ?WAYPOINT$/.test(title)) title = "Direct To, select waypoint";
    return { context: title || "Dialog", lines: lines };
  };

  // The active page inside the right pane, with its group and number from the footer.
  A.pageInfo = function () {
    var root = A.root();
    var label = root && root.querySelector(".page-label");
    var boxes = root ? root.querySelectorAll(".pager .pager-box") : [];
    var active = -1;
    for (var i = 0; i < boxes.length; i++) if (hasClass(boxes[i], "active")) active = i;
    return { group: label ? tidy(label.textContent) : "", page: active + 1, pages: boxes.length };
  };

  A.activePage = function () {
    var root = A.root();
    var right = root && root.querySelector(".mainscreen-right");
    if (!right) return null;
    // The NRST pages are an `.aux-page` with no `.page` on it; the innermost visible one
    // is the page on top.
    var pages = right.querySelectorAll(".page, .aux-page");
    var page = null;
    for (var i = 0; i < pages.length; i++) if (visible(pages[i])) page = pages[i];
    return page;
  };

  // Pages that carry no header of their own get a name in the context line; a page
  // with a header reads it as its first line instead, once.
  A.page = function () {
    var info = A.pageInfo();
    var page = A.activePage();
    var lines = [];
    var context = info.group ? info.group + " page " + info.page + " of " + info.pages : "Page";
    if (!page) return { context: context, lines: lines };
    var title = "";
    if (hasClass(page, "std-map")) title = "Map";
    else if (hasClass(page, "tfc-map")) title = "Traffic";
    else if (hasClass(page, "gps-status")) title = "GPS status";
    else if (hasClass(page, "terrain-map-page")) title = "Terrain";
    else if (page.querySelector(".arc-map-container")) title = "Navigation";
    if (title) context += ", " + title;
    emit(page, lines);
    return { context: context, lines: lines };
  };

  // The left pane: COM and VLOC with their standby, which of them the tuning knob is on
  // (the `active` class sits on the standby row of the selected pane - measured live),
  // the VLOC ident / radial / distance, and the status pane (ENR, INTEG...).
  A.radios = function () {
    var root = A.root();
    var left = root && root.querySelector(".mainscreen-left");
    if (!left) return [];
    var out = [];
    var panes = left.querySelectorAll(".com-pane");
    for (var i = 0; i < panes.length; i++) {
      var p = panes[i];
      if (hidden(p)) continue;
      if (hasClass(p, "navinfo-pane")) {
        var parts = [];
        var rows = p.querySelectorAll(".navinfo-ident, .navinfo-radial, .navinfo-distance, .navinfo-airport, .navinfo-runway");
        for (var r = 0; r < rows.length; r++) { if (!visible(rows[r])) continue; var t = textOf(rows[r]); if (t) parts.push(t); }
        if (parts.length) out.push("VLOC " + parts.join(", "));
        continue;
      }
      var name = textOf(p.querySelector("h1"));
      var freqs = p.querySelectorAll(".radio-frequency");
      if (freqs.length >= 2) {
        var knob = hasClass(freqs[0], "active") || hasClass(freqs[1], "active");
        out.push(name + " " + textOf(freqs[0]) + ", standby " + textOf(freqs[1]) + (knob ? ", knob" : ""));
      } else if (name) out.push(name + " " + textOf(p));
    }
    var status = left.querySelector(".status-pane");
    var statusText = "";
    if (status) statusText = textOf(status);
    else {
      // The 530 draws the mode and INTEG boxes as bare divs after the navinfo pane.
      var extra = [];
      for (var k = 0; k < left.children.length; k++) {
        var c = left.children[k];
        if (hidden(c) || hasClass(c, "com-pane")) continue;
        var s = textOf(c);
        if (s) extra.push(s);
      }
      statusText = extra.join(" ");
    }
    if (statusText) out.push("Mode " + statusText);
    return out;
  };

  A.footer = function () {
    var root = A.root();
    var f = root && root.querySelector(".mainscreen-footer");
    if (!f) return [];
    var out = [];
    var cdi = f.querySelector(".cdi-label");
    if (cdi && !hidden(cdi)) { var c = textOf(cdi); if (c) out.push("CDI " + c); }
    var msg = f.querySelector(".msg-label");
    if (msg && !hidden(msg)) { var m = textOf(msg); if (m) out.push("Message pending"); }
    return out;
  };

  // ---------------------------------------------------------- the readout

  // Which layer is on top, as the rows the window shows: context first, the layer's
  // lines, then the radios and the footer.
  // The stock instrument template wraps everything in #Electricity, whose `state`
  // attribute the sim flips to "off" when the unit has no power.
  A.isOff = function () {
    var e = document.getElementById("Electricity");
    return !!(e && e.getAttribute("state") === "off");
  };

  A.layer = function () {
    var root = A.root();
    if (!root) return { context: "The GNS is not rendering.", lines: [], kind: "none" };
    if (A.isOff()) return { context: "The GNS is off. It needs the avionics master and bus power.", lines: [], kind: "off" };
    var startup = A.startup();
    if (startup) return { context: startup.context, lines: startup.lines, kind: "startup" };
    var test = A.selfTest();
    if (test) return { context: test.context, lines: test.lines, kind: "selftest" };
    var dialog = A.dialog();
    if (dialog) return { context: dialog.context, lines: dialog.lines, kind: "dialog" };
    var page = A.page();
    return { context: page.context, lines: page.lines, kind: "page" };
  };

  A.rows = function () {
    var layer = A.layer();
    var rows = [layer.context];
    for (var i = 0; i < layer.lines.length; i++) rows.push(layer.lines[i]);
    if (layer.kind !== "none" && layer.kind !== "off") {
      var radios = A.radios();
      var footer = A.footer();
      if (radios.length || footer.length) rows.push("---");
      for (var r = 0; r < radios.length; r++) rows.push(radios[r]);
      for (var f = 0; f < footer.length; f++) rows.push(footer[f]);
    }
    return rows;
  };

  // The cursor line of the layer on top, if any.
  A.cursorLine = function (layer) {
    layer = layer || A.layer();
    for (var i = 0; i < layer.lines.length; i++) {
      if (layer.lines[i].indexOf(CURSOR) === 0) return layer.lines[i].substring(CURSOR.length);
      if (layer.lines[i].indexOf(", cursor on ") > 0) return layer.lines[i];
    }
    return "";
  };

  // The character under the cursor in an ident entry, if one is open.
  A.entryChar = function () {
    var root = A.root();
    var slots = root ? root.querySelectorAll(".alpha-num-slot") : [];
    for (var i = 0; i < slots.length; i++) {
      if (!isCursor(slots[i]) || !visible(slots[i])) continue;
      var c = tidy(slots[i].textContent) || "_";
      return c === "_" ? "blank" : c;
    }
    return "";
  };

  // ---------------------------------------------------------- typed entry
  //
  // TYPE AN IDENT INTO THE FIELD UNDER THE CURSOR. This is the instrument's OWN keyboard
  // path, not a shortcut around it: every AlphaNumInput is built with `enableKeyboard`
  // and carries `setValueFromOS(text)` for the sim's on-screen keyboard, which writes each
  // character through the same onSlotChanged the knob uses — so the database search, the
  // autocomplete and the facility lookup all run exactly as they do for a sighted pilot.
  // Thirty-odd knob clicks to spell one ident is the same aircraft made unusable; the
  // knob path stays for anyone who wants it, and both end in the same component.
  //
  // The component instance is found by walking the instrument's object graph from its
  // main screen (FSComponent attaches no instance to the DOM), matching on the `el` ref
  // that points at the VISIBLE `.alpha-num-input` — the one whose slot carries the cursor
  // when there is one, else the only visible one.
  function findInput() {
    var root = A.root();
    var ms = root && root.mainScreen && root.mainScreen.instance;
    if (!ms) return null;
    var domTarget = null, anyVisible = null;
    var inputs = document.querySelectorAll(".alpha-num-input");
    for (var i = 0; i < inputs.length; i++) {
      if (!visible(inputs[i])) continue;
      anyVisible = anyVisible || inputs[i];
      var slots = inputs[i].querySelectorAll(".alpha-num-slot");
      for (var s = 0; s < slots.length; s++) if (isCursor(slots[s])) { domTarget = inputs[i]; break; }
      if (!domTarget && containsCursor(inputs[i])) domTarget = inputs[i];
      if (domTarget) break;
    }
    var want = domTarget || anyVisible;
    if (!want) return null;
    var seen = [], queue = [{ o: ms, d: 0 }];
    while (queue.length) {
      var cur = queue.shift();
      var o = cur.o;
      if (!o || typeof o !== "object" || cur.d > 9) continue;
      if (seen.indexOf(o) >= 0) continue;
      seen.push(o);
      if (seen.length > 20000) break;
      // ⚠️ A NodeReference's `instance` GETTER THROWS while it is unset ("Instance was
      // null"), so every ref is read through its `_instance` field and never the getter —
      // the first version of this walk died on the first unrendered ref it met.
      try {
        if (typeof o.setValueFromOS === "function" && typeof o.onSlotChanged === "function" &&
            o.el && o.el._instance === want) return o;
      } catch (e) { }
      var names;
      try { names = Object.keys(o); } catch (e2) { continue; }
      for (var k = 0; k < names.length; k++) {
        var v;
        try { v = o[names[k]]; } catch (e3) { continue; }
        if (!v || typeof v !== "object") continue;
        if (v instanceof Node) continue;
        queue.push({ o: v, d: cur.d + 1 });
        var inst = null;
        try { inst = v._instance; } catch (e5) { inst = null; }
        if (inst && typeof inst === "object" && !(inst instanceof Node)) queue.push({ o: inst, d: cur.d + 1 });
      }
    }
    return null;
  }

  A.typeIdent = function (str) {
    var s = String(str || "").toUpperCase().replace(/[^A-Z0-9]/g, "");
    if (!s) return "nothing to type";
    var inp;
    try { inp = findInput(); } catch (e) { return "error " + (e && e.message ? e.message : e); }
    if (!inp) return "no field";
    var len = inp.props && inp.props.length ? inp.props.length : 5;
    if (s.length > len) s = s.substring(0, len);
    // setValueFromOS also touches the keyboard's hidden <input>, which the WPT pages never
    // render (measured: "Instance was null" on the airport page). Its per-slot body is
    // onSlotChanged(i, char) — the same call — so that is used directly; the value, the
    // onChanged search and the slot focus all happen exactly as they would for the keyboard.
    try {
      for (var i = 0; i < s.length; i++) inp.onSlotChanged(i, s.charAt(i));
      return "ok " + s;
    } catch (e4) { return "error " + (e4 && e4.message ? e4.message : e4); }
  };

  // What the unit made of the ident, once its search has run: the field as it now reads
  // and the facility the dialog resolved beside it (Direct-To: region, name, city; the
  // flight-plan insert dialog and the WPT pages: their own info blocks).
  A.typed = function () {
    var out = [];
    var inputs = document.querySelectorAll(".alpha-num-input");
    for (var i = 0; i < inputs.length; i++) {
      if (!visible(inputs[i])) continue;
      var t = alphaNum(inputs[i]).replace(/, cursor on .*$/, "").replace(/_+$/, "");
      if (t) { out.push(t); break; }
    }
    var sel = ".dto-waypoint-info-region, .dto-waypoint-info-name, .dto-waypoint-info-city, .waypoint-info-region, .waypoint-info-name, .waypoint-info-city, .waypoint-airport-location, .waypoint-vor-location, .waypoint-ndb-location, .waypoint-intersection-region";
    var infos = document.querySelectorAll(sel);
    for (var k = 0; k < infos.length; k++) {
      if (!visible(infos[k])) continue;
      // The block's label ("FACILITY & CITY NAME: ...") is dropped; a blank value means the
      // unit found nothing for that ident on this page (a VOR typed on the airport page).
      var v = textOf(infos[k]).replace(/^[A-Z &,]+:\s*/, "");
      if (v && v !== "blank" && !/^(blank[ ,]*)+$/.test(v) && out.indexOf(v) < 0) out.push(v);
    }
    if (!out.length) return "nothing entered";
    if (out.length === 1) return out[0] + ", no match on this page";
    return out.join(", ");
  };

  // The standby frequency of the pane the tuning knob is on.
  A.tuning = function () {
    var root = A.root();
    var act = root && root.querySelector(".radio-frequency.active");
    if (!act) return "";
    var pane = act.parentElement;
    var name = pane ? textOf(pane.querySelector("h1")) : "";
    return name + " standby " + textOf(act);
  };

  // The display's state as ONE string for the window's post-key announcement:
  // "ok|<kind>|<context>|<cursor line>|<entry char>|<tuning>". Pipes never occur in the
  // instrument's text.
  A.state = function () {
    try {
      var layer = A.layer();
      var off = layer.kind === "off" || layer.kind === "none";
      return ["ok", layer.kind, layer.context, off ? "" : A.cursorLine(layer), off ? "" : A.entryChar(), off ? "" : A.tuning()].join("|").replace(/\n/g, " ");
    } catch (e) {
      return "error " + (e && e.message ? e.message : String(e));
    }
  };

  A.scrape = function () {
    try {
      return JSON.stringify({ ok: true, rows: A.rows() });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) });
    }
  };

  window.__MSFSBA_GNS = A;
  window.__MSFSBA_DISP = { scrape: A.scrape, ping: function () { return "MSFSBA_DISP_OK"; } };
  return "MSFSBA_DISP_INSTALLED gns v" + A.VERSION;
})();
