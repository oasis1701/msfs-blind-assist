// HorizonSim 787-9 EFB agent — read/drive the Boeing EFB over the Coherent GT remote
// debugger (port 19999, HSB789_EFB view), REPLACING the HTTP-injection bridge
// (hs787-efb-bridge.js + EFBBridgeServer). Installed via CoherentHS787EfbClient;
// exposes window.__MSFSBA_HS787_EFB.
//
// ES5 ONLY (Coherent GT = old Chromium): var, no arrow funcs, no String.includes,
// top-level try/catch. The Boeing EFB DOM uses stable class hooks:
//   .boeing-efb-button / .boeing-efb-dropdown-button / .boeing-efb-textfield-button
//   .boeing-efb-button-disabled  (disabled state)
//   .button-name                 (the visible label inside a button)
//   [class*="efb-title"]         (the current page title)
(function () {
  try {
    var A = {};

    function txt(el) { return el ? (el.textContent || '').replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, '') : ''; }
    function hasCls(el, c) { return el && el.className && (' ' + el.className + ' ').indexOf(' ' + c + ' ') >= 0; }
    function clsContains(el, sub) { return el && typeof el.className === 'string' && el.className.indexOf(sub) >= 0; }
    function vis(el) {
      if (!el || !el.getBoundingClientRect) return false;
      var r = el.getBoundingClientRect();
      if (r.width < 2 || r.height < 2) return false;
      var s = el.ownerDocument.defaultView.getComputedStyle(el);
      return s.visibility !== 'hidden' && s.display !== 'none' && parseFloat(s.opacity || '1') > 0.05;
    }

    A.label = function (el) {
      // prefer the inner .button-name; else aria-label/title; else own text
      var nm = el.querySelector ? el.querySelector('.button-name, [class*="button-name"]') : null;
      var t = nm ? txt(nm) : '';
      if (!t) t = (el.getAttribute && (el.getAttribute('aria-label') || el.getAttribute('title'))) || '';
      if (!t) t = txt(el);
      return t.replace(/\s+/g, ' ').slice(0, 60);
    };

    A.kindOf = function (el) {
      if (clsContains(el, 'dropdown')) return 'dropdown';
      if (clsContains(el, 'textfield')) return 'input';
      return 'button';
    };

    // Returns the current page title + the visible interactive elements (each stamped with a
    // stable idx via a data attribute so click()/setValue() can resolve it after re-scrapes).
    // NOTE on navigation: the Boeing EFB's only way back to the menu from a sub-page is the
    // hardware "MENU" bezel key (a 3D-cockpit interaction the EFB receives internally — it is
    // NOT exposed as a DOM button, an L-var, or a bus/onInteractionEvent we can drive from the
    // debugger; verified that clicking the hidden page-switch buttons does not navigate out of a
    // modal sub-page like DOORS). So from the MAIN MENU every page is reachable by clicking its
    // tile; returning from a sub-page currently needs the cockpit MENU key. (TODO: find the
    // bezel-key mechanism — likely an airframe interaction.)
    A.scrape = function () {
      // Title: the EFB keeps a hidden title node per page in the DOM, so pick the VISIBLE one.
      var title = '';
      var titles = document.querySelectorAll('[class*="efb-title"]');
      for (var t = 0; t < titles.length; t++) { if (vis(titles[t]) && txt(titles[t])) { title = txt(titles[t]); break; } }

      var sel = '.boeing-efb-button, .boeing-efb-dropdown-button, .boeing-efb-textfield-button';
      var nodes = document.querySelectorAll(sel);
      var els = [], idx = 0;
      for (var i = 0; i < nodes.length; i++) {
        var el = nodes[i];
        if (!vis(el)) continue;
        var label = A.label(el);
        if (!label) continue;
        el.setAttribute('data-msfsba-efb-idx', idx);
        var value = '';
        if (clsContains(el, 'textfield')) {
          var inp = el.querySelector('input,textarea');
          value = inp ? (inp.value || '') : '';
        }
        els.push({
          idx: idx,
          kind: A.kindOf(el),
          label: label,
          value: value,
          disabled: clsContains(el, 'disabled')
        });
        idx++;
      }
      return { ok: true, title: title, elements: els };
    };

    A.byIdx = function (idx) { return document.querySelector('[data-msfsba-efb-idx="' + idx + '"]'); };

    A.clickElement = function (el) {
      if (!el) return false;
      var o = { bubbles: true, cancelable: true, view: window };
      // Dispatch the full mouse sequence (some Boeing-EFB buttons react to mousedown, not
      // click alone — same as the CDU page keys).
      try { el.dispatchEvent(new PointerEvent('pointerdown', o)); } catch (e) {}
      try { el.dispatchEvent(new MouseEvent('mousedown', o)); } catch (e) {}
      try { el.dispatchEvent(new PointerEvent('pointerup', o)); } catch (e) {}
      try { el.dispatchEvent(new MouseEvent('mouseup', o)); } catch (e) {}
      try { el.dispatchEvent(new MouseEvent('click', o)); } catch (e) {}
      return true;
    };

    A.click = function (idx) { return A.clickElement(A.byIdx(idx)); };

    A.setValue = function (idx, text) {
      var el = A.byIdx(idx); if (!el) return false;
      var inp = (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') ? el : el.querySelector('input,textarea');
      if (!inp) { A.clickElement(el); return false; }
      try {
        var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value');
        if (setter && setter.set) setter.set.call(inp, text); else inp.value = text;
      } catch (e) { inp.value = text; }
      try { inp.dispatchEvent(new Event('input', { bubbles: true })); } catch (e) {}
      try { inp.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, keyCode: 13 })); } catch (e) {}
      try { inp.blur(); } catch (e) {}
      return true;
    };

    A.ping = function () { return { ok: true }; };

    window.__MSFSBA_HS787_EFB = A;
    return 'MSFSBA_HS787_EFB_INSTALLED';
  } catch (e) {
    return 'MSFSBA_HS787_EFB_ERROR:' + (e && e.message);
  }
})()
