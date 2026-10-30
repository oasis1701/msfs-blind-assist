'use strict';
// The Takeoff (depperf) and Landing (arrperf, arrperf-electrical) calculators, checked against the LIVE captures
// (fixtures/depperf.html, arrperf.html, arrperf-electrical.html; Fenix 3.0.0.7438, A320-232 IAE, kilograms and hPa):
// the fields named by the page's own labels, the Takeoff results grid read label then value with every other child in
// its place, the Landing runway condition as a radio group with one Tab stop at its place in the
// page's Tab order, the Landing options and procedures as buttons, a procedure's sub-page with its Back
// button and tick boxes, and the pictures with no alt hidden.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const { computeAccessibleName } = require('dom-accessibility-api');
const { load, loadCounting, press, attributesOf, hiddenByLayer, turns, LAYER } = require('./load');

const nameOf = el => computeAccessibleName(el);
const all = (window, selector) => [...window.document.querySelectorAll(selector)];
const textOf = window => window.__msfsbaFenixEfb.textOf;

// Every .departure-label of the Takeoff capture, in page order, as the layer reads them (<br> and nbsp are
// spaces; "ANTI-<br>ICE" is "ANTI- ICE"). The last three belong to the results, not to a field.
const TAKEOFF_LABELS = [
  'ICAO CODE', 'RUNWAY', 'RUNWAY SURFACE', 'FLAP CONFIG', 'FORCE TOGA', 'ANTI- ICE', 'PACKS', 'TOW (T)',
  'MACTOW (%)', 'WIND', 'TEMPERATURE', 'QNH (hPa)', 'TOPL (KG)', 'HW (KT)', 'Intersection'
];
// The twelve fields, in page order: the label of each, and the value text a drop-down shows (the page names a
// mat-select by its value, so its name is the label and then the value).
const TAKEOFF_FIELDS = [
  ['ICAO CODE', 'input'], ['RUNWAY', 'mat-select', ''], ['RUNWAY SURFACE', 'mat-select', 'DRY'],
  ['FLAP CONFIG', 'mat-select', 'OPT'], ['FORCE TOGA', 'mat-select', 'NO'], ['ANTI- ICE', 'mat-select', 'OFF'],
  ['PACKS', 'mat-select', 'OFF'], ['TOW (T)', 'input'], ['MACTOW (%)', 'input'], ['WIND', 'input'],
  ['TEMPERATURE', 'input'], ['QNH (hPa)', 'input']
];
const FIELDS = 'app-keyboard-input input, mat-select';

test('Every_takeoff_field_is_named_by_its_label', async () => {
  const window = await load('depperf');
  try {
    const labels = all(window, '.departure-label').map(textOf(window));
    assert.deepStrictEqual(labels, TAKEOFF_LABELS, 'the capture\'s own labels');

    const fields = all(window, FIELDS);
    assert.strictEqual(fields.length, TAKEOFF_FIELDS.length);
    for (let i = 0; i < fields.length; i++) {
      const [label, kind, value] = TAKEOFF_FIELDS[i];
      const field = fields[i];
      assert.strictEqual(field.localName, kind, label);
      // The label is the .departure-label in the field's own column (derived from the page, not from the list).
      const column = field.closest('[class^="col-"]');
      assert.strictEqual(textOf(window)(column.querySelector('.departure-label')), label);
      assert.strictEqual(field.getAttribute('aria-label'), label);
      // An input is named by the label alone; a drop-down by the label, then the value the page shows.
      assert.strictEqual(nameOf(field), kind === 'input' ? label : (label + ' ' + value).trim(), label);
    }
    // A placeholder is never the name.
    for (const input of all(window, 'app-keyboard-input input')) {
      assert.ok(!['----', '---/--', '--'].includes(nameOf(input)));
    }
  } finally {
    window.close();
  }
});

test('The_takeoff_fields_keep_their_role_tab_stop_placeholder_and_class_and_the_drop_downs_their_value_reference', async () => {
  const bare = await load('depperf', { layer: false });
  const window = await load('depperf');
  try {
    const keep = el => ['role', 'tabindex', 'placeholder', 'class', 'id', 'type', 'aria-expanded', 'aria-haspopup']
      .map(a => a + '=' + el.getAttribute(a)).join('|');
    assert.deepStrictEqual(all(window, FIELDS).map(keep), all(bare, FIELDS).map(keep));
    const selects = all(window, 'mat-select');
    assert.ok(selects.every(s => s.getAttribute('role') === 'combobox'), 'a mat-select keeps its role');
    // A labelledby wins over an aria-label, so the select's own id goes in front of what the page lists:
    // the select is named by its label and the page's value follows (as Material does with a mat-label).
    assert.deepStrictEqual(selects.map(s => s.getAttribute('aria-labelledby')), [
      'mat-select-0 mat-select-value-1', 'mat-select-2 mat-select-value-3', 'mat-select-4 mat-select-value-5',
      'mat-select-6 mat-select-value-7', 'mat-select-8 mat-select-value-9', 'mat-select-10 mat-select-value-11'
    ]);
    // The page's value text itself is untouched.
    assert.deepStrictEqual(all(window, '.mat-select-value').map(textOf(window)), ['', 'DRY', 'OPT', 'NO', 'OFF', 'OFF']);
  } finally {
    bare.close();
    window.close();
  }
});

test('A_takeoff_field_the_page_labels_itself_keeps_the_pages_label_and_value_reference', async () => {
  const window = await load('depperf', { layer: false });
  try {
    const [runway, surface, flap, force] = all(window, 'mat-select');
    const [icao] = all(window, 'app-keyboard-input input');
    surface.setAttribute('aria-label', 'Surface of the runway');
    icao.setAttribute('aria-label', 'Airport');
    // A select that already lists its own id is left as the page has it.
    flap.setAttribute('aria-labelledby', 'mat-select-4 mat-select-value-5');
    // A select that lists nothing has nothing to extend: it is named by its aria-label alone.
    force.removeAttribute('aria-labelledby');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();

    assert.strictEqual(surface.getAttribute('aria-label'), 'Surface of the runway');
    assert.strictEqual(surface.getAttribute('aria-labelledby'), 'mat-select-value-3', 'the page\'s own list');
    assert.strictEqual(icao.getAttribute('aria-label'), 'Airport');
    assert.strictEqual(runway.getAttribute('aria-label'), 'RUNWAY', 'the others are named as usual');

    assert.strictEqual(flap.getAttribute('aria-labelledby'), 'mat-select-4 mat-select-value-5');
    assert.strictEqual(nameOf(flap), 'FLAP CONFIG OPT');
    assert.strictEqual(force.hasAttribute('aria-labelledby'), false, 'nothing to extend, nothing written');
    assert.strictEqual(nameOf(force), 'FORCE TOGA');
  } finally {
    window.close();
  }
});

test('A_takeoff_label_that_goes_takes_the_name_with_it_and_puts_the_pages_value_reference_back', async () => {
  const window = await load('depperf');
  try {
    const surface = all(window, 'mat-select')[1];
    const label = surface.closest('[class^="col-"]').querySelector('.departure-label');
    assert.strictEqual(nameOf(surface), 'RUNWAY SURFACE DRY');

    // The label's text changes: the name follows.
    label.firstChild.nodeValue = 'SURFACE';
    await turns();
    assert.strictEqual(nameOf(surface), 'SURFACE DRY');

    // The label goes: no name of the layer's is left behind, and the page's own list is back as it was.
    const parent = label.parentElement;
    const next = label.nextSibling;
    parent.removeChild(label);
    await turns();
    assert.strictEqual(surface.hasAttribute('aria-label'), false);
    assert.strictEqual(surface.getAttribute('aria-labelledby'), 'mat-select-value-3');
    assert.strictEqual(nameOf(surface), 'DRY');

    // And it comes back.
    parent.insertBefore(label, next);
    await turns();
    assert.strictEqual(nameOf(surface), 'SURFACE DRY');
    assert.strictEqual(surface.getAttribute('aria-labelledby'), 'mat-select-2 mat-select-value-3');
  } finally {
    window.close();
  }
});

