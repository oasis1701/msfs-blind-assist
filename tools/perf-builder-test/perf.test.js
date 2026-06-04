'use strict';
// Regression tests for the A380 MFD PERF / STEP-ALTs structure-aware scrape
// (coherent-a380-agent.js buildPerfLines). Fixtures are real in-flight + on-ground DOM
// captures (live visibility/geometry baked in). Asserts: clean per-field output (no
// comma-soup / null rows / mislabels), the green-dot label, the APPR F-speed staying on
// its own line (not fused into the wind line), and the SELECTABILITY invariant (every
// interactive element keeps a stamped idx). Also guards that F-PLN is unaffected (the
// builder is gated by isPerfPage).

const test = require('node:test');
const assert = require('node:assert');
const { scrapeElements } = require('./run');

const GROUND = ['perf_to_gnd', 'perf_clb_gnd', 'perf_crz_gnd', 'perf_des_gnd', 'perf_appr_gnd', 'perf_ga_gnd'];
const INTERACTIVE = ['input', 'dropdown', 'radio', 'subtab', 'button', 'menu', 'fplnwpt', 'fplndisc', 'surv', 'survstatus'];

function els(name) { return scrapeElements(name); }
function joined(name) { return els(name).map((e) => e.text).join('\n'); }

function assertSelectable(name) {
  for (const e of els(name)) {
    if (INTERACTIVE.includes(e.kind)) {
      assert.ok(e.idx > 0, `${name}: ${e.kind} "${e.text}" lost its stamped idx (not selectable)`);
    }
  }
}

test('PERF tabs: labeled fields are clean (no comma-soup / null rows), and selectable', () => {
  for (const f of GROUND) {
    const j = joined(f);
    assert.match(j, /OPT: FL \d/, `${f}: OPT/REC-MAX not emitted cleanly`);
    assert.doesNotMatch(j, /OPT, FL,/, `${f}: OPT comma-soup regressed`);
    assert.doesNotMatch(j, /(^|\n)null:/, `${f}: a "null" grid row leaked`);
    assertSelectable(f);
  }
});

test('T.O: green-dot speed is labeled, V-speed companions on their own lines', () => {
  const j = joined('perf_to_gnd');
  assert.match(j, /green dot: \d+ KT/, 'green-dot speed not labeled');
  assert.match(j, /(^|\n)F: \d+ KT/, 'F speed not on its own clean line');
  assert.match(j, /(^|\n)S: \d+ KT/, 'S speed not on its own clean line');
});

test('APPR: F-speed is its own line, not fused into the wind (HD/CROSS) line', () => {
  const list = els('perf_appr_gnd');
  assert.ok(list.some((e) => /^F: \d+ KT/.test(e.text)), 'F speed not its own line');
  for (const e of list) {
    if (/CROSS/.test(e.text) || /^HD:/.test(e.text)) {
      assert.doesNotMatch(e.text, /\d{2,3}\s*KT\s*,\s*\d/, `wind line fused a speed: "${e.text}"`);
    }
  }
});

test('CLB cruise (inactive state): PRED-TO is not mislabeled as MACH', () => {
  // The cruise CLB fixture captures the inactive-field state; owning the speed grid
  // removed the bogus "MACH: FL 380" pairing of the read-only PRED-TO prediction.
  assert.doesNotMatch(joined('perf_clb'), /MACH: FL/, 'PRED-TO still mislabeled as MACH');
});

test('F-PLN is unaffected (PERF builder gated to PERF pages only)', () => {
  const list = els('fpln');
  assert.ok(list.some((e) => e.kind === 'fplnwpt'), 'F-PLN lost its waypoint lines');
  assert.ok(!list.some((e) => /OPT: FL/.test(e.text)), 'PERF artifacts leaked into F-PLN');
});

