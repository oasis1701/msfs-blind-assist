'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { flightInfo } = require('./run.js');

// Mirrors the bug fixed in coherent-a32nx-flightinfo.js:50 — a cruise StepDescent
// pseudo-waypoint carries ident '(T/D)' but mcduIdent '(S/D)', and sits UPSTREAM (earlier
// in currentPseudoWaypoints) of the real top-of-descent. flightInfo's per-waypoint
// classifier must key off mcduIdent first (it disambiguates step-descent vs. real T/D);
// checking ident first latches onto the step and masks the real T/D forever (the loop's
// "if (info.distToTD == null)" first-match-wins gate never lets the real one overwrite it).
test('flightInfo picks the real T/D, not an earlier step-descent pseudo-waypoint', () => {
  const info = flightInfo({
    pw: [
      // Step descent: ident says '(T/D)' but mcduIdent (the disambiguating field) says '(S/D)'.
      { ident: '(T/D)', mcduIdent: '(S/D)', flightPlanInfo: { secondsFromPresent: 300, distanceFromAircraft: 40 } },
      // Real top of descent, listed AFTER the step in the FMS's pseudo-waypoint array.
      { ident: '(T/D)', mcduIdent: '(T/D)', flightPlanInfo: { secondsFromPresent: 1800, distanceFromAircraft: 220 } }
    ]
  });

  assert.equal(info.ok, true);
  assert.equal(info.distToTD, 220, 'distToTD must be the REAL T/D (220 NM), not the step-descent point (40 NM)');
  assert.equal(info.timeToTD, 1800, 'timeToTD must be the REAL T/D time-to-go (1800s), not the step-descent point (300s)');
});

test('flightInfo still ignores a genuine (DECEL) pseudo-waypoint', () => {
  const info = flightInfo({
    pw: [
      { ident: '(DECEL)', mcduIdent: '(DECEL)', flightPlanInfo: { secondsFromPresent: 100, distanceFromAircraft: 10 } },
      { ident: '(T/D)', mcduIdent: '(T/D)', flightPlanInfo: { secondsFromPresent: 1800, distanceFromAircraft: 220 } }
    ]
  });
  assert.equal(info.distToTD, 220);
});
