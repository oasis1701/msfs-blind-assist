'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { scrape, lines } = require('./run');

test('signed out: heading, explanation, Sign in', () => {
  assert.deepStrictEqual(lines(scrape('charts-signedout')).slice(7),
    ['heading|Charts', 'static|You are currently not signed in with Navigraph', 'button|Sign in']);
});

test('signing in: the site and the code to type are readable text, the QR code is skipped', () => {
  assert.deepStrictEqual(lines(scrape('charts-signin', { autoVis: true, nav: 'Charts' })).slice(7),
    ['heading|Signing in with Navigraph', 'heading|Scan a QR Code', 'static|OR', 'heading|Visit Navigraph',
     'static|navigraph.com/code', 'static|Type in:', 'static|ABCD-EFGH']);
});

test('signed in: search field, Search button, the chart-type strip with (selected), one button per chart', () => {
  assert.deepStrictEqual(lines(scrape('charts-list', { autoVis: true, nav: 'Charts' })).slice(7),
    ['/text|Airport ICAO=KLAX', 'button|Search', 'tab|STAR (selected)', 'tab|APP', 'tab|TAXI', 'tab|SID', 'tab|REF',
     'button|ANJLL FOUR (RNAV) 10-2A', 'button|BASET ONE 10-2B',
     'heading|Select an airport', 'static|To get started, type an airport into the "Airport ICAO" box and press search!']);
});
