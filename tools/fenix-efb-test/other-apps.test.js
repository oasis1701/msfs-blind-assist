'use strict';
// Settings, Failures, Documents, Charts and the quick settings, checked against the LIVE captures
// (fixtures/settings.html, failures.html, documents.html, charts.html, quick-settings.html; Fenix 3.0.0.7438,
// A320-232 IAE). A name is the page's own text wherever the page gives one; where it gives none the name is
// one chosen for the layer (quick settings, Charts, Documents) or the owner's; states
// and failure words follow the page's own classes.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const { computeAccessibleName } = require('dom-accessibility-api');
const { load, loadCounting, press, attributesOf, hiddenByLayer, turns, LAYER } = require('./load');

const nameOf = el => computeAccessibleName(el);
const all = (window, selector) => [...window.document.querySelectorAll(selector)];
const one = (window, selector) => window.document.querySelector(selector);
const attr = name => el => el.getAttribute(name);
const squash = text => text.replace(/\s+/g, ' ').trim();

// What the layer writes on a pressable element; a control it never touched carries none of it.
const layerStamp = el => ({
  role: el.getAttribute('role'),
  tabindex: el.getAttribute('tabindex'),
  label: el.getAttribute('aria-label'),
  mark: el.getAttribute('data-msfsba')
});
const NO_STAMP = { role: null, tabindex: null, label: null, mark: null };

// Presses Enter on a pressable element and reports whether the page's own click ran.
function pressed(window, el, key = 'Enter') {
  let clicks = 0;
  el.click = () => { clicks++; };
  const event = press(window, el, { key });
  return { caught: event.defaultPrevented, clicks };
}

// ---- Settings ----

const SETTING_NAMES = [
  'A/C ESS Auto Switching', 'Air Data Inertial Reference System (ADIRS)', 'AP/FD TCAS', 'Audio Control Panel (ACP)',
  'Auto TOGA', 'Back-Up Speed Scale (BUSS)', 'Barometric Unit', 'Brake Fans'
];

test('Settings_options_say_name_and_choice_and_follow_the_page', async () => {
  const window = await load('settings');
  try {
    // A checkbox is named by the heading of its article, without the "Default" badge in that heading.
    const boxes = all(window, 'label.toggle > input[type=checkbox]');
    assert.deepStrictEqual(boxes.map(nameOf), [
      'A/C ESS Auto Switching', 'AP/FD TCAS', 'Auto TOGA', 'Back-Up Speed Scale (BUSS)', 'Brake Fans'
    ]);
    for (const box of boxes) {
      const heading = box.closest('article').querySelector('.settings-name');
      assert.strictEqual(squash(heading.querySelector('.settings-source').textContent), 'Default', 'the badge is in the heading');
      assert.strictEqual(nameOf(box), squash(heading.firstChild.nodeValue), 'the heading\'s own text, the badge left out');
    }

    // The options say which one is chosen, from the page's own class; the name is the option's own label text, written
    // as a label (Chromium's tree showed them unnamed).
    const options = all(window, 'button.option');
    assert.deepStrictEqual(options.map(nameOf), ['GNADIRS', 'ADIRS', 'Classic', 'Modern', 'hPa', 'inHg']);
    assert.deepStrictEqual(options.map(attr('aria-label')), options.map(o => squash(o.querySelector('.option-label').textContent)));
    assert.deepStrictEqual(options.map(o => o.classList.contains('option--active')), [false, true, false, true, true, false]);
    assert.deepStrictEqual(options.map(attr('aria-pressed')), ['false', 'true', 'false', 'true', 'true', 'false']);
    assert.ok(options.every(o => !o.hasAttribute('role') && !o.hasAttribute('tabindex')), 'real buttons: no role or Tab stop added');

    // The page moves the choice: the state follows after a pass.
    options[1].classList.toggle('option--active', false);
    options[0].classList.toggle('option--active', true);
    await turns();
    assert.deepStrictEqual(options.map(attr('aria-pressed')), ['true', 'false', 'false', 'true', 'true', 'false']);
    options[0].classList.toggle('option--active', false);
    options[5].classList.toggle('option--active', true);
    options[4].classList.toggle('option--active', false);
    window.__msfsbaFenixEfb.pass();
    assert.deepStrictEqual(options.map(attr('aria-pressed')), ['false', 'false', 'false', 'true', 'false', 'true']);

    // The three dots say what they open; the popup state is the page's own.
    const dots = all(window, 'button.overflow-button');
    assert.strictEqual(dots.length, 8);
    for (const dot of dots) assert.strictEqual(dot.querySelectorAll('span.overflow-dot').length, 3, 'the capture\'s three dots');
    assert.deepStrictEqual(dots.map(nameOf), SETTING_NAMES.map(s => 'More options for ' + s));
    assert.ok(dots.every(d => d.getAttribute('aria-haspopup') === 'true' && d.getAttribute('aria-expanded') === 'false'));
    assert.ok(dots.every(d => !d.hasAttribute('role')), 'real buttons already');

    // The categories are buttons with a Tab stop; only the open one is the current page.
    const categories = all(window, 'li.category');
    assert.deepStrictEqual(categories.map(nameOf),
      ['Aircraft', 'Audio', 'Cabin', 'Connections', 'Ground Services', 'Misc', 'Printer', 'Realism']);
    assert.ok(categories.every(c => c.getAttribute('role') === 'button' && c.tabIndex === 0));
    assert.deepStrictEqual(categories.map(c => c.classList.contains('category--active')), [true, false, false, false, false, false, false, false]);
    assert.deepStrictEqual(categories.map(attr('aria-current')), ['page', null, null, null, null, null, null, null]);

    // The page opens another category: aria-current follows, and only one has it.
    categories[0].classList.toggle('category--active', false);
    categories[1].classList.toggle('category--active', true);
    await turns();
    assert.deepStrictEqual(categories.map(attr('aria-current')), [null, 'page', null, null, null, null, null, null]);

    // Enter and Space press a category through the page's own click; a real button keeps its own keys.
    assert.deepStrictEqual(pressed(window, categories[3]), { caught: true, clicks: 1 });
    assert.deepStrictEqual(pressed(window, categories[3], ' '), { caught: true, clicks: 1 });
    assert.deepStrictEqual(pressed(window, options[0]), { caught: false, clicks: 0 });
  } finally {
    window.close();
  }
});

// Each article's choices sit in a container of the page's own; it becomes a group named by the setting, so a
// Tab into "Classic" or "Modern" says which setting they choose. The choices keep their own names and states.
test('Settings_choices_sit_in_a_group_named_by_their_setting', async () => {
  const window = await load('settings');
  try {
    const options = all(window, 'button.option');
    const containers = [...new Set(options.map(o => o.parentElement))];
    assert.strictEqual(containers.length, 3);
    assert.ok(containers.every(c => c.classList.contains('option-grid')), 'the page\'s own container, as captured');
    assert.deepStrictEqual(containers.map(attr('role')), ['group', 'group', 'group']);
    assert.deepStrictEqual(containers.map(nameOf),
      ['Air Data Inertial Reference System (ADIRS)', 'Audio Control Panel (ACP)', 'Barometric Unit']);
    // The same setting name the article's other controls use (here, its "More options for ..." button).
    for (const container of containers) {
      const dots = container.closest('article').querySelector('button.overflow-button');
      assert.strictEqual('More options for ' + nameOf(container), nameOf(dots));
    }
    assert.deepStrictEqual(options.map(nameOf), ['GNADIRS', 'ADIRS', 'Classic', 'Modern', 'hPa', 'inHg']);
    assert.deepStrictEqual(options.map(attr('aria-pressed')), ['false', 'true', 'false', 'true', 'true', 'false']);
    assert.strictEqual(all(window, '[role=group]').length, 3, 'nothing else became a group');

    // The heading's text changes: the group's name follows.
    const acp = containers[1];
    const heading = acp.closest('article').querySelector('.settings-name');
    heading.firstChild.nodeValue = ' Audio panel ';
    await turns();
    assert.strictEqual(nameOf(acp), 'Audio panel');

    // The container stops holding choices: the layer's own role and label go (the test takes the choices out);
    // they come back with the choices.
    const choices = [...acp.querySelectorAll('button.option')];
    for (const choice of choices) acp.removeChild(choice);
    await turns();
    assert.strictEqual(acp.hasAttribute('role'), false);
    assert.strictEqual(acp.hasAttribute('aria-label'), false);
    assert.strictEqual(acp.hasAttribute('data-msfsba'), false);
    for (const choice of choices) acp.appendChild(choice);
    await turns();
    assert.strictEqual(acp.getAttribute('role'), 'group');
    assert.strictEqual(nameOf(acp), 'Audio panel');
  } finally {
    window.close();
  }
});

test('A_role_or_label_the_page_gave_a_settings_choice_container_is_kept', async () => {
  const window = await load('settings', { layer: false });
  try {
    const [adirs, acp] = [...new Set(all(window, 'button.option').map(o => o.parentElement))];
    adirs.setAttribute('role', 'radiogroup');
    acp.setAttribute('aria-label', 'Panel style');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(adirs.getAttribute('role'), 'radiogroup');
    assert.strictEqual(nameOf(adirs), 'Air Data Inertial Reference System (ADIRS)');
    assert.strictEqual(acp.getAttribute('aria-label'), 'Panel style');
    assert.strictEqual(acp.getAttribute('role'), 'group');

    // Withdrawn, the layer takes back only its own: the page's role and label stay.
    for (const container of [adirs, acp]) {
      for (const choice of [...container.querySelectorAll('button.option')]) container.removeChild(choice);
    }
    await turns();
    assert.strictEqual(adirs.getAttribute('role'), 'radiogroup');
    assert.strictEqual(adirs.hasAttribute('aria-label'), false);
    assert.strictEqual(acp.getAttribute('aria-label'), 'Panel style');
    assert.strictEqual(acp.hasAttribute('role'), false);
  } finally {
    window.close();
  }
});

test('Settings_badges_and_the_pages_other_buttons_are_named_by_their_own_text', async () => {
  const window = await load('settings');
  try {
    // The "Default" badge is a text with a Tab stop of the page's own and no role: it is a note named by its text.
    const badges = all(window, 'span.settings-source');
    assert.strictEqual(badges.length, 8);
    assert.deepStrictEqual(badges.map(nameOf), Array(8).fill('Default'));
    assert.ok(badges.every(b => b.getAttribute('role') === 'note' && b.getAttribute('tabindex') === '0'));
    // textOf takes one argument: map passes the index as a second, and it is not a selector to leave out.
    assert.strictEqual(window.__msfsbaFenixEfb.textOf.length, 1);
    assert.deepStrictEqual(badges.map(window.__msfsbaFenixEfb.textOf), Array(8).fill('Default'));

    // The page's own labels and titles are the names; the layer adds none and stamps none.
    const view = all(window, 'button.toggle-button');
    assert.deepStrictEqual(view.map(nameOf), ['Expanded view', 'Compact view']);
    const clear = one(window, 'button.search-clear');
    assert.strictEqual(nameOf(clear), 'Clear search');
    const own = [...view, clear, one(window, 'button.top-bar-settings-btn'), ...all(window, 'button.sidebar-action')];
    assert.strictEqual(own.length, 6);
    for (const el of own) assert.deepStrictEqual(layerStamp(el), { ...NO_STAMP, label: el.getAttribute('aria-label') });
    assert.deepStrictEqual(all(window, 'button.sidebar-action').map(nameOf), ['Restart EFB', 'Sleep EFB']);
    assert.strictEqual(nameOf(one(window, 'app-search-input input')), 'Search settings');
  } finally {
    window.close();
  }
});

