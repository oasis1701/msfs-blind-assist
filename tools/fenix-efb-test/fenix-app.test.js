'use strict';
// The Fenix app's screens, checked against the LIVE captures (fixtures/my-flight.html,
// ground-services.html, mass-balance.html, panel-states.html, switcher.html; Fenix 3.0.0.7438, A320-232
// IAE, kilograms): the tab bar, My Flights' cards, Ground Services' switches and pushback arrows, Mass and
// Balance's boxes and buttons, the Panel States pop-up, the app switcher's chevron state. The loading
// notices run on a HAND-MADE fixture (fixtures/notices.html) written from the page bundle's markup.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const { computeAccessibleName } = require('dom-accessibility-api');
const { load, loadCounting, press, attributesOf, hiddenByLayer, turns, LAYER } = require('./load');

const nameOf = el => computeAccessibleName(el);
const all = (window, selector) => [...window.document.querySelectorAll(selector)];
const squash = text => text.replace(/\s+/g, ' ').trim();

// The Fenix-app captures that show the tab bar (the switcher's shows My Flights behind it).
const TAB_BAR_SCREENS = ['my-flight', 'switcher', 'ground-services', 'mass-balance', 'panel-states'];

// Ground Services, in page order, as captured (the page's capitals kept).
const SWITCH_NAMES = [
  'GPU', 'PCA', 'ENGINE FAN HEATING', 'CHOCKS & CONES', 'FWD STAIRS', 'AFT STAIRS',
  '1 left', '1 right', '2 left', '2 right', 'fwd cargo', 'aft cargo', 'bulk', 'CONNECT TUG'
];

test('Ground_services_switches_are_named_by_their_text', async () => {
  const window = await load('ground-services');
  try {
    const switches = all(window, 'input[role=switch]');
    assert.strictEqual(switches.length, 14);
    assert.deepStrictEqual(switches.map(nameOf), SWITCH_NAMES);

    // The names are the page's own div.text, one beside each toggle, not a list kept here.
    const texts = all(window, '.slide-container > div.text').map(el => el.textContent.trim());
    assert.deepStrictEqual(switches.map(nameOf), texts);

    // The page keeps its own state and role: the layer adds a name and nothing else.
    for (const sw of switches) {
      assert.strictEqual(sw.getAttribute('role'), 'switch');
      assert.ok(['true', 'false'].includes(sw.getAttribute('aria-checked')), 'the page\'s own aria-checked stays');
    }

    // The pushback arrows are bare divs: buttons with the names the owner chose.
    const arrows = ['left-arrow', 'down-arrow', 'right-arrow'].map(cls => window.document.querySelector('div.' + cls));
    assert.ok(arrows.every(Boolean), 'the capture has the three arrows');
    assert.deepStrictEqual(arrows.map(nameOf), ['Pushback left', 'Pushback straight', 'Pushback right']);
    for (const arrow of arrows) {
      assert.strictEqual(arrow.getAttribute('role'), 'button');
      assert.strictEqual(arrow.tabIndex, 0);
    }

    // Enter and Space press an arrow through the page's own click.
    let clicks = 0;
    arrows[1].click = () => { clicks++; };
    assert.ok(press(window, arrows[1], { key: 'Enter' }).defaultPrevented);
    assert.ok(press(window, arrows[1], { key: ' ' }).defaultPrevented);
    assert.strictEqual(clicks, 2);
  } finally {
    window.close();
  }
});

test('A_ground_services_switch_name_follows_the_pages_text_and_a_switch_without_text_stays_unnamed', async () => {
  const window = await load('ground-services');
  try {
    const doc = window.document;
    const [gpu] = all(window, 'input[role=switch]');
    const text = gpu.closest('mat-slide-toggle').nextElementSibling;
    assert.strictEqual(text.textContent, 'GPU');

    // Angular updates a text node in place.
    text.firstChild.nodeValue = 'GPU (400 Hz)';
    await turns();
    assert.strictEqual(nameOf(gpu), 'GPU (400 Hz)');

    // A switch with no text beside it (a Fenix update) is left unnamed, not guessed, and nothing throws.
    const lone = doc.createElement('div');
    lone.innerHTML = '<mat-slide-toggle><label><input type="checkbox" role="switch" aria-checked="false"></label></mat-slide-toggle>';
    doc.body.appendChild(lone);
    await turns();
    const loneSwitch = lone.querySelector('input');
    assert.strictEqual(loneSwitch.hasAttribute('aria-label'), false);
    assert.strictEqual(nameOf(gpu), 'GPU (400 Hz)', 'the other rules still ran');
  } finally {
    window.close();
  }
});

test('Mass_and_balance_boxes_say_what_they_hold', async () => {
  const window = await load('mass-balance');
  try {
    const boxes = all(window, 'input.planned-input');
    assert.strictEqual(boxes.length, 4);
    // "<section title before its first parenthesis> <column header>, <placeholder>", every part the
    // page's own (the column header keeps its unit span: "Planned (KG)").
    assert.deepStrictEqual(boxes.map(nameOf), [
      'Passengers Planned, Max 180',
      'Cargo Planned (KG), Max 8,827',
      'Fuel Planned (KG), Max 19,004',
      'Planned Weights Zero Fuel Weight (KG), Max 61,000'
    ]);
    assert.deepStrictEqual(boxes.map(b => b.getAttribute('placeholder')),
      ['Max 180', 'Max 8,827', 'Max 19,004', 'Max 61,000'], 'the page\'s own placeholders are untouched');

    // The buttons are named by their text with the <br> folded to a space (page capitals kept).
    const buttons = all(window, 'div.action-button-cell > button');
    assert.deepStrictEqual(buttons.map(nameOf), ['Reset All', 'Resend Loadsheet', 'Load Aircraft', 'SEND TO MCDU']);
    assert.ok(buttons.every(b => !b.hasAttribute('role')), 'they are real buttons already');
    assert.strictEqual(buttons[1].disabled, true, 'a disabled button stays disabled');
  } finally {
    window.close();
  }
});

