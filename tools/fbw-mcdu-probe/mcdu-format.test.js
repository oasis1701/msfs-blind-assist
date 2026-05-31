'use strict';
const test = require('node:test');
const assert = require('node:assert');
const fmt = require('./mcdu-format');

test('decodeCell strips color, size and end tags', () => {
  assert.strictEqual(fmt.decodeCell('{small}1/2{end}'), '1/2');
  assert.strictEqual(fmt.decodeCell('{green}CLB{end}'), 'CLB');
  assert.strictEqual(fmt.decodeCell('{big}{cyan}DIRECT{end}'), 'DIRECT');
});

test('decodeCell converts {sp} to space and passes unknown text through', () => {
  assert.strictEqual(fmt.decodeCell('{sp}{sp}AB'), '  AB');
  assert.strictEqual(fmt.decodeCell('[ ]'), '[ ]');
});

test('decodeCell marks selected green option on a mixed-color line', () => {
  assert.strictEqual(fmt.decodeCell('{cyan}OFF{end}/{green}ON{end}'), 'OFF/*ON');
});

test('decodeCell leaves a single-color line unmarked', () => {
  assert.strictEqual(fmt.decodeCell('{green}ON{end}'), 'ON');
  assert.strictEqual(fmt.decodeCell(''), '');
});

test('litAnnunciators returns lit lamp labels in fixed order', () => {
  assert.deepStrictEqual(
    fmt.litAnnunciators({ fail: false, fmgc: true, mcdu_menu: true, fm1: false, blank: true }),
    ['FMGC', 'MENU']
  );
  assert.deepStrictEqual(fmt.litAnnunciators(null), []);
});

test('decodeSide maps label/value row pairs and header fields', () => {
  const side = {
    title: 'INIT', page: '{small}1/2{end}', scratchpad: '{amber}SELECT{end}',
    arrows: [true, false, false, false],
    annunciators: { fmgc: true },
    lines: [
      ['CO RTE', 'FROM/TO', ''], ['{cyan}________{end}', '{amber}____|____{end}', ''],
      ['', '', ''], ['', '', ''],
      ['', '', ''], ['', '', ''],
      ['', '', ''], ['', '', ''],
      ['', '', ''], ['', '', ''],
      ['', '', ''], ['', '', ''],
    ],
  };
  const d = fmt.decodeSide(side);
  assert.strictEqual(d.title, 'INIT');
  assert.strictEqual(d.page, '1/2');
  assert.strictEqual(d.scratchpad, 'SELECT');
  assert.deepStrictEqual(d.annunciators, ['FMGC']);
  assert.deepStrictEqual(d.arrows, [true, false, false, false]);
  assert.strictEqual(d.rows[0].labelLeft, 'CO RTE');
  assert.strictEqual(d.rows[0].labelRight, 'FROM/TO');
  assert.strictEqual(d.rows[0].valueLeft, '________');
  assert.strictEqual(d.rows[0].valueRight, '____|____');
});

test('renderLines builds the accessible ListBox text', () => {
  const decoded = {
    title: 'PERF CLB', page: '1/2', scratchpad: '',
    arrows: [false, true, false, false], annunciators: ['FMGC'],
    rows: [
      { labelLeft: 'ACT MODE', labelRight: '', labelCenter: '', valueLeft: 'CLB', valueRight: '', valueCenter: '' },
      { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: '', valueRight: '', valueCenter: '' },
      { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: '', valueRight: '', valueCenter: '' },
      { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: '', valueRight: '', valueCenter: '' },
      { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: '', valueRight: '', valueCenter: '' },
      { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: '', valueRight: '', valueCenter: '' },
    ],
  };
  const out = fmt.renderLines(decoded);
  assert.strictEqual(out[0], 'Annunciators: FMGC');
  assert.strictEqual(out[1], 'Title: PERF CLB   1/2 ▼');
  assert.strictEqual(out[2], '   ACT MODE');
  assert.strictEqual(out[3], '1: CLB');
  assert.strictEqual(out[out.length - 1], 'Scratchpad: ');
});
