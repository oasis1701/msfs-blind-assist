'use strict';
// Writes the hand-made A300 tablet fixtures: the freighter's Weight and Balance page, whose two
// halves (payload selection, then loading) slide by 100vw, plus My Flight. The structure and ids
// follow the tablet as it renders live (2026-10-04); the words are its short labels only.
// Run: node fixtures/make.js
const fs = require('fs');
const path = require('path');

function preset(id, weight, name) {
  return '<div class="col-sm-4"><button id="' + id + '" class="btn btn-info btn-lg w-100"><img src="' + id + '.png"><h1>' +
    '<span>' + weight + '</span><br><span>' + name + '</span></h1></button></div>';
}

function field(id, label, value, unit) {
  return '<div class="row mb-2"><div class="col-sm-4 text-end ini-input-label">' + label + '</div><div class="col-sm-8">' +
    '<div class="input-group"><input id="' + id + '" class="form-control ini-small-input" type="number" value="' + value + '">' +
    '<span class="input-group-text bg-dark text-white">' + unit + '</span></div></div></div>';
}

function output(id, label, value) {
  return '<div class="row mb-2"><div class="col-sm-6 text-end ini-input-label">' + label + '</div><div class="col-sm-6">' +
    '<input id="' + id + '" class="form-control ini-small-input" type="number" readonly value="' + value + '"></div></div>';
}

function page1(rect, forwardShown) {
  return '<div id="page1" data-rect="' + rect + '">' +
    '<div id="switcher_page2"' + (forwardShown ? '' : ' data-display="none"') + '><img src="chevron-right.png"></div>' +
    '<div class="row"><h1>PAYLOAD SELECTION</h1>' +
    preset('payload_race', "31'000Kgs / 68'300Lbs", 'Racing Team Charter') +
    preset('payload_aero', "26'500Kgs / 58'400Lbs", 'Aero Parts Transport') +
    preset('payload_horse', "15'700Kgs / 34'600Lbs", 'Horse Stable Transport') +
    preset('payload_human', "21'000Kgs / 46'300Lbs", 'Humanitarian Charter') +
    preset('payload_post', "18'500Kgs / 40'800Lbs", 'Postal Freight') +
    preset('payload_cargo', 'Custom', 'Custom Cargo') +
    preset('payload_cargo_sb', 'From SimBrief', 'Custom Cargo') +
    '<div class="col-sm-12"><button id="payload_clear" class="btn btn-danger btn-lg w-100">Unload Cargo</button></div>' +
    '</div></div>';
}

function page2(rect, unit) {
  return '<div id="page2" data-rect="' + rect + '">' +
    '<div id="switcher_page1"><img src="chevron-left.png"></div>' +
    '<div class="row"><div class="col-sm-12"><h1 class="mb-2">WEIGHT AND BALANCE [' + unit + ']</h1>' +
    '<div class="card"><div class="card-body bg-dark">' +
    '<div class="weight-image"><div id="weight_freight_fwd_box"></div><div id="weight_freight_mid_box"></div><div id="weight_freight_aft_box"></div></div>' +
    '<div class="row pb-5"><div class="col-sm-8 offset-sm-2">' +
    '<div class="row mt-5"><div class="col"><div class="row mb-2"><div class="col-sm-8"></div></div></div>' +
    '<div class="col">' + field('input_payload', 'LOAD', '0', unit).replace('class="col-sm-4 text-end ini-input-label"', 'id="input_payload_title" class="col-sm-4 text-end ini-input-label"') + '</div>' +
    '<div class="col">' + field('weight_fuel', 'FUEL', '60110', unit) + '</div></div>' +
    '<div class="row mt-2">' +
    '<div class="col-sm-4">' + field('weight_freight_fwd', 'FWD', '10333', unit) + '</div>' +
    '<div class="col-sm-4">' + field('weight_freight_mid', 'MID', '10333', unit) + '</div>' +
    '<div class="col-sm-4">' + field('weight_freight_aft', 'AFT', '10333', unit) + '</div></div>' +
    '<div class="row pb-5">' +
    '<div class="col-sm-6"><button id="update_from_simbrief" class="ini-small-button w-100 mt-3 mb-2">Update from SimBrief</button></div>' +
    '<div class="col-sm-6"><button id="weight_freight_apply" class="ini-small-button w-100 mt-3 mb-2">Apply Load to Aircraft</button></div></div>' +
    '<div class="row mt-5">' +
    '<div class="col-sm-4">' + output('output_maczfw', 'MACZFW %', '00.0') + output('output_maccg', 'MACGW %', '24.95') + '</div>' +
    '<div class="col-sm-4">' + output('output_zfw', 'ZFW', '000.0') + output('output_tow', 'GW', '102.40') + '</div>' +
    '<div class="col-sm-4">' + output('output_payload', 'PAYLOAD', '031.0') + output('output_blkfuel', 'BLK FUEL', '060.11') + '</div>' +
    '</div></div></div></div></div></div></div></div>';
}