// The first text the page gives a field without a label of its own: the header of its table column; else a
// placeholder that has letters in it ("LANDING WT") with the unit written after the field ("LANDING WT, T"),
// or the unit alone. A placeholder of only dashes, digits or punctuation ("----", "---/--") is never a name,
// and a field with nothing else stays unnamed.
test('A_field_without_its_own_label_takes_its_column_header_else_a_placeholder_with_letters_and_its_unit', async () => {
  const window = await load('depperf');
  try {
    const doc = window.document;
    const holder = doc.createElement('div');
    holder.innerHTML =
      '<div class="in-table"><table><tr><th>Cross <b>wind</b></th><th>Gust</th></tr>' +
      '<tr><td><app-keyboard-input placeholder="x"><input placeholder="---/--"></app-keyboard-input></td>' +
      '<td><app-keyboard-input placeholder="x"><input placeholder="GUST KT"></app-keyboard-input></td></tr></table></div>' +
      '<div class="section-item"><app-keyboard-input placeholder="x"><input placeholder="--"></app-keyboard-input> KT </div>' +
      '<div class="section-item"><div class="boxed"><div class="inner"><app-keyboard-input placeholder="x"><input placeholder="LANDING WT"></app-keyboard-input></div></div>' +
      '<button>FMS</button><div class="unit"> T <!----></div></div>' +
      '<div class="bare"><app-keyboard-input placeholder="x"><input placeholder=" Nothing   around me "></app-keyboard-input></div>' +
      '<div class="bare"><app-keyboard-input placeholder="x"><input placeholder="----"></app-keyboard-input></div>' +
      '<div class="bare"><app-keyboard-input placeholder="x"><input placeholder="12.5 / -- %"></app-keyboard-input></div>';
    doc.querySelector('app-departure').appendChild(holder);
    await turns();
    const inputs = [...holder.querySelectorAll('input')];
    // A column header wins over a placeholder; a placeholder with letters joins the unit; the last two say
    // nothing but dashes, digits and punctuation, and stay unnamed.
    assert.deepStrictEqual(inputs.map(i => i.getAttribute('aria-label')),
      ['Cross wind', 'Gust', 'KT', 'LANDING WT, T', 'Nothing around me', null, null]);
    assert.deepStrictEqual(inputs.slice(0, 5).map(nameOf), ['Cross wind', 'Gust', 'KT', 'LANDING WT, T', 'Nothing around me']);
    assert.deepStrictEqual(inputs.map(i => i.getAttribute('placeholder')),
      ['---/--', 'GUST KT', '--', 'LANDING WT', ' Nothing   around me ', '----', '12.5 / -- %'], 'the page\'s own placeholders stay');

    // The placeholder changes: the name follows (a pass by hand; the observer does not watch placeholders).
    inputs[3].setAttribute('placeholder', 'LDG WT');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(inputs[3].getAttribute('aria-label'), 'LDG WT, T');
    // The real fields keep their labels.
    assert.strictEqual(all(window, 'app-keyboard-input input')[0].getAttribute('aria-label'), 'ICAO CODE');
  } finally {
    window.close();
  }
});

// ---- The results grid ----
// The results are the MCDU's take-off page: a title, then rows that alternate, a row of small labels and under it a
// row of values, then an output line. A screen reader reads the page in DOM order, so it met every label of a row
// before any of its values. The grid's aria-owns lists every child of the grid in DOM order, with each label / value
// row pair expanded into its cells, the label cell and then the value cell under it, column by column: each label is
// read right before its value, and nothing (the output line, a row without a pair) is read before the title or out of
// its place. Nothing is hidden and no cell is a picture, except an empty "not computed" value cell (only the MCDU's
// yellow boxes), which is one named "not computed". The ids aria-owns needs are the layer's own ("msfsba-<n>"),
// written only on what it lists; an id the page set is used as it is.

const rowsOf = window => [...window.document.querySelector('.mcdu-screen').children].filter(el => el.classList.contains('row'));
const cellsOf = row => [...row.children];
const screenOf = window => window.document.querySelector('.mcdu-screen');
const titleOf = window => window.document.querySelector('.mcdu-screen > .text-center');
const outputOf = window => window.document.querySelector('.mcdu-screen > .text-output');

// The elements the grid's aria-owns lists, in its order (a missing id would show as null).
const ownedOf = window => (screenOf(window).getAttribute('aria-owns') || '').split(/\s+/).filter(Boolean)
  .map(id => window.document.getElementById(id));
// What a screen reader says for each of them, in that order: a picture's label, else the element's text.
const reading = window => ownedOf(window)
  .map(el => el.getAttribute('role') === 'img' ? el.getAttribute('aria-label') : textOf(window)(el));
// The elements that carry an id of the layer's.
const layerIds = window => all(window, '[id^="msfsba-"]');
// A label row and the value row under it, as the list has them: label cell, value cell, column by column.
const pairOf = (labelRow, valueRow) => cellsOf(labelRow).flatMap((label, c) => [label, cellsOf(valueRow)[c]]);
// The capture's own order: the title, the five pairs, the output line.
const fullOrder = window => {
  const rows = rowsOf(window);
  return [titleOf(window), ...[0, 2, 4, 6, 8].flatMap(r => pairOf(rows[r], rows[r + 1])), outputOf(window)];
};
// The list is exactly these elements, in this order.
function assertOwned(window, expected, message) {
  const owned = ownedOf(window);
  assert.strictEqual(owned.length, expected.length, (message || '') + ': how many');
  owned.forEach((el, i) => assert.strictEqual(el, expected[i], (message || '') + ': entry ' + i));
}

// Each column's label and value, as captured (the three empty speeds are "not computed").
const PAIRS = [
  ['V1', 'not computed'], ['FLP RETR', 'F=---'], ['STOP MARGIN', '----'],
  ['VR', 'not computed'], ['SLT RETR', 'S=---'], ['TO SHIFT', '[M] [ ]*'],
  ['V2', 'not computed'], ['CLEAN', 'O=---'], ['FLAPS/THS', '[]/[ ]'],
  ['TRANS ALT', '----'], ['FLEX TO TEMP', '[]°'],
  ['THR RED/ACC', '----/----'], ['ENG OUT ACC', '----']
];
const TITLE = 'TAKE OFF RWY';
// The output line under the grid is empty in the capture.
const READING = [TITLE, ...PAIRS.flat(), ''];
// The cells the layer has written nothing on.
const untouched = cell => ['role', 'aria-label', 'aria-roledescription', 'aria-hidden', 'data-msfsba', 'id'].every(a => !cell.hasAttribute(a));
function makeRow(window, texts) {
  const row = window.document.createElement('div');
  row.className = 'row';
  row.innerHTML = texts.map(t => '<div class="col-6"><div class="text-mcdu-small">' + t + '</div></div>').join('');
  return row;
}

test('Takeoff_results_read_each_label_right_before_its_value_and_every_other_child_in_its_place', async () => {
  const bare = await load('depperf', { layer: false });
  const window = await load('depperf');
  try {
    const screen = screenOf(window);
    const rows = rowsOf(window);
    // As captured: the title, ten rows (five label rows, each with its value row under it) and the output line.
    assert.deepStrictEqual([...screen.children].map(c => c.className.split(' ')[0]),
      ['text-center', ...Array(10).fill('row'), 'text-output']);
    assert.strictEqual(textOf(window)(titleOf(window)), TITLE);

    // The order: every child in DOM order, each pair of rows as label, value, label, value ... column by column.
    const expected = [titleOf(window)];
    for (let r = 0; r < 10; r += 2) {
      const labels = cellsOf(rows[r]);
      const values = cellsOf(rows[r + 1]);
      assert.strictEqual(labels.length, values.length);
      for (let c = 0; c < labels.length; c++) expected.push(labels[c], values[c]);
    }
    expected.push(outputOf(window));
    assertOwned(window, expected, 'the full order');
    assert.deepStrictEqual(reading(window), READING);
    assert.strictEqual(ownedOf(window)[0], titleOf(window), 'nothing is read before the title');

    // The ids are the layer's own: on the listed elements and nowhere else, each new (none had an id on the page).
    const ids = screen.getAttribute('aria-owns').split(' ');
    assert.strictEqual(ids.length, 1 + 2 * 13 + 1);
    assert.ok(ids.every(id => /^msfsba-\d+$/.test(id)), ids.join(' '));
    assert.strictEqual(new Set(ids).size, ids.length, 'each id once');
    assert.strictEqual(layerIds(window).length, ids.length, 'no other element carries an id of the layer\'s');
    const bareAll = [...bare.document.querySelectorAll('*')];
    const windowAll = [...window.document.querySelectorAll('*')];
    for (const el of ownedOf(window)) assert.strictEqual(bareAll[windowAll.indexOf(el)].hasAttribute('id'), false);
    assert.deepStrictEqual(windowAll.filter(el => el.hasAttribute('id') && !el.id.startsWith('msfsba-')).map(el => el.id),
      bareAll.filter(el => el.hasAttribute('id')).map(el => el.id), 'the page\'s own ids are as they were');

    // No picture but the three empty speeds, nothing hidden, no other name: the labels and values are read as text. An
    // empty speed is read as a value, not a graphic (aria-roledescription "value").
    const pictures = all(window, '[role=img]');
    assert.deepStrictEqual(pictures, [1, 3, 5].map(r => cellsOf(rows[r])[0]), 'V1, VR and V2');
    assert.ok(pictures.every(p => p.getAttribute('aria-label') === 'not computed' && nameOf(p) === 'not computed'));
    assert.ok(pictures.every(p => p.getAttribute('aria-roledescription') === 'value'));
    assert.deepStrictEqual(all(window, '[aria-roledescription]'), pictures, 'nothing else gets one');
    assert.strictEqual(all(window, '.mcdu-screen [aria-hidden]').length, 0);
    assert.strictEqual(all(window, '.mcdu-screen [aria-label]').length, 3);
    // The grid gets its order and nothing else.
    assert.ok(['role', 'aria-label', 'id', 'tabindex'].every(a => !screen.hasAttribute(a)));
  } finally {
    bare.close();
    window.close();
  }
});

