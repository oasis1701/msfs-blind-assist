// Fenix EFB accessibility layer, injected into the Fenix EFB web page by FenixEFBForm.
// It only adds roles, names, states, Tab stops and key handlers that call the page's own click (and takes
// back what it wrote itself when the page moves on) -- never a request, a removed element, written text or
// a written value, and never an override of a page's own aria-hidden, aria-label, role or tabindex.
// It runs in the top document only. Plain ES2017, no modules.
(function () {
  if (window.__msfsbaFenixEfb) { return; }
  // WebView2 runs a document-created script in every iframe too; the layer belongs to the top document
  // only (an iframe gets no export, no observer, no Alt+Home). A window that cannot be compared is a frame.
  try { if (window.top !== window) { return; } } catch (err) { return; }

  // Marks every element the layer has handled, so a later pass knows it has been here.
  var MARK = 'data-msfsba';

  // The titles of the nine home tiles, in home order. The switcher's icons are pictures with no text,
  // so they take these names (plan 03, Owner decision 5), and only when the switcher holds nine.
  var TILE_TITLES = ['Fenix', 'Takeoff', 'Landing', 'Charts', 'Documents', 'Failures', 'Settings', 'MCDU', 'FAP'];

  // Elements whose edges (and <br>) are a word boundary in the text a reader says.
  var BLOCK_TAGS = ' address article aside blockquote br caption dd details dialog div dl dt fieldset ' +
    'figcaption figure footer form h1 h2 h3 h4 h5 h6 header hr li main nav ol p pre section summary ' +
    'table tbody td tfoot th thead tr ul ';
  // Elements whose content is never visible text.
  var SKIP_TAGS = ' script style noscript template ';

  // Elements that already have a keydown handler from the layer. A page that clones a node copies its
  // attributes but not its listeners, so the element itself is the record, not the attribute.
  var handled = new WeakSet();

  // The aria-label the layer wrote on each element. A label that is there and is not the one written is
  // the page's, and the layer never touches it; the one it wrote it keeps in step with the text.
  var written = new WeakMap();

  // The elements the layer currently treats as buttons. A key on any other element is not its business: a
  // listener cannot be taken off an element, so a withdrawn one keeps its listener and the handler checks.
  var pressable = new WeakSet();
  // The elements whose role the layer wrote (a page's own role is never removed).
  var wroteRole = new WeakSet();
  // The tabindex an element had before the layer set it to 0: the attribute's text, or null for none. Its
  // presence means the layer wrote the 0. It is put back when the control stops being a button or is
  // disabled.
  var tabOriginal = new WeakMap();
  // The aria-expanded the layer wrote on the app switcher's chevron (the way `written` keeps a label).
  var expandedWritten = new WeakMap();

  function collect(node, out, isRoot) {
    if (node.nodeType === 3) { out.push(node.nodeValue); return; }
    if (node.nodeType !== 1) { return; }
    if (!isRoot && node.getAttribute('aria-hidden') === 'true') { return; }
    var tag = (node.localName || '').toLowerCase();
    if (SKIP_TAGS.indexOf(' ' + tag + ' ') !== -1) { return; }
    var block = BLOCK_TAGS.indexOf(' ' + tag + ' ') !== -1;
    if (block) { out.push(' '); }
    for (var child = node.firstChild; child; child = child.nextSibling) { collect(child, out, false); }
    if (block) { out.push(' '); }
  }

  // The element's visible text: <br> and block boundaries are single spaces, runs of white space are
  // one space, trimmed; anything the page marks aria-hidden is left out.
  function textOf(el) {
    if (!el) { return ''; }
    var out = [];
    collect(el, out, true);
    return out.join('').replace(/\s+/g, ' ').trim();
  }

  function mark(el) {
    if (el.getAttribute(MARK) !== '1') { el.setAttribute(MARK, '1'); }
  }

  // Writes an attribute only when it differs, so a second pass writes nothing.
  function setAttr(el, attr, value) {
    if (el.getAttribute(attr) !== value) { el.setAttribute(attr, value); }
  }

  // Gives the element an accessible name. A non-empty label the page wrote is never touched. A label the
  // layer wrote follows the text: rewritten when it changes, removed when it becomes empty (no name beats
  // a stale one). Only attributes are written, so a change here never wakes the observer.
  function name(el, text) {
    var existing = el.getAttribute('aria-label');
    var mine = written.get(el);
    var ours = mine !== undefined && existing === mine;
    if (!ours && existing !== null && existing.trim() !== '') {
      written.delete(el); // the page's label (it may have replaced ours): not ours any more
      return;
    }
    if (!text) {
      if (ours) { el.removeAttribute('aria-label'); }
      written.delete(el);
      return;
    }
    if (existing !== text) { el.setAttribute('aria-label', text); }
    written.set(el, text);
    mark(el);
  }

  // True when the page marks the control unavailable: aria-disabled "true", the disabled attribute or
  // property, or Material's mat-tab-disabled class.
  function isDisabled(el) {
    return el.getAttribute('aria-disabled') === 'true' || el.hasAttribute('disabled') || el.disabled === true ||
      el.classList.contains('mat-tab-disabled');
  }

  // Puts the tabindex back to what it was before the layer set it to 0 (none, or the page's own -1). A
  // tabindex the page has changed since is the page's, and stays.
  function restoreTab(el) {
    if (!tabOriginal.has(el)) { return; }
    var original = tabOriginal.get(el);
    tabOriginal.delete(el);
    if (el.getAttribute('tabindex') !== '0') { return; }
    if (original === null) { el.removeAttribute('tabindex'); } else { el.setAttribute('tabindex', original); }
  }

  function onButtonKey(e) {
    try {
      if (e.key !== 'Enter' && e.key !== ' ' && e.key !== 'Spacebar') { return; }
      if (e.altKey || e.ctrlKey || e.metaKey) { return; }
      // A withdrawn element, or one the page has disabled, is not pressed and its keys stay the page's.
      if (!pressable.has(e.currentTarget) || isDisabled(e.currentTarget)) { return; }
      var target = e.target;
      // Typing in a text box is never caught.
      var tag = target && target.localName ? target.localName.toLowerCase() : '';
      if (tag === 'input' || tag === 'textarea' || tag === 'select' || (target && target.isContentEditable)) { return; }
      // Only the button itself is pressed: a control inside it (a link, a drop-down) keeps its own keys.
      if (target !== e.currentTarget) { return; }
      e.preventDefault();
      // A held key presses once, as the page's own buttons do.
      if (!e.repeat) { e.currentTarget.click(); }
    } catch (err) { /* never throw into the page */ }
  }

  // Makes an element a button a keyboard and a screen reader can use: a role, a Tab stop, a name, and
  // Enter / Space calling the page's own click. A role or a label the page gave is kept; an element the
  // Tab key skips (no tabindex, or a negative one) gets tabindex 0 -- unless the page marks it disabled,
  // which is no Tab stop and takes no key (a tab stop the layer gave before is taken back).
  function button(el, text) {
    if (!el.getAttribute('role')) { el.setAttribute('role', 'button'); wroteRole.add(el); }
    if (isDisabled(el)) {
      restoreTab(el);
    } else if (el.tabIndex < 0) {
      tabOriginal.set(el, el.getAttribute('tabindex'));
      el.setAttribute('tabindex', '0');
    }
    name(el, text);
    mark(el);
    pressable.add(el);
    if (!handled.has(el)) {
      handled.add(el);
      el.addEventListener('keydown', onButtonKey);
    }
  }

  // Takes back what button() gave an element, and only what the layer wrote itself: its role, its tab stop
  // (put back to what it was), its label and its stamp. The listener stays; the handler ignores the element.
  function unbutton(el) {
    if (!pressable.has(el)) { return; }
    pressable.delete(el);
    if (wroteRole.has(el)) {
      if (el.getAttribute('role') === 'button') { el.removeAttribute('role'); }
      wroteRole.delete(el);
    }
    restoreTab(el);
    name(el, '');
    if (el.getAttribute(MARK) === '1') { el.removeAttribute(MARK); }
  }

  // Each rule is a function (root). Later tasks add their rules here; a rule that fails never stops the
  // others (pass() runs each in its own try/catch).
  var RULES = [];

  // Home: each application tile is a div.appIcon holding a picture, a <br> and the title.
  RULES.push(function (root) {
    var tiles = root.querySelectorAll('#appContainer > div.appIcon');
    for (var i = 0; i < tiles.length; i++) {
      var title = tiles[i].querySelector('.appTitle');
      button(tiles[i], textOf(title || tiles[i]));
    }
  });

  // Every app: the home button is an empty draggable div.
  RULES.push(function (root) {
    var buttons = root.querySelectorAll('div#homeButtonFree.home-button');
    for (var i = 0; i < buttons.length; i++) { button(buttons[i], 'Home'); }
  });

  // Every app: the top bar's chevron opens the app switcher. It is a bare div and the only way to the
  // switcher. Where the page hides it (home, quick settings: class top-bar-slot-hidden, aria-hidden
  // "true") it is no Tab stop, also if it was one a moment ago (the tab stop the layer gave is taken back).
  // While it is shown, the page's own icon says whether the switcher is open: the chevron points up while
  // it is, down while it is closed. That aria-expanded is the layer's own only while the layer wrote it: a
  // state the page sets is never touched, and an icon the layer does not know takes back only its own.
  RULES.push(function (root) {
    var arrows = root.querySelectorAll('div.popup-arrow');
    for (var i = 0; i < arrows.length; i++) {
      var arrow = arrows[i];
      if (arrow.getAttribute('aria-hidden') === 'true' || arrow.classList.contains('top-bar-slot-hidden')) {
        restoreTab(arrow);
      } else {
        button(arrow, 'App switcher');
        var icon = arrow.querySelector('i');
        var expanded = null;
        if (icon && icon.classList.contains('fa-chevron-up')) { expanded = 'true'; }
        else if (icon && icon.classList.contains('fa-chevron-down')) { expanded = 'false'; }
        var existing = arrow.getAttribute('aria-expanded');
        var mine = expandedWritten.get(arrow);
        var ours = mine !== undefined && existing === mine;
        if (existing !== null && !ours) {
          expandedWritten.delete(arrow); // the page's own state: not ours to change or take back
        } else if (expanded !== null) {
          setAttr(arrow, 'aria-expanded', expanded);
          expandedWritten.set(arrow, expanded);
        } else {
          if (ours) { arrow.removeAttribute('aria-expanded'); }
          expandedWritten.delete(arrow);
        }
      }
    }
  });

  // True when the picture's parent holds nothing else: no element but the picture and line breaks, and no
  // text of its own. The live switcher has <div class="col noSel"><img class="app-img"><br></div>.
  function holdsOnly(parent, img) {
    for (var child = parent.firstElementChild; child; child = child.nextElementSibling) {
      if (child !== img && child.localName !== 'br') { return false; }
    }
    return textOf(parent) === '';
  }

  // The app switcher: pictures without names, each in its own clickable parent. The home tiles hold the
  // same pictures, so they are told apart by their div.appIcon container.
  RULES.push(function (root) {
    var all = root.querySelectorAll('img.app-img');
    var icons = [];
    for (var i = 0; i < all.length; i++) {
      if (!all[i].closest('div.appIcon')) { icons.push(all[i]); }
    }
    // Names only when the switcher is the nine apps in home order; otherwise they stay unnamed.
    var named = icons.length === TILE_TITLES.length;
    for (var j = 0; j < icons.length; j++) {
      var parent = icons[j].parentElement;
      // The parent is the clickable thing when it is only the picture's holder; otherwise (it holds more,
      // or text) pressing the picture itself reaches the page's own handler.
      var target = parent && parent !== root && holdsOnly(parent, icons[j]) ? parent : icons[j];
      button(target, named ? TILE_TITLES[j] : '');
    }
  });

  // ---- The Fenix app: My Flights, Ground Services, Mass and Balance, Panel States ----

  // The app's tab bar (Angular Material's nav bar). Its two paging arrows have no text: they get their
  // decision-5 names as a label. Material marks them aria-hidden "true" and the layer never overrides a
  // page's own aria-hidden, so they keep it (a screen reader meets them only if the page ever shows them);
  // they stay as the page has them otherwise (disabled when there is nothing to scroll, out of the Tab
  // order, real buttons that answer their own keys). The Panel States tab is a link with no href, so it has
  // no role and a screen reader does not say it can be pressed: it becomes a button. The links that have an
  // href are links already and are left alone.
  RULES.push(function (root) {
    var arrows = root.querySelectorAll(
      '.mat-tab-nav-bar button.mat-tab-header-pagination-before, .mat-tab-nav-bar button.mat-tab-header-pagination-after');
    for (var i = 0; i < arrows.length; i++) {
      var before = arrows[i].classList.contains('mat-tab-header-pagination-before');
      name(arrows[i], before ? 'Previous tabs' : 'Next tabs');
    }
    var links = root.querySelectorAll('.mat-tab-nav-bar a.mat-tab-link:not([href])');
    for (var j = 0; j < links.length; j++) { button(links[j], textOf(links[j])); }
  });

  // The title row of the My Flights card holding an element ("A320 Flight", "Route", "Weather").
  function cardTitle(card) {
    return textOf(card ? card.querySelector('.slide-title') : null);
  }

  // My Flights: three cards (flight, route, weather) slide past each other; the page marks the one in front
  // active-tab. Only the active card's import zone is offered: it imports from SimBrief when pressed, and
  // its text is the same on all three, so its name starts with the card's title ("A320 Flight: Tap to
  // import from simbrief"). A side card's zone is not a button, and one that stops being the active card's
  // is withdrawn (what the layer wrote is taken back, nothing else).
  RULES.push(function (root) {
    var zones = root.querySelectorAll('div.import-sb-zone');
    for (var i = 0; i < zones.length; i++) {
      var card = zones[i].closest('.slide-content');
      if (card && !card.classList.contains('active-tab')) { unbutton(zones[i]); continue; }
      var heading = cardTitle(card || zones[i].parentElement);
      var text = textOf(zones[i]);
      button(zones[i], heading && text ? heading + ': ' + text : (heading || text));
    }
  });

  // My Flights: a side card lies under a div.slide-prevent-click; pressing it slides that card to the front
  // and imports nothing. It is the button "Show <card title>", and Enter is its own click. The page adds and
  // removes the overlay itself (the active card has none); one that sits in an active card, or has no card
  // title to name it by, is withdrawn.
  RULES.push(function (root) {
    var overlays = root.querySelectorAll('app-my-flight-tab .slide-prevent-click');
    for (var i = 0; i < overlays.length; i++) {
      var card = overlays[i].closest('.slide-content');
      var heading = card && !card.classList.contains('active-tab') ? cardTitle(card) : '';
      if (heading) { button(overlays[i], 'Show ' + heading); } else { unbutton(overlays[i]); }
    }
  });

  // The first div.text beside an element (its sibling in the same parent), or null.
  function textBeside(el) {
    var parent = el.parentElement;
    if (!parent) { return null; }
    for (var child = parent.firstElementChild; child; child = child.nextElementSibling) {
      if (child !== el && child.localName === 'div' && child.classList.contains('text')) { return child; }
    }
    return null;
  }

  // Ground Services: each slide toggle's input is a switch with no name; its text is the div.text beside the
  // toggle. The page keeps the switch's state (aria-checked) itself. A switch with no text beside it stays
  // unnamed rather than guessed.
  RULES.push(function (root) {
    var switches = root.querySelectorAll('input[role=switch]');
    for (var i = 0; i < switches.length; i++) {
      var toggle = switches[i].closest('mat-slide-toggle');
      var label = toggle ? textBeside(toggle) : null;
      if (label) { name(switches[i], textOf(label)); }
    }
  });

  // Ground Services: the three pushback arrows are bare divs holding an arrow picture (owner decision 5).
  var PUSHBACK = [
    ['left-arrow', 'Pushback left'],
    ['down-arrow', 'Pushback straight'],
    ['right-arrow', 'Pushback right']
  ];
  RULES.push(function (root) {
    for (var k = 0; k < PUSHBACK.length; k++) {
      var arrows = root.querySelectorAll('app-ground-services-tab div.' + PUSHBACK[k][0]);
      for (var i = 0; i < arrows.length; i++) { button(arrows[i], PUSHBACK[k][1]); }
    }
  });

  // Elements that sit inside a line of text.
  var INLINE_TAGS = ' a abbr b bdi bdo cite code em hr i kbd mark small span strong sub sup time u ';

  // A Mass and Balance section's title: the text just before its table (the rule between them is skipped),
  // up to the first parenthesis ("Cargo (Capacity: ...)" is "Cargo"). Comment markers Angular leaves between
  // nodes are skipped; anything that is not a line of text ends the title.
  function sectionTitle(table) {
    var parts = [];
    for (var n = table.previousSibling; n; n = n.previousSibling) {
      if (n.nodeType === 8) { continue; }
      if (n.nodeType === 3) { parts.unshift(n.nodeValue); continue; }
      if (n.nodeType !== 1) { break; }
      var tag = (n.localName || '').toLowerCase();
      if (INLINE_TAGS.indexOf(' ' + tag + ' ') === -1) { break; }
      if (tag !== 'hr') { parts.unshift(n.textContent); }
    }
    var text = parts.join('').replace(/\s+/g, ' ');
    var paren = text.indexOf('(');
    return (paren === -1 ? text : text.slice(0, paren)).trim();
  }

  // The text of the table's first row at the box's column ("Planned (KG)"), or '' when the box is in the
  // first row itself or the table has no cell there.
  function columnHeader(box, table) {
    var cell = box.closest('td, th');
    var head = table.querySelector('tr');
    if (!cell || !head || head === cell.parentElement || !head.cells) { return ''; }
    return textOf(head.cells[cell.cellIndex]);
  }

  // Mass and Balance: each box is named "<section> <column header>, <placeholder>", every part the page's
  // own ("Passengers Planned, Max 180"). Parts the page does not give are left out.
  RULES.push(function (root) {
    var boxes = root.querySelectorAll('input.planned-input');
    for (var i = 0; i < boxes.length; i++) {
      var table = boxes[i].closest('table');
      var where = table ? [sectionTitle(table), columnHeader(boxes[i], table)].filter(Boolean).join(' ') : '';
      var hint = (boxes[i].getAttribute('placeholder') || '').replace(/\s+/g, ' ').trim();
      name(boxes[i], where && hint ? where + ', ' + hint : (where || hint));
    }
  });

  // Mass and Balance: the four action buttons break their text with <br> ("Reset<br>All"); named by the
  // text with each break a space.
  RULES.push(function (root) {
    var buttons = root.querySelectorAll('div.action-button-cell > button');
    for (var i = 0; i < buttons.length; i++) { name(buttons[i], textOf(buttons[i])); }
  });

  // Panel States: the pop-up has four buttons and no title (owner decision 5 gives it one). Other dialogs
  // are not this rule's.
  RULES.push(function (root) {
    var dialogs = root.querySelectorAll('mat-dialog-container');
    for (var i = 0; i < dialogs.length; i++) {
      if (!dialogs[i].querySelector('app-dialog-panel-states')) { continue; }
      if (!dialogs[i].getAttribute('role')) { dialogs[i].setAttribute('role', 'dialog'); }
      name(dialogs[i], 'Panel states');
    }
  });

  // The progress, 0 to 100, from the percentage in the width of the bar's filled span (the page keeps it
  // there; the bar's own value attribute is empty). A width that is not a percentage gives none.
  function widthPercent(fill) {
    var m = /^\s*(\d+(?:\.\d+)?)\s*%\s*$/.exec(fill.style.width || '');
    return m ? String(Math.round(Math.min(100, parseFloat(m[1])) * 100) / 100) : null;
  }

  // Loading notices: the page's notice becomes a polite live region (a live region the page set is the
  // page's) ...
  RULES.push(function (root) {
    var notices = root.querySelectorAll('fap-notification');
    for (var i = 0; i < notices.length; i++) {
      if (!notices[i].getAttribute('aria-live')) { notices[i].setAttribute('aria-live', 'polite'); }
      mark(notices[i]);
    }
  });

  // ... and its progress bar a progress bar with its value. A bar the page gave another role is the page's
  // and is left alone. A rule of its own, so a failure in the notice rule never skips the bars.
  RULES.push(function (root) {
    var bars = root.querySelectorAll('fap-progress-bar');
    for (var j = 0; j < bars.length; j++) {
      var role = bars[j].getAttribute('role');
      if (role && role !== 'progressbar') { continue; }
      setAttr(bars[j], 'role', 'progressbar');
      setAttr(bars[j], 'aria-valuemin', '0');
      setAttr(bars[j], 'aria-valuemax', '100');
      var fill = bars[j].querySelector('span.bar');
      var now = fill ? widthPercent(fill) : null;
      if (now !== null) { setAttr(bars[j], 'aria-valuenow', now); }
      mark(bars[j]);
    }
  });

  function pass() {
    var root = document.body;
    if (!root) { return; }
    for (var i = 0; i < RULES.length; i++) {
      try { RULES[i](root); } catch (err) { /* a failing rule never stops the others or reaches the page */ }
    }
  }

  // One pass per 50 ms however many mutations arrive. The observer sees child-list changes, text changes
  // (Angular updates a text node in place), the class attribute and the two attributes a page disables a
  // control with (aria-disabled, disabled), so the layer's own writes (role, aria-label and the other
  // aria-* it writes, tabindex, data-msfsba: attributes, never text or nodes, never those two) never wake it.
  var timer = null;
  function schedule() {
    if (timer !== null) { return; }
    timer = setTimeout(function () {
      timer = null;
      pass();
    }, 50);
  }

  function start() {
    try {
      pass();
      new MutationObserver(function () {
        try { schedule(); } catch (err) { /* never throw into the page */ }
      }).observe(document.body, {
        childList: true, subtree: true, characterData: true, attributes: true,
        attributeFilter: ['class', 'aria-disabled', 'disabled']
      });
    } catch (err) { /* never throw into the page */ }
  }

  // Alt+Home goes to the EFB home from anywhere, a text box included (the iFly window's key).
  window.addEventListener('keydown', function (e) {
    try {
      if (e.key === 'Home' && e.altKey && !e.ctrlKey && !e.metaKey) {
        window.location.hash = '#/efb';
        e.preventDefault();
      }
    } catch (err) { /* never throw into the page */ }
  }, true);

  // The script runs when the document is created, before <body> exists: start once it does.
  if (document.body) {
    start();
  } else {
    document.addEventListener('DOMContentLoaded', start, { once: true });
  }

  window.__msfsbaFenixEfb = { pass: pass, textOf: textOf };
})();