// The label-aware Y-merge composer turns sibling label/value/unit data rows into
// "LABEL: value unit, LABEL: value unit" instead of comma-soup. POSITION/IRS is the
// canonical multi-column data-row page (captured live).
test('POSITION/IRS: sibling-label data rows compose as "LABEL: value unit"', () => {
  const j = joined('irs');
  // multi-column rows: label binds its value with ":", a second column starts after ","
  assert.match(j, /T\.TRK: 134\.4 °T, T\.HDG: 134\.4 °T/, 'T.TRK/T.HDG row not composed');
  assert.match(j, /GND SPD: 0\.0 KT, MAG HDG: 122\.9 °/, 'GND SPD/MAG HDG row not composed');
  // single label:value rows
  assert.match(j, /(^|\n)IRS 1: NAV/, 'IRS 1 status not composed');
  assert.match(j, /GPIRS POSITION: 33°56\.5N\/118°22\.2W/, 'GPIRS POSITION not composed');
  // the colon-join header (label already printing its own ":")
  assert.match(j, /IRS ALIGNED ON GPS POS: 33°/, 'aligned-on header lost its colon-join');
  // no raw comma-soup left on the composed rows
  assert.doesNotMatch(j, /T\.TRK, 134\.4, °T/, 'IRS comma-soup regressed');
  // selectability invariant: every IRS button keeps its stamped idx
  assertSelectable('irs');
});

// SURV CONTROLS: each radio / toggle is prefixed with its column header so a blind
// pilot knows which system it belongs to ("TCAS: TA/RA" vs "XPDR: AUTO"). Captured live.
test('SURV CONTROLS: radios + toggles carry their system header, stay selectable', () => {
  const j = joined('surv_controls');
  assert.match(j, /(^|\n)TCAS: TA\/RA(\n|$)/, 'TCAS mode radio not header-prefixed');
  assert.match(j, /(^|\n)XPDR: AUTO(\n|$)/, 'XPDR mode radio not header-prefixed');
  assert.match(j, /(^|\n)ELEVN\/TILT: AUTO(\n|$)/, 'ELEVN/TILT radio not header-prefixed');
  assert.match(j, /(^|\n)WXR: AUTO(\n|$)/, 'WXR toggle not header-prefixed');
  assert.match(j, /(^|\n)MODE: WX(\n|$)/, 'MODE toggle not header-prefixed');
  assert.match(j, /(^|\n)TERR SYS: /, 'TERR SYS toggle not header-prefixed');
  // the XPDR/TCAS header cells are owned → not also read as bare lines
  assert.doesNotMatch(j, /(^|\n)XPDR(\n|$)/, 'XPDR header double-read as a bare line');
  assert.doesNotMatch(j, /(^|\n)TCAS(\n|$)/, 'TCAS header double-read as a bare line');
  // selectability invariant preserved (every radio/surv/button keeps its idx)
  assertSelectable('surv_controls');
});

// SURV STATUS & SWITCHING: .mfd-surv-status-item cells must NOT row-merge — each
// stays its own line, and a mid-row heading (TAWS/XPDR/TCAS) must not bind a status
// cell as "HEADING: status" (a false relationship). Captured live.
test('SURV STATUS & SWITCHING: status items stay on their own lines', () => {
  const list = els('surv_status');
  const j = list.map((e) => e.text).join('\n');
  assert.match(j, /(^|\n)TERR SYS 1(\n|$)/, 'TERR SYS 1 not its own line');
  assert.match(j, /(^|\n)TERR SYS OFF(\n|$)/, 'TERR SYS OFF not its own line');
  assert.match(j, /(^|\n)WXR DISPLAY 1(\n|$)/, 'WXR DISPLAY 1 not its own line');
  // no false heading-bind across the column status cells
  assert.doesNotMatch(j, /TAWS: /, 'TAWS spuriously bound a status cell');
  assert.doesNotMatch(j, /XPDR: XPDR OFF/, 'XPDR spuriously bound a status cell');
  assert.doesNotMatch(j, /TERR SYS 1,/, 'status items wrongly comma-merged');
});

// D-ATIS/LIST: the per-station "UPDATE OR PRINT" function selector is a .mfd-button
// (no .mfd-dropdown-inner); it must read its own label with word spacing and must NOT
// geometrically absorb the ATIS report sitting below it. The ATIS reads once as text.
test('D-ATIS: function dropdown reads "UPDATE OR PRINT", ATIS not duplicated into it', () => {
  const list = els('datis');
  const dd = list.filter((e) => e.kind === 'dropdown');
  assert.ok(dd.length >= 1, 'no D-ATIS function dropdown found');
  for (const e of dd) {
    assert.strictEqual(e.text, 'UPDATE OR PRINT', `dropdown text wrong: "${e.text}"`);
    assert.doesNotMatch(e.text, /ATIS INFO|UPDATEOR/, 'dropdown absorbed the ATIS / lost spacing');
  }
  // the ATIS report still reads as its own (text) line
  assert.ok(list.some((e) => e.kind === 'text' && /ATIS INFO K/.test(e.text)), 'ATIS report line missing');
  assertSelectable('datis');
});