test('Takeoff_results_keep_their_order_when_Calculate_changes_them_in_place', async () => {
  const window = await load('depperf');
  try {
    const doc = window.document;
    const screen = screenOf(window);
    const owns = screen.getAttribute('aria-owns');
    const rows = rowsOf(window);
    const [v1, flp, stop] = cellsOf(rows[1]);
    const [, flpLabel] = cellsOf(rows[0]);
    const at = (reads, label) => reads[reads.indexOf(label) + 1];

    // Angular updates a text node in place: the order holds and the new value is read after its label.
    const flpText = [...flp.firstElementChild.childNodes].find(n => n.nodeType === 3);
    flpText.nodeValue = ' F=161 ';
    await turns();
    assert.strictEqual(screen.getAttribute('aria-owns'), owns);
    assert.strictEqual(at(reading(window), 'FLP RETR'), 'F=161');

    // The yellow boxes give way to a number: the cell is no picture any more, and is read as text in its place.
    v1.firstElementChild.textContent = ' 143 ';
    await turns();
    assert.strictEqual(screen.getAttribute('aria-owns'), owns);
    assert.ok(['role', 'aria-label', 'aria-roledescription', 'data-msfsba'].every(a => !v1.hasAttribute(a)),
      'the role, the label and the role description go together');
    assert.deepStrictEqual(reading(window).slice(0, 3), [TITLE, 'V1', '143']);
    // And the yellow boxes come back.
    v1.firstElementChild.textContent = '';
    for (let i = 0; i < 3; i++) {
      const box = doc.createElement('span');
      box.className = 'small-yellow-box';
      v1.firstElementChild.appendChild(box);
    }
    await turns();
    assert.strictEqual(v1.getAttribute('role'), 'img');
    assert.strictEqual(nameOf(v1), 'not computed');
    assert.strictEqual(v1.getAttribute('aria-roledescription'), 'value');
    assert.strictEqual(screen.getAttribute('aria-owns'), owns);

    // A label's text changes: it is read as the page now has it, in its place.
    flpLabel.firstElementChild.firstChild.nodeValue = 'FLP RETR (KT)';
    await turns();
    assert.strictEqual(at(reading(window), 'FLP RETR (KT)'), 'F=161');

    // A value cell that empties stays in its place (read as nothing), and is no picture: it has no yellow boxes.
    stop.firstElementChild.textContent = '';
    await turns();
    assert.strictEqual(screen.getAttribute('aria-owns'), owns);
    assert.ok(['role', 'aria-label', 'data-msfsba'].every(a => !stop.hasAttribute(a)));
    stop.firstElementChild.textContent = ' 1450 ';
    await turns();
    assert.strictEqual(at(reading(window), 'STOP MARGIN'), '1450');

    // The output line gets a message: it is read last, where the page has it.
    outputOf(window).textContent = 'CHECK TOW';
    await turns();
    assert.strictEqual(screen.getAttribute('aria-owns'), owns);
    assert.strictEqual(reading(window).slice(-1)[0], 'CHECK TOW');

    // A pass run by hand sees the same, and writes nothing new.
    flpText.nodeValue = ' F=158 ';
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(screen.getAttribute('aria-owns'), owns);
    assert.strictEqual(at(reading(window), 'FLP RETR (KT)'), 'F=158');
  } finally {
    window.close();
  }
});

test('A_results_grid_that_does_not_line_up_is_left_in_its_place', async () => {
  const window = await load('depperf', { layer: false });
  try {
    // A label row and value row of different widths: no cell is paired by guess, so both rows are listed as rows, in
    // their place, and their cells are left as they are.
    const rows = rowsOf(window);
    rows[1].removeChild(rows[1].lastElementChild);
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.ok([...cellsOf(rows[0]), ...cellsOf(rows[1])].every(untouched));
    assertOwned(window, [titleOf(window), rows[0], rows[1], ...fullOrder(window).slice(7)]);
    assert.strictEqual(cellsOf(rows[3])[0].getAttribute('aria-label'), 'not computed', 'the other pairs still line up');
  } finally {
    window.close();
  }
});

test('A_role_label_hidden_state_or_id_the_page_set_in_the_results_grid_is_never_overridden', async () => {
  const window = await load('depperf', { layer: false });
  try {
    const rows = rowsOf(window);
    const [v1, flp] = cellsOf(rows[1]);
    const [, , stopLabel] = cellsOf(rows[0]);
    const [vrLabel] = cellsOf(rows[2]);
    v1.setAttribute('role', 'status');                        // the page's own role on an empty speed
    flp.setAttribute('aria-label', 'Flap retraction speed');  // the page's own label on a value
    stopLabel.setAttribute('aria-hidden', 'true');            // the page's own hidden state on a label
    vrLabel.setAttribute('id', 'vr-label');                   // the page's own id on a label
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();

    assert.strictEqual(v1.getAttribute('role'), 'status');
    assert.strictEqual(v1.hasAttribute('aria-label'), false, 'a cell with a role of the page\'s is not named');
    assert.strictEqual(v1.hasAttribute('aria-roledescription'), false, 'nor described');
    assert.strictEqual(flp.getAttribute('aria-label'), 'Flap retraction speed');
    assert.strictEqual(stopLabel.getAttribute('aria-hidden'), 'true');
    // The page's id is the one listed for its cell, in its place; the layer writes none over it.
    assert.strictEqual(vrLabel.id, 'vr-label');
    assertOwned(window, fullOrder(window));
    assert.strictEqual(ownedOf(window).indexOf(vrLabel), 7, 'the title and three pairs before it');

    // Withdrawn, the page's id stays (it is no longer listed); the layer's own go.
    const screen = screenOf(window);
    screen.insertBefore(makeRow(window, ['A', 'B', 'C']), rows[3]);
    await turns();
    assert.strictEqual(vrLabel.id, 'vr-label');
    assert.ok(!ownedOf(window).includes(vrLabel));
    assert.ok(cellsOf(rows[3]).every(untouched));
  } finally {
    window.close();
  }
});

test('A_results_grid_the_page_orders_itself_is_left_to_the_page', async () => {
  const window = await load('depperf', { layer: false });
  try {
    screenOf(window).setAttribute('aria-owns', 'page-own-order');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(screenOf(window).getAttribute('aria-owns'), 'page-own-order');
    assert.strictEqual(layerIds(window).length, 0, 'no id written');
    assert.strictEqual(all(window, '[role=img]').length, 0, 'the grid is the page\'s: no picture either');

    // The page takes its own away: the layer reads the grid from then on.
    screenOf(window).removeAttribute('aria-owns');
    window.__msfsbaFenixEfb.pass();
    assert.deepStrictEqual(reading(window), READING);
    assert.strictEqual(all(window, '[role=img]').length, 3);
  } finally {
    window.close();
  }
});

// ---- The results grid keeps up with its rows ----
// A row that falls out of its label / value pair is let go of: its cells leave the list and lose what the layer wrote,
// and the row itself is listed in its place. A pairing is trusted only while its two rows still sit together; a row
// that turns up between them (or anywhere) never shifts the pairs below it or mixes the cells of two rows.

test('A_label_row_left_without_its_value_row_is_listed_as_a_row_and_pairs_again_when_the_value_row_returns', async () => {
  const window = await load('depperf');
  try {
    const screen = screenOf(window);
    const rows = rowsOf(window);
    const last = rows[9];
    const after = last.nextSibling;
    assertOwned(window, fullOrder(window));
    assert.strictEqual(layerIds(window).length, 1 + 2 * 13 + 1);

    // The page drops the last value row: its two labels have nothing under them.
    screen.removeChild(last);
    await turns();
    const orphans = cellsOf(rows[8]);
    assert.ok(orphans.every(untouched), 'no id, no role, no name, not hidden');
    assertOwned(window, [titleOf(window), ...[0, 2, 4, 6].flatMap(r => pairOf(rows[r], rows[r + 1])), rows[8], outputOf(window)]);
    assert.deepStrictEqual(reading(window).slice(-2), ['THR RED/ACC ENG OUT ACC', ''], 'the row as a row, then the output line');
    assert.strictEqual(layerIds(window).length, 1 + 2 * 11 + 1 + 1);
    assert.ok(cellsOf(last).every(untouched), 'the dropped row\'s cells get back what the layer wrote');

    // The value row comes back: so does the pair.
    screen.insertBefore(last, after);
    await turns();
    assertOwned(window, fullOrder(window));
    assert.deepStrictEqual(reading(window), READING);
    assert.ok(orphans.every(c => c.id.startsWith('msfsba-')));
    assert.strictEqual(rows[8].hasAttribute('id'), false, 'the row is not listed now: its id is taken back');
    assert.strictEqual(layerIds(window).length, 1 + 2 * 13 + 1);
  } finally {
    window.close();
  }
});

