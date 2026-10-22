'use strict';
// Navigation, checked against the LIVE captures (fixtures/home.html, switcher.html, Fenix 3.0.0.7438):
// the nine home tiles, the app switcher's nine pictures, the Home button and the top bar's chevron
// are buttons with names, and a second pass changes nothing.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const { computeAccessibleName } = require('dom-accessibility-api');
const { load } = require('./load');

const LAYER = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'fenix-efb-accessibility.js');
const TITLES = ['Fenix', 'Takeoff', 'Landing', 'Charts', 'Documents', 'Failures', 'Settings', 'MCDU', 'FAP'];

// Two turns of 60 ms: the layer's pass runs 50 ms after a mutation (load.js does the same).
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const turns = async () => { await sleep(60); await sleep(60); };

function press(window, target, init) {
  const event = new window.KeyboardEvent('keydown', Object.assign({ bubbles: true, cancelable: true }, init));
  target.dispatchEvent(event);
  return event;
}

// Loads a capture WITHOUT the layer, wraps addEventListener to count every listener added to an
// element, then evaluates the layer and lets its first pass settle.
async function loadCounting(fixture) {
  const window = await load(fixture, { layer: false });
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

// Every attribute of every element, in document order: what "the same attributes" means.
const attributesOf = window => [...window.document.querySelectorAll('*')]
  .map(el => el.localName + ':' + [...el.attributes].map(a => a.name + '=' + a.value).join('|'));

test('Home_tiles_are_named_buttons_and_the_quick_settings_button_keeps_its_own_name', async () => {
  const window = await load('home');
  try {
    const tiles = [...window.document.querySelectorAll('#appContainer > div.appIcon')];
    assert.strictEqual(tiles.length, 9);
    tiles.forEach((tile, i) => {
      assert.strictEqual(tile.getAttribute('role'), 'button', TITLES[i]);
      assert.strictEqual(tile.tabIndex, 0, TITLES[i]);
      assert.strictEqual(computeAccessibleName(tile), TITLES[i]);
    });

    // The home tiles hold the same pictures as the switcher, but the pictures are not buttons here.
    for (const img of window.document.querySelectorAll('img.app-img')) {
      assert.strictEqual(img.getAttribute('role'), null);
      assert.strictEqual(img.hasAttribute('data-msfsba'), false);
    }

    // The page already names this one; the layer leaves it exactly as it is.
    const quick = window.document.querySelector('button.top-bar-settings-btn');
    assert.strictEqual(computeAccessibleName(quick), 'Open quick settings');
    assert.strictEqual(quick.getAttribute('aria-label'), 'Open quick settings');
    assert.strictEqual(quick.hasAttribute('data-msfsba'), false);
    assert.strictEqual(quick.hasAttribute('role'), false);
  } finally {
    window.close();
  }
});

test('Home_button_is_a_button_named_Home_on_home', async () => {
  const window = await load('home');
  try {
    const home = window.document.getElementById('homeButtonFree');
    assert.strictEqual(home.getAttribute('role'), 'button');
    assert.strictEqual(home.tabIndex, 0);
    assert.strictEqual(computeAccessibleName(home), 'Home');
  } finally {
    window.close();
  }
});

test('Switcher_pictures_are_buttons_named_by_the_tile_titles_in_order', async () => {
  const window = await load('switcher');
  try {
    const images = [...window.document.querySelectorAll('img.app-img')];
    assert.strictEqual(images.length, 9);
    images.forEach((img, i) => {
      // The live parent is <div class="col noSel"><img class="app-img"><br></div>: it is the clickable thing.
      const parent = img.parentElement;
      assert.strictEqual(parent.className, 'col noSel', 'icon ' + i);
      assert.strictEqual(parent.getAttribute('role'), 'button', 'icon ' + i);
      assert.strictEqual(parent.tabIndex, 0, 'icon ' + i);
      assert.strictEqual(computeAccessibleName(parent), TITLES[i]);
      assert.strictEqual(img.getAttribute('role'), null, 'the picture itself is not the button');
      assert.strictEqual(img.hasAttribute('data-msfsba'), false);
    });
  } finally {
    window.close();
  }
});

test('Switcher_pictures_are_pressed_with_the_keyboard', async () => {
  const window = await load('switcher');
  try {
    const [first] = window.document.querySelectorAll('img.app-img');
    const parent = first.parentElement;
    let clicks = 0;
    parent.click = () => { clicks++; };
    assert.ok(press(window, parent, { key: 'Enter' }).defaultPrevented);
    assert.ok(press(window, parent, { key: ' ' }).defaultPrevented);
    assert.strictEqual(clicks, 2);
  } finally {
    window.close();
  }
});

test('Home_button_is_a_button_named_Home_on_the_switcher', async () => {
  const window = await load('switcher');
  try {
    const home = window.document.getElementById('homeButtonFree');
    assert.strictEqual(home.getAttribute('role'), 'button');
    assert.strictEqual(home.tabIndex, 0);
    assert.strictEqual(computeAccessibleName(home), 'Home');
  } finally {
    window.close();
  }
});

// R11: the top bar's chevron is the only way to open the switcher, and it is a bare div.
for (const fixture of ['switcher', 'my-flight']) {
  test('The_top_bar_chevron_is_a_button_named_App_switcher_on_' + fixture, async () => {
    const window = await load(fixture);
    try {
      const arrow = window.document.querySelector('div.popup-arrow');
      assert.ok(arrow, 'the capture has the chevron');
      assert.strictEqual(arrow.getAttribute('role'), 'button');
      assert.strictEqual(arrow.tabIndex, 0);
      assert.strictEqual(computeAccessibleName(arrow), 'App switcher');

      // Enter and Space press it through the page's own click.
      let clicks = 0;
      arrow.click = () => { clicks++; };
      assert.ok(press(window, arrow, { key: 'Enter' }).defaultPrevented);
      assert.ok(press(window, arrow, { key: ' ' }).defaultPrevented);
      assert.strictEqual(clicks, 2);
    } finally {
      window.close();
    }
  });
}

// The page hides the chevron where there is nothing to open (the home screen, quick settings): class
// top-bar-slot-hidden and aria-hidden="true". A hidden control must not be a Tab stop.
for (const fixture of ['home', 'quick-settings']) {
  test('The_top_bar_chevron_the_page_hides_is_left_alone_on_' + fixture, async () => {
    const window = await load(fixture);
    try {
      const arrow = window.document.querySelector('div.popup-arrow');
      assert.strictEqual(arrow.getAttribute('aria-hidden'), 'true', 'the capture has it hidden');
      assert.strictEqual(arrow.hasAttribute('role'), false);
      assert.strictEqual(arrow.hasAttribute('tabindex'), false);
      assert.strictEqual(arrow.hasAttribute('aria-label'), false);
      assert.strictEqual(arrow.hasAttribute('data-msfsba'), false);
    } finally {
      window.close();
    }
  });
}

test('The_top_bar_chevron_stops_being_a_Tab_stop_when_the_page_hides_it_and_is_one_again_when_shown', async () => {
  const window = await load('switcher');
  try {
    const arrow = window.document.querySelector('div.popup-arrow');
    assert.strictEqual(arrow.tabIndex, 0);

    // The page shows and hides it by toggling a class and aria-hidden (both captured above).
    arrow.classList.add('top-bar-slot-hidden');
    arrow.setAttribute('aria-hidden', 'true');
    await turns();
    assert.ok(arrow.tabIndex < 0, 'hidden: not a Tab stop');

    arrow.classList.remove('top-bar-slot-hidden');
    arrow.setAttribute('aria-hidden', 'false');
    await turns();
    assert.strictEqual(arrow.tabIndex, 0, 'shown again: a Tab stop');
    assert.strictEqual(computeAccessibleName(arrow), 'App switcher');
  } finally {
    window.close();
  }
});

test('Second_pass_changes_nothing', async () => {
  const { window, counts } = await loadCounting('home');
  try {
    const doc = window.document;
    const before = doc.documentElement.outerHTML;
    const attributes = attributesOf(window);
    window.__msfsbaFenixEfb.pass();
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(doc.documentElement.outerHTML, before);
    assert.deepStrictEqual(attributesOf(window), attributes);

    // The nine tiles and the home button; the hidden chevron is left alone. One listener on each.
    const handled = [...doc.querySelectorAll('[data-msfsba]')];
    assert.strictEqual(handled.length, 10);
    for (const el of handled) assert.strictEqual(counts.get(el), 1, 'one listener on ' + el.className);
    assert.strictEqual(counts.size, 10, 'no other element got a listener');
  } finally {
    window.close();
  }
});

test('Second_pass_changes_nothing_on_the_switcher', async () => {
  const { window, counts } = await loadCounting('switcher');
  try {
    const doc = window.document;
    const before = doc.documentElement.outerHTML;
    const attributes = attributesOf(window);
    window.__msfsbaFenixEfb.pass();
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(doc.documentElement.outerHTML, before);
    assert.deepStrictEqual(attributesOf(window), attributes);

    // Nine icons, the home button and the chevron.
    const handled = [...doc.querySelectorAll('[data-msfsba]')];
    assert.strictEqual(handled.length, 11);
    for (const el of handled) assert.strictEqual(counts.get(el), 1, 'one listener on ' + el.className);
    assert.strictEqual(counts.size, 11, 'no other element got a listener');
  } finally {
    window.close();
  }
});
