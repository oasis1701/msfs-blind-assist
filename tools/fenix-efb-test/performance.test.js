'use strict';
// The Takeoff (depperf) and Landing (arrperf) calculators, checked against the LIVE captures
// (fixtures/depperf.html, fixtures/arrperf.html; Fenix 3.0.0.7438, A320-232 IAE, kilograms and hPa): the
// fields named by the page's own labels, the Takeoff results grid read as "<label>: <value>" (ruling R14),
// the Landing runway condition as a radio group, the Landing options and procedures as buttons.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const { computeAccessibleName } = require('dom-accessibility-api');
const { load, loadCounting, press, attributesOf, turns, LAYER } = require('./load');

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

// The first text the page gives a field without a label of its own: the header of its table column, else
// the unit written after it; a field with neither stays unnamed, and the placeholder is never used.
test('A_takeoff_field_without_its_own_label_takes_the_column_header_above_it_else_its_unit_and_never_the_placeholder', async () => {
  const window = await load('depperf');
  try {
    const doc = window.document;
    const holder = doc.createElement('div');
    holder.innerHTML =
      '<div class="in-table"><table><tr><th>Cross <b>wind</b></th><th>Gust</th></tr>' +
      '<tr><td><app-keyboard-input placeholder="x"><input placeholder="---/--"></app-keyboard-input></td>' +
      '<td><app-keyboard-input placeholder="x"><input placeholder="--"></app-keyboard-input></td></tr></table></div>' +
      '<div class="section-item"><app-keyboard-input placeholder="x"><input placeholder="--"></app-keyboard-input> KT </div>' +
      '<div class="section-item"><div class="boxed"><div class="inner"><app-keyboard-input placeholder="x"><input placeholder="LANDING WT"></app-keyboard-input></div></div>' +
      '<button>FMS</button><div class="unit"> T <!----></div></div>' +
      '<div class="bare"><app-keyboard-input placeholder="x"><input placeholder="Nothing around me"></app-keyboard-input></div>';
    doc.querySelector('app-departure').appendChild(holder);
    await turns();
    const inputs = [...holder.querySelectorAll('input')];
    // The last one has no text anywhere but its placeholder: the layer leaves it unnamed and does not give it
    // the placeholder as a name (a reader that falls back to the placeholder still can, as for any such box).
    assert.deepStrictEqual(inputs.map(i => i.getAttribute('aria-label')), ['Cross wind', 'Gust', 'KT', 'T', null]);
    assert.deepStrictEqual(inputs.slice(0, 4).map(nameOf), ['Cross wind', 'Gust', 'KT', 'T']);
    assert.strictEqual(inputs[4].getAttribute('placeholder'), 'Nothing around me', 'the page\'s own placeholder stays');
    // The real fields are untouched by the new ones.
    assert.strictEqual(all(window, 'app-keyboard-input input')[0].getAttribute('aria-label'), 'ICAO CODE');
  } finally {
    window.close();
  }
});

// ---- The results grid (ruling R14) ----

const rowsOf = window => [...window.document.querySelector('.mcdu-screen').children].filter(el => el.classList.contains('row'));
const cellsOf = row => [...row.children];

test('Takeoff_results_read_label_and_value', async () => {
  const window = await load('depperf');
  try {
    const rows = rowsOf(window);
    assert.strictEqual(rows.length, 10, 'five label rows, each with its value row under it');
    const expected = [
      ['V1: not computed', 'FLP RETR: F=---', 'STOP MARGIN: ----'],
      ['VR: not computed', 'SLT RETR: S=---', 'TO SHIFT: [M] [ ]*'],
      ['V2: not computed', 'CLEAN: O=---', 'FLAPS/THS: []/[ ]'],
      ['TRANS ALT: ----', 'FLEX TO TEMP: []°'],
      ['THR RED/ACC: ----/----', 'ENG OUT ACC: ----']
    ];
    for (let r = 0; r < 5; r++) {
      const labels = cellsOf(rows[2 * r]);
      const values = cellsOf(rows[2 * r + 1]);
      assert.strictEqual(values.length, expected[r].length);
      assert.strictEqual(labels.length, values.length);
      for (let c = 0; c < values.length; c++) {
        // One named object per value cell: role img (a generic div's label is not read in browse mode).
        assert.strictEqual(values[c].getAttribute('role'), 'img', expected[r][c]);
        assert.strictEqual(values[c].getAttribute('aria-label'), expected[r][c]);
        assert.strictEqual(nameOf(values[c]), expected[r][c]);
        // The label above is not read twice.
        assert.strictEqual(labels[c].getAttribute('aria-hidden'), 'true', expected[r][c]);
        assert.ok(!labels[c].hasAttribute('role') && !labels[c].hasAttribute('aria-label'));
      }
    }
    // The title above the grid is still read.
    const title = window.document.querySelector('.mcdu-screen > .text-center');
    assert.strictEqual(title.hasAttribute('aria-hidden'), false);
    assert.strictEqual(textOf(window)(title), 'TAKE OFF RWY');
    // Nothing else on the page became an image or was hidden.
    assert.strictEqual(all(window, '[role=img]').length, 13);
    assert.strictEqual(all(window, '[aria-hidden=true]').length, 13);
  } finally {
    window.close();
  }
});

