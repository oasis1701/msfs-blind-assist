// Fenix EFB accessibility layer, injected into the Fenix EFB web page by FenixEFBForm.
// It only adds roles, names, states, Tab stops and key handlers that call the page's own click, and takes
// back what it wrote itself when the page moves on -- never a request, a removed or moved element, written
// text or a written value. It never overrides a page's own aria-hidden, aria-label or role, never gives a
// disabled control a Tab stop, and takes back only its own Tab stops. (It does give tabindex 0 to an enabled
// control the Tab key skips, one the page set to -1 too, and puts the page's own value back afterwards.)
// More attributes it writes: a drop-down's aria-labelledby gets the drop-down's own id in front of the page's
// list (the page's list goes back when the layer's label goes); aria-hidden "true" goes on what a screen reader
// does not need: decorative pictures and every picture with no alt, icon-font glyphs inside a control, the W&B
// chart's picture and labels but its limits, the seat map; and the Takeoff results grid gets an aria-owns that
// reads each label right before its value and every other child in its place (an empty result is a picture
// described as a "value").
// Four exceptions to "attributes only", each a deliberate one: ids for aria-owns (the results grid's and an open
// Settings "More options" menu's), written only on what it lists and only as "msfsba-<n>" (an id the page set is
// used, never replaced); the runway condition's roving Tab stop; one focus helper,
// moveFocus, the only place the focus moves: the runway condition's arrow keys press the next code and move to
// it, and a "More options" menu takes the focus to its first item when it opens and back to its button when
// it closes with the focus; and one change listener on the document that only schedules a pass (a Settings
// switch's state is a checkbox property the observer cannot see).
// It runs in the top document only. Plain ES2017, no modules.
(function () {
  if (window.__msfsbaFenixEfb) { return; }
  // WebView2 runs a document-created script in every iframe too; the layer belongs to the top document
  // only (an iframe gets no export, no observer, no Alt+Home). A window that cannot be compared is a frame.
  try { if (window.top !== window) { return; } } catch (err) { return; }

  // Marks every element the layer has handled, so a later pass knows it has been here.
  var MARK = 'data-msfsba';

  // The titles of the nine home tiles, in home order. The switcher's icons are pictures with no text,
  // so they take these names (the owner's wording), and only when the switcher holds nine.
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
  // The aria-expanded the layer wrote on the app switcher's chevron, a failure chapter and a Documents folder
  // (the way `written` keeps a label).
  var expandedWritten = new WeakMap();
  // The aria-checked the layer wrote on a radio, a switch or a check box, the same way.
  var checkedWritten = new WeakMap();
  // The runway condition's roving Tab stop: each code's tabindex before the layer's first write (the
  // attribute's text, or null for none) and the one the layer wrote last, so the page's own goes back when the group
  // stops being the runway condition, unless the page changed it since.
  var rovingTab = new WeakMap();
  // The codes of each container the layer treats as the runway condition, in page order: the arrows move along them.
  var rovingCodes = new WeakMap();
  // The aria-disabled it wrote on a Settings switch whose checkbox the page disabled, the same way.
  var disabledWritten = new WeakMap();
  // The aria-pressed it wrote on a Settings option and a map control, and the aria-current on a category and
  // on the selected Documents file.
  var pressedWritten = new WeakMap();
  var currentWritten = new WeakMap();
  // The results grid's pairs, as the layer made them: each label row's value row and each value row's label
  // row. A pairing is kept while the two rows sit together (see readGrid).
  var gridValueOf = new WeakMap();
  var gridLabelOf = new WeakMap();
  // The aria-owns the layer wrote on a results grid (the way `written` keeps a label), and the id it wrote on each
  // element that list names. An id that is there and is not the one written is the page's.
  var ownsWritten = new WeakMap();
  // The aria-roledescription the layer wrote on a "not computed" cell, the way `written` keeps a label.
  var roleDescWritten = new WeakMap();
  var idWritten = new WeakMap();
  var idCount = 0;
  // What each results grid's list named on the last pass, so what the layer wrote on an element it no
  // longer lists is taken back.
  var gridListed = new WeakMap();
  // The aria-hidden "true" the layer wrote on a picture, a glyph or a chart label a screen reader does not need.
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
  // property, or Material's mat-tab-disabled class. A label (a Settings switch) is as unavailable as the control it
  // labels. An aria-disabled the layer wrote itself (on such a switch) is not the page's word, and is not read here.
  function isDisabled(el) {
    var aria = el.getAttribute('aria-disabled');
    return (aria === 'true' && disabledWritten.get(el) !== aria) || el.hasAttribute('disabled') || el.disabled === true ||
      el.classList.contains('mat-tab-disabled') ||
      (el.localName === 'label' && !!el.control && el.control !== el && isDisabled(el.control));
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

  // The arrow keys, and which way each moves through the runway condition's codes.
  var ARROW_STEPS = { ArrowDown: 1, ArrowRight: 1, ArrowUp: -1, ArrowLeft: -1, Down: 1, Right: 1, Up: -1, Left: -1 };

  // The runway condition code an arrow on `el` moves to: the next one (step 1) or the previous one (step -1) in the
  // page's order, round the ends, passing over a code the page disables. Null when `el` is not one of its codes.
  function nextCode(el, step) {
    var codes = el.parentElement ? rovingCodes.get(el.parentElement) : undefined;
    var at = codes ? codes.indexOf(el) : -1;
    if (at === -1) { return null; }
    for (var k = 1; k < codes.length; k++) {
      var code = codes[((at + step * k) % codes.length + codes.length) % codes.length];
      if (!isDisabled(code)) { return code; }
    }
    return null;
  }

  // The one place the layer moves the focus: the runway condition's arrows take it to the code they
  // pressed, and a Settings "More options" menu takes it to its first item when it opens and back to its button when it
  // closes and the focus is lost. Nothing else moves it.
  function moveFocus(el) {
    el.focus();
  }

  function onButtonKey(e) {
    try {
      var activate = e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar';
      var step = Object.prototype.hasOwnProperty.call(ARROW_STEPS, e.key) ? ARROW_STEPS[e.key] : 0;
      if (!activate && !step) { return; }
      if (e.altKey || e.ctrlKey || e.metaKey) { return; }
      var el = e.currentTarget;
      // A withdrawn element, or one the page has disabled, is not pressed and its keys stay the page's.
      if (!pressable.has(el) || isDisabled(el)) { return; }
      var target = e.target;
      // Typing in a text box is never caught.
      var tag = target && target.localName ? target.localName.toLowerCase() : '';
      if (tag === 'input' || tag === 'textarea' || tag === 'select' || (target && target.isContentEditable)) { return; }
      // Only the button itself is pressed: a control inside it (a link, a drop-down) keeps its own keys.
      if (target !== el) { return; }
      // Enter and Space press the control itself. An arrow on a runway condition code presses the code it moves to;
      // anywhere else an arrow is the page's.
      var chosen = activate ? el : nextCode(el, step);
      if (!chosen) { return; }
      e.preventDefault();
      // A held key presses once, as the page's own buttons do.
      if (e.repeat) { return; }
      chosen.click();
      // The arrow takes the focus to the code it pressed, as in any radio group.
      if (chosen !== el) { moveFocus(chosen); }
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

  // Takes back the role the layer gave an element that now needs another one, so giveRole can write the new one (a
  // role the page set is never touched).
  function changeRole(el, role) {
    var mine = wroteRole.get(el);
    if (mine !== undefined && mine !== role) { takeRole(el); }
  }

  // Takes back a role, a label and a stamp the layer wrote (never the page's own).
  function release(el) {
    takeRole(el);
    name(el, '');
    if (el.getAttribute(MARK) === '1') { el.removeAttribute(MARK); }
  }

  // Everything button() gives an element but the Tab stop: a role, a name, the stamp and the key handler.
  function pressableAs(el, text, role) {
    giveRole(el, role || 'button');
    name(el, text);
    mark(el);
    pressable.add(el);
    if (!handled.has(el)) {
      handled.add(el);
      el.addEventListener('keydown', onButtonKey);
    }
  }

  // Makes an element a button a keyboard and a screen reader can use: a role (another, when asked), a Tab
  // stop, a name, and Enter / Space calling the page's own click. A role or a label the page gave is kept; an
  // element the Tab key skips (no tabindex, or a negative one) gets tabindex 0 -- unless the page marks it
  // disabled, which is no Tab stop and takes no key (a tab stop the layer gave before is taken back).
  function button(el, text, role) {
    pressableAs(el, text, role);
    if (isDisabled(el)) {
      restoreTab(el);
    } else if (el.tabIndex < 0) {
      tabOriginal.set(el, el.getAttribute('tabindex'));
      el.setAttribute('tabindex', '0');
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

  // Follows what a rule finds from pass to pass: `keep` is applied to each element found on this pass, and `drop` to each
  // found on an earlier pass and not on this one (so what the layer wrote on it is taken back). Returns this pass's list,
  // to hand in as `before` next time.
  function followFound(found, before, keep, drop) {
    var now = [];
    for (var i = 0; i < found.length; i++) { keep(found[i]); now.push(found[i]); }
    for (var j = 0; j < before.length; j++) {
      if (now.indexOf(before[j]) === -1) { drop(before[j]); }
    }
    return now;
  }

  // Whether the page's own icon inside an element says open ('true'), closed ('false') or neither (null):
  // the classes of the icon when open and when closed. The page flips the class; the state follows it.
  function iconState(el, openClass, closedClass) {
    var icon = el.querySelector('i');
    if (icon && icon.classList.contains(openClass)) { return 'true'; }
    if (icon && icon.classList.contains(closedClass)) { return 'false'; }
    return null;
  }

  // A runway condition code: a radio that says whether it is the checked one, with the group's roving Tab stop:
  // -1 on every code but the group's one stop, written over the page's own (30 to 35), which unradio puts
  // back. The stop keeps its place in the page's Tab sequence: the Landing page orders its controls with
  // positive tabindex values, which a browser visits before any 0, so the stop takes the code's own recorded tabindex
  // when that was 0 or more, and 0 when the code had none (or a negative one).
  function rovingRadio(el, text, checked, stop) {
    pressableAs(el, text, 'radio');
    ownState(el, 'aria-checked', checked ? 'true' : 'false', checkedWritten);
    var record = rovingTab.get(el);
    if (!record) {
      record = { original: el.getAttribute('tabindex'), wrote: null };
      rovingTab.set(el, record);
    }
    var own = record.original === null ? NaN : parseInt(record.original, 10);
    record.wrote = stop ? (own >= 0 ? String(own) : '0') : '-1';
    setAttr(el, 'tabindex', record.wrote);
  }

  // Takes back what rovingRadio gave a code: the page's own tabindex goes back (unless the page changed it since), and
  // the checked state, role, label and stamp the layer wrote go.
  function unradio(el) {
    var record = rovingTab.get(el);
    if (record) {
      rovingTab.delete(el);
      if (el.getAttribute('tabindex') === record.wrote) {
        if (record.original === null) { el.removeAttribute('tabindex'); } else { el.setAttribute('tabindex', record.original); }
      }
    }
    ownState(el, 'aria-checked', null, checkedWritten);
    unbutton(el);
  }

  // Each rule is a function (root). Later tasks add their rules here; a rule that fails never stops the
  // others (pass() runs each in its own try/catch).
  var RULES = [];

  // True when a <br> in the element is its own: not inside a button within it.
  function holdsLineBreak(el) {
    var breaks = el.querySelectorAll('br');
    for (var i = 0; i < breaks.length; i++) {
      if (breaks[i].parentElement.closest('button, [role=button]') === el) { return true; }
    }
    return false;
  }

  // Every screen: a button whose own content holds a <br> ("SYNC LIVE WX" after its glyph, "Resend<br>Loadsheet") is
  // shown by NVDA over two lines, the first empty when a hidden glyph comes before the break. It is named by its own text,
  // the <br> a space, so it is one item. The label is the layer's own: it follows the text and goes when the
  // <br> or the text goes; a label the page gives is the page's. This rule runs first, so a rule below that names the
  // same control (the home tiles, Mass and Balance's buttons) has the last word.
  var lineButtons = [];
  function nameByText(el) { name(el, textOf(el)); }
  // The label and the stamp go; a control another rule made a button keeps its stamp (that rule writes it again).
  function unnameText(el) {
    name(el, '');
    if (!pressable.has(el) && el.getAttribute(MARK) === '1') { el.removeAttribute(MARK); }
  }
  RULES.push(function (root) {
    var candidates = root.querySelectorAll('button, [role=button]');
    var found = [];
    for (var i = 0; i < candidates.length; i++) {
      if (holdsLineBreak(candidates[i]) && textOf(candidates[i])) { found.push(candidates[i]); }
    }
    lineButtons = followFound(found, lineButtons, nameByText, unnameText);
  });

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

  // Ground Services: the three pushback arrows are bare divs holding an arrow picture (the names are the owner's).
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

  // Panel States: the pop-up has four buttons and no title (the owner chose the title the layer gives it).
  // Other dialogs are not this rule's.
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

  // A box's placeholder when it says what the box is: one with a letter in it ("LANDING WT"). A placeholder of
  // only dashes, digits or punctuation ("----", "---/--") shows the format, and is never a name.
  function wordyPlaceholder(field) {
    if (field.localName !== 'input') { return ''; }
    var text = (field.getAttribute('placeholder') || '').replace(/\s+/g, ' ').trim();
    return /[A-Za-z]/.test(text) ? text : '';
  }

  // What the page says a field is: its own label; a field without one takes the header of its table column,
  // else a placeholder with letters in it and its unit ("LANDING WT, T"), else its unit. A field with none of
  // these stays unnamed.
  function fieldText(field) {
    var text = ownLabel(field);
    if (!text) {
      var table = field.closest('table');
      text = table ? columnHeader(field, table) : '';
    }
    if (text) { return text; }
    return [wordyPlaceholder(field), unitAfter(field)].filter(Boolean).join(', ');
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

  // A value cell the MCDU has not worked out shows only yellow boxes, which a screen reader cannot say: it is one
  // picture named "not computed", the only picture in the grid, described as a value so NVDA says "value",
  // not "graphic". A cell that holds a value, or has a role of the page's, is read as the page has it, and
  // what the layer wrote on it is taken back.
  function notComputed(cell) {
    var role = cell.getAttribute('role');
    var empty = textOf(cell) === '' && cell.querySelector('.small-yellow-box') !== null;
    if (!empty || (role && wroteRole.get(cell) !== role)) { unpicture(cell); return; }
    giveRole(cell, 'img');
    name(cell, 'not computed');
    ownState(cell, 'aria-roledescription', 'value', roleDescWritten);
  }

  // Takes back what notComputed wrote on a cell: its role description, role, label and stamp (never the page's own).
  function unpicture(cell) {
    ownState(cell, 'aria-roledescription', null, roleDescWritten);
    release(cell);
  }

  // The id an element in the grid's list goes by: the page's own when it has one, else one the layer
  // writes, "msfsba-" and a number no element of the page uses. Null for an empty id of the page's (not usable, and
  // the page's to keep).
  function ownId(el) {
    var id = el.getAttribute('id');
    if (id !== null) {
      if (idWritten.get(el) !== id) { idWritten.delete(el); }
      return id || null;
    }
    do { idCount++; } while (document.getElementById('msfsba-' + idCount));
    el.setAttribute('id', 'msfsba-' + idCount);
    idWritten.set(el, el.getAttribute('id'));
    return el.getAttribute('id');
  }

  // Takes back the id the layer wrote on an element, if it is still there (an id the page set is the page's).
  function dropId(el) {
    var mine = idWritten.get(el);
    if (mine === undefined) { return; }
    idWritten.delete(el);
    if (el.getAttribute('id') === mine) { el.removeAttribute('id'); }
  }

  // Takes back what the layer wrote on an element of a results grid it no longer lists: its role description, role,
  // label, stamp and id (never anything the page set).
  function letGo(el) {
    unpicture(el);
    dropId(el);
  }

  // Reads one results grid. A screen reader reads it in page order: every
  // label of a row, then every value under them. Its aria-owns lists every child of the grid in page order, with each
  // label / value row pair expanded into its cells, the label cell and then the value cell under it, column by column:
  // each label is read right before its value, and the title, the output line and any row without a pair keep their
  // place. Nothing is hidden. The ids it needs are written only on what it lists (see ownId). Rows pair like
  // this: the first time the layer sees two rows side by side that have no pairing yet, the first is a label row and
  // the second its value row, and the pairing is kept; after that a row is expanded only with the row it was paired
  // with, and only while the two still sit together and have as many cells. A row the page puts between them, or
  // takes out from under the label, withdraws that pairing instead of shifting every pair below it; a pairing whose
  // other row has left the grid is forgotten, so rows the page replaces pair up again. What the layer wrote on an
  // element it listed before and no longer lists (its pair broke, or the page moved it out of the grid) is taken back.
  // A grid with no pair has nothing to reorder, and one with an aria-owns of the page's own is the page's: the layer
  // lists nothing there.
  function readGrid(screen) {
    var children = [];
    var rows = [];
    for (var child = screen.firstElementChild; child; child = child.nextElementSibling) {
      children.push(child);
      if (child.classList.contains('row')) { rows.push(child); }
    }
    for (var k = 0; k < rows.length; k++) {
      var value = gridValueOf.get(rows[k]);
      if (value !== undefined && rows.indexOf(value) === -1) { gridValueOf.delete(rows[k]); gridLabelOf.delete(value); }
      var label = gridLabelOf.get(rows[k]);
      if (label !== undefined && rows.indexOf(label) === -1) { gridLabelOf.delete(rows[k]); gridValueOf.delete(label); }
    }
    // This pass's pairs whose rows line up (as many cells each): a value is paired with the label in its column, and
    // rows that do not line up are not paired by guess.
    var labelRows = [];
    var valueRows = [];
    for (var r = 0; r < rows.length; ) {
      var next = rows[r + 1];
      var known = gridValueOf.has(rows[r]) || gridLabelOf.has(rows[r]);
      if (!known && next && !gridValueOf.has(next) && !gridLabelOf.has(next)) {
        gridValueOf.set(rows[r], next);
        gridLabelOf.set(next, rows[r]);
      }
      if (next && gridValueOf.get(rows[r]) === next) {
        if (gridCells(rows[r]).length === gridCells(next).length) { labelRows.push(rows[r]); valueRows.push(next); }
        r += 2;
      } else {
        r += 1;
      }
    }
    var owns = screen.getAttribute('aria-owns');
    var pagesOwn = owns !== null && owns !== ownsWritten.get(screen);
    var listed = [];
    if (!pagesOwn && labelRows.length > 0) {
      for (var c = 0; c < children.length; c++) {
        if (valueRows.indexOf(children[c]) !== -1) { continue; }
        var at = labelRows.indexOf(children[c]);
        if (at === -1) { listed.push(children[c]); continue; }
        var labels = gridCells(labelRows[at]);
        var values = gridCells(valueRows[at]);
        for (var i = 0; i < labels.length; i++) {
          unpicture(labels[i]);
          notComputed(values[i]);
          listed.push(labels[i], values[i]);
        }
      }
    }
    var before = gridListed.get(screen) || [];
    for (var b = 0; b < before.length; b++) {
      if (listed.indexOf(before[b]) === -1) { letGo(before[b]); }
    }
    gridListed.set(screen, listed);
    if (pagesOwn) { ownsWritten.delete(screen); return; }
    var ids = [];
    for (var n = 0; n < listed.length; n++) {
      var id = ownId(listed[n]);
      if (id) { ids.push(id); }
    }
    ownState(screen, 'aria-owns', ids.length ? ids.join(' ') : null, ownsWritten);
  }

  // Lets go of a grid that is no longer a results grid (the page took the class off, or the grid is gone): its aria-owns
  // and everything the layer wrote on what it listed are taken back.
  function letGoGrid(screen) {
    var before = gridListed.get(screen) || [];
    for (var b = 0; b < before.length; b++) { letGo(before[b]); }
    gridListed.delete(screen);
    ownState(screen, 'aria-owns', null, ownsWritten);
  }

  // Takeoff: the results are the MCDU's take-off page, a title, rows that alternate (a row of small labels and, under
  // it, a row of values) and an output line. After Calculate the page changes the values in place; each pass reads them
  // again (the list keeps its cells, so the order holds). The grids read on the last pass are remembered, so a grid
  // that stops being one is given back.
  var grids = [];
  RULES.push(function (root) {
    grids = followFound(root.querySelectorAll('.mcdu-screen'), grids, readGrid, letGoGrid);
  });

  // Landing: the runway condition is the group of exactly six items whose texts start 6 to 1 ("6 DRY" ...
  // "1 POOR"). The page uses div.section-item for much more (ICAO CODE, the units, the METAR), so the group
  // is found by what it holds. The page marks the chosen one with section-selected; it is the checked radio.
  var CONDITION_CODES = '654321';

  function conditionCode(item) {
    var m = /^\d/.exec(textOf(item));
    return m ? m[0] : '';
  }

  // The group is one Tab stop: the checked code, or the first one when none is checked; never a code the
  // page disables. The arrows move through the codes (see onButtonKey). A group that stops matching gets the page's
  // own Tab stops back. A tabindex the page writes on a code while the group matches is overwritten by the next pass:
  // that is what a roving Tab stop is (only one code may be a stop), so only a value the page wrote after the group
  // stopped matching, or in the pass that withdraws it, is the page's to keep.
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
        rovingCodes.set(groups[i], items);
        var stop = null;
        for (var s = 0; s < items.length && !stop; s++) {
          if (items[s].classList.contains('section-selected') && !isDisabled(items[s])) { stop = items[s]; }
        }
        for (var f = 0; f < items.length && !stop; f++) {
          if (!isDisabled(items[f])) { stop = items[f]; }
        }
        for (var j = 0; j < items.length; j++) {
          rovingRadio(items[j], textOf(items[j]), items[j].classList.contains('section-selected'), items[j] === stop);
        }
      } else {
        rovingCodes.delete(groups[i]);
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

  // Landing: the nine system procedures are bare divs with no Tab stop (the layer gives them one). Pressing one opens its
  // sub-page in place (the route stays), whose items are the procedure's conditions: each holds a tick box
  // (div.arrival-checkbox) and is ticked while the page marks it section-selected. Those are check boxes named by their
  // text. An item that gains or loses its tick box changes role; the layer's checked state goes with it.
  RULES.push(function (root) {
    var items = root.querySelectorAll('div.arrival-procedure-item');
    for (var i = 0; i < items.length; i++) {
      var tick = items[i].querySelector('.arrival-checkbox') !== null;
      var role = tick ? 'checkbox' : 'button';
      changeRole(items[i], role);
      button(items[i], textWithout(items[i], '.arrival-checkbox'), role);
      var ticked = items[i].classList.contains('section-selected') ? 'true' : 'false';
      ownState(items[i], 'aria-checked', tick ? ticked : null, checkedWritten);
    }
  });

  // Landing: a procedure's sub-page has no way back but its title row, whose arrow (div.arrival-back) is only an icon; the
  // page's click is on the row and returns to the main Landing page. The row is the button "Back, <title>".
  // A title row without the arrow (the main page's section titles) is not a button.
  RULES.push(function (root) {
    var titles = root.querySelectorAll('div.arrival-title');
    for (var i = 0; i < titles.length; i++) {
      if (titles[i].querySelector('div.arrival-back')) {
        var text = textWithout(titles[i], 'div.arrival-back');
        button(titles[i], text ? 'Back, ' + text : 'Back');
      } else {
        unbutton(titles[i]);
      }
    }
  });

  // ---- Settings ----

  // The setting an article is about: its heading without the "Default" / "Livery" badge inside it.
  function settingName(article) {
    var heading = article ? article.querySelector('.settings-name') : null;
    return textWithout(heading, '.settings-source');
  }

  // Settings: a switch is a checkbox in a label with no text (label.toggle); it takes the name of its article's heading.
  RULES.push(function (root) {
    var boxes = root.querySelectorAll('app-settings2 article input[type=checkbox]');
    for (var i = 0; i < boxes.length; i++) {
      var holder = boxes[i].parentElement;
      if (holder && holder.matches('label.toggle')) { name(boxes[i], settingName(boxes[i].closest('article'))); }
    }
  });

  // Settings: an on/off option is a label holding that checkbox and a styled slider, and the page hides the checkbox
  // (display: none), so a screen reader never meets it. The label is the control: a switch named by the
  // setting, checked while the hidden checkbox is (its checked property), with a Tab stop, and Enter and Space as the
  // label's own click, which toggles the checkbox as a mouse click on the slider does. The checkbox keeps the name the
  // rule above gives it; hidden by the page, it is not read twice. While the page disables the checkbox the switch says
  // it is unavailable (aria-disabled, the layer's own) and has no Tab stop. A label that loses its checkbox or leaves the
  // article is let go of: what the layer wrote on it is taken back.
  function makeSwitch(label) {
    var box = label.querySelector('input[type=checkbox]');
    ownState(label, 'aria-disabled', isDisabled(box) ? 'true' : null, disabledWritten);
    button(label, settingName(label.closest('article')), 'switch');
    ownState(label, 'aria-checked', box.checked ? 'true' : 'false', checkedWritten);
  }

  function dropSwitch(label) {
    ownState(label, 'aria-disabled', null, disabledWritten);
    ownState(label, 'aria-checked', null, checkedWritten);
    unbutton(label);
  }

  var switches = [];
  RULES.push(function (root) {
    var labels = root.querySelectorAll('app-settings2 article label.toggle');
    var found = [];
    for (var i = 0; i < labels.length; i++) {
      if (labels[i].querySelector('input[type=checkbox]')) { found.push(labels[i]); }
    }
    switches = followFound(found, switches, makeSwitch, dropSwitch);
  });

  // Settings: a text, password or file box in an article has no label of its own, and its placeholder ("Enter value")
  // does not say which setting it is: it takes the setting's name, as the switch and the three dots do.
  RULES.push(function (root) {
    var boxes = root.querySelectorAll('app-settings2 article input:not([type=checkbox])');
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

  // Settings: a three-dot button opens its menu as a CDK overlay at the end of <body>: a backdrop, then a
  // pane holding div.overflow-menu and its items. The focus stayed on the button and the items were read nowhere near
  // it, so the menu seemed empty. The open menu: exactly one three-dot button the page marks expanded, in its
  // div.settings-overflow, and exactly one overlay pane holding a menu; null when there are none or more than one. The
  // pane's backdrop is the element just before the pane's host, where CDK puts it.
  function openMenu(root) {
    var marked = root.querySelectorAll('app-settings2 .settings-overflow [aria-expanded="true"]');
    var origins = [];
    for (var i = 0; i < marked.length; i++) {
      if (marked[i].matches('button.overflow-button')) { origins.push(marked[i]); }
    }
    var menus = root.querySelectorAll('.cdk-overlay-pane > div.overflow-menu');
    if (origins.length !== 1 || menus.length !== 1) { return null; }
    var pane = menus[0].parentElement;
    var host = pane.parentElement;
    var backdrop = host ? host.previousElementSibling : null;
    return {
      pane: pane,
      container: origins[0].closest('div.settings-overflow'),
      origin: origins[0],
      backdrop: backdrop && backdrop.classList.contains('cdk-overlay-backdrop') ? backdrop : null,
      // The CDK overlay container that holds the backdrop and the pane's host.
      overlay: host ? host.parentElement : null
    };
  }

  // A menu's first item the page has not disabled, or null.
  function firstItem(pane) {
    var items = pane.querySelectorAll('button.overflow-menu__item');
    for (var i = 0; i < items.length; i++) {
      if (!isDisabled(items[i])) { return items[i]; }
    }
    return null;
  }

  // The open menu as the layer saw it on the last pass (its pane, its button's container, the button recorded when it
  // opened, its backdrop, its overlay container), or null.
  var menu = null;

  // True when the focus went with a menu that closed: it is on the page's body or nowhere, still in the emptied pane, or
  // on the menu's backdrop or anything else in its overlay container ("Close menu" closes the menu with the
  // focus on it, and CDK keeps the backdrop in the page for its fade before the focus drops to the body).
  function focusLostWith(was) {
    var active = document.activeElement;
    if (!active || active === document.body) { return true; }
    if (was.pane.contains(active) || active === was.backdrop) { return true; }
    return was.overlay !== null && was.overlay.contains(active);
  }

  // While a menu is open, its button's container aria-owns the pane (the page's own id, else one of the layer's), so the
  // items are read right after the button, and the backdrop is the button "Close menu", whose click closes the menu as
  // a mouse click outside does. When a menu opens the focus goes to its first item that is not disabled; when it closes
  // and the focus is lost with it (see focusLostWith), the focus goes back to the button the layer recorded when the
  // menu opened, if that is still on the page. That is decided before the backdrop's Tab stop is taken back. What the
  // layer wrote for a menu goes with it.
  function followMenu(root) {
    var now = openMenu(root);
    var was = menu;
    var opened = now !== null && (was === null || was.pane !== now.pane);
    if (now && !opened) { now.origin = was.origin; }
    if (was) {
      var gone = !was.pane.isConnected || !was.pane.querySelector('div.overflow-menu');
      if (!now && gone && focusLostWith(was) && was.origin.isConnected) { moveFocus(was.origin); }
      if (!now || now.container !== was.container) { ownState(was.container, 'aria-owns', null, ownsWritten); }
      if (!now || now.pane !== was.pane) { dropId(was.pane); }
      if (was.backdrop && (!now || now.backdrop !== was.backdrop)) { unbutton(was.backdrop); }
    }
    if (now) {
      ownState(now.container, 'aria-owns', ownId(now.pane), ownsWritten);
      if (now.backdrop) { button(now.backdrop, 'Close menu'); }
      if (opened) {
        var first = firstItem(now.pane);
        if (first) { moveFocus(first); }
      }
    }
    menu = now;
  }
  RULES.push(followMenu);

  // A Settings choice's name: its label's text, else (a choice that is only a picture) the picture's alt.
  function optionName(option) {
    var text = textOf(option.querySelector('.option-label'));
    if (text) { return text; }
    var picture = option.querySelector('img');
    return picture ? (picture.getAttribute('alt') || '').replace(/\s+/g, ' ').trim() : '';
  }

  // Settings: an option is a real button. Chromium's tree showed it unnamed once its picture was hidden, so it is
  // named by its own label's text, written as a label. It says whether it is the chosen one, as the page
  // does with option--active.
  RULES.push(function (root) {
    var options = root.querySelectorAll('app-settings2 button.option');
    for (var i = 0; i < options.length; i++) {
      name(options[i], optionName(options[i]));
      ownState(options[i], 'aria-pressed', options[i].classList.contains('option--active') ? 'true' : 'false', pressedWritten);
    }
  });

  // The containers the layer made a group of on its last pass.
  var choiceGroups = [];

  // Settings: an article's choices (button.option) sit in a container of the page's own. It is a group named by
  // the setting, as the article's checkbox and three dots are, so a Tab into "Classic" or "Modern" says which
  // setting they choose. A container that no longer holds a choice is let go of: the layer takes back its own
  // role and label, never the page's.
  RULES.push(function (root) {
    var options = root.querySelectorAll('app-settings2 article button.option');
    var groups = [];
    for (var i = 0; i < options.length; i++) {
      var container = options[i].parentElement;
      var article = options[i].closest('article');
      if (!container || container === article || groups.indexOf(container) !== -1) { continue; }
      groups.push(container);
      giveRole(container, 'group');
      name(container, settingName(article));
    }
    for (var j = 0; j < choiceGroups.length; j++) {
      if (groups.indexOf(choiceGroups[j]) === -1) { release(choiceGroups[j]); }
    }
    choiceGroups = groups;
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

  // The page shows a failure's state by colour only, with classes of its own. A chapter
  // (div.manual-failure-ata, the title's container) carries manual-failure-ata--ata-failed while a failure in it
  // is set and --ata-armed while one is armed; failed wins. The words go after the chapter's title.
  function chapterState(title) {
    var chapter = title.closest('div.manual-failure-ata');
    if (!chapter) { return ''; }
    if (chapter.classList.contains('manual-failure-ata--ata-failed')) { return ', has failures'; }
    if (chapter.classList.contains('manual-failure-ata--ata-armed')) { return ', has armed failures'; }
    return '';
  }

  // An item button carries manual-failure-failed while it is set and manual-failure-armed while it is armed.
  function itemState(item) {
    if (item.classList.contains('manual-failure-failed')) { return ', failed'; }
    if (item.classList.contains('manual-failure-armed')) { return ', armed'; }
    return '';
  }

  // Failures: a chapter's title (ATA 21 ...) is a bare div the page opens and closes; its chevron points down
  // while the chapter is open and right while it is closed. Its name says whether the chapter has failures.
  RULES.push(function (root) {
    var titles = root.querySelectorAll('app-manual-failures div.manual-failure-ata-title');
    for (var i = 0; i < titles.length; i++) {
      var text = textOf(titles[i]);
      button(titles[i], text ? text + chapterState(titles[i]) : '');
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
  // "Overheat Pack 1") and ends with its state ("CPC 1, failed"). An item in a group without a title and with no
  // state is named by its own text, which the button already is, so the layer writes no name there.
  RULES.push(function (root) {
    var items = root.querySelectorAll('app-manual-failures button.manual-failure-item');
    for (var i = 0; i < items.length; i++) {
      var group = groupTitleOf(items[i]);
      var text = textOf(items[i]);
      var state = itemState(items[i]);
      var full = group && text ? group + ' ' + text : text;
      name(items[i], text && (group || state) ? full + state : '');
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

  // Documents: a folder's row and a file's row are bare divs the page clicks, and each is a button. A row that
  // holds controls of its own (a button, a box or a link: "My Documents" has "Open documents folder" and "Upload
  // document") is not a button, because a control inside a button cannot be reached: its label is the button
  // instead, and Enter on the label is a click on the text, which reaches the row's own handler. A row that gains
  // or loses controls hands the button over; each time only the stamp the layer wrote on the element that no
  // longer applies is taken back. Returns the element that is the button (null when there is none).
  function documentRow(row, nameFromLabel) {
    var label = row.querySelector('span.label');
    if (row.querySelector('button, input, a[href]')) {
      unbutton(row);
      if (label) { button(label, textOf(label)); }
      return label;
    }
    if (label) { unbutton(label); }
    button(row, textOf(nameFromLabel && label ? label : row));
    return row;
  }

  // A row's state goes on its button (the row or its label); the one the layer wrote on the other is taken back.
  function rowState(row, target, attr, value, store) {
    var label = row.querySelector('span.label');
    if (row !== target) { ownState(row, attr, null, store); }
    if (label && label !== target) { ownState(label, attr, null, store); }
    if (target) { ownState(target, attr, value, store); }
  }

  // Documents: a folder is open while its div.folder (the row's container) carries the page's `open` class;
  // a row with no div.folder around it says nothing about being open.
  RULES.push(function (root) {
    var folders = root.querySelectorAll('app-documents div.folder-name');
    for (var i = 0; i < folders.length; i++) {
      var target = documentRow(folders[i], true);
      var folder = folders[i].parentElement;
      var open = folder && folder.localName === 'div' && folder.classList.contains('folder') ?
        (folder.classList.contains('open') ? 'true' : 'false') : null;
      rowState(folders[i], target, 'aria-expanded', open, expandedWritten);
    }
  });

  // Documents: the file the page marks is-selected is the current one.
  RULES.push(function (root) {
    var files = root.querySelectorAll('app-documents div.file');
    for (var i = 0; i < files.length; i++) {
      var target = documentRow(files[i], false);
      rowState(files[i], target, 'aria-current', files[i].classList.contains('is-selected') ? 'true' : null, currentWritten);
    }
  });

  // Documents: the button on the edge of the list has no text and no title.
  RULES.push(function (root) {
    var toggles = root.querySelectorAll('app-documents button.sidebar-toggle-btn');
    for (var i = 0; i < toggles.length; i++) { name(toggles[i], 'Toggle document list'); }
  });

  // ---- Charts and quick settings ----

  // Charts: the sidebar's items (the div of each app-navigraph-sidebar-item) are divs the page clicks, named by
  // their text. The "My Flight" header above them carries the item class too, but it is a heading.
  RULES.push(function (root) {
    var items = root.querySelectorAll('app-navigraph-sidebar-item div.navigraph-sidebar-item');
    for (var i = 0; i < items.length; i++) { button(items[i], textOf(items[i])); }
  });

  // Charts: the zoom controls are icon fonts. The ids (mapZoomInBtn, mapZoomOutBtn) sit on the icons and their
  // parent holds both, so each icon is the button (a capture cannot show where the page's listener is). The
  // names are the owner's.
  var ZOOM = [['fa-plus-circle', 'Zoom in'], ['fa-minus-circle', 'Zoom out']];
  RULES.push(function (root) {
    for (var k = 0; k < ZOOM.length; k++) {
      var icons = root.querySelectorAll('div.chart-viewer-controls i.' + ZOOM[k][0]);
      for (var i = 0; i < icons.length; i++) { button(icons[i], ZOOM[k][1]); }
    }
  });

  // Charts: the map controls are icon fonts too; the "World Map" item has its own text. The page
  // marks the chosen item map-control-item-selected. The settings cog opens the settings and is not a choice:
  // it is a button with no pressed state; the map modes and the two themes say which is chosen.
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

  // Charts: the list toggle on the edge of the sidebar is an empty div the page clicks (Documents'
  // is "Toggle document list").
  RULES.push(function (root) {
    var toggles = root.querySelectorAll('app-charts div.sidebar-toggle-btn');
    for (var i = 0; i < toggles.length; i++) { button(toggles[i], 'Toggle chart list'); }
  });

  // Charts: the moving map is Leaflet's own focusable container (tabindex 0, no text). It only gets a name:
  // Leaflet owns its keyboard, so no role, no Tab stop of ours and no key handler.
  RULES.push(function (root) {
    var maps = root.querySelectorAll('app-charts-map div.leaflet-container');
    for (var i = 0; i < maps.length; i++) { name(maps[i], 'Moving map'); }
  });

  // Quick settings: the airplane button holds only its picture, found by its class or its file name.
  RULES.push(function (root) {
    var pictures = root.querySelectorAll('img.flight-mode-btn-icon, img[src*=airplane]');
    for (var i = 0; i < pictures.length; i++) {
      var holder = pictures[i].closest('button.quick-settings-btn');
      if (holder) { name(holder, 'Airplane mode'); }
    }
  });

  // Quick settings: the brightness slider is a mat-slider with a value and no name.
  RULES.push(function (root) {
    var sliders = root.querySelectorAll('mat-slider.brightness-popup-slider');
    for (var i = 0; i < sliders.length; i++) { name(sliders[i], 'Brightness'); }
  });

  // Quick settings: the backdrop behind the pop-up is an empty div whose click closes it, as a mouse click outside
  // the pop-up does. Escape closes the whole window, so it is the keyboard's way out.
  RULES.push(function (root) {
    var backdrops = root.querySelectorAll('div.brightness-popup-backdrop');
    for (var i = 0; i < backdrops.length; i++) { button(backdrops[i], 'Close quick settings'); }
  });

  // ---- What a screen reader does not need ----
  // These rules come last: the glyph rule asks which elements are controls, and the rules above make them so.

  // Hides an element from a screen reader. A hidden state the page set is the page's (never overridden); the layer
  // takes back only its own.
  function hide(el) {
    if (!el.hasAttribute('aria-hidden')) { el.setAttribute('aria-hidden', 'true'); hiddenWritten.add(el); }
  }

  function unhide(el) {
    if (!hiddenWritten.has(el)) { return; }
    hiddenWritten.delete(el);
    if (el.getAttribute('aria-hidden') === 'true') { el.removeAttribute('aria-hidden'); }
  }

  // Hides from a screen reader the elements a rule found on this pass, and reads again the ones it hid on an earlier
  // pass and no longer finds (only the layer's own hidden state, never the page's). Returns this pass's list.
  function hideOnly(found, before) {
    return followFound(found, before, hide, unhide);
  }

  // Every screen: the background picture and the top bar's status icons (airplane mode, signal, link) are pictures with
  // no alt, of nothing a pilot acts on: decorative.
  var DECORATIVE = 'img#appBg, img.airplane-icon, img.signal-icon, img#linkedup';
  var decorative = [];
  RULES.push(function (root) {
    decorative = hideOnly(root.querySelectorAll(DECORATIVE), decorative);
  });

  // Mass and Balance: the W&B chart is a picture with labels laid over it. Without the picture its axis
  // ticks (digits, dots or commas only) are a bare run of numbers, and its axis title ("%MAC") and line captions
  // ("Take-Off Limits") name what no one can see: the picture and every label are hidden, except a label that states a
  // limit ("MZFW = 61,000KG", a text with "="). A label is a child of the chart with text and no element of its own; the
  // figures table under the chart (live values) is not a label and stays.
  var W_AND_B = 'div#mainWandBCont';
  var chartHidden = [];
  RULES.push(function (root) {
    var charts = root.querySelectorAll(W_AND_B);
    var found = [];
    for (var i = 0; i < charts.length; i++) {
      for (var child = charts[i].firstElementChild; child; child = child.nextElementSibling) {
        if (child.localName === 'img') { found.push(child); continue; }
        if (child.firstElementChild) { continue; }
        var text = textOf(child);
        if (text && text.indexOf('=') === -1) { found.push(child); }
      }
    }
    chartHidden = hideOnly(found, chartHidden);
  });

  // Mass and Balance: the seat map is a few hundred seats the page clicks, each read as "clickable"; it shows only
  // occupancy, and the boarded counts are in the table beside it.
  var seatMaps = [];
  RULES.push(function (root) {
    seatMaps = hideOnly(root.querySelectorAll('#seatContainer'), seatMaps);
  });

  // Settings: a choice's picture repeats the choice's own text in its alt ("GNADIRS"), so the choice was read twice. It
  // is hidden while the choice has text of its own; a choice that is only a picture keeps it as its name.
  var optionPictures = [];
  RULES.push(function (root) {
    var pictures = root.querySelectorAll('button.option img.option-image');
    var found = [];
    for (var i = 0; i < pictures.length; i++) {
      if (textOf(pictures[i].closest('button.option'))) { found.push(pictures[i]); }
    }
    optionPictures = hideOnly(found, optionPictures);
  });

  // Two plain wrappers the page gives a click handler, which a screen reader announces as "clickable": the span around
  // the top bar's status icons and quick settings button, and the div around every app's view. Neither is a control;
  // role "none" quiets them. A role the page gives is the page's; the layer's own goes with the class.
  var quieted = [];
  function quiet(el) { giveRole(el, 'none'); }
  RULES.push(function (root) {
    quieted = followFound(root.querySelectorAll('span.status-icons-clickable, div.router-container'), quieted, quiet, takeRole);
  });

  // The controls a glyph can sit in: the page's own, and every element the layer stamped.
  var CONTROL = 'button, a[href], [role=button], [role=switch], [role=checkbox], [role=radio], [role=tab], ' +
    '[role=combobox], [role=link], [' + MARK + ']';

  // Every screen: an icon font draws its glyph from a style sheet as a private-use character, which Chromium makes the
  // name of a button that has only a title ("Expanded view") or reads as a stray line before a button's text ("SYNC
  // LIVE WX"). A glyph inside a control is hidden; the control keeps its own name. An icon that is itself
  // the control (Charts' zoom icons) is left alone, and so is one in no control.
  var glyphs = [];
  RULES.push(function (root) {
    var icons = root.querySelectorAll('i[class*="fa-"]');
    var found = [];
    for (var i = 0; i < icons.length; i++) {
      var holder = icons[i].parentElement;
      if (!icons[i].matches(CONTROL) && holder && holder.closest(CONTROL)) { found.push(icons[i]); }
    }
    glyphs = hideOnly(found, glyphs);
  });

  // Every screen: a picture with no alt attribute says nothing to a screen reader but "graphic", or its file name (the
  // Landing logo and FMS and one-click pictures, the Charts sidebar icons, quick settings' airplane): it is hidden.
  // An alt, an empty one included, is the page's own word on the picture, and a picture that is itself a
  // control is the control: both are left alone. The pictures the rules above hide (the background and status icons,
  // the W&B chart's picture) are theirs. A control that holds a hidden picture keeps its name: a picture with no alt
  // gives none.
  var unlabeled = [];
  RULES.push(function (root) {
    var pictures = root.querySelectorAll('img:not([alt])');
    var found = [];
    for (var i = 0; i < pictures.length; i++) {
      var picture = pictures[i];
      var chart = picture.parentElement && picture.parentElement.matches(W_AND_B);
      if (!chart && !picture.matches(CONTROL) && !picture.matches(DECORATIVE)) { found.push(picture); }
    }
    unlabeled = hideOnly(found, unlabeled);
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
  // aria-* it writes, tabindex, data-msfsba: attributes, never text or nodes, never `disabled`) do not wake it. The one
  // exception is the aria-disabled it writes or takes back on a Settings switch whose checkbox the page disabled or
  // enabled: that wakes one more pass, which writes nothing new.
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
      // A Settings switch's state is its hidden checkbox's checked property, which the observer cannot see: a change
      // event (heard here on its way down, before the page's own listeners) schedules a pass. It is neither stopped
      // nor prevented, so it changes nothing the page does.
      document.addEventListener('change', function () {
        try { schedule(); } catch (err) { /* never throw into the page */ }
      }, true);
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
