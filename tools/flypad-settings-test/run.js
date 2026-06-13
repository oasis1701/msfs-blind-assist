'use strict';
// Offline harness for the flyPad Settings scrape (coherent-flypad-agent.js).
// Loads a real flyPad DOM fixture under jsdom with live visibility (data-vis) and
// geometry (data-rect) baked in by tools/_probe/_capture_flypad_fixture.js, so the
// scrape runs exactly as it does in the sim — no layout engine needed.
//
//   node run.js aircraft     # print the scraped lines for a fixture
//   node run.js              # defaults to aircraft
//
// Fixtures live in ./fixtures/*.html.
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');
const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-flypad-agent.js');

function loadFixture(name) {
  const html = fs.readFileSync(path.join(__dirname, 'fixtures', name + '.html'), 'utf8');
  // Wrap so findRoot's #MSFS_REACT_MOUNT lookup succeeds.
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
  A.isVisible = function (el) { return !!(el && el.getAttribute && el.getAttribute('data-vis') === '1'); };
  return { window, A };
}

function scrape(name) { const { A } = loadFixture(name); return JSON.parse(A.scrape()); }
function lines(name) { return scrape(name).elements.map(e => e.kind + ' | idx ' + e.idx + ' | ' + e.text + (e.value ? ' = ' + e.value : '')); }

if (require.main === module) {
  const n = process.argv[2] || 'aircraft';
  console.log('# ' + n + ' (page: ' + scrape(n).page + ')');
  console.log(lines(n).join('\n'));
}
module.exports = { loadFixture, scrape, lines };
