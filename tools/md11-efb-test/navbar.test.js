'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, lines } = require('./run');

const TAB_TEXTS = active => ['Dispatch', 'Payload', 'Perf', 'Charts', 'Services', 'State', 'Options']
  .map(t => t + (t === active ? ' (current page)' : ''));

// The Charts chart-type strip (STAR/APP/TAXI/SID/REF) is a row of exactly five buttons, and five was
// TAB_BAR_MIN. First in DOM order, it WAS the nav bar: the five chart types were read as page tabs
// and the real tabs were lost.
test('the Charts strip is never taken for the nav bar, even ahead of it in the DOM', () => {
  const { A, document } = load('charts-list', { autoVis: true, nav: 'Charts' });
  const root = document.getElementById('MSFS_REACT_MOUNT');
  const nav = root.firstElementChild;              // the harness prepends the nav bar …
  root.appendChild(nav);                           // … move it after the page content
  assert.equal(A.findTabBar(), nav);
  const els = JSON.parse(A.scrape()).elements;
  assert.deepStrictEqual(els.slice(0, 7).map(e => e.text), TAB_TEXTS('Charts'));
  assert.ok(els.some(e => e.kind === 'tab' && e.text === 'STAR (selected)'), 'the strip still reads as its own tabs');
});

test('with no nav bar on the page, the Charts strip still does not become one', () => {
  const { A } = load('charts-list', { autoVis: true });
  assert.equal(A.findTabBar(), null);
});

// React keeps inactive views mounted; a hidden row of buttons is not the bar the pilot is looking at.
test('a hidden row of buttons ahead of the nav bar is skipped', () => {
  const { A, document } = load('options-general');
  const root = document.getElementById('MSFS_REACT_MOUNT');
  const ghost = document.createElement('div');     // no data-vis: hidden in the harness
  for (let i = 0; i < 7; i++) {
    const b = document.createElement('button');
    b.textContent = 'Ghost ' + i;
    ghost.appendChild(b);
  }
  root.insertBefore(ghost, root.firstChild);
  assert.notEqual(A.findTabBar(), ghost);
  assert.deepStrictEqual(JSON.parse(A.scrape()).elements.slice(0, 7).map(e => e.text), TAB_TEXTS('Options'));
});

// The marked nav row is the bar even with something else in it, and that something is still read.
test('a non-button child in the nav row keeps the bar, and is still read', () => {
  const { A, document } = load('perf-stepper-fallback', { autoVis: true, nav: 'Perf' });
  const nav = document.getElementById('MSFS_REACT_MOUNT').firstElementChild;
  const clock = document.createElement('div');
  clock.setAttribute('data-vis', '1');
  clock.textContent = '12:34Z';
  nav.appendChild(clock);
  assert.equal(A.findTabBar(), nav);
  const ls = lines(JSON.parse(A.scrape()).elements);
  assert.deepStrictEqual(ls.slice(0, 7), TAB_TEXTS('Perf').map(t => 'tab|' + t));
  assert.ok(ls.includes('static|12:34Z'), 'the other child in the nav row is still read: ' + ls.join('; '));
});
