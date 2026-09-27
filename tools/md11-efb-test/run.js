'use strict';
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-md11-efb-agent.js');

// The EFB's nav bar (IconButton): seven buttons, the active one bg-red-800. Synthetic fixtures
// prepend it with {nav: 'Perf'} so page-gated blocks (the Dispatch header) and the tab list behave.
const TABS = ['Dispatch', 'Payload', 'Perf', 'Charts', 'Services', 'State', 'Options'];
function navBar(active) {
  return '<div class="flex flex-row" data-vis="1">' + TABS.map(function (t) {
    return '<button class="mx-2 flex flex-grow items-center rounded-b-lg ' + (t === active ? 'bg-red-800' : 'bg-zinc-600') + '" data-vis="1">' +
      '<div class="z-10 w-full text-center text-xs text-white" data-vis="1"><div class="flex place-content-center gap-x-3" data-vis="1">' +
      '<span class="font-mono text-xs" data-vis="1">' + t + '</span></div></div></button>';
  }).join('') + '</div>';
}

// Load a fixture and the real agent into jsdom.
// Visibility convention (same as the live captures): visible elements carry data-vis="1"; anything
// without it is hidden. Live captures also carry data-rect="top,left,right,bottom".
// opts.nav      prepend the nav bar with that tab active
// opts.autoVis  synthetic fixture: mark every element visible unless it or an ancestor has
//               class "hidden" or data-vis="0"
function load(fixtureName, opts) {
  opts = opts || {};
  let html = fs.readFileSync(path.join(__dirname, 'fixtures', fixtureName + '.html'), 'utf8');
  if (opts.nav) html = navBar(opts.nav) + html;
  const dom = new JSDOM('<!DOCTYPE html><html><body><div id="MSFS_REACT_MOUNT" data-vis="1">' + html + '</div></body></html>');
  const { window } = dom;
  const doc = window.document;
  global.window = window; global.document = doc;
  // window.eval() resolves bare identifiers against the Node global, not the jsdom window's own
  // realm (a jsdom/indirect-eval quirk a real Coherent GT browser doesn't have) -- MutationObserver
  // must be exposed the same way window/document are, or the agent's `new MutationObserver(...)`
  // throws and its own defensive "no observer -> never gate" fallback permanently disables the
  // unchanged:true dirty gate, forcing every scrape() call to do a full traversal.
  global.MutationObserver = window.MutationObserver;

  if (opts.autoVis) {
    (function mark(el, hiddenAbove) {
      const hidden = hiddenAbove || el.classList.contains('hidden') || el.getAttribute('data-vis') === '0';
      if (hidden) el.removeAttribute('data-vis'); else el.setAttribute('data-vis', '1');
      for (const c of el.children) mark(c, hidden);
    })(doc.getElementById('MSFS_REACT_MOUNT'), false);
  }

  const vis = el => el.getAttribute && el.getAttribute('data-vis') === '1';

  window.getComputedStyle = function (el) {
    return { display: vis(el) ? 'block' : 'none', visibility: 'visible', opacity: '1' };
  };
  window.Element.prototype.getBoundingClientRect = function () {
    const r = this.getAttribute('data-rect') || (vis(this) ? '0,0,100,20' : '0,0,0,0');
    const p = r.split(',').map(Number);
    return { top: p[0], left: p[1], right: p[2], bottom: p[3], width: p[2] - p[1], height: p[3] - p[0], x: p[1], y: p[0] };
  };

  // jsdom has no innerText. Chromium's innerText (what the live agent reads) skips display:none
  // subtrees, breaks lines around block elements, and APPLIES text-transform — the EFB's
  // Tailwind "uppercase" class makes "Save" read "SAVE". All three are emulated: hidden = no
  // data-vis; a block child contributes "\n" before and after (so <p>A</p><p>B</p> reads "A B"
  // once the agent collapses whitespace, never "AB"); uppercase = class "uppercase" on the
  // element or any ancestor inside the walk.
  const BLOCK = new Set(['P', 'DIV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'LI', 'UL', 'OL', 'PRE', 'HR', 'TR', 'TABLE', 'SECTION', 'LABEL']);
  function hasUpper(el) {
    for (let n = el; n && n.nodeType === 1; n = n.parentElement) if (n.classList.contains('uppercase')) return true;
    return false;
  }
  Object.defineProperty(window.HTMLElement.prototype, 'innerText', {
    configurable: true,
    get() {
      let s = '';
      (function walk(n, up) {
        for (const c of n.childNodes) {
          if (c.nodeType === 3) s += up ? c.data.toUpperCase() : c.data;
          else if (c.nodeType === 1 && c.tagName === 'BR') s += '\n';
          else if (c.nodeType === 1 && vis(c)) {
            const block = BLOCK.has(c.tagName);
            if (block) s += '\n';
            walk(c, up || c.classList.contains('uppercase'));
            if (block) s += '\n';
          }
        }
      })(this, hasUpper(this));
      return s;
    }
  });

  // Stepper option lists live in React's fiber tree on the live EFB
  // (input[__reactFiber$…].return.return.memoizedProps.options — 2 hops, probed 2026-09-05).
  // A fixture seeds the same shape from data-options="A|B|C" on the disabled input.
  // data-options="" seeds an EMPTY options array — the shape the live Select renders before its
  // list exists (no runway options until an airport is entered). Splitting "" would seed a
  // one-option list labelled "", which is a different, non-existent state.
  for (const inp of doc.querySelectorAll('input[data-options]')) {
    const spec = inp.getAttribute('data-options');
    const labels = spec === '' ? [] : spec.split('|');
    inp['__reactFiber$jsdom'] = { memoizedProps: {}, return: { memoizedProps: {}, return: { memoizedProps: { options: labels.map(l => ({ label: l, value: l })) } } } };
  }

  window.eval(fs.readFileSync(AGENT, 'utf8'));
  return { window, document: doc, A: window.__MSFSBA_MD11_EFB };
}

// Re-run the agent IIFE into a window that already carries one — what the client's EnsureConnected
// does on a STILL-OPEN socket when an eval times out. `A` is replaced by a brand new object, so
// whatever the previous injection left attached to the page (MutationObserver, document listeners)
// must be torn down through the handles stashed on `window`. Returns the new agent.
function reinject(window) {
  window.eval(fs.readFileSync(AGENT, 'utf8'));
  return window.__MSFSBA_MD11_EFB;
}

function scrape(fixtureName, opts) {
  const { A } = load(fixtureName, opts);
  const r = JSON.parse(A.scrape());
  if (!r.ok) throw new Error('scrape failed: ' + r.error);
  return r.elements;
}

// One readable line per element — the shape the whole-page assertions compare against.
function lines(els) {
  return els.map(e => (e.kind || ('/' + e.controlType)) + '|' + e.text + (e.value ? '=' + e.value : ''));
}

module.exports = { load, reinject, scrape, lines, navBar, TABS };