test('Takeoff_results_follow_the_page_when_Calculate_changes_them_in_place', async () => {
  const window = await load('depperf');
  try {
    const doc = window.document;
    const rows = rowsOf(window);
    const [v1, flp, stop] = cellsOf(rows[1]);
    const [v1Label, flpLabel] = cellsOf(rows[0]);

    // Angular updates a text node in place: the label follows.
    const flpText = [...flp.firstElementChild.childNodes].find(n => n.nodeType === 3);
    flpText.nodeValue = ' F=161 ';
    await turns();
    assert.strictEqual(flp.getAttribute('aria-label'), 'FLP RETR: F=161');

    // The three yellow boxes give way to a number, and come back.
    v1.firstElementChild.textContent = ' 143 ';
    await turns();
    assert.strictEqual(v1.getAttribute('aria-label'), 'V1: 143');
    assert.strictEqual(nameOf(v1), 'V1: 143');
    v1.firstElementChild.textContent = '';
    for (let i = 0; i < 3; i++) {
      const box = doc.createElement('span');
      box.className = 'small-yellow-box';
      v1.firstElementChild.appendChild(box);
    }
    await turns();
    assert.strictEqual(v1.getAttribute('aria-label'), 'V1: not computed');

    // The label above changes its text: the name follows it.
    flpLabel.firstElementChild.firstChild.nodeValue = 'FLP RETR (KT)';
    await turns();
    assert.strictEqual(flp.getAttribute('aria-label'), 'FLP RETR (KT): F=161');

    // A value cell that empties is not an image any more: what the layer wrote is taken back and the
    // label above is read again.
    stop.firstElementChild.textContent = '';
    await turns();
    assert.strictEqual(stop.hasAttribute('role'), false);
    assert.strictEqual(stop.hasAttribute('aria-label'), false);
    assert.strictEqual(stop.hasAttribute('data-msfsba'), false);
    const stopLabel = cellsOf(rows[0])[2];
    assert.strictEqual(stopLabel.hasAttribute('aria-hidden'), false);
    assert.strictEqual(textOf(window)(stopLabel), 'STOP MARGIN');
    // The rest carried on.
    assert.strictEqual(v1Label.getAttribute('aria-hidden'), 'true');
    assert.strictEqual(flpLabel.getAttribute('aria-hidden'), 'true');

    // The value comes back: so does the name.
    stop.firstElementChild.textContent = ' 1450 ';
    await turns();
    assert.strictEqual(stop.getAttribute('aria-label'), 'STOP MARGIN: 1450');
    assert.strictEqual(stopLabel.getAttribute('aria-hidden'), 'true');

    // A pass run by hand sees the same: change the text, run it, and the label follows.
    flpText.nodeValue = ' F=158 ';
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(flp.getAttribute('aria-label'), 'FLP RETR (KT): F=158');
  } finally {
    window.close();
  }
});

test('A_results_grid_that_does_not_line_up_is_left_alone', async () => {
  const window = await load('depperf', { layer: false });
  try {
    // A label row and value row of different widths: no cell is paired by guess.
    const rows = rowsOf(window);
    rows[1].removeChild(rows[1].lastElementChild);
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    const [firstLabels, firstValues] = [cellsOf(rows[0]), cellsOf(rows[1])];
    assert.ok(firstValues.every(v => !v.hasAttribute('role') && !v.hasAttribute('aria-label')));
    assert.ok(firstLabels.every(l => !l.hasAttribute('aria-hidden')));
    // The other pairs still line up.
    assert.strictEqual(cellsOf(rows[3])[0].getAttribute('aria-label'), 'VR: not computed');
    assert.strictEqual(cellsOf(rows[2])[0].getAttribute('aria-hidden'), 'true');
  } finally {
    window.close();
  }
});

