'use strict';
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-a300-efb-agent.js');

// The tablet view is 100vw wide; the payload screen's two halves slide by 100vw.
const VIEW_WIDTH = 2048;

// Where an element sits: its data-rect="top,left,right,bottom", else on screen at (0,0,100,20).
function rectOf(el) {
  const p = (el.getAttribute('data-rect') || '0,0,100,20').split(',').map(Number);
  return { top: p[0], left: p[1], right: p[2], bottom: p[3], width: p[2] - p[1], height: p[3] - p[0], x: p[1], y: p[0] };
}

// Load a hand-made fixture of the iniBuilds A300 tablet and the real agent into jsdom.
// Geometry: an element carries data-rect="top,left,right,bottom"; without one it is on screen
// (0,0,100,20). data-display="none" hides it. An inline background-color is the computed one.
// opts.simvars answers SimVar.GetSimVarValue. document.elementFromPoint answers from the same
// boxes (below) unless opts.noHitTest, which loads a view that lacks the call.
function load(fixtureName, opts) {
  opts = opts || {};
  const html = fs.readFileSync(path.join(__dirname, 'fixtures', fixtureName + '.html'), 'utf8');
  const dom = new JSDOM('<!DOCTYPE html><html><body class="contentLoaded">' + html + '</body></html>');
  const { window } = dom;
  global.window = window; global.document = window.document;
  // window.eval() resolves bare identifiers against the Node global, not the jsdom window (a
  // jsdom quirk Coherent GT does not have): expose the event constructors the agent's press uses.
  global.MouseEvent = window.MouseEvent;
  Object.defineProperty(window, 'innerWidth', { configurable: true, get() { return VIEW_WIDTH; } });

  // The slide buttons are red or green by an inline background-color, which the agent reads here.
  window.getComputedStyle = function (el) {
    return {
      display: el.getAttribute('data-display') || 'block', visibility: 'visible',
      backgroundColor: (el.style && el.style.backgroundColor) || ''
    };
  };
  window.Element.prototype.getBoundingClientRect = function () { return rectOf(this); };

  // jsdom has no layout and no hit testing, so a press would never be asked where a finger lands.
  // The boxes above answer it. The topmost element at a point is the LAST one in document order
  // that is displayed and whose box holds the point: a later element paints over an earlier one,
  // and a child comes after its parent, so it is also the deepest. An element is displayed unless
  // it or an ancestor has data-display="none". A point outside the view hits nothing (null), as
  // in Chromium; a point no box holds hits the body. Elements without a data-rect all share the
  // default box, so give a data-rect to a control and to whatever sits in it or over it when a
  // test depends on which of them is hit. opts.noHitTest leaves the call out, as a view that lacks
  // it would; the agent then looks for the handler inside the control instead.
  window.document.elementFromPoint = opts.noHitTest ? undefined : function (x, y) {
    if (!(x >= 0 && x < VIEW_WIDTH && y >= 0)) return null;
    let top = window.document.body;
    for (const el of window.document.body.querySelectorAll('*')) {
      if (el.closest('[data-display="none"]')) continue;
      const r = rectOf(el);
      if (x >= r.left && x < r.right && y >= r.top && y < r.bottom) top = el;
    }
    return top;
  };
  // jsdom has no innerText. Chromium's breaks lines at <br> and around blocks; the agent collapses
  // whitespace, so a newline is enough to keep words apart.
  const BLOCK = new Set(['P', 'DIV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'LI', 'LABEL']);
  Object.defineProperty(window.HTMLElement.prototype, 'innerText', {
    configurable: true,
    get() {
      let s = '';
      (function walk(n) {
        for (const c of n.childNodes) {
          if (c.nodeType === 3) s += c.data;
          else if (c.nodeType === 1 && c.tagName === 'BR') s += '\n';
          else if (c.nodeType === 1 && c.getAttribute('data-display') !== 'none') {
            const block = BLOCK.has(c.tagName);
            if (block) s += '\n';
            walk(c);
            if (block) s += '\n';
          }
        }
      })(this);
      return s;
    }
  });

  const sim = opts.simvars || {};
  // Answers 'NAME|unit' first (so a test can pin the unit asked for), then 'NAME'; anything else is 0.
  window.SimVar = {
    GetSimVarValue: function (name, unit) {
      const has = k => Object.prototype.hasOwnProperty.call(sim, k);
      if (has(name + '|' + unit)) return sim[name + '|' + unit];
      return has(name) ? sim[name] : 0;
    }
  };
  global.SimVar = window.SimVar;

  window.eval(fs.readFileSync(AGENT, 'utf8'));
  return { window, A: window.__MSFSBA_A300_EFB, sim };
}

function scrape(A) {
  const r = JSON.parse(A.scrape());
  if (!r.ok) throw new Error('scrape failed: ' + r.error);
  return r;
}

const texts = r => r.elements.map(e => e.text);
const find = (r, text) => r.elements.find(e => e.text === text);

module.exports = { load, scrape, texts, find };