// ---- Settings: on/off options and boxes, in every category ----

// The eight Settings captures, one per category, and how many on/off options each holds (as captured).
const SETTINGS_SCREENS = {
  'settings': 5, 'settings-audio': 1, 'settings-cabin': 6, 'settings-connections': 0, 'settings-ground-services': 8,
  'settings-misc': 4, 'settings-printer': 2, 'settings-realism': 3
};
// The setting an article is about, from the page: its heading without the "Default" / "User" badge.
function settingOf(article) {
  const heading = article.querySelector('.settings-name').cloneNode(true);
  heading.querySelectorAll('.settings-source').forEach(badge => badge.remove());
  return squash(heading.textContent);
}
const togglesOf = window => all(window, 'label.toggle').filter(label => label.querySelector('input[type=checkbox]'));

// The page hides an on/off option's real checkbox (display: none) and shows a styled slider, so a screen reader met
// nothing (jsdom has no CSS, so the checkbox looked named here). The label that holds them is the control: a switch
// named by its setting, checked while the checkbox is, with a Tab stop.
for (const [fixture, count] of Object.entries(SETTINGS_SCREENS)) {
  test('Every_on_off_option_is_a_named_switch_on_' + fixture.replace(/-/g, '_'), async () => {
    const window = await load(fixture);
    try {
      const toggles = togglesOf(window);
      assert.strictEqual(toggles.length, count, 'as captured');
      assert.strictEqual(all(window, 'label.toggle').length, count, 'every label.toggle holds a checkbox');
      for (const label of toggles) {
        const setting = settingOf(label.closest('article'));
        const box = label.querySelector('input[type=checkbox]');
        assert.ok(setting, 'the article has a heading');
        assert.strictEqual(label.getAttribute('role'), 'switch', setting);
        assert.strictEqual(nameOf(label), setting);
        assert.strictEqual(label.getAttribute('aria-checked'), box.checked ? 'true' : 'false', setting);
        assert.strictEqual(label.getAttribute('tabindex'), '0', setting);
        // The same setting name the article's three dots use.
        const dots = label.closest('article').querySelector('button.overflow-button');
        assert.strictEqual(nameOf(dots), 'More options for ' + setting);
        // The hidden checkbox keeps its own name and nothing else: it is not a second switch.
        assert.strictEqual(nameOf(box), setting);
        assert.ok(!box.hasAttribute('role') && !box.hasAttribute('tabindex') && !box.hasAttribute('aria-checked'));
      }
      assert.strictEqual(all(window, '[role=switch]').length, count, 'nothing else is a switch');
    } finally {
      window.close();
    }
  });
}

test('A_settings_switch_toggles_with_Enter_and_Space_and_says_its_new_state', async () => {
  const window = await load('settings');
  try {
    const [label] = togglesOf(window);
    const box = label.querySelector('input');
    assert.strictEqual(box.checked, false, 'as captured');

    // Enter and Space are the label's own click.
    assert.deepStrictEqual(pressed(window, label), { caught: true, clicks: 1 });
    assert.deepStrictEqual(pressed(window, label, ' '), { caught: true, clicks: 1 });
    delete label.click;

    // That click toggles the hidden checkbox, as a mouse click on the slider does. The checked state is a property
    // (no attribute or class changes, so the observer sees nothing): the change event brings the switch up to date.
    let changes = 0;
    let stopped = false;
    box.addEventListener('change', e => { changes++; stopped = stopped || e.defaultPrevented; });
    assert.ok(press(window, label, { key: 'Enter' }).defaultPrevented);
    assert.strictEqual(box.checked, true);
    await turns();
    assert.strictEqual(label.getAttribute('aria-checked'), 'true');
    assert.ok(press(window, label, { key: ' ' }).defaultPrevented);
    assert.strictEqual(box.checked, false);
    await turns();
    assert.strictEqual(label.getAttribute('aria-checked'), 'false');
    // The page's own change listener still hears every change, unprevented.
    assert.strictEqual(changes, 2);
    assert.strictEqual(stopped, false);

    // The page sets the state itself and says so with a change event: the switch follows.
    box.checked = true;
    box.dispatchEvent(new window.Event('change', { bubbles: true }));
    await turns();
    assert.strictEqual(label.getAttribute('aria-checked'), 'true');
  } finally {
    window.close();
  }
});

// A switch whose checkbox the page disables is unavailable: it says so (aria-disabled, the layer's own, review minor 3),
// has no Tab stop and takes no key; all of it comes back when the checkbox is enabled.
test('A_settings_switch_whose_checkbox_the_page_disables_says_so_has_no_Tab_stop_and_takes_no_key', async () => {
  const window = await load('settings');
  try {
    const [label] = togglesOf(window);
    const box = label.querySelector('input');
    assert.strictEqual(label.hasAttribute('aria-disabled'), false, 'enabled, as captured');
    box.setAttribute('disabled', '');
    await turns();
    assert.strictEqual(label.getAttribute('aria-disabled'), 'true');
    assert.strictEqual(label.hasAttribute('tabindex'), false, 'no Tab stop: none was there before the layer');
    assert.strictEqual(label.getAttribute('role'), 'switch', 'still read');
    assert.deepStrictEqual(pressed(window, label), { caught: false, clicks: 0 });

    // The checkbox is enabled again. Before the next pass the switch still carries the layer's aria-disabled, which is
    // not the page's: the switch is not unavailable, and a key presses it.
    box.removeAttribute('disabled');
    assert.strictEqual(label.getAttribute('aria-disabled'), 'true', 'no pass yet');
    assert.deepStrictEqual(pressed(window, label), { caught: true, clicks: 1 });
    await turns();
    assert.strictEqual(label.hasAttribute('aria-disabled'), false, 'the layer\'s own is taken back');
    assert.strictEqual(label.getAttribute('tabindex'), '0');
    assert.deepStrictEqual(pressed(window, label), { caught: true, clicks: 1 });
  } finally {
    window.close();
  }
});

test('An_unavailable_state_the_page_gives_a_settings_switch_is_the_pages', async () => {
  const window = await load('settings', { layer: false });
  try {
    const [first, second] = togglesOf(window);
    first.setAttribute('aria-disabled', 'true');    // the page says the switch is unavailable
    second.setAttribute('aria-disabled', 'false');  // the page says it is available, though its checkbox is not
    second.querySelector('input').setAttribute('disabled', '');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(first.getAttribute('aria-disabled'), 'true');
    assert.strictEqual(first.hasAttribute('tabindex'), false, 'the page\'s own unavailable state: no Tab stop');
    assert.deepStrictEqual(pressed(window, first), { caught: false, clicks: 0 });
    assert.strictEqual(second.getAttribute('aria-disabled'), 'false', 'the page\'s own, not overridden');
    assert.strictEqual(second.hasAttribute('tabindex'), false, 'its checkbox is disabled: no Tab stop');
    // The checkbox is enabled: the page's own states stay as the page set them.
    second.querySelector('input').removeAttribute('disabled');
    await turns();
    assert.strictEqual(second.getAttribute('aria-disabled'), 'false');
    assert.strictEqual(second.getAttribute('tabindex'), '0');
    assert.strictEqual(first.getAttribute('aria-disabled'), 'true');
  } finally {
    window.close();
  }
});

// Review minor 4: the switch rule is scoped to the articles, like its sibling rules; a toggle elsewhere in Settings is
// not a switch of the layer's.
test('A_toggle_outside_a_settings_article_is_not_made_a_switch', async () => {
  const window = await load('settings');
  try {
    const stray = window.document.createElement('label');
    stray.className = 'toggle';
    stray.innerHTML = '<input type="checkbox"><span class="toggle-slider"></span>';
    window.document.querySelector('app-settings2 .modern-app-content-toolbar').appendChild(stray);
    await turns();
    assert.deepStrictEqual(layerStamp(stray), NO_STAMP);
    assert.strictEqual(stray.hasAttribute('aria-checked'), false);
    assert.strictEqual(all(window, '[role=switch]').length, 5, 'the five in the articles');
  } finally {
    window.close();
  }
});

test('A_role_name_or_state_the_page_gave_a_settings_switch_is_kept_and_the_layers_own_goes_with_the_checkbox', async () => {
  const window = await load('settings', { layer: false });
  try {
    const [first, second, third, fourth] = togglesOf(window);
    first.setAttribute('role', 'checkbox');
    second.setAttribute('aria-label', 'Autopilot TCAS');
    third.setAttribute('aria-checked', 'mixed');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(first.getAttribute('role'), 'checkbox');
    assert.strictEqual(second.getAttribute('aria-label'), 'Autopilot TCAS');
    assert.strictEqual(second.getAttribute('role'), 'switch');
    assert.strictEqual(third.getAttribute('aria-checked'), 'mixed');

    // The checkbox goes from a label (the test takes it out): the label is not a switch any more; what the layer wrote
    // is taken back, the page's own stays. It comes back with the checkbox.
    second.removeChild(second.querySelector('input'));
    const fourthBox = fourth.querySelector('input');
    fourth.removeChild(fourthBox);
    await turns();
    assert.strictEqual(second.hasAttribute('role'), false);
    assert.strictEqual(second.getAttribute('aria-label'), 'Autopilot TCAS');
    assert.deepStrictEqual(layerStamp(fourth), NO_STAMP);
    assert.strictEqual(fourth.hasAttribute('aria-checked'), false);
    fourth.insertBefore(fourthBox, fourth.firstChild);
    await turns();
    assert.strictEqual(fourth.getAttribute('role'), 'switch');
    assert.strictEqual(fourth.getAttribute('aria-checked'), 'false');
  } finally {
    window.close();
  }
});

// ---- Settings: the "More options" menus ----
// The three dots open a CDK overlay at the end of <body>: a transparent backdrop, then a pane holding div.overflow-menu
// and its items. The focus stayed on the button, so the arrows and Tab moved on to the next setting and the menu seemed
// empty. While a menu is open its button's container aria-owns the pane (the items are read right after the button),
// the focus goes to the first item that is not disabled, and the backdrop is the button "Close menu"; when the menu
// goes and the focus is lost, the focus goes back to the button.

const MENU_ITEMS = ['Set as user override', 'Reset to default'];

// The overlay of the open capture (backdrop, bounding box, pane, menu), to put into the closed capture as the page puts
// it there when it opens the menu: the test adds and removes the nodes, the layer never does.
async function openMenuMarkup(change = html => html) {
  const open = await load('settings-overflow-open', { layer: false });
  try {
    return change(open.document.querySelector('div.app-overlay-container').innerHTML);
  } finally {
    open.close();
  }
}

// Opens the first "More options" menu of the closed settings capture, as the page does, with its button focused first.
async function openFirstMenu(window, markup) {
  const origin = one(window, 'button.overflow-button');
  origin.focus();
  origin.setAttribute('aria-expanded', 'true');
  one(window, 'div.app-overlay-container').innerHTML = markup;
  await turns();
  return origin;
}