test('A_role_label_or_hidden_state_the_page_set_in_the_results_grid_is_never_overridden', async () => {
  const window = await load('depperf', { layer: false });
  try {
    const rows = rowsOf(window);
    const [v1, flp, stop] = cellsOf(rows[1]);
    const [, , stopLabel] = cellsOf(rows[0]);
    const vrLabel = cellsOf(rows[2])[0];
    const vrValue = cellsOf(rows[3])[0];
    stop.setAttribute('role', 'status');                         // the page's own role
    flp.setAttribute('aria-label', 'Flap retraction speed');     // the page's own label
    vrLabel.setAttribute('aria-hidden', 'false');                // the page's own hidden state
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();

    assert.strictEqual(stop.getAttribute('role'), 'status');
    assert.strictEqual(stop.hasAttribute('aria-label'), false, 'a cell with a role of the page\'s is not named');
    assert.strictEqual(stopLabel.hasAttribute('aria-hidden'), false, 'its label is not carried, so it stays read');

    assert.strictEqual(flp.getAttribute('aria-label'), 'Flap retraction speed');
    assert.strictEqual(cellsOf(rows[0])[1].hasAttribute('aria-hidden'), false, 'the page\'s label is not the grid\'s: label above stays read');

    assert.strictEqual(vrLabel.getAttribute('aria-hidden'), 'false');
    assert.strictEqual(vrValue.getAttribute('aria-label'), 'VR: not computed');
    assert.strictEqual(v1.getAttribute('aria-label'), 'V1: not computed');
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

    // Every radio stays a Tab stop: the page gives each its own tabindex and the layer writes none.
    assert.deepStrictEqual(items.map(i => i.getAttribute('tabindex')), ['30', '31', '32', '33', '34', '35']);

    // The page moves section-selected to "3 MED": aria-checked follows.
    items[0].classList.remove('section-selected');
    items[3].classList.add('section-selected');
    await turns();
    assert.deepStrictEqual(items.map(i => i.getAttribute('aria-checked')), ['false', 'false', 'false', 'true', 'false', 'false']);
    // And back, with a pass run by hand.
    items[3].classList.remove('section-selected');
    items[0].classList.add('section-selected');
    window.__msfsbaFenixEfb.pass();
    assert.deepStrictEqual(items.map(i => i.getAttribute('aria-checked')), ['true', 'false', 'false', 'false', 'false', 'false']);

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

    // The page drops a code: five items are not the runway condition, and the layer takes back what it wrote.
    parent.removeChild(last);
    await turns();
    assert.strictEqual(all(window, '[role=radio],[role=radiogroup]').length, 0);
    assert.strictEqual(all(window, '[aria-checked]').length, 0);
    assert.strictEqual(group.hasAttribute('aria-label'), false);
    assert.ok(items.slice(0, 5).every(i => !i.hasAttribute('data-msfsba') && i.getAttribute('tabindex') !== '0'));

    // Six again, but not the six codes: still not the runway condition.
    const other = window.document.createElement('div');
    other.className = 'section-item';
    other.textContent = ' Extra ';
    parent.appendChild(other);
    await turns();
    assert.strictEqual(all(window, '[role=radio],[role=radiogroup]').length, 0);

    // The six codes are back.
    parent.removeChild(other);
    parent.appendChild(last);
    await turns();
    assert.strictEqual(all(window, '[role=radio]').length, 6);
    assert.strictEqual(group.getAttribute('role'), 'radiogroup');
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

test('Landing_fields_are_named_by_their_label_or_the_unit_after_them_and_never_the_placeholder', async () => {
  const window = await load('arrperf');
  try {
    const fields = all(window, FIELDS);
    assert.strictEqual(fields.length, 6);
    // ICAO CODE and RUNWAY have a label of their own; wind, temperature and pressure only the unit written
    // after the box; the landing weight the unit "T" (its FMS button is a button, not part of the name).
    assert.deepStrictEqual(fields.map(f => f.getAttribute('aria-label')), ['ICAO CODE', 'RUNWAY', 'KT', '°C', 'HPA', 'T']);
    assert.deepStrictEqual(fields.map(nameOf), ['ICAO CODE', 'RUNWAY', 'KT', '°C', 'HPA', 'T']);
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

// ---- The layer's own rules ----

for (const fixture of ['depperf', 'arrperf']) {
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
  depperf: { handled: 2 + 12 + 13, listeners: 2, note: 'home, chevron; six boxes and six drop-downs; thirteen result cells' },
  arrperf: { handled: 2 + 6 + 1 + 6 + 5 + 9, listeners: 2 + 6 + 5 + 9, note: 'home, chevron; six fields; the group and six radios; five options; nine procedures' }
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
    ['mcdu-screen', '[role=img]', 13],
    ['app-keyboard-input', 'app-keyboard-input input[aria-label]', 6],
    ['mat-select', 'mat-select[aria-label]', 6]
  ],
  arrperf: [
    ['one-click-change', '.one-click-change[role=button]', 5],
    ['arrival-procedure-item', '.arrival-procedure-item[role=button]', 9],
    ['section-group', '[role=radio]', 6]
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
