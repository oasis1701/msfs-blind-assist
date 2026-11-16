'use strict';
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

// SIM_TABLET_AGENT runs another copy of the reader (an older one, to diff what a change does).
const AGENT = process.env.SIM_TABLET_AGENT ||
  path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-sim-tablet-agent.js');

// Load a captured fixture (capture.js) and the real agent into jsdom.
// Visibility convention, the same as tools/md11-efb-test: a rendered element carries data-vis="1"
// and its live box as data-rect="top,left,right,bottom"; anything without data-vis is hidden.
// data-h names the press events the live element listened to ("click", "mousedown"…): each becomes a
// stub that records the press in window.__presses as "<event>:<text>", so a test can tell which event
// reached which element. A click or mousedown stub is an on* property, as the OS sets them; any other
// event, and every event with { listeners: 'bound' }, is an addEventListener listener bound BEFORE
// the reader is installed, as an add-on's are live, so only window.__gel (a stand-in for the
// debugger's getEventListeners, read from data-h) shows it. A capture of an app's own frame starts
// with its <body>, whose attributes go on the document's body.
function load(fixtureName, opts) {
  opts = opts || {};
  let html = fs.readFileSync(path.join(__dirname, 'fixtures', fixtureName + '.html'), 'utf8');
  let bodyAttrs = '';
  const frame = /^\s*<body([^>]*)>([\s\S]*)<\/body>\s*$/.exec(html);
  if (frame) { bodyAttrs = frame[1]; html = frame[2]; }
  const dom = new JSDOM('<!DOCTYPE html><html><body' + bodyAttrs + '>' + html + '</body></html>');
  const { window } = dom;
  const doc = window.document;
  global.window = window; global.document = doc;
  // window.eval resolves bare identifiers against the Node global (a jsdom quirk), so the agent's
  // `new MutationObserver(...)` and `new InputEvent(...)` need them here too.
  global.MutationObserver = window.MutationObserver;
  global.InputEvent = window.InputEvent;
  global.MouseEvent = window.MouseEvent;
  global.Event = window.Event;          // else Node's own Event, which a jsdom element refuses

  const vis = el => el.getAttribute && el.getAttribute('data-vis') === '1';
  // data-ptr="1": the page set a pointer cursor on the element itself (capture.js), which the reader's
  // A.ownPointer reads; fixtures captured before 2026-11-16 carry none.
  window.getComputedStyle = function (el) {
    return { display: vis(el) ? 'block' : 'none', visibility: 'visible', opacity: '1',
             cursor: el.getAttribute && el.getAttribute('data-ptr') === '1' ? 'pointer' : 'auto' };
  };
  window.Element.prototype.getBoundingClientRect = function () {
    const r = this.getAttribute('data-rect') || (vis(this) ? '0,0,100,20' : '0,0,0,0');
    const p = r.split(',').map(Number);
    return { top: p[0], left: p[1], right: p[2], bottom: p[3], width: p[2] - p[1], height: p[3] - p[0], x: p[1], y: p[0] };
  };

  window.__presses = [];
  for (const el of doc.querySelectorAll('[data-h]')) {
    const name = el.textContent.replace(/\s+/g, ' ').trim().slice(0, 40);
    for (const type of el.getAttribute('data-h').split(',').filter(Boolean)) {
      const stub = () => window.__presses.push(type + ':' + name);
      if (opts.listeners !== 'bound' && (type === 'click' || type === 'mousedown')) el['on' + type] = stub;
      else el.addEventListener(type, stub);
    }
  }
  window.__gel = el => {
    const out = {};
    for (const t of (el.getAttribute('data-h') || '').split(',').filter(Boolean)) out[t] = [{ type: t }];
    return out;
  };

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
