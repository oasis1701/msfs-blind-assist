// Synaptic A220 DisplayUnits agent — reads the PFD FMA/ASA band, the speed-tape
// V-speed band, and the EICAS CAS column from the ONE React/SVG view that hosts all
// five DUs ("VCockpit02 - DisplayUnits"). Installed via Runtime.evaluate by
// A220DisplaysClient; polled with __a220Displays.snapshot().
//
// The view pre-renders every text node and toggles visibility, so each candidate is
// filtered on computedStyle visibility and POSITION-filtered after transforming its
// bbox into the MOUNT's own coordinate space (m = mount.getCTM()^-1 x el.getCTM()) —
// tape-scroll texts carry huge off-clip coordinates that only the transform reveals.
// Band constants were measured live 2026-07-28 (recon + this agent's probe): FMA
// x[415,1105] y[-25,145] of PFD_1_MOUNT; CAS x[385,760] y[-15,440] of EICAS_2_MOUNT
// (the recon's canvas-space x[1115,1480] minus the mount's 722px placement shift);
// CAS y<=440 excludes the gear/flap block below the message column.
//
// ES5 ONLY (Coherent GT = old Chromium): var, no arrow funcs, top-level try/catch.
(function () {
  try {
    var A = {};

    // fill/class -> color word. Class tokens first (Lime/Magenta/Amber/White...),
    // then the computed fill rgb (many nodes carry no color class at all).
    function colorOf(el) {
      var k = ' ' + (el.getAttribute('class') || '') + ' ';
      if (k.indexOf(' Lime ') >= 0 || k.indexOf(' Green ') >= 0) return 'green';
      if (k.indexOf(' Magenta ') >= 0) return 'magenta';
      if (k.indexOf(' Amber ') >= 0 || k.indexOf(' Yellow ') >= 0) return 'amber';
      if (k.indexOf(' Red ') >= 0) return 'red';
      if (k.indexOf(' Cyan ') >= 0) return 'cyan';
      if (k.indexOf(' Gray ') >= 0 || k.indexOf(' Grey ') >= 0) return 'gray';
      if (k.indexOf(' White ') >= 0) return 'white';
      var fill = '';
      try { fill = window.getComputedStyle(el).fill || ''; } catch (e) { }
      var m = /rgb\((\d+),\s*(\d+),\s*(\d+)\)/.exec(fill);
      if (!m) return 'white';
      var r = +m[1], g = +m[2], b = +m[3];
      if (g > 150 && r < 110 && b < 110) return 'green';
      if (r > 180 && b > 180 && g < 130) return 'magenta';
      if (r > 180 && g > 140 && b < 110) return 'amber';
      if (r > 180 && g < 110 && b < 110) return 'red';
      if (g > 150 && b > 150 && r < 130) return 'cyan';
      if (r < 160 && g < 160 && b < 160) return 'gray';
      return 'white';
    }

    // Visible text nodes inside `mount` whose transformed origin falls in the band.
    function collect(mount, xMin, xMax, yMin, yMax) {
      var out = [];
      var inv;
      try { inv = mount.getCTM().inverse(); } catch (e) { return out; }
      var nodes = mount.querySelectorAll('text');
      for (var i = 0; i < nodes.length; i++) {
        var el = nodes[i];
        var t = (el.textContent || '').replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, '');
        if (!t) continue;
        var vis;
        try { vis = window.getComputedStyle(el).visibility; } catch (e2) { continue; }
        if (vis !== 'visible') continue;
        var m, bb;
        try { m = inv.multiply(el.getCTM()); bb = el.getBBox(); } catch (e3) { continue; }
        var x = m.a * bb.x + m.c * bb.y + m.e;
        var y = m.b * bb.x + m.d * bb.y + m.f;
        if (x < xMin || x > xMax || y < yMin || y > yMax) continue;
        out.push({ el: el, t: t, x: Math.round(x), y: Math.round(y) });
      }
      return out;
    }

    // ---- AFDX CommBus tap ------------------------------------------------
    // The FG/EFCS/FCP TRUTHS are not SimVars and not L:vars: the aircraft's WASM
    // broadcasts them as JSON over the MSFS CommBus, and the DUs subscribe with
    // ii("A22X.<store>", rate, defaults) (instrument.js, src/avionics/lib/afdx/*).
    // We register a SECOND JS_LISTENER_COMM_BUS subscriber for the three stores we
    // need — the same call the aircraft's own bus wrapper makes in this very page,
    // so the pattern is proven here. Nothing is written; this is read-only.
    //
    // Why it matters (each of these has NO other readable source):
    //   Autoflight    l_fd/r_fd    real FD on/off. The "A22X L/R Flight Director"
    //                              L:var is bound to A220_ButtonMomentary in
    //                              Interior/Glareshield/Autopilot.xml — it is a
    //                              PRESS pulse, so it reads 0 whatever the FD does.
    //                 cmd_*        the FD command bars (bank / FPA / pitch target).
    //   FCP           *_sel        the selected SPD/HDG/ALT/VS at 30 Hz — exact, and
    //                              the FCP walk's read-back (the 1 Hz SimConnect
    //                              batch is what made the walk slow and imprecise).
    //   Flight Control pitch_trim  stab trim UNITS + the takeoff green band, and
    //                              rudder_trim. Neither exists as a SimVar.
    //   L Nav Data    source/course the CAPTAIN's HSI is actually flying (FMS1/FMS2/
    //                 VOR1/VOR2/LOC1/LOC2 — src/avionics/lib/afdx/nav.ts), its tuned
    //                 frequency, CDI and marker.
    //   L CTP Data    the captain CTP's own course setting and nav-source selection.
    //   Radio Data    vhf.nav_1 / nav_2 (the CNS NAV CONTROL windows).
    var CB_STORES = [
      ['A22X.Autoflight Data', 'af'],
      ['A22X.FCP Data', 'fcp'],
      ['A22X.Flight Control Data', 'fc'],
      ['A22X.L Nav Data', 'lnav'],
      ['A22X.L CTP Data', 'lctp'],
      ['A22X.Radio Data', 'radio']
    ];
    // Survives a re-install: the agent is re-evaluated whenever the page reloads or the
    // socket reconnects, and registering a fresh view listener each time would leak one
    // per reconnect (the aircraft's own wrapper has an unregister(); we simply keep ours).
    // VERSIONED: a listener's message handlers are closures of the agent that
    // registered them, so a behaviour change in the handler (the deep merge, v2)
    // needs a FRESH listener and data object — the old one keeps writing into its
    // own, now-orphaned data. One listener leaks per upgrade, never per reconnect.
    var CB_VERSION = 2;
    var cbState = window.__a220CommBus;
    if (!cbState || cbState.v !== CB_VERSION) {
      cbState = { v: CB_VERSION, data: {}, stamp: {}, listener: null, ready: false, subs: 0, tries: 0, lastTry: 0, err: '' };
      window.__a220CommBus = cbState;
    }
    var cbData = cbState.data, cbStamp = cbState.stamp;

    function cbEverDelivered() {
      for (var k in cbStamp) { if (cbStamp[k]) return true; }
      return false;
    }

    function deepMerge(cur, upd) {
      for (var k in upd) {
        if (!Object.prototype.hasOwnProperty.call(upd, k)) continue;
        var v = upd[k];
        if (v && typeof v === 'object' && !(v instanceof Array) &&
            cur[k] && typeof cur[k] === 'object' && !(cur[k] instanceof Array)) {
          cur[k] = deepMerge(cur[k], v);
        } else {
          cur[k] = v;
        }
      }
      return cur;
    }

    // The stores only broadcast CHANGES; the aircraft's own wrapper asks for a full
    // copy with "A22X.Resync" (src/avionics/lib/afdx/index.ts). A store we have never
    // seen whole (the radios, first opened long after the page loaded) arrives as {}
    // until something asks — so ask, at most every 3 s.
    function cbResync() {
      var now = Date.now();
      if (!cbState.listener || (now - (cbState.lastResync || 0)) < 3000) return;
      cbState.lastResync = now;
      try { cbState.listener.call('COMM_BUS_WASM_CALLBACK', 'A22X.Resync', '{}'); } catch (e) { }
    }

    function cbSubscribe() {
      if (!cbState.listener) return;
      // Per-store latch: the listener outlives an agent re-install (the page keeps it
      // in window.__a220CommBus), so a store added in a newer agent must still get
      // subscribed on an old listener — and an existing one never twice.
      if (!cbState.subscribed) cbState.subscribed = {};
      for (var i = 0; i < CB_STORES.length; i++) {
        if (cbState.subscribed[CB_STORES[i][0]]) continue;
        cbState.subscribed[CB_STORES[i][0]] = true;
        (function (name) {
          try {
            cbState.listener.on(name, function (json) {
              // MERGE, never replace. These stores broadcast PARTIAL updates — the
              // aircraft's own wrapper (src/avionics/lib/afdx/index.ts) keeps itself
              // whole by calling "A22X.Resync" whenever a message changes something
              // and again 3 s after the last one, so a single broadcast is not a full
              // snapshot. Replacing the cached object therefore ERASED every field the
              // latest message happened not to carry: live 2026-09-22 the FCP block
              // arrived with `alt_sel_ft` absent, so the altitude readout announced
              // "Altitude not set — the selector shows dashes" with FL280 selected,
              // and the Flight Control block arrived without `pitch_trim`, which is
              // what made the stabilizer-trim walk refuse as "display link not
              // connected" while the link was up the whole time.
              // A genuine null still lands: JSON carries it as a key, so it merges.
              // NESTED too: Radio Data's vhf.nav_1 arrives as {"autotune":true} alone
              // after a tuning change (live 2026-09-24) — a shallow merge replaced the
              // whole vhf object and lost every frequency.
              try {
                cbData[name] = deepMerge(cbData[name] || {}, JSON.parse(json));
                cbStamp[name] = Date.now();
              } catch (e2) { }
            });
            cbState.subs++;
          } catch (e3) { cbState.err = 'on:' + (e3 && e3.message); }
        })(CB_STORES[i][0]);
      }
    }

    // RegisterViewListener is ASYNCHRONOUS: it takes a callback fired once the listener is
    // actually connected, and a subscription made before that can be dropped on the floor.
    // The aircraft's own wrapper gets away with subscribing immediately only because its
    // listener is created at page load and its .on() calls happen much later, during
    // component construction. Ours is created and used in the same tick, so it must
    // subscribe from the ready callback (we also subscribe immediately — harmless if that
    // path works, and it covers a build where the callback never fires). Everything is
    // retried while NOTHING has ever arrived, because a silently-dead tap is exactly the
    // failure that cost a whole session of guessing (2026-07-31).
    function ensureCommBus() {
      if (cbState.listener && (cbState.ready || cbEverDelivered())) { cbSubscribe(); return true; }
      var now = Date.now();
      if (cbState.listener && (cbState.tries >= 6 || (now - cbState.lastTry) < 5000)) return true;
      cbState.tries++;
      cbState.lastTry = now;
      try {
        cbState.subscribed = {};   // a NEW listener has no subscriptions yet
        cbState.listener = RegisterViewListener('JS_LISTENER_COMM_BUS', function () {
          cbState.ready = true;
          cbState.subscribed = {};  // (re)subscribe once connected — see above
          cbSubscribe();
        });
      } catch (e) { cbState.listener = null; cbState.err = 'register:' + (e && e.message); }
      if (!cbState.listener) return false;
      cbSubscribe();
      return true;
    }

    /// Why the tap is (or isn't) delivering — so a dead link is diagnosable from one
    /// spoken readout instead of another round of deploy-and-guess.
    function cbDiag() {
      var newest = 0;
      for (var k in cbStamp) { if (cbStamp[k] > newest) newest = cbStamp[k]; }
      return {
        listener: !!cbState.listener,
        ready: !!cbState.ready,
        subs: cbState.subs,
        tries: cbState.tries,
        ever: cbEverDelivered(),
        ageMs: newest ? (Date.now() - newest) : -1,
        err: cbState.err || ''
      };
    }

    // Pick only the fields C# consumes — these stores carry 10-60 fields at 20-30 Hz
    // and shipping them whole would bloat every 1 s poll.
    var CB_FIELDS = {
      af: ['l_fd', 'r_fd', 'lateral', 'lateral_arm', 'vertical', 'vertical_arm_vert',
           'at_mode', 'cmd_lateral', 'cmd_vertical_fpa', 'cmd_vertical_pitch',
           'ap_master', 'at_master', 'approach_status'],
      fcp: ['spd_sel_ias', 'spd_sel_mach', 'spd_in_mach', 'spd_fms', 'hdg_sel',
            'alt_sel_ft', 'alt_sel_m', 'alt_in_m', 'vs_sel', 'vs_mode'],
      fc: ['pitch_trim', 'pitch_trim_up', 'pitch_trim_dn', 'rudder_trim'],
      lnav: ['source', 'frequency', 'course', 'cdi', 'to', 'bc', 'marker',
             'preview_source', 'preview_course', 'preview_frequency'],
      lctp: ['nav_source', 'course', 'crosstune'],
      radio: ['vhf', 'display_tune_inhib', 'l_ctp_inhib']
    };

    // A store the WASM stopped broadcasting is STALE, not current: the aircraft's own
    // stores self-reset to defaults after 3 s of silence, so we mirror that window and
    // return null rather than a frozen value C# would read as truth.
    function cbBlock(key, name) {
      var raw = cbData[name];
      if (!raw || !cbStamp[name] || (Date.now() - cbStamp[name]) > 3000) return null;
      var want = CB_FIELDS[key], out = {};
      for (var i = 0; i < want.length; i++) {
        var f = want[i];
        out[f] = (raw[f] === undefined) ? null : raw[f];
      }
      return out;
    }

    /// Fresh AFDX blocks alone — used by the FCP knob walk, which needs a read-back
    /// far faster than the 1 s display poll.
    A.afdx = function () {
      ensureCommBus();
      var out = { ok: !!cbState.listener, diag: cbDiag() };
      for (var i = 0; i < CB_STORES.length; i++) out[CB_STORES[i][1]] = cbBlock(CB_STORES[i][1], CB_STORES[i][0]);
      return JSON.stringify(out);
    };

    /// Captain nav/radio truth for the A220 Radios window (read-only).
    A.radios = function () {
      ensureCommBus();
      var rd = cbData['A22X.Radio Data'], nd = cbData['A22X.L Nav Data'], cd = cbData['A22X.L CTP Data'];
      if (!rd || !rd.vhf || !nd || !('source' in nd) || !cd || !('course' in cd)) cbResync();
      return JSON.stringify({
        ok: !!cbState.listener,
        lnav: cbBlock('lnav', 'A22X.L Nav Data'),
        lctp: cbBlock('lctp', 'A22X.L CTP Data'),
        radio: cbBlock('radio', 'A22X.Radio Data'),
        diag: cbDiag()
      });
    };

    // WRITE side — exactly the calls the aircraft's own displays make
    // (src/avionics/lib/afdx/ctp.ts + radio.ts): the bus wrapper's call() is
    // viewListener.call("COMM_BUS_WASM_CALLBACK", event, JSON.stringify(payload)).
    //   "A22X.L CTP Action" {type:"SetConfig", course:n}      — the CTP CRS field
    //   "A22X.Display Tune" {index:3|4, cmd:{type:"Set", active:false, frequency:Hz}}
    //                       {index:3|4, cmd:{type:"ConfigNav", autotune:bool}}
    // NAV index 3 = NAV1, 4 = NAV2. Only PRESET (active:false) frequencies are ever
    // sent from MSFSBA — see the NAV-to-NAV transfer invariant in docs/a220.md.
    function cbCall(event, payload) {
      if (!ensureCommBus() || !cbState.listener) return 'NO_BUS';
      try {
        cbState.listener.call('COMM_BUS_WASM_CALLBACK', event, JSON.stringify(payload));
        return 'SENT';
      } catch (e) { return 'ERR:' + (e && e.message); }
    }
    A.setCourse = function (side, course) {
      return cbCall('A22X.' + (side === 2 ? 'R' : 'L') + ' CTP Action', { type: 'SetConfig', course: course });
    };
    A.setNavPreset = function (index, hz) {
      if (index !== 3 && index !== 4) return 'BAD_INDEX';
      return cbCall('A22X.Display Tune', { index: index, cmd: { type: 'Set', active: false, frequency: hz } });
    };
    A.setNavAutotune = function (index, auto) {
      if (index !== 3 && index !== 4) return 'BAD_INDEX';
      return cbCall('A22X.Display Tune', { index: index, cmd: { type: 'ConfigNav', autotune: !!auto } });
    };

    ensureCommBus();   // start filling immediately; the first poll is ~1 s away

    A.snapshot = function () {
      ensureCommBus();
      var pfd = document.querySelector('#PFD_1_MOUNT') || document.querySelector('#PFD_4_MOUNT');
      var eicas = document.querySelector('#EICAS_2_MOUNT');
      if (!pfd || !eicas) return JSON.stringify({ ok: false });

      // FMA/ASA band. Bare integers are the attitude-ladder pitch marks drifting
      // through the band (no FMA/ASA token is ever a bare number) and dash/dot
      // runs are empty-field placeholders — both dropped.
      var fma = [];
      var raw = collect(pfd, 415, 1105, -25, 145);
      for (var i = 0; i < raw.length; i++) {
        if (/^\d+$/.test(raw[i].t) || /^[-–.]+$/.test(raw[i].t)) continue;
        fma.push({ t: raw[i].t, x: raw[i].x, c: colorOf(raw[i].el) });
      }
      fma.sort(function (a, b) { return a.x - b.x; });

      // Speed-tape band: V-speed bug labels/values. Keep V-tokens and 2-3 digit
      // numbers (raw texts + y — C# pairs a bare label with its same-row number).
      // No y clamp: tape-riding bugs scroll far outside the clip.
      var spd = [];
      raw = collect(pfd, 330, 415, -1e9, 1e9);
      for (var j = 0; j < raw.length; j++) {
        var t = raw[j].t;
        if (!/^V(1|R|2|REF|APP)/.test(t) && !/^\d{2,3}$/.test(t)) continue;
        spd.push({ t: t, y: raw[j].y });
      }

      // CAS/memo column, top-down priority order; color = severity.
      var cas = [];
      raw = collect(eicas, 385, 760, -15, 440);
      raw.sort(function (a, b) { return a.y - b.y; });
      for (var n = 0; n < raw.length; n++) {
        if (/full ?screen|press and hold/i.test(raw[n].t)) continue; // OS overlay bleed
        cas.push({ t: raw[n].t, c: colorOf(raw[n].el) });
      }

      return JSON.stringify({
        ok: true, fma: fma, spd: spd, cas: cas,
        af: cbBlock('af', 'A22X.Autoflight Data'),
        fcp: cbBlock('fcp', 'A22X.FCP Data'),
        fc: cbBlock('fc', 'A22X.Flight Control Data'),
        diag: cbDiag()
      });
    };

    // ---- P3c/P4: FMS + ECL (CHKL) window scrape & drive ------------------
    // The FMS and checklist windows render as anonymous top-level <g> children of
    // the DU <svg>s (no ids, no classes — recon 2026-07-29). They are located by
    // CONTENT: the FMS window pre-renders its tile row (DBASE/POS/FPLN/PERF/ROUTE)
    // and an FMS1/FMS2 header; the CHKL window its SUMMARY/NON-NORMAL/FCTN tiles.
    // Input protocol (proven live): MKP keys are H:A220_KBD_* events fired by the
    // C# side; field COMMIT is a synthetic pointer/mouse click sequence on the
    // field's value node (the CCP cursor is a real mouse in this view).
    function winOf(kind) {
      var root = document.getElementById('MSFS_REACT_MOUNT');
      if (!root) return null;
      var gs = root.querySelectorAll('svg > g');
      for (var i = 0; i < gs.length; i++) {
        var tc = gs[i].textContent || '';
        if (kind === 'fms') {
          if (tc.indexOf('DBASE') >= 0 && tc.indexOf('FPLN') >= 0 && tc.indexOf('PERF') >= 0
            && (tc.indexOf('FMS1') >= 0 || tc.indexOf('FMS2') >= 0)) return gs[i];
        } else if (kind === 'ecl') {
          if (tc.indexOf('SUMMARY') >= 0 && tc.indexOf('NON-NORMAL') >= 0 && tc.indexOf('FCTN') >= 0) return gs[i];
        }
      }
      return null;
    }

    function norm(s) { return (s || '').replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, ''); }

    // ---- CLOSED-dropdown detection (structural, via React fibers) -----------
    // A Fusion dropdown renders ONLY its current value while closed — no text
    // distinguishes it from a plain data row, so it used to read as an ordinary
    // value and a blind pilot had no way to know a chooser was there (the last
    // known FMS-form gap). Its component, though, always carries an `options`
    // array plus an `onSelect` handler, exactly like the legs rows carry legIdx.
    // The fiber KEY is stable for the whole render root, so it is resolved once
    // and reused — this runs per token on a 1.3 s poll.
    var FIBER_KEY = null;
    function fiberKeyOf(el) {
      if (FIBER_KEY && el[FIBER_KEY] !== undefined) return FIBER_KEY;
      for (var k in el) {
        if (k.indexOf('__reactFiber$') === 0) { FIBER_KEY = k; return k; }
      }
      return null;
    }
    // ONE fiber walk per token, answering BOTH structural questions:
    //   dd         — {n, cur} when the text is the face of a closed dropdown
    //                (component carries an options array + onSelect);
    //   clickDepth — how many fiber hops up the FIRST onClick handler sits, -1
    //                when none within 10. Depth is the discriminator (measured
    //                live 2026-08-10 on the DBASE + POS pages): a REAL pressable
    //                (page tile, sub-tab, soft key, LOAD/IRS/GNSS buttons) finds
    //                its own component's onClick at depth 1 (the ACT dropdown at
    //                3), while data cells and labels only reach the page-level
    //                click catcher at depth 4+. A bare "has an onClick ancestor"
    //                test is useless — EVERY token has one.
    //   input      — the Fusion scratchpad Input this text renders (props with
    //                onSubmit + format + value), for the pending-entry repair
    //                in visTokens;
    //   mn / mx    — the entry's numeric range (the Input's wrapper carries
    //                min/max: ZFW 81750..128000 lb on the FUEL page), so a
    //                refusal can say what WOULD be accepted.
    function fiberInfoOf(el) {
      var key = fiberKeyOf(el);
      if (!key) return null;
      var f = el[key], n = 0, dd = null, clickDepth = -1, input = null, mn = null, mx = null;
      var inputSeen = false, ro = false;
      while (f && n < 10) {
        var p = f.memoizedProps;
        if (p) {
          // The first Input-shaped component (format + value) decides: with an
          // onSubmit it takes entries; WITHOUT one it is a framed READ-OUT that
          // merely looks like a box (the FUEL page's RESERVE weight, computed
          // from the contingency %, bundle `controlled: !0` and no onSubmit).
          if (!inputSeen && typeof p.format === 'function' && ('value' in p)) {
            inputSeen = true;
            if (typeof p.onSubmit === 'function') input = p; else ro = true;
          }
          if (mn === null && typeof p.min === 'number' && typeof p.max === 'number'
              && typeof p.onSubmit === 'function') { mn = p.min; mx = p.max; }
          if (clickDepth < 0 && typeof p.onClick === 'function') clickDepth = n;
          if (!dd && p.options && typeof p.options.length === 'number'
              && p.options.length > 0 && typeof p.onSelect === 'function') {
            var cur = '';
            for (var i = 0; i < p.options.length; i++) {
              var o = p.options[i];
              if (o && o.value === p.value && o.label !== undefined) { cur = '' + o.label; break; }
            }
            dd = { n: p.options.length, cur: cur };
          }
        }
        f = f.return; n++;
      }
      return { dd: dd, clickDepth: clickDepth, input: input, mn: mn, mx: mx, ro: ro };
    }

    // A Fusion Input that has been SUBMITTED and refused stays in its "pending"
    // state (the bundle's `k` flag is set before the submit and only cleared on
    // success), and while pending it renders the SHARED SCRATCHPAD in cyan
    // instead of its own value. After a refusal MSFSBA empties the scratchpad,
    // so the box renders an EMPTY text node — no token, no field, and the row
    // vanished from the form (the "fields disappear" FUEL-page report,
    // reproduced live 2026-09-24: ZFW gone after one INVALID ENTRY). What the
    // box really holds is its own value, so read that: the same format() call
    // the component makes, with its ▯-for-required-and-empty rule. Uncontrolled
    // inputs (no `value` prop) keep their state in a hook we cannot read, so an
    // empty one falls back to a bare blank slot rather than disappearing.
    var PENDING_CYAN = 'rgb(0, 191, 230)';
    // While refused the Input draws AMBER for the ~3 s its error box is up, then
    // cyan until the next successful entry — both are the scratchpad echo.
    function isPendingFill(fill) {
      var f = fill || '';
      return f.indexOf(PENDING_CYAN) >= 0 || f.indexOf('rgb(255, 227, 0)') >= 0;
    }
    // Cyan text on an Input is the pending echo ONLY when cyan is not the
    // Input's own colour: the origin runway ("RW25") is drawn cyan by design
    // (textColor #00bfe6), and reading it as a refused entry turned it white
    // and cost the legs row its "origin" tag (caught 2026-09-24).
    function isPendingEcho(p, fill) {
      if (!p) return false;
      var tc = ('' + (p.textColor || '')).toLowerCase();
      if ((fill || '').indexOf(PENDING_CYAN) >= 0)
        return tc !== '#00bfe6' && tc.indexOf('0, 191, 230') < 0;
      return tc !== '#ffe300' && tc.indexOf('255, 227, 0') < 0;   // amber
    }
    function inputDisplayValue(p) {
      if (!p || p.value === undefined) return '';
      try {
        var v = p.value != null ? p.value : undefined;
        var s = p.format(v);
        if (typeof s !== 'string') s = s == null ? '' : '' + s;
        if (p.required && v === undefined) s = s.replace(/-/g, '▯');
        return norm(s);
      } catch (e) { return ''; }
    }
    // {n: option count, cur: current label} when this text sits inside a closed
    // dropdown, else null.
    function dropdownInfoOf(el) {
      var fi = fiberInfoOf(el);
      return fi ? fi.dd : null;
    }
    // A token is genuinely PRESSABLE when its own component (or a close wrapper)
    // carries the onClick — see fiberInfoOf. Threshold 3 admits the dropdown faces
    // and every real key, and excludes everything that merely inherits the page's
    // click catcher.
    var CLICK_DEPTH_MAX = 3;

    // A LEGS constraint ("↑210/7000") mixes two things in one <text>: the pilot's
    // or procedure's CONSTRAINT in large type (tspan ContentText) and the FMS's
    // PREDICTION in small type (tspan LabelText) — on the real display the size
    // difference is the only thing telling them apart (live 2026-09-24: "↑150/9000"
    // is a predicted 150 kt over a 9000 ft constraint; "↑210/7000" a 210 kt
    // constraint over a predicted 7000). Flattened, a prediction read as a
    // restriction. Wrap each small-type part in ~…~ for FormatConstraint.
    function markPredicted(el) {
      var sp = el.getElementsByTagName ? el.getElementsByTagName('tspan') : null;
      if (!sp || !sp.length) return '';
      var own = ' ' + (el.getAttribute('class') || '') + ' ';
      if (own.indexOf(' LabelText ') >= 0) return '';          // the whole text is small
      var out = '', any = false;
      for (var i = 0; i < sp.length; i++) {
        var tx = norm(sp[i].textContent);
        if (!tx) continue;
        var cls = ' ' + (sp[i].getAttribute('class') || '') + ' ';
        if (cls.indexOf(' LabelText ') >= 0 && /[0-9]/.test(tx)) { out += '~' + tx + '~'; any = true; }
        else out += tx;
      }
      return any ? out : '';
    }

    // Visible text tokens, window-relative coordinates (bounding rects share one
    // canvas space, so rect deltas are stable regardless of DU placement).
    function visTokens(win) {
      var wr = win.getBoundingClientRect();
      var out = [];
      var ts = win.querySelectorAll('text');
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        var t = norm(el.textContent);
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        // An EMPTY node is normally nothing — except a pending Input's (see
        // inputDisplayValue), which is a real field showing an empty scratchpad.
        var pendingInput = isPendingFill(cs.fill);
        if (!t && !pendingInput) continue;
        // visibility AND opacity: a stale/faded node a sighted pilot cannot see
        // must be equally invisible to the scrape, or the form shows phantom
        // rows that misalign every occurrence-addressed click under them.
        if (cs.visibility !== 'visible') continue;
        if (parseFloat(cs.opacity || '1') === 0) continue;
        var r = el.getBoundingClientRect();
        var tok = { t: t, x: Math.round(r.left - wr.left), y: Math.round(r.top - wr.top), c: colorOf(el) };
        if (tok.c === 'green' && t) tok.t = markPredicted(el) || t;
        // Only VALUES can be a dropdown's face; gray label text never is, so the
        // fiber walk is skipped for labels (keeps the per-poll cost down). The
        // same single walk yields `cl` (genuinely pressable — its own component
        // owns an onClick), which is what stops table cells and dates reading as
        // "…, button" in the form.
        if (tok.c !== 'gray') {
          var fi = fiberInfoOf(el);
          if (fi) {
            if (fi.dd) { tok.dd = 1; tok.n = fi.dd.n; }
            if (fi.clickDepth >= 0 && fi.clickDepth <= CLICK_DEPTH_MAX) tok.cl = 1;
            if (fi.mn !== null) { tok.mn = fi.mn; tok.mx = fi.mx; }
            if (fi.ro) tok.ro = 1;
            if (pendingInput && isPendingEcho(fi.input, cs.fill)) {
              var real = inputDisplayValue(fi.input);
              // An EMPTY text node reports its box's origin, not where its text
              // would sit (x+7, y+5 units — every filled value in the same box
              // measures there); report the text position so it pairs exactly
              // like the filled value it stands in for.
              if (!t) {
                var sc = wr.width / 740 || 1;
                tok.x += Math.round(7 * sc); tok.y += Math.round(5 * sc);
              }
              tok.t = real || '▯';
              tok.c = 'white';
            }
          }
        }
        if (!tok.t) continue;                             // empty and not an Input
        out.push(tok);
      }
      return out;
    }

    // el = the element we MEANT to click (optional). document.elementFromPoint is
    // the proven path for on-screen nodes, but it returns null for a row scrolled
    // out of its clip region — which is exactly the bottom of every long list
    // (STARs, approaches, flight-plan legs). Those rows are still live React nodes
    // with their handlers attached, and React listens at the document root, so
    // dispatching straight at the element bubbles to the same handler and the
    // aircraft accepts it (PROVEN live 2026-07-29: selected XORK1Q, the 32nd STAR
    // at LGAV, while it was clipped far below the viewport). So: hit-test first,
    // fall back to the element itself. No scrolling is needed to click.
    function fireClick(cx, cy, el) {
      var hit = document.elementFromPoint(cx, cy);
      var target = hit;
      if (el && (!hit || (hit !== el && !el.contains(hit) && !hit.contains(el)))) target = el;
      if (!target) return false;
      function fire(type, Ctor) {
        var ev;
        try {
          ev = new Ctor(type, { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0, pointerId: 1, isPrimary: true, view: window });
        } catch (e) {
          ev = document.createEvent('MouseEvents');
          ev.initMouseEvent(type, true, true, window, 1, 0, 0, cx, cy, false, false, false, false, 0, null);
        }
        target.dispatchEvent(ev);
      }
      if (window.PointerEvent) fire('pointerdown', PointerEvent);
      fire('mousedown', MouseEvent);
      if (window.PointerEvent) fire('pointerup', PointerEvent);
      fire('mouseup', MouseEvent);
      fire('click', MouseEvent);
      return true;
    }

    A.fms = function () {
      var w = winOf('fms');
      if (!w) return JSON.stringify({ ok: false, reason: 'no_fms_window' });
      // Value-box rects: the stroked frame Fusion draws around ENTERABLE fields.
      // C# marks a field editable only when its value token sits inside one
      // (kind 'vbox'), so read-only data rows stop reading as "edit box".
      var wr = w.getBoundingClientRect();
      var boxes = [];
      var rects = w.querySelectorAll('rect');
      for (var i = 0; i < rects.length; i++) {
        var el = rects[i];
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        var stroke = cs.stroke || '';
        if (!stroke || stroke === 'none') continue;
        var r = el.getBoundingClientRect();
        if (r.width < 20 || r.width > 500 || r.height < 12 || r.height > 60) continue;
        boxes.push({
          k: 'vbox',
          x: Math.round(r.left - wr.left), y: Math.round(r.top - wr.top),
          w: Math.round(r.width), h: Math.round(r.height)
        });
      }
      return JSON.stringify({ ok: true, tokens: withoutErrorBox(w, visTokens(w)), boxes: boxes });
    };

    // The FMS's refusal box (amber 2.5px frame, ~360x40 units, under the field —
    // see fmsError) is read out separately by the form. Left in the page scrape
    // its text paired as the NEXT field's value: "GWT, edit box: INVALID ENTRY"
    // (live 2026-09-24, FUEL page), so drop anything drawn inside it.
    function withoutErrorBox(w, toks) {
      var g = fmsScale(w);
      if (!g) return toks;
      var boxes = [];
      var rects = w.querySelectorAll('rect');
      for (var i = 0; i < rects.length; i++) {
        var cs;
        try { cs = window.getComputedStyle(rects[i]); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        if ((cs.stroke || '').indexOf(AMBER) < 0) continue;
        var r = rects[i].getBoundingClientRect();
        if (Math.abs(r.height / g.scale - 40) > 10 || r.width / g.scale < 200) continue;
        boxes.push({ l: r.left - g.winRect.left, t: r.top - g.winRect.top,
                     r: r.right - g.winRect.left, b: r.bottom - g.winRect.top });
      }
      if (!boxes.length) return toks;
      var out = [];
      for (var k = 0; k < toks.length; k++) {
        var tk = toks[k], inside = false;
        for (var b = 0; b < boxes.length; b++)
          if (tk.x >= boxes[b].l - 2 && tk.x <= boxes[b].r && tk.y >= boxes[b].t - 4 && tk.y <= boxes[b].b)
          { inside = true; break; }
        if (!inside) out.push(tk);
      }
      return out;
    }

    A.ecl = function () {
      var w = winOf('ecl');
      if (!w) return JSON.stringify({ ok: false, reason: 'no_chkl_window' });
      var wr = w.getBoundingClientRect();
      var boxes = [];
      var shapes = w.querySelectorAll('rect,circle');
      for (var i = 0; i < shapes.length; i++) {
        var el = shapes[i];
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        var r = el.getBoundingClientRect();
        if (r.width < 15 || r.width > 45 || r.height < 15 || r.height > 45) continue;
        var x = Math.round(r.left - wr.left);
        // Item checkboxes hug the left edge; wider rects elsewhere are chrome.
        if (el.tagName.toLowerCase() === 'rect' && x > 60) continue;
        boxes.push({
          k: el.tagName.toLowerCase() === 'circle' ? 'radio' : 'box',
          x: x, y: Math.round(r.top - wr.top), f: cs.fill || '', s: cs.stroke || ''
        });
      }
      return JSON.stringify({ ok: true, tokens: visTokens(w), boxes: boxes });
    };

    // Click a visible text (tile, sub-tab, soft button, checklist name) inside the
    // FMS or CHKL window. Match: exact normalized text, or prefix once a trailing
    // ellipsis is stripped ("FPLN UPLINK" matches "FPLN UPLINK…"). occ = 0-based
    // occurrence in document order.
    A.clickWinText = function (kind, needle, occ) {
      var w = winOf(kind);
      if (!w) return 'NO_WINDOW';
      var want = norm(needle);
      var n = occ || 0;
      var ts = w.querySelectorAll('text');
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        var t = norm(el.textContent);
        if (!t) continue;
        var vis;
        try { vis = window.getComputedStyle(el).visibility; } catch (e) { continue; }
        if (vis !== 'visible') continue;
        var bare = t.replace(/…$/, '');
        if (t !== want && bare !== want) continue;
        if (n-- > 0) continue;
        var r = el.getBoundingClientRect();
        // Fall back to the row <g> (rect + text), which is what carries the React
        // handler — that is the node the live clipped-row test dispatched on.
        return fireClick(r.left + r.width / 2, r.top + r.height / 2, el.parentNode || el)
          ? ('CLICKED:' + t) : 'NO_TARGET';
      }
      return 'NOT_FOUND';
    };

    // Document-wide variant of clickWinText: for popup layers (e.g. the ACT/SEC
    // flight-plan dropdown, which only renders its option texts once opened and
    // may mount OUTSIDE the FMS window <g>). Same matching rules.
    A.clickAnyText = function (needle, occ) {
      var want = norm(needle);
      var n = occ || 0;
      var ts = document.querySelectorAll('text');
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        var t = norm(el.textContent);
        if (!t) continue;
        var vis;
        try { vis = window.getComputedStyle(el).visibility; } catch (e) { continue; }
        if (vis !== 'visible') continue;
        var bare = t.replace(/…$/, '');
        if (t !== want && bare !== want) continue;
        if (n-- > 0) continue;
        var r = el.getBoundingClientRect();
        return fireClick(r.left + r.width / 2, r.top + r.height / 2, el.parentNode || el)
          ? ('CLICKED:' + t) : 'NO_TARGET';
      }
      return 'NOT_FOUND';
    };

    // ---- scratchpad DELETE arming ----------------------------------------
    // The A220 scratchpad is a SHARED store synced across every DU view through
    // the ViewListener event "A220.Scratchpad" (the aircraft's own
    // src/avionics/lib/store/scratchpad.ts does exactly this:
    //   Sse = RegisterViewListener('JS_LISTENER_SIMVARS', null, true)
    //   A5 = (s) => Sse.triggerToAllSubscribers('A220.Scratchpad', s)).
    //
    // Its `text` has THREE states, and the Fusion Input component branches on
    // all three when a field is clicked:
    //   text = "TEXT"  -> SUBMIT that text into the field
    //   text = ""      -> COPY the field's current value into the scratchpad
    //   text = null    -> DELETE the field: calls the field's own onDelete()
    // `null` is the cockpit's "--DELETE--" armed state (the component literally
    // renders "--DELETE--" while text is null). For a discontinuity row the
    // slot's onDelete is `setLegFix(legIdx, null)` — i.e. remove the
    // discontinuity — so a delete is: arm null, click the slot, reset to "".
    // Going through the aircraft's own scratchpad + field handler means the FMS
    // applies its normal rules (it raises a MOD the pilot must EXEC, and answers
    // "INVALID DELETE" on a field that refuses) rather than us mutating a plan.
    var SP_EVENT = 'A220.Scratchpad';
    var spL = null, spTap = null;
    function spListener() {
      if (spL) return spL;
      if (typeof RegisterViewListener !== 'function') return null;
      try { spL = RegisterViewListener('JS_LISTENER_SIMVARS', null, true); } catch (e) { return null; }
      return spL;
    }
    // Passive tap: a SECOND listener instance subscribed like the aircraft's own
    // store, recording the last-received scratchpad state. triggerToAllSubscribers
    // round-trips through the engine's event pump, so the store sees the armed
    // state a beat AFTER armDelete() returns — the tap is how the C# side confirms
    // the state actually LANDED before clicking (clicking early hits the Input's
    // copy branch, a silent no-op — the original one-shot delete's failure mode).
    function ensureSpTap() {
      if (spTap) return spTap;
      if (typeof RegisterViewListener !== 'function') return null;
      try {
        spTap = RegisterViewListener('JS_LISTENER_SIMVARS', null, true);
        // Events are PARTIAL states ({text,data} from typing, {error} from a
        // refused entry) — keep both the last event (armStatus) and a MERGED
        // copy (spState), like the aircraft's own store reducer does.
        spTap.on(SP_EVENT, function (s) {
          A.__sp = s;
          var m = A.__spm || {};
          for (var k in s) if (Object.prototype.hasOwnProperty.call(s, k)) m[k] = s[k];
          A.__spm = m;
        });
      } catch (e) { spTap = null; }
      return spTap;
    }
    ensureSpTap();
    function setScratchpad(state) {
      var L = spListener();
      if (!L || typeof L.triggerToAllSubscribers !== 'function') return false;
      L.triggerToAllSubscribers(SP_EVENT, state);
      return true;
    }
    A.armDelete = function () {
      ensureSpTap();
      A.__sp = undefined;
      return setScratchpad({ text: null, data: null }) ? 'ARMED' : 'NO_LISTENER';
    };
    // 'ARMED' once the tap has seen the armed (text=null) state arrive on the
    // event bus — the store subscribes the same way, so arrival here means the
    // Input components' scratchpad is armed too.
    A.armStatus = function () {
      if (A.__sp === undefined) return 'PENDING';
      return (A.__sp && A.__sp.text === null) ? 'ARMED' : 'OTHER';
    };
    A.resetScratchpad = function () { return setScratchpad({ text: '', data: null }) ? 'RESET' : 'NO_LISTENER'; };
    // The merged scratchpad state, for the C# side to VERIFY a clear/type landed
    // before clicking a field, and to read the FMS's broadcast refusal message
    // ("INVALID ENTRY"…) even when the amber-box geometry scan misses it.
    // t: UNKNOWN (no event seen since agent install) / NULL (--DELETE-- armed) /
    // TEXT (text field holds the string in `text`).
    A.spState = function () {
      var s = A.__spm;
      if (!s || s.text === undefined) return JSON.stringify({ t: 'UNKNOWN', err: (s && s.error) || '' });
      if (s.text === null) return JSON.stringify({ t: 'NULL', err: s.error || '' });
      return JSON.stringify({ t: 'TEXT', text: s.text, err: s.error || '' });
    };

    // Click the enterable slot of the nth (0-based) flight-plan discontinuity on
    // the LEGS page. Row layout measured live: "THEN" sits ~29 px above, and the
    // enterable ▯-run slot shares the DISCONTINUITY row's y at the far left —
    // that slot is the input whose onDelete removes the discontinuity.
    // The CALLER must arm the scratchpad first (armDelete) and wait for
    // armStatus()=='ARMED': with the scratchpad armed null, the Input's click
    // handler runs its own onDelete (the FMS's setLegFix(idx, null)).
    A.deleteDiscontinuityClick = function (occ) {
      var w = winOf('fms');
      if (!w) return 'NO_WINDOW';
      var n = occ || 0;
      var ts = w.querySelectorAll('text');
      var i, el, vis, marker = null;
      for (i = 0; i < ts.length; i++) {
        el = ts[i];
        if (norm(el.textContent) !== 'DISCONTINUITY') continue;
        try { vis = window.getComputedStyle(el).visibility; } catch (e) { continue; }
        if (vis !== 'visible') continue;
        if (n-- > 0) continue;
        marker = el;
        break;
      }
      if (!marker) return 'NOT_FOUND';
      var mr = marker.getBoundingClientRect();
      var wr = w.getBoundingClientRect();
      var slot = null;
      for (i = 0; i < ts.length; i++) {
        el = ts[i];
        var t = norm(el.textContent);
        if (!t || t.replace(/[▯□]/g, '').length !== 0) continue;   // ▯/□ run only
        try { vis = window.getComputedStyle(el).visibility; } catch (e) { continue; }
        if (vis !== 'visible') continue;
        var r = el.getBoundingClientRect();
        if (Math.abs(r.top - mr.top) > 14) continue;
        if (r.left - wr.left > 80) continue;                                  // left column
        slot = el;
        break;
      }
      if (!slot) return 'NO_SLOT';
      var sr = slot.getBoundingClientRect();
      var ok = fireClick(sr.left + sr.width / 2, sr.top + sr.height / 2, slot.parentNode || slot);
      // The component resets the scratchpad itself on success; reset anyway so a
      // refused delete can never leave DELETE armed under the next click.
      window.setTimeout(function () { A.resetScratchpad(); }, 600);
      return ok ? 'CLICKED' : 'NO_TARGET';
    };

    // ---- waypoint revision (task) menu ------------------------------------
    // The TaskMenu (TaskMenu/Leg/*.tsx) is a bottom-right-anchored <g> holding
    // a BLACK rect with a 2px WHITE border (default width 385 canvas units), an
    // optional 36px header band (the fix ident) under a white separator line,
    // then 36px-tall item bands (→…/REROUTE/DELETE/HOLD…/FLY OVER/…). Disabled
    // items render #848484; FLY OVER carries a checkbox (28×28 box + a
    // 3px-stroke checkmark path when selected) and does NOT auto-close; every
    // other item closes the menu when clicked. While open, the aircraft draws
    // a full-window TRANSPARENT BACKDROP rect whose own click closes the menu
    // — that backdrop is the official "cancel" gesture taskMenuClose() uses.
    //
    // OPENING it (proven live 2026-07-30, LCLK→LGAV): the trigger is NOT the
    // waypoint ident — the ident is a Fusion scratchpad Input (empty scratchpad
    // click COPIES the ident into the scratchpad; a click with text SUBMITS it
    // into the leg, which is how a stray "GENOS" got inserted over a real
    // waypoint). The menu opens ONLY from the row's little fix-symbol icon, the
    // ~30-unit clickable <g> the leg row draws at canvas x=144 (bundle:
    // `onClick: () => F(<TaskMenuLeg leg legIdx/>)`; discontinuity rows have no
    // icon). openLegMenu targets that icon by React-onClick + geometry.

    // ---- FIBER-ADDRESSED legs (the reliable path) --------------------------
    // Everything above addresses rows by TEXT + geometry, which cannot survive a
    // list that repeats idents or shifts under an edit (live 2026-07-30: the
    // menu opened for the wrong duplicate GENOS). React's own fibers carry the
    // truth: each legs-page row is rendered by a component whose props hold the
    // aircraft's `legIdx`, and the row's widgets expose their real handlers
    // (`onClick` = open this leg's TaskMenu, `onDelete` = the FMS's own
    // setLegFix(idx,null)). Addressing BY legIdx and invoking those props
    // DIRECTLY removes every guess: no occurrence counting, no icon x-band, no
    // scratchpad arming, no synthetic mouse events that can hit a neighbour.
    // PROVEN live: direct onClick opened GENOS legIdx=1's menu; the disco slot's
    // onDelete ran the FMS's own delete (which legitimately raised the aircraft's
    // SELECT CONSTRAINT dialog — a real prompt, not a failure).
    function fiberOf(el) {
      for (var k in el) if (k.indexOf('__reactFiber$') === 0) return el[k];
      return null;
    }
    function propsOf(el) {
      for (var k in el) if (k.indexOf('__reactProps$') === 0) return el[k];
      return null;
    }
    // Walk up this element's fiber chain, collecting the nearest legIdx plus the
    // nearest onDelete/value props (the row's Input component).
    function legInfoOf(el) {
      var f = fiberOf(el), out = { idx: -1, del: null, val: undefined }, n = 0;
      while (f && n < 16) {
        var p = f.memoizedProps;
        if (p) {
          if (out.idx < 0 && typeof p.legIdx === 'number') out.idx = p.legIdx;
          if (!out.del && typeof p.onDelete === 'function') out.del = p.onDelete;
          if (out.val === undefined && 'value' in p) out.val = p.value;
        }
        f = f.return; n++;
      }
      return out;
    }
    // The IDENT text of a leg row, as opposed to the other left-column texts that
    // share the row's legIdx (the track/distance line, "THEN", "HOLD AT"). The
    // discriminator is structural: only the ident sits inside the row's Fusion
    // Input, which is the component carrying `onDelete` — the same component whose
    // handler performs the delete. Geometry cannot tell these apart (track x=10 vs
    // ident x=17, and the track line is the EARLIER node), which is what made the
    // first fiber cut report "266° 29.9" as a leg name and miss every row icon.
    function isIdentNode(el) {
      var f = fiberOf(el), n = 0;
      while (f && n < 10) {
        var p = f.memoizedProps;
        if (p && typeof p.onDelete === 'function' && 'value' in p) return true;
        f = f.return; n++;
      }
      return false;
    }
    // Every visible legs-page row, in screen order, as
    // {idx: aircraft legIdx, id: ident or '' for a discontinuity, y}. The C# side
    // keys its rows on `idx`, so a click can never drift to another row.
    A.legs = function () {
      var w = winOf('fms');
      if (!w) return JSON.stringify({ ok: false, reason: 'no_fms_window' });
      var wr = w.getBoundingClientRect();
      var scale = wr.width / 740;
      if (!(scale > 0)) return JSON.stringify({ ok: false, reason: 'no_scale' });
      var ts = w.querySelectorAll('text');
      var seen = [], rows = [];
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        var t = norm(el.textContent);
        if (!t) continue;
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        if (parseFloat(cs.opacity || '1') === 0) continue;
        var r = el.getBoundingClientRect();
        var ux = (r.left - wr.left) / scale, uy = (r.top - wr.top) / scale;
        if (ux > 45 || uy < 200) continue;              // left column, below header
        if (!isIdentNode(el)) continue;                 // track line / THEN / HOLD AT
        var info = legInfoOf(el);
        if (info.idx < 0) continue;                     // not a leg row (chrome)
        // Dedupe SAME-ROW repeats only (same legIdx at ~the same y). A bare
        // idx-keyed dedupe silently DROPPED a discontinuity sharing its legIdx
        // with a neighbouring waypoint, shifting every row below by one — which
        // is how DEL on a gap deleted real waypoints (live 2026-07-30). Distinct
        // rows legitimately share an idx; only co-located texts are duplicates.
        var dup = false;
        for (var s = 0; s < seen.length; s++)
          if (seen[s].i === info.idx && Math.abs(seen[s].y - uy) < 12) { dup = true; break; }
        if (dup) continue;
        seen.push({ i: info.idx, y: uy });
        var placeholder = t.replace(/[▯□\-. ]/g, '').length === 0;
        rows.push({ idx: info.idx, id: placeholder ? '' : t, y: Math.round(uy) });
      }
      rows.sort(function (a, b) { return a.y - b.y; });
      return JSON.stringify({ ok: true, rows: rows });
    };
    // The row <g> (and its props) for a given aircraft legIdx, found via fibers.
    // wantDisco says WHICH row the caller means when a discontinuity shares its
    // legIdx with a neighbouring waypoint (the aircraft does this — live
    // 2026-07-30): true prefers the ▯-placeholder slot (delete the gap), false
    // prefers the real ident (open the waypoint's menu). Without the preference,
    // "first DOM match" could hand a waypoint's onDelete to a gap delete — the
    // exact wrong-row bug that removed real waypoints.
    function rowNodesFor(legIdx, wantDisco) {
      var w = winOf('fms');
      if (!w) return null;
      var wr = w.getBoundingClientRect();
      var scale = wr.width / 740;
      if (!(scale > 0)) return null;
      var out = { win: w, wr: wr, scale: scale, ident: null, del: null, icon: null,
                  val: '', isPh: false };
      var ts = w.querySelectorAll('text');
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        if (!norm(el.textContent)) continue;
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        if (parseFloat(cs.opacity || '1') === 0) continue;
        var r = el.getBoundingClientRect();
        if ((r.left - wr.left) / scale > 45 || (r.top - wr.top) / scale < 200) continue;
        if (!isIdentNode(el)) continue;      // must be the row's Input, not its track line
        var info = legInfoOf(el);
        if (info.idx !== legIdx) continue;
        var t = norm(el.textContent);
        var ph = t.replace(/[▯□\-. ]/g, '').length === 0;
        var preferred = wantDisco ? ph : !ph;
        if (!out.ident || (preferred && (wantDisco ? !out.isPh : out.isPh))) {
          out.ident = el; out.del = info.del; out.rowRect = r;
          out.val = t; out.isPh = ph;
        }
        if (preferred) break;
      }
      if (!out.ident) return out;
      // The menu icon: the row's OTHER clickable <g> — identified by fiber legIdx
      // (not an x band), so a layout change cannot silently retarget it.
      var rowCy = out.rowRect.top + out.rowRect.height / 2;
      var gs = w.querySelectorAll('g');
      for (i = 0; i < gs.length; i++) {
        var p = propsOf(gs[i]);
        if (!p || !p.onClick) continue;
        var gr;
        try { gr = gs[i].getBoundingClientRect(); } catch (e) { continue; }
        if (Math.abs((gr.top + gr.height / 2) - rowCy) > 20 * scale + 6) continue;
        if (gr.width / scale > 70) continue;            // the wide ident Input
        if (legInfoOf(gs[i]).idx !== legIdx) continue;
        out.icon = gs[i];
        break;
      }
      return out;
    }
    // Open a leg's revision menu BY legIdx, by calling the icon's own React
    // onClick. Returns CLICKED / NO_ROW / NO_ICON / NO_WINDOW.
    A.openLegMenuAt = function (legIdx) {
      var n = rowNodesFor(legIdx);
      if (!n) return 'NO_WINDOW';
      if (!n.ident) return 'NO_ROW';
      if (!n.icon) return 'NO_ICON';
      var p = propsOf(n.icon);
      if (!p || !p.onClick) return 'NO_ICON';
      try { p.onClick({ stopPropagation: function () {}, preventDefault: function () {} }); }
      catch (e) { return 'CALL_ERR'; }
      return 'CLICKED';
    };
    // Delete a leg / discontinuity BY legIdx through the FMS's OWN onDelete prop
    // (setLegFix(idx,null)) — no scratchpad arming, so the copy/submit branch
    // that once corrupted a plan can never be reached. The FMS applies its normal
    // rules: it may raise a MOD to EXEC, refuse via its amber box, or open the
    // SELECT CONSTRAINT dialog; the CALLER inspects which.
    // wantDisco: refuse (WRONG_ROW) rather than run a real waypoint's onDelete
    // when the caller meant a discontinuity but no ▯-slot carries this legIdx —
    // a silent wrong-row delete is exactly the failure this addressing exists to
    // prevent. The success echo carries what was called ("CALLED|" + row text)
    // so the C# log can prove which row's handler ran.
    A.deleteLegAt = function (legIdx, wantDisco) {
      var n = rowNodesFor(legIdx, !!wantDisco);
      if (!n) return 'NO_WINDOW';
      if (!n.ident) return 'NO_ROW';
      if (wantDisco && !n.isPh) return 'WRONG_ROW|' + n.val;
      if (!n.del) return 'NO_DELETE';
      try { n.del(); } catch (e) { return 'CALL_ERR'; }
      return 'CALLED|' + n.val;
    };
    // Same icon as openLegMenuAt, but via synthetic pointer/mouse events at its
    // center (fireClick) instead of a direct props.onClick call. Fallback for
    // rows scrolled below the page fold, where the direct call has been seen to
    // produce no menu (live 2026-07-31, arrival legs at the list bottom): the
    // event dispatch bubbles to React's root listener — the path proven to act
    // on clipped rows (the XORK1Q STAR selection). Still fiber-addressed by the
    // aircraft's legIdx, so it cannot land on a neighbouring row; the
    // scratchpad-hazard ident Input is a SIBLING of the icon, never an
    // ancestor, so nothing the click bubbles through can touch the scratchpad.
    A.openLegMenuAtMouse = function (legIdx) {
      var n = rowNodesFor(legIdx);
      if (!n) return 'NO_WINDOW';
      if (!n.ident) return 'NO_ROW';
      if (!n.icon) return 'NO_ICON';
      var gr;
      try { gr = n.icon.getBoundingClientRect(); } catch (e) { return 'NO_ICON'; }
      return fireClick(gr.left + gr.width / 2, gr.top + gr.height / 2, n.icon)
        ? 'CLICKED' : 'NO_TARGET';
    };
    // Why did a leg's revision menu not open? One JSON blob for the debug log:
    // where the row and its icon sit relative to the window, in canvas units, so
    // "clipped below the fold" is distinguishable from "icon missing" without a
    // live probe session. `below` = the row starts beyond the window's bottom
    // edge (the list draws it, but the viewport has scrolled past it).
    A.legRowDiag = function (legIdx) {
      var n = rowNodesFor(legIdx);
      if (!n) return JSON.stringify({ ok: false, reason: 'no_window' });
      var winH = n.wr.height / n.scale;
      var out = { ok: true, winHU: Math.round(winH), ident: !!n.ident, icon: !!n.icon };
      if (n.ident && n.rowRect) {
        var rowY = (n.rowRect.top - n.wr.top) / n.scale;
        out.rowYU = Math.round(rowY);
        out.below = rowY > winH;
      }
      if (n.icon) {
        try {
          var ir = n.icon.getBoundingClientRect();
          out.iconXU = Math.round((ir.left - n.wr.left) / n.scale);
          out.iconYU = Math.round((ir.top - n.wr.top) / n.scale);
          var p = propsOf(n.icon);
          out.iconOnClick = !!(p && p.onClick);
        } catch (e) { }
      }
      return JSON.stringify(out);
    };

    // The nth (0-based) LEFT-COLUMN occurrence of a leg ident, mirroring the C#
    // leg grouper's occurrence counting (canvas x<=45, below the header band,
    // in y order) so row N in the form always targets the same leg here.
    function findLegIdent(w, wr, scale, ident, occ) {
      var want = norm(ident);
      var ts = w.querySelectorAll('text');
      var rows = [];
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        if (norm(el.textContent) !== want) continue;
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        if (parseFloat(cs.opacity || '1') === 0) continue;   // stale/faded node
        var r = el.getBoundingClientRect();
        if ((r.left - wr.left) / scale > 45) continue;   // constraint/RNP columns
        if ((r.top - wr.top) / scale < 200) continue;    // window header band
        rows.push({ el: el, r: r });
      }
      rows.sort(function (a, b) { return a.r.top - b.r.top; });
      return (occ || 0) < rows.length ? rows[occ || 0] : null;
    }
    // Open the revision (task) menu for a leg. Returns CLICKED on a dispatched
    // icon click (the CALLER verifies the menu actually opened via taskMenu()/
    // overlay()), NO_ICON when the row has no fix-symbol icon (discontinuities,
    // the (DIR) annotation row), NOT_FOUND / NO_WINDOW as usual.
    A.openLegMenu = function (ident, occ) {
      var w = winOf('fms');
      if (!w) return 'NO_WINDOW';
      var wr = w.getBoundingClientRect();
      var scale = wr.width / 740;
      if (!(scale > 0)) return 'NO_WINDOW';
      var hit = findLegIdent(w, wr, scale, ident, occ);
      if (!hit) return 'NOT_FOUND';
      var rowCy = hit.r.top + hit.r.height / 2;
      // The icon: a clickable <g> (React onClick prop) centered on the row's y,
      // in the canvas x band around 144 (measured live: a 30×30 g whose left
      // edge sat 112 px right of the ident at scale 0.88 → canvas x 144).
      var gs = w.querySelectorAll('g');
      for (var i = 0; i < gs.length; i++) {
        var g = gs[i], has = false;
        for (var k in g) {
          if (k.indexOf('__reactProps$') === 0 && g[k] && g[k].onClick) { has = true; break; }
        }
        if (!has) continue;
        var gr;
        try { gr = g.getBoundingClientRect(); } catch (e) { continue; }
        if (Math.abs((gr.top + gr.height / 2) - rowCy) > 18 * scale + 6) continue;
        var ux = (gr.left - wr.left) / scale, uw = gr.width / scale;
        if (ux < 110 || ux > 190 || uw < 14 || uw > 70) continue;
        return fireClick(gr.left + gr.width / 2, gr.top + gr.height / 2, g)
          ? 'CLICKED' : 'NO_TARGET';
      }
      return 'NO_ICON';
    };
    // Click the leg IDENT Input itself — the cockpit's waypoint-entry gesture.
    // The CALLER must ensure the aircraft scratchpad holds the text to submit:
    // with an EMPTY scratchpad this click silently COPIES the ident into the
    // scratchpad instead (Fusion Input semantics), which is the corruption
    // loop openLegMenu exists to avoid. C# only calls this from the explicit
    // scratchpad-commit path, never from Enter.
    A.clickLegIdent = function (ident, occ) {
      var w = winOf('fms');
      if (!w) return 'NO_WINDOW';
      var wr = w.getBoundingClientRect();
      var scale = wr.width / 740;
      if (!(scale > 0)) return 'NO_WINDOW';
      var hit = findLegIdent(w, wr, scale, ident, occ);
      if (!hit) return 'NOT_FOUND';
      return fireClick(hit.r.left + hit.r.width / 2, hit.r.top + hit.r.height / 2,
                       hit.el.parentNode || hit.el)
        ? 'CLICKED' : 'NO_TARGET';
    };
    function findTaskMenu() {
      var w = winOf('fms');
      if (!w) return null;
      var wr = w.getBoundingClientRect();
      var scale = wr.width / 740;                    // FMS window canvas width
      if (!(scale > 0)) return null;
      var rects = w.querySelectorAll('rect');
      for (var i = 0; i < rects.length; i++) {
        var el = rects[i];
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        if ((cs.stroke || '').indexOf('rgb(255, 255, 255)') < 0) continue;
        if ((cs.fill || '').indexOf('rgb(0, 0, 0)') < 0) continue;
        var r = el.getBoundingClientRect();
        var wu = r.width / scale, hu = r.height / scale;
        // Menus are ~385-465 units wide; ≥2 bands tall. The 590-wide CROSSING
        // dialog and the window's own frame fall outside this width band, and
        // value-box frames (~33 units tall) fail the height test.
        if (wu < 250 || wu > 520 || hu < 70) continue;
        return { g: el.parentNode, rect: r, winRect: wr, scale: scale };
      }
      return null;
    }
    // Header + 36px item bands of an open menu. Placeholder-only fragments
    // (the dashes/▯ of a PB/D WPT inline input) are dropped from band labels.
    function taskMenuModel(m) {
      var texts = m.g.querySelectorAll('text');
      var toks = [];
      var i, el, vis, r;
      for (i = 0; i < texts.length; i++) {
        el = texts[i];
        var t = norm(el.textContent);
        if (!t) continue;
        try { vis = window.getComputedStyle(el).visibility; } catch (e) { continue; }
        if (vis !== 'visible') continue;
        r = el.getBoundingClientRect();
        if (r.top < m.rect.top - 2 || r.top > m.rect.top + m.rect.height) continue;
        var fill = '';
        try { fill = window.getComputedStyle(el).fill || ''; } catch (e2) { }
        toks.push({ t: t, el: el, cy: r.top + r.height / 2, x: r.left,
                    gray: fill.indexOf('rgb(132, 132, 132)') >= 0 });
      }
      toks.sort(function (a, b) { return (a.cy - b.cy) || (a.x - b.x); });
      // Header separator line: a <line> ~36 units below the menu top.
      var headerCut = -1;
      var lines = m.g.querySelectorAll('line');
      for (i = 0; i < lines.length; i++) {
        var lr = lines[i].getBoundingClientRect();
        var du = (lr.top - m.rect.top) / m.scale;
        if (du > 28 && du < 44 && lr.width / m.scale > 200) { headerCut = lr.top; break; }
      }
      var header = '';
      var bands = [];
      var bandPitch = 36 * m.scale;
      for (i = 0; i < toks.length; i++) {
        var tk = toks[i];
        if (headerCut > 0 && tk.cy < headerCut) {
          header = header ? header + ' ' + tk.t : tk.t;
          continue;
        }
        var placeholder = tk.t.replace(/[-▯□°/.\s]/g, '').length === 0;
        var b = bands.length > 0 ? bands[bands.length - 1] : null;
        if (!b || tk.cy - b.cy > bandPitch * 0.5) {
          bands.push({ t: placeholder ? '' : tk.t, cy: tk.cy, el: tk.el,
                       gray: tk.gray, grayAll: tk.gray });
        } else {
          if (!placeholder) b.t = b.t ? b.t + ' ' + tk.t : tk.t;
          b.grayAll = b.grayAll && tk.gray;
          if (!b.el) b.el = tk.el;
        }
      }
      var items = [];
      for (i = 0; i < bands.length; i++) {
        var bd = bands[i];
        if (!bd.t) continue;                          // input-only band, no label
        // Checkbox state: a ~28-unit box rect in the band; checkmark = a
        // visible <path> whose box also falls in the band.
        var hasBox = false, hasCheck = false, hasInput = false;
        var boxes2 = m.g.querySelectorAll('rect');
        var j, br;
        for (j = 0; j < boxes2.length; j++) {
          br = boxes2[j].getBoundingClientRect();
          if (Math.abs((br.top + br.height / 2) - bd.cy) > bandPitch / 2) continue;
          var side = br.width / m.scale;
          if (side > 20 && side < 36 && Math.abs(br.width - br.height) < 6 * m.scale) hasBox = true;
          // An INLINE ENTRY FIELD in the band (PB/D WPT takes a bearing/distance,
          // ALONG TRK WPT a distance — bundle: those items hold an input and carry
          // NO onClick of their own). Pressing such an item just closes the menu
          // and does nothing, so the C# side must say so instead of dead-pressing.
          if (side >= 60 && side <= 200 && br.height / m.scale > 16) hasInput = true;
        }
        if (hasBox) {
          var paths = m.g.querySelectorAll('path');
          for (j = 0; j < paths.length; j++) {
            try { if (window.getComputedStyle(paths[j]).visibility !== 'visible') continue; } catch (e3) { continue; }
            br = paths[j].getBoundingClientRect();
            if (Math.abs((br.top + br.height / 2) - bd.cy) > bandPitch / 2) continue;
            if (br.width / m.scale < 36) { hasCheck = true; break; }
          }
        }
        items.push({ t: bd.t, d: bd.grayAll, cb: hasBox, ck: hasCheck, inp: hasInput,
                     cy: bd.cy, el: bd.el });
      }
      return { header: header, items: items };
    }
    A.taskMenu = function () {
      var m = findTaskMenu();
      if (!m) return JSON.stringify({ open: false });
      var mm = taskMenuModel(m);
      var out = [];
      for (var i = 0; i < mm.items.length; i++) {
        var it = mm.items[i];
        out.push({ t: it.t, d: it.d, cb: it.cb, ck: it.ck, inp: it.inp });
      }
      return JSON.stringify({ open: true, header: mm.header, items: out });
    };

    // ---- the other two overlay widgets + the FMS's own error box ------------
    // Colours are the aircraft's palette (Components/Colours.ts): Amber #ffe300,
    // Cyan #00bfe6. Dialog frame (Components/Dialog `ht`): fill #232323, stroke
    // grey, strokeWidth 5, a 39-unit header band, an optional status line near
    // the bottom, and a DONE (or CNCL, when the dialog can be cancelled) button
    // at x=width-101/y=height-61. Dropdown option list (`I5`+`x5`): a grey
    // 2px-stroke frame over 41-unit-pitch option rows, each row a
    // FILL-TRANSPARENT rect; the CURRENT option's text is Cyan, the rest white;
    // long lists paginate with their own PREV/NEXT rows. Input error box: a
    // black rect with an AMBER 2.5px stroke, 360x40 units, holding the FMS's own
    // message ("INVALID ENTRY", "NOT IN DATA BASE", "INVALID DELETE"…) — it
    // self-clears after 3 s, so it must be read on the very next poll.
    var AMBER = 'rgb(255, 227, 0)', CYAN = 'rgb(0, 191, 230)';
    function fmsScale(w) {
      var wr = w.getBoundingClientRect();
      var s = wr.width / 740;
      return s > 0 ? { winRect: wr, scale: s } : null;
    }
    // Dialogs/dropdowns may mount OUTSIDE the FMS window's own <g> (the dialog
    // layer is a sibling) — so search the window first, then the whole document.
    function overlayRoots() {
      var out = [];
      var w = winOf('fms');
      if (w) out.push(w);
      var root = document.getElementById('MSFS_REACT_MOUNT');
      if (root && root !== w) out.push(root);
      return out;
    }
    function findDialog() {
      var w = winOf('fms');
      var g = w ? fmsScale(w) : null;
      if (!g) return null;
      var roots = overlayRoots();
      for (var k = 0; k < roots.length; k++) {
        var rects = roots[k].querySelectorAll('rect');
        for (var i = 0; i < rects.length; i++) {
          var el = rects[i], cs;
          try { cs = window.getComputedStyle(el); } catch (e) { continue; }
          if (cs.visibility !== 'visible') continue;
          if ((cs.fill || '').indexOf('rgb(35, 35, 35)') < 0) continue;
          var sw = parseFloat(cs.strokeWidth || '0') / g.scale;
          if (!(sw > 3.5 && sw < 7)) continue;
          var r = el.getBoundingClientRect();
          if (r.width / g.scale < 200 || r.height / g.scale < 100) continue;
          return { g: el.parentNode, rect: r, winRect: g.winRect, scale: g.scale };
        }
      }
      return null;
    }
    // A <text>'s content with its tspan RUNS separated. Fusion composes a dialog
    // title from two tspans ("HOLD AT" + the ident), and raw textContent glues
    // them into "HOLD ATRDS" (live 2026-07-30) — unreadable, and it also breaks
    // the form's first-token ident check. Single-run elements are untouched.
    function textOfEl(el) {
      var sp = el.getElementsByTagName ? el.getElementsByTagName('tspan') : null;
      if (!sp || sp.length < 2) return norm(el.textContent);
      var parts = [];
      for (var i = 0; i < sp.length; i++) {
        var t = norm(sp[i].textContent);
        if (t) parts.push(t);
      }
      return parts.length ? parts.join(' ') : norm(el.textContent);
    }

    // Visible texts whose centre lies inside `rect`, in reading order.
    function textsIn(root, rect, pad) {
      var out = [];
      var ts = root.querySelectorAll('text');
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        var t = textOfEl(el);
        if (!t) continue;
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        var r = el.getBoundingClientRect();
        var cx = r.left + r.width / 2, cy = r.top + r.height / 2;
        var p = pad || 0;
        if (cx < rect.left - p || cx > rect.left + rect.width + p) continue;
        if (cy < rect.top - p || cy > rect.top + rect.height + p) continue;
        out.push({ t: t, el: el, x: r.left, y: r.top, cy: cy,
                   fill: cs.fill || '', gray: (cs.fill || '').indexOf('rgb(132, 132, 132)') >= 0 });
      }
      out.sort(function (a, b) { return (a.y - b.y) || (a.x - b.x); });
      return out;
    }
    A.fmsDialog = function () {
      var d = findDialog();
      if (!d) return JSON.stringify({ open: false });
      var ts = textsIn(d.g, d.rect, 0);
      // Header = the gray text in the top 39-unit band. Status = text in the
      // bottom band. DONE/CNCL identifies itself by name.
      var headBand = d.rect.top + 39 * d.scale;
      var header = '', done = null, i;
      for (i = 0; i < ts.length; i++) {
        if (ts[i].cy <= headBand && ts[i].gray) header = header ? header + ' ' + ts[i].t : ts[i].t;
        if (ts[i].t === 'DONE' || ts[i].t === 'CNCL') done = ts[i].t;
      }
      return JSON.stringify({
        open: true, header: header, done: done,
        x: Math.round(d.rect.left - d.winRect.left), y: Math.round(d.rect.top - d.winRect.top),
        w: Math.round(d.rect.width), h: Math.round(d.rect.height)
      });
    };
    // The dialog's OWN content, scoped by DOM ANCESTRY (its <g> subtree) rather
    // than by its rectangle. A geometric clip is wrong here: the dialog floats
    // over the page, so page text that merely happens to sit under it lands in
    // the clip. Live proof (2026-07-30, INTC CRS - RDS at 250,548 482x488): the
    // clip swallowed RNP/2.00//-----/DISCONTINUITY/FIX…/COPY TO SEC/DTG/ETE/ETA
    // from the legs list behind, which is exactly the "messed up format" a pilot
    // hears when a follow-up dialog opens. Ancestry cannot make that mistake.
    A.fmsDialogTokens = function () {
      var d = findDialog();
      if (!d) return JSON.stringify({ ok: false, reason: 'no_dialog' });
      var wr = d.winRect;
      var ts = d.g.querySelectorAll('text');
      var tokens = [];
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        var t = textOfEl(el);
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        var pendingInput = isPendingFill(cs.fill);
        if (!t && !pendingInput) continue;
        if (cs.visibility !== 'visible') continue;
        if (parseFloat(cs.opacity || '1') === 0) continue;
        var r = el.getBoundingClientRect();
        var dtok = {
          t: t, x: Math.round(r.left - wr.left), y: Math.round(r.top - wr.top),
          c: colorOf(el)
        };
        // Same structural facts as the page scrape (visTokens): dropdown faces,
        // genuinely pressable tokens (`cl` — without it every data cell of the
        // SELECT WPT picker read as a button), entry ranges, and a refused
        // Input's real value instead of the scratchpad it is echoing.
        if (dtok.c !== 'gray') {
          var fi = fiberInfoOf(el);
          if (fi) {
            if (fi.dd) { dtok.dd = 1; dtok.n = fi.dd.n; }
            if (fi.clickDepth >= 0 && fi.clickDepth <= CLICK_DEPTH_MAX) dtok.cl = 1;
            if (fi.mn !== null) { dtok.mn = fi.mn; dtok.mx = fi.mx; }
            if (fi.ro) dtok.ro = 1;
            if (pendingInput && isPendingEcho(fi.input, cs.fill)) {
              dtok.t = inputDisplayValue(fi.input) || '▯';
              dtok.c = 'white';
            }
          }
        }
        if (!dtok.t) continue;
        tokens.push(dtok);
      }
      var boxes = [];
      var rects = d.g.querySelectorAll('rect');
      for (i = 0; i < rects.length; i++) {
        var bel = rects[i], bcs;
        try { bcs = window.getComputedStyle(bel); } catch (e) { continue; }
        if (bcs.visibility !== 'visible') continue;
        var stroke = bcs.stroke || '';
        if (!stroke || stroke === 'none') continue;
        var br = bel.getBoundingClientRect();
        if (br.width < 20 || br.width > 500 || br.height < 12 || br.height > 60) continue;
        boxes.push({
          k: 'vbox', x: Math.round(br.left - wr.left), y: Math.round(br.top - wr.top),
          w: Math.round(br.width), h: Math.round(br.height)
        });
      }
      var parsed = JSON.parse(A.fmsDialog());
      return JSON.stringify({
        ok: true, header: parsed.header, done: parsed.done,
        x: parsed.x, y: parsed.y, w: parsed.w, h: parsed.h,
        tokens: tokens, boxes: boxes
      });
    };
    A.dialogDone = function () {
      var d = findDialog();
      if (!d) return 'NO_DIALOG';
      var ts = textsIn(d.g, d.rect, 0);
      for (var i = 0; i < ts.length; i++) {
        if (ts[i].t !== 'DONE' && ts[i].t !== 'CNCL') continue;
        var r = ts[i].el.getBoundingClientRect();
        return fireClick(r.left + r.width / 2, r.top + r.height / 2,
                         ts[i].el.parentNode || ts[i].el) ? ('CLICKED:' + ts[i].t) : 'NO_TARGET';
      }
      return 'NO_BUTTON';
    };
    // An OPEN dropdown list: >=2 fill-transparent rects of 41-unit height that
    // share a left edge and stack on a 41-unit pitch.
    function findDropdown() {
      var w = winOf('fms');
      var g = w ? fmsScale(w) : null;
      if (!g) return null;
      var roots = overlayRoots();
      for (var k = 0; k < roots.length; k++) {
        var rects = roots[k].querySelectorAll('rect');
        var cands = [];
        for (var i = 0; i < rects.length; i++) {
          var el = rects[i], cs;
          try { cs = window.getComputedStyle(el); } catch (e) { continue; }
          if (cs.visibility !== 'visible') continue;
          if ((cs.fill || '').indexOf('rgba(0, 0, 0, 0)') < 0) continue;   // transparent
          var r = el.getBoundingClientRect();
          if (Math.abs(r.height / g.scale - 41) > 5) continue;
          if (r.width / g.scale < 60) continue;
          cands.push({ el: el, r: r });
        }
        // Group by left edge; a real list is >= 2 rows on a ~41-unit pitch.
        for (i = 0; i < cands.length; i++) {
          var col = [cands[i]];
          for (var j = 0; j < cands.length; j++) {
            if (j === i) continue;
            if (Math.abs(cands[j].r.left - cands[i].r.left) > 6 * g.scale) continue;
            if (Math.abs(cands[j].r.width - cands[i].r.width) > 8 * g.scale) continue;
            col.push(cands[j]);
          }
          if (col.length < 2) continue;
          col.sort(function (a, b) { return a.r.top - b.r.top; });
          var pitchOk = true;
          for (j = 1; j < col.length; j++)
            if (Math.abs((col[j].r.top - col[j - 1].r.top) / g.scale - 41) > 6) pitchOk = false;
          if (!pitchOk) continue;
          return { rows: col, root: roots[k], scale: g.scale, winRect: g.winRect };
        }
      }
      return null;
    }
    function dropdownModel(d) {
      // Scope the text read to the dropdown's OWN subtree — the lowest common
      // ancestor of its option rects. The option rows are TRANSPARENT rects
      // floating over the page, so a geometric read against the whole window
      // pulls in whatever lies beneath each row (live 2026-07-30, CROSSING's
      // altitude chooser: options read as "AT FLT PHASE" and "CLEAR ALL BETWEEN
      // DTG" — dialog text bleeding through). If the walk reaches the window
      // root the scope degrades to the old behavior rather than dropping rows.
      var lca = d.rows[0].el.parentNode;
      function containsAll(node) {
        for (var q = 0; q < d.rows.length; q++)
          if (!node.contains(d.rows[q].el)) return false;
        return true;
      }
      var guard = 0;
      while (lca && lca !== d.root && !containsAll(lca) && guard++ < 20)
        lca = lca.parentNode;
      var scope = lca && containsAll(lca) ? lca : d.root;
      var items = [];
      for (var i = 0; i < d.rows.length; i++) {
        var r = d.rows[i].r;
        var ts = textsIn(scope, r, 0);
        var label = '', sel = false;
        for (var j = 0; j < ts.length; j++) {
          label = label ? label + ' ' + ts[j].t : ts[j].t;
          if (ts[j].fill.indexOf(CYAN) >= 0) sel = true;
        }
        if (!label) continue;
        items.push({ t: label, sel: sel, el: d.rows[i].el, r: r });
      }
      return items;
    }
    A.fmsDropdown = function () {
      var d = findDropdown();
      if (!d) return JSON.stringify({ open: false });
      var items = dropdownModel(d);
      if (items.length < 2) return JSON.stringify({ open: false });
      var out = [];
      for (var i = 0; i < items.length; i++) out.push({ t: items[i].t, sel: items[i].sel });
      return JSON.stringify({ open: true, items: out });
    };
    A.dropdownClick = function (idx) {
      var d = findDropdown();
      if (!d) return 'NO_DROPDOWN';
      var items = dropdownModel(d);
      if (idx < 0 || idx >= items.length) return 'NOT_FOUND';
      var r = items[idx].r;
      return fireClick(r.left + r.width / 2, r.top + r.height / 2,
                       items[idx].el.parentNode || items[idx].el) ? 'CLICKED' : 'NO_TARGET';
    };
    // Dismiss an OPEN dropdown. `taskMenuClose` cannot do this: it requires the
    // task menu's own frame (black fill + white stroke, 250-520 units) and returns
    // NO_MENU for a dropdown — so Escape on an open list used to report "Dropdown
    // cancelled" while the list stayed open (found 2026-07-30). Fusion mounts a
    // full-window click-swallowing BACKDROP under every overlay, and clicking it is
    // the aircraft's own cancel; aim at a point clear of the option column.
    A.dropdownClose = function () {
      var d = findDropdown();
      if (!d) return 'NO_DROPDOWN';
      var first = d.rows[0].r, last = d.rows[d.rows.length - 1].r;
      var wr = d.winRect;
      // Prefer a point well above the list; fall back to well below it.
      var cx = first.left + first.width / 2;
      var cy = first.top - 80 * d.scale;
      if (cy < wr.top + 20 * d.scale) cy = last.top + last.height + 80 * d.scale;
      if (cy > wr.top + wr.height - 20 * d.scale) {
        // Nowhere vertical is clear — go left of the column instead.
        cx = Math.max(wr.left + 20 * d.scale, first.left - 80 * d.scale);
        cy = first.top + first.height / 2;
      }
      return fireClick(cx, cy, null) ? 'CLOSED' : 'NO_TARGET';
    };
    // The FMS's own rejection message (amber-bordered box under a field). Read on
    // every poll: it self-clears after 3 s, and it is the ONLY place the aircraft
    // says WHY an entry was refused.
    A.fmsError = function () {
      var w = winOf('fms');
      var g = w ? fmsScale(w) : null;
      if (!g) return '';
      var roots = overlayRoots();
      for (var k = 0; k < roots.length; k++) {
        var rects = roots[k].querySelectorAll('rect');
        for (var i = 0; i < rects.length; i++) {
          var el = rects[i], cs;
          try { cs = window.getComputedStyle(el); } catch (e) { continue; }
          if (cs.visibility !== 'visible') continue;
          if ((cs.stroke || '').indexOf(AMBER) < 0) continue;
          var r = el.getBoundingClientRect();
          if (Math.abs(r.height / g.scale - 40) > 10) continue;
          if (r.width / g.scale < 200) continue;
          var ts = textsIn(roots[k], r, 0);
          var msg = '';
          for (var j = 0; j < ts.length; j++)
            if (ts[j].fill.indexOf(AMBER) >= 0) msg = msg ? msg + ' ' + ts[j].t : ts[j].t;
          if (msg) return msg;
        }
      }
      return '';
    };
    // ONE call per poll for every overlay state (the form polls at 1.3 s; four
    // separate round trips would be pure waste).
    A.overlay = function () {
      var menu = findTaskMenu();
      var out = { kind: 'none', err: A.fmsError() };
      if (menu) {
        var mm = taskMenuModel(menu);
        var mi = [];
        for (var i = 0; i < mm.items.length; i++)
          mi.push({ t: mm.items[i].t, d: mm.items[i].d, cb: mm.items[i].cb,
                    ck: mm.items[i].ck, inp: mm.items[i].inp });
        if (mi.length) { out.kind = 'menu'; out.header = mm.header; out.items = mi; return JSON.stringify(out); }
      }
      var dd = findDropdown();
      if (dd) {
        var di = dropdownModel(dd);
        if (di.length >= 2) {
          var o2 = [];
          for (i = 0; i < di.length; i++) o2.push({ t: di[i].t, sel: di[i].sel });
          out.kind = 'dropdown'; out.items = o2; return JSON.stringify(out);
        }
      }
      var dl = findDialog();
      if (dl) {
        var parsed = JSON.parse(A.fmsDialog());
        out.kind = 'dialog'; out.header = parsed.header; out.done = parsed.done;
        out.x = parsed.x; out.y = parsed.y; out.w = parsed.w; out.h = parsed.h;
      }
      return JSON.stringify(out);
    };
    A.taskMenuClick = function (idx) {
      var m = findTaskMenu();
      if (!m) return 'NO_MENU';
      var mm = taskMenuModel(m);
      if (idx < 0 || idx >= mm.items.length) return 'NOT_FOUND';
      var it = mm.items[idx];
      if (it.d) return 'DISABLED';
      var cx = m.rect.left + m.rect.width / 2;
      var ok = fireClick(cx, it.cy, it.el ? (it.el.parentNode || it.el) : null);
      return ok ? 'CLICKED' : 'NO_TARGET';
    };
    A.taskMenuClose = function () {
      var m = findTaskMenu();
      if (!m) return 'NO_MENU';
      // Click the aircraft's own backdrop: any window point clear of the menu
      // (which is bottom-right-anchored — aim upper-left, below the tab row).
      var cx = m.winRect.left + 60 * m.scale;
      var cy = Math.max(m.winRect.top + 120 * m.scale, m.rect.top - 60 * m.scale);
      var ok = fireClick(cx, cy, null);
      return ok ? 'CLOSED' : 'NO_TARGET';
    };

    // ---- shared value-node hunt (the COMMIT target) ------------------------
    // Given a field's gray LABEL node, find the text node holding that field's
    // VALUE. This MUST mirror ParseFms's pairing rules exactly: the form reads
    // fields with one rule set and clicks them with this one, so any divergence
    // types the pilot's number into a field they never chose.
    //
    // Two rules, both learned from live failures:
    //  1. A GRAY token is a LABEL and can never be a value. Without this, the
    //     FUEL page's right-aligned label columns (ZFW/GWT/TOW/LW at one x,
    //     BLOCK/TAXI/TRIP below each other) offered the NEXT LABEL DOWN at
    //     dx 0 — so a plain nearest-dx hunt clicked GWT for ZFW and TAXI for
    //     BLOCK, and the pilot's ZFW went nowhere (live 2026-07-30).
    //  2. INLINE beats BELOW. Fusion draws entry tables as "LABEL [box]" on ONE
    //     row, and the row BELOW is the next field's box — equally close in x.
    //     Preferring the label's own row resolves that tie the right way.
    // Returns null when nothing qualifies (caller answers NO_VALUE).
    function bestValueNode(ts, anchor, vboxes, scale) {
      var ar = anchor.getBoundingClientRect();
      var best = null, bestTier = 9, bestDx = 1e9;
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        if (el === anchor) continue;
        var vcs;
        try { vcs = window.getComputedStyle(el); } catch (e) { continue; }
        // A pending Input shows the (empty) scratchpad as an EMPTY node — still
        // the field's value slot, and still the commit target (see visTokens).
        if (!textOfEl(el) && !isPendingFill(vcs.fill)) continue;
        if (vcs.visibility !== 'visible') continue;
        if (colorOf(el) === 'gray') continue;              // rule 1: labels aren't values
        var r = el.getBoundingClientRect();
        var dy = r.top - ar.top;
        var below = dy >= 10 * scale && dy <= 60 * scale;
        var inl = Math.abs(dy) <= 8 * scale && r.left > ar.left;
        if (!below && !inl) continue;
        var dx = Math.abs(r.left - ar.left);
        var shared = false, inlineBoxed = false;
        for (var b = 0; b < vboxes.length; b++) {
          var vb = vboxes[b];
          var cy = r.top + r.height / 2;
          if (!(r.left >= vb.left - 6 && r.left <= vb.right + 6
                && cy >= vb.top - 8 && cy <= vb.bottom + 8)) continue;
          if (ar.left >= vb.left - 6 && ar.left <= vb.right + 6) shared = true;
          // 180, not 130: the FUEL page's NUMBER OF PAX box starts 162 units
          // right of its label (bundle Fuel.tsx: label x=8, input x=170), the
          // DEPARTURES dialog's OTHER AIRPORT box 172.
          if (vb.left >= ar.left - 6 && vb.left - ar.left <= 180 * scale) inlineBoxed = true;
        }
        var tier = 9;
        if (inl && inlineBoxed) tier = 0;                  // rule 2: the label's own row
        else if (below && (dx < 45 * scale || shared)) tier = 1;
        if (tier === 9) continue;
        if (tier < bestTier || (tier === bestTier && dx < bestDx)) {
          bestTier = tier; bestDx = dx; best = el;
        }
      }
      return best;
    }

    // Click the VALUE node of an FMS field: the nth visible text equal to `label`,
    // then its value node per bestValueNode above. The scratchpad COMMIT gesture.
    // Dialog-scoped field click. MUST be used while a dialog is open instead of
    // clickFmsField: that one searches the whole window, so (a) its occurrence
    // count differs from the dialog-scoped row list the pilot is choosing from,
    // and (b) its value hunt (dy 10-60, dx<45 from the label) can land on a PAGE
    // token showing through beneath the floating dialog — the same trap that
    // polluted the dialog's rows, but on the WRITE side, where it would type a
    // course into the wrong place instead of merely reading oddly.
    A.clickDialogField = function (label, occ) {
      var d = findDialog();
      if (!d) return 'NO_DIALOG';
      var want = norm(label);
      var n = occ || 0;
      var ts = d.g.querySelectorAll('text');
      var i, el, vis, anchor = null;
      for (i = 0; i < ts.length; i++) {
        el = ts[i];
        if (textOfEl(el) !== want) continue;
        try { vis = window.getComputedStyle(el).visibility; } catch (e) { continue; }
        if (vis !== 'visible') continue;
        // LABELS only, counted like ParseFms counts them (gray tokens). A value
        // can carry the same text as a label — the CROSSING dialog's altitude
        // chooser shows "AT" right above the "AT" label of the altitude box —
        // and anchoring on the chooser's face found no value and answered
        // NO_VALUE, so a plain AT crossing altitude could never be set (live
        // 2026-09-24).
        if (colorOf(el) !== 'gray') continue;
        if (n-- > 0) continue;
        anchor = el;
        break;
      }
      if (!anchor) return 'NOT_FOUND';
      // Dialog-scoped value boxes, for the same pairing rules as clickFmsField:
      // value INLINE in a box to the label's right ("CRS [255]°" — the Direct-To
      // dialog's intercept-course entry, live 2026-07-30), else BELOW it.
      var vboxes = [];
      var rects = d.g.querySelectorAll('rect');
      for (i = 0; i < rects.length; i++) {
        var rcs;
        try { rcs = window.getComputedStyle(rects[i]); } catch (e3) { continue; }
        if (rcs.visibility !== 'visible') continue;
        var stroke = rcs.stroke || '';
        if (!stroke || stroke === 'none') continue;
        var rr = rects[i].getBoundingClientRect();
        if (rr.width < 20 * d.scale || rr.width > 500 * d.scale) continue;
        if (rr.height < 12 * d.scale || rr.height > 60 * d.scale) continue;
        vboxes.push(rr);
      }
      var best = bestValueNode(ts, anchor, vboxes, d.scale);
      if (!best) return 'NO_VALUE';
      var br = best.getBoundingClientRect();
      return fireClick(br.left + br.width / 2, br.top + br.height / 2, best.parentNode || best)
        ? ('CLICKED:' + textOfEl(best)) : 'NO_TARGET';
    };
    // Dialog-scoped TEXT click, for dialog rows whose label also exists on the
    // page beneath (the Direct-To dialog lists the SAME idents as the legs list
    // it floats over — a window-wide click's occurrence count can land on the
    // page copy). Matches are ordered by (y, x) — reading order — to agree with
    // the C# renderer's occurrence numbering, NOT DOM order.
    A.clickDialogText = function (needle, occ) {
      var d = findDialog();
      if (!d) return 'NO_DIALOG';
      var want = norm(needle);
      var ts = d.g.querySelectorAll('text');
      var hits = [];
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        if (textOfEl(el) !== want) continue;
        var vis;
        try { vis = window.getComputedStyle(el).visibility; } catch (e) { continue; }
        if (vis !== 'visible') continue;
        var r = el.getBoundingClientRect();
        hits.push({ el: el, r: r });
      }
      hits.sort(function (a, b) { return (a.r.top - b.r.top) || (a.r.left - b.r.left); });
      var n = occ || 0;
      if (n < 0 || n >= hits.length) return 'NOT_FOUND';
      var hr = hits[n].r;
      return fireClick(hr.left + hr.width / 2, hr.top + hr.height / 2,
                       hits[n].el.parentNode || hits[n].el) ? 'CLICKED' : 'NO_TARGET';
    };
    // The Direct-To dialog's typed-waypoint entry: the ONE row whose left arrow
    // glyph is GRAY (every plan-leg row's arrow is white/colored). Found
    // structurally, no occurrence math: gray "→" below the header band with a
    // value box to its right within 130 units; clicks the box's value node —
    // the scratchpad-commit gesture (ee.directToFix).
    A.clickDirectToEntry = function () {
      var d = findDialog();
      if (!d) return 'NO_DIALOG';
      var ts = d.g.querySelectorAll('text');
      var i, el;
      for (i = 0; i < ts.length; i++) {
        el = ts[i];
        if (textOfEl(el) !== '→') continue;
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        if ((cs.fill || '').indexOf('rgb(132, 132, 132)') < 0) continue;   // gray only
        var ar = el.getBoundingClientRect();
        if (ar.top - d.rect.top < 60 * d.scale) continue;                  // the header glyph
        // the value node: a text inside a box starting right of the arrow
        var rects = d.g.querySelectorAll('rect');
        for (var b = 0; b < rects.length; b++) {
          var rcs;
          try { rcs = window.getComputedStyle(rects[b]); } catch (e2) { continue; }
          if (rcs.visibility !== 'visible') continue;
          if (!rcs.stroke || rcs.stroke === 'none') continue;
          var vb = rects[b].getBoundingClientRect();
          if (vb.left < ar.left || vb.left - ar.left > 130 * d.scale) continue;
          // The entry box sits BELOW-right of its gray arrow, not beside it
          // (bundle DirectTo.tsx: arrow text y=15, Input y=42 — box centre
          // ~43 units lower; live 2026-09-24). A same-row test never matched,
          // so "direct to a typed waypoint" was unreachable.
          var bdy = (vb.top + vb.height / 2) - (ar.top + ar.height / 2);
          if (bdy < -20 * d.scale || bdy > 70 * d.scale) continue;
          return fireClick(vb.left + vb.width / 2, vb.top + vb.height / 2, rects[b].parentNode)
            ? 'CLICKED' : 'NO_TARGET';
        }
      }
      return 'NOT_FOUND';
    };
    // ---- paged dialog lists (the Fusion scroll bar, bundle `Ur`) -------------
    // Some dialogs PAGINATE rather than scroll: the Direct-To dialog renders
    // exactly five slots (the typed-entry row + four legs) and swaps their
    // content as its scroll bar's position changes, so every leg past the
    // fourth is simply NOT in the DOM until the list is paged — a pilot on a
    // real route could only ever go direct to the first four legs. The scroll
    // bar component carries position/setPosition/childCount; calling
    // setPosition is exactly what its own up/down arrow buttons do.
    function scrollersIn(root) {
      var out = [];
      if (!root) return out;
      var els = root.querySelectorAll('g,rect');
      for (var i = 0; i < els.length; i++) {
        var f = fiberOf(els[i]), n = 0;
        while (f && n < 3) {
          var p = f.memoizedProps;
          if (p && typeof p.setPosition === 'function' && typeof p.position === 'number'
              && typeof p.childCount === 'number') {
            var dup = false;
            for (var k = 0; k < out.length; k++) if (out[k].p.setPosition === p.setPosition) { dup = true; break; }
            if (!dup) {
              var ch = p.childHeight || p.childWidth || 1;
              var len = p.childHeight ? (p.height || 44) : (p.width || 44);
              var pages = p.pages != null ? p.pages
                : Math.ceil((p.childCount * ch) / Math.max(ch, Math.round(len / ch) * ch));
              out.push({ p: p, pos: p.position, pages: pages });
            }
            break;
          }
          f = f.return; n++;
        }
      }
      return out;
    }
    // {pos, pages} of the open dialog's paged list(s), [] when it has none.
    A.dialogPages = function () {
      var d = findDialog();
      var s = d ? scrollersIn(d.g) : [];
      var out = [];
      for (var i = 0; i < s.length; i++) out.push({ pos: s[i].pos, pages: s[i].pages });
      return JSON.stringify(out);
    };
    // Page every paged list in the open dialog by `dir` (+1 / -1), clamped.
    // Returns "PAGED|pos|pages" (first list), "EDGE" when already at that end,
    // "NONE" when the dialog has no paged list.
    A.dialogPage = function (dir) {
      var d = findDialog();
      if (!d) return 'NO_DIALOG';
      var s = scrollersIn(d.g);
      if (!s.length) return 'NONE';
      var moved = false, first = null;
      for (var i = 0; i < s.length; i++) {
        var to = Math.max(0, Math.min(s[i].pages - 1, s[i].pos + dir));
        if (!first) first = { pos: to, pages: s[i].pages };
        if (to === s[i].pos) continue;
        try { s[i].p.setPosition(to); moved = true; } catch (e) { }
      }
      return moved ? ('PAGED|' + first.pos + '|' + first.pages) : 'EDGE';
    };

    // The FMS page's OWN paged list (ROUTE ▸ LEGS today: `fse` renders eight
    // rows per page through the same `Ur` scroll bar). The MKP PREV/NEXT and
    // UP/DOWN keys do NOT page it — probed live 2026-09-24, neither moved the
    // LEGS list, nor did a wheel event — so a pilot on a real route could only
    // ever read the first page. Scroll bars inside an open dialog belong to the
    // dialog (dialogPage) and are excluded. Same return shape as dialogPage.
    function fmsScrollers() {
      var w = winOf('fms');
      if (!w) return null;
      var d = findDialog();
      var s = scrollersIn(w), out = [];
      for (var i = 0; i < s.length; i++) {
        var inDialog = false;
        if (d && d.g) {
          var dl = scrollersIn(d.g);
          for (var k = 0; k < dl.length; k++) if (dl[k].p.setPosition === s[i].p.setPosition) inDialog = true;
        }
        if (!inDialog) out.push(s[i]);
      }
      return out;
    }
    A.fmsListPages = function () {
      var s = fmsScrollers() || [];
      return s.length ? JSON.stringify({ pos: s[0].pos, pages: s[0].pages }) : '';
    };
    A.fmsListPage = function (dir) {
      var s = fmsScrollers();
      if (!s) return 'NO_WINDOW';
      if (!s.length) return 'NONE';
      var p = s[0];
      var to = Math.max(0, Math.min(p.pages - 1, p.pos + dir));
      if (to === p.pos) return 'EDGE';
      try { p.p.setPosition(to); } catch (e) { return 'CALL_ERR'; }
      return 'PAGED|' + to + '|' + p.pages;
    };

    // Click the value node of a field that has NO label of its own (the FUEL
    // page's contingency-percent box, beside the reserve box it belongs to) by
    // the window-relative position fms() reported for it. Same click as a field
    // commit, just addressed by the token the form rendered.
    A.clickFmsAt = function (x, y) {
      var w = winOf('fms');
      if (!w) return 'NO_WINDOW';
      var wr = w.getBoundingClientRect();
      var ts = w.querySelectorAll('text');
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i], cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        if (colorOf(el) === 'gray') continue;
        var r = el.getBoundingClientRect();
        // an empty pending node is REPORTED at its text position (see visTokens)
        var sc = wr.width / 740 || 1, empty = !textOfEl(el);
        var ex = Math.round(r.left - wr.left) + (empty ? Math.round(7 * sc) : 0);
        var ey = Math.round(r.top - wr.top) + (empty ? Math.round(5 * sc) : 0);
        if (Math.abs(ex - x) > 3 || Math.abs(ey - y) > 3) continue;
        return fireClick(r.left + r.width / 2, r.top + r.height / 2, el.parentNode || el)
          ? 'CLICKED' : 'NO_TARGET';
      }
      return 'NOT_FOUND';
    };

    // The speed/altitude CONSTRAINT of a LEGS row ("↑250/4000A", "/-----") is its
    // own scratchpad Input (bundle: props onSubmit + transition), exactly like
    // the real FMS: type "/9000A" and click it. This is the ONLY way to set a
    // crossing restriction on a terminal waypoint — the aircraft opens no
    // revision menu (so no CROSSING…) for those (live 2026-09-24, EGNT NTS08).
    // Addressed by the aircraft's legIdx like every other legs action. The
    // CALLER must have text on the scratchpad: an empty one COPIES the value out.
    A.clickLegConstraintAt = function (legIdx) {
      var w = winOf('fms');
      if (!w) return 'NO_WINDOW';
      var ts = w.querySelectorAll('text');
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i], cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        if (parseFloat(cs.opacity || '1') === 0) continue;
        var f = fiberOf(el), n = 0, isCons = false;
        while (f && n < 10) {
          var p = f.memoizedProps;
          if (p && typeof p.onSubmit === 'function' && ('transition' in p)) { isCons = true; break; }
          f = f.return; n++;
        }
        if (!isCons) continue;
        if (legInfoOf(el).idx !== legIdx) continue;
        var r = el.getBoundingClientRect();
        return fireClick(r.left + r.width / 2, r.top + r.height / 2, el.parentNode || el)
          ? 'CLICKED' : 'NO_TARGET';
      }
      return 'NOT_FOUND';
    };

    // The Direct-To dialog's per-leg crossing altitude (the green box right of
    // VERT →) is an INPUT: typing an altitude there sets an AT constraint on
    // that leg (bundle: onSubmit → setLegAttributes(idx, {altitude: At})), and
    // it is what ENABLES VERT → on a leg that had none. `vertOcc` counts every
    // "VERT →" in reading order, enabled or not — ParseDirectTo's VertOcc.
    A.clickDirectToVertAlt = function (vertOcc) {
      var d = findDialog();
      if (!d) return 'NO_DIALOG';
      var ts = d.g.querySelectorAll('text');
      var verts = [], all = [];
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i], cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible') continue;
        var r = el.getBoundingClientRect();
        var t = textOfEl(el);
        if (t === 'VERT →' || t === 'VERT →') verts.push(r);
        else if (colorOf(el) !== 'gray') all.push({ el: el, r: r });
      }
      verts.sort(function (a, b) { return (a.top - b.top) || (a.left - b.left); });
      var v = verts[vertOcc || 0];
      if (!v) return 'NOT_FOUND';
      var vc = v.top + v.height / 2;
      for (var k = 0; k < all.length; k++) {
        var ar = all[k].r;
        if (Math.abs((ar.top + ar.height / 2) - vc) > 8 * d.scale) continue;
        if (ar.left <= v.left || ar.left - v.left > 150 * d.scale) continue;
        return fireClick(ar.left + ar.width / 2, ar.top + ar.height / 2, all[k].el.parentNode || all[k].el)
          ? 'CLICKED' : 'NO_TARGET';
      }
      return 'NO_VALUE';
    };

    A.clickFmsField = function (label, occ) {
      var w = winOf('fms');
      if (!w) return 'NO_WINDOW';
      var want = norm(label);
      var n = occ || 0;
      var ts = w.querySelectorAll('text');
      var anchor = null;
      var i, el, t, vis;
      for (i = 0; i < ts.length; i++) {
        el = ts[i];
        t = norm(el.textContent);
        if (t !== want) continue;
        try { vis = window.getComputedStyle(el).visibility; } catch (e) { continue; }
        if (vis !== 'visible') continue;
        if (colorOf(el) !== 'gray') continue;   // labels only — see clickDialogField
        if (n-- > 0) continue;
        anchor = el;
        break;
      }
      if (!anchor) return 'NOT_FOUND';
      var wscale = w.getBoundingClientRect().width / 740;
      if (!(wscale > 0)) wscale = 1;
      // Value-box frames — the stroked entry-field rects. Bounds are in CANVAS
      // units (scaled), not raw pixels: on a scaled-up view an unscaled 500 px
      // ceiling silently dropped the wider boxes and their fields became
      // unclickable. See bestValueNode for how a label is paired to one.
      var vboxes = [];
      var rects = w.querySelectorAll('rect');
      for (i = 0; i < rects.length; i++) {
        var rcs;
        try { rcs = window.getComputedStyle(rects[i]); } catch (e3) { continue; }
        if (rcs.visibility !== 'visible') continue;
        var stroke = rcs.stroke || '';
        if (!stroke || stroke === 'none') continue;
        var rr = rects[i].getBoundingClientRect();
        if (rr.width < 20 * wscale || rr.width > 500 * wscale) continue;
        if (rr.height < 12 * wscale || rr.height > 60 * wscale) continue;
        vboxes.push(rr);
      }
      var best = bestValueNode(ts, anchor, vboxes, wscale);
      if (!best) return 'NO_VALUE';
      var br = best.getBoundingClientRect();
      return fireClick(br.left + br.width / 2, br.top + br.height / 2, best.parentNode || best)
        ? ('CLICKED:' + textOfEl(best)) : 'NO_TARGET';
    };

    // ---- Navigraph navdata load (MFW Maintenance "DATALOAD" format) ----------
    // The aircraft's working Navigraph updater lives HERE, not in the EFB (whose
    // UPDATE NAVDATA button calls a WASM callback v1.0.9 never answers). MFW
    // format 8 = DATALOAD (6 CHART, 7 MAINTENANCE — the MFW Menu.tsx constants);
    // formats are chosen from a CCP cursor menu with no CTP key, so we dispatch
    // the display's own redux action (slice "duState", reducer setMfwFormat) on
    // the CAPTAIN's upper MFW (dus index 2, its left/right partition). Live
    // 2026-09-24: this route loaded Navigraph_v2_2609_03SEP26 into FMS1+FMS2.
    function duStore() {
      if (A._duStore) return A._duStore;
      var root = document.getElementById('MSFS_REACT_MOUNT');
      var svg = root && root.querySelector('svg');
      if (!svg) return null;
      var key = null;
      for (var k in svg) { if (k.indexOf('__reactFiber') === 0 || k.indexOf('__reactInternalInstance') === 0) { key = k; break; } }
      if (!key) return null;
      var f = svg[key], n = 0;
      while (f && n < 250) {
        var p = f.memoizedProps;
        if (p && p.store && p.store.dispatch && p.store.getState) { A._duStore = p.store; return p.store; }
        if (p && p.value && p.value.store && p.value.store.dispatch) { A._duStore = p.value.store; return p.value.store; }
        f = f['return']; n++;
      }
      return null;
    }

    function captainMfw() {
      var s = duStore();
      if (!s) return null;
      var st = s.getState();
      var part = st.dus && st.dus.leftPartition;
      if (!part || !st.dus[2] || !st.dus[2][part]) return null;
      return { store: s, part: part, fmt: st.dus[2][part].mfw };
    }

    A.setCaptainMfwFormat = function (format) {
      var c = captainMfw();
      if (!c) return 'NO_STORE';
      c.store.dispatch({ type: 'duState/setMfwFormat', payload: { index: 2, partition: c.part, format: format } });
      var after = captainMfw();
      return after && after.fmt === format ? 'OK' : 'NOT_APPLIED';
    };

    function visibleTexts(scope) {
      var out = [];
      var ts = (scope || document).querySelectorAll('text');
      for (var i = 0; i < ts.length; i++) {
        var el = ts[i];
        var t = norm(el.textContent);
        if (!t) continue;
        var cs;
        try { cs = window.getComputedStyle(el); } catch (e) { continue; }
        if (cs.visibility !== 'visible' || cs.display === 'none') continue;
        var r = el.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue;
        out.push({ el: el, t: t, x: r.left, y: r.top, w: r.width, h: r.height });
      }
      return out;
    }

    // The captain's MFW window <g> showing the data-load format (or its menu).
    function dataloadWin() {
      var root = document.getElementById('MSFS_REACT_MOUNT');
      if (!root) return null;
      var gs = root.querySelectorAll('svg > g');
      for (var i = 0; i < gs.length; i++) {
        var tc = gs[i].textContent || '';
        if (tc.indexOf('Load New Databases') >= 0 || tc.indexOf('Maintenance Data Load Menu') >= 0) return gs[i];
      }
      return null;
    }

    function fiberPropsWith(el, prop) {
      var key = null;
      for (var k in el) { if (k.indexOf('__reactFiber') === 0 || k.indexOf('__reactInternalInstance') === 0) { key = k; break; } }
      if (!key) return null;
      var f = el[key], n = 0;
      while (f && n < 12) {
        var p = f.memoizedProps;
        if (p && typeof p === 'object' && prop in p) return p;
        f = f['return']; n++;
      }
      return null;
    }

    // Navigraph device-flow dialog ("Navigraph Authentication Required"), shown by
    // the MFW whenever a Navigraph-backed function is opened without a sign-in.
    // The code is the one 8-character token below "the following code:".
    function navigraphAuth() {
      var all = visibleTexts(document);
      var hdr = null;
      for (var i = 0; i < all.length; i++) if (all[i].t === 'Navigraph Authentication Required') { hdr = all[i]; break; }
      if (!hdr) return null;
      var code = null;
      for (var j = 0; j < all.length; j++) {
        var a = all[j];
        if (a.y > hdr.y && Math.abs(a.x - hdr.x) < 700 && /^[A-Z0-9]{6,10}$/.test(a.t)) { code = a.t; break; }
      }
      return { code: code, url: 'https://navigraph.com/code' };
    }

    A.dataload = function () {
      var c = captainMfw();
      var res = { ok: true, fmt: c ? c.fmt : -1, auth: navigraphAuth(), page: 'none', rows: [],
                  canStart: false, progress: null, complete: null };
      var w = dataloadWin();
      if (!w) return JSON.stringify(res);
      var ts = visibleTexts(w);
      var txt = ts.map(function (a) { return a.t; });
      if (txt.indexOf('Load New Databases') >= 0) res.page = 'databases';
      else if (txt.indexOf('Maintenance Data Load Menu') >= 0) res.page = 'menu';
      if (res.page === 'databases') {
        for (var i = 0; i < ts.length; i++) {
          var p = fiberPropsWith(ts[i].el, 'selected');
          if (!p || typeof p.name !== 'string' || p.name !== ts[i].t || !('status' in p)) continue;
          res.rows.push({ name: p.name, selected: !!p.selected, disabled: !!p.disabled, status: p.status || '' });
        }
        for (var k = 0; k < ts.length; k++) {
          if (ts[k].t === 'Start Load') {
            var sp = fiberPropsWith(ts[k].el, 'disabled');
            res.canStart = !(sp && sp.disabled);
          }
          if (/^\d{1,3}%$/.test(ts[k].t) && res.progress === null) res.progress = parseInt(ts[k].t, 10);
          if (ts[k].t === 'Load Complete') res.complete = 'ok';
          if (ts[k].t === 'Load Complete with Errors') res.complete = 'errors';
        }
      }
      return JSON.stringify(res);
    };

    // Click a label in the data-load window (tab "LOAD NEW", a database row,
    // "Start Load") or the Navigraph sign-in dialog ("Open in Browser", "Cancel").
    A.dataloadClick = function (needle) {
      var want = norm(needle);
      var w = dataloadWin();
      var pools = [w ? visibleTexts(w) : [], visibleTexts(document)];
      for (var p = 0; p < pools.length; p++) {
        var ts = pools[p];
        for (var i = 0; i < ts.length; i++) {
          if (ts[i].t !== want && ts[i].t.indexOf(want) !== 0) continue;
          var a = ts[i];
          return fireClick(a.x + a.w / 2, a.y + a.h / 2, a.el.parentNode || a.el) ? ('CLICKED:' + a.t) : 'NO_TARGET';
        }
      }
      return 'NOT_FOUND';
    };

    window.__a220Displays = A;
    return 'MSFSBA_A220_DISPLAYS_INSTALLED';
  } catch (e) {
    return 'MSFSBA_A220_DISPLAYS_ERROR:' + (e && e.message);
  }
})()
