// Fenix EFB accessibility layer, injected into the Fenix EFB web page by FenixEFBForm.
// It only adds roles, names, states, Tab stops and key handlers that call the page's own click, and takes
// back what it wrote itself when the page moves on -- never a request, a removed or moved element, written
// text or a written value. It never overrides a page's own aria-hidden, aria-label or role, never gives a
// disabled control a Tab stop, and takes back only its own Tab stops. (It does give tabindex 0 to an enabled
// control the Tab key skips, one the page set to -1 too, and puts the page's own value back afterwards.)
// Two more attributes it writes: a drop-down's aria-labelledby gets the drop-down's own id in front of the
// page's list (the page's list goes back when the layer's label goes), and aria-hidden "true" goes only on
// a results label whose value cell carries that label.
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

  // The elements the layer currently treats as buttons (or radios). A key on any other element is not its
  // business: a listener cannot be taken off an element, so a withdrawn one keeps its listener and the
  // handler checks.
  var pressable = new WeakSet();
  // The role the layer wrote on each element (a page's own role is never removed).
  var wroteRole = new WeakMap();
  // The tabindex an element had before the layer set it to 0: the attribute's text, or null for none. Its
  // presence means the layer wrote the 0. It is put back when the control stops being a button or is
  // disabled.
  var tabOriginal = new WeakMap();
  // The aria-expanded the layer wrote on the app switcher's chevron and on a failure chapter (the way
  // `written` keeps a label).
  var expandedWritten = new WeakMap();
  // The aria-checked the layer wrote on a radio, the same way.
  var checkedWritten = new WeakMap();
  // The aria-pressed it wrote on a Settings option and a map control, and the aria-current on a category.
  var pressedWritten = new WeakMap();
  var currentWritten = new WeakMap();
  // The results grid's pairs, as the layer made them: each label row's value row and each value row's label
  // row. A pairing is kept while the two rows sit together (see readGrid).
  var gridValueOf = new WeakMap();
  var gridLabelOf = new WeakMap();
  // The aria-hidden "true" the layer wrote on a label the cell under it now carries.
  var hiddenWritten = new WeakSet();
  // The aria-labelledby a drop-down had before the layer put the drop-down's own id in front of it.
  var labelledbyWas = new WeakMap();

  // `skip`, when given, is a selector: an element below the root that matches it is left out.
  function collect(node, out, isRoot, skip) {
    if (node.nodeType === 3) { out.push(node.nodeValue); return; }
    if (node.nodeType !== 1) { return; }
    if (!isRoot && (node.getAttribute('aria-hidden') === 'true' || (skip && node.matches(skip)))) { return; }
    var tag = (node.localName || '').toLowerCase();
    if (SKIP_TAGS.indexOf(' ' + tag + ' ') !== -1) { return; }
    var block = BLOCK_TAGS.indexOf(' ' + tag + ' ') !== -1;
    if (block) { out.push(' '); }
    for (var child = node.firstChild; child; child = child.nextSibling) { collect(child, out, false, skip); }
    if (block) { out.push(' '); }
  }

  // The element's visible text: <br> and block boundaries are single spaces, runs of white space are
  // one space, trimmed; anything the page marks aria-hidden is left out.
  function textOf(el) {
    return textWithout(el, null);
  }

  // The same, without the parts that match a selector (the badge inside a heading).
  function textWithout(el, skip) {
    if (!el) { return ''; }
    var out = [];
    collect(el, out, true, skip);
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

  // Gives an element a role unless the page gave it one.
  function giveRole(el, role) {
    if (!el.getAttribute('role')) { el.setAttribute('role', role); wroteRole.set(el, role); }
  }

  // Takes back the role the layer wrote, if it is still there (a role the page set since is the page's).
  function takeRole(el) {
    var role = wroteRole.get(el);
    if (role === undefined) { return; }
    wroteRole.delete(el);
    if (el.getAttribute('role') === role) { el.removeAttribute('role'); }
  }

  // Takes back a role, a label and a stamp the layer wrote (never the page's own).
  function release(el) {
    takeRole(el);
    name(el, '');
    if (el.getAttribute(MARK) === '1') { el.removeAttribute(MARK); }
  }

  // Makes an element a button a keyboard and a screen reader can use: a role (a radio's, when asked), a Tab
  // stop, a name, and Enter / Space calling the page's own click. A role or a label the page gave is kept; an
  // element the Tab key skips (no tabindex, or a negative one) gets tabindex 0 -- unless the page marks it
  // disabled, which is no Tab stop and takes no key (a tab stop the layer gave before is taken back).
  function button(el, text, role) {
    giveRole(el, role || 'button');
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
    restoreTab(el);
    release(el);
  }

  // Writes a state the layer owns (aria-checked, aria-expanded, aria-pressed, aria-current) only while it is
  // the layer's own: a state the page set is never touched. A null value takes back the layer's own state.
  function ownState(el, attr, value, store) {
    var existing = el.getAttribute(attr);
    var mine = store.get(el);
    var ours = mine !== undefined && existing === mine;
    if (existing !== null && !ours) { store.delete(el); return; }
    if (value === null) {
      if (ours) { el.removeAttribute(attr); }
      store.delete(el);
    } else {
      setAttr(el, attr, value);
      store.set(el, value);
    }
  }

  // Whether the page's own icon inside an element says open ('true'), closed ('false') or neither (null):
  // the classes of the icon when open and when closed. The page flips the class; the state follows it.
  function iconState(el, openClass, closedClass) {
    var icon = el.querySelector('i');
    if (icon && icon.classList.contains(openClass)) { return 'true'; }
    if (icon && icon.classList.contains(closedClass)) { return 'false'; }
    return null;
  }

  // A radio button: a button with the radio role that says whether it is the checked one.
  function radio(el, text, checked) {
    button(el, text, 'radio');
    ownState(el, 'aria-checked', checked ? 'true' : 'false', checkedWritten);
  }

  function unradio(el) {
    ownState(el, 'aria-checked', null, checkedWritten);
    unbutton(el);
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
        ownState(arrow, 'aria-expanded', iconState(arrow, 'fa-chevron-up', 'fa-chevron-down'), expandedWritten);
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

  // ---- Takeoff and Landing ----

  // The boxes and drop-downs of the two calculators.
  var FIELDS = 'app-keyboard-input input, mat-select';
  // Elements that are controls, never the text after a field.
  var CONTROL_TAGS = ' a app-keyboard-input button input mat-select select textarea ';

  // The .departure-label in the field's own container: the nearest ancestor that holds this field and no
  // other (so a label beside another field is never taken). Takeoff and Landing's ICAO CODE and RUNWAY have
  // one, and so has every Takeoff field.
  function ownLabel(field) {
    for (var box = field.parentElement; box && box.querySelectorAll(FIELDS).length === 1; box = box.parentElement) {
      for (var child = box.firstElementChild; child; child = child.nextElementSibling) {
        if (child.classList.contains('departure-label')) {
          var text = textOf(child);
          if (text) { return text; }
        }
      }
    }
    return '';
  }

  // The unit written after a field inside its own .section-item ("KT", "°C", "HPA", "T" on Landing): the
  // text that follows the field, or the box that holds it, up to the item's edge. A control after the field
  // (the FMS button) is not text.
  function unitAfter(field) {
    var item = field.closest('.section-item');
    if (!item) { return ''; }
    for (var node = field; node && node !== item; node = node.parentElement) {
      var parts = [];
      for (var next = node.nextSibling; next; next = next.nextSibling) {
        if (next.nodeType === 3) { parts.push(next.nodeValue); }
        else if (next.nodeType === 1 && CONTROL_TAGS.indexOf(' ' + next.localName + ' ') === -1) { parts.push(textOf(next)); }
      }
      var text = parts.join(' ').replace(/\s+/g, ' ').trim();
      if (text) { return text; }
    }
    return '';
  }

  // What the page says a field is: its own label; a field without one takes the header of its table column,
  // else its unit. A placeholder is never the name, and a field with none of these stays unnamed.
  function fieldText(field) {
    var text = ownLabel(field);
    if (!text) {
      var table = field.closest('table');
      text = table ? columnHeader(field, table) : '';
    }
    return text || unitAfter(field);
  }

  // Takeoff and Landing: the text boxes. The page hides each label in a div beside the box.
  RULES.push(function (root) {
    var boxes = root.querySelectorAll('app-departure app-keyboard-input input, app-arrival app-keyboard-input input');
    for (var i = 0; i < boxes.length; i++) { name(boxes[i], fieldText(boxes[i])); }
  });

  // A drop-down is a mat-select with the role combobox; the layer names it and changes nothing else. Angular
  // Material names a mat-select by its value (its aria-labelledby points at the value text), and a
  // labelledby wins over an aria-label, so a label added alone is never read (and a drop-down with no value
  // has no name at all). The drop-down's own id goes in front of what the page lists: it is named by its
  // aria-label and the page's value follows, as Material does with a mat-label. This holds only while the
  // aria-label is the layer's own; the page's list is put back when it is not.
  function nameSelect(select, text) {
    name(select, text);
    var label = written.get(select);
    var ours = label !== undefined && select.getAttribute('aria-label') === label;
    var current = select.getAttribute('aria-labelledby');
    var was = labelledbyWas.get(select);
    var extended = was !== undefined && current === select.id + ' ' + was;
    if (!ours || !select.id) {
      if (extended) { select.setAttribute('aria-labelledby', was); }
      labelledbyWas.delete(select);
      return;
    }
    if (extended) { return; }
    labelledbyWas.delete(select);
    if (!current || !current.trim() || current.split(/\s+/).indexOf(select.id) !== -1) { return; }
    labelledbyWas.set(select, current);
    select.setAttribute('aria-labelledby', select.id + ' ' + current);
  }

  // Takeoff and Landing: the drop-downs, a rule of their own so the boxes never wait for them.
  RULES.push(function (root) {
    var selects = root.querySelectorAll('app-departure mat-select, app-arrival mat-select');
    for (var i = 0; i < selects.length; i++) { nameSelect(selects[i], fieldText(selects[i])); }
  });

  // The cells of a results row: its div children, one per column.
  function gridCells(row) {
    var cells = [];
    for (var cell = row.firstElementChild; cell; cell = cell.nextElementSibling) {
      if (cell.localName === 'div') { cells.push(cell); }
    }
    return cells;
  }

  // What a results cell says: its text, or "not computed" for the yellow boxes the MCDU shows where a value
  // has not been worked out.
  function resultText(cell) {
    var text = textOf(cell);
    if (text) { return text; }
    return cell.querySelector('.small-yellow-box') ? 'not computed' : '';
  }

  // Makes a results cell one named image: "V1: not computed". A role="img" is the one thing a screen reader
  // reads as a single named object in browse mode (a generic div's aria-label is not read, and the glyphs
  // inside, the yellow boxes and the brackets, are read one by one); an image's children are presentational.
  // Returns true when the cell now carries the layer's own label (so the label above can be hidden); a cell
  // with a role of the page's is the page's.
  function resultCell(cell, text) {
    var role = cell.getAttribute('role');
    if (role && wroteRole.get(cell) !== role) { name(cell, ''); return false; }
    giveRole(cell, 'img');
    name(cell, text);
    return written.get(cell) !== undefined && cell.getAttribute('aria-label') === written.get(cell);
  }

  // Hides a label from a screen reader because the value under it says it. A hidden state the page set is
  // the page's (never overridden); the layer takes back only its own.
  function hide(el) {
    if (!el.hasAttribute('aria-hidden')) { el.setAttribute('aria-hidden', 'true'); hiddenWritten.add(el); }
  }

  function unhide(el) {
    if (!hiddenWritten.has(el)) { return; }
    hiddenWritten.delete(el);
    if (el.getAttribute('aria-hidden') === 'true') { el.removeAttribute('aria-hidden'); }
  }

  // One label row and the value row under it. A value is paired with the label in the same column; rows that
  // do not line up (a different number of cells) are not paired by guess.
  function readPair(labelRow, valueRow) {
    var labels = gridCells(labelRow);
    var values = gridCells(valueRow);
    var lined = labels.length === values.length;
    for (var i = 0; i < Math.max(labels.length, values.length); i++) {
      var carried = false;
      if (values[i]) {
        var label = lined ? textOf(labels[i]) : '';
        var value = label ? resultText(values[i]) : '';
        if (value) { carried = resultCell(values[i], label + ': ' + value); } else { release(values[i]); }
      }
      if (labels[i]) { if (carried) { hide(labels[i]); } else { unhide(labels[i]); } }
    }
  }

  // Lets go of a row that is not part of a pair: what the layer wrote on its cells is taken back and a label
  // the layer hid is read again (never anything the page set).
  function letGo(row) {
    var cells = gridCells(row);
    for (var i = 0; i < cells.length; i++) { release(cells[i]); unhide(cells[i]); }
  }

  // Pairs the rows of one results grid. The first time the layer sees two rows side by side that have no
  // pairing yet, the first is a label row and the second its value row, and the pairing is kept. After that
  // a row is read only with the row it was paired with, and only while the two still sit together: a row the
  // page puts between them, or takes out from under the label, withdraws that pairing instead of shifting
  // every pair below it into a garbled name. A pairing whose other row has left the grid is forgotten, so
  // rows the page replaces pair up again. A row without a pair is let go of.
  function readGrid(screen) {
    var rows = [];
    for (var child = screen.firstElementChild; child; child = child.nextElementSibling) {
      if (child.classList.contains('row')) { rows.push(child); } else { letGo(child); }
    }
    for (var k = 0; k < rows.length; k++) {
      var value = gridValueOf.get(rows[k]);
      if (value !== undefined && rows.indexOf(value) === -1) { gridValueOf.delete(rows[k]); gridLabelOf.delete(value); }
      var label = gridLabelOf.get(rows[k]);
      if (label !== undefined && rows.indexOf(label) === -1) { gridLabelOf.delete(rows[k]); gridValueOf.delete(label); }
    }
    for (var r = 0; r < rows.length; ) {
      var next = rows[r + 1];
      var known = gridValueOf.has(rows[r]) || gridLabelOf.has(rows[r]);
      if (!known && next && !gridValueOf.has(next) && !gridLabelOf.has(next)) {
        gridValueOf.set(rows[r], next);
        gridLabelOf.set(next, rows[r]);
      }
      if (next && gridValueOf.get(rows[r]) === next) {
        readPair(rows[r], next);
        r += 2;
      } else {
        letGo(rows[r]);
        r += 1;
      }
    }
  }

  // Takeoff: the results are the MCDU's take-off page, a title and then rows that alternate: a row of small
  // labels and, under it, a row of values. After Calculate the page changes the values in place; each pass
  // reads them again (name() keeps the label in step with the text).
  RULES.push(function (root) {
    var screens = root.querySelectorAll('.mcdu-screen');
    for (var s = 0; s < screens.length; s++) { readGrid(screens[s]); }
  });

  // Landing: the runway condition is the group of exactly six items whose texts start 6 to 1 ("6 DRY" ...
  // "1 POOR"). The page uses div.section-item for much more (ICAO CODE, the units, the METAR), so the group
  // is found by what it holds. The page marks the chosen one with section-selected; it is the checked radio.
  var CONDITION_CODES = '654321';

  function conditionCode(item) {
    var m = /^\d/.exec(textOf(item));
    return m ? m[0] : '';
  }

  RULES.push(function (root) {
    var groups = root.querySelectorAll('div.section-group');
    for (var i = 0; i < groups.length; i++) {
      var items = [];
      for (var child = groups[i].firstElementChild; child; child = child.nextElementSibling) {
        if (child.classList.contains('section-item')) { items.push(child); }
      }
      if (items.length === CONDITION_CODES.length && items.map(conditionCode).join('') === CONDITION_CODES) {
        giveRole(groups[i], 'radiogroup');
        name(groups[i], 'Runway condition');
        // Every radio stays a Tab stop: the page gives each its own tabindex, and the layer writes none.
        for (var j = 0; j < items.length; j++) {
          radio(items[j], textOf(items[j]), items[j].classList.contains('section-selected'));
        }
      } else {
        release(groups[i]);
        for (var k = 0; k < items.length; k++) { unradio(items[k]); }
      }
    }
  });

  // Landing: the five switches (LOW A/BRAKE, IDLE/NO REV ...) are divs with a Tab stop and a click.
  RULES.push(function (root) {
    var options = root.querySelectorAll('div.one-click-change');
    for (var i = 0; i < options.length; i++) { button(options[i], textOf(options[i])); }
  });

  // Landing: the nine system procedures are bare divs with no Tab stop (the layer gives them one).
  RULES.push(function (root) {
    var items = root.querySelectorAll('div.arrival-procedure-item');
    for (var i = 0; i < items.length; i++) { button(items[i], textOf(items[i])); }
  });

  // ---- Settings ----

  // The setting an article is about: its heading without the "Default" / "Livery" badge inside it.
  function settingName(article) {
    var heading = article ? article.querySelector('.settings-name') : null;
    return textWithout(heading, '.settings-source');
  }

  // Settings: a switch is a checkbox in a label with no text; it takes the name of its article's heading.
  RULES.push(function (root) {
    var boxes = root.querySelectorAll('app-settings2 article label.toggle > input[type=checkbox]');
    for (var i = 0; i < boxes.length; i++) { name(boxes[i], settingName(boxes[i].closest('article'))); }
  });

  // Settings: the three dots hold no text; they say which setting's options they open.
  RULES.push(function (root) {
    var dots = root.querySelectorAll('app-settings2 article button.overflow-button');
    for (var i = 0; i < dots.length; i++) {
      var setting = settingName(dots[i].closest('article'));
      name(dots[i], setting ? 'More options for ' + setting : '');
    }
  });

  // Settings: an option is a real button named by its own text; it says whether it is the chosen one, as the
  // page does with option--active.
  RULES.push(function (root) {
    var options = root.querySelectorAll('app-settings2 button.option');
    for (var i = 0; i < options.length; i++) {
      ownState(options[i], 'aria-pressed', options[i].classList.contains('option--active') ? 'true' : 'false', pressedWritten);
    }
  });

  // Settings: the categories are list items the page clicks; each is a button, and the open one is the
  // current page (the page marks it category--active).
  RULES.push(function (root) {
    var categories = root.querySelectorAll('app-settings2 li.category');
    for (var i = 0; i < categories.length; i++) {
      button(categories[i], textOf(categories[i]));
      ownState(categories[i], 'aria-current', categories[i].classList.contains('category--active') ? 'page' : null, currentWritten);
    }
  });

  // Settings: the "Default" / "Livery" badge has a Tab stop of the page's own (it shows a tooltip) and a text,
  // but no role, so no screen reader reads it as anything. It is a note, named by its text.
  RULES.push(function (root) {
    var badges = root.querySelectorAll('app-settings2 span.settings-source');
    for (var i = 0; i < badges.length; i++) {
      var text = textOf(badges[i]);
      if (text) {
        giveRole(badges[i], 'note');
        name(badges[i], text);
      } else {
        release(badges[i]);
      }
    }
  });

  // ---- Failures ----

  // Failures: a chapter's title (ATA 21 ...) is a bare div the page opens and closes; its chevron points down
  // while the chapter is open and right while it is closed.
  RULES.push(function (root) {
    var titles = root.querySelectorAll('app-manual-failures div.manual-failure-ata-title');
    for (var i = 0; i < titles.length; i++) {
      button(titles[i], textOf(titles[i]));
      ownState(titles[i], 'aria-expanded', iconState(titles[i], 'fa-chevron-circle-down', 'fa-chevron-circle-right'), expandedWritten);
    }
  });

  // The title of the group an item belongs to ("CPC", "Overheat"), or '' when its group has none.
  function groupTitleOf(item) {
    var group = item.closest('.manual-failure-group');
    for (var child = group ? group.firstElementChild : null; child; child = child.nextElementSibling) {
      if (child.classList.contains('manual-failure-group-title')) { return textOf(child); }
    }
    return '';
  }

  // Failures: an item says only its own part ("1", "Pack 1"); its name starts with its group's title ("CPC 1",
  // "Overheat Pack 1"). An item in a group without a title is named by its own text, which the button already is.
  RULES.push(function (root) {
    var items = root.querySelectorAll('app-manual-failures button.manual-failure-item');
    for (var i = 0; i < items.length; i++) {
      var group = groupTitleOf(items[i]);
      var text = textOf(items[i]);
      name(items[i], group && text ? group + ' ' + text : '');
    }
  });

  // Failures: the search box has no label; its placeholder is the only word the page gives it (Mass and
  // Balance's boxes use theirs the same way).
  RULES.push(function (root) {
    var boxes = root.querySelectorAll('app-manual-failures app-keyboard-input input[placeholder]');
    for (var i = 0; i < boxes.length; i++) {
      name(boxes[i], boxes[i].getAttribute('placeholder').replace(/\s+/g, ' ').trim());
    }
  });

  // ---- Documents ----

  // Documents: a folder's row and a file's row are bare divs the page clicks, and each is a button. The
  // capture has no open / closed class on a folder (both icons are always there), so no aria-expanded is
  // written: that would be a guess. A row that holds controls of its own (a button, a box or a link: "My
  // Documents" has "Open documents folder" and "Upload document") is not a button, because a control inside a
  // button cannot be reached: its label is the button instead, and Enter on the label is a click on the text,
  // which reaches the row's own handler. A row that gains or loses controls hands the button over; each time
  // only the stamp the layer wrote on the element that no longer applies is taken back.
  function documentRow(row, nameFromLabel) {
    var label = row.querySelector('span.label');
    if (row.querySelector('button, input, a[href]')) {
      unbutton(row);
      if (label) { button(label, textOf(label)); }
    } else {
      if (label) { unbutton(label); }
      button(row, textOf(nameFromLabel && label ? label : row));
    }
  }

  RULES.push(function (root) {
    var folders = root.querySelectorAll('app-documents div.folder-name');
    for (var i = 0; i < folders.length; i++) { documentRow(folders[i], true); }
  });

  RULES.push(function (root) {
    var files = root.querySelectorAll('app-documents div.file');
    for (var i = 0; i < files.length; i++) { documentRow(files[i], false); }
  });

  // Documents: the button on the edge of the list has no text and no title (ruling R21).
  RULES.push(function (root) {
    var toggles = root.querySelectorAll('app-documents button.sidebar-toggle-btn');
    for (var i = 0; i < toggles.length; i++) { name(toggles[i], 'Toggle document list'); }
  });

  // ---- Charts and quick settings ----

  // Charts: the sidebar's items are divs the page clicks, named by their text.
  RULES.push(function (root) {
    var items = root.querySelectorAll('app-navigraph-sidebar div.navigraph-sidebar-item');
    for (var i = 0; i < items.length; i++) { button(items[i], textOf(items[i])); }
  });

  // Charts: the zoom controls are icon fonts. The ids (mapZoomInBtn, mapZoomOutBtn) sit on the icons and their
  // parent holds both, so each icon is the button (a capture cannot show where the page's listener is). The
  // names are owner decision 5's.
  var ZOOM = [['fa-plus-circle', 'Zoom in'], ['fa-minus-circle', 'Zoom out']];
  RULES.push(function (root) {
    for (var k = 0; k < ZOOM.length; k++) {
      var icons = root.querySelectorAll('div.chart-viewer-controls i.' + ZOOM[k][0]);
      for (var i = 0; i < icons.length; i++) { button(icons[i], ZOOM[k][1]); }
    }
  });

  // Charts: the map controls are icon fonts too (ruling R15); the "World Map" item has its own text. The page
  // marks the chosen item map-control-item-selected. The settings cog opens the settings and is not a choice
  // (ruling R26): it is a button with no pressed state; the map modes and the two themes say which is chosen.
  var MAP_ICONS = [['fa-cog', 'Map settings', false], ['fa-moon', 'Dark map', true], ['fa-sun', 'Light map', true]];

  // A map control's name, and whether it is a choice (one of several, the chosen one marked).
  function mapControl(item) {
    var text = textOf(item);
    for (var k = 0; k < MAP_ICONS.length; k++) {
      if (item.querySelector('i.' + MAP_ICONS[k][0])) { return { name: text || MAP_ICONS[k][1], choice: MAP_ICONS[k][2] }; }
    }
    return { name: text, choice: text !== '' };
  }

  RULES.push(function (root) {
    var items = root.querySelectorAll('app-charts-map div.map-control-item');
    for (var i = 0; i < items.length; i++) {
      var control = mapControl(items[i]);
      button(items[i], control.name);
      var chosen = items[i].classList.contains('map-control-item-selected') ? 'true' : 'false';
      ownState(items[i], 'aria-pressed', control.choice ? chosen : null, pressedWritten);
    }
  });

  // Charts: the list toggle on the edge of the sidebar is an empty div the page clicks (ruling R26; Documents'
  // is "Toggle document list").
  RULES.push(function (root) {
    var toggles = root.querySelectorAll('app-charts div.sidebar-toggle-btn');
    for (var i = 0; i < toggles.length; i++) { button(toggles[i], 'Toggle chart list'); }
  });

  // Charts: the moving map is Leaflet's own focusable container (tabindex 0, no text). It only gets a name
  // (ruling R24): Leaflet owns its keyboard, so no role, no Tab stop of ours and no key handler.
  RULES.push(function (root) {
    var maps = root.querySelectorAll('app-charts-map div.leaflet-container');
    for (var i = 0; i < maps.length; i++) { name(maps[i], 'Moving map'); }
  });

  // Quick settings: the airplane button holds only its picture, found by its class or its file name (ruling R10).
  RULES.push(function (root) {
    var pictures = root.querySelectorAll('img.flight-mode-btn-icon, img[src*=airplane]');
    for (var i = 0; i < pictures.length; i++) {
      var holder = pictures[i].closest('button.quick-settings-btn');
      if (holder) { name(holder, 'Airplane mode'); }
    }
  });

  // Quick settings: the brightness slider is a mat-slider with a value and no name (ruling R10).
  RULES.push(function (root) {
    var sliders = root.querySelectorAll('mat-slider.brightness-popup-slider');
    for (var i = 0; i < sliders.length; i++) { name(sliders[i], 'Brightness'); }
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
