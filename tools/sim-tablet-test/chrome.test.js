'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape, scrapeResult, lines } = require('./run');

const DOCK = ['Flight Planner', 'Aircraft', 'Pilot book', 'Settings', 'More apps'];

test('the dock comes first, as tabs named by their tooltips, the open app marked', () => {
  const els = scrape('home');
  const tabs = els.slice(0, 5);
  assert.deepStrictEqual(tabs.map(e => e.kind), Array(5).fill('tab'));
  assert.deepStrictEqual(tabs.map(e => e.text), DOCK.slice(0, 4).concat(['More apps (current page)']));
  assert.ok(tabs.every(e => e.clickable && e.key === 'dock:' + e.text.replace(' (current page)', '')));
});

test('the home screen lists every installed app by its own name', () => {
  const r = scrapeResult('home');
  assert.equal(r.page, 'Home');
  const apps = r.elements.filter(e => e.kind === 'button').map(e => e.text);
  assert.deepStrictEqual(apps, ['Flight Planner', 'Aircraft', 'Pilot book', 'Settings', 'CIVA INS', 'Ground Services', 'GSX', 'SayIntentions.AI']);
});

test('the page names the app and the page it shows', () => {
  assert.equal(scrapeResult('aircraft-main').page, 'Aircraft');
  assert.equal(scrapeResult('aircraft-mass-seats').page, 'Aircraft, Mass and balance');
  assert.equal(scrapeResult('aircraft-checklists').page, 'Aircraft, Checklists');
  assert.equal(scrapeResult('aircraft-info').page, 'Aircraft, Aircraft information');
  assert.equal(scrapeResult('atlas-route-loaded').page, 'Flight Planner, Route');
  assert.equal(scrapeResult('settings').page, 'Settings');
});

test('the SimBrief action shows on the Flight Planner only, at a fixed idx and key', () => {
  const route = scrape('atlas-route-loaded');
  const sb = route.find(e => e.key === 'action:simbrief');
  assert.ok(sb, 'on the Flight Planner');
  assert.equal(sb.idx, 990001);
  assert.equal(sb.text, 'Load latest SimBrief plan');
  assert.equal(route.indexOf(sb), 5, 'right after the dock');
  for (const f of ['home', 'aircraft-main', 'settings']) assert.ok(!scrape(f).some(e => e.key === 'action:simbrief'), f);
});

test('a number and its unit, and a label and its value, are spaced even where their boxes touch', () => {
  const info = lines(scrape('aircraft-info'));
  assert.ok(info.includes('static|Cruise Speed 124 KT'));
  assert.ok(info.includes('static|Max Altitude 14 000 FT'));
  assert.ok(info.includes('static|Range 640 NM'));
  assert.ok(lines(scrape('settings')).includes('static|OS Version 1.0.3'));
});

test('a row of separate spans reads as ONE line', () => {
  const seats = lines(scrape('aircraft-mass-seats'));
  assert.ok(seats.includes('static|340 LBS - 2/4 seats'));
  assert.ok(lines(scrape('aircraft-main')).includes('static|Type Airplane'));
});

test('a caption over its value reads as one line with a colon (the map strip)', () => {
  const l = lines(scrape('atlas-route-loaded'));
  for (const s of ['Ground Speed: 0 KT', 'Altitude GPS: 370 FT', 'Track: 270°', 'ETE: - HR']) assert.ok(l.includes('static|' + s), s);
});

test('icon buttons are named by tooltip, class or icon, and say what they are for', () => {
  const l = lines(scrape('atlas-route-loaded'));
  for (const s of ['Route (selected)', 'Payload & fuel', 'Navlog', 'Nearest airports', 'Recenter on aircraft', 'Departure airport info', 'Arrival airport info'])
    assert.ok(l.includes('button|' + s), s);
  assert.ok(lines(scrape('settings')).includes('button|Trash Flight Planner'));
  assert.ok(lines(scrape('aircraft-mass-seats')).includes('button|More information'));
});

test('an open popup is read first, its title an alert', () => {
  const els = scrape('atlas-route-string');
  const i = els.findIndex(e => e.kind === 'alert' && e.text === 'Enter route string');
  assert.ok(i > 0 && i < 10, 'near the top');
  assert.equal(els[i + 1].text, 'Close');
  assert.equal(els[i + 2].controlType, 'text');
  assert.equal(els[i + 2].text, 'Enter route string');
  assert.equal(els[i + 3].text, 'SAVE');
  assert.equal(els.filter(e => e.text === 'Enter route string').length, 2, 'the title is not read again as a line');
});

test('a disabled control is dim: disabled is an ATTRIBUTE on the OS controls', () => {
  const e = scrape('atlas-load-from-web').find(x => x.text === 'NO FURTHER FLIGHT PLANS');
  assert.ok(e);
  assert.equal(e.disabled, true);
});

test('capture support: findRoot and isVisible exist for tools/coherent.ps1', () => {
  const { A, document } = load('home');
  assert.equal(A.findRoot(), document.querySelector('.efb'));
  assert.equal(A.isVisible(document.querySelector('.dock')), true);
});
