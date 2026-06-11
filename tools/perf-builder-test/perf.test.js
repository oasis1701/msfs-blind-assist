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

// SURV CONTROLS: the page lays XPDR/TCAS side-by-side and WXR ELEVN/TILT beside the
// WXR grid, so a flat geometric read interleaves unrelated systems. buildSurvControls
// re-groups it into contiguous, self-labelled blocks each led by a heading
// (XPDR → TCAS → WXR → TAWS → SURV); the headerless TCAS NORM/ABV/BLW range becomes
// "TCAS display: …"; and SurvButton toggles read their LIVE state ("TERR SYS: ON",
// not the ambiguous "OFF: ON"). Captured live. (Supersedes the older "headers owned,
// never bare" behaviour — every group now intentionally LEADS with its heading.)
test('SURV CONTROLS: logically grouped, headed, self-labelled, selectable', () => {
  const j = joined('surv_controls');
  // each group leads with its heading line
  assert.match(j, /(^|\n)XPDR(\n|$)/, 'XPDR group heading missing');
  assert.match(j, /(^|\n)TCAS(\n|$)/, 'TCAS group heading missing');
  assert.match(j, /(^|\n)WXR(\n|$)/, 'WXR group heading missing');
  assert.match(j, /(^|\n)TAWS(\n|$)/, 'TAWS group heading missing');
  // controls stay self-labelled with their system
  assert.match(j, /(^|\n)XPDR: AUTO(\n|$)/, 'XPDR mode radio lost its label');
  assert.match(j, /(^|\n)TCAS: TA\/RA(\n|$)/, 'TCAS mode radio lost its label');
  assert.match(j, /(^|\n)ELEVN\/TILT: AUTO(\n|$)/, 'ELEVN/TILT radio lost its label');
  assert.match(j, /(^|\n)WXR: AUTO(\n|$)/, 'WXR toggle lost its label');
  // headerless TCAS vertical-range column is now qualified
  assert.match(j, /(^|\n)TCAS display: NORM(\n|$)/, 'TCAS NORM/ABV/BLW range not relabelled');
  // toggles read their LIVE state, not the ambiguous "OFF: ON"
  assert.match(j, /(^|\n)TERR SYS: (ON|OFF)(\n|$)/, 'TERR SYS toggle not reading live state');
  assert.match(j, /(^|\n)ALT RPTG: (ON|OFF)(\n|$)/, 'ALT RPTG toggle not reading live state');
  assert.doesNotMatch(j, /: OFF: ON(\n|$)/, 'a SurvButton still reads the ambiguous "OFF: ON"');
  // groups are CONTIGUOUS and in logical order (XPDR → TCAS → WXR → TAWS)
  const at = (re) => j.search(re);
  assert.ok(at(/(^|\n)XPDR(\n|$)/) < at(/(^|\n)TCAS(\n|$)/), 'XPDR group not before TCAS');
  assert.ok(at(/(^|\n)TCAS(\n|$)/) < at(/(^|\n)WXR(\n|$)/), 'TCAS group not before WXR');
  assert.ok(at(/(^|\n)WXR(\n|$)/) < at(/(^|\n)TAWS(\n|$)/), 'WXR group not before TAWS');
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

// F-PLN VERT REV → STEP ALTs sub-page: the two anonymous fields are clarified to
// "Step waypoint" / "Step altitude" (only on this subtab), and a leading "FL" unit
// binds its flight level ("FROM CRZ: FL 390", not "FL, 390"). Captured live.
test('STEP ALTs: WPT/ALT relabeled, FL binds its level, fields selectable', () => {
  const list = els('vertrev_stepalts');
  const j = list.map((e) => e.text).join('\n');
  assert.match(j, /Step waypoint: /, 'WPT not relabeled to "Step waypoint"');
  assert.match(j, /Step altitude: /, 'ALT not relabeled to "Step altitude"');
  assert.doesNotMatch(j, /(^|\n)WPT: /, 'bare "WPT:" still present');
  assert.doesNotMatch(j, /(^|\n)ALT: /, 'bare "ALT:" still present');
  assert.match(j, /FROM CRZ: FL 390/, 'leading FL not bound to its level');
  assert.doesNotMatch(j, /FL, 390/, 'FL still comma-split from its level');
  // the WPT combobox + ALT input keep their stamped idx (still settable)
  assertSelectable('vertrev_stepalts');
});

// F-PLN DEPARTURE sub-page: the RWY/SID/TRANS selector dropdowns must show their
// CURRENT selection (comboSelectedValue reads the summary cell under each header),
// so they read "RWY, 24L" / "SID, OSHNN1" / "TRANS, BEALE", not bare names. Captured live.
test('DEPARTURE: selector dropdowns show their selected value, stay selectable', () => {
  const list = els('departure');
  const dd = list.filter((e) => e.kind === 'dropdown');
  const j = dd.map((e) => e.text).join('\n');
  assert.match(j, /RWY, 24L/, 'RWY dropdown not showing selected runway');
  assert.match(j, /SID, OSHNN1/, 'SID dropdown not showing selected SID');
  assert.match(j, /TRANS, BEALE/, 'TRANS dropdown not showing selected transition');
  assertSelectable('departure');
});

// F-PLN fixed-grid, live fixture (THE SPEC — Braille column reading, user decision
// 2026-06): each waypoint is ONE fixed-width padded line of LITERAL screen values
// (ident 10 / airway 8 / track 5 / dist 5 / fpa 5 / spd 6 / alt 8 / time), a padded
// HEADER line calibrates the columns, and the '"' ditto marks (HoneywellMCDU,
// formatSpeed ~1887 / formatAltitude ~1808 — prediction unchanged from the line
// above) are shown LITERALLY, never resolved and never omitted. Spoken connector
// labels ("via"/"track"/"Mach"/"flight level"/"ETA") must NOT appear on waypoint
// lines; the destination footer KEEPS its labels (a summary, not a grid row).
test('F-PLN: fixed-grid waypoint lines, header, literal dittos, labeled footer', () => {
  const list = els('fpln');
  const j = list.map((e) => e.text).join('\n');
  const wpts = list.filter((e) => e.kind === 'fplnwpt');
  // the padded header line exists with the column titles, aligned to the grid
  assert.ok(
    list.some((e) => e.kind === 'text' && e.text === ' '.repeat(23) + 'DIST FPA  SPD   ALT     TIME'),
    'column-header line missing or misaligned');
  // first cruise line carries the literal values…
  assert.ok(wpts.some((e) => e.text === '(T/C)                            .84   FL380   18:01'),
    '(T/C) line lost its literal .84 / FL380 grid cells');
  // …and the unchanged cruise legs show the literal ditto marks, padded in place
  assert.strictEqual(
    wpts.filter((e) => /^RYLIE/.test(e.text)).map((e) => e.text)[0],
    'RYLIE     J102    261° 29        "     "       18:04',
    'RYLIE grid line wrong (dittos must stay literal, columns padded)');
  assert.ok(wpts.filter((e) => /"\s+"/.test(e.text)).length >= 8,
    'ditto marks missing on the unchanged cruise legs');
  // spoken-label verbosity must never return on waypoint lines (footer excluded)
  for (const e of wpts) {
    assert.doesNotMatch(e.text, /, via |track |Mach |flight level |ETA /,
      `spoken labels leaked into a grid line: "${e.text}"`);
  }
  // destination footer KEEPS its spoken labels
  assert.match(j, /(^|\n)Destination KLAX24R, ETA 19:57, 877 NM, EFOB 29\.4 KLB(\n|$)/, 'destination footer line missing');
  // the destination ident / DEST buttons stay clickable
  assert.ok(list.some((e) => e.kind === 'button' && e.text === 'KLAX24R' && e.idx > 0), 'dest ident button lost');
  assertSelectable('fpln');
});

// F-PLN polish, source-derived fixture (fpln_polish.html — built from the
// MfdFmsFpln.tsx / ActivePageTitleBar.tsx render structure, not live-captured).
// Fixed-grid expectations (literal screen values in their padded columns):
//  - coexisting SPD + ALT constraints both land literally in their own columns
//    ("250" / "+5000" — the old single-variable parse kept only the last);
//  - a two-altitude WINDOW constraint (FBW renders only the literal "WINDOW",
//    ~1861) stays the literal "WINDOW" in the alt column;
//  - the descent FPA upper-row value ("3.0", ~1658) fills the fpa column;
//  - hold rows (~1693) carry the time-column hold speed as "SPD 210" (the
//    on-screen SPD annotation), never a bogus ETA;
//  - visible TMPY/EO title flags (ActivePageTitleBar.tsx:66-78) append to the
//    title line; hidden PENALTY stays silent and no raw flag leaks as own line.
test('F-PLN polish: grid columns — constraints, WINDOW, FPA, hold SPD, title flags', () => {
  const list = els('fpln_polish');
  const j = list.map((e) => e.text).join('\n');
  assert.match(j, /(^|\n)ACTIVE\/F-PLN \(engine out\) \(temporary\)(\n|$)/, 'title flags not appended to the title line');
  assert.doesNotMatch(j, /\(penalty\)/, 'hidden PENALTY flag leaked');
  assert.doesNotMatch(j, /(^|\n)(TMPY|EO)(\n|$)/, 'raw flag span leaked as its own line');
  const wpt = (id) => {
    const e = list.find((x) => x.kind === 'fplnwpt' && x.text.indexOf(id) === 0);
    return e ? e.text : '(line missing)';
  };
  assert.strictEqual(wpt('WAYPT1'), 'WAYPT1    J102    261° 29   3.0  250   5000    18:04', 'FPA / prediction columns wrong');
  assert.strictEqual(wpt('WAYPT2'), 'WAYPT2    J102    259° 41        "     "       18:09', 'dittos not literal in the grid');
  assert.strictEqual(wpt('WAYPT3'), 'WAYPT3            252° 157       250   +5000', 'coexisting SPD+ALT constraints not literal in their columns');
  assert.strictEqual(wpt('WAYPT4'), 'WAYPT4            250° 10              WINDOW', 'WINDOW constraint not literal in the alt column');
  assert.strictEqual(wpt('HOLD L'), 'HOLD L                                         SPD 210', 'hold speed not "SPD 210" in the time column');
  assert.ok(list.some((e) => e.kind === 'text' && e.text === ' '.repeat(23) + 'DIST FPA  SPD   ALT     TIME'),
    'column-header line missing or misaligned');
  assert.match(j, /(^|\n)Destination KLAX24R, ETA 19:57, 877 NM, EFOB 29\.4 KLB(\n|$)/, 'destination footer missing');
  assertSelectable('fpln_polish');
});

// NAVAIDS → SELECTED FOR FMS NAV: a trailing unit that the Y-row bucketing split onto
// its own line (the glideslope "°" 2px past a rounding boundary from "-3.0") is folded
// back onto its value, so it reads "SLOPE: -3.0 °", not "SLOPE: -3.0" + a stray "°".
test('NAVAIDS SELECTED: orphaned trailing unit re-attaches to its value', () => {
  const j = joined('navaids_selected');
  assert.match(j, /SLOPE: -3\.0 °/, 'SLOPE value did not keep its degree unit');
  assert.doesNotMatch(j, /(^|\n)°(\n|$)/, 'a bare "°" line was left stranded');
});
