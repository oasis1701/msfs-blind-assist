'use strict';
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const LAYER = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'fenix-efb-accessibility.js');
// The address the window loads (FenixEFBForm.EfbUrl), so location reads as it does in the app.
const EFB_URL = 'http://127.0.0.1:8083/?target=external#/efb';

// "Two macrotask turns" so the observer's pass has run. The layer schedules its pass 50 ms after a
// mutation, so each turn waits 60 ms: the first lets the layer's first pass fire, the second lets
// anything that pass changed (and the follow-up pass it schedules) settle before a test reads.
const TURN_MS = 60;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const turn = () => sleep(TURN_MS);
// Two turns, as load() waits after the layer starts: for a test that changes the page and then reads.
const turns = async () => { await turn(); await turn(); };

// The titles of the nine home tiles, in home order (the switcher's pictures take these names).
const TITLES = ['Fenix', 'Takeoff', 'Landing', 'Charts', 'Documents', 'Failures', 'Settings', 'MCDU', 'FAP'];

// The home screen keeps its tiles at visibility: hidden until their pictures have loaded: the page's
// app-select-app-screen binds #appContainer's visibility to presentationReady, which it sets after
// waitForImages and two animation frames (Fenix 3.0.0.7438 bundle). A capture taken before that moment
// (home.html of 2026-10-22) has the tiles hidden, where jsdom (and a screen reader) meets nothing, and jsdom
// loads no pictures to finish it. So the harness shows them as the page does a moment later, before the layer
// runs; it changes nothing else, and nothing on a capture whose tiles are already shown.
// The Mass and Balance tab's content is the same: the fuller mass-balance.html (2026-10-22, with the chart
// picture and the seat map) caught its div.tab-content at visibility: hidden, where the capture of the same
// screen before it had it visible and the owner's in-sim check heard its boxes. It is shown the same way.
const SHOWN_LATER = ['#appContainer', 'app-mass-balance-tab > div.tab-content'];
function present(window) {
  for (const selector of SHOWN_LATER) {
    for (const el of window.document.querySelectorAll(selector)) {
      if (el.style.visibility === 'hidden') el.style.visibility = 'visible';
    }
  }
}

// Load fixtures/<fixtureName>.html into jsdom and, unless { layer: false }, evaluate the real
// layer in it. Returns the jsdom window. runScripts "outside-only" runs no script the fixture
// carries (captures have none) and gives window.eval the jsdom realm, so a bare MutationObserver
// or Event in the layer is jsdom's own. (md11-efb-test/run.js patches the Node global because its
// JSDOM sets no runScripts, so window.eval runs in the Node realm; no patch is needed here.)
async function load(fixtureName, { layer = true } = {}) {
  const file = path.join(__dirname, 'fixtures', fixtureName + '.html');
  if (!fs.existsSync(file)) {
    throw new Error('fixture missing: ' + file + ' -- capture it as capture.md describes');
  }
  const html = fs.readFileSync(file, 'utf8');
  const dom = new JSDOM(html, { runScripts: 'outside-only', url: EFB_URL });
  const { window } = dom;
  present(window);
  if (layer) {
    try {
      window.eval(fs.readFileSync(LAYER, 'utf8'));
    } catch (err) {
      // A layer that throws on load must not leave a live window (its timers and observer) behind.
      window.close();
      throw err;
    }
  }
  await turns();
  return window;
}

// Loads a fixture WITHOUT the layer, wraps addEventListener to count every listener added to an
// element, then evaluates the layer and lets its first pass settle. Returns { window, counts }.
async function loadCounting(fixtureName) {
  const window = await load(fixtureName, { layer: false });
  const counts = new Map();
  const original = window.EventTarget.prototype.addEventListener;
  window.EventTarget.prototype.addEventListener = function (type, ...rest) {
    if (this.nodeType === 1) counts.set(this, (counts.get(this) || 0) + 1);
    return original.call(this, type, ...rest);
  };
  window.eval(fs.readFileSync(LAYER, 'utf8'));
  await turns();
  return { window, counts };
}

// Dispatches a keydown on the target and returns the event (so a test can read defaultPrevented).
function press(window, target, init) {
  const event = new window.KeyboardEvent('keydown', Object.assign({ bubbles: true, cancelable: true }, init));
  target.dispatchEvent(event);
  return event;
}

// Every attribute of every element, in document order: what "the same attributes" means.
const attributesOf = window => [...window.document.querySelectorAll('*')]
  .map(el => el.localName + ':' + [...el.attributes].map(a => a.name + '=' + a.value).join('|'));

// An element as tag#id.class (Angular's ng- classes left out), for a readable list.
function describe(el) {
  const id = el.getAttribute('id');
  const cls = (el.getAttribute('class') || '').split(/\s+/).filter(c => c && !c.startsWith('ng-')).join('.');
  return el.localName + (id ? '#' + id : '') + (cls ? '.' + cls : '');
}

// The elements the layer hid from a screen reader: aria-hidden "true" in `window` and not in `bare`, the same
// capture loaded without the layer. The layer adds and moves no node, so the two hold the same elements in the
// same order. Returns each as describe() gives it, in document order.
function hiddenByLayer(bare, window) {
  const before = [...bare.document.querySelectorAll('*')];
  const after = [...window.document.querySelectorAll('*')];
  if (before.length !== after.length) throw new Error('the two windows do not hold the same elements');
  return after.filter((el, i) => el.getAttribute('aria-hidden') === 'true' && before[i].getAttribute('aria-hidden') !== 'true')
    .map(describe);
}

module.exports = { load, loadCounting, press, attributesOf, describe, hiddenByLayer, sleep, turns, LAYER, TITLES };