test('Mass_and_balance_names_follow_the_pages_text', async () => {
  const window = await load('mass-balance');
  try {
    const [pax, cargo] = all(window, 'input.planned-input');
    // The unit changes (the page re-renders the span's text): the name follows, from the page.
    cargo.closest('table').querySelector('span.units').firstChild.nodeValue = '(LB)';
    // The section title's text changes.
    pax.closest('table').parentElement.childNodes.forEach(n => {
      if (n.nodeType === 3 && n.nodeValue.includes('Passengers')) n.nodeValue = ' Pax (Capacity: 180 Economy) ';
    });
    await turns();
    assert.strictEqual(nameOf(cargo), 'Cargo Planned (LB), Max 8,827');
    assert.strictEqual(nameOf(pax), 'Pax Planned, Max 180');
  } finally {
    window.close();
  }
});

// The W&B chart is a picture with labels laid over it: axis ticks (numbers only), the axis title "%MAC", line
// captions, and the three limits ("MZFW = 61,000KG"). Without the picture the ticks are a bare run of numbers and the
// captions name lines no one can see: hidden. The limits are values a pilot uses: kept. A label is a text-only child of
// the chart; the figures table under it (ZFW, GW and their %MAC, live values) is not a label and is left alone.
const LIMITS = ['MZFW = 61,000KG', 'MLW = 64,500KG', 'MTOW = 73,500KG'];
const chartLabels = chart => [...chart.children].filter(el => el.firstElementChild === null && squash(el.textContent) !== '');

test('The_weight_and_balance_chart_keeps_only_its_limits_for_a_screen_reader', async () => {
  const window = await load('mass-balance');
  try {
    const chart = window.document.getElementById('mainWandBCont');
    const text = el => squash(el.textContent);
    // The expected set, counted from the capture by the page's own classes (review minor 8: not by the rule's idea of a
    // label): the top axis (its title "%MAC" and 18 ticks), the left axis (9 ticks) and the lines' texts (5 captions,
    // 3 limits).
    const byClass = cls => [...chart.children].filter(el => el.classList.contains(cls));
    const top = byClass('text-numbers-top-in-graphics');
    const left = byClass('text-numbers-left-in-graphics');
    const lines = byClass('line-with-text');
    assert.deepStrictEqual([top.length, left.length, lines.length], [19, 9, 8], 'as captured');
    const ticks = [...top, ...left].filter(el => /^\d+$/.test(text(el)));
    assert.strictEqual(ticks.length, 27);
    assert.deepStrictEqual(top.filter(el => !ticks.includes(el)).map(text), ['%MAC']);
    const limits = lines.filter(el => text(el).includes('='));
    const captions = lines.filter(el => !limits.includes(el));
    assert.deepStrictEqual(limits.map(text), LIMITS);
    assert.deepStrictEqual(captions.map(text), ['Operational Limits', 'Take-Off Limits', 'Zfw limit', 'Zfw limit', 'Take-Off Limits']);
    // Exactly these are hidden in the chart: its picture, the 27 ticks, "%MAC" and the 5 captions; the limits are read.
    const expectedHidden = [window.document.getElementById('mainWandB'), ...top, ...left, ...captions];
    const hiddenNow = [...chart.querySelectorAll('[aria-hidden=true]')];
    assert.strictEqual(hiddenNow.length, expectedHidden.length);
    assert.ok(expectedHidden.every(el => hiddenNow.includes(el)), 'each expected one is hidden');
    assert.ok(limits.every(el => !el.hasAttribute('aria-hidden')), 'the limits are read');
    assert.ok([...top, ...left, ...lines].every(l => !l.hasAttribute('role') && !l.hasAttribute('aria-label')), 'hidden or kept, nothing else');

    // The chart picture is hidden; the figures under the chart are values and stay readable.
    assert.strictEqual(window.document.getElementById('mainWandB').getAttribute('aria-hidden'), 'true');
    const figures = chart.querySelector('.mandbInfoArea');
    assert.ok(figures, 'the capture has the figures');
    assert.strictEqual(figures.querySelectorAll('[aria-hidden]').length + (figures.hasAttribute('aria-hidden') ? 1 : 0), 0);
    assert.ok(window.__msfsbaFenixEfb.textOf(figures).includes('43,839'), 'the live ZFW is read');

    // A label's text changes: what is hidden follows the text, not the label.
    const mlw = limits.find(l => text(l) === 'MLW = 64,500KG');
    mlw.firstChild.nodeValue = ' MLW ';
    await turns();
    assert.strictEqual(mlw.getAttribute('aria-hidden'), 'true');
    mlw.firstChild.nodeValue = ' MLW = 64,000KG ';
    await turns();
    assert.strictEqual(mlw.hasAttribute('aria-hidden'), false, 'the layer\'s own hidden state is taken back');
    ticks[0].firstChild.nodeValue = ' CG = 21 ';
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(ticks[0].hasAttribute('aria-hidden'), false);
  } finally {
    window.close();
  }
});

test('A_hidden_state_the_page_set_on_the_chart_is_kept', async () => {
  const window = await load('mass-balance', { layer: false });
  try {
    const labels = chartLabels(window.document.getElementById('mainWandBCont'));
    const mac = labels.find(l => squash(l.textContent) === '%MAC');
    const mzfw = labels.find(l => squash(l.textContent) === LIMITS[0]);
    mac.setAttribute('aria-hidden', 'false');
    mzfw.setAttribute('aria-hidden', 'true');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(mac.getAttribute('aria-hidden'), 'false', 'the page\'s own, not overridden');
    assert.strictEqual(mzfw.getAttribute('aria-hidden'), 'true', 'the page\'s own, not taken back');
  } finally {
    window.close();
  }
});