test('An_open_More_options_menu_is_read_right_after_its_button', async () => {
  const window = await load('settings-overflow-open');
  try {
    const origins = all(window, 'button.overflow-button[aria-expanded="true"]');
    assert.strictEqual(origins.length, 1, 'as captured');
    const container = origins[0].closest('div.settings-overflow');
    const pane = one(window, '.cdk-overlay-pane');
    assert.strictEqual(pane.id, 'cdk-overlay-0', 'the page\'s own id');
    assert.strictEqual(container.getAttribute('aria-owns'), 'cdk-overlay-0', 'the pane is read right after the button');
    assert.strictEqual(all(window, '[id^="msfsba-"]').length, 0, 'the page\'s id is used: none of the layer\'s');
    assert.ok(['role', 'aria-label', 'tabindex', 'data-msfsba'].every(a => !pane.hasAttribute(a)), 'the pane gets no role');
    const items = all(window, 'button.overflow-menu__item');
    assert.deepStrictEqual(items.map(nameOf), MENU_ITEMS, 'the items keep their own text as their names');
    assert.ok(items.every(i => !i.hasAttribute('aria-label')));
    assert.strictEqual(nameOf(origins[0]), 'More options for A/C ESS Auto Switching');

    // The backdrop is the menu's "Close menu" button: Enter and Space are its own click, as a mouse click outside is.
    const backdrop = one(window, '.cdk-overlay-backdrop');
    assert.strictEqual(backdrop.getAttribute('role'), 'button');
    assert.strictEqual(backdrop.tabIndex, 0);
    assert.strictEqual(nameOf(backdrop), 'Close menu');
    assert.deepStrictEqual(pressed(window, backdrop), { caught: true, clicks: 1 });
    assert.deepStrictEqual(pressed(window, backdrop, ' '), { caught: true, clicks: 1 });

    // The menu was open when the layer first saw it: the focus is on its first item.
    assert.strictEqual(window.document.activeElement, items[0]);
  } finally {
    window.close();
  }
});

test('Opening_a_More_options_menu_takes_the_focus_to_its_first_item_and_closing_it_brings_it_back', async () => {
  const markup = await openMenuMarkup();
  const window = await load('settings');
  try {
    const doc = window.document;
    const overlay = one(window, 'div.app-overlay-container');
    const origin = await openFirstMenu(window, markup);
    const container = origin.closest('div.settings-overflow');
    const items = [...overlay.querySelectorAll('button.overflow-menu__item')];
    const backdrop = overlay.querySelector('.cdk-overlay-backdrop');
    assert.strictEqual(doc.activeElement, items[0], 'the focus is on the first item');
    assert.strictEqual(container.getAttribute('aria-owns'), 'cdk-overlay-0');
    assert.strictEqual(nameOf(backdrop), 'Close menu');

    // The user moves within the menu: a later pass does not take the focus back to the first item.
    items[1].focus();
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(doc.activeElement, items[1]);

    // The page closes the menu (an item was chosen): the overlay goes and the button is collapsed. The focus, lost with
    // the item, goes back to the button; what the layer wrote for the menu is taken back.
    origin.setAttribute('aria-expanded', 'false');
    overlay.innerHTML = '';
    assert.strictEqual(doc.activeElement, doc.body, 'the focus is lost with the menu');
    await turns();
    assert.strictEqual(doc.activeElement, origin, 'and comes back to the button');
    assert.strictEqual(container.hasAttribute('aria-owns'), false);
    assert.deepStrictEqual(layerStamp(backdrop), NO_STAMP);
    assert.strictEqual(all(window, '[id^="msfsba-"]').length, 0, 'no id is left behind');

    // It opens again: the focus goes in again.
    await openFirstMenu(window, markup);
    assert.strictEqual(doc.activeElement, overlay.querySelector('button.overflow-menu__item'));
  } finally {
    window.close();
  }
});

// Shift+Tab from the first item reaches the "Close menu" backdrop, and Enter there closes the menu. CDK takes the
// pane away at once but keeps the backdrop in the page for its fade (up to 500 ms), with the focus still on it: that
// focus is lost with the menu too, and goes back to the button. So does the focus when the backdrop goes as well.
for (const backdropLingers of [true, false]) {
  test('Closing_a_menu_from_its_Close_menu_backdrop_brings_the_focus_back_to_its_button_' +
    (backdropLingers ? 'while_the_backdrop_lingers' : 'when_the_backdrop_goes_too'), async () => {
    const markup = await openMenuMarkup();
    const window = await load('settings');
    try {
      const doc = window.document;
      const origin = await openFirstMenu(window, markup);
      const overlay = one(window, 'div.app-overlay-container');
      const backdrop = overlay.querySelector('.cdk-overlay-backdrop');
      assert.strictEqual(nameOf(backdrop), 'Close menu');
      backdrop.focus();
      assert.strictEqual(doc.activeElement, backdrop, 'the focus is on "Close menu"');

      // The page closes the menu: the button is collapsed, the pane (in its bounding box) goes at once.
      origin.setAttribute('aria-expanded', 'false');
      overlay.removeChild(overlay.querySelector('.cdk-overlay-connected-position-bounding-box'));
      if (!backdropLingers) overlay.removeChild(backdrop);
      assert.strictEqual(backdrop.isConnected, backdropLingers, 'as the test set it up');
      await turns();
      assert.strictEqual(doc.activeElement, origin, 'the focus is back on the button');
      assert.deepStrictEqual(layerStamp(backdrop), NO_STAMP, 'the backdrop is no button of the layer\'s any more');
      assert.strictEqual(origin.closest('div.settings-overflow').hasAttribute('aria-owns'), false);
    } finally {
      window.close();
    }
  });
}

test('A_menu_that_closes_while_the_focus_is_elsewhere_leaves_the_focus_there', async () => {
  const markup = await openMenuMarkup();
  const window = await load('settings');
  try {
    const doc = window.document;
    const origin = await openFirstMenu(window, markup);
    // The user goes on to a category with the menu still open; then the page closes the menu.
    const category = all(window, 'li.category')[1];
    category.focus();
    origin.setAttribute('aria-expanded', 'false');
    one(window, 'div.app-overlay-container').innerHTML = '';
    await turns();
    assert.strictEqual(doc.activeElement, category, 'the focus is not lost: it stays where the user put it');
  } finally {
    window.close();
  }
});

test('A_menu_pane_without_an_id_of_its_own_gets_one_of_the_layers_and_it_goes_with_the_menu', async () => {
  const markup = await openMenuMarkup(html => html.replace(' id="cdk-overlay-0"', ''));
  const window = await load('settings');
  try {
    const origin = await openFirstMenu(window, markup);
    const pane = one(window, '.cdk-overlay-pane');
    assert.ok(/^msfsba-\d+$/.test(pane.id), pane.id);
    assert.strictEqual(origin.closest('div.settings-overflow').getAttribute('aria-owns'), pane.id);
    origin.setAttribute('aria-expanded', 'false');
    one(window, 'div.app-overlay-container').innerHTML = '';
    await turns();
    assert.strictEqual(pane.hasAttribute('id'), false, 'the layer\'s id is taken back');
    assert.strictEqual(origin.closest('div.settings-overflow').hasAttribute('aria-owns'), false);
  } finally {
    window.close();
  }
});

test('A_disabled_first_menu_item_is_passed_over', async () => {
  const markup = await openMenuMarkup(html => html.replace('class="overflow-menu__item"', 'class="overflow-menu__item" disabled=""'));
  const window = await load('settings');
  try {
    await openFirstMenu(window, markup);
    const items = all(window, 'button.overflow-menu__item');
    assert.strictEqual(items[0].disabled, true, 'the first item is disabled');
    assert.strictEqual(window.document.activeElement, items[1]);
  } finally {
    window.close();
  }
});

test('No_menu_is_followed_while_no_button_or_more_than_one_says_it_is_open', async () => {
  const markup = await openMenuMarkup();
  const window = await load('settings');
  try {
    const doc = window.document;
    const [first, second] = all(window, 'button.overflow-button');
    first.focus();
    // The overlay appears but no button says it is expanded: the layer does nothing.
    one(window, 'div.app-overlay-container').innerHTML = markup;
    await turns();
    const backdrop = one(window, '.cdk-overlay-backdrop');
    assert.strictEqual(all(window, 'div.settings-overflow[aria-owns]').length, 0);
    assert.deepStrictEqual(layerStamp(backdrop), NO_STAMP);
    assert.strictEqual(doc.activeElement, first);
    // Two say so: still nothing.
    first.setAttribute('aria-expanded', 'true');
    second.setAttribute('aria-expanded', 'true');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(all(window, 'div.settings-overflow[aria-owns]').length, 0);
    assert.deepStrictEqual(layerStamp(backdrop), NO_STAMP);
    assert.strictEqual(doc.activeElement, first);
    // One does: the menu is followed.
    second.setAttribute('aria-expanded', 'false');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(first.closest('div.settings-overflow').getAttribute('aria-owns'), 'cdk-overlay-0');
    assert.strictEqual(doc.activeElement, one(window, 'button.overflow-menu__item'));
  } finally {
    window.close();
  }
});

test('The_Panel_States_dialog_backdrop_is_not_a_menu', async () => {
  const window = await load('panel-states');
  try {
    const backdrop = one(window, '.cdk-overlay-backdrop');
    assert.ok(backdrop, 'the capture has the dialog\'s backdrop');
    assert.deepStrictEqual(layerStamp(backdrop), NO_STAMP);
  } finally {
    window.close();
  }
});

// Settings' text and password boxes (and Misc's picture upload box) have no label: their placeholder "Enter value" says
// nothing of the setting. Each takes its article's setting name, as the switch and the three dots do.
const SETTINGS_BOXES = {
  'settings-audio': ['ACP Volume: CAB', 'ACP Volume: HF1', 'ACP Volume: HF2', 'ACP Volume: INT', 'ACP Volume: VHF1',
    'ACP Volume: VHF2', 'ACP Volume: VHF3'],
  'settings-connections': ['Hoppie Logon Code', 'Pushover: Application Key', 'Pushover: Token', 'SimBrief Username / ID'],
  'settings-misc': ['Background Image'],
  'settings-printer': null
};
for (const [fixture, expected] of Object.entries(SETTINGS_BOXES)) {
  test('Settings_boxes_are_named_by_their_setting_on_' + fixture.replace(/-/g, '_'), async () => {
    const bare = await load(fixture, { layer: false });
    const window = await load(fixture);
    try {
      const boxes = all(window, 'app-settings2 article input:not([type=checkbox])');
      assert.ok(boxes.length > 0, 'the capture has boxes');
      const names = boxes.map(nameOf);
      assert.deepStrictEqual(names, boxes.map(box => settingOf(box.closest('article'))));
      if (expected) assert.deepStrictEqual(names, expected);
      // The page's own type, placeholder and Tab stop stay; nothing but a name is added.
      const keep = el => ['type', 'placeholder', 'tabindex', 'role', 'class'].map(a => a + '=' + el.getAttribute(a)).join('|');
      assert.deepStrictEqual(boxes.map(keep), all(bare, 'app-settings2 article input:not([type=checkbox])').map(keep));
      // The search box keeps the page's own name.
      assert.strictEqual(nameOf(one(window, 'app-search-input input')), 'Search settings');
    } finally {
      bare.close();
      window.close();
    }
  });
}

// An icon font draws its glyph from a style sheet as a private-use character, and Chromium makes that character the
// name of a button that has only a title ("Expanded view") or reads it as a stray line before the text. So a glyph
// inside a control is hidden from a screen reader; the control keeps its own name (its text, title or label).
test('Icon_glyphs_inside_a_control_are_hidden_and_the_control_keeps_its_own_name', async () => {
  const bare = await load('settings', { layer: false });
  const window = await load('settings');
  try {
    assert.deepStrictEqual(hiddenByLayer(bare, window), [
      'i.fas.fa-chevron-down', 'i.fas.fa-cog', 'i.fas.fa-solid.fa-power-off', 'i.fas.fa-solid.fa-moon',
      'i.fas.fa-solid.fa-images', 'i.fas.fa-solid.fa-list-ul'
    ], 'the chevron\'s, the quick settings cog\'s, the two sidebar actions\' and the two view buttons\'');
    const view = all(window, 'button.toggle-button');
    assert.deepStrictEqual(view.map(nameOf), ['Expanded view', 'Compact view'], 'named by their titles');
    assert.ok(view.every(b => b.querySelector('i').getAttribute('aria-hidden') === 'true'));
    assert.deepStrictEqual(all(window, 'button.sidebar-action').map(nameOf), ['Restart EFB', 'Sleep EFB']);
    // The glyph is hidden, nothing else: no role, label or stamp on it.
    for (const icon of all(window, 'button i')) {
      assert.ok(['role', 'aria-label', 'tabindex', 'data-msfsba'].every(a => !icon.hasAttribute(a)));
    }
  } finally {
    bare.close();
    window.close();
  }
});

