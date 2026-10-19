// The sim tablet in-page agent: MSFS 2024's own EFB, installed at runtime into its Coherent GT view
// "Electronic Flight Bag" (coui://html_ui/efb_ui/efb_os/efb_ui.html) as window.__MSFSBA_SIM_TABLET.
// The view belongs to the sim, not to an aircraft: every MSFS 2024 aircraft shows the same tablet,
// with the sim's apps (Flight Planner, Aircraft, Pilot book, Settings, Ground Services, CIVA) and any
// add-on apps installed into it. MSFS 2020 has no such view.
//
// ES5 ONLY. Coherent GT is Chromium 49: var (no const/let), no arrow functions, no template
// literals, no String.includes / Array.from / Object.assign. Use indexOf and plain loops.
//
// Contract, identical to coherent-pmdg-efb-agent.js so the shared FbwEfbForm consumes both:
//   scrape()            -> JSON string {ok, unchanged?, page, elements:[...]}
//   clickElement(idx)   -> press the element carrying that stamped idx
//   setValue(idx, text) -> type into it, tick it, or pick a dropdown choice
// Element: {idx,text,value,controlType,kind,clickable,level,live,disabled,options,key?,announceChange?}
//   kind: 'tab' | 'button' | 'heading' (+level) | 'static' | 'alert'
//   controlType: 'text' | 'checkbox' | 'select'
//
// Plus the Flight Planner route primitives (A.route.*) the app's SimBrief import drives one step at
// a time; see the block near the end.
//
// HOW THE TABLET TAKES INPUT (measured live 2026-10-19, and read from efb_os.js):
//   * Buttons, tabs, dropdown items and switches answer el.click(): the OS button sets onclick.
//   * A few lists answer mousedown only (the Flight Planner's search results, FacilityResultItem
//     sets onmousedown). A.press fires the ONE event the element listens to, never both.
//   * A text box takes its value from .value plus an 'input' event, with NO focus.
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

  A.isHidden = function (el) {
    if (!el || !el.getBoundingClientRect) return false;
    var r = el.getBoundingClientRect();
    if (r.width <= 0 && r.height <= 0) return true;
    var st = null;
    try { st = window.getComputedStyle(el); } catch (e) { return false; }
    if (!st) return false;
    return st.display === 'none' || st.visibility === 'hidden' || st.opacity === '0';
  };
  A.isVisible = function (el) { return !!el && !A.isHidden(el); };

  A.root = function () { return document.querySelector('.efb') || document.body; };
  // tools/coherent.ps1's `capture` command looks for findRoot()/isVisible() on the installed agent.
  A.findRoot = function () { return A.root(); };

  // The OS disables a control with setAttribute("disabled", "") — on <div> list items as well as
  // buttons, where el.disabled is undefined. One container carries disabled="false" while enabled.
  // The OS also shows a disabled control's own .disabled-layer child, which is what a captured
  // fixture keeps.
  A.isDisabled = function (el) {
    if (!el || el.nodeType !== 1) return false;
    if (el.disabled === true) return true;
    var a = el.getAttribute('disabled');
    if (a !== null && a !== 'false') return true;
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
        if (tag === 'svg' || tag === 'script' || tag === 'style' || tag === 'canvas' || tag === 'img') continue;
        if (mode === 'model' ? A.modelHidden(c) : A.isHidden(c)) continue;
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
  A.iconName = function (el) {
    var svg = el.querySelector('svg');
    if (!svg) return '';
    var n = svg.getAttribute('data-icon') || svg.id || '';
    if (!n) { var i = svg.querySelector('[id]'); n = i ? i.id : ''; }
    n = n.replace(/--.*$/, '').replace(/^clip-/, '').replace(/[-_]+/g, ' ');
    return /^(svg\d*|defs\d*|Layer \d+|Groupe.*|Trac.*)$/i.test(n) ? '' : A.norm(n);
  };

  // Icon buttons the OS names nowhere: by their class.
  A.CLASS_NAMES = {
    'more-info-button': 'More information',
    'fold-button': 'Fold menu',
    'delete-text-button': 'Clear',
    'refresh': 'Refresh',
    'split-view-btn': 'Split view',
    'back-button': 'Back',
    'incremental-decrease': 'Decrease',
    'incremental-increase': 'Increase'
  };
  A.className = function (el) {
    for (var k in A.CLASS_NAMES) if (A.CLASS_NAMES.hasOwnProperty(k) && A.hasClass(el, k)) return A.CLASS_NAMES[k];
    return '';
  };

  A.buttonLabel = function (el) {
    var n = A.lineText(el) || A.tooltipOf(el) || el.getAttribute('aria-label') || el.getAttribute('title');
    if (n) return n;
    n = A.className(el);
    if (n) return n;
    n = A.iconName(el);
    // A panel's or popup's header leads with an unnamed icon button: its close.
    if (!n && el.parentElement && /header/.test(A.cls(el.parentElement)) && el === el.parentElement.firstElementChild) return 'Close';
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
  A.labelFor = function (ctl) {
    var node = ctl;
    for (var up = 0; up < 3 && node && node !== A._view; up++) {
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

  A.CONTROL_SELECTOR = 'button,input,textarea,select,.tab-selector,.dropdown-button,.incrementable-item,' +
    '.checkpoint-item,.switch-container,.facility-result-item';
  A.isControlEl = function (el) {
    var t = el.tagName;
    if (t === 'BUTTON' || t === 'INPUT' || t === 'TEXTAREA' || t === 'SELECT') return true;
    if (typeof el.onclick === 'function') return true;
    return A.hasClass(el, 'tab-selector') || A.hasClass(el, 'dropdown-button') || A.hasClass(el, 'incrementable-item') ||
      A.hasClass(el, 'checkpoint-item') || A.hasClass(el, 'switch-container') || A.hasClass(el, 'facility-result-item');
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

  // The app on screen. Apps stay mounted once opened, hidden behind the shown one.
  A.shownView = function () {
    var vs = A.root().querySelectorAll('.efb-container > .efb-view');
    for (var i = 0; i < vs.length; i++) if (!A.isHidden(vs[i])) return vs[i];
    return null;
  };
  A.viewKey = function (v) {
    var c = A.norm(A.cls(v)).split(' ');
    for (var i = 0; i < c.length; i++) if (c[i] && c[i] !== 'efb-view') return c[i];
    return '';
  };

  A.APP_FALLBACK = {
    '_HomePageApp': 'Home', 'AtlasApp': 'Flight Planner', 'AircraftApp': 'Aircraft', 'PilotBookApp': 'Pilot book',
    'SettingsApp': 'Settings', 'GSApp': 'Ground Services', 'CivaApp': 'CIVA INS'
  };
  // The app's own (translated) name from the EFB API: "@fs-base-efb,TT:EFB.COMMON.AIRCRAFT" ->
  // "aircraft". The home screen's is "more apps", so it keeps "Home".
  A.appName = function (key) {
    if (key === '_HomePageApp') return 'Home';
    try {
      var apps = window.EFB_API && EFB_API._installedApps && EFB_API._installedApps.array;
      for (var i = 0; apps && i < apps.length; i++) {
        if (apps[i].constructor && apps[i].constructor.name === key) {
          var n = apps[i].name, t = n;
          if (typeof Utils !== 'undefined' && Utils.Translate) t = Utils.Translate(n);
          if (t && t.indexOf('TT:') < 0) return A.ucfirst(A.norm(t));
        }
      }
    } catch (e) {}
    if (A.APP_FALLBACK[key]) return A.APP_FALLBACK[key];
    return key.replace(/App$/, '').replace(/^_+/, '').replace(/([a-z])([A-Z])/g, '$1 $2');
  };

  // "Aircraft, Mass and balance": the app plus the title of the page it shows, when it has one.
  A.currentPage = function () {
    var v = A.shownView();
    if (!v) return '';
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
  A.runBlocks = function (el, els) {
    for (var i = 0; i < A.BLOCKS.length; i++) {
      if (A.BLOCKS[i].match(el)) { A.BLOCKS[i].emit(el, els); return true; }
    }
    return false;
  };

  // Decorations and pieces another control already reads.
  A.block('skip',
    function (el) {
      return A.hasClass(el, 'eye-button-container') ||        // checklist "Look at": a camera move, nothing to hear
        A.hasClass(el, 'flight-plan-step-dot') ||             // the route's timeline dots
        A.hasClass(el, 'waypoint-list-header') ||             // read into each waypoint row,
        A.hasClass(el.firstElementChild, 'waypoint-list-header') ||   // and so is the pair's wrapper (live)
        A.hasClass(el, 'slider') && !A.hasClass(el, 'tank') ||   // the quick-fill slider: its % box is the control
        A.hasClass(el, 'aircraft-shape') || A.hasClass(el, 'nav-map') || A.hasClass(el, 'map-container') ||
        A.hasClass(el, 'disabled-layer') || A.hasClass(el, 'tooltip');
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
  A.block('choice',
    function (el) { return A.hasClass(el, 'multiple-buttons'); },
    function (el, els) {
      var bs = el.getElementsByTagName('button');
      for (var i = 0; i < bs.length; i++) {
        if (A.isHidden(bs[i])) continue;
        var t = A.lineText(bs[i]) || A.buttonLabel(bs[i]);
        var on = A.hasClass(bs[i], 'selected');
        els.push(A.button(bs[i], t + (on ? ' (selected)' : ''), { key: 'choice:' + t, announceChange: true }));
      }
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
      els.push(A.el(el, { controlType: 'select', text: name || A.labelFor(el) || A.tooltipOf(el) || A.ucfirst(lead) || A.headingBefore(el) || 'Choice',
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

  // Any other text box (a load's quick-fill %, a wind or QNH box, the route string area).
  A.block('textbox',
    function (el) { return A.hasClass(el, 'textbox') || el.tagName === 'TEXTAREA' || el.tagName === 'INPUT'; },
    function (el, els) {
      var inp = (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') ? el : el.querySelector('input, textarea');
      if (!inp) return;
      var suffix = el.querySelector ? el.querySelector('.suffix') : null;
      var unit = suffix && !A.hasClass(suffix, 'suffix--hidden') ? A.lineText(suffix) : '';
      var label = A.labelFor(A.hasClass(el.parentElement, 'unitbox') ? el.parentElement : el) ||
        inp.getAttribute('placeholder') || 'Value';
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

  // ---------------------------------------------------------------------------------
  // scrape
  // ---------------------------------------------------------------------------------

  A._scrapeId = 0;

  A.headingLevel = function (el) {
    var t = el.tagName;
    if (t.length === 2 && t.charAt(0) === 'H' && /[1-6]/.test(t.charAt(1))) return parseInt(t.charAt(1), 10);
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
  // saved-flight-plan-popup), on screen.
  A.popups = function (view) {
    var cands = view.querySelectorAll('[class*="-popup"]'), out = [];
    for (var i = 0; i < cands.length; i++) {
      var c = A.norm(A.cls(cands[i])).split(' '), is = false;
      for (var j = 0; j < c.length; j++) if (/-popup$/.test(c[j])) { is = true; break; }
      if (is && !A.isHidden(cands[i])) out.push(cands[i]);
    }
    return out;
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

  A.collect = function () {
    var els = [];
    A._idx = 0;
    A._nodes = {};
    A._scrapeId++;

    // Every scrape restamps from 1 in DOM order; an old stamp left on a node no longer walked
    // would collide with a live one.
    var stale = A.root().querySelectorAll('[' + A.ATTR + ']');
    for (var s = 0; s < stale.length; s++) stale[s].removeAttribute(A.ATTR);

    var root = A.root();
    A.markControls(root);
    var view = A.shownView();
    A._view = view;

    // The first-run tour, over everything else.
    var tour = root.querySelector('.onboarding-container');
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
    var dock = root.querySelectorAll('.dock .dock-item');
    for (var d = 0; d < dock.length; d++) {
      if (A.isHidden(dock[d])) continue;
      var dn = A.tooltipOf(dock[d]) || A.iconName(dock[d]);
      var on = A.hasClass(dock[d], 'dock-item--active');
      els.push(A.el(dock[d], { kind: 'tab', clickable: true, text: dn + (on ? ' (current page)' : ''),
                               disabled: A.isDisabled(dock[d]), key: 'dock:' + dn }));
    }

    if (view && A.viewKey(view) === 'AtlasApp') {
      els.push({ idx: A.SIMBRIEF_IDX, kind: 'button', clickable: true, text: A.SIMBRIEF_LABEL, value: '', controlType: '',
                 level: 0, live: '', disabled: false, options: null, key: 'action:simbrief' });
    }

    var sink = els, walkingPopup = false;

    function walk(el) {
      if (!el || el.nodeType !== 1) return;
      var tag = el.tagName.toLowerCase();
      if (tag === 'svg' || tag === 'canvas' || tag === 'img' || tag === 'script' || tag === 'style') return;
      if (A.isHidden(el)) return;
      if (el.__simTabletPopup === A._scrapeId && !walkingPopup) return;   // read already, first
      if (A.runBlocks(el, sink)) return;

      if (el.tagName === 'BUTTON' || (typeof el.onclick === 'function' && !A.hasCtl(el))) {
        var label = A.buttonLabel(el);
        if (!label) return;                                    // a nameless icon says nothing; skip it
        sink.push(A.button(el, label + (A.hasClass(el, 'selected') ? ' (selected)' : ''),
                           A.hasClass(el, 'selected') ? { key: 'button:' + label } : null));
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
        // A caption above its value ("Ground Speed" over "0 KT") reads as one line with a colon.
        var two = el.getElementsByTagName('*').length <= A.ONE_LINE_MAX_NODES ? A.rows(el) : [];
        if (two.length === 2 && el.children.length === 2) {
          var top = A.lineText(el.children[0]), bottom = A.lineText(el.children[1]);
          if (top && bottom) { sink.push(A.el(el, { kind: 'static', text: top + ': ' + bottom })); return; }
        }
      }

      var own = '';
      for (var k = 0; k < el.childNodes.length; k++) if (el.childNodes[k].nodeType === 3) own += el.childNodes[k].nodeValue + ' ';
      own = A.norm(own);
      if (own && !A.isClaimed(el)) sink.push(A.el(el, { kind: 'static', text: own }));
      for (var c = 0; c < el.children.length; c++) walk(el.children[c]);
    }

    // An open popup (the route string box, the saved flight plans) sits over the page and is read
    // FIRST, its title as an alert so it is spoken as it opens; the page follows.
    if (view) {
      var pops = A.popups(view);
      for (var pi = 0; pi < pops.length; pi++) pops[pi].__simTabletPopup = A._scrapeId;
      for (var pj = 0; pj < pops.length; pj++) {
        var pt = pops[pj].querySelector('[class*="popup-header"] .title');
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

    // The notification bell, when it has something.
    var count = root.querySelector('.status-bar .notification-count');
    var n = count ? parseInt(A.norm(count.textContent), 10) : 0;
    if (n > 0) els.push(A.el(count, { kind: 'static', text: 'Notifications: ' + n }));
    var notes = root.querySelectorAll('.status-bar .notification-container .list > *');
    for (var m = 0; m < notes.length; m++) {
      var nt = A.modelText(notes[m]);
      if (nt) els.push(A.el(notes[m], { kind: 'static', text: nt }));
    }

    return els;
  };

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
  A.scrape = function () {
    try {
      A._pollCount++;
      var forceFull = !A._everScraped || (A._pollCount % A.FORCE_FULL_EVERY === 0);
      if (!A._dirty && !forceFull) return JSON.stringify({ ok: true, unchanged: true });
      A._dirty = false;
      A._everScraped = true;
      var page = A.currentPage(), els;
      if (A._obs) { try { A._obs.disconnect(); } catch (e1) {} }
      try { els = A.collect(); }
      finally { if (A._obs) { try { A._obs.observe(A.root(), A.OBSERVER_OPTS); } catch (e2) {} } }
      return JSON.stringify({ ok: true, page: page, elements: els });
    } catch (e) {
      return JSON.stringify({ ok: false, error: String(e && e.message ? e.message : e) });
    }
  };

  // ---------------------------------------------------------------------------------
  // input
  // ---------------------------------------------------------------------------------

  A.find = function (idx) { return document.querySelector('[' + A.ATTR + '="' + idx + '"]'); };

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

  // One press, with the one event the element listens to. el.click() is a real click (onclick);
  // an onmousedown-only element gets a mousedown alone. Never both: a handler that listens to both
  // would run twice, and a mousedown reaching a text box focuses it.
  A.press = function (el) {
    if (typeof el.onclick === 'function' || el.tagName === 'BUTTON') el.click();
    else if (typeof el.onmousedown === 'function') {
      var ev;
      try { ev = new MouseEvent('mousedown', { bubbles: true, cancelable: true, view: window }); }
      catch (e) { ev = document.createEvent('MouseEvents'); ev.initEvent('mousedown', true, true); }
      el.dispatchEvent(ev);
    } else el.click();
    A._dirty = true;
    A.releaseKeyboard();
  };

  A.clickElement = function (idx) {
    if (idx === A.SIMBRIEF_IDX) return false;    // the app's own action: the client runs it
    var el = A.find(idx);
    if (!el || A.isDisabled(el)) return false;
    try { A.press(el); return true; } catch (e) { return false; }
  };

  A.setValue = function (idx, text) {
    var el = A.find(idx);
    if (!el || A.isDisabled(el)) return false;
    var want = String(text == null ? '' : text);
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
        if (isOn === (want.toLowerCase() === 'true')) return true;
        A.press(el);
        return true;
      }
      if (A.hasClass(el, 'switch-container')) {
        if (A.hasClass(el, 'checked') === (want.toLowerCase() === 'true')) return true;
        A.press(el);
        return true;
      }
      if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') {
        // The OS TextBox reads the box on 'input' (debounced into its model). No focus: see the top.
        el.value = want;
        var ev;
        try { ev = new InputEvent('input'); } catch (e1) { ev = document.createEvent('Event'); ev.initEvent('input', true, true); }
        el.dispatchEvent(ev);
        A._dirty = true;
        A.releaseKeyboard();
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

  // Open "Enter route string" and type the route into it. Returns {ok} or {ok:false, error}:
  // 'locked' when the tablet has the item disabled (its flight plan is locked).
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
      return JSON.stringify(st);
    } catch (e) { return JSON.stringify({ ok: false, error: String(e) }); }
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
    obs.observe(A.root(), A.OBSERVER_OPTS);
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