// The seat map is a table of a few hundred seats the page clicks, each read as "clickable"; it shows occupancy,
// and the boarded counts are in the table beside it. Hidden from a screen reader, the seats themselves untouched.
test('The_seat_map_is_hidden_from_a_screen_reader', async () => {
  const bare = await load('mass-balance', { layer: false });
  const window = await load('mass-balance');
  try {
    const seats = window.document.getElementById('seatContainer');
    assert.ok(seats, 'the capture keeps the seat map');
    assert.ok(seats.querySelectorAll('td.seat-td').length > 100, 'as captured');
    assert.strictEqual(seats.getAttribute('aria-hidden'), 'true');
    assert.ok(['role', 'aria-label', 'tabindex', 'data-msfsba'].every(a => !seats.hasAttribute(a)));
    assert.strictEqual(seats.querySelectorAll('[role],[aria-hidden],[aria-label],[data-msfsba]').length, 0, 'the seats are left as they are');

    // On this screen the layer hides exactly: the top bar's pictures and glyphs, every other picture with no alt (the
    // closed switcher's icons, the battery pictures, the logos, the seat map's background), the chart's picture and
    // labels but its limits, and the seat map.
    const hidden = hiddenByLayer(bare, window);
    assert.deepStrictEqual(hidden.filter(h => !/^div\.(line-with-text|text-numbers-)/.test(h)), [
      'img#appBg', 'i.fas.fa-chevron-down', 'img.airplane-icon.top-icon-fade-out', 'img.signal-icon.top-icon-fade-in',
      'img#linkedup', 'i.fas.fa-cog', ...Array(9).fill('img.app-img'), 'img.battery-none', 'img.lightning', 'img',
      'img#mainWandB', 'img.logo.opacity-10.logo-big', 'img#seatMapBg', 'div#seatContainer'
    ]);
    assert.strictEqual(hidden.length, 22 + 33);
  } finally {
    bare.close();
    window.close();
  }
});

const MASS_BALANCE_ISOLATION = [
  ['mainWandBCont', '#mainWandBCont > [aria-hidden=true]', 34],
  ['seatContainer', '#seatContainer[aria-hidden=true]', 1],
  ['planned-input', 'input.planned-input[aria-label]', 4]
];
for (const [failing] of MASS_BALANCE_ISOLATION) {
  test('A_failing_rule_does_not_skip_the_others_on_mass_balance_when_' + failing.replace(/-/g, '_') + '_fails', async () => {
    const window = await load('mass-balance', { layer: false });
    const proto = window.Element.prototype;
    const original = proto.querySelectorAll;
    try {
      proto.querySelectorAll = function (s) {
        if (this === window.document.body && s.includes(failing)) throw new Error('boom');
        return original.call(this, s);
      };
      window.eval(fs.readFileSync(LAYER, 'utf8'));
      await turns();
      proto.querySelectorAll = original;
      assert.strictEqual(window.document.getElementById('homeButtonFree').getAttribute('role'), 'button', 'an older rule ran');
      for (const [needle, selector, count] of MASS_BALANCE_ISOLATION) {
        const found = window.document.querySelectorAll(selector).length;
        assert.strictEqual(found, needle === failing ? 0 : count,
          needle === failing ? 'the failing rule did nothing' : 'the rule for ' + needle + ' still ran');
      }
    } finally {
      proto.querySelectorAll = original;
      window.close();
    }
  });
}

test('A_box_the_layer_cannot_place_in_a_table_is_named_by_what_the_page_gives_it', async () => {
  const window = await load('mass-balance');
  try {
    const doc = window.document;
    const holder = doc.createElement('div');
    holder.innerHTML =
      '<input class="planned-input" placeholder="Max 5">' +
      '<table><tr><td><input class="planned-input" placeholder="Max 6"></td></tr></table>' +
      '<input class="planned-input">';
    doc.body.appendChild(holder);
    await turns();
    const [bare, headerless, empty] = holder.querySelectorAll('input');
    assert.strictEqual(nameOf(bare), 'Max 5', 'no table: the placeholder alone');
    assert.strictEqual(nameOf(headerless), 'Max 6', 'a table whose only row is the box\'s own: no header to read');
    assert.strictEqual(nameOf(empty), '', 'nothing on the page to say: no name is made up');
    assert.strictEqual(empty.hasAttribute('aria-label'), false);
  } finally {
    window.close();
  }
});

// The three My Flights cards in page order, as captured: A320 Flight is the active card, Route sits to the
// right and Weather to the left, each side card under a div.slide-prevent-click overlay.
const cardsOf = window => all(window, 'app-my-flight-tab .slide-content');
const zoneOf = card => card.querySelector('div.import-sb-zone');
const overlayOf = card => card.querySelector('.slide-prevent-click');
const IMPORT = ': Tap to import from simbrief';

// What the layer writes on a pressable element. A withdrawn or never-stamped element carries none of it.
const layerStamp = el => ({
  role: el.getAttribute('role'),
  tabindex: el.getAttribute('tabindex'),
  label: el.getAttribute('aria-label'),
  mark: el.getAttribute('data-msfsba')
});
const NO_STAMP = { role: null, tabindex: null, label: null, mark: null };