test('An_icon_that_is_itself_the_control_is_left_alone_and_one_outside_a_control_too', async () => {
  const bare = await load('charts', { layer: false });
  const window = await load('charts');
  try {
    // The zoom icons are the buttons themselves: not hidden.
    for (const id of ['mapZoomInBtn', 'mapZoomOutBtn']) {
      const icon = window.document.getElementById(id);
      assert.strictEqual(icon.getAttribute('role'), 'button');
      assert.strictEqual(icon.hasAttribute('aria-hidden'), false, id);
    }
    assert.deepStrictEqual(all(window, 'div.map-control-item').map(nameOf), ['Map settings', 'World Map', 'Dark map', 'Light map']);
    assert.deepStrictEqual(hiddenByLayer(bare, window), [
      'i.fas.fa-chevron-down', 'i.fas.fa-cog', 'i.fa-download.fas', 'i.fas.fa-cog', 'i.fas.fa-moon', 'i.fas.fa-sun'
    ], 'the chevron\'s, the quick settings cog\'s, Sync SimBrief\'s and the three map controls\'');
  } finally {
    bare.close();
    window.close();
  }
});

test('A_glyph_follows_its_control_and_a_hidden_state_the_page_set_on_a_glyph_is_kept', async () => {
  const window = await load('settings');
  try {
    const doc = window.document;
    const holder = doc.createElement('div');
    holder.innerHTML =
      '<div class="page-control" role="button"><i class="fas fa-star"></i></div>' +
      '<button class="own"><i class="fas fa-star" aria-hidden="false"></i></button>' +
      '<button class="holds-a-control"><i class="fas fa-plus" role="button"></i></button>' +
      '<a href="#x"><i class="far fa-file"></i> File</a>' +
      '<div class="plain"><i class="fas fa-star"></i></div>';
    doc.querySelector('app-settings2').appendChild(holder);
    await turns();
    const [inRole, own, self, inLink, plain] = [...holder.querySelectorAll('i')];
    assert.strictEqual(inRole.getAttribute('aria-hidden'), 'true', 'inside a role=button of the page\'s');
    assert.strictEqual(own.getAttribute('aria-hidden'), 'false', 'the page\'s own');
    assert.strictEqual(self.hasAttribute('aria-hidden'), false, 'the icon is itself a control');
    assert.strictEqual(inLink.getAttribute('aria-hidden'), 'true', 'inside a link');
    assert.strictEqual(plain.hasAttribute('aria-hidden'), false, 'inside no control');

    // The control goes (the page takes its role away; the observer does not watch roles, so the pass is run by hand):
    // the glyph is read again. It comes back with the role.
    inRole.parentElement.removeAttribute('role');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(inRole.hasAttribute('aria-hidden'), false);
    inRole.parentElement.setAttribute('role', 'button');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(inRole.getAttribute('aria-hidden'), 'true');
  } finally {
    window.close();
  }
});

// A Settings choice can show a picture whose alt repeats the choice's own text ("GNADIRS"), so a screen reader read
// each choice twice. The settings capture has the choices without their pictures (the old snippet removed every <img>),
// so they are put back here as the live page has them, as the button's first child.
// Loads the settings capture with each choice's picture put back, as the live page has it (the button's first child, its
// alt the choice's own text), and a choice that is only a picture. The layer is not run yet.
async function loadSettingsWithPictures() {
  const window = await load('settings', { layer: false });
  const doc = window.document;
  const options = all(window, 'button.option');
  for (const option of options) {
    const img = doc.createElement('img');
    img.className = 'option-image';
    img.setAttribute('src', '/assets/img/settings/option.png');
    img.setAttribute('alt', squash(option.textContent));
    option.insertBefore(img, option.firstChild);
  }
  const lone = doc.createElement('button');
  lone.className = 'option';
  lone.setAttribute('type', 'button');
  lone.innerHTML = '<img class="option-image" src="/assets/img/settings/only.png" alt="Picture only">';
  options[0].parentElement.appendChild(lone);
  return { window, options, lone };
}

test('Option_pictures_that_repeat_the_options_text_are_hidden', async () => {
  const { window, options, lone } = await loadSettingsWithPictures();
  try {
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();

    const pictures = options.map(o => o.querySelector('img.option-image'));
    assert.ok(pictures.every(p => p.getAttribute('aria-hidden') === 'true'));
    assert.deepStrictEqual(options.map(nameOf), ['GNADIRS', 'ADIRS', 'Classic', 'Modern', 'hPa', 'inHg'], 'each read once');
    // A choice that is only a picture: its picture stays, and its alt is the choice's name.
    assert.strictEqual(lone.querySelector('img').hasAttribute('aria-hidden'), false);
    assert.strictEqual(lone.getAttribute('aria-label'), 'Picture only');
    assert.strictEqual(nameOf(lone), 'Picture only');

    // The choice's text goes: its picture is read again, and its alt is the name.
    const label = options[2].querySelector('span.option-label');
    label.firstChild.nodeValue = '';
    await turns();
    assert.strictEqual(pictures[2].hasAttribute('aria-hidden'), false);
    assert.strictEqual(options[2].getAttribute('aria-label'), 'Classic');
    assert.strictEqual(nameOf(options[2]), 'Classic');
  } finally {
    window.close();
  }
});

// A choice is named by its own label text as a label of the layer's, which follows the text; a label the page
// gives is the page's.
test('A_settings_choice_is_named_by_its_label_text_and_a_label_the_page_gives_it_is_kept', async () => {
  const window = await load('settings', { layer: false });
  try {
    const [gnadirs, adirs, classic] = all(window, 'button.option');
    adirs.setAttribute('aria-label', 'Modern ADIRS');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(gnadirs.getAttribute('aria-label'), 'GNADIRS');
    assert.strictEqual(gnadirs.getAttribute('data-msfsba'), '1');
    assert.strictEqual(adirs.getAttribute('aria-label'), 'Modern ADIRS', 'the page\'s own');
    // Angular updates the label's text in place: the name follows.
    classic.querySelector('.option-label').firstChild.nodeValue = ' Classic panel ';
    await turns();
    assert.strictEqual(nameOf(classic), 'Classic panel');
  } finally {
    window.close();
  }
});

// The choice-picture rule is its own: when it fails, the choices are still named and pressed, and the other rules ran.
test('A_failing_rule_does_not_skip_the_others_on_settings_when_img_option_image_fails', async () => {
  const { window, options } = await loadSettingsWithPictures();
  const proto = window.Element.prototype;
  const original = proto.querySelectorAll;
  try {
    proto.querySelectorAll = function (s) {
      if (this === window.document.body && s.includes('img.option-image')) throw new Error('boom');
      return original.call(this, s);
    };
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    proto.querySelectorAll = original;
    assert.ok(options.every(o => !o.querySelector('img').hasAttribute('aria-hidden')), 'the failing rule did nothing');
    assert.deepStrictEqual(options.map(attr('aria-label')), ['GNADIRS', 'ADIRS', 'Classic', 'Modern', 'hPa', 'inHg']);
    assert.strictEqual(all(window, 'button.option[aria-pressed]').length, 7);
    assert.strictEqual(all(window, 'label.toggle[role=switch]').length, 5);
  } finally {
    proto.querySelectorAll = original;
    window.close();
  }
});

test('Settings_names_follow_the_pages_heading', async () => {
  const window = await load('settings');
  try {
    const [first] = all(window, 'article');
    const box = first.querySelector('input[type=checkbox]');
    const dots = first.querySelector('button.overflow-button');
    const heading = first.querySelector('.settings-name');
    assert.strictEqual(nameOf(box), 'A/C ESS Auto Switching');

    // Angular updates the heading's text in place; the badge changes to "Livery" and is still not part of the name.
    heading.firstChild.nodeValue = ' A/C Essential Bus ';
    heading.querySelector('.settings-source').firstChild.nodeValue = ' Livery ';
    await turns();
    assert.strictEqual(nameOf(box), 'A/C Essential Bus');
    assert.strictEqual(nameOf(dots), 'More options for A/C Essential Bus');
    assert.strictEqual(nameOf(heading.querySelector('.settings-source')), 'Livery');

    // The heading goes: no stale name is left (a name from nothing beats a wrong one).
    const parent = heading.parentElement;
    const next = heading.nextSibling;
    parent.removeChild(heading);
    await turns();
    assert.strictEqual(box.hasAttribute('aria-label'), false);
    assert.strictEqual(dots.hasAttribute('aria-label'), false);
    parent.insertBefore(heading, next);
    await turns();
    assert.strictEqual(nameOf(box), 'A/C Essential Bus');
    assert.strictEqual(nameOf(dots), 'More options for A/C Essential Bus');
  } finally {
    window.close();
  }
});

test('A_state_or_name_the_page_set_on_a_settings_control_is_kept_and_the_layers_own_is_taken_back_alone', async () => {
  const window = await load('settings', { layer: false });
  try {
    const [box] = all(window, 'label.toggle > input[type=checkbox]');
    const [gnadirs, adirs] = all(window, 'button.option');
    const [aircraft, audio] = all(window, 'li.category');
    const [dots] = all(window, 'button.overflow-button');
    const [badge] = all(window, 'span.settings-source');
    box.setAttribute('aria-label', 'Essential bus');
    gnadirs.setAttribute('aria-pressed', 'mixed');
    aircraft.setAttribute('aria-current', 'true');
    audio.setAttribute('role', 'tab');
    dots.setAttribute('aria-label', 'Menu');
    badge.setAttribute('role', 'status');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();

    assert.strictEqual(box.getAttribute('aria-label'), 'Essential bus');
    assert.strictEqual(dots.getAttribute('aria-label'), 'Menu');
    assert.strictEqual(gnadirs.getAttribute('aria-pressed'), 'mixed', 'the page\'s state stays though the class says not chosen');
    assert.strictEqual(adirs.getAttribute('aria-pressed'), 'true', 'the others are as usual');
    assert.strictEqual(aircraft.getAttribute('aria-current'), 'true', 'the page\'s own value stays');
    assert.strictEqual(audio.getAttribute('role'), 'tab', 'the page\'s role stays');
    assert.strictEqual(badge.getAttribute('role'), 'status');

    // The page takes its own state away: the layer writes its own from the class from then on (the observer
    // does not watch these two attributes, so the pass is run by hand).
    gnadirs.removeAttribute('aria-pressed');
    aircraft.removeAttribute('aria-current');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(gnadirs.getAttribute('aria-pressed'), 'false');
    assert.strictEqual(aircraft.getAttribute('aria-current'), 'page');

    // And the layer's own is taken back when the class stops saying it; the page's replacement is left alone.
    aircraft.classList.toggle('category--active', false);
    await turns();
    assert.strictEqual(aircraft.hasAttribute('aria-current'), false);
    aircraft.classList.toggle('category--active', true);
    await turns();
    aircraft.setAttribute('aria-current', 'step');
    aircraft.classList.toggle('category--active', false);
    await turns();
    assert.strictEqual(aircraft.getAttribute('aria-current'), 'step');
  } finally {
    window.close();
  }
});