test('A_row_inserted_into_the_results_grid_never_mixes_two_rows_and_a_pairing_it_breaks_is_withdrawn', async () => {
  const window = await load('depperf');
  try {
    const screen = screenOf(window);
    const rows = rowsOf(window);
    const [title, output] = [titleOf(window), outputOf(window)];
    const pairs = [0, 2, 4, 6, 8].map(r => pairOf(rows[r], rows[r + 1]));

    // Between two pairs, two columns wide like the pairs below it: every pair still holds, the new row is listed as a
    // row, in its place.
    const extra = makeRow(window, ['NOTE', 'HERE']);
    screen.insertBefore(extra, rows[6]);
    await turns();
    assertOwned(window, [title, ...pairs[0], ...pairs[1], ...pairs[2], extra, ...pairs[3], ...pairs[4], output]);
    assert.ok(cellsOf(extra).every(untouched));

    // Between a label row and its value row, three columns wide like the pair it breaks: that pairing no longer holds
    // and is withdrawn (VR, SLT RETR and TO SHIFT leave the list as cells; the three rows are listed as rows).
    const between = makeRow(window, ['A', 'B', 'C']);
    screen.insertBefore(between, rows[3]);
    await turns();
    for (const row of [rows[2], rows[3], between]) assert.ok(cellsOf(row).every(untouched));
    assertOwned(window, [title, ...pairs[0], rows[2], between, rows[3], ...pairs[2], extra, ...pairs[3], ...pairs[4], output]);

    // Taken out again, the grid is as it was.
    screen.removeChild(between);
    await turns();
    assertOwned(window, [title, ...pairs[0], ...pairs[1], ...pairs[2], extra, ...pairs[3], ...pairs[4], output]);
    screen.removeChild(extra);
    await turns();
    assertOwned(window, fullOrder(window));
    assert.strictEqual(layerIds(window).length, 1 + 2 * 13 + 1);
  } finally {
    window.close();
  }
});

test('A_row_that_stops_being_a_value_row_is_released_and_a_new_pair_of_rows_is_read', async () => {
  const window = await load('depperf');
  try {
    const screen = screenOf(window);
    const rows = rowsOf(window);
    const [labelRow, valueRow] = [rows[2], rows[3]];
    const [title, output] = [titleOf(window), outputOf(window)];

    // The page takes the row class off the value row: it is not a row of the grid any more, but still a child of it.
    valueRow.className = 'not-a-row';
    await turns();
    assert.ok(cellsOf(valueRow).every(untouched), 'no stale picture or id is left behind');
    assert.ok(cellsOf(labelRow).every(untouched));
    assertOwned(window, [title, ...pairOf(rows[0], rows[1]), labelRow, valueRow,
      ...[4, 6, 8].flatMap(r => pairOf(rows[r], rows[r + 1])), output]);
    valueRow.className = 'row';
    await turns();
    assertOwned(window, fullOrder(window));

    // A new pair of rows (a label row and its value row) is read as a pair, wherever the page puts it.
    const newLabels = makeRow(window, ['ALT', 'SPD']);
    const newValues = makeRow(window, ['350', '250']);
    screen.insertBefore(newLabels, rows[4]);
    screen.insertBefore(newValues, rows[4]);
    await turns();
    assert.deepStrictEqual(reading(window),
      [TITLE, ...PAIRS.slice(0, 6).flat(), 'ALT', '350', 'SPD', '250', ...PAIRS.slice(6).flat(), '']);

    // The page puts a fresh value row in place of one it dropped: the labels pair with it again.
    const fresh = makeRow(window, ['FRESH', 'ROW']);
    screen.replaceChild(fresh, rows[9]);
    await turns();
    assert.deepStrictEqual(reading(window).slice(-5), ['THR RED/ACC', 'FRESH', 'ENG OUT ACC', 'ROW', '']);
  } finally {
    window.close();
  }
});

test('A_withdrawn_pairing_takes_back_only_what_the_layer_wrote', async () => {
  const window = await load('depperf', { layer: false });
  try {
    const screen = screenOf(window);
    const rows = rowsOf(window);
    const [vr, slt] = cellsOf(rows[3]);
    const [vrLabel, sltLabel] = cellsOf(rows[2]);
    vr.setAttribute('aria-label', 'Rotation speed');   // the page's own label on an empty speed
    vrLabel.setAttribute('aria-hidden', 'true');       // the page's own hidden state on a label
    sltLabel.setAttribute('id', 'slt-label');          // the page's own id on a label
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(vr.getAttribute('role'), 'img');
    assert.strictEqual(vr.getAttribute('aria-label'), 'Rotation speed', 'the page\'s label is not replaced');
    assert.ok(slt.id.startsWith('msfsba-') && vr.id.startsWith('msfsba-'));
    assert.ok(ownedOf(window).includes(sltLabel));

    // The pairing is withdrawn: the layer's role, ids and stamp go, the page's label, hidden state and id stay.
    screen.insertBefore(makeRow(window, ['A', 'B', 'C']), rows[3]);
    await turns();
    assert.strictEqual(vr.getAttribute('aria-label'), 'Rotation speed', 'the page\'s label is not taken back');
    assert.ok(['role', 'aria-roledescription', 'id', 'data-msfsba'].every(a => !vr.hasAttribute(a)), 'the layer\'s are');
    assert.ok(untouched(slt));
    assert.strictEqual(vrLabel.getAttribute('aria-hidden'), 'true', 'the page\'s hidden state stays');
    assert.strictEqual(sltLabel.id, 'slt-label', 'the page\'s id stays');
    assert.ok(!ownedOf(window).includes(sltLabel));
  } finally {
    window.close();
  }
});

// What the layer wrote on a grid that stops being one, or on rows the page moves out of it, is taken back: the grid's
// aria-owns, the ids and the "not computed" pictures (review minor 1). The grid as the page left it is what remains.
test('A_grid_that_stops_being_a_results_grid_gets_back_everything_the_layer_wrote', async () => {
  const bare = await load('depperf', { layer: false });
  const window = await load('depperf');
  try {
    const screen = screenOf(window);
    const subtree = grid => [grid, ...grid.querySelectorAll('*')]
      .map(el => el.localName + ':' + [...el.attributes].map(a => a.name + '=' + a.value).join('|'));
    const bareGrid = screenOf(bare);
    const asCaptured = subtree(bareGrid);
    assert.ok(screen.hasAttribute('aria-owns') && all(window, '[role=img]').length === 3, 'read as a grid first');

    screen.setAttribute('class', 'mcdu-screen-off');
    await turns();
    assert.strictEqual(screen.hasAttribute('aria-owns'), false);
    assert.strictEqual(layerIds(window).length, 0);
    assert.strictEqual(all(window, '[role=img]').length, 0);
    assert.strictEqual(all(window, '[aria-roledescription]').length, 0);
    // Everything in it is as the page has it (the grid itself carries only the class the test changed).
    assert.deepStrictEqual(subtree(screen).slice(1), asCaptured.slice(1));
    assert.deepStrictEqual([...screen.attributes].map(a => a.name), [...bareGrid.attributes].map(a => a.name));

    // It becomes a grid again: it is read as one again.
    screen.setAttribute('class', 'mcdu-screen');
    await turns();
    assertOwned(window, fullOrder(window));
    assert.strictEqual(all(window, '[role=img]').length, 3);
  } finally {
    bare.close();
    window.close();
  }
});

test('Cells_of_rows_the_page_moves_out_of_the_grid_get_back_what_the_layer_wrote', async () => {
  const window = await load('depperf');
  try {
    const doc = window.document;
    const rows = rowsOf(window);
    const holder = doc.createElement('div');
    doc.body.appendChild(holder);
    // The first pair (V1, with its "not computed" picture) moves out as a pair, and the last label row alone.
    holder.appendChild(rows[0]);
    holder.appendChild(rows[1]);
    holder.appendChild(rows[8]);
    await turns();
    for (const row of [rows[0], rows[1], rows[8]]) {
      assert.ok(cellsOf(row).every(untouched), 'nothing of the layer\'s is left on a moved cell');
      assert.strictEqual(row.hasAttribute('id'), false);
    }
    assert.strictEqual(cellsOf(rows[1])[0].hasAttribute('role'), false, 'V1 is no picture out of the grid');
    // The grid lists what it holds now: the last value row has no label row above it any more, so it is a row.
    assertOwned(window, [titleOf(window), ...[2, 4, 6].flatMap(r => pairOf(rows[r], rows[r + 1])), rows[9], outputOf(window)]);
  } finally {
    window.close();
  }
});