test('My_flight_cards_are_buttons', async () => {
  const window = await load('my-flight');
  try {
    const [flight, route, weather] = cardsOf(window);
    assert.deepStrictEqual([flight, route, weather].map(c => c.className.match(/(active|right|left)-tab/)[0]),
      ['active-tab', 'right-tab', 'left-tab'], 'the capture\'s cards');

    // Only the active card's import zone is a button: the card title comes from the page's own title row,
    // the zone's own text follows it.
    const zone = zoneOf(flight);
    assert.strictEqual(zone.getAttribute('role'), 'button');
    assert.strictEqual(zone.tabIndex, 0);
    assert.strictEqual(nameOf(zone), 'A320 Flight' + IMPORT);
    // The side cards' zones are not offered: pressing a side card's zone is not how the page imports.
    for (const card of [route, weather]) assert.deepStrictEqual(layerStamp(zoneOf(card)), NO_STAMP);

    let clicks = 0;
    zone.click = () => { clicks++; };
    assert.ok(press(window, zone, { key: 'Enter' }).defaultPrevented);
    assert.ok(press(window, zone, { key: ' ' }).defaultPrevented);
    assert.strictEqual(clicks, 2);

    // Each side card's overlay is the button that slides that card in (the page imports nothing there).
    assert.strictEqual(overlayOf(flight), null, 'the active card has no overlay');
    const overlays = [route, weather].map(overlayOf);
    assert.deepStrictEqual(overlays.map(nameOf), ['Show Route', 'Show Weather']);
    for (const overlay of overlays) {
      assert.strictEqual(overlay.getAttribute('role'), 'button');
      assert.strictEqual(overlay.tabIndex, 0);
    }
    let slides = 0;
    overlays[0].click = () => { slides++; };
    assert.ok(press(window, overlays[0], { key: 'Enter' }).defaultPrevented);
    assert.strictEqual(slides, 1, 'Enter is the overlay\'s own click, as a mouse click is');

    // The tab-bar arrows are named with a label (the page keeps them aria-hidden).
    assert.strictEqual(window.document.querySelector('button.mat-tab-header-pagination-before').getAttribute('aria-label'), 'Previous tabs');
    assert.strictEqual(window.document.querySelector('button.mat-tab-header-pagination-after').getAttribute('aria-label'), 'Next tabs');
  } finally {
    window.close();
  }
});

// The cards move the way the page moves them: the classes change, and the page adds and removes the
// overlay div itself (the active card has none). The test moves the existing overlay node; the layer never
// does. The buttons must follow, the withdrawn ones must carry none of the layer's attributes, and nothing
// gets a second listener.
for (const how of ['the observer', 'a pass']) {
  test('The_my_flight_buttons_follow_the_cards_when_they_move_by_' + how.replace(' ', '_'), async () => {
    const { window, counts } = await loadCounting('my-flight');
    try {
      const settle = async () => { if (how === 'a pass') window.__msfsbaFenixEfb.pass(); else await turns(); };
      const [flight, route, weather] = cardsOf(window);
      const [flightZone, routeZone, weatherZone] = [flight, route, weather].map(zoneOf);
      const routeOverlay = overlayOf(route);
      const weatherOverlay = overlayOf(weather);
      const retag = (card, from, to) => { card.classList.toggle(from, false); card.classList.toggle(to, true); };

      // Step 1: the classes change; the overlay has not moved yet (Route is active and still has it).
      retag(route, 'right-tab', 'active-tab');
      retag(flight, 'active-tab', 'left-tab');
      retag(weather, 'left-tab', 'right-tab');
      await settle();
      assert.strictEqual(routeZone.getAttribute('role'), 'button');
      assert.strictEqual(routeZone.tabIndex, 0);
      assert.strictEqual(nameOf(routeZone), 'Route' + IMPORT);
      assert.deepStrictEqual(layerStamp(flightZone), NO_STAMP, 'A320 Flight\'s zone is withdrawn');
      assert.deepStrictEqual(layerStamp(weatherZone), NO_STAMP);
      assert.deepStrictEqual(layerStamp(routeOverlay), NO_STAMP, 'the active card\'s overlay is withdrawn');
      assert.strictEqual(nameOf(weatherOverlay), 'Show Weather', 'Weather is still a side card');

      // A key on an element whose stamp was withdrawn does nothing: its listener is still attached.
      let clicks = 0;
      flightZone.click = routeOverlay.click = () => { clicks++; };
      for (const el of [flightZone, routeOverlay]) {
        const event = press(window, el, { key: 'Enter' });
        assert.ok(!event.defaultPrevented, 'nothing is caught on a withdrawn element');
      }
      assert.strictEqual(clicks, 0);

      // Step 2: the page takes the overlay off the active card and puts one on the card that moved aside.
      flight.querySelector('.container').insertBefore(routeOverlay, flight.querySelector('.container').firstChild);
      await settle();
      assert.strictEqual(routeOverlay.getAttribute('role'), 'button');
      assert.strictEqual(routeOverlay.tabIndex, 0);
      assert.strictEqual(nameOf(routeOverlay), 'Show A320 Flight');
      assert.strictEqual(nameOf(weatherOverlay), 'Show Weather');
      assert.ok(press(window, routeOverlay, { key: 'Enter' }).defaultPrevented);
      assert.strictEqual(clicks, 1);
      assert.deepStrictEqual(layerStamp(flightZone), NO_STAMP);

      // Step 3: and back to the start.
      retag(flight, 'left-tab', 'active-tab');
      retag(route, 'active-tab', 'right-tab');
      retag(weather, 'right-tab', 'left-tab');
      route.querySelector('.container').insertBefore(routeOverlay, route.querySelector('.container').firstChild);
      await settle();
      assert.strictEqual(nameOf(flightZone), 'A320 Flight' + IMPORT);
      assert.deepStrictEqual(layerStamp(routeZone), NO_STAMP);
      assert.strictEqual(nameOf(routeOverlay), 'Show Route');

      // No second listener on anything, however often the cards moved.
      assert.ok(counts.size >= 5);
      for (const [el, n] of counts) assert.strictEqual(n, 1, 'one listener on ' + el.localName + '.' + el.className);
    } finally {
      window.close();
    }
  });
}

