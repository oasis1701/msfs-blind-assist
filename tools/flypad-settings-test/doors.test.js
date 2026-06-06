// flyPad Ground door tiles read open/closed (user request) instead of active/[silent].
const { test } = require('node:test');
const assert = require('node:assert');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const { scrape } = require('./run');
const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-flypad-agent.js');

// A door tile = a cursor-pointer wrapper (the colour carrier) around an <h2> name.
const tile = (color) =>
  '<div class="flex cursor-pointer flex-row p-6 ' + (color || '') + '"><h2>Door Fwd</h2></div>';

test('doorOpenState: green => open, amber => in transit, none => closed', () => {
  const dom = new JSDOM('<!DOCTYPE html><body>' + tile('bg-green-500') + tile('bg-amber-500') + tile('') + '</body>', { runScripts: 'outside-only' });
  const w = dom.window;
  w.SimVar = { GetSimVarValue() { return 0; }, SetSimVarValue() {} };
  w.eval(fs.readFileSync(AGENT, 'utf8'));
  const A = w.__MSFSBA_FLYPAD;
  const heads = w.document.querySelectorAll('h2');
  assert.strictEqual(A.doorOpenState(heads[0]), 'open');
  assert.strictEqual(A.doorOpenState(heads[1]), 'in transit');
  assert.strictEqual(A.doorOpenState(heads[2]), 'closed');
});

test('ground fixture (in flight, all doors closed): door tiles read "(closed)", none read "(active)"', () => {
  const els = scrape('ground').elements;
  const doors = els.filter(e => /\bdoor\b/i.test(e.text));
  assert.ok(doors.length >= 3, 'expected several door tiles, got ' + doors.length);
  doors.forEach(d => assert.match(d.text, /\((closed|open|in transit)\)$/, 'door without open/closed state: ' + d.text));
  assert.ok(!doors.some(d => /\(active\)/.test(d.text)), 'a door still reads "(active)": ' + doors.map(d => d.text).join(' | '));
});

test('ground door tiles are grouped contiguously (incl. in-flight headings)', () => {
  // Among the Ground service tiles, the door tiles must form one contiguous run
  // (orderGroundServices band 150) — not be scattered among GPU/stairs/trucks.
  const tiles = scrape('ground').elements.filter(e => e.kind === 'heading' || e.kind === 'button');
  const doorIdx = tiles.map((e, i) => (/\bdoor\b/i.test(e.text) ? i : -1)).filter(i => i >= 0);
  assert.ok(doorIdx.length >= 3, 'expected several door tiles');
  assert.strictEqual(doorIdx[doorIdx.length - 1] - doorIdx[0], doorIdx.length - 1,
    'door tiles are not contiguous: positions ' + doorIdx.join(','));
});

test('non-door ground service tiles still use active/called wording (not open/closed)', () => {
  // A green non-door service tile must read "(active)", proving the door remap is scoped.
  const dom = new JSDOM('<!DOCTYPE html><body><div id="MSFS_REACT_MOUNT">' +
    '<div class="bg-green-500 cursor-pointer" onclick="1"><h2>GPU</h2></div>' +
    '</div></body>', { runScripts: 'outside-only' });
  const w = dom.window;
  w.Element.prototype.getBoundingClientRect = () => ({ top: 200, left: 400, right: 600, bottom: 240, width: 200, height: 40, x: 400, y: 200 });
  w.SimVar = { GetSimVarValue() { return 0; }, SetSimVarValue() {} };
  w.eval(fs.readFileSync(AGENT, 'utf8'));
  const A = w.__MSFSBA_FLYPAD;
  A.isVisible = () => true;
  const gpu = A.serviceState(w.document.querySelector('div.bg-green-500'));
  assert.strictEqual(gpu, 'active', 'non-door tile state helper unchanged');
});