// The tablet's frame, laid out as it renders live (2026-10-04): #iniEFB holds the maintenance timer
// list, then one div with the powered-off screen, the simulation-rate badge, the control box (the
// gear icon's panel, with its dimming background and its panel-state confirmation) and the header
// bar, then the page renderer and the pause dialog. o: { home, chargeIcon, simRate, controlBox,
// confirm, poweredOff, paused: [title, text, resumeShown], timers: html, maintenanceShown,
// pageClass } — everything else hidden, as at rest.
function tablet(inner, extra, o) {
  o = o || {};
  const hide = shown => (shown ? '' : ' data-display="none"');
  return '<div id="iniEFB">' +
    '<div id="timerContainer"' + hide(!!o.timers) + '>' + (o.timers || '') + '</div>' +
    '<div>' +
    '<div id="powered-off"' + hide(o.poweredOff) + (o.poweredOff ? ' data-rect="0,0,2048,1476"' : '') + '></div>' +
    '<div id="bgimg"></div>' +
    '<div class="sim-rate"' + hide(!!o.simRate) + '><span>SIMULATION RATE: ' + (o.simRate || '2x') + '</span></div>' +
    '<div id="control-box-background" class="control-box-background"' + hide(o.controlBox) + (o.controlBox ? ' data-rect="0,0,2048,1476"' : '') + '></div>' +
    '<div id="control-box"' + hide(o.controlBox) + '><div class="row mt-5"><div class="col-sm-12"><div class="d-grid gap-2 mt-5 mb-5">' +
    '<button class="btn btn-primary mb-2 btn-lg btn-custom" id="powerOff" type="button">Power Off EFB</button>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-custom" id="lockEfb" type="button">Lock EFB</button><hr>' +
    '<h1 class="text-center text-white">TIME COMPRESSION</h1><div class="row time-compression">' +
    '<div class="col"><button class="btn btn-primary mb-1 btn-custom w-100">DISABLED</button></div>' +
    '<div class="col"><button class="btn btn-primary mb-1 btn-custom w-100">MAX 2x</button></div>' +
    '<div class="col"><button class="btn btn-primary mb-1 btn-custom w-100">MAX 4x</button></div></div>' +
    '<div class="text-center text-white mt-4 time-compression__label">Time Compression: OFF</div><hr>' +
    '<h1 class="text-center text-white">Brightness</h1><input type="range" class="ini-range" min="0" max="100" step="0.1" id="brightness" value="75"><hr>' +
    '<h1 class="text-center text-white">Panel States</h1>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-custom" id="panelstate_0" type="button">Cold and Dark</button>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-custom" id="panelstate_1" type="button">Ready for Takeoff</button>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-custom" id="panelstate_2" type="button">On APU</button>' +
    '<button class="btn btn-primary mb-5 btn-lg btn-custom" id="panelstate_3" type="button">On GPU</button>' +
    '</div></div></div></div>' +
    '<div id="confirm-box"' + hide(o.confirm) + '><div class="row"><div class="col-sm-12"><div class="d-grid gap-2 mt-5 mb-5">' +
    '<h1 class="mb-3 text-center text-white">Confirm New Panel State</h1>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-success" id="confirmBoxConfirmButton" type="button">Confirm</button>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-danger" id="confirmBoxCancelButton" type="button">Cancel</button></div></div></div></div>' +
    '<div id="header-bar"><div id="left-menu-bar"><div class="menu-bar-item ml-5 mt-3"><img id="menu-home-button" src="home.png"' + hide(o.home !== false) + '></div></div>' +
    '<div id="right-menu-bar">' +
    '<div class="menu-bar-item wider"><button id="toggle-maintenance" class="btn btn-sm btn-danger"' + hide(o.maintenanceShown) + '><img id="maint-spin" class="spin" src="convert.png"></button></div>' +
    '<div class="menu-bar-item wider"><div class="time" title="LOCAL TIME"><span class="time-small">LOCAL</span><br>1357</div></div>' +
    '<div class="menu-bar-item wider"><div class="time" title="ZULU TIME"><span class="time-small">ZULU</span><br>2057</div></div>' +
    '<div class="menu-bar-item"><img id="charge-state" src="/Pages/VCockpit/Instruments/ini-common/Icons/' + (o.chargeIcon || 'plug.png') + '"></div>' +
    '<div class="menu-bar-item" id="control-box-button" data-rect="0,1950,2040,100"><img id="control-box-button-icon" src="gear.png" data-rect="10,1960,2030,90"></div>' +
    '</div></div>' +
    '</div>' +
    '<div id="renderer"><div class="' + (o.pageClass ? o.pageClass + ' ' : '') + 'visiblePage">' + inner + '</div></div>' +
    '<div class="paused-overlay' + (o.paused ? '' : ' hidden') + '"' + hide(!!o.paused) + '><div id="paused" class="paused-overlay__dialog">' +
    '<h2 class="text-danger">' + (o.paused ? o.paused[0] : '') + '</h2><p>' + (o.paused ? o.paused[1] : '') + '</p>' +
    '<button type="button" class="btn btn-secondary' + (o.paused && o.paused[2] ? '' : ' hidden') + '"' + hide(!!(o.paused && o.paused[2])) + '><span>RESUME FLIGHT</span></button></div></div>' +
    '</div>' + (extra || '');
}