test('A_page_that_sets_its_own_tab_stop_or_label_on_a_my_flight_element_keeps_it', async () => {
  const window = await load('my-flight', { layer: false });
  try {
    const [flight, route] = cardsOf(window);
    const zone = zoneOf(flight);
    zone.setAttribute('tabindex', '-1');
    zone.setAttribute('aria-label', 'Import from SimBrief');
    const overlay = overlayOf(route);
    overlay.setAttribute('role', 'link');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(zone.getAttribute('aria-label'), 'Import from SimBrief', 'the page\'s label stays');
    assert.strictEqual(overlay.getAttribute('role'), 'link', 'the page\'s role stays');

    // The cards move: the zone is withdrawn, and what is taken back is only what the layer wrote -- the
    // tab stop it gave goes back to the page's own -1, the page's label stays.
    flight.classList.toggle('active-tab', false);
    flight.classList.toggle('left-tab', true);
    await turns();
    assert.strictEqual(zone.getAttribute('tabindex'), '-1');
    assert.strictEqual(zone.getAttribute('aria-label'), 'Import from SimBrief');
    assert.strictEqual(zone.hasAttribute('role'), false, 'the role the layer wrote is taken back');
    assert.strictEqual(overlay.getAttribute('role'), 'link');
  } finally {
    window.close();
  }
});

for (const fixture of TAB_BAR_SCREENS) {
  test('The_tab_bar_is_named_and_the_Panel_States_link_is_a_button_on_' + fixture, async () => {
    const window = await load(fixture);
    try {
      const before = window.document.querySelector('button.mat-tab-header-pagination-before');
      const after = window.document.querySelector('button.mat-tab-header-pagination-after');
      assert.strictEqual(before.getAttribute('aria-label'), 'Previous tabs');
      assert.strictEqual(after.getAttribute('aria-label'), 'Next tabs');
      // Material hides these two arrows from a screen reader (aria-hidden) and the layer never overrides a
      // page's own aria-hidden: they keep it and carry their decision-5 names as a label only. The rest is
      // the page's too: still disabled, still out of the Tab order, no key handler of their own.
      for (const arrow of [before, after]) {
        assert.strictEqual(arrow.getAttribute('aria-hidden'), 'true');
        assert.strictEqual(arrow.disabled, true);
        assert.strictEqual(arrow.getAttribute('tabindex'), '-1');
        assert.strictEqual(arrow.hasAttribute('role'), false);
      }

      // The tab that opens the Panel States pop-up is a link with no href: a button, named by its text.
      const links = all(window, 'a.mat-tab-link');
      assert.deepStrictEqual(links.map(nameOf), ['My Flights', 'Ground Services', 'Mass And Balance', 'Panel States']);
      assert.strictEqual(links.filter(a => !a.hasAttribute('href')).length, 1);
      const panel = links.find(a => !a.hasAttribute('href'));
      assert.strictEqual(panel.textContent.trim(), 'Panel States');
      assert.strictEqual(panel.getAttribute('role'), 'button');
      assert.strictEqual(panel.tabIndex, 0);
      let clicks = 0;
      panel.click = () => { clicks++; };
      assert.ok(press(window, panel, { key: 'Enter' }).defaultPrevented);
      assert.strictEqual(clicks, 1);

      // The links that go somewhere are links already: left exactly as the page has them.
      for (const link of links.filter(a => a.hasAttribute('href'))) {
        assert.strictEqual(link.hasAttribute('role'), false);
        assert.strictEqual(link.hasAttribute('aria-label'), false);
        assert.strictEqual(link.hasAttribute('data-msfsba'), false);
      }
    } finally {
      window.close();
    }
  });
}

test('Panel_states_popup_has_a_title', async () => {
  const window = await load('panel-states');
  try {
    const dialog = window.document.querySelector('mat-dialog-container');
    assert.strictEqual(dialog.getAttribute('role'), 'dialog');
    assert.strictEqual(nameOf(dialog), 'Panel states');
    assert.strictEqual(dialog.getAttribute('aria-modal'), 'true', 'the page\'s own modal flag stays');

    // The four buttons keep their text as their names; the layer leaves them alone.
    const buttons = all(window, 'mat-dialog-container button');
    assert.deepStrictEqual(buttons.map(nameOf), ['COLD & DARK (DEFAULT)', 'TURNAROUND With APU', 'TURNAROUND With PCA', 'READY']);
    for (const button of buttons) {
      assert.strictEqual(button.hasAttribute('aria-label'), false);
      assert.strictEqual(button.hasAttribute('data-msfsba'), false);
    }

    // The Mass and Balance page behind the pop-up is named too.
    assert.strictEqual(nameOf(window.document.querySelector('input.planned-input')), 'Passengers Planned, Max 180');
  } finally {
    window.close();
  }
});

test('A_dialog_that_is_not_the_panel_states_pop_up_is_left_alone', async () => {
  const window = await load('my-flight');
  try {
    const other = window.document.createElement('mat-dialog-container');
    other.setAttribute('role', 'dialog');
    other.innerHTML = '<p>Something else</p>';
    window.document.body.appendChild(other);
    await turns();
    assert.strictEqual(other.hasAttribute('aria-label'), false);
    assert.strictEqual(other.hasAttribute('data-msfsba'), false);
  } finally {
    window.close();
  }
});

test('The_Panel_States_pop_up_is_named_when_it_opens_later', async () => {
  const window = await load('my-flight');
  try {
    // Angular adds the dialog after the page is up; the role is the page's, the name is the layer's.
    const dialog = window.document.createElement('mat-dialog-container');
    dialog.innerHTML = '<app-dialog-panel-states><div><button type="button"> READY </button></div></app-dialog-panel-states>';
    window.document.body.appendChild(dialog);
    await turns();
    assert.strictEqual(dialog.getAttribute('role'), 'dialog', 'a dialog with no role gets one');
    assert.strictEqual(nameOf(dialog), 'Panel states');
  } finally {
    window.close();
  }
});