// ---- Failures ----

const CHAPTERS = [
  'ATA 21 - Air Conditioning', 'ATA 22 - AutoFlight', 'ATA 24 - Electrical power', 'ATA 26 - Fire protection',
  'ATA 28 - Fuel', 'ATA 29 - Hydraulic power', 'ATA 32 - Landing gear', 'ATA 34 - Navigation',
  'ATA 36 - Pneumatic', 'ATA 38 - Water and waste systems', 'ATA 49 - APU', 'ATA 23 - Communications',
  'ATA 30 - Ice and rain protection', 'ATA 27 - Flight controls', 'ATA 80 - Start faults',
  'ATA 31 - Indicating/Recording system', 'ATA 35 - Oxygen', 'ATA 46 - Information systems', 'ATA 52 - Doors',
  'ATA 70 - Power plant'
];
// The capture has ATA 34 marked manual-failure-ata--ata-failed (a failure is set in it), so its title says so.
const CHAPTER_NAMES = CHAPTERS.map(c => c === 'ATA 34 - Navigation' ? c + ', has failures' : c);

// The group title of a failure item, from the page: the title row of the item's own group.
const groupTitleOf = item => {
  const title = item.closest('.manual-failure-group').querySelector('.manual-failure-group-title');
  return title ? squash(title.textContent) : '';
};

test('Failure_chapters_open_and_items_name_their_group', async () => {
  const window = await load('failures');
  try {
    const chapters = all(window, 'div.manual-failure-ata-title');
    assert.deepStrictEqual(chapters.map(c => squash(c.textContent)), CHAPTERS, 'the page\'s own titles');
    assert.deepStrictEqual(chapters.map(nameOf), CHAPTER_NAMES);
    assert.ok(chapters.every(c => c.getAttribute('role') === 'button' && c.tabIndex === 0));
    // ATA 21 is the open chapter (the capture shows its groups and the down chevron); the rest are closed.
    assert.deepStrictEqual(chapters.map(attr('aria-expanded')), ['true', ...Array(19).fill('false')]);
    assert.ok(chapters[0].querySelector('i').classList.contains('fa-chevron-circle-down'));
    assert.ok(chapters[0].closest('.manual-failure-ata').querySelector('.manual-failure-group'));

    // Items are "<group title> <item>", the page's own two texts.
    const items = all(window, 'button.manual-failure-item');
    assert.strictEqual(items.length, 57);
    assert.deepStrictEqual(items.slice(0, 10).map(nameOf), [
      'Lavatory/Galley fan', 'CPC 1', 'CPC 2', 'Overheat Pack 1', 'Overheat Pack 2',
      'Regulator fault Pack 1', 'Regulator fault Pack 2', 'Zone controller Primary', 'Zone controller Secondary',
      'Trim air Hot Air Fault'
    ]);
    for (const item of items) {
      const own = squash(item.textContent);
      const group = groupTitleOf(item);
      assert.strictEqual(nameOf(item), group ? group + ' ' + own : own);
      // An item without a group title keeps its own text: the layer names nothing there.
      assert.strictEqual(item.hasAttribute('aria-label'), group !== '' ? true : false, own);
    }
    assert.deepStrictEqual(items.filter(i => groupTitleOf(i) === '').map(nameOf), [
      'Lavatory/Galley fan', 'Cargo ventilation controller', 'Recirculation fans', 'Outflow valve stuck'
    ]);
    assert.ok(items.every(i => !i.hasAttribute('role') && i.getAttribute('tabindex') === null), 'real buttons: nothing else added');

    // The page's own buttons and the search box are named by what the page shows.
    assert.deepStrictEqual(all(window, 'button.btn-failure-action').map(nameOf), ['Failed', 'Armed', 'Reload']);
    assert.ok(all(window, 'button.btn-failure-action').every(b => !b.hasAttribute('aria-label')));
    const search = one(window, 'app-manual-failures app-keyboard-input input');
    assert.strictEqual(nameOf(search), 'search');
    assert.strictEqual(search.getAttribute('placeholder'), 'search', 'the page\'s placeholder stays');

    // Enter and Space press a chapter through the page's own click.
    assert.deepStrictEqual(pressed(window, chapters[1]), { caught: true, clicks: 1 });
    assert.deepStrictEqual(pressed(window, chapters[1], ' '), { caught: true, clicks: 1 });
  } finally {
    window.close();
  }
});

test('Failure_chapters_follow_the_page_when_one_opens_and_another_closes', async () => {
  const window = await load('failures');
  try {
    const chapters = all(window, 'div.manual-failure-ata-title');
    const icon = i => chapters[i].querySelector('i');
    const set = (i, cls) => icon(i).setAttribute('class', 'fas fa-xs ' + cls);

    set(0, 'fa-chevron-circle-right');
    set(1, 'fa-chevron-circle-down');
    await turns();
    assert.deepStrictEqual(chapters.slice(0, 3).map(attr('aria-expanded')), ['false', 'true', 'false']);
    set(1, 'fa-chevron-circle-right');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(chapters[1].getAttribute('aria-expanded'), 'false');

    // An icon the layer does not know says nothing: no state is made up, and the layer's own is taken back.
    set(2, 'fa-bars');
    set(0, 'fa-chevron-circle-down');
    await turns();
    assert.strictEqual(chapters[2].hasAttribute('aria-expanded'), false);
    assert.strictEqual(chapters[0].getAttribute('aria-expanded'), 'true');
    set(0, 'fa-bars');
    await turns();
    assert.strictEqual(chapters[0].hasAttribute('aria-expanded'), false);
  } finally {
    window.close();
  }
});

// The page shows a failure's state by colour only: the item button gets manual-failure-failed or
// manual-failure-armed, its chapter manual-failure-ata--ata-failed or --ata-armed (the bundle's ngClass). The
// layer says it in the name, and the name follows the class.
test('Failure_states_are_spoken_in_the_name_and_follow_the_page', async () => {
  const window = await load('failures', { layer: false });
  try {
    // A synthetic copy of a real item (CPC 1) in its own group, and of the item that has no group title, so the
    // capture's own items are not the ones whose state is moved.
    const [lavatory, cpc1] = all(window, 'button.manual-failure-item');
    const copy = cpc1.cloneNode(true);
    cpc1.parentElement.appendChild(copy);
    const bare = lavatory.cloneNode(true);
    lavatory.parentElement.appendChild(bare);
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();

    // The capture: ATA 34's chapter carries the failed class (on the chapter, the title's parent).
    const chapters = all(window, 'div.manual-failure-ata-title');
    const navigation = chapters[7];
    const chapterOf = title => title.closest('div.manual-failure-ata');
    assert.strictEqual(chapterOf(navigation), navigation.parentElement);
    assert.ok(chapterOf(navigation).classList.contains('manual-failure-ata--ata-failed'), 'as captured');
    assert.strictEqual(nameOf(navigation), 'ATA 34 - Navigation, has failures');
    assert.strictEqual(chapters.filter(c => /, has/.test(nameOf(c))).length, 1, 'no other chapter has a state');

    // A chapter's state follows its class; failed wins over armed.
    const air = chapters[0];
    chapterOf(air).classList.add('manual-failure-ata--ata-armed');
    await turns();
    assert.strictEqual(nameOf(air), 'ATA 21 - Air Conditioning, has armed failures');
    chapterOf(air).classList.add('manual-failure-ata--ata-failed');
    await turns();
    assert.strictEqual(nameOf(air), 'ATA 21 - Air Conditioning, has failures');
    chapterOf(air).classList.remove('manual-failure-ata--ata-failed', 'manual-failure-ata--ata-armed');
    chapterOf(navigation).classList.remove('manual-failure-ata--ata-failed');
    await turns();
    assert.strictEqual(nameOf(air), 'ATA 21 - Air Conditioning');
    assert.strictEqual(nameOf(navigation), 'ATA 34 - Navigation');
    assert.strictEqual(air.getAttribute('aria-expanded'), 'true', 'the open state is still the icon\'s');

    // An item's state follows its class, on the name the item already had.
    assert.strictEqual(nameOf(copy), 'CPC 1');
    copy.classList.add('manual-failure-failed');
    await turns();
    assert.strictEqual(nameOf(copy), 'CPC 1, failed');
    copy.classList.remove('manual-failure-failed');
    copy.classList.add('manual-failure-armed');
    await turns();
    assert.strictEqual(nameOf(copy), 'CPC 1, armed');
    copy.classList.add('manual-failure-failed');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(nameOf(copy), 'CPC 1, failed', 'failed wins');
    copy.classList.remove('manual-failure-failed', 'manual-failure-armed');
    await turns();
    assert.strictEqual(nameOf(copy), 'CPC 1');

    // An item without a group title is named by its own text until it has a state; then the layer names it.
    assert.strictEqual(bare.hasAttribute('aria-label'), false);
    bare.classList.add('manual-failure-failed');
    await turns();
    assert.strictEqual(nameOf(bare), 'Lavatory/Galley fan, failed');
    bare.classList.remove('manual-failure-failed');
    await turns();
    assert.strictEqual(bare.hasAttribute('aria-label'), false, 'the layer\'s label is taken back');
    assert.strictEqual(nameOf(bare), 'Lavatory/Galley fan');

    // A label the page gives is the page's: the state is not added to it.
    copy.setAttribute('aria-label', 'Cabin pressure controller 1');
    copy.classList.add('manual-failure-armed');
    await turns();
    assert.strictEqual(copy.getAttribute('aria-label'), 'Cabin pressure controller 1');
  } finally {
    window.close();
  }
});

test('A_failure_chapter_state_the_page_set_is_kept', async () => {
  const window = await load('failures', { layer: false });
  try {
    const [first, second] = all(window, 'div.manual-failure-ata-title');
    first.setAttribute('aria-expanded', 'false');   // the page says closed though the icon is the open one
    second.setAttribute('aria-label', 'Autoflight chapter');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(first.getAttribute('aria-expanded'), 'false');
    assert.strictEqual(second.getAttribute('aria-label'), 'Autoflight chapter');
    assert.strictEqual(second.getAttribute('aria-expanded'), 'false', 'the others are as usual');
  } finally {
    window.close();
  }
});

test('Failure_item_names_follow_the_pages_group_title', async () => {
  const window = await load('failures');
  try {
    const items = all(window, 'button.manual-failure-item');
    const [, cpc1, cpc2] = items;
    const title = cpc1.closest('.manual-failure-group').querySelector('.manual-failure-group-title');
    assert.strictEqual(nameOf(cpc1), 'CPC 1');

    // Angular updates the title's text in place; both items follow.
    title.firstChild.nodeValue = ' CPC (cabin) ';
    await turns();
    assert.strictEqual(nameOf(cpc1), 'CPC (cabin) 1');
    assert.strictEqual(nameOf(cpc2), 'CPC (cabin) 2');

    // The title goes: the items are left with their own text and none of the layer's names.
    const parent = title.parentElement;
    const next = title.nextSibling;
    parent.removeChild(title);
    await turns();
    assert.deepStrictEqual([cpc1, cpc2].map(nameOf), ['1', '2']);
    assert.ok([cpc1, cpc2].every(i => !i.hasAttribute('aria-label')));
    parent.insertBefore(title, next);
    await turns();
    assert.strictEqual(nameOf(cpc2), 'CPC (cabin) 2');

    // A group that gains a title gives its items that group; a label the page set on an item is kept.
    const lavatory = items[0];
    const cargo = items[13];
    assert.strictEqual(nameOf(cargo), 'Cargo ventilation controller');
    lavatory.setAttribute('aria-label', 'Lavatory fan');
    for (const item of [lavatory, cargo]) {
      const group = item.closest('.manual-failure-group');
      const added = window.document.createElement('div');
      added.className = 'manual-failure-group-title ng-star-inserted';
      added.textContent = ' Fans ';
      group.insertBefore(added, group.firstChild);
    }
    await turns();
    assert.strictEqual(lavatory.getAttribute('aria-label'), 'Lavatory fan');
    assert.strictEqual(nameOf(cargo), 'Fans Cargo ventilation controller');
  } finally {
    window.close();
  }
});

