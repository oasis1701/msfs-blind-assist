// The FBW A380X OANS "runway ahead" advisory, as MSFSBA's in-page agent
// (MSFSBlindAssist/Resources/coherent-oans-agent.js) decodes it.
//
// FBW publishes it as bit 11 of the ARINC discrete L:A32NX_OANS_WORD_1
// (OansBrakeToVacateSelection.transmitRwyAheadAdvisory), and a discrete word carries its bitfield
// as the float's VALUE. Until 2026-09-25 the agent tested bit 10 of the RAW low word instead — a
// bit of the float's mantissa, which is 0 in every word FBW writes with bit 11 alone set
// (1024.0f = 0x44800000) — so the OANS window's "Caution: runway X ahead" line never showed.
//
// No dependencies: the agent only needs SimVar and a document that finds nothing, so it runs in a
// bare vm context.

'use strict';

const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-oans-agent.js');
const WORD = 'L:A32NX_OANS_WORD_1';

// Arinc429SignStatusMatrix (fbw-common/src/systems/shared/src/arinc429.ts).
const FAILURE_WARNING = 0b00;
const NO_COMPUTED_DATA = 0b01;
const FUNCTIONAL_TEST = 0b10;
const NORMAL_OPERATION = 0b11;

const BIT_11 = 1 << 10;

/** A word packed as FBW's Arinc429Register.writeToSimVar packs it: the float32 bits of the value, plus ssm * 2^32. */
function word(ssm, value) {
  const b = Buffer.alloc(4);
  b.writeFloatBE(value, 0);
  return b.readUInt32BE(0) + ssm * 2 ** 32;
}

/**
 * The agent installed into a fresh page context. `lvars` maps full "L:" names to raw values; any
 * other read returns 0. `nd` is what document.querySelector("a380x-nd") finds (none by default).
 */
function install(lvars, nd) {
  const reads = [];
  const page = {
    document: { querySelector: (sel) => (sel === 'a380x-nd' ? nd || null : null) },
    SimVar: {
      GetSimVarValue: (name) => {
        reads.push(name);
        return Object.prototype.hasOwnProperty.call(lvars, name) ? lvars[name] : 0;
      },
    },
  };
  page.window = page;
  vm.createContext(page);
  const result = vm.runInContext(fs.readFileSync(AGENT, 'utf8'), page, { filename: AGENT });
  assert.strictEqual(result, 'MSFSBA_OANS_INSTALLED');
  return { agent: page.__MSFSBA_OANS, reads };
}

const rwyAhead = (raw) => install({ [WORD]: raw }).agent.rwyAheadActive();

test('the word FBW writes for an advisory reads as runway ahead', () => {
  // transmitRwyAheadAdvisory: an empty register, Normal Operation, setBitValue(11, true).
  assert.strictEqual(rwyAhead(word(NORMAL_OPERATION, BIT_11)), true);
});

test('the fixture is the shape the old raw-bit reading got wrong', () => {
  // Guards the test's own premise: raw bit 10 of that word is 0, so a raw-bit decode reads "clear".
  const low = word(NORMAL_OPERATION, BIT_11) % 2 ** 32;
  assert.strictEqual(low, 0x44800000);
  assert.strictEqual((low >>> 10) & 1, 0);
});

test('the word FBW writes with no advisory reads as clear', () => {
  assert.strictEqual(rwyAhead(word(NORMAL_OPERATION, 0)), false);
});

test('bit 11 reads alongside other bits, and its neighbours never stand in for it', () => {
  assert.strictEqual(rwyAhead(word(NORMAL_OPERATION, BIT_11 | (1 << 12))), true);
  assert.strictEqual(rwyAhead(word(NORMAL_OPERATION, 1 << 9)), false);   // bit 10
  assert.strictEqual(rwyAhead(word(NORMAL_OPERATION, 1 << 11)), false);  // bit 12
  assert.strictEqual(rwyAhead(word(NORMAL_OPERATION, 0xffffff & ~BIT_11)), false);
});

test('functional test carries data, as in FBW bitValueOr and C# Arinc429Word.BitValueOr', () => {
  assert.strictEqual(rwyAhead(word(FUNCTIONAL_TEST, BIT_11)), true);
});

test('a failed or uncomputed word says nothing', () => {
  // transmitRwyAheadAdvisory's faulty path writes Failure Warning.
  assert.strictEqual(rwyAhead(word(FAILURE_WARNING, BIT_11)), false);
  assert.strictEqual(rwyAhead(word(NO_COMPUTED_DATA, BIT_11)), false);
});

test('an unreadable variable reads as clear', () => {
  for (const raw of [NaN, Infinity, undefined, null, '4294968320']) {
    assert.strictEqual(rwyAhead(raw), false, `raw ${String(raw)}`);
  }
  const page = install({}).agent;
  page.lvar = () => { throw new Error('SimVar gone'); };
  assert.strictEqual(page.rwyAheadActive(), false);
});

test('it reads the OANS word and nothing else', () => {
  const { agent, reads } = install({ [WORD]: word(NORMAL_OPERATION, BIT_11) });
  agent.rwyAheadActive();
  assert.deepStrictEqual(reads, [WORD]);
});

/** A loaded OANS with BTV ready and nothing armed, whose monitor last named `qfu` ahead. */
function ndWithBtv(qfu) {
  const subject = (v) => ({ get: () => v });
  return {
    fsInstrument: {
      oansRef: {
        instance: {
          btvUtils: {
            rwyAheadQfu: qfu,
            btvRunway: subject(null),
            btvExit: subject(null),
            btvRunwayLda: subject(null),
            btvExitDistance: subject(null),
            btvRunwayBearingTrue: subject(null),
          },
          data: { features: [] },
          labelManager: { labels: [] },
          dataAirportIcao: subject('EDDF'),
          dataAirportName: subject('FRANKFURT'),
        },
      },
    },
  };
}

const snapshot = (raw, qfu) =>
  JSON.parse(install({ [WORD]: raw }, ndWithBtv(qfu)).agent.snapshot());

test('the snapshot carries the runway ahead while the word says so', () => {
  const s = snapshot(word(NORMAL_OPERATION, BIT_11), '07C - 25C');
  assert.strictEqual(s.ok, true);
  assert.strictEqual(s.btv.ready, true);
  assert.strictEqual(s.btv.rwyAheadQfu, '07C - 25C');
});

test('the snapshot drops a stale runway once the word clears', () => {
  // btvUtils.rwyAheadQfu is left behind when FBW's monitor early-bails (stopped, or above 40 kt).
  assert.strictEqual(snapshot(word(NORMAL_OPERATION, 0), '07C - 25C').btv.rwyAheadQfu, '');
  assert.strictEqual(snapshot(word(FAILURE_WARNING, BIT_11), '07C - 25C').btv.rwyAheadQfu, '');
});
