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

  A.txt = function (el) {
    var t = el ? (el.innerText || el.textContent || '') : '';
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

  // ---------------------------------------------------------------------------------
  // the nav bar
  // ---------------------------------------------------------------------------------

  // The tab bar is the first element whose direct children are >=5 buttons. Found structurally
  // rather than by class: the class list is Tailwind soup that changes with any restyle, whereas
  // "a row of button siblings at the top of the app" is what the bar actually IS.
  A.findTabBar = function () {
    var divs = A.root().querySelectorAll('div');
    for (var i = 0; i < divs.length; i++) {
      var kids = divs[i].children, n = 0;
      for (var j = 0; j < kids.length; j++) if (kids[j].tagName === 'BUTTON') n++;
      if (n >= A.TAB_BAR_MIN && n === kids.length) return divs[i];
    }
    return null;
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

  A.controlFor = function (el, idx) {
    var o = {
      idx: idx, text: '', value: '', controlType: '', kind: '',
      clickable: false, level: 0, live: '', disabled: !!el.disabled, options: null
    };

    if (el.tagName === 'INPUT') {
      var ty = (el.type || 'text').toLowerCase();
      if (ty === 'checkbox' || ty === 'radio') {
        o.controlType = 'checkbox';
        o.value = el.checked ? 'true' : 'false';
      } else if (ty === 'range') {
        o.controlType = 'range';
        o.value = String(el.value == null ? '' : el.value);
        if (el.min !== '') o.min = Number(el.min);
        if (el.max !== '') o.max = Number(el.max);
        if (el.step !== '') o.step = Number(el.step);
      } else {
        o.controlType = 'text';
        o.value = String(el.value == null ? '' : el.value);
      }
      // An unlabelled input is useless to a screen reader, so fall back through every label
      // source the EFB might have used before giving up.
      o.text = A.labelFor(el);
      return o;
    }

    if (el.tagName === 'SELECT') {
      o.controlType = 'select';
      o.value = String(el.value == null ? '' : el.value);
      o.options = [];
      for (var i = 0; i < el.options.length; i++) o.options.push(A.txt(el.options[i]));
      o.text = A.labelFor(el);
      return o;
    }

    if (el.tagName === 'A') {
      o.kind = 'link';
      o.text = A.txt(el);
      return o;
    }

    // BUTTON
    o.kind = 'button';
    o.clickable = true;
    o.text = A.txt(el) || el.getAttribute('aria-label') || el.getAttribute('title') || '';
    return o;
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
    }
  };

  A.clearClaims = function () {
    var was = A.root().querySelectorAll('[' + A.CLAIM + ']');
    for (var i = 0; i < was.length; i++) was[i].removeAttribute(A.CLAIM);
  };

  // ---------------------------------------------------------------------------------
  // scrape
  // ---------------------------------------------------------------------------------

  A._idx = 0;
  A._everScraped = false;
  A._dirty = true;


  A.collect = function () {
    var els = [];
    A._idx = 0;

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
    (function walk(el) {
      if (!el || el.nodeType !== 1) return;
      if (el === bar) return;                       // already emitted above, as tabs
      if (el.tagName === 'SVG' || el.tagName === 'svg') return;   // decorative icons only
      if (A.isHidden(el)) return;

      if (A.isControl(el)) {
        el.setAttribute(A.ATTR, String(++A._idx));
        var c = A.controlFor(el, A._idx);
        if (c.text || c.value || c.controlType) els.push(c);
        return;                                     // do NOT descend into a control
      }

      var lvl = A.headingLevel(el);
      if (lvl > 0) {
        var ht = A.txt(el);
        if (ht) {
          el.setAttribute(A.ATTR, String(++A._idx));
          els.push({
            idx: A._idx, text: ht, value: '', controlType: '', kind: 'heading',
            clickable: false, level: lvl, live: '', disabled: false, options: null
          });
        }
        return;
      }

      // Claimed = a control already speaks this element as its label; emitting it again would
      // read every field's caption twice. Still recurse: only THIS node's own text is suppressed.
      var own = el.hasAttribute(A.CLAIM) ? '' : A.ownText(el);
      if (own) {
        el.setAttribute(A.ATTR, String(++A._idx));
        els.push({
          idx: A._idx, text: own, value: '', controlType: '', kind: 'static',
          clickable: false, level: 0, live: '', disabled: false, options: null
        });
      }

      for (var k = 0; k < el.children.length; k++) walk(el.children[k]);
    })(A.root());

    return els;
  };

  A.scrape = function () {
    try {
      // Dirty gate: if nothing in the DOM changed since the last full scrape, say so and skip the
      // traversal entirely. The client keeps showing what it has. The FIRST scrape after injection
      // is always full (_everScraped), so "unchanged" can never be the client's first answer.
      if (A._everScraped && !A._dirty) {
        return JSON.stringify({ ok: true, unchanged: true });
      }
      A._dirty = false;
      A._everScraped = true;

      return JSON.stringify({ ok: true, page: A.currentPage(), elements: A.collect() });
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

  // React listens for the full pointer/mouse sequence, not a bare .click(). The WT787 CDU keys
  // taught this repo the same lesson: dispatch the whole sequence or the component never reacts —
  // and a click that silently does nothing is indistinguishable from a working one to a blind
  // pilot. .click() is also fired last, which covers plain onClick handlers.
  A.clickElement = function (idx) {
    var el = A.find(idx);
    if (!el) return false;
    try {
      A.fire(el, 'pointerdown', window.PointerEvent || window.MouseEvent);
      A.fire(el, 'mousedown', window.MouseEvent);
      A.fire(el, 'pointerup', window.PointerEvent || window.MouseEvent);
      A.fire(el, 'mouseup', window.MouseEvent);
      A.fire(el, 'click', window.MouseEvent);
      if (typeof el.click === 'function') el.click();
      A._dirty = true;
      return true;
    } catch (e) { return false; }
  };

  A.setValue = function (idx, text) {
    var el = A.find(idx);
    if (!el) return false;
    try {
      if (el.tagName === 'INPUT' && (el.type === 'checkbox' || el.type === 'radio')) {
        var want = (String(text).toLowerCase() === 'true');
        if (el.checked !== want) A.clickElement(idx);   // let React's own handler flip it
        return true;
      }

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
      A._dirty = true;
      return true;
    } catch (e) { return false; }
  };

  // ---------------------------------------------------------------------------------
  // install
  // ---------------------------------------------------------------------------------

  // Re-injection must not leave the previous observer running: the client re-installs on a live
  // socket whenever an eval times out, and a leaked observer per install would mark the page dirty
  // forever, defeating the gate.
  try {
    if (window.__MSFSBA_MD11_EFB_OBS && typeof window.__MSFSBA_MD11_EFB_OBS.disconnect === 'function') {
      window.__MSFSBA_MD11_EFB_OBS.disconnect();
    }
  } catch (e) {}

  try {
    var obs = new MutationObserver(function () { A._dirty = true; });
    obs.observe(A.root(), { childList: true, subtree: true, characterData: true, attributes: true });
    window.__MSFSBA_MD11_EFB_OBS = obs;
  } catch (e) {
    // No observer -> never gate. Slower, but correct: a stale screen is far worse than a busy one.
    A._dirty = true;
    A.scrape = (function (inner) {
      return function () { A._dirty = true; return inner.apply(A, arguments); };
    })(A.scrape);
  }

  window.__MSFSBA_MD11_EFB = A;
  return A.INSTALLED;
})();