function weights(p1Rect, p2Rect, forwardShown, unit, extra) {
  return tablet('<div id="weights-bg"></div><div id="weights">' + page1(p1Rect, forwardShown) + page2(p2Rect, unit) + '</div>', extra);
}


// Take Off's runway list (#runway_opts, built by the tablet from the airport's runways: red when
// the runway is at or below its shortest table, 1,700 m) and its read-only results.
function rwy(name, metres, red) {
  return '<div class="col-sm-4 mt-3"><div class="input-group mb-3"><button class="form-control rwy-select" id="' + name + '">RWY ' + name +
    '<br><small style="padding-top: 10px; color: ' + (red ? 'rgb(255, 0, 0)' : 'rgb(153, 204, 255)') + '">[' + metres + 'm / ' + Math.round(metres * 3.28084) + 'ft]</small></button></div></div>';
}
function perfRow(label, input) {
  return '<div class="row mb-2"><div class="col-sm-4 text-end ini-input-label">' + label + '</div><div class="col-sm-8"><div class="input-group">' + input + '</div></div></div>';
}
function wearBox() {
  let h = '';
  for (const b of ['LFI', 'LFO', 'LRI', 'LRO', 'RFI', 'RFO', 'RRI', 'RRO']) h += '<div class="brake-wear-indicator" id="Brake_Wear_Indicator_' + b + '"></div>';
  for (const t of ['NG_LEFT', 'NG_RIGHT', 'LAI', 'LAO', 'RAO', 'RAI', 'RFO', 'RFI', 'LFI', 'LFO']) h += '<div class="tire-wear-indicator" id="INI_TIRE_' + t + '_WEAR"></div>';
  return h;
}
function level(id, label, value) {
  return '<div class="col text-center"><p>' + label + '</p><div id="' + id + '" class="condition-bar"></div><p id="' + id + '_val" class="text-center mt-1">' + value + '</p></div>';
}
function chart(guid, name, kind, pinned) {
  return '<div data-guid="' + guid + '" class="col-sm-11 chart-button"> <h1>' + name + '</h1> <p class="text-warning">' + kind + ' </p> </div>' +
    '<div class="col-sm-1 chart-pin-button' + (pinned ? ' bg-success' : '') + '"><img class="chart-pin-icon" src="pin.png"></div>';
}