// ---- Landing ----

const CONDITIONS = ['6 DRY', '5 GOOD', '4 GOOD/MED', '3 MED', '2 MED/POOR', '1 POOR'];
const conditionItems = window => all(window, 'app-arrival-runway-configuration .section-group')[1].children;

test('Runway_condition_is_a_radio_group_that_follows_the_page', async () => {
  const window = await load('arrperf');
  try {
    const items = [...conditionItems(window)];
    assert.strictEqual(items.length, 6);
    const group = items[0].parentElement;
    assert.strictEqual(group.getAttribute('role'), 'radiogroup');
    assert.strictEqual(group.getAttribute('aria-label'), 'Runway condition');
    assert.strictEqual(nameOf(group), 'Runway condition');

    assert.deepStrictEqual(items.map(i => i.getAttribute('role')), Array(6).fill('radio'));
    assert.deepStrictEqual(items.map(nameOf), CONDITIONS);
    assert.deepStrictEqual(items.map(i => i.getAttribute('aria-checked')), ['true', 'false', 'false', 'false', 'false', 'false']);
    // The page's selection is the section-selected class.
    assert.deepStrictEqual(items.map(i => i.classList.contains('section-selected')), [true, false, false, false, false, false]);

    // Only these six: the other section-items (ICAO CODE, RUNWAY, KT, degrees C, HPA, METAR) are not radios.
    assert.strictEqual(all(window, '[role=radio]').length, 6);
    assert.strictEqual(all(window, '[role=radiogroup]').length, 1);
    assert.strictEqual(all(window, '.section-item').length, 18, 'the capture has twelve other section-items: the six are not all of them');

    // One Tab stop: the checked radio, the others reached with the arrows. The stop keeps the code's own place
    // in the page's Tab sequence, its own tabindex (6 DRY's is 30).
    assert.deepStrictEqual(items.map(i => i.getAttribute('tabindex')), ['30', '-1', '-1', '-1', '-1', '-1']);

    // The page moves section-selected to "3 MED": aria-checked and the Tab stop follow (3 MED's own is 33).
    items[0].classList.remove('section-selected');
    items[3].classList.add('section-selected');
    await turns();
    assert.deepStrictEqual(items.map(i => i.getAttribute('aria-checked')), ['false', 'false', 'false', 'true', 'false', 'false']);
    assert.deepStrictEqual(items.map(i => i.getAttribute('tabindex')), ['-1', '-1', '-1', '33', '-1', '-1']);
    // And back, with a pass run by hand.
    items[3].classList.remove('section-selected');
    items[0].classList.add('section-selected');
    window.__msfsbaFenixEfb.pass();
    assert.deepStrictEqual(items.map(i => i.getAttribute('aria-checked')), ['true', 'false', 'false', 'false', 'false', 'false']);
    assert.deepStrictEqual(items.map(i => i.getAttribute('tabindex')), ['30', '-1', '-1', '-1', '-1', '-1']);

    // Enter and Space press a radio through the page's own click, like every button.
    let clicks = 0;
    items[4].click = () => { clicks++; };
    assert.ok(press(window, items[4], { key: 'Enter' }).defaultPrevented);
    assert.ok(press(window, items[4], { key: ' ' }).defaultPrevented);
    assert.strictEqual(clicks, 2);
  } finally {
    window.close();
  }
});

test('The_radio_group_is_the_container_that_holds_exactly_the_six_codes_and_is_withdrawn_when_it_does_not', async () => {
  const window = await load('arrperf');
  try {
    const items = [...conditionItems(window)];
    const group = items[0].parentElement;
    const last = items[5];
    const parent = last.parentElement;

    // The page drops a code: five items are not the runway condition, and the layer takes back what it wrote; each
    // item has the page's own Tab stop again.
    parent.removeChild(last);
    await turns();
    assert.strictEqual(all(window, '[role=radio],[role=radiogroup]').length, 0);
    assert.strictEqual(all(window, '[aria-checked]').length, 0);
    assert.strictEqual(group.hasAttribute('aria-label'), false);
    assert.ok(items.slice(0, 5).every(i => !i.hasAttribute('data-msfsba')));
    assert.deepStrictEqual(items.slice(0, 5).map(i => i.getAttribute('tabindex')), ['30', '31', '32', '33', '34']);

    // Six again, but not the six codes: still not the runway condition.
    const other = window.document.createElement('div');
    other.className = 'section-item';
    other.textContent = ' Extra ';
    parent.appendChild(other);
    await turns();
    assert.strictEqual(all(window, '[role=radio],[role=radiogroup]').length, 0);

    // The six codes are back: one Tab stop again.
    parent.removeChild(other);
    parent.appendChild(last);
    await turns();
    assert.strictEqual(all(window, '[role=radio]').length, 6);
    assert.strictEqual(group.getAttribute('role'), 'radiogroup');
    assert.deepStrictEqual(items.map(i => i.getAttribute('tabindex')), ['30', '-1', '-1', '-1', '-1', '-1']);
    // Withdrawn once more, the page's own Tab stops come back again, the dropped code's too.
    parent.removeChild(items[0]);
    await turns();
    assert.deepStrictEqual(items.slice(1).map(i => i.getAttribute('tabindex')), ['31', '32', '33', '34', '35']);
  } finally {
    window.close();
  }
});

// The runway condition is one Tab stop; the arrows move through the codes as in any radio group, each move the
// code's own click (it picks that code, as the page's mouse click does) and then the focus on it, wrapping round.
test('The_runway_condition_arrows_press_the_next_code_and_move_to_it_wrapping_round', async () => {
  const window = await load('arrperf');
  try {
    const doc = window.document;
    const items = [...conditionItems(window)];
    const clicks = [];
    items.forEach((item, i) => { item.click = () => { clicks.push(i); }; });
    const arrow = (from, key, init = {}) => press(window, items[from], Object.assign({ key }, init));

    assert.ok(arrow(0, 'ArrowDown').defaultPrevented);
    assert.deepStrictEqual(clicks, [1], 'the next code, once');
    assert.strictEqual(doc.activeElement, items[1], 'and the focus with it');
    assert.ok(arrow(1, 'ArrowRight').defaultPrevented);
    assert.deepStrictEqual(clicks, [1, 2]);
    assert.strictEqual(doc.activeElement, items[2]);
    assert.ok(arrow(2, 'ArrowUp').defaultPrevented);
    assert.ok(arrow(1, 'ArrowLeft').defaultPrevented);
    assert.deepStrictEqual(clicks, [1, 2, 1, 0]);
    assert.strictEqual(doc.activeElement, items[0]);

    // Round the ends.
    assert.ok(arrow(0, 'ArrowUp').defaultPrevented);
    assert.deepStrictEqual(clicks.slice(-1), [5]);
    assert.strictEqual(doc.activeElement, items[5]);
    assert.ok(arrow(5, 'ArrowRight').defaultPrevented);
    assert.deepStrictEqual(clicks.slice(-1), [0]);
    assert.strictEqual(doc.activeElement, items[0]);

    // A held arrow moves once (as a held Enter presses once); an arrow with Alt, Ctrl or the Windows key, and any other
    // key, is the page's.
    const count = clicks.length;
    assert.ok(arrow(0, 'ArrowDown', { repeat: true }).defaultPrevented);
    for (const init of [{ altKey: true }, { ctrlKey: true }, { metaKey: true }]) {
      assert.strictEqual(arrow(0, 'ArrowDown', init).defaultPrevented, false);
    }
    assert.strictEqual(arrow(0, 'a').defaultPrevented, false);
    assert.strictEqual(arrow(0, 'Home').defaultPrevented, false);
    assert.strictEqual(clicks.length, count);
    assert.strictEqual(doc.activeElement, items[0]);
    // An arrow on any other button the layer made is the page's too.
    const option = doc.querySelector('div.one-click-change');
    let optionClicks = 0;
    option.click = () => { optionClicks++; };
    assert.strictEqual(press(window, option, { key: 'ArrowDown' }).defaultPrevented, false);
    assert.strictEqual(optionClicks, 0);
    assert.strictEqual(doc.activeElement, items[0]);

    // Enter and Space still press the code itself, and move nothing.
    assert.ok(arrow(3, 'Enter').defaultPrevented);
    assert.ok(arrow(3, ' ').defaultPrevented);
    assert.deepStrictEqual(clicks.slice(-2), [3, 3]);
    assert.strictEqual(doc.activeElement, items[0]);

    // A code the page disables is passed over by the arrows, and is never the Tab stop.
    items[2].setAttribute('aria-disabled', 'true');
    await turns();
    assert.ok(arrow(1, 'ArrowDown').defaultPrevented);
    assert.deepStrictEqual(clicks.slice(-1), [3]);
    assert.strictEqual(doc.activeElement, items[3]);
    items[0].classList.remove('section-selected');
    items[2].classList.add('section-selected');
    await turns();
    assert.deepStrictEqual(items.map(i => i.getAttribute('tabindex')), ['30', '-1', '-1', '-1', '-1', '-1'],
      'the checked code is disabled: the first one that is not is the Tab stop');
  } finally {
    window.close();
  }
});

