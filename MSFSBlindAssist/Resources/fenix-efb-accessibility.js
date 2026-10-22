// Fenix EFB accessibility layer, injected into the Fenix EFB web page by FenixEFBForm.
// It only adds roles, names, states, Tab stops and key handlers that call the page's own click, and takes
// back what it wrote itself when the page moves on -- never a request, a removed or moved element, written
// text or a written value. It never overrides a page's own aria-hidden, aria-label or role, never gives a
// disabled control a Tab stop, and takes back only its own Tab stops. (It does give tabindex 0 to an enabled
// control the Tab key skips, one the page set to -1 too, and puts the page's own value back afterwards.)
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
  // The aria-expanded the layer wrote on the app switcher's chevron (the way `written` keeps a label).
  var expandedWritten = new WeakMap();
  // The aria-checked the layer wrote on a radio, the same way.
  var checkedWritten = new WeakMap();
  // The aria-hidden "true" the layer wrote on a label the cell under it now carries.
  var hiddenWritten = new WeakSet();
  // The aria-labelledby a drop-down had before the layer put the drop-down's own id in front of it.
  var labelledbyWas = new WeakMap();

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

  // Writes a state the layer owns (aria-checked) only while it is the layer's own: a state the page set is
  // never touched. A null value takes back the layer's own state (the way the switcher's chevron does).
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

  // Takeoff: the results are the MCDU's take-off page, a title and then rows that alternate: a row of small
  // labels and, under it, a row of values. After Calculate the page changes the values in place; each pass
  // reads them again (name() keeps the label in step with the text).
  RULES.push(function (root) {
    var screens = root.querySelectorAll('.mcdu-screen');
    for (var s = 0; s < screens.length; s++) {
      var rows = [];
      for (var row = screens[s].firstElementChild; row; row = row.nextElementSibling) {
        if (row.classList.contains('row')) { rows.push(row); }
      }
      for (var r = 0; r + 1 < rows.length; r += 2) { readPair(rows[r], rows[r + 1]); }
    }
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