// ---- Documents ----

test('Documents_folders_and_files_are_buttons', async () => {
  const window = await load('documents');
  try {
    const folders = all(window, 'div.folder-name');
    assert.strictEqual(folders.length, 4);
    const [qrh, ecam, guide, mine] = folders;
    const files = all(window, 'div.file');
    assert.deepStrictEqual([qrh, ecam, guide].map(nameOf), ['QRH', 'ECAM', 'User Guide']);
    assert.deepStrictEqual(files.map(nameOf), ['Checklists', 'Delay Allocation Codes']);
    // A row with no controls of its own is the button. "My Documents" holds two buttons and a box, so its
    // label is the button instead (see the next test).
    for (const el of [qrh, ecam, guide, ...files]) {
      assert.strictEqual(el.getAttribute('role'), 'button', nameOf(el));
      assert.strictEqual(el.tabIndex, 0, nameOf(el));
    }
    assert.deepStrictEqual(layerStamp(mine), NO_STAMP);

    // A folder is open while its div.folder (the row's container) carries the page's `open` class (the
    // bundle's [class.open]); every folder in the capture is closed. The state sits on the folder's button: the
    // row, or for My Documents its label.
    assert.ok(folders.every(f => f.parentElement.localName === 'div' && f.parentElement.classList.contains('folder')));
    assert.ok(folders.every(f => !f.parentElement.classList.contains('open')), 'every folder closed, as captured');
    assert.deepStrictEqual([qrh, ecam, guide].map(attr('aria-expanded')), ['false', 'false', 'false']);
    assert.strictEqual(mine.hasAttribute('aria-expanded'), false, 'My Documents\' row is not its button');
    assert.strictEqual(mine.querySelector('span.label').getAttribute('aria-expanded'), 'false');
    assert.ok(files.every(f => !f.hasAttribute('aria-expanded')), 'a file is not a folder');
    // No file is selected in the capture, so none is the current one.
    assert.ok(files.every(f => !f.classList.contains('is-selected') && !f.hasAttribute('aria-current')));

    // Enter and Space press them through the page's own click.
    assert.deepStrictEqual(pressed(window, ecam), { caught: true, clicks: 1 });
    assert.deepStrictEqual(pressed(window, files[0], ' '), { caught: true, clicks: 1 });
  } finally {
    window.close();
  }
});

// A row that holds controls of its own is not a button (a control inside a button cannot be reached); the
// text of the row is.
test('A_documents_row_with_controls_of_its_own_makes_its_label_the_button', async () => {
  const window = await load('documents');
  try {
    const mine = all(window, 'div.folder-name')[3];
    const label = mine.querySelector('span.label');
    assert.strictEqual(label.textContent, 'My Documents');
    assert.strictEqual(label.getAttribute('role'), 'button');
    assert.strictEqual(label.tabIndex, 0);
    assert.strictEqual(nameOf(label), 'My Documents');
    assert.deepStrictEqual(layerStamp(mine), NO_STAMP, 'the row has no role, no Tab stop, no name and no stamp');
    assert.strictEqual(mine.getAttribute('aria-label'), null);

    // The two buttons in the row keep their own titles and their own keys, and no element the layer made a
    // button holds them (nor does the hidden file box).
    const icons = all(window, 'button.icon-btn');
    assert.deepStrictEqual(icons.map(nameOf), ['Open documents folder', 'Upload document']);
    for (const icon of icons) {
      assert.deepStrictEqual(layerStamp(icon), NO_STAMP);
      assert.strictEqual(icon.closest('[role=button]'), null);
      assert.deepStrictEqual(pressed(window, icon), { caught: false, clicks: 0 });
    }
    assert.strictEqual(one(window, 'input[type=file]').closest('[role=button]'), null);
    assert.strictEqual(all(window, '.folder-name [role=button], .file [role=button]').length, 1, 'only the label');

    // Enter and Space on the label press once, as the label's own click.
    assert.deepStrictEqual(pressed(window, label), { caught: true, clicks: 1 });
    assert.deepStrictEqual(pressed(window, label, ' '), { caught: true, clicks: 1 });

    // That click is the one a mouse click on the text makes: it bubbles to the row's own handler.
    let rowClicks = 0;
    mine.addEventListener('click', () => { rowClicks++; });
    delete label.click;                   // the element's own click, not the stub of the lines above
    assert.ok(press(window, label, { key: 'Enter' }).defaultPrevented);
    assert.strictEqual(rowClicks, 1);
  } finally {
    window.close();
  }
});

test('A_documents_row_that_gains_or_loses_controls_hands_the_button_over_and_takes_back_only_its_own_stamp', async () => {
  const window = await load('documents');
  try {
    const doc = window.document;
    const [qrh] = all(window, 'div.folder-name');
    const [checklists] = all(window, 'div.file');
    const qrhLabel = qrh.querySelector('span.label');
    const fileLabel = checklists.querySelector('span.label');
    assert.strictEqual(qrh.getAttribute('role'), 'button');
    assert.strictEqual(checklists.getAttribute('role'), 'button');
    assert.deepStrictEqual(layerStamp(qrhLabel), NO_STAMP);

    // A button comes into the folder's row, a link into the file's: each row stops being a button (its Tab
    // stop, role, name and stamp go) and its label is the button.
    const inner = doc.createElement('button');
    inner.title = 'Rename';
    qrh.appendChild(inner);
    const link = doc.createElement('a');
    link.setAttribute('href', '#share');
    link.textContent = 'Share';
    checklists.appendChild(link);
    await turns();
    assert.deepStrictEqual(layerStamp(qrh), NO_STAMP);
    assert.deepStrictEqual(layerStamp(checklists), NO_STAMP);
    assert.deepStrictEqual([qrhLabel, fileLabel].map(el => el.getAttribute('role')), ['button', 'button']);
    assert.deepStrictEqual([qrhLabel, fileLabel].map(nameOf), ['QRH', 'Checklists']);
    assert.ok([qrhLabel, fileLabel].every(l => l.tabIndex === 0));
    assert.deepStrictEqual(pressed(window, qrhLabel), { caught: true, clicks: 1 });
    assert.deepStrictEqual(layerStamp(inner), NO_STAMP);

    // The controls go (the test takes them out): the rows are the buttons again, the labels are not.
    qrh.removeChild(inner);
    checklists.removeChild(link);
    await turns();
    assert.deepStrictEqual([qrh, checklists].map(el => el.getAttribute('role')), ['button', 'button']);
    assert.ok([qrh, checklists].every(el => el.tabIndex === 0));
    assert.deepStrictEqual([qrh, checklists].map(nameOf), ['QRH', 'Checklists']);
    assert.deepStrictEqual(layerStamp(qrhLabel), NO_STAMP);
    assert.deepStrictEqual(layerStamp(fileLabel), NO_STAMP);
    assert.strictEqual(qrhLabel.hasAttribute('tabindex'), false);
    // A label whose button was withdrawn takes no key: its listener stays but the handler ignores it.
    assert.deepStrictEqual(pressed(window, qrhLabel), { caught: false, clicks: 0 });
    assert.deepStrictEqual(pressed(window, qrh), { caught: true, clicks: 1 });
  } finally {
    window.close();
  }
});

// The folder's open state and the selected file follow the page's own classes, on whichever element is the
// button; what the layer wrote is taken back when the class goes or the button moves, and a state the page set
// is never touched.
test('Documents_folder_and_file_states_follow_the_page', async () => {
  const window = await load('documents');
  try {
    const [qrh, ecam, , mine] = all(window, 'div.folder-name');
    const mineLabel = mine.querySelector('span.label');
    const qrhLabel = qrh.querySelector('span.label');
    const [checklists, codes] = all(window, 'div.file');

    // A folder opens and closes: the page moves `open` on its div.folder.
    qrh.parentElement.classList.add('open');
    await turns();
    assert.deepStrictEqual([qrh, ecam].map(attr('aria-expanded')), ['true', 'false']);
    qrh.parentElement.classList.remove('open');
    mine.parentElement.classList.add('open');
    await turns();
    assert.strictEqual(qrh.getAttribute('aria-expanded'), 'false');
    assert.strictEqual(mineLabel.getAttribute('aria-expanded'), 'true', 'on My Documents\' label, its button');
    assert.strictEqual(mine.hasAttribute('aria-expanded'), false);

    // The button moves to the label (a control comes into the row) and back: the state moves with it.
    qrh.parentElement.classList.add('open');
    const inner = window.document.createElement('button');
    inner.title = 'Rename';
    qrh.appendChild(inner);
    await turns();
    assert.strictEqual(qrh.hasAttribute('aria-expanded'), false, 'the row\'s state is taken back');
    assert.strictEqual(qrhLabel.getAttribute('aria-expanded'), 'true');
    qrh.removeChild(inner);
    await turns();
    assert.strictEqual(qrh.getAttribute('aria-expanded'), 'true');
    assert.strictEqual(qrhLabel.hasAttribute('aria-expanded'), false);

    // The selected file is the current one, and only while the page marks it is-selected.
    checklists.classList.add('is-selected');
    await turns();
    assert.deepStrictEqual([checklists, codes].map(attr('aria-current')), ['true', null]);
    checklists.classList.remove('is-selected');
    codes.classList.add('is-selected');
    window.__msfsbaFenixEfb.pass();
    assert.deepStrictEqual([checklists, codes].map(attr('aria-current')), [null, 'true']);
    codes.classList.remove('is-selected');
    await turns();
    assert.deepStrictEqual([checklists, codes].map(attr('aria-current')), [null, null]);
  } finally {
    window.close();
  }
});

test('A_folder_or_file_state_the_page_set_is_kept', async () => {
  const window = await load('documents', { layer: false });
  try {
    const [qrh, ecam] = all(window, 'div.folder-name');
    const [checklists, codes] = all(window, 'div.file');
    qrh.setAttribute('aria-expanded', 'true');       // the page's own, though its folder is closed
    checklists.setAttribute('aria-current', 'page');  // the page's own, though the file is not selected
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(qrh.getAttribute('aria-expanded'), 'true');
    assert.strictEqual(checklists.getAttribute('aria-current'), 'page');
    assert.strictEqual(ecam.getAttribute('aria-expanded'), 'false', 'the others are as usual');

    // The page selects the other file and opens the folder: the page's own states stay as the page set them.
    codes.classList.add('is-selected');
    qrh.parentElement.classList.add('open');
    qrh.setAttribute('aria-expanded', 'false');
    await turns();
    assert.strictEqual(codes.getAttribute('aria-current'), 'true');
    assert.strictEqual(checklists.getAttribute('aria-current'), 'page');
    assert.strictEqual(qrh.getAttribute('aria-expanded'), 'false');

    // A folder row with no div.folder around it says nothing about being open (no state is made up).
    const loose = window.document.createElement('div');
    loose.className = 'folder-name';
    loose.innerHTML = '<span class="label">Loose</span>';
    window.document.querySelector('app-documents').appendChild(loose);
    await turns();
    assert.strictEqual(loose.getAttribute('role'), 'button');
    assert.strictEqual(loose.hasAttribute('aria-expanded'), false);
  } finally {
    window.close();
  }
});

