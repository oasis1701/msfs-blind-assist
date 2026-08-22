'use strict';
// Offline harness for A.flightInfo() in coherent-a380-agent.js (the FMS flight-progress
// readout behind the A380 D / Shift+D hotkeys). Unlike the DOM-scrape harnesses
// (perf-builder-test, pmdg-efb-test, ...), flightInfo() never touches rendered DOM
// geometry/visibility — it walks a live JS object graph
// (document.querySelector('a380x-mfd').fsInstrument.fmcService.master.guidanceController).
// So instead of loading a captured HTML fixture, this harness builds a minimal jsdom
// document, creates a bare <a380x-mfd> element, and hangs a stubbed object graph directly
// off it as plain properties — jsdom elements are ordinary JS objects, so this is legal
// and far cheaper than faking rendered markup for data that was never in the DOM anyway.
//
//   node run.js            # prints flightInfo() JSON for the built-in step-descent stub
//
// Tests: flightinfo.test.js

const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-a380-agent.js');

// Builds a jsdom window with a stubbed <a380x-mfd> object graph and the agent installed.
// opts.pw            : array of pseudo-waypoint stubs -> gc.currentPseudoWaypoints
// opts.alongTrack     : value returned by alongTrackDistancesToDestination.get(0)
// opts.flightPhase    : value SimVar.GetSimVarValue('L:A32NX_FMGC_FLIGHT_PHASE') returns
function load(opts) {
  opts = opts || {};
  const dom = new JSDOM('<!DOCTYPE html><html><body></body></html>', { runScripts: 'outside-only' });
  const { window } = dom;
  const document = window.document;

  const mfd = document.createElement('a380x-mfd');
  document.body.appendChild(mfd);

  const gc = {
    currentPseudoWaypoints: opts.pw || [],
    alongTrackDistancesToDestination: {
      get: function (i) { return (i === 0 && typeof opts.alongTrack === 'number') ? opts.alongTrack : null; }
    }
  };
  const master = { guidanceController: gc, flightPlanInterface: { active: null }, flightPlanService: null };
  mfd.fsInstrument = { fmcService: { master: master } };

  window.SimVar = {
    GetSimVarValue: function () { return (typeof opts.flightPhase === 'number') ? opts.flightPhase : 0; },
    SetSimVarValue: function () {}
  };
  window.Coherent = { trigger: function () {}, call: function () { return Promise.resolve(); } };

  window.eval(fs.readFileSync(AGENT, 'utf8'));   // IIFE -> window.__MSFSBA_A380
  const A = window.__MSFSBA_A380;
  if (!A) throw new Error('agent did not install window.__MSFSBA_A380');
  return { window, A };
}

// Convenience: run flightInfo() and return the parsed JSON.
function flightInfo(opts) {
  const { A } = load(opts);
  return JSON.parse(A.flightInfo());
}

if (require.main === module) {
  // Step-descent pseudo-waypoint (ident '(T/D)', mcduIdent '(S/D)') placed BEFORE the
  // real top-of-descent (ident '(T/D)', mcduIdent '(T/D)') — the exact shape reported for
  // the A32NX bug this mirrors.
  const info = flightInfo({
    pw: [
      { ident: '(T/D)', mcduIdent: '(S/D)', flightPlanInfo: { secondsFromPresent: 300, distanceFromAircraft: 40 } },
      { ident: '(T/D)', mcduIdent: '(T/D)', flightPlanInfo: { secondsFromPresent: 1800, distanceFromAircraft: 220 } }
    ]
  });
  console.log(JSON.stringify(info, null, 2));
}

module.exports = { load, flightInfo };