// The switcher chevron: the page's own icon says whether the switcher is open (up) or closed (down).
test('The_top_bar_chevron_says_whether_the_app_switcher_is_open', async () => {
  const opened = await load('switcher');
  const closed = await load('my-flight');
  try {
    const open = opened.document.querySelector('div.popup-arrow');
    const shut = closed.document.querySelector('div.popup-arrow');
    assert.ok(open.querySelector('i').classList.contains('fa-chevron-up'), 'the switcher capture has the switcher open');
    assert.ok(shut.querySelector('i').classList.contains('fa-chevron-down'), 'the my-flight capture has it closed');
    assert.strictEqual(open.getAttribute('aria-expanded'), 'true');
    assert.strictEqual(shut.getAttribute('aria-expanded'), 'false');

    // The page flips the icon's class as the switcher opens and closes: the state follows.
    const icon = shut.querySelector('i');
    icon.classList.remove('fa-chevron-down');
    icon.classList.add('fa-chevron-up');
    await turns();
    assert.strictEqual(shut.getAttribute('aria-expanded'), 'true');
    icon.classList.remove('fa-chevron-up');
    icon.classList.add('fa-chevron-down');
    await turns();
    assert.strictEqual(shut.getAttribute('aria-expanded'), 'false');

    // An icon the layer does not know says nothing: no state is made up.
    icon.setAttribute('class', 'fas fa-bars');
    await turns();
    assert.strictEqual(shut.hasAttribute('aria-expanded'), false);
  } finally {
    opened.close();
    closed.close();
  }
});

for (const fixture of ['home', 'quick-settings']) {
  test('The_chevron_the_page_hides_has_no_state_on_' + fixture, async () => {
    const window = await load(fixture);
    try {
      assert.strictEqual(window.document.querySelector('div.popup-arrow').hasAttribute('aria-expanded'), false);
    } finally {
      window.close();
    }
  });
}

// The chevron's aria-expanded is the layer's own only when the layer wrote it (the way a label is).
test('A_chevron_state_the_page_set_is_never_touched_and_the_one_the_layer_wrote_is_taken_back_alone', async () => {
  const window = await load('my-flight', { layer: false });
  try {
    const arrow = window.document.querySelector('div.popup-arrow');
    const icon = arrow.querySelector('i');
    // The page set its own state before the layer ran.
    arrow.setAttribute('aria-expanded', 'true');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(arrow.getAttribute('aria-expanded'), 'true', 'the page\'s state stays though the icon says closed');
    icon.classList.toggle('fa-chevron-down', false);
    icon.classList.toggle('fa-chevron-up', true);
    await turns();
    icon.setAttribute('class', 'fas fa-bars');
    await turns();
    assert.strictEqual(arrow.getAttribute('aria-expanded'), 'true', 'the page\'s state stays when the icon is unknown');

    // The page takes its own state away: the layer writes its own from the icon from then on.
    icon.setAttribute('class', 'fas fa-chevron-down');
    arrow.removeAttribute('aria-expanded');
    await turns();
    assert.strictEqual(arrow.getAttribute('aria-expanded'), 'false');

    // The page replaces the layer's state with its own: that one is the page's, not taken back.
    arrow.setAttribute('aria-expanded', 'true');
    icon.setAttribute('class', 'fas fa-bars');
    await turns();
    assert.strictEqual(arrow.getAttribute('aria-expanded'), 'true');

    // The layer's own state goes when the icon becomes unknown.
    arrow.removeAttribute('aria-expanded');
    icon.setAttribute('class', 'fas fa-chevron-up');
    await turns();
    assert.strictEqual(arrow.getAttribute('aria-expanded'), 'true');
    icon.setAttribute('class', 'fas fa-bars');
    await turns();
    assert.strictEqual(arrow.hasAttribute('aria-expanded'), false);
  } finally {
    window.close();
  }
});

test('Loading_notices_are_spoken', async () => {
  const window = await load('notices');
  try {
    const doc = window.document;
    const notices = all(window, 'fap-notification');
    assert.strictEqual(notices.length, 3, 'the loading notice, the bare one and the no-data one');
    for (const notice of notices) assert.strictEqual(notice.getAttribute('aria-live'), 'polite');

    // The progress bar: a role and a range, and the value is the width of its filled span (the page
    // keeps it there; the bar's own value attribute is empty and stays so).
    const bar = doc.querySelector('fap-progress-bar');
    assert.strictEqual(bar.getAttribute('role'), 'progressbar');
    assert.strictEqual(bar.getAttribute('aria-valuemin'), '0');
    assert.strictEqual(bar.getAttribute('aria-valuemax'), '100');
    assert.strictEqual(bar.getAttribute('aria-valuenow'), '40');
    assert.strictEqual(bar.getAttribute('value'), '', 'the layer writes no value');
    assert.strictEqual(all(window, 'fap-progress-bar').length, 1);

    // The page widens the bar; the next pass reports it.
    bar.querySelector('span.bar').style.width = '60%';
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(bar.getAttribute('aria-valuenow'), '60');
    assert.strictEqual(bar.getAttribute('value'), '');
    assert.strictEqual(bar.querySelector('span.bar').style.width, '60%', 'the page\'s width is as the page left it');
  } finally {
    window.close();
  }
});

