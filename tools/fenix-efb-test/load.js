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
const turn = () => new Promise(resolve => setTimeout(resolve, TURN_MS));

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
  if (layer) {
    try {
      window.eval(fs.readFileSync(LAYER, 'utf8'));
    } catch (err) {
      // A layer that throws on load must not leave a live window (its timers and observer) behind.
      window.close();
      throw err;
    }
  }
  await turn();
  await turn();
  return window;
}

module.exports = { load };
