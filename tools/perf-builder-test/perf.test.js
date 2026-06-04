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
const INTERACTIVE = ['input', 'dropdown', 'radio', 'subtab', 'button', 'menu', 'fplnwpt', 'fplndisc'];

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