test('A_role_or_label_the_page_gave_a_documents_row_or_its_label_is_kept', async () => {
  const window = await load('documents', { layer: false });
  try {
    const [qrh, , , mine] = all(window, 'div.folder-name');
    const label = mine.querySelector('span.label');
    qrh.setAttribute('role', 'treeitem');
    qrh.setAttribute('aria-label', 'Quick reference');
    label.setAttribute('aria-label', 'My own documents');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(qrh.getAttribute('role'), 'treeitem');
    assert.strictEqual(qrh.getAttribute('aria-label'), 'Quick reference');
    assert.strictEqual(label.getAttribute('aria-label'), 'My own documents');
    assert.strictEqual(label.getAttribute('role'), 'button');

    // The folder gains a control: the layer takes back its Tab stop and stamp, never the page's role or label.
    qrh.appendChild(window.document.createElement('input'));
    await turns();
    assert.strictEqual(qrh.getAttribute('role'), 'treeitem');
    assert.strictEqual(qrh.getAttribute('aria-label'), 'Quick reference');
    assert.strictEqual(qrh.hasAttribute('tabindex'), false);
    assert.strictEqual(qrh.hasAttribute('data-msfsba'), false);
  } finally {
    window.close();
  }
});

test('The_documents_list_toggle_is_named_and_a_folder_name_follows_the_page', async () => {
  const window = await load('documents');
  try {
    const toggle = one(window, 'button.sidebar-toggle-btn');
    assert.strictEqual(nameOf(toggle), 'Toggle document list');
    assert.strictEqual(toggle.hasAttribute('role'), false, 'a real button already');
    assert.strictEqual(toggle.hasAttribute('title'), false);

    const [qrh] = all(window, 'div.folder-name');
    qrh.querySelector('.label').firstChild.nodeValue = 'Quick Reference Handbook';
    await turns();
    assert.strictEqual(nameOf(qrh), 'Quick Reference Handbook');
    const [checklists] = all(window, 'div.file');
    checklists.querySelector('.label').lastChild.nodeValue = ' Checklists (v2) ';
    await turns();
    assert.strictEqual(nameOf(checklists), 'Checklists (v2)');
  } finally {
    window.close();
  }
});

// ---- Charts and quick settings ----

test('Charts_and_quick_settings_controls_are_named', async () => {
  let charts = null;
  let quick = null;
  try {
    charts = await load('charts');
    quick = await load('quick-settings');
    // The sidebar's items: each app-navigraph-sidebar-item's div is a button named by its text. The
    // "My Flight" header above them carries the item class too, but it is a heading, not a button.
    const sidebar = all(charts, 'app-navigraph-sidebar-item div.navigraph-sidebar-item');
    assert.deepStrictEqual(sidebar.map(nameOf), ['Route', 'Sync SimBrief']);
    assert.ok(sidebar.every(el => el.getAttribute('role') === 'button' && el.tabIndex === 0));
    assert.deepStrictEqual(pressed(charts, sidebar[0]), { caught: true, clicks: 1 });
    const header = one(charts, 'div.navigraph-sidebar-header');
    assert.ok(header.classList.contains('navigraph-sidebar-item'), 'as captured');
    assert.strictEqual(squash(header.textContent), 'My Flight');
    assert.deepStrictEqual(layerStamp(header), NO_STAMP);
    assert.deepStrictEqual(pressed(charts, header), { caught: false, clicks: 0 });

    // Quick settings: the backdrop behind the pop-up closes it when clicked, as a mouse click outside does;
    // it is the keyboard's way out ("Close quick settings").
    const backdrop = one(quick, 'div.brightness-popup-backdrop');
    assert.strictEqual(backdrop.getAttribute('role'), 'button');
    assert.strictEqual(backdrop.tabIndex, 0);
    assert.strictEqual(nameOf(backdrop), 'Close quick settings');
    assert.deepStrictEqual(pressed(quick, backdrop), { caught: true, clicks: 1 });
    assert.deepStrictEqual(pressed(quick, backdrop, ' '), { caught: true, clicks: 1 });
    let closes = 0;
    backdrop.addEventListener('click', () => { closes++; });
    delete backdrop.click;                // the element's own click: it reaches the page's handler
    assert.ok(press(quick, backdrop, { key: 'Enter' }).defaultPrevented);
    assert.strictEqual(closes, 1);

    // The zoom icons (the names are the owner's): the ids sit on the icons and their parent holds both, so each
    // icon is the button (a capture cannot show where the page's listener is).
    const zoomIn = one(charts, '#mapZoomInBtn');
    const zoomOut = one(charts, '#mapZoomOutBtn');
    assert.deepStrictEqual([zoomIn, zoomOut].map(nameOf), ['Zoom in', 'Zoom out']);
    assert.strictEqual(zoomIn.parentElement, zoomOut.parentElement, 'the parent holds both, so the icons are the buttons');
    for (const icon of [zoomIn, zoomOut]) {
      assert.strictEqual(icon.getAttribute('role'), 'button');
      assert.strictEqual(icon.tabIndex, 0);
    }
    assert.deepStrictEqual(layerStamp(zoomIn.parentElement), NO_STAMP);
    assert.deepStrictEqual(pressed(charts, zoomOut, ' '), { caught: true, clicks: 1 });

    // The map controls are icon fonts: named by the icon, the World Map item by its own text.
    const controls = all(charts, 'div.map-control-item');
    assert.deepStrictEqual(controls.map(nameOf), ['Map settings', 'World Map', 'Dark map', 'Light map']);
    assert.deepStrictEqual(controls.map(c => c.querySelector('i') ? c.querySelector('i').className : ''),
      ['fas fa-cog', '', 'fas fa-moon', 'fas fa-sun']);
    assert.ok(controls.every(el => el.getAttribute('role') === 'button' && el.tabIndex === 0));
    // aria-pressed follows the page's map-control-item-selected on the map mode and the two themes. The
    // settings cog opens the settings and is not a choice: it has none, whatever the page marks.
    assert.deepStrictEqual(controls.map(c => c.classList.contains('map-control-item-selected')), [false, true, false, false]);
    assert.deepStrictEqual(controls.map(attr('aria-pressed')), [null, 'true', 'false', 'false']);
    controls[1].classList.toggle('map-control-item-selected', false);
    controls[2].classList.toggle('map-control-item-selected', true);
    await turns();
    assert.deepStrictEqual(controls.map(attr('aria-pressed')), [null, 'false', 'true', 'false']);
    controls[0].classList.toggle('map-control-item-selected', true);
    await turns();
    assert.deepStrictEqual(controls.map(attr('aria-pressed')), [null, 'false', 'true', 'false']);
    controls[0].classList.toggle('map-control-item-selected', false);

    // The page's own buttons keep their text.
    assert.deepStrictEqual(all(charts, 'button.btn-default').map(nameOf), ['Navigraph', 'Reset']);
    assert.ok(all(charts, 'button.btn-default').every(b => !b.hasAttribute('aria-label')));

    // Quick settings: the slider and the airplane button, the other two keep the page's own labels.
    const slider = one(quick, 'mat-slider.brightness-popup-slider');
    assert.strictEqual(nameOf(slider), 'Brightness');
    assert.strictEqual(slider.getAttribute('role'), 'slider');
    assert.strictEqual(slider.getAttribute('aria-valuenow'), '0.775', 'the page\'s own value stays');
    const airplane = one(quick, 'button.quick-settings-btn img[src*=airplane]').closest('button');
    assert.strictEqual(nameOf(airplane), 'Airplane mode');
    assert.strictEqual(airplane.hasAttribute('role'), false, 'a real button already');
    const settings = one(quick, 'button[aria-label="Open settings"]');
    const open = one(quick, 'button.top-bar-settings-btn');
    assert.deepStrictEqual([settings, open].map(nameOf), ['Open settings', 'Open quick settings']);
    assert.ok([settings, open].every(b => b.getAttribute('data-msfsba') === null));
  } finally {
    if (charts) charts.close();
    if (quick) quick.close();
  }
});

// The quick settings' airplane picture and the Charts sidebar's item icons have no alt: hidden from a screen
// reader, and the buttons holding them keep their names. The charts capture has the sidebar without its pictures (the old
// snippet removed every <img>), so they are put back here as the live page has them, first in each item's left part.
test('Unlabeled_pictures_on_Charts_and_quick_settings_are_hidden_and_their_buttons_keep_their_names', async () => {
  let quick = null;
  let charts = null;
  try {
    quick = await load('quick-settings');
    const airplane = one(quick, 'button.quick-settings-btn img[src*=airplane]');
    assert.strictEqual(airplane.hasAttribute('alt'), false, 'as captured');
    assert.strictEqual(airplane.getAttribute('aria-hidden'), 'true');
    assert.strictEqual(nameOf(airplane.closest('button')), 'Airplane mode');

    charts = await load('charts', { layer: false });
    const items = all(charts, 'app-navigraph-sidebar-item');
    for (const item of items) {
      const icon = charts.document.createElement('img');
      icon.className = 'navigraph-sidebar-item--left-icon';
      icon.setAttribute('src', item.getAttribute('iconimage'));
      const left = item.querySelector('.navigraph-sidebar-item--left');
      left.insertBefore(icon, left.firstChild);
    }
    charts.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    const icons = all(charts, 'img.navigraph-sidebar-item--left-icon');
    assert.strictEqual(icons.length, 2);
    assert.ok(icons.every(icon => icon.getAttribute('aria-hidden') === 'true'));
    assert.deepStrictEqual(all(charts, 'app-navigraph-sidebar-item div.navigraph-sidebar-item').map(nameOf), ['Route', 'Sync SimBrief']);
  } finally {
    if (quick) quick.close();
    if (charts) charts.close();
  }
});

test('The_moving_map_is_named_without_a_role_or_a_Tab_stop_of_ours_and_the_chart_list_toggle_is_a_button', async () => {
  const window = await load('charts');
  try {
    // Leaflet owns the map's keyboard: the layer adds a name and nothing else (the page's tabindex 0 stays).
    const map = one(window, '#map-container');
    assert.ok(map.classList.contains('leaflet-container'));
    assert.strictEqual(nameOf(map), 'Moving map');
    assert.strictEqual(map.getAttribute('aria-label'), 'Moving map');
    assert.strictEqual(map.hasAttribute('role'), false);
    assert.strictEqual(map.getAttribute('tabindex'), '0', 'the page\'s own, as captured');
    assert.deepStrictEqual(pressed(window, map), { caught: false, clicks: 0 });
    assert.deepStrictEqual(pressed(window, map, ' '), { caught: false, clicks: 0 });
    // Leaflet's own attribution links are named by their text.
    assert.deepStrictEqual(all(window, '.leaflet-control-attribution a').map(nameOf), ['Leaflet', 'Esri']);

    // The list toggle is an empty div the page clicks: a button, like Documents' "Toggle document list".
    const toggle = one(window, 'div.sidebar-toggle-btn');
    assert.strictEqual(nameOf(toggle), 'Toggle chart list');
    assert.strictEqual(toggle.getAttribute('role'), 'button');
    assert.strictEqual(toggle.tabIndex, 0);
    assert.deepStrictEqual(pressed(window, toggle), { caught: true, clicks: 1 });
    assert.deepStrictEqual(pressed(window, toggle, ' '), { caught: true, clicks: 1 });

    // A name the page gave the map is kept.
    map.setAttribute('aria-label', 'World chart');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(map.getAttribute('aria-label'), 'World chart');
  } finally {
    window.close();
  }
});