test('A_notice_that_appears_later_is_spoken_too_and_the_pages_own_settings_stay', async () => {
  const window = await load('notices');
  try {
    const doc = window.document;
    const barMarkup = (attributes, width) => '<fap-progress-bar class="progress-bar" ' + attributes + '>' +
      '<fap-panel><fap-inset class="inset"><span class="bar" style="width: ' + width + '"></span></fap-inset></fap-panel>' +
      '</fap-progress-bar>';
    const late = doc.createElement('fap-notification');
    late.setAttribute('aria-live', 'assertive');
    late.innerHTML = '<div class="notification-content"><p>Later</p>' + barMarkup('', '33.3333333%') + '</div>';
    doc.body.appendChild(late);
    await turns();
    assert.strictEqual(late.getAttribute('aria-live'), 'assertive', 'the page\'s own aria-live is kept');
    const bar = late.querySelector('fap-progress-bar');
    assert.strictEqual(bar.getAttribute('role'), 'progressbar');
    assert.strictEqual(bar.getAttribute('aria-valuenow'), '33.33', 'the number from the width, without float noise');

    // A bar the page gave a role of its own is the page's: nothing is added to it.
    const own = doc.createElement('div');
    own.innerHTML = barMarkup('role="meter" aria-valuenow="7"', '55%');
    doc.body.appendChild(own);
    await turns();
    const meter = own.querySelector('fap-progress-bar');
    assert.strictEqual(meter.getAttribute('role'), 'meter');
    assert.strictEqual(meter.getAttribute('aria-valuenow'), '7');
    assert.strictEqual(meter.hasAttribute('aria-valuemin'), false);
    assert.strictEqual(meter.hasAttribute('data-msfsba'), false);

    // A width that is not a percentage says nothing about progress: the value is not made up.
    const odd = doc.createElement('fap-progress-bar');
    odd.innerHTML = '<span class="bar" style="width: 120px"></span>';
    doc.body.appendChild(odd);
    const none = doc.createElement('fap-progress-bar');
    doc.body.appendChild(none);
    await turns();
    for (const el of [odd, none]) {
      assert.strictEqual(el.getAttribute('role'), 'progressbar');
      assert.strictEqual(el.hasAttribute('aria-valuenow'), false);
    }

    // A bar over 100% is held to the range.
    odd.querySelector('span.bar').setAttribute('style', 'width: 140%');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(odd.getAttribute('aria-valuenow'), '100');
  } finally {
    window.close();
  }
});

// The notice rule and the progress-bar rule are separate, so a throw in one never skips the other.
test('A_failing_notice_rule_does_not_skip_the_progress_bar_rule_and_the_other_way_round', async () => {
  const window = await load('notices');
  try {
    const doc = window.document;
    const notices = all(window, 'fap-notification');
    const bar = doc.querySelector('fap-progress-bar');
    const proto = window.Element.prototype;
    const original = proto.querySelectorAll;
    const failOn = selector => {
      proto.querySelectorAll = function (s) {
        if (s === selector) throw new Error('boom');
        return original.call(this, s);
      };
    };
    try {
      // The bars cannot be found: the notices still become live regions.
      for (const n of notices) n.removeAttribute('aria-live');
      failOn('fap-progress-bar');
      assert.doesNotThrow(() => window.__msfsbaFenixEfb.pass());
      assert.deepStrictEqual(notices.map(n => n.getAttribute('aria-live')), ['polite', 'polite', 'polite']);

      // The notices cannot be found: the bar is still a progress bar.
      for (const attr of ['role', 'aria-valuemin', 'aria-valuemax', 'aria-valuenow']) bar.removeAttribute(attr);
      failOn('fap-notification');
      assert.doesNotThrow(() => window.__msfsbaFenixEfb.pass());
      assert.strictEqual(bar.getAttribute('role'), 'progressbar');
      assert.strictEqual(bar.getAttribute('aria-valuenow'), '40');
    } finally {
      proto.querySelectorAll = original;
    }
  } finally {
    window.close();
  }
});

// A copy of the href-less Panel States tab (the capture's own markup), added to the tab bar before the layer
// runs. Material gives its tab links a tabindex of their own, so the copy has the page's -1 (a tab the
// Tab key skips) unless the test says otherwise; the layer gives it 0 when it is not disabled.
async function loadWithPanelStatesCopy(attributes, text = 'Locked tab') {
  const window = await load('my-flight', { layer: false });
  const real = all(window, 'a.mat-tab-link').find(a => !a.hasAttribute('href'));
  const copy = real.cloneNode(false);
  copy.removeAttribute('id');
  copy.removeAttribute('aria-disabled');
  copy.setAttribute('tabindex', '-1');
  copy.textContent = text;
  for (const [name, value] of Object.entries(attributes)) copy.setAttribute(name, value);
  real.parentElement.appendChild(copy);
  window.eval(fs.readFileSync(LAYER, 'utf8'));
  await turns();
  return { window, copy };
}

function pressesNothing(window, el) {
  let clicks = 0;
  el.click = () => { clicks++; };
  const event = press(window, el, { key: 'Enter' });
  assert.ok(!event.defaultPrevented, 'a disabled control catches no key');
  assert.strictEqual(press(window, el, { key: ' ' }).defaultPrevented, false);
  assert.strictEqual(clicks, 0, 'a disabled control is not pressed');
}

for (const [how, attributes] of [
  ['aria-disabled', { 'aria-disabled': 'true' }],
  ['the disabled attribute', { disabled: '' }],
  ['mat-tab-disabled', { class: 'mat-tab-link mat-focus-indicator app-nav-link mat-tab-disabled', 'aria-disabled': 'false' }]
]) {
  test('A_control_the_page_marks_disabled_gets_no_Tab_stop_and_no_key_by_' + how.replace(/[- ]/g, '_'), async () => {
    const { window, copy } = await loadWithPanelStatesCopy(attributes);
    try {
      assert.strictEqual(copy.getAttribute('role'), 'button', 'still a button, named');
      assert.strictEqual(copy.getAttribute('aria-label'), 'Locked tab');
      assert.strictEqual(copy.getAttribute('tabindex'), '-1', 'the page\'s own -1: no Tab stop given');
      pressesNothing(window, copy);
    } finally {
      window.close();
    }
  });
}

