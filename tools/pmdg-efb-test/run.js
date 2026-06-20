'use strict';
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-pmdg-efb-agent.js');

// Load a fixture HTML file and the agent into a jsdom window.
// Fixtures bake geometry into data-rect="top,left,right,bottom" and visibility into data-vis="1".
// opts.settings seeds a window.Settings object (for the checkbox active-unit rule).
// opts.side seeds getTabletSide(); opts.path seeds pmdg_tablet_path.
function load(fixtureName, opts) {
  opts = opts || {};
  const html = fs.readFileSync(path.join(__dirname, 'fixtures', fixtureName + '.html'), 'utf8');
  const dom = new JSDOM('<!DOCTYPE html><html><body>' + html + '</body></html>');
  const { window } = dom;
  global.window = window; global.document = window.document;

  window.getComputedStyle = function (el, pseudo) {
    if (pseudo) {
      return { content: el.getAttribute('data-' + (pseudo === '::before' ? 'before' : 'after')) || 'none' };
    }
    return {
      display: el.getAttribute('data-display') || 'block',
      visibility: el.getAttribute('data-visibility') || 'visible',
      cursor: el.getAttribute('data-cursor') || 'auto',
      whiteSpace: el.getAttribute('data-ws') || 'normal',
      position: el.getAttribute('data-position') || 'static'
    };
  };
  window.Element.prototype.getBoundingClientRect = function () {
    const r = (this.getAttribute('data-rect')) || '0,0,0,0';
    const p = r.split(',').map(Number);
    return { top: p[0], left: p[1], right: p[2], bottom: p[3], width: p[2] - p[1], height: p[3] - p[0], x: p[1], y: p[0] };
  };
  // offsetParent: visible unless data-vis="0"
  Object.defineProperty(window.HTMLElement.prototype, 'offsetParent', {
    get() { return this.getAttribute('data-vis') === '0' ? null : (this.parentElement || window.document.body); }
  });
  window.getTabletSide = function () { return opts.side || 'CA'; };
  window.pmdg_tablet_path = opts.path || '/Pages/VCockpit/Instruments/PMDGTablet/pmdg-777-300ER';
  if (opts.settings) window.Settings = opts.settings;

  window.eval(fs.readFileSync(AGENT, 'utf8'));
  return { window, A: window.__MSFSBA_PMDG_EFB };
}

// Convenience: return the parsed scrape element list.
function scrape(fixtureName, opts) {
  const { A } = load(fixtureName, opts);
  return JSON.parse(A.scrape()).elements;
}

module.exports = { load, scrape };
