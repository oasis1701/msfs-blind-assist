'use strict';
// Offline harness for the A380 MFD scrape (coherent-a380-agent.js). Loads a real
// in-flight DOM fixture under jsdom with the LIVE visibility (data-vis) and geometry
// (data-rect) baked in by tools/_probe/_capture_fixture.js, so A.enumerateLines runs
// exactly as it does in the sim — no layout engine needed.
//
//   node run.js perf_clb         # print the scraped lines for a fixture
//   node run.js                  # defaults to perf_clb
//
// Fixtures live in ./fixtures/*.html. Use this to develop buildPerfLines: edit
// ../../MSFSBlindAssist/Resources/coherent-a380-agent.js, re-run, compare.

const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-a380-agent.js');

function loadFixture(name) {
  const html = fs.readFileSync(path.join(__dirname, 'fixtures', name + '.html'), 'utf8');
  const dom = new JSDOM('<!DOCTYPE html><html><body>' + html + '</body></html>', { runScripts: 'outside-only' });
  const { window } = dom;

  // jsdom has no layout: feed back the geometry captured live.
  window.Element.prototype.getBoundingClientRect = function () {
    const r = (this.getAttribute && this.getAttribute('data-rect')) || '0,0,0,0';
    const p = r.split(',').map(Number);
    const top = p[0] || 0, left = p[1] || 0, right = p[2] || 0, bottom = p[3] || 0;
    return { top, left, right, bottom, width: right - left, height: bottom - top, x: left, y: top };
  };
  // Stubs the agent may touch (not needed for the scrape, but avoid ReferenceErrors).
  window.SimVar = { GetSimVarValue() { return ''; }, SetSimVarValue() {} };
  window.Coherent = { trigger() {}, call() { return Promise.resolve(); } };

  window.eval(fs.readFileSync(AGENT, 'utf8'));   // IIFE → window.__MSFSBA_A380
  const A = window.__MSFSBA_A380;
  if (!A) throw new Error('agent did not install window.__MSFSBA_A380');
  // Visibility from the captured data-vis flag (jsdom can't compute it).
  A.isVisible = function (el) { return !!(el && el.getAttribute && el.getAttribute('data-vis') === '1'); };
  return { window, A };
}

function scrapeLines(name) {
  const { window, A } = loadFixture(name);
  // The fixture's top element IS findRoot's root (what scrape passes to
  // enumerateLines); let enumerateLines do its own navigator-container scoping,
  // exactly as the live scrape does.
  const root = window.document.body.firstElementChild;
  const lines = A.enumerateLines(root);
  return lines.map(function (e) { return e.kind + ' | idx ' + e.idx + ' | ' + e.text; });
}

function scrapeElements(name) {
  const { window, A } = loadFixture(name);
  const root = window.document.body.firstElementChild;
  return A.enumerateLines(root).map(function (e) { return { kind: e.kind, idx: e.idx, text: e.text }; });
}

if (require.main === module) {
  const name = process.argv[2] || 'perf_clb';
  console.log('# ' + name);
  console.log(scrapeLines(name).join('\n'));
}

module.exports = { loadFixture, scrapeLines, scrapeElements };
