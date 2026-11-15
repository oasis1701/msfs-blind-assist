// The sim tablet in-page agent: MSFS 2024's own EFB, installed at runtime into its Coherent GT view
// "Electronic Flight Bag" (coui://html_ui/efb_ui/efb_os/efb_ui.html) as window.__MSFSBA_SIM_TABLET.
// The view belongs to the sim, not to an aircraft: every MSFS 2024 aircraft shows the same tablet,
// with the sim's apps (Flight Planner, Aircraft, Pilot book, Settings, Ground Services, CIVA) and any
// add-on apps installed into it. MSFS 2020 has no such view.
//
// The SAME script also reads an app that lives in its own cross-origin frame (GSX Remote at
// 127.0.0.1:8744, Navigraph Charts): the page cannot reach into such a frame, so the reader emits a
// 'frame' marker where it sits and CoherentPmdgEfbClient evaluates this script inside the frame's own
// execution context and splices its elements in (STAB-14). Outside the EFB OS document (no .efb) the
// reader reads the whole document: no dock, no SimBrief action.
//
// ES5 ONLY. Coherent GT is Chromium 49: var (no const/let), no arrow functions, no template
// literals, no String.includes / Array.from / Object.assign. Use indexOf and plain loops.
//
// Contract, identical to coherent-pmdg-efb-agent.js so the shared FbwEfbForm consumes both:
//   scrape()            -> JSON string {ok, unchanged?, page, elements:[...]}
//   clickElement(idx)   -> press the element carrying that stamped idx
//   setValue(idx, text) -> type into it, tick it, pick a dropdown choice, or move a slider
// Element: {idx,text,value,controlType,kind,clickable,level,live,disabled,options,min,max,step,key?,announceChange?}
//   kind: 'tab' | 'button' | 'heading' (+level) | 'static' | 'alert' | 'frame' (the client's marker)
//   controlType: 'text' | 'checkbox' | 'select' | 'range'
//
// Plus the Flight Planner route primitives (A.route.*) the app's SimBrief import drives one step at
// a time; see the block near the end.
//
// HOW THE TABLET TAKES INPUT (measured live 2026-10-19 and 2026-11-15, and read from the apps' code):
//   * Buttons, tabs, dropdown items and switches answer el.click(): the OS button sets onclick.
//   * A few lists answer mousedown only (the Flight Planner's search results, FacilityResultItem
//     sets onmousedown; MK Airport's jetway buttons). A.press fires the ONE event the element
//     listens to, never both (STAB-2).
//   * A text box takes its value from .value plus an 'input' event, with NO focus.
//   * The OS slider takes a mousedown on its bar, then follows the DOCUMENT's mousemove and mouseup
//     (STAB-12); its position is the --ratio-value style on the slider.
// ⚠️ NEVER FOCUS A TABLET TEXT BOX, and never send it a mousedown: the OS TextBox focuses its input
// on mousedown, and focusing it fires Coherent FOCUS_INPUT_FIELD, which hands the pilot's KEYBOARD to
// the tablet (every sim key and app hotkey stops working). A.releaseKeyboard undoes a focus the
// tablet itself took after one of our presses (a "focus on clear" box).
(function () {
  var A = {};
  A.INSTALLED = 'MSFSBA_SIM_TABLET_INSTALLED';
  A.ATTR = 'data-sim-tablet-idx';

  // The app's own action, shown on every Flight Planner page: the client intercepts a click on this
  // idx and runs the SimBrief import itself (SimTabletSimBriefImport). Sequential stamping never
  // reaches it, so it is stable across scrapes.
  A.SIMBRIEF_IDX = 990001;
  A.SIMBRIEF_LABEL = 'Load latest SimBrief plan';

  // The client's other action (STAB-16): shown while nothing in the sim shows the tablet (its view
  // gets no frames, STAB-15), it opens the sim's own tablet panel from the toolbar, as a sighted pilot
  // does, so the parts of an app that only move with frames fill in (Pilot book's logbook list).
  // While the panel MSFSBA showed still shows (window.__MSFSBA_TABLET_SHOWN, set by the client), the
  // same button hides it again, so it stays in its place in the list.
  A.SHOW_TABLET_IDX = 990002;
  A.SHOW_TABLET_LABEL = 'Show the tablet in the sim (some lists fill in only while the sim shows it)';
  A.HIDE_TABLET_LABEL = 'Hide the tablet in the sim';

  // ---------------------------------------------------------------------------------
  // which listeners an element has (STAB-13)
  // ---------------------------------------------------------------------------------

  // Add-on apps bind presses with addEventListener (MK Airport's tabs: "click"; its jetway buttons:
  // "mousedown"), which leaves no trace on the element. Record, per element, which press events it
  // was given from now on, so A.press can fire the one it answers. Installed ONCE per document (a
  // re-injection keeps the first hook), and it only RECORDS: the listener is passed through unchanged.
  try {
    if (!window.__MSFSBA_LISTEN_HOOKED && window.EventTarget && EventTarget.prototype.addEventListener) {
      var origAdd = EventTarget.prototype.addEventListener;
      EventTarget.prototype.addEventListener = function (type) {
        try {
          if (this && this.nodeType === 1 && (type === 'click' || type === 'mousedown' || type === 'mouseup' ||
              type === 'pointerdown' || type === 'pointerup' || type === 'touchstart')) {
            var h = this.__msfsbaEv || (this.__msfsbaEv = {});
            h[type] = true;
          }
        } catch (e) {}
        return origAdd.apply(this, arguments);
      };
      window.__MSFSBA_LISTEN_HOOKED = true;
    }
  } catch (e) {}

  // The page's frame callbacks (STAB-15). An app draws in requestAnimationFrame callbacks (the OS's
  // VirtualList builds its rows there after a scroll, a ResizeObserver polyfill measures there), and
  // while the sim shows the tablet nowhere no frame comes to run them. Each callback queued from now
  // on is recorded, wrapped so it runs ONCE whoever calls it first, the browser at a frame or the
  // reader's pump (A.runFrameCallbacks); a cancelled one is forgotten. Installed once per document;
  // the reader's own frame watch uses the browser's function (A.raf), so a pump is never a frame.
  try {
    if (!window.__MSFSBA_RAF && window.requestAnimationFrame) {
      var rafState = { raf: window.requestAnimationFrame, caf: window.cancelAnimationFrame, pending: {}, hook: null };
      rafState.hook = function (cb) {
        var done = false, id = 0;
        var once = function (ts) { if (done) return undefined; done = true; delete rafState.pending[id]; return cb(ts); };
        id = rafState.raf.call(window, once);
        rafState.pending[id] = once;
        return id;
      };
      window.requestAnimationFrame = rafState.hook;
      if (rafState.caf) {
        window.cancelAnimationFrame = function (id) { delete rafState.pending[id]; return rafState.caf.call(window, id); };
      }
      window.__MSFSBA_RAF = rafState;
    }
  } catch (e) {}

  // ---------------------------------------------------------------------------------
  // small helpers
  // ---------------------------------------------------------------------------------

  A.cls = function (el) {
    if (!el || el.nodeType !== 1) return ' ';
    var c = el.className;
    if (c && typeof c !== 'string') c = c.baseVal;     // an SVG element's className is an SVGAnimatedString
    return ' ' + (c || '') + ' ';
  };
  A.hasClass = function (el, c) { return A.cls(el).indexOf(' ' + c + ' ') >= 0; };
  A.tokens = function (el) { return A.norm(A.cls(el)).toLowerCase().split(' '); };
  A.attr = function (el, n) { return el && el.getAttribute ? el.getAttribute(n) : null; };
  // A data attribute that says yes: data-active="1", data-on="true".
  A.yes = function (el, n) { var v = A.attr(el, n); return v === '1' || v === 'true' || v === 'on' || v === 'yes'; };

  A.styleOf = function (el) {
    try {
      var w = (el.ownerDocument && el.ownerDocument.defaultView) || window;
      return w.getComputedStyle(el);
    } catch (e) { return null; }
  };

  A.isHidden = function (el) {
    if (!el || !el.getBoundingClientRect) return false;
    var r = el.getBoundingClientRect();
    if (r.width <= 0 && r.height <= 0) return true;
    var st = A.styleOf(el);
    if (!st) return false;
    if (st.display === 'none') return true;
    // Hidden until the mouse is over it: a control a sighted pilot finds by hovering (the Flight
    // Planner's "Clear Selected Facility", shown by ".atlas-nav-bar-facility:hover
    // .icon-button-close-facility { visibility: visible }") is on screen for the reader too.
    if (st.visibility === 'hidden' || st.opacity === '0') return !A.hoverRevealed(el);
    return false;
  };

  // The page's own hover rules that make something visible when the mouse is over an ANCESTOR of it
  // (".row:hover .x-close"), with the :hover taken out (".row .x-close"), so an element one matches is
  // one a sighted pilot finds by moving over its row. A rule hovering the element itself
  // (".btn:hover{opacity:1}") is left out: nothing shows where to move to find it. Rules inside
  // @media and @supports count. Kept per document (a same-origin frame has its own sheets), re-read
  // when the sheets or their rule counts change (an app installs its sheet, a library inserts rules),
  // and checked once per read.
  A.hoverSelectors = function (doc) {
    doc = doc || document;
    var c = doc.__msfsbaHover;
    if (c && c.scrape === A._scrapeId) return c.sels;
    var sheets = doc.styleSheets || [], sig = String(sheets.length), i;
    for (i = 0; i < sheets.length; i++) {
      var rl = null;
      try { rl = sheets[i].cssRules; } catch (e0) { rl = null; }
      sig += ',' + (rl ? rl.length : -1);
    }
    if (c && c.sig === sig) { c.scrape = A._scrapeId; return c.sels; }
    var out = [];
    function scan(rules, depth) {
      for (var j = 0; rules && j < rules.length && depth < 4; j++) {
        var r = rules[j];
        if (r.cssRules && !r.selectorText) { scan(r.cssRules, depth + 1); continue; }   // @media, @supports
        var sel = r.selectorText || '', css = r.style ? r.style.cssText : '';
        if (sel.indexOf(':hover') < 0 || !/visibility:\s*(visible|inherit)|opacity:\s*1(\D|$)/.test(css)) continue;
        var parts = sel.split(',');
        for (var k = 0; k < parts.length; k++) {
          var p = A.norm(parts[k]);
          if (p.indexOf(':hover') < 0 || p.indexOf(':not(') >= 0) continue;
          var comps = p.replace(/\s*[>+~]\s*/g, ' ').split(' ');
          if (comps[comps.length - 1].indexOf(':hover') >= 0) continue;               // hovering itself
          out.push(A.norm(p.replace(/:hover/g, '')));
        }
      }
    }
    for (i = 0; i < sheets.length; i++) {
      var rules = null;
      try { rules = sheets[i].cssRules; } catch (e) { rules = null; }
      scan(rules, 0);
    }
    doc.__msfsbaHover = { scrape: A._scrapeId, sig: sig, sels: out };
    return out;
  };
  A.matchesAny = function (n, sels) {
    for (var i = 0; i < sels.length; i++) {
      try { if (n.matches ? n.matches(sels[i]) : n.webkitMatchesSelector(sels[i])) return true; } catch (e) {}
    }
    return false;
  };
  // A hover reveals a CONTROL, or a group holding controls (a row's hover menu), and the words inside
  // it, never a tooltip, which has hiding of its own and shows on a hover of the control: it would
  // merge into its name. A wrapper named for a tooltip that holds the control ("tooltip-component")
  // is no tooltip. The OS's .disabled-layer needs no exception: it hides with display:none (measured
  // 2026-11-15), which A.isHidden reads before any hover rule, so a revealed disabled control still
  // reads dimmed (STAB-3).
  A.OWN_HIDING = /^(tooltip|tip|hint)$|^tooltip[-_]|[-_](tooltip|tip)$/i;
  A.ownHiding = function (n) {
    if (A.attr(n, 'role') === 'tooltip') return true;
    var t = A.tokens(n);
    for (var i = 0; i < t.length; i++) if (A.OWN_HIDING.test(t[i])) return true;
    return false;
  };
  // Memoized per read: every hidden element under one revealed row asks the same question.
  A.holdsControl = function (n) {
    if (n.__msfsbaHolds && n.__msfsbaHolds.s === A._scrapeId) return n.__msfsbaHolds.v;
    var v = A.isControlEl(n);
    var all = n.getElementsByTagName('*');
    for (var i = 0; !v && i < all.length && i < 200; i++) if (A.isControlEl(all[i])) v = true;
    n.__msfsbaHolds = { s: A._scrapeId, v: v };
    return v;
  };
  // The element, or the ancestor it inherits its hiding from (a revealed wrapper reveals its button).
  A.hoverRevealed = function (el) {
    var sels = A.hoverSelectors(el.ownerDocument);
    if (!sels.length) return false;
    for (var n = el, up = 0; n && n.nodeType === 1 && up < 6; n = n.parentElement, up++) {
      if (!n.matches && !n.webkitMatchesSelector) return false;
      if (A.matchesAny(n, sels)) return A.holdsControl(n);
      if (A.ownHiding(n) && !A.holdsControl(n)) return false;
      var ps = n.parentElement ? A.styleOf(n.parentElement) : null;
      if (!ps || (ps.visibility !== 'hidden' && ps.opacity !== '0')) return false;   // hidden here, not inherited
    }
    return false;
  };
  A.isVisible = function (el) { return !!el && !A.isHidden(el); };

  // A virtual list places its rows with position:absolute and a top of their own, in any DOM order
  // (the Pilot book's logbook: the September header between October flights). Children that are all
  // positioned so are read in their on-screen order, top to bottom then left to right; any other
  // parent keeps its DOM order. Stable: rows at the same place keep theirs.
  A.childrenInOrder = function (el) {
    var kids = [], i;
    for (i = 0; i < el.children.length; i++) kids.push(el.children[i]);
    if (kids.length < 2) return kids;
    var st0 = A.styleOf(kids[0]);
    if (!st0 || st0.position !== 'absolute') return kids;
    var keyed = [];
    for (i = 0; i < kids.length; i++) {
      var st = A.styleOf(kids[i]);
      if (!st || st.position !== 'absolute') return kids;
      var r = kids[i].getBoundingClientRect();
      keyed.push({ el: kids[i], top: Math.round(r.top), left: Math.round(r.left), i: i });
    }
    keyed.sort(function (a, b) { return a.top - b.top || a.left - b.left || a.i - b.i; });
    for (i = 0; i < keyed.length; i++) kids[i] = keyed[i].el;
    return kids;
  };

  // An OS icon that stands for a word on a line of values (the Pilot book's day and night totals:
  // "Total flight hours 167h33: day 57h32 night 110h02"). Only icons whose meaning is certain.
  A.ICON_WORDS = { 'day-icon': 'day', 'night-icon': 'night' };
  A.iconWord = function (el) {
    if (!A.hasClass(el, 'icon-element')) return '';
    var t = A.tokens(el);
    for (var i = 0; i < t.length; i++) if (A.ICON_WORDS.hasOwnProperty(t[i])) return A.ICON_WORDS[t[i]];
    return '';
  };

  // The EFB OS document (its .efb holds the .efb-container of app views), or an app's own frame, read
  // whole: a frame page that happens to use an "efb" class is still a frame.
  A.inEfb = function () { return !!document.querySelector('.efb .efb-container'); };
  A.root = function () { return (A.inEfb() && document.querySelector('.efb')) || document.body; };
  // tools/coherent.ps1's `capture` command looks for findRoot()/isVisible() on the installed agent.
  A.findRoot = function () { return A.root(); };

  // The OS disables a control with setAttribute("disabled", "") — on <div> list items as well as
  // buttons, where el.disabled is undefined. One container carries disabled="false" while enabled.
  // The OS also shows a disabled control's own .disabled-layer child, which is what a captured
  // fixture keeps. Add-on apps say it with aria-disabled or a class ("mk-btn--disabled").
  A.isDisabled = function (el) {
    if (!el || el.nodeType !== 1) return false;
    if (el.disabled === true) return true;
    var a = el.getAttribute('disabled');
    if (a !== null && a !== 'false') return true;
    if (el.getAttribute('aria-disabled') === 'true') return true;
    // A form control inside a disabled <fieldset> is disabled with it.
    if (/^(INPUT|SELECT|TEXTAREA|BUTTON)$/.test(el.tagName)) {
      for (var f = el.parentElement; f; f = f.parentElement) if (f.tagName === 'FIELDSET' && f.disabled) return true;
    }
    var t = A.tokens(el);
    for (var j = 0; j < t.length; j++) if (t[j] === 'disabled' || t[j] === 'is-disabled' || /--disabled$/.test(t[j])) return true;
    for (var i = 0; i < el.children.length; i++) {
      if (A.hasClass(el.children[i], 'disabled-layer')) return !A.isHidden(el.children[i]);
    }
    return false;
  };

  A.norm = function (s) { return String(s == null ? '' : s).replace(/\s+/g, ' ').trim(); };

  A.ucfirst = function (s) { return s ? s.charAt(0).toUpperCase() + s.slice(1) : s; };

  // Is this element left out of a hidden subtree's MODEL text? A hidden dropdown list or a seat's
  // closed detail panel is read on purpose, but inside it the OS still hides alternatives with the
  // "hide" class (the fuel tank's LBS total beside its GAL one) or an inline display:none.
  A.modelHidden = function (el) {
    if (A.hasClass(el, 'hide') || A.hasClass(el, 'hidden')) return true;
    return !!(el.style && el.style.display === 'none');
  };

  // The text of an element as one line. Parts from different elements are joined with a space
  // only where the page shows one: by the gap between their boxes on screen (a number and its unit
  // sit 3 px apart, the date's "19" and "," touch), or, inside a hidden subtree whose boxes are all
  // zero, by punctuation.
  A.textOf = function (el, mode) {
    var parts = [];
    (function rec(n) {
      for (var i = 0; i < n.childNodes.length; i++) {
        var c = n.childNodes[i];
        if (c.nodeType === 3) { if (c.nodeValue) parts.push({ t: c.nodeValue, el: n }); continue; }
        if (c.nodeType !== 1) continue;
        var tag = c.tagName.toLowerCase();
        if (tag === 'svg' || tag === 'script' || tag === 'style' || tag === 'canvas' || tag === 'img' || tag === 'iframe') continue;
        if (mode === 'model' ? A.modelHidden(c) : A.isHidden(c)) continue;
        var word = A.iconWord(c);
        if (word) { parts.push({ t: word, el: c }); continue; }
        rec(c);
      }
    })(el);
    var s = '', prev = null;
    for (var i = 0; i < parts.length; i++) {
      var p = parts[i], t = p.t;
      if (prev && prev.el !== p.el && s && !/\s$/.test(s) && !/^\s/.test(t) && A.needsSpace(prev.el, p.el, s, t)) s += ' ';
      s += t;
      prev = p;
    }
    return A.norm(s);
  };
  A.needsSpace = function (a, b, left, right) {
    // A number against its unit or a label against its number is never one word, even where the
    // boxes touch ("11890" + "FT", "OS Version" + "1.0.3").
    if (/\d$/.test(left) && /^[A-Za-z]/.test(right) || /[A-Za-z]$/.test(left) && /^\d/.test(right)) return true;
    // Nor is a word against the next word in a box of its own (a flex or grid item, never an inline
    // run that can split one word: "<b>Lon</b>don"), or a lone dash standing for a value (MK
    // Airport's diagnostics: "Airport module" + "not responding", "Last update" + "-").
    var sa = A.styleOf(a), sb = A.styleOf(b);
    var boxed = (sa && sa.display && sa.display !== 'inline') || (sb && sb.display && sb.display !== 'inline');
    if (boxed && /[A-Za-z]{2}$/.test(left) && /^[A-Za-z]{2}/.test(right)) return true;
    if (boxed && /^[-–—]\s*$/.test(right) && /[A-Za-z]$/.test(left)) return true;
    var ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect();
    if (ra.width > 0 && rb.width > 0) {
      var sameRow = Math.abs((ra.top + ra.bottom) / 2 - (rb.top + rb.bottom) / 2) <= 6;
      if (!sameRow) return true;
      return rb.left - ra.right > 1;
    }
    if (/^[,.:;!?)%°]/.test(right)) return false;
    if (/[(\/]$/.test(left)) return false;
    return true;
  };
  A.lineText = function (el) { return A.textOf(el, 'visible'); };
  A.modelText = function (el) { return A.textOf(el, 'model'); };

  // A tooltip's text, when the OS would show one for this control. A Tooltip whose condition is
  // false renders with the "forceHide" class (the launcher's "not compatible with this aircraft"
  // note on an app that IS compatible), so that one is never a name.
  A.tooltipOf = function (el) {
    var tc = el, depth = 0;
    while (tc && depth < 4 && !A.hasClass(tc, 'tooltip-component')) { tc = tc.parentElement; depth++; }
    if (!tc || !A.hasClass(tc, 'tooltip-component')) return '';
    for (var i = 0; i < tc.children.length; i++) {
      var k = tc.children[i];
      if (!A.hasClass(k, 'tooltip')) continue;
      if (A.hasClass(k, 'forceHide')) return '';
      var d = k.querySelector('.tooltip-description');
      return d ? A.modelText(d) : '';
    }
    return '';
  };

  // An icon's name: the first id inside its SVG live, data-icon in a captured fixture. The asset
  // names carry their icon set ("Save--Streamline-Carbon") and clip prefixes ("clip-Arrow").
  // The first id that says something: an editor's own names ("svg1", "layer1", "path1", Illustrator's
  // "Calque_1", Figma's "paint0_linear_12_3") are skipped (the Flight Planner's airport header chevron
  // is "svg1" > "layer1" > "down"). An id INSIDE the drawing counts only as a word, letters alone, 3 or
  // more: editors number what they name ("Rectangle 4", "filter0_d_1_2"), a person names the icon.
  A.GENERIC_ID = new RegExp('^(svg|defs|layer|path|g|group|groupe|trac[e\u00e9]?|rect|rectangle|circle|mask|masque|clip|clippath|' +
    'stop|use|polygon|polyline|ellipse|line|ligne|calque|union|titre|title|paint|filter|lineargradient|radialgradient|' +
    'gradient|pattern|symbol|image|shape|vector|frame|object|objet|text|isolation mode|mask group|clip path group|clip path|' +
    'svgrepo [a-z]*)[\\s\\d]*$', 'i');
  A.ICON_WORD = /^[A-Za-zÀ-ɏ ]{3,}$/;
  A.iconName = function (el) {
    var svg = el.querySelector('svg');
    if (!svg) return '';
    var cands = [svg.getAttribute('data-icon'), svg.id], inner = svg.querySelectorAll('[id]');
    for (var j = 0; j < inner.length && j < 12; j++) cands.push(inner[j].id);
    for (var i = 0; i < cands.length; i++) {
      var n = A.norm(String(cands[i] || '').replace(/--.*$/, '').replace(/^clip-/, '').replace(/[-_]+/g, ' '));
      if (!n || A.GENERIC_ID.test(n)) continue;
      if (i >= 2 && !A.ICON_WORD.test(n)) continue;
      return n;
    }
    return '';
  };

  // A picture that names its control: <img alt="CIVA Options">.
  A.imgName = function (el) {
    var imgs = el.tagName === 'IMG' ? [el] : el.querySelectorAll('img');
    for (var i = 0; i < imgs.length; i++) {
      if (A.isHidden(imgs[i])) continue;
      var n = A.norm(imgs[i].getAttribute('alt') || imgs[i].getAttribute('title'));
      if (n) return n;
    }
    return '';
  };

  // aria-labelledby names a control by other elements' text.
  A.labelledBy = function (el) {
    var ids = A.norm(A.attr(el, 'aria-labelledby'));
    if (!ids) return '';
    var parts = ids.split(' '), out = [];
    for (var i = 0; i < parts.length; i++) {
      var n = el.ownerDocument.getElementById(parts[i]);
      if (n) { var t = A.modelText(n); if (t) out.push(t); }
    }
    return out.join(' ');
  };

  // Icon buttons the OS names nowhere: by their class.
  A.CLASS_NAMES = {
    'more-info-button': 'More information',
    'fold-button': 'Fold menu',
    'delete-text-button': 'Clear',
    'refresh': 'Refresh',
    'split-view-btn': 'Split view',
    'back-button': 'Back',
    'return-button': 'Back',                // the Flight Planner's dialogs: a chevron that goes back
    'close-button': 'Close',
    'to-top-button': 'Back to the top',     // a scrolled list's arrow up (the Pilot book's logbook)
    'incremental-decrease': 'Decrease',
    'incremental-increase': 'Increase'
  };
  A.className = function (el) {
    for (var k in A.CLASS_NAMES) if (A.CLASS_NAMES.hasOwnProperty(k) && A.hasClass(el, k)) return A.CLASS_NAMES[k];
    return '';
  };

  A.buttonLabel = function (el) {
    var n = A.lineText(el) || A.attr(el, 'aria-label') || A.labelledBy(el) || A.tooltipOf(el) || A.attr(el, 'title') ||
      A.imgName(el) || A.attr(el, 'data-label');
    if (el.tagName === 'INPUT') n = n || el.value;
    // A drawn cross is a close button; a letter x only where it says so (a "close" class), never a
    // keypad's X key.
    if (n && (/^\s*[×✕✖✗]\s*$/.test(n) || (/^\s*[xX]\s*$/.test(n) && /close/i.test(A.cls(el))))) return 'Close';
    if (n) return A.norm(n);
    n = A.className(el);
    if (n) return n;
    n = A.iconName(el);
    // A panel's or popup's header leads with an unnamed icon button: its close.
    if (!n && el.parentElement && /header/.test(A.cls(el.parentElement)) && el === el.parentElement.firstElementChild) return 'Close';
    // A chevron in a header folds the panel under it (the Flight Planner's airport panel).
    if (n && /^(down|up|chevron.*|caret.*|arrow (down|up))$/i.test(n)) {
      for (var hp = el.parentElement, d = 0; hp && d < 3; hp = hp.parentElement, d++) if (/header/.test(A.cls(hp))) return 'Fold or unfold';
    }
    // An icon named only by its picture says which thing it is for: the Flight Planner step it
    // sits in ("Departure airport info"), or the text beside it ("Trash Flight Planner").
    if (n) {
      var step = A.stepTitle(el);
      if (step) return step + ' ' + n.toLowerCase();
      var sib = el.previousElementSibling;
      if (sib && !A.isHidden(sib) && !A.hasCtl(sib) && !A.isControlEl(sib)) {
        var t = A.lineText(sib);
        if (t && /[A-Za-z]/.test(t)) return n + ' ' + t;     // a name, never a clock beside it
      }
    }
    return n;
  };

  // ---------------------------------------------------------------------------------
  // what takes a press, and what state a control shows (STAB-13)
  // ---------------------------------------------------------------------------------

  // The debugger's getEventListeners, handed in by the client on each scrape, press and set: it
  // exists only inside an eval that asked for the command line API (CoherentPmdgEfbClient.ListenerArg).
  // Null when absent (the jsdom tests, an older client).
  A._gel = null;
  A.withListeners = function (gel, fn) {
    var prev = A._gel;
    A._gel = typeof gel === 'function' ? gel : null;
    A._gelStamp = (A._gelStamp || 0) + 1;
    try { return fn(); } finally { A._gel = prev; }
  };
  A.PRESS_EVENTS = ['click', 'mousedown', 'pointerdown', 'mouseup', 'pointerup', 'touchstart'];
  // Which press events the element itself listens to: on* properties, getEventListeners when the client
  // handed it in (exact, whenever the listener was bound), else the hook's record (bound after the
  // reader was installed).
  A.pressEventsOf = function (el) {
    if (el.__msfsbaGelStamp === A._gelStamp && el.__msfsbaGel) return el.__msfsbaGel;
    var out = {}, h = el.__msfsbaEv || {}, l = null;
    if (A._gel) { try { l = A._gel(el); } catch (e) { l = null; } }
    for (var i = 0; i < A.PRESS_EVENTS.length; i++) {
      var t = A.PRESS_EVENTS[i];
      if (typeof el['on' + t] === 'function' || h[t] || (l && l[t] && l[t].length)) out[t] = true;
    }
    if (A._gel) { el.__msfsbaGel = out; el.__msfsbaGelStamp = A._gelStamp; }
    return out;
  };
  // The one press event an element answers; '' when it shows none (a listener bound before the reader
  // was installed with no getEventListeners to ask, or a delegated one).
  A.handlerOf = function (el) {
    var h = A.pressEventsOf(el);
    for (var i = 0; i < A.PRESS_EVENTS.length; i++) if (h[A.PRESS_EVENTS[i]]) return A.PRESS_EVENTS[i];
    return '';
  };

  A.CLICK_ROLES = { button: 1, link: 1, tab: 1, menuitem: 1, option: 1, treeitem: 1, menuitemcheckbox: 1,
                    menuitemradio: 1, 'switch': 1, checkbox: 1, radio: 1 };
  // A class that names a pressable thing, for a listener bound before the reader was installed:
  // "mk-tab", "mk-btn--primary", "jetway-button", "si-btn", Tailwind's "cursor-pointer". Never a BEM
  // ELEMENT of one ("jetway-button__label"), never a group ("mk-tabs", "button-group"), and a tab
  // or switch only under an app's own prefix: the OS calls a whole load panel "tab" and its status
  // dot "chip". Never "link": AtmosphereMod's "atmo-link" is its METAR link's status line, and a real
  // link is an <a>.
  A.BEM_PRESS = /^(?:(?:[a-z0-9]+[-_])+(?:btn|button|tab|switch|toggle)|btn|button)(?:--[a-z0-9-]+)?$/;
  A.isTabLike = function (el) {
    if (A.attr(el, 'role') === 'tab') return true;
    var t = A.tokens(el);
    for (var i = 0; i < t.length; i++) if (t[i].indexOf('__') < 0 && /[-_]tab(?:--[a-z0-9-]+)?$/.test(t[i])) return true;
    return false;
  };
  // A drag handle (Settings' favourites "move-button") is no press: reordering by drag is not offered.
  A.DRAG_HANDLE = /(?:^|[-_])(?:move|drag|grab|handle|reorder)(?:[-_]|$)/;
  // With getEventListeners at hand the listeners themselves say what takes a press, and a class name is
  // only a guess (the OS's "seats-tab" panel, its "chip" dot), so the -btn/-tab guess is used only
  // without it; a class that says "pointer" or "clickable" is the page's own word either way.
  A.looksPressable = function (el) {
    var t = A.tokens(el);
    for (var j = 0; j < t.length; j++) if (A.DRAG_HANDLE.test(t[j])) return false;
    for (var i = 0; i < t.length; i++) {
      if (t[i] === 'cursor-pointer' || t[i] === 'clickable') return true;
      if (!A._gel && t[i].indexOf('__') < 0 && A.BEM_PRESS.test(t[i])) return true;
    }
    return false;
  };
  // Form fields and their captions are never a press: a <label> click focuses its field (STAB-1).
  A.NEVER_PRESS = { INPUT: 1, TEXTAREA: 1, SELECT: 1, LABEL: 1, OPTION: 1, OUTPUT: 1 };
  A.isPressable = function (el) {
    var tag = el.tagName;
    if (tag === 'BUTTON' || tag === 'SUMMARY') return true;
    if (tag === 'A') return el.getAttribute('href') !== null || !!A.handlerOf(el);
    if (tag === 'INPUT') { var ty = (el.type || '').toLowerCase(); return ty === 'button' || ty === 'submit' || ty === 'reset' || ty === 'image'; }
    if (A.NEVER_PRESS[tag]) return false;
    if (A.handlerOf(el)) return true;
    var role = A.attr(el, 'role');
    if (role && A.CLICK_ROLES[role]) return true;
    return A.looksPressable(el);
  };
  // A pointer cursor the page sets on the element itself, not one it inherits from its parent.
  A.ownPointer = function (el) {
    if (A.NEVER_PRESS[el.tagName]) return false;
    var st = A.styleOf(el);
    if (!st || st.cursor !== 'pointer') return false;
    var ps = el.parentElement ? A.styleOf(el.parentElement) : null;
    return !ps || ps.cursor !== 'pointer';
  };
  // A handler on a whole page or panel (iniJetways' body, which clears the selection) is a
  // container, never one button with the page's text as its name.
  A.isBig = function (el) {
    if (el.getElementsByTagName('*').length > 60) return true;
    var v = A._view, r = el.getBoundingClientRect(), vr = v && v !== el ? v.getBoundingClientRect() : null;
    return !!vr && vr.height > 0 && r.height > vr.height * 0.6 && r.width > vr.width * 0.6;
  };

  // The state a control shows, as words: from ARIA, from data attributes (SayIntentions' data-active,
  // iniJetways' data-state), and from class names ("selected", "active", "mk-tab--on", iniJetways'
  // "transit" while the jetway moves).
  A.STATE_CLASS = { selected: 'selected', active: 'active', checked: 'checked', on: 'on', current: 'current',
                    open: 'open', opened: 'open', expanded: 'expanded', connected: 'connected', transit: 'moving',
                    moving: 'moving', pressed: 'on', toggled: 'on', error: 'error', failed: 'failed', locked: 'locked' };
  A.NO_STATE = { '': 1, 'default': 1, normal: 1, none: 1, 'null': 1, undefined: 1, ready: 1 };
  A.stateOf = function (el) {
    var w = [], seen = {};
    function add(s) { if (s && !seen[s]) { seen[s] = 1; w.push(s); } }
    var a;
    if (A.attr(el, 'aria-selected') === 'true') add('selected');
    if (A.attr(el, 'aria-pressed') === 'true') add('on');
    if ((a = A.attr(el, 'aria-current')) && a !== 'false') add('current');
    if ((a = A.attr(el, 'aria-expanded')) === 'true') add('expanded'); else if (a === 'false') add('collapsed');
    if (A.yes(el, 'data-active') || A.yes(el, 'data-selected')) add('selected');
    if (A.yes(el, 'data-on')) add('on');
    var t = A.tokens(el);
    for (var i = 0; i < t.length; i++) {
      var k = t[i];
      if (k.indexOf(':') >= 0) continue;                  // Tailwind's "hover:…", "active:…" variants
      var m = /^(?:is-|has-)?([a-z]+)$/.exec(k) || /--([a-z]+)$/.exec(k) ||
        // a word at the end of an app's own class: CIVA's "civaefb-text-selector-active" (never
        // "...-inactive", whose last word is "inactive", nor "not-selected", "non-active", "un-checked")
        (/(^|[-_])(not|non|no|un)[-_](active|selected|current|checked)$/.test(k) ? null : /[-_](active|selected|current|checked)$/.exec(k));
      if (m && A.STATE_CLASS.hasOwnProperty(m[1])) add(A.STATE_CLASS[m[1]]);
      else if (/(^|-)error$/.test(k)) add('error');
    }
    var ds = A.attr(el, 'data-state');
    if (ds && /^[a-z][a-z-]{1,23}$/i.test(ds) && !A.NO_STATE[ds.toLowerCase()]) add(ds.toLowerCase().replace(/-/g, ' '));
    // A tab that is on is the selected tab ("mk-tab--on").
    if ((seen.on || seen.active || seen.current) && A.isTabLike(el)) add('selected');
    if (seen.selected) {                                    // one word for one thing
      var out = [];
      for (var j = 0; j < w.length; j++) if (w[j] !== 'active' && w[j] !== 'current' && w[j] !== 'on') out.push(w[j]);
      w = out;
    }
    return w;
  };
  // Can this control's state change under the pilot's press: it, or a sibling of the same kind,
  // carries state (an unselected tab beside the selected one, a jetway that is not yet docked).
  A.stateful = function (el) {
    if (A.stateOf(el).length || A.attr(el, 'data-state') !== null || A.attr(el, 'aria-pressed') !== null ||
        A.attr(el, 'aria-selected') !== null || A.attr(el, 'data-active') !== null) return true;
    var p = el.parentElement;
    if (!p) return false;
    for (var i = 0; i < p.children.length && i < 30; i++) {
      var s = p.children[i];
      if (s !== el && s.tagName === el.tagName && A.stateOf(s).length) return true;
    }
    return false;
  };

  // An on/off switch drawn by an add-on: ARIA (role="switch" + aria-checked), or a class that says
  // which way it is ("civaefb-bg-toggle-on", "mk-switch mk-switch--on"). Null when it is no switch.
  A.SWITCH_STATE = /(?:^|[-_])(?:toggle|switch)[-_](on|off|checked|unchecked|active|inactive|enabled)$/;
  A.SWITCH_NAME = /(?:^|[-_])(?:toggle|switch)(?:--([a-z]+))?$/;
  A.switchValue = function (el) {
    var role = A.attr(el, 'role'), a = A.attr(el, 'aria-checked');
    if (a !== null && (role === 'switch' || role === 'checkbox' || role === 'menuitemcheckbox' || role === 'radio' || role === 'menuitemradio')) {
      return a === 'true' ? 'true' : 'false';
    }
    var t = A.tokens(el), isSw = false, on = false;
    for (var i = 0; i < t.length; i++) {
      var k = t[i], m;
      if (k.indexOf('__') >= 0 || k.indexOf(':') >= 0) continue;
      if ((m = A.SWITCH_STATE.exec(k))) return /^(on|checked|active|enabled)$/.test(m[1]) ? 'true' : 'false';
      if ((m = A.SWITCH_NAME.exec(k))) { isSw = true; if (m[1] && /^(on|checked|active|enabled|selected)$/.test(m[1])) on = true; }
    }
    if (!isSw) return null;
    if (A.hasClass(el, 'checked') || A.hasClass(el, 'on') || A.hasClass(el, 'active') || A.hasClass(el, 'is-on') ||
        A.hasClass(el, 'is-checked') || A.hasClass(el, 'selected') || A.yes(el, 'data-on') || A.yes(el, 'data-checked') ||
        A.attr(el, 'aria-pressed') === 'true') on = true;
    return on ? 'true' : 'false';
  };

  // ---------------------------------------------------------------------------------
  // element records
  // ---------------------------------------------------------------------------------

  A._idx = 0;
  A._nodes = {};
  A.stamp = function (node) { node.setAttribute(A.ATTR, String(++A._idx)); A._nodes[A._idx] = node; return A._idx; };
  A.el = function (node, f) {
    var o = { idx: node ? A.stamp(node) : ++A._idx, text: '', value: '', controlType: '', kind: '',
              clickable: false, level: 0, live: '', disabled: false, options: null };
    for (var k in f) if (f.hasOwnProperty(k)) o[k] = f[k];
    return o;
  };
  A.button = function (node, text, extra) {
    var f = { kind: 'button', clickable: true, text: text, disabled: A.isDisabled(node) };
    if (extra) for (var k in extra) if (extra.hasOwnProperty(k)) f[k] = extra[k];
    return A.el(node, f);
  };

  // A field's caption: the nearest text before it in its own row or section (Environment's
  // "Wind magnitude" span sits beside its unit box; the Runway h2 heads its search bar), else
  // the box's placeholder. The caption element is CLAIMED so the walk does not read it again.
  // The texts gathered are the ones right before it in the same parent, up to the previous control
  // ("Cruise" + "FL" before the cruise level box), in reading order.
  // maxUp limits how far up it looks (3 levels by default).
  A.labelFor = function (ctl, maxUp) {
    var node = ctl, lim = maxUp || 3;
    for (var up = 0; up < lim && node && node !== A._view; up++) {
      var got = [], sib = node.previousElementSibling;
      for (var hop = 0; sib && hop < 3; hop++, sib = sib.previousElementSibling) {
        if (A.isHidden(sib)) continue;
        if (A.hasCtl(sib) || A.isControlEl(sib)) break;
        var t = A.lineText(sib);
        if (t) { got.unshift(t); A.claim(sib); }
      }
      if (got.length) return got.join(' ');
      node = node.parentElement;
    }
    return '';
  };
  // A form control's own name, the way a browser finds it: aria-label, aria-labelledby, a
  // <label for>, the <label> around it (its own text only, never a value read-out inside it), then
  // the text before it, a title, a placeholder.
  A.fieldName = function (ctl) {
    var n = A.attr(ctl, 'aria-label') || A.labelledBy(ctl);
    if (n) return A.norm(n);
    if (ctl.id) {
      try {
        var lf = ctl.ownerDocument.querySelector('label[for="' + ctl.id.replace(/"/g, '') + '"]');
        if (lf && A.modelText(lf)) { A.claim(lf); return A.modelText(lf); }
      } catch (e) {}
    }
    var lab = ctl.parentElement;
    while (lab && lab.tagName !== 'LABEL' && lab !== A._view) lab = lab.parentElement;
    if (lab && lab.tagName === 'LABEL') {
      var t = A.ownLabelText(lab, ctl);
      if (t) { A.claim(lab); return t; }
    }
    return A.labelFor(ctl) || A.attr(ctl, 'title') || A.attr(ctl, 'placeholder') || '';
  };
  // A wrapping label's words, without the control and any <output> read-out ("Briefing volume" of
  // "Briefing volume [slider] 75%"), so the name does not change as the value does.
  A.ownLabelText = function (lab, ctl) {
    var s = '';
    for (var i = 0; i < lab.childNodes.length; i++) {
      var c = lab.childNodes[i];
      if (c.nodeType === 3) { s += ' ' + c.nodeValue; continue; }
      if (c.nodeType !== 1 || c === ctl || c.contains(ctl) || c.tagName === 'OUTPUT' || A.isControlEl(c) || A.modelHidden(c)) continue;
      s += ' ' + A.modelText(c);
    }
    return A.norm(s);
  };
  // The Flight Planner names a field by its step: "Departure airport", "Arrival airport".
  A.stepTitle = function (el) {
    var s = el;
    while (s && s !== A._view && !A.hasClass(s, 'flight-plan-step-content')) s = s.parentElement;
    if (!s || s === A._view) return '';
    var t = s.querySelector('.flight-plan-step-content-prefix .title');
    return t ? A.modelText(t) : '';
  };
  // The last heading before an element: a dropdown's section ("Aircraft Configuration").
  A.headingBefore = function (el) {
    if (!A._view) return '';
    var hs = A._view.querySelectorAll('h1, h2, h3, h4, h5, h6'), best = '';
    for (var i = 0; i < hs.length; i++) {
      if (A.isHidden(hs[i])) continue;
      if (hs[i].compareDocumentPosition(el) & 4) best = A.lineText(hs[i]);   // 4 = el FOLLOWS the heading
      else break;
    }
    return best;
  };
  A.claim = function (el) { el.__simTabletClaim = A._scrapeId; };
  A.isClaimed = function (el) { return el.__simTabletClaim === A._scrapeId; };

  // ---------------------------------------------------------------------------------
  // "does this subtree hold a control" — one pass per scrape marks every control's ancestors
  // ---------------------------------------------------------------------------------

  A.isControlEl = function (el) {
    var t = el.tagName;
    if (t === 'BUTTON' || t === 'INPUT' || t === 'TEXTAREA' || t === 'SELECT' || t === 'IFRAME') return true;
    if (A.hasClass(el, 'tab-selector') || A.hasClass(el, 'dropdown-button') || A.hasClass(el, 'incrementable-item') ||
      A.hasClass(el, 'checkpoint-item') || A.hasClass(el, 'switch-container') || A.hasClass(el, 'facility-result-item')) return true;
    // A slider is a control of its own only where no text box beside it takes its value; its bar
    // (which holds the mousedown) is part of it, never a control apart.
    if (A.hasClass(el, 'slider')) return !!el.querySelector('.slider-bar') && !A.sliderHasBox(el);
    if (A.hasClass(el, 'slider-bar') || A.hasClass(el, 'slider-button') || A.hasClass(el, 'slider-rail') || A.hasClass(el, 'slider-track')) return false;
    return A.isPressable(el);
  };
  // A hidden control does not count: it is not on screen, and it must not split the row around it
  // ("340 LBS - 2/4 seats" holds a hidden icon button) into a line per word.
  A.markControls = function (root) {
    var all = root.getElementsByTagName('*');
    for (var i = 0; i < all.length; i++) {
      if (!A.isControlEl(all[i]) || A.isHidden(all[i])) continue;
      var n = all[i].parentElement;
      while (n && n.__simTabletCtl !== A._scrapeId) { n.__simTabletCtl = A._scrapeId; if (n === root) break; n = n.parentElement; }
    }
  };
  A.hasCtl = function (el) { return el.__simTabletCtl === A._scrapeId; };

  // ---------------------------------------------------------------------------------
  // the page
  // ---------------------------------------------------------------------------------

  // The app on screen. Apps stay mounted once opened, hidden behind the shown one. In an app's own
  // frame, the whole document.
  A.shownView = function () {
    if (!A.inEfb()) return document.body;
    var vs = A.root().querySelectorAll('.efb-container > .efb-view');
    for (var i = 0; i < vs.length; i++) if (!A.isHidden(vs[i])) return vs[i];
    return null;
  };
  A.viewKey = function (v) {
    var c = A.norm(A.cls(v)).split(' ');
    for (var i = 0; i < c.length; i++) if (c[i] && c[i] !== 'efb-view' && c[i] !== 'hidden') return c[i];
    return '';
  };

  A.APP_FALLBACK = {
    '_HomePageApp': 'Home', 'AtlasApp': 'Flight Planner', 'AircraftApp': 'Aircraft', 'PilotBookApp': 'Pilot book',
    'SettingsApp': 'Settings', 'GSApp': 'Ground Services', 'CivaApp': 'CIVA INS'
  };
  // The app's own name from the EFB API. An app's view is named by its internalName, which an add-on
  // built with a minifier no longer shares with its class ("NavigraphSimBriefApp" is class "Wp"). A
  // sim app's name is a translation key ("@fs-base-efb,TT:EFB.COMMON.AIRCRAFT" -> "aircraft"),
  // capitalised; an add-on's is its own ("iniJetways"), kept as written. The home screen's is
  // "more apps", so it keeps "Home".
  A.appName = function (key) {
    if (key === '_HomePageApp') return 'Home';
    try {
      var api = window.EFB_API, apps = api && api._installedApps && api._installedApps.array;
      for (var i = 0; apps && i < apps.length; i++) {
        var ik = '';
        try { ik = apps[i].internalName; } catch (e0) {}
        if (ik !== key && !(apps[i].constructor && apps[i].constructor.name === key)) continue;
        var n = apps[i].name;
        if (!n || typeof n !== 'string') continue;
        if (n.indexOf('TT:') < 0) return A.norm(n);
        var t = n;
        if (typeof Utils !== 'undefined' && Utils.Translate) t = Utils.Translate(n);
        if (t && t.indexOf('TT:') < 0) return A.ucfirst(A.norm(t));
      }
    } catch (e) {}
    if (A.APP_FALLBACK[key]) return A.APP_FALLBACK[key];
    return key.replace(/App$/, '').replace(/^_+/, '').replace(/([a-z])([A-Z])/g, '$1 $2');
  };

  // "Aircraft, Mass and balance": the app plus the title of the page it shows, when it has one.
  A.currentPage = function () {
    var v = A.shownView();
    if (!v) return '';
    if (!A.inEfb()) {
      var dt = A.norm(document.title);
      if (dt) return dt;
      var h = document.querySelector('h1');
      return h ? A.lineText(h) : '';
    }
    var app = A.appName(A.viewKey(v));
    var titles = v.querySelectorAll('.text--title, h1, .atlas-view-title, .checklist-menu-header .title');
    for (var i = 0; i < titles.length; i++) {
      if (A.isHidden(titles[i])) continue;
      var t = A.lineText(titles[i]);
      if (t && t.toLowerCase() !== app.toLowerCase()) return app + ', ' + A.ucfirst(t);
      if (t) return app;
    }
    // A page with no title: by its view's class ("AircraftInformationPage" -> "Aircraft information").
    var pages = v.querySelectorAll('.ui-view.page');
    for (var j = 0; j < pages.length; j++) {
      if (A.isHidden(pages[j])) continue;
      var c = A.norm(A.cls(pages[j])).split(' ');
      for (var k = 0; k < c.length; k++) {
        if (!/Page$/.test(c[k]) || /Main/.test(c[k])) continue;
        var words = c[k].replace(/Page$/, '').replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
        return app + ', ' + A.ucfirst(words);
      }
    }
    return app;
  };

  // ---------------------------------------------------------------------------------
  // building blocks: tried on every element before the generic rules; the first match emits for
  // the element and owns its whole subtree. Order is priority.
  // ---------------------------------------------------------------------------------

  A.BLOCKS = [];
  A.block = function (name, match, emit) { A.BLOCKS.push({ name: name, match: match, emit: emit }); };
  // A block whose emit returns false declines the element (it found nothing of its own) and the next
  // block, or the plain walk, reads it.
  A.runBlocks = function (el, els) {
    for (var i = 0; i < A.BLOCKS.length; i++) {
      if (A.BLOCKS[i].match(el) && A.BLOCKS[i].emit(el, els) !== false) return true;
    }
    return false;
  };

  // An OS slider whose value a text box beside it already takes (a load's quick fill, Settings'
  // brightness): the box is the control, the slider adds nothing.
  A.sliderHasBox = function (el) {
    var p = el.parentElement;
    return !!p && !!p.querySelector('input:not([type="range"]):not([type="hidden"]):not([type="checkbox"]):not([type="radio"]), textarea');
  };

  // Decorations and pieces another control already reads.
  A.block('skip',
    function (el) {
      return A.hasClass(el, 'eye-button-container') ||        // checklist "Look at": a camera move, nothing to hear
        A.hasClass(el, 'flight-plan-step-dot') ||             // the route's timeline dots
        A.hasClass(el, 'waypoint-list-header') ||             // read into each waypoint row,
        A.hasClass(el.firstElementChild, 'waypoint-list-header') ||   // and so is the pair's wrapper (live)
        A.hasClass(el, 'slider') && !A.hasClass(el, 'tank') && (!el.querySelector('.slider-bar') || A.sliderHasBox(el)) ||
        A.hasClass(el, 'aircraft-shape') || A.hasClass(el, 'nav-map') || A.hasClass(el, 'map-container') ||
        A.hasClass(el, 'disabled-layer') || A.hasClass(el, 'tooltip') ||
        (el.tagName === 'INPUT' && (el.type || '').toLowerCase() === 'hidden');
    },
    function () {});

  // The home screen's launcher: an icon button with the app's name under it.
  A.block('app-button',
    function (el) { return A.hasClass(el, 'app-button'); },
    function (el, els) {
      var b = el.querySelector('button');
      if (!b) return;
      var name = A.modelText(el.querySelector('.app-title') || el);
      // The launcher tooltip only shows (no forceHide) on an app the loaded aircraft cannot use.
      var tip = A.tooltipOf(el);
      var off = !!tip || A.isDisabled(b);
      els.push(A.button(b, name + (off ? ' (not available on this aircraft)' : ''), { disabled: off }));
    });

  // The Pilot book's flight hours chart: per bar, the hours above an empty bar and the period below
  // it, read as one line, the period first, with the exact time a hover shows ("15/11: 0h, 00h00");
  // the current day, week or month says so.
  A.block('chart-stick',
    function (el) { return A.hasClass(el, 'flight-hours-stick'); },
    function (el, els) {
      var period = el.querySelector('.period'), dur = el.querySelector('.flight-duration-display');
      if (!period || !dur) return false;
      var exact = dur.querySelector('.date-display-container');
      var shown = '';
      for (var i = 0; i < dur.childNodes.length; i++) if (dur.childNodes[i] !== exact) shown += ' ' + (dur.childNodes[i].textContent || '');
      shown = A.norm(shown);
      var ex = exact ? A.modelText(exact) : '';
      var stick = el.querySelector('.stick');
      var t = A.lineText(period) + (stick && A.hasClass(stick, 'current') ? ' (current)' : '') + ': ' +
        (shown && ex && ex !== shown ? shown + ', ' + ex : shown || ex);
      els.push(A.el(el, { kind: 'static', text: t }));
    });

  // Mass and balance's Seats / Cargo / Fuel / Empty strip.
  A.block('tab-selector',
    function (el) { return A.hasClass(el, 'tab-selector'); },
    function (el, els) {
      var t = A.modelText(el.querySelector('.text') || el);
      var on = A.hasClass(el, 'active');
      els.push(A.el(el, { kind: 'tab', clickable: true, text: t + (on ? ' (selected)' : ''), disabled: A.isDisabled(el),
                          key: 'tab:' + t, announceChange: true }));
    });

  // A row of exclusive buttons (TAKE OFF / LANDING, METAR / MANUAL, a unit choice).
  // Its items are <button>s, or pressable divs (the Flight Planner's map types: "selectable-button").
  A.block('choice',
    function (el) { return A.hasClass(el, 'multiple-buttons'); },
    function (el, els) {
      var bs = el.getElementsByTagName('button');
      if (!bs.length) {
        var all = el.getElementsByTagName('*'), items = [];
        for (var a = 0; a < all.length; a++) {
          if (!A.isPressable(all[a])) continue;
          var inner = false;
          for (var q = 0; q < items.length; q++) if (items[q].contains(all[a])) { inner = true; break; }
          if (!inner) items.push(all[a]);
        }
        bs = items;
      }
      var pushed = 0;
      for (var i = 0; i < bs.length; i++) {
        if (A.isHidden(bs[i])) continue;
        var t = A.lineText(bs[i]) || A.buttonLabel(bs[i]);
        var on = A.hasClass(bs[i], 'selected');
        els.push(A.button(bs[i], t + (on ? ' (selected)' : ''), { key: 'choice:' + t, announceChange: true }));
        pushed++;
      }
      if (!pushed) return false;             // nothing to pick: the row is read as it is
    });

  // A dropdown. Two kinds: an action menu (Import / Save / Send: its list holds BUTTONS, class
  // dropdown-button-list-buttons) reads as one button per action under the menu's name; a choice
  // (flaps, a runway, a checklist level) reads as a select whose options are the list items, which
  // are read while the list is closed and picked by clicking the item (A.setValue).
  A.block('dropdown',
    function (el) { return A.hasClass(el, 'dropdown-button'); },
    function (el, els) {
      var list = el.querySelector('.dropdown-button-list');
      var items = list ? list.querySelectorAll('.dropdown-button-list-item') : [];
      var sel = el.querySelector('.dropdown-button-selector');
      var shown = sel ? A.modelText(sel.querySelector('.text') || sel) : '';
      if (list && A.hasClass(list, 'dropdown-button-list-buttons')) {
        var menu = A.tooltipOf(el) || shown || A.iconName(sel || el) || 'Menu';
        for (var i = 0; i < items.length; i++) {
          els.push(A.button(items[i], A.ucfirst(menu) + ': ' + A.modelText(items[i]), { key: 'menu:' + menu + ':' + i }));
        }
        return;
      }
      var opts = [];
      for (var j = 0; j < items.length; j++) opts.push(A.modelText(items[j]));
      var step = A.stepTitle(el);
      var name = step ? step + (A.hasClass(el.parentElement, 'runway-selector') ? ' runway' : '') : '';
      var value = A.pickOption(shown, opts);
      // A selector that prefixes its choice names itself: "level standard" is the Level dropdown.
      var lead = '';
      if (value && shown.length > value.length && shown.toLowerCase().slice(-value.length) === value.toLowerCase()) lead = A.norm(shown.slice(0, -value.length));
      // Last, the row's own name: a caption cut off from the dropdown by another control in its row
      // (the Flight Planner's "Cruise altitude" | Auto | FL/FT | 450).
      els.push(A.el(el, { controlType: 'select', text: name || A.labelFor(el) || A.tooltipOf(el) || A.ucfirst(lead) ||
                                                        A.rowContext(el, '', true) || A.headingBefore(el) || 'Choice',
                          value: value, options: opts, disabled: sel ? A.isDisabled(sel) : false }));
    });
  // The selector shows a short form of the choice ("Dry" for "Dry runway", "level standard" for
  // "Standard"); the select's value has to be one of its options.
  A.pickOption = function (shown, opts) {
    var s = shown.toLowerCase(), i;
    for (i = 0; i < opts.length; i++) if (opts[i].toLowerCase() === s) return opts[i];
    for (i = 0; i < opts.length; i++) if (opts[i] && (opts[i].toLowerCase().indexOf(s) >= 0 || s.indexOf(opts[i].toLowerCase()) >= 0)) return opts[i];
    return shown;
  };

  // A load: a seat, a cargo hold, a fuel tank. Its name, limit and number box live in a detail
  // panel that opens over it (hidden, class "hide"), read anyway; the number box takes a typed
  // value with no focus (measured: Co-Pilot 170 -> 160 moved the total 2079 -> 2069 LBS).
  A.block('load',
    function (el) { return A.hasClass(el, 'incrementable-item'); },
    function (el, els) {
      var inp = el.querySelector('.incremental input');
      var title = A.modelText(el.querySelector('.item-title') || el);
      var max = '', unit = '';
      var tots = el.querySelectorAll('.item-description .numberunit-num');
      for (var i = 0; i < tots.length; i++) {
        var holder = tots[i].parentElement;
        if (holder && A.modelHidden(holder)) continue;
        max = A.norm(tots[i].textContent);
        var u = holder ? holder.querySelector('.numberunit-unit-small') : null;
        unit = u ? A.norm(u.textContent) : '';
        break;
      }
      var lock = el.querySelector('.item-lock-icon');
      var locked = !!lock && !A.modelHidden(lock);
      var label = title + (unit || max ? ' (' + [unit, max ? 'max ' + max : ''].join(', ').replace(/^, |, $/g, '') + ')' : '');
      if (!inp) { els.push(A.el(el, { kind: 'static', text: label })); return; }
      els.push(A.el(inp, { controlType: 'text', text: label, value: String(inp.value == null ? '' : inp.value),
                           disabled: locked || A.isDisabled(inp) }));
    });

  // A checklist line: a tick box with the item, what it should be, and a hint.
  A.block('checkpoint',
    function (el) { return A.hasClass(el, 'checkpoint-item'); },
    function (el, els) {
      var b = el.querySelector('.checkpoint-button');
      if (!b) return;
      var subj = A.modelText(el.querySelector('.subject') || b);
      var exp = A.modelText(el.querySelector('.expectation') || document.createElement('i'));
      var clue = A.modelText(el.querySelector('.clue') || document.createElement('i'));
      var box = el.querySelector('.checkbox');
      var text = subj + (exp ? ': ' + exp : '') + (clue ? '. ' + clue : '');
      els.push(A.el(b, { controlType: 'checkbox', text: text, value: box && A.hasClass(box, 'checkbox--checked') ? 'true' : 'false',
                         disabled: A.isDisabled(b) }));
    });

  // An on/off switch, captioned by the text before it.
  A.block('switch',
    function (el) { return A.hasClass(el, 'switch-container'); },
    function (el, els) {
      els.push(A.el(el, { controlType: 'checkbox', text: A.labelFor(el) || 'Switch',
                          value: A.hasClass(el, 'checked') ? 'true' : 'false', disabled: A.isDisabled(el) }));
    });

  // A checklist group header that folds its checklists away.
  A.block('accordion',
    function (el) { return el.tagName === 'BUTTON' && A.hasClass(el, 'accordion-button'); },
    function (el, els) {
      var t = A.lineText(el.querySelector('.title') || el);
      var content = el.nextElementSibling;
      var open = !!content && !A.isHidden(content);
      els.push(A.button(el, t + (open ? ' (expanded)' : ' (collapsed)'), { key: 'accordion:' + t, announceChange: true }));
    });

  // A search box: the field, then its results. The results list only SHOWS while the box has
  // focus, which the reader never gives it, so the results are read while hidden and pressed by
  // mousedown, the one event they answer (FacilityResultItem).
  A.block('search',
    function (el) { return A.hasClass(el, 'search-bar'); },
    function (el, els) {
      var inp = el.querySelector('input');
      if (inp) {
        els.push(A.el(inp, { controlType: 'text', text: A.stepTitle(el) || A.labelFor(el) || inp.getAttribute('placeholder') || 'Search',
                             value: String(inp.value == null ? '' : inp.value), disabled: A.isDisabled(inp) }));
      }
      var clear = el.querySelector('.delete-text-button');
      if (clear && !A.modelHidden(clear) && !A.isHidden(clear)) els.push(A.button(clear, 'Clear'));
      var list = el.querySelector('.search-bar-list');
      if (!list || !inp || !A.norm(inp.value)) return;
      for (var i = 0; i < list.children.length; i++) {
        var r = list.children[i];
        var t = A.modelText(r);
        if (t) els.push(A.button(r, 'Result: ' + t));
      }
    });

  // The OS slider (Settings, AtmosphereMod): a bar the pilot drags. Its position is the
  // --ratio-value style, in percent of its travel, which is not the app's own value (AtmosphereMod's
  // haze slider sits at 50 % of travel for "100 %"). So when the row shows the value next to it, the
  // slider reads as a text box holding that value, and a typed value is found along the bar
  // (A.setSlider); with no such read-out it is a 0-100 range of its travel.
  A.ratioOf = function (el) {
    var m = /--ratio-value:\s*(-?[\d.]+)%/.exec(el.getAttribute('style') || '');
    return m ? parseFloat(m[1]) : null;
  };
  A.sliderReadout = function (el) {
    var node = el;
    for (var up = 0; up < 3 && node && node !== A._view; up++) {
      var sib = node.nextElementSibling;
      for (var hop = 0; sib && hop < 2; hop++, sib = sib.nextElementSibling) {
        if (A.isHidden(sib) || A.hasCtl(sib) || A.isControlEl(sib)) continue;
        var t = A.lineText(sib);
        if (t && t.length <= 24 && /\d/.test(t)) return sib;
      }
      node = node.parentElement;
    }
    return null;
  };
  A.block('os-slider',
    function (el) { return A.hasClass(el, 'slider') && !!el.querySelector('.slider-bar'); },
    function (el, els) {
      var row = el.parentElement, name = '';
      for (var up = 0; up < 3 && row && !name && row !== A._view; up++, row = row.parentElement) {
        name = A.labelFor(up === 0 ? el : row);
        if (!name) {
          var nm = row.querySelector('[class*="name"], [class*="title"], [class*="label"]');
          if (nm && !nm.contains(el) && !A.isHidden(nm)) { name = A.lineText(nm); if (name) A.claim(nm); }
        }
      }
      name = name || 'Slider';
      var ro = A.sliderReadout(el), ratio = A.ratioOf(el);
      if (ro) {
        A.claim(ro);
        els.push(A.el(el, { controlType: 'text', text: name + ' (slider)', value: A.lineText(ro), disabled: A.isDisabled(el),
                            key: 'slider:' + name }));
        return;
      }
      // A slider whose position the page does not show takes a typed percentage: a range would start
      // at its minimum and move the slider there on the first arrow press.
      if (ratio == null) {
        els.push(A.el(el, { controlType: 'text', text: name + ' (slider, percent)', value: '', disabled: A.isDisabled(el), key: 'slider:' + name }));
        return;
      }
      els.push(A.el(el, { controlType: 'range', text: name, value: String(Math.round(ratio)),
                          min: 0, max: 100, step: 1, disabled: A.isDisabled(el), key: 'slider:' + name }));
    });

  // A browser's own form controls, as add-on apps use them (UTL Ride Advisor, SayIntentions).
  A.DATE_HINT = { date: 'year-month-day', time: 'hours:minutes', 'datetime-local': 'year-month-dayThours:minutes', month: 'year-month' };
  A.block('native-select',
    function (el) { return el.tagName === 'SELECT'; },
    function (el, els) {
      var opts = [], val = '';
      for (var i = 0; i < el.options.length; i++) {
        var t = A.norm(el.options[i].textContent || el.options[i].label || el.options[i].value);
        opts.push(t);
        if (i === el.selectedIndex) val = t;
      }
      els.push(A.el(el, { controlType: 'select', text: A.fieldName(el) || 'Choice', value: val, options: opts, disabled: A.isDisabled(el) }));
    });
  A.block('native-input',
    function (el) { return el.tagName === 'INPUT' && !A.hasClass(el.parentElement, 'textbox'); },
    function (el, els) {
      var ty = (el.type || 'text').toLowerCase(), name = A.fieldName(el), dis = A.isDisabled(el);
      if (ty === 'checkbox' || ty === 'radio') {
        els.push(A.el(el, { controlType: 'checkbox', text: (name || (ty === 'radio' ? 'Option' : 'Checkbox')) + (ty === 'radio' ? ' (one of a group)' : ''),
                            value: el.checked ? 'true' : 'false', disabled: dis }));
        return;
      }
      if (ty === 'range') {
        var mn = parseFloat(el.min), mx = parseFloat(el.max), st = parseFloat(el.step);
        els.push(A.el(el, { controlType: 'range', text: name || 'Slider', value: String(el.value),
                            min: isNaN(mn) ? 0 : mn, max: isNaN(mx) ? 100 : mx, step: isNaN(st) ? 1 : st, disabled: dis }));
        return;
      }
      if (ty === 'button' || ty === 'submit' || ty === 'reset' || ty === 'image') {
        els.push(A.button(el, A.buttonLabel(el) || 'Button'));
        return;
      }
      if (ty === 'file') return;
      var hint = '';
      if (ty === 'number' && (el.min !== '' || el.max !== '')) hint = ' (' + (el.min !== '' ? el.min : '') + ' to ' + (el.max !== '' ? el.max : '') + ')';
      else if (A.DATE_HINT[ty]) hint = ' (' + A.DATE_HINT[ty] + ')';
      els.push(A.el(el, { controlType: 'text', text: (name || 'Field') + hint, value: String(el.value == null ? '' : el.value),
                          disabled: dis || el.readOnly === true }));
    });

  // Any other text box (a load's quick-fill %, a wind or QNH box, the route string area).
  A.block('textbox',
    function (el) { return A.hasClass(el, 'textbox') || el.tagName === 'TEXTAREA' || el.tagName === 'INPUT'; },
    function (el, els) {
      var inp = (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') ? el : el.querySelector('input, textarea');
      if (!inp) return;
      var suffix = el.querySelector ? el.querySelector('.suffix') : null;
      var unit = suffix && !A.hasClass(suffix, 'suffix--hidden') ? A.lineText(suffix) : '';
      // The caption beside the box or just above it; a box that has none there but has a placeholder
      // is named by that, never by a line two rows up (AtmosphereMod's preset name box sits under
      // the "Active: unsaved settings" line and reads "A name for your settings").
      var at = A.hasClass(el.parentElement, 'unitbox') ? el.parentElement : el;
      var ph = inp.getAttribute('placeholder');
      var label = A.labelFor(at, 2) || (ph ? '' : A.labelFor(at)) ||
        (inp.tagName === 'TEXTAREA' ? A.fieldName(inp) : '') || inp.getAttribute('aria-label') || ph || A.rowContext(at, '', true) || 'Value';
      els.push(A.el(inp, { controlType: 'text', text: label + (unit ? ' (' + unit + ')' : ''),
                           value: String(inp.value == null ? '' : inp.value), disabled: A.isDisabled(inp) }));
    });

  // A Flight Planner waypoint: its ident, then each column that holds a value, named by the
  // list's header ("SOONE: Hdg 138°, Dist 24.6 NM"). Placeholder dashes are skipped.
  A.block('waypoint',
    function (el) { return A.hasClass(el, 'waypoint-row'); },
    function (el, els) {
      var title = A.modelText(el.querySelector('.title') || el);
      var header = null, sub = null, p = el;
      for (var up = 0; up < 6 && p && !header; up++) {
        p = p.parentElement;
        if (p && p.querySelector) { header = p.querySelector('.waypoint-list-header:not(.sub-header)'); sub = p.querySelector('.waypoint-list-header.sub-header'); }
      }
      var cols = ['ete', 'speed', 'altitude', 'hdg', 'distance'], bits = [];
      for (var i = 0; i < cols.length; i++) {
        var cell = el.querySelector('.data-item.' + cols[i]);
        if (!cell) continue;
        var v = A.modelText(cell);
        if (!v || /^[-\s:°]*$/.test(v)) continue;
        var hn = header ? header.querySelector('.' + cols[i]) : null;
        var un = sub ? sub.querySelector('.' + cols[i]) : null;
        var name = hn ? A.norm(hn.textContent) : '';
        var unit = un ? A.norm(un.textContent) : '';
        bits.push((name ? name + ' ' : '') + v + (unit && v.indexOf(unit) < 0 && cols[i] !== 'hdg' ? ' ' + unit : ''));
      }
      els.push(A.el(el, { kind: 'static', text: title + (bits.length ? ': ' + bits.join(', ') : '') }));
      var more = el.querySelectorAll('button');
      for (var j = 0; j < more.length; j++) {
        if (A.hasClass(more[j], 'icon-container') || A.isHidden(more[j])) continue;   // the ident's own icon
        els.push(A.button(more[j], title + ' options'));
      }
    });

  // An add-on's own switch: role="switch", or a pressable whose class says on or off (CIVA's
  // "civaefb-bg-toggle-on", MK Airport's "mk-switch--on"). A switch that holds another control is
  // a row, never the switch itself (AtmosphereMod's "atmo-switch" wraps the OS switch).
  A.block('class-switch',
    function (el) {
      if (A.hasCtl(el) || el.tagName === 'INPUT' || el.tagName === 'LABEL') return false;
      var v = A.switchValue(el);
      return v !== null && (A.isPressable(el) || A.attr(el, 'role') !== null);
    },
    function (el, els) {
      var name = A.attr(el, 'aria-label') || A.labelledBy(el) || A.lineText(el) || A.labelFor(el) || A.attr(el, 'title') || A.imgName(el) || 'Switch';
      els.push(A.el(el, { controlType: 'checkbox', text: A.norm(name), value: A.switchValue(el), disabled: A.isDisabled(el) }));
    });

  // An ARIA tab or option list item reads as a tab with its state.
  A.block('aria-tab',
    function (el) { var r = A.attr(el, 'role'); return (r === 'tab') && !A.hasCtl(el); },
    function (el, els) {
      var t = A.buttonLabel(el), st = A.stateOf(el);
      els.push(A.el(el, { kind: 'tab', clickable: true, text: t + (st.length ? ' (' + st.join(', ') + ')' : ''),
                          disabled: A.isDisabled(el), key: 'tab:' + t, announceChange: true }));
    });

  // ---------------------------------------------------------------------------------
  // scrape
  // ---------------------------------------------------------------------------------

  A._scrapeId = 0;

  A.headingLevel = function (el) {
    var t = el.tagName;
    if (t.length === 2 && t.charAt(0) === 'H' && /[1-6]/.test(t.charAt(1))) return parseInt(t.charAt(1), 10);
    var r = A.attr(el, 'role');
    if (r === 'heading') return parseInt(A.attr(el, 'aria-level'), 10) || 2;
    if (A.hasClass(el, 'text--title')) return 1;
    if (A.hasClass(el, 'atlas-view-title') || A.hasClass(el, 'environment-title')) return 2;
    return 0;
  };

  // The visible text of a small subtree as rows, top to bottom: [{c: centre y, t: text}].
  A.rows = function (el) {
    var all = el.getElementsByTagName('*'), nodes = [el], rows = [];
    for (var i = 0; i < all.length; i++) nodes.push(all[i]);
    for (var j = 0; j < nodes.length; j++) {
      var n = nodes[j], own = '';
      for (var k = 0; k < n.childNodes.length; k++) if (n.childNodes[k].nodeType === 3) own += n.childNodes[k].nodeValue;
      if (!A.norm(own) || A.isHidden(n)) continue;
      var r = n.getBoundingClientRect(), c = (r.top + r.bottom) / 2, row = null;
      for (var q = 0; q < rows.length; q++) if (Math.abs(rows[q].c - c) <= 6) { row = rows[q]; break; }
      if (!row) { row = { c: c, nodes: [] }; rows.push(row); }
      row.nodes.push(n);
    }
    rows.sort(function (a, b) { return a.c - b.c; });
    return rows;
  };

  // The popups open in a view: an element whose class ends in "-popup" (route-parsing-popup,
  // saved-flight-plan-popup), or a dialog (role="dialog", aria-modal, a "modal" class), on screen.
  A.isPopup = function (el) {
    var c = A.tokens(el);
    // The OS's own popup views are "ui-view <Name> popup" (Mass and balance's "LOAD AIRCRAFT").
    for (var j = 0; j < c.length; j++) if (c[j].indexOf('__') < 0 && /(^popup|-popup|^modal|-modal|^dialog|-dialog)$/.test(c[j])) return true;
    var r = A.attr(el, 'role');
    if (r === 'dialog' || r === 'alertdialog' || A.attr(el, 'aria-modal') === 'true') return true;
    // A backdrop over most of the page with controls on it (CIVA's options: "civaefb-bg-overlay"),
    // never a layer of controls over a map (the Flight Planner's "map-overlay").
    for (var k = 0; k < c.length; k++) {
      if (c[k].indexOf('__') >= 0 || !/(^|[-_])(bg|backdrop|modal|dialog|scrim|dim)[-_]?overlay$|(^|[-_])(backdrop|scrim)$/.test(c[k])) continue;
      return A.coversView(el) && !!el.querySelector('button, input, select, textarea, [role="button"]');
    }
    return false;
  };
  A.coversView = function (el) {
    var v = A._view, r = el.getBoundingClientRect(), vr = v ? v.getBoundingClientRect() : null;
    return !!vr && vr.width > 0 && r.width >= vr.width * 0.6 && r.height >= vr.height * 0.6;
  };
  A.popups = function (view) {
    var cands = view.querySelectorAll('[class*="popup"], [class*="modal"], [class*="dialog"], [class*="overlay"], [class*="backdrop"], [class*="scrim"], [role="dialog"], [role="alertdialog"], [aria-modal="true"]'), out = [];
    for (var i = 0; i < cands.length; i++) {
      if (!A.isPopup(cands[i]) || A.isHidden(cands[i])) continue;
      var inner = false;
      for (var k = 0; k < out.length; k++) if (out[k].contains(cands[i])) { inner = true; break; }
      if (!inner) out.push(cands[i]);
    }
    return out;
  };
  // Its title: the header's title, else the first short line whose class names a title
  // ("mb-popup-title"), else its first heading.
  A.popupTitle = function (p) {
    var cands = [p.querySelector('[class*="popup-header"] .title'), p.querySelector('[class*="header"] [class*="title"]')];
    var ts = p.querySelectorAll('[class*="title"]');
    for (var i = 0; i < ts.length && i < 6; i++) cands.push(ts[i]);
    cands.push(p.querySelector('h1, h2, h3, [role="heading"]'));
    for (var k = 0; k < cands.length; k++) {
      var t = cands[k];
      if (!t || A.isHidden(t) || A.hasCtl(t) || A.isControlEl(t)) continue;
      var s = A.lineText(t);
      if (s && s.length <= 80) return t;
    }
    return null;
  };

  // A run of text on one line (a label and its value, a number and its unit, the per-character
  // date) reads as ONE line, never a word per element.
  // Asked only of a small subtree: a big one is a section, never a line, and measuring every box in
  // it on every level of the walk would make the Flight Planner's scrape slow.
  A.ONE_LINE_MAX_NODES = 30;
  A.isOneLine = function (el) {
    var min = 1e9, max = -1e9, any = false;
    var all = el.getElementsByTagName('*');
    if (all.length > A.ONE_LINE_MAX_NODES) return false;
    var nodes = [el];
    for (var i = 0; i < all.length; i++) nodes.push(all[i]);
    for (var j = 0; j < nodes.length; j++) {
      var n = nodes[j], own = false;
      for (var k = 0; k < n.childNodes.length; k++) if (n.childNodes[k].nodeType === 3 && A.norm(n.childNodes[k].nodeValue)) { own = true; break; }
      if (!own || A.isHidden(n)) continue;
      var r = n.getBoundingClientRect(), c = (r.top + r.bottom) / 2;
      any = true;
      if (c < min) min = c;
      if (c > max) max = c;
    }
    return any && max - min <= 8;
  };

  // Buttons that say the same thing ("LOAD" beside each preset, "CHANGE" on every hotkey row, "VIEW"
  // under Terms and under Privacy) each say which row they are on: the first line of text before
  // the button in the nearest container that has one ("Default: LOAD", "Mod on/off: CHANGE"). Left
  // as they are when that does not tell them apart.
  // `skip`: the control's own name, which its caption repeats (a "Manual" tick box's "Manual").
  // `claim`: the text names a field, which reads it with itself, so its line is dropped (A.labelFor).
  A.rowContext = function (b, skip, claim) {
    var anc = b.parentElement;
    for (var up = 0; up < 4 && anc && anc !== A._view; up++, anc = anc.parentElement) {
      var all = anc.getElementsByTagName('*');
      if (all.length > 200) return '';
      for (var i = 0; i < all.length; i++) {
        var n = all[i];
        if (n === b) break;
        if (n.contains(b) || A.isHidden(n)) continue;
        var own = '';
        for (var k = 0; k < n.childNodes.length; k++) if (n.childNodes[k].nodeType === 3) own += n.childNodes[k].nodeValue;
        if (!A.norm(own)) continue;
        var line = A.norm(own);
        if (line === skip) continue;
        if (!A.isNameText(line)) continue;                   // a name, never a value ("29", "GAL", "14947 L")
        var inCtl = false;
        for (var p = n; p && p !== anc; p = p.parentElement) if (A.isControlEl(p)) { inCtl = true; break; }
        if (!inCtl) {
          // Only a caption in the control's own row is the field's: a heading, or text further up,
          // heads a section and stays a line of its own.
          if (claim && up < 2 && !/^H[1-6]$/.test(n.tagName) && A.attr(n, 'role') !== 'heading') A.claim(n);
          return line;
        }
      }
    }
    return '';
  };
  // The whole row's own words, when its first words repeat from row to row (the Flight Planner's
  // frequencies: "DUBAI 118.350", "DUBAI 121.650"): every text in the button's nearest container that
  // has text of its own, outside any control. Only the text says it, never the key, so a number in it
  // can change freely.
  A.rowLine = function (b, skip) {
    var anc = b.parentElement;
    for (var up = 0; up < 4 && anc && anc !== A._view; up++, anc = anc.parentElement) {
      var all = anc.getElementsByTagName('*'), parts = [];
      if (all.length > 200) return '';
      for (var i = 0; i < all.length; i++) {
        var n = all[i];
        if (n === b || b.contains(n) || n.contains(b) || A.isHidden(n)) continue;
        var own = '';
        for (var k = 0; k < n.childNodes.length; k++) if (n.childNodes[k].nodeType === 3) own += n.childNodes[k].nodeValue;
        if (!A.norm(own) || A.norm(own) === skip) continue;
        var inCtl = false;
        for (var p = n; p && p !== anc; p = p.parentElement) if (A.isControlEl(p)) { inCtl = true; break; }
        if (!inCtl) parts.push(A.norm(own));
      }
      if (parts.length) return parts.join(' ');
    }
    return '';
  };
  // Words that name a row ("Left", "Default", a translated name): never a number with its unit
  // ("14947 L"), nor a unit alone in capitals ("GAL", "KG").
  A.isNameText = function (s) {
    if (!/[^\s\d.,:;!?()\[\]\-+\/%°]{2,}/.test(s)) return false;
    return !/^[\d.,\s\-]+\S{0,4}$/.test(s) && !/^[A-Z%°]{1,4}$/.test(s);
  };
  // The repeats are found by the button's NAME, without the state it shows, so a selected row's
  // button stays in its group; and only its text gains the row: its key stays 'button:<name>' (the
  // shell tells repeats apart by their order), so a row whose words change, or a press that changes
  // which row is selected, never rebuilds the node under the screen reader (FPD-4, FPD-5).
  // Fields that say the same thing (the Flight Planner's two "Manual" fuel tick boxes) do the same.
  // Every field is keyed by its own name ('field:Manual'), one or many, so the row's words never
  // reach the key, and a twin appearing or going never rebuilds the one already there (FPD-5).
  A.qualifyRepeats = function (els) {
    var groups = {};
    for (var i = 0; i < els.length; i++) {
      var e = els[i];
      if (!A._nodes[e.idx] || !e.text) continue;
      var base;
      if (e.controlType) {
        if (e.key && e.key.indexOf('field:') !== 0) continue;
        base = e.text;
        e.key = 'field:' + base;
      } else {
        if (e.kind !== 'button') continue;
        var km = /^(button|choice):(.*)$/.exec(e.key || '');
        base = km ? km[2] : e.text;
        if (e.text.indexOf(base) !== 0) continue;
      }
      var gk = (e.controlType ? 'f|' : 'b|') + base;
      (groups[gk] = groups[gk] || []).push(e);
    }
    // First by the row's name, else by its whole line; left alone when neither tells them apart.
    function namesBy(g, t, fn) {
      var names = [], seen = {};
      for (var j = 0; j < g.length; j++) {
        var ctx = fn(A._nodes[g[j].idx], t);
        if (!ctx || ctx === t || seen[ctx]) return null;
        seen[ctx] = 1;
        names.push(ctx);
      }
      return names;
    }
    for (var gk2 in groups) {
      if (!groups.hasOwnProperty(gk2)) continue;
      var g = groups[gk2], t = gk2.slice(2);
      if (g.length < 2) continue;
      var names = namesBy(g, t, A.rowContext) || namesBy(g, t, A.rowLine);
      if (!names) continue;
      for (var m = 0; m < g.length; m++) g[m].text = names[m] + ': ' + g[m].text;
    }
  };

  // An app's own frame: same-origin is read in place; a cross-origin one cannot be reached from this
  // page, so a marker says where it sits and which origin it is, and the client reads it in the
  // frame's own execution context (STAB-14).
  A.originOf = function (src) { var m = /^([a-z][a-z0-9+.-]*:\/\/[^\/?#]+)/i.exec(src || ''); return m ? m[1].toLowerCase() : ''; };
  A.frameDoc = function (f) { try { var d = f.contentDocument; return d && d.body ? d : null; } catch (e) { return null; } };

  A.collect = function () {
    var els = [];
    A._idx = 0;
    A._nodes = {};
    A._scrapeId++;
    A._frameRoots = [];
    A._scrollers = [];
    var efb = A.inEfb();

    // Every scrape restamps from 1 in DOM order; an old stamp left on a node no longer walked
    // would collide with a live one.
    var stale = A.root().querySelectorAll('[' + A.ATTR + ']');
    for (var s = 0; s < stale.length; s++) stale[s].removeAttribute(A.ATTR);

    var root = A.root();
    var view = A.shownView();
    A._view = view;
    // Only what can be on screen is marked: the shown app, the dock, the tour, the status bar.
    if (view) A.markControls(view);
    if (efb) {
      var marks = root.querySelectorAll('.dock, .onboarding-container, .status-bar');
      for (var mk = 0; mk < marks.length; mk++) A.markControls(marks[mk]);
    }

    // The first-run tour, over everything else.
    var tour = efb ? root.querySelector('.onboarding-container') : null;
    if (tour && !A.isHidden(tour)) {
      var step = tour.querySelector('.step');
      if (step && !A.isHidden(step)) {
        var tt = A.lineText(step.querySelector('.title') || step);
        var td = A.lineText(step.querySelector('.description') || step);
        els.push(A.el(step, { kind: 'alert', live: 'assertive', text: tt + (td ? ': ' + td : '') }));
        var tbs = step.querySelectorAll('.actions button');
        for (var a = 0; a < tbs.length; a++) els.push(A.button(tbs[a], A.lineText(tbs[a])));
      }
    }

    // The dock comes first, always: it is the way to every app, whatever page is showing.
    var dock = efb ? root.querySelectorAll('.dock .dock-item') : [];
    for (var d = 0; d < dock.length; d++) {
      if (A.isHidden(dock[d])) continue;
      var dn = A.tooltipOf(dock[d]) || A.iconName(dock[d]);
      var on = A.hasClass(dock[d], 'dock-item--active');
      els.push(A.el(dock[d], { kind: 'tab', clickable: true, text: dn + (on ? ' (current page)' : ''),
                               disabled: A.isDisabled(dock[d]), key: 'dock:' + dn }));
    }

    // Show: once a frame asked for has been missing a full second, never on a view that renders.
    // Hide: while the panel MSFSBA showed still shows. One key for both, so the node keeps its place.
    A._offeredShow = efb ? A.tabletOffer() : '';
    if (A._offeredShow) {
      els.push({ idx: A.SHOW_TABLET_IDX, kind: 'button', clickable: true, value: '', controlType: '',
                 text: A._offeredShow === 'hide' ? A.HIDE_TABLET_LABEL : A.SHOW_TABLET_LABEL,
                 level: 0, live: '', disabled: false, options: null, key: 'action:tablet-panel' });
    }

    if (efb && view && A.viewKey(view) === 'AtlasApp') {
      els.push({ idx: A.SIMBRIEF_IDX, kind: 'button', clickable: true, text: A.SIMBRIEF_LABEL, value: '', controlType: '',
                 level: 0, live: '', disabled: false, options: null, key: 'action:simbrief' });
    }

    var sink = els, walkingPopup = false;

    function walk(el) {
      if (!el || el.nodeType !== 1) return;
      var tag = el.tagName.toLowerCase();
      if (tag === 'svg' || tag === 'canvas' || tag === 'script' || tag === 'style' || tag === 'noscript' || tag === 'template') return;
      if (A.isHidden(el)) return;
      if (el.__simTabletPopup === A._scrapeId && !walkingPopup) return;   // read already, first
      if (tag === 'iframe') {
        var fd = A.frameDoc(el);
        if (fd) {
          if (A._frameRoots.indexOf(fd.body) < 0) A._frameRoots.push(fd.body);
          A.markControls(fd.body);
          walk(fd.body);
          return;
        }
        // The marker carries the frame's origin and its size: the client finds the frame's context
        // by origin, or by its window's size when the page has moved to another origin (STAB-14).
        // clientWidth/Height: the frame's own window size, unlike a box that counts borders and transforms.
        var origin = A.originOf(el.getAttribute('src') || el.src);
        var fw = el.clientWidth || Math.round(el.getBoundingClientRect().width), fh = el.clientHeight || Math.round(el.getBoundingClientRect().height);
        if (origin) sink.push(A.el(el, { kind: 'frame', text: fw + 'x' + fh, value: origin }));
        return;
      }
      if (tag === 'img') {
        // A picture that is a control (CIVA's cog) is named by its alt on the control; a lone
        // picture with alt text is read like text.
        var alt = A.norm(el.getAttribute('alt'));
        if (alt && !A.isPressable(el) && !/^(icon|image|logo|img)$/i.test(alt) && !A.isClaimed(el)) sink.push(A.el(el, { kind: 'static', text: alt }));
        else if (alt && A.isPressable(el)) sink.push(A.button(el, alt));
        return;
      }
      if (A.runBlocks(el, sink)) return;

      var pressable = A.isPressable(el) || (!A.hasCtl(el) && A.ownPointer(el) && !A.isClaimed(el));
      if (pressable && (tag === 'button' || tag === 'a' || (!A.hasCtl(el) && !A.isBig(el)))) {
        var label = A.buttonLabel(el);
        if (!label) {                                          // a nameless icon says nothing; skip it
          for (var bc = 0; bc < el.children.length; bc++) walk(el.children[bc]);
          return;
        }
        // Always keyed by its name, so a button that first shows state ("GPU" -> "GPU (connected)") keeps
        // its node and the screen reader's place on it (FPD-5); spoken after a press only when it can
        // carry state.
        var st = A.stateOf(el);
        sink.push(A.button(el, label + (st.length ? ' (' + st.join(', ') + ')' : ''),
                           { key: 'button:' + label, announceChange: A.stateful(el) }));
        return;
      }

      // A field's caption is read with the field (A.labelFor claimed it).
      if (!A.hasCtl(el) && A.isClaimed(el)) return;

      var lvl = A.headingLevel(el);
      if (lvl > 0 && !A.hasCtl(el)) {
        var ht = A.lineText(el);
        if (ht) sink.push(A.el(el, { kind: 'heading', text: ht, level: lvl }));
        return;
      }

      if (!A.hasCtl(el)) {
        if (A.isOneLine(el)) {
          var lt = A.lineText(el);
          // A panel title wrapped in a container still reads as the heading it is ("Route").
          var inner = el.querySelector('.text--title, .atlas-view-title, h1, h2, h3, h4, h5, h6');
          if (lt && inner) sink.push(A.el(el, { kind: 'heading', text: lt, level: A.headingLevel(inner) || 2 }));
          else if (lt) sink.push(A.el(el, { kind: 'static', text: lt }));
          return;
        }
        // A caption above its value ("Ground Speed" over "0 KT") reads as one line with a colon; two
        // rows that are each a name with its value (the Pilot book's "By day 00h26" over "By night
        // 0h") with a comma.
        var two = el.getElementsByTagName('*').length <= A.ONE_LINE_MAX_NODES ? A.rows(el) : [];
        if (two.length === 2 && el.children.length === 2) {
          var top = A.lineText(el.children[0]), bottom = A.lineText(el.children[1]);
          var pairs = /[A-Za-z].*\s\d\S*$/.test(top) && /^[A-Za-z]/.test(bottom) && /\d/.test(bottom);
          if (top && bottom) { sink.push(A.el(el, { kind: 'static', text: top + (pairs ? ', ' : ': ') + bottom })); return; }
        }
      }

      var own = '';
      for (var k = 0; k < el.childNodes.length; k++) if (el.childNodes[k].nodeType === 3) own += el.childNodes[k].nodeValue + ' ';
      own = A.norm(own);
      if (own && !A.isClaimed(el)) sink.push(A.el(el, { kind: 'static', text: own }));
      var kids = A.childrenInOrder(el), scroller = A.isVirtualScroller(el), sb;
      if (scroller && (sb = A.scrollButtons(el, false))) sink.push(sb);
      for (var c = 0; c < kids.length; c++) walk(kids[c]);
      if (scroller && (sb = A.scrollButtons(el, true))) sink.push(sb);
    }

    // An open popup (the route string box, the saved flight plans, an add-on's dialog) sits over the
    // page and is read FIRST, its title as an alert so it is spoken as it opens; the page follows.
    if (view) {
      var pops = A.popups(view);
      for (var pi = 0; pi < pops.length; pi++) pops[pi].__simTabletPopup = A._scrapeId;
      for (var pj = 0; pj < pops.length; pj++) {
        var pt = A.popupTitle(pops[pj]);
        if (pt) {
          sink.push(A.el(pt, { kind: 'alert', live: 'assertive', text: A.lineText(pt) }));
          A.claim(pt);
        }
        walkingPopup = true;
        try { walk(pops[pj]); } finally { walkingPopup = false; }
      }
      walk(view);
    }

    // A caption is claimed by its field only when the field is reached, after the walk has already
    // read the caption (it comes first on the page). Drop those lines now; the field carries them.
    var kept = [];
    for (var q = 0; q < els.length; q++) {
      var e = els[q];
      if ((e.kind === 'static' || e.kind === 'heading') && A._nodes[e.idx] && A.isClaimed(A._nodes[e.idx])) continue;
      kept.push(e);
    }
    els = kept;

    A.qualifyRepeats(els);

    // The notification bell, when it has something.
    var count = efb ? root.querySelector('.status-bar .notification-count') : null;
    var n = count ? parseInt(A.norm(count.textContent), 10) : 0;
    if (n > 0) els.push(A.el(count, { kind: 'static', text: 'Notifications: ' + n }));
    var notes = efb ? root.querySelectorAll('.status-bar .notification-container .list > *') : [];
    for (var m = 0; m < notes.length; m++) {
      var nt = A.modelText(notes[m]);
      if (nt) els.push(A.el(notes[m], { kind: 'static', text: nt }));
    }

    return els;
  };

  // ---------------------------------------------------------------------------------
  // frames (STAB-15)
  // ---------------------------------------------------------------------------------

  // The tablet's OS runs the app on screen once per animation frame (its view service's update), and
  // the apps read the sim there: Ground Services publishes its doors, services and pushback state from
  // it. Coherent gives the view frames only while something shows it. An aircraft with a tablet of its
  // own shows the sim's nowhere (measured 2026-11-15, iniBuilds A350: 0 frames in 5 s, where the C172
  // that shows it in its cockpit got 30 a second), so the apps freeze: Ground Services said "Door 1
  // Closed" while the door opened, and its press then toggled from that stale state. While the view
  // gets no frames, the reader runs that update once before each read, so the app on screen is
  // current; once frames run, reads add nothing. A press or set runs it unless a frame came in the
  // last 200 ms, which at the client's pace is nearly always: one extra update per press, so the
  // press never acts on a frozen state.
  // Stalled = a frame asked for more than 500 ms ago has not come (one request is kept pending, so at
  // the client's 600 ms poll a view that renders has always answered it); before any request, the
  // very first read counts as stalled.
  A._lastFrame = 0;          // Date.now() when the last frame came
  A._lastTs = 0;             // that frame's own timestamp
  A._pumpTs = 0;             // the last timestamp the reader handed the app
  A._frameAsked = false;
  A._askedAt = 0;
  A.watchFrames = function () {
    if (A._frameAsked) return;
    try {
      A._frameAsked = true;
      A._askedAt = Date.now();
      A.raf(function (ts) { A._lastFrame = Date.now(); A._lastTs = ts || 0; A._frameAsked = false; });
    } catch (e) { A._frameAsked = false; }
  };
  // The browser's own requestAnimationFrame, never the recording hook: a pump must never count as a
  // frame. A page that replaced the function after the hook keeps its own.
  A.raf = function (cb) {
    var st = window.__MSFSBA_RAF;
    var f = st && window.requestAnimationFrame === st.hook ? st.raf : window.requestAnimationFrame;
    return f.call(window, cb);
  };
  // Run the page's queued frame callbacks, with a time on the view's frame clock; ones they queue
  // run too, three rounds at most (an animation loop gets three steps, never a spin).
  A.runFrameCallbacks = function (t) {
    var st = window.__MSFSBA_RAF, n = 0;
    if (!st) return 0;
    for (var round = 0; round < 3; round++) {
      var ids = [];
      for (var k in st.pending) if (st.pending.hasOwnProperty(k)) ids.push(k);
      if (!ids.length) break;
      for (var i = 0; i < ids.length; i++) {
        var f = st.pending[ids[i]];
        if (!f) continue;
        try { f(t); } catch (e) {}
        n++;
      }
    }
    return n;
  };
  A.isStalled = function () {
    return A._frameAsked ? Date.now() - A._askedAt > 500 : A._lastFrame === 0;
  };
  // The tablet panel button (STAB-16): 'show' once a frame asked for has been missing a full second,
  // 'hide' while frames come to a panel MSFSBA showed, else ''. Frames stopping or starting changes
  // no element, so scrape compares this with what the last read offered (A._offeredShow) and reads
  // again when it moved.
  // For 3 s after a press of the toolbar button (window.__MSFSBA_TABLET_PRESS_AT, stamped by the
  // client) it stays offered, by the mark alone: frames start or stop a moment after the press, and
  // the button the pilot just pressed must never leave the list meanwhile.
  A._offeredShow = '';
  A.tabletOffer = function () {
    if (Date.now() - (window.__MSFSBA_TABLET_PRESS_AT || 0) < 3000) return window.__MSFSBA_TABLET_SHOWN ? 'hide' : 'show';
    if (A._frameAsked && Date.now() - A._askedAt > 1000) return 'show';
    return window.__MSFSBA_TABLET_SHOWN ? 'hide' : '';
  };
  // A time on the view's own frame clock: its last frame's timestamp plus the time since, never
  // earlier than one already handed out (an app may time its updates by it).
  A.frameTime = function () {
    var t = A._lastFrame ? A._lastTs + (Date.now() - A._lastFrame)
      : (window.performance && performance.now ? performance.now() : Date.now());
    if (t <= A._pumpTs) t = A._pumpTs + 1;
    A._pumpTs = t;
    return t;
  };
  A.viewService = function () {
    try {
      var api = window.EFB_API, apps = api && api._installedApps && api._installedApps.array;
      for (var i = 0; apps && i < apps.length; i++) {
        var vs = apps[i].options && apps[i].options.efbViewService;
        if (vs && typeof vs.update === 'function') return vs;
      }
    } catch (e) {}
    return null;
  };
  A.PUMP_SLOW_MS = 20;
  // A press or set (always) runs it unless a frame came in the last 200 ms: the pilot's press must act
  // on the app's current state, and one extra update on a press costs nothing.
  A.pumpIfStalled = function (always) {
    if (!A.inEfb()) return;
    var stalled = always ? !(A._lastFrame && Date.now() - A._lastFrame < 200) : A.isStalled();
    A.watchFrames();
    if (!stalled) return;
    // A heavy update (measured once per app) runs before presses and sets only, and on every third
    // read, so a slow app never makes every read slow.
    if (!always && A._pumpMs > A.PUMP_SLOW_MS && (A._pollCount % 3) !== 0) return;
    var vs = A.viewService(), t0 = Date.now(), ft = A.frameTime();
    A.fireMovedScrolls();
    if (vs) { try { vs.update(ft); } catch (e) {} }
    // Then what the page queued for a frame: a list that builds its rows after a scroll, a size
    // measured once laid out (the Pilot book's logbook). A list those moved hears its scroll, and
    // builds its rows in one more round.
    A.runFrameCallbacks(ft);
    if (A.fireMovedScrolls()) A.runFrameCallbacks(ft);
    A._pumpMs = Date.now() - t0;
    // What the update changed counts for this very read, not the next: the observer reports it only
    // after this evaluation ends.
    try { if (A._obs && A._obs.takeRecords && A._obs.takeRecords().length) A._dirty = true; } catch (e2) {}
  };
  A._pumpMs = 0;

  A._dirty = true;
  A._everScraped = false;
  A._pollCount = 0;
  A._obs = null;
  A.OBSERVER_OPTS = { childList: true, subtree: true, characterData: true, attributes: true };
  A.FORCE_FULL_EVERY = 10;
  A._markDirty = function () { A._dirty = true; };

  // The observer is disconnected around collect(): collect() stamps idx attributes itself, and an
  // observer left running would re-arm the dirty flag from the reader's own bookkeeping, so
  // "unchanged" could never fire (the MD-11 reader's SELF-TRIGGER TRAP).
  A.scrape = function (gel) {
    try {
      A.pumpIfStalled();             // a change it brings marks the page dirty through the observer
      if (A.inEfb() && A.tabletOffer() !== A._offeredShow) A._dirty = true;
      A._pollCount++;
      var forceFull = !A._everScraped || (A._pollCount % A.FORCE_FULL_EVERY === 0);
      if (!A._dirty && !forceFull) return JSON.stringify({ ok: true, unchanged: true });
      A._dirty = false;
      A._everScraped = true;
      var page = A.currentPage(), els;
      if (A._obs) { try { A._obs.disconnect(); } catch (e1) {} }
      try { els = A.withListeners(gel, A.collect); }
      finally { A.observeAll(); }
      return JSON.stringify({ ok: true, page: page, elements: els });
    } catch (e) {
      return JSON.stringify({ ok: false, error: String(e && e.message ? e.message : e) });
    }
  };
  // Watch the page and every same-origin frame read in place (A.frameDoc), so a change inside one
  // is not left stale until the next forced full scrape.
  A._frameRoots = [];
  A.observeAll = function () {
    if (!A._obs) return;
    try { if (A.root()) A._obs.observe(A.root(), A.OBSERVER_OPTS); } catch (e1) {}
    for (var i = 0; i < A._frameRoots.length; i++) {
      try { A._obs.observe(A._frameRoots[i], A.OBSERVER_OPTS); } catch (e2) {}
    }
  };

  // ---------------------------------------------------------------------------------
  // input
  // ---------------------------------------------------------------------------------

  A.find = function (idx) {
    var n = A._nodes[idx];
    if (n && n.getAttribute && n.getAttribute(A.ATTR) === String(idx)) return n;
    return document.querySelector('[' + A.ATTR + '="' + idx + '"]');
  };

  // Hand the pilot's keyboard back if the tablet took it. A press can make the OS focus a text box
  // by itself (a box built with focusOnClear), and a focused box has told the sim to send every key
  // to the tablet. The box's own focus-out path (blur + an InputEvent 'focusout') is what tells the
  // sim to stop (UNFOCUS_INPUT_FIELD). Run now and on the next ticks, as the OS focuses after its own
  // debounce.
  A.releaseKeyboard = function () {
    function release() {
      try {
        var a = document.activeElement;
        if (!a || (a.tagName !== 'INPUT' && a.tagName !== 'TEXTAREA')) return;
        if (!A.root().contains(a)) return;
        a.blur();
        var ev;
        try { ev = new InputEvent('focusout'); } catch (e) { ev = document.createEvent('Event'); ev.initEvent('focusout', true, true); }
        a.dispatchEvent(ev);
      } catch (e2) {}
    }
    release();
    setTimeout(release, 0);
    setTimeout(release, 150);
    setTimeout(release, 400);
  };

  A.mouse = function (target, type, x, y) {
    var ev, view = (target.ownerDocument && target.ownerDocument.defaultView) || window;
    try { ev = new MouseEvent(type, { bubbles: true, cancelable: true, view: view, clientX: x || 0, clientY: y || 0 }); }
    catch (e) {
      ev = document.createEvent('MouseEvents');
      ev.initMouseEvent(type, true, true, view, 0, x || 0, y || 0, x || 0, y || 0, false, false, false, false, 0, null);
    }
    target.dispatchEvent(ev);
  };

  // One press, with the one event the element listens to (STAB-2). el.click() is a real click
  // (onclick, or a click listener); an element that answers only mousedown (or only pointerdown,
  // mouseup…) gets that event alone, a <button> too. Never two: a handler that listens to both would
  // run twice, and a mousedown reaching a text box focuses it. A control that shows no listener of its
  // own (a delegated one) gets click(), which bubbles.
  // An element whose click does something of its own (a link opens, a form submits or resets, a
  // <summary> folds) is always clicked, whatever else it listens to.
  A.hasDefaultClick = function (el) {
    var tag = el.tagName, ty = (el.getAttribute('type') || '').toLowerCase();
    if (tag === 'A') return el.getAttribute('href') !== null;
    if (tag === 'SUMMARY') return true;
    if (tag === 'INPUT') return ty === 'submit' || ty === 'reset' || ty === 'image';
    if (tag === 'BUTTON') return !!el.form && (ty === '' || ty === 'submit' || ty === 'reset');
    return false;
  };
  A.press = function (el) {
    var h = A.handlerOf(el);
    if (!h || h === 'click' || A.hasDefaultClick(el)) el.click();
    else if (h === 'touchstart') {
      var te;
      try { te = new Event('touchstart', { bubbles: true, cancelable: true }); } catch (e) { te = document.createEvent('Event'); te.initEvent('touchstart', true, true); }
      el.dispatchEvent(te);
    } else {
      var r = el.getBoundingClientRect();
      A.mouse(el, h, (r.left + r.right) / 2, (r.top + r.bottom) / 2);
    }
    A._dirty = true;
    A.releaseKeyboard();
  };

  // A virtual list (the OS's VirtualList: the Pilot book's logbook) builds only the rows in its window,
  // ten of 135 flights, and builds the others as it scrolls; a sighted pilot scrolls it. The reader
  // puts "Scroll up" before its rows and "Scroll down" after them, saying how far down the list is;
  // a press moves it nine tenths of its height and tells its scroll listener. Their idx is synthetic
  // (A.SCROLL_IDX + 2 x the list's place on the page, + 1 for down), below the client's actions.
  A.SCROLL_IDX = 900000;
  A._scrollers = [];
  A.isVirtualScroller = function (el) {
    if (!(el.clientHeight > 0) || el.scrollHeight <= el.clientHeight + 4) return false;
    if (A.hasClass(el, 'virtual-list-container')) return true;
    for (var i = 0; i < el.children.length; i++) if (A.hasClass(el.children[i], 'virtual-list')) return true;
    return false;
  };
  A.scrollButtons = function (el, down) {
    var n = A._scrollers.indexOf(el);
    if (n < 0) { n = A._scrollers.length; A._scrollers.push(el); }
    if (el.__msfsbaTop === undefined) el.__msfsbaTop = el.scrollTop;
    var max = el.scrollHeight - el.clientHeight;
    if (down ? el.scrollTop >= max - 2 : el.scrollTop <= 2) return null;
    var pct = Math.max(0, Math.min(100, Math.round(el.scrollTop / max * 100)));
    return { idx: A.SCROLL_IDX + 2 * n + (down ? 1 : 0), kind: 'button', clickable: true, value: '', controlType: '',
             text: (down ? 'Scroll down' : 'Scroll up') + ' (list at ' + pct + '%)', level: 0, live: '', disabled: false,
             options: null, key: 'scroll:' + n + (down ? ':down' : ':up') };
  };
  A.scrollStep = function (idx) {
    var k = idx - A.SCROLL_IDX, el = A._scrollers[k >> 1];
    if (!el || !el.parentNode) return false;
    el.scrollTop = Math.max(0, el.scrollTop + ((k & 1) ? 1 : -1) * Math.round(el.clientHeight * 0.9));
    A.fireScroll(el);
    el.__msfsbaTop = el.scrollTop;
    A._dirty = true;
    return true;
  };
  // The browser fires a scroll event at a frame; without frames the list never hears it, so the
  // reader fires it. A press that moves a list itself ("Back to the top") gets the same, after it.
  A.fireScroll = function (el) {
    var ev;
    try { ev = new Event('scroll', { bubbles: false }); } catch (e) { ev = document.createEvent('Event'); ev.initEvent('scroll', false, false); }
    el.dispatchEvent(ev);
  };
  // Each list keeps the position the reader last saw (__msfsbaTop). One that has moved since, by a
  // press, by the page's own animation, whenever it happened, hears its scroll; true when one did.
  A.fireMovedScrolls = function () {
    var moved = false;
    for (var i = 0; i < A._scrollers.length; i++) {
      var s = A._scrollers[i];
      if (!s.parentNode) continue;
      if (s.__msfsbaTop !== undefined && s.scrollTop !== s.__msfsbaTop) { A.fireScroll(s); moved = true; }
      s.__msfsbaTop = s.scrollTop;
    }
    if (moved) A._dirty = true;
    return moved;
  };

  A.clickElement = function (idx, gel) {
    if (idx === A.SIMBRIEF_IDX || idx === A.SHOW_TABLET_IDX) return false;    // the app's own actions: the client runs them
    if (idx >= A.SCROLL_IDX && idx < A.SCROLL_IDX + 2000) {
      return A.withListeners(gel, function () { A.pumpIfStalled(true); var ok = A.scrollStep(idx); A.pumpIfStalled(true); return ok; });
    }
    return A.withListeners(gel, function () {
      A.pumpIfStalled(true);         // the app presses from its current state, never a frozen one
      var el = A.find(idx);
      if (!el || A.isDisabled(el)) return false;
      try { A.press(el); } catch (e) { return false; }
      // What the press set going (a list sent back to its top) runs now, not at the next read; a
      // list it moved hears its scroll.
      A.fireMovedScrolls();
      A.pumpIfStalled(true);
      return true;
    });
  };

  // Type into a box. The OS TextBox reads 'input' on the input itself; a framework one (React,
  // Vue) listens higher up and keeps its own copy of the value, so the value goes in through the
  // element type's own setter and the events bubble. No focus: see the top.
  A.typeInto = function (el, want, alsoChange) {
    var proto = el.tagName === 'TEXTAREA' ? window.HTMLTextAreaElement : window.HTMLInputElement, setter = null;
    try { setter = Object.getOwnPropertyDescriptor(proto.prototype, 'value').set; } catch (e0) {}
    if (setter) setter.call(el, want); else el.value = want;
    A.fire(el, 'input');
    if (alsoChange) A.fire(el, 'change');
  };
  A.fire = function (el, type) {
    var ev;
    try { ev = type === 'input' ? new InputEvent('input', { bubbles: true }) : new Event(type, { bubbles: true }); }
    catch (e1) { ev = document.createEvent('Event'); ev.initEvent(type, true, true); }
    el.dispatchEvent(ev);
  };

  // Move an OS slider: a mousedown on its bar, then a mousemove at the target and a mouseup, both on
  // the document, which is what the slider listens to while dragging (STAB-12). The first move goes
  // one pixel aside, as the slider ignores a move to the x it last saw.
  A.dragSlider = function (el, pct) {
    var bar = el.querySelector('.slider-bar') || el, r = el.getBoundingClientRect();
    var vertical = A.hasClass(el, 'vertical');
    var p = Math.max(0, Math.min(100, pct)) / 100;
    var x = vertical ? (r.left + r.right) / 2 : r.left + r.width * p;
    var y = vertical ? r.bottom - r.height * p : (r.top + r.bottom) / 2;
    var doc = el.ownerDocument || document;
    A.mouse(bar, 'mousedown', x, y);
    if (vertical) A.mouse(doc, 'mousemove', x, y > r.top + 1 ? y - 1 : y + 1);
    else A.mouse(doc, 'mousemove', x > r.left + 1 ? x - 1 : x + 1, y);
    A.mouse(doc, 'mousemove', x, y);
    A.mouse(doc, 'mouseup', x, y);
    A._dirty = true;
  };
  // A number the pilot typed ("0,5" in a comma-decimal language, "−5" with a Unicode minus).
  A.numberIn = function (s) {
    var m = /-?\d+(?:[.,]\d+)?/.exec(String(s == null ? '' : s).replace(/−/g, '-'));
    return m ? parseFloat(m[0].replace(',', '.')) : null;
  };
  // A number the page shows: "1,013 hPa" is a thousand and thirteen (a comma before three digits groups
  // thousands), "0,5" a half.
  A.readoutNumber = function (s) {
    var t = String(s == null ? '' : s).replace(/−/g, '-').replace(/(\d),(\d{3})(?!\d)/g, '$1$2');
    return A.numberIn(t);
  };
  // Set a slider that shows its value beside it: find along the bar where the read-out says the
  // typed number (or the nearest the slider's steps allow). The read-out follows the slider in the
  // same tick (measured on AtmosphereMod), so this runs to its end in one call. Before trusting it,
  // the read-out has to be seen to move with the bar; if it does not (it updates later, it is not this
  // slider's, it is not a number at one end), the slider goes back to where it was and the set is
  // refused (STAB-12). "+5" moves it from where it is; "-5" too, unless the read-out runs below zero,
  // where it is the number minus five.
  A.setSlider = function (el, text) {
    var ro = A.sliderReadout(el), start = A.ratioOf(el);
    var want = A.numberIn(text);
    if (want === null) return false;
    var signed = /^\s*[+\-−]/.test(String(text));
    if (!ro) {                                              // a range of its travel
      if (signed && start === null) return false;           // "+5" from where? Not shown.
      A.dragSlider(el, signed ? start + want : want);
      return true;
    }
    function read() { return A.readoutNumber(A.lineText(ro)); }
    function giveUp() { if (start !== null) A.dragSlider(el, start); return false; }
    var now = read();
    // An end that reads as a word ("Off") is measured just inside it.
    A.dragSlider(el, 0); var lo = read();
    if (lo === null) { A.dragSlider(el, 2); lo = read(); }
    A.dragSlider(el, 100); var hi = read();
    if (hi === null) { A.dragSlider(el, 98); hi = read(); }
    if (now === null || lo === null || hi === null || lo === hi) return giveUp();
    if (signed && (/^\s*\+/.test(String(text)) || Math.min(lo, hi) >= 0)) want = now + want;
    var up = hi >= lo, a = 0, b = 100, best = 100, bestErr = Math.abs(hi - want);
    if (Math.abs(lo - want) < bestErr) { best = 0; bestErr = Math.abs(lo - want); }
    for (var i = 0; i < 14 && bestErr > 0; i++) {
      var mid = (a + b) / 2;
      A.dragSlider(el, mid);
      var v = read();
      if (v === null) return giveUp();
      var err = Math.abs(v - want);
      if (err < bestErr) { bestErr = err; best = mid; }
      if ((v < want) === up) a = mid; else b = mid;
    }
    A.dragSlider(el, best);
    return true;
  };

  A.setValue = function (idx, text, gel) {
    return A.withListeners(gel, function () { A.pumpIfStalled(true); return A.setValueNow(idx, text); });
  };
  // The OS's own text boxes take 'input' alone (STAB-1): the TextBox component and the Flight
  // Planner's route box. A page's own box also commits on 'change'.
  A.isOsBox = function (el) {
    return A.hasClass(el.parentElement, 'textbox') || A.hasClass(el, 'route-parsing-textarea');
  };
  A.setValueNow = function (idx, text) {
    var el = A.find(idx);
    if (!el || A.isDisabled(el) || el.readOnly === true) return false;
    var want = String(text == null ? '' : text);
    var on = want.toLowerCase() === 'true';
    try {
      if (A.hasClass(el, 'dropdown-button')) {
        var items = el.querySelectorAll('.dropdown-button-list-item');
        for (var i = 0; i < items.length; i++) {
          if (A.modelText(items[i]).toLowerCase() !== A.norm(want).toLowerCase()) continue;
          if (A.isDisabled(items[i])) return false;
          A.press(items[i]);
          return true;
        }
        return false;
      }
      if (A.hasClass(el, 'checkpoint-button')) {
        var box = el.querySelector('.checkbox');
        var isOn = !!box && A.hasClass(box, 'checkbox--checked');
        if (isOn === on) return true;
        A.press(el);
        return true;
      }
      if (A.hasClass(el, 'switch-container')) {
        if (A.hasClass(el, 'checked') === on) return true;
        A.press(el);
        return true;
      }
      if (A.hasClass(el, 'slider') && el.querySelector('.slider-bar')) {
        var ok = A.setSlider(el, want);
        A.releaseKeyboard();
        return ok;
      }
      if (el.tagName === 'SELECT') {
        for (var o = 0; o < el.options.length; o++) {
          var ot = A.norm(el.options[o].textContent || el.options[o].label || el.options[o].value);
          if (ot.toLowerCase() !== A.norm(want).toLowerCase() && el.options[o].value !== want) continue;
          if (el.options[o].disabled) return false;
          var ss = null;
          try { ss = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value').set; } catch (e3) {}
          if (ss) ss.call(el, el.options[o].value); else el.selectedIndex = o;
          A.fire(el, 'input');
          A.fire(el, 'change');
          A._dirty = true;
          return true;
        }
        return false;
      }
      if (el.tagName === 'INPUT') {
        var ty = (el.type || 'text').toLowerCase();
        if (ty === 'checkbox' || ty === 'radio') {
          if (el.checked === on) return true;
          if (!on && ty === 'radio') return false;         // a radio is unset by picking another
          el.click();                                      // the browser flips it and fires input + change
          A._dirty = true;
          A.releaseKeyboard();
          return true;
        }
        if (ty === 'range') {
          // A browser puts a range given no number back to its midpoint: refuse instead.
          var n = A.numberIn(want);
          if (n === null) return false;
          A.typeInto(el, String(n), true);
          A._dirty = true;
          return true;
        }
      }
      if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') {
        A.typeInto(el, want, !A.isOsBox(el));
        A._dirty = true;
        A.releaseKeyboard();
        return true;
      }
      if (A.switchValue(el) !== null) {
        if ((A.switchValue(el) === 'true') === on) return true;
        A.press(el);
        return true;
      }
      return false;
    } catch (e) { return false; }
  };

  // ---------------------------------------------------------------------------------
  // the Flight Planner route, driven one step at a time by the app's SimBrief import
  // (SimTabletSimBriefImport). Each call is synchronous and returns JSON; the app waits between
  // them, because the tablet applies a typed value on its next tick and loads airports async.
  // Everything here finds controls by STRUCTURE (classes, positions, icons), never by the English
  // labels, which the OS translates.
  // ---------------------------------------------------------------------------------

  A.route = {};

  A.route.atlas = function () {
    var v = A.shownView();
    return v && A.viewKey(v) === 'AtlasApp' ? v : null;
  };
  A.route.view = function () {
    var v = A.root().querySelector('.efb-view.AtlasApp .view.Route');
    return v || null;
  };

  // Open the Flight Planner and its Route panel. Returns {ok, done}: done once the Route panel is
  // on screen, so the app calls again until it is.
  A.route.open = function () {
    try {
      if (!A.route.atlas()) {
        var dockBtn = document.getElementById('ONBOARDING-TARGET-ATLASAPP');
        if (!dockBtn) return JSON.stringify({ ok: false, error: 'no-flight-planner' });
        A.press(dockBtn);
        return JSON.stringify({ ok: true, done: false });
      }
      var rv = A.route.view();
      if (rv && !A.isHidden(rv)) return JSON.stringify({ ok: true, done: true });
      var imgs = A.route.atlas().querySelectorAll('.nav-bar-icon-button img');
      for (var i = 0; i < imgs.length; i++) {
        if ((imgs[i].getAttribute('src') || '').indexOf('arrow-roadmap') < 0) continue;
        var b = imgs[i].parentElement;
        while (b && b.tagName !== 'BUTTON') b = b.parentElement;
        if (b) { A.press(b); return JSON.stringify({ ok: true, done: false }); }
      }
      return JSON.stringify({ ok: false, error: 'no-route-button' });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e) }); }
  };

  // The Import menu: the route header's dropdown whose selector carries the Folder icon. Its items
  // are LOAD_FROM_WEB, LOAD_PLN_FILE, ENTER_ROUTE_STRING, REQUEST_FROM_AVIONICS, in that fixed order
  // (the Flight Planner's own enum).
  A.route.ENTER_ROUTE_STRING = 2;
  A.route.menu = function (iconNeedle) {
    var rv = A.route.view();
    if (!rv) return null;
    var dds = rv.querySelectorAll('.saved-flight-plan-controls .dropdown-button');
    for (var i = 0; i < dds.length; i++) {
      var ic = dds[i].querySelector('.dropdown-button-selector icon-element');
      if (ic && (ic.getAttribute('icon-url') || '').indexOf(iconNeedle) >= 0) return dds[i];
    }
    return null;
  };

  // Open "Enter route string" and type the route into it. Returns {ok}, {ok, unchanged} or
  // {ok:false, error}: 'locked' when the tablet has the item disabled (its flight plan is locked).
  // The box opens holding the route the Flight Planner already has, and its SAVE stays DISABLED
  // while the text is that same route (measured: re-entering the loaded KFFC-KSAV route left SAVE
  // disabled for good). So when the plan is already in, the box is closed again and the answer is
  // unchanged: there is nothing to save.
  A.route.sameRoute = function (a, b) { return A.norm(a).toUpperCase() === A.norm(b).toUpperCase(); };
  A.route.enter = function (text) {
    try {
      var menu = A.route.menu('Folder');
      if (!menu) return JSON.stringify({ ok: false, error: 'no-import-menu' });
      var item = menu.querySelectorAll('.dropdown-button-list-item')[A.route.ENTER_ROUTE_STRING];
      if (!item) return JSON.stringify({ ok: false, error: 'no-route-string-item' });
      if (A.isDisabled(item)) return JSON.stringify({ ok: false, error: 'locked' });
      A.press(item);
      var ta = A.root().querySelector('.efb-view.AtlasApp .route-parsing-textarea');
      if (!ta) return JSON.stringify({ ok: false, error: 'no-route-box' });
      if (A.route.sameRoute(ta.value, text)) {
        A.route.closeBox();
        return JSON.stringify({ ok: true, unchanged: true });
      }
      ta.value = String(text);
      var ev;
      try { ev = new InputEvent('input'); } catch (e1) { ev = document.createEvent('Event'); ev.initEvent('input', true, true); }
      ta.dispatchEvent(ev);
      A.releaseKeyboard();
      return JSON.stringify({ ok: true });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e) }); }
  };

  // Press the route box's SAVE. Called on a later tick than enter(), once the box's model has the text.
  A.route.save = function () {
    try {
      var b = A.root().querySelector('.efb-view.AtlasApp .route-parsing-popup-button');
      if (!b) return JSON.stringify({ ok: false, error: 'no-save-button' });
      if (A.isDisabled(b)) return JSON.stringify({ ok: false, error: 'save-disabled' });
      A.press(b);
      return JSON.stringify({ ok: true });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e) }); }
  };

  // Close the route box without saving: its header's first button. Used when there is nothing to
  // save, and by the app when SAVE never enabled, so a failed import leaves no popup open.
  A.route.closeBox = function () {
    try {
      var popup = A.root().querySelector('.efb-view.AtlasApp .route-parsing-popup');
      if (!popup || A.isHidden(popup)) return JSON.stringify({ ok: true, done: true });
      var b = popup.querySelector('[class*="popup-header"] button');
      if (!b) return JSON.stringify({ ok: false, error: 'no-close-button' });
      A.press(b);
      return JSON.stringify({ ok: true, done: true });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e) }); }
  };

  // The route's airport steps (departure, arrival, alternate, in that order): every step holding
  // an airport search box.
  A.route.airportSteps = function () {
    var rv = A.route.view(), out = [];
    if (!rv) return out;
    var boxes = rv.querySelectorAll('.airport-selection input');
    for (var i = 0; i < boxes.length; i++) {
      var s = boxes[i];
      while (s && !A.hasClass(s, 'flight-plan-step-content')) s = s.parentElement;
      if (s) out.push({ step: s, input: boxes[i] });
    }
    return out;
  };

  // "runway 01" -> "1", "runway 16L" -> "16L", SimBrief "01" -> "1": the designator, no leading zero.
  A.route.rwyKey = function (s) {
    var m = /(\d{1,2})\s*([LRC]?)\s*$/i.exec(A.norm(s));
    return m ? String(parseInt(m[1], 10)) + m[2].toUpperCase() : '';
  };

  A.route.airport = function (entry) {
    var dd = entry.step.querySelector('.runway-selector .dropdown-button');
    var rw = '', opts = [];
    if (dd && !A.modelHidden(dd.parentElement)) {
      var sel = dd.querySelector('.dropdown-button-selector');
      rw = sel ? A.modelText(sel.querySelector('.text') || sel) : '';
      var items = dd.querySelectorAll('.dropdown-button-list-item');
      for (var i = 0; i < items.length; i++) opts.push(A.modelText(items[i]));
    }
    var box = A.norm(entry.input.value);
    return { text: box, icao: A.norm(box.split(',')[0]).toUpperCase(), runway: rw, runwayKey: A.route.rwyKey(rw), runways: opts };
  };

  // Everything the import reads back, as JSON.
  A.route.state = function () {
    try {
      A.pumpIfStalled(true);         // the import's waits read a current Flight Planner (STAB-15)
      var rv = A.route.view();
      var st = { ok: true, atlas: !!A.route.atlas(), routeShown: !!rv && !A.isHidden(rv), airports: [], procedures: [], waypoints: [] };
      if (!rv) return JSON.stringify(st);
      var ap = A.route.airportSteps();
      for (var i = 0; i < ap.length; i++) st.airports.push(A.route.airport(ap[i]));
      // The procedure steps carry a "selected procedure" row: departure, arrival, approach in order.
      var rows = rv.querySelectorAll('.flight-plan-selected-procedure-row .flight-plan-selected-procedure-name');
      for (var j = 0; j < rows.length; j++) st.procedures.push(A.modelText(rows[j]));
      // En route = the route step with no procedure row.
      var steps = rv.querySelectorAll('.flight-plan-step-content.route');
      for (var k = 0; k < steps.length; k++) {
        if (steps[k].querySelector('.flight-plan-selected-procedure-row')) continue;
        var wps = steps[k].querySelectorAll('.waypoint-row .title');
        for (var w = 0; w < wps.length; w++) st.waypoints.push(A.modelText(wps[w]));
        break;
      }
      var popup = A.root().querySelector('.efb-view.AtlasApp .route-parsing-popup');
      st.routeBoxOpen = !!popup && !A.isHidden(popup);
      var c = A.route.cruise();
      if (c) { st.cruise = c.value; st.cruiseUnit = c.unit; st.cruiseWarnings = c.warnings; }
      return JSON.stringify(st);
    } catch (e) { return JSON.stringify({ ok: false, error: String(e) }); }
  };

  // The plan's cruise altitude as the Flight Planner's Performance panel shows it: that box is bound
  // to the plan itself (it read FL112, then FL030, as the route set it), unlike the Route page's
  // copy, which only shows what was typed into it. The row is the one beside the aircraft's ceiling;
  // its unit is the FL/FT dropdown, and the tablet's cruise warnings ("higher than the ceiling of the
  // aircraft") sit with it. Null until the panel has been built (the Flight Planner builds a panel
  // the first time it opens).
  A.route.cruise = function () {
    var ceiling = A.root().querySelector('.efb-view.AtlasApp .view.Performance .cruise-altitude-ceiling');
    var row = ceiling ? ceiling.parentElement : null;
    if (!row) return null;
    var inp = row.querySelector('.atlas-data-table-row-value input');
    if (!inp) return null;
    var unitEl = row.querySelector('.atlas-data-table-row-dropdown .dropdown-button-selector .text');
    var warnings = [], ws = row.querySelectorAll('.warning');
    for (var i = 0; i < ws.length; i++) {
      if (A.hasClass(ws[i], 'hidden')) continue;
      var t = A.modelText(ws[i].querySelector('span') || ws[i]);
      if (t) warnings.push(t);
    }
    return { value: A.norm(inp.value), unit: unitEl ? A.norm(unitEl.textContent).toUpperCase() : '', warnings: warnings };
  };

  // Pick a runway on the departure (0) or arrival (1) airport. The tablet then picks a procedure
  // for it by itself (measured: arrival runway 01 at KSAV brought "ILS 1 via Direct"), which the
  // import reads back and says.
  A.route.setRunway = function (which, designator) {
    try {
      var ap = A.route.airportSteps()[which];
      if (!ap) return JSON.stringify({ ok: false, error: 'no-airport' });
      var dd = ap.step.querySelector('.runway-selector .dropdown-button');
      if (!dd) return JSON.stringify({ ok: false, error: 'no-runway-list' });
      var want = A.route.rwyKey(designator);
      var items = dd.querySelectorAll('.dropdown-button-list-item');
      for (var i = 0; i < items.length; i++) {
        if (A.route.rwyKey(A.modelText(items[i])) !== want) continue;
        if (A.isDisabled(items[i])) return JSON.stringify({ ok: false, error: 'disabled' });
        A.press(items[i]);
        return JSON.stringify({ ok: true, picked: A.modelText(items[i]) });
      }
      return JSON.stringify({ ok: false, error: 'no-such-runway' });
    } catch (e) { return JSON.stringify({ ok: false, error: String(e) }); }
  };

  // ---------------------------------------------------------------------------------
  // install
  // ---------------------------------------------------------------------------------

  // Re-injection must not leave the previous observer or listeners running: the client re-installs
  // on a live socket whenever an eval times out, and a leaked observer per install would keep the
  // page dirty forever.
  try {
    if (window.__MSFSBA_SIM_TABLET_OBS && typeof window.__MSFSBA_SIM_TABLET_OBS.disconnect === 'function') {
      window.__MSFSBA_SIM_TABLET_OBS.disconnect();
    }
  } catch (e) {}
  try {
    var oldH = window.__MSFSBA_SIM_TABLET_HANDLERS;
    if (oldH) {
      try { document.removeEventListener('change', oldH.change, true); } catch (eh1) {}
      try { document.removeEventListener('input', oldH.input, true); } catch (eh2) {}
    }
  } catch (e) {}

  try {
    var obs = new MutationObserver(A._markDirty);
    // A frame installed before its body exists is watched from the first scrape on (A.observeAll).
    if (A.root()) obs.observe(A.root(), A.OBSERVER_OPTS);
    A._obs = obs;
    window.__MSFSBA_SIM_TABLET_OBS = obs;
  } catch (e) {
    // No observer -> never gate. Slower, but a stale screen is far worse than a busy one.
    A._dirty = true;
    A.scrape = (function (inner) {
      return function () { A._dirty = true; return inner.apply(A, arguments); };
    })(A.scrape);
  }
  try {
    document.addEventListener('change', A._markDirty, true);
    document.addEventListener('input', A._markDirty, true);
    window.__MSFSBA_SIM_TABLET_HANDLERS = { change: A._markDirty, input: A._markDirty };
  } catch (e) {}

  window.__MSFSBA_SIM_TABLET = A;
  return A.INSTALLED;
})();
