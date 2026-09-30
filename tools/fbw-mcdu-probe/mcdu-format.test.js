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

test('decodeCell drops a lone "{" arrow glyph and keeps the text', () => {
  // "{" is the FBW MCDU LSK arrow glyph, not a tag — drop it, keep the content.
  assert.strictEqual(fmt.decodeCell('abc{green'), 'abcgreen');
});

test('decodeCell keeps the * marker adjacent when a green segment has a leading space', () => {
  assert.strictEqual(fmt.decodeCell('{cyan}X{end}/{green}{sp}ON{end}'), 'X/ *ON');
});

test('renderLines suppresses empty label rows and renders all rows', () => {
  const decoded = {
    title: 'F-PLN', page: '1/3', scratchpad: '',
    arrows: [false, false, true, true], annunciators: [],
    rows: [
      { labelLeft: 'FROM', labelRight: '', labelCenter: '', valueLeft: 'EGLL', valueRight: '', valueCenter: '' },
      { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: 'KJFK', valueRight: '', valueCenter: '' },
      { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: '', valueRight: '', valueCenter: '' },
      { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: '', valueRight: '', valueCenter: '' },
      { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: '', valueRight: '', valueCenter: '' },
      { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: '', valueRight: '', valueCenter: '' },
    ],
  };
  const out = fmt.renderLines(decoded);
  // Title shows left+right arrows (◄ ►), no up/down.
  assert.strictEqual(out[0], 'Title: F-PLN   1/3 ◄ ►');
  // Row 1 has a label → its label line is present; row 2 has no label → only its value line.
  assert.strictEqual(out[1], '   FROM');
  assert.strictEqual(out[2], '1: EGLL');
  assert.strictEqual(out[3], '2: KJFK');
  // 6 value lines + 1 title + 1 label line + 1 scratchpad = 9 total.
  assert.strictEqual(out.length, 9);
  assert.strictEqual(out[out.length - 1], 'Scratchpad: ');
});

test('decodeCell keeps content after a non-tag "{" (FBW LSK arrow glyph)', () => {
  // Real DEPARTURES cell: "{" before 08L is the LSK arrow, NOT a markup tag.
  assert.strictEqual(
    fmt.decodeCell('{white}{cyan}{08L{small}-ILS{end}  9012{small}FT{end}{end}{end}'),
    '08L-ILS  9012FT'); // designator 08L must survive
  assert.strictEqual(fmt.decodeCell('{white}{RETURN{end}'), 'RETURN'); // arrow dropped, text kept
});

test('decodeCell still strips genuine known tags and drops stray "}"', () => {
  assert.strictEqual(fmt.decodeCell('{small}1/2{end}'), '1/2');
  assert.strictEqual(fmt.decodeCell('{green}CLB{end}'), 'CLB');
  assert.strictEqual(fmt.decodeCell('AB}'), 'AB'); // stray right-bracket glyph dropped
});

test('positionLine right-aligns a right-only cell (no more left-collapse)', () => {
  // right value must sit at the END of the 24-col field, only spaces before it
  assert.ok(/^ +FROM\/TO$/.test(fmt.positionLine('', '', 'FROM/TO')), 'FROM/TO not right-aligned');
  assert.ok(/^ {21}XYZ$/.test(fmt.positionLine('', '', 'XYZ')), 'XYZ not at column 21');
});

test('positionLine keeps left on the left and right on the right', () => {
  const out = fmt.positionLine('CO RTE', '', 'FROM/TO');
  assert.ok(out.startsWith('CO RTE'), 'left not at start');
  assert.ok(out.endsWith('FROM/TO'), 'right not at end');
});

test('positionLine centres a centre-only cell', () => {
  const out = fmt.positionLine('', 'AVAILABLE RUNWAYS', '');
  assert.strictEqual(out.trim(), 'AVAILABLE RUNWAYS');
  assert.ok(out.startsWith('   '), 'centre cell not indented: ' + JSON.stringify(out));
});

test('positionLine keeps a gap between centre time and right speed/alt (F-PLN)', () => {
  // Live-captured F-PLN value row shape: centre carries the FBW {sp} padding
  // ("2053" + 4 spaces), right is a 10-char SPD/ALT cell. The old trim-and-recentre
  // ran the time straight into the speed: "2053.78/ FL370".
  const out = fmt.positionLine('DANGR', '2053    ', '.78/ FL370');
  assert.strictEqual(out.indexOf('2053.78'), -1, 'time ran into speed: ' + JSON.stringify(out));
  assert.ok(/2053 +\.78\/ FL370$/.test(out), 'columns not aligned: ' + JSON.stringify(out));
});