test('Typing_in_a_text_box_is_unaffected_by_the_runway_condition_arrows', async () => {
  const window = await load('arrperf');
  try {
    const doc = window.document;
    const items = [...conditionItems(window)];
    let clicks = 0;
    items.forEach(item => { item.click = () => { clicks++; }; });
    // The Landing page's own text boxes keep their arrows.
    const icao = all(window, 'app-keyboard-input input')[0];
    icao.focus();
    for (const key of ['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight']) {
      assert.strictEqual(press(window, icao, { key }).defaultPrevented, false, key);
    }
    // So does a text box inside a code.
    const box = doc.createElement('input');
    items[1].appendChild(box);
    await turns();
    assert.strictEqual(all(window, '[role=radio]').length, 6, 'still the runway condition');
    for (const key of ['ArrowDown', 'ArrowRight']) {
      assert.strictEqual(press(window, box, { key }).defaultPrevented, false, key);
    }
    assert.strictEqual(clicks, 0);
    assert.strictEqual(doc.activeElement, icao);
  } finally {
    window.close();
  }
});

test('A_runway_condition_tab_stop_the_page_changes_is_the_pages_when_the_group_is_withdrawn', async () => {
  const window = await load('arrperf');
  try {
    const items = [...conditionItems(window)];
    const parent = items[0].parentElement;
    // The page gives one code a Tab stop of its own; the next pass would be the layer's, but the page drops a code
    // first: that pass withdraws the group, and the page's value stays where the page put it.
    items[1].setAttribute('tabindex', '7');
    parent.removeChild(items[5]);
    await turns();
    assert.deepStrictEqual(items.slice(0, 5).map(i => i.getAttribute('tabindex')), ['30', '7', '32', '33', '34']);
    // A code the page left without a tabindex of its own gets none back.
    const bare = await load('arrperf', { layer: false });
    try {
      const codes = [...conditionItems(bare)];
      codes[2].removeAttribute('tabindex');
      bare.eval(fs.readFileSync(LAYER, 'utf8'));
      await turns();
      assert.strictEqual(codes[2].getAttribute('tabindex'), '-1');
      codes[0].parentElement.removeChild(codes[5]);
      await turns();
      assert.strictEqual(codes[2].hasAttribute('tabindex'), false);
    } finally {
      bare.close();
    }
  } finally {
    window.close();
  }
});

// A stop whose code has no Tab stop of the page's own (no tabindex, or a negative one) takes 0.
test('A_runway_condition_stop_without_a_tab_stop_of_the_pages_takes_0', async () => {
  const window = await load('arrperf', { layer: false });
  try {
    const codes = [...conditionItems(window)];
    codes[2].removeAttribute('tabindex');
    codes[4].setAttribute('tabindex', '-1');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    codes[0].classList.remove('section-selected');
    codes[2].classList.add('section-selected');
    await turns();
    assert.deepStrictEqual(codes.map(c => c.getAttribute('tabindex')), ['-1', '-1', '0', '-1', '-1', '-1']);
    codes[2].classList.remove('section-selected');
    codes[4].classList.add('section-selected');
    await turns();
    assert.deepStrictEqual(codes.map(c => c.getAttribute('tabindex')), ['-1', '-1', '-1', '-1', '0', '-1']);
    // Withdrawn, each code has what the page gave it.
    codes[0].parentElement.removeChild(codes[5]);
    await turns();
    assert.deepStrictEqual(codes.slice(0, 5).map(c => c.getAttribute('tabindex')), ['30', '31', null, '33', '-1']);
  } finally {
    window.close();
  }
});

// The page's sequential Tab order as HTML defines it: elements with a positive tabindex first, in ascending order (equal
// values in page order), then those with tabindex 0 and the natively focusable ones, in page order. A disabled control,
// a hidden input and anything under the page's `hidden` attribute are left out; jsdom has no CSS, so nothing else is.
function tabOrder(window) {
  const focusable = [...window.document.querySelectorAll('*')].filter(el => {
    if (el.closest('[hidden]') || el.disabled || el.tabIndex < 0) return false;
    if (el.localName === 'input' && el.type === 'hidden') return false;
    const natural = ['input', 'button', 'select', 'textarea'].includes(el.localName) ||
      (el.localName === 'a' && el.hasAttribute('href'));
    return natural || el.hasAttribute('tabindex');
  });
  const positive = focusable.filter(el => el.tabIndex > 0).sort((a, b) => a.tabIndex - b.tabIndex);
  return [...positive, ...focusable.filter(el => el.tabIndex === 0)];
}

// The Landing page orders its controls with positive tabindex values (ICAO CODE 10, RUNWAY 20, the codes 30 to 35,
// the wind, temperature and pressure boxes 70 to 90 ...), and a browser visits positive values first. The runway
// condition's one stop keeps the code's own value, so the group is reached right after RUNWAY, as it was before.
test('The_runway_condition_is_reached_right_after_RUNWAY_in_the_Landing_Tab_order', async () => {
  const window = await load('arrperf');
  try {
    const items = [...conditionItems(window)];
    const [icao] = all(window, 'app-keyboard-input input');
    const runway = window.document.querySelector('app-arrival mat-select');
    let order = tabOrder(window);
    assert.strictEqual(order[0], icao, 'ICAO CODE first');
    assert.strictEqual(order[1], runway, 'then RUNWAY');
    assert.strictEqual(order[2], items[0], 'then the runway condition, at its checked code');
    assert.deepStrictEqual(items.map(i => order.includes(i)), [true, false, false, false, false, false], 'one stop');
    assert.strictEqual(nameOf(order[2]), '6 DRY');

    // The checked code moves: the stop moves with it, still right after RUNWAY.
    items[0].classList.remove('section-selected');
    items[3].classList.add('section-selected');
    await turns();
    order = tabOrder(window);
    assert.strictEqual(order[order.indexOf(runway) + 1], items[3]);
    assert.deepStrictEqual(items.map(i => order.includes(i)), [false, false, false, true, false, false]);
  } finally {
    window.close();
  }
});

test('A_role_or_state_the_page_gave_the_runway_condition_is_kept', async () => {
  const window = await load('arrperf', { layer: false });
  try {
    const items = [...conditionItems(window)];
    items[1].setAttribute('role', 'option');
    items[2].setAttribute('aria-checked', 'mixed');
    items[3].setAttribute('aria-label', 'Medium');
    items[0].parentElement.setAttribute('role', 'listbox');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(items[0].parentElement.getAttribute('role'), 'listbox');
    assert.strictEqual(items[1].getAttribute('role'), 'option');
    assert.strictEqual(items[2].getAttribute('aria-checked'), 'mixed');
    assert.strictEqual(items[3].getAttribute('aria-label'), 'Medium');
    // The ones the page left alone are radios as usual.
    assert.strictEqual(items[0].getAttribute('role'), 'radio');
    assert.strictEqual(items[0].getAttribute('aria-checked'), 'true');
  } finally {
    window.close();
  }
});

test('Landing_options_and_procedures_are_buttons', async () => {
  const window = await load('arrperf');
  try {
    const options = all(window, 'div.one-click-change');
    assert.deepStrictEqual(options.map(nameOf), ['LOW A/BRAKE', 'IDLE/NO REV', 'MANUAL LAND', 'FLAP FULL', 'A/THR ON']);
    for (const option of options) {
      assert.strictEqual(option.getAttribute('role'), 'button');
    }
    assert.deepStrictEqual(options.map(o => o.getAttribute('tabindex')), ['110', '120', '130', '140', '150'], 'the page\'s own Tab stops');

    const procedures = all(window, 'div.arrival-procedure-item');
    assert.deepStrictEqual(procedures.map(nameOf), [
      'Electrical System', 'Flight Controls System', 'Slats and Flaps System', 'Hydraulics System',
      'Anti-Ice System', 'Braking System', 'Navigation System', 'Bleed System', 'Engine System'
    ]);
    for (const procedure of procedures) {
      assert.strictEqual(procedure.getAttribute('role'), 'button');
      assert.strictEqual(procedure.tabIndex, 0, 'a bare div the Tab key skipped is a Tab stop now');
    }

    // Enter and Space press one through the page's own click.
    for (const el of [options[2], procedures[5]]) {
      let clicks = 0;
      el.click = () => { clicks++; };
      assert.ok(press(window, el, { key: 'Enter' }).defaultPrevented);
      assert.ok(press(window, el, { key: ' ' }).defaultPrevented);
      assert.strictEqual(clicks, 2);
    }
  } finally {
    window.close();
  }
});