test('A_control_that_becomes_disabled_gets_back_the_tab_stop_it_had_and_gets_it_again_when_enabled', async () => {
  const { window, copy } = await loadWithPanelStatesCopy({});
  try {
    // The page's own -1: the layer gives 0 and puts the -1 back when the control is disabled.
    assert.strictEqual(copy.getAttribute('tabindex'), '0');
    let clicks = 0;
    copy.click = () => { clicks++; };
    assert.ok(press(window, copy, { key: 'Enter' }).defaultPrevented);
    assert.strictEqual(clicks, 1);

    copy.setAttribute('aria-disabled', 'true');
    await turns();
    assert.strictEqual(copy.getAttribute('tabindex'), '-1', 'what it had before');
    assert.strictEqual(copy.getAttribute('role'), 'button');
    pressesNothing(window, copy);

    copy.setAttribute('aria-disabled', 'false');
    await turns();
    assert.strictEqual(copy.getAttribute('tabindex'), '0');
    copy.click = () => { clicks++; };
    assert.ok(press(window, copy, { key: 'Enter' }).defaultPrevented);
    assert.strictEqual(clicks, 2, 'pressable again');

    // The same through the class the page toggles.
    copy.classList.add('mat-tab-disabled');
    await turns();
    assert.strictEqual(copy.getAttribute('tabindex'), '-1');
    pressesNothing(window, copy);
    copy.classList.toggle('mat-tab-disabled', false);
    await turns();
    assert.strictEqual(copy.getAttribute('tabindex'), '0');

    // And through the disabled attribute.
    copy.setAttribute('disabled', '');
    await turns();
    assert.strictEqual(copy.getAttribute('tabindex'), '-1');
    copy.removeAttribute('disabled');
    await turns();
    assert.strictEqual(copy.getAttribute('tabindex'), '0');

    // A tab stop the page changes meanwhile is the page's: not put back over it.
    copy.setAttribute('aria-disabled', 'true');
    await turns();
    copy.setAttribute('tabindex', '5');
    copy.setAttribute('aria-disabled', 'false');
    await turns();
    assert.strictEqual(copy.getAttribute('tabindex'), '5');
  } finally {
    window.close();
  }
});

test('A_control_with_no_tabindex_of_the_pages_gets_none_back_when_it_is_disabled', async () => {
  // The home button is a bare div: no tabindex until the layer gives 0.
  const window = await load('my-flight');
  try {
    const home = window.document.getElementById('homeButtonFree');
    assert.strictEqual(home.getAttribute('tabindex'), '0');
    home.setAttribute('aria-disabled', 'true');
    await turns();
    assert.strictEqual(home.hasAttribute('tabindex'), false, 'the attribute is gone, not set to a made-up value');
    assert.strictEqual(home.getAttribute('role'), 'button');
    pressesNothing(window, home);
    home.removeAttribute('aria-disabled');
    await turns();
    assert.strictEqual(home.getAttribute('tabindex'), '0');
  } finally {
    window.close();
  }
});

test('A_tab_stop_the_page_gave_is_left_alone_when_the_control_becomes_disabled', async () => {
  // The capture's own Panel States tab has tabindex 0 from the page: the layer never wrote it.
  const window = await load('my-flight');
  try {
    const panel = all(window, 'a.mat-tab-link').find(a => !a.hasAttribute('href'));
    assert.strictEqual(panel.getAttribute('tabindex'), '0');
    panel.setAttribute('aria-disabled', 'true');
    await turns();
    assert.strictEqual(panel.getAttribute('tabindex'), '0', 'the page\'s own tab stop');
    pressesNothing(window, panel);
  } finally {
    window.close();
  }
});

// The layer writes attributes only: no node added or removed, no text changed, no value written.
for (const fixture of [...TAB_BAR_SCREENS, 'notices']) {
  test('The_layer_writes_attributes_only_on_' + fixture, async () => {
    const bare = await load(fixture, { layer: false });
    const shape = window => ({
      text: window.document.body.textContent,
      nodes: window.document.body.getElementsByTagName('*').length,
      values: [...window.document.querySelectorAll('input')].map(i => i.value),
      placeholders: [...window.document.querySelectorAll('input')].map(i => i.getAttribute('placeholder'))
    });
    try {
      const expected = shape(bare);
      bare.eval(fs.readFileSync(LAYER, 'utf8'));
      await turns();
      assert.ok(bare.__msfsbaFenixEfb, 'the layer ran');
      assert.deepStrictEqual(shape(bare), expected);
    } finally {
      bare.close();
    }
  });
}

// A second pass finds everything handled already: nothing changes, no element gets a second listener, and
// the elements that took a key handler are exactly the ones that are not real buttons or inputs.
const SECOND_PASS = {
  'switcher': { handled: 11 + 6, listeners: 11 + 4, note: 'nine icons, home, chevron; behind them the three cards, Panel States and the two tab-bar arrows' },
  'my-flight': { handled: 6 + 2, listeners: 6, note: 'home, chevron, three cards, Panel States; the two tab-bar arrows' },
  'ground-services': { handled: 6 + 2 + 14, listeners: 6, note: 'home, chevron, three pushback arrows, Panel States; the two tab-bar arrows; 14 switches' },
  // The fuller capture of 2026-10-22 also holds the app switcher, closed (div.quick-move-buttons, hidden), with its
  // nine icons: they are buttons as on the switcher capture, out of a screen reader's way while the page hides them.
  'mass-balance': {
    handled: 3 + 9 + 2 + 4 + 4, listeners: 3 + 9,
    note: 'home, chevron, Panel States; the nine icons of the closed switcher; the two tab-bar arrows; four boxes; four buttons'
  },
  'panel-states': { handled: 3 + 2 + 4 + 4 + 1, listeners: 3, note: 'as mass-balance, which shows behind the pop-up, plus the pop-up' }
};
for (const [fixture, expected] of Object.entries(SECOND_PASS)) {
  test('Second_pass_changes_nothing_on_' + fixture, async () => {
    const { window, counts } = await loadCounting(fixture);
    try {
      const doc = window.document;
      const before = doc.documentElement.outerHTML;
      const attributes = attributesOf(window);
      window.__msfsbaFenixEfb.pass();
      window.__msfsbaFenixEfb.pass();
      assert.strictEqual(doc.documentElement.outerHTML, before);
      assert.deepStrictEqual(attributesOf(window), attributes);

      const handled = all(window, '[data-msfsba]');
      assert.strictEqual(handled.length, expected.handled, expected.note);
      for (const [el, n] of counts) assert.strictEqual(n, 1, 'one listener on ' + el.localName + '.' + el.className);
      assert.strictEqual(counts.size, expected.listeners, 'only the pressable divs got a key handler');
    } finally {
      window.close();
    }
  });
}