test('positionLine honours right-cell trailing padding (F-PLN ditto row)', () => {
  // Live-captured: right cell '.78/   "  ' has two trailing spaces — FBW uses them
  // to column-align the ditto mark; the content must shift left accordingly.
  const out = fmt.positionLine('DANGR', '2053    ', '.78/   "  ');
  assert.ok(/^DANGR +2053 +\.78\/ +"$/.test(out), JSON.stringify(out));
});

test('renderLines keeps the right-hand value when green markers precede it', () => {
  // Live INIT FUEL PRED line 2: the whole row, BLOCK included, arrives in cell 0 padded
  // to exactly 24 columns. The '*' markers must not cost columns, or "5.1" is clipped.
  const sp = (n) => '{sp}'.repeat(n);
  const row = '{white}{sp}{sp}{small}{green}3.1{end}{end}{small}{green}/{end}{end}'
    + '{small}{green}0137{end}{end}' + sp(11) + '{cyan}5.1{end}{small}{end}{big}{end}{end}';
  const lines = [['', '', ''], [row, '', '']];
  for (let k = 1; k < 6; k++) { lines.push(['', '', ''], ['', '', '']); }
  const d = fmt.decodeSide({ title: 'INIT', lines });
  // One '*' for the one green value, in the blank padding column in front of "3.1".
  const expected = ' *3.1/0137' + ' '.repeat(11) + '5.1';
  assert.strictEqual(d.rows[0].valueText, expected);
  assert.strictEqual(d.rows[0].valueLeft, '  *3.1/0137' + ' '.repeat(11) + '5.1');
  assert.ok(fmt.renderLines(d).includes('1: ' + expected));
});

test('a marked right cell still ends at column 24', () => {
  const lines = [['', '', ''], ['A', '{green}ON{end}/OFF', '']];
  for (let k = 1; k < 6; k++) { lines.push(['', '', ''], ['', '', '']); }
  const d = fmt.decodeSide({ title: 'X', lines });
  assert.strictEqual(d.rows[0].valueText, 'A' + ' '.repeat(16) + '*ON/OFF');
});

// --- Live pages, 2026-09-30 (FBW A320 at KIAH, cells as captured) ------------------------

function valueRow(left, right, center) {
  const lines = [['', '', ''], [left, right || '', center || '']];
  for (let k = 1; k < 6; k++) { lines.push(['', '', ''], ['', '', '']); }
  return fmt.decodeSide({ title: 'X', lines }).rows[0].valueText;
}

test('touching green pieces get one star', () => {
  assert.strictEqual(fmt.decodeCell('{cyan}0.8{end}{green}/{end}{green}0011{end}'), '0.8*/0011');
  assert.strictEqual(fmt.decodeCell('{green}A{end} {green}B{end}{white}C{end}'), '*A *BC');
});

test('non-breaking padding never overwrites text', () => {
  assert.strictEqual(fmt.positionLine('ABCD', '', '\u00a0\u00a0X', 5), 'ABCDX');
});

test('a star takes non-breaking padding (PERF APPR line 5)', () => {
  assert.strictEqual(
    valueRow('{white}{cyan}{small}136{end}{end}\u00a0\u00a0\u00a0\u00a0{green}131{end}{end}',
      '{white}{cyan}FULL/{end}{small}CONF3{end}*{end}'),
    '136   *131   FULL/CONF3*');
});

test('an inserted star is paid back by the next gap (INIT FUEL PRED line 4)', () => {
  assert.strictEqual(
    valueRow('{white}{sp}{sp}{small}{cyan}0.8{end}{end}{small}{green}/{end}{end}'
      + '{small}{green}0011{end}{end}{sp}{sp}{sp}{small}{green}155.3{end}{end}'
      + '{small}{green}/{end}{end}{small}{green}145.1{end}{end}{small}{end}{big}{end}{end}'),
    '  0.8*/0011 *155.3/145.1');
});

test('the right cell stays in place after an inserted star (PERF TAKE OFF line 2)', () => {
  assert.strictEqual(
    valueRow('{white}{cyan}149{end}{small}\u00a0\u00a0\u00a0{end}\u00a0S={green}201{end}{end}',
      '{white}{inop}{small}[M]{end}[\u00a0\u00a0]*{end}{end}'),
    '149    S=*201   [M][  ]*');
});
