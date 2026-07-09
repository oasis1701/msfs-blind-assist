'use strict';
// Cross-generation regression test for the A._gen memoization in
// coherent-flypad-agent.js (A.isVisible/A.classify).
//
// run.js's loadFixture() stubs A.isVisible with a data-vis-attribute reader
// (see run.js:32), which is great for fixture-driven output tests but means
// NONE of the existing suites ever exercise the real A._isVisibleRaw/A._gen
// memo wrappers — they can't catch a caching bug in those wrappers. This file
// loads the agent WITHOUT that stub (own loader below) so the real
// isVisible/classify memoization runs, then scrapes the same live DOM twice
// with a visibility mutation in between, exactly like the app does across
// its 600ms polls, to prove a change made between scrapes is observed.
const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-flypad-agent.js');

// Same fixture wrapping / getBoundingClientRect stub as run.js's loadFixture,
// but deliberately does NOT overwrite A.isVisible — we want the REAL memoizing
// wrapper (A._gen / __msfsbaGen / __msfsbaVis) under test, not the harness stub.
function loadFixtureReal(name) {
  const html = fs.readFileSync(path.join(__dirname, 'fixtures', name + '.html'), 'utf8');
  const dom = new JSDOM('<!DOCTYPE html><html><body><div id="MSFS_REACT_MOUNT">' + html + '</div></body></html>', { runScripts: 'outside-only' });
  const { window } = dom;
  window.Element.prototype.getBoundingClientRect = function () {
    const r = (this.getAttribute && this.getAttribute('data-rect')) || '0,0,0,0';
    const p = r.split(',').map(Number);
    const top = p[0] || 0, left = p[1] || 0, right = p[2] || 0, bottom = p[3] || 0;
    return { top, left, right, bottom, width: right - left, height: bottom - top, x: left, y: top };
  };
  window.SimVar = { GetSimVarValue() { return 0; }, SetSimVarValue() {} };
  window.Coherent = { trigger() {}, call() { return Promise.resolve(); } };
  window.eval(fs.readFileSync(AGENT, 'utf8'));
  const A = window.__MSFSBA_FLYPAD;
  if (!A) throw new Error('agent did not install window.__MSFSBA_FLYPAD');
  // NOTE: intentionally no `A.isVisible = ...` override here (that's the whole point).
  return { window, A };
}

function scrapeTexts(A) { return JSON.parse(A.scrape()).elements.map(e => e.text); }

test('a node hidden AFTER the first scrape is excluded from the second scrape (no frozen isVisible cache)', () => {
  const { window, A } = loadFixtureReal('aircraft');
  const document = window.document;

  // Gen 1: baseline scrape — "US Units" toggle row is visible (real isVisible
  // computation via getComputedStyle + the data-rect-backed getBoundingClientRect;
  // no inline display/visibility set yet, so it reads visible).
  const gen1 = scrapeTexts(A);
  assert.ok(gen1.some(t => t === 'US Units'), 'US Units missing from baseline scrape: ' + gen1.join(' | '));

  // Locate the setting ROW (the flex-row ancestor settingUnits() keys visibility
  // off of) by its label text, and hide it — simulating a real between-scrape DOM
  // mutation (e.g. a control becoming conditionally hidden on a later poll).
  const spans = Array.from(document.querySelectorAll('span'));
  const label = spans.find(s => s.textContent.trim() === 'US Units');
  assert.ok(label, 'could not locate "US Units" label span in fixture');
  const row = label.parentElement;
  assert.ok(row && row.classList.contains('flex-row'), 'unexpected DOM shape for the US Units row');
  row.style.display = 'none';

  // Gen 2: A.scrape() bumps A._gen internally, so every node's stale
  // __msfsbaGen/__msfsbaVis/__msfsbaKind must be invalidated and recomputed —
  // including nodes isVisible() touched FIRST last generation (the exact
  // scenario the gen-mismatch reset must cover for BOTH cached fields, not
  // just the field the "other" wrapper owns).
  const gen2 = scrapeTexts(A);
  assert.ok(!gen2.some(t => t === 'US Units'),
    'US Units still present after being hidden — isVisible cache was not invalidated across generations: ' + gen2.join(' | '));
});
