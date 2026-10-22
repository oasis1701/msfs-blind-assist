'use strict';
// Settings, Failures, Documents, Charts and the quick settings, checked against the LIVE captures
// (fixtures/settings.html, failures.html, documents.html, charts.html, quick-settings.html; Fenix 3.0.0.7438,
// A320-232 IAE). A name is the page's own text wherever the page gives one; where it gives none the name is
// one the rulings chose (R10 quick settings, R15 Charts, R21 Documents) or owner decision 5's.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const { computeAccessibleName } = require('dom-accessibility-api');
const { load, loadCounting, press, attributesOf, turns, LAYER } = require('./load');

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

    // The options say which one is chosen, from the page's own class; the name is the option's own text.
    const options = all(window, 'button.option');
    assert.deepStrictEqual(options.map(nameOf), ['GNADIRS', 'ADIRS', 'Classic', 'Modern', 'hPa', 'inHg']);
    assert.deepStrictEqual(options.map(o => o.classList.contains('option--active')), [false, true, false, true, true, false]);
    assert.deepStrictEqual(options.map(attr('aria-pressed')), ['false', 'true', 'false', 'true', 'true', 'false']);
    assert.ok(options.every(o => !o.hasAttribute('role') && !o.hasAttribute('aria-label')), 'real buttons: nothing else added');

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

// The group title of a failure item, from the page: the title row of the item's own group.
const groupTitleOf = item => {
  const title = item.closest('.manual-failure-group').querySelector('.manual-failure-group-title');
  return title ? squash(title.textContent) : '';
};

test('Failure_chapters_open_and_items_name_their_group', async () => {
  const window = await load('failures');
  try {
    const chapters = all(window, 'div.manual-failure-ata-title');
    assert.deepStrictEqual(chapters.map(nameOf), CHAPTERS);
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
    // A row with no controls of its own is the button. "My Documents" holds two buttons and a box (R27), so its
    // label is the button instead (see the next test).
    for (const el of [qrh, ecam, guide, ...files]) {
      assert.strictEqual(el.getAttribute('role'), 'button', nameOf(el));
      assert.strictEqual(el.tabIndex, 0, nameOf(el));
    }
    assert.deepStrictEqual(layerStamp(mine), NO_STAMP);

    // The capture shows no open or closed state on a folder: every folder has both icons and none carries a
    // class that says which is shown. So no aria-expanded is written (it would be a guess). A Fenix update
    // that adds such a class shows up here, and the rule can then follow it.
    assert.ok(all(window, '.folder, .folder-name, app-document-tree-node').every(el => !/open|clos|expand|collaps/i.test(el.className)));
    assert.ok(folders.every(f => !f.hasAttribute('aria-expanded')));
    assert.ok(files.every(f => !f.hasAttribute('aria-expanded')));
    assert.ok(all(window, 'span.label').every(l => !l.hasAttribute('aria-expanded')));

    // Enter and Space press them through the page's own click.
    assert.deepStrictEqual(pressed(window, ecam), { caught: true, clicks: 1 });
    assert.deepStrictEqual(pressed(window, files[0], ' '), { caught: true, clicks: 1 });
  } finally {
    window.close();
  }
});

// R27: a row that holds controls of its own is not a button (a control inside a button cannot be reached); the
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
    // The sidebar has three items as captured, all buttons named by their text.
    const sidebar = all(charts, 'div.navigraph-sidebar-item');
    assert.deepStrictEqual(sidebar.map(nameOf), ['My Flight', 'Route', 'Sync SimBrief']);
    assert.ok(sidebar.every(el => el.getAttribute('role') === 'button' && el.tabIndex === 0));
    assert.deepStrictEqual(pressed(charts, sidebar[1]), { caught: true, clicks: 1 });

    // The zoom icons (R15 / owner decision 5): the ids sit on the icons and their parent holds both, so each
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

    // The map controls are icon fonts (R15): named by the icon, the World Map item by its own text.
    const controls = all(charts, 'div.map-control-item');
    assert.deepStrictEqual(controls.map(nameOf), ['Map settings', 'World Map', 'Dark map', 'Light map']);
    assert.deepStrictEqual(controls.map(c => c.querySelector('i') ? c.querySelector('i').className : ''),
      ['fas fa-cog', '', 'fas fa-moon', 'fas fa-sun']);
    assert.ok(controls.every(el => el.getAttribute('role') === 'button' && el.tabIndex === 0));
    // aria-pressed follows the page's map-control-item-selected on the map mode and the two themes (R26). The
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

// R24 and R26.
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
    // The picture is found by its class or by its file name (R10): either alone is enough.
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

const FIXTURES = ['settings', 'failures', 'documents', 'charts', 'quick-settings'];

// The layer writes attributes only: no node added or removed, no text changed, no value written.
for (const fixture of FIXTURES) {
  test('The_layer_writes_attributes_only_on_' + fixture.replace('-', '_'), async () => {
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
    handled: 2 + 8 + 8 + 5 + 8, listeners: 2 + 8,
    note: 'home, chevron; eight categories; eight badges; five checkboxes; eight three-dot buttons'
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
    handled: 2 + 3 + 2 + 4 + 1 + 1, listeners: 2 + 3 + 2 + 4 + 1,
    note: 'home, chevron; three sidebar items; two zoom icons; four map controls; the list toggle; the map (a name only)'
  },
  'quick-settings': {
    handled: 1 + 9 + 1 + 1, listeners: 1 + 9,
    note: 'home (the chevron is hidden here); nine home tiles; the slider; the airplane button'
  }
};
for (const [fixture, expected] of Object.entries(SECOND_PASS)) {
  test('Second_pass_changes_nothing_on_' + fixture.replace('-', '_'), async () => {
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
    ['button.option', 'button.option[aria-pressed]', 6],
    ['button.overflow-button', 'button.overflow-button[aria-label]', 8],
    ['label.toggle', 'input[type=checkbox][aria-label]', 5],
    ['span.settings-source', 'span.settings-source[aria-label]', 8]
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
    ['div.navigraph-sidebar-item', 'div.navigraph-sidebar-item[role=button]', 3],
    ['div.chart-viewer-controls', '.chart-viewer-controls i[role=button]', 2],
    ['div.map-control-item', 'div.map-control-item[role=button]', 4],
    ['div.sidebar-toggle-btn', 'div.sidebar-toggle-btn[role=button]', 1],
    ['div.leaflet-container', '#map-container[aria-label="Moving map"]', 1]
  ],
  'quick-settings': [
    ['mat-slider', 'mat-slider[aria-label]', 1],
    ['img[src*=airplane]', 'button[aria-label="Airplane mode"]', 1]
  ]
};
for (const [fixture, cases] of Object.entries(ISOLATION)) {
  for (const [failing] of cases) {
    test('A_failing_rule_does_not_skip_the_others_on_' + fixture.replace('-', '_') + '_when_' +
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
