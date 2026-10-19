'use strict';
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-sim-tablet-agent.js');

// Load a captured fixture (capture.js) and the real agent into jsdom.
// Visibility convention, the same as tools/md11-efb-test: a rendered element carries data-vis="1"
// and its live box as data-rect="top,left,right,bottom"; anything without data-vis is hidden.
// data-h names the handlers the live element had ("click", "mousedown"): each becomes a stub
// that records the press in window.__presses as "<event>:<text>", so a test can tell which event
// reached which element.
function load(fixtureName) {
  const html = fs.readFileSync(path.join(__dirname, 'fixtures', fixtureName + '.html'), 'utf8');
  const dom = new JSDOM('<!DOCTYPE html><html><body>' + html + '</body></html>');
  const { window } = dom;
  const doc = window.document;
  global.window = window; global.document = doc;
  // window.eval resolves bare identifiers against the Node global (a jsdom quirk), so the agent's
  // `new MutationObserver(...)` and `new InputEvent(...)` need them here too.
  global.MutationObserver = window.MutationObserver;
  global.InputEvent = window.InputEvent;
  global.MouseEvent = window.MouseEvent;

  const vis = el => el.getAttribute && el.getAttribute('data-vis') === '1';
  window.getComputedStyle = function (el) {
    return { display: vis(el) ? 'block' : 'none', visibility: 'visible', opacity: '1' };
  };
  window.Element.prototype.getBoundingClientRect = function () {
    const r = this.getAttribute('data-rect') || (vis(this) ? '0,0,100,20' : '0,0,0,0');
    const p = r.split(',').map(Number);
    return { top: p[0], left: p[1], right: p[2], bottom: p[3], width: p[2] - p[1], height: p[3] - p[0], x: p[1], y: p[0] };
  };

  window.__presses = [];
  for (const el of doc.querySelectorAll('[data-h]')) {
    const name = el.textContent.replace(/\s+/g, ' ').trim().slice(0, 40);
    const hs = el.getAttribute('data-h').split(',');
    if (hs.indexOf('click') >= 0) el.onclick = () => window.__presses.push('click:' + name);
    if (hs.indexOf('mousedown') >= 0) el.onmousedown = () => window.__presses.push('mousedown:' + name);
  }

  window.eval(fs.readFileSync(AGENT, 'utf8'));
  return { window, document: doc, A: window.__MSFSBA_SIM_TABLET };
}

function scrapeResult(fixtureName) {
  const { A } = load(fixtureName);
  const r = JSON.parse(A.scrape());
  if (!r.ok) throw new Error('scrape failed: ' + r.error);
  return r;
}

function scrape(fixtureName) { return scrapeResult(fixtureName).elements; }

// One readable line per element, the shape the whole-page assertions compare against.
function lines(els) {
  return els.map(e => (e.kind || ('/' + e.controlType)) + '|' + e.text + (e.value ? '=' + e.value : '') + (e.disabled ? ' [dim]' : ''));
}

module.exports = { load, scrape, scrapeResult, lines };

// `node run.js <fixture>` prints a fixture's page and elements, for writing a test.
if (require.main === module) {
  const r = scrapeResult(process.argv[2]);
  console.log('page: ' + r.page);
  console.log(lines(r.elements).join('\n'));
}