test('A_map_control_without_text_or_a_known_icon_and_an_airplane_button_without_its_picture_stay_unnamed', async () => {
  let quick = null;
  let charts = null;
  try {
    quick = await load('quick-settings');
    charts = await load('charts');
    const doc = charts.document;
    const holder = doc.createElement('div');
    holder.innerHTML = '<div class="map-control-item"><i class="fas fa-globe"></i></div>' +
      '<div class="map-control-item" aria-label="Layers"><i class="fas fa-sun"></i></div>';
    one(charts, 'app-charts-map').appendChild(holder);
    await turns();
    const [unknown, labelled] = [...holder.children];
    assert.strictEqual(unknown.hasAttribute('aria-label'), false, 'no name from an icon the layer does not know');
    assert.strictEqual(unknown.hasAttribute('aria-pressed'), false, 'and no choice made up for it');
    assert.strictEqual(unknown.getAttribute('role'), 'button', 'still a button with a Tab stop');
    assert.strictEqual(labelled.getAttribute('aria-label'), 'Layers', 'the page\'s own label stays');
    assert.strictEqual(labelled.getAttribute('aria-pressed'), 'false', 'a theme item is a choice');

    const toggles = one(quick, '.quick-settings-toggles');
    const bare = quick.document.createElement('button');
    bare.className = 'quick-settings-btn';
    bare.innerHTML = '<img class="quick-settings-btn-icon" src="/assets/img/globe.png">';
    toggles.appendChild(bare);
    const own = quick.document.createElement('button');
    own.className = 'quick-settings-btn';
    own.setAttribute('aria-label', 'Flight mode');
    own.innerHTML = '<img class="quick-settings-btn-icon flight-mode-btn-icon" src="/assets/img/airplane.png">';
    toggles.appendChild(own);
    // The picture is found by its class or by its file name: either alone is enough.
    const byClass = quick.document.createElement('button');
    byClass.className = 'quick-settings-btn';
    byClass.innerHTML = '<img class="quick-settings-btn-icon flight-mode-btn-icon" src="/assets/img/flight.png">';
    toggles.appendChild(byClass);
    const byName = quick.document.createElement('button');
    byName.className = 'quick-settings-btn';
    byName.innerHTML = '<img class="quick-settings-btn-icon" src="/assets/img/airplane-white.png">';
    toggles.appendChild(byName);
    await turns();
    assert.strictEqual(bare.hasAttribute('aria-label'), false);
    assert.strictEqual(own.getAttribute('aria-label'), 'Flight mode', 'the page\'s own label stays');
    assert.deepStrictEqual([byClass, byName].map(nameOf), ['Airplane mode', 'Airplane mode']);
  } finally {
    if (quick) quick.close();
    if (charts) charts.close();
  }
});

// ---- The layer's own rules ----

const FIXTURES = ['settings', ...Object.keys(SETTINGS_SCREENS).filter(s => s !== 'settings'), 'settings-overflow-open',
  'failures', 'documents', 'charts', 'quick-settings'];

// The layer writes attributes only: no node added or removed, no text changed, no value written.
for (const fixture of FIXTURES) {
  test('The_layer_writes_attributes_only_on_' + fixture.replace(/-/g, '_'), async () => {
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
// the elements that took a key handler are exactly the ones that are not real buttons.
const SECOND_PASS = {
  'settings': {
    handled: 2 + 8 + 8 + 5 + 5 + 8 + 3 + 6, listeners: 2 + 8 + 5,
    note: 'home, chevron; eight categories; eight badges; five checkboxes and the five switches that hold them; eight ' +
      'three-dot buttons; three choice groups; their six choices'
  },
  'failures': {
    handled: 2 + 20 + 53 + 1, listeners: 2 + 20,
    note: 'home, chevron; twenty chapters; the 53 items that have a group title; the search box'
  },
  'documents': {
    handled: 2 + 4 + 2 + 1, listeners: 2 + 4 + 2,
    note: 'home, chevron; three folder rows and the label of My Documents; two files; the list toggle'
  },
  'charts': {
    handled: 2 + 2 + 2 + 4 + 1 + 1, listeners: 2 + 2 + 2 + 4 + 1,
    note: 'home, chevron; two sidebar items (not the My Flight header); two zoom icons; four map controls; the list toggle; the map (a name only)'
  },
  'quick-settings': {
    handled: 1 + 9 + 1 + 1 + 1, listeners: 1 + 9 + 1,
    note: 'home (the chevron is hidden here); nine home tiles; the slider; the airplane button; the backdrop that closes the pop-up'
  }
};
// The seven other Settings captures, counted from each capture by hand: the closed app switcher's icons, the badges
// with text, the on/off options (each a checkbox and its switch), the three-dot buttons, the choice groups and their
// choices, and the boxes. Each also has home, the chevron and the eight categories; the switches and the categories,
// with home, the chevron and the switcher's icons, are the elements that take a key handler.
const SETTINGS_PARTS = {
  'settings-audio': { switcher: 9, badges: 8, toggles: 1, dots: 8, groups: 0, options: 0, boxes: 7 },
  'settings-cabin': { switcher: 9, badges: 8, toggles: 6, dots: 8, groups: 2, options: 6, boxes: 0 },
  'settings-connections': { switcher: 9, badges: 8, toggles: 0, dots: 8, groups: 3, options: 9, boxes: 4 },
  'settings-ground-services': { switcher: 9, badges: 8, toggles: 8, dots: 8, groups: 0, options: 0, boxes: 0 },
  'settings-misc': { switcher: 9, badges: 6, toggles: 4, dots: 6, groups: 2, options: 4, boxes: 1 },
  'settings-printer': { switcher: 9, badges: 8, toggles: 2, dots: 8, groups: 0, options: 0, boxes: 5 },
  'settings-realism': { switcher: 9, badges: 8, toggles: 3, dots: 8, groups: 5, options: 13, boxes: 0 },
  // Aircraft with the first menu open: the menu's backdrop is the "Close menu" button.
  'settings-overflow-open': { switcher: 9, badges: 8, toggles: 5, dots: 8, groups: 3, options: 6, boxes: 0, backdrop: 1 }
};
for (const [fixture, p] of Object.entries(SETTINGS_PARTS)) {
  const backdrop = p.backdrop || 0;
  SECOND_PASS[fixture] = {
    handled: 2 + p.switcher + 8 + p.badges + 2 * p.toggles + p.dots + p.groups + p.options + p.boxes + backdrop,
    listeners: 2 + p.switcher + 8 + p.toggles + backdrop,
    note: 'home, chevron, ' + p.switcher + ' switcher icons, 8 categories, ' + p.badges + ' badges, ' + p.toggles +
      ' checkboxes and switches, ' + p.dots + ' three-dot buttons, ' + p.groups + ' choice groups, ' + p.options +
      ' choices, ' + p.boxes + ' boxes, ' + backdrop + ' menu backdrop'
  };
}
for (const [fixture, expected] of Object.entries(SECOND_PASS)) {
  test('Second_pass_changes_nothing_on_' + fixture.replace(/-/g, '_'), async () => {
    const { window, counts } = await loadCounting(fixture);
    try {
      const doc = window.document;
      const before = doc.documentElement.outerHTML;
      const attributes = attributesOf(window);
      window.__msfsbaFenixEfb.pass();
      window.__msfsbaFenixEfb.pass();
      assert.strictEqual(doc.documentElement.outerHTML, before);
      assert.deepStrictEqual(attributesOf(window), attributes);

      assert.strictEqual(all(window, '[data-msfsba]').length, expected.handled, expected.note);
      for (const [el, n] of counts) assert.strictEqual(n, 1, 'one listener on ' + el.localName + '.' + el.className);
      assert.strictEqual(counts.size, expected.listeners, 'only the pressable divs got a key handler');
    } finally {
      window.close();
    }
  });
}

// Each rule is its own: one that throws never skips the others. A rule is made to fail by its own query of
// the whole page; every other rule still does its work.
const ISOLATION = {
  'settings': [
    ['li.category', 'li.category[role=button]', 8],
    // The pressed-state rule asks for 'app-settings2 button.option', the group rule for 'app-settings2 article
    // button.option': each needle is in its own rule's query only.
    ['app-settings2 button.option', 'button.option[aria-pressed]', 6],
    ['article button.option', 'div.option-grid[role=group]', 3],
    ['button.overflow-button', 'button.overflow-button[aria-label]', 8],
    // The checkbox rule asks for 'app-settings2 article input[type=checkbox]', the switch rule for 'app-settings2 article
    // label.toggle', the box rule for 'app-settings2 article input:not([type=checkbox])': each needle is in its own rule's
    // query only.
    ['article input[type=checkbox]', 'input[type=checkbox][aria-label]', 5],
    ['article label.toggle', 'label.toggle[role=switch]', 5],
    ['span.settings-source', 'span.settings-source[aria-label]', 8]
  ],
  // The open menu: the menu rule asks for the open button through '.settings-overflow' and for the menu through
  // '.cdk-overlay-pane >', so the three-dot rule's needle is in its own query only.
  'settings-overflow-open': [
    ['.cdk-overlay-pane >', 'div.settings-overflow[aria-owns]', 1],
    ['button.overflow-button', 'button.overflow-button[aria-label]', 8]
  ],
  // Audio has the boxes the box rule names, next to one on/off option.
  'settings-audio': [
    ['input:not(', 'article input:not([type=checkbox])[aria-label]', 7],
    ['article input[type=checkbox]', 'input[type=checkbox][aria-label]', 1],
    ['article label.toggle', 'label.toggle[role=switch]', 1],
    ['button.overflow-button', 'button.overflow-button[aria-label]', 8]
  ],
  'failures': [
    ['manual-failure-ata-title', 'div.manual-failure-ata-title[role=button]', 20],
    ['manual-failure-item', 'button.manual-failure-item[aria-label]', 53],
    ['app-manual-failures app-keyboard-input', 'input[placeholder=search][aria-label]', 1]
  ],
  'documents': [
    ['div.folder-name', 'div.folder-name[role=button], span.label[role=button]', 4],
    ['div.file', 'div.file[role=button]', 2],
    ['button.sidebar-toggle-btn', 'button.sidebar-toggle-btn[aria-label]', 1]
  ],
  'charts': [
    ['div.navigraph-sidebar-item', 'div.navigraph-sidebar-item[role=button]', 2],
    ['div.chart-viewer-controls', '.chart-viewer-controls i[role=button]', 2],
    ['div.map-control-item', 'div.map-control-item[role=button]', 4],
    ['div.sidebar-toggle-btn', 'div.sidebar-toggle-btn[role=button]', 1],
    ['div.leaflet-container', '#map-container[aria-label="Moving map"]', 1]
  ],
  'quick-settings': [
    ['mat-slider', 'mat-slider[aria-label]', 1],
    ['img[src*=airplane]', 'button[aria-label="Airplane mode"]', 1],
    ['brightness-popup-backdrop', 'div.brightness-popup-backdrop[role=button]', 1]
  ]
};
for (const [fixture, cases] of Object.entries(ISOLATION)) {
  for (const [failing] of cases) {
    test('A_failing_rule_does_not_skip_the_others_on_' + fixture.replace(/-/g, '_') + '_when_' +
      failing.replace(/[^A-Za-z0-9]+/g, '_') + '_fails', async () => {
      const window = await load(fixture, { layer: false });
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
        for (const [needle, selector, count] of cases) {
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
}
