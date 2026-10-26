'use strict';
// Navigation, checked against the LIVE captures (fixtures/home.html, switcher.html, Fenix 3.0.0.7438):
// the nine home tiles, the app switcher's nine pictures, the Home button and the top bar's chevron
// are buttons with names, and a second pass changes nothing.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const { computeAccessibleName } = require('dom-accessibility-api');
const { load, loadCounting, press, attributesOf, hiddenByLayer, turns, LAYER, TITLES } = require('./load');

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

    // The home tiles hold the same pictures as the switcher, but the pictures are not buttons here. They have no alt,
    // so they are hidden from a screen reader; the tile is named by its title.
    for (const img of window.document.querySelectorAll('img.app-img')) {
      assert.strictEqual(img.getAttribute('role'), null);
      assert.strictEqual(img.hasAttribute('data-msfsba'), false);
      assert.strictEqual(img.getAttribute('aria-hidden'), 'true');
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
      // The live parent is <div class="col noSel"><img class="app-img"><br></div> (Angular adds its
      // ng-star-inserted marker too): it is the clickable thing.
      const parent = img.parentElement;
      assert.ok(parent.classList.contains('col') && parent.classList.contains('noSel'), 'icon ' + i + ': ' + parent.className);
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

// The switcher's pictures carry no text, so the layer names them from a constant list of the tile titles in
// home order. That is right only while the switcher shows the home tiles' own pictures in home order: the
// live check of 2026-10-22 found exactly that, and both captures keep each picture's path, so this pins it.
// A Fenix update that reorders the switcher (or the tiles) fails here.
test('Switcher_pictures_are_the_home_tiles_pictures_in_home_order_and_each_takes_its_own_tiles_title', async () => {
  let home = null;
  let switcher = null;
  try {
    home = await load('home', { layer: false });
    switcher = await load('switcher');
    const tiles = [...home.document.querySelectorAll('#appContainer > div.appIcon')];
    const tileSrcs = tiles.map(tile => tile.querySelector('img.app-img').getAttribute('src'));
    assert.strictEqual(tiles.length, 9);
    assert.ok(tileSrcs.every(src => typeof src === 'string' && src.startsWith('/assets/img/')),
      'each home tile keeps its picture\'s path: ' + JSON.stringify(tileSrcs));
    assert.strictEqual(new Set(tileSrcs).size, 9, 'nine different pictures');
    assert.deepStrictEqual(tiles.map(tile => tile.querySelector('.appTitle').textContent.trim()), TITLES);

    const icons = [...switcher.document.querySelectorAll('img.app-img')].filter(img => !img.closest('div.appIcon'));
    assert.deepStrictEqual(icons.map(img => img.getAttribute('src')), tileSrcs, 'the switcher shows them in home order');
    // So each switcher picture is named by the title of the home tile that shows the same picture.
    for (const img of icons) {
      const title = TITLES[tileSrcs.indexOf(img.getAttribute('src'))];
      assert.strictEqual(computeAccessibleName(img.parentElement), title, img.getAttribute('src'));
    }
  } finally {
    if (home) home.close();
    if (switcher) switcher.close();
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

// The top bar's chevron is the only way to open the switcher, and it is a bare div.
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

    // Nine icons, the home button and the chevron. The capture also shows the Fenix app behind the switcher
    // (its tab bar and My Flights cards); those are fenix-app.test.js's, which counts them on this capture.
    const outside = el => !el.closest('app-fenix');
    const handled = [...doc.querySelectorAll('[data-msfsba]')].filter(outside);
    assert.strictEqual(handled.length, 11);
    for (const el of handled) assert.strictEqual(counts.get(el), 1, 'one listener on ' + el.className);
    assert.strictEqual([...counts.keys()].filter(outside).length, 11, 'no other element got a listener');
  } finally {
    window.close();
  }
});

// ---- What a screen reader does not need ----

// The background picture is the first thing on every screen, and the top bar's status icons are pictures of a
// state the pilot does not act on. None has an alt of its own; all are hidden from a screen reader. So is the glyph
// of the quick settings cog (an icon font's glyph inside a button), and so are the tiles' pictures (a picture
// with no alt). Nothing else on home is.
test('The_background_and_the_top_bar_status_icons_are_hidden_from_a_screen_reader', async () => {
  const bare = await load('home', { layer: false });
  const window = await load('home');
  try {
    const pictures = ['img#appBg', 'img.airplane-icon', 'img.signal-icon', 'img#linkedup'].map(s => window.document.querySelector(s));
    assert.ok(pictures.every(Boolean), 'the capture has all four');
    assert.ok(pictures.every(img => !img.hasAttribute('alt')), 'none has an alt of its own');
    assert.deepStrictEqual(pictures.map(img => img.getAttribute('aria-hidden')), ['true', 'true', 'true', 'true']);
    assert.ok(pictures.every(img => !img.hasAttribute('role') && !img.hasAttribute('data-msfsba')), 'hidden, nothing else');
    assert.deepStrictEqual(hiddenByLayer(bare, window), [
      'img#appBg', 'img.airplane-icon.top-icon-fade-out', 'img.signal-icon.top-icon-fade-in', 'img#linkedup', 'i.fas.fa-cog',
      'img.battery-none', 'img.lightning', ...Array(9).fill('img.app-img')
    ], 'the top bar\'s battery pictures have no alt either, so they are hidden too');
    // The quick settings button keeps the page's own name; its glyph is not part of it.
    assert.strictEqual(computeAccessibleName(window.document.querySelector('button.top-bar-settings-btn')), 'Open quick settings');
  } finally {
    bare.close();
    window.close();
  }
});

test('A_hidden_state_the_page_set_on_a_top_bar_picture_is_kept', async () => {
  const window = await load('home', { layer: false });
  try {
    const background = window.document.getElementById('appBg');
    const link = window.document.getElementById('linkedup');
    background.setAttribute('aria-hidden', 'false');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(background.getAttribute('aria-hidden'), 'false', 'the page\'s own');
    assert.strictEqual(link.getAttribute('aria-hidden'), 'true', 'the others as usual');
  } finally {
    window.close();
  }
});

// The page gives two plain wrappers a click handler of their own, so a screen reader says "clickable" on them: the
// span around the top bar's status icons and quick settings button, and the div around every app's view (the empty
// "clickable" between Home and the tiles). role="none" quiets them; it is the layer's own and goes when they go.
test('The_clickable_wrappers_have_no_role_of_their_own', async () => {
  const window = await load('home');
  try {
    const doc = window.document;
    const status = doc.querySelector('span.status-icons-clickable');
    const router = doc.querySelector('div.router-container');
    for (const wrapper of [status, router]) {
      assert.strictEqual(wrapper.getAttribute('role'), 'none');
      assert.ok(['aria-label', 'tabindex', 'data-msfsba'].every(a => !wrapper.hasAttribute(a)), 'a role, nothing else');
    }
    assert.strictEqual(doc.querySelectorAll('[role=none]').length, 2, 'nothing else');
    // What they hold is as it was.
    assert.strictEqual(computeAccessibleName(doc.querySelector('button.top-bar-settings-btn')), 'Open quick settings');
    assert.deepStrictEqual([...doc.querySelectorAll('#appContainer > div.appIcon')].map(computeAccessibleName), TITLES);

    // The page takes the class off: the layer's role goes; it comes back with the class.
    status.setAttribute('class', 'status-icons');
    await turns();
    assert.strictEqual(status.hasAttribute('role'), false);
    status.setAttribute('class', 'status-icons-clickable');
    await turns();
    assert.strictEqual(status.getAttribute('role'), 'none');
  } finally {
    window.close();
  }
});

test('A_role_the_page_gave_a_clickable_wrapper_is_kept', async () => {
  const window = await load('home', { layer: false });
  try {
    const router = window.document.querySelector('div.router-container');
    router.setAttribute('role', 'main');
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(router.getAttribute('role'), 'main');
    assert.strictEqual(window.document.querySelector('span.status-icons-clickable').getAttribute('role'), 'none');
    // Withdrawn, the page's role stays.
    router.setAttribute('class', 'router');
    await turns();
    assert.strictEqual(router.getAttribute('role'), 'main');
  } finally {
    window.close();
  }
});

// A picture with no alt attribute that is not itself a control says nothing to a screen reader but "graphic" (or
// its file name): it is hidden, wherever it is. A picture with an alt (an empty one included: that is the page saying
// it is decorative) is the page's to describe, and one that is itself a control is the control.
test('A_picture_with_no_alt_is_hidden_and_the_control_holding_it_keeps_its_name', async () => {
  const window = await load('home');
  try {
    const doc = window.document;
    const tiles = [...doc.querySelectorAll('#appContainer > div.appIcon')];
    assert.ok(tiles.every(t => t.querySelector('img.app-img').getAttribute('aria-hidden') === 'true'));
    assert.deepStrictEqual(tiles.map(computeAccessibleName), TITLES, 'each tile keeps its name');

    const holder = doc.createElement('div');
    holder.className = 'pictures';
    holder.innerHTML =
      '<img class="no-alt" src="/assets/img/x.png">' +
      '<img class="empty-alt" src="/assets/img/y.png" alt="">' +
      '<img class="alt" src="/assets/img/z.png" alt="Logo">' +
      '<img class="own-role" src="/assets/img/w.png" role="button" tabindex="0">' +
      '<img class="page-shown" src="/assets/img/v.png" aria-hidden="false">';
    doc.body.appendChild(holder);
    await turns();
    const pic = cls => holder.querySelector('img.' + cls);
    assert.strictEqual(pic('no-alt').getAttribute('aria-hidden'), 'true');
    assert.ok(['empty-alt', 'alt', 'own-role'].every(c => !pic(c).hasAttribute('aria-hidden')), 'the page\'s to describe, or a control');
    assert.strictEqual(pic('page-shown').getAttribute('aria-hidden'), 'false', 'the page\'s own');
    assert.deepStrictEqual([...holder.querySelectorAll('img')].map(img => img.getAttribute('alt')), [null, '', 'Logo', null, null],
      'the layer writes no alt');

    // The page gives the picture an alt (the observer does not watch alt, so the pass is run by hand): the layer's
    // hidden state is taken back.
    pic('no-alt').setAttribute('alt', 'Now described');
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(pic('no-alt').hasAttribute('aria-hidden'), false);
  } finally {
    window.close();
  }
});

// Each rule is its own: one that throws never skips the others. A rule is made to fail by its own query of the
// whole page; every other rule still does its work.
const HOME_ISOLATION = [
  ['img#appBg', ['#appBg', '#linkedup', '.airplane-icon', '.signal-icon'].map(s => 'img' + s + '[aria-hidden=true]').join(', '), 4],
  ['img:not([alt])', 'img.app-img[aria-hidden=true]', 9],
  ['status-icons-clickable', '[role=none]', 2],
  ['i[class*=', 'i[aria-hidden=true]', 1]
];
for (const [failing] of HOME_ISOLATION) {
  test('A_failing_rule_does_not_skip_the_others_on_home_when_' + failing.replace(/[^A-Za-z0-9]+/g, '_') + '_fails', async () => {
    const window = await load('home', { layer: false });
    const proto = window.Element.prototype;
    const original = proto.querySelectorAll;
    try {
      proto.querySelectorAll = function (s) {
        if (this === window.document.body && s.includes(failing)) throw new Error('boom');
        return original.call(this, s);
      };
      window.eval(fs.readFileSync(LAYER, 'utf8'));
      await turns();
      proto.querySelectorAll = original;
      assert.strictEqual(window.document.getElementById('homeButtonFree').getAttribute('role'), 'button', 'an older rule ran');
      for (const [needle, selector, count] of HOME_ISOLATION) {
        const found = window.document.querySelectorAll(selector).length;
        assert.strictEqual(found, needle === failing ? 0 : count,
          needle === failing ? 'the failing rule did nothing' : 'the rule for ' + needle + ' still ran');
      }
    } finally {
      proto.querySelectorAll = original;
      window.close();
    }
  });
}