// A procedure ("Electrical System") opens a sub-page in place (the route stays #/efb/arrperf). Its only way back is
// the title row, whose arrow is an icon and whose click (the page's, on the row) returns to the main Landing page: it is
// the button "Back, <title>". The sub-page's items are the procedure's conditions, each with a tick box the page marks
// section-selected: check boxes named by their text.
const ELECTRICAL = [
  'AC BUS 1 FAULT', 'DC BUS 2 FAULT', 'DC BUS 1+2 FAULT', 'DC ESS BUS FAULT (if no Ice Accretion)',
  'DC ESS BUS FAULT (if Ice Accretion)', 'DC ESS SHED BUS (if Ice Accretion)', 'DC EMER CONFIG (calculated with 140kt min)',
  'EMER CONFIG (calculated with 140kt min)'
];
const squash = text => text.replace(/\s+/g, ' ').trim();

test('The_Landing_sub_page_has_a_Back_button_and_tick_boxes', async () => {
  const window = await load('arrperf-electrical');
  try {
    const doc = window.document;
    const title = doc.querySelector('div.arrival-title');
    const arrow = title.querySelector('div.arrival-back');
    assert.ok(arrow, 'the capture has the arrow in the title row');
    assert.strictEqual(title.getAttribute('role'), 'button');
    assert.strictEqual(title.tabIndex, 0);
    assert.strictEqual(nameOf(title), 'Back, Electrical System');
    assert.ok(['role', 'tabindex', 'aria-label', 'data-msfsba'].every(a => !arrow.hasAttribute(a)), 'the arrow is not the button');
    assert.strictEqual(arrow.querySelector('i').getAttribute('aria-hidden'), 'true', 'its icon-font glyph is hidden');
    // Enter and Space are the row's own click; that click is the one a mouse click makes, on the row's own handler.
    let clicks = 0;
    title.click = () => { clicks++; };
    assert.ok(press(window, title, { key: 'Enter' }).defaultPrevented);
    assert.ok(press(window, title, { key: ' ' }).defaultPrevented);
    assert.strictEqual(clicks, 2);
    delete title.click;
    let backs = 0;
    title.addEventListener('click', () => { backs++; });
    press(window, title, { key: 'Enter' });
    assert.strictEqual(backs, 1);

    const items = all(window, 'div.arrival-procedure-item');
    assert.deepStrictEqual(items.map(i => squash(i.querySelector('span').textContent)), ELECTRICAL, 'the page\'s own texts');
    assert.deepStrictEqual(items.map(nameOf), ELECTRICAL);
    assert.ok(items.every(i => i.getAttribute('role') === 'checkbox' && i.tabIndex === 0));
    assert.ok(items.every(i => !i.classList.contains('section-selected')), 'nothing ticked, as captured');
    assert.deepStrictEqual(items.map(i => i.getAttribute('aria-checked')), Array(8).fill('false'));
    assert.ok(items.every(i => i.querySelector('.arrival-checkbox i').getAttribute('aria-hidden') === 'true'), 'the tick glyph is hidden');

    // The page ticks one and unticks it: the state follows its class.
    items[2].classList.add('section-selected');
    await turns();
    assert.deepStrictEqual(items.map(i => i.getAttribute('aria-checked')), ['false', 'false', 'true', 'false', 'false', 'false', 'false', 'false']);
    items[2].classList.remove('section-selected');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(items[2].getAttribute('aria-checked'), 'false');

    // Enter and Space tick through the page's own click.
    let ticks = 0;
    items[5].click = () => { ticks++; };
    assert.ok(press(window, items[5], { key: 'Enter' }).defaultPrevented);
    assert.ok(press(window, items[5], { key: ' ' }).defaultPrevented);
    assert.strictEqual(ticks, 2);
  } finally {
    window.close();
  }
});

test('The_main_Landing_page_has_no_Back_button_and_its_procedures_are_not_tick_boxes', async () => {
  const window = await load('arrperf');
  try {
    const titles = all(window, 'div.arrival-title');
    assert.ok(titles.length > 0, 'the main page has title rows too');
    assert.ok(titles.every(t => !t.querySelector('div.arrival-back') && !t.hasAttribute('role') && !t.hasAttribute('tabindex')));
    assert.strictEqual(all(window, '[role=checkbox]').length, 0);
    assert.ok(all(window, 'div.arrival-procedure-item').every(i => i.getAttribute('role') === 'button' && !i.hasAttribute('aria-checked')));
  } finally {
    window.close();
  }
});

test('A_Landing_item_or_title_that_gains_or_loses_its_tick_box_or_arrow_follows_and_the_pages_own_stays', async () => {
  const window = await load('arrperf-electrical', { layer: false });
  try {
    const doc = window.document;
    const title = doc.querySelector('div.arrival-title');
    const arrow = title.querySelector('div.arrival-back');
    const [first, second, third] = all(window, 'div.arrival-procedure-item');
    third.setAttribute('role', 'option');
    third.setAttribute('aria-checked', 'mixed');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(third.getAttribute('role'), 'option', 'the page\'s own role');
    assert.strictEqual(third.getAttribute('aria-checked'), 'mixed', 'the page\'s own state');

    // An item loses its tick box (the test takes it out): it is a button, with no checked state; and back.
    const tick = first.querySelector('.arrival-checkbox');
    first.removeChild(tick);
    await turns();
    assert.strictEqual(first.getAttribute('role'), 'button');
    assert.strictEqual(first.hasAttribute('aria-checked'), false);
    assert.strictEqual(nameOf(first), 'AC BUS 1 FAULT');
    first.appendChild(tick);
    await turns();
    assert.strictEqual(first.getAttribute('role'), 'checkbox');
    assert.strictEqual(first.getAttribute('aria-checked'), 'false');
    assert.strictEqual(second.getAttribute('role'), 'checkbox');

    // The title row loses its arrow: it is not a button any more (what the layer wrote is taken back); and back.
    title.removeChild(arrow);
    await turns();
    assert.ok(['role', 'tabindex', 'aria-label', 'data-msfsba'].every(a => !title.hasAttribute(a)));
    title.insertBefore(arrow, title.firstChild);
    await turns();
    assert.strictEqual(nameOf(title), 'Back, Electrical System');
  } finally {
    window.close();
  }
});

test('Landing_fields_are_named_by_their_label_or_unit_and_the_weight_box_by_its_placeholder_and_unit', async () => {
  const window = await load('arrperf');
  try {
    const fields = all(window, FIELDS);
    assert.strictEqual(fields.length, 6);
    // ICAO CODE and RUNWAY have a label of their own; wind, temperature and pressure only the unit written
    // after the box (their placeholders are dashes); the landing weight its placeholder "LANDING WT" and the
    // unit "T" (its FMS button is a button, not part of the name).
    const NAMES = ['ICAO CODE', 'RUNWAY', 'KT', '°C', 'HPA', 'LANDING WT, T'];
    assert.deepStrictEqual(fields.map(f => f.getAttribute('aria-label')), NAMES);
    assert.deepStrictEqual(fields.map(nameOf), NAMES);
    assert.deepStrictEqual(all(window, 'app-keyboard-input input').map(i => i.getAttribute('placeholder')),
      ['----', '---/--', '--', '----', 'LANDING WT'], 'the placeholders are the page\'s own and stay');
    const runway = fields[1];
    assert.strictEqual(runway.getAttribute('role'), 'combobox');
    assert.strictEqual(runway.getAttribute('aria-labelledby'), 'mat-select-12 mat-select-value-13');
    // The page's own buttons keep their own names.
    assert.strictEqual(nameOf(window.document.querySelector('button.btn-from-fms')), 'FMS');
    assert.strictEqual(nameOf(window.document.querySelector('button.arrival-btn-live-wx')), 'SYNC LIVE WX');
  } finally {
    window.close();
  }
});

