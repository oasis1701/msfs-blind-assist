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
      // prefer the inner .button-name (buttons/dropdowns carry it inside)
      var nm = el.querySelector ? el.querySelector('.button-name, [class*="button-name"]') : null;
      var t = nm ? txt(nm) : '';
      // textfields ONLY carry their label as a SIBLING .button-name in the parent
      // (.boeing-efb-top-textfield) — NOT inside the field — so a plain inner query missed it and
      // the inputs came out unlabeled. Fall back to the parent's .button-name for textfields.
      // (Buttons/dropdowns keep falling to their own textContent below, so a parent sibling never
      // steals their label.)
      if (!t && clsContains(el, 'textfield') && el.parentElement) {
        var pnm = el.parentElement.querySelector('.button-name, [class*="button-name"]');
        if (pnm && pnm !== el && !el.contains(pnm)) t = txt(pnm);
      }
      if (!t) t = (el.getAttribute && (el.getAttribute('aria-label') || el.getAttribute('title'))) || '';
      if (!t) t = txt(el);
      return t.replace(/\s+/g, ' ').slice(0, 60);
    };

    // Own (direct) text of an element — its label/value text, not its descendants'.
    A.ownText = function (el) {
      var t = '';
      for (var i = 0; i < el.childNodes.length; i++) { var n = el.childNodes[i]; if (n.nodeType === 3) t += n.nodeValue; }
      return t.replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, '');
    };

    A.kindOf = function (el) {
      // A choice inside an OPEN dropdown's option list (.dropdown-items) — read it as an option,
      // not as another setting dropdown, so an expanded dropdown reads "PICK ONE: a, b, c".
      if (el.closest && el.closest('[class*="dropdown-items"]')) return 'option';
      if (clsContains(el, 'dropdown')) return 'dropdown';
      if (clsContains(el, 'textfield')) return 'input';
      return 'button';
    };

    // --- Page navigation via the EFB instrument instance (the reliable path) ---
    // The WT EFB exposes a settable `visiblePage` Subject on its instrument instance, plus a
    // window.EfbPages enum {MainMenu:0, Performance:1, Doors:2, Video:3, dataLOAD:4}. Setting it
    // navigates to that page from ANY page (no bezel MENU key, no DOM click) — so we always offer
    // "Main Menu" + the functional pages as nav, which lets the user reach everything.
    A.efbInstance = function () {
      var all = document.querySelectorAll('*');
      for (var i = 0; i < all.length; i++) { if (all[i].fsInstrument) return all[i].fsInstrument; }
      return null;
    };
    A.currentPageId = function () {
      try { var inst = A.efbInstance(); var vp = inst && inst.visiblePage; if (!vp) return -1;
            return vp.get ? vp.get() : vp.value; } catch (e) { return -1; }
    };
    A.gotoPage = function (id) {
      try { var inst = A.efbInstance(); if (inst && inst.visiblePage) { inst.visiblePage.set(id); return true; } }
      catch (e) {}
      return false;
    };
    // Ordered nav list from the EfbPages enum (numeric keys -> friendly labels).
    A.navPages = function () {
      var labels = { 0: 'Main Menu', 1: 'Performance', 2: 'Doors', 3: 'Video', 4: 'Data Load' };
      var E = window.EfbPages || {}, out = [];
      for (var k in E) { var n = E[k]; if (typeof n === 'number' && out.indexOf(n) < 0) out.push(n); }
      out.sort(function (a, b) { return a - b; });
      return out.map(function (n) { return { id: n, label: labels[n] || ('Page ' + n) }; });
    };

    // True for the always-present main-menu page tiles (they live in an efb-main-menu* container
    // and stay hidden in the DOM on sub-pages); we surface those as the kind 'nav' page-switch
    // list instead, so they're skipped from the page-content + static-text scrape.
    A.isMenuNav = function (el) { return !!(el.closest && el.closest('[class*="efb-main-menu"]')); };

    // Returns the current page title + the interactive elements (each stamped with a stable idx).
    // When NOT on the main menu, the functional EFB pages (Main Menu / Performance / Doors / Video /
    // Data Load) are prepended as kind 'nav' so the user can jump to any of them — including BACK
    // to the menu — from any sub-page; nav clicks set the instrument's visiblePage Subject directly
    // (the reliable path — the hardware MENU bezel key is a 3D-cockpit interaction not drivable from
    // the DOM). On the main menu the tiles themselves are the navigation, so nav isn't duplicated.
    A.scrape = function () {
      // Title: the EFB keeps a hidden title node per page in the DOM, so pick the VISIBLE one.
      var title = '';
      var titles = document.querySelectorAll('[class*="efb-title"]');
      for (var t = 0; t < titles.length; t++) { if (vis(titles[t]) && txt(titles[t])) { title = txt(titles[t]); break; } }

      var els = [], idx = 0;
      A._navMap = {};   // idx -> page id, rebuilt each scrape

      var curPage = A.currentPageId();
      var onMainMenu = (curPage === 0) || title === 'MAIN MENU';
      if (!onMainMenu) {
        var pages = A.navPages();
        for (var p = 0; p < pages.length; p++) {
          if (pages[p].id === curPage) continue;   // don't list the page we're already on
          A._navMap[idx] = pages[p].id;
          els.push({ idx: idx, kind: 'nav', label: pages[p].label, value: '', disabled: false });
          idx++;
        }
      }

      // Build the page in READING ORDER: every interactive control (button / dropdown / labelled
      // input) PLUS standalone static text (read-only info + field values), so the page is fully
      // readable, not just operable. Items are positioned and sorted top-to-bottom, left-to-right.
      var sel = '.boeing-efb-button, .boeing-efb-dropdown-button, .boeing-efb-textfield-button';
      var items = [];

      var ctrlNodes = document.querySelectorAll(sel);
      for (var i = 0; i < ctrlNodes.length; i++) {
        var el = ctrlNodes[i];
        if (!vis(el)) continue;
        if (!onMainMenu && A.isMenuNav(el)) continue;
        var label = A.label(el);
        if (!label) continue;
        var value = '';
        if (clsContains(el, 'textfield')) {
          var inp = el.querySelector('input,textarea');
          value = inp ? (inp.value || '') : '';
        }
        var rc = el.getBoundingClientRect();
        items.push({ ctrl: true, el: el, kind: A.kindOf(el), label: label, value: value,
                     disabled: clsContains(el, 'disabled'), y: rc.top, x: rc.left });
      }

      // Standalone readable text — visible own-text leaves that are NOT inside a control, NOT a
      // control's .button-name label (already shown on the control), NOT the title, NOT the menu.
      var allv = document.querySelectorAll('body *');
      for (var j = 0; j < allv.length; j++) {
        var e = allv[j];
        if (!vis(e)) continue;
        if (e.closest(sel)) continue;
        if (clsContains(e, 'button-name') || (e.closest && e.closest('.button-name'))) continue;
        if (e.closest('[class*="efb-title"]')) continue;
        if (!onMainMenu && A.isMenuNav(e)) continue;
        var ot = A.ownText(e);
        if (!ot || ot.length > 45) continue;
        if (/^[\s:.,\-/]+$/.test(ot)) continue;   // pure punctuation / separators
        var rt = e.getBoundingClientRect();
        items.push({ ctrl: false, text: ot, y: rt.top, x: rt.left });
      }

      items.sort(function (a, b) { return (a.y - b.y) || (a.x - b.x); });

      var seenText = {};
      for (var k = 0; k < items.length; k++) {
        var it = items[k];
        if (it.ctrl) {
          it.el.setAttribute('data-msfsba-efb-idx', idx);
          els.push({ idx: idx, kind: it.kind, label: it.label, value: it.value, disabled: it.disabled });
          idx++;
        } else {
          var tkey = Math.round(it.y) + '|' + it.text;
          if (seenText[tkey]) continue;
          seenText[tkey] = 1;
          els.push({ idx: idx, kind: 'text', label: it.text, value: '', disabled: false });
          idx++;
        }
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

    A.click = function (idx) {
      // A 'nav' element (page-switch) is not a DOM node — drive the instrument's visiblePage.
      if (A._navMap && A._navMap.hasOwnProperty(idx)) return A.gotoPage(A._navMap[idx]);
      return A.clickElement(A.byIdx(idx));
    };

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