const ON = '0,0,2048,1536', OFF_LEFT = '0,-2048,0,1536', OFF_RIGHT = '0,2048,4096,1536';
const files = {
  // My Flight's checklist (a picture) and its flight plan (a <pre>), each opened over the page.
  'myflight-checklist': tablet('<div id="flight"><div id="checklist_viewer"><img src="a306checklist.png" id="checklist">' +
    '<button id="close_checklist_viewer" class="btn btn-lg btn-danger">Close</button></div>' +
    '<div id="ofp_viewer" data-display="none"><pre id="ofp_data">OFP Not Downloaded</pre><button id="close_ofp_viewer" class="btn btn-lg btn-danger">Close</button></div>' +
    '<div id="dep_icao">EGLL</div><button id="open_checklist_viewer" class="btn">CHECKLIST</button></div>'),
  'myflight-ofp': tablet('<div id="flight"><div id="checklist_viewer" data-display="none"><img src="a306checklist.png" id="checklist">' +
    '<button id="close_checklist_viewer" class="btn btn-lg btn-danger">Close</button></div>' +
    '<div id="ofp_viewer"><pre id="ofp_data">[ OFP ]   \nEGLL-EHAM   TST101\n\n\n\nBLOCK FUEL   6200\n</pre><button id="close_ofp_viewer" class="btn btn-lg btn-danger">Close</button></div>' +
    '<div id="dep_icao">EGLL</div></div>'),
  // A viewer on a hidden page is not open, whatever its own display says.
  'myflight-hidden-ofp': tablet('<div id="dashboard"></div>').replace('</div></div><div class="paused-overlay',
    '</div><div class="hiddenPage" data-display="none"><div id="flight"><div id="ofp_viewer"><pre id="ofp_data">x</pre><button id="close_ofp_viewer">Close</button></div></div></div></div><div class="paused-overlay'),
  'takeoff': tablet('<div id="perf-bg"></div><div id="takeOffPerfViewer">' +
    '<div id="runwayPanel"><h1>SELECT RUNWAY</h1><input type="button" id="hide_runway_select" value="Cancel">' +
    '<div id="runway_opts" class="row">' + rwy('18L', 3000, false) + rwy('09', 1500, true) + '</div></div>' +
    '<div id="conditions"><h1>CONDITIONS <button id="syncMetar" class="btn">SYNC</button></h1>' +
    perfRow('RWY', '<input type="button" id="show_runway_select" value="SELECT">') + '</div>' +
    '<div id="config"><button id="calculate" class="btn">CALCULATE &gt;&gt;</button></div>' +
    '<div id="performance"><h1>PERFORMANCE</h1>' +
    perfRow('FLEX', '<input type="text" id="output_flex" disabled value="45"><span class="input-group-text">°C</span>') +
    perfRow('V1', '<input type="number" id="output_v1" disabled value="">') + '</div></div>'),
  'groundequip': tablet('<div id="equip"><button id="door1Larmed" class="door-arm" style="background-color: rgb(0, 128, 0)"></button>' +
    '<h1>Emergency Slides: Red - Armed // Green - Disarmed</h1></div>'),
  'maintenance': tablet('<div id="maintenance"><div class="row"><div class="col-sm-12"><h1 class="mb-2">AIRCRAFT MAINTENANCE</h1>' +
    '<div class="card m-2"><div class="card-body"><h2>MAINTENANCE PANELS</h2><div class="row mt-3">' +
    '<div class="col-sm-4"><button id="fuel_panel" class="btn btn-primary">Fuel Panel</button></div>' +
    '<div id="eng_cowl_l_button" class="col-sm-4"><button id="maint_eng_cowl_l" class="btn btn-primary">Eng Cowl L</button></div>' +
    '<div id="eng_cowl_r_button" class="col-sm-4"><button id="maint_eng_cowl_r" class="btn btn-primary">Eng Cowl R</button></div>' +
    '<div class="col-sm-4"><button id="maint_apu_cowl" class="btn btn-primary">APU Cowl</button></div>' +
    '<div class="col-sm-4"><button id="maint_stow_rat" class="btn btn-primary">Stow RAT</button></div></div></div></div>' +
    '<div class="col-sm-8"><h1 class="ms-2 mt-2">COMPONENT STATE</h1><div><div></div>' + wearBox() +
    '<div id="oil-levels"><h2 class="text-light">OIL</h2><div class="row">' + level('APU_OIL', 'APU', '100%') + level('ENG1_OIL', 'ENG1', '97%') + level('ENG2_OIL', 'ENG2', '100%') + '</div></div>' +
    '<div id="hyd-levels"><h2 class="text-end text-light">HYDRAULICS</h2><div class="row">' + level('HYD_BLU', 'B', '14.5') + level('HYD_GRE', 'G', '31.5') + level('HYD_YEL', 'Y', '19.5') + '</div></div>' +
    '</div></div></div></div></div>'),
  'throttle': tablet('<div id="throttlecalibration"><h1 class="mb-2">THROTTLE CALIBRATION</h1>' +
    '<p id="calibration">Move both throttles to TOGA and press Set TOGA Position.</p>' +
    '<button id="tcab_start" class="btn">Start Calibration</button>' +
    '<div class="col-sm-2"><div id="l_toga" class="indicator i-left"><p>TOGA</p><div class="arrow-right"></div></div><div id="l_idle" class="indicator i-left"><p>IDLE</p></div></div>' +
    '<div class="col-sm-4 text-center"><h1>LEFT <span id="l_pct">50%</span></h1><div id="l_pct_box" class="tpos"></div></div>' +
    '<div class="col-sm-4 text-center"><h1>RIGHT <span id="r_pct">48%</span></h1><div id="r_pct_box" class="tpos"></div></div>' +
    '<div class="col-sm-2"><div id="r_toga" class="indicator i-right"><p>TOGA</p></div><div id="r_idle" class="indicator i-right"><p>IDLE</p></div></div></div>'),
  'settings-maint': tablet('<div id="settings"><div class="card mt-4"><div class="card-body bg-dark">' +
    '<p class="float-end">Maintenance system is currently set to: <span id="maint_state">REALISTIC</span></p><h1 class="mt-2">Maintenance Mode</h1>' +
    '<button class="btn" id="maint_disabled" type="button" data-active="false">Disabled</button>' +
    '<button class="btn" id="maint_real" type="button" data-active="true">Realistic</button>' +
    '<button class="btn" id="maint_fast" type="button" data-active="false">Fast</button></div></div></div>'),
  'charts': tablet('<div class="termcharts-bg"></div><div id="unauthed_content" class="termcharts-panel" data-display="none"><h1 id="ng_user_code"></h1></div>' +
    '<div id="authed_content" class="termcharts-panel"><div class="row"><div class="col-sm-3 termcharts-sidebar">' +
    '<div class="row"><div class="col-sm-6"><input id="ng_search" class="form-control" value="KMEM"></div><div class="col-sm-6"><button id="ng_search_button" class="btn">SEARCH</button></div></div>' +
    '<div class="row mt-3 w-100 chart-list" id="chart_list">' + chart('g1', 'CONDR 4 RNAV [ATC]', 'STAR', false) + chart('g2', 'ELVIS 4', 'SID', true) + '</div>' +
    '<div class="btn-group filter-toolbar"><button id="filter_all" class="filter-button">ALL</button><button id="filter_pinned" class="filter-button"><img class="chart-pin-icon" src="pin.png"></button></div></div>' +
    '<div class="col chart-viewer"><div id="chart_ctrl_zoom" class="chart-controls"><button id="ctrlZoomIn" class="btn chart-control-button"><img src="zoomin.png"></button>' +
    '<button id="ctrlPanUp" class="btn chart-control-button"><i class="arrow up"></i></button></div>' +
    '<div class="chart-controls--pages"><button class="btn page--left"><i class="arrow left"></i></button><div class="btn page--count"><span>1 / 2</span></div>' +
    '<button class="btn page--right"><i class="arrow right"></i></button></div>' +
    '<div id="chart_image_box" class="chart-image-box"><img id="chart_image" class="chart-image" src="chart"></div></div></div></div>', '', { pageClass: 'termcharts' }),
  'enroute': tablet('<div class="page-title">ENROUTE</div><div id="enroutemap-sidebar" data-rect="140,2048,2348,1116"><div class="row pa-2">' +
    '<button data-rect="160,1998,2048,232">&lt;</button><div class="col-12"><button class="p-2 w-100 mb-2">Standard</button></div></div></div>' +
    '<div id="position-icon" class="plane-marker"><img src="progress.png"></div><div id="enroutemap-bg"></div>' +
    '<div id="enroute"><div class="ol-viewport"><div class="ol-zoom"><button class="ol-zoom-in" type="button" title="Zoom in">+</button>' +
    '<button class="ol-zoom-out" type="button" title="Zoom out">-</button></div></div></div>', '', { pageClass: 'enroute-map' }),

  // The Home page with the header bar: the gear opens the control box; the tablet charges.
  'home': tablet('<div id="dashboard"><div class="menu-row">' +
    '<div class="menu-row-item is-button text-center" rel="flight"><img class="home-button" src="button_myflight.png"><div class="menu-text">My Flight</div></div>' +
    '<div class="menu-row-item is-button text-center disabled" rel="charts"><img class="home-button" src="button_charts.png"><div class="menu-text">Charts</div></div>' +
    '</div></div>', '', { home: false, simRate: '4x' }),
  'home-battery': tablet('<div id="dashboard"></div>', '', { home: false, chargeIcon: 'battery-half.png' }),
  // The control box open over a page, and its panel-state confirmation open over it.
  'control-box': tablet('<div id="dashboard"><div class="menu-row"><div class="menu-row-item is-button text-center" rel="flight"><div class="menu-text">My Flight</div></div></div></div>', '', { home: false, controlBox: true }),
  'confirm-box': tablet('<div id="dashboard"></div>', '', { home: false, controlBox: true, confirm: true }),
  'powered-off': tablet('<div id="dashboard"><div class="menu-row"><div class="menu-row-item is-button text-center" rel="flight"><div class="menu-text">My Flight</div></div></div></div>', '', { home: false, poweredOff: true }),
  'paused': tablet('<div id="dashboard"></div>', '', { home: false, paused: ['TOP OF DESCENT PAUSE', 'Aircraft has reached top of descent. The simulation has been paused. Click below to resume.', true] }),
  // Maintenance under way: the header's spinning button shows, and its timer list is open.
  'timers': tablet('<div id="dashboard"></div>', '', { home: false, maintenanceShown: true,
    timers: '<div id="timer_1" class="pop_timer"><h1>Replace Brakes</h1><p>All eight brakes are being replaced.</p>' +
      '<p><strong>Complete in <span id="timer_dur_1">4:59</span></strong></p>' +
      '<button id="timer_complete_1" class="btn btn-info btn-lg btn-custom w-100">Finish Now</button></div>' }),
  // The lock screen: the unlock handler is the tablet's onclick on the picture INSIDE the button
  // (LockComponent sets it on document.querySelector('.home-button')), never on the button itself.
  'lock': tablet('<div id="lockscreen"><div class="lock-time mt-5 text-center">1355</div>' +
    '<div class="unlock-button" data-rect="688,152,1895,869"><div class="menu-row-item text-center" data-rect="688,152,1895,869">' +
    '<img class="home-button" src="unlock-button.png" data-rect="688,933,1114,869"></div></div></div>', '', { home: false }),
  // Payload selection on screen; no load chosen yet, so the forward chevron is hidden.
  'weights-selection': weights(ON, OFF_RIGHT, false, 'kg'),
  // A load was chosen and the pilot came back: the forward chevron shows.
  'weights-selection-chosen': weights(ON, OFF_RIGHT, true, 'kg'),
  // The loading half on screen, the selection slid off to the left.
  'weights-loading': weights(OFF_LEFT, ON, true, 'kg'),
  'weights-loading-lbs': weights(OFF_LEFT, ON, true, 'lbs'),
  // A notie toast (appended to the body) over the selection half.
  'weights-toast': weights(ON, OFF_RIGHT, false, 'kg',
    '<div class="notie-container notie-alert notie-background-success"><div class="notie-textbox"><div class="notie-textbox-inner">Payload Removed</div></div></div>'),
  // Settings, Third Party Settings: the SimBrief box is labelled only by its heading.
  'settings': tablet('<div id="settings-bg"></div><div id="settings"><div id="settings-container" class="row">' +
    '<h1 class="mb-2">SETTINGS</h1><div class="card"><div class="card-body bg-dark"><h1 class="mt-2">Third Party Settings</h1>' +
    '<div class="row"><div class="col-sm-4"><h2 class="mt-2 text-info">SimBrief</h2><div class="row"><div class="col">' +
    '<div class="d-grid gap-2 mb-1 p-3"><input id="simbrief" class="form-control form-control-lg w-100" type="text" name="simbrief" value=""></div></div></div></div>' +
    '<div class="col-sm-4"><h2 class="mt-2 text-info">Hoppie</h2><div class="row"><div class="col">' +
    '<div class="d-grid gap-2 mb-1 p-3"><input id="hoppie" class="form-control form-control-lg w-100" type="password" name="hoppie" value=""></div></div></div></div>' +
    '</div></div></div></div></div>'),
  'myflight': tablet('<div id="flight">' +
    '<div class="row"><h2>DEPARTURE</h2><div id="dep_icao">EGLL</div><div id="depcit">London/Heathrow</div></div>' +
    '<div class="row"><h2>ARRIVAL</h2><div id="arr_icao">EHAM</div></div>' +
    '<div class="row"><button id="btn_simbrief" class="btn btn-primary">IMPORT FROM SIMBRIEF</button></div></div>')
};

for (const name of Object.keys(files))
  fs.writeFileSync(path.join(__dirname, name + '.html'), files[name] + '\n');
console.log('wrote', Object.keys(files).length, 'fixtures');