// The Landing page's pictures have no alt: the logo (a data: PNG), the FMS button's picture and the one-click
// options' pictures. They are hidden, and the buttons holding them keep their names. The arrperf capture has them
// removed (the old snippet removed every <img>), so they are put back here as the live page has them; the sub-page
// capture has the logo itself.
test('Unlabeled_Landing_pictures_are_hidden_and_the_buttons_holding_them_keep_their_names', async () => {
  const window = await load('arrperf', { layer: false });
  let sub = null;
  try {
    const doc = window.document;
    const put = (parent, cls, src) => {
      const img = doc.createElement('img');
      if (cls) img.className = cls;
      img.setAttribute('src', src);
      parent.insertBefore(img, parent.firstChild);
      return img;
    };
    const logo = put(doc.querySelector('div.arrival-logo'), '', 'data:,');
    const fms = put(doc.querySelector('button.btn-from-fms'), 'fms-img', '/assets/img/fms.png');
    const ones = all(window, 'div.one-click-change').map(o => put(o, 'one-click-change-img', '/assets/img/up.png'));
    // A picture the page describes is the page's.
    const described = put(doc.querySelector('div.arrival-logo'), 'described', '/assets/img/fenix.png');
    described.setAttribute('alt', 'Fenix');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();

    assert.ok([logo, fms, ...ones].every(img => img.getAttribute('aria-hidden') === 'true'));
    assert.strictEqual(described.hasAttribute('aria-hidden'), false);
    assert.strictEqual(nameOf(doc.querySelector('button.btn-from-fms')), 'FMS');
    assert.deepStrictEqual(all(window, 'div.one-click-change').map(nameOf),
      ['LOW A/BRAKE', 'IDLE/NO REV', 'MANUAL LAND', 'FLAP FULL', 'A/THR ON']);

    sub = await load('arrperf-electrical');
    const subLogo = sub.document.querySelector('div.arrival-logo img');
    assert.strictEqual(subLogo.getAttribute('src'), 'data:,', 'as captured');
    assert.strictEqual(subLogo.getAttribute('aria-hidden'), 'true');
  } finally {
    window.close();
    if (sub) sub.close();
  }
});

// "SYNC LIVE WX" holds an icon-font glyph before its text, which a screen reader met as an unlabeled
// line of its own. The glyph is hidden; the button keeps its text as its name. So is the intersection button's arrow.
// Takeoff's button also breaks its content with a <br> after the glyph, so NVDA still showed it over two lines, the
// first empty: it is named by its own text (below). Landing's has no <br> and keeps the page's text as its name.
for (const [fixture, selector, glyphs, label] of [
  ['depperf', 'button.btn-live-wx', ['i.fas.fa-chevron-down', 'i.fas.fa-cog', 'i.fas.fa-2x.fa-sync-alt', 'i.select-arrow.fas.fa-arrow-right'], 'SYNC LIVE WX'],
  ['arrperf', 'button.arrival-btn-live-wx', ['i.fas.fa-chevron-down', 'i.fas.fa-cog', 'i.fas.fa-sync-alt'], null]
]) {
  test('The_SYNC_LIVE_WX_glyph_is_hidden_and_the_button_keeps_its_text_on_' + fixture, async () => {
    const bare = await load(fixture, { layer: false });
    const window = await load(fixture);
    try {
      const sync = window.document.querySelector(selector);
      assert.strictEqual(sync.querySelector('i').getAttribute('aria-hidden'), 'true');
      assert.strictEqual(nameOf(sync), 'SYNC LIVE WX');
      assert.strictEqual(sync.querySelector('br') !== null, label !== null, 'as captured');
      assert.strictEqual(sync.getAttribute('aria-label'), label);
      // Only glyphs inside controls are hidden here (the wind arrows on Landing's results are in no control).
      assert.deepStrictEqual(hiddenByLayer(bare, window).filter(h => /^i[.#]/.test(h)), glyphs);
    } finally {
      bare.close();
      window.close();
    }
  });
}

// A button whose own content holds a <br> is named by its own text, the <br> a space, so NVDA presents it as one
// item. The label is the layer's own: it follows the text, goes when the <br> or the text goes, and a label the page
// gives is the page's.
test('A_button_whose_content_holds_a_line_break_is_named_by_its_own_text', async () => {
  const window = await load('depperf');
  try {
    const doc = window.document;
    const sync = doc.querySelector('button.btn-live-wx');
    const br = sync.querySelector('br');
    assert.strictEqual(sync.getAttribute('aria-label'), 'SYNC LIVE WX');
    assert.strictEqual(sync.getAttribute('data-msfsba'), '1');
    assert.ok(!sync.hasAttribute('role') && sync.getAttribute('tabindex') === '140', 'a real button: a name, nothing else');

    // The <br> goes: the label goes with it; it comes back with the <br>.
    sync.removeChild(br);
    await turns();
    assert.strictEqual(sync.hasAttribute('aria-label'), false);
    assert.strictEqual(sync.hasAttribute('data-msfsba'), false);
    sync.insertBefore(br, sync.lastChild);
    await turns();
    assert.strictEqual(sync.getAttribute('aria-label'), 'SYNC LIVE WX');

    // The text changes: the label follows; the text goes: the label goes.
    const text = [...sync.childNodes].find(n => n.nodeType === 3 && n.nodeValue.trim());
    text.nodeValue = ' SYNC WEATHER ';
    await turns();
    assert.strictEqual(sync.getAttribute('aria-label'), 'SYNC WEATHER');
    text.nodeValue = ' ';
    await turns();
    assert.strictEqual(sync.hasAttribute('aria-label'), false);

    // A page's own label is never touched.
    text.nodeValue = 'SYNC LIVE WX';
    sync.setAttribute('aria-label', 'Live weather');
    await turns();
    assert.strictEqual(sync.getAttribute('aria-label'), 'Live weather');
  } finally {
    window.close();
  }
});

test('Line_break_buttons_elsewhere_keep_the_names_they_had', async () => {
  let mass = null;
  let home = null;
  let switcher = null;
  try {
    mass = await load('mass-balance');
    const buttons = all(mass, 'div.action-button-cell > button');
    assert.ok(buttons.every(b => b.querySelector('br')), 'as captured: each breaks its text');
    assert.deepStrictEqual(buttons.map(nameOf), ['Reset All', 'Resend Loadsheet', 'Load Aircraft', 'SEND TO MCDU']);
    assert.deepStrictEqual(buttons.map(b => b.getAttribute('aria-label')), ['Reset All', 'Resend Loadsheet', 'Load Aircraft', 'SEND TO MCDU']);
    // The home tiles hold a <br> too and keep their titles; the switcher's icons hold one and no text, and keep the
    // names the layer's tile-title list gives them.
    home = await load('home');
    assert.deepStrictEqual(all(home, '#appContainer > div.appIcon').map(nameOf),
      ['Fenix', 'Takeoff', 'Landing', 'Charts', 'Documents', 'Failures', 'Settings', 'MCDU', 'FAP']);
    switcher = await load('switcher');
    assert.deepStrictEqual(all(switcher, 'img.app-img').map(img => nameOf(img.parentElement)),
      ['Fenix', 'Takeoff', 'Landing', 'Charts', 'Documents', 'Failures', 'Settings', 'MCDU', 'FAP']);
  } finally {
    if (mass) mass.close();
    if (home) home.close();
    if (switcher) switcher.close();
  }
});

// ---- The layer's own rules ----

for (const fixture of ['depperf', 'arrperf', 'arrperf-electrical']) {
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
  depperf: { handled: 2 + 12 + 3 + 1, listeners: 2, note: 'home, chevron; six boxes and six drop-downs; the three "not computed" speeds; SYNC LIVE WX (named from its text, which holds a <br>)' },
  arrperf: { handled: 2 + 6 + 1 + 6 + 5 + 9, listeners: 2 + 6 + 5 + 9, note: 'home, chevron; six fields; the group and six radios; five options; nine procedures' },
  // The sub-page capture also holds the app switcher, closed, with its nine icons (as mass-balance does).
  'arrperf-electrical': { handled: 2 + 9 + 1 + 8, listeners: 2 + 9 + 1 + 8, note: 'home, chevron; the nine icons of the closed switcher; Back; eight tick boxes' }
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

      assert.strictEqual(all(window, '[data-msfsba]').length, expected.handled, expected.note);
      for (const [el, n] of counts) assert.strictEqual(n, 1, 'one listener on ' + el.localName + '.' + el.className);
      assert.strictEqual(counts.size, expected.listeners, 'only the pressable divs got a key handler');
    } finally {
      window.close();
    }
  });
}

// Each rule is its own: one that throws never skips the others. A rule is made to fail by its own query of
// the whole page (the layer's rules ask document.body); every other rule still does its work.
const ISOLATION = {
  depperf: [
    ['mcdu-screen', '.mcdu-screen[aria-owns]', 1],
    ['app-keyboard-input', 'app-keyboard-input input[aria-label]', 6],
    ['mat-select', 'mat-select[aria-label]', 6]
  ],
  arrperf: [
    ['one-click-change', '.one-click-change[role=button]', 5],
    ['arrival-procedure-item', '.arrival-procedure-item[role=button]', 9],
    ['section-group', '[role=radio]', 6]
  ],
  'arrperf-electrical': [
    ['arrival-title', 'div.arrival-title[role=button]', 1],
    ['arrival-procedure-item', '.arrival-procedure-item[role=checkbox]', 8]
  ]
};
for (const [fixture, cases] of Object.entries(ISOLATION)) {
  for (const [failing] of cases) {
    test('A_failing_rule_does_not_skip_the_others_on_' + fixture + '_when_' + failing.replace(/-/g, '_') + '_fails', async () => {
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
