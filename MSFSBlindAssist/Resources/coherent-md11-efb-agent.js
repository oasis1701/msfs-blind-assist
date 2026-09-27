// TFDi MD-11 EFB in-page agent — installed at runtime into the EFB's Coherent GT view
// (window.__MSFSBA_MD11_EFB). Ships in the separate tfdidesign-aircraft-efb package, but it is an
// ordinary Coherent view: a React app mounted at #MSFS_REACT_MOUNT with a real DOM. (The MD-11's
// six display units are WASM-rendered with NO DOM and cannot be scraped at all — the EFB is the
// one screen on this aircraft that can. Don't confuse the two.)
//
// ES5 ONLY. Coherent GT is Chromium 49: var (no const/let), no arrow functions, no template
// literals, no String.includes / Array.from / Object.assign. Use indexOf and plain loops.
//
// Contract, identical to coherent-pmdg-efb-agent.js so the shared FbwEfbForm consumes both:
//   scrape()            -> JSON string {ok, unchanged?, page, elements:[...]}
//   clickElement(idx)   -> click the element carrying that stamped idx
//   setValue(idx, text) -> type into it
// Element: {idx,text,value,controlType,kind,clickable,level,live,disabled,options,min,max,step}
//   kind: 'tab' | 'button' | 'link' | 'heading' (+level) | 'static' | 'alert'
//   controlType: 'text' | 'checkbox' | 'select' | 'range'
// Two optional fields ask the shared shell for what only this reader knows. The PMDG agent sends
// neither, so its elements are keyed and announced exactly as before.
//   announceChange: true — speak this control's label again after the pilot's own press changed it.
//   key — a reconcile key, stable across state flips and unique on the page. It sits on every
//     element whose LABEL carries changing state:
//       'tile:<name>'           the Services tile;
//       'tile-action:<name>'    the State tile's action button, check mark included;
//       'step-prev:<field>', 'step-next:<field>', 'step-value:<field>'   the stepper fallback;
//       'readout:<caption>'     a read-out.
//     Without it the shell keys a node by its label, so "GPU: Connect" -> "GPU: Disconnect" would be
//     a NEW node: the button the pilot just pressed would be rebuilt under their focus, its new label
//     never spoken. The key is stamped HERE, never inferred by the shell from text: an after-colon
//     strip in the shell once merged the flyPad ATC page's "UNICOM 122.800: Set Active" /
//     "...: Set Standby" into one key.
(function () {
  var A = {};
  A.INSTALLED = 'MSFSBA_MD11_EFB_INSTALLED';
  A.ATTR = 'data-md11-efb-idx';
  A.CLAIM = 'data-md11-efb-label';

  // The EFB's nav tabs, in DOM order. Tailwind: the ACTIVE tab is bg-red-800, the others
  // bg-zinc-600 — colour is the only thing distinguishing them (no aria-selected, no role="tab"),
  // which is exactly why a blind pilot gets nothing from this EFB unaided.
  A.ACTIVE_TAB_CLASS = 'bg-red-800';
  A.TAB_BAR_MIN = 5;          // a row of >=5 sibling buttons is the nav bar, not a content row

  // ---------------------------------------------------------------------------------
  // small helpers
  // ---------------------------------------------------------------------------------

  A.hasClass = function (el, c) {
    if (!el || !el.className || typeof el.className !== 'string') return false;
    return (' ' + el.className + ' ').indexOf(' ' + c + ' ') >= 0;
  };

  // Tailwind's `uppercase` class makes innerText shout ("SAVE", "GENERAL") while the DOM holds
  // "Save"/"General". A screen reader spells an all-caps word, so read the DOM form there.
  A.isUppercased = function (el) {
    var n = el;
    for (var i = 0; i < 3 && n && n.nodeType === 1; i++) {
      if (A.hasClass(n, 'uppercase')) return true;
      n = n.parentElement;
    }
    return false;
  };

  // textContent minus every hidden subtree, by the same visibility rule the walk uses (A.isHidden).
  // The uppercase branch of A.txt cannot use innerText, which applies the CSS transform ("SAVE"). Plain
  // textContent reads text the EFB has HIDDEN as well: a zero-size badge inside a button read "Save0".
  A.renderedText = function (el) {
    var s = '';
    for (var i = 0; i < el.childNodes.length; i++) {
      var n = el.childNodes[i];
      if (n.nodeType === 3) s += n.textContent;
      else if (n.nodeType === 1 && !A.isHidden(n)) s += A.renderedText(n);
    }
    return s;
  };

  A.txt = function (el) {
    if (!el) return '';
    var t = A.isUppercased(el) ? A.renderedText(el) : (el.innerText || el.textContent || '');
    return t.replace(/\s+/g, ' ').trim();
  };

  // Text belonging to THIS element rather than its descendants. Emitting a parent's full innerText
  // AND its children's would read every line of the page twice.
  A.ownText = function (el) {
    var s = '';
    for (var i = 0; i < el.childNodes.length; i++) {
      var n = el.childNodes[i];
      if (n.nodeType === 3) s += n.textContent + ' ';
    }
    return s.replace(/\s+/g, ' ').trim();
  };

  A.isHidden = function (el) {
    if (!el || !el.getBoundingClientRect) return false;
    // A React app keeps inactive pages mounted but zero-sized/hidden; reading them would mix
    // several pages together with no cue which is on screen.
    var r = el.getBoundingClientRect();
    if (r.width <= 0 && r.height <= 0) return true;
    var st = null;
    try { st = window.getComputedStyle(el); } catch (e) { return false; }
    if (!st) return false;
    return st.display === 'none' || st.visibility === 'hidden' || st.opacity === '0';
  };

  A.root = function () {
    return document.getElementById('MSFS_REACT_MOUNT') || document.body;
  };

  // tools/coherent.ps1's `capture` command (and its agent finder) look for findRoot()/isVisible()
  // on whichever agent is installed — aliases, so the documented fixture-capture tool works here.
  A.findRoot = function () { return A.root(); };
  A.isVisible = function (el) { return !!el && !A.isHidden(el); };

  // ---------------------------------------------------------------------------------
  // the nav bar
  // ---------------------------------------------------------------------------------

  // The tab bar is a VISIBLE row of >=5 sibling buttons. It is found structurally rather than by
  // class: the class list is Tailwind soup that changes with any restyle, whereas "a row of button
  // siblings at the top of the app" is what the bar actually IS.
  //   - Among such rows, the one carrying the current-page marker wins, and other children in it
  //     (a spacer, a clock) are tolerated.
  //   - A row with no marker is taken only as a fallback, and only when it is buttons alone. That is
  //     the old rule, kept so a restyle that drops the marker still finds the bar.
  //   - Never a hidden row: React keeps inactive views mounted.
  //   - Never a content row of buttons. The Charts chart-type strip is exactly five buttons, and
  //     under the old first-match rule, first in the DOM, it WAS the nav bar.
  A.findTabBar = function () {
    var divs = A.root().querySelectorAll('div'), fallback = null;
    for (var i = 0; i < divs.length; i++) {
      var d = divs[i], kids = d.children, n = 0, marked = false;
      for (var j = 0; j < kids.length; j++) {
        if (kids[j].tagName !== 'BUTTON') continue;
        n++;
        if (A.hasClass(kids[j], A.ACTIVE_TAB_CLASS)) marked = true;
      }
      if (n < A.TAB_BAR_MIN || A.isHidden(d) || A.isChartStrip(d) || A.isChoiceGroup(d)) continue;
      if (marked) return d;
      if (!fallback && n === kids.length) fallback = d;
    }
    return fallback;
  };

  A.tabButtons = function () {
    var bar = A.findTabBar();
    if (!bar) return [];
    var out = [];
    for (var i = 0; i < bar.children.length; i++)
      if (bar.children[i].tagName === 'BUTTON') out.push(bar.children[i]);
    return out;
  };

  // The page the EFB is currently showing = the active tab's label.
  A.currentPage = function () {
    var tabs = A.tabButtons();
    for (var i = 0; i < tabs.length; i++)
      if (A.hasClass(tabs[i], A.ACTIVE_TAB_CLASS)) return A.txt(tabs[i]);
    return '';
  };

  // ---------------------------------------------------------------------------------
  // classification
  // ---------------------------------------------------------------------------------

  A.isControl = function (el) {
    var t = el.tagName;
    return t === 'BUTTON' || t === 'INPUT' || t === 'SELECT' || t === 'TEXTAREA' || t === 'A';
  };

  A.headingLevel = function (el) {
    var t = el.tagName;
    if (t.length === 2 && t.charAt(0) === 'H') {
      var n = parseInt(t.charAt(1), 10);
      if (n >= 1 && n <= 6) return n;
    }
    return 0;
  };

  // A generic control's element record, built on A.el like every block's. It is therefore stamped
  // the same way, and the locked-page rule (A._inert -> disabled) lives in A.el alone instead of
  // being re-applied by each caller.
  A.controlFor = function (el) {
    var f = { disabled: !!el.disabled };

    if (el.tagName === 'INPUT') {
      // A disabled field the row rules did not reach is still a read-out, never an edit box. Like the
      // read-out rows, it is keyed by its caption (see the contract at the top).
      if (el.disabled && !A.isStepperInput(el) && A.isReadoutType(el)) {
        var rl = A.labelFor(el) || 'Value';
        f.kind = 'static';
        f.text = A.readoutText(rl, el);
        f.key = 'readout:' + rl;
        return A.el(el, f);
      }

      var ty = (el.type || 'text').toLowerCase();
      if (ty === 'checkbox' || ty === 'radio') {
        f.controlType = 'checkbox';
        f.value = el.checked ? 'true' : 'false';
      } else if (ty === 'range') {
        f.controlType = 'range';
        f.value = String(el.value == null ? '' : el.value);
        if (el.min !== '') f.min = Number(el.min);
        if (el.max !== '') f.max = Number(el.max);
        if (el.step !== '') f.step = Number(el.step);
      } else {
        f.controlType = 'text';
        f.value = String(el.value == null ? '' : el.value);
      }
      // An unlabelled input is useless to a screen reader, so fall back through every label
      // source the EFB might have used before giving up.
      f.text = A.labelFor(el);
      var unit = A.unitFor(el);
      if (unit) f.text = (f.text ? f.text + ' ' : '') + '(' + unit + ')';
      return A.el(el, f);
    }

    if (el.tagName === 'SELECT') {
      f.controlType = 'select';
      f.value = String(el.value == null ? '' : el.value);
      f.options = [];
      for (var i = 0; i < el.options.length; i++) f.options.push(A.txt(el.options[i]));
      f.text = A.labelFor(el);
      return A.el(el, f);
    }

    if (el.tagName === 'A') {
      f.kind = 'link';
      f.text = A.txt(el);
      return A.el(el, f);
    }

    // BUTTON
    f.kind = 'button';
    f.clickable = true;
    f.text = A.txt(el) || el.getAttribute('aria-label') || el.getAttribute('title') || A.iconButtonName(el) || '';
    return A.el(el, f);
  };

  // ---------------------------------------------------------------------------------
  // icon-only buttons
  // ---------------------------------------------------------------------------------

  // An icon-only button (an SVG child, no text, no aria-label, no title) says NOTHING to a screen
  // reader — and, unnamed, it was dropped from the scrape altogether. The EFB's own Select
  // component (src/components/Select.tsx) is built from exactly two of them: a DISABLED input
  // showing the current choice, flanked by a ChevronUp and a ChevronDown button that step
  // through the options. That is the departure and arrival runway pickers on the Perf page,
  // and Thrust Setting, Anti-Ice, Runway Condition, Autobrake and Reversers besides — the
  // pilot heard "Runway: 09L" and had no control that could ever change it (2026-09-05).
  //
  // lucide-react stamps the icon's name on the SVG's class ("lucide lucide-chevron-up"); that
  // class is the only name these buttons carry anywhere in the DOM.
  A.ICON_WORDS = {
    'chevron-up': 'previous', 'chevron-down': 'next',
    'chevron-left': 'previous', 'chevron-right': 'next',
    'x': 'close', 'search': 'search', 'delete': 'delete',
    'zoom-in': 'zoom in', 'zoom-out': 'zoom out'
  };

  A.iconName = function (el) {
    var svgs = el.getElementsByTagName('svg');
    for (var i = 0; i < svgs.length; i++) {
      // SVG className is an SVGAnimatedString, not a string — read the attribute.
      var cls = svgs[i].getAttribute('class') || '';
      var m = /(?:^|\s)lucide-([a-z0-9-]+)/.exec(cls);
      if (m && m[1]) return m[1];
    }
    return '';
  };

  // A stepper button is named after the field it steps and carries the CURRENT choice —
  // "Runway next (now 09L)" — so the pilot hears what will change and where it stands. The
  // "(now …)" suffix is dynamic, so the stepper block does two things. It stamps the button with a
  // key that leaves the suffix out (step-next:<field>), so the shell patches the button in place and
  // focus stays on it. It flags the button announceChange, so the shell speaks the new label after
  // the pilot's own press. That is how "Runway next (now 27R)" reaches them without hunting for the
  // field.
  A.iconButtonName = function (el) {
    var icon = A.iconName(el);
    if (!icon) return '';
    var word = A.ICON_WORDS[icon] || icon.replace(/-/g, ' ');
    var p = el.parentElement;
    if (p && (icon === 'chevron-up' || icon === 'chevron-down')) {
      var inputs = p.getElementsByTagName('input');
      if (inputs.length === 1) {
        var field = A.labelFor(inputs[0]);
        var cur = String(inputs[0].value == null ? '' : inputs[0].value).replace(/\s+/g, ' ').trim();
        if (field) return field + ' ' + word + (cur ? ' (now ' + cur + ')' : '');
      }
    }
    return word.charAt(0).toUpperCase() + word.slice(1);
  };

  // Returns {text, src}: the field's label, and the ELEMENT it was taken from (null when the label
  // came from an attribute). src matters: if the label was lifted off a visible caption element,
  // that caption must not also emit as its own static line, or every field reads twice.
  //
  // The MD-11 EFB gives its inputs NO aria-label, NO placeholder, NO title and NO id (verified
  // live) — the caption is simply the element next to the input:
  //     <div class="relative flex ..."> <div>Cargo (<span>LBS</span>)</div> <input> </div>
  // so the sibling walk is not a fallback here, it is the ONLY thing that ever names a field.
  A.labelInfo = function (el) {
    // aria-label is authoritative WHEN it names the field. Everything below demands isName for the
    // same reason: the MD-11 EFB's placeholders are dash renders ("----", "---/--", "--") showing
    // the field's FORMAT, not its name — taking one as the label announces an edit box called
    // "dash dash dash dash", while the real caption sits one element away.
    var aria = el.getAttribute('aria-label');
    if (aria && A.isName(aria)) return { text: aria, src: null };

    var id = el.getAttribute('id');
    if (id) {
      var lab = document.querySelector('label[for="' + id + '"]');
      if (lab) { var lt = A.txt(lab); if (lt) return { text: lt, src: lab }; }
    }

    var p = el.parentElement;
    if (p) {
      var t = A.ownText(p);
      if (t && A.isName(t)) return { text: t, src: p };
    }

    // Walk OUTWARD for the caption. The EFB wraps fields to varying depth — "ICAO code" is the
    // input's own previous sibling, "Runway Length"/"Wind"/"Temperature" are its wrapper's, and
    // "Runway" is a level deeper still — so a fixed depth of one leaves boxes unlabelled, which is
    // useless to a blind pilot. Four covers the deepest nesting this EFB actually uses.
    var node = el;
    for (var up = 0; up < 4 && node; up++) {
      var prev = node.previousElementSibling;
      if (prev) {
        var pt = A.txt(prev);
        if (pt && A.isName(pt)) return { text: pt, src: prev };
      }
      node = node.parentElement;
      if (!node || node === A.root()) break;
    }

    // A row that puts its caption AFTER the field still names it.
    var next = el.nextElementSibling;
    if (next) { var nt = A.txt(next); if (nt && A.isName(nt)) return { text: nt, src: next }; }

    // Last resort. A dashy placeholder is a poor name, but "----" still beats an edit box with no
    // name at all — at least the pilot knows the field is there and what shape it wants.
    var ph = el.getAttribute('placeholder') || el.getAttribute('title') || '';
    if (ph) return { text: ph, src: null };

    return { text: '', src: null };
  };

  // A label has to contain a letter. "----", "-- %", "0" are value renders, not names — the same
  // rule the PMDG agent needs ("A field label must contain letters").
  A.isName = function (s) { return /[a-z]/i.test(s || ''); };

  A.labelFor = function (el) { return A.labelInfo(el).text; };

  // B5: a unit box — the short, text-only <div> the EFB puts right after an input ("°C", "inHg",
  // "lb", "ft"). Folded into the field's name and claimed, so it is never read as a loose line.
  // A unit is a WORD: it carries a letter, or is a bare ° or %. A short number in that spot
  // ("507.9") is a VALUE. Taken as a unit, it would rename the field "GW (507.9)" and, once claimed,
  // never be read at all. The same goes for a dash placeholder ("----").
  A.unitEl = function (input) {
    var n = input.nextElementSibling;
    if (!n || n.tagName !== 'DIV' || n.children.length > 0) return null;
    var t = A.txt(n);
    if (!t || t.length > 6 || t.indexOf(' ') >= 0 || !/[A-Za-z°%]/.test(t)) return null;
    return n;
  };

  A.unitFor = function (input) { var u = A.unitEl(input); return u ? A.txt(u) : ''; };

  // "0KT" / "----ft" / "7587ft" → "0 KT" / "---- ft" / "7587 ft". Only a number-or-dashes token
  // ending in 1-4 letters: "0.1%", "N/A" and "RW06L" are left alone.
  A.spaceUnit = function (s) { return String(s).replace(/^([0-9.,-]+)([A-Za-z°]{1,4})$/, '$1 $2'); };

  // Mark every element a control uses as its caption, so the walk can skip emitting it again.
  // Done as a PRE-PASS because the caption is reached BEFORE its input in DOM order — by the time
  // the input claims it, the static line has already been pushed.
  A.markClaimedLabels = function () {
    var ctrls = A.root().querySelectorAll('input, select, textarea');
    for (var i = 0; i < ctrls.length; i++) {
      var info = A.labelInfo(ctrls[i]);
      if (info.src) {
        try { info.src.setAttribute(A.CLAIM, '1'); } catch (e) {}
      }
      var ue = A.unitEl(ctrls[i]);
      if (ue) { try { ue.setAttribute(A.CLAIM, '1'); } catch (e2) {} }
    }

    // A choice group's caption is the span just before it. Claim it so it is read once — as the
    // dropdown's name — and not again as a loose line ahead of the dropdown.
    var groups = A.root().querySelectorAll('div[role="group"]');
    for (var g = 0; g < groups.length; g++) {
      if (!A.isChoiceGroup(groups[g])) continue;
      var gl = A.groupLabelEl(groups[g]);
      if (gl) { try { gl.setAttribute(A.CLAIM, '1'); } catch (e) {} }
    }
  };

  A.clearClaims = function () {
    var was = A.root().querySelectorAll('[' + A.CLAIM + ']');
    for (var i = 0; i < was.length; i++) was[i].removeAttribute(A.CLAIM);
  };

  // ---------------------------------------------------------------------------------
  // element factory + block dispatcher
  // ---------------------------------------------------------------------------------

  // While walking a subtree the EFB has made inert (its "cannot be used right now" overlay),
  // every control emitted is dimmed. Set by the locked-page walk, never by a block.
  A._inert = false;

  // The EFB locks a page in flight by wrapping its content in a `pointer-events-none` subtree
  // beneath that overlay. The bare class also sits on the zero-child backdrop, which locks
  // nothing — so an inert WRAPPER is the class on a node that actually wraps something.
  A.INERT_CLASS = 'pointer-events-none';
  A.isInertRoot = function (el) {
    return !!el && el.nodeType === 1 && el.children.length > 0 && A.hasClass(el, A.INERT_CLASS);
  };

  // ...and the same question asked of an element anywhere inside such a wrapper. This is what
  // clickElement/setValue refuse on: `pointer-events: none` blocks REAL hit-testing only, so a
  // dispatched event or el.click() still reaches the handler — and the EFB's door, GPU and state
  // handlers carry no ground check of their own. The walk dims what it reads; without this the
  // reader would still happily open a door in flight (and the shell's native-list fallback,
  // which has no dimmed gate at all, is reached by exactly that path).
  A.isInert = function (el) {
    var root = A.root();
    for (var n = el; n && n.nodeType === 1; n = n.parentElement) {
      if (A.isInertRoot(n)) return true;
      if (n === root) break;
    }
    return false;
  };

  A.stamp = function (node) { node.setAttribute(A.ATTR, String(++A._idx)); return A._idx; };

  // One element record with every contract field present. `node` is stamped with the idx
  // (null for a synthetic element with nothing to click).
  A.el = function (node, f) {
    var o = { idx: node ? A.stamp(node) : ++A._idx, text: '', value: '', controlType: '', kind: '',
              clickable: false, level: 0, live: '', disabled: false, options: null };
    for (var k in f) if (f.hasOwnProperty(k)) o[k] = f[k];
    if (A._inert) o.disabled = true;
    return o;
  };

  // The EFB's building blocks. A block is tried on every element BEFORE the generic rules;
  // the first whose match() says yes emits for that element and owns its whole subtree.
  // Register specific blocks before general ones — order is the priority.
  A.BLOCKS = [];
  A.block = function (name, match, emit) { A.BLOCKS.push({ name: name, match: match, emit: emit }); };
  A.runBlocks = function (el, els) {
    for (var i = 0; i < A.BLOCKS.length; i++) {
      var b = A.BLOCKS[i];
      if (b.match(el)) { b.emit(el, els); return true; }
    }
    return false;
  };

  // ---------------------------------------------------------------------------------
  // B2: section strip — <ul role="tablist"> (Options: General…Behavior; Payload: Passenger &
  // Cargo / ZFW). The EFB marks the open section by colour + underline only.
  // ---------------------------------------------------------------------------------

  A.block('tablist',
    function (el) { return el.tagName === 'UL' && el.getAttribute('role') === 'tablist'; },
    function (el, els) {
      var bs = el.getElementsByTagName('button');
      for (var i = 0; i < bs.length; i++) {
        if (A.isHidden(bs[i])) continue;
        els.push(A.el(bs[i], { kind: 'tab', clickable: true, disabled: !!bs[i].disabled,
          text: A.txt(bs[i]) + (A.hasClass(bs[i], 'underline') ? ' (selected)' : '') }));
      }
    });

  // ---------------------------------------------------------------------------------
  // B3: choice group — <div role="group"> of buttons (ToggleComponent, the 3-way groups,
  // Perf's Flaps/Packs). The EFB shows the chosen one by colour alone (bg-green-700 in the
  // Options/Perf 2-way groups, bg-green-600 in the 3-way groups). Read as ONE dropdown named by
  // the caption span the EFB puts just before the group.
  // ---------------------------------------------------------------------------------

  A.SELECTED_CLASSES = ['bg-green-700', 'bg-green-600'];

  A.isHighlighted = function (btn) {
    for (var i = 0; i < A.SELECTED_CLASSES.length; i++) if (A.hasClass(btn, A.SELECTED_CLASSES[i])) return true;
    return false;
  };

  // The element children of `el` when they are all buttons; null otherwise.
  A.buttonChildren = function (el) {
    var out = [];
    for (var i = 0; i < el.children.length; i++) {
      if (el.children[i].tagName !== 'BUTTON') return null;
      out.push(el.children[i]);
    }
    return out.length ? out : null;
  };

  A.isChoiceGroup = function (el) {
    return el.tagName === 'DIV' && el.getAttribute('role') === 'group' && !!A.buttonChildren(el);
  };

  A.groupLabelEl = function (g) {
    var p = g.previousElementSibling;
    return (p && A.isName(A.txt(p))) ? p : null;
  };

  A.groupLabel = function (g) { var l = A.groupLabelEl(g); return l ? A.txt(l) : ''; };

  A.block('choice-group', A.isChoiceGroup, function (g, els) {
    var bs = A.buttonChildren(g), label = A.groupLabel(g);
    // B3d: the EFB greyed the setting out and put the reason where the choices would be
    // ("N/A on Pax Model") — read the reason, offer nothing to change.
    if (bs.length === 1 && bs[0].disabled) {
      els.push(A.el(g, { kind: 'static', text: (label ? label + ': ' : '') + A.txt(bs[0]) }));
      return;
    }
    var opts = [], val = '', dis = false;
    for (var i = 0; i < bs.length; i++) {
      var t = A.txt(bs[i]);
      opts.push(t);
      if (A.isHighlighted(bs[i])) val = t;
      if (bs[i].disabled) dis = true;
    }
    els.push(A.el(g, { controlType: 'select', text: label, value: val, options: opts, disabled: dis }));
  });

  // ---------------------------------------------------------------------------------
  // B4: stepper — the EFB's Select component (src/components/Select.tsx): a DISABLED input
  // showing the current choice between a ChevronUp and a ChevronDown button. The full option
  // list is not in the DOM; it sits in React's fiber tree on the input
  // (input[__reactFiber$…].return.return.memoizedProps.options — 2 hops live, walked up to 8,
  // read defensively). With the list: ONE dropdown. Without it: today's reading — the value,
  // then the two arrow buttons named after the field.
  // ---------------------------------------------------------------------------------

  A.chevronButtons = function (container) {
    var bs = container.getElementsByTagName('button'), up = null, down = null;
    for (var i = 0; i < bs.length; i++) {
      var ic = A.iconName(bs[i]);
      if (ic === 'chevron-up') up = bs[i];
      else if (ic === 'chevron-down') down = bs[i];
    }
    return (up && down) ? { up: up, down: down } : null;
  };

  A.isStepper = function (el) {
    if (el.tagName !== 'DIV' || el.children.length !== 3) return false;
    var inp = el.children[0];
    return inp.tagName === 'INPUT' && !!inp.disabled
      && el.children[1].tagName === 'BUTTON' && el.children[2].tagName === 'BUTTON'
      && !!A.chevronButtons(el);
  };

  A.isStepperInput = function (el) {
    return !!el && el.tagName === 'INPUT' && !!el.disabled && !!el.parentElement && A.isStepper(el.parentElement);
  };

  A.inputValue = function (inp) { return String(inp.value == null ? '' : inp.value).replace(/\s+/g, ' ').trim(); };

  A.fiberOptions = function (input) {
    try {
      var keys = Object.keys(input), fk = '';
      for (var i = 0; i < keys.length; i++) if (keys[i].indexOf('__reactFiber$') === 0) { fk = keys[i]; break; }
      if (!fk) return null;
      var f = input[fk];
      for (var hop = 0; f && hop < 8; hop++) {
        var mp = f.memoizedProps;
        // The FIRST fiber carrying an `options` array is this Select's own — stop there whatever it
        // holds. The EFB renders the component with an EMPTY list before the list exists (no runway
        // options until an airport is entered), and walking PAST an empty one to keep looking finds
        // an unrelated ANCESTOR's `options` prop and offers a dropdown of somebody else's choices.
        // Empty means "no list yet": fall back to the chevrons.
        if (mp && mp.options && typeof mp.options.length === 'number') {
          if (mp.options.length === 0) return null;
          var out = [];
          for (var o = 0; o < mp.options.length; o++) {
            var lab = mp.options[o] ? mp.options[o].label : null;
            if (lab === null || lab === undefined) return null;
            out.push(String(lab));
          }
          return out;
        }
        f = f.return;
      }
    } catch (e) {}
    return null;
  };

  A.block('stepper', A.isStepper, function (st, els) {
    var inp = st.children[0], opts = A.fiberOptions(inp), label = A.labelFor(inp), cur = A.inputValue(inp);
    if (opts) {
      els.push(A.el(inp, { controlType: 'select', text: label, value: cur, options: opts }));
      return;
    }
    var cb = A.chevronButtons(st);
    // An empty field is the EFB's "nothing picked yet" (the runway before an airport is entered).
    // The bare "Runway: " it used to read is a sentence that stops dead; borrow readoutText's
    // (empty) marker so the pilot hears that the field is there and holds nothing. readoutText
    // ITSELF is not reused here: it runs the value through spaceUnit, which would split a runway
    // designator like "06L" into the spoken "06 L".
    els.push(A.el(inp, { kind: 'static', text: (label ? label + ': ' : '') + (cur || '(empty)'), key: 'step-value:' + label }));
    // announceChange: the opt-in that asks the shared shell (FbwEfbForm's patchEl) to SPEAK this
    // control's label again once the pilot's own press has changed it. It belongs on exactly those
    // controls whose label carries their own NEW STATE: these two arrows, and the tiles further
    // down. A press moves the field, so "Runway next (now 06L)" becomes "Runway next (now 06R)" on
    // the very button the pilot is still focused on, and nothing else would read the new choice to
    // them. Every OTHER control's post-press label change IS the press the screen reader has
    // already spoken — a tab's "(current page)", a flyPad tile's "(called)" — and stays silent.
    // key: the label carries the current choice, so it is no identity; the field's name is. The up
    // chevron reads "previous" and the down one "next" (A.ICON_WORDS).
    els.push(A.el(cb.up, { kind: 'button', clickable: true, disabled: !!cb.up.disabled, text: A.iconButtonName(cb.up), key: 'step-prev:' + label, announceChange: true }));
    els.push(A.el(cb.down, { kind: 'button', clickable: true, disabled: !!cb.down.disabled, text: A.iconButtonName(cb.down), key: 'step-next:' + label, announceChange: true }));
  });

  // ---------------------------------------------------------------------------------
  // B7: span row — a <div> whose children are only plain spans ("Slope" "0.1%",
  // "Headwind" "0KT") is one fact; read it as one line.
  // ---------------------------------------------------------------------------------

  A.spanRow = function (el) {
    if (el.tagName !== 'DIV' || el.children.length < 2) return null;
    var parts = [];
    for (var i = 0; i < el.children.length; i++) {
      var c = el.children[i];
      if (A.isHidden(c)) continue;
      if (c.tagName !== 'SPAN' || c.children.length > 0) return null;
      var t = A.txt(c);
      if (t) parts.push(A.spaceUnit(t));
    }
    return parts.length >= 2 ? parts.join(' ') : null;
  };

  A.block('span-row',
    function (el) { return !!A.spanRow(el); },
    function (el, els) { els.push(A.el(el, { kind: 'static', text: A.spanRow(el) })); });

  // ---------------------------------------------------------------------------------
  // B6: read-out row — a label and a value the EFB lays out as two elements:
  //   (a) <h3>Block Fuel</h3><p>77347 lbs</p>   /   <p>Estimated Landing Distance</p><p>----ft</p>
  //   (b) <span>V1</span><input disabled value=155>   /   <label>Load</label><input disabled value=35%>
  //       (the locked input may sit one wrapper deep beside a unit box: Flex Temperature °C)
  // Read as ONE line "Label: value unit". A disabled input is a read-out, never an edit field.
  // ---------------------------------------------------------------------------------

  A.isTextEl = function (el) {
    return el.tagName === 'H3' || el.tagName === 'P' || el.tagName === 'SPAN' || el.tagName === 'LABEL';
  };

  // (a) two text-only children: a name (starts with a letter; never a <label>, which names a
  // control) and a short value. Long second texts are prose, not values, and stay separate.
  A.pairRow = function (el) {
    if (el.tagName !== 'DIV' || el.children.length !== 2) return null;
    var a = el.children[0], b = el.children[1];
    if (!A.isTextEl(a) || !A.isTextEl(b) || a.tagName === 'LABEL') return null;
    if (a.children.length > 0 || b.children.length > 0) return null;
    var label = A.txt(a), value = A.txt(b);
    if (!/^[A-Za-z]/.test(label) || !value || value.length > 24) return null;
    return label + ': ' + A.spaceUnit(value);
  };

  // A checkbox, radio or range is a STATE the shell renders as its own kind of control, so it is
  // never a read-out however it is laid out: folding one into a "Label: value" line would hide it
  // behind a static sentence. The same rule the generic input path applies.
  A.isReadoutType = function (inp) {
    var t = (inp.type || 'text').toLowerCase();
    return t !== 'checkbox' && t !== 'radio' && t !== 'range';
  };

  // The one disabled, non-stepper input inside `el`, with nothing else interactive beside it.
  A.readoutInput = function (el) {
    var inputs = el.getElementsByTagName('input');
    if (inputs.length !== 1) return null;
    if (el.getElementsByTagName('button').length > 0 || el.getElementsByTagName('select').length > 0) return null;
    var inp = inputs[0];
    if (!inp.disabled || A.isStepperInput(inp) || !A.isReadoutType(inp)) return null;
    return inp;
  };

  A.readoutText = function (label, inp) {
    var value = A.spaceUnit(A.inputValue(inp)), unit = A.unitFor(inp);
    return label + ': ' + (value || '(empty)') + (unit ? ' ' + unit : '');
  };

  // (b) a text label then the locked field (directly, or in a wrapper beside its unit box).
  A.readoutRow = function (el) {
    if (el.tagName !== 'DIV' || el.children.length !== 2) return null;
    var a = el.children[0], b = el.children[1];
    if (!A.isTextEl(a) || a.children.length > 0) return null;
    var inp = null;
    if (b.tagName === 'INPUT') inp = b;
    else if (b.tagName === 'DIV') inp = A.readoutInput(b);
    if (!inp || !inp.disabled || A.isStepperInput(inp) || !A.isReadoutType(inp)) return null;
    var label = A.txt(a);
    if (!A.isName(label)) return null;
    return A.readoutText(label, inp);
  };

  // Both rows read "Label: value" with the label in the FIRST child, so that label — never the
  // changing value — is the row's reconcile key (see the contract at the top).
  A.block('pair-row',
    function (el) { return !!A.pairRow(el); },
    function (el, els) { els.push(A.el(el, { kind: 'static', text: A.pairRow(el), key: 'readout:' + A.txt(el.children[0]) })); });

  A.block('readout-row',
    function (el) { return !!A.readoutRow(el); },
    function (el, els) { els.push(A.el(el, { kind: 'static', text: A.readoutRow(el), key: 'readout:' + A.txt(el.children[0]) })); });

  // ---------------------------------------------------------------------------------
  // B8: tile — a name above its action.
  //   (a) Services: <div><p>Passenger 1L</p><button>Closed</button></div>  → "Passenger 1L: Closed"
  //   (b) State:    <div><button><svg/><p>Cold and Dark</p></button><button>Set as default | <svg check/></button></div>
  //       → "Cold and Dark" (load it), then "Cold and Dark: Set as default", or the BUTTON
  //         "Cold and Dark is the default" when the second button is the green check mark.
  // ---------------------------------------------------------------------------------

  A.isTileA = function (el) {
    if (el.tagName !== 'DIV' || el.children.length !== 2) return false;
    var p = el.children[0], b = el.children[1];
    return p.tagName === 'P' && b.tagName === 'BUTTON' && p.children.length === 0 && A.isName(A.txt(p));
  };

  A.block('tile', A.isTileA, function (el, els) {
    var name = A.txt(el.children[0]), btn = el.children[1];
    // announceChange (see the stepper block): the label carries the tile's own state, so the flip a
    // press produces — "Passenger 1L: Closed" -> "Passenger 1L: Open" — is the OUTCOME the pilot
    // asked for (did the door open?) and nobody else reads it to them. For the same reason the label
    // is no identity: the tile's name is its key.
    els.push(A.el(btn, { kind: 'button', clickable: true, disabled: !!btn.disabled, text: name + ': ' + A.txt(btn), key: 'tile:' + name, announceChange: true }));
  });

  A.isTileB = function (el) {
    if (el.tagName !== 'DIV' || el.children.length !== 2) return false;
    var a = el.children[0], b = el.children[1];
    return a.tagName === 'BUTTON' && b.tagName === 'BUTTON' && a.getElementsByTagName('p').length === 1;
  };

  A.block('state-tile', A.isTileB, function (el, els) {
    var a = el.children[0], b = el.children[1], name = A.txt(a.getElementsByTagName('p')[0]);
    // The LOAD button's label is the state's name and never changes on a press, so it asks for
    // nothing and needs no key: its label already is one. Its ACTION button's label carries the
    // action's own state, so it opts in exactly like the tile above (announceChange — see the
    // stepper block). It is keyed by the state's name, whichever face the EFB shows.
    var actionKey = 'tile-action:' + name;
    els.push(A.el(a, { kind: 'button', clickable: true, disabled: !!a.disabled, text: name }));
    var action = A.txt(b);
    if (action) els.push(A.el(b, { kind: 'button', clickable: true, disabled: !!b.disabled, text: name + ': ' + action, key: actionKey, announceChange: true }));
    // Once this state IS the default, the EFB swaps "Set as default" for a green check mark. The
    // reader keeps a BUTTON under the same key, so the node the pilot just pressed survives the swap
    // and its new label — the outcome of their press — is spoken. A static line in its place was a
    // different node, destroyed under their focus. It is deliberately NOT stamped on the EFB's
    // check-mark button (A.el(null, ...)): nothing here knows what the EFB does with a click on its
    // check mark, so a press on this element finds no node and clickElement refuses it.
    //
    // disabled is forced TRUE, never read off the check-mark button: with nothing stamped, a press
    // reaches A.find(idx) -> null and clickElement returns false, which NOTHING surfaces. Reported
    // enabled, the pilot heard the shell's "Activating Cold and Dark is the default" (or, in the
    // native list fallback, nothing at all) over a control that did nothing and said nothing. Marked
    // disabled, BOTH refusal paths answer "Unavailable" — the shell's data-disabled branch in
    // onActivate, and FbwEfbForm.AnnounceUnavailable in list mode. The key and text are unchanged, so
    // the reconcile key still holds the node the press landed on and announceChange still speaks the
    // outcome.
    else if (A.iconName(b) === 'check') els.push(A.el(null, { kind: 'button', clickable: true, disabled: true, text: name + ' is the default', key: actionKey, announceChange: true }));
    else els.push(A.el(b, { kind: 'button', clickable: true, disabled: !!b.disabled, text: name + ': ' + A.iconButtonName(b), key: actionKey, announceChange: true }));
  });

  // ---------------------------------------------------------------------------------
  // B9: pre-formatted text — the OFP (<pre> blocks under #ofp). One element per block with its
  // line breaks kept; the shell renders controlType 'pre' as a monospace block read line by line.
  // ---------------------------------------------------------------------------------

  A.block('pre',
    function (el) { return el.tagName === 'PRE'; },
    function (el, els) {
      var t = (el.textContent || '').replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').replace(/^\s+|\s+$/g, '');
      if (t) els.push(A.el(el, { kind: 'static', controlType: 'pre', text: t }));
    });

  // ---------------------------------------------------------------------------------
  // B10: toast — the Router's pop-up (bottom-right; green success / red error): the message as
  // the card's own text, then an icon-only X. The shell speaks kind 'alert' immediately, once
  // per appearance; the X reads "Close".
  // ---------------------------------------------------------------------------------

  A.isToast = function (el) {
    if (el.tagName !== 'DIV' || !A.hasClass(el, 'absolute') || !A.hasClass(el, 'bottom-0') || !A.hasClass(el, 'right-0') || !A.hasClass(el, 'z-10')) return false;
    if (el.children.length !== 1 || el.children[0].tagName !== 'DIV') return false;
    return el.children[0].getElementsByTagName('button').length === 1;
  };

  A.block('toast', A.isToast, function (el, els) {
    var card = el.children[0], msg = A.ownText(card), btn = card.getElementsByTagName('button')[0];
    if (msg) els.push(A.el(card, { kind: 'alert', live: 'assertive', text: msg }));
    els.push(A.el(btn, { kind: 'button', clickable: true, text: 'Close' }));
  });

  // ---------------------------------------------------------------------------------
  // B12: Charts chart-type strip — a row of buttons (STAR/APP/TAXI/SID/REF) where the active one
  // carries a coloured bg-*-500 class and the others bg-navigraph-background. Read as tabs.
  // ---------------------------------------------------------------------------------

  A.isChartStrip = function (el) {
    if (el.tagName !== 'DIV' || el.getAttribute('role') === 'group') return false;
    var bs = A.buttonChildren(el);
    if (!bs || bs.length < 2) return false;
    var active = 0;
    for (var i = 0; i < bs.length; i++) {
      if (A.hasClass(bs[i], 'bg-navigraph-background')) continue;
      if (/(^|\s)bg-[a-z]+-500(\s|$)/.test(String(bs[i].className || ''))) { active++; continue; }
      return false;
    }
    return active === 1;
  };

  A.block('chart-strip', A.isChartStrip, function (el, els) {
    var bs = A.buttonChildren(el);
    for (var i = 0; i < bs.length; i++) {
      var on = !A.hasClass(bs[i], 'bg-navigraph-background');
      els.push(A.el(bs[i], { kind: 'tab', clickable: true, disabled: !!bs[i].disabled, text: A.txt(bs[i]) + (on ? ' (selected)' : '') }));
    }
  });

  // ---------------------------------------------------------------------------------
  // D1: the Dispatch flight header (FlightPlanViewer). The EFB shows callsign, aircraft, origin,
  // destination, times and alternate with NO captions at all — position is the only cue — so
  // this page-specific pass names them. Every match is gated on the Dispatch tab being active and
  // on the exact row shape; an unrecognised layout falls through to the generic reading.
  // ---------------------------------------------------------------------------------

  A._page = '';
  A.isDispatch = function () { return A._page === 'Dispatch'; };

  A.onlyChild = function (el, tag) {
    return (el.children.length === 1 && el.children[0].tagName === tag) ? el.children[0] : null;
  };

  // <div><h2>BVI2GP</h2><div>TFDi MD-11F GE (TFDI-MD11)</div></div>
  A.dispatchRowA = function (el) {
    if (!A.isDispatch() || el.tagName !== 'DIV' || el.children.length !== 2) return null;
    var h = el.children[0], d = el.children[1];
    if (h.tagName !== 'H2' || d.tagName !== 'DIV' || d.children.length > 0) return null;
    var flight = A.txt(h), ac = A.txt(d);
    if (!flight || !ac) return null;
    return ['Flight ' + flight, 'Aircraft ' + ac];
  };

  // <div><div><h1>KMEM</h1></div><div>…<p>03:50 (air: 03:22)</p><p>34000 ft (CI 20)</p>…</div><div><h1>KLAX</h1></div></div>
  A.dispatchRowB = function (el) {
    if (!A.isDispatch() || el.tagName !== 'DIV' || el.children.length !== 3) return null;
    var from = A.onlyChild(el.children[0], 'H1'), to = A.onlyChild(el.children[2], 'H1');
    if (!from || !to) return null;
    var ps = el.children[1].getElementsByTagName('p');
    if (ps.length !== 2) return null;
    return ['From ' + A.txt(from), 'Block time ' + A.txt(ps[0]).replace('(air: ', '(air '), 'Cruise ' + A.txt(ps[1]), 'To ' + A.txt(to)];
  };

  // <div><div><h3>23:35 UTC</h3></div><div><h3>3:25 UTC</h3><h3>[KONT]</h3></div></div>
  A.dispatchRowC = function (el) {
    if (!A.isDispatch() || el.tagName !== 'DIV' || el.children.length !== 2) return null;
    var dep = A.onlyChild(el.children[0], 'H3'), r = el.children[1];
    if (!dep || r.children.length !== 2 || r.children[0].tagName !== 'H3' || r.children[1].tagName !== 'H3') return null;
    var alt = A.txt(r.children[1]).replace(/^\[|\]$/g, '');
    return ['Departure ' + A.txt(dep), 'Arrival ' + A.txt(r.children[0]), 'Alternate ' + alt];
  };

  // <div class="pb-3 text-center …"><p>CHLDR5 ANSWA …</p></div>
  A.dispatchRoute = function (el) {
    if (!A.isDispatch() || el.tagName !== 'DIV' || !A.hasClass(el, 'pb-3')) return null;
    var p = A.onlyChild(el, 'P');
    if (!p) return null;
    var t = A.txt(p);
    return t ? ['Route ' + t] : null;
  };

  A.dispatchBlock = function (name, fn) {
    A.block(name,
      function (el) { return !!fn(el); },
      function (el, els) {
        var ls = fn(el);
        for (var i = 0; i < ls.length; i++) els.push(A.el(i === 0 ? el : null, { kind: 'static', text: ls[i] }));
      });
  };
  A.dispatchBlock('dispatch-flight', A.dispatchRowA);
  A.dispatchBlock('dispatch-legs', A.dispatchRowB);
  A.dispatchBlock('dispatch-times', A.dispatchRowC);
  A.dispatchBlock('dispatch-route', A.dispatchRoute);

  // ---------------------------------------------------------------------------------
  // scrape
  // ---------------------------------------------------------------------------------

  A._idx = 0;
  A._everScraped = false;
  A._dirty = true;
  A._pollCount = 0;
  A._obs = null;
  A.OBSERVER_OPTS = { childList: true, subtree: true, characterData: true, attributes: true };
  // Safety net for a change the observer cannot see (a property flipped with neither a DOM mutation
  // nor a re-render). At the window's ~400-600 ms poll this bounds staleness to a few seconds.
  A.FORCE_FULL_EVERY = 10;
  A._markDirty = function () { A._dirty = true; };

  // CHECKED-PROPERTY CAVEAT. A.controlFor reads a toggle's LIVE `el.checked` IDL property, and setValue
  // drives real <input type="checkbox"|"radio"> by clicking them. `.checked` is DECOUPLED from the
  // `checked` CONTENT ATTRIBUTE the moment it is flipped by a tap or by script — neither path writes
  // the attribute — so MutationObserver's attributes:true NEVER fires for a toggle flip and the gate
  // above cannot see it. Without the listeners installed at the bottom of this file, a box the EFB's
  // own UI flipped left the reader reporting the OLD state until the next FORCE_FULL_EVERY poll: ~6 s
  // at the client's 600 ms cadence. A native click DOES fire bubbling 'change'/'input' as part of its
  // default action, so a CAPTURE-phase listener on document closes the gap for every realistic path
  // (a real tablet tap, and our own clickElement/setValue), leaving FORCE_FULL_EVERY as the
  // last-resort net for a property written with neither a mutation nor an event. collect() dispatches
  // no events of its own, so unlike the observer these listeners cannot self-trigger and stay
  // attached across the scrape.


  A.collect = function () {
    var els = [];
    A._idx = 0;

    // Every scrape restamps from 1 in DOM order. A node that is no longer walked (hidden, or
    // detached but still referenced) would keep an old number that collides with a live one,
    // and find(idx) takes the first match in document order — so drop every old stamp first.
    var stale = A.root().querySelectorAll('[' + A.ATTR + ']');
    for (var s = 0; s < stale.length; s++) stale[s].removeAttribute(A.ATTR);

    A._page = A.currentPage();

    // Re-marked every scrape rather than cached: React rebuilds these nodes on every page change,
    // so a stale claim would silence a caption that now belongs to nothing.
    A.clearClaims();
    A.markClaimedLabels();

    // The nav tabs come FIRST, always, whatever page is showing. This is the one bit of the EFB
    // that must never be missing: it is the pilot's only way back to the other pages, and a page
    // whose own content happens to render nothing would otherwise be a dead end with no exit.
    var tabs = A.tabButtons();
    var tabSet = {};
    for (var i = 0; i < tabs.length; i++) {
      var tb = tabs[i];
      tb.setAttribute(A.ATTR, String(++A._idx));
      tabSet[String(A._idx)] = true;
      var active = A.hasClass(tb, A.ACTIVE_TAB_CLASS);
      els.push({
        idx: A._idx,
        // " (current page)" is the app-wide active marker. FbwEfbForm strips this suffix when
        // keying its reconcile, so switching page patches the tab in place instead of destroying
        // the node the user just activated (which would throw the screen reader's focus off it).
        text: A.txt(tb) + (active ? ' (current page)' : ''),
        value: '', controlType: '', kind: 'tab',
        clickable: true, level: 0, live: '', disabled: !!tb.disabled, options: null
      });
    }

    var bar = A.findTabBar();

    // Walk the content. Interactive elements are emitted whole and their subtree skipped —
    // otherwise a button's inner <span> emits again as loose text right after its own button.
    // A subtree the EFB made inert (GroundPage: "This page cannot be used right now" over a
    // pointer-events-none wrapper) is still READ, but every control in it is dimmed — the shell
    // says ", dimmed" and answers a press with "Unavailable" instead of silently doing nothing.
    function walkBody(el) {
      if (A.runBlocks(el, els)) return;              // a building block owns this element and its subtree

      if (A.isControl(el)) {
        var c = A.controlFor(el);                   // stamps el, and dims it on a locked page (A.el)
        if (c.text || c.value || c.controlType) els.push(c);
        return;                                     // do NOT descend into a control
      }

      var lvl = A.headingLevel(el);
      if (lvl > 0) {
        var ht = A.txt(el);
        if (ht) els.push(A.el(el, { kind: 'heading', text: ht, level: lvl }));
        return;
      }

      // Claimed = a control already speaks this element as its label; emitting it again would
      // read every field's caption twice. Still recurse: only THIS node's own text is suppressed.
      var own = el.hasAttribute(A.CLAIM) ? '' : A.ownText(el);
      if (own) els.push(A.el(el, { kind: 'static', text: own }));

      for (var k = 0; k < el.children.length; k++) walk(el.children[k]);
    }

    function walk(el) {
      if (!el || el.nodeType !== 1) return;
      if (el === bar) {                             // its buttons were emitted above, as tabs;
        for (var b = 0; b < el.children.length; b++)   // anything else in the row is still read
          if (el.children[b].tagName !== 'BUTTON') walk(el.children[b]);
        return;
      }
      if (el.tagName === 'SVG' || el.tagName === 'svg') return;   // decorative icons only
      if (A.isHidden(el)) return;
      var inertHere = !A._inert && A.isInertRoot(el);
      if (inertHere) A._inert = true;
      try { walkBody(el); }
      finally { if (inertHere) A._inert = false; }
    }

    A._inert = false;
    walk(A.root());

    return els;
  };

  // SELF-TRIGGER TRAP (why the observer is disconnected around collect(), not left running):
  // collect() MUTATES THE DOM ITSELF. It clears every stale `data-md11-efb-idx`, stamps a fresh one
  // on each element it emits, and sets/clears the `data-md11-efb-label` claim attributes. Those are
  // real attribute mutations inside the observed subtree, so an observer armed with attributes:true
  // queues them; its callback runs as a MICROTASK as soon as this synchronous scrape() returns —
  // after the JSON has gone out, but before the next poll — which re-arms _dirty from the reader's
  // OWN bookkeeping and means `unchanged:true` can never fire on the live page. (The harness only
  // caught this once its test awaited a tick between the two scrapes; without the await no
  // MutationRecord was ever delivered and the gate looked like it worked.) The cost is real: the
  // window polls every ~400-600 ms and the OFP page streams ~100 KB per full scrape.
  //
  // JS is single-threaded and collect() never yields, so NOTHING else can mutate the DOM while it
  // runs — disconnecting for exactly its duration drops only the reader's own stamping and still
  // catches every page-driven mutation, which happens on the page's own async tasks between polls.
  A.scrape = function () {
    try {
      // Dirty gate: if nothing in the DOM changed since the last full scrape, say so and skip the
      // traversal entirely. The client keeps showing what it has. The FIRST scrape after injection
      // is always full (_everScraped), so "unchanged" can never be the client's first answer, and
      // every FORCE_FULL_EVERY-th one is full regardless of the flag.
      A._pollCount++;
      var forceFull = !A._everScraped || (A._pollCount % A.FORCE_FULL_EVERY === 0);
      if (!A._dirty && !forceFull) {
        return JSON.stringify({ ok: true, unchanged: true });
      }
      // Cleared BEFORE the traversal, so a mutation landing mid-scrape dirties the NEXT poll
      // instead of being swallowed.
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

  A.find = function (idx) {
    return document.querySelector('[' + A.ATTR + '="' + idx + '"]');
  };

  A.fire = function (el, type, Ctor) {
    var ev;
    try {
      ev = new Ctor(type, { bubbles: true, cancelable: true, view: window });
    } catch (e) {
      ev = document.createEvent('Event');
      ev.initEvent(type, true, true);
    }
    el.dispatchEvent(ev);
  };

  // The full press: the pointer/mouse sequence React's own synthetic events are built on, then
  // el.click() — a REAL click event with activation behaviour, bubbling, which React's onClick
  // handles exactly ONCE.
  //
  // Until 2026-09-06 a synthetic bubbling 'click' MouseEvent was dispatched here as well, so every
  // press ran the page's onClick TWICE. It went unnoticed because this EFB's handlers close over
  // captured state — a door tile toggles to the state it captured at render, so running it twice
  // lands on the same place — but a handler that read live state or counted presses would have
  // stepped twice per press. The "ONE call = ONE step on the Autobrake stepper" measurement taken
  // live on 2026-09-05 was made WITH the double dispatch, so it does not carry over: the next live
  // check re-measures the stepper.
  A.click = function (el) {
    A.fire(el, 'pointerdown', window.PointerEvent || window.MouseEvent);
    A.fire(el, 'mousedown', window.MouseEvent);
    A.fire(el, 'pointerup', window.PointerEvent || window.MouseEvent);
    A.fire(el, 'mouseup', window.MouseEvent);
    if (typeof el.click === 'function') el.click();
    else A.fire(el, 'click', window.MouseEvent);
  };

  // A stepper press is applied by React on the next tick, so the walk presses, waits, re-reads
  // the field and presses again — never inline. Ends (done(ok)) at the target, on an unknown
  // value, on a press that moved nothing, or after 64 presses.
  A.STEP_DELAY_MS = 60;

  A.stepTo = function (inp, want, done) {
    var opts = A.fiberOptions(inp);
    if (!opts) return false;
    var target = opts.indexOf(want), cur = opts.indexOf(A.inputValue(inp));
    if (target < 0 || cur < 0) return false;
    var presses = 0;
    var finish = function (ok) { A._dirty = true; if (typeof done === 'function') done(ok); };
    (function step() {
      var now = opts.indexOf(A.inputValue(inp));
      if (now === target) { finish(true); return; }
      if (now < 0 || presses >= 64 || (presses > 0 && now === cur)) { finish(false); return; }
      cur = now;
      var cb = A.chevronButtons(inp.parentElement);
      var btn = cb ? (target > now ? cb.down : cb.up) : null;
      if (!btn || btn.disabled) { finish(false); return; }
      presses++;
      A.click(btn);
      setTimeout(step, A.STEP_DELAY_MS);
    })();
    return true;
  };

  A.clickElement = function (idx) {
    var el = A.find(idx);
    if (!el) return false;
    if (A.isInert(el)) return false;      // the EFB has this page locked; never press through it
    if (el.disabled) return false;        // el.click() is a no-op on a disabled control per spec; refuse, don't claim success
    try { A.click(el); A._dirty = true; return true; } catch (e) { return false; }
  };

  A.setValue = function (idx, text, done) {
    var el = A.find(idx);
    if (!el) return false;
    if (A.isInert(el)) return false;      // ditto: a locked page is read, never written
    try {
      // A dropdown built from a choice group: pick = press that choice's own button.
      if (A.isChoiceGroup(el)) {
        var want = String(text).trim(), bs = A.buttonChildren(el);
        for (var b = 0; b < bs.length; b++) {
          if (A.txt(bs[b]) !== want) continue;
          if (bs[b].disabled) return false;
          A.click(bs[b]); A._dirty = true; return true;
        }
        return false;
      }

      // A dropdown built from a stepper: walk the arrows until the field shows the pick.
      if (A.isStepperInput(el)) return A.stepTo(el, String(text).trim(), done);

      if (el.tagName === 'INPUT' && (el.type === 'checkbox' || el.type === 'radio')) {
        var want = (String(text).toLowerCase() === 'true');
        if (el.checked === want) return true;          // already there: nothing to press
        return A.clickElement(idx);                    // React's own handler flips it; a REFUSED press (disabled, locked) is a failed set
      }

      // A greyed-out field takes no input. The native setter below would write it anyway, then
      // 'input'/'change' would tell React it had been typed into, and the set would report success.
      if (el.disabled) return false;

      // React tracks an input's value on the node and IGNORES a plain el.value = x — the change
      // never reaches state. Writing through the prototype's native setter defeats that tracker,
      // then 'input' + 'change' notify React exactly as typing would.
      var proto = el.tagName === 'TEXTAREA'
        ? window.HTMLTextAreaElement.prototype
        : (el.tagName === 'SELECT' ? window.HTMLSelectElement.prototype : window.HTMLInputElement.prototype);
      var desc = Object.getOwnPropertyDescriptor(proto, 'value');
      if (desc && desc.set) desc.set.call(el, String(text));
      else el.value = String(text);

      A.fire(el, 'input', window.Event);
      A.fire(el, 'change', window.Event);
      // Commit like the EFB's keyboard would: its Input runs formatting in onBlur (React: focusout).
      if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') {
        A.fire(el, 'blur', window.FocusEvent || window.Event);
        A.fire(el, 'focusout', window.FocusEvent || window.Event);
      }
      A._dirty = true;
      return true;
    } catch (e) { return false; }
  };

  // ---------------------------------------------------------------------------------
  // install
  // ---------------------------------------------------------------------------------

  // Re-injection must not leave the previous observer or listeners running: the client re-installs
  // on a live socket whenever an eval times out, and a leaked observer per install would mark the
  // page dirty forever, defeating the gate. Re-running this IIFE replaces `A` with a fresh object,
  // so each live handle is stashed on `window` (which survives an injection) for its successor to
  // tear down — the previous `A._markDirty` is a different function object, so the listeners can
  // only be removed through the stashed reference.
  try {
    if (window.__MSFSBA_MD11_EFB_OBS && typeof window.__MSFSBA_MD11_EFB_OBS.disconnect === 'function') {
      window.__MSFSBA_MD11_EFB_OBS.disconnect();
    }
  } catch (e) {}

  try {
    var oldH = window.__MSFSBA_MD11_EFB_HANDLERS;
    if (oldH) {
      try { document.removeEventListener('change', oldH.change, true); } catch (eh1) {}
      try { document.removeEventListener('input', oldH.input, true); } catch (eh2) {}
    }
  } catch (e) {}

  try {
    var obs = new MutationObserver(A._markDirty);
    obs.observe(A.root(), A.OBSERVER_OPTS);
    // Held on A as well as window: scrape() disconnects and re-observes it around collect() (see
    // the SELF-TRIGGER TRAP note), while the window handle is what a re-injection tears down.
    A._obs = obs;
    window.__MSFSBA_MD11_EFB_OBS = obs;
  } catch (e) {
    // No observer -> never gate. Slower, but correct: a stale screen is far worse than a busy one.
    A._dirty = true;
    A.scrape = (function (inner) {
      return function () { A._dirty = true; return inner.apply(A, arguments); };
    })(A.scrape);
  }

  // Installed in their OWN try, outside the observer's: they are the only signal for a toggle flip
  // (see the CHECKED-PROPERTY CAVEAT above), so an observer that failed to construct must not take
  // them down with it. They go on `document`, not A.root(): capture phase reaches document first
  // whatever the observer watches.
  try {
    document.addEventListener('change', A._markDirty, true);
    document.addEventListener('input', A._markDirty, true);
    window.__MSFSBA_MD11_EFB_HANDLERS = { change: A._markDirty, input: A._markDirty };
  } catch (e) {}

  window.__MSFSBA_MD11_EFB = A;
  return A.INSTALLED;
})();
