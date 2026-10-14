// iniBuilds A300 tablet in-page agent — installed at runtime into the tablet's Coherent GT view
// ("VCockpit18 - iniEfbA300") as window.__MSFSBA_A300_EFB. The tablet is a Bootstrap-styled page
// with a real DOM: buttons, inputs, Bootstrap radio groups (input.btn-check + label.btn), range
// inputs, headings and text. States a sighted pilot reads from color are put into words here:
// a door's open/moving/closed (its classes: door-open, door-transit) and a slide's armed/disarmed
// (its background: red armed, green disarmed, per the page's own legend).
//
// ES5 ONLY. Coherent GT is Chromium 49: var (no const/let), no arrow functions, no template
// literals, no String.includes / Array.from / Object.assign. Element.closest exists there.
//
// Contract, the same as the PMDG and MD-11 agents so the shared FbwEfbForm consumes it:
//   scrape()            -> JSON string {ok, page, elements:[...]}
//   clickElement(idx)   -> press the element carrying that stamped idx
//   setValue(idx, text) -> type into a field, pick a radio option, or move a slider; false when
//                          nothing was written
// Element: {idx,text,value,controlType,kind,clickable,level,disabled,options,min,max,step,key,announceChange}
//
// A press is ONE click: pointerdown, mousedown, pointerup, mouseup, click. The tablet ignores a bare
// el.click() (measured: its unlock button did nothing), and two click events would run a toggle twice.
(function () {
  var A = {};
  A.INSTALLED = 'MSFSBA_A300_EFB_INSTALLED';
  A.ATTR = 'data-a300-efb-idx';

  // The page's root element id → its name.
  A.PAGES = {
    lockscreen: 'Locked', dashboard: 'Home', flight: 'My Flight', equip: 'Ground Equipment',
    weights: 'Weight and Balance', takeOffPerfViewer: 'Take Off', landingPerfViewer: 'Landing',
    maintenance: 'Aircraft Maintenance', throttlecalibration: 'Throttle Calibration', settings: 'Settings'
  };

  // Icon-only controls, and buttons whose own words read badly, by id.
  A.NAMES = {
    'menu-home-button': 'Home', 'control-box-button': 'Control box',
    refreshMetar: 'Refresh METAR', pb_left: 'Pushback, turn left', pb_right: 'Pushback, turn right',
    pb_stop: 'Pushback, stop', pb_aft: 'Pushback, straight back',
    switcher_page1: 'Back to payload selection', switcher_page2: 'Loading page',
    payload_cargo: 'Custom cargo', payload_cargo_sb: 'Cargo from SimBrief',
    hide_runway_select: 'Cancel runway selection', land_hide_runway_select: 'Cancel runway selection',
    close_checklist_viewer: 'Close checklist', close_ofp_viewer: 'Close flight plan',
    // Charts: icon-only buttons.
    filter_pinned: 'Pinned charts', ctrlZoomIn: 'Zoom in', ctrlZoomOut: 'Zoom out', ctrlReset: 'Reset chart view',
    ctrlSidebar: 'Chart list', ctrlDarkLight: 'Day or night chart', ctrlPanUp: 'Pan up', ctrlPanLeft: 'Pan left',
    ctrlPanRight: 'Pan right', ctrlPanDown: 'Pan down'
  };

  // Pages whose root is a class on the page container rather than an id.
  A.PAGE_CLASSES = { termcharts: 'Charts', 'enroute-map': 'Enroute Map' };
  // A line opening a page that is a picture for a sighted pilot.
  A.PAGE_NOTES = {
    termcharts: 'Charts are pictures and cannot be read here; the list gives their names.',
    'enroute-map': 'The map is a picture and cannot be read here.'
  };

  // Aircraft Maintenance's panel and cowl toggles: their buttons carry no state, so it is read from
  // the variable each one toggles (the tablet's own handlers: 1, or above 0.5, is open).
  A.MAINT_PANELS = {
    fuel_panel: 'L:INI_Fuel_Panel', maint_eng_cowl_l: 'L:INI_Engine_Cowl_Left',
    maint_eng_cowl_r: 'L:INI_Engine_Cowl_Right', maint_apu_cowl: 'L:INI_APU_Cowl_TGT'
  };
  // Its component state: oil and hydraulic levels, a label over a bar over a value.
  A.LEVELS = {
    APU_OIL: 'APU oil', ENG1_OIL: 'Engine 1 oil', ENG2_OIL: 'Engine 2 oil',
    HYD_BLU: 'Blue hydraulic reservoir', HYD_GRE: 'Green hydraulic reservoir', HYD_YEL: 'Yellow hydraulic reservoir'
  };
  // And its wear, which the tablet shows only as colored blocks on a drawing of the gear. The
  // colors are five bands of each variable (its updateMaintIndicators, read 2026-10-04): a brake
  // from 0 to 1, a tire from 0 to 200, green under a fifth, then dark green, yellow, orange, and
  // red up to the full scale; black beyond it.
  A.BRAKES = [
    ['L:INI_Brake_Wear_Indicator_LFI', 'left forward inner'], ['L:INI_Brake_Wear_Indicator_LFO', 'left forward outer'],
    ['L:INI_Brake_Wear_Indicator_LRI', 'left rear inner'], ['L:INI_Brake_Wear_Indicator_LRO', 'left rear outer'],
    ['L:INI_Brake_Wear_Indicator_RFI', 'right forward inner'], ['L:INI_Brake_Wear_Indicator_RFO', 'right forward outer'],
    ['L:INI_Brake_Wear_Indicator_RRI', 'right rear inner'], ['L:INI_Brake_Wear_Indicator_RRO', 'right rear outer']
  ];
  A.TYRES = [
    ['L:INI_TIRE0_NG_WEAR', 'nose left'], ['L:INI_TIRE1_NG_WEAR', 'nose right'],
    ['L:INI_TIRE0_WEAR', 'left rear inner'], ['L:INI_TIRE1_WEAR', 'left rear outer'],
    ['L:INI_TIRE2_WEAR', 'right rear outer'], ['L:INI_TIRE3_WEAR', 'right rear inner'],
    ['L:INI_TIRE4_WEAR', 'right forward outer'], ['L:INI_TIRE5_WEAR', 'right forward inner'],
    ['L:INI_TIRE6_WEAR', 'left forward inner'], ['L:INI_TIRE7_WEAR', 'left forward outer']
  ];
  A.WEAR_BANDS = ['under 20 percent', '20 to 40 percent', '40 to 60 percent', '60 to 80 percent', '80 to 100 percent', 'beyond the scale'];

  // WEIGHT AND BALANCE. Its two halves slide by 100vw: payload selection (#page1: preset loads)
  // and loading (#page2: the hold weights, the fuel, Update and Apply, and the results). Read from
  // the tablet's own code (2026-10-04): a preset fills the three hold weights with a third of its
  // load each and slides to the loading half; Update from SimBrief fills the holds (a third of the
  // plan's payload each) and the fuel (the plan's ramp fuel); only Apply Load to Aircraft puts
  // them on board. The fuel field is the TOTAL fuel to have on board. Unload Cargo only hides the
  // cargo models (the INI_LOAD_*_SHOW visuals); the weight stays on board.
  A.WEIGHTS_HALVES = { page1: 'payload selection', page2: 'loading' };
  A.WEIGHTS_HELP = {
    page1: 'Choosing a load fills in the hold weights and opens the loading page. Nothing goes on board until you press Apply Load to Aircraft there. ' +
      'Unload Cargo only removes the cargo you can see in the hold; to take its weight off, set the holds to 0 on the loading page and apply.',
    page2: 'Update from SimBrief fills in the cargo and fuel from your latest SimBrief plan. Apply Load to Aircraft puts the cargo and fuel on board.'
  };
  A.FIELD_NAMES = {
    weight_fuel: 'Total fuel', weight_freight_fwd: 'Forward hold', weight_freight_mid: 'Middle hold',
    weight_freight_aft: 'Aft hold', input_pax: 'Passengers',
    // Settings: the tablet fetches xml.fetcher.php?userid=, so this box takes the numeric SimBrief
    // Pilot ID, never the user name; its only label is the heading "SimBrief".
    simbrief: 'SimBrief Pilot ID, numbers only',
    ng_search: 'Airport'
  };
  // The cargo field's label column says LOAD (CARGO on the passenger version).
  A.FIELD_TITLES = { LOAD: 'Total cargo', CARGO: 'Total cargo' };
  // What a stored code adds to a password box's name ("Hoppie, code set"). It is the box's state, not
  // part of the name that a heading above the box echoes (A.dropEchoHeadings).
  A.CODE_SET = ', code set';
  // The results: read-only boxes, printed in thousands of the tablet's weight unit or in % MAC.
  A.READOUTS = {
    output_maczfw: ['Zero fuel weight center of gravity', 'mac'], output_maccg: ['Gross weight center of gravity', 'mac'],
    output_zfw: ['Zero fuel weight', 'weight'], output_tow: ['Gross weight', 'weight'],
    output_payload: ['Payload', 'weight'], output_blkfuel: ['Block fuel', 'weight']
  };
  // A preset's two lines: "31'000Kgs / 68'300Lbs", then its name.
  A.PRESET_WEIGHT = /^([\d',.]+)\s*kgs?\s*\/\s*([\d',.]+)\s*lbs?$/i;

  // My Flight's values, which sit under headings ("METAR", "DEP TIME") or under nothing at all.
  A.VALUE_NAMES = {
    dep_icao: 'Departure', depcit: 'Departure city', arr_icao: 'Arrival', arrcit: 'Arrival city',
    depmet: 'Departure METAR', arrmet: 'Arrival METAR', deptime: 'Departure time', arrtime: 'Arrival time',
    route: 'Route'
  };

  A.visible = function (el) {
    if (!el || el.nodeType !== 1) return false;
    try {
      var cs = window.getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden') return false;
      var r = el.getBoundingClientRect();
      // Slid off the side: Weight and Balance moves its other half 100vw away, still laid out.
      var vw = window.innerWidth || 0;
      if (vw > 0 && r.width > 0 && (r.right <= 0 || r.left >= vw)) return false;
      if (r.width > 0 && r.height > 0) return true;
      // A zero-size container can still hold visible children: the page container is 2048 x 0,
      // its pages being positioned absolutely inside it.
      return el.children.length > 0 && el.tagName !== 'BUTTON' && el.tagName !== 'INPUT';
    } catch (e) { return false; }
  };

  A.clean = function (s) { return String(s == null ? '' : s).replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, ''); };
  // "31'000" → 31000; 31000 → "31,000" (the tablet's own separator is an apostrophe).
  A.num = function (s) { return parseFloat(String(s).replace(/[',]/g, '')); };
  A.thousands = function (n) { return String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ','); };
  // "000.0" → "0.0", "060.11" → "60.11".
  A.plainNumber = function (s) { return A.clean(s).replace(/^(-?)0+(?=\d)/, '$1'); };
  A.sentence = function (s) { s = A.clean(s).toLowerCase(); return s ? s.charAt(0).toUpperCase() + s.slice(1) : ''; };
  A.txt = function (el) { return el ? A.clean(el.innerText || el.textContent || '') : ''; };
  A.ownText = function (el) {
    var s = '';
    for (var i = 0; i < el.childNodes.length; i++) if (el.childNodes[i].nodeType === 3) s += el.childNodes[i].textContent;
    return A.clean(s);
  };
  A.hasClass = function (el, c) {
    return !!el && typeof el.className === 'string' && (' ' + el.className + ' ').indexOf(' ' + c + ' ') >= 0;
  };

  // "toggleStairsL" / "doors_l1" → "Toggle stairs l" — the last resort for a nameless control.
  A.idWords = function (id) {
    var s = String(id || '').replace(/[_-]+/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2');
    s = A.clean(s).toLowerCase();
    return s ? s.charAt(0).toUpperCase() + s.slice(1) : '';
  };

  // Shown on screen: el and every element above it are displayed. A.visible looks at el alone,
  // which is enough while walking down from the visible page; an overlay found by id is not
  // walked to, and a page the tablet has hidden leaves its viewers' own display untouched.
  A.shown = function (el) {
    for (var n = el; n && n.nodeType === 1 && n !== document.body; n = n.parentElement) {
      try {
        var cs = window.getComputedStyle(n);
        if (cs.display === 'none' || cs.visibility === 'hidden') return false;
      } catch (e) { return false; }
    }
    return !!el;
  };

  A.listWords = function (names) {
    if (names.length < 2) return names.join('');
    return names.slice(0, -1).join(', ') + ' and ' + names[names.length - 1];
  };

  // One line for a set of wear indicators: "all 8 under 20 percent", or the most common band as a
  // count and the other wheels by name. '' when the simulator cannot be read.
  A.wearLine = function (title, list, full) {
    try {
      if (typeof SimVar === 'undefined' || !SimVar.GetSimVarValue) return '';
      var bands = [[], [], [], [], [], []], n = 0;
      for (var i = 0; i < list.length; i++) {
        var v = +SimVar.GetSimVarValue(list[i][0], 'number');
        if (!isFinite(v)) continue;
        var f = v / full;
        var b = f < 0.2 ? 0 : f < 0.4 ? 1 : f < 0.6 ? 2 : f < 0.8 ? 3 : f <= 1 ? 4 : 5;
        bands[b].push(list[i][1]);
        n++;
      }
      if (!n) return '';
      var most = 0;
      for (var m = 1; m < bands.length; m++) if (bands[m].length > bands[most].length) most = m;
      if (bands[most].length === n) return title + ': all ' + n + ' ' + A.WEAR_BANDS[most];
      var parts = [];
      for (var k = 0; k < bands.length; k++) {
        if (!bands[k].length) continue;
        parts.push((k === most ? bands[k].length : A.listWords(bands[k])) + ' ' + A.WEAR_BANDS[k]);
      }
      return title + ': ' + parts.join('; ');
    } catch (e) { return ''; }
  };

  // A Take Off or Landing runway button: "RWY 18L [3000m / 9843ft]", red when the tablet's tables
  // start above its length (1,700 m for take-off, 1,300 m for landing).
  A.runwayLabel = function (btn) {
    var m = /^RWY\s+(\S+)\s*\[\s*([\d.]+)\s*m\s*\/\s*([\d.]+)\s*ft\s*\]$/i.exec(A.txt(btn));
    if (!m) return '';
    var small = btn.querySelector('small');
    var red = !!small && /^rgb\(\s*255,\s*0,\s*0\s*\)$/.test(small.style.color || '');
    return 'Runway ' + m[1] + ': ' + A.thousands(+m[2]) + ' meters, ' + A.thousands(+m[3]) + ' feet' + (red ? ', marked short' : '');
  };

  // Take Off's and Landing's RWY button opens the airport's runway list ABOVE the page, away from
  // the button, so a screen reader heard nothing happen when it was pressed (PR #300 in-sim run,
  // 2026-10-14). It is read as a choice instead: the list's runways in words, the value the runway
  // the button names ("RWY 18L"). Each picker's own list, by its container: the tablet ids a runway
  // button by the runway alone, so both pages can hold a "09".
  A.RUNWAY_PICKERS = { show_runway_select: 'runway_opts', land_show_runway_select: 'land_runway_opts' };
  // The first choice until a runway is picked (the button says SELECT): a list with nothing chosen
  // would show its first runway as if it were.
  A.NO_RUNWAY = 'None selected';
  A.runwayButtons = function (picker) {
    var box = A.byId(A.RUNWAY_PICKERS[picker.id]);
    return box ? box.getElementsByTagName('button') : [];
  };
  // Emits the picker as a choice; false, leaving it the tablet's button, while no runway is listed.
  A.runwayPicker = function (picker, emit) {
    var m = /^RWY\s+(\S+)$/i.exec(A.clean(picker.value)), picked = m ? m[1] : '';
    var btns = A.runwayButtons(picker), opts = [], value = '';
    for (var i = 0; i < btns.length; i++) {
      var label = A.runwayLabel(btns[i]);
      if (!label) continue;
      opts.push(label);
      if (btns[i].id === picked) value = label;
    }
    if (!opts.length) return false;
    if (!value) { opts.unshift(A.NO_RUNWAY); value = A.NO_RUNWAY; }
    emit({ idx: A.stamp(picker), text: 'Runway', value: value, controlType: 'select', kind: 'static', clickable: false,
      options: opts, key: 'runway:' + picker.id });
    return true;
  };
  // A pick is what a finger does: RWY opens the list, then the runway's own button, whose handler
  // (selectRunway) closes the list and fills TORA, HDG and RWY. False, pressing nothing, for a
  // runway the list does not hold or for the placeholder.
  A.pickRunway = function (picker, text) {
    var btns = A.runwayButtons(picker);
    for (var i = 0; i < btns.length; i++) {
      if (A.runwayLabel(btns[i]) !== text) continue;
      A.press(picker);
      A.press(btns[i]);
      return true;
    }
    return false;
  };
  // The tablet's message after a pick: the choice already says the new runway, and a message read
  // as an alert would cut off the screen reader reading it.
  A.QUIET_TOASTS = { 'Runway Updated': true };

  // Landing's temperature, weight and extra speed are text boxes the tablet reads with parseInt: a
  // weight typed "99,934" was 99 kg, and a landing distance of 500 m where 99934 gave 1,200 m
  // (measured 2026-10-14). They take a typed number as a number box does (A.setValue).
  A.NUMBER_TEXT = { land_input_temp: true, land_input_weight: true, land_input_vls: true };

  // A chart in the Charts list: its name (h1) and its kind (p), "CONDR 4 RNAV [ATC], STAR".
  // A chart named after its kind ("AFC", "AOI") says it once.
  A.chartName = function (row) {
    var name = A.txt(row.querySelector('h1')), kind = A.txt(row.querySelector('p'));
    return name + (kind && kind !== name ? ', ' + kind : '');
  };

  // True when every element under el is inline text formatting, so el reads as one line.
  A.INLINE = { SPAN: 1, STRONG: 1, B: 1, I: 1, EM: 1, SMALL: 1, BR: 1, SUP: 1, SUB: 1, U: 1 };
  A.inlineOnly = function (el) {
    var all = el.getElementsByTagName('*');
    for (var i = 0; i < all.length; i++) if (!A.INLINE[all[i].tagName] || A.isPress(all[i])) return false;
    return true;
  };

  A.isPress = function (el) {
    var t = el.tagName;
    if (t === 'BUTTON') return true;
    if (t === 'INPUT' && (el.type === 'button' || el.type === 'submit')) return true;
    if (t === 'IMG' && el.id === 'menu-home-button') return true;
    if (t === 'DIV' && (el.id === 'control-box-button' || el.id === 'switcher_page1' || el.id === 'switcher_page2' ||
        A.hasClass(el, 'is-button') || A.hasClass(el, 'unlock-button') ||
        A.hasClass(el, 'chart-button') || A.hasClass(el, 'chart-pin-button'))) return true;
    return false;
  };

  // The label column of an input's row: "RWY", "LOAD", "FUEL", "Master" (volumes).
  A.rowLabel = function (input) {
    var row = input.closest('.row');
    for (var depth = 0; row && depth < 2; depth++) {
      var c = row.querySelector('.flight-value-title, .ini-input-label, p.text-end');
      if (c && !c.contains(input) && A.visible(c)) return A.clean(A.ownText(c) || A.txt(c));
      row = row.parentElement ? row.parentElement.closest('.row') : null;
    }
    return '';
  };

  // The nearest heading before an element within its card/column: Settings' "SimBrief", "MCDU Export".
  A.headingBefore = function (el) {
    var n = el;
    for (var up = 0; n && up < 6; up++) {
      var p = n.previousElementSibling;
      while (p) {
        if (/^H[1-6]$/.test(p.tagName) && A.visible(p)) {
          var own = A.clean(A.ownText(p) || A.txt(p));
          if (own) return own;
          // An empty heading (Settings' Top Of Descent Pause choice): the card's own title.
          var body = el.closest('.card-body');
          var title = body ? body.querySelector('h1') : null;
          return title ? A.clean(A.ownText(title) || A.txt(title)) : '';
        }
        var h = p.querySelector ? p.querySelectorAll('h1, h2, h3, h4, h5') : [];
        if (h.length && A.visible(h[h.length - 1])) return A.clean(A.ownText(h[h.length - 1]) || A.txt(h[h.length - 1]));
        p = p.previousElementSibling;
      }
      n = n.parentElement;
    }
    return '';
  };

  A.unit = function (input) {
    var g = input.closest('.input-group');
    var u = g ? g.querySelector('.input-group-text') : null;
    return u ? A.txt(u) : '';
  };

  A.fieldLabel = function (input) {
    if (A.FIELD_NAMES[input.id]) return A.FIELD_NAMES[input.id];
    var l = A.rowLabel(input) || A.headingBefore(input) || A.NAMES[input.id] || A.idWords(input.id);
    return A.FIELD_TITLES[l] || l;
  };

  // The tablet's weight unit, from the unit beside its fuel box ("kg" or "lbs").
  A.inPounds = function () {
    var f = document.getElementById('weight_fuel');
    return !!f && /^lb/i.test(A.unit(f));
  };

  // A Weight and Balance result in words: "Gross weight: 102.40 tonnes", "... 24.95 percent MAC".
  A.readout = function (input) {
    var r = A.READOUTS[input.id];
    var unit = r[1] === 'mac' ? 'percent MAC' : (A.inPounds() ? 'thousand pounds' : 'tonnes');
    return r[0] + ': ' + A.plainNumber(input.value) + ' ' + unit;
  };

  // What is on board now, from the simulator: the payload (stations 3 to 8; 1 and 2 are the
  // pilots) and the fuel to the nearest hundred, so an APU's burn does not change the line every
  // poll. While the tablet fuels the aircraft (L:INI_IS_REFUELING) the fuel says so instead of a
  // number that moves every second; the line then changes once, when fueling ends. '' when the
  // simulator cannot be read.
  A.onBoardLine = function () {
    try {
      if (typeof SimVar === 'undefined' || !SimVar.GetSimVarValue) return '';
      var unit = A.inPounds() ? 'pounds' : 'kilograms';
      var payload = 0;
      for (var st = 3; st <= 8; st++) payload += +SimVar.GetSimVarValue('PAYLOAD STATION WEIGHT:' + st, unit) || 0;
      var fuelling = +SimVar.GetSimVarValue('L:INI_IS_REFUELING', 'number') >= 0.5;
      var fuel = +SimVar.GetSimVarValue('FUEL TOTAL QUANTITY WEIGHT', unit) || 0;
      return 'On board: payload ' + A.thousands(payload) + ' ' + unit + ', ' +
        (fuelling ? 'fueling in progress' : 'fuel ' + A.thousands(Math.round(fuel / 100) * 100) + ' ' + unit);
    } catch (e) { return ''; }
  };

  // A preset load button: "Racing Team Charter: 31,000 kilograms, 68,300 pounds".
  A.presetLabel = function (btn) {
    var spans = btn.querySelectorAll ? btn.querySelectorAll('h1 span') : [];
    if (spans.length < 2) return '';
    var m = A.PRESET_WEIGHT.exec(A.txt(spans[0]));
    if (!m) return '';
    return A.txt(spans[1]) + ': ' + A.thousands(A.num(m[1])) + ' kilograms, ' + A.thousands(A.num(m[2])) + ' pounds';
  };

  // A door button's state from its classes; a slide button's from its color.
  A.doorState = function (btn) {
    if (A.hasClass(btn, 'door-transit')) return 'moving';
    if (A.hasClass(btn, 'door-open')) return 'open';
    return 'closed';
  };
  A.slideState = function (btn) {
    var m = /rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(window.getComputedStyle(btn).backgroundColor || '');
    if (!m) return '';
    var r = +m[1], g = +m[2];
    if (r > g) return 'armed';
    if (g > r) return 'disarmed';
    return '';
  };
  A.slideName = function (id) {
    var m = /^door(\d)([LR])armed$/.exec(id || '');
    return m ? 'Door ' + m[1] + m[2] + ' slide' : A.idWords(id);
  };

  A.pressLabel = function (el) {
    if (A.hasClass(el, 'door')) return A.txt(el) + ': ' + A.doorState(el);
    if (A.hasClass(el, 'door-arm')) {
      var s = A.slideState(el);
      return A.slideName(el.id) + (s ? ': ' + s : '');
    }
    if (A.NAMES[el.id]) return A.NAMES[el.id];
    if (el.tagName === 'INPUT') {
      var lab = A.rowLabel(el) || A.idWords(el.id);
      return lab ? lab + ': ' + A.clean(el.value) : A.clean(el.value);
    }
    if (el.tagName === 'DIV' && A.hasClass(el, 'unlock-button')) return 'Unlock';
    // A maintenance timer's Finish Now names the job it finishes, as several can run at once.
    var timer = /^timer_complete_/.test(el.id || '') ? el.closest('.pop_timer') : null;
    if (timer) {
      var job = timer.querySelector('h1');
      return 'Finish now' + (job ? ': ' + A.txt(job) : '');
    }
    if (A.hasClass(el, 'rwy-select') || A.hasClass(el, 'land-rwy-select')) {
      var rwy = A.runwayLabel(el);
      if (rwy) return rwy;
    }
    if (A.MAINT_PANELS[el.id]) {
      var open = A.panelOpen(el.id);
      return A.txt(el) + (open === null ? '' : open ? ': open' : ': closed');
    }
    if (A.hasClass(el, 'chart-button')) return A.chartName(el);
    if (A.hasClass(el, 'chart-pin-button')) {
      var chart = el.previousElementSibling;
      var named = chart && A.hasClass(chart, 'chart-button') ? ' ' + A.txt(chart.querySelector('h1')) : '';
      return (A.hasClass(el, 'bg-success') ? 'Unpin' : 'Pin') + named;
    }
    if (A.hasClass(el, 'page--left')) return 'Previous chart page';
    if (A.hasClass(el, 'page--right')) return 'Next chart page';
    // The Enroute Map's "<" opens its list of map types.
    if (el.closest('#enroutemap-sidebar') && /^[<>]$/.test(A.txt(el))) return 'Map type';
    var preset = A.presetLabel(el);
    if (preset) return preset;
    // The tablet's words, without the arrows it draws after some ("CALCULATE >>").
    var t = A.clean(A.txt(el).replace(/(>>|<<|»|«)/g, ' '));
    // A symbol for a name ("+", "-") gives way to the button's own title ("Zoom in").
    if (!/[A-Za-z0-9]/.test(t) && el.getAttribute('title')) t = el.getAttribute('title');
    if (t) return t + A.selectedSuffix(el);
    var img = el.querySelector ? el.querySelector('img[title], img[alt]') : null;
    if (img) return img.getAttribute('title') || img.getAttribute('alt');
    return A.idWords(el.id) || 'Button';
  };

  // A maintenance panel or cowl: true open, false closed, null when the simulator cannot be read.
  A.panelOpen = function (id) {
    try {
      if (typeof SimVar === 'undefined' || !SimVar.GetSimVarValue) return null;
      return +SimVar.GetSimVarValue(A.MAINT_PANELS[id], 'number') > 0.5;
    } catch (e) { return null; }
  };

  // Settings' choice buttons (Maintenance Mode) mark the chosen one with data-active="true",
  // which a sighted pilot sees as a highlight.
  A.selectedSuffix = function (el) { return el.getAttribute('data-active') === 'true' ? ' (selected)' : ''; };

  // A press label that carries the control's own state, so the shell keeps one node for it and
  // speaks its new label after the pilot's own press.
  A.stateKey = function (el) {
    if (A.hasClass(el, 'door') || A.hasClass(el, 'door-arm') || el.tagName === 'INPUT' || el.hasAttribute('data-active') ||
        A.MAINT_PANELS[el.id]) return 'press:' + el.id;
    if (A.hasClass(el, 'chart-pin-button')) {
      var chart = el.previousElementSibling;
      return 'pin:' + (chart && chart.getAttribute('data-guid') || el.id);
    }
    return '';
  };

  // The page container's own class among A.PAGE_CLASSES ("termcharts", "enroute-map"), or ''.
  A.pageClass = function (vp) {
    for (var c in A.PAGE_CLASSES) if (A.hasClass(vp, c)) return c;
    return '';
  };

  A.pageName = function () {
    var overlay = A.openOverlay();
    if (overlay) return overlay.def.page;
    var vp = document.querySelector('#renderer .visiblePage');
    if (!vp) return '';
    var byClass = A.PAGE_CLASSES[A.pageClass(vp)];
    if (byClass) return byClass;
    for (var i = 0; i < vp.children.length; i++) {
      var c = vp.children[i];
      if (c.id && A.PAGES[c.id] && A.visible(c)) return A.PAGES[c.id] + (c.id === 'weights' ? A.weightsHalf() : '');
    }
    for (var j = 0; j < vp.children.length; j++) {
      var d = vp.children[j];
      if (d.id && !/-bg$/.test(d.id) && A.visible(d)) return A.idWords(d.id);
    }
    return '';
  };

  // ", payload selection" or ", loading": the Weight and Balance half on screen.
  A.weightsHalf = function () {
    for (var id in A.WEIGHTS_HALVES) {
      var half = document.getElementById(id);
      if (half && half.closest('#weights') && A.visible(half)) return ', ' + A.WEIGHTS_HALVES[id];
    }
    return '';
  };

  A._idx = 0;
  A.stamp = function (node) { A._idx++; node.setAttribute(A.ATTR, String(A._idx)); return A._idx; };

  A.collect = function () {
    var els = [];
    var old = document.querySelectorAll('[' + A.ATTR + ']');
    for (var o = 0; o < old.length; o++) old[o].removeAttribute(A.ATTR);
    A._idx = 0;
    var doneGroups = {};
    var consumed = [];

    function emit(e) { els.push(e); }

    function walk(el) {
      // The Enroute Map's list of map types slides in from the right edge, its "<" toggle staying on
      // screen while the list is off it.
      if (el.id === 'enroutemap-sidebar' && !A.visible(el)) {
        var toggle = el.querySelector('button');
        if (toggle && A.visible(toggle)) emit({ idx: A.stamp(toggle), text: 'Map type', value: '', kind: 'button', clickable: true });
        return;
      }
      if (!A.visible(el)) return;
      var tag = el.tagName;

      // A Weight and Balance half opens with what its buttons do and what is on board.
      if (A.WEIGHTS_HELP[el.id] && el.closest('#weights')) {
        emit({ idx: 0, text: A.WEIGHTS_HELP[el.id], value: '', kind: 'static', clickable: false, key: 'help:weights-' + el.id });
        var line = A.onBoardLine();
        if (line) emit({ idx: 0, text: line, value: '', kind: 'static', clickable: false, live: 'polite', key: 'status:onboard' });
      }
      if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'svg' || tag === 'CANVAS') return;
      // Pictures with letters on them: Take Off's speed diagram (its V1/VR/V2 marks repeat the fields).
      if (el.id === 'diagram' || el.id === 'weight-image') return;
      // The picture's own words: Throttle Calibration's TOGA and IDLE scale marks, a page's title
      // banner (the page name says it).
      if (A.hasClass(el, 'indicator') || A.hasClass(el, 'page-title')) return;

      // Aircraft Maintenance's wear blocks and levels, in words (the blocks are only colors).
      if (A.hasClass(el, 'brake-wear-indicator') || A.hasClass(el, 'tire-wear-indicator')) {
        if (el.id === 'Brake_Wear_Indicator_LFI') {
          var brakes = A.wearLine('Brake wear', A.BRAKES, 1), tyres = A.wearLine('Tire wear', A.TYRES, 200);
          if (brakes) emit({ idx: 0, text: brakes, value: '', kind: 'static', clickable: false, key: 'status:brakewear' });
          if (tyres) emit({ idx: 0, text: tyres, value: '', kind: 'static', clickable: false, key: 'status:tyrewear' });
        }
        return;
      }
      if (el.id === 'oil-levels' || el.id === 'hyd-levels') {
        for (var lv in A.LEVELS) {
          var val = el.querySelector('#' + lv + '_val');
          if (val) emit({ idx: 0, text: A.LEVELS[lv] + ': ' + A.txt(val), value: '', kind: 'static', clickable: false, key: 'text:' + lv });
        }
        return;
      }

      // Pre-formatted text, the flight plan: one block with its line breaks kept, which the shell
      // reads line by line (the MD-11 and PMDG readers do the same).
      if (tag === 'PRE') {
        var block = (el.textContent || '').replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').replace(/^\s+|\s+$/g, '');
        if (block) emit({ idx: 0, text: block, value: '', kind: 'static', controlType: 'pre', clickable: false, key: el.id ? 'pre:' + el.id : undefined });
        return;
      }
      // The Charts viewer's page counter, "1 / 2".
      if (A.hasClass(el, 'page--count')) {
        var pc = /(\d+)\s*\/\s*(\d+)/.exec(A.txt(el));
        if (pc) emit({ idx: 0, text: 'Chart page ' + pc[1] + ' of ' + pc[2], value: '', kind: 'static', clickable: false, key: 'status:chartpage' });
        return;
      }

      // Radio groups: one choice control per group, labeled by the heading above it.
      if (tag === 'INPUT' && el.type === 'radio') {
        if (doneGroups[el.name]) return;
        doneGroups[el.name] = true;
        var radios = document.getElementsByName(el.name);
        var opts = [], value = '';
        for (var r = 0; r < radios.length; r++) {
          var lab = document.querySelector('label[for="' + radios[r].id + '"]');
          var word = lab ? A.txt(lab) : A.idWords(radios[r].id);
          opts.push(word);
          if (radios[r].checked) value = word;
        }
        var holder = el.closest('.btn-group') || el.parentElement;
        var idxR = A.stamp(holder);
        holder.setAttribute('data-a300-radio', el.name);
        emit({ idx: idxR, text: A.headingBefore(holder) || A.idWords(el.name.replace(/^opt_/, '')), value: value,
          controlType: 'select', kind: 'static', clickable: false, options: opts, key: 'radio:' + el.name });
        return;
      }
      if (tag === 'LABEL' && el.htmlFor && document.getElementById(el.htmlFor) && document.getElementById(el.htmlFor).type === 'radio') return;

      if (tag === 'INPUT' && el.type === 'range') {
        var idxG = A.stamp(el);
        var rl = A.rowLabel(el) || A.headingBefore(el) || A.idWords(el.id);
        if (el.id === 'lights_cargo') {
          var t = document.getElementById('lighting_title');
          rl = (t ? A.txt(t) + ' ' : '') + 'lighting';
        }
        emit({ idx: idxG, text: rl, value: String(el.value), controlType: 'range', kind: 'static', clickable: false,
          min: +el.min || 0, max: +(el.max || 100), step: +(el.step || 1), key: 'range:' + (el.id || idxG) });
        return;
      }

      if (A.RUNWAY_PICKERS[el.id] && A.runwayPicker(el, emit)) return;

      if (A.isPress(el)) {
        var idxP = A.stamp(el);
        var label = A.pressLabel(el);
        // A Home menu item the tablet has switched off carries the class "disabled" and no handler.
        var item = { idx: idxP, text: label, value: '', kind: 'button', clickable: true, disabled: !!el.disabled || A.hasClass(el, 'disabled') };
        var stateKey = A.stateKey(el);
        if (stateKey) {
          item.key = stateKey;
          item.announceChange = true;   // the label carries the control's own new state
        }
        emit(item);
        return;   // a button's inner text and icons are its label
      }

      if (tag === 'INPUT' && A.READOUTS[el.id]) {
        emit({ idx: 0, text: A.readout(el), value: '', kind: 'static', clickable: false, key: 'readout:' + el.id });
        return;
      }

      // A box the pilot cannot type in is a result: Take Off's FLEX, V1, VR, V2..., Landing's distances.
      if (tag === 'INPUT' && (el.type === 'text' || el.type === 'number') && (el.disabled || el.readOnly)) {
        var ru = A.unit(el);
        emit({ idx: 0, text: A.fieldLabel(el) + (ru ? ' (' + ru + ')' : '') + ': ' + (A.clean(el.value) || 'blank'), value: '',
          kind: 'static', clickable: false, key: 'readout:' + (el.id || A._idx) });
        return;
      }

      if (tag === 'INPUT' && (el.type === 'text' || el.type === 'number' || el.type === 'password')) {
        var idxI = A.stamp(el);
        var unit = A.unit(el);
        var name = A.fieldLabel(el) + (unit ? ' (' + unit + ')' : '');
        // A password (the Hoppie logon code) is never given out: its box comes up empty, and its name says
        // whether a code is stored. setValue never writes an empty value to it, so Enter on the untouched
        // box leaves the stored code alone (a placeholder value here used to be written over the code).
        var secret = el.type === 'password';
        if (secret && el.value) name += A.CODE_SET;
        emit({ idx: idxI, text: name, value: secret ? '' : A.clean(el.value), controlType: 'text',
          kind: 'static', clickable: false, disabled: !!el.disabled || !!el.readOnly, key: 'field:' + (el.id || idxI) });
        return;
      }

      // My Flight's headings only title the values named below.
      if (/^H[1-6]$/.test(tag) && el.closest('#flight')) return;
      if (el.id && A.VALUE_NAMES[el.id]) {
        emit({ idx: 0, text: A.VALUE_NAMES[el.id] + ': ' + A.txt(el), value: '', kind: 'static', clickable: false, key: 'text:' + el.id });
        return;
      }

      // Throttle Calibration's "LEFT 50%" headings are the throttle positions.
      var pct = /^H[1-6]$/.test(tag) ? el.querySelector('#l_pct, #r_pct') : null;
      if (pct) {
        emit({ idx: 0, text: (pct.id === 'l_pct' ? 'Left' : 'Right') + ' throttle: ' + A.txt(pct), value: '', kind: 'static',
          clickable: false, key: 'text:' + pct.id });
        return;
      }

      if (/^H[1-6]$/.test(tag)) {
        var ht = A.txt(el);
        // A heading holding a button (Take Off's "Conditions" + SYNC) reads its own words only.
        var btns = el.querySelectorAll('button, input');
        if (btns.length) ht = A.ownText(el);
        // Weight and Balance's capitals and bracketed unit: "WEIGHT AND BALANCE [kg]" → "Weight and balance".
        if (el.closest('#weights')) ht = A.sentence(ht.replace(/\s*\[[^\]]*\]\s*$/, ''));
        // Ground Equipment's slide legend, "Emergency Slides: Red - Armed // Green - Disarmed", sits
        // after the slide buttons, which say armed or disarmed in words: it is not read at all.
        if (/^Emergency Slides\b/i.test(ht)) ht = '';
        if (ht) emit({ idx: 0, text: ht, value: '', kind: 'heading', level: Math.min(6, +tag.charAt(1)), clickable: false });
        for (var b = 0; b < btns.length; b++) walk(btns[b]);
        return;
      }

      // Label columns are read with their field, never on their own.
      if (A.hasClass(el, 'flight-value-title') || A.hasClass(el, 'ini-input-label') || A.hasClass(el, 'input-group-text')) return;
      if (tag === 'P' && A.hasClass(el, 'text-end') && el.closest('.row') && el.closest('.row').querySelector('input[type=range]')) return;

      // A line of text broken up by inline tags ("Complete in <span>4:59</span>", "currently set to:
      // <span>REALISTIC</span>") is one line, not a phrase and an orphaned value.
      if (tag !== 'LABEL' && el.children.length && A.inlineOnly(el)) {
        var line = A.txt(el);
        if (line) emit({ idx: 0, text: line, value: '', kind: 'static', clickable: false, key: el.id ? 'text:' + el.id : undefined });
        return;
      }

      var own = A.ownText(el);
      if (own && tag !== 'LABEL') {
        var said = { idx: 0, text: own, value: '', kind: 'static', clickable: false, key: el.id ? 'text:' + el.id : undefined };
        // Throttle Calibration's instruction changes at each step of the calibration.
        if (el.id === 'calibration') said.live = 'polite';
        emit(said);
      }
      for (var k = 0; k < el.children.length; k++) walk(el.children[k]);
    }

    // The tablet's messages ("Payload Removed", a SimBrief error, "Unable to fuel aircraft when
    // engines are running.") pop up outside the page, in notie alert boxes on the body: read
    // first, as an alert the window speaks when it appears. The tablet only ever uses notie.alert.
    var toasts = document.querySelectorAll('.notie-container');
    for (var n = 0; n < toasts.length; n++) {
      if (!A.visible(toasts[n])) continue;
      var said = A.txt(toasts[n].querySelector('.notie-textbox-inner') || toasts[n]);
      if (said && !A.QUIET_TOASTS[said]) emit({ idx: 0, text: said, value: '', kind: 'alert', clickable: false });
    }

    var overlay = A.openOverlay();
    if (overlay) { A.readOverlay(overlay, emit, walk, els); return els; }
    if (A.visible(A.byId('confirm-box')) === false) A._pendingPanelState = '';

    // Servicing under way: the header's maintenance button shows a list of timers, each with a
    // Finish Now. Not modal, so it is read above the page while it shows.
    var timers = A.byId('timerContainer');
    if (timers && A.visible(timers) && timers.children.length) {
      emit({ idx: 0, text: 'Maintenance in progress', value: '', kind: 'heading', level: 2, clickable: false });
      walk(timers);
    }

    var page = document.querySelector('#renderer .visiblePage');
    if (page) {
      var note = A.PAGE_NOTES[A.pageClass(page)];
      if (note) emit({ idx: 0, text: note, value: '', kind: 'static', clickable: false, key: 'help:page' });
      walk(page);
    }
    A.header(emit);
    return els;
  };

  A.byId = function (id) { return document.getElementById(id); };

  // What the tablet puts over its pages, most covering first: the powered-off screen, the pause
  // dialog, the control box's panel-state confirmation, and the control box behind the header's
  // gear. Each covers the page for a sighted pilot, so while one is open it is read alone, under
  // its own page name, and nothing behind it can be pressed.
  A.OVERLAYS = [
    { id: 'powered-off', page: 'Powered off' },
    { sel: '.paused-overlay', page: 'Paused' },
    { id: 'confirm-box', page: 'Control box, confirm panel state' },
    { id: 'control-box', page: 'Control box' },
    // My Flight's checklist (a picture) and flight plan viewers, fixed over the page.
    { id: 'checklist_viewer', page: 'My Flight, checklist' },
    { id: 'ofp_viewer', page: 'My Flight, flight plan' }
  ];
  A.openOverlay = function () {
    for (var i = 0; i < A.OVERLAYS.length; i++) {
      var o = A.OVERLAYS[i];
      var el = o.id ? A.byId(o.id) : document.querySelector(o.sel);
      if (el && A.shown(el) && !A.hasClass(el, 'hidden')) return { def: o, el: el };
    }
    return null;
  };
  // The panel state last pressed in the control box: the confirmation itself only says "Confirm
  // New Panel State", the state it will set being held inside the tablet's code.
  A._pendingPanelState = '';
  A.readOverlay = function (o, emit, walk, els) {
    var id = o.def.id;
    if (id === 'powered-off') {
      emit({ idx: 0, text: 'The tablet is powered off.', value: '', kind: 'static', clickable: false });
      emit({ idx: A.stamp(o.el), text: 'Power on', value: '', kind: 'button', clickable: true });
      return;
    }
    if (id === 'checklist_viewer')
      emit({ idx: 0, text: 'The checklist is a picture and cannot be read here.', value: '', kind: 'static', clickable: false });
    var start = els.length;
    walk(o.el);
    if (id === 'confirm-box' && A._pendingPanelState) {
      for (var i = start; i < els.length; i++) {
        if (els[i].kind !== 'heading') continue;
        els.splice(i + 1, 0, { idx: 0, text: 'Panel state: ' + A._pendingPanelState, value: '', kind: 'static', clickable: false });
        break;
      }
    }
    if (id === 'control-box') {
      var bg = A.byId('control-box-background');
      if (bg && A.visible(bg)) emit({ idx: A.stamp(bg), text: 'Close control box', value: '', kind: 'button', clickable: true });
    }
  };

  // The header bar, after the page: the simulation-rate badge (shown only above 1x), one status
  // line with the clocks and the tablet's battery, then Home and the gear. Only Home used to be
  // read, so the control box behind the gear could not be reached at all.
  A.BATTERY = { 'plug.png': 'charging', 'battery-full.png': 'battery full', 'battery-half.png': 'battery half', 'battery-empty.png': 'battery low' };
  A.clock = function (s) { return /^\d{4}$/.test(s) ? s.slice(0, 2) + ':' + s.slice(2) : s; };
  A.statusLine = function () {
    var parts = [];
    var times = document.querySelectorAll('#right-menu-bar .time');
    for (var i = 0; i < times.length; i++) {
      var small = times[i].querySelector('.time-small');
      var which = small ? A.sentence(A.txt(small)) : '';
      var value = A.ownText(times[i]);
      if (value) parts.push((which ? which + ' time ' : '') + A.clock(value));
    }
    var charge = document.getElementById('charge-state');
    var src = charge ? String(charge.getAttribute('src') || '') : '';
    var word = A.BATTERY[src.substring(src.lastIndexOf('/') + 1)];
    if (word) parts.push(word);
    return parts.join(', ');
  };
  A.header = function (emit) {
    var rate = document.querySelector('.sim-rate');
    if (rate && A.visible(rate)) {
      var m = /SIMULATION RATE:\s*(\S+)/i.exec(A.txt(rate));
      if (m) emit({ idx: 0, text: 'Simulation rate: ' + m[1], value: '', kind: 'static', clickable: false, key: 'status:simrate' });
    }
    var line = A.statusLine();
    if (line) emit({ idx: 0, text: line, value: '', kind: 'static', clickable: false, key: 'status:header' });
    var home = document.getElementById('menu-home-button');
    if (home && A.visible(home)) emit({ idx: A.stamp(home), text: 'Home', value: '', kind: 'button', clickable: true });
    // Shown only while servicing runs; it shows and hides the timer list.
    var maint = A.byId('toggle-maintenance');
    if (maint && A.visible(maint)) {
      var shown = A.visible(A.byId('timerContainer'));
      emit({ idx: A.stamp(maint), text: 'Maintenance timers: ' + (shown ? 'shown' : 'hidden'), value: '', kind: 'button',
        clickable: true, key: 'press:toggle-maintenance', announceChange: true });
    }
    var gear = document.getElementById('control-box-button');
    if (gear && A.visible(gear)) emit({ idx: A.stamp(gear), text: 'Control box', value: '', kind: 'button', clickable: true });
  };

  // A heading that only repeats the name of the field right after it (Settings' "SimBrief" above the
  // SimBrief box) is dropped: the field says it.
  // "SimBrief" names the field "SimBrief", and "CARGO LIGHTING" the slider "CARGO lighting" — but
  // "PUSHBACK" is a section over "Pushback, turn left", not its name.
  A.sameLabel = function (heading, label) {
    var h = heading.toLowerCase(), l = label.toLowerCase();
    return l === h || l.indexOf(h + ' (') === 0 || l.indexOf(h + ':') === 0;
  };

  // The name of a field that a heading above it echoes: a password box's stored-code state ("Hoppie,
  // code set") is not part of it, so the "Hoppie" heading goes whether or not a code is stored.
  A.echoName = function (e) {
    var t = e.text, n = A.CODE_SET.length;
    return e.controlType === 'text' && t.length > n && t.slice(-n) === A.CODE_SET ? t.slice(0, -n) : t;
  };

  A.dropEchoHeadings = function (els) {
    var out = [];
    for (var i = 0; i < els.length; i++) {
      var e = els[i], next = els[i + 1];
      if (e.kind === 'heading' && next && next.kind !== 'heading' && next.idx && A.sameLabel(e.text, A.echoName(next))) continue;
      out.push(e);
    }
    return out;
  };

  A.scrape = function () {
    try {
      return JSON.stringify({ ok: true, page: A.pageName(), elements: A.dropEchoHeadings(A.collect()) });
    } catch (e) {
      return JSON.stringify({ ok: false, error: String(e && e.message || e), elements: [] });
    }
  };

  A.find = function (idx) { return document.querySelector('[' + A.ATTR + '="' + idx + '"]'); };

  // Where a finger on the control's center would land. The tablet wires its controls with
  // element.onclick, and the Unlock button and the header's gear hang it on the picture INSIDE the
  // control (measured 2026-10-04: a click dispatched on the Unlock box itself never unlocked). So
  // the press goes to the topmost element under the center when that lies inside the control; when
  // something else covers it, to the first element inside the control that has an onclick of its
  // own; otherwise to the control. Events bubble, so a handler on the control still runs once.
  A.pressTarget = function (el) {
    try {
      var r = el.getBoundingClientRect();
      if (document.elementFromPoint && r.width > 0 && r.height > 0) {
        var hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
        if (hit && (hit === el || el.contains(hit))) return hit;
      }
    } catch (e) { }
    if (typeof el.onclick !== 'function') {
      var inner = el.getElementsByTagName('*');
      for (var i = 0; i < inner.length; i++) if (typeof inner[i].onclick === 'function') return inner[i];
    }
    return el;
  };

  A.press = function (el) {
    var target = A.pressTarget(el);
    var r = el.getBoundingClientRect();
    var x = r.left + r.width / 2, y = r.top + r.height / 2;
    var types = ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'];
    for (var i = 0; i < types.length; i++) {
      var t = types[i], ev;
      if (t.indexOf('pointer') === 0 && typeof PointerEvent !== 'undefined')
        ev = new PointerEvent(t, { bubbles: true, cancelable: true, clientX: x, clientY: y, button: 0 });
      else
        ev = new MouseEvent(t, { bubbles: true, cancelable: true, clientX: x, clientY: y, button: 0, view: window });
      target.dispatchEvent(ev);
    }
  };

  A.clickElement = function (idx) {
    var el = A.find(idx);
    if (!el || !A.visible(el) || el.disabled) return false;
    if (/^panelstate_\d+$/.test(el.id || '')) A._pendingPanelState = A.txt(el);
    A.press(el);
    return true;
  };

  A.fire = function (el, type) {
    var ev = document.createEvent('HTMLEvents');
    ev.initEvent(type, true, true);
    el.dispatchEvent(ev);
  };

  // A number the pilot typed → the number, or NaN when it is not one. A comma or a space groups thousands
  // only in a well formed grouping: one to three leading digits, the first not 0, then groups of exactly
  // three ("20,000", "1,234,567", "20 000", "12 345,5"; A.clean makes a no-break, thin or narrow space a
  // plain one). Spaces group the whole number only. Any other single comma is the decimal point ("12,5",
  // "0,25"), so "0,500" is a half and "1234,567" a thousand and a fraction. What is still not a plain
  // number ("abc", "15 kg", "1e3", "+5", "1.234,5", "1,234 567", "1234 567", "0 500") is NaN.
  A.typedNumber = function (text) {
    var s = A.clean(text), cut = s.search(/[,.]/);
    if (cut < 0) cut = s.length;
    if (/^-?[1-9]\d{0,2}( \d{3})+$/.test(s.slice(0, cut))) s = s.slice(0, cut).replace(/ /g, '') + s.slice(cut);
    if (/^-?[1-9]\d{0,2}(,\d{3})+(\.\d+)?$/.test(s)) s = s.replace(/,/g, '');
    else if (/^-?\d*,\d+$/.test(s)) s = s.replace(',', '.');
    if (!/^-?(\d+\.?\d*|\.\d+)$/.test(s)) return NaN;
    var n = +s;
    return isFinite(n) ? n : NaN;
  };

  // Types into a field, picks a radio option or moves a slider. False when nothing was written.
  A.setValue = function (idx, text) {
    var el = A.find(idx);
    if (!el) return false;
    if (A.RUNWAY_PICKERS[el.id]) return A.pickRunway(el, text);
    var group = el.getAttribute('data-a300-radio');
    if (group) {
      var radios = document.getElementsByName(group);
      for (var r = 0; r < radios.length; r++) {
        var lab = document.querySelector('label[for="' + radios[r].id + '"]');
        if (lab && A.txt(lab) === text) { A.press(lab); return true; }
      }
      return false;
    }
    if (el.tagName !== 'INPUT') return false;
    if (el.type === 'range') {
      el.value = String(text);
      A.fire(el, 'input');
      A.fire(el, 'change');
      return true;
    }
    // A password box is never emptied or blanked: an empty entry is the untouched box, not a new code,
    // and clearing a stored code is not offered here.
    if (el.type === 'password' && !A.clean(text)) return false;
    // A number box blanks itself on text that is not a plain number ("20,000", "12,5"), so the number
    // the pilot meant is written. An empty entry (or spaces alone) clears the box: it is emitted with its
    // real value, so Enter on an emptied box is a deliberate deletion. Any other text that is not a
    // number is refused, and the box is left as it was. Landing's text boxes that hold numbers
    // (A.NUMBER_TEXT) are written the same way: the tablet reads them with parseInt.
    if (el.type === 'number' || A.NUMBER_TEXT[el.id]) {
      if (!A.clean(text)) text = '';
      else {
        var n = A.typedNumber(text);
        if (isNaN(n)) return false;
        text = String(n);
      }
    }
    // Never focus(): the tablet's on-screen keyboard opens on a focused field and covers the page.
    el.value = String(text);
    A.fire(el, 'input');
    A.fire(el, 'change');
    A.fire(el, 'keyup');
    A.fire(el, 'blur');
    return true;
  };

  window.__MSFSBA_A300_EFB = A;
  return A.INSTALLED;
})();
