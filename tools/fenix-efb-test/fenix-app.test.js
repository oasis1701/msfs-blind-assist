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
const { load, loadCounting, press, attributesOf, turns, LAYER } = require('./load');

const nameOf = el => computeAccessibleName(el);
const all = (window, selector) => [...window.document.querySelectorAll(selector)];

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

    // The pushback arrows are bare divs: buttons with the names the owner chose (decision 5).
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

test('My_flight_cards_are_buttons', async () => {
  const window = await load('my-flight');
  try {
    const zones = all(window, 'div.import-sb-zone');
    assert.strictEqual(zones.length, 3);
    // The card title comes from the page's own title row; the zone's own text follows it.
    assert.deepStrictEqual(zones.map(nameOf), [
      'A320 Flight: Tap to import from simbrief',
      'Route: Tap to import from simbrief',
      'Weather: Tap to import from simbrief'
    ]);
    for (const zone of zones) {
      assert.strictEqual(zone.getAttribute('role'), 'button');
      assert.strictEqual(zone.tabIndex, 0);
    }

    let clicks = 0;
    zones[0].click = () => { clicks++; };
    assert.ok(press(window, zones[0], { key: 'Enter' }).defaultPrevented);
    assert.ok(press(window, zones[0], { key: ' ' }).defaultPrevented);
    assert.strictEqual(clicks, 2);

    // The tab-bar arrows.
    assert.strictEqual(nameOf(window.document.querySelector('button.mat-tab-header-pagination-before')), 'Previous tabs');
    assert.strictEqual(nameOf(window.document.querySelector('button.mat-tab-header-pagination-after')), 'Next tabs');
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
      assert.strictEqual(nameOf(before), 'Previous tabs');
      assert.strictEqual(nameOf(after), 'Next tabs');
      // Material hides these two arrows from a screen reader (aria-hidden), and a hidden control has no
      // name: the layer shows them. Everything else about them is the page's: still disabled, still out of
      // the Tab order, no key handler of their own (a real button already answers Enter and Space).
      for (const arrow of [before, after]) {
        assert.notStrictEqual(arrow.getAttribute('aria-hidden'), 'true');
        assert.strictEqual(arrow.disabled, true);
        assert.strictEqual(arrow.getAttribute('tabindex'), '-1');
      }

      // The tab that opens the Panel States pop-up is a link with no href: a button, named by its text.
      const links = all(window, 'a.mat-tab-link');
      assert.deepStrictEqual(links.map(nameOf), ['My Flights', 'Ground Services', 'Mass And Balance', 'Panel States']);
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
  'mass-balance': { handled: 3 + 2 + 4 + 4, listeners: 3, note: 'home, chevron, Panel States; the two tab-bar arrows; four boxes; four buttons' },
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
