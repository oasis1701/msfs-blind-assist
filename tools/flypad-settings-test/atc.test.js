// ATC page FrequencyCard tune buttons: bare "Set Active"/"Set Standby" get the
// card's callsign + frequency prefixed so multiple controllers stay
// distinguishable. Fixture mirrors the LIVE DOM (2026-06-11): the buttons sit in
// their OWN h2s inside the opacity-0 hover overlay; the card div carries
// bg-theme-secondary with callsign + frequency as its first two h2s.
const { test } = require('node:test');
const assert = require('node:assert');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-flypad-agent.js');

const card = (callsign, freq) =>
  '<div class="relative w-full overflow-hidden rounded-md bg-theme-secondary p-6">' +
  '<h2 class="font-bold">' + callsign + '</h2><h2>' + freq + '</h2>' +
  '<div class="absolute inset-0 flex flex-row opacity-0 transition duration-100 hover">' +
  '<h2><div class="flex w-full items-center justify-center border-2">Set Active</div></h2>' +
  '<h2><div class="flex w-full items-center justify-center border-2">Set Standby</div></h2>' +
  '</div></div>';

test('ATC tune buttons get callsign + frequency prefixed from the card', () => {
  const dom = new JSDOM('<!DOCTYPE html><body>' + card('UNICOM', '122.800') + card('KSFO_TWR', '120.500') + '</body>', { runScripts: 'outside-only' });
  const w = dom.window;
  w.SimVar = { GetSimVarValue() { return 0; }, SetSimVarValue() {} };
  w.eval(fs.readFileSync(AGENT, 'utf8'));
  const A = w.__MSFSBA_FLYPAD;
  const btns = [...w.document.querySelectorAll('div')].filter(d => /^Set (Active|Standby)$/.test(d.textContent.trim()) && d.children.length === 0);
  assert.strictEqual(btns.length, 4);
  assert.strictEqual(A.labelFor(btns[0]), 'UNICOM 122.800: Set Active');
  assert.strictEqual(A.labelFor(btns[1]), 'UNICOM 122.800: Set Standby');
  assert.strictEqual(A.labelFor(btns[2]), 'KSFO_TWR 120.500: Set Active');
  assert.strictEqual(A.labelFor(btns[3]), 'KSFO_TWR 120.500: Set Standby');
});

test('non-ATC buttons named differently are unaffected', () => {
  const dom = new JSDOM('<!DOCTYPE html><body><div class="bg-theme-secondary"><h2>Card</h2><button>Apply</button></div></body>', { runScripts: 'outside-only' });
  const w = dom.window;
  w.SimVar = { GetSimVarValue() { return 0; }, SetSimVarValue() {} };
  w.eval(fs.readFileSync(AGENT, 'utf8'));
  const A = w.__MSFSBA_FLYPAD;
  assert.strictEqual(A.labelFor(w.document.querySelector('button')), 'Apply');
});
